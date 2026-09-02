namespace TwitchSubscriberPictures.Core.Models;

public enum SubscriberChangeType
{
    Subscribed,
    Resubscribed,
    Gifted,
    Unsubscribed
}

public sealed record SubscriberChangedEventArgs(
    SubscriberChangeType ChangeType,
    ActiveSubscriber Subscriber);
