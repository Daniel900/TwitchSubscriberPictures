using System;
using System.Threading.Tasks;
using System.Windows;
using TwitchSubscriberPictures.Core.Storage;
using TwitchSubscriberPictures.Core.Services;
using TwitchSubscriberPictures.ViewModels;

namespace TwitchSubscriberPictures;

public partial class App : Application
{
    private MainWindow? _mainWindow;
    private MainViewModel? _viewModel;
    private TrayIconController? _trayIcon;
    private TwitchDeviceCodeClient? _deviceCodeClient;
    private bool _isShuttingDown;

    private async void OnStartup(object sender, StartupEventArgs e)
    {
        var logSink = new UiLogSink();
        var settingsStore = new AppSettingsStore();
        var tokenStore = new TokenFileStore(new DpapiTokenProtector());
        var apiFactory = new TwitchApiFactory();
        var subscriberClient = new TwitchSubscriberClient(apiFactory, logSink);
        _deviceCodeClient = new TwitchDeviceCodeClient();
        var deviceCodeAuthService = new TwitchDeviceCodeAuthService(
            _deviceCodeClient,
            new DefaultBrowserOpener(),
            logSink);
        var photoSync = new PhotoSyncEngine(logSink);

        _viewModel = new MainViewModel(
            settingsStore,
            tokenStore,
            apiFactory,
            subscriberClient,
            deviceCodeAuthService,
            _deviceCodeClient,
            photoSync,
            logSink);

        _viewModel.ConnectionStateChanged += status => _trayIcon?.SetStatus(status);

        _mainWindow = new MainWindow
        {
            DataContext = _viewModel
        };

        _trayIcon = new TrayIconController(ShowMainWindow, CloseFromTray);
        _trayIcon.Initialize();
        _trayIcon.SetStatus(_viewModel.ConnectionState);

        _mainWindow.Show();
        await _viewModel.InitializeAsync();
    }

    public async Task ShutdownApplicationAsync()
    {
        if (_isShuttingDown)
        {
            return;
        }

        _isShuttingDown = true;

        if (_viewModel is not null)
        {
            await _viewModel.DisposeAsync();
        }

        _trayIcon?.Dispose();
        _trayIcon = null;

        _deviceCodeClient?.Dispose();
        _deviceCodeClient = null;

        Application.Current.Shutdown();
    }

    private void OnExit(object sender, ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        _deviceCodeClient?.Dispose();
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null || _isShuttingDown)
        {
            return;
        }

        _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        _mainWindow.Activate();
    }

    private async void CloseFromTray()
    {
        if (_mainWindow is null || _isShuttingDown)
        {
            return;
        }

        _mainWindow.AllowClose = true;
        await ShutdownApplicationAsync();
    }
}
