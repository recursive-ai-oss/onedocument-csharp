using System.Buffers.Binary;
using RecursiveAI.OneDocument.Cfb;
using RecursiveAI.OneDocument.Encryption;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Ppt.Binary;

namespace RecursiveAI.OneDocument.Presentations.Tests;

/// <summary>
/// Password-protects a PowerPoint 97-2003 file the way [MS-PPT] §2.3.7 describes (RC4 CryptoAPI), so the reader can be tested on a scheme
/// the black-box converters do not write. Test-only; checked against LibreOffice reading its output (tools/Encryption/README.md).
/// </summary>
internal static class PptEncryptor
{
    internal static byte[] Encrypt(byte[] plain, string password)
    {
        PptFile file = PptFile.Open(ByteSource.FromMemory(plain), new OpenContext());
        PptUserEdit newest = file.UserEdits[0];
        byte[] doc = file.DocumentStream.ToArray();
        byte[] pictures = file.PicturesStream.ToArray();
        var persist = file.PersistOffsets.ToDictionary(e => e.Key, e => e.Value);

        byte[] salt = Enumerable.Range(1, 16).Select(i => (byte)(i * 11)).ToArray();
        byte[] verifier = Enumerable.Range(1, 16).Select(i => (byte)(i * 5)).ToArray();
        BinaryRc4Encryption enc = BinaryRc4Encryption.Create(password, salt, verifier, 128);

        // Encrypt every persist object (header and body) with the block key of its persist identifier.
        foreach ((uint id, long offset) in persist)
        {
            Rc4Cipher c = enc.CreateCipher(id);
            c.Process(doc.AsSpan((int)offset, 8));
            // The header is encrypted now; its length is the plain one, which was read before.
            c = enc.CreateCipher(id);
            int len = (int)BinaryPrimitives.ReadUInt32LittleEndian(file.DocumentStream.Span[(int)(offset + 4)..]);
            c.Skip(8);
            c.Process(doc.AsSpan((int)offset + 8, len));
        }

        // New tail: the CryptSession10Container, a persist directory that names it, and a user edit that points at both.
        uint cryptId = newest.PersistIdSeed + 1;
        byte[] header = enc.ToBytes();
        using var tail = new MemoryStream();
        long cryptOffset = doc.Length;
        Write(tail, Record(0x2F14, header, version: 0xF));
        long dirOffset = doc.Length + tail.Length;

        // [MS-PPT] §2.3.7: an encrypted file has exactly one user edit, whose directory names every persist object.
        persist[cryptId] = cryptOffset;
        byte[] dir = new byte[persist.Count * 8];
        int at = 0;
        foreach ((uint id, long offset) in persist.OrderBy(e => e.Key))
        {
            BinaryPrimitives.WriteUInt32LittleEndian(dir.AsSpan(at), id | (1u << 20));
            BinaryPrimitives.WriteUInt32LittleEndian(dir.AsSpan(at + 4), (uint)offset);
            at += 8;
        }

        Write(tail, Record(0x1772, dir));
        long editOffset = doc.Length + tail.Length;
        byte[] edit = new byte[0x20];
        BinaryPrimitives.WriteUInt32LittleEndian(edit, newest.LastSlideIdRef);
        file.DocumentStream.Span.Slice((int)newest.Offset + 8 + 4, 4).CopyTo(edit.AsSpan(4)); // the version fields stay as the file had them
        BinaryPrimitives.WriteUInt32LittleEndian(edit.AsSpan(8), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(edit.AsSpan(12), (uint)dirOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(edit.AsSpan(16), newest.DocumentPersistId);
        BinaryPrimitives.WriteUInt32LittleEndian(edit.AsSpan(20), cryptId + 1);
        BinaryPrimitives.WriteUInt32LittleEndian(edit.AsSpan(28), cryptId);
        Write(tail, Record(0x0FF5, edit));
        byte[] newDoc = new byte[doc.Length + tail.Length];
        doc.CopyTo(newDoc, 0);
        tail.ToArray().CopyTo(newDoc, doc.Length);

        // Pictures: every field of each blip record (header, UIDs, tag or metafile header, data) with the block 0 key started at the field.
        long pos = 0;
        while (pos + 8 <= pictures.Length)
        {
            int type = BinaryPrimitives.ReadUInt16LittleEndian(pictures.AsSpan((int)pos + 2));
            int instance = BinaryPrimitives.ReadUInt16LittleEndian(pictures.AsSpan((int)pos)) >> 4;
            int len = (int)BinaryPrimitives.ReadUInt32LittleEndian(pictures.AsSpan((int)pos + 4));
            if (pos + 8 + len > pictures.Length)
            {
                break;
            }

            int cursor = (int)pos;
            int end = cursor + 8 + len;
            foreach (int size in new[] { 8, 16, (instance & 1) != 0 ? 16 : 0, type is 0xF01A or 0xF01B or 0xF01C ? 34 : 1, int.MaxValue })
            {
                int n = Math.Min(size, end - cursor);
                if (n > 0)
                {
                    enc.CreateCipher(0).Process(pictures.AsSpan(cursor, n));
                    cursor += n;
                }
            }

            pos = end;
        }

        using CfbPackage cfb = CfbPackage.Open(ByteSource.FromMemory(plain));
        cfb.FindPart("/PowerPoint Document")!.SetBytes(newDoc);
        if (cfb.FindPart("/Pictures") is { } p)
        {
            p.SetBytes(pictures);
        }

        byte[] currentUser = cfb.FindPart("/Current User")!.GetBytes().ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(currentUser.AsSpan(12), 0xF3D1C4DF);
        BinaryPrimitives.WriteUInt32LittleEndian(currentUser.AsSpan(16), (uint)editOffset);
        cfb.FindPart("/Current User")!.SetBytes(currentUser);
        using var ms = new MemoryStream();
        cfb.WriteTo(ms);
        return ms.ToArray();
    }

    private static byte[] Record(int type, byte[] data, int version = 0)
    {
        byte[] r = new byte[8 + data.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(r, (ushort)version);
        BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(2), (ushort)type);
        BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(4), (uint)data.Length);
        data.CopyTo(r, 8);
        return r;
    }

    private static void Write(MemoryStream s, byte[] b) => s.Write(b);
}
