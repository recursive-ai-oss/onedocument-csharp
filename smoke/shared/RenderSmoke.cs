using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Charts;
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

        failures.AddRange(ChartCheck());
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
}
