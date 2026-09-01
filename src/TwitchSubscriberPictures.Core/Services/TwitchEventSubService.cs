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

namespace TwitchSubscriberPictures.Core.Services;

public sealed class TwitchEventSubService : IAsyncDisposable
{
    private readonly ITwitchApiFactory _apiFactory;
    private readonly IAppLogger _logger;
    private readonly EventSubWebsocketClient _client;
    private readonly bool _registerSubscriptions;
    private readonly Uri? _webSocketUri;

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
        bool registerSubscriptions = true)
    {
        _apiFactory = apiFactory ?? throw new ArgumentNullException(nameof(apiFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _webSocketUri = webSocketUri;
        _registerSubscriptions = registerSubscriptions;
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

    public event Func<Task>? SubscriberListChanged;

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

        if (_webSocketUri is null)
        {
            await _client.ConnectAsync().ConfigureAwait(false);
        }
        else
        {
            await _client.ConnectAsync(_webSocketUri).ConfigureAwait(false);
        }
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
        var message = string.IsNullOrWhiteSpace(e.Message) ? e.Exception?.Message : e.Message;
        _logger.Log(AppLogLevel.Error, $"EventSub error: {message ?? "unknown error"}");
        return Task.CompletedTask;
    }

    private Task OnChannelSubscribe(object? sender, ChannelSubscribeArgs e)
    {
        _logger.Log(AppLogLevel.Info, $"EventSub: {e.Payload.Event.UserLogin} subscribed.");
        return RaiseSubscriberListChangedAsync();
    }

    private Task OnChannelSubscriptionEnd(object? sender, ChannelSubscriptionEndArgs e)
    {
        _logger.Log(AppLogLevel.Info, $"EventSub: {e.Payload.Event.UserLogin} subscription ended.");
        return RaiseSubscriberListChangedAsync();
    }

    private Task OnChannelSubscriptionGift(object? sender, ChannelSubscriptionGiftArgs e)
    {
        _logger.Log(AppLogLevel.Info, $"EventSub: {e.Payload.Event.UserLogin} gifted {e.Payload.Event.Total} subscription(s).");
        return RaiseSubscriberListChangedAsync();
    }

    private Task OnChannelSubscriptionMessage(object? sender, ChannelSubscriptionMessageArgs e)
    {
        _logger.Log(AppLogLevel.Info, $"EventSub: subscription message from {e.Payload.Event.UserLogin}.");
        return RaiseSubscriberListChangedAsync();
    }

    private async Task RaiseSubscriberListChangedAsync()
    {
        var handler = SubscriberListChanged;
        if (handler is null)
        {
            return;
        }

        foreach (var invocation in handler.GetInvocationList())
        {
            if (invocation is Func<Task> asyncInvocation)
            {
                await asyncInvocation().ConfigureAwait(false);
            }
        }
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
                _logger.Log(AppLogLevel.Warning, $"EventSub reconnect attempt {attempt} failed: {ex.Message}");
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
