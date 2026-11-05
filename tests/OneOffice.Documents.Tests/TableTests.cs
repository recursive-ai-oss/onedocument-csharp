using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.Documents.Styles;

namespace RecursiveAI.OneOffice.Documents.Tests;

public class TableTests
{
    private static string Cell(string text, string props = "") =>
        $"<w:tc>{(props.Length > 0 ? "<w:tcPr>" + props + "</w:tcPr>" : "")}<w:p><w:r><w:t>{text}</w:t></w:r></w:p></w:tc>";

    private static string Row(params string[] cells) => "<w:tr>" + string.Concat(cells) + "</w:tr>";

    private static Table Single(Document doc) => doc.Body.Blocks().OfType<Table>().First();

    private static string Grid(int n) => "<w:tblGrid>" + string.Concat(Enumerable.Repeat("<w:gridCol w:w=\"1000\"/>", n)) + "</w:tblGrid>";

    private static string Dump(LogicalTable lt)
    {
        var sb = new System.Text.StringBuilder();
        for (int r = 0; r < lt.RowCount; r++)
        {
            for (int c = 0; c < lt.ColumnCount; c++)
            {
                sb.Append(lt[r, c]?.Cell.Text ?? ".").Append(c + 1 < lt.ColumnCount ? "|" : "");
            }

            sb.Append('/');
        }

        return sb.ToString();
    }

    [Fact]
    public void Logical_ResolvesGridSpanAndVerticalMerge()
    {
        var defects = new DefectCollection();
        using Document doc = TestDocs.Open("<w:tbl>" + Grid(3) +
            Row(Cell("a", "<w:gridSpan w:val=\"2\"/>"), Cell("b")) +
            Row(Cell("c", "<w:vMerge w:val=\"restart\"/>"), Cell("d"), Cell("e")) +
            Row(Cell("", "<w:vMerge/>"), Cell("f"), Cell("g")) +
            "</w:tbl>", defects: defects);
        LogicalTable lt = Single(doc).Logical();
        Assert.Equal((3, 3), (lt.RowCount, lt.ColumnCount));
        Assert.Equal("a|a|b/c|d|e/c|f|g/", Dump(lt));
        Assert.Equal(2, lt[1, 0]!.RowSpan);
        Assert.Equal(2, lt[0, 1]!.ColumnSpan);
        Assert.Equal(new MergeCounts(1, 1, 1, 0, 0), lt.Merges);
        Assert.Equal(7, lt.Cells.Count);
        Assert.Empty(defects.ToArray());
    }

    [Fact]
    public void Logical_OrphanContinuation_IsADefect_FirstRowPlain_ElsewhereMergedUpward()
    {
        var defects = new DefectCollection();
        using Document doc = TestDocs.Open("<w:tbl>" + Grid(2) +
            Row(Cell("a", "<w:vMerge/>"), Cell("b")) +
            Row(Cell("c"), Cell("d", "<w:vMerge/>")) + "</w:tbl>", defects: defects);
        LogicalTable lt = Single(doc).Logical();
        // Observed in Word 365 (16.0.20527): the continuation under the unmerged "b" merges into it.
        Assert.Equal("a|b/c|b/", Dump(lt));
        Assert.Equal(2, lt[0, 1]!.RowSpan);
        Assert.Equal(2, lt.Merges.Orphans);
        Assert.Equal(2, defects.ToArray().Count(d => d.Code == "docx.table-merge-orphan"));
        Assert.All(defects.ToArray(), d => Assert.Equal("/word/document.xml", d.Location.Container));
    }

    // The generated case layout-vmerge-orphan; observed in Word 365 (16.0.20527): 4 cells, the first column is one cell.
    [Fact]
    public void Logical_ContinuationsUnderAnUnmergedCell_FormOneRegion()
    {
        using Document doc = TestDocs.Open("<w:tbl>" + Grid(2) +
            Row(Cell("r1c1"), Cell("r1c2")) + Row(Cell("r2c1", "<w:vMerge/>"), Cell("r2c2")) +
            Row(Cell("r3c1", "<w:vMerge w:val=\"continue\"/>"), Cell("r3c2")) + "</w:tbl>");
        LogicalTable lt = Single(doc).Logical();
        Assert.Equal(3, lt[0, 0]!.RowSpan);
        Assert.Equal(4, lt.Cells.Count);
        Assert.Equal(1, lt.Merges.Orphans);
    }

    [Fact]
    public void Logical_SpanMismatch_AndInvalidSpan_AreDefects()
    {
        var defects = new DefectCollection();
        using Document doc = TestDocs.Open("<w:tbl>" + Grid(2) +
            Row(Cell("a", "<w:vMerge w:val=\"restart\"/>"), Cell("b", "<w:gridSpan w:val=\"0\"/>")) +
            Row(Cell("", "<w:gridSpan w:val=\"2\"/><w:vMerge/>")) + "</w:tbl>", defects: defects);
        LogicalTable lt = Single(doc).Logical();
        Assert.Equal(2, lt[0, 0]!.RowSpan);
        string[] codes = defects.ToArray().Select(d => d.Code).ToArray();
        Assert.Contains("docx.table-merge-span-mismatch", codes);
        Assert.Contains("docx.table-span-invalid", codes);
    }

    [Fact]
    public void Logical_RowsWiderThanGrid_ExtendTheGrid()
    {
        var defects = new DefectCollection();
        using Document doc = TestDocs.Open("<w:tbl>" + Grid(1) + Row(Cell("a"), Cell("b")) + "</w:tbl>", defects: defects);
        Assert.Equal(2, Single(doc).Logical().ColumnCount);
        Assert.Contains(defects.ToArray(), d => d.Code == "docx.table-grid-mismatch");
    }

    [Fact]
    public void Logical_LegacyHorizontalMerge_AndGridBefore()
    {
        using Document doc = TestDocs.Open("<w:tbl>" + Grid(4) +
            "<w:tr><w:trPr><w:gridBefore w:val=\"1\"/></w:trPr>" + Cell("a", "<w:hMerge w:val=\"restart\"/>") + Cell("", "<w:hMerge/>") + Cell("b") + "</w:tr>" + "</w:tbl>");
        LogicalTable lt = Single(doc).Logical();
        Assert.Equal(".|a|a|b/", Dump(lt));
        Assert.Equal(1, lt.Merges.LegacyHorizontalContinuations);
    }

    [Fact]
    public void Nested_AndWrappers()
    {
        using Document doc = TestDocs.Open("<w:tbl>" + Grid(1) + "<w:tr><w:tc><w:tbl>" + Grid(1) + Row(Cell("inner")) + "</w:tbl><w:p/></w:tc></w:tr></w:tbl>");
        Table outer = Single(doc);
        Table inner = outer.Rows().Single().Cells().Single().Blocks().OfType<Table>().Single();
        Assert.False(outer.IsNested);
        Assert.True(inner.IsNested);
        Assert.Equal("inner", inner.GetCell(0, 0)!.Text);
    }

    [Fact]
    public void InsertRow_KeepsMergesConsistent_AndChangesOnlyTheTable()
    {
        var p = TestDocs.Package("<w:p><w:r><w:t>before</w:t></w:r></w:p><w:tbl>" + Grid(2) +
            Row(Cell("h1"), Cell("h2")) +
            Row(Cell("m", "<w:vMerge w:val=\"restart\"/>"), Cell("x")) +
            Row(Cell("", "<w:vMerge/>"), Cell("y")) + "</w:tbl>", "<w:style w:type=\"paragraph\" w:styleId=\"A\"/>");
        byte[] styles = p.FindPart("/word/styles.xml")!.GetBytes().ToArray();
        using var doc = Document.FromPackage(p);
        Table t = Single(doc);
        t.InsertRow(2); // between the two rows of the merged region: continues it
        Assert.Equal("h1|h2/m|x/m|/m|y/", Dump(t.Logical()));
        Assert.Equal(3, t.Logical()[1, 0]!.RowSpan);
        t.InsertRow(4); // after the last row: plain
        Assert.Equal(5, t.Logical().RowCount);
        Assert.Equal(1, t.Logical()[4, 0]!.RowSpan);
        t.InsertRow(0);
        Assert.Equal(6, t.Logical().RowCount);
        Assert.Equal(new MergeCounts(0, 1, 2, 0, 0), t.Logical().Merges);
        doc.CommitChanges();
        Assert.Equal(styles, p.FindPart("/word/styles.xml")!.GetBytes().ToArray());
    }

    [Fact]
    public void DeleteRow_PromotesTheNextCellOfAMerge()
    {
        using Document doc = TestDocs.Open("<w:tbl>" + Grid(2) +
            Row(Cell("m", "<w:vMerge w:val=\"restart\"/>"), Cell("x")) +
            Row(Cell("", "<w:vMerge/>"), Cell("y")) + Row(Cell("", "<w:vMerge/>"), Cell("z")) + "</w:tbl>");
        Table t = Single(doc);
        t.DeleteRow(0);
        LogicalTable lt = t.Logical();
        Assert.Equal(2, lt.RowCount);
        Assert.Equal(2, lt[0, 0]!.RowSpan);
        Assert.Equal(0, lt.Merges.Orphans);
        Assert.Throws<ArgumentOutOfRangeException>(() => t.DeleteRow(5));
        t.DeleteRow(0);
        Assert.Throws<InvalidOperationException>(() => t.DeleteRow(0));
    }

    [Fact]
    public void InsertColumn_WidensSpanningCells_AndAddsGridColumn()
    {
        using Document doc = TestDocs.Open("<w:tbl>" + Grid(3) +
            Row(Cell("a", "<w:gridSpan w:val=\"3\"/>")) + Row(Cell("b"), Cell("c"), Cell("d")) + "</w:tbl>");
        Table t = Single(doc);
        t.InsertColumn(1);
        Assert.Equal(4, t.GridColumns.Count);
        LogicalTable lt = t.Logical();
        Assert.Equal(4, lt.ColumnCount);
        Assert.Equal(4, lt[0, 0]!.ColumnSpan);
        Assert.Equal("a|a|a|a/b||c|d/", Dump(lt));
        t.InsertColumn(4); // append
        Assert.Equal("a|a|a|a|/b||c|d|/", Dump(t.Logical()));
    }

    [Fact]
    public void DeleteColumn_NarrowsOrRemovesCells()
    {
        using Document doc = TestDocs.Open("<w:tbl>" + Grid(3) +
            Row(Cell("a", "<w:gridSpan w:val=\"2\"/>"), Cell("b")) + Row(Cell("c"), Cell("d"), Cell("e")) + "</w:tbl>");
        Table t = Single(doc);
        t.DeleteColumn(1);
        Assert.Equal(2, t.GridColumns.Count);
        Assert.Equal("a|b/c|e/", Dump(t.Logical()));
        Assert.Equal(1, t.Logical()[0, 0]!.ColumnSpan);
        t.DeleteColumn(0);
        Assert.Throws<InvalidOperationException>(() => t.DeleteColumn(0));
    }

    [Fact]
    public void SetCellText_KeepsFormatting_AndEscapes()
    {
        using Document doc = TestDocs.Open("<w:tbl>" + Grid(1) +
            "<w:tr><w:tc><w:p><w:pPr><w:jc w:val=\"right\"/></w:pPr><w:r><w:rPr><w:b/></w:rPr><w:t>old</w:t></w:r><w:r><w:t>more</w:t></w:r></w:p><w:p><w:r><w:t>second</w:t></w:r></w:p></w:tc></w:tr></w:tbl>");
        Table t = Single(doc);
        t.SetCellText(0, 0, "a<b & c\nd\te");
        TableCell c = t.GetCell(0, 0)!;
        Assert.Equal("a<b & c\nd\te", c.Paragraphs().Single().Text);
        string xml = t.Markup.Node.OuterXml;
        Assert.Contains("<w:jc w:val=\"right\"/>", xml);
        Assert.Contains("<w:rPr><w:b/></w:rPr>", xml);
        Assert.DoesNotContain("second", xml);
        Assert.Throws<ArgumentOutOfRangeException>(() => t.SetCellText(3, 0, "x"));
    }

    [Fact]
    public void Properties_AndLook()
    {
        using Document doc = TestDocs.Open("<w:tbl><w:tblPr><w:tblStyle w:val=\"TS\"/><w:tblW w:w=\"5000\" w:type=\"pct\"/><w:tblLayout w:type=\"fixed\"/><w:tblLook w:val=\"04A0\"/></w:tblPr>" + Grid(1) +
            "<w:tr><w:trPr><w:tblHeader/><w:trHeight w:val=\"400\" w:hRule=\"exact\"/></w:trPr>" + Cell("x", "<w:tcW w:w=\"900\" w:type=\"dxa\"/><w:vAlign w:val=\"center\"/>") + "</w:tr></w:tbl>");
        Table t = Single(doc);
        Assert.Equal("TS", t.StyleId);
        Assert.Equal(new TableMeasure(5000, "pct"), t.Width);
        Assert.True(t.IsFixedLayout);
        // 04A0 = firstRow, firstColumn, noVBand
        Assert.Equal(new TableLookFlags(true, false, true, false, true, false), t.Look);
        TableRow row = t.Rows().Single();
        Assert.True(row.IsHeader);
        Assert.Equal((400, "exact"), (row.Height, row.HeightRule));
        TableCell cell = row.Cells().Single();
        Assert.Equal(new TableMeasure(900, "dxa"), cell.Width);
        Assert.Equal("center", cell.VerticalAlignment);
        t.StyleId = "Other";
        Assert.Equal("Other", t.StyleId);
    }

    [Fact]
    public void TableStyle_RegionsAndCellFormatting()
    {
        const string styles = "<w:style w:type=\"table\" w:styleId=\"TS\"><w:name w:val=\"TS\"/><w:tblPr><w:tblBorders><w:top w:val=\"single\" w:sz=\"4\"/><w:insideH w:val=\"single\"/></w:tblBorders></w:tblPr>" +
            "<w:tblStylePr w:type=\"firstRow\"><w:tcPr><w:shd w:val=\"clear\" w:fill=\"112233\"/></w:tcPr></w:tblStylePr>" +
            "<w:tblStylePr w:type=\"band1Horz\"><w:tcPr><w:shd w:val=\"clear\" w:fill=\"EEEEEE\"/></w:tcPr></w:tblStylePr></w:style>";
        using Document doc = TestDocs.Open("<w:tbl><w:tblPr><w:tblStyle w:val=\"TS\"/><w:tblLook w:firstRow=\"1\" w:noHBand=\"0\" w:noVBand=\"1\"/></w:tblPr>" + Grid(2) +
            Row(Cell("h1"), Cell("h2")) + Row(Cell("a"), Cell("b")) + Row(Cell("c"), Cell("d", "<w:shd w:val=\"clear\" w:fill=\"FF0000\"/>")) + Row(Cell("e"), Cell("f")) + "</w:tbl>", styles);
        Table t = Single(doc);
        LogicalTable lt = t.Logical();
        StyleSheet ss = doc.Styles;
        Assert.Equal(["wholeTable", "firstRow"], ss.GetTableRegions(t, lt, lt[0, 0]!));
        Assert.Equal(["wholeTable", "band1Horz"], ss.GetTableRegions(t, lt, lt[1, 1]!));
        Assert.Equal(["wholeTable", "band2Horz"], ss.GetTableRegions(t, lt, lt[2, 0]!));
        Assert.Equal("112233", ss.ResolveTableCell(t, lt, lt[0, 1]!).Get("shd")!.Attributes["fill"].Value);
        Assert.Equal("EEEEEE", ss.ResolveTableCell(t, lt, lt[1, 0]!).Get("shd")!.Attributes["fill"].Value);
        Assert.Equal("FF0000", ss.ResolveTableCell(t, lt, lt[2, 1]!).Get("shd")!.Attributes["fill"].Value);
        Assert.Equal("single", ss.ResolveTableCell(t, lt, lt[3, 1]!).Get("tblBorders/insideH")!.Value);
    }

    // Generated cases layout-tbllook and layout-table-regions; observed in Word 365 (16.0.20527).
    [Fact]
    public void Look_AttributesWinOverTheHexMask()
    {
        static TableLookFlags Look(string xml)
        {
            using Document d = TestDocs.Open("<w:tbl><w:tblPr>" + xml + "</w:tblPr>" + Grid(1) + Row(Cell("x")) + "</w:tbl>");
            return Single(d).Look;
        }

        Assert.True(Look("<w:tblLook w:val=\"0020\"/>").FirstRow);
        Assert.False(Look("<w:tblLook w:val=\"0020\" w:firstRow=\"0\" w:lastRow=\"0\" w:firstColumn=\"0\" w:lastColumn=\"0\" w:noHBand=\"1\" w:noVBand=\"1\"/>").FirstRow);
        TableLookFlags c2 = Look("<w:tblLook w:val=\"0600\" w:firstRow=\"1\" w:lastRow=\"0\" w:firstColumn=\"0\" w:lastColumn=\"0\" w:noHBand=\"0\" w:noVBand=\"0\"/>");
        Assert.True(c2.FirstRow);
        Assert.True(c2.BandedRows);
        Assert.True(c2.BandedColumns);
    }

    [Fact]
    public void FirstColumnRegion_FollowsThePhysicalFirstCell_AfterGridBefore()
    {
        const string styles = "<w:style w:type=\"table\" w:styleId=\"TS5\"><w:name w:val=\"TS5\"/><w:tblStylePr w:type=\"firstCol\"><w:rPr><w:b/></w:rPr></w:tblStylePr></w:style>";
        using Document doc = TestDocs.Open("<w:tbl><w:tblPr><w:tblStyle w:val=\"TS5\"/><w:tblLook w:val=\"0280\" w:firstRow=\"0\" w:lastRow=\"0\" w:firstColumn=\"1\" w:lastColumn=\"0\" w:noHBand=\"1\" w:noVBand=\"0\"/></w:tblPr>" + Grid(3) +
            Row(Cell("A", "<w:gridSpan w:val=\"2\"/>"), Cell("B")) +
            "<w:tr><w:trPr><w:gridBefore w:val=\"1\"/></w:trPr>" + Cell("C") + Cell("D") + "</w:tr>" +
            Row(Cell("E"), Cell("F"), Cell("G")) + "</w:tbl>", styles);
        Table t = Single(doc);
        LogicalTable lt = t.Logical();
        StyleSheet ss = doc.Styles;
        Assert.Contains("firstCol", ss.GetTableRegions(t, lt, lt[0, 0]!));
        Assert.DoesNotContain("firstCol", ss.GetTableRegions(t, lt, lt[0, 2]!));
        Assert.Contains("firstCol", ss.GetTableRegions(t, lt, lt[1, 1]!));
        Assert.DoesNotContain("firstCol", ss.GetTableRegions(t, lt, lt[1, 2]!));
        Assert.Contains("firstCol", ss.GetTableRegions(t, lt, lt[2, 0]!));
        Assert.DoesNotContain("firstCol", ss.GetTableRegions(t, lt, lt[2, 2]!));
    }
}
