using System.Windows;

namespace TwitchSubscriberPictures;

public enum ClosePromptAction
{
    Cancel,
    MinimizeToTray,
    Close
}

public partial class ClosePromptWindow : Window
{
    public ClosePromptWindow()
    {
        InitializeComponent();
    }

    public ClosePromptAction SelectedAction { get; private set; } = ClosePromptAction.Cancel;

    private void OnMinimizeToTray(object sender, RoutedEventArgs e)
    {
        SelectedAction = ClosePromptAction.MinimizeToTray;
        DialogResult = true;
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        SelectedAction = ClosePromptAction.Close;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        SelectedAction = ClosePromptAction.Cancel;
        DialogResult = false;
    }
}
