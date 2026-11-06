using System.Text;
using RecursiveAI.OneDocument.Documents;

namespace RecursiveAI.OneDocument.Smoke;

/// <summary>Reads Markdown into a document, writes it back as Markdown and as DOCX, under NativeAOT and WASM.</summary>
internal static class MarkdownSmoke
{
    public static List<string> Run()
    {
        var failures = new List<string>();
        const string md = "# Title\n\nSome *emphasis*, a [link](https://example.com) and `code` &copy;.\n\n- one\n- two\n\n| a | b |\n|---|--:|\n| 1 | 2 |\n";
        using Document doc = Document.ParseMarkdown(md);
        if (new List<Paragraph>(doc.Paragraphs)[0].Text != "Title") failures.Add("markdown heading");
        string text = doc.ExtractText();
        if (!text.Contains("Some emphasis, a link and code \u00A9.", StringComparison.Ordinal)) failures.Add("markdown text: " + text);
        string back = doc.ToMarkdown();
        if (!back.Contains("# Title", StringComparison.Ordinal) || !back.Contains("| a | b |", StringComparison.Ordinal)) failures.Add("markdown export: " + back);
        using var docx = new MemoryStream();
        doc.SaveAs(docx, OfficeFormat.Docx);
        using Document reopened = Document.Open(new MemoryStream(docx.ToArray()));
        if (reopened.ToMarkdown() != back) failures.Add("markdown docx round trip");
        return failures;
    }
}
