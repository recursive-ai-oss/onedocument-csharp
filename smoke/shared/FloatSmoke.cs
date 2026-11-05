using System.IO.Compression;
using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Layout;

namespace RecursiveAI.OneDocument.Smoke;

/// <summary>Lays out a paragraph with a square-wrapped anchored shape and checks that text goes round it: under NativeAOT and WASM.</summary>
internal static class FloatSmoke
{
    public static List<string> Run()
    {
        var failures = new List<string>();
        string anchor =
            "<w:r><w:drawing><wp:anchor xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
            "xmlns:wps=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\" distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\" simplePos=\"0\" relativeHeight=\"1\" behindDoc=\"0\" locked=\"0\" layoutInCell=\"1\" allowOverlap=\"1\">" +
            "<wp:simplePos x=\"0\" y=\"0\"/><wp:positionH relativeFrom=\"margin\"><wp:posOffset>1778000</wp:posOffset></wp:positionH><wp:positionV relativeFrom=\"paragraph\"><wp:posOffset>0</wp:posOffset></wp:positionV>" +
            "<wp:extent cx=\"1905000\" cy=\"1270000\"/><wp:wrapSquare wrapText=\"bothSides\" distT=\"76200\" distB=\"76200\" distL=\"76200\" distR=\"76200\"/><wp:docPr id=\"1\" name=\"box\"/>" +
            "<a:graphic><a:graphicData uri=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\"><wps:wsp><wps:cNvSpPr/><wps:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"1905000\" cy=\"1270000\"/></a:xfrm>" +
            "<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></wps:spPr><wps:bodyPr/></wps:wsp></a:graphicData></a:graphic></wp:anchor></w:drawing></w:r>";
        string words = string.Concat(Enumerable.Repeat("alpha beta gamma delta epsilon zeta ", 40));
        string body = "<w:p>" + anchor + "<w:r><w:t>" + words + "</w:t></w:r></w:p>";
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
            Add(zip, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
            Add(zip, "word/document.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>" + body + "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/></w:sectPr></w:body></w:document>");
        }

        ms.Position = 0;
        using Document doc = Document.Open(ms, leaveOpen: true);
        LayoutDocument layout = LayoutEngine.Layout(doc);
        LayoutPage page = layout.Pages[0];
        LayoutFloat? f = page.Floats.FirstOrDefault();
        if (f is null || f.Wrap != RecursiveAI.OneDocument.Rendering.Drawing.DrawingWrap.Square)
        {
            failures.Add("float placed");
            return failures;
        }

        // Some line has text on both sides of the object, and no text runs inside it.
        bool both = false;
        foreach (LayoutParagraph p in page.BodyParagraphs)
        {
            foreach (LayoutLine line in p.Lines)
            {
                foreach (LayoutRun run in line.Runs)
                {
                    if (run.Bounds.X < f.Bounds.Right - 0.01 && run.Bounds.Right > f.Bounds.X + 0.01 && line.Baseline > f.Bounds.Y && line.Baseline - line.Ascent < f.Bounds.Bottom)
                    {
                        failures.Add("float wrap overlap");
                    }
                }

                both |= line.Runs.Any(r => r.Bounds.Right <= f.Bounds.X) && p.Lines.Any(l => System.Math.Abs(l.Baseline - line.Baseline) < 0.01 && l.Runs.Any(r => r.Bounds.X >= f.Bounds.Right));
            }
        }

        if (!both)
        {
            failures.Add("float wrap both sides");
        }

        return failures;
    }

    private static void Add(ZipArchive zip, string name, string text)
    {
        using var w = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
        w.Write(text);
    }
}
