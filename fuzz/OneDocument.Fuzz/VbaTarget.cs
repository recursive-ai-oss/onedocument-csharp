using System;
using System.IO;
using RecursiveAI.OneDocument.Cfb;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Tests;
using RecursiveAI.OneDocument.Vba;

namespace RecursiveAI.OneDocument.Fuzz;

/// <summary>
/// The VBA project readers and removers over arbitrary bytes. Only <see cref="OfficeException"/> may come out; the decompressor never throws at all.
/// </summary>
internal static class VbaTarget
{
    private static readonly OfficeLimits s_limits = new() { MaxPartSize = 4 << 20, MaxRecordCount = 100_000, MaxAllocationBytes = 64 << 20, MaxRecursionDepth = 32 };

    /// <summary>The decompressor: any bytes decode or say why not; a literal container of the input decodes back to the input.</summary>
    public static void RunDecompress(ReadOnlySpan<byte> data)
    {
        bool ok = VbaCompression.TryDecompress(data, 1 << 22, out byte[] output, out string? problem);
        if (ok == (problem is not null) || output.Length > 1 << 22)
        {
            throw new InvalidOperationException("the decompressor's answer and its problem disagree");
        }

        byte[] literal = VbaTestProject.Compress(data);
        if (!VbaCompression.TryDecompress(literal, 1 << 22, out byte[] back, out _) || !back.AsSpan().SequenceEqual(data))
        {
            throw new InvalidOperationException("a literal container did not decode to its input");
        }
    }

    /// <summary>The dir stream parser and the module readers: the input is the decompressed dir stream, and its tail also serves as a module's source and as the PROJECT stream.</summary>
    public static void RunDir(ReadOnlySpan<byte> data)
    {
        try
        {
            var cfb = CfbPackage.Create(s_limits);
            cfb.AddPart("/VBA/dir", string.Empty, VbaTestProject.Compress(data));
            cfb.AddPart("/VBA/_VBA_PROJECT", string.Empty, new byte[] { 0xCC, 0x61, 0xB3, 0x00, 0, 0, 0, 1, 2, 3 });
            foreach (string name in new[] { "Module1", "ThisWorkbook", "Class1", "UserForm1", "Sheet1" })
            {
                cfb.AddPart("/VBA/" + name, string.Empty, VbaTestProject.Compress(data[Math.Min(data.Length, name.Length * 3)..]));
            }

            cfb.AddPart("/PROJECT", string.Empty, data.ToArray());
            using var ms = new MemoryStream();
            cfb.WriteTo(ms);
            Read(ms.ToArray());
        }
        catch (OfficeException)
        {
        }
    }

    /// <summary>Raw bytes as a vbaProject.bin.</summary>
    public static void RunProject(ReadOnlySpan<byte> data)
    {
        try
        {
            Read(data.ToArray());
        }
        catch (OfficeException)
        {
        }
    }

    private static void Read(byte[] compoundFile)
    {
        VbaProject p = VbaProject.FromProjectStorage(compoundFile, new OpenContext(s_limits));
        foreach (VbaModule m in p.Modules)
        {
            _ = m.Source;
            _ = m.ExportText();
            _ = m.FileName;
        }
    }

    /// <summary>The removers: the first byte picks the layout (0: a workbook stream with a project storage, 1: a PowerPoint document stream, 2: a Word file), the rest is the stream.</summary>
    public static void RunRemove(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2)
        {
            return;
        }

        try
        {
            var cfb = CfbPackage.Create(s_limits);
            byte[] project = VbaTestProject.Typical().Build();
            string root = (data[0] % 3) switch { 0 => "/_VBA_PROJECT_CUR", 1 => string.Empty, _ => "/Macros" };
            switch (data[0] % 3)
            {
                case 0:
                    cfb.AddPart("/Workbook", string.Empty, data[1..].ToArray());
                    break;
                case 1:
                    cfb.AddPart("/PowerPoint Document", string.Empty, data[1..].ToArray());
                    cfb.AddPart("/Current User", string.Empty, new byte[] { 0, 0, 0xF6, 0x0F, 20, 0, 0, 0, 20, 0, 0, 0, 0x5F, 0xC0, 0x91, 0xE3, 0, 0, 0, 0 });
                    break;
                default:
                    cfb.AddPart("/WordDocument", string.Empty, data[1..].ToArray());
                    break;
            }

            if (data[0] % 3 != 1)
            {
                using CfbPackage inner = CfbPackage.Open(ByteSource.FromMemory(project));
                foreach (CfbStreamPart s in inner.Streams)
                {
                    cfb.AddPart(root + s.Name, string.Empty, s.GetBytes());
                }
            }

            using var ms = new MemoryStream();
            cfb.WriteTo(ms);
            using var output = new MemoryStream();
            VbaRemoval.Remove(new MemoryStream(ms.ToArray()), output, new OpenContext(s_limits));
            using CfbPackage back = CfbPackage.Open(ByteSource.FromMemory(output.ToArray()), new OpenContext(s_limits));
            if (back.Streams.Count == 0)
            {
                throw new InvalidOperationException("the removal produced a file with no streams");
            }
        }
        catch (OfficeException)
        {
        }
    }

    public static int WriteSeeds(string directory)
    {
        Directory.CreateDirectory(directory);
        VbaTestProject t = VbaTestProject.Typical();
        File.WriteAllBytes(Path.Combine(directory, "project.bin"), t.Build());
        File.WriteAllBytes(Path.Combine(directory, "dir.bin"), t.BuildDir(System.Text.Encoding.GetEncoding(1252)));
        File.WriteAllBytes(Path.Combine(directory, "compressed-normal.bin"), Convert.FromHexString("012FB000236161616263646582660070616768696A013808616B6C00206D6E6F700671027004007273747576107778797A002C".Replace(" ", string.Empty, StringComparison.Ordinal)));
        File.WriteAllBytes(Path.Combine(directory, "compressed-max.bin"), Convert.FromHexString("0103B002614500"));
        byte[] workbook = [0x09, 0x08, 0x10, 0x00, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xD3, 0x00, 0x00, 0x00, 0x0A, 0x00, 0x00, 0x00];
        File.WriteAllBytes(Path.Combine(directory, "remove-xls.bin"), [0, .. workbook]);
        return 0;
    }
}
