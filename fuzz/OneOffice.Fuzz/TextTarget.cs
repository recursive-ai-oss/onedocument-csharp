using System;
using System.IO;
using System.Text;
using RecursiveAI.OneOffice.Markup;
using RecursiveAI.OneOffice.TextExtraction;

namespace RecursiveAI.OneOffice.Fuzz;

/// <summary>
/// The OOXML text extractors. The first input byte picks the extractor (word-processing, drawing, shared strings plus a worksheet) and the
/// rest is the part. Only a typed rejection may come out; accepted parts are extracted twice and the two results must be identical
/// (extraction is deterministic and keeps no state between parts).
/// </summary>
internal static class TextTarget
{
    private const string Sst = "<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><si><t>a</t></si><si><r><t>b</t></r></si></sst>";

    public static void Run(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
        {
            return;
        }

        int mode = data[0] & 3;
        byte[] part = data[1..].ToArray();
        string first = Extract(mode, part, deleted: false);
        string second = Extract(mode, part, deleted: (data[0] & 4) != 0);
        if ((data[0] & 4) == 0 && first != second)
        {
            throw new InvalidOperationException("extraction is not deterministic");
        }
    }

    private static string Extract(int mode, byte[] part, bool deleted)
    {
        var writer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        var sink = new PlainTextSink(writer);
        var context = new OpenContext();
        var options = new TextExtractionOptions { IncludeDeletedText = deleted };
        try
        {
            using var reader = new MarkupReader(part, context, "/fuzz.xml");
            switch (mode)
            {
                case 0:
                    new WordTextExtractor(context, options, sink, default).Run(reader);
                    break;
                case 1:
                    new DrawingTextExtractor(context, sink, default).Run(reader);
                    break;
                case 2:
                    SharedStrings.Read(reader, context, default);
                    break;
                default:
                    using (var sst = new MarkupReader(Encoding.UTF8.GetBytes(Sst), context, "/sst.xml"))
                    {
                        new SheetTextExtractor(context, SharedStrings.Read(sst, context, default), sink, "/fuzz.xml", default).Run(reader);
                    }

                    break;
            }
        }
        catch (OfficeException)
        {
        }

        return writer.ToString();
    }

    public static int WriteSeeds(string directory)
    {
        Directory.CreateDirectory(directory);
        const string W = "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"";
        (string Name, byte Mode, string Xml)[] seeds =
        [
            ("word-basic", 0, $"<w:document {W}><w:body><w:p><w:r><w:t>a</w:t><w:tab/><w:t>b &amp; c</w:t></w:r></w:p><w:tbl><w:tr><w:tc><w:p><w:r><w:t>x</w:t></w:r></w:p></w:tc></w:tr></w:tbl></w:body></w:document>"),
            ("word-revisions", 4, $"<w:document {W}><w:body><w:p><w:pPr><w:rPr><w:del w:id=\"1\"/></w:rPr></w:pPr><w:del><w:r><w:delText>x</w:delText></w:r></w:del></w:p><w:p><w:r><w:t>y</w:t></w:r></w:p></w:body></w:document>"),
            ("word-boxes", 0, $"<w:document {W} xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" xmlns:wps=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\"><w:body><w:p><mc:AlternateContent><mc:Choice Requires=\"wps\"><w:txbxContent><w:p><w:r><w:t>b</w:t></w:r></w:p></w:txbxContent></mc:Choice><mc:Fallback><w:t>f</w:t></mc:Fallback></mc:AlternateContent></w:p></w:body></w:document>"),
            ("drawing", 1, "<p:sld xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><p:sp><p:nvSpPr><p:nvPr><p:ph type=\"sldNum\"/></p:nvPr></p:nvSpPr><p:txBody><a:p><a:r><a:t>1</a:t></a:r></a:p></p:txBody></p:sp><a:tbl><a:tr><a:tc><a:p><a:r><a:t>c</a:t></a:r><a:br/></a:p></a:tc></a:tr></a:tbl></p:sld>"),
            ("sst", 2, Sst.Replace("</si></sst>", "</si><si><rPh><t>p</t></rPh><t>c</t></si></sst>", StringComparison.Ordinal)),
            ("sheet", 3, "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row><c t=\"s\"><v>1</v></c><c t=\"s\"><v>7</v></c><c t=\"inlineStr\"><is><r><t>i</t></r></is></c><c t=\"str\"><v>f</v></c></row></sheetData></worksheet>"),
        ];
        foreach ((string name, byte mode, string xml) in seeds)
        {
            File.WriteAllBytes(Path.Combine(directory, "text-" + name), [mode, .. Encoding.UTF8.GetBytes(xml)]);
        }

        return 0;
    }
}
