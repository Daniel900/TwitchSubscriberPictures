using System;
using System.Linq;
using System.Threading.Tasks;
using TwitchSubscriberPictures.Core.Abstractions;
using TwitchSubscriberPictures.Core.Services;

namespace TwitchSubscriberPictures.IntegrationTests;

[Collection("Twitch CLI")]
public sealed class TwitchSubscriberClientIntegrationTests
{
    private readonly TwitchCliFixture _fixture;

    public TwitchSubscriberClientIntegrationTests(TwitchCliFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetActiveSubscribersAsync_ReturnsSubscribersFromMockApi()
    {
        var clientInfo = await _fixture.GetFirstClientAsync();
        var subscription = await _fixture.GetFirstSubscriptionAsync();
        var authorizationClient = new MockAuthorizationClient(_fixture.MockApiBaseUrl);

        var token = await authorizationClient.ExchangeAuthorizationCodeAsync(
            subscription.BroadcasterId,
            clientInfo.Id,
            clientInfo.Secret,
            "http://localhost:3000/");

        var apiFactory = new TwitchApiFactory(new MockApiHttpCallHandler(_fixture.MockApiBaseUrl));
        var subscriberClient = new TwitchSubscriberClient(apiFactory, NullAppLogger.Instance);

        var subscribers = await subscriberClient.GetActiveSubscribersAsync(
            clientInfo.Id,
            clientInfo.Secret,
            token.AccessToken,
            subscription.BroadcasterId);

        Assert.NotEmpty(subscribers);
        Assert.Contains(subscribers, subscriber =>
            string.Equals(subscriber.UserLogin, subscription.UserLogin, StringComparison.OrdinalIgnoreCase));
    }
}
