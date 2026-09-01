using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using TwitchSubscriberPictures.Core.Abstractions;

namespace TwitchSubscriberPictures.Core.Services;

public sealed class DefaultBrowserOpener : IBrowserOpener
{
    public Task OpenAsync(string url, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });

        return Task.CompletedTask;
    }
}
