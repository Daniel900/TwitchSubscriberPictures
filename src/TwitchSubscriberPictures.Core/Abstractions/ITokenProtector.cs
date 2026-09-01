namespace TwitchSubscriberPictures.Core.Abstractions;

public interface ITokenProtector
{
    byte[] Protect(byte[] plaintext);

    byte[] Unprotect(byte[] protectedData);
}
