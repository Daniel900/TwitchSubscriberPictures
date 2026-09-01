using System;
using System.Threading;
using System.Threading.Tasks;
using TwitchSubscriberPictures.Core.Abstractions;
using TwitchSubscriberPictures.Core.Models;

namespace TwitchSubscriberPictures.Core.Services;

public sealed class TwitchDeviceCodeAuthService
{
    private readonly ITwitchDeviceCodeClient _deviceCodeClient;
    private readonly IBrowserOpener _browserOpener;
    private readonly IAppLogger _logger;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public TwitchDeviceCodeAuthService(
        ITwitchDeviceCodeClient deviceCodeClient,
        IBrowserOpener browserOpener,
        IAppLogger logger,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _deviceCodeClient = deviceCodeClient ?? throw new ArgumentNullException(nameof(deviceCodeClient));
        _browserOpener = browserOpener ?? throw new ArgumentNullException(nameof(browserOpener));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _delay = delay ?? Task.Delay;
    }

    public async Task<TwitchToken> StartAsync(
        string clientId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException("Twitch Client ID is required.");
        }

        var deviceCode = await _deviceCodeClient.GetDeviceCodeAsync(
            clientId,
            TwitchOAuthConstants.RequiredScope,
            cancellationToken).ConfigureAwait(false);

        var verificationUri = BuildVerificationUri(deviceCode);
        _logger.Log(AppLogLevel.Info, $"Twitch device code: {deviceCode.UserCode}");
        _logger.Log(AppLogLevel.Info, $"Open {verificationUri} and enter the code shown above.");

        await _browserOpener.OpenAsync(verificationUri, cancellationToken).ConfigureAwait(false);

        var delay = TimeSpan.FromSeconds(Math.Max(deviceCode.PollIntervalSeconds, 5));
        var deadline = DateTimeOffset.UtcNow.AddSeconds(deviceCode.ExpiresInSeconds);

        while (DateTimeOffset.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            await _delay(delay, cancellationToken).ConfigureAwait(false);

            var pollResult = await _deviceCodeClient.PollForTokenAsync(
                clientId,
                TwitchOAuthConstants.RequiredScope,
                deviceCode.DeviceCode,
                cancellationToken).ConfigureAwait(false);

            switch (pollResult.Status)
            {
                case DeviceCodePollStatus.Success:
                    _logger.Log(AppLogLevel.Info, "Twitch device authorization completed.");
                    return pollResult.Token
                        ?? throw new InvalidOperationException("Twitch returned success but no token was present.");

                case DeviceCodePollStatus.SlowDown:
                    delay += TimeSpan.FromSeconds(5);
                    _logger.Log(AppLogLevel.Info, "Twitch requested a slower device-code polling interval.");
                    break;

                case DeviceCodePollStatus.Pending:
                    break;

                case DeviceCodePollStatus.AccessDenied:
                    throw new InvalidOperationException("Twitch authorization was denied.");

                case DeviceCodePollStatus.Expired:
                    throw new InvalidOperationException("The Twitch device code expired before authorization completed.");

                default:
                    throw new InvalidOperationException(
                        string.IsNullOrWhiteSpace(pollResult.Message)
                            ? "Twitch device authorization failed."
                            : $"Twitch device authorization failed: {pollResult.Message}");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        throw new TimeoutException("Twitch device authorization timed out.");
    }

    private static string BuildVerificationUri(DeviceCodeResponse deviceCode)
    {
        var uri = deviceCode.VerificationUri;
        if (uri.Contains("device-code", StringComparison.OrdinalIgnoreCase))
        {
            return uri;
        }

        var separator = uri.Contains('?') ? '&' : '?';
        return $"{uri}{separator}device-code={Uri.EscapeDataString(deviceCode.UserCode)}";
    }
}
