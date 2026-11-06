using System.Buffers.Binary;
using RecursiveAI.OneDocument.Cfb;
using RecursiveAI.OneDocument.Encryption;
using RecursiveAI.OneDocument.IO;

namespace RecursiveAI.OneDocument.Documents.Tests;

/// <summary>
/// Password-protects a Word 97-2003 file the way [MS-DOC] §2.2.6 describes, so the reader can be tested on schemes the black-box
/// converters do not write (RC4 CryptoAPI, XOR obfuscation). Test-only: it follows the specification, and is checked against
/// LibreOffice reading its output (tools/Encryption/README.md).
/// </summary>
internal static class DocEncryptor
{
    internal static byte[] CryptoApi(byte[] plain, string password)
    {
        using CfbPackage cfb = CfbPackage.Open(ByteSource.FromMemory(plain));
        byte[] word = cfb.FindPart("/WordDocument")!.GetBytes().ToArray();
        bool oneTable = (BinaryPrimitives.ReadUInt16LittleEndian(word.AsSpan(10)) & 0x0200) != 0;
        string tablePath = oneTable ? "/1Table" : "/0Table";
        byte[] table = cfb.FindPart(tablePath)!.GetBytes().ToArray();
        byte[] data = cfb.FindPart("/Data")?.GetBytes().ToArray() ?? [];

        byte[] salt = Enumerable.Range(1, 16).Select(i => (byte)(i * 7)).ToArray();
        byte[] verifier = Enumerable.Range(1, 16).Select(i => (byte)(i * 13)).ToArray();
        BinaryRc4Encryption enc = BinaryRc4Encryption.Create(password, salt, verifier, 128);
        byte[] header = enc.ToBytes();

        // The header takes the first bytes of the table stream, so every table offset in the FIB moves by its size.
        ShiftTableOffsets(word, header.Length);
        byte[] newTable = new byte[header.Length + table.Length];
        header.CopyTo(newTable, 0);
        table.CopyTo(newTable, header.Length);

        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(word.AsSpan(10));
        BinaryPrimitives.WriteUInt16LittleEndian(word.AsSpan(10), (ushort)(flags | 0x0100));
        BinaryPrimitives.WriteUInt32LittleEndian(word.AsSpan(14), (uint)header.Length);

        var w = new Rc4BlockCipher(enc, 512);
        w.Skip(68);
        w.Process(word.AsSpan(68));
        var t = new Rc4BlockCipher(enc, 512);
        t.Skip(header.Length);
        t.Process(newTable.AsSpan(header.Length));
        new Rc4BlockCipher(enc, 512).Process(data);
        return Save(cfb, tablePath, word, newTable, data);
    }

    internal static byte[] Xor(byte[] plain, string password)
    {
        using CfbPackage cfb = CfbPackage.Open(ByteSource.FromMemory(plain));
        byte[] word = cfb.FindPart("/WordDocument")!.GetBytes().ToArray();
        bool oneTable = (BinaryPrimitives.ReadUInt16LittleEndian(word.AsSpan(10)) & 0x0200) != 0;
        string tablePath = oneTable ? "/1Table" : "/0Table";
        byte[] table = cfb.FindPart(tablePath)!.GetBytes().ToArray();
        byte[] data = cfb.FindPart("/Data")?.GetBytes().ToArray() ?? [];

        XorObfuscation xor = XorObfuscation.CreateMethod2(password, out uint verifier);
        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(word.AsSpan(10));
        BinaryPrimitives.WriteUInt16LittleEndian(word.AsSpan(10), (ushort)(flags | 0x0100 | 0x8000));
        BinaryPrimitives.WriteUInt32LittleEndian(word.AsSpan(14), verifier);
        xor.TransformMethod2(word.AsSpan(68), 68);
        xor.TransformMethod2(table, 0);
        xor.TransformMethod2(data, 0);
        return Save(cfb, tablePath, word, table, data);
    }

    // [MS-DOC] §2.5.15: FibRgFcLcb97 and the later arrays are pairs of (fc, lcb); a pair with a zero size points at nothing.
    private static void ShiftTableOffsets(byte[] word, int by)
    {
        int pos = 32;
        int csw = BinaryPrimitives.ReadUInt16LittleEndian(word.AsSpan(pos));
        pos += 2 + (csw * 2);
        int cslw = BinaryPrimitives.ReadUInt16LittleEndian(word.AsSpan(pos));
        pos += 2 + (cslw * 4);
        int pairs = BinaryPrimitives.ReadUInt16LittleEndian(word.AsSpan(pos));
        pos += 2;
        for (int i = 0; i < pairs; i++)
        {
            int at = pos + (i * 8);
            if (BinaryPrimitives.ReadUInt32LittleEndian(word.AsSpan(at + 4)) != 0)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(word.AsSpan(at), BinaryPrimitives.ReadUInt32LittleEndian(word.AsSpan(at)) + (uint)by);
            }
        }
    }

    private static byte[] Save(CfbPackage cfb, string tablePath, byte[] word, byte[] table, byte[] data)
    {
        cfb.FindPart("/WordDocument")!.SetBytes(word);
        cfb.FindPart(tablePath)!.SetBytes(table);
        if (cfb.FindPart("/Data") is { } d)
        {
            d.SetBytes(data);
        }

        using var ms = new MemoryStream();
        cfb.WriteTo(ms);
        return ms.ToArray();
    }
}
