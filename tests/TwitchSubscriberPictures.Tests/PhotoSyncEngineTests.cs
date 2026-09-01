using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TwitchSubscriberPictures.Core.Models;
using TwitchSubscriberPictures.Core.Services;

namespace TwitchSubscriberPictures.Tests;

public sealed class PhotoSyncEngineTests
{
    [Fact]
    public async Task ReconcileAsync_CopiesActiveSubscriberPhoto_AndDoesNotDeleteMaster()
    {
        using var temp = new TempDirectory();
        var all = Path.Combine(temp.Path, "AllPhotos");
        var active = Path.Combine(temp.Path, "ActivePhotos");
        Directory.CreateDirectory(all);

        var source = Path.Combine(all, "alice.png");
        await File.WriteAllBytesAsync(source, new byte[] { 1, 2, 3 });

        var logger = new ListAppLogger();
        var engine = new PhotoSyncEngine(logger);

        var result = await engine.ReconcileAsync(
            new[] { new ActiveSubscriber("1", "alice", "Alice", "1000", false) },
            all,
            active);

        Assert.Empty(result.Errors);
        Assert.False(result.Subscribers.Single().IsMissingPhoto);
        Assert.True(File.Exists(Path.Combine(active, "alice.png")));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task ReconcileAsync_MissingPhoto_IsFlagged()
    {
        using var temp = new TempDirectory();
        var all = Path.Combine(temp.Path, "AllPhotos");
        var active = Path.Combine(temp.Path, "ActivePhotos");
        Directory.CreateDirectory(all);

        var logger = new ListAppLogger();
        var engine = new PhotoSyncEngine(logger);

        var result = await engine.ReconcileAsync(
            new[] { new ActiveSubscriber("1", "alice", "Alice", "1000", false) },
            all,
            active);

        Assert.True(result.Subscribers.Single().IsMissingPhoto);
        Assert.Empty(Directory.EnumerateFiles(active));
    }

    [Fact]
    public async Task ReconcileAsync_RemovesOnlyInactivePhotoFromActivePhotos()
    {
        using var temp = new TempDirectory();
        var all = Path.Combine(temp.Path, "AllPhotos");
        var active = Path.Combine(temp.Path, "ActivePhotos");
        Directory.CreateDirectory(all);
        Directory.CreateDirectory(active);

        await File.WriteAllBytesAsync(Path.Combine(all, "alice.png"), new byte[] { 1 });
        await File.WriteAllBytesAsync(Path.Combine(active, "alice.png"), new byte[] { 1 });
        await File.WriteAllBytesAsync(Path.Combine(active, "bob.jpg"), new byte[] { 2 });
        await File.WriteAllBytesAsync(Path.Combine(all, "bob.jpg"), new byte[] { 2 });

        var logger = new ListAppLogger();
        var engine = new PhotoSyncEngine(logger);

        var result = await engine.ReconcileAsync(
            new[] { new ActiveSubscriber("1", "alice", "Alice", "1000", false) },
            all,
            active);

        Assert.True(File.Exists(Path.Combine(active, "alice.png")));
        Assert.False(File.Exists(Path.Combine(active, "bob.jpg")));
        Assert.True(File.Exists(Path.Combine(all, "bob.jpg")));
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ReconcileAsync_MultipleMatches_UsesFirstAndLogsWarning()
    {
        using var temp = new TempDirectory();
        var all = Path.Combine(temp.Path, "AllPhotos");
        var active = Path.Combine(temp.Path, "ActivePhotos");
        Directory.CreateDirectory(all);

        await File.WriteAllBytesAsync(Path.Combine(all, "alice.png"), new byte[] { 1 });
        await File.WriteAllBytesAsync(Path.Combine(all, "alice.jpg"), new byte[] { 2 });

        var logger = new ListAppLogger();
        var engine = new PhotoSyncEngine(logger);

        var result = await engine.ReconcileAsync(
            new[] { new ActiveSubscriber("1", "alice", "Alice", "1000", false) },
            all,
            active);

        Assert.False(result.Subscribers.Single().IsMissingPhoto);
        Assert.True(File.Exists(Path.Combine(active, "alice.jpg")));
        Assert.Contains(logger.Messages, message => message.Contains("Multiple files", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tsp-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
