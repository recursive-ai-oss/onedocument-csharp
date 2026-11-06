using RecursiveAI.OneDocument.Formats;

namespace RecursiveAI.OneDocument.Spreadsheets.Tests;

public class XlsEmptyStyleSheetTests
{
    static XlsEmptyStyleSheetTests() => SpreadsheetFormats.Register(FormatRegistry.Default);

    [Fact]
    public void WorkbookWhoseStyleSheetListsNoFillsOrBordersSavesAsXls()
    {
        // TestBook's styles part is an empty <styleSheet/> while its cells name style indices; Excel opens such files, and the BIFF8 writer
        // must give those formats the empty fill and border instead of failing.
        using Workbook wb = TestBook.Open();
        using var ms = new MemoryStream();
        wb.SaveAs(ms, OfficeFormat.Xls);
        Assert.True(ms.Length > 0);
        ms.Position = 0;
        using Workbook back = Workbook.Open(ms, leaveOpen: true);
        Assert.Equal(wb.Sheets.Count, back.Sheets.Count);
    }
}
