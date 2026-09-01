using System.ComponentModel;
using System.Windows;
using TwitchSubscriberPictures.ViewModels;

namespace TwitchSubscriberPictures;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    public bool AllowClose { get; set; }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

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
                Hide();
                break;

            case ClosePromptAction.Close:
                AllowClose = true;
                Close();
                break;

            default:
                break;
        }
    }
}
