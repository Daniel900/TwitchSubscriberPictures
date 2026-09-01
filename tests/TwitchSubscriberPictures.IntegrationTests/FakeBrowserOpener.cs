using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TwitchSubscriberPictures.Core.Abstractions;

namespace TwitchSubscriberPictures.IntegrationTests;

public sealed class FakeBrowserOpener : IBrowserOpener
{
    private readonly string _authorizationCode;

    public FakeBrowserOpener(string authorizationCode)
    {
        _authorizationCode = authorizationCode;
    }

    public Task OpenAsync(string url, CancellationToken cancellationToken = default)
    {
        var uri = new Uri(url);
        var query = ParseQuery(uri.Query);
        var redirectUri = query["redirect_uri"];
        var state = query["state"];

        _ = Task.Run(async () =>
        {
            await Task.Delay(300, cancellationToken);
            using var http = new HttpClient();
            await http.GetAsync(
                $"{redirectUri}?code={Uri.EscapeDataString(_authorizationCode)}&state={Uri.EscapeDataString(state)}",
                cancellationToken);
        }, cancellationToken);

        return Task.CompletedTask;
    }

    private static System.Collections.Generic.Dictionary<string, string> ParseQuery(string query)
    {
        return query
            .TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(
                parts => Uri.UnescapeDataString(parts[0]),
                parts => parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : string.Empty,
                StringComparer.OrdinalIgnoreCase);
    }
}
