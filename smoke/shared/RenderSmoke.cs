using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Layout;
using RecursiveAI.OneDocument.Rendering;

namespace RecursiveAI.OneDocument.Smoke;

/// <summary>Lays out a small document with the packaged fonts and writes it as PDF and PNG: under NativeAOT and WASM.</summary>
internal static class RenderSmoke
{
    public static List<string> Run()
    {
        var failures = new List<string>();
        using Document document = Document.Create();
        document.Body.AddParagraph("Rendering smoke: a paragraph long enough to need shaping and a line break or two, " +
            "written once by the layout core and painted into a tagged PDF.");
        using var pdf = new MemoryStream();
        LayoutDocument layout = DocumentRenderer.WritePdf(document, pdf);
        byte[] bytes = pdf.ToArray();
        if (layout.Pages.Count != 1 || !layout.Pages[0].Paragraphs.Any())
        {
            failures.Add("render layout");
        }

        if (bytes.Length < 500 || Encoding.ASCII.GetString(bytes, 0, 5) != "%PDF-")
        {
            failures.Add("render pdf");
        }

        byte[] png = DocumentRenderer.RenderPng(layout.Pages[0]);
        if (png.Length < 500 || png[0] != 0x89)
        {
            failures.Add("render png");
        }

        // Two columns with a rule, a footer and a PAGE field's page: the page model under the same constraints.
        using Document columns = Document.Create();
        columns.Sections[0].SetEqualColumns(2, 720, true);
        columns.Sections[0].AddOwn(HeaderFooterKind.Footer, HeaderFooterType.Default).Paragraphs().First().SetText("Footer");
        for (int i = 0; i < 80; i++)
        {
            columns.Body.AddParagraph("Column text " + i);
        }

        LayoutDocument two = LayoutEngine.Layout(columns);
        LayoutPage first = two.Pages[0];
        if (first.Columns.Count != 2 || !first.FooterParagraphs.Any() || !first.Decorations.Any(d => d.DecorationKind == LayoutDecorationKind.ColumnSeparator) || !first.Paragraphs.Any(p => p.Column == 1))
        {
            failures.Add("render columns");
        }

        return failures;
    }
}
