using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using TwitchLib.Api.Core.Enums;
using TwitchLib.Api.Core.Interfaces;
using TwitchSubscriberPictures.Core.Abstractions;
using TwitchSubscriberPictures.Core.Services;

namespace TwitchSubscriberPictures.Tests;

public sealed class RetryingHttpCallHandlerTests
{
    [Fact]
    public async Task GeneralRequestAsync_RetriesTransientFailureAndSucceeds()
    {
        var inner = new FlakyHttpCallHandler(failures: 2, CreateTlsResetException());
        var logger = new ListAppLogger();
        var handler = new RetryingHttpCallHandler(inner, logger, maxAttempts: 3, baseDelay: TimeSpan.Zero);

        var result = await handler.GeneralRequestAsync("https://api.twitch.tv/helix/subscriptions", "GET");

        Assert.Equal(200, result.Key);
        Assert.Equal(3, inner.RequestCount);
        Assert.Equal(2, logger.Messages.Count);
    }

    [Fact]
    public async Task GeneralRequestAsync_StopsAfterMaxAttempts()
    {
        var inner = new FlakyHttpCallHandler(failures: int.MaxValue, CreateTlsResetException());
        var logger = new ListAppLogger();
        var handler = new RetryingHttpCallHandler(inner, logger, maxAttempts: 3, baseDelay: TimeSpan.Zero);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => handler.GeneralRequestAsync("https://api.twitch.tv/helix/subscriptions", "GET"));

        Assert.Equal(3, inner.RequestCount);
        Assert.Equal(2, logger.Messages.Count);
    }

    [Fact]
    public async Task GeneralRequestAsync_DoesNotRetryNonTransientFailure()
    {
        var inner = new FlakyHttpCallHandler(failures: int.MaxValue, new InvalidOperationException("bad request"));
        var handler = new RetryingHttpCallHandler(
            inner,
            NullAppLogger.Instance,
            maxAttempts: 3,
            baseDelay: TimeSpan.Zero);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.GeneralRequestAsync("https://api.twitch.tv/helix/subscriptions", "GET"));

        Assert.Equal(1, inner.RequestCount);
    }

    [Fact]
    public async Task PutBytesAsync_RetriesTransientFailure()
    {
        var inner = new FlakyHttpCallHandler(failures: 1, CreateTlsResetException());
        var handler = new RetryingHttpCallHandler(
            inner,
            NullAppLogger.Instance,
            maxAttempts: 2,
            baseDelay: TimeSpan.Zero);

        await handler.PutBytesAsync("https://api.twitch.tv/helix/example", new byte[] { 1, 2, 3 });

        Assert.Equal(2, inner.RequestCount);
    }

    [Fact]
    public async Task RequestReturnResponseCodeAsync_RetriesTransientFailure()
    {
        var inner = new FlakyHttpCallHandler(failures: 1, CreateTlsResetException());
        var handler = new RetryingHttpCallHandler(
            inner,
            NullAppLogger.Instance,
            maxAttempts: 2,
            baseDelay: TimeSpan.Zero);

        var status = await handler.RequestReturnResponseCodeAsync("https://api.twitch.tv/helix/example", "GET");

        Assert.Equal(204, status);
        Assert.Equal(2, inner.RequestCount);
    }

    [Fact]
    public void IsTransient_RecognizesWrappedTlsResetAndIgnoresOtherErrors()
    {
        Assert.True(NetworkFailure.IsTransient(CreateTlsResetException()));
        Assert.False(NetworkFailure.IsTransient(new InvalidOperationException("not a network error")));
    }

    [Fact]
    public void Describe_IncludesInnerExceptionChain()
    {
        var description = NetworkFailure.Describe(CreateTlsResetException());

        Assert.Contains("HttpRequestException", description);
        Assert.Contains("IOException", description);
        Assert.Contains("SocketException", description);
    }

    private static HttpRequestException CreateTlsResetException()
        => new(
            "The SSL connection could not be established, see inner exception.",
            new IOException(
                "Unable to read data from the transport connection.",
                new SocketException(10054)));

    private sealed class FlakyHttpCallHandler : IHttpCallHandler
    {
        private readonly int _failures;
        private readonly Exception _exception;

        public FlakyHttpCallHandler(int failures, Exception exception)
        {
            _failures = failures;
            _exception = exception;
        }

        public int RequestCount { get; private set; }

        public Task<KeyValuePair<int, string>> GeneralRequestAsync(
            string url,
            string method,
            string? payload = null,
            ApiVersion api = ApiVersion.Helix,
            string? clientId = null,
            string? accessToken = null)
        {
            RegisterAttempt();
            return Task.FromResult(new KeyValuePair<int, string>(200, "{}"));
        }

        public Task PutBytesAsync(string url, byte[] payload)
        {
            RegisterAttempt();
            return Task.CompletedTask;
        }

        public Task<int> RequestReturnResponseCodeAsync(
            string url,
            string method,
            List<KeyValuePair<string, string>>? getParams = null)
        {
            RegisterAttempt();
            return Task.FromResult(204);
        }

        private void RegisterAttempt()
        {
            RequestCount++;
            if (RequestCount <= _failures)
            {
                throw _exception;
            }
        }
    }
}
