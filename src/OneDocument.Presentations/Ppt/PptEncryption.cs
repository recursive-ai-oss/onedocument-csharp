using System.Buffers.Binary;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Encryption;

namespace RecursiveAI.OneDocument.Ppt.Binary;

/// <summary>
/// Decrypts a password-protected PowerPoint binary file in memory ([MS-PPT] §2.3.7, [MS-OFFCRYPTO] §2.3.5). Only RC4 CryptoAPI is
/// defined for this format. The user edits, the persist directories and the header of the <c>CryptSession10Container</c> are never
/// encrypted; every persist object is, with the persist identifier as the block number, and so is the <c>Pictures</c> stream.
/// </summary>
internal static class PptEncryption
{
    /// <summary>
    /// The password PowerPoint used for a file that was protected against editing only ([MS-OFFCRYPTO] §2.4.2.3); such a file opens with no password.
    /// </summary>
    internal const string DefaultPassword = "/01Hannes Ruescher/01";

    // [MS-PPT] §2.3.1: a record header is 8 bytes, ending with the 4-byte length.
    private const int HeaderSize = 8;

    internal static (byte[] Document, byte[] Pictures, BinaryRc4Encryption Header) Decrypt(
        ReadOnlySpan<byte> document,
        ReadOnlySpan<byte> pictures,
        IReadOnlyDictionary<uint, long> persistOffsets,
        long cryptOffset,
        OpenContext context)
    {
        DefectLocation at = DefectLocation.InRecord(PptFile.DocumentStreamName, cryptOffset);
        if (cryptOffset < 0 || cryptOffset + HeaderSize > document.Length)
        {
            throw new OfficeFormatException(DefectKind.Encryption, "encryption.malformed-header", "The CryptSession10Container is outside the document stream.", at);
        }

        long length = BinaryPrimitives.ReadUInt32LittleEndian(document[(int)(cryptOffset + 4)..]);
        if (cryptOffset + HeaderSize + length > document.Length)
        {
            throw new OfficeFormatException(DefectKind.Encryption, "encryption.malformed-header", "The CryptSession10Container runs past the end of the document stream.", at);
        }

        BinaryRc4Encryption enc = BinaryRc4Encryption.Parse(document.Slice((int)(cryptOffset + HeaderSize), (int)length), at);
        string?[] candidates = context.Password is null ? [DefaultPassword] : [context.Password];
        bool unlocked = false;
        foreach (string? candidate in candidates)
        {
            if (enc.TryUnlock(candidate!))
            {
                unlocked = true;
                break;
            }
        }

        if (!unlocked)
        {
            throw new OfficeEncryptedException(context.Password is null ? EncryptionProblem.PasswordRequired : EncryptionProblem.PasswordIncorrect, at);
        }

        byte[] plain = document.ToArray();
        foreach (KeyValuePair<uint, long> entry in persistOffsets)
        {
            long offset = entry.Value;
            if (offset == cryptOffset)
            {
                continue;
            }

            if (offset < 0 || offset + HeaderSize > plain.Length)
            {
                continue;
            }

            // [MS-PPT] §2.3.7: the object is 8 bytes of header plus recLen, all encrypted with the block key of its persist identifier.
            Rc4Cipher cipher = enc.CreateCipher(entry.Key);
            cipher.Process(plain.AsSpan((int)offset, HeaderSize));
            long recLen = BinaryPrimitives.ReadUInt32LittleEndian(plain.AsSpan((int)(offset + 4)));
            if (offset + HeaderSize + recLen > plain.Length)
            {
                context.Defects.Report(new Defect(DefectKind.Record, "ppt.persist-object-range", DefectSeverity.Dropped,
                    $"Persist object {entry.Key} claims {recLen} bytes after decryption, more than the stream holds.", DefectLocation.InRecord(PptFile.DocumentStreamName, offset)));
                continue;
            }

            cipher.Process(plain.AsSpan((int)(offset + HeaderSize), (int)recLen));
        }

        return (plain, DecryptPictures(pictures, enc), enc);
    }

    // [MS-PPT] §2.3.7: every field of each blip record in the Pictures stream is decrypted with the block 0 key started afresh at the field.
    // The fields are the record header, one or two UIDs, then a one-byte tag (bitmaps) or the 34-byte metafile header, then the picture data
    // ([MS-ODRAW] §2.2.23 to §2.2.31). Every pair of instances for one blip type differs in the low bit, which says whether a second UID follows.
    private static byte[] DecryptPictures(ReadOnlySpan<byte> pictures, BinaryRc4Encryption enc)
    {
        byte[] plain = pictures.ToArray();
        long pos = 0;
        while (pos + HeaderSize <= plain.Length)
        {
            enc.CreateCipher(0).Process(plain.AsSpan((int)pos, HeaderSize));
            int type = BinaryPrimitives.ReadUInt16LittleEndian(plain.AsSpan((int)pos + 2));
            int instance = BinaryPrimitives.ReadUInt16LittleEndian(plain.AsSpan((int)pos)) >> 4;
            long recLen = BinaryPrimitives.ReadUInt32LittleEndian(plain.AsSpan((int)(pos + 4)));
            if (pos + HeaderSize + recLen > plain.Length)
            {
                break;
            }

            int at = (int)pos + HeaderSize;
            int end = at + (int)recLen;
            int uids = (instance & 1) != 0 ? 2 : 1;
            int fixedFields = type is 0xF01A or 0xF01B or 0xF01C ? 34 : 1;
            foreach (int size in new[] { 16, uids == 2 ? 16 : 0, fixedFields, int.MaxValue })
            {
                int n = Math.Min(size, end - at);
                if (n > 0)
                {
                    enc.CreateCipher(0).Process(plain.AsSpan(at, n));
                    at += n;
                }
            }

            pos = end;
        }

        return plain;
    }
}
