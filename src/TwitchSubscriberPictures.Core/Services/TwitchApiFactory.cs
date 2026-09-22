using TwitchLib.Api;
using TwitchLib.Api.Core;
using TwitchLib.Api.Core.HttpCallHandlers;
using TwitchLib.Api.Core.Interfaces;
using TwitchSubscriberPictures.Core.Abstractions;

namespace TwitchSubscriberPictures.Core.Services;

public sealed class TwitchApiFactory : ITwitchApiFactory
{
    private readonly IHttpCallHandler _httpCallHandler;

    public TwitchApiFactory(IHttpCallHandler? httpCallHandler = null, IAppLogger? logger = null)
    {
        // A single shared handler keeps one pooled HttpClient (and therefore one
        // warm TLS connection per Twitch host) for the whole application. That
        // matters because Twitch's edge occasionally aborts fresh TLS handshakes.
        _httpCallHandler = httpCallHandler
            ?? new RetryingHttpCallHandler(new TwitchHttpClient(), logger);
    }

    public TwitchAPI Create(string clientId, string clientSecret, string? accessToken = null)
    {
        var settings = new ApiSettings
        {
            ClientId = clientId ?? string.Empty,
            Secret = clientSecret ?? string.Empty,
            AccessToken = accessToken ?? string.Empty
        };

        return new TwitchAPI(settings: settings, http: _httpCallHandler);
    }
}
