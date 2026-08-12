using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using TuckClip.Core.Persistence;

namespace TuckClip.Platform.Windows.Security;

/// <summary>
/// Protects clipboard payloads with Windows DPAPI for the current interactive
/// user. The purpose is bound as optional entropy, so data cannot be moved
/// between TuckClip persistence domains accidentally.
/// </summary>
public sealed partial class DpapiCurrentUserDataProtector : IDataProtector
{
    private const uint CryptProtectUiForbidden = 0x00000001;
    private const string EntropyDomain = "io.github.iajihga.TuckClip\0";

    public byte[] Protect(ReadOnlySpan<byte> plaintext, string purpose)
    {
        EnsureWindowsAndPurpose(purpose);
        var entropyBytes = DeriveEntropy(purpose);
        var inputBytes = plaintext.ToArray();
        var input = Allocate(inputBytes);
        var entropy = Allocate(entropyBytes);

        try
        {
            if (!CryptProtectData(
                    ref input,
                    "TuckClip local clipboard data",
                    ref entropy,
                    0,
                    0,
                    CryptProtectUiForbidden,
                    out var output))
            {
                throw CreateCryptographicException("DPAPI could not protect the clipboard data.");
            }

            return CopyAndReleaseLocal(output);
        }
        finally
        {
            ClearAndFree(input);
            ClearAndFree(entropy);
            CryptographicOperations.ZeroMemory(inputBytes);
            CryptographicOperations.ZeroMemory(entropyBytes);
        }
    }

    public byte[] Unprotect(ReadOnlySpan<byte> protectedData, string purpose)
    {
        EnsureWindowsAndPurpose(purpose);
        var entropyBytes = DeriveEntropy(purpose);
        var inputBytes = protectedData.ToArray();
        var input = Allocate(inputBytes);
        var entropy = Allocate(entropyBytes);
        nint description = 0;

        try
        {
            if (!CryptUnprotectData(
                    ref input,
                    out description,
                    ref entropy,
                    0,
                    0,
                    CryptProtectUiForbidden,
                    out var output))
            {
                throw CreateCryptographicException("DPAPI could not unprotect the clipboard data.");
            }

            return CopyAndReleaseLocal(output);
        }
        finally
        {
            if (description != 0)
            {
                _ = LocalFree(description);
            }

            ClearAndFree(input);
            ClearAndFree(entropy);
            CryptographicOperations.ZeroMemory(inputBytes);
            CryptographicOperations.ZeroMemory(entropyBytes);
        }
    }

    private static void EnsureWindowsAndPurpose(string purpose)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("DPAPI is only available on Windows.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
    }

    private static byte[] DeriveEntropy(string purpose) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(EntropyDomain + purpose));

    private static DataBlob Allocate(byte[] bytes)
    {
        var pointer = Marshal.AllocHGlobal(Math.Max(bytes.Length, 1));
        if (bytes.Length > 0)
        {
            Marshal.Copy(bytes, 0, pointer, bytes.Length);
        }

        return new DataBlob(bytes.Length, pointer);
    }

    private static unsafe void ClearAndFree(DataBlob blob)
    {
        if (blob.Data == 0)
        {
            return;
        }

        new Span<byte>((void*)blob.Data, Math.Max(blob.Length, 1)).Clear();
        Marshal.FreeHGlobal(blob.Data);
    }

    private static unsafe byte[] CopyAndReleaseLocal(DataBlob blob)
    {
        try
        {
            if (blob.Length < 0 || (blob.Length > 0 && blob.Data == 0))
            {
                throw new CryptographicException("DPAPI returned an invalid data buffer.");
            }

            var result = new byte[blob.Length];
            if (blob.Length > 0)
            {
                Marshal.Copy(blob.Data, result, 0, blob.Length);
                new Span<byte>((void*)blob.Data, blob.Length).Clear();
            }

            return result;
        }
        finally
        {
            if (blob.Data != 0)
            {
                _ = LocalFree(blob.Data);
            }
        }
    }

    private static CryptographicException CreateCryptographicException(string message)
    {
        var error = Marshal.GetLastPInvokeError();
        return new CryptographicException(message, new Win32Exception(error));
    }

    [LibraryImport(
        "crypt32.dll",
        EntryPoint = "CryptProtectData",
        SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptProtectData(
        ref DataBlob dataIn,
        string? description,
        ref DataBlob optionalEntropy,
        nint reserved,
        nint prompt,
        uint flags,
        out DataBlob dataOut);

    [LibraryImport("crypt32.dll", EntryPoint = "CryptUnprotectData", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptUnprotectData(
        ref DataBlob dataIn,
        out nint description,
        ref DataBlob optionalEntropy,
        nint reserved,
        nint prompt,
        uint flags,
        out DataBlob dataOut);

    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint memory);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct DataBlob
    {
        internal DataBlob(int length, nint data)
        {
            Length = length;
            Data = data;
        }

        internal readonly int Length;
        internal readonly nint Data;
    }
}
