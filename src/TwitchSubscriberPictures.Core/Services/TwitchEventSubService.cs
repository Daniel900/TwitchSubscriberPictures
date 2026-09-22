using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using TwitchLib.Api.Core.Enums;
using TwitchLib.EventSub.Core.EventArgs.Channel;
using TwitchLib.EventSub.Websockets;
using TwitchLib.EventSub.Websockets.Core.EventArgs;
using TwitchSubscriberPictures.Core.Abstractions;
using TwitchSubscriberPictures.Core.Models;

namespace TwitchSubscriberPictures.Core.Services;

public sealed class TwitchEventSubService : IAsyncDisposable
{
    private const int MaxConnectAttempts = 4;

    private readonly ITwitchApiFactory _apiFactory;
    private readonly IAppLogger _logger;
    private readonly EventSubWebsocketClient _client;
    private readonly bool _registerSubscriptions;
    private readonly Uri? _webSocketUri;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    private CancellationTokenSource _stopCts = new();
    private bool _reconnectLoopRunning;
    private bool _started;
    private string _clientId = string.Empty;
    private string _clientSecret = string.Empty;
    private string _accessToken = string.Empty;
    private string _broadcasterId = string.Empty;

    public TwitchEventSubService(
        ITwitchApiFactory apiFactory,
        IAppLogger logger,
        EventSubWebsocketClient? client = null,
        Uri? webSocketUri = null,
        bool registerSubscriptions = true,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _apiFactory = apiFactory ?? throw new ArgumentNullException(nameof(apiFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _webSocketUri = webSocketUri;
        _registerSubscriptions = registerSubscriptions;
        _delay = delay ?? Task.Delay;
        _client = client ?? new EventSubWebsocketClient(NullLoggerFactory.Instance);

        _client.WebsocketConnected += OnWebsocketConnected;
        _client.WebsocketDisconnected += OnWebsocketDisconnected;
        _client.WebsocketReconnected += OnWebsocketReconnected;
        _client.ErrorOccurred += OnErrorOccurred;
        _client.ChannelSubscribe += OnChannelSubscribe;
        _client.ChannelSubscriptionEnd += OnChannelSubscriptionEnd;
        _client.ChannelSubscriptionGift += OnChannelSubscriptionGift;
        _client.ChannelSubscriptionMessage += OnChannelSubscriptionMessage;
    }

    public event Func<SubscriberChangedEventArgs, Task>? SubscriberListChanged;

    public event Func<string, Task>? ConnectionError;

    public string? SessionId => _client.SessionId;

    public async Task StartAsync(
        string clientId,
        string clientSecret,
        string accessToken,
        string broadcasterId,
        CancellationToken cancellationToken = default)
    {
        if (_started)
        {
            return;
        }

        _clientId = clientId;
        _clientSecret = clientSecret;
        _accessToken = accessToken;
        _broadcasterId = broadcasterId;
        _stopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _started = true;

        _logger.Log(AppLogLevel.Info, "Connecting to Twitch EventSub WebSocket.");

        await ConnectWithRetryAsync().ConfigureAwait(false);
    }

    private async Task ConnectWithRetryAsync()
    {
        Exception? lastFailure = null;

        for (var attempt = 1; attempt <= MaxConnectAttempts; attempt++)
        {
            var connected = false;

            try
            {
                connected = _webSocketUri is null
                    ? await _client.ConnectAsync().ConfigureAwait(false)
                    : await _client.ConnectAsync(_webSocketUri).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (!NetworkFailure.IsTransient(ex))
                {
                    throw;
                }

                lastFailure = ex;
            }

            if (connected)
            {
                if (attempt > 1)
                {
                    _logger.Log(AppLogLevel.Info, $"EventSub WebSocket connected on attempt {attempt}.");
                }

                return;
            }

            if (attempt == MaxConnectAttempts)
            {
                break;
            }

            var delay = TimeSpan.FromSeconds(attempt);
            var detail = lastFailure is null
                ? string.Empty
                : $" ({NetworkFailure.Describe(lastFailure)})";

            // TwitchLib reports a failed websocket handshake by returning false
            // (and raising ErrorOccurred) rather than by throwing, so the result
            // has to be checked explicitly to make the retry actually happen.
            _logger.Log(
                AppLogLevel.Warning,
                $"EventSub connection attempt {attempt}/{MaxConnectAttempts} failed{detail}. " +
                $"Retrying in {delay.TotalSeconds:0} s.");

            await _delay(delay, _stopCts.Token).ConfigureAwait(false);
        }

        throw new InvalidOperationException(
            lastFailure is null
                ? $"The EventSub WebSocket connection failed after {MaxConnectAttempts} attempts."
                : $"The EventSub WebSocket connection failed after {MaxConnectAttempts} attempts: " +
                  NetworkFailure.Describe(lastFailure),
            lastFailure);
    }

    public async Task StopAsync()
    {
        if (!_started)
        {
            return;
        }

        _started = false;
        _stopCts.Cancel();

        try
        {
            await _client.DisconnectAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Log(AppLogLevel.Warning, $"EventSub disconnect failed: {ex.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);

        _client.WebsocketConnected -= OnWebsocketConnected;
        _client.WebsocketDisconnected -= OnWebsocketDisconnected;
        _client.WebsocketReconnected -= OnWebsocketReconnected;
        _client.ErrorOccurred -= OnErrorOccurred;
        _client.ChannelSubscribe -= OnChannelSubscribe;
        _client.ChannelSubscriptionEnd -= OnChannelSubscriptionEnd;
        _client.ChannelSubscriptionGift -= OnChannelSubscriptionGift;
        _client.ChannelSubscriptionMessage -= OnChannelSubscriptionMessage;

        _stopCts.Dispose();
    }

    private Task OnWebsocketConnected(object? sender, WebsocketConnectedArgs e)
    {
        _logger.Log(AppLogLevel.Info, $"EventSub WebSocket connected ({_client.SessionId}).");

        if (e.IsRequestedReconnect)
        {
            return Task.CompletedTask;
        }

        return RegisterSubscriptionsAsync();
    }

    private async Task RegisterSubscriptionsAsync()
    {
        if (!_registerSubscriptions)
        {
            return;
        }

        var api = _apiFactory.Create(_clientId, _clientSecret, _accessToken);
        var broadcasterCondition = new Dictionary<string, string>
        {
            ["broadcaster_user_id"] = _broadcasterId
        };

        var messageCondition = new Dictionary<string, string>
        {
            ["broadcaster_user_id"] = _broadcasterId,
            ["user_id"] = _broadcasterId
        };

        await TryCreateSubscriptionAsync(api, "channel.subscribe", "1", broadcasterCondition);
        await TryCreateSubscriptionAsync(api, "channel.subscription.end", "1", broadcasterCondition);
        await TryCreateSubscriptionAsync(api, "channel.subscription.gift", "1", broadcasterCondition);
        await TryCreateSubscriptionAsync(api, "channel.subscription.message", "1", messageCondition);
    }

    private async Task TryCreateSubscriptionAsync(
        TwitchLib.Api.TwitchAPI api,
        string type,
        string version,
        IReadOnlyDictionary<string, string> condition)
    {
        try
        {
            await api.Helix.EventSub.CreateEventSubSubscriptionAsync(
                type,
                version,
                new Dictionary<string, string>(condition),
                EventSubTransportMethod.Websocket,
                _client.SessionId).ConfigureAwait(false);

            _logger.Log(AppLogLevel.Info, $"Registered EventSub subscription: {type}.");
        }
        catch (Exception ex)
        {
            _logger.Log(AppLogLevel.Error, $"Could not register EventSub subscription {type}: {ex.Message}");
        }
    }

    private Task OnWebsocketDisconnected(object? sender, WebsocketDisconnectedArgs e)
    {
        _logger.Log(AppLogLevel.Warning, "EventSub WebSocket disconnected.");
        StartReconnectLoop();
        return Task.CompletedTask;
    }

    private Task OnWebsocketReconnected(object? sender, WebsocketReconnectedArgs e)
    {
        _logger.Log(AppLogLevel.Info, "EventSub WebSocket reconnected.");
        return Task.CompletedTask;
    }

    private Task OnErrorOccurred(object? sender, ErrorOccuredArgs e)
    {
        var message = e.Exception is not null
            ? NetworkFailure.Describe(e.Exception)
            : string.IsNullOrWhiteSpace(e.Message) ? null : e.Message;
        var effectiveMessage = message ?? "unknown error";
        _logger.Log(AppLogLevel.Error, $"EventSub error: {effectiveMessage}");
        return RaiseConnectionErrorAsync(effectiveMessage);
    }

    private async Task RaiseConnectionErrorAsync(string message)
    {
        var handler = ConnectionError;
        if (handler is null)
        {
            return;
        }

        foreach (var invocation in handler.GetInvocationList())
        {
            if (invocation is Func<string, Task> asyncInvocation)
            {
                await asyncInvocation(message).ConfigureAwait(false);
            }
        }
    }

    private Task OnChannelSubscribe(object? sender, ChannelSubscribeArgs e)
    {
        var subscriber = MapSubscriber(
            e.Payload.Event.UserId,
            e.Payload.Event.UserLogin,
            e.Payload.Event.UserName,
            e.Payload.Event.Tier,
            e.Payload.Event.IsGift);

        _logger.Log(AppLogLevel.Info, $"EventSub: {subscriber.UserLogin} subscribed.");
        return RaiseSubscriberListChangedAsync(SubscriberChangeType.Subscribed, subscriber);
    }

    private Task OnChannelSubscriptionEnd(object? sender, ChannelSubscriptionEndArgs e)
    {
        var subscriber = MapSubscriber(
            e.Payload.Event.UserId,
            e.Payload.Event.UserLogin,
            e.Payload.Event.UserName,
            e.Payload.Event.Tier,
            e.Payload.Event.IsGift);

        _logger.Log(AppLogLevel.Info, $"EventSub: {subscriber.UserLogin} subscription ended.");
        return RaiseSubscriberListChangedAsync(SubscriberChangeType.Unsubscribed, subscriber);
    }

    private Task OnChannelSubscriptionGift(object? sender, ChannelSubscriptionGiftArgs e)
    {
        var subscriber = MapSubscriber(
            e.Payload.Event.UserId,
            e.Payload.Event.UserLogin,
            e.Payload.Event.UserName,
            e.Payload.Event.Tier,
            isGift: true);

        _logger.Log(AppLogLevel.Info, $"EventSub: {subscriber.UserLogin} gifted {e.Payload.Event.Total} subscription(s).");
        return RaiseSubscriberListChangedAsync(SubscriberChangeType.Gifted, subscriber);
    }

    private Task OnChannelSubscriptionMessage(object? sender, ChannelSubscriptionMessageArgs e)
    {
        var subscriber = MapSubscriber(
            e.Payload.Event.UserId,
            e.Payload.Event.UserLogin,
            e.Payload.Event.UserName,
            e.Payload.Event.Tier,
            isGift: false);

        _logger.Log(AppLogLevel.Info, $"EventSub: subscription message from {subscriber.UserLogin}.");
        return RaiseSubscriberListChangedAsync(SubscriberChangeType.Resubscribed, subscriber);
    }

    private async Task RaiseSubscriberListChangedAsync(
        SubscriberChangeType changeType,
        ActiveSubscriber subscriber)
    {
        var handler = SubscriberListChanged;
        if (handler is null)
        {
            return;
        }

        foreach (var invocation in handler.GetInvocationList())
        {
            if (invocation is Func<SubscriberChangedEventArgs, Task> asyncInvocation)
            {
                await asyncInvocation(new SubscriberChangedEventArgs(changeType, subscriber))
                    .ConfigureAwait(false);
            }
        }
    }

    private static ActiveSubscriber MapSubscriber(
        string? userId,
        string? userLogin,
        string? userName,
        string? tier,
        bool isGift)
    {
        return new ActiveSubscriber(
            userId ?? string.Empty,
            userLogin ?? string.Empty,
            string.IsNullOrWhiteSpace(userName) ? userLogin ?? string.Empty : userName,
            tier ?? string.Empty,
            isGift);
    }

    private void StartReconnectLoop()
    {
        if (_reconnectLoopRunning)
        {
            return;
        }

        _reconnectLoopRunning = true;
        _ = Task.Run(ReconnectLoopAsync);
    }

    // EventSub WebSockets can drop for reasons outside our control. Reconnect with
    // bounded exponential backoff and keep the loop alive until StopAsync cancels it.
    private async Task ReconnectLoopAsync()
    {
        var attempt = 0;

        while (!_stopCts.IsCancellationRequested && _started)
        {
            attempt++;
            var delaySeconds = Math.Min(30, Math.Pow(2, Math.Min(attempt, 5)));

            try
            {
                if (await _client.ReconnectAsync().ConfigureAwait(false))
                {
                    _logger.Log(AppLogLevel.Info, "EventSub WebSocket reconnect succeeded.");
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.Log(
                    AppLogLevel.Warning,
                    $"EventSub reconnect attempt {attempt} failed: {NetworkFailure.Describe(ex)}");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), _stopCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _reconnectLoopRunning = false;
    }
}
