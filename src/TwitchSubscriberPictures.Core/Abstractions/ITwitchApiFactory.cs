using TwitchLib.Api;

namespace TwitchSubscriberPictures.Core.Abstractions;

public interface ITwitchApiFactory
{
    TwitchAPI Create(string clientId, string clientSecret, string? accessToken = null);
}
