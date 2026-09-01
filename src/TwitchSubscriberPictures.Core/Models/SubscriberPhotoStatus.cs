namespace TwitchSubscriberPictures.Core.Models;

public sealed record SubscriberPhotoStatus(
    string Login,
    string DisplayName,
    bool IsMissingPhoto);
