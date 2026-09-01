namespace TwitchSubscriberPictures.Core.Models;

public sealed record ActiveSubscriber(
    string UserId,
    string UserLogin,
    string UserName,
    string Tier,
    bool IsGift);
