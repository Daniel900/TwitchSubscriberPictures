using TwitchLib.Api;
using TwitchLib.Api.Core;
using TwitchLib.Api.Core.Interfaces;
using TwitchSubscriberPictures.Core.Abstractions;

namespace TwitchSubscriberPictures.Core.Services;

public sealed class TwitchApiFactory : ITwitchApiFactory
{
    private readonly IHttpCallHandler? _httpCallHandler;

    public TwitchApiFactory(IHttpCallHandler? httpCallHandler = null)
    {
        _httpCallHandler = httpCallHandler;
    }

    public TwitchAPI Create(string clientId, string clientSecret, string? accessToken = null)
    {
        var settings = new ApiSettings
        {
            ClientId = clientId ?? string.Empty,
            Secret = clientSecret ?? string.Empty,
            AccessToken = accessToken ?? string.Empty
        };

        return _httpCallHandler is null
            ? new TwitchAPI(settings: settings)
            : new TwitchAPI(settings: settings, http: _httpCallHandler);
    }
}
