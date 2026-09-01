using System;

namespace TwitchSubscriberPictures.ViewModels;

public sealed class LogEntryViewModel
{
    public LogEntryViewModel(DateTimeOffset timestamp, string level, string message)
    {
        Timestamp = timestamp;
        Level = level;
        Message = message;
    }

    public DateTimeOffset Timestamp { get; }

    public string Level { get; }

    public string Message { get; }

    public string DisplayText => $"[{Timestamp:HH:mm:ss}] {Level}: {Message}";
}
