using RecursiveAI.OneDocument.Spreadsheets.Calc;

namespace RecursiveAI.OneDocument.Spreadsheets.Tests;

public class CorpusGateCalcTests
{
    private static Worksheet Sheet(out Workbook wb)
    {
        wb = Workbook.Create();
        return wb.Sheets[0];
    }

    [Fact]
    public void SumifsOverWholeColumnsAcrossSheets()
    {
        Worksheet a = Sheet(out Workbook wb);
        using (wb)
        {
            Worksheet v = wb.Sheets.Add("Valuation");
            v["D2"].SetNumber(5);
            v["E2"].SetNumber(39050);
            v["D3"].SetNumber(7);
            v["E3"].SetNumber(39051);
            a["A5"].SetNumber(39050);
            a["AAB5"].Formula = "SUMIFS(Valuation!D:D,Valuation!E:E,A5)";
            wb.RecalculateAll();
            Assert.Equal(5, a["AAB5"].Value.Number);
        }
    }

    [Fact]
    public void IsFormulaIsTrueForACellWithAFormula()
    {
        Worksheet a = Sheet(out Workbook wb);
        using (wb)
        {
            a["A1"].Formula = "1+1";
            a["B1"].Formula = "IF(A1>0,\"x\",\"\")";
            a["C1"].Formula = "ISFORMULA(A1)";
            a["D1"].Formula = "ISFORMULA(B1)";
            a["E1"].Formula = "ISFORMULA(Z9)";
            a["F1"].Formula = "IF(AND(ISFORMULA(A1),ISFORMULA(B1))=TRUE,0,1)";
            wb.RecalculateAll();
            Assert.True(a["C1"].Value.Boolean);
            Assert.True(a["D1"].Value.Boolean);
            Assert.False(a["E1"].Value.Boolean);
            Assert.Equal(0, a["F1"].Value.Number);
        }
    }

    [Fact]
    public void XlookupOfAnEmptyLookupValueIsNotAnArgumentError()
    {
        Worksheet a = Sheet(out Workbook wb);
        using (wb)
        {
            Worksheet d = wb.Sheets.Add("Data");
            d["G2"].SetNumber(1);
            d["H2"].SetNumber(10);
            a["C2"].Formula = "IF(XLOOKUP(B2,Data!G:G,Data!H:H,\"none\")=0,\"\",\"found\")";
            wb.RecalculateAll();
            Assert.NotEqual(CellValueKind.Error, a["C2"].Value.Kind);
        }
    }

    [Fact]
    public void CriteriaRangesOfDifferentSizesAreValueErrorsWithoutReadingThem()
    {
        Worksheet a = Sheet(out Workbook wb);
        using (wb)
        {
            a["A1"].SetNumber(1);
            a["C1"].SetNumber(2);
            a["AAB5"].Formula = "MAXIFS(C1:C9960,A1:ZW9960,1)";
            a["AAB6"].Formula = "COUNTIFS(C1:C9960,1,A1:ZW9960,1)";
            a["AAB7"].Formula = "SUMIFS(C1:C9960,A1:ZW9960,1)";
            wb.RecalculateAll();
            Assert.Equal(CellValueKind.Error, a["AAB5"].Value.Kind);
            Assert.Equal(CellError.Value, a["AAB5"].Value.Error);
            Assert.Equal(CellError.Value, a["AAB6"].Value.Error);
            Assert.Equal(CellError.Value, a["AAB7"].Value.Error);
        }
    }

    [Fact]
    public void TableNameAloneIsItsDataRows()
    {
        Worksheet a = Sheet(out Workbook wb);
        using (wb)
        {
            a["D1"].SetString("DoW");
            a["E1"].SetString("Dayname");
            a["D2"].SetNumber(1);
            a["E2"].SetString("Sunday");
            a["D3"].SetNumber(2);
            a["E3"].SetString("Monday");
            a.Tables.Add("Dow", CellRange.Parse("D1:E3"));
            a["A1"].Formula = "VLOOKUP(2,Dow,2)";
            a["A2"].Formula = "ROWS(Dow)";
            wb.RecalculateAll();
            Assert.Equal("Monday", a.GetText(a["A1"].Value));
            Assert.Equal(2, a["A2"].Value.Number);
        }
    }

    [Fact]
    public void ArrayConstantDefinedNameWorksInVlookup()
    {
        Worksheet a = Sheet(out Workbook wb);
        using (wb)
        {
            wb.Names.Add("Dow", "{1,\"Sunday\";2,\"Monday\"}");
            a["A1"].Formula = "VLOOKUP(2,Dow,2)";
            wb.RecalculateAll();
            Assert.Equal("Monday", a.GetText(a["A1"].Value));
        }
    }
}
