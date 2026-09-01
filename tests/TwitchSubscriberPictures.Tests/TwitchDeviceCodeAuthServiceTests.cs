using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TwitchSubscriberPictures.Core.Abstractions;
using TwitchSubscriberPictures.Core.Models;
using TwitchSubscriberPictures.Core.Services;

namespace TwitchSubscriberPictures.Tests;

public sealed class TwitchDeviceCodeAuthServiceTests
{
    [Fact]
    public async Task StartAsync_ReturnsToken_AfterPendingPoll()
    {
        var client = new FakeDeviceCodeClient();
        client.PollResults.Enqueue(new DeviceCodePollResult(
            DeviceCodePollStatus.Pending,
            null,
            null));
        client.PollResults.Enqueue(new DeviceCodePollResult(
            DeviceCodePollStatus.Success,
            new TwitchToken("access", "refresh", DateTimeOffset.UtcNow.AddHours(1), new[] { "channel:read:subscriptions" }),
            null));

        var browser = new RecordingBrowserOpener();
        var service = new TwitchDeviceCodeAuthService(
            client,
            browser,
            new ListAppLogger(),
            delay: (_, _) => Task.CompletedTask);

        var token = await service.StartAsync("client-id");

        Assert.Equal("access", token.AccessToken);
        Assert.Contains("device-code=ABCD12", browser.OpenedUrl);
    }

    [Fact]
    public async Task StartAsync_Throws_WhenAuthorizationIsDenied()
    {
        var client = new FakeDeviceCodeClient();
        client.PollResults.Enqueue(new DeviceCodePollResult(
            DeviceCodePollStatus.AccessDenied,
            null,
            "access_denied"));

        var service = new TwitchDeviceCodeAuthService(
            client,
            new RecordingBrowserOpener(),
            new ListAppLogger(),
            delay: (_, _) => Task.CompletedTask);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync("client-id"));
    }

    private sealed class FakeDeviceCodeClient : ITwitchDeviceCodeClient
    {
        public Queue<DeviceCodePollResult> PollResults { get; } = new();

        public Task<DeviceCodeResponse> GetDeviceCodeAsync(
            string clientId,
            string scope,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new DeviceCodeResponse(
                "device-code",
                "ABCD12",
                "https://www.twitch.tv/activate?device-code=ABCD12",
                600,
                5));
        }

        public Task<DeviceCodePollResult> PollForTokenAsync(
            string clientId,
            string scope,
            string deviceCode,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(PollResults.Dequeue());
        }

        public Task<TwitchToken?> RefreshTokenAsync(
            string clientId,
            string refreshToken,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<TwitchToken?>(null);
        }
    }

    private sealed class RecordingBrowserOpener : IBrowserOpener
    {
        public string OpenedUrl { get; private set; } = string.Empty;

        public Task OpenAsync(string url, CancellationToken cancellationToken = default)
        {
            OpenedUrl = url;
            return Task.CompletedTask;
        }
    }
}
