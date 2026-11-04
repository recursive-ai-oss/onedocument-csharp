using RecursiveAI.OneOffice.Diagnostics;

namespace RecursiveAI.OneOffice.Spreadsheets.Tests;

public class WorkbookTests
{
    [Fact]
    public void OpenListsSheetsWithoutReadingThem()
    {
        using Workbook wb = TestBook.Open();
        Assert.Equal(2, wb.Sheets.Count);
        Assert.Equal("Data", wb.Sheets[0].Name);
        Assert.Equal(SheetState.Hidden, wb.Sheets["other"].State);
        Assert.Equal(5, wb.Sheets[1].SheetId);
        Assert.False(wb.Sheets[0].IsLoaded);
        Assert.Equal(OfficeFormat.Xlsx, wb.Format);
    }

    [Fact]
    public void ReadsValuesAndFormulas()
    {
        using Workbook wb = TestBook.Open();
        Worksheet s = wb.Sheets["Data"];
        Assert.Equal("First", s["A1"].Text);
        Assert.Equal(1.5, s["B1"].Number);
        Assert.Equal(1, s["B1"].StyleIndex);
        Assert.True(s["C1"].Value.Boolean);
        Assert.Equal(CellError.DivideByZero, s["D1"].Value.Error);
        Assert.Equal("B1*2", s["A2"].Formula);
        Assert.Equal(3, s["A2"].Number);
        Assert.Equal("helloxx & more", s["B2"].Text);
        Assert.Equal(" inline ", s["C2"].Text);
        Assert.Equal(CellFormulaKind.Array, s["D2"].FormulaDetail!.Kind);
        Assert.Equal("D2:D3", s["D2"].FormulaDetail!.Reference);
        Assert.Equal(CellFormulaKind.Shared, s["B4"].FormulaDetail!.Kind);
        Assert.Equal("", s["B4"].Formula);
        Assert.Equal(0, s["B4"].FormulaDetail!.SharedIndex);
        Assert.False(s["Z9"].Exists);
        Assert.True(s["Z9"].Value.IsEmpty);
        Assert.Equal(CellValueKind.Empty, s["A3"].Value.Kind);
        Assert.Equal("A1:D4", s.UsedRange().ToString());
        Assert.Equal(10, s.Store.CellCount);
    }

    [Fact]
    public void ReadsRowColumnMergeAndPaneProperties()
    {
        using Workbook wb = TestBook.Open();
        Worksheet s = wb.Sheets[0];
        Assert.Equal(20, s.GetRow(1).Height);
        Assert.True(s.GetRow(2).Hidden);
        Assert.Equal(1, s.GetRow(2).OutlineLevel);
        Assert.Equal(-1, s.GetRow(3).Height);
        Assert.Equal(2, s.Columns.Count);
        Assert.Equal(12.5, s.Columns[0].Width);
        Assert.Equal(2, s.Columns[0].Max);
        Assert.True((s.Columns[1].Flags & ColumnOptions.Hidden) != 0);
        Assert.Equal(2, s.Columns[1].Style);
        Assert.Equal("A5:B6", Assert.Single(s.MergedCells).ToString());
        Assert.True(s.TryGetFreezePane(out int rows, out int cols));
        Assert.Equal((2, 1), (rows, cols));
        Assert.Equal("A1:D4", s.DeclaredDimension.ToString());
    }

    [Fact]
    public void UntouchedSaveKeepsEveryPartIdentical()
    {
        byte[] original = TestBook.Bytes();
        using Workbook wb = TestBook.Open(original);
        _ = wb.Sheets[0]["A1"].Text; // reading must not dirty anything
        byte[] saved = TestBook.Save(wb);
        Assert.Equal(TestBook.PartHashes(original), TestBook.PartHashes(saved));
    }

    [Fact]
    public void ForcedRewriteReadsBackEqualAndKeepsUnknownMarkup()
    {
        byte[] original = TestBook.Bytes();
        using Workbook wb = TestBook.Open(original);
        wb.Sheets[0].Rewrite();
        byte[] saved = TestBook.Save(wb);
        var before = TestBook.PartHashes(original);
        var after = TestBook.PartHashes(saved);
        Assert.NotEqual(before["/xl/worksheets/sheet1.xml"], after["/xl/worksheets/sheet1.xml"]);
        foreach (string name in before.Keys.Where(k => k != "/xl/worksheets/sheet1.xml"))
        {
            Assert.Equal(before[name], after[name]);
        }

        using Workbook again = TestBook.Open(saved);
        Worksheet a = wb.Sheets[0];
        Worksheet b = again.Sheets[0];
        Assert.Equal(a.Store.CellCount, b.Store.CellCount);
        foreach (CellRowView row in a.Store.EnumerateRows())
        {
            for (int i = 0; i < row.Count; i++)
            {
                Cell ca = a[row.Row, row.ColumnAt(i)];
                Cell cb = b[row.Row, row.ColumnAt(i)];
                Assert.Equal(ca.Text, cb.Text);
                Assert.Equal(ca.Value.Kind, cb.Value.Kind);
                Assert.Equal(ca.StyleIndex, cb.StyleIndex);
                Assert.Equal(ca.FormulaDetail, cb.FormulaDetail);
            }
        }

        Assert.Equal(20, b.GetRow(1).Height);
        Assert.Equal("A5:B6", Assert.Single(b.MergedCells).ToString());

        string xml = System.Text.Encoding.UTF8.GetString(PartBytes(saved, "/xl/worksheets/sheet1.xml"));
        Assert.Contains("mc:Ignorable=\"x14ac\"", xml, StringComparison.Ordinal);
        Assert.Contains("x14ac:dyDescent=\"0.25\"", xml, StringComparison.Ordinal);
        Assert.Contains("<extLst><ext uri=\"{abc}\"", xml, StringComparison.Ordinal);
        Assert.Contains("<extLst><ext uri=\"{x}\"/></extLst>", xml, StringComparison.Ordinal);
        Assert.Contains(" cm=\"1\"", xml, StringComparison.Ordinal);
        Assert.Contains("<pageMargins", xml, StringComparison.Ordinal);
        Assert.True(xml.IndexOf("<cols>", StringComparison.Ordinal) < xml.IndexOf("<sheetData>", StringComparison.Ordinal));
        Assert.True(xml.IndexOf("<mergeCells", StringComparison.Ordinal) > xml.IndexOf("</sheetData>", StringComparison.Ordinal));
    }

    [Fact]
    public void SingleCellEditChangesOnlyItsSheetAndStalesNothing()
    {
        byte[] original = TestBook.Bytes();
        using Workbook wb = TestBook.Open(original);
        wb.Sheets[1]["A1"].SetNumber(8);
        byte[] saved = TestBook.Save(wb);
        var before = TestBook.PartHashes(original);
        var after = TestBook.PartHashes(saved);
        Assert.Equal(before.Keys.OrderBy(k => k), after.Keys.OrderBy(k => k));
        string[] changed = before.Keys.Where(k => before[k] != after[k]).ToArray();
        Assert.Equal(["/xl/worksheets/sheet2.xml"], changed);
        using Workbook again = TestBook.Open(saved);
        Assert.Equal(8, again.Sheets[1]["A1"].Number);
    }

    [Fact]
    public void SettingStringsAppendsToSharedTable()
    {
        byte[] original = TestBook.Bytes();
        using Workbook wb = TestBook.Open(original);
        wb.Sheets[1]["B2"].SetString("new & <text>");
        wb.Sheets[1]["B3"].SetString("new & <text>");
        wb.Sheets[1]["B4"].SetString(" lead\u0001");
        byte[] saved = TestBook.Save(wb);
        var before = TestBook.PartHashes(original);
        var after = TestBook.PartHashes(saved);
        Assert.NotEqual(before["/xl/sharedStrings.xml"], after["/xl/sharedStrings.xml"]);
        Assert.Equal(before["/xl/worksheets/sheet1.xml"], after["/xl/worksheets/sheet1.xml"]);
        string sst = System.Text.Encoding.UTF8.GetString(PartBytes(saved, "/xl/sharedStrings.xml"));
        Assert.Contains("uniqueCount=\"4\"", sst, StringComparison.Ordinal);
        Assert.Contains("<rPh sb=\"0\"", sst, StringComparison.Ordinal);
        using Workbook again = TestBook.Open(saved);
        Assert.Equal("new & <text>", again.Sheets[1]["B3"].Text);
        Assert.Equal(" lead\u0001", again.Sheets[1]["B4"].Text);
        Assert.Equal("Rich text", again.SharedStrings.GetText(1));
        Assert.True(again.SharedStrings.IsRich(1));
    }

    [Fact]
    public void ClearingAFormulaDropsTheStaleCalcChain()
    {
        using Workbook wb = TestBook.Open();
        wb.Sheets[0]["A2"].Formula = null;
        byte[] saved = TestBook.Save(wb);
        Assert.DoesNotContain("/xl/calcChain.xml", TestBook.PartHashes(saved).Keys);
        using Workbook again = TestBook.Open(saved);
        Assert.Null(again.Sheets[0]["A2"].Formula);
    }

    [Fact]
    public void SettingAFormulaRequestsRecalculationWithoutCalculating()
    {
        using Workbook wb = TestBook.Open();
        Assert.False(wb.NeedsRecalculation);
        Cell c = wb.Sheets[1]["C3"];
        c.Formula = "=A1+1";
        Assert.Equal("A1+1", c.Formula);
        Assert.True(c.Value.IsEmpty);
        Assert.True(wb.NeedsRecalculation);
        byte[] saved = TestBook.Save(wb);
        Assert.Contains("fullCalcOnLoad=\"1\"", System.Text.Encoding.UTF8.GetString(PartBytes(saved, "/xl/workbook.xml")), StringComparison.Ordinal);
        Assert.Contains("/xl/calcChain.xml", TestBook.PartHashes(saved).Keys);
    }

    [Fact]
    public void SheetStructureEditsKeepNamesAndScopes()
    {
        using Workbook wb = TestBook.Open();
        Worksheet added = wb.Sheets.Insert(0, "New");
        Assert.Equal(["New", "Data", "Other"], wb.Sheets.Select(s => s.Name));
        added["A1"].SetNumber(1);
        wb.Sheets["Data"].Rename("Renamed");
        wb.Sheets.Move(wb.Sheets["Other"], 0);
        byte[] saved = TestBook.Save(wb);
        using Workbook again = TestBook.Open(saved);
        Assert.Equal(["Other", "New", "Renamed"], again.Sheets.Select(s => s.Name));
        Assert.Equal(1, again.Sheets["New"]["A1"].Number);
        Assert.Equal(7, again.Sheets["Other"]["A1"].Number);
        string wbXml = System.Text.Encoding.UTF8.GetString(PartBytes(saved, "/xl/workbook.xml"));
        Assert.Contains("localSheetId=\"0\"", wbXml, StringComparison.Ordinal); // "Other" is first now
        Assert.Contains("activeTab=\"0\"", wbXml, StringComparison.Ordinal);
        Assert.DoesNotContain("/xl/calcChain.xml", TestBook.PartHashes(saved).Keys);
        Assert.Throws<ArgumentException>(() => wb.Sheets.Add("other"));
        Assert.Throws<ArgumentException>(() => wb.Sheets.Add("bad:name"));
    }

    [Fact]
    public void RemovingASheetRemovesItsPartAndScopedNames()
    {
        using Workbook wb = TestBook.Open();
        wb.Sheets.Remove(wb.Sheets["Other"]);
        byte[] saved = TestBook.Save(wb);
        var parts = TestBook.PartHashes(saved);
        Assert.DoesNotContain("/xl/worksheets/sheet2.xml", parts.Keys);
        Assert.DoesNotContain("definedName", System.Text.Encoding.UTF8.GetString(PartBytes(saved, "/xl/workbook.xml")), StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => wb.Sheets.Remove(wb.Sheets[0]));
    }

    [Fact]
    public void CreateWritesAValidWorkbookThatReadsBack()
    {
        using Workbook wb = Workbook.Create();
        wb.Sheets[0]["B2"].SetNumber(42);
        wb.Sheets[0]["C3"].SetString("hi");
        byte[] a = TestBook.Save(wb);
        using Workbook again = TestBook.Open(a);
        Assert.Equal(42, again.Sheets["Sheet1"]["B2"].Number);
        Assert.Equal("hi", again.Sheets[0]["C3"].Text);
        using Workbook second = Workbook.Create();
        second.Sheets[0]["B2"].SetNumber(42);
        second.Sheets[0]["C3"].SetString("hi");
        Assert.Equal(a, TestBook.Save(second));
    }

    [Fact]
    public void OutOfOrderAndDuplicateCellsAreRepairedWithLocations()
    {
        string xml = "<?xml version=\"1.0\"?><worksheet xmlns=\"" + TestBook.Main + "\"><sheetData><row r=\"2\"><c r=\"B2\"><v>1</v></c><c r=\"A2\"><v>2</v></c><c r=\"A2\"><v>3</v></c></row><row r=\"1\"><c r=\"A1\"><v>4</v></c></row></sheetData></worksheet>";
        var ctx = new OpenContext();
        using Workbook wb = TestBook.Open(TestBook.Bytes(xml), ctx);
        Worksheet s = wb.Sheets[0];
        Assert.Equal(3, s["A2"].Number);
        Assert.Equal(1, s["B2"].Number);
        Assert.Equal(4, s["A1"].Number);
        string[] codes = TestBook.Defects(ctx).Select(d => d.Code).ToArray();
        Assert.Contains("xlsx.cell-out-of-order", codes);
        Assert.Contains("xlsx.cell-duplicate", codes);
        Assert.Contains("xlsx.row-out-of-order", codes);
        Assert.Equal("Data!A2", TestBook.Defects(ctx).First(d => d.Code == "xlsx.cell-duplicate").Location.ToString());
    }

    [Fact]
    public void InvalidAddressesAreDroppedAndTruncatedPartsAreTypedErrors()
    {
        string xml = "<?xml version=\"1.0\"?><worksheet xmlns=\"" + TestBook.Main + "\"><sheetData><row r=\"1\"><c r=\"ZZZZ1\"><v>1</v></c><c r=\"A1\" t=\"n\"><v>abc</v></c></row></sheetData></worksheet>";
        var ctx = new OpenContext();
        using Workbook wb = TestBook.Open(TestBook.Bytes(xml), ctx);
        Assert.Equal(1, wb.Sheets[0].Store.CellCount);
        string[] codes = TestBook.Defects(ctx).Select(d => d.Code).ToArray();
        Assert.Contains("xlsx.cell-address-invalid", codes);
        Assert.Contains("xlsx.cell-value-invalid", codes);

        string cut = TestBook.Sheet1Xml[..(TestBook.Sheet1Xml.IndexOf("<row r=\"2\"", StringComparison.Ordinal) + 20)];
        using Workbook broken = TestBook.Open(TestBook.Bytes(cut));
        OfficeFormatException ex = Assert.Throws<OfficeFormatException>(() => broken.Sheets[0]["A1"].Text);
        Assert.StartsWith("xml.", ex.Defect.Code, StringComparison.Ordinal);
        Assert.Equal("/xl/worksheets/sheet1.xml", ex.Location.Container);
    }

    [Fact]
    public void ExcelAddressesParseAndFormat()
    {
        Assert.Equal("XFD1048576", new CellAddress(1_048_576, 16_384).ToString());
        Assert.Equal(new CellAddress(2, 28), CellAddress.Parse("$AB$2"));
        Assert.False(CellAddress.TryParse("XFE1".AsSpan(), out _));
        Assert.False(CellAddress.TryParse("A1048577".AsSpan(), out _));
        Assert.False(CellAddress.TryParse("A0".AsSpan(), out _));
        Assert.Equal("AA", CellAddress.ColumnName(27));
        Assert.Equal("A1:C3", CellRange.Parse("c3:a1").ToString());
    }

    [Fact]
    public void ErrorTextRoundTripsAndUnknownErrorsStayAsText()
    {
        string xml = "<?xml version=\"1.0\"?><worksheet xmlns=\"" + TestBook.Main + "\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"e\"><v>#SPILL!</v></c><c r=\"B1\" t=\"e\"><v>#WEIRD!</v></c></row></sheetData></worksheet>";
        using Workbook wb = TestBook.Open(TestBook.Bytes(xml));
        Assert.Equal(CellError.Spill, wb.Sheets[0]["A1"].Value.Error);
        Assert.Equal(CellError.Unknown, wb.Sheets[0]["B1"].Value.Error);
        wb.Sheets[0].Rewrite();
        string saved = System.Text.Encoding.UTF8.GetString(PartBytes(TestBook.Save(wb), "/xl/worksheets/sheet1.xml"));
        Assert.Contains("#SPILL!", saved, StringComparison.Ordinal);
        Assert.Contains("#WEIRD!", saved, StringComparison.Ordinal);
    }

    [Fact]
    public void FlatOpcAndTemplateFormatsSave()
    {
        using Workbook wb = TestBook.Open();
        using var flat = new MemoryStream();
        wb.SaveAs(flat, OfficeFormat.FlatOpcWorkbook);
        Assert.Equal(OfficeFormat.FlatOpcWorkbook, FormatSniffer_Detect(flat.ToArray()));
        using var tpl = new MemoryStream();
        wb.SaveAs(tpl, OfficeFormat.Xltx);
        Assert.Equal(OfficeFormat.Xltx, FormatSniffer_Detect(tpl.ToArray()));
        Assert.Equal(OfficeFormat.Xlsx, wb.Format);
    }

    private static OfficeFormat FormatSniffer_Detect(byte[] bytes) => RecursiveAI.OneOffice.Formats.FormatSniffer.Detect(bytes);

    internal static byte[] PartBytes(byte[] file, string name)
    {
        using RecursiveAI.OneOffice.Packaging.OpcPackage p = RecursiveAI.OneOffice.Packaging.OpcPackage.Open(new MemoryStream(file));
        return p.FindPart(name)!.GetBytes().ToArray();
    }

    [Fact]
    public void SharedStringRootWithLineBreakBeforeAttributeValueSurvivesAnEdit()
    {
        // Found by fuzzing: whitespace other than a space between '=' and the quote ran the root-tag rewrite off the tag.
        string sst = "<sst xmlns=\"" + TestBook.Main + "\" uniqueCount=\n\"1\"><si><t>x</t></si></sst>";
        using Workbook wb = TestBook.Open(TestBook.Bytes(shared: sst));
        wb.SharedStrings.Add("more");
        wb.Sheets[0]["C3"].SetString("y");
        using var ms = new MemoryStream();
        wb.SaveAs(ms, OfficeFormat.Xlsx);
        using Workbook back = Workbook.Open(new MemoryStream(ms.ToArray()));
        Assert.Equal("y", back.Sheets[0]["C3"].Text);
    }
}
