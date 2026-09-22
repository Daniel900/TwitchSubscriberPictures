using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TwitchLib.Api.Core.Enums;
using TwitchLib.Api.Core.Interfaces;
using TwitchSubscriberPictures.Core.Abstractions;

namespace TwitchSubscriberPictures.Core.Services;

/// <summary>
/// Decorates TwitchLib's HTTP handler with retries for transport-level failures.
/// Twitch's edge endpoints occasionally abort a TLS handshake; a fresh attempt
/// almost always succeeds, so this keeps startup and authorization from failing
/// on a single reset connection.
/// </summary>
public sealed class RetryingHttpCallHandler : IHttpCallHandler
{
    public const int DefaultMaxAttempts = 3;
    private static readonly TimeSpan DefaultBaseDelay = TimeSpan.FromMilliseconds(500);

    private readonly IHttpCallHandler _inner;
    private readonly IAppLogger _logger;
    private readonly int _maxAttempts;
    private readonly TimeSpan _baseDelay;
    private readonly Func<TimeSpan, Task> _delay;

    public RetryingHttpCallHandler(
        IHttpCallHandler inner,
        IAppLogger? logger = null,
        int maxAttempts = DefaultMaxAttempts,
        TimeSpan? baseDelay = null,
        Func<TimeSpan, Task>? delay = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _logger = logger ?? NullAppLogger.Instance;
        _maxAttempts = Math.Max(1, maxAttempts);
        _baseDelay = baseDelay ?? DefaultBaseDelay;
        _delay = delay ?? Task.Delay;
    }

    public Task<KeyValuePair<int, string>> GeneralRequestAsync(
        string url,
        string method,
        string? payload = null,
        ApiVersion api = ApiVersion.Helix,
        string? clientId = null,
        string? accessToken = null)
        => ExecuteAsync(
            () => _inner.GeneralRequestAsync(url, method, payload, api, clientId, accessToken),
            $"{method} {url}");

    public Task PutBytesAsync(string url, byte[] payload)
        => ExecuteAsync(
            () => _inner.PutBytesAsync(url, payload),
            $"PUT {url}");

    public Task<int> RequestReturnResponseCodeAsync(
        string url,
        string method,
        List<KeyValuePair<string, string>>? getParams = null)
        => ExecuteAsync(
            () => _inner.RequestReturnResponseCodeAsync(url, method, getParams),
            $"{method} {url}");

    private async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, string description)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await operation().ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < _maxAttempts && NetworkFailure.IsTransient(ex))
            {
                await RetryAsync(description, attempt, ex).ConfigureAwait(false);
            }
        }
    }

    private async Task ExecuteAsync(Func<Task> operation, string description)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await operation().ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (attempt < _maxAttempts && NetworkFailure.IsTransient(ex))
            {
                await RetryAsync(description, attempt, ex).ConfigureAwait(false);
            }
        }
    }

    private Task RetryAsync(string description, int attempt, Exception exception)
    {
        var delay = GetDelay(attempt);
        _logger.Log(
            AppLogLevel.Warning,
            $"{description} failed on attempt {attempt}/{_maxAttempts} " +
            $"({NetworkFailure.Describe(exception)}). Retrying in {delay.TotalMilliseconds:0} ms.");

        return _delay(delay);
    }

    private TimeSpan GetDelay(int attempt)
    {
        if (_baseDelay <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        var exponentialMilliseconds = _baseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
        var jitter = (Random.Shared.NextDouble() * 0.4) - 0.2;
        return TimeSpan.FromMilliseconds(Math.Max(0, exponentialMilliseconds * (1 + jitter)));
    }
}
