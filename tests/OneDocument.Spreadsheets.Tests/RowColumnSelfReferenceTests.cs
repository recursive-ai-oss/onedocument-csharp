using RecursiveAI.OneDocument.Spreadsheets.Calc;

namespace RecursiveAI.OneDocument.Spreadsheets.Tests;

public class RowColumnSelfReferenceTests
{
    [Fact]
    public void RowAndColumnOfTheCellItselfAreNotCircular()
    {
        // ROW(A2) in A2 and COLUMN(C5) in C5 use only the position of the reference, never its value, so Excel does not call them circular.
        using Workbook wb = Workbook.Create();
        Worksheet ws = wb.Sheets.Add("S");
        ws["A2"].Formula = "ROW(A2)";
        ws["C5"].Formula = "COLUMN(C5)";
        ws["D1"].Formula = "ROW(A2:B9)+COLUMN(D1)";
        RecalcResult r = wb.RecalculateAll();
        Assert.Equal(0, r.CircularCells);
        Assert.Equal(2, ws["A2"].Value.Number);
        Assert.Equal(3, ws["C5"].Value.Number);
        Assert.Equal(6, ws["D1"].Value.Number);
    }

    [Fact]
    public void AddressWithAnEmptyA1ArgumentIsA1Style()
    {
        // ADDRESS(1,2,,,"S T"): the third and fourth arguments are empty, which means absolute and A1 style, not R1C1.
        using Workbook wb = Workbook.Create();
        Worksheet ws = wb.Sheets.Add("S T");
        ws["A1"].Formula = "ADDRESS(1,2,,,\"S T\")";
        ws["A2"].Formula = "ADDRESS(1,2,4,FALSE)";
        wb.RecalculateAll();
        Assert.Equal("'S T'!$B$1", ws.GetText(ws["A1"].Value));
        Assert.Equal("R[1]C[2]", ws.GetText(ws["A2"].Value));
    }
}
