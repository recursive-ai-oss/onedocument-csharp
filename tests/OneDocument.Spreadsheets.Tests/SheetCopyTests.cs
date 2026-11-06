using RecursiveAI.OneDocument.Operations;

namespace RecursiveAI.OneDocument.Spreadsheets.Tests;

public class SheetCopyTests
{
    // Source workbook: Data (values, formulas over Data and Other, a name, a table, a merge, widths, a validation and a conditional format) and Other.
    private static Workbook Source()
    {
        var wb = Workbook.Create();
        Worksheet data = wb.Sheets[0];
        data.Rename("Data");
        Worksheet other = wb.Sheets.Add("Other");
        other["A1"].SetNumber(10);
        other["A2"].SetString("from other");
        data["A1"].SetNumber(1);
        data["A2"].SetNumber(2);
        data["A3"].Formula = "SUM(A1:A2)";
        data["B1"].SetString("label");
        data["B1"].StyleIndex = wb.Styles.GetOrAdd(new CellStyleSpec { Font = wb.Styles.GetFont(0) with { Bold = true }, NumberFormatCode = "0.00" });
        data["C1"].Formula = "Other!A1*2";
        data["C2"].Formula = "Data!A1+1";
        data["C3"].Formula = "Rate*3";
        data["C4"].Formula = "Other!A2&\"!\"";
        data["D1"].Formula = "Local+1";
        wb.Names.Add("Rate", "Other!$A$1");
        wb.Names.Add("Local", "Data!$A$2", data);
        data["E1"].SetString("Item");
        data["F1"].SetString("Qty");
        data["E2"].SetString("a");
        data["F2"].SetNumber(3);
        data["E3"].SetString("b");
        data["F3"].SetNumber(4);
        data.Tables.Add("Items", new CellRange(new CellAddress(1, 5), new CellAddress(3, 6)));
        data["H1"].SetNumber(5);
        data.Merge(new CellRange(new CellAddress(5, 1), new CellAddress(6, 2)));
        data.SetColumns([new ColumnProperties(1, 1, 22.5, 0, ColumnOptions.None)]);
        data.GetRow(2).Height = 30;
        data.Validations.Add(new CellRange(new CellAddress(1, 8), new CellAddress(5, 8)), DataValidationSpec.WholeBetween(1, 10));
        wb.Recalculate();
        return wb;
    }

    private static Workbook Destination()
    {
        var wb = Workbook.Create();
        wb.Sheets[0].Rename("Existing");
        wb.Sheets[0]["A1"].SetNumber(99);
        wb.Names.Add("Rate", "Existing!$A$1");
        return wb;
    }

    private static Workbook Reopen(Workbook wb)
    {
        wb.CommitChanges();
        var ms = new MemoryStream();
        wb.SaveAs(ms, OfficeFormat.Xlsx);
        ms.Position = 0;
        return Workbook.Open(ms);
    }

    [Fact]
    public void Copy_ToAnotherWorkbook_KeepsValuesAndFormulasRecalculateToTheSame()
    {
        using Workbook src = Source();
        using Workbook dst = Destination();
        SheetCopyResult r = src.Sheets["Data"].CopyTo(dst);
        Worksheet copy = r.Sheet;
        Assert.Equal("Data", copy.Name);
        Assert.Equal(1, r.Report.CountOf("sheets"));

        // Cached values arrive as they were; nothing was recalculated.
        foreach (string a in new[] { "A1", "A2", "A3", "B1", "C1", "C2", "C3", "C4", "D1", "E2", "F3" })
        {
            Assert.Equal(src.Sheets["Data"][a].Value.Kind, copy[a].Value.Kind);
            Assert.Equal(src.Sheets["Data"][a].Text, copy[a].Text);
        }

        // After a recalculation of the destination the copy still calculates to what the source calculated to.
        dst.Recalculate();
        Assert.Equal(3, copy["A3"].Number);
        Assert.Equal(20, copy["C1"].Number);
        Assert.Equal(2, copy["C2"].Number);
        Assert.Equal("from other!", copy["C4"].Text);
        Assert.Equal(3, copy["D1"].Number);
        // Rate exists in the destination with another definition (99), and the copy uses that.
        Assert.Equal(297, copy["C3"].Number);
        Assert.Equal("Data!$A$2", dst.Names.Find("Local", copy)!.Formula);
    }

    [Fact]
    public void Copy_RepointsSheetsThatStayBehindAsAnExternalLinkAndKeepsTheirValues()
    {
        using Workbook src = Source();
        using Workbook dst = Destination();
        SheetCopyResult r = src.Sheets["Data"].CopyTo(dst, new SheetCopyOptions { SourceLinkTarget = "book.xlsx" });
        Assert.Equal("[1]Other!A1*2", r.Sheet["C1"].Formula);
        Assert.Equal("Data!A1+1", r.Sheet["C2"].Formula);
        Assert.Single(dst.ExternalLinks);
        Assert.Equal("book.xlsx", dst.ExternalLinks[0].Target);
        Assert.Equal(["Other"], dst.ExternalLinks[0].SheetNames.ToArray());
        Assert.True(r.Report.Loss.CountOf("merge.external-references") > 0);

        using Workbook again = Reopen(dst);
        Assert.Equal("[1]Other!A1*2", again.Sheets["Data"]["C1"].Formula);
        Assert.Single(again.ExternalLinks);
        again.Recalculate();
        Assert.Equal(20, again.Sheets["Data"]["C1"].Number);
    }

    [Fact]
    public void Copy_NamesAreAddedAndAConflictingNameKeepsTheDestinationsDefinition()
    {
        using Workbook src = Source();
        using Workbook dst = Destination();
        SheetCopyResult r = src.Sheets["Data"].CopyTo(dst);
        Assert.Equal("Existing!$A$1", dst.Names.Find("Rate")!.Formula);
        Assert.Contains(r.Report.Resolutions, x => x.Code == "merge.name-conflict" && x.Item == "Rate");
        Assert.NotNull(dst.Names.Find("Local", r.Sheet));
        Assert.Null(dst.Names.Find("Local", dst.Sheets[0]));

        using Workbook empty = Workbook.Create();
        SheetCopyResult r2 = src.Sheets["Data"].CopyTo(empty);
        Assert.Equal("[1]Other!$A$1", empty.Names.Find("Rate")!.Formula);
    }

    [Fact]
    public void Copy_StylesTablesMergesColumnsRowsAndValidationsArrive()
    {
        using Workbook src = Source();
        using Workbook dst = Destination();
        SheetCopyResult r = src.Sheets["Data"].CopyTo(dst);
        Worksheet copy = r.Sheet;
        Assert.Equal(src.Sheets["Data"]["B1"].Style.Font, copy["B1"].Style.Font);
        Assert.Equal("0.00", copy["B1"].Style.NumberFormatCode);
        Assert.True(copy["B1"].Style.Font.Bold);
        Assert.Equal("Items", Assert.Single(copy.Tables).Name);
        Assert.Equal(new CellRange(new CellAddress(1, 5), new CellAddress(3, 6)), copy.Tables["Items"]!.Range);
        Assert.Equal(src.Sheets["Data"].MergedCells, copy.MergedCells);
        Assert.Equal(22.5, copy.Columns[0].Width);
        Assert.Equal(30, copy.GetRow(2).Height);
        Assert.Single(copy.Validations);
        Assert.Equal(1, r.Report.CountOf("tables"));
    }

    [Fact]
    public void Copy_WithinTheWorkbook_RenamesTheCopyAndFollowsItsOwnName()
    {
        using Workbook src = Source();
        SheetCopyResult r = src.Sheets["Data"].CopyTo(src);
        Assert.Equal("Data (2)", r.Sheet.Name);
        Assert.Equal("'Data (2)'!A1+1", r.Sheet["C2"].Formula);
        Assert.Equal("Other!A1*2", r.Sheet["C1"].Formula);
        Assert.Empty(src.ExternalLinks);
        Assert.Equal("Items_2", Assert.Single(r.Sheet.Tables).Name);
        Assert.Contains(r.Report.Resolutions, x => x.Code == "merge.sheet-renamed");
        Assert.Contains(r.Report.Resolutions, x => x.Code == "merge.table-renamed");
        src.Recalculate();
        Assert.Equal(2, r.Sheet["C2"].Number);
    }

    [Fact]
    public void Move_RemovesTheSourceSheetAndCountsReferencesLeftBehind()
    {
        using Workbook src = Source();
        src.Sheets["Other"]["B1"].Formula = "Data!A1";
        using Workbook dst = Destination();
        SheetCopyResult r = src.Sheets["Data"].MoveTo(dst);
        Assert.False(src.Sheets.TryGet("Data", out _));
        Assert.Equal("Data", r.Sheet.Name);
        Assert.Contains(r.Report.Problems, p => p.Code == "merge.references-left");
    }

    [Fact]
    public void Copy_ReportsWhatHangsOffTheSheetAndWasNotCopied()
    {
        // A sheet with a drawing relationship: the picture is not copied, and the report says so.
        using Workbook src = Source();
        Worksheet data = src.Sheets["Data"];
        src.Package.AddPart("/xl/drawings/drawing1.xml", "application/vnd.openxmlformats-officedocument.drawing+xml", "<d/>"u8.ToArray());
        src.Package.SetRelationships(data.PartName, [new RecursiveAI.OneDocument.Packaging.Relationship("rId9", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing", "../drawings/drawing1.xml")]);
        using Workbook dst = Destination();
        SheetCopyResult r = data.CopyTo(dst);
        Assert.Contains(r.Report.Problems, p => p.Code == "merge.sheet-part-not-copied" && p.Message.Contains("drawing", StringComparison.Ordinal));
    }

    [Fact]
    public void Copy_AFormulaThatCannotBeReadIsCopiedAsWrittenWithAProblem()
    {
        using Workbook src = Source();
        src.Sheets["Data"]["J1"].Formula = "SUM(1,";
        using Workbook dst = Destination();
        SheetCopyResult r = src.Sheets["Data"].CopyTo(dst);
        Assert.Equal("SUM(1,", r.Sheet["J1"].Formula);
        Assert.Contains(r.Report.Problems, p => p.Code == "merge.formula-unreadable" && p.Location.Address == "J1");
    }

    [Fact]
    public void Copy_BringsTheNamedCellStylesTheCellsBuildOn()
    {
        using Workbook src = Source();
        src.Styles.AddNamedStyle("Money", new CellStyleSpec { NumberFormatCode = "#,##0.00", Font = src.Styles.GetFont(0) with { Italic = true } });
        src.Sheets["Data"]["K1"].SetNumber(12.5);
        src.Sheets["Data"]["K1"].StyleIndex = src.Styles.GetOrAdd(new CellStyleSpec { StyleName = "Money", NumberFormatCode = "#,##0.00", Font = src.Styles.GetFont(0) with { Italic = true } });
        using Workbook dst = Destination();
        SheetCopyResult r = src.Sheets["Data"].CopyTo(dst);
        Assert.Contains(dst.Styles.CellStyles, c => c.Name == "Money");
        Assert.Equal("Money", r.Sheet["K1"].Style.StyleName);
        Assert.Equal("#,##0.00", r.Sheet["K1"].Style.NumberFormatCode);
        Assert.True(r.Sheet["K1"].Style.Font.Italic);
    }

    [Fact]
    public void Copy_WithoutADestinationIsRefused()
    {
        using Workbook src = Source();
        Worksheet data = src.Sheets["Data"];
        Assert.Throws<ArgumentNullException>(() => data.CopyTo(null!));
    }
}

public class WorkbookSplitTests
{
    [Fact]
    public void SplitBySheet_GivesOneWorkbookPerWorksheetWithTheValuesAndExternalReferences()
    {
        using var wb = Workbook.Create();
        wb.Sheets[0].Rename("A");
        Worksheet b = wb.Sheets.Add("B");
        wb.Sheets[0]["A1"].SetNumber(4);
        wb.Sheets[0]["A2"].Formula = "A1*2+B!A1";
        b["A1"].SetNumber(1);
        wb.Recalculate();
        using WorkbookSplitResult r = wb.SplitBySheet("all.xlsx");
        Assert.Equal(["A", "B"], r.Pieces.Select(p => p.SheetName).ToArray());
        Workbook first = r.Pieces[0].Workbook;
        Assert.Single(first.Sheets);
        Assert.Equal("A", first.Sheets[0].Name);
        Assert.Equal(9, first.Sheets[0]["A2"].Number);
        Assert.Equal("A1*2+[1]B!A1", first.Sheets[0]["A2"].Formula);
        first.Recalculate();
        Assert.Equal(9, first.Sheets[0]["A2"].Number);
        Assert.Single(r.Pieces[1].Workbook.Sheets);
        Assert.Empty(r.Pieces[1].Workbook.ExternalLinks);
    }
}
