using System;
using System.Threading;
using System.Threading.Tasks;
using TwitchSubscriberPictures.Core.Services;

namespace TwitchSubscriberPictures.Tests;

public sealed class TwitchEventSubServiceTests
{
    [Fact]
    public async Task StartAsync_RetriesFailedWebsocketConnectAndThrowsAfterMaxAttempts()
    {
        var logger = new ListAppLogger();
        var service = new TwitchEventSubService(
            new TwitchApiFactory(),
            logger,
            webSocketUri: new Uri("wss://127.0.0.1:1/ws"),
            registerSubscriptions: false,
            delay: (_, _) => Task.CompletedTask);

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.StartAsync("client-id", string.Empty, "access-token", "broadcaster-id"));

            var retryMessages = logger.Messages
                .Where(message => message.Contains("EventSub connection attempt"))
                .ToList();

            Assert.Equal(3, retryMessages.Count);
            Assert.Contains(retryMessages, message => message.Contains("attempt 1/4 failed"));
            Assert.Contains(retryMessages, message => message.Contains("attempt 3/4 failed"));
        }
        finally
        {
            await service.DisposeAsync();
        }
    }
}
