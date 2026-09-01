using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TwitchSubscriberPictures.Core.Abstractions;
using TwitchSubscriberPictures.Core.Models;

namespace TwitchSubscriberPictures.Core.Storage;

/// <summary>
/// Stores the Twitch token on disk. The JSON representation is encrypted by an
/// <see cref="ITokenProtector"/> before it is written, so access tokens are never
/// persisted as plaintext.
/// </summary>
public sealed class TokenFileStore : ITokenStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly string _filePath;
    private readonly ITokenProtector _protector;

    public TokenFileStore(ITokenProtector protector, string? filePath = null)
    {
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        _filePath = filePath ?? GetDefaultFilePath();
    }

    public static string GetDefaultFilePath()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TwitchSubscriberPictures");

        return Path.Combine(root, "twitch-token.bin");
    }

    public async Task<TwitchToken?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        var encrypted = await File.ReadAllBytesAsync(_filePath, cancellationToken).ConfigureAwait(false);
        var decrypted = _protector.Unprotect(encrypted);
        var json = Encoding.UTF8.GetString(decrypted);
        return JsonSerializer.Deserialize<TwitchToken>(json, SerializerOptions);
    }

    public async Task SaveAsync(TwitchToken token, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);

        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var json = JsonSerializer.Serialize(token, SerializerOptions);
        var plaintext = Encoding.UTF8.GetBytes(json);
        var encrypted = _protector.Protect(plaintext);

        await File.WriteAllBytesAsync(_filePath, encrypted, cancellationToken).ConfigureAwait(false);
    }

    public Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
        }

        return Task.CompletedTask;
    }
}
