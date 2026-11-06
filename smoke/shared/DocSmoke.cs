using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Documents.Tests;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.IO;

namespace RecursiveAI.OneDocument.Smoke;

/// <summary>Reads a small generated Word binary file: text extraction, the conversion into the model (character formatting and a table) and the loss report, under NativeAOT and WASM.</summary>
internal static class DocSmoke
{
    public static List<string> Run()
    {
        var failures = new List<string>();
        DocumentFormats.Register(FormatRegistry.Default);
        var b = new DocBuilder().Piece("plain bold \u0013 PAGE \u00141\u0015\r").Piece("a\ab\a\a", eightBit: false);
        b.CharRuns.Add(new DocBuilder.Run(6, 10, DocBuilder.Sprm(0x0835, 1)));
        byte[] inTable = DocBuilder.Sprm(0x2416, 1);
        int baseCp = "plain bold \u0013 PAGE \u00141\u0015\r".Length;
        b.ParaRuns.Add(new DocBuilder.ParaProps(baseCp - 1, 0, []));
        b.ParaRuns.Add(new DocBuilder.ParaProps(baseCp + 1, 0, inTable));
        b.ParaRuns.Add(new DocBuilder.ParaProps(baseCp + 3, 0, inTable));
        b.ParaRuns.Add(new DocBuilder.ParaProps(baseCp + 4, 0, DocBuilder.Cat(inTable, DocBuilder.Sprm(0x2417, 1), DocBuilder.Sprm(0xD608, 8, 0, 2, 0, 0, 0xD0, 0x07, 0xA0, 0x0F))));
        byte[] file = b.Build();

        using (IByteSource src = ByteSource.FromMemory(file))
        {
            using var w = new StringWriter();
            Office.ExtractText(src, w);
            if (w.ToString() != "plain bold 1\na\tb\n") failures.Add("doc text: " + w.ToString().Replace('\n', '|'));
        }

        using Document d = Document.Open(new MemoryStream(file));
        if (d.SourceFormat != OfficeFormat.Doc) failures.Add("doc source format");
        string xml = d.MainMarkup.Root.OuterXml;
        if (!xml.Contains("<w:b/>", StringComparison.Ordinal)) failures.Add("doc bold");
        if (!xml.Contains("<w:tbl>", StringComparison.Ordinal)) failures.Add("doc table");
        using var docx = new MemoryStream();
        d.SaveAs(docx, OfficeFormat.Docx);
        using Document again = Document.Open(new MemoryStream(docx.ToArray()));
        if (again.Paragraphs.First().Text != "plain bold 1") failures.Add("doc docx round trip");
        return failures;
    }
}
