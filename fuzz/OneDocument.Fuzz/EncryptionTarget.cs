using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using RecursiveAI.OneDocument.Cfb;
using RecursiveAI.OneDocument.Encryption;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Spreadsheets;
using RecursiveAI.OneDocument.Spreadsheets.Biff;

namespace RecursiveAI.OneDocument.Fuzz;

/// <summary>
/// The encryption headers. <c>encinfo</c> feeds bytes to the <c>EncryptionInfo</c> parsers (standard, agile, extensible and the binary
/// RC4 headers of Word and PowerPoint) and then tries a password against them. <c>filepass</c> puts the bytes into a <c>FilePass</c>
/// record of a small BIFF stream and reads it as a workbook. Only <see cref="OfficeException"/> may escape.
/// </summary>
internal static class EncryptionTarget
{
    // A spin count in the hundreds keeps each execution fast; the limit itself is what is under test.
    private static readonly OfficeLimits Limits = new() { MaxEncryptionSpinCount = 300, MaxAllocationBytes = 1 << 24, MaxPartSize = 8 << 20, MaxDefects = 100 };

    private static readonly byte[] Package = new byte[8 + 64];

    public static void RunInfo(ReadOnlySpan<byte> data)
    {
        try
        {
            _ = PackageEncryption.Describe(data, new OpenContext(Limits));
        }
        catch (OfficeException)
        {
        }

        try
        {
            _ = PackageEncryption.Decrypt(data, Package, new OpenContext(Limits, password: "a"));
        }
        catch (OfficeException)
        {
        }

        try
        {
            BinaryRc4Encryption h = BinaryRc4Encryption.Parse(data, default);
            if (h.TryUnlock("a"))
            {
                h.CreateCipher(1).Process(new byte[16]);
            }
        }
        catch (OfficeException)
        {
        }
    }

    public static void RunFilePass(ReadOnlySpan<byte> data)
    {
        int len = Math.Min(data.Length, 8000);
        using var ms = new MemoryStream();
        Record(ms, 0x0809, new byte[16]);
        Record(ms, 0x002F, data[..len].ToArray());
        Record(ms, 0x0042, [0xE4, 0x04]);
        Record(ms, 0x0085, new byte[20]);
        Record(ms, 0x000A, []);
        try
        {
            using Workbook wb = XlsConverter.ToWorkbook(ByteSource.FromMemory(ms.ToArray()), new OpenContext(Limits, password: "a"));
        }
        catch (OfficeException)
        {
        }
    }

    private static void Record(Stream s, int type, byte[] body)
    {
        s.WriteByte((byte)type);
        s.WriteByte((byte)(type >> 8));
        s.WriteByte((byte)body.Length);
        s.WriteByte((byte)(body.Length >> 8));
        s.Write(body);
    }

    public static int WriteSeeds(string dir)
    {
        Directory.CreateDirectory(dir);
        string data = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(ThisFile())))!, "testdata", "encryption");
        int n = 0;
        if (!Directory.Exists(data))
        {
            return 0;
        }


        foreach (string f in Directory.GetFiles(data, "secret-*"))
        {
            byte[] file = File.ReadAllBytes(f);
            if (file.Length < 8 || file[0] != 0xD0)
            {
                continue;
            }

            using CfbPackage cfb = CfbPackage.Open(ByteSource.FromMemory(file));
            if (cfb.FindPart("/EncryptionInfo") is { } info)
            {
                byte[] bytes = info.GetBytes().ToArray();
                File.WriteAllBytes(Path.Combine(dir, Path.GetFileName(f) + ".info"), bytes);

                // The agile descriptors ask for 100,000 iterations, more than this target's limit; a copy with fewer keeps the verifier path in reach.
                string text = System.Text.Encoding.Latin1.GetString(bytes);
                if (text.Contains("spinCount=\"100000\"", StringComparison.Ordinal))
                {
                    File.WriteAllBytes(Path.Combine(dir, Path.GetFileName(f) + ".spin.info"), System.Text.Encoding.Latin1.GetBytes(text.Replace("spinCount=\"100000\"", "spinCount=\"000100\"", StringComparison.Ordinal)));
                }

                n++;
            }
            else if ((cfb.FindPart("/Workbook") ?? cfb.FindPart("/Book")) is { } wb)
            {
                byte[] stream = wb.GetBytes().ToArray();
                if (XlsEncryption.TryFindFilePass(stream, out int at))
                {
                    int len = stream[at + 2] | (stream[at + 3] << 8);
                    File.WriteAllBytes(Path.Combine(dir, Path.GetFileName(f) + ".filepass"), stream.AsSpan(at + 4, len).ToArray());
                    n++;
                }
            }
        }

        byte[] header = BinaryRc4Encryption.Create("a", new byte[16], new byte[16], 128).ToBytes();
        File.WriteAllBytes(Path.Combine(dir, "cryptoapi.header"), header);
        byte[] filepass = new byte[2 + header.Length];
        filepass[0] = 1;
        header.CopyTo(filepass, 2);
        File.WriteAllBytes(Path.Combine(dir, "cryptoapi.filepass"), filepass);
        File.WriteAllBytes(Path.Combine(dir, "xor.filepass"), [0, 0, 0x12, 0x34, 0x56, 0x78]);
        return n >= 0 ? 0 : 1;
    }

    private static string ThisFile([CallerFilePath] string path = "") => path;
}
