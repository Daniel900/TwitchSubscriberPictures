using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using TwitchSubscriberPictures.Core.Abstractions;
using TwitchSubscriberPictures.Core.Models;
using TwitchSubscriberPictures.Core.Services;
using TwitchSubscriberPictures.Core.Storage;

namespace TwitchSubscriberPictures.ViewModels;

public sealed class MainViewModel : ObservableObject, IAsyncDisposable
{
    private readonly AppSettingsStore _settingsStore;
    private readonly ITokenStore _tokenStore;
    private readonly ITwitchApiFactory _apiFactory;
    private readonly TwitchSubscriberClient _subscriberClient;
    private readonly TwitchDeviceCodeAuthService _deviceCodeAuthService;
    private readonly ITwitchDeviceCodeClient _deviceCodeClient;
    private readonly PhotoSyncEngine _photoSync;
    private readonly UiLogSink _logSink;

    private AppSettings _settings = new();
    private TwitchToken? _activeToken;
    private string? _broadcasterId;
    private List<ActiveSubscriber> _activeSubscribers = new();
    private TwitchEventSubService? _eventSubService;
    private CancellationTokenSource? _pollCts;
    private Uri? _eventSubWebSocketUri;
    private bool _registerEventSubSubscriptions;
    private bool _isBusy;
    private bool _isDisposed;

    private string _allPhotosPath = string.Empty;
    private string _activePhotosPath = string.Empty;
    private string _twitchClientId = string.Empty;
    private int _pollIntervalMinutes = AppSettings.DefaultPollIntervalMinutes;
    private string _connectionStatusText = "Disconnected";
    private Brush _connectionStatusBrush = Brushes.Gray;
    private TwitchConnectionStatus _connectionState = TwitchConnectionStatus.Disconnected;

    public MainViewModel(
        AppSettingsStore settingsStore,
        ITokenStore tokenStore,
        ITwitchApiFactory apiFactory,
        TwitchSubscriberClient subscriberClient,
        TwitchDeviceCodeAuthService deviceCodeAuthService,
        ITwitchDeviceCodeClient deviceCodeClient,
        PhotoSyncEngine photoSync,
        UiLogSink logSink,
        Uri? eventSubWebSocketUri = null,
        bool registerEventSubSubscriptions = true)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _tokenStore = tokenStore ?? throw new ArgumentNullException(nameof(tokenStore));
        _apiFactory = apiFactory ?? throw new ArgumentNullException(nameof(apiFactory));
        _subscriberClient = subscriberClient ?? throw new ArgumentNullException(nameof(subscriberClient));
        _deviceCodeAuthService = deviceCodeAuthService ?? throw new ArgumentNullException(nameof(deviceCodeAuthService));
        _deviceCodeClient = deviceCodeClient ?? throw new ArgumentNullException(nameof(deviceCodeClient));
        _photoSync = photoSync ?? throw new ArgumentNullException(nameof(photoSync));
        _logSink = logSink ?? throw new ArgumentNullException(nameof(logSink));
        _eventSubWebSocketUri = eventSubWebSocketUri;
        _registerEventSubSubscriptions = registerEventSubSubscriptions;

        BrowseAllPhotosCommand = new AsyncRelayCommand(BrowseAllPhotosAsync);
        BrowseActivePhotosCommand = new AsyncRelayCommand(BrowseActivePhotosAsync);
        ConnectTwitchCommand = new AsyncRelayCommand(ConnectTwitchAsync);
        UpdateNowCommand = new AsyncRelayCommand(UpdateNowAsync);
        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync);
    }

    public event Action<TwitchConnectionStatus>? ConnectionStateChanged;

    public ObservableCollection<SubscriberPhotoStatus> Subscribers { get; } = new();

    public ObservableCollection<LogEntryViewModel> LogEntries => _logSink.Entries;

    public ICommand BrowseAllPhotosCommand { get; }

    public ICommand BrowseActivePhotosCommand { get; }

    public ICommand ConnectTwitchCommand { get; }

    public ICommand UpdateNowCommand { get; }

    public ICommand SaveSettingsCommand { get; }

    public string AllPhotosPath
    {
        get => _allPhotosPath;
        set => SetProperty(ref _allPhotosPath, value);
    }

    public string ActivePhotosPath
    {
        get => _activePhotosPath;
        set => SetProperty(ref _activePhotosPath, value);
    }

    public string TwitchClientId
    {
        get => _twitchClientId;
        set => SetProperty(ref _twitchClientId, value);
    }

    public int PollIntervalMinutes
    {
        get => _pollIntervalMinutes;
        set => SetProperty(ref _pollIntervalMinutes, value);
    }

    public string ConnectionStatusText
    {
        get => _connectionStatusText;
        private set => SetProperty(ref _connectionStatusText, value);
    }

    public Brush ConnectionStatusBrush
    {
        get => _connectionStatusBrush;
        private set => SetProperty(ref _connectionStatusBrush, value);
    }

    public TwitchConnectionStatus ConnectionState
    {
        get => _connectionState;
        private set => SetProperty(ref _connectionState, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public async Task InitializeAsync()
    {
        _settings = await _settingsStore.LoadAsync();
        AllPhotosPath = _settings.AllPhotosPath;
        ActivePhotosPath = _settings.ActivePhotosPath;
        TwitchClientId = AppSettings.DefaultTwitchClientId;
        PollIntervalMinutes = _settings.PollIntervalMinutes;

        _logSink.Log(AppLogLevel.Info, "Settings loaded.");

        _activeToken = await _tokenStore.LoadAsync();
        if (_activeToken is null)
        {
            SetStatus(TwitchConnectionStatus.TokenInvalid, "Token invalid");

            if (!string.IsNullOrWhiteSpace(TwitchClientId))
            {
                _logSink.Log(AppLogLevel.Info, "No stored Twitch token found. Starting authorization.");
                await StartAuthorizationAsync();
            }
            else
            {
                _logSink.Log(AppLogLevel.Info, "No stored Twitch token found. Enter your Client ID and click Connect Twitch.");
            }

            return;
        }

        await TryStartWithStoredTokenAsync();
    }

    public async Task InitializeMockAsync(
        string clientId,
        string broadcasterId,
        TwitchToken token)
    {
        TwitchClientId = clientId;
        _activeToken = token;
        _broadcasterId = broadcasterId;
        _logSink.Log(AppLogLevel.Info, "Starting in Twitch CLI mock API mode.");
        await StartConnectedAsync();
    }

    public async Task ShutdownAsync()
    {
        if (_pollCts is not null)
        {
            _pollCts.Cancel();
            _pollCts.Dispose();
            _pollCts = null;
        }

        if (_eventSubService is not null)
        {
            await _eventSubService.DisposeAsync().ConfigureAwait(false);
            _eventSubService = null;
        }

        await SaveSettingsAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        await ShutdownAsync().ConfigureAwait(false);
    }

    private async Task TryStartWithStoredTokenAsync()
    {
        if (string.IsNullOrWhiteSpace(TwitchClientId))
        {
            SetStatus(TwitchConnectionStatus.TokenInvalid, "Token invalid");
            _logSink.Log(AppLogLevel.Warning, "Twitch Client ID is missing. Enter it and click Connect Twitch.");
            return;
        }

        try
        {
            var validation = await _subscriberClient.ValidateTokenAsync(
                TwitchClientId,
                string.Empty,
                _activeToken!.AccessToken);

            if (validation is null || _activeToken.IsExpired())
            {
                if (!string.IsNullOrWhiteSpace(_activeToken.RefreshToken))
                {
                    _logSink.Log(AppLogLevel.Info, "Stored Twitch token is invalid or expired. Attempting refresh.");
                    var refreshed = await _deviceCodeClient.RefreshTokenAsync(
                        TwitchClientId,
                        _activeToken.RefreshToken);

                    if (refreshed is not null)
                    {
                        _activeToken = refreshed;
                        await _tokenStore.SaveAsync(_activeToken);
                    }
                }
            }

            if (_activeToken.IsExpired() ||
                await _subscriberClient.ValidateTokenAsync(TwitchClientId, string.Empty, _activeToken.AccessToken) is null)
            {
                await StartAuthorizationAsync();
                return;
            }

            _broadcasterId = await _subscriberClient.GetBroadcasterIdFromTokenAsync(
                TwitchClientId,
                string.Empty,
                _activeToken.AccessToken);

            if (string.IsNullOrWhiteSpace(_broadcasterId))
            {
                _logSink.Log(AppLogLevel.Error, "The Twitch token is valid but did not include a broadcaster user ID.");
                SetStatus(TwitchConnectionStatus.TokenInvalid, "Token invalid");
                return;
            }

            await StartConnectedAsync();
        }
        catch (Exception ex)
        {
            if (NetworkFailure.IsTransient(ex))
            {
                _logSink.Log(
                    AppLogLevel.Error,
                    $"Could not connect to Twitch on startup: {NetworkFailure.Describe(ex)}");
                SetStatus(TwitchConnectionStatus.Disconnected, "No connection to Twitch");
                return;
            }

            _logSink.Log(
                AppLogLevel.Error,
                $"Could not start with stored Twitch token: {NetworkFailure.Describe(ex)}");
            SetStatus(TwitchConnectionStatus.TokenInvalid, "Token invalid");
        }
    }

    private async Task ConnectTwitchAsync()
    {
        await SaveSettingsAsync();
        await StartAuthorizationAsync();
    }

    private async Task StartAuthorizationAsync()
    {
        if (string.IsNullOrWhiteSpace(TwitchClientId))
        {
            SetStatus(TwitchConnectionStatus.TokenInvalid, "Token invalid");
            _logSink.Log(AppLogLevel.Error, "Enter your Twitch Client ID before connecting.");
            return;
        }

        try
        {
            SetStatus(TwitchConnectionStatus.TokenInvalid, "Waiting for Twitch authorization...");
            var token = await _deviceCodeAuthService.StartAsync(TwitchClientId);

            _activeToken = token;
            await _tokenStore.SaveAsync(token);
            _broadcasterId = await _subscriberClient.GetBroadcasterIdFromTokenAsync(
                TwitchClientId,
                string.Empty,
                token.AccessToken);

            await StartConnectedAsync();
        }
        catch (OperationCanceledException)
        {
            SetStatus(TwitchConnectionStatus.TokenInvalid, "Token invalid");
            _logSink.Log(AppLogLevel.Warning, "Twitch authorization was cancelled.");
        }
        catch (Exception ex)
        {
            SetStatus(TwitchConnectionStatus.TokenInvalid, "Token invalid");
            _logSink.Log(AppLogLevel.Error, $"Twitch authorization failed: {NetworkFailure.Describe(ex)}");
        }
    }

    private async Task StartConnectedAsync()
    {
        if (_activeToken is null)
        {
            return;
        }

        SetStatus(TwitchConnectionStatus.Connected, "Connected");
        await RunReconciliationAsync();
        await StartEventSubAsync();
        StartPollLoop();
    }

    private async Task StartEventSubAsync()
    {
        if (_eventSubService is not null)
        {
            await _eventSubService.DisposeAsync();
            _eventSubService = null;
        }

        if (_activeToken is null || string.IsNullOrWhiteSpace(_broadcasterId))
        {
            return;
        }

        _eventSubService = new TwitchEventSubService(
            _apiFactory,
            _logSink,
            webSocketUri: _eventSubWebSocketUri,
            registerSubscriptions: _registerEventSubSubscriptions);
        _eventSubService.SubscriberListChanged += OnSubscriberListChanged;
        _eventSubService.ConnectionError += OnEventSubConnectionError;

        try
        {
            await _eventSubService.StartAsync(
                TwitchClientId,
                string.Empty,
                _activeToken.AccessToken,
                _broadcasterId);

            SetStatus(TwitchConnectionStatus.EventSubLive, "Connected — EventSub live");
        }
        catch (Exception ex)
        {
            SetStatus(TwitchConnectionStatus.Connected, "Connected");
            _logSink.Log(AppLogLevel.Error, $"Could not start EventSub connection: {NetworkFailure.Describe(ex)}");
        }
    }

    private async Task OnEventSubConnectionError(string message)
    {
        SetStatus(TwitchConnectionStatus.Connected, "Connected - EventSub unavailable");
        await Task.CompletedTask;
    }

    private async Task OnSubscriberListChanged(SubscriberChangedEventArgs e)
    {
        switch (e.ChangeType)
        {
            case SubscriberChangeType.Subscribed:
            case SubscriberChangeType.Resubscribed:
                UpsertActiveSubscriber(e.Subscriber);
                await RunFileSyncAsync();
                break;

            case SubscriberChangeType.Unsubscribed:
                RemoveActiveSubscriber(e.Subscriber);
                await RunFileSyncAsync();
                break;

            case SubscriberChangeType.Gifted:
                // A channel.subscription.gift event identifies the gifter but not
                // the individual recipients, so a full Helix refresh is required here.
                await RunReconciliationAsync();
                break;
        }
    }

    private void UpsertActiveSubscriber(ActiveSubscriber subscriber)
    {
        var existingIndex = _activeSubscribers.FindIndex(existing =>
            string.Equals(existing.UserId, subscriber.UserId, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(existing.UserId));

        if (existingIndex < 0)
        {
            existingIndex = _activeSubscribers.FindIndex(existing =>
                string.Equals(existing.UserLogin, subscriber.UserLogin, StringComparison.OrdinalIgnoreCase));
        }

        if (existingIndex >= 0)
        {
            _activeSubscribers[existingIndex] = subscriber;
        }
        else
        {
            _activeSubscribers.Add(subscriber);
        }
    }

    private void RemoveActiveSubscriber(ActiveSubscriber subscriber)
    {
        _activeSubscribers.RemoveAll(existing =>
            string.Equals(existing.UserId, subscriber.UserId, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(existing.UserId) ||
            string.Equals(existing.UserLogin, subscriber.UserLogin, StringComparison.OrdinalIgnoreCase));
    }

    private void StartPollLoop()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = new CancellationTokenSource();

        var token = _pollCts.Token;
        _ = Task.Run(async () =>
        {
            var interval = TimeSpan.FromMinutes(Math.Max(AppSettings.MinimumPollIntervalMinutes, PollIntervalMinutes));
            using var timer = new PeriodicTimer(interval);

            try
            {
                while (await timer.WaitForNextTickAsync(token))
                {
                    _logSink.Log(AppLogLevel.Info, "Scheduled reconciliation started.");
                    await RunReconciliationAsync();
                }
            }
            catch (OperationCanceledException)
            {
            }
        }, token);
    }

    private async Task UpdateNowAsync()
    {
        await RunReconciliationAsync();
    }

    private async Task RunReconciliationAsync()
    {
        if (_isBusy)
        {
            _logSink.Log(AppLogLevel.Info, "Reconciliation is already running.");
            return;
        }

        if (_activeToken is null || string.IsNullOrWhiteSpace(_broadcasterId))
        {
            _logSink.Log(AppLogLevel.Warning, "Twitch is not connected, so reconciliation cannot run.");
            return;
        }

        if (string.IsNullOrWhiteSpace(AllPhotosPath) || string.IsNullOrWhiteSpace(ActivePhotosPath))
        {
            _logSink.Log(AppLogLevel.Error, "Both AllPhotos and ActivePhotos folders must be selected.");
            return;
        }

        IsBusy = true;

        try
        {
            var subscribers = await _subscriberClient.GetActiveSubscribersAsync(
                TwitchClientId,
                string.Empty,
                _activeToken.AccessToken,
                _broadcasterId);

            _activeSubscribers = subscribers.ToList();
            await SyncActiveSubscribersAsync(_activeSubscribers);
        }
        catch (Exception ex)
        {
            _logSink.Log(AppLogLevel.Error, $"Reconciliation failed: {NetworkFailure.Describe(ex)}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunFileSyncAsync()
    {
        if (_isBusy)
        {
            _logSink.Log(AppLogLevel.Info, "File sync is already running.");
            return;
        }

        if (string.IsNullOrWhiteSpace(AllPhotosPath) || string.IsNullOrWhiteSpace(ActivePhotosPath))
        {
            _logSink.Log(AppLogLevel.Error, "Both AllPhotos and ActivePhotos folders must be selected.");
            return;
        }

        IsBusy = true;

        try
        {
            await SyncActiveSubscribersAsync(_activeSubscribers);
        }
        catch (Exception ex)
        {
            _logSink.Log(AppLogLevel.Error, $"File sync failed: {NetworkFailure.Describe(ex)}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SyncActiveSubscribersAsync(IReadOnlyCollection<ActiveSubscriber> subscribers)
    {
        var result = await _photoSync.ReconcileAsync(subscribers, AllPhotosPath, ActivePhotosPath);

        UpdateSubscribers(result.Subscribers);
        _logSink.Log(AppLogLevel.Info, $"Photo sync complete: {subscribers.Count} active subscriber(s).");

        foreach (var error in result.Errors)
        {
            _logSink.Log(AppLogLevel.Error, error);
        }
    }

    private void UpdateSubscribers(IReadOnlyList<SubscriberPhotoStatus> statuses)
    {
        RunOnUi(() =>
        {
            Subscribers.Clear();
            foreach (var status in statuses)
            {
                Subscribers.Add(status);
            }
        });
    }

    private async Task BrowseAllPhotosAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select the AllPhotos folder",
            InitialDirectory = GetInitialDirectory(AllPhotosPath)
        };

        if (dialog.ShowDialog() == true)
        {
            AllPhotosPath = dialog.FolderName;
            await SaveSettingsAsync();
            await TryRunImmediateSyncAsync();
        }
    }

    private async Task BrowseActivePhotosAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select the ActivePhotos folder",
            InitialDirectory = GetInitialDirectory(ActivePhotosPath)
        };

        if (dialog.ShowDialog() == true)
        {
            ActivePhotosPath = dialog.FolderName;
            await SaveSettingsAsync();
            await TryRunImmediateSyncAsync();
        }
    }

    private async Task TryRunImmediateSyncAsync()
    {
        if (string.IsNullOrWhiteSpace(AllPhotosPath) ||
            string.IsNullOrWhiteSpace(ActivePhotosPath) ||
            _activeToken is null ||
            string.IsNullOrWhiteSpace(_broadcasterId))
        {
            return;
        }

        await RunReconciliationAsync();
    }

    private static string GetInitialDirectory(string currentPath)
    {
        if (!string.IsNullOrWhiteSpace(currentPath) && Directory.Exists(currentPath))
        {
            return currentPath;
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
    }

    private async Task SaveSettingsAsync()
    {
        _settings.AllPhotosPath = AllPhotosPath;
        _settings.ActivePhotosPath = ActivePhotosPath;
        _settings.TwitchClientId = TwitchClientId;
        _settings.PollIntervalMinutes = Math.Max(AppSettings.MinimumPollIntervalMinutes, PollIntervalMinutes);
        _settings.Normalize();

        PollIntervalMinutes = _settings.PollIntervalMinutes;

        try
        {
            await _settingsStore.SaveAsync(_settings);
            _logSink.Log(AppLogLevel.Info, "Settings saved.");
        }
        catch (Exception ex)
        {
            _logSink.Log(AppLogLevel.Error, $"Could not save settings: {ex.Message}");
        }
    }

    private void SetStatus(TwitchConnectionStatus status, string text)
    {
        RunOnUi(() =>
        {
            ConnectionState = status;
            ConnectionStatusText = text;
            ConnectionStatusBrush = status switch
            {
                TwitchConnectionStatus.EventSubLive => Brushes.Green,
                TwitchConnectionStatus.Connected => Brushes.YellowGreen,
                TwitchConnectionStatus.TokenInvalid => Brushes.OrangeRed,
                _ => Brushes.Gray
            };

            ConnectionStateChanged?.Invoke(status);
        });
    }

    private void RunOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.InvokeAsync(action);
        }
    }
}
