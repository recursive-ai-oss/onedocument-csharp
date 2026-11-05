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
        if (layout.Pages.Count != 1 || layout.Pages[0].Paragraphs.Count == 0)
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

        return failures;
    }
}
