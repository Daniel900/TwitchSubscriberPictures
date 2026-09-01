using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Authentication;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TwitchSubscriberPictures.Core.Abstractions;
using TwitchSubscriberPictures.Core.Models;

namespace TwitchSubscriberPictures.Core.Services;

public sealed class TwitchDeviceCodeClient : ITwitchDeviceCodeClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly Uri _baseUri;

    public TwitchDeviceCodeClient(HttpClient? httpClient = null, Uri? baseUri = null)
    {
        _ownsHttpClient = httpClient is null;
        var handler = new SocketsHttpHandler
        {
            SslOptions = new System.Net.Security.SslClientAuthenticationOptions
            {
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
            }
        };

        _httpClient = httpClient ?? new HttpClient(handler);

        var rawBaseUri = (baseUri ?? new Uri("https://id.twitch.tv/oauth2/", UriKind.Absolute)).AbsoluteUri;
        if (!rawBaseUri.EndsWith("/", StringComparison.Ordinal))
        {
            rawBaseUri += "/";
        }

        _baseUri = new Uri(rawBaseUri, UriKind.Absolute);
    }

    public async Task<DeviceCodeResponse> GetDeviceCodeAsync(
        string clientId,
        string scope,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(_baseUri, "device"))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["scopes"] = scope
            })
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Twitch device-code request failed ({(int)response.StatusCode}): {json}");
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        return new DeviceCodeResponse(
            root.GetProperty("device_code").GetString()!,
            root.GetProperty("user_code").GetString()!,
            root.GetProperty("verification_uri").GetString()!,
            root.GetProperty("expires_in").GetInt32(),
            root.TryGetProperty("interval", out var interval) ? interval.GetInt32() : 5);
    }

    public async Task<DeviceCodePollResult> PollForTokenAsync(
        string clientId,
        string scope,
        string deviceCode,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(_baseUri, "token"))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["scopes"] = scope,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                ["device_code"] = deviceCode
            })
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            return new DeviceCodePollResult(
                DeviceCodePollStatus.Success,
                ParseToken(json),
                null);
        }

        var error = ReadOAuthError(json);
        var status = error.ToLowerInvariant() switch
        {
            "authorization_pending" => DeviceCodePollStatus.Pending,
            "slow_down" => DeviceCodePollStatus.SlowDown,
            "access_denied" => DeviceCodePollStatus.AccessDenied,
            "expired_token" => DeviceCodePollStatus.Expired,
            _ => DeviceCodePollStatus.Failed
        };

        return new DeviceCodePollResult(status, null, error);
    }

    public async Task<TwitchToken?> RefreshTokenAsync(
        string clientId,
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return null;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(_baseUri, "token"))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken
            })
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return ParseToken(json);
    }

    private static TwitchToken ParseToken(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var accessToken = root.GetProperty("access_token").GetString()!;
        var refreshToken = root.TryGetProperty("refresh_token", out var refreshElement)
            ? refreshElement.GetString() ?? string.Empty
            : string.Empty;
        var expiresIn = root.GetProperty("expires_in").GetInt32();
        var scopes = root.TryGetProperty("scope", out var scopeElement)
            ? ParseScopes(scopeElement)
            : Array.Empty<string>();

        return new TwitchToken(
            accessToken,
            refreshToken,
            DateTimeOffset.UtcNow.AddSeconds(expiresIn),
            scopes);
    }

    private static string[] ParseScopes(JsonElement scopeElement)
    {
        if (scopeElement.ValueKind == JsonValueKind.Array)
        {
            return scopeElement
                .EnumerateArray()
                .Where(scope => scope.ValueKind == JsonValueKind.String)
                .Select(scope => scope.GetString()!)
                .ToArray();
        }

        if (scopeElement.ValueKind == JsonValueKind.String)
        {
            return scopeElement
                .GetString()!
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }

        return Array.Empty<string>();
    }

    private static string ReadOAuthError(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.TryGetProperty("message", out var message))
            {
                return message.GetString() ?? string.Empty;
            }

            if (root.TryGetProperty("error", out var error))
            {
                return error.GetString() ?? string.Empty;
            }
        }
        catch (JsonException)
        {
        }

        return json;
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
