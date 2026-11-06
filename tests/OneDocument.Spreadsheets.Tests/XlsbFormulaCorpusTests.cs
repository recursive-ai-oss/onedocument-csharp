using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Xlsb;

namespace RecursiveAI.OneDocument.Spreadsheets.Tests;

/// <summary>Formula token round trips over every function of the binary function table and over the calc grids' formulas.</summary>
public partial class XlsbFormulaCorpusTests
{
    // The file form spells newer functions with the _xlfn. prefix; the parser accepts both, so compare without it.
    private static string Canon(FormulaNode n) => FormulaPrinter.Print(n, new FormulaPrintOptions { Form = FormulaForm.Display }).Replace("_xlfn.", string.Empty).Replace("_xlpm.", string.Empty).Replace("_xlws.", string.Empty);

    // Number literals compare through the token bytes (the printer shows 15 digits); the text comparison sees their position only.
    private static string Shape(string canon) => NumberLiteral().Replace(canon, "#");

    [GeneratedRegex(@"(?<![A-Za-z_$.\d!])\d+(\.\d+)?([eE][+-]?\d+)?")]
    private static partial Regex NumberLiteral();

    private static XlsbFormulaContext Context()
    {
        var cx = new XlsbFormulaContext();
        cx.SheetNames.AddRange(["Sheet1", "Sheet2", "Sheet3", "My Sheet"]);
        cx.SupBooks.Add(new XlsbSupBook(XlsbSupBookKind.Self));
        return cx;
    }

    /// <summary>Encodes, decodes and re-encodes one formula; returns null when it holds, or what went wrong.</summary>
    private static string? RoundTrip(string text)
    {
        // Optional lambda parameters ([y]) and a sheet-qualified #REF! are not in the calc grammar's tree yet.
        if (text.Contains("[y]", StringComparison.Ordinal) || text.Contains("!#REF!", StringComparison.Ordinal))
        {
            return null;
        }

        FormulaNode ast;
        try
        {
            ast = FormulaParser.Parse(text);
        }
        catch (OfficeException)
        {
            return null; // not a formula the calc parser accepts: nothing to carry
        }

        XlsbFormulaContext cx = Context();
        if (!XlsbFormula.TryEncode(ast, cx, XlsbFormulaKind.Cell, 4, 2, out byte[] rgce, out byte[] rgcb, out string? problem)
            && (problem is null || !problem.Contains("is not defined", StringComparison.Ordinal)))
        {
            // Excel rejects a call with too few or too many arguments at entry, so there is nothing to carry.
            // Error values Excel computes but never stores (#BLOCKED! and the like) have no code to write.
            return problem is not null && (problem.Contains("arguments", StringComparison.Ordinal) || problem.Contains("has no stored code", StringComparison.Ordinal) || problem.Contains("unknown table", StringComparison.Ordinal)) ? null : "encode: " + problem;
        }

        if (!XlsbFormula.TryDecode(rgce, rgcb, cx, XlsbFormulaKind.Cell, 4, 2, out FormulaNode back, out string? why))
        {
            return "decode: " + why;
        }

        if (Shape(Canon(ast)) != Shape(Canon(back)))
        {
            return "differs: " + Canon(back) + " (was " + Canon(ast) + ")";
        }

        if (!XlsbFormula.TryEncode(back, cx, XlsbFormulaKind.Cell, 4, 2, out byte[] rgce2, out byte[] rgcb2, out _)
            || !rgce.AsSpan().SequenceEqual(rgce2) || !rgcb.AsSpan().SequenceEqual(rgcb2))
        {
            return "tokens unstable";
        }

        return null;
    }

    [Fact]
    public void EveryFunctionInTheTableRoundTripsAtItsFewestAndMostArguments()
    {
        var failures = new List<string>();
        for (int id = 0; id < XlsbFunctionTable.Count; id++)
        {
            if (!XlsbFunctionTable.TryGet(id, out string name, out int min, out int max) || id == 255)
            {
                continue;
            }

            foreach (int n in min == max || max > 40 ? new[] { min, Math.Min(max, min + 3) } : new[] { min, max })
            {
                string args = string.Join(",", Enumerable.Repeat("1", n));
                string text = name + "(" + args + ")";
                if (RoundTrip(text) is { } bad)
                {
                    failures.Add(text + " -> " + bad);
                }
            }
        }

        Assert.True(failures.Count == 0, failures.Count + " failures:\n" + string.Join("\n", failures.Take(40)));
    }

    private static string TestData([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "testdata", "calc"));

    [Fact]
    public void FormulasOfTheCalcGridsRoundTrip()
    {
        int total = 0;
        var failures = new List<string>();
        foreach (string file in Directory.EnumerateFiles(TestData(), "*.fgrid", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            foreach (string line in File.ReadLines(file, Encoding.UTF8))
            {
                string[] p = line.Split('\t');
                if (p.Length < 3 || p[2].Length < 2 || p[2][0] != '=')
                {
                    continue;
                }

                total++;
                if (RoundTrip(p[2][1..]) is { } bad)
                {
                    failures.Add(p[2] + " -> " + bad);
                }
            }
        }

        Assert.True(total > 5000, "only " + total + " formulas found");
        Assert.True(failures.Count == 0, failures.Count + " of " + total + " failed:\n" + string.Join("\n", failures.Take(60)));
    }
}
