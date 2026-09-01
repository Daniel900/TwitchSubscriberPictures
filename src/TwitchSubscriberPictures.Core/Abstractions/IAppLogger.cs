using System;

namespace TwitchSubscriberPictures.Core.Abstractions;

public enum AppLogLevel
{
    Info,
    Warning,
    Error
}

public interface IAppLogger
{
    void Log(AppLogLevel level, string message);
}

public sealed class NullAppLogger : IAppLogger
{
    public static NullAppLogger Instance { get; } = new();

    private NullAppLogger()
    {
    }

    public void Log(AppLogLevel level, string message)
    {
    }
}
