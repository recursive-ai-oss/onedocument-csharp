using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Cfb;

namespace RecursiveAI.OneDocument.Encryption;

/// <summary>Which ECMA-376 package encryption an <c>EncryptionInfo</c> stream describes.</summary>
public enum PackageEncryptionScheme
{
    /// <summary>Standard encryption: a binary header, AES in ECB mode, SHA-1 ([MS-OFFCRYPTO] §2.3.4.5), versions 2.2, 3.2 and 4.2.</summary>
    Standard = 0,

    /// <summary>Agile encryption: an XML descriptor, AES in CBC (or CFB) mode, SHA-1 or SHA-2, an HMAC over the data ([MS-OFFCRYPTO] §2.3.4.10), version 4.4.</summary>
    Agile = 1,
}

/// <summary>What an <c>EncryptionInfo</c> stream says about how a package is encrypted, without trying a password.</summary>
/// <param name="Scheme">Standard or agile.</param>
/// <param name="VersionMajor">The major version of the <c>EncryptionVersionInfo</c>.</param>
/// <param name="VersionMinor">The minor version.</param>
/// <param name="KeyBits">The AES key length in bits.</param>
/// <param name="HashAlgorithm">The hash named by the descriptor, such as <c>SHA-1</c> or <c>SHA512</c>.</param>
/// <param name="SpinCount">The number of password hash iterations (50,000 for standard encryption).</param>
public sealed record PackageEncryptionInfo(PackageEncryptionScheme Scheme, int VersionMajor, int VersionMinor, int KeyBits, string HashAlgorithm, long SpinCount);

/// <summary>
/// Decryption of ECMA-376 encrypted packages: the compound file with an <c>EncryptionInfo</c> and an <c>EncryptedPackage</c> stream that
/// Office writes for a password-protected DOCX, XLSX, PPTX or XLSB. It uses the .NET AES, hash and HMAC primitives only. Writing is
/// not here.
/// </summary>
/// <remarks>
/// A file that Office protects only against editing is encrypted with a fixed password and opens without one; when no password
/// is given, or the one given does not match, that default (<see cref="DefaultPassword"/>) is tried before the file is refused.
/// </remarks>
public static class PackageEncryption
{
    /// <summary>
    /// The password Excel uses for a workbook that is protected against editing only ([MS-OFFCRYPTO] §2.4.1, the note on write protection):
    /// such a file opens with no password at all.
    /// </summary>
    public const string DefaultPassword = "VelvetSweatshop";

    private const string InfoPath = "/EncryptionInfo";
    private const string PackagePath = "/EncryptedPackage";
    private const int Segment = 4096;
    private const int MaxHeaderSize = 64 * 1024;
    private const int StandardSpin = 50_000;

    // [MS-OFFCRYPTO] §2.3.4.13 and §2.3.4.14: the block keys that separate the uses of one derived key.
    private static readonly byte[] BlockVerifierInput = [0xFE, 0xA7, 0xD2, 0x76, 0x3B, 0x4B, 0x9E, 0x79];
    private static readonly byte[] BlockVerifierValue = [0xD7, 0xAA, 0x0F, 0x6D, 0x30, 0x61, 0x34, 0x4E];
    private static readonly byte[] BlockKeyValue = [0x14, 0x6E, 0x0B, 0xE7, 0xAB, 0xAC, 0xD0, 0xD6];
    private static readonly byte[] BlockHmacKey = [0x5F, 0xB2, 0xAD, 0x01, 0x0C, 0xB9, 0xE1, 0xF6];
    private static readonly byte[] BlockHmacValue = [0xA0, 0x67, 0x7F, 0x02, 0xB2, 0x2C, 0x84, 0x33];

    /// <summary>Describes the encryption an <c>EncryptionInfo</c> stream declares. No password is needed.</summary>
    /// <param name="encryptionInfo">The stream's bytes.</param>
    /// <param name="context">Limits and defect sink; null for the defaults.</param>
    /// <returns>The description.</returns>
    /// <exception cref="OfficeFormatException">The stream is damaged (<c>encryption.malformed-header</c>).</exception>
    /// <exception cref="OfficeUnsupportedException">The version, cipher, chaining or hash is not handled; the exception names it.</exception>
    /// <exception cref="OfficeLimitException">The spin count is above <see cref="OfficeLimits.MaxEncryptionSpinCount"/>.</exception>
    public static PackageEncryptionInfo Describe(ReadOnlySpan<byte> encryptionInfo, OpenContext? context = null)
    {
        context ??= new OpenContext();
        Parsed p = ParseInfo(encryptionInfo, context);
        return new PackageEncryptionInfo(p.Agile is null ? PackageEncryptionScheme.Standard : PackageEncryptionScheme.Agile, p.Major, p.Minor, p.KeyBits, p.HashName, p.Spin);
    }

    /// <summary>
    /// Decrypts an <c>EncryptedPackage</c> stream to the package it holds (the bytes of a ZIP file).
    /// </summary>
    /// <param name="encryptionInfo">The <c>EncryptionInfo</c> stream.</param>
    /// <param name="encryptedPackage">The <c>EncryptedPackage</c> stream.</param>
    /// <param name="context">Limits, defect sink and the password (<see cref="OpenContext.Password"/>).</param>
    /// <returns>The package bytes.</returns>
    /// <exception cref="OfficeEncryptedException">No password was given and the default does not open the file (<see cref="EncryptionProblem.PasswordRequired"/>), or a password was given and neither it nor the default opens it (<see cref="EncryptionProblem.PasswordIncorrect"/>).</exception>
    /// <exception cref="OfficeFormatException">A stream is damaged (<c>encryption.malformed-header</c>, <c>encryption.malformed-data</c>).</exception>
    /// <exception cref="OfficeUnsupportedException">The scheme uses something not handled; the exception names it.</exception>
    /// <exception cref="OfficeLimitException">A limit was exceeded.</exception>
    /// <remarks>
    /// Agile encryption carries an HMAC over the encrypted data. When it does not match, the package is still returned and the defect
    /// <c>encryption.integrity-mismatch</c> is reported, because a damaged file is more useful read than refused; the verifier check
    /// already proved the password.
    /// </remarks>
    public static byte[] Decrypt(ReadOnlySpan<byte> encryptionInfo, ReadOnlySpan<byte> encryptedPackage, OpenContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            return DecryptCore(encryptionInfo, encryptedPackage, context);
        }
        catch (PlatformNotSupportedException ex)
        {
            // Browser WebAssembly has no AES in the runtime; say so rather than leak the runtime's exception.
            throw new OfficeUnsupportedException("AES decryption on this platform (the runtime has no AES implementation, as in browser WebAssembly)", DefectLocation.InRecord(InfoPath, 0), ex);
        }
    }

    private static byte[] DecryptCore(ReadOnlySpan<byte> encryptionInfo, ReadOnlySpan<byte> encryptedPackage, OpenContext context)
    {
        Parsed p = ParseInfo(encryptionInfo, context);
        DefectLocation where = DefectLocation.InRecord(PackagePath, 0);
        if (encryptedPackage.Length < 8)
        {
            throw new OfficeFormatException(DefectKind.Encryption, "encryption.malformed-data", "The EncryptedPackage stream is shorter than its size field.", where);
        }

        ulong size = BinaryPrimitives.ReadUInt64LittleEndian(encryptedPackage);
        ReadOnlySpan<byte> data = encryptedPackage[8..];
        if (size > (ulong)data.Length)
        {
            throw new OfficeFormatException(DefectKind.Encryption, "encryption.malformed-data", $"The package claims {size} bytes but {data.Length} are stored.", where);
        }

        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxPartSize), context.Limits.MaxPartSize, (long)size, where);

        string?[] candidates = context.Password is null ? [DefaultPassword] : [context.Password, DefaultPassword];
        foreach (string? candidate in candidates)
        {
            byte[]? plain = p.Agile is not null
                ? DecryptAgile(p.Agile, candidate!, data, encryptedPackage, (int)size, context)
                : DecryptStandard(p, candidate!, data, (int)size);
            if (plain is not null)
            {
                return plain;
            }
        }

        throw new OfficeEncryptedException(
            context.Password is null ? EncryptionProblem.PasswordRequired : EncryptionProblem.PasswordIncorrect,
            DefectLocation.InRecord(InfoPath, 0));
    }

    /// <summary>
    /// Reads an encrypted package from its compound file and decrypts it: finds the two streams, then calls
    /// <see cref="Decrypt(ReadOnlySpan{byte}, ReadOnlySpan{byte}, OpenContext)"/>.
    /// </summary>
    /// <param name="source">The compound file; it is read, not disposed.</param>
    /// <param name="context">Limits, defect sink and the password.</param>
    /// <returns>The package bytes, ready to be sniffed and opened as the format they are.</returns>
    /// <exception cref="OfficeFormatException">The compound file lacks one of the streams, or they are damaged.</exception>
    /// <exception cref="OfficeEncryptedException">The password is missing or wrong.</exception>
    public static byte[] DecryptCompoundFile(IByteSource source, OpenContext context)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(context);
        using CfbPackage cfb = CfbPackage.Open(new Office.BorrowedByteSource(source), context);
        IPackagePart? info = cfb.FindPart(InfoPath);
        IPackagePart? package = cfb.FindPart(PackagePath);
        if (info is null || package is null)
        {
            throw new OfficeFormatException(DefectKind.Encryption, "encryption.malformed-header", "The compound file lacks an EncryptionInfo or an EncryptedPackage stream.", DefectLocation.InRecord(InfoPath));
        }

        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxPartSize), context.Limits.MaxPartSize, package.Length, DefectLocation.InRecord(PackagePath));
        return Decrypt(info.GetBytes().Span, package.GetBytes().Span, context);
    }

    // ---- parsing ----

    private sealed class Agile
    {
        public string HashName = string.Empty;
        public HashKind DataHash;
        public byte[] KeyDataSalt = [];
        public int BlockSize;
        public int KeyBits;
        public string Chaining = string.Empty;

        public byte[]? HmacKey;
        public byte[]? HmacValue;

        // The password key encryptor.
        public HashKind KeyHash;
        public byte[] Salt = [];
        public int KeyEncBlockSize;
        public int KeyEncKeyBits;
        public string KeyEncChaining = string.Empty;
        public int HashSize;
        public int Spin;
        public byte[] VerifierInput = [];
        public byte[] VerifierValue = [];
        public byte[] KeyValue = [];
    }

    private sealed class Parsed
    {
        public int Major;
        public int Minor;
        public int KeyBits;
        public string HashName = "SHA-1";
        public long Spin;
        public Agile? Agile;
        public byte[] Salt = [];
        public byte[] EncryptedVerifier = [];
        public byte[] EncryptedVerifierHash = [];
        public int VerifierHashSize;
    }

    private static Parsed ParseInfo(ReadOnlySpan<byte> info, OpenContext context)
    {
        DefectLocation at = DefectLocation.InRecord(InfoPath, 0);
        if (info.Length < 8)
        {
            throw Malformed("The EncryptionInfo stream is shorter than its version fields.", at);
        }

        int major = BinaryPrimitives.ReadUInt16LittleEndian(info);
        int minor = BinaryPrimitives.ReadUInt16LittleEndian(info[2..]);
        var p = new Parsed { Major = major, Minor = minor };

        // [MS-OFFCRYPTO] §2.3.4.10: agile is 4.4 (some writers use 3.4); §2.3.4.6: 3.3 and 4.3 name an extension module.
        if (minor == 4 && major is 3 or 4)
        {
            p.Agile = ParseAgile(info, context, at);
            p.KeyBits = p.Agile.KeyBits;
            p.HashName = p.Agile.HashName;
            p.Spin = p.Agile.Spin;
            return p;
        }

        if (minor == 3 && major is 3 or 4)
        {
            throw new OfficeUnsupportedException("extensible (third-party) encryption", at);
        }

        if (minor == 2 && major is 2 or 3 or 4)
        {
            ParseStandard(info, p, at);
            return p;
        }

        throw new OfficeUnsupportedException($"encryption version {major}.{minor}", at);
    }

    // [MS-OFFCRYPTO] §2.3.4.5.
    private static void ParseStandard(ReadOnlySpan<byte> info, Parsed p, DefectLocation at)
    {
        if (info.Length < 12)
        {
            throw Malformed("The standard EncryptionInfo is cut short before its header size.", at);
        }

        uint headerSize = BinaryPrimitives.ReadUInt32LittleEndian(info[8..]);
        if (headerSize is < 32 or > MaxHeaderSize || 12 + (long)headerSize > info.Length)
        {
            throw Malformed($"The standard EncryptionInfo header claims {headerSize} bytes; the stream holds {info.Length - 12}.", at);
        }

        ReadOnlySpan<byte> h = info.Slice(12, (int)headerSize);
        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(h);
        uint algId = BinaryPrimitives.ReadUInt32LittleEndian(h[8..]);
        uint hashId = BinaryPrimitives.ReadUInt32LittleEndian(h[12..]);
        uint keySize = BinaryPrimitives.ReadUInt32LittleEndian(h[16..]);
        if ((flags & 0x10) != 0)
        {
            throw new OfficeUnsupportedException("extensible (third-party) encryption", at);
        }

        if ((flags & 0x20) == 0 || (flags & 0x04) == 0)
        {
            throw new OfficeUnsupportedException("standard encryption without AES (RC4 in an ECMA-376 package)", at);
        }

        if (hashId is not (0 or 0x8004))
        {
            throw new OfficeUnsupportedException($"standard encryption hash 0x{hashId:X}", at);
        }

        int bits = algId switch
        {
            0x660E => 128,
            0x660F => 192,
            0x6610 => 256,
            0 => 128,
            _ => throw new OfficeUnsupportedException($"standard encryption cipher 0x{algId:X}", at),
        };
        if (keySize != 0 && keySize != bits)
        {
            throw Malformed($"The AES key size of {keySize} bits does not match the cipher 0x{algId:X}.", at);
        }

        ReadOnlySpan<byte> v = info[(12 + (int)headerSize)..];
        if (v.Length < 4 + 16 + 16 + 4 + 32)
        {
            throw Malformed("The standard EncryptionInfo verifier is cut short.", at);
        }

        if (BinaryPrimitives.ReadUInt32LittleEndian(v) != 16)
        {
            throw Malformed("The standard EncryptionInfo salt is not 16 bytes.", at);
        }

        p.KeyBits = bits;
        p.HashName = "SHA-1";
        p.Spin = StandardSpin;
        p.Salt = v.Slice(4, 16).ToArray();
        p.EncryptedVerifier = v.Slice(20, 16).ToArray();
        p.VerifierHashSize = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(v[36..]), 32u);
        p.EncryptedVerifierHash = v.Slice(40, 32).ToArray();
    }

    private static Agile ParseAgile(ReadOnlySpan<byte> info, OpenContext context, DefectLocation at)
    {
        // The 8 bytes before the XML are the version and a reserved word ([MS-OFFCRYPTO] §2.3.4.10).
        MarkupDocument doc;
        try
        {
            doc = MarkupDocument.Parse(info[8..].ToArray(), context, InfoPath);
        }
        catch (OfficeFormatException ex)
        {
            throw new OfficeFormatException(DefectKind.Encryption, "encryption.malformed-header", "The agile EncryptionInfo descriptor is not well-formed XML: " + ex.Message, at, ex);
        }

        MarkupNode root = doc.Root;
        if (root.IsNull || root.LocalNameString != "encryption")
        {
            throw Malformed("The agile EncryptionInfo descriptor does not start with an encryption element.", at);
        }

        MarkupNode keyData = default;
        MarkupNode integrity = default;
        MarkupNode encryptedKey = default;
        foreach (MarkupNode child in root.ChildElements)
        {
            switch (child.LocalNameString)
            {
                case "keyData":
                    keyData = child;
                    break;
                case "dataIntegrity":
                    integrity = child;
                    break;
                case "keyEncryptors":
                    foreach (MarkupNode ke in child.ChildElements)
                    {
                        if (ke.LocalNameString == "keyEncryptor" && (ke.GetAttribute("uri") ?? string.Empty).EndsWith("/keyEncryptor/password", StringComparison.Ordinal))
                        {
                            foreach (MarkupNode k in ke.ChildElements)
                            {
                                if (k.LocalNameString == "encryptedKey")
                                {
                                    encryptedKey = k;
                                }
                            }
                        }
                    }

                    break;
            }
        }

        if (keyData.IsNull)
        {
            throw Malformed("The agile EncryptionInfo has no keyData element.", at);
        }

        if (encryptedKey.IsNull)
        {
            throw new OfficeUnsupportedException("agile encryption without a password key encryptor (certificate-only)", at);
        }

        var a = new Agile();
        string cipher = Attr(keyData, "cipherAlgorithm", at);
        a.Chaining = Attr(keyData, "cipherChaining", at);
        a.HashName = Attr(keyData, "hashAlgorithm", at);
        a.BlockSize = Int(keyData, "blockSize", 2, 4096, at);
        a.KeyBits = Int(keyData, "keyBits", 8, 65536, at);
        int saltSize = Int(keyData, "saltSize", 1, 65536, at);
        a.KeyDataSalt = Bytes(keyData, "saltValue", at);
        if (a.KeyDataSalt.Length != saltSize)
        {
            throw Malformed("keyData saltValue does not have saltSize bytes.", at);
        }

        a.DataHash = ResolveHash(a.HashName, at);
        CheckCipher(cipher, a.Chaining, a.KeyBits, a.BlockSize, at);

        string keCipher = Attr(encryptedKey, "cipherAlgorithm", at);
        a.KeyEncChaining = Attr(encryptedKey, "cipherChaining", at);
        a.KeyEncBlockSize = Int(encryptedKey, "blockSize", 2, 4096, at);
        a.KeyEncKeyBits = Int(encryptedKey, "keyBits", 8, 65536, at);
        a.HashSize = Int(encryptedKey, "hashSize", 1, 65536, at);
        int keSaltSize = Int(encryptedKey, "saltSize", 1, 65536, at);
        a.Salt = Bytes(encryptedKey, "saltValue", at);
        if (a.Salt.Length != keSaltSize)
        {
            throw Malformed("encryptedKey saltValue does not have saltSize bytes.", at);
        }

        a.KeyHash = ResolveHash(Attr(encryptedKey, "hashAlgorithm", at), at);
        if (HashLength(a.KeyHash) != a.HashSize)
        {
            throw Malformed("encryptedKey hashSize does not match its hash algorithm.", at);
        }

        CheckCipher(keCipher, a.KeyEncChaining, a.KeyEncKeyBits, a.KeyEncBlockSize, at);

        // [MS-OFFCRYPTO] §2.3.4.10 (ST_SpinCount): at most 10,000,000; the limit object can lower that.
        long spin = Int(encryptedKey, "spinCount", 0, int.MaxValue, at);
        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxEncryptionSpinCount), context.Limits.MaxEncryptionSpinCount, spin, at);
        a.Spin = (int)spin;
        a.VerifierInput = Bytes(encryptedKey, "encryptedVerifierHashInput", at);
        a.VerifierValue = Bytes(encryptedKey, "encryptedVerifierHashValue", at);
        a.KeyValue = Bytes(encryptedKey, "encryptedKeyValue", at);
        if (!integrity.IsNull)
        {
            a.HmacKey = Bytes(integrity, "encryptedHmacKey", at);
            a.HmacValue = Bytes(integrity, "encryptedHmacValue", at);
        }

        return a;
    }

    private static void CheckCipher(string cipher, string chaining, int keyBits, int blockSize, DefectLocation at)
    {
        if (!string.Equals(cipher, "AES", StringComparison.Ordinal))
        {
            throw new OfficeUnsupportedException($"agile encryption cipher {cipher}", at);
        }

        if (chaining is not ("ChainingModeCBC" or "ChainingModeCFB"))
        {
            throw new OfficeUnsupportedException($"agile encryption chaining mode {chaining}", at);
        }

        if (keyBits is not (128 or 192 or 256))
        {
            throw new OfficeUnsupportedException($"AES key length of {keyBits} bits", at);
        }

        if (blockSize != 16)
        {
            throw Malformed($"AES blockSize is {blockSize}; it is 16.", at);
        }
    }

    private static string Attr(MarkupNode node, string name, DefectLocation at) =>
        node.GetAttribute(name) ?? throw Malformed($"The {node.LocalNameString} element has no {name} attribute.", at);

    private static int Int(MarkupNode node, string name, int min, int max, DefectLocation at)
    {
        string text = Attr(node, name, at);
        if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long v) || v < min || v > max)
        {
            // A spin count over int range is still a number the limit check should name, not a parse failure.
            if (name == "spinCount" && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long big) && big > max)
            {
                throw new OfficeLimitException(nameof(OfficeLimits.MaxEncryptionSpinCount), int.MaxValue, big, at);
            }

            throw Malformed($"{name}=\"{text}\" is not a number from {min} to {max}.", at);
        }

        return (int)v;
    }

    private static byte[] Bytes(MarkupNode node, string name, DefectLocation at)
    {
        string text = Attr(node, name, at);
        try
        {
            return Convert.FromBase64String(text);
        }
        catch (FormatException ex)
        {
            throw new OfficeFormatException(DefectKind.Encryption, "encryption.malformed-header", $"{name} is not base64.", at, ex);
        }
    }

    private static OfficeFormatException Malformed(string message, DefectLocation at) =>
        new(DefectKind.Encryption, "encryption.malformed-header", message, at);

    // ---- hashes ----

    // [MS-OFFCRYPTO] §2.3.4.10: the defined names are SHA-1, SHA256, SHA384, SHA512, MD5 (writers also spell SHA-1 as SHA1);
    // MD2, MD4, RIPEMD and WHIRLPOOL are defined but not in the .NET libraries.
    private static HashKind ResolveHash(string name, DefectLocation at)
    {
        switch (name.Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant())
        {
            case "SHA1":
                return HashKind.Sha1;
            case "SHA256":
                return HashKind.Sha256;
            case "SHA384":
                return HashKind.Sha384;
            case "SHA512":
                return HashKind.Sha512;
            case "MD5":
                return HashKind.Md5;
            default:
                throw new OfficeUnsupportedException($"agile encryption hash {name}", at);
        }
    }

    private static int HashLength(HashKind k) => OfficeHash.Length(k);

    private static byte[] Hash(HashKind k, params byte[][] parts) => OfficeHash.Hash(k, parts);

    private static void HashInto(HashKind k, ReadOnlySpan<byte> source, Span<byte> destination) => OfficeHash.HashInto(k, source, destination);

    // [MS-OFFCRYPTO] §2.3.4.11 and §2.3.4.12: a derived key or IV is cut, or padded with 0x36, to the length needed.
    private static byte[] Resize(byte[] value, int length)
    {
        byte[] r = new byte[length];
        Array.Fill(r, (byte)0x36);
        Array.Copy(value, r, Math.Min(value.Length, length));
        return r;
    }

    // ---- agile ----

    // [MS-OFFCRYPTO] §2.3.4.11: H0 = H(salt + password); Hn = H(iterator + Hn-1) for spinCount iterations; key = H(Hn + blockKey).
    private static byte[] SpinHash(HashKind k, byte[] salt, byte[] password, int spin)
    {
        int len = HashLength(k);
        byte[] h = Hash(k, salt, password);
        byte[] buffer = new byte[4 + len];
        byte[] next = new byte[len];
        for (int i = 0; i < spin; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, (uint)i);
            h.CopyTo(buffer, 4);
            HashInto(k, buffer, next);
            (h, next) = (next, h);
        }

        return h;
    }

    private static byte[] AgileKey(HashKind k, byte[] spun, byte[] blockKey, int keyBytes) => Resize(Hash(k, spun, blockKey), keyBytes);

    private static byte[]? DecryptAgile(Agile a, string password, ReadOnlySpan<byte> data, ReadOnlySpan<byte> wholeStream, int size, OpenContext context)
    {
        byte[] pw = EncryptionText.Utf16(password);
        int keyBytes = a.KeyEncKeyBits / 8;
        byte[] salt16 = Resize(a.Salt, a.KeyEncBlockSize);
        byte[] spun = SpinHash(a.KeyHash, a.Salt, pw, a.Spin);
        byte[] kIn = AgileKey(a.KeyHash, spun, BlockVerifierInput, keyBytes);
        byte[] verifierInput = AesDecrypt(kIn, salt16, a.VerifierInput, a.KeyEncChaining);
        byte[] kVal = AgileKey(a.KeyHash, spun, BlockVerifierValue, keyBytes);
        byte[] verifierValue = AesDecrypt(kVal, salt16, a.VerifierValue, a.KeyEncChaining);

        // The random input is saltSize bytes; its hash is stored encrypted, zero-padded to the block size.
        byte[] input = verifierInput.AsSpan(0, Math.Min(a.Salt.Length, verifierInput.Length)).ToArray();
        byte[] expected = Hash(a.KeyHash, input);
        if (verifierValue.Length < expected.Length || !CryptographicOperations.FixedTimeEquals(expected, verifierValue.AsSpan(0, expected.Length)))
        {
            return null;
        }

        byte[] kKey = AgileKey(a.KeyHash, spun, BlockKeyValue, keyBytes);
        byte[] intermediate = AesDecrypt(kKey, salt16, a.KeyValue, a.KeyEncChaining);
        int dataKeyBytes = a.KeyBits / 8;
        if (intermediate.Length < dataKeyBytes)
        {
            throw new OfficeFormatException(DefectKind.Encryption, "encryption.malformed-header", "The encrypted key value is shorter than the data key.", DefectLocation.InRecord(InfoPath, 0));
        }

        byte[] dataKey = intermediate.AsSpan(0, dataKeyBytes).ToArray();
        byte[] output = new byte[size];
        using Aes aes = Aes.Create();
        aes.Key = dataKey;
        byte[] segment = new byte[Segment];
        bool cfb = a.Chaining == "ChainingModeCFB";
        byte[] block = new byte[4];
        for (int offset = 0, index = 0; offset < size; offset += Segment, index++)
        {
            int want = Math.Min(Segment, size - offset);
            int cipherLen = (want + 15) & ~15;
            if (offset + cipherLen > data.Length)
            {
                cipherLen = (data.Length - offset) & ~15;
                if (cipherLen < want)
                {
                    throw new OfficeFormatException(DefectKind.Encryption, "encryption.malformed-data", "The encrypted data ends inside a block.", DefectLocation.InRecord(PackagePath, 8 + offset));
                }
            }

            BinaryPrimitives.WriteUInt32LittleEndian(block, (uint)index);
            byte[] iv = Resize(Hash(a.DataHash, a.KeyDataSalt, block), a.BlockSize);
            ReadOnlySpan<byte> src = data.Slice(offset, cipherLen);
            Span<byte> dst = segment.AsSpan(0, cipherLen);
            try
            {
                if (cfb)
                {
                    aes.DecryptCfb(src, iv, dst, PaddingMode.None, 8);
                }
                else
                {
                    aes.DecryptCbc(src, iv, dst, PaddingMode.None);
                }
            }
            catch (CryptographicException ex)
            {
                throw new OfficeFormatException(DefectKind.Encryption, "encryption.malformed-data", "The encrypted data cannot be decrypted: " + ex.Message, DefectLocation.InRecord(PackagePath, 8 + offset), ex);
            }

            segment.AsSpan(0, want).CopyTo(output.AsSpan(offset));
        }

        CheckIntegrity(a, intermediate.AsSpan(0, dataKeyBytes).ToArray(), wholeStream, context);
        return output;
    }

    // [MS-OFFCRYPTO] §2.3.4.14: the HMAC key and value are encrypted under the data key with IVs derived from the keyData salt.
    private static void CheckIntegrity(Agile a, byte[] dataKey, ReadOnlySpan<byte> wholeStream, OpenContext context)
    {
        if (a.HmacKey is null || a.HmacValue is null)
        {
            return;
        }

        try
        {
            byte[] keyIv = Resize(Hash(a.DataHash, a.KeyDataSalt, BlockHmacKey), a.BlockSize);
            byte[] valueIv = Resize(Hash(a.DataHash, a.KeyDataSalt, BlockHmacValue), a.BlockSize);
            int hashLen = HashLength(a.DataHash);
            byte[] hmacKey = AesDecrypt(dataKey, keyIv, a.HmacKey, a.Chaining);
            byte[] hmacValue = AesDecrypt(dataKey, valueIv, a.HmacValue, a.Chaining);
            using IncrementalHash hmac = OfficeHash.CreateHmac(a.DataHash, hmacKey.AsSpan(0, Math.Min(hashLen, hmacKey.Length)));
            hmac.AppendData(wholeStream);
            byte[] actual = hmac.GetHashAndReset();
            if (hmacValue.Length < hashLen || !CryptographicOperations.FixedTimeEquals(actual, hmacValue.AsSpan(0, hashLen)))
            {
                ReportMismatch(context);
            }
        }
        catch (OfficeFormatException)
        {
            ReportMismatch(context);
        }
    }

    private static void ReportMismatch(OpenContext context) =>
        context.Defects.Report(new Defect(DefectKind.Encryption, "encryption.integrity-mismatch", DefectSeverity.Repaired,
            "The data integrity check of the encrypted package does not match: the file was changed or damaged after it was encrypted.", DefectLocation.InRecord(PackagePath, 0)));

    private static byte[] AesDecrypt(byte[] key, byte[] iv, byte[] cipher, string chaining)
    {
        if (cipher.Length == 0 || cipher.Length % 16 != 0)
        {
            throw new OfficeFormatException(DefectKind.Encryption, "encryption.malformed-header", "An encrypted value in the EncryptionInfo is not a whole number of AES blocks.", DefectLocation.InRecord(InfoPath, 0));
        }

        try
        {
            using Aes aes = Aes.Create();
            aes.Key = key;
            return chaining == "ChainingModeCFB"
                ? aes.DecryptCfb(cipher, iv, PaddingMode.None, 8)
                : aes.DecryptCbc(cipher, iv, PaddingMode.None);
        }
        catch (CryptographicException ex)
        {
            throw new OfficeFormatException(DefectKind.Encryption, "encryption.malformed-header", "The EncryptionInfo holds a key or IV the cipher cannot use: " + ex.Message, DefectLocation.InRecord(InfoPath, 0), ex);
        }
    }

    // ---- standard ----

    // [MS-OFFCRYPTO] §2.3.4.7: key derivation, then §2.3.4.9: verification; the package is AES in ECB mode.
    private static byte[]? DecryptStandard(Parsed p, string password, ReadOnlySpan<byte> data, int size)
    {
        byte[] pw = EncryptionText.Utf16(password);
        byte[] h = Hash(HashKind.Sha1, p.Salt, pw);
        byte[] buffer = new byte[24];
        byte[] next = new byte[20];
        for (int i = 0; i < StandardSpin; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, (uint)i);
            h.CopyTo(buffer, 4);
            OfficeHash.Sha1(buffer, next);
            (h, next) = (next, h);
        }

        byte[] hfinal = Hash(HashKind.Sha1, h, new byte[4]);
        byte[] b36 = new byte[64];
        byte[] b5c = new byte[64];
        Array.Fill(b36, (byte)0x36);
        Array.Fill(b5c, (byte)0x5C);
        for (int i = 0; i < hfinal.Length; i++)
        {
            b36[i] ^= hfinal[i];
            b5c[i] ^= hfinal[i];
        }

        byte[] x3 = new byte[40];
        OfficeHash.Sha1(b36, x3.AsSpan(0, 20));
        OfficeHash.Sha1(b5c, x3.AsSpan(20, 20));
        byte[] key = x3.AsSpan(0, p.KeyBits / 8).ToArray();

        try
        {
            using Aes aes = Aes.Create();
            aes.Key = key;
            byte[] verifier = aes.DecryptEcb(p.EncryptedVerifier, PaddingMode.None);
            byte[] verifierHash = aes.DecryptEcb(p.EncryptedVerifierHash, PaddingMode.None);
            byte[] expected = OfficeHash.Compute(HashKind.Sha1, verifier);
            int n = Math.Min(20, p.VerifierHashSize == 0 ? 20 : p.VerifierHashSize);
            if (!CryptographicOperations.FixedTimeEquals(expected.AsSpan(0, n), verifierHash.AsSpan(0, n)))
            {
                return null;
            }

            int cipherLen = data.Length & ~15;
            if (cipherLen < size)
            {
                throw new OfficeFormatException(DefectKind.Encryption, "encryption.malformed-data", "The encrypted data is shorter than the package size.", DefectLocation.InRecord(PackagePath, 8));
            }

            byte[] plain = aes.DecryptEcb(data[..cipherLen], PaddingMode.None);
            return plain.AsSpan(0, size).ToArray();
        }
        catch (CryptographicException ex)
        {
            throw new OfficeFormatException(DefectKind.Encryption, "encryption.malformed-data", "The standard encryption cannot be decrypted: " + ex.Message, DefectLocation.InRecord(InfoPath, 0), ex);
        }
    }
}
