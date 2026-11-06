using System.Runtime.CompilerServices;
using RecursiveAI.OneDocument.Schema.Spreadsheet;

namespace RecursiveAI.OneDocument.Schema.Tests;

/// <summary>
/// Small parts we wrote for every root vocabulary added beyond WordprocessingML and PresentationML (testdata/schema): each is read
/// through the generated content models, and edits through the views change only the bytes they mean to.
/// </summary>
public sealed class FixtureTests
{
    private static string Dir([CallerFilePath] string here = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "testdata", "schema"));

    private static string Read(string name) => File.ReadAllText(Path.Combine(Dir(), name));

    public static TheoryData<string> Fixtures() => new(
        "sml-worksheet.xml", "sml-workbook.xml", "sml-styles.xml", "sml-sharedStrings.xml", "sml-table.xml", "sml-pivotCacheDefinition.xml",
        "sml-comments.xml", "dml-chart.xml", "cx-chartex.xml", "dgm-data.xml", "dsp-drawing.xml", "xdr-drawing.xml", "dml-theme.xml",
        "bibliography.xml", "customXml-props.xml", "threaded-comments.xml", "cdr-userShapes.xml", "rd-richdata.xml");

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void EveryElementOfTheFixtureIsReachedThroughADeclaredMember(string name)
    {
        var xml = Read(name);
        var tree = TextTree.Parse(xml);
        var root = tree.RootRef;
        var kind = ViewFactory.RootKind(root.Namespace, root.LocalName);
        Assert.NotEqual(ViewKind.None, kind);
        var counts = new WalkTests.Counts();
        WalkTests.Walk(root, kind, counts);
        // The worksheet carries one vendor extension we invented, to show unknown content staying reachable; nothing else is unknown.
        var expectedUnknown = name == "sml-worksheet.xml" ? 1 : 0;
        Assert.True(counts.Unknown == expectedUnknown, name + ": unknown " + string.Join("; ", counts.UnknownNames.Keys));
        Assert.Equal(xml, tree.Serialize());
    }

    private static (TextTree Tree, string Xml) Load(string name) { var xml = Read(name); return (TextTree.Parse(xml), xml); }

    private static void AssertOnlyThisChanged(string xml, string actual, string was, string now)
    {
        Assert.Contains(was, xml, StringComparison.Ordinal);
        Assert.Equal(xml.Replace(was, now, StringComparison.Ordinal), actual);
    }

    [Fact]
    public void WorksheetEditsChangeOnlyTheEditedBytes()
    {
        var (tree, xml) = Load("sml-worksheet.xml");
        var ws = Worksheet.Create(tree.RootRef);
        var cell = ws.SheetData!.Value.Rows.ToArray()[0].Cells.ToArray()[1];
        Assert.Equal("B1", cell.R);
        cell.S = 5;
        var row = ws.SheetData!.Value.Rows.ToArray()[1];
        row.Ht = 33.5;
        // A new child goes where the content model puts it: sheetProtection follows sheetData and precedes mergeCells.
        ws.GetOrAddSheetProtection().Sheet = true;
        var actual = tree.Serialize();
        var at = actual.IndexOf("<sheetProtection sheet=\"true\"/>", StringComparison.Ordinal);
        Assert.True(at > actual.IndexOf("</sheetData>", StringComparison.Ordinal) && at < actual.IndexOf("<mergeCells", StringComparison.Ordinal));
        var expected = xml.Replace("<c r=\"B1\" s=\"1\">", "<c r=\"B1\" s=\"5\">", StringComparison.Ordinal).Replace("customHeight=\"1\" ht=\"20\"", "customHeight=\"1\" ht=\"33.5\"", StringComparison.Ordinal);
        Assert.Equal(expected, actual.Remove(at, "<sheetProtection sheet=\"true\"/>".Length));
    }

    [Fact]
    public void WorkbookSheetRenameChangesOneAttribute()
    {
        var (tree, xml) = Load("sml-workbook.xml");
        var wb = Spreadsheet.Workbook.Create(tree.RootRef);
        var sheet = wb.Sheets!.Value.SheetList.ToArray()[0];
        Assert.Equal("Sales", sheet.Name);
        sheet.Name = "Revenue";
        AssertOnlyThisChanged(xml, tree.Serialize(), "<sheet name=\"Sales\"", "<sheet name=\"Revenue\"");
    }

    [Fact]
    public void StylesheetNumberFormatEditChangesOneAttribute()
    {
        var (tree, xml) = Load("sml-styles.xml");
        var styles = Stylesheet.Create(tree.RootRef);
        var fmt = styles.NumberFormats!.Value.NumberFormatList.ToArray()[0];
        Assert.Equal(164, fmt.NumFmtId);
        fmt.FormatCode = "0.0";
        AssertOnlyThisChanged(xml, tree.Serialize(), "formatCode=\"0.00&quot; kg&quot;\"", "formatCode=\"0.0\"");
    }

    [Fact]
    public void ChartSpaceEditChangesOneAttribute()
    {
        var (tree, xml) = Load("dml-chart.xml");
        var space = Chart.ChartSpace.Create(tree.RootRef);
        var date1904 = space.Date1904!.Value;
        Assert.False(date1904.Val);
        date1904.Val = true;
        AssertOnlyThisChanged(xml, tree.Serialize(), "<c:date1904 val=\"0\"/>", "<c:date1904 val=\"true\"/>");
        Assert.Single(space.Chart!.Value.PlotArea!.Value.BarCharts.ToArray());
    }

    [Fact]
    public void ChartExSeriesEditChangesOneAttribute()
    {
        var (tree, xml) = Load("cx-chartex.xml");
        var space = ChartEx.ChartSpace.Create(tree.RootRef);
        var series = space.Chart!.Value.PlotArea!.Value.PlotAreaRegion!.Value.SeriesEntries.ToArray()[0];
        series.Hidden = true;
        AssertOnlyThisChanged(xml, tree.Serialize(), "uniqueId=\"{00000000-0000-0000-0000-000000000001}\">", "uniqueId=\"{00000000-0000-0000-0000-000000000001}\" hidden=\"true\">");
    }

    [Fact]
    public void DiagramPointEditKeepsTheRest()
    {
        var (tree, xml) = Load("dgm-data.xml");
        var model = Diagram.DataModel.Create(tree.RootRef);
        var pt = model.PtLst!.Value.Points.ToArray()[1];
        pt.ModelIdText = "{00000000-0000-0000-0000-0000000000AA}";
        AssertOnlyThisChanged(xml, tree.Serialize(), "<dgm:pt modelId=\"{00000000-0000-0000-0000-000000000011}\">", "<dgm:pt modelId=\"{00000000-0000-0000-0000-0000000000AA}\">");
    }

    [Fact]
    public void SpreadsheetDrawingAnchorEditKeepsTheRest()
    {
        var (tree, xml) = Load("xdr-drawing.xml");
        var drawing = SpreadsheetDrawing.WorksheetDrawing.Create(tree.RootRef);
        Assert.Single(drawing.TwoCellAnchors.ToArray());
        Assert.Single(drawing.OneCellAnchors.ToArray());
        var from = drawing.TwoCellAnchors.ToArray()[0].From!.Value;
        from.Col!.Value.Node.SetText("3");
        AssertOnlyThisChanged(xml, tree.Serialize(), "<xdr:col>1</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>4", "<xdr:col>3</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>4");
    }

    [Fact]
    public void ThemeBibliographyAndThreadedCommentsEditKeepTheRest()
    {
        var (tree, xml) = Load("dml-theme.xml");
        Drawing.OfficeStyleSheet.Create(tree.RootRef).Name = "Renamed";
        AssertOnlyThisChanged(xml, tree.Serialize(), "name=\"Office Theme\"", "name=\"Renamed\"");

        (tree, xml) = Load("bibliography.xml");
        Bibliography.Sources.Create(tree.RootRef).StyleName = "MLA";
        AssertOnlyThisChanged(xml, tree.Serialize(), "StyleName=\"APA\"", "StyleName=\"MLA\"");

        (tree, xml) = Load("threaded-comments.xml");
        var list = ThreadedComments.ThreadedComments.Create(tree.RootRef).ThreadedCommentList.ToArray();
        Assert.Equal(2, list.Length);
        list[1].Ref = "B2";
        Assert.Contains("ref=\"B2\" dT=\"2024-01-01T11:00:00.00\"", tree.Serialize(), StringComparison.Ordinal);
    }

    [Fact]
    public void ExtensionContentInsideAnExtensionListStaysReachable()
    {
        var (tree, _) = Load("sml-worksheet.xml");
        var ext = Worksheet.Create(tree.RootRef).ExtensionList!.Value.Extensions.ToArray();
        Assert.Equal(2, ext.Length);
        MarkupRef groups = default;
        foreach (var c in ext[0].Children) { groups = c; break; }
        Assert.Equal(SchemaNamespace.Spreadsheet2009, groups.Namespace);
        Assert.Equal("sparklineGroups", groups.LocalNameText);
        var kind = ViewFactory.RootKind(groups.Namespace, groups.LocalName);
        Assert.NotEqual(ViewKind.None, kind);
    }
}
