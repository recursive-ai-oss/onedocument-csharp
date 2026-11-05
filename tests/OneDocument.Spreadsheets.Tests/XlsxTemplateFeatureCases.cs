using System.IO.Compression;
using System.Text;
using System.Text.Json;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Spreadsheets.Templating;
using RecursiveAI.OneDocument.Templating;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Spreadsheets.Tests;

// Goldens for what hangs off a sheet: charts, pivot sources, rule formulas, copies of sheets, tags outside cells, and pictures.
public partial class XlsxTemplateTests
{
    private const string Main = TestBook.Main;
    private const string R = TestBook.R;
    private const string RelBase = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private const string ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private const string DrawNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private const string XdrNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private const string Decl = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n";

    // A 1 x 1 PNG.
    private const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    private static void AddPart(Workbook wb, string name, string contentType, string xml) => wb.Package.AddPart(name, contentType, Encoding.UTF8.GetBytes(xml));

    private static string Link(Workbook wb, string source, string type, string target)
    {
        var rels = new List<Relationship>(wb.Package.GetRelationships(source));
        string id = "rId" + (rels.Count + 1);
        rels.Add(new Relationship(id, RelBase + type, target));
        wb.Package.SetRelationships(source, rels);
        return id;
    }

    private static void Frame(Worksheet s, string local, string xml)
    {
        s.InsertFrameElement(local, xml);
        s.NoteFrameEdited();
    }

    private static string ChartXml(string sheet, string cat, string val) =>
        Decl + "<c:chartSpace xmlns:c=\"" + ChartNs + "\" xmlns:a=\"" + DrawNs + "\" xmlns:r=\"" + R + "\"><c:chart><c:plotArea><c:layout/><c:barChart><c:barDir val=\"col\"/><c:grouping val=\"clustered\"/>"
        + "<c:ser><c:idx val=\"0\"/><c:order val=\"0\"/><c:tx><c:strRef><c:f>" + sheet + "!$B$1</c:f><c:strCache><c:ptCount val=\"1\"/><c:pt idx=\"0\"><c:v>Qty</c:v></c:pt></c:strCache></c:strRef></c:tx>"
        + "<c:cat><c:strRef><c:f>" + sheet + "!" + cat + "</c:f><c:strCache><c:ptCount val=\"1\"/><c:pt idx=\"0\"><c:v>old</c:v></c:pt></c:strCache></c:strRef></c:cat>"
        + "<c:val><c:numRef><c:f>" + sheet + "!" + val + "</c:f><c:numCache><c:formatCode>0.0</c:formatCode><c:ptCount val=\"1\"/><c:pt idx=\"0\"><c:v>0</c:v></c:pt></c:numCache></c:numRef></c:val></c:ser>"
        + "<c:axId val=\"111\"/><c:axId val=\"222\"/></c:barChart><c:catAx><c:axId val=\"111\"/><c:scaling><c:orientation val=\"minMax\"/></c:scaling><c:delete val=\"0\"/><c:axPos val=\"b\"/><c:crossAx val=\"222\"/></c:catAx>"
        + "<c:valAx><c:axId val=\"222\"/><c:scaling><c:orientation val=\"minMax\"/></c:scaling><c:delete val=\"0\"/><c:axPos val=\"l\"/><c:crossAx val=\"111\"/></c:valAx></c:plotArea><c:plotVisOnly val=\"1\"/></c:chart></c:chartSpace>";

    private static string DrawingXml(string anchors) =>
        Decl + "<xdr:wsDr xmlns:xdr=\"" + XdrNs + "\" xmlns:a=\"" + DrawNs + "\">" + anchors + "</xdr:wsDr>";

    private static string Anchor(int fromCol, int fromRow, int toCol, int toRow, string content) =>
        "<xdr:twoCellAnchor><xdr:from><xdr:col>" + fromCol + "</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>" + fromRow + "</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>"
        + "<xdr:to><xdr:col>" + toCol + "</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>" + toRow + "</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>" + content + "<xdr:clientData/></xdr:twoCellAnchor>";

    private static string ChartFrame(string rid) =>
        "<xdr:graphicFrame macro=\"\"><xdr:nvGraphicFramePr><xdr:cNvPr id=\"2\" name=\"Chart 1\"/><xdr:cNvGraphicFramePr/></xdr:nvGraphicFramePr><xdr:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/></xdr:xfrm>"
        + "<a:graphic><a:graphicData uri=\"" + ChartNs + "\"><c:chart xmlns:c=\"" + ChartNs + "\" xmlns:r=\"" + R + "\" r:id=\"" + rid + "\"/></a:graphicData></a:graphic></xdr:graphicFrame>";

    // A drawing on the sheet with one chart anchored at E6:K21.
    private static void AddChart(Workbook wb, Worksheet s, string sheetName, string cat, string val)
    {
        AddPart(wb, "/xl/charts/chart1.xml", "application/vnd.openxmlformats-officedocument.drawingml.chart+xml", ChartXml(sheetName, cat, val));
        AddPart(wb, "/xl/drawings/drawing1.xml", "application/vnd.openxmlformats-officedocument.drawing+xml", DrawingXml(Anchor(4, 5, 10, 20, ChartFrame("rId1"))));
        Link(wb, "/xl/drawings/drawing1.xml", "chart", "../charts/chart1.xml");
        string rid = Link(wb, s.PartName!, "drawing", "../drawings/drawing1.xml");
        Frame(s, "drawing", "<drawing xmlns=\"" + Main + "\" xmlns:r=\"" + R + "\" r:id=\"" + rid + "\"/>");
    }

    private static void AddComments(Workbook wb, Worksheet s, params (string Ref, int Row, int Col, string Text)[] comments)
    {
        var list = new StringBuilder();
        var shapes = new StringBuilder();
        int id = 1025;
        foreach ((string r, int row, int col, string text) in comments)
        {
            list.Append("<comment ref=\"").Append(r).Append("\" authorId=\"0\"><text><r><rPr><b/><sz val=\"9\"/></rPr><t>").Append(text).Append("</t></r></text></comment>");
            shapes.Append("<v:shape id=\"_x0000_s").Append(id++).Append("\" type=\"#_x0000_t202\" style=\"position:absolute;margin-left:80pt;margin-top:5pt;width:100pt;height:50pt;z-index:1;visibility:hidden\" fillcolor=\"#ffffe1\" o:insetmode=\"auto\">")
                .Append("<v:fill color2=\"#ffffe1\"/><v:shadow on=\"t\" color=\"black\" obscured=\"t\"/><v:path o:connecttype=\"none\"/><v:textbox style=\"mso-direction-alt:auto\"><div style=\"text-align:left\"></div></v:textbox>")
                .Append("<x:ClientData ObjectType=\"Note\"><x:MoveWithCells/><x:SizeWithCells/><x:Anchor>").Append(col + 1).Append(", 15, ").Append(row).Append(", 2, ").Append(col + 3).Append(", 15, ").Append(row + 3).Append(", 16</x:Anchor><x:AutoFill>False</x:AutoFill><x:Row>")
                .Append(row).Append("</x:Row><x:Column>").Append(col).Append("</x:Column></x:ClientData></v:shape>");
        }

        AddPart(wb, "/xl/comments1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.comments+xml",
            Decl + "<comments xmlns=\"" + Main + "\"><authors><author>Author</author></authors><commentList>" + list + "</commentList></comments>");
        AddPart(wb, "/xl/drawings/vmlDrawing1.vml", "application/vnd.openxmlformats-officedocument.vmlDrawing",
            "<xml xmlns:v=\"urn:schemas-microsoft-com:vml\" xmlns:o=\"urn:schemas-microsoft-com:office:office\" xmlns:x=\"urn:schemas-microsoft-com:office:excel\"><o:shapelayout v:ext=\"edit\"><o:idmap v:ext=\"edit\" data=\"1\"/></o:shapelayout>"
            + "<v:shapetype id=\"_x0000_t202\" coordsize=\"21600,21600\" o:spt=\"202\" path=\"m,l,21600r21600,l21600,xe\"><v:stroke joinstyle=\"miter\"/><v:path gradientshapeok=\"t\" o:connecttype=\"rect\"/></v:shapetype>" + shapes + "</xml>");
        Link(wb, s.PartName!, "comments", "../comments1.xml");
        string rid = Link(wb, s.PartName!, "vmlDrawing", "../drawings/vmlDrawing1.vml");
        Frame(s, "legacyDrawing", "<legacyDrawing xmlns=\"" + Main + "\" xmlns:r=\"" + R + "\" r:id=\"" + rid + "\"/>");
    }

    // The text of the saved parts whose names start with each prefix, in name order: what the golden records beside the cells.
    private static string Parts(Workbook wb, params string[] prefixes)
    {
        using var ms = new MemoryStream();
        wb.SaveAs(ms, OfficeFormat.Xlsx);
        ms.Position = 0;
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var sb = new StringBuilder();
        foreach (string prefix in prefixes)
        {
            foreach (ZipArchiveEntry e in zip.Entries.Where(e => e.FullName.StartsWith(prefix, StringComparison.Ordinal)).OrderBy(e => e.FullName, StringComparer.Ordinal))
            {
                using var reader = new StreamReader(e.Open(), Encoding.UTF8);
                sb.Append('{').Append(e.FullName).Append("} ").Append(reader.ReadToEnd().Replace("\r\n", "\n").Replace(Decl, string.Empty)).Append('\n');
            }
        }

        return sb.ToString().TrimEnd('\n');
    }

    private static string Rels(Workbook wb, string part) => string.Join(";", wb.Package.GetRelationships(part).Select(r => r.Id + ":" + r.Type[(r.Type.LastIndexOf('/') + 1)..] + "->" + r.Target));

    public static IEnumerable<object[]> FeatureCaseNames() => FeatureCases().Select(static c => new object[] { c.Name });

    // The committed template files, as an author's tool would have saved them, render to the same output as the workbook built in code.
    [Theory]
    [MemberData(nameof(FeatureCaseNames))]
    public void ACommittedFeatureTemplateRendersToItsGolden(string name)
    {
        Case c = Cases().Single(x => x.Name == name);
        using Workbook wb = Workbook.Open(Path.Combine(GoldenDir, name + ".template.xlsx"));
        using JsonDocument json = JsonDocument.Parse(File.ReadAllText(Path.Combine(GoldenDir, name + ".data.json")));
        XlsxTemplateBinder.Render(wb, json, new TemplateOptions { Clock = new FixedClock() });
        Assert.Equal(File.ReadAllText(Path.Combine(GoldenDir, name + ".expected.txt")).Replace("\r\n", "\n"), Dump(wb, c.Extra));
    }

    [Fact]
    public void AnInvalidImageInALoopIsReportedAtItsRow()
    {
        Case c = Cases().Single(x => x.Name == "image-per-row");
        (_, IReadOnlyList<Defect> warnings) = Render(c);
        Defect d = Assert.Single(warnings, w => w.Code == "template.image-invalid");
        Assert.Contains("B3", d.Location.ToString());
    }

    [Fact]
    public void ChartCachesHoldTheRecalculatedValues()
    {
        using Workbook wb = Workbook.Create();
        Worksheet s = wb.Sheets[0];
        Put(s, "A1", "{% for it in items %}{{ it.qty }}"); Put(s, "B1", null, "A1*2"); Put(s, "C1", "{% endfor %}");
        AddChart(wb, s, "Sheet1", "$A$1:$A$1", "$B$1:$B$1");
        using JsonDocument json = JsonDocument.Parse("""{"items":[{"qty":3},{"qty":10},{"qty":2}]}""");
        XlsxTemplateBinder.Render(wb, json, null, new XlsxTemplateOptions { RecalculateNow = true });
        string chart = Parts(wb, "xl/charts/");
        Assert.Contains("<c:f>Sheet1!$B$1:$B$3</c:f><c:numCache><c:formatCode>0.0</c:formatCode><c:ptCount val=\"3\"/><c:pt idx=\"0\"><c:v>6</c:v></c:pt><c:pt idx=\"1\"><c:v>20</c:v></c:pt><c:pt idx=\"2\"><c:v>4</c:v></c:pt>", chart);
    }

    [Fact]
    public void ValidateLocatesErrorsInCellsHeadersAndComments()
    {
        using Workbook wb = Workbook.Create();
        Worksheet s = wb.Sheets[0];
        Put(s, "B3", "{% for x in %}");
        Frame(s, "headerFooter", "<headerFooter xmlns=\"" + Main + "\"><oddHeader>&amp;C{{ 1 + }}</oddHeader></headerFooter>");
        IReadOnlyList<Defect> found = XlsxTemplateBinder.Validate(wb);
        Assert.Contains(found, d => d.Location.ToString().Contains("B3", StringComparison.Ordinal));
        Assert.Contains(found, d => d.Location.ToString().Contains("oddHeader", StringComparison.Ordinal));

        using Workbook good = Workbook.Create();
        Put(good.Sheets[0], "A1", "{{ title }}");
        Assert.Empty(XlsxTemplateBinder.Validate(good));
    }

    private static IEnumerable<Case> FeatureCases()
    {
        const string items = """{"title":"Autumn","items":[{"name":"Bolt","qty":3,"price":1.5},{"name":"Nut","qty":10,"price":0.25},{"name":"Gear","qty":2,"price":12}]}""";
        const string regions = """{"regions":[{"name":"North","total":10},{"name":"South","total":20}]}""";

        void Loop(Worksheet s)
        {
            Put(s, "A1", "Name"); Put(s, "B1", "Qty");
            Put(s, "A2", "{% for it in items %}{{ it.name }}"); Put(s, "B2", "{{ it.qty }}{% endfor %}");
        }

        yield return new("chart-series-grow", wb =>
        {
            Worksheet s = First(wb);
            Loop(s);
            AddChart(wb, s, "Sheet1", "$A$2:$A$2", "$B$2:$B$2");
        }, items, wb => Parts(wb, "xl/charts/", "xl/drawings/drawing"));

        yield return new("chart-on-other-sheet", wb =>
        {
            Loop(First(wb));
            Worksheet o = wb.Sheets.Add("Summary");
            Put(o, "A1", "chart below");
            AddChart(wb, o, "Sheet1", "$A$2:$A$2", "$B$2:$B$2");
        }, items, wb => Parts(wb, "xl/charts/"));

        yield return new("chart-per-sheet-copy", wb =>
        {
            Worksheet s = First(wb);
            s.Rename("{{ r.name }}");
            Put(s, "A1", "{% for r in regions scope=sheet %}{{ r.name }}"); Put(s, "A2", "x"); Put(s, "B2", "{{ r.total }}{% endfor %}");
            AddChart(wb, s, "'{{ r.name }}'", "$A$2:$A$2", "$B$2:$B$2");
        }, regions, wb => Parts(wb, "xl/charts/") + "\n" + Rels(wb, "/xl/drawings/drawing1.xml") + "|" + Rels(wb, "/xl/drawings/drawing2.xml") + "|" + Rels(wb, "/xl/worksheets/sheet2.xml"));

        yield return new("pivot-source-grows", wb => { }, """{"rows":[{"region":"East","product":"A","qtr":"H1","sales":100,"units":1},{"region":"West","product":"B","qtr":"H2","sales":50,"units":5},{"region":"East","product":"B","qtr":"H2","sales":80,"units":7}]}""",
            wb => Parts(wb, "xl/pivotCache/pivotCacheDefinition"),
            () =>
            {
                var book = new PivotBook
                {
                    Data = [["{% for r in rows %}{{ r.region }}", "{{ r.product }}", "{{ r.qtr }}", "{{ r.sales }}", "{{ r.units }}{% endfor %}"]],
                    SourceXml = "<worksheetSource ref=\"A1:E2\" sheet=\"Data\"/>",
                    RowFields = [0],
                    DataFields = [PivotBook.DataField("Sum of Sales", 3)],
                };
                return Workbook.Open(new MemoryStream(book.Build()));
            });

        yield return new("cf-rule-formula", wb =>
        {
            Worksheet s = First(wb);
            Put(s, "A1", "{% for it in items %}{{ it.name }}"); Put(s, "B1", "{{ it.qty }}{% endfor %}"); Put(s, "A2", "limit"); Put(s, "B2", 5);
            s.ConditionalFormats.Add(CellRange.Parse("B1:B1"), ConditionalRuleSpec.Expression("B1>$B$2", 0));
        }, items, wb => Parts(wb, "xl/worksheets/sheet1.xml").Split("<conditionalFormatting")[1].Split("</conditionalFormatting>")[0]);

        yield return new("validation-rule-formula", wb =>
        {
            Worksheet s = First(wb);
            Put(s, "A1", "{% for it in items %}{{ it.name }}"); Put(s, "B1", "{% endfor %}"); Put(s, "D1", "pick");
            s.Validations.Add(CellRange.Parse("D1:D1"), DataValidationSpec.ListFromRange("$A$1:$A$1"));
        }, items, wb => Parts(wb, "xl/worksheets/sheet1.xml").Split("<dataValidations")[1].Split("</dataValidations>")[0]);

        yield return new("sheet-copy-features", wb =>
        {
            Worksheet s = First(wb);
            s.Rename("{{ r.name }}");
            Put(s, "A1", "{% for r in regions scope=sheet %}Name"); Put(s, "B1", "Total");
            Put(s, "A2", "{{ r.name }}"); Put(s, "B2", "{{ r.total }}{% endfor %}");
            s.Tables.Add("Totals", CellRange.Parse("A1:B2"));
            s.ConditionalFormats.Add(CellRange.Parse("B2:B2"), ConditionalRuleSpec.CellIs("greaterThan", "15", 0));
            s.Validations.Add(CellRange.Parse("B2:B2"), DataValidationSpec.List("a", "b"));
            Frame(s, "pageMargins", "<pageMargins xmlns=\"" + Main + "\" left=\"0.5\" right=\"0.5\" top=\"0.75\" bottom=\"0.75\" header=\"0.3\" footer=\"0.3\"/>");
            Frame(s, "pageSetup", "<pageSetup xmlns=\"" + Main + "\" orientation=\"landscape\"/>");
            AddChart(wb, s, "Sheet1", "$A$2:$A$2", "$B$2:$B$2");
        }, regions, wb => string.Join(";", wb.Sheets.Select(x => x.Name + ":" + string.Join(",", x.Tables.Select(t => t.Name + "=" + t.Range)))) + "\n"
            + string.Join("\n", wb.Sheets.Select(x => x.Name + " " + Parts(wb, "xl/worksheets/sheet" + (x.Index + 1) + ".xml").Split("</sheetData>").Last().Replace("</worksheet>", string.Empty))));

        yield return new("header-footer-tags", wb =>
        {
            Worksheet s = First(wb);
            s.Rename("{{ r.name }}");
            Put(s, "A1", "{% for r in regions scope=sheet %}{{ r.name }}"); Put(s, "B1", "{{ r.total }}{% endfor %}");
            Frame(s, "headerFooter", "<headerFooter xmlns=\"" + Main + "\"><oddHeader>&amp;L{{ r.name }}&amp;CReport {{ r.total }}</oddHeader><oddFooter>&amp;RPage &amp;P of &amp;N</oddFooter></headerFooter>");
        }, regions, wb => string.Join("\n", wb.Sheets.Select(x => Parts(wb, "xl/worksheets/sheet" + (x.Index + 1) + ".xml").Split("<headerFooter")[1].Split("</headerFooter>")[0])));

        yield return new("comment-tags-in-loop", wb =>
        {
            Worksheet s = First(wb);
            Put(s, "A1", "{% for it in items %}{{ it.name }}"); Put(s, "B1", "{{ it.qty }}{% endfor %}"); Put(s, "A3", "total");
            AddComments(wb, s, ("A1", 0, 0, "{{ it.name }} costs {{ it.price }}"), ("A3", 2, 0, "sum row"));
        }, items, wb => Parts(wb, "xl/comments", "xl/drawings/vmlDrawing"));

        yield return new("comment-tags-in-sheet-copies", wb =>
        {
            Worksheet s = First(wb);
            s.Rename("{{ r.name }}");
            Put(s, "A1", "{% for r in regions scope=sheet %}{{ r.name }}"); Put(s, "B1", "{{ r.total }}{% endfor %}");
            AddComments(wb, s, ("B1", 0, 1, "total of {{ r.name }}"));
        }, regions, wb => Parts(wb, "xl/comments", "xl/drawings/vmlDrawing").Replace("\n", "\n") + "\n" + Rels(wb, "/xl/worksheets/sheet2.xml"));

        yield return new("text-box-tags", wb =>
        {
            Worksheet s = First(wb);
            Put(s, "A1", "{{ title }}");
            string box = "<xdr:sp macro=\"\" textlink=\"\"><xdr:nvSpPr><xdr:cNvPr id=\"2\" name=\"TextBox 1\"/><xdr:cNvSpPr txBox=\"1\"/></xdr:nvSpPr><xdr:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"1000000\" cy=\"300000\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></xdr:spPr>"
                + "<xdr:txBody><a:bodyPr/><a:lstStyle/><a:p><a:r><a:rPr lang=\"en-US\" b=\"1\"/><a:t>Report {{ ti</a:t></a:r><a:r><a:rPr lang=\"en-US\"/><a:t>tle }} for {{ items.length }} items</a:t></a:r></a:p><a:p><a:r><a:rPr lang=\"en-US\"/><a:t>static line</a:t></a:r></a:p></xdr:txBody></xdr:sp>";
            AddPart(wb, "/xl/drawings/drawing1.xml", "application/vnd.openxmlformats-officedocument.drawing+xml", DrawingXml(Anchor(2, 1, 6, 4, box)));
            string rid = Link(wb, s.PartName!, "drawing", "../drawings/drawing1.xml");
            Frame(s, "drawing", "<drawing xmlns=\"" + Main + "\" xmlns:r=\"" + R + "\" r:id=\"" + rid + "\"/>");
        }, items, wb => Parts(wb, "xl/drawings/drawing"));

        yield return new("image-at-cell", wb =>
        {
            Worksheet s = First(wb);
            Put(s, "A1", "Logo"); Put(s, "B2", "{% image logo width=40 alt=logo %}");
        }, "{\"logo\":\"" + Png + "\"}", wb => Parts(wb, "xl/drawings/drawing") + "\n" + Rels(wb, "/xl/drawings/drawing1.xml") + "|" + Rels(wb, "/xl/worksheets/sheet1.xml"));

        yield return new("image-per-row", wb =>
        {
            Worksheet s = First(wb);
            Put(s, "A1", "{% for it in items %}{{ it.name }}"); Put(s, "B1", "{% image it.pic height=30 %}{% endfor %}");
        }, "{\"items\":[{\"name\":\"a\",\"pic\":\"" + Png + "\"},{\"name\":\"b\",\"pic\":\"" + Png + "\"},{\"name\":\"c\",\"pic\":\"nope\"}]}", wb => Parts(wb, "xl/drawings/drawing") + "\n" + string.Join(";", wb.Package.Parts.Where(p => p.Name.StartsWith("/xl/media/", StringComparison.Ordinal)).Select(p => p.Name)));
    }
}
