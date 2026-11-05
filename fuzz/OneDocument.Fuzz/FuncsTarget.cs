using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Calc;

namespace RecursiveAI.OneDocument.Fuzz;

/// <summary>
/// Calls of the math, logical, information and text functions with random arguments. The input picks a function from the registry (sorted by
/// name), an argument count within its limits and a value for each argument (numbers from a table of edge values or raw doubles, text, booleans,
/// errors, blanks, small arrays, cell references into a fixed grid). The call runs with small limits; only a typed <see cref="OfficeException"/>
/// may escape, a result is never a reference or a non-finite number, and a non-volatile call gives the same answer twice.
/// </summary>
internal static class FuncsTarget
{
    private static readonly string[] Names = BuildNames();

    private static readonly double[] Edge = [0, 1, -1, 0.5, -0.5, 2, 3, 10, 100, 255, 256, 1e15, -1e15, 1e-15, 1e100, 1e308, 4294967296, 9007199254740992, 170, 171, 3999, 4000, 36, 37];

    private static readonly string[] Words = ["", "a", "abc", "ABC", " a  b ", "1,234.5", "12%", "x*y", "a?c", "~*", "[0-9]+", "(", "a++", "$1", "-", "MMXX", "ff", "\u00e9\u00df", "\ud83d\ude00", "TRUE", ">=5", "<>a"];

    private static string[] BuildNames()
    {
        var skip = new HashSet<string>(StringComparer.Ordinal) { "LET", "LAMBDA", "MAP", "MAKEARRAY", "ANCHORARRAY", "SINGLE" };
        return FunctionRegistry.CreateDefault().Definitions.Select(d => d.Name).Where(n => !skip.Contains(n)).OrderBy(n => n, StringComparer.Ordinal).ToArray();
    }

    private sealed class Grid : ICalcResolver
    {
        public CalcValue GetCell(string? sheet, int row, int column) => ((row + column) % 5) switch
        {
            0 => CalcValue.FromNumber(row * 1.5),
            1 => CalcValue.FromText("t" + column.ToString(CultureInfo.InvariantCulture)),
            2 => CalcValue.Blank,
            3 => CalcValue.FromBoolean(row % 2 == 0),
            _ => CalcValue.FromNumber(-column),
        };
    }

    private sealed class Reader(ReadOnlySpan<byte> data)
    {
        private readonly byte[] _d = data.ToArray();
        private int _p;

        public byte Next() => _p < _d.Length ? _d[_p++] : (byte)0;

        public double Double()
        {
            var b = new byte[8];
            for (int i = 0; i < 8; i++)
            {
                b[i] = Next();
            }

            double v = BitConverter.ToDouble(b);
            return double.IsFinite(v) ? v : 0;
        }
    }

    public static void Run(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2 || Names.Length == 0)
        {
            return;
        }

        var rd = new Reader(data);
        string name = Names[((rd.Next() << 8) | rd.Next()) % Names.Length];
        var registry = FunctionRegistry.CreateDefault();
        registry.TryGet(name, out FunctionDefinition? def);
        int max = Math.Min(def!.MaxArgs, def.MinArgs + 4);
        int count = def.MinArgs + (rd.Next() % (max - def.MinArgs + 1));
        var sb = new StringBuilder(name).Append('(');
        for (int i = 0; i < count; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            Arg(rd, sb, 0);
        }

        sb.Append(')');
        if (!FormulaParser.TryParse(sb.ToString(), out FormulaNode? node, out _, new FormulaParserOptions { FileForm = false }) || node is null)
        {
            return;
        }

        try
        {
            var limits = new OfficeLimits { MaxRecursionDepth = 32, MaxAllocationBytes = 4 << 20, RegexMatchTimeoutMilliseconds = 50 };
            var options = new EvaluatorOptions { MaxSteps = 100_000, RandomSeed = 1 };
            CalcValue v = new Evaluator(new Grid(), registry, limits, options).Evaluate(node, new CalcCell("Sheet1", 3, 2));
            if (v.Kind == ValueKind.Reference)
            {
                throw new InvalidOperationException("a call must not return a reference: " + sb);
            }

            if (v.Kind == ValueKind.Number && !double.IsFinite(v.Number))
            {
                throw new InvalidOperationException("a number result must be finite: " + sb);
            }

            if (!def.IsVolatile && v.IsScalar)
            {
                CalcValue again = new Evaluator(new Grid(), registry, limits, options).Evaluate(node, new CalcCell("Sheet1", 3, 2));
                if (!v.Equals(again))
                {
                    throw new InvalidOperationException("evaluation is not deterministic: " + sb);
                }
            }
        }
        catch (OfficeException)
        {
            // A typed limit is the right answer to a call that asks for too much.
        }
    }

    private static void Arg(Reader rd, StringBuilder sb, int depth)
    {
        byte k = rd.Next();
        switch (k % 10)
        {
            case 0:
                sb.Append(Edge[rd.Next() % Edge.Length].ToString("R", CultureInfo.InvariantCulture));
                break;
            case 1:
                sb.Append(rd.Double().ToString("R", CultureInfo.InvariantCulture));
                break;
            case 2:
            case 3:
                sb.Append('"').Append(Words[rd.Next() % Words.Length].Replace("\"", "\"\"", StringComparison.Ordinal)).Append('"');
                break;
            case 4:
                sb.Append((rd.Next() & 1) == 0 ? "TRUE" : "FALSE");
                break;
            case 5:
                sb.Append((rd.Next() % 4) switch { 0 => "#N/A", 1 => "#DIV/0!", 2 => "#VALUE!", _ => "#NUM!" });
                break;
            case 6:
                break; // an omitted argument
            case 7:
                sb.Append("A1:B3");
                break;
            case 8:
                sb.Append((rd.Next() % 3) switch { 0 => "C2", 1 => "D:D", _ => "A1:C1" });
                break;
            default:
                if (depth > 0)
                {
                    sb.Append('0');
                    break;
                }

                sb.Append('{');
                int rows = 1 + (rd.Next() % 2);
                int cols = 1 + (rd.Next() % 3);
                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        if (c > 0)
                        {
                            sb.Append(',');
                        }

                        int before = sb.Length;
                        Arg(rd, sb, depth + 1);
                        if (sb.Length == before || sb[before] is 'A' or 'C' or 'D' or '{')
                        {
                            sb.Length = before;
                            sb.Append('1');
                        }
                    }

                    if (r < rows - 1)
                    {
                        sb.Append(';');
                    }
                }

                sb.Append('}');
                break;
        }
    }

    public static int WriteSeeds(string dir)
    {
        Directory.CreateDirectory(dir);
        for (int i = 0; i < Names.Length; i++)
        {
            File.WriteAllBytes(Path.Combine(dir, $"f-{i}"), [(byte)(i >> 8), (byte)i, 1, 0, 3, 0, 2, 5, 9, 1, 0, 0, 4, 1, 7, 2, 3, 1]);
        }

        return 0;
    }
}
