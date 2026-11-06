using System.Buffers.Binary;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Encryption;

namespace RecursiveAI.OneDocument.Doc.Binary;

/// <summary>
/// Decrypts a password-protected Word binary file in memory ([MS-DOC] §2.2.6, [MS-OFFCRYPTO] §2.3.5 to §2.3.7). Three streams are
/// protected: the <c>WordDocument</c> stream after its first 68 bytes, the table stream (after the encryption header, for the RC4
/// schemes) and the <c>Data</c> stream. Every other stream is stored in the clear.
/// </summary>
internal static class DocEncryption
{
    // [MS-DOC] §2.2.6.1 to §2.2.6.3: the first 68 bytes of the WordDocument stream (the FIB base and the start of its arrays) stay as they are.
    private const int ClearPrefix = 68;

    // [MS-DOC] §2.2.6.2 and §2.2.6.3: the RC4 key changes every 512 bytes.
    private const int BlockSize = 512;

    /// <summary>
    /// Reads the encryption fields at the start of a <c>WordDocument</c> stream without parsing the rest, which is not readable until
    /// the stream is decrypted.
    /// </summary>
    /// <param name="word">The <c>WordDocument</c> stream.</param>
    /// <param name="info">What the FIB base says.</param>
    /// <returns>True when the file is encrypted or obfuscated.</returns>
    internal static bool TryPeek(ReadOnlySpan<byte> word, out DocEncryptionInfo info)
    {
        info = default;
        if (word.Length < 18)
        {
            return false;
        }

        ushort wIdent = BinaryPrimitives.ReadUInt16LittleEndian(word);
        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(word[10..]);
        if ((flags & 0x0100) == 0)
        {
            return false;
        }

        info = new DocEncryptionInfo(
            IsWord97: wIdent == 0xA5EC,
            Obfuscated: (flags & 0x8000) != 0,
            OneTable: (flags & 0x0200) != 0,
            Key: BinaryPrimitives.ReadUInt32LittleEndian(word[14..]));
        return true;
    }

    /// <summary>Decrypts the three streams in place.</summary>
    /// <param name="word">The <c>WordDocument</c> stream.</param>
    /// <param name="table">The table stream, or empty.</param>
    /// <param name="data">The <c>Data</c> stream, or empty.</param>
    /// <param name="info">From <see cref="TryPeek"/>.</param>
    /// <param name="context">Limits, defect sink and password.</param>
    /// <param name="tablePath">The table stream path, for locations.</param>
    /// <exception cref="OfficeEncryptedException">The password is missing or wrong.</exception>
    /// <exception cref="OfficeFormatException">The encryption header is damaged.</exception>
    /// <exception cref="OfficeUnsupportedException">The scheme is not handled.</exception>
    internal static void Decrypt(byte[] word, byte[] table, byte[] data, DocEncryptionInfo info, OpenContext context, string tablePath)
    {
        DefectLocation at = DefectLocation.InRecord(DocFile.WordPath, 14);
        if (!info.IsWord97)
        {
            throw new OfficeUnsupportedException("password-protected Word 6/95 documents", at);
        }

        if (info.Obfuscated)
        {
            // [MS-DOC] §2.2.6.1: the verifier of Method 2 is in lKey; the transform runs from the start of each stream.
            if (context.Password is null)
            {
                throw new OfficeEncryptedException(EncryptionProblem.PasswordRequired, at);
            }

            XorObfuscation? xor = XorObfuscation.TryUnlockMethod2(context.Password, info.Key);
            if (xor is null)
            {
                throw new OfficeEncryptedException(EncryptionProblem.PasswordIncorrect, at);
            }

            if (word.Length > ClearPrefix)
            {
                xor.TransformMethod2(word.AsSpan(ClearPrefix), ClearPrefix);
            }

            xor.TransformMethod2(table, 0);
            xor.TransformMethod2(data, 0);
            return;
        }

        // [MS-DOC] §2.2.6.2 and §2.2.6.3: the encryption header fills the first lKey bytes of the table stream.
        if (info.Key < 4 || info.Key > table.Length)
        {
            throw new OfficeFormatException(DefectKind.Encryption, "encryption.malformed-header", $"The encryption header is {info.Key} bytes; the table stream holds {table.Length}.", DefectLocation.InRecord(tablePath, 0));
        }

        BinaryRc4Encryption enc = BinaryRc4Encryption.Parse(table.AsSpan(0, (int)info.Key), DefectLocation.InRecord(tablePath, 0));
        if (context.Password is null)
        {
            throw new OfficeEncryptedException(EncryptionProblem.PasswordRequired, at);
        }

        if (!enc.TryUnlock(context.Password))
        {
            throw new OfficeEncryptedException(EncryptionProblem.PasswordIncorrect, at);
        }

        // The key stream starts at byte 0 of each stream even where the bytes are clear, so those bytes are skipped, not ignored.
        var w = new Rc4BlockCipher(enc, BlockSize);
        w.Skip(Math.Min(ClearPrefix, word.Length));
        if (word.Length > ClearPrefix)
        {
            w.Process(word.AsSpan(ClearPrefix));
        }

        var t = new Rc4BlockCipher(enc, BlockSize);
        t.Skip((int)info.Key);
        t.Process(table.AsSpan((int)info.Key));
        new Rc4BlockCipher(enc, BlockSize).Process(data);
    }
}

/// <summary>The encryption fields of a Word FIB base ([MS-DOC] §2.5.2).</summary>
/// <param name="IsWord97">True for the Word 97 and later FIB (<c>wIdent</c> 0xA5EC); the Word 6/95 layout is not handled.</param>
/// <param name="Obfuscated"><c>fObfuscated</c>: XOR obfuscation instead of encryption.</param>
/// <param name="OneTable"><c>fWhichTblStm</c>: the table stream is <c>1Table</c>.</param>
/// <param name="Key"><c>lKey</c>: the XOR verifier, or the size of the encryption header at the start of the table stream.</param>
internal readonly record struct DocEncryptionInfo(bool IsWord97, bool Obfuscated, bool OneTable, uint Key);
