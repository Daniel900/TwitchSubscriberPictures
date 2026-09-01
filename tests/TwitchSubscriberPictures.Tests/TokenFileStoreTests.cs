using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using TwitchSubscriberPictures.Core.Models;
using TwitchSubscriberPictures.Core.Storage;

namespace TwitchSubscriberPictures.Tests;

public sealed class TokenFileStoreTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTripsToken()
    {
        var file = GetTempFile();
        var store = new TokenFileStore(new FakeTokenProtector(), file);
        var token = new TwitchToken(
            "access-token",
            "refresh-token",
            DateTimeOffset.UtcNow.AddHours(1),
            new[] { "channel:read:subscriptions" });

        await store.SaveAsync(token);
        var loaded = await store.LoadAsync();

        Assert.NotNull(loaded);
        Assert.Equal("access-token", loaded.AccessToken);
        Assert.Equal("refresh-token", loaded.RefreshToken);
        Assert.Contains("channel:read:subscriptions", loaded.Scopes);
    }

    [Fact]
    public async Task Save_WritesNoPlaintextTokenToDisk()
    {
        var file = GetTempFile();
        var store = new TokenFileStore(new FakeTokenProtector(), file);
        var token = new TwitchToken(
            "super-secret-access-token",
            "super-secret-refresh-token",
            DateTimeOffset.UtcNow.AddHours(1),
            Array.Empty<string>());

        await store.SaveAsync(token);
        var bytes = await File.ReadAllBytesAsync(file);
        var text = Encoding.UTF8.GetString(bytes);

        Assert.DoesNotContain("super-secret-access-token", text);
        Assert.DoesNotContain("super-secret-refresh-token", text);
    }

    [Fact]
    public async Task Delete_RemovesStoredToken()
    {
        var file = GetTempFile();
        var store = new TokenFileStore(new FakeTokenProtector(), file);
        await store.SaveAsync(new TwitchToken("a", "r", DateTimeOffset.UtcNow.AddHours(1), Array.Empty<string>()));

        await store.DeleteAsync();

        Assert.False(File.Exists(file));
        Assert.Null(await store.LoadAsync());
    }

    private static string GetTempFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tsp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "token.bin");
    }
}
