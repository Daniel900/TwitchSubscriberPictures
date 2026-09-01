namespace TwitchSubscriberPictures.Core.Models;

public sealed record DeviceCodeResponse(
    string DeviceCode,
    string UserCode,
    string VerificationUri,
    int ExpiresInSeconds,
    int PollIntervalSeconds);
