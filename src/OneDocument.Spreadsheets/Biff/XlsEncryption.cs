using System.Buffers.Binary;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Encryption;

namespace RecursiveAI.OneDocument.Spreadsheets.Biff;

/// <summary>
/// Password protection of a BIFF workbook stream ([MS-XLS] §2.2.10, [MS-OFFCRYPTO] §2.3.5 to §2.3.7): finds the <c>FilePass</c> record,
/// checks the password and decrypts the stream in place, record by record; and the writing direction, which adds a <c>FilePass</c>
/// record to a BIFF8 stream and encrypts it with RC4 CryptoAPI.
/// </summary>
public static class XlsEncryption
{
    private const int FilePass = 0x002F;
    private const int BoundSheet8 = 0x0085;
    private const int Index = 0x020B;
    private const int ExtSst = 0x00FF;
    private const int WriteProtect = 0x0086;
    private const int Bof8 = 0x0809;
    private const int BlockSize = 1024; // [MS-XLS] §2.2.10: RC4 re-keys every 1024 bytes of the stream.

    /// <summary>
    /// The records that are never encrypted ([MS-XLS] §2.2.10): BOF, FilePass, UsrExcl, FileLock, InterfaceHdr, RRDInfo and RRDHead.
    /// </summary>
    private static bool IsClear(int type) =>
        type is 0x0809 or 0x0409 or 0x0209 or 0x0009 or FilePass or 0x0194 or 0x0195 or 0x00E1 or 0x0196 or 0x0138;

    /// <summary>Finds a <c>FilePass</c> record in the global records at the start of a BIFF stream.</summary>
    /// <param name="stream">The workbook stream.</param>
    /// <param name="offset">The offset of the record header, when found.</param>
    /// <returns>True when the workbook is password protected.</returns>
    public static bool TryFindFilePass(ReadOnlySpan<byte> stream, out int offset)
    {
        offset = -1;
        int pos = 0;
        int depth = 0;
        while (pos + 4 <= stream.Length)
        {
            int type = BinaryPrimitives.ReadUInt16LittleEndian(stream[pos..]);
            int len = BinaryPrimitives.ReadUInt16LittleEndian(stream[(pos + 2)..]);
            if (type == FilePass)
            {
                offset = pos;
                return true;
            }

            // The encryption record sits in the global substream, before its first sheet: stop at the second BOF or at an EOF.
            if (type is 0x0809 or 0x0409 or 0x0209 or 0x0009)
            {
                if (++depth > 1)
                {
                    return false;
                }
            }
            else if (type == 0x000A)
            {
                return false;
            }

            pos += 4 + len;
        }

        return false;
    }

    /// <summary>
    /// Decrypts a password-protected workbook stream. The result is a copy in which every record body is plain; record headers, the
    /// <c>FilePass</c> record and the records that were never encrypted are as they were, so offsets do not change.
    /// </summary>
    /// <param name="stream">The workbook stream.</param>
    /// <param name="filePassOffset">The offset found by <see cref="TryFindFilePass"/>.</param>
    /// <param name="context">Limits, defect sink and password. When no password is given, the default for workbooks that are protected against editing only (<see cref="PackageEncryption.DefaultPassword"/>) is tried; a given password that does not match is not retried with it.</param>
    /// <param name="path">The stream path for locations.</param>
    /// <returns>The plain stream.</returns>
    /// <exception cref="OfficeEncryptedException">The password is missing or wrong.</exception>
    /// <exception cref="OfficeFormatException">The <c>FilePass</c> record is damaged (<c>encryption.malformed-header</c>).</exception>
    /// <exception cref="OfficeUnsupportedException">The scheme is not handled; the exception names it.</exception>
    public static byte[] Decrypt(ReadOnlySpan<byte> stream, int filePassOffset, OpenContext context, string path) =>
        Decrypt(stream, filePassOffset, context, path, out _);

    /// <summary>
    /// Decrypts the workbook stream like <see cref="Decrypt(ReadOnlySpan{byte}, int, OpenContext, string)"/> and also returns the unlocked RC4 header, which
    /// reads the encrypted summary stream of a file whose header has <c>fDocProps</c> cleared.
    /// </summary>
    /// <param name="stream">The workbook stream.</param>
    /// <param name="filePassOffset">The offset found by <see cref="TryFindFilePass"/>.</param>
    /// <param name="context">Limits, defect sink and password.</param>
    /// <param name="path">The stream path for locations.</param>
    /// <param name="unlocked">The unlocked RC4 header; null for XOR obfuscation.</param>
    /// <returns>The plain stream.</returns>
    /// <exception cref="OfficeEncryptedException">The password is missing or wrong.</exception>
    /// <exception cref="OfficeFormatException">The <c>FilePass</c> record is damaged.</exception>
    /// <exception cref="OfficeUnsupportedException">The scheme is not handled.</exception>
    public static byte[] Decrypt(ReadOnlySpan<byte> stream, int filePassOffset, OpenContext context, string path, out BinaryRc4Encryption? unlocked)
    {
        unlocked = null;
        ArgumentNullException.ThrowIfNull(context);
        DefectLocation at = DefectLocation.InRecord(path, filePassOffset);
        int type0 = BinaryPrimitives.ReadUInt16LittleEndian(stream);
        if (type0 is 0x0009 or 0x0209 or 0x0409)
        {
            throw new OfficeUnsupportedException("encrypted Excel 2.0-4.0 workbooks (BIFF2-4)", at);
        }

        int len = BinaryPrimitives.ReadUInt16LittleEndian(stream[(filePassOffset + 2)..]);
        ReadOnlySpan<byte> data = stream.Slice(filePassOffset + 4, Math.Min(len, stream.Length - filePassOffset - 4));
        if (data.Length < 2)
        {
            throw new OfficeFormatException(DefectKind.Encryption, "encryption.malformed-header", "The FilePass record is shorter than its type field.", at);
        }

        int scheme = BinaryPrimitives.ReadUInt16LittleEndian(data);
        string?[] candidates = context.Password is null ? [PackageEncryption.DefaultPassword] : [context.Password];
        byte[] copy = stream.ToArray();
        if (scheme == 0)
        {
            // [MS-XLS] §2.4.117 and §2.5.343: XOR obfuscation carries a 16-bit key and a 16-bit verifier.
            if (data.Length < 6)
            {
                throw new OfficeFormatException(DefectKind.Encryption, "encryption.malformed-header", "The XOR obfuscation FilePass record is cut short.", at);
            }

            ushort key = BinaryPrimitives.ReadUInt16LittleEndian(data[2..]);
            ushort verifier = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
            foreach (string? candidate in candidates)
            {
                XorObfuscation? xor = XorObfuscation.TryUnlockMethod1(candidate!, key, verifier);
                if (xor is not null)
                {
                    ApplyXor(copy, filePassOffset, xor);
                    return copy;
                }
            }
        }
        else if (scheme == 1)
        {
            BinaryRc4Encryption enc = BinaryRc4Encryption.Parse(data[2..], at);
            foreach (string? candidate in candidates)
            {
                if (enc.TryUnlock(candidate!))
                {
                    ApplyRc4(copy, filePassOffset, enc);
                    unlocked = enc;
                    return copy;
                }
            }
        }
        else
        {
            throw new OfficeUnsupportedException($"FilePass encryption type {scheme}", at);
        }

        throw new OfficeEncryptedException(context.Password is null ? EncryptionProblem.PasswordRequired : EncryptionProblem.PasswordIncorrect, at);
    }

    // [MS-XLS] §2.2.10: the key stream runs over the whole stream, headers and clear records included; those bytes are skipped, the rest transformed.
    private static void ApplyRc4(byte[] stream, int filePassOffset, BinaryRc4Encryption enc)
    {
        var cipher = new Rc4BlockCipher(enc, BlockSize);
        int pos = 0;
        while (pos + 4 <= stream.Length)
        {
            int type = BinaryPrimitives.ReadUInt16LittleEndian(stream.AsSpan(pos));
            int len = BinaryPrimitives.ReadUInt16LittleEndian(stream.AsSpan(pos + 2));
            if (pos + 4 + len > stream.Length)
            {
                break;
            }

            cipher.Skip(4);
            Span<byte> body = stream.AsSpan(pos + 4, len);
            if (pos <= filePassOffset || IsClear(type))
            {
                cipher.Skip(len);
            }
            else if (type == BoundSheet8 && len >= 4)
            {
                // [MS-XLS] §2.2.10: the sheet position field is not encrypted.
                cipher.Skip(4);
                cipher.Process(body[4..]);
            }
            else
            {
                cipher.Process(body);
            }

            pos += 4 + len;
        }
    }

    // [MS-OFFCRYPTO] §2.3.7.3. The array index of a record's first byte is not given by either specification; see docs/encryption.md.
    private static void ApplyXor(byte[] stream, int filePassOffset, XorObfuscation xor)
    {
        int pos = 0;
        while (pos + 4 <= stream.Length)
        {
            int type = BinaryPrimitives.ReadUInt16LittleEndian(stream.AsSpan(pos));
            int len = BinaryPrimitives.ReadUInt16LittleEndian(stream.AsSpan(pos + 2));
            if (pos + 4 + len > stream.Length)
            {
                break;
            }

            if (pos > filePassOffset && !IsClear(type))
            {
                int skip = type == BoundSheet8 && len >= 4 ? 4 : 0;
                Span<byte> body = stream.AsSpan(pos + 4 + skip, len - skip);
                xor.DecryptMethod1(body, pos + 4 + skip + len);
            }

            pos += 4 + len;
        }
    }

    /// <summary>
    /// Password-protects a BIFF8 workbook stream with RC4 CryptoAPI encryption (128-bit key), as Excel does: a <c>FilePass</c> record is
    /// inserted after the BOF, the file positions the stream holds (sheet positions, index and shared-string-table offsets) are moved
    /// to match, and every record body except those the format leaves clear is encrypted in 1024-byte blocks. The document property
    /// streams stay in the clear, and that is declared in the header, so no encrypted summary stream is needed.
    /// </summary>
    /// <param name="workbookStream">A complete BIFF8 workbook stream (starting with a BOF record), not yet encrypted.</param>
    /// <param name="password">The password; at most 255 UTF-16 code units are used.</param>
    /// <param name="seed">Seeds the salt and the verifier, so the same inputs give the same bytes.</param>
    /// <returns>The encrypted stream.</returns>
    /// <exception cref="ArgumentException">The stream is not a BIFF8 stream, is already encrypted, or is cut short.</exception>
    public static byte[] Encrypt(ReadOnlySpan<byte> workbookStream, string password, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (workbookStream.Length < 4 || BinaryPrimitives.ReadUInt16LittleEndian(workbookStream) != Bof8)
        {
            throw new ArgumentException("The stream must be a BIFF8 workbook stream starting with a BOF record.", nameof(workbookStream));
        }

        if (TryFindFilePass(workbookStream, out _))
        {
            throw new ArgumentException("The stream is already password protected.", nameof(workbookStream));
        }

        (byte[] salt, byte[] verifier) = SaltAndVerifier(password, seed);
        BinaryRc4Encryption enc = BinaryRc4Encryption.Create(password, salt, verifier, 128);
        byte[] payload = new byte[2 + enc.ToBytes().Length];
        BinaryPrimitives.WriteUInt16LittleEndian(payload, 1); // wEncryptionType: RC4
        enc.ToBytes().CopyTo(payload, 2);
        byte[] output = InsertFilePass(workbookStream, payload, out int insertAt);
        ApplyRc4(output, insertAt, enc);
        return output;
    }

    // The FilePass record goes after the BOF and an optional WriteProtect record ([MS-XLS] §2.1.7.20.1); the file positions after it move.
    internal static byte[] InsertFilePass(ReadOnlySpan<byte> workbookStream, byte[] payload, out int insertAt)
    {
        insertAt = 4 + BinaryPrimitives.ReadUInt16LittleEndian(workbookStream[2..]);
        if (insertAt + 4 <= workbookStream.Length && BinaryPrimitives.ReadUInt16LittleEndian(workbookStream[insertAt..]) == WriteProtect)
        {
            insertAt += 4 + BinaryPrimitives.ReadUInt16LittleEndian(workbookStream[(insertAt + 2)..]);
        }

        if (insertAt > workbookStream.Length)
        {
            throw new ArgumentException("The BOF record runs past the end of the stream.", nameof(workbookStream));
        }

        int delta = 4 + payload.Length;
        byte[] output = new byte[workbookStream.Length + delta];
        workbookStream[..insertAt].CopyTo(output);
        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(insertAt), FilePass);
        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(insertAt + 2), (ushort)payload.Length);
        payload.CopyTo(output.AsSpan(insertAt + 4));
        workbookStream[insertAt..].CopyTo(output.AsSpan(insertAt + delta));
        ShiftFilePointers(output, insertAt + delta, delta);
        return output;
    }

    // [MS-XLS] FilePointer fields: BoundSheet8.lbPlyPos, Index.ibXF and rgibRw, and ISSTInf.ib in ExtSST all point into the stream, so
    // every record after the inserted one moves them by the size of what was inserted.
    private static void ShiftFilePointers(byte[] stream, int from, int delta)
    {
        int pos = from;
        while (pos + 4 <= stream.Length)
        {
            int type = BinaryPrimitives.ReadUInt16LittleEndian(stream.AsSpan(pos));
            int len = BinaryPrimitives.ReadUInt16LittleEndian(stream.AsSpan(pos + 2));
            if (pos + 4 + len > stream.Length)
            {
                break;
            }

            Span<byte> body = stream.AsSpan(pos + 4, len);
            switch (type)
            {
                case BoundSheet8 when len >= 4:
                    Add(body, 0, delta);
                    break;
                case Index when len >= 16:
                    Add(body, 12, delta);
                    for (int o = 16; o + 4 <= len; o += 4)
                    {
                        Add(body, o, delta);
                    }

                    break;
                case ExtSst when len >= 2:
                    for (int o = 2; o + 8 <= len; o += 8)
                    {
                        Add(body, o, delta);
                    }

                    break;
            }

            pos += 4 + len;
        }
    }

    private static void Add(Span<byte> body, int offset, int delta) =>
        BinaryPrimitives.WriteUInt32LittleEndian(body[offset..], BinaryPrimitives.ReadUInt32LittleEndian(body[offset..]) + (uint)delta);

    private static (byte[] Salt, byte[] Verifier) SaltAndVerifier(string password, ulong seed)
    {
        // Nothing random may reach written bytes, so the salt and the verifier come from the seed and the password. They are not secrets.
        byte[] pw = System.Text.Encoding.Unicode.GetBytes(password);
        byte[] input = new byte[8 + pw.Length + 1];
        BinaryPrimitives.WriteUInt64LittleEndian(input, seed);
        pw.CopyTo(input, 8);
        input[^1] = 1;
        byte[] a = System.Security.Cryptography.SHA256.HashData(input);
        input[^1] = 2;
        byte[] b = System.Security.Cryptography.SHA256.HashData(input);
        return (a.AsSpan(0, 16).ToArray(), b.AsSpan(0, 16).ToArray());
    }
}
