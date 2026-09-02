using System;
using System.Threading;
using System.Threading.Tasks;
using TwitchSubscriberPictures.Core.Abstractions;
using TwitchSubscriberPictures.Core.Services;
using Xunit.Abstractions;

namespace TwitchSubscriberPictures.IntegrationTests;

[Collection("Twitch CLI")]
public sealed class TwitchEventSubServiceIntegrationTests
{
    private readonly TwitchCliFixture _fixture;
    private readonly ITestOutputHelper _output;

    public TwitchEventSubServiceIntegrationTests(TwitchCliFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task ChannelSubscribe_FromTwitchCliWebSocket_RaisesSubscriberListChanged()
    {
        var service = new TwitchEventSubService(
            new TwitchApiFactory(),
            NullAppLogger.Instance,
            webSocketUri: new Uri(_fixture.WebSocketBaseUrl + "/ws"),
            registerSubscriptions: false);

        var changed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.SubscriberListChanged += _ =>
        {
            _output.WriteLine("EventSub service raised SubscriberListChanged.");
            changed.TrySetResult(true);
            return Task.CompletedTask;
        };

        await service.StartAsync("client-id", "client-secret", "access-token", "broadcaster-id");

        try
        {
            await WaitForSessionAsync(service);
            _output.WriteLine($"Using EventSub session: {service.SessionId}");
            await _fixture.TriggerWebSocketEventAsync("subscribe", service.SessionId!);

            var completed = await Task.WhenAny(changed.Task, Task.Delay(TimeSpan.FromSeconds(20)));
            Assert.Same(changed.Task, completed);
        }
        finally
        {
            await service.DisposeAsync();
        }
    }

    private static async Task WaitForSessionAsync(TwitchEventSubService service)
    {
        for (var i = 0; i < 80; i++)
        {
            if (!string.IsNullOrWhiteSpace(service.SessionId))
            {
                return;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException("EventSub WebSocket did not receive a session ID in time.");
    }
}
