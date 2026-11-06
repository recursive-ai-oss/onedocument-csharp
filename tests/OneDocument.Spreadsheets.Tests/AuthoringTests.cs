using System.Text;
using RecursiveAI.OneDocument.Inspection;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Spreadsheets.Tests;

public class AuthoringTests
{
    // A 1 x 1 pixel PNG.
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static byte[] Save(Workbook wb)
    {
        using var ms = new MemoryStream();
        wb.SaveAs(ms, OfficeFormat.Xlsx);
        return ms.ToArray();
    }

    [Fact]
    public void RangeValues_RoundTripBulkAndThroughTheFile()
    {
        using var wb = Workbook.Create();
        Worksheet sheet = wb.Sheets.Add("Data");
        SheetRange range = sheet.Range("B2:D3");
        Assert.Equal(2, range.RowCount);
        Assert.Equal(3, range.ColumnCount);
        range.Values = new object?[,] { { "name", 1, 2.5 }, { true, null, new DateTime(2024, 3, 1) } };

        object?[,] read = range.Values;
        Assert.Equal("name", read[0, 0]);
        Assert.Equal(1.0, read[0, 1]);
        Assert.Equal(2.5, read[0, 2]);
        Assert.Equal(true, read[1, 0]);
        Assert.Null(read[1, 1]);
        Assert.Equal(new DateTime(2024, 3, 1), read[1, 2]);
        Assert.Equal("name", sheet["B2"].Text);

        using Workbook back = Workbook.Open(new MemoryStream(Save(wb)));
        Assert.Equal("name", back.Sheets["Data"].Range("B2").Values[0, 0]);
        Assert.Equal(2.5, back.Sheets["Data"]["D2"].Number);
    }

    [Fact]
    public void RangeValues_BadInput_ThrowsAndWritesNothing()
    {
        using var wb = Workbook.Create();
        Worksheet sheet = wb.Sheets.Add("S");
        SheetRange range = sheet.Range("A1:B1");
        Assert.Throws<ArgumentException>(() => range.Values = new object?[,] { { 1 } });                 // wrong size
        Assert.Throws<ArgumentException>(() => range.Values = new object?[,] { { 1, new object() } });   // unknown type
        Assert.Throws<ArgumentException>(() => range.Values = new object?[,] { { 1, double.NaN } });
        Assert.Throws<ArgumentException>(() => range.Values = new object?[,] { { 1, "lone\ud800" } });
        Assert.False(sheet["A1"].Exists);                                                                 // nothing was written
        Assert.Throws<FormatException>(() => sheet.Range("not a range"));
        Assert.Throws<InvalidOperationException>(() => sheet.Range("A1:XFD1048576").Values.GetLength(0));
    }

    [Fact]
    public void RangeStyle_NumberFormat_AndClear()
    {
        using var wb = Workbook.Create();
        Worksheet sheet = wb.Sheets.Add("S");
        SheetRange range = sheet.Range("A1:B2");
        range.Values = new object?[,] { { 1.5, 2.25 }, { 3, 4 } };
        range.NumberFormat = "0.00";
        Assert.Equal("1.50", sheet["A1"].FormattedText);
        Assert.Equal("4.00", sheet["B2"].FormattedText);
        range.UpdateStyle(s => s with { Font = (s.Font ?? new CellFont()) with { Bold = true } });
        Assert.True(sheet["B1"].Style.Font.Bold);
        Assert.Equal("0.00", sheet["B1"].Style.NumberFormatCode);                      // the number format survived the font change
        range.SetStyle(new CellStyleSpec { NumberFormatCode = "0%" });
        Assert.Equal("0%", range.NumberFormat);

        Assert.Equal(4, range.Clear());
        Assert.False(sheet["A1"].Exists);
        Assert.Equal(0, sheet.Range("A1:B2").Clear());
    }

    [Fact]
    public void RangeMerge_KeepsTheTopLeftValue_AndRefusesOverlaps()
    {
        using var wb = Workbook.Create();
        Worksheet sheet = wb.Sheets.Add("S");
        sheet.Range("A1:C2").Values = new object?[,] { { "top", 2, 3 }, { 4, 5, 6 } };
        sheet.Range("A1:C2").Merge();
        Assert.Equal("top", sheet["A1"].Text);
        Assert.True(sheet["B1"].Value.IsEmpty);
        Assert.True(sheet["C2"].Value.IsEmpty);
        Assert.Contains(sheet.MergedCells, m => m.ToString() == "A1:C2");
        Assert.Throws<InvalidOperationException>(() => sheet.Range("C2:D3").Merge());
        Assert.Throws<InvalidOperationException>(() => sheet.Range("E5").Merge());

        using Workbook back = Workbook.Open(new MemoryStream(Save(wb)));
        Assert.Contains(back.Sheets["S"].MergedCells, m => m.ToString() == "A1:C2");
        Assert.True(sheet.Range("A1:C2").Unmerge());
    }

    [Fact]
    public void AddPicture_StoresMediaDrawingAndRelationships()
    {
        using var wb = Workbook.Create();
        Worksheet sheet = wb.Sheets.Add("S");
        int first = sheet.AddPicture(Png, "B2", 64, 32, "logo");
        int second = sheet.AddPicture(Png, "D4");
        Assert.NotEqual(first, second);

        byte[] bytes = Save(wb);
        using OpcPackage package = OpcPackage.Open(new MemoryStream(bytes));
        Assert.Single(package.Parts, p => p.Name.StartsWith("/xl/drawings/drawing", StringComparison.Ordinal) && p.Name.EndsWith(".xml", StringComparison.Ordinal));
        Assert.Equal(2, package.Parts.Count(p => p.Name.StartsWith("/xl/media/", StringComparison.Ordinal)));
        string drawing = Encoding.UTF8.GetString(package.Parts.Single(p => p.Name.StartsWith("/xl/drawings/drawing", StringComparison.Ordinal) && p.Name.EndsWith(".xml", StringComparison.Ordinal)).GetBytes().Span);
        Assert.Contains("descr=\"logo\"", drawing, StringComparison.Ordinal);
        Assert.Contains("cx=\"609600\"", drawing, StringComparison.Ordinal);                           // 64 px
        Assert.Contains("<drawing", Encoding.UTF8.GetString(package.FindPart(sheet.PartName!)!.GetBytes().Span), StringComparison.Ordinal);

        Assert.Throws<ArgumentException>(() => sheet.AddPicture([1, 2, 3, 4], "A1"));
        Assert.Throws<ArgumentException>(() => sheet.AddPicture(Png, "A1", -5));
        Assert.Throws<FormatException>(() => sheet.AddPicture(Png, "nope"));
    }

    [Fact]
    public void AuthoringEdits_LeaveOtherPartsByteIdentical()
    {
        byte[] original = TestBook.Bytes();
        byte[] edited;
        using (Workbook wb = TestBook.Open(original))
        {
            wb.Sheets["Other"].Range("B2:B3").Values = new object?[,] { { "x" }, { 2 } };
            edited = Save(wb);
        }

        using OpcPackage a = OpcPackage.Open(new MemoryStream(original));
        using OpcPackage b = OpcPackage.Open(new MemoryStream(edited));
        foreach (IPackagePart part in a.Parts)
        {
            if (part.Name is "/xl/worksheets/sheet2.xml" or "/xl/sharedStrings.xml" or "/xl/workbook.xml" or "/xl/calcChain.xml" or "/xl/styles.xml")
            {
                continue;
            }

            Assert.True(b.FindPart(part.Name) is { } now && now.GetBytes().Span.SequenceEqual(part.GetBytes().Span), part.Name + " changed");
        }
    }

    [Fact]
    public void Sanitize_RunsInMemory_AndTheWorkbookStaysUsable()
    {
        OpcPackage package = OpcPackage.Open(new MemoryStream(TestBook.Bytes()));
        package.AddPart("/docProps/core.xml", "application/vnd.openxmlformats-package.core-properties+xml", Encoding.UTF8.GetBytes(
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><dc:creator>Jane Doe</dc:creator></cp:coreProperties>"));
        package.SetRelationships(null, [.. package.GetRelationships(), new Relationship("rId9", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties", "docProps/core.xml")]);
        using Workbook wb = Workbook.FromPackage(package, ownsPackage: true);
        wb.Sheets["Data"]["A9"].SetNumber(42);
        byte[] sheet2 = package.FindPart("/xl/worksheets/sheet2.xml")!.GetBytes().ToArray();

        SanitizeReport report = wb.Sanitize();

        Assert.Contains(report.Removed, r => r.Category == InspectionCategory.Properties);
        Assert.Equal(42, wb.Sheets["Data"]["A9"].Number);                                       // the pending edit is in the sanitised workbook
        Assert.Equal("First", wb.Sheets["Data"]["A1"].Text);
        Assert.True(package.FindPart("/xl/worksheets/sheet2.xml")!.GetBytes().Span.SequenceEqual(sheet2));
        wb.Sheets["Data"]["A10"].SetNumber(1);
        byte[] saved = Save(wb);
        Assert.Equal(0, Inspector.Inspect(new MemoryStream(saved)).CountOf(InspectionCategory.Properties));
    }
}
