using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using TwitchLib.Api.Core.Enums;
using TwitchLib.Api.Core.Interfaces;

namespace TwitchSubscriberPictures.IntegrationTests;

public sealed class MockApiHttpCallHandler : IHttpCallHandler
{
    private readonly HttpClient _http = new();
    private readonly string _mockBaseUrl;

    public MockApiHttpCallHandler(string mockBaseUrl)
    {
        _mockBaseUrl = mockBaseUrl.TrimEnd('/');
    }

    public async Task<KeyValuePair<int, string>> GeneralRequestAsync(
        string url,
        string method,
        string payload,
        ApiVersion api,
        string clientId,
        string accessToken)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), RewriteUrl(url));

        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        if (!string.IsNullOrWhiteSpace(clientId))
        {
            request.Headers.TryAddWithoutValidation("Client-Id", clientId);
        }

        if (!string.IsNullOrWhiteSpace(payload))
        {
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        }

        using var response = await _http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        return new KeyValuePair<int, string>((int)response.StatusCode, body);
    }

    public async Task PutBytesAsync(string url, byte[] payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, RewriteUrl(url))
        {
            Content = new ByteArrayContent(payload)
        };

        using var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    public async Task<int> RequestReturnResponseCodeAsync(
        string url,
        string method,
        List<KeyValuePair<string, string>> getParams)
    {
        var builder = new UriBuilder(RewriteUrl(url));
        var query = new StringBuilder(builder.Query.TrimStart('?'));

        foreach (var parameter in getParams)
        {
            if (query.Length > 0)
            {
                query.Append('&');
            }

            query.Append(Uri.EscapeDataString(parameter.Key));
            query.Append('=');
            query.Append(Uri.EscapeDataString(parameter.Value));
        }

        builder.Query = query.ToString();
        using var request = new HttpRequestMessage(new HttpMethod(method), builder.Uri);
        using var response = await _http.SendAsync(request);
        return (int)response.StatusCode;
    }

    private string RewriteUrl(string url)
    {
        var uri = new Uri(url);

        if (uri.Host.Equals("api.twitch.tv", StringComparison.OrdinalIgnoreCase))
        {
            var path = uri.PathAndQuery;
            if (path.StartsWith("/helix/", StringComparison.OrdinalIgnoreCase))
            {
                path = "/mock" + path["/helix".Length..];
            }
            else if (path.Equals("/helix", StringComparison.OrdinalIgnoreCase))
            {
                path = "/mock";
            }
            else
            {
                path = "/mock" + path;
            }

            return new Uri(new Uri(_mockBaseUrl), path).ToString();
        }

        if (uri.Host.Equals("id.twitch.tv", StringComparison.OrdinalIgnoreCase))
        {
            var path = uri.PathAndQuery;
            if (path.StartsWith("/oauth2/token", StringComparison.OrdinalIgnoreCase))
            {
                path = "/auth/token" + path["/oauth2/token".Length..];
            }
            else if (path.StartsWith("/oauth2/validate", StringComparison.OrdinalIgnoreCase))
            {
                path = "/auth/validate" + path["/oauth2/validate".Length..];
            }
            else if (path.StartsWith("/oauth2/", StringComparison.OrdinalIgnoreCase))
            {
                path = "/auth/" + path["/oauth2/".Length..];
            }

            return new Uri(new Uri(_mockBaseUrl), path).ToString();
        }

        return url;
    }
}
