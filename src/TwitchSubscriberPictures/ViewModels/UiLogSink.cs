using System;
using System.Collections.ObjectModel;
using System.Windows;
using TwitchSubscriberPictures.Core.Abstractions;

namespace TwitchSubscriberPictures.ViewModels;

/// <summary>
/// Writes log entries into an ObservableCollection on the WPF UI thread. This is
/// intentionally non-modal: errors never surface as message boxes or popups.
/// </summary>
public sealed class UiLogSink : IAppLogger
{
    private const int MaxEntries = 1000;

    public ObservableCollection<LogEntryViewModel> Entries { get; } = new();

    public void Log(AppLogLevel level, string message)
    {
        var entry = new LogEntryViewModel(
            DateTimeOffset.Now,
            level.ToString().ToUpperInvariant(),
            message);

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            AddEntry(entry);
        }
        else
        {
            dispatcher.InvokeAsync(() => AddEntry(entry));
        }
    }

    private void AddEntry(LogEntryViewModel entry)
    {
        Entries.Add(entry);

        while (Entries.Count > MaxEntries)
        {
            Entries.RemoveAt(0);
        }
    }
}
