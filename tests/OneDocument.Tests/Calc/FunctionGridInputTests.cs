using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using RecursiveAI.OneDocument.Calc;
using Xunit;

namespace RecursiveAI.OneDocument.Tests.Calc;

/// <summary>
/// The committed per-function grids hold inputs only (Excel answers them later). This walks every formula once to make sure each one parses,
/// evaluates to a value or an Excel error without throwing, and does not run away.
/// </summary>
public class FunctionGridInputTests
{
    private static string GridDir([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "..", "testdata", "calc", "function-grids"));

    private static FakeResolver DatabaseCells()
    {
        var r = new FakeResolver();
        foreach (string line in File.ReadAllLines(Path.Combine(GridDir(), "database.cells.tsv")))
        {
            string[] p = line.Split('\t');
            if (p.Length < 2)
            {
                continue;
            }

            CalcValue v = p[1].Length == 0 ? CalcValue.Blank
                : double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? CalcValue.FromNumber(d)
                : p[1] == "=TRUE" ? CalcValue.True : CalcValue.FromText(p[1]);
            r.Set(p[0], v);
        }

        return r;
    }

    [Theory]
    [InlineData("financial.fgrid")]
    [InlineData("engineering.fgrid")]
    [InlineData("database.fgrid")]
    public void EveryInputEvaluates(string file)
    {
        FakeResolver res = DatabaseCells();
        int count = 0;
        var worst = TimeSpan.Zero;
        foreach (string line in File.ReadAllLines(Path.Combine(GridDir(), file)))
        {
            string[] p = line.Split('\t');
            string formula = p[2][1..];
            var sw = Stopwatch.StartNew();
            CalcValue v;
            try
            {
                v = CalcTestHelper.Eval(formula, res);
            }
            catch (Exception ex)
            {
                throw new Xunit.Sdk.XunitException(p[0] + " " + formula + ": " + ex.GetType().Name + " " + ex.Message);
            }

            worst = sw.Elapsed > worst ? sw.Elapsed : worst;
            Assert.True(v.Kind != ValueKind.Reference, formula);
            if (v.Kind == ValueKind.Number)
            {
                Assert.True(double.IsFinite(v.Number), formula);
            }

            count++;
        }

        Assert.True(count > 100);
        Assert.True(worst < TimeSpan.FromSeconds(5), "slowest input took " + worst);
    }
}
