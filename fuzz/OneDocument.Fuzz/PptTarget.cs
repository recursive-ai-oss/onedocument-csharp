using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using RecursiveAI.OneDocument.Cfb;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Ppt.Binary;
using RecursiveAI.OneDocument.Presentations;

namespace RecursiveAI.OneDocument.Fuzz;

/// <summary>
/// The PowerPoint 97-2003 reader over arbitrary bytes. The input is a 4-byte offset for the Current User stream followed by the
/// PowerPoint Document stream. The file is wrapped in a compound file, read at record level, converted to PresentationML and opened
/// through the hub. Only <see cref="OfficeException"/> may come out.
/// </summary>
internal static class PptTarget
{
    private static readonly OfficeLimits s_limits = new() { MaxXmlDepth = 64, MaxPartSize = 4 << 20, MaxRecursionDepth = 32, MaxRecordCount = 200_000, MaxAllocationBytes = 64 << 20 };

    public static void Run(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12)
        {
            return;
        }

        uint editOffset = BinaryPrimitives.ReadUInt32LittleEndian(data);
        byte[] stream = data[4..].ToArray();
        byte[] currentUser = new byte[8 + 12 + 8];
        BinaryPrimitives.WriteUInt16LittleEndian(currentUser.AsSpan(2), 0x0FF6);
        BinaryPrimitives.WriteUInt32LittleEndian(currentUser.AsSpan(4), 20);
        BinaryPrimitives.WriteUInt32LittleEndian(currentUser.AsSpan(8), 0x14);
        BinaryPrimitives.WriteUInt32LittleEndian(currentUser.AsSpan(12), 0xE391C05F);
        BinaryPrimitives.WriteUInt32LittleEndian(currentUser.AsSpan(16), editOffset);
        try
        {
            using var cfb = CfbPackage.Create(s_limits);
            cfb.AddPart("/PowerPoint Document", "", stream);
            cfb.AddPart("/Current User", "", currentUser);
            using var ms = new MemoryStream();
            cfb.WriteTo(ms);
            var context = new OpenContext(s_limits);
            PptFile file = PptFile.Open(ByteSource.FromMemory(ms.ToArray()), context);
            using OpcPackage package = PptConverter.Convert(file, new LossReport());
            using Presentation p = Presentation.FromPackage(package, context);
            foreach (Slide s in p.Slides)
            {
                _ = s.Text;
            }
        }
        catch (OfficeException)
        {
        }
    }

    public static int WriteSeeds(string directory)
    {
        Directory.CreateDirectory(directory);
        string fixtures = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(FilePath())!, "..", "..", "testdata", "ppt"));
        int n = 0;
        foreach (string file in Directory.EnumerateFiles(fixtures, "*.ppt").OrderBy(f => f, StringComparer.Ordinal))
        {
            using var cfb = CfbPackage.Open(file);
            byte[] stream = cfb.FindPart("/PowerPoint Document")!.GetBytes().ToArray();
            byte[] user = cfb.FindPart("/Current User")!.GetBytes().ToArray();
            var seed = new byte[4 + stream.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(seed, BinaryPrimitives.ReadUInt32LittleEndian(user.AsSpan(16)));
            stream.CopyTo(seed, 4);
            File.WriteAllBytes(Path.Combine(directory, "ppt-" + n++ + ".bin"), seed);
        }

        return 0;
    }

    private static string FilePath([CallerFilePath] string path = "") => path;
}
