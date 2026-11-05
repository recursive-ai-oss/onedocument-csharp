using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Layout;
using OneRender.Painting;
using RecursiveAI.OneDocument.Rendering;
using RecursiveAI.OneDocument.Rendering.Metafiles;

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

        // A tiny enhanced metafile (header, one rectangle, end) drawn as vector content: the interpreter, stroker and display list under AOT.
        var list = new DisplayList();
        uint owner = 0;
        var context = new MetafilePaintContext(list, new PictureImages(), () => ItemId.Of(++owner));
        if (!MetafilePainter.TryPaint(TinyEmf(), context, new RectF(0, 0, 100, 100)) || list.Count < 3)
        {
            failures.Add("render metafile");
        }

        return failures;
    }

    // [MS-EMF] 2.2.9 header (88 bytes), 2.3.5.34 EMR_RECTANGLE, 2.3.4.1 EMR_EOF.
    private static byte[] TinyEmf()
    {
        var b = new List<byte>();
        void I(int v) => b.AddRange(BitConverter.GetBytes(v));
        I(1); I(88); I(0); I(0); I(99); I(99); I(0); I(0); I(2646); I(2646);
        I(0x464D4520); I(0x10000); I(88 + 24 + 20); I(3); I(1); I(0); I(0); I(0);
        I(1000); I(1000); I(100); I(100);
        I(43); I(24); I(10); I(10); I(80); I(80);
        I(14); I(20); I(0); I(16); I(20);
        return b.ToArray();
    }
}
