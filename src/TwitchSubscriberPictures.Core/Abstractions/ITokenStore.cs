using System.Threading;
using System.Threading.Tasks;
using TwitchSubscriberPictures.Core.Models;

namespace TwitchSubscriberPictures.Core.Abstractions;

public interface ITokenStore
{
    Task<TwitchToken?> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(TwitchToken token, CancellationToken cancellationToken = default);

    Task DeleteAsync(CancellationToken cancellationToken = default);
}
