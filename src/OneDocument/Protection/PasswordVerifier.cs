using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using RecursiveAI.OneDocument.Encryption;

namespace RecursiveAI.OneDocument.Protection;

/// <summary>The hash algorithms a protection password can be written with.</summary>
public enum ProtectionHash
{
    /// <summary>SHA-512, the default Office writes.</summary>
    Sha512 = 0,

    /// <summary>SHA-384.</summary>
    Sha384 = 1,

    /// <summary>SHA-256.</summary>
    Sha256 = 2,

    /// <summary>SHA-1 (weak; for files that older Office versions must read).</summary>
    Sha1 = 3,

    /// <summary>MD5 (weak; for files that older Office versions must read).</summary>
    Md5 = 4,
}

/// <summary>The outcome of checking a password against stored protection data.</summary>
public enum PasswordCheck
{
    /// <summary>The password produces the stored hash.</summary>
    Match = 0,

    /// <summary>The password does not produce the stored hash.</summary>
    Mismatch = 1,

    /// <summary>The stored hash uses an algorithm this library cannot compute (MD2, MD4, RIPEMD, Whirlpool, or an extension algorithm); nothing can be said about the password.</summary>
    UnsupportedAlgorithm = 2,
}

/// <summary>
/// A protection password as the formats store it: the algorithm name, a salt, an iteration count and the hash. Used for
/// <c>w:documentProtection</c>, <c>w:writeProtection</c>, <c>sheetProtection</c>, <c>workbookProtection</c>, <c>fileSharing</c>,
/// <c>protectedRange</c> and <c>p:modifyVerifier</c>.
/// </summary>
/// <remarks>
/// <para><b>Protection is not encryption.</b> The hash only lets an application decide whether to stop enforcing restrictions; the content
/// is not hidden and anyone who edits the XML can remove the restriction. Use encryption (a password to open) for confidentiality.</para>
/// <para><b>Algorithm</b> (ECMA-376 Part 1 §17.15.1.29 <c>spinCount</c> and <c>saltValue</c>): the password is encoded as UTF-16LE
/// (a leading U+FEFF is dropped), the salt is prepended and hashed, and then <c>spinCount</c> times the previous result followed by the
/// 0-based iteration number as a 4-byte little-endian integer is hashed again. The last value is the hash.</para>
/// <para><b>Salt.</b> A new verifier takes its salt from a seed, never from the clock or a random generator, so the same inputs write the
/// same bytes. The default seed is empty, which makes the salt depend on the context only; pass a seed that differs per document to
/// give every document its own salt.</para>
/// </remarks>
public sealed class PasswordVerifier
{
    /// <summary>The default iteration count written: 100,000, what Office writes.</summary>
    public const int DefaultSpinCount = 100_000;

    private readonly byte[] _hash;
    private readonly byte[] _salt;

    /// <summary>Creates a verifier from stored values.</summary>
    /// <param name="algorithmName">The ECMA-376 algorithm name, for example <c>SHA-512</c>.</param>
    /// <param name="hash">The stored hash.</param>
    /// <param name="salt">The stored salt (empty for none).</param>
    /// <param name="spinCount">The stored iteration count.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="spinCount"/> is negative.</exception>
    public PasswordVerifier(string algorithmName, ReadOnlySpan<byte> hash, ReadOnlySpan<byte> salt, int spinCount)
    {
        ArgumentNullException.ThrowIfNull(algorithmName);
        ArgumentOutOfRangeException.ThrowIfNegative(spinCount);
        AlgorithmName = algorithmName;
        _hash = hash.ToArray();
        _salt = salt.ToArray();
        SpinCount = spinCount;
    }

    /// <summary>Gets the ECMA-376 algorithm name (<c>SHA-512</c>, <c>SHA-1</c>, …) as stored.</summary>
    public string AlgorithmName { get; }

    /// <summary>Gets a copy of the stored hash.</summary>
    public ReadOnlyMemory<byte> Hash => _hash;

    /// <summary>Gets a copy of the stored salt.</summary>
    public ReadOnlyMemory<byte> Salt => _salt;

    /// <summary>Gets the iteration count.</summary>
    public int SpinCount { get; }

    /// <summary>Gets the hash in base64, as the attributes hold it.</summary>
    public string HashBase64 => Convert.ToBase64String(_hash);

    /// <summary>Gets the salt in base64, as the attributes hold it.</summary>
    public string SaltBase64 => Convert.ToBase64String(_salt);

    /// <summary>Makes a verifier for a new password.</summary>
    /// <param name="password">The password; not empty.</param>
    /// <param name="algorithm">The hash algorithm; default SHA-512.</param>
    /// <param name="spinCount">The iteration count; default 100,000.</param>
    /// <param name="seed">Bytes the salt is derived from (see the type remarks); may be empty.</param>
    /// <param name="context">A string that separates the salts of different elements written from the same seed (for example the element name).</param>
    /// <returns>The verifier, with a 16-byte salt.</returns>
    /// <exception cref="ArgumentException">The password is empty.</exception>
    public static PasswordVerifier Create(string password, ProtectionHash algorithm = ProtectionHash.Sha512, int spinCount = DefaultSpinCount, ReadOnlySpan<byte> seed = default, string context = "")
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(context);
        if (password.Length == 0)
        {
            throw new ArgumentException("The password is empty.", nameof(password));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(spinCount);
        byte[] salt = DeriveSalt(seed, context);
        HashKind kind = ToKind(algorithm);
        byte[] hash = Compute(kind, Utf16(password), salt, spinCount);
        return new PasswordVerifier(NameOf(algorithm), hash, salt, spinCount);
    }

    /// <summary>Reads a verifier from attribute text.</summary>
    /// <param name="algorithmName">The algorithm name attribute.</param>
    /// <param name="hashBase64">The hash attribute.</param>
    /// <param name="saltBase64">The salt attribute; null or empty for none.</param>
    /// <param name="spinCount">The spin count attribute text; null or unreadable reads as 0.</param>
    /// <param name="verifier">The verifier, when the hash is valid base64.</param>
    /// <returns>True when a verifier was read.</returns>
    public static bool TryRead(string? algorithmName, string? hashBase64, string? saltBase64, string? spinCount, out PasswordVerifier? verifier)
    {
        verifier = null;
        if (string.IsNullOrEmpty(algorithmName) || string.IsNullOrEmpty(hashBase64))
        {
            return false;
        }

        byte[] hash;
        byte[] salt = [];
        try
        {
            hash = Convert.FromBase64String(hashBase64);
            if (!string.IsNullOrEmpty(saltBase64))
            {
                salt = Convert.FromBase64String(saltBase64);
            }
        }
        catch (FormatException)
        {
            return false;
        }

        int spin = 0;
        if (!string.IsNullOrEmpty(spinCount))
        {
            _ = int.TryParse(spinCount, NumberStyles.None, CultureInfo.InvariantCulture, out spin);
        }

        verifier = new PasswordVerifier(algorithmName, hash, salt, spin);
        return true;
    }

    /// <summary>Checks a password against this verifier.</summary>
    /// <param name="password">The password to test.</param>
    /// <param name="limits">The limits whose <see cref="OfficeLimits.MaxEncryptionSpinCount"/> bounds the work; null means the defaults.</param>
    /// <returns>The outcome; a stored algorithm that cannot be computed gives <see cref="PasswordCheck.UnsupportedAlgorithm"/>.</returns>
    /// <exception cref="OfficeLimitException">The stored spin count is larger than the limit.</exception>
    public PasswordCheck Check(string password, OfficeLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (!TryKind(AlgorithmName, out HashKind kind))
        {
            return PasswordCheck.UnsupportedAlgorithm;
        }

        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxEncryptionSpinCount), (limits ?? new OfficeLimits()).MaxEncryptionSpinCount, SpinCount);
        byte[] h = Compute(kind, Utf16(password), _salt, SpinCount);
        return CryptographicOperations.FixedTimeEquals(h, _hash) ? PasswordCheck.Match : PasswordCheck.Mismatch;
    }

    /// <summary>Checks a password against a hash made the way Word 2007 wrote it: the 32-bit pre-2007 key of the password, byte-reversed, is salted and hashed.</summary>
    /// <param name="password">The password to test.</param>
    /// <param name="cryptAlgorithmSid">The <c>cryptAlgorithmSid</c> attribute: the CryptoAPI algorithm id (1 MD2, 2 MD4, 3 MD5, 4 SHA-1, 12 SHA-256, 13 SHA-384, 14 SHA-512).</param>
    /// <param name="salt">The salt attribute bytes (empty for none).</param>
    /// <param name="hash">The hash attribute bytes.</param>
    /// <param name="spinCount">The <c>cryptSpinCount</c> attribute (0 for none).</param>
    /// <param name="limits">Limits bounding the work; null means the defaults.</param>
    /// <returns>The outcome.</returns>
    /// <remarks>
    /// ECMA-376 Part 4 §14.8.2.3 (<c>documentProtection</c> additional attributes): the password is first reduced to the key of the
    /// pre-2007 word processors ([MS-OFFCRYPTO] §2.3.7.4), the key's bytes are reversed, and that value is hashed as the attributes say.
    /// </remarks>
    /// <exception cref="OfficeLimitException">The spin count is larger than the limit.</exception>
    public static PasswordCheck CheckLegacyWord(string password, int cryptAlgorithmSid, ReadOnlySpan<byte> salt, ReadOnlySpan<byte> hash, int spinCount, OfficeLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(password);
        HashKind kind;
        switch (cryptAlgorithmSid)
        {
            case 3:
                kind = HashKind.Md5;
                break;
            case 4:
                kind = HashKind.Sha1;
                break;
            case 12:
                kind = HashKind.Sha256;
                break;
            case 13:
                kind = HashKind.Sha384;
                break;
            case 14:
                kind = HashKind.Sha512;
                break;
            default:
                return PasswordCheck.UnsupportedAlgorithm;
        }

        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxEncryptionSpinCount), (limits ?? new OfficeLimits()).MaxEncryptionSpinCount, spinCount);
        if (password.Length == 0)
        {
            return PasswordCheck.Mismatch;
        }

        byte[] h = Compute(kind, WordKeyBytes(password), salt, spinCount);
        return CryptographicOperations.FixedTimeEquals(h, hash) ? PasswordCheck.Match : PasswordCheck.Mismatch;
    }

    /// <summary>The legacy Word key of a password (ECMA-376 Part 4 §14.8.2.3), the value Word 2007 reversed and hashed.</summary>
    /// <param name="password">The password; not empty.</param>
    /// <returns>The 32-bit key (for the password <c>Example</c>: 0x64CEED7E).</returns>
    /// <exception cref="ArgumentException">The password is empty.</exception>
    public static uint WordKey(string password)
    {
        _ = XorObfuscation.CreateMethod2(password, out uint key);
        return key;
    }

    /// <summary>The 16-bit hash Excel stores for sheet, workbook-structure and write-reservation passwords before 2013 ([MS-OFFCRYPTO] §2.3.7.1).</summary>
    /// <param name="password">The password; not empty.</param>
    /// <returns>The 16-bit verifier.</returns>
    /// <exception cref="ArgumentException">The password is empty.</exception>
    public static ushort ExcelHash(string password)
    {
        _ = XorObfuscation.CreateMethod1(password, out _, out ushort verifier);
        return verifier;
    }

    /// <summary>Checks a password against the 16-bit hash of a <c>password</c>, <c>workbookPassword</c> or <c>reservationPassword</c> attribute.</summary>
    /// <param name="password">The password to test.</param>
    /// <param name="storedHex">The attribute text: up to four hexadecimal digits.</param>
    /// <returns>The outcome; unreadable stored text is a mismatch.</returns>
    public static PasswordCheck CheckExcelHash(string password, string storedHex)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(storedHex);
        if (!ushort.TryParse(storedHex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ushort stored) || password.Length == 0)
        {
            return PasswordCheck.Mismatch;
        }

        return ExcelHash(password) == stored ? PasswordCheck.Match : PasswordCheck.Mismatch;
    }

    /// <summary>Formats a 16-bit hash as the attribute text: four upper-case hexadecimal digits.</summary>
    /// <param name="hash">The hash.</param>
    /// <returns>The text.</returns>
    public static string FormatExcelHash(ushort hash) => hash.ToString("X4", CultureInfo.InvariantCulture);

    private static string NameOf(ProtectionHash a) => a switch
    {
        ProtectionHash.Sha384 => "SHA-384",
        ProtectionHash.Sha256 => "SHA-256",
        ProtectionHash.Sha1 => "SHA-1",
        ProtectionHash.Md5 => "MD5",
        _ => "SHA-512",
    };

    private static HashKind ToKind(ProtectionHash a) => a switch
    {
        ProtectionHash.Sha384 => HashKind.Sha384,
        ProtectionHash.Sha256 => HashKind.Sha256,
        ProtectionHash.Sha1 => HashKind.Sha1,
        ProtectionHash.Md5 => HashKind.Md5,
        _ => HashKind.Sha512,
    };

    private static bool TryKind(string name, out HashKind kind)
    {
        switch (name.ToUpperInvariant())
        {
            case "SHA-512":
            case "SHA512":
                kind = HashKind.Sha512;
                return true;
            case "SHA-384":
            case "SHA384":
                kind = HashKind.Sha384;
                return true;
            case "SHA-256":
            case "SHA256":
                kind = HashKind.Sha256;
                return true;
            case "SHA-1":
            case "SHA1":
                kind = HashKind.Sha1;
                return true;
            case "MD5":
                kind = HashKind.Md5;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    // ECMA-376 Part 1 §17.15.1.29: the password is hashed starting from a UTF-16LE string; a leading U+FEFF is removed first.
    private static byte[] Utf16(string password)
    {
        string p = password.Length > 0 && password[0] == '\uFEFF' ? password[1..] : password;
        return Encoding.Unicode.GetBytes(p);
    }

    private static byte[] WordKeyBytes(string password)
    {
        uint key = WordKey(password);
        // ECMA-376 Part 4 §14.8.2.3: the byte order of the key is reversed (0x64CEED7E becomes 7EEDCE64), which is its little-endian form.
        return [(byte)key, (byte)(key >> 8), (byte)(key >> 16), (byte)(key >> 24)];
    }

    // H0 = H(salt + input); Hn = H(Hn-1 + n as a 4-byte little-endian integer), n counting from 0 (ECMA-376 Part 1 §17.15.1.29, spinCount).
    private static byte[] Compute(HashKind kind, byte[] input, ReadOnlySpan<byte> salt, int spinCount)
    {
        int len = OfficeHash.Length(kind);
        byte[] first = new byte[salt.Length + input.Length];
        salt.CopyTo(first);
        input.CopyTo(first, salt.Length);
        byte[] h = OfficeHash.Compute(kind, first);
        byte[] buffer = new byte[len + 4];
        for (int i = 0; i < spinCount; i++)
        {
            h.CopyTo(buffer, 0);
            buffer[len] = (byte)i;
            buffer[len + 1] = (byte)(i >> 8);
            buffer[len + 2] = (byte)(i >> 16);
            buffer[len + 3] = (byte)(i >> 24);
            OfficeHash.HashInto(kind, buffer, h);
        }

        return h;
    }

    private static byte[] DeriveSalt(ReadOnlySpan<byte> seed, string context)
    {
        byte[] ctx = Encoding.UTF8.GetBytes(context);
        byte[] label = Encoding.ASCII.GetBytes("OneDocument.salt\0");
        byte[] input = new byte[label.Length + seed.Length + ctx.Length];
        label.CopyTo(input, 0);
        seed.CopyTo(input.AsSpan(label.Length));
        ctx.CopyTo(input, label.Length + seed.Length);
        return SHA256.HashData(input)[..16];
    }
}
