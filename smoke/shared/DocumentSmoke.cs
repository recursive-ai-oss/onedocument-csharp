using System.Linq;
using RecursiveAI.OneDocument.Documents;
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

        failures.AddRange(RunTemplate());
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
}
