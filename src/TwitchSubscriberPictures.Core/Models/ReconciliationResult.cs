using System.Collections.Generic;

namespace TwitchSubscriberPictures.Core.Models;

public sealed record ReconciliationResult(
    IReadOnlyList<SubscriberPhotoStatus> Subscribers,
    IReadOnlyList<string> Errors);
