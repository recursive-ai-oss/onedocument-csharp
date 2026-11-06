using System;
using System.IO;
using RecursiveAI.OneDocument.Cfb;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Doc.Binary;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Documents.Tests;
using RecursiveAI.OneDocument.IO;

namespace RecursiveAI.OneDocument.Fuzz;

/// <summary>
/// The Word binary readers over arbitrary bytes. The input is a 24-bit length followed by the WordDocument stream and then the table
/// stream; the Data stream repeats the table stream, so picture and huge-paragraph paths get input too. The streams go into a
/// compound file and through the text extractor and the conversion into the document model. Only <see cref="OfficeException"/> may
/// come out, and the reader's last-resort code (<c>doc.malformed</c>) counts as a finding: it means a check is missing.
/// </summary>
internal static class DocTarget
{
    private static readonly OfficeLimits s_limits = new() { MaxXmlDepth = 64, MaxPartSize = 1 << 21, MaxRecursionDepth = 32, MaxAllocationBytes = 1 << 26 };

    public static void Run(ReadOnlySpan<byte> data)
    {
        if (data.Length < 3)
        {
            return;
        }

        int wl = Math.Min(data[0] | (data[1] << 8) | (data[2] << 16), data.Length - 3);
        byte[] word = data.Slice(3, wl).ToArray();
        byte[] table = data[(3 + wl)..].ToArray();
        using CfbPackage cfb = CfbPackage.Create(s_limits);
        cfb.AddPart("/WordDocument", string.Empty, word);
        cfb.AddPart("/1Table", string.Empty, table);
        cfb.AddPart("/0Table", string.Empty, table);
        cfb.AddPart("/Data", string.Empty, table);
        using var ms = new MemoryStream();
        cfb.WriteTo(ms);
        byte[] file = ms.ToArray();
        try
        {
            using IByteSource src = ByteSource.FromMemory(file);
            using var sw = new StringWriter();
            DocTextExtractor.Extract(DocFile.Open(src, new OpenContext(s_limits)), sw);
        }
        catch (OfficeException ex)
        {
            Check(ex);
        }

        try
        {
            using Document d = Document.Open(new MemoryStream(file), new OpenContext(s_limits), leaveOpen: false);
            _ = d.ExtractText();
            using var outp = new MemoryStream();
            d.SaveAs(outp, OfficeFormat.Docx);
        }
        catch (OfficeException ex)
        {
            Check(ex);
        }
    }

    private static void Check(OfficeException ex)
    {
        if (ex.Defect.Code == "doc.malformed")
        {
            throw new InvalidOperationException("The Word binary reader fell back to doc.malformed.", ex);
        }
    }

    /// <summary>Property modifier lists: the splitter must accept any bytes.</summary>
    public static void RunSprm(ReadOnlySpan<byte> data)
    {
        foreach (Prl p in SprmReader.Read(data.ToArray(), out _))
        {
            _ = p.Name;
            _ = p.Sgc;
            _ = p.OperandI32;
        }
    }

    public static int WriteSeeds(string dir)
    {
        Directory.CreateDirectory(dir);
        foreach ((string name, byte[] bytes) in Seeds())
        {
            File.WriteAllBytes(Path.Combine(dir, name), bytes);
        }

        return 0;
    }

    public static int WriteSprmSeeds(string dir)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "char.bin"), DocBuilder.Cat(DocBuilder.Sprm(0x0835, 1), DocBuilder.Sprm(0x4A43, 24, 0), DocBuilder.Sprm(0xD608, 8, 0, 2, 0, 0, 0xD0, 0x07, 0xA0, 0x0F)));
        return 0;
    }

    private static System.Collections.Generic.IEnumerable<(string, byte[])> Seeds()
    {
        var text = new DocBuilder().Piece("Hello world\r");
        yield return ("text.bin", Split(text.Build()));
        var fmt = new DocBuilder().Piece("plain bold \u0013 PAGE \u00141\u0015 end\r");
        fmt.CharRuns.Add(new DocBuilder.Run(6, 10, DocBuilder.Sprm(0x0835, 1)));
        fmt.ParaRuns.Add(new DocBuilder.ParaProps(31, 0, DocBuilder.Sprm(0x2461, 1)));
        yield return ("format.bin", Split(fmt.Build()));
        var tbl = new DocBuilder().Piece("a\ab\a\a");
        byte[] inTable = DocBuilder.Sprm(0x2416, 1);
        tbl.ParaRuns.Add(new DocBuilder.ParaProps(1, 0, inTable));
        tbl.ParaRuns.Add(new DocBuilder.ParaProps(3, 0, inTable));
        tbl.ParaRuns.Add(new DocBuilder.ParaProps(4, 0, DocBuilder.Cat(inTable, DocBuilder.Sprm(0x2417, 1), DocBuilder.Sprm(0xD608, 8, 0, 2, 0, 0, 0xD0, 0x07, 0xA0, 0x0F))));
        yield return ("table.bin", Split(tbl.Build()));
        var fast = new DocBuilder { ReversePieces = true };
        fast.Piece("One\r").Piece("Two\r", eightBit: false);
        yield return ("fast.bin", Split(fast.Build()));
    }

    // The target wants the two streams, not the compound file: pull them back out.
    private static byte[] Split(byte[] cfbBytes)
    {
        using CfbPackage cfb = CfbPackage.Open(new MemoryStream(cfbBytes));
        byte[] word = cfb.FindPart("/WordDocument")!.GetBytes().ToArray();
        byte[] table = cfb.FindPart("/1Table")!.GetBytes().ToArray();
        byte[] result = new byte[3 + word.Length + table.Length];
        result[0] = (byte)word.Length;
        result[1] = (byte)(word.Length >> 8);
        result[2] = (byte)(word.Length >> 16);
        word.CopyTo(result, 3);
        table.CopyTo(result, 3 + word.Length);
        return result;
    }
}
