namespace TuckClip.Core.Persistence;

public interface IDataProtector
{
    byte[] Protect(ReadOnlySpan<byte> plaintext, string purpose);

    byte[] Unprotect(ReadOnlySpan<byte> protectedData, string purpose);
}
