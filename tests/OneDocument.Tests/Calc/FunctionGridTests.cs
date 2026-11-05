using System.Globalization;
using System.Runtime.CompilerServices;
using RecursiveAI.OneDocument.Calc;

namespace RecursiveAI.OneDocument.Tests.Calc;

/// <summary>The generated per-function grids: every formula must parse and evaluate without anything but a typed result.</summary>
public class FunctionGridTests
{
    private sealed class Empty : ICalcResolver
    {
        public CalcValue GetCell(string? sheet, int row, int column) => CalcValue.Blank;
    }

    private static string GridDir([CallerFilePath] string here = "") =>
        Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "..", "testdata", "calc", "function-grid");

    [Fact]
    public void EveryGridFormulaEvaluates()
    {
        string dir = GridDir();
        int n = 0;
        var ev = new Evaluator(new Empty(), options: new EvaluatorOptions { RandomSeed = 1 });
        foreach (string f in Directory.EnumerateFiles(dir, "*.expected.tsv"))
        {
            foreach (string line in File.ReadAllLines(f))
            {
                string[] p = line.Split('\t');
                string text = p[1].Replace("\\\\", "\\", StringComparison.Ordinal).TrimStart('=');
                CalcValue v = ev.Evaluate(FormulaParser.Parse(text, new FormulaParserOptions { FileForm = false }), new CalcCell("Sheet1", 1, 2));
                Assert.NotEqual(ValueKind.Reference, v.Kind);
                if (v.Kind == ValueKind.Number)
                {
                    Assert.True(double.IsFinite(v.Number), text);
                }

                n++;
            }
        }

        Assert.True(n > 3000, n.ToString(CultureInfo.InvariantCulture));
    }
}
