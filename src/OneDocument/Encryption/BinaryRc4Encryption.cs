using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Encryption;

/// <summary>Which RC4 scheme an Office binary file uses.</summary>
public enum BinaryEncryptionScheme
{
    /// <summary>"Office binary document RC4 encryption": MD5-derived 40-bit key, header version 1.1 ([MS-OFFCRYPTO] §2.3.6).</summary>
    Rc4 = 1,

    /// <summary>"RC4 CryptoAPI encryption": SHA-1-derived key of 40 to 128 bits, header version 2.2, 3.2 or 4.2 ([MS-OFFCRYPTO] §2.3.5).</summary>
    Rc4CryptoApi = 2,
}

/// <summary>
/// The encryption header of an Office binary file (XLS <c>FilePass</c>, the start of a DOC table stream, a PPT
/// <c>CryptSession10Container</c>) for the two RC4 schemes: parsing, password verification, per-block key derivation, and building a
/// header for writing. Create one with <see cref="Parse"/>, call <see cref="TryUnlock"/> with a password, then take ciphers with
/// <see cref="CreateCipher"/>. The password is never kept; only the intermediate hash the key derivation needs is.
/// </summary>
public sealed class BinaryRc4Encryption
{
    /// <summary>The header version the writer produces for RC4 CryptoAPI: 4.2, as current Excel writes.</summary>
    private const ushort WrittenMajor = 4;

    private const uint FlagCryptoApi = 0x04;
    private const uint FlagDocProps = 0x08;
    private const uint FlagAes = 0x20;
    private const uint FlagExternal = 0x10;
    private const int SaltSize = 16;
    private const int MaxHeaderSize = 4096;

    private byte[]? _baseHash;

    private BinaryRc4Encryption(BinaryEncryptionScheme scheme, int keyBits, bool propsClear, byte[] salt, byte[] encryptedVerifier, byte[] encryptedVerifierHash, int length)
    {
        Scheme = scheme;
        KeyBits = keyBits;
        DocumentPropertiesClear = propsClear;
        Salt = salt;
        EncryptedVerifier = encryptedVerifier;
        EncryptedVerifierHash = encryptedVerifierHash;
        Length = length;
    }

    /// <summary>Gets the scheme.</summary>
    public BinaryEncryptionScheme Scheme { get; }

    /// <summary>Gets the key length in bits: 40 for <see cref="BinaryEncryptionScheme.Rc4"/> (its key is derived from 40 bits), 40 to 128 for CryptoAPI.</summary>
    public int KeyBits { get; }

    /// <summary>
    /// Gets a value indicating whether the document property streams are left unencrypted (the <c>fDocProps</c> flag of an RC4 CryptoAPI
    /// header, [MS-OFFCRYPTO] §2.3.1). Always true for <see cref="BinaryEncryptionScheme.Rc4"/>, which never encrypts them.
    /// </summary>
    public bool DocumentPropertiesClear { get; }

    /// <summary>Gets the 16-byte password salt.</summary>
    public byte[] Salt { get; }

    /// <summary>Gets the 16-byte encrypted verifier.</summary>
    public byte[] EncryptedVerifier { get; }

    /// <summary>Gets the encrypted verifier hash (20 bytes for CryptoAPI, 16 for RC4).</summary>
    public byte[] EncryptedVerifierHash { get; }

    /// <summary>Gets the number of bytes the parsed header occupied (the size to write for a header from <see cref="Create"/>).</summary>
    public int Length { get; }

    /// <summary>Gets a value indicating whether <see cref="TryUnlock"/> has succeeded, so <see cref="CreateCipher"/> may be used.</summary>
    public bool IsUnlocked => _baseHash is not null;

    /// <summary>
    /// Parses an encryption header: the 4-byte version, then the layout that version names.
    /// </summary>
    /// <param name="header">The bytes from the start of the header; more bytes after it are ignored.</param>
    /// <param name="location">Where the header is, for the exceptions.</param>
    /// <returns>The parsed header.</returns>
    /// <exception cref="OfficeFormatException">The header is cut short or holds impossible values (<c>encryption.malformed-header</c>).</exception>
    /// <exception cref="OfficeUnsupportedException">The header names a version, cipher or hash this library does not handle; the exception names it.</exception>
    public static BinaryRc4Encryption Parse(ReadOnlySpan<byte> header, DefectLocation location)
    {
        if (header.Length < 4)
        {
            throw Malformed("The encryption header is shorter than its version field.", location);
        }

        int major = BinaryPrimitives.ReadUInt16LittleEndian(header);
        int minor = BinaryPrimitives.ReadUInt16LittleEndian(header[2..]);

        // [MS-OFFCRYPTO] §2.3.6.1: version 1.1, 16-byte salt, verifier and verifier hash.
        if (major == 1 && minor == 1)
        {
            if (header.Length < 52)
            {
                throw Malformed("The RC4 encryption header is " + header.Length + " bytes; it needs 52.", location);
            }

            return new BinaryRc4Encryption(BinaryEncryptionScheme.Rc4, 40, true, header.Slice(4, 16).ToArray(), header.Slice(20, 16).ToArray(), header.Slice(36, 16).ToArray(), 52);
        }

        // [MS-OFFCRYPTO] §2.3.5.1: versions 2.2, 3.2 and 4.2 share one layout.
        if (major is 2 or 3 or 4 && minor == 2)
        {
            return ParseCryptoApi(header, location);
        }

        throw new OfficeUnsupportedException($"binary encryption header version {major}.{minor}", location);
    }

    private static BinaryRc4Encryption ParseCryptoApi(ReadOnlySpan<byte> header, DefectLocation location)
    {
        if (header.Length < 12)
        {
            throw Malformed("The CryptoAPI encryption header is cut short before its size field.", location);
        }

        uint headerSize = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
        if (headerSize is < 32 or > MaxHeaderSize || 12 + (long)headerSize > header.Length)
        {
            throw Malformed($"The CryptoAPI encryption header claims {headerSize} bytes; the data holds {header.Length - 12}.", location);
        }

        ReadOnlySpan<byte> h = header.Slice(12, (int)headerSize);
        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(h);
        uint algId = BinaryPrimitives.ReadUInt32LittleEndian(h[8..]);
        uint hashId = BinaryPrimitives.ReadUInt32LittleEndian(h[12..]);
        uint keySize = BinaryPrimitives.ReadUInt32LittleEndian(h[16..]);

        // [MS-OFFCRYPTO] §2.3.1: fAES marks ECMA-376 package encryption; fExternal an extension module. Neither is an RC4 binary header.
        if ((flags & FlagExternal) != 0)
        {
            throw new OfficeUnsupportedException("extensible (third-party) encryption", location);
        }

        if ((flags & FlagAes) != 0 || algId is 0x660E or 0x660F or 0x6610)
        {
            throw new OfficeUnsupportedException("AES encryption in an Office binary file", location);
        }

        if ((flags & FlagCryptoApi) == 0)
        {
            throw Malformed("The CryptoAPI encryption header does not set the fCryptoAPI flag.", location);
        }

        if (algId is not (0 or 0x6801))
        {
            throw new OfficeUnsupportedException($"CryptoAPI cipher 0x{algId:X} (only RC4 is defined for binary files)", location);
        }

        if (hashId is not (0 or 0x8004))
        {
            throw new OfficeUnsupportedException($"CryptoAPI hash 0x{hashId:X} (only SHA-1 is defined)", location);
        }

        // [MS-OFFCRYPTO] §2.3.5.1: 0 reads as 40 bits; otherwise 40 to 128 in steps of 8.
        int bits = keySize == 0 ? 40 : (int)Math.Min(keySize, 100_000u);
        if (bits is < 40 or > 128 || bits % 8 != 0)
        {
            throw Malformed($"The RC4 key size of {keySize} bits is not 40 to 128 in steps of 8.", location);
        }

        ReadOnlySpan<byte> v = header[(12 + (int)headerSize)..];
        if (v.Length < 4 + SaltSize + 16 + 4 + 20)
        {
            throw Malformed("The encryption verifier is cut short.", location);
        }

        if (BinaryPrimitives.ReadUInt32LittleEndian(v) != SaltSize)
        {
            throw Malformed("The encryption verifier's salt is not 16 bytes.", location);
        }

        uint hashSize = BinaryPrimitives.ReadUInt32LittleEndian(v[(4 + SaltSize + 16)..]);
        if (hashSize != 20)
        {
            throw Malformed($"The verifier hash size is {hashSize}; SHA-1 gives 20.", location);
        }

        int length = 12 + (int)headerSize + 4 + SaltSize + 16 + 4 + 20;
        return new BinaryRc4Encryption(
            BinaryEncryptionScheme.Rc4CryptoApi,
            bits,
            (flags & FlagDocProps) != 0,
            v.Slice(4, SaltSize).ToArray(),
            v.Slice(4 + SaltSize, 16).ToArray(),
            v.Slice(4 + SaltSize + 16 + 4, 20).ToArray(),
            length);
    }

    /// <summary>
    /// Checks a password against the header's verifier and, when it matches, keeps what is needed to derive keys.
    /// </summary>
    /// <param name="password">The password; at most 255 UTF-16 code units are used, as Office does.</param>
    /// <returns>True when the password opens the file; false when it does not (nothing is kept).</returns>
    public bool TryUnlock(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        byte[] pw = EncryptionText.Utf16(password);
        byte[] baseHash = Scheme == BinaryEncryptionScheme.Rc4CryptoApi ? Sha1(Salt, pw) : Rc4BaseHash(Salt, pw);

        // [MS-OFFCRYPTO] §2.3.5.6 and §2.3.6.4: decrypt the verifier then its hash with one unbroken key stream (block 0).
        Rc4Cipher c = CipherFor(baseHash, 0);
        byte[] verifier = (byte[])EncryptedVerifier.Clone();
        c.Process(verifier);
        byte[] hash = (byte[])EncryptedVerifierHash.Clone();
        c.Process(hash);
        byte[] expected = Scheme == BinaryEncryptionScheme.Rc4CryptoApi ? OfficeHash.Compute(HashKind.Sha1, verifier) : OfficeHash.Compute(HashKind.Md5, verifier);
        if (!CryptographicOperations.FixedTimeEquals(expected, hash.AsSpan(0, expected.Length)))
        {
            return false;
        }

        _baseHash = baseHash;
        return true;
    }

    /// <summary>Creates the key stream for one block (the unit Office re-keys on: 1024 bytes in XLS, 512 in DOC, a persist object in PPT).</summary>
    /// <param name="block">The block number.</param>
    /// <returns>A cipher at the start of that block's key stream.</returns>
    /// <exception cref="InvalidOperationException"><see cref="TryUnlock"/> has not succeeded.</exception>
    public Rc4Cipher CreateCipher(uint block) =>
        CipherFor(_baseHash ?? throw new InvalidOperationException("Call TryUnlock with the right password first."), block);

    /// <summary>The name Office gives the encrypted summary stream in a compound file ([MS-PPT] §2.1.6 names it; Word and Excel use the same name).</summary>
    public const string EncryptedSummaryStreamName = "EncryptedSummary";

    /// <summary>
    /// Reads an RC4 CryptoAPI encrypted summary stream ([MS-OFFCRYPTO] §2.3.5.4), where a file whose header has <c>fDocProps</c> cleared keeps
    /// its document property streams (and any other streams or storages the application put there) encrypted.
    /// </summary>
    /// <param name="summary">The bytes of the encrypted summary stream.</param>
    /// <param name="streams">On success, the decrypted streams by name (for example <c>U+0005 SummaryInformation</c>), in descriptor order.</param>
    /// <returns>True when the stream is well formed and every descriptor lies inside it; false (with no streams) otherwise.</returns>
    /// <exception cref="InvalidOperationException"><see cref="TryUnlock"/> has not succeeded.</exception>
    public bool TryReadEncryptedSummary(ReadOnlySpan<byte> summary, out IReadOnlyList<KeyValuePair<string, byte[]>> streams)
    {
        const int MaxDescriptors = 256;
        const int FixedDescriptor = 16;
        streams = [];
        if (_baseHash is null)
        {
            throw new InvalidOperationException("Call TryUnlock with the right password first.");
        }

        if (summary.Length < 8)
        {
            return false;
        }

        // The two leading fields and the descriptor array are each encrypted from the start of the block-0 key stream.
        byte[] head = summary[..8].ToArray();
        CipherFor(_baseHash, 0).Process(head);
        uint arrayOffset = BinaryPrimitives.ReadUInt32LittleEndian(head);
        uint arraySize = BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(4));
        if (arrayOffset < 8 || arraySize < 4 || (ulong)arrayOffset + arraySize > (ulong)summary.Length)
        {
            return false;
        }

        byte[] array = summary.Slice((int)arrayOffset, (int)arraySize).ToArray();
        CipherFor(_baseHash, 0).Process(array);
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(array);
        if (count > MaxDescriptors)
        {
            return false;
        }

        var result = new List<KeyValuePair<string, byte[]>>((int)count);
        int at = 4;
        for (uint i = 0; i < count; i++)
        {
            if (array.Length - at < FixedDescriptor)
            {
                return false;
            }

            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(array.AsSpan(at));
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(array.AsSpan(at + 4));
            ushort block = BinaryPrimitives.ReadUInt16LittleEndian(array.AsSpan(at + 8));
            int nameChars = array[at + 10];
            int nameBytes = (nameChars + 1) * 2;
            if (array.Length - at - FixedDescriptor < nameBytes || offset < 8 || (ulong)offset + size > arrayOffset)
            {
                return false;
            }

            string name = Encoding.Unicode.GetString(array, at + FixedDescriptor, nameChars * 2);
            byte[] data = summary.Slice((int)offset, (int)size).ToArray();
            CipherFor(_baseHash, block).Process(data);
            result.Add(new KeyValuePair<string, byte[]>(name, data));
            at += FixedDescriptor + nameBytes;
        }

        streams = result;
        return true;
    }

    private Rc4Cipher CipherFor(byte[] baseHash, uint block)
    {
        byte[] input = new byte[baseHash.Length + 4];
        baseHash.CopyTo(input, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(input.AsSpan(baseHash.Length), block);
        if (Scheme == BinaryEncryptionScheme.Rc4CryptoApi)
        {
            // [MS-OFFCRYPTO] §2.3.5.2: Hfinal = SHA-1(H0 + block); the key is its first KeyBits bits, and a 40-bit key is padded with zeros to 128 bits.
            byte[] hfinal = OfficeHash.Compute(HashKind.Sha1, input);
            byte[] key = new byte[KeyBits == 40 ? 16 : KeyBits / 8];
            Array.Copy(hfinal, key, KeyBits / 8);
            return new Rc4Cipher(key);
        }
        else
        {
            // [MS-OFFCRYPTO] §2.3.6.2: Hfinal = MD5(TruncatedHash + block); the key is its first 128 bits.
            return new Rc4Cipher(OfficeHash.Compute(HashKind.Md5, input));
        }
    }

    private static byte[] Sha1(byte[] salt, byte[] pw)
    {
        byte[] input = new byte[salt.Length + pw.Length];
        salt.CopyTo(input, 0);
        pw.CopyTo(input, salt.Length);
        return OfficeHash.Compute(HashKind.Sha1, input);
    }

    // [MS-OFFCRYPTO] §2.3.6.2: H0 = MD5(password); the first 5 bytes plus the salt, 21 bytes, repeated 16 times; H1 = MD5 of that; keep 5 bytes.
    private static byte[] Rc4BaseHash(byte[] salt, byte[] pw)
    {
        byte[] h0 = OfficeHash.Compute(HashKind.Md5, pw);
        byte[] buffer = new byte[16 * 21];
        for (int i = 0; i < 16; i++)
        {
            Array.Copy(h0, 0, buffer, i * 21, 5);
            salt.CopyTo(buffer, (i * 21) + 5);
        }

        return OfficeHash.Compute(HashKind.Md5, buffer).AsSpan(0, 5).ToArray();
    }

    /// <summary>
    /// Builds an RC4 CryptoAPI header for writing a file, with the verifier encrypted under the password. Nothing random happens here:
    /// the caller supplies the salt and the verifier, so output can be reproduced from a seed.
    /// </summary>
    /// <param name="password">The password; at most 255 UTF-16 code units are used.</param>
    /// <param name="salt">The 16-byte salt.</param>
    /// <param name="verifier">The 16-byte verifier value.</param>
    /// <param name="keyBits">The RC4 key length, 40 to 128 in steps of 8. 128 is what current Excel writes.</param>
    /// <returns>The unlocked header, whose <see cref="ToBytes"/> is the payload for the <c>FilePass</c> record.</returns>
    /// <exception cref="ArgumentException">A size is wrong.</exception>
    public static BinaryRc4Encryption Create(string password, ReadOnlySpan<byte> salt, ReadOnlySpan<byte> verifier, int keyBits = 128)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (salt.Length != SaltSize || verifier.Length != 16)
        {
            throw new ArgumentException("The salt and the verifier are 16 bytes each.");
        }

        if (keyBits is < 40 or > 128 || keyBits % 8 != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(keyBits), keyBits, "The RC4 key is 40 to 128 bits in steps of 8.");
        }

        byte[] s = salt.ToArray();
        byte[] baseHash = Sha1(s, EncryptionText.Utf16(password));

        // Document properties stay in the clear (fDocProps), so no encrypted summary stream has to be written.
        var probe = new BinaryRc4Encryption(BinaryEncryptionScheme.Rc4CryptoApi, keyBits, true, s, new byte[16], new byte[20], 0);
        Rc4Cipher c = probe.CipherFor(baseHash, 0);
        byte[] encVerifier = verifier.ToArray();
        c.Process(encVerifier);
        byte[] encHash = OfficeHash.Compute(HashKind.Sha1, verifier);
        c.Process(encHash);
        var result = new BinaryRc4Encryption(BinaryEncryptionScheme.Rc4CryptoApi, keyBits, true, s, encVerifier, encHash, CryptoApiHeaderLength);
        result._baseHash = baseHash;
        return result;
    }

    private const string CspName = "Microsoft Enhanced Cryptographic Provider v1.0";

    // 4 version + 4 flags + 4 header size + 32 fixed fields + CSP name with its terminator, then the verifier.
    private static int CryptoApiHeaderLength => 12 + 32 + ((CspName.Length + 1) * 2) + 4 + SaltSize + 16 + 4 + 20;

    /// <summary>Serialises a header made by <see cref="Create"/> ([MS-OFFCRYPTO] §2.3.5.1).</summary>
    /// <returns>The header bytes.</returns>
    /// <exception cref="NotSupportedException">The header is an RC4 (version 1.1) header, which this library only reads.</exception>
    public byte[] ToBytes()
    {
        if (Scheme != BinaryEncryptionScheme.Rc4CryptoApi)
        {
            throw new NotSupportedException("Only RC4 CryptoAPI headers are written.");
        }

        byte[] csp = Encoding.Unicode.GetBytes(CspName + "\0");
        int headerSize = 32 + csp.Length;
        byte[] b = new byte[CryptoApiHeaderLength];
        Span<byte> s = b;
        BinaryPrimitives.WriteUInt16LittleEndian(s, WrittenMajor);
        BinaryPrimitives.WriteUInt16LittleEndian(s[2..], 2);
        uint flags = FlagCryptoApi | (DocumentPropertiesClear ? FlagDocProps : 0);
        BinaryPrimitives.WriteUInt32LittleEndian(s[4..], flags);
        BinaryPrimitives.WriteUInt32LittleEndian(s[8..], (uint)headerSize);
        Span<byte> h = s[12..];
        BinaryPrimitives.WriteUInt32LittleEndian(h, flags);
        BinaryPrimitives.WriteUInt32LittleEndian(h[8..], 0x6801);
        BinaryPrimitives.WriteUInt32LittleEndian(h[12..], 0x8004);
        BinaryPrimitives.WriteUInt32LittleEndian(h[16..], (uint)KeyBits);
        BinaryPrimitives.WriteUInt32LittleEndian(h[20..], 1);
        csp.CopyTo(h[32..]);
        Span<byte> v = s[(12 + headerSize)..];
        BinaryPrimitives.WriteUInt32LittleEndian(v, SaltSize);
        Salt.CopyTo(v[4..]);
        EncryptedVerifier.CopyTo(v[(4 + SaltSize)..]);
        BinaryPrimitives.WriteUInt32LittleEndian(v[(4 + SaltSize + 16)..], 20);
        EncryptedVerifierHash.CopyTo(v[(4 + SaltSize + 16 + 4)..]);
        return b;
    }

    private static OfficeFormatException Malformed(string message, DefectLocation location) =>
        new(DefectKind.Encryption, "encryption.malformed-header", message, location);
}

/// <summary>
/// Applies Office's block-wise RC4 to a stream that is encrypted in fixed-size blocks counted from its start (1024 bytes in an XLS
/// workbook stream, 512 in DOC streams, [MS-XLS] §2.2.10 and [MS-DOC] §2.2.6). Bytes the format leaves in the clear still consume key
/// stream, so a reader or writer calls <see cref="Skip"/> for them and <see cref="Process"/> for the rest, in stream order.
/// </summary>
public sealed class Rc4BlockCipher
{
    private readonly BinaryRc4Encryption _encryption;
    private readonly int _blockSize;
    private Rc4Cipher? _cipher;
    private long _position;

    /// <summary>Creates a stream cipher positioned at byte 0 of the stream.</summary>
    /// <param name="encryption">An unlocked header.</param>
    /// <param name="blockSize">The block size in bytes (the key changes every block).</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="blockSize"/> is not positive.</exception>
    public Rc4BlockCipher(BinaryRc4Encryption encryption, int blockSize)
    {
        ArgumentNullException.ThrowIfNull(encryption);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockSize);
        _encryption = encryption;
        _blockSize = blockSize;
    }

    /// <summary>Gets the current position in the stream, counting clear bytes.</summary>
    public long Position => _position;

    /// <summary>Transforms the bytes in place and advances.</summary>
    /// <param name="data">Ciphertext to decrypt or plaintext to encrypt.</param>
    public void Process(Span<byte> data) => Run(data, data.Length, transform: true);

    /// <summary>Advances over bytes that are stored in the clear.</summary>
    /// <param name="count">The number of bytes.</param>
    public void Skip(int count) => Run(default, count, transform: false);

    private void Run(Span<byte> data, int count, bool transform)
    {
        int done = 0;
        while (done < count)
        {
            long block = _position / _blockSize;
            int inBlock = (int)(_position % _blockSize);
            if (inBlock == 0 || _cipher is null)
            {
                _cipher = _encryption.CreateCipher((uint)block);
                if (inBlock != 0)
                {
                    _cipher.Skip(inBlock);
                }
            }

            int n = Math.Min(count - done, _blockSize - inBlock);
            if (transform)
            {
                _cipher.Process(data.Slice(done, n));
            }
            else
            {
                _cipher.Skip(n);
            }

            done += n;
            _position += n;
        }
    }
}
