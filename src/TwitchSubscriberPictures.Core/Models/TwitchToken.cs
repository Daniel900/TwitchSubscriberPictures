using System;
using System.Collections.Generic;
using System.Linq;

namespace TwitchSubscriberPictures.Core.Models;

public sealed record TwitchToken(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAtUtc,
    IReadOnlyList<string> Scopes)
{
    public bool IsExpired(DateTimeOffset? now = null)
    {
        var effectiveNow = now ?? DateTimeOffset.UtcNow;
        return effectiveNow >= ExpiresAtUtc - TimeSpan.FromMinutes(2);
    }

    public bool HasScope(string scope)
    {
        return Scopes.Any(existing =>
            string.Equals(existing, scope, StringComparison.OrdinalIgnoreCase));
    }
}
