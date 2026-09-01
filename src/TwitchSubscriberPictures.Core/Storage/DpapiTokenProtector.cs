using System.Security.Cryptography;
using System.Text;
using System.Runtime.Versioning;
using TwitchSubscriberPictures.Core.Abstractions;

namespace TwitchSubscriberPictures.Core.Storage;

/// <summary>
/// Protects token bytes with Windows DPAPI, scoped to the current user.
/// The entropy value is not a secret; it simply binds the ciphertext to this application.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiTokenProtector : ITokenProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TwitchSubscriberPictures.Token.v1");

    public byte[] Protect(byte[] plaintext)
    {
        return ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
    }

    public byte[] Unprotect(byte[] protectedData)
    {
        return ProtectedData.Unprotect(protectedData, Entropy, DataProtectionScope.CurrentUser);
    }
}
