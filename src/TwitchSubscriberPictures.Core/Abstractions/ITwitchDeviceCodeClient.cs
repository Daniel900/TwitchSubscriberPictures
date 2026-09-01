using System.Threading;
using System.Threading.Tasks;
using TwitchSubscriberPictures.Core.Models;

namespace TwitchSubscriberPictures.Core.Abstractions;

public interface ITwitchDeviceCodeClient
{
    Task<DeviceCodeResponse> GetDeviceCodeAsync(
        string clientId,
        string scope,
        CancellationToken cancellationToken = default);

    Task<DeviceCodePollResult> PollForTokenAsync(
        string clientId,
        string scope,
        string deviceCode,
        CancellationToken cancellationToken = default);

    Task<TwitchToken?> RefreshTokenAsync(
        string clientId,
        string refreshToken,
        CancellationToken cancellationToken = default);
}
