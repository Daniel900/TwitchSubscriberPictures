namespace TwitchSubscriberPictures.Core.Models;

public enum DeviceCodePollStatus
{
    Pending,
    SlowDown,
    Success,
    AccessDenied,
    Expired,
    Failed
}

public sealed record DeviceCodePollResult(
    DeviceCodePollStatus Status,
    TwitchToken? Token,
    string? Message);
