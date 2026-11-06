using System.Security.Cryptography;

namespace RecursiveAI.OneDocument.Encryption;

internal enum HashKind
{
    Sha1,
    Sha256,
    Sha384,
    Sha512,
    Md5,
}

/// <summary>
/// The hashes the Office encryption schemes are defined over. SHA-1 and MD5 are broken for new designs, but [MS-OFFCRYPTO] fixes them
/// for the key derivation and the password verifier of existing files (and for the files Excel still writes), so they are called here and
/// nowhere else. The analyzer rules about weak hashes are switched off for this file only.
/// </summary>
#pragma warning disable CA5350, CA5351 // Mandated by the file formats; see the summary above.
internal static class OfficeHash
{
    internal static int Length(HashKind k) => k switch
    {
        HashKind.Sha1 => 20,
        HashKind.Sha256 => 32,
        HashKind.Sha384 => 48,
        HashKind.Sha512 => 64,
        _ => 16,
    };

    internal static HashAlgorithmName Name(HashKind k) => k switch
    {
        HashKind.Sha1 => HashAlgorithmName.SHA1,
        HashKind.Sha256 => HashAlgorithmName.SHA256,
        HashKind.Sha384 => HashAlgorithmName.SHA384,
        HashKind.Sha512 => HashAlgorithmName.SHA512,
        _ => HashAlgorithmName.MD5,
    };

    internal static void HashInto(HashKind k, ReadOnlySpan<byte> source, Span<byte> destination)
    {
        switch (k)
        {
            case HashKind.Sha1:
                SHA1.HashData(source, destination);
                break;
            case HashKind.Sha256:
                SHA256.HashData(source, destination);
                break;
            case HashKind.Sha384:
                SHA384.HashData(source, destination);
                break;
            case HashKind.Sha512:
                SHA512.HashData(source, destination);
                break;
            default:
                try
                {
                    MD5.HashData(source, destination);
                }
                catch (CryptographicException ex)
                {
                    // Browser WebAssembly has no MD5: the runtime throws a CryptographicException there. The legacy RC4 schemes need it,
                    // so a file that uses one says what is missing instead of failing with a bare runtime exception.
                    throw new OfficeUnsupportedException("MD5 on this platform (the runtime has no MD5 implementation, as in browser WebAssembly)", default, ex);
                }

                break;
        }
    }

    /// <summary>Creates an HMAC over <paramref name="kind"/>; a platform without it raises <see cref="OfficeUnsupportedException"/>.</summary>
    internal static IncrementalHash CreateHmac(HashKind kind, ReadOnlySpan<byte> key)
    {
        try
        {
            return IncrementalHash.CreateHMAC(Name(kind), key);
        }
        catch (Exception ex) when (ex is CryptographicException or PlatformNotSupportedException)
        {
            throw new OfficeUnsupportedException("HMAC over " + Name(kind).Name + " on this platform", default, ex);
        }
    }

    internal static void Sha1(ReadOnlySpan<byte> source, Span<byte> destination) => SHA1.HashData(source, destination);

    internal static byte[] Compute(HashKind k, ReadOnlySpan<byte> source)
    {
        byte[] output = new byte[Length(k)];
        HashInto(k, source, output);
        return output;
    }

    internal static byte[] Hash(HashKind k, params byte[][] parts)
    {
        int total = 0;
        foreach (byte[] part in parts)
        {
            total += part.Length;
        }

        byte[] input = new byte[total];
        int at = 0;
        foreach (byte[] part in parts)
        {
            part.CopyTo(input, at);
            at += part.Length;
        }

        return Compute(k, input);
    }
}
#pragma warning restore CA5350, CA5351
