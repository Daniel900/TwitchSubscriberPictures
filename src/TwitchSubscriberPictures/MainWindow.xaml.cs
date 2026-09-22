using System.ComponentModel;
using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TwitchSubscriberPictures.ViewModels;

namespace TwitchSubscriberPictures;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Icon = LoadApplicationIcon();
    }

    public bool AllowClose { get; set; }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    /// <summary>
    /// Uses the icon that the build embeds into the executable, so the title bar and
    /// the taskbar always show the same application icon.
    /// </summary>
    private static ImageSource? LoadApplicationIcon()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executablePath))
        {
            return null;
        }

        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(executablePath);
            if (icon is null)
            {
                return null;
            }

            var source = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (Exception)
        {
            // The window icon is cosmetic; never let it block startup.
            return null;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (AllowClose)
        {
            return;
        }

        e.Cancel = true;

        var prompt = new ClosePromptWindow
        {
            Owner = this
        };

        if (prompt.ShowDialog() != true)
        {
            return;
        }

        switch (prompt.SelectedAction)
        {
            case ClosePromptAction.MinimizeToTray:
                Dispatcher.BeginInvoke(Hide);
                break;

            case ClosePromptAction.Close:
                AllowClose = true;
                Dispatcher.BeginInvoke(async () =>
                {
                    if (Application.Current is App app)
                    {
                        await app.ShutdownApplicationAsync();
                    }
                });
                break;

            default:
                break;
        }
    }
}
