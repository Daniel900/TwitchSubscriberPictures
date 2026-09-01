using System.Collections.Generic;

namespace TwitchSubscriberPictures.Core.Models;

public sealed record TokenValidationInfo(
    string UserId,
    string Login,
    IReadOnlyList<string> Scopes,
    int ExpiresInSeconds);
