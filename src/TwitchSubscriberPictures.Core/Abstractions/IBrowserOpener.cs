using System.Threading;
using System.Threading.Tasks;

namespace TwitchSubscriberPictures.Core.Abstractions;

public interface IBrowserOpener
{
    Task OpenAsync(string url, CancellationToken cancellationToken = default);
}
