using System.Linq;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Smoke;

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

        // Mail merge: a MERGEFIELD with a picture and a switch, merged into one document per record.
        return failures.Concat(MailMergeSmoke()).ToList();
    }

    private static List<string> MailMergeSmoke()
    {
        var failures = new List<string>();
        const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        using var package = new InMemoryPackage();
        const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
        package.SetRelationships(null, [new Relationship("rId1", Rel + "officeDocument", "word/document.xml")]);
        package.AddPart("/word/document.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml", System.Text.Encoding.UTF8.GetBytes(
            "<w:document xmlns:w=\"" + W + "\"><w:body><w:p><w:fldSimple w:instr=\" MERGEFIELD Name \\b &quot;Dear &quot; \"><w:r><w:t>x</w:t></w:r></w:fldSimple>"
            + "<w:fldSimple w:instr=\" MERGEFIELD Qty \\# x## \"><w:r><w:t>y</w:t></w:r></w:fldSimple></w:p></w:body></w:document>"));
        using Document merged = Document.FromPackage(package);
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
}
