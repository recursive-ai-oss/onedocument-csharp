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
            a["B5"].Formula = "SUMIFS(Valuation!D:D,Valuation!E:E,A5)";
            wb.RecalculateAll();
            Assert.Equal(5, a["B5"].Value.Number);
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
