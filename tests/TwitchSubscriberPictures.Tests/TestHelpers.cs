using System;
using System.Collections.Generic;
using System.Text;
using TwitchSubscriberPictures.Core.Abstractions;

namespace TwitchSubscriberPictures.Tests;

public sealed class ListAppLogger : IAppLogger
{
    public List<string> Messages { get; } = new();

    public void Log(AppLogLevel level, string message)
    {
        Messages.Add($"{level}: {message}");
    }
}

public sealed class FakeTokenProtector : ITokenProtector
{
    public byte[] Protect(byte[] plaintext)
    {
        var copy = new byte[plaintext.Length];
        Buffer.BlockCopy(plaintext, 0, copy, 0, plaintext.Length);
        return Encoding.UTF8.GetBytes("protected:" + Convert.ToBase64String(copy));
    }

    public byte[] Unprotect(byte[] protectedData)
    {
        var text = Encoding.UTF8.GetString(protectedData);
        if (!text.StartsWith("protected:", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Invalid protected payload.");
        }

        return Convert.FromBase64String(text["protected:".Length..]);
    }
}
