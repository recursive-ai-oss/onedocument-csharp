using System.Linq;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.Templating;

namespace RecursiveAI.OneDocument.Smoke;

/// <summary>A template model bound by the source generator.</summary>
[TemplateModel]
internal sealed partial class SmokeLetterData
{
    public string Name { get; init; } = string.Empty;

    public string Note { get; init; } = string.Empty;

    public List<SmokeLetterLine> Lines { get; init; } = [];
}

[TemplateModel]
internal sealed partial record SmokeLetterLine(string Item, int Qty);

/// <summary>Creates a document, saves it to memory, opens it again, edits a paragraph, saves again and reads the text back: under NativeAOT and WASM.</summary>
internal static class DocumentSmoke
{
    public static List<string> Run()
    {
        var failures = new List<string>();
        using var first = new MemoryStream();
        using (Document created = Document.Create())
        {
            created.Body.AddParagraph("hello");
            created.SaveAs(first, OfficeFormat.Docx);
        }

        first.Position = 0;
        using var second = new MemoryStream();
        using (Document opened = Document.Open(first))
        {
            if (opened.Format != OfficeFormat.Docx || opened.Paragraphs.Last().Text != "hello")
            {
                failures.Add("document open");
            }

            opened.Paragraphs.Last().SetText("edited & saved");
            opened.SaveAs(second, OfficeFormat.Docx);
        }

        second.Position = 0;
        using Document again = Document.Open(second);
        if (again.Paragraphs.Last().Text != "edited & saved" || !again.ExtractText().Contains("edited & saved", StringComparison.Ordinal))
        {
            failures.Add("document edit");
        }

        failures.AddRange(RtfSmoke());
        failures.AddRange(OdfSmoke());

        // Mail merge: a MERGEFIELD with a picture and a switch, merged into one document per record.
        failures.AddRange(MailMergeSmoke());
        failures.AddRange(RunTemplate());
        return failures;
    }

    // RTF: read into the model, edited, written as RTF (the exporter) and read again; a table, a list-free paragraph with formatting and
    // a Unicode character go through both directions.
    private static List<string> RtfSmoke()
    {
        var failures = new List<string>();
        byte[] rtf = System.Text.Encoding.ASCII.GetBytes(
            "{\\rtf1\\ansi\\deff0{\\fonttbl{\\f0 Arial;}}\\trowd\\cellx2000\\cellx4000\\pard\\intbl A\\cell B\\cell\\row\\pard {\\b bold} caf\\'e9 \\u8364?\\par}");
        using var written = new MemoryStream();
        using (Document d = Document.Open(new MemoryStream(rtf)))
        {
            if (d.Format != OfficeFormat.Rtf || d.Tables.Count() != 1 || !d.ExtractText().Contains("A\tB", StringComparison.Ordinal))
            {
                failures.Add("rtf open");
            }

            d.Paragraphs.Last().SetText("edited \u20ac");
            d.SaveAs(written, OfficeFormat.Rtf);
        }

        written.Position = 0;
        using Document back = Document.Open(written);
        if (back.Tables.Count() != 1 || !back.ExtractText().Contains("edited \u20ac", StringComparison.Ordinal))
        {
            failures.Add("rtf write");
        }

        return failures;
    }

    // OpenDocument text: a flat file is read into the model with a table, a list and a note, edited, written as ODT and as flat ODT, and read again.
    private static List<string> OdfSmoke()
    {
        var failures = new List<string>();
        const string flat = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><office:document xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" " +
            "xmlns:text=\"urn:oasis:names:tc:opendocument:xmlns:text:1.0\" xmlns:table=\"urn:oasis:names:tc:opendocument:xmlns:table:1.0\" office:version=\"1.3\" " +
            "office:mimetype=\"application/vnd.oasis.opendocument.text\"><office:body><office:text><text:h text:outline-level=\"1\">Title</text:h><text:p>Hello <text:span>world</text:span></text:p>" +
            "<text:list><text:list-item><text:p>item</text:p></text:list-item></text:list><table:table><table:table-column/><table:table-row><table:table-cell><text:p>cell</text:p></table:table-cell></table:table-row></table:table>" +
            "<text:p>note<text:note text:note-class=\"footnote\"><text:note-body><text:p>foot</text:p></text:note-body></text:note></text:p></office:text></office:body></office:document>";
        using var odt = new MemoryStream();
        using var fodt = new MemoryStream();
        using (Document d = Document.Open(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(flat))))
        {
            if (d.Format != OfficeFormat.Fodt || d.Tables.Count() != 1 || !d.ExtractText().Contains("Hello world", StringComparison.Ordinal) || d.ImportLoss is null)
            {
                failures.Add("odf open");
            }

            d.Paragraphs.First().SetText("Edited title");
            d.SaveAs(odt, OfficeFormat.Odt);
            d.SaveAs(fodt, OfficeFormat.Fodt);
        }

        foreach (MemoryStream written in new[] { odt, fodt })
        {
            written.Position = 0;
            using Document back = Document.Open(written);
            if (back.Tables.Count() != 1 || !back.ExtractText().Contains("Edited title", StringComparison.Ordinal) || !back.ExtractText().Contains("foot", StringComparison.Ordinal))
            {
                failures.Add("odf write " + back.Format);
            }
        }

        return failures;
    }

    private static List<string> MailMergeSmoke()
    {
        var failures = new List<string>();
        using Document merged = Document.Open(new MemoryStream(DocxZip(
            "<w:p><w:fldSimple w:instr=\" MERGEFIELD Name \\b &quot;Dear &quot; \"><w:r><w:t>x</w:t></w:r></w:fldSimple>"
            + "<w:fldSimple w:instr=\" MERGEFIELD Qty \\# x## \"><w:r><w:t>y</w:t></w:r></w:fldSimple></w:p>")));
        var records = new List<object?>
        {
            new Dictionary<string, object?> { ["Name"] = "Ann", ["Qty"] = 222492 },
            new Dictionary<string, object?> { ["Name"] = "Bo", ["Qty"] = 3 },
        };
        using (RecursiveAI.OneDocument.Documents.MailMerge.MailMergeResult result = merged.MailMerge(records))
        {
            if (result.Documents.Count != 2 || result.Documents[0].ExtractText().Trim() != "Dear Ann492" || result.Documents[1].ExtractText().Trim() != "Dear Bo3")
            {
                failures.Add("mail merge");
            }
        }

        return failures;
    }

    // Renders a document template against a generated model, with a loop, a link and markdown, saves and reads it back.
    private static List<string> RunTemplate()
    {
        var failures = new List<string>();
        DocumentFormats.Register(FormatRegistry.Default);
        using var saved = new MemoryStream();
        using (Document template = Document.Create())
        {
            template.Body.AddParagraph("Dear {{ na" + "me }},");
            template.Body.AddParagraph("{% for l in lines %}");
            template.Body.AddParagraph("{{ l.item }} x {{ l.qty }}");
            template.Body.AddParagraph("{% endfor %}");
            template.Body.AddParagraph("{% markdown note %}");
            Template.Render(template, new SmokeLetterData { Name = "Ada", Note = "Some **bold** [link](https://example.com)", Lines = [new SmokeLetterLine("Pen", 2), new SmokeLetterLine("Ink", 1)] });
            template.SaveAs(saved, OfficeFormat.Docx);
        }

        saved.Position = 0;
        using Document rendered = Document.Open(saved);
        string text = rendered.ExtractText();
        if (!text.Contains("Dear Ada,", StringComparison.Ordinal) || !text.Contains("Pen x 2", StringComparison.Ordinal) || !text.Contains("Ink x 1", StringComparison.Ordinal) || !text.Contains("Some bold link", StringComparison.Ordinal) || text.Contains("{%", StringComparison.Ordinal))
        {
            failures.Add("document template");
        }

        return failures;
    }

    private static byte[] DocxZip(string body)
    {
        const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string name, string content)
            {
                using Stream s = zip.CreateEntry(name, System.IO.Compression.CompressionLevel.NoCompression).Open();
                s.Write(System.Text.Encoding.UTF8.GetBytes(content));
            }

            Add("[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
            Add("_rels/.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
            Add("word/document.xml", "<w:document xmlns:w=\"" + W + "\"><w:body>" + body + "</w:body></w:document>");
        }

        return ms.ToArray();
    }
}
