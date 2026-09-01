using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TwitchLib.Api.Helix.Models.Subscriptions;
using TwitchSubscriberPictures.Core.Abstractions;
using TwitchSubscriberPictures.Core.Models;

namespace TwitchSubscriberPictures.Core.Services;

public sealed class TwitchSubscriberClient
{
    private readonly ITwitchApiFactory _apiFactory;
    private readonly IAppLogger _logger;

    public TwitchSubscriberClient(ITwitchApiFactory apiFactory, IAppLogger logger)
    {
        _apiFactory = apiFactory ?? throw new ArgumentNullException(nameof(apiFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<TokenValidationInfo?> ValidateTokenAsync(
        string clientId,
        string clientSecret,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return null;
        }

        var api = _apiFactory.Create(clientId, clientSecret, accessToken);
        var response = await api.Auth.ValidateAccessTokenAsync(accessToken).ConfigureAwait(false);
        if (response is null)
        {
            return null;
        }

        return new TokenValidationInfo(
            response.UserId,
            response.Login,
            (response.Scopes ?? new List<string>()).ToArray(),
            response.ExpiresIn);
    }

    public async Task<TwitchToken?> RefreshTokenAsync(
        string clientId,
        string clientSecret,
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return null;
        }

        var api = _apiFactory.Create(clientId, clientSecret);
        var response = await api.Auth.RefreshAuthTokenAsync(refreshToken, clientSecret, clientId).ConfigureAwait(false);
        if (response is null)
        {
            return null;
        }

        return new TwitchToken(
            response.AccessToken,
            response.RefreshToken ?? string.Empty,
            DateTimeOffset.UtcNow.AddSeconds(response.ExpiresIn),
            (response.Scopes ?? Array.Empty<string>()).ToArray());
    }

    public async Task<IReadOnlyList<ActiveSubscriber>> GetActiveSubscribersAsync(
        string clientId,
        string clientSecret,
        string accessToken,
        string broadcasterId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(broadcasterId))
        {
            throw new ArgumentException("A broadcaster ID is required.", nameof(broadcasterId));
        }

        var api = _apiFactory.Create(clientId, clientSecret, accessToken);
        var subscribers = new List<ActiveSubscriber>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? after = null;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();

            var response = await api.Helix.Subscriptions.GetBroadcasterSubscriptionsAsync(
                broadcasterId,
                first: 100,
                after: after,
                accessToken: accessToken).ConfigureAwait(false);

            var page = response.Data ?? Array.Empty<Subscription>();
            foreach (var subscription in page)
            {
                var key = string.IsNullOrWhiteSpace(subscription.UserId)
                    ? subscription.UserLogin
                    : subscription.UserId;

                if (string.IsNullOrWhiteSpace(key))
                {
                    _logger.Log(AppLogLevel.Warning, "Skipped a Twitch subscription with no user ID or login.");
                    continue;
                }

                if (!seen.Add(key))
                {
                    _logger.Log(AppLogLevel.Warning, $"Duplicate subscriber entry skipped for {subscription.UserLogin}.");
                    continue;
                }

                subscribers.Add(new ActiveSubscriber(
                    subscription.UserId ?? string.Empty,
                    subscription.UserLogin ?? string.Empty,
                    subscription.UserName ?? subscription.UserLogin ?? string.Empty,
                    subscription.Tier ?? string.Empty,
                    subscription.IsGift));
            }

            after = response.Pagination?.Cursor;
        }
        while (!string.IsNullOrWhiteSpace(after));

        _logger.Log(AppLogLevel.Info, $"Fetched {subscribers.Count} active subscriber(s) from Twitch.");
        return subscribers;
    }

    public async Task<string?> GetBroadcasterIdFromTokenAsync(
        string clientId,
        string clientSecret,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        var validation = await ValidateTokenAsync(clientId, clientSecret, accessToken, cancellationToken)
            .ConfigureAwait(false);
        return validation?.UserId;
    }
}
