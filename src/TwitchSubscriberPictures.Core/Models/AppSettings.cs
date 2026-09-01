using System;

namespace TwitchSubscriberPictures.Core.Models;

public sealed class AppSettings
{
    public const int DefaultPollIntervalMinutes = 15;
    public const int MinimumPollIntervalMinutes = 10;
    public const string DefaultTwitchClientId = "h0qx0mz94bss0sdvdk6objq03jbb3j";

    public string AllPhotosPath { get; set; } = string.Empty;

    public string ActivePhotosPath { get; set; } = string.Empty;

    public string TwitchClientId { get; set; } = DefaultTwitchClientId;

    public int PollIntervalMinutes { get; set; } = DefaultPollIntervalMinutes;

    public void Normalize()
    {
        AllPhotosPath = AllPhotosPath?.Trim() ?? string.Empty;
        ActivePhotosPath = ActivePhotosPath?.Trim() ?? string.Empty;
        TwitchClientId = TwitchClientId?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(TwitchClientId))
        {
            TwitchClientId = DefaultTwitchClientId;
        }
        if (PollIntervalMinutes < MinimumPollIntervalMinutes)
        {
            PollIntervalMinutes = MinimumPollIntervalMinutes;
        }

    }
}
