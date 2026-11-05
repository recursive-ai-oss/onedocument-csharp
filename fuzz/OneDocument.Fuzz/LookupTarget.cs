using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RecursiveAI.OneDocument.Calc;

namespace RecursiveAI.OneDocument.Fuzz;

/// <summary>
/// The lookup, reference, dynamic-array and LAMBDA-helper functions. The input picks a function, builds up to three small arrays of mixed
/// values (numbers, text, logicals, errors) and scalar arguments from its bytes, and evaluates the call with small limits. Only a typed
/// <see cref="OfficeException"/> may escape, a result is never a reference, and the result is the same on a second run.
/// </summary>
internal static class LookupTarget
{
    private static readonly string[] Calls =
    [
        "MATCH(S,A,T)", "XMATCH(S,A,T,M)", "XLOOKUP(S,A,B,S2,T,M)", "LOOKUP(S,A,B)", "VLOOKUP(S,A,N,T)", "HLOOKUP(S,A,N,T)", "CHOOSECOLS(A,N,N2)",
        "CHOOSEROWS(A,N,N2)", "FILTER(A,B,S)", "SORT(A,N,N2,T)", "SORTBY(A,B,N,B,N2)", "UNIQUE(A,T,M)", "TAKE(A,N,N2)", "DROP(A,N,N2)",
        "EXPAND(A,N,N2,S)", "HSTACK(A,B,S)", "VSTACK(A,B,S)", "TOCOL(A,N,T)", "TOROW(A,N,T)", "WRAPROWS(A,N,S)", "WRAPCOLS(A,N,S)", "TRANSPOSE(A)",
        "RANDARRAY(N,N2,S,S2,T)", "MAP(A,B,LAMBDA(x,y,x&y))", "REDUCE(S,A,LAMBDA(a,v,a&v))", "SCAN(S,A,LAMBDA(a,v,a&v))", "BYROW(A,LAMBDA(r,ROWS(r)))",
        "BYCOL(A,LAMBDA(c,COLUMNS(c)))", "MAKEARRAY(N,N2,LAMBDA(r,c,r+c))", "GROUPBY(A,B,SUM,N,N2)", "PIVOTBY(A,B,A,SUM)", "INDEX(A,N,N2)",
        "OFFSET(B7,N,N2,N,N2)", "INDIRECT(S,T)", "ADDRESS(N,N2,N,T,S)", "ROW(A)", "COLUMN(B7:D9)", "AREAS(A)", "TRIMRANGE(A,N,N2)", "HYPERLINK(S,S2)",
    ];

    private sealed class GridResolver : ICalcResolver
    {
        public CalcValue GetCell(string? sheet, int row, int column)
        {
            int h = unchecked((row * 31) + (column * 17));
            return (h & 7) switch
            {
                0 => CalcValue.Blank,
                1 => CalcValue.FromText("t" + (h & 15).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                2 => CalcValue.FromBoolean((h & 8) != 0),
                3 => CalcValue.FromError(CalcError.NA),
                _ => CalcValue.FromNumber(h % 100),
            };
        }
    }

    private static string Scalar(ref ReadOnlySpan<byte> d)
    {
        byte b = Take(ref d);
        return (b & 7) switch
        {
            0 => "\"" + (char)('a' + ((b >> 3) % 6)) + (((b & 0x80) != 0) ? "*" : string.Empty) + "\"",
            1 => "TRUE",
            2 => "#N/A",
            3 => "\"\"",
            _ => ((sbyte)Take(ref d) / 8).ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
    }

    private static string Array(ref ReadOnlySpan<byte> d)
    {
        int rows = 1 + (Take(ref d) % 4);
        int cols = 1 + (Take(ref d) % 4);
        var sb = new StringBuilder("{");
        for (int r = 0; r < rows; r++)
        {
            if (r > 0)
            {
                sb.Append(';');
            }

            for (int c = 0; c < cols; c++)
            {
                if (c > 0)
                {
                    sb.Append(',');
                }

                sb.Append(Scalar(ref d));
            }
        }

        return sb.Append('}').ToString();
    }

    private static byte Take(ref ReadOnlySpan<byte> d)
    {
        if (d.Length == 0)
        {
            return 0;
        }

        byte b = d[0];
        d = d[1..];
        return b;
    }

    private static string Number(ref ReadOnlySpan<byte> d) => (((sbyte)Take(ref d)) / 16).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string Build(ReadOnlySpan<byte> data)
    {
        string call = Calls[data[0] % Calls.Length];
        ReadOnlySpan<byte> d = data[1..];
        string a = Array(ref d);
        string b = Array(ref d);
        var sb = new StringBuilder();
        for (int i = 0; i < call.Length; i++)
        {
            char c = call[i];
            bool word = i + 1 >= call.Length || !char.IsLetterOrDigit(call[i + 1]);
            bool start = i == 0 || !char.IsLetterOrDigit(call[i - 1]);
            if (start && word && char.IsUpper(c) && (c is 'A' or 'B' or 'S' or 'T' or 'M' or 'N'))
            {
                sb.Append(c switch { 'A' => a, 'B' => b, 'S' => Scalar(ref d), _ => Number(ref d) });
            }
            else if (start && i + 1 < call.Length && char.IsDigit(call[i + 1]) && c is 'S' or 'N')
            {
                sb.Append(c == 'S' ? Scalar(ref d) : Number(ref d));
                i++;
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    public static void Run(ReadOnlySpan<byte> data)
    {
        if (data.Length < 3)
        {
            return;
        }

        string text = Build(data);
        if (!FormulaParser.TryParse(text, out FormulaNode? n, out _, new FormulaParserOptions { FileForm = false }) || n is null)
        {
            return;
        }

        var limits = new OfficeLimits { MaxRecursionDepth = 32, MaxAllocationBytes = 1 << 20 };
        string first = Eval(n, limits);
        string second = Eval(n, limits);
        if (first != second && !text.Contains("RANDARRAY", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("not repeatable: " + text);
        }
    }

    private static string Eval(FormulaNode n, OfficeLimits limits)
    {
        try
        {
            var ev = new Evaluator(new GridResolver(), limits: limits, options: new EvaluatorOptions { MaxSteps = 200_000 });
            CalcValue v = ev.Evaluate(n, new CalcCell("Sheet1", 7, 5));
            if (v.Kind == ValueKind.Reference)
            {
                throw new InvalidOperationException("Evaluate returned a reference");
            }

            return v.Kind == ValueKind.Array ? v.Array!.Rows + "x" + v.Array.Columns + ":" + v.Array[0, 0] : v.ToString();
        }
        catch (OfficeException)
        {
            return "typed";
        }
    }

    public static int WriteSeeds(string dir)
    {
        Directory.CreateDirectory(dir);
        for (int i = 0; i < Calls.Length; i++)
        {
            File.WriteAllBytes(Path.Combine(dir, $"c-{i}"), [(byte)i, 3, 3, 0, 9, 17, 25, 1, 34, 2, 3, 3, 12, 20, 28, 36, 44, 52, 60, 68, 76, 84, 92, 100, 108, 116]);
        }

        return 0;
    }
}
