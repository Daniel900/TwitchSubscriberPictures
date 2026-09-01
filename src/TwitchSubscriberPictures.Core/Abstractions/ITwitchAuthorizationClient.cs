using System.Threading;
using System.Threading.Tasks;
using TwitchSubscriberPictures.Core.Models;

namespace TwitchSubscriberPictures.Core.Abstractions;

public interface ITwitchAuthorizationClient
{
    Task<TwitchToken> ExchangeAuthorizationCodeAsync(
        string code,
        string clientId,
        string clientSecret,
        string redirectUri,
        CancellationToken cancellationToken = default);
}
