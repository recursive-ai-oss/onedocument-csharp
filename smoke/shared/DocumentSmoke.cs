using System.Linq;
using RecursiveAI.OneOffice.Documents;

namespace RecursiveAI.OneOffice.Smoke;

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

        return failures;
    }
}
