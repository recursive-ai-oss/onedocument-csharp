using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Charts;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Layout;
using OneRender.Painting;
using RecursiveAI.OneDocument.Rendering;
using RecursiveAI.OneDocument.Rendering.Spreadsheets;
using RecursiveAI.OneDocument.Spreadsheets;
using RecursiveAI.OneDocument.Rendering.Metafiles;

namespace RecursiveAI.OneDocument.Smoke;

/// <summary>Lays out a small document and a small workbook with the packaged fonts and writes them as PDF and PNG: under NativeAOT and WASM.</summary>
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

        using Workbook workbook = Workbook.Create();
        Worksheet sheet = workbook.Sheets[0];
        sheet["A1"].SetString("Item");
        sheet["B1"].SetString("Amount");
        sheet["A2"].SetString("Widgets");
        sheet["B2"].SetNumber(1234.5);
        using var sheetPdf = new MemoryStream();
        SheetLayoutDocument sheets = WorkbookRenderer.WritePdf(workbook, sheetPdf);
        if (sheets.Pages.Count != 1 || sheets.Pages[0].Body.ToString() != "A1:B2")
        {
            failures.Add("render sheet layout");
        }

        byte[] sheetBytes = sheetPdf.ToArray();
        if (sheetBytes.Length < 500 || Encoding.ASCII.GetString(sheetBytes, 0, 5) != "%PDF-")
        {
            failures.Add("render sheet pdf");
        }

        if (WorkbookRenderer.RenderPng(sheets, sheets.Pages[0]) is not { Length: > 500 } sheetPng || sheetPng[0] != 0x89)
        {
            failures.Add("render sheet png");
        }

        // A tiny enhanced metafile (header, one rectangle, end) drawn as vector content: the interpreter, stroker and display list under AOT.
        var list = new DisplayList();
        uint owner = 0;
        var context = new MetafilePaintContext(list, new PictureImages(), () => ItemId.Of(++owner));
        if (!MetafilePainter.TryPaint(TinyEmf(), context, new RectF(0, 0, 100, 100)) || list.Count < 3)
        {
            failures.Add("render metafile");
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

        failures.AddRange(ChartCheck());
        failures.AddRange(MathCheck());
        return failures;
    }

    // Reads an equation (a fraction under a square root) and lays it out with the packaged math font, under the same constraints.
    private static List<string> MathCheck()
    {
        var failures = new List<string>();
        const string Xml = "<m:oMath xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\"><m:rad><m:radPr><m:degHide m:val=\"1\"/></m:radPr><m:deg/><m:e><m:f><m:num><m:r><m:t>a+b</m:t></m:r></m:num><m:den><m:r><m:t>c</m:t></m:r></m:den></m:f></m:e></m:rad></m:oMath>";
        if (!RecursiveAI.OneDocument.Markup.MarkupDocument.TryParse(Encoding.UTF8.GetBytes(Xml), out RecursiveAI.OneDocument.Markup.MarkupDocument? doc) || doc is null)
        {
            failures.Add("math parse");
            return failures;
        }

        RecursiveAI.OneDocument.Equations.MathNode? node = RecursiveAI.OneDocument.Equations.OmmlReader.Read(doc.Root, out _);
        RecursiveAI.OneDocument.Rendering.Equations.MathBox box = RecursiveAI.OneDocument.Rendering.Equations.MathLayouter.Layout(node!, new RecursiveAI.OneDocument.Rendering.Equations.MathLayoutOptions { Fonts = FontLibrary.CreateDefault(), FontSize = 12, Display = true });
        if (box.LinearText != "\u221A((a+b)/c)" || box.Items.Count < 6 || box.Notes.Count != 0 || !(box.Width > 0 && box.Ascent > 0))
        {
            failures.Add("math layout " + box.LinearText);
        }

        return failures;
    }

    // Reads a small classic chart, paints it with the packaged fonts, and writes it as PDF and PNG.
    private static List<string> ChartCheck()
    {
        var failures = new List<string>();
        const string Xml = "<c:chartSpace xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\"><c:chart><c:plotArea><c:barChart><c:barDir val=\"col\"/><c:grouping val=\"clustered\"/>" +
            "<c:ser><c:idx val=\"0\"/><c:order val=\"0\"/><c:cat><c:strLit><c:ptCount val=\"3\"/><c:pt idx=\"0\"><c:v>A</c:v></c:pt><c:pt idx=\"1\"><c:v>B</c:v></c:pt><c:pt idx=\"2\"><c:v>C</c:v></c:pt></c:strLit></c:cat>" +
            "<c:val><c:numLit><c:ptCount val=\"3\"/><c:pt idx=\"0\"><c:v>3</c:v></c:pt><c:pt idx=\"1\"><c:v>7</c:v></c:pt><c:pt idx=\"2\"><c:v>5</c:v></c:pt></c:numLit></c:val></c:ser>" +
            "<c:axId val=\"1\"/><c:axId val=\"2\"/></c:barChart><c:catAx><c:axId val=\"1\"/><c:crossAx val=\"2\"/></c:catAx><c:valAx><c:axId val=\"2\"/><c:crossAx val=\"1\"/><c:majorGridlines/></c:valAx></c:plotArea></c:chart></c:chartSpace>";
        ChartSpace chart = ChartReader.Read(Encoding.UTF8.GetBytes(Xml));
        using var pdf = new MemoryStream();
        ChartHost.WritePdf(chart, pdf, 360, 216);
        if (chart.Groups.Count != 1 || pdf.Length < 500 || Encoding.ASCII.GetString(pdf.ToArray(), 0, 5) != "%PDF-")
        {
            failures.Add("chart pdf");
        }

        byte[] png = ChartHost.RenderPng(chart, 360, 216);
        if (png.Length < 500 || png[0] != 0x89)
        {
            failures.Add("chart png");
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
