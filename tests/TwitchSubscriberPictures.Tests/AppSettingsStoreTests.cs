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
        Assert.Equal(AppSettings.DefaultTwitchClientId, loaded.TwitchClientId);
        Assert.Equal(AppSettings.MinimumPollIntervalMinutes, loaded.PollIntervalMinutes);
    }

    [Fact]
    public async Task LoadAsync_ReturnsDefaults_WhenFileIsEmpty()
    {
        var file = GetTempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, string.Empty);

        var loaded = await new AppSettingsStore(file).LoadAsync();

        Assert.Equal(AppSettings.DefaultTwitchClientId, loaded.TwitchClientId);
        Assert.Equal(AppSettings.DefaultPollIntervalMinutes, loaded.PollIntervalMinutes);
        Assert.Equal(string.Empty, loaded.AllPhotosPath);
    }

    [Fact]
    public async Task LoadAsync_ReturnsDefaults_WhenFileIsCorrupt()
    {
        var file = GetTempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, "{ this is not valid json");

        var loaded = await new AppSettingsStore(file).LoadAsync();

        Assert.Equal(AppSettings.DefaultTwitchClientId, loaded.TwitchClientId);
    }

    [Fact]
    public async Task SaveAsync_OverwritesFileWithoutLeavingTemporaryFiles()
    {
        var file = GetTempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var store = new AppSettingsStore(file);
        await File.WriteAllTextAsync(file, string.Empty);

        await store.SaveAsync(new AppSettings { AllPhotosPath = @"C:\Photos\All" });
        await store.SaveAsync(new AppSettings { AllPhotosPath = @"C:\Photos\New" });

        var loaded = await store.LoadAsync();
        Assert.Equal(@"C:\Photos\New", loaded.AllPhotosPath);

        var files = Directory.GetFiles(Path.GetDirectoryName(file)!);
        Assert.Single(files);
        Assert.Equal(file, files[0]);
    }

    private static string GetTempFile()
        => Path.Combine(Path.GetTempPath(), "tsp-tests", Guid.NewGuid().ToString("N"), "settings.json");
}
