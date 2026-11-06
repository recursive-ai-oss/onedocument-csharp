using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Templating;
using Xunit;

namespace RecursiveAI.OneDocument.Tests.Calc;

/// <summary>Inputs the fuzz campaign found, reduced to the formulas or templates that triggered them.</summary>
public class FuzzRegressionTests
{
    // Past 2^53 a double does not advance by one, so the period-by-period loop never ended.
    [Theory]
    [InlineData("CUMIPMT(0.01,1E+300,1E+300,1E+300,1E+300,1)")]
    [InlineData("CUMPRINC(0.01,1E+300,1E+300,1E+300,1E+300,0)")]
    [InlineData("CUMIPMT(1E+300,1E+300,1E+300,1E+300,1E+300,1)")]
    public void CumulativePaymentsWithHugePeriodsReturnPromptly(string formula)
    {
        CalcValue v = CalcTestHelper.Eval(formula);
        Assert.True(v.Kind is ValueKind.Number or ValueKind.Error, formula);
        if (v.Kind == ValueKind.Number)
        {
            Assert.True(double.IsFinite(v.Number));
        }
    }

    [Fact]
    public void PercentOfAnOverflowingQuotientIsANumberError()
    {
        Assert.Equal("#NUM!", CalcTestHelper.Show(CalcTestHelper.Eval("PERCENTOF(256,4.4E-323)")));
    }

    // A whole-column reference in a template read a million variable names per column.
    [Fact]
    public void TemplateWholeColumnRangeIsBounded()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _ = TemplateEngine.RenderText("Ha {{ N:ak }}", new Dictionary<string, object?> { ["n"] = 3 });
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), sw.Elapsed.ToString());
    }
}
