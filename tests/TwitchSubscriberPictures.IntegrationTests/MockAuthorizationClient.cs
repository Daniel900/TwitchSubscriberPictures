using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TwitchSubscriberPictures.Core.Abstractions;
using TwitchSubscriberPictures.Core.Models;

namespace TwitchSubscriberPictures.IntegrationTests;

public sealed class MockAuthorizationClient : ITwitchAuthorizationClient
{
    private readonly HttpClient _http = new();
    private readonly string _mockBaseUrl;

    public MockAuthorizationClient(string mockBaseUrl)
    {
        _mockBaseUrl = mockBaseUrl.TrimEnd('/');
    }

    public async Task<TwitchToken> ExchangeAuthorizationCodeAsync(
        string code,
        string clientId,
        string clientSecret,
        string redirectUri,
        CancellationToken cancellationToken = default)
    {
        var url =
            $"{_mockBaseUrl}/auth/authorize" +
            $"?client_id={Uri.EscapeDataString(clientId)}" +
            $"&client_secret={Uri.EscapeDataString(clientSecret)}" +
            "&grant_type=user_token" +
            $"&user_id={Uri.EscapeDataString(code)}" +
            $"&scope={Uri.EscapeDataString(TwitchOAuthConstants.RequiredScope)}";

        using var response = await _http.PostAsync(url, content: null, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var accessToken = root.GetProperty("access_token").GetString()!;
        var expiresIn = root.GetProperty("expires_in").GetInt32();
        var scopes = root.TryGetProperty("scope", out var scopeElement)
            ? scopeElement.EnumerateArray()
                .Where(scope => scope.ValueKind == JsonValueKind.String)
                .Select(scope => scope.GetString()!)
                .ToArray()
            : Array.Empty<string>();

        return new TwitchToken(
            accessToken,
            string.Empty,
            DateTimeOffset.UtcNow.AddSeconds(expiresIn),
            scopes);
    }
}
