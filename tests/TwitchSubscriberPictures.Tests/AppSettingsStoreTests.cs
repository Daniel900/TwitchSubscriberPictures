using System;
using System.IO;
using System.Threading.Tasks;
using TwitchSubscriberPictures.Core.Models;
using TwitchSubscriberPictures.Core.Storage;

namespace TwitchSubscriberPictures.Tests;

public sealed class AppSettingsStoreTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTripsAndClampsPollInterval()
    {
        var file = Path.Combine(Path.GetTempPath(), "tsp-tests", Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);

        var store = new AppSettingsStore(file);
        var settings = new AppSettings
        {
            AllPhotosPath = @"C:\Photos\All",
            ActivePhotosPath = @"C:\Photos\Active",
            TwitchClientId = "client",
            PollIntervalMinutes = 2
        };

        await store.SaveAsync(settings);
        var loaded = await store.LoadAsync();

        Assert.Equal(@"C:\Photos\All", loaded.AllPhotosPath);
        Assert.Equal(@"C:\Photos\Active", loaded.ActivePhotosPath);
        Assert.Equal("client", loaded.TwitchClientId);
        Assert.Equal(AppSettings.MinimumPollIntervalMinutes, loaded.PollIntervalMinutes);
    }
}
