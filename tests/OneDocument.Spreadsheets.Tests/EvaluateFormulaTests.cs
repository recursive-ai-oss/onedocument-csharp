using RecursiveAI.OneDocument.Calc;

namespace RecursiveAI.OneDocument.Spreadsheets.Tests;

public class EvaluateFormulaTests
{
    [Fact]
    public void AFormulaReadsStoredValuesAndRelativeReferencesMove()
    {
        using Workbook wb = Workbook.Create();
        Worksheet s = wb.Sheets[0];
        s["A1"].SetNumber(2);
        s["A2"].SetNumber(5);
        s["B1"].SetNumber(10);
        s["B2"].SetNumber(20);
        s["C1"].Formula = "A1*100";
        s["C1"].SetNumber(7);   // the stored result is 7; nothing is recalculated

        // Written for A1, evaluated for A2: the reference moves down one row.
        CalcValue moved = wb.EvaluateFormula("A1+B1", s, 2, 1, 1, 1);
        Assert.Equal(25, moved.Number);
        CalcValue same = wb.EvaluateFormula("A1+B1", s, 1, 1, 1, 1);
        Assert.Equal(12, same.Number);
        // A formula cell is read as stored.
        Assert.Equal(7, wb.EvaluateFormula("C1", s, 1, 1, 1, 1).Number);
        Assert.True(wb.EvaluateFormula("MOD(ROW(),2)=0", s, 2, 1, 1, 1).Boolean);
        Assert.Equal(CalcError.Name, wb.EvaluateFormula("(((", s, 1, 1, 1, 1).Error);
        Assert.Equal(7, s["C1"].Number);
    }
}
