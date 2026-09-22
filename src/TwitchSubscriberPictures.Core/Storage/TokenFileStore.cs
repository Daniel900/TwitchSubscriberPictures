using System;
using System.IO;
using System.Security.Cryptography;
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

        try
        {
            var encrypted = await File.ReadAllBytesAsync(_filePath, cancellationToken).ConfigureAwait(false);
            if (encrypted.Length == 0)
            {
                return null;
            }

            var decrypted = _protector.Unprotect(encrypted);
            var json = Encoding.UTF8.GetString(decrypted);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            return JsonSerializer.Deserialize<TwitchToken>(json, SerializerOptions);
        }
        catch (Exception ex) when (ex is JsonException or CryptographicException or InvalidOperationException)
        {
            // A damaged token file must not block startup; the app simply runs
            // the device-code flow again and overwrites it.
            return null;
        }
    }

    public async Task SaveAsync(TwitchToken token, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);

        var directory = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(token, SerializerOptions);
        var plaintext = Encoding.UTF8.GetBytes(json);
        var encrypted = _protector.Protect(plaintext);
        var tempPath = Path.Combine(directory, $"{Path.GetFileName(_filePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllBytesAsync(tempPath, encrypted, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                    // Cleanup is best effort.
                }
            }
        }
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
