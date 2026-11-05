using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using RecursiveAI.OneOffice.Calc;

namespace RecursiveAI.OneOffice.Tools.FunctionGrid;

/// <summary>
/// Writes the per-function test grids for the math, logical, information and text families, and compares them with Excel's answers.
/// <c>FunctionGrid gen DIR</c> writes one <c>FAMILY.fgrid</c> per family (the oracle's formula-grid input: <c>id TAB a1 TAB formula</c>) and a matching
/// <c>FAMILY.expected.tsv</c> (<c>id TAB formula TAB expected</c>) whose expected column is empty until Excel has answered.
/// <c>FunctionGrid fill DIR ANSWERS</c> copies the answers (<c>*.fgrid.tsv</c> from the oracle) into the expected columns.
/// <c>FunctionGrid compare DIR</c> evaluates every formula that has an expected value and lists the disagreements.
/// Each function gets the cross product of its argument domains (below), thinned to a fixed number of cases by a fixed stride, so the files are reproducible.
/// </summary>
internal static class Program
{
    private const int MaxPerFunction = 120;

    // Argument domains. Every list mixes typical values, boundaries, coercions (text that reads as a number, booleans), errors and array constants.
    private static readonly Dictionary<char, string[]> Domains = new()
    {
        ['N'] = ["0", "1", "-1", "2.5", "-2.5", "0.5", "1E+15", "\"3\"", "\"a\"", "\"\"", "TRUE", "FALSE", "#N/A", "{1,-2.5;3,4}", "1E-20", "12345.6789"],
        ['I'] = ["0", "1", "2", "3", "-1", "10", "2.7", "\"2\"", "TRUE", "\"x\"", "#NUM!", "{1,2,3}", "255", "3999"],
        ['S'] = ["\"abc\"", "\"Hello World\"", "\" a  b \"", "\"\"", "\"12\"", "\"A*b\"", "TRUE", "12.5", "#N/A", "{\"a\",\"bb\"}", "\"1,234.5\"", "\"x-y-z\""],
        ['P'] = ["0", "1", "2", "3", "5", "-1", "1.9", "\"2\"", "\"x\"", "20"],
        ['B'] = ["TRUE", "FALSE", "0", "1", "\"x\"", "2"],
        ['D'] = ["\",\"", "\"-\"", "\"b\"", "\"\"", "\" \"", "{\",\",\"-\"}"],
        ['C'] = ["\">=5\"", "\"<>a\"", "\"a*\"", "5", "\"\"", "\"=\"", "TRUE", "\"~*\""],
        ['M'] = ["{1,2;3,4}", "{2,0;0,2}", "{1,2;2,4}", "{1,2,3}", "{1;2;3}", "{\"a\",1;2,3}", "5", "{1,#N/A;2,3}"],
        ['R'] = ["\"[0-9]+\"", "\"(a)(b)?\"", "\"\\\\d\"", "\"a++\"", "\"(\"", "\"^[[:alpha:]]+$\"", "\"\"", "\"B\""],
        ['T'] = ["\"a1b22c333\"", "\"ab\"", "\"A1\"", "\"\""],
        ['K'] = ["\"SUM\"", "\"row\"", "\"col\"", "\"address\"", "\"type\"", "\"nope\""],
    };

    // family -> function specs: NAME followed by one domain letter per argument; a trailing '?' marks the argument as optional (cases with and without it).
    private static readonly (string Family, string[] Specs)[] Families =
    [
        ("math",
        [
            "ABS N", "SIGN N", "SQRT N", "SQRTPI N", "EXP N", "LN N", "LOG10 N", "LOG N N?", "POWER N N", "DEGREES N", "RADIANS N", "INT N", "EVEN N", "ODD N",
            "ROUND N I", "ROUNDUP N I", "ROUNDDOWN N I", "TRUNC N I?", "MROUND N N", "CEILING N N", "FLOOR N N", "CEILING.MATH N N? I?", "FLOOR.MATH N N? I?",
            "CEILING.PRECISE N N?", "FLOOR.PRECISE N N?", "ISO.CEILING N N?", "MOD N N", "QUOTIENT N N",
            "SIN N", "COS N", "TAN N", "ASIN N", "ACOS N", "ATAN N", "ATAN2 N N", "SINH N", "COSH N", "TANH N", "ASINH N", "ACOSH N", "ATANH N",
            "COT N", "COTH N", "CSC N", "CSCH N", "SEC N", "SECH N", "ACOT N", "ACOTH N", "PI",
            "FACT P", "FACTDOUBLE P", "COMBIN P P", "COMBINA P P", "PERMUT P P", "PERMUTATIONA P P", "MULTINOMIAL P P P?", "GCD I I I?", "LCM I I I?",
            "ROMAN I I?", "ARABIC S", "BASE I I I?", "DECIMAL S I",
            "SUM N N N?", "PRODUCT N N N?", "SUMSQ N N N?", "SUMPRODUCT N N?", "SUMX2MY2 M M", "SUMX2PY2 M M", "SUMXMY2 M M", "SERIESSUM N N N M",
            "MMULT M M", "MDETERM M", "MINVERSE M", "MUNIT I", "RAND", "RANDBETWEEN N N", "RANDARRAY P? P? N? N? B?",
        ]),
        ("logical",
        [
            "AND N N N?", "OR N N N?", "XOR N N N?", "NOT N", "IF N N N?", "IFS N N N? N?", "SWITCH N N N N? N?", "IFERROR N N", "IFNA N N", "TRUE", "FALSE",
        ]),
        ("information",
        [
            "ISBLANK N", "ISNUMBER N", "ISTEXT N", "ISNONTEXT N", "ISLOGICAL N", "ISERR N", "ISERROR N", "ISNA N", "ISEVEN N", "ISODD N", "ISREF N",
            "N N", "T N", "NA", "TYPE N", "ERROR.TYPE N", "ISOMITTED N", "CELL K", "INFO K", "SHEET K?", "SHEETS",
        ]),
        ("text",
        [
            "LEFT S P?", "RIGHT S P?", "MID S P P", "LEFTB S P?", "RIGHTB S P?", "MIDB S P P", "LEN S", "LENB S", "FIND S S P?", "FINDB S S P?", "SEARCH S S P?", "SEARCHB S S P?",
            "SUBSTITUTE S S S P?", "REPLACE S P P S", "REPLACEB S P P S", "REPT S P", "TRIM S", "CLEAN S", "UPPER S", "LOWER S", "PROPER S", "EXACT S S",
            "CHAR I", "CODE S", "UNICHAR I", "UNICODE S", "CONCAT S S S?", "CONCATENATE S S S?", "TEXTJOIN D B S S?", "ARRAYTOTEXT M B?", "VALUETOTEXT S B?",
            "TEXTBEFORE S D I? B? B? S?", "TEXTAFTER S D I? B? B? S?", "TEXTSPLIT S D D? B? B? S?",
            "VALUE S", "NUMBERVALUE S D? D?", "FIXED N P? B?", "DOLLAR N P?", "TEXT N S", "BAHTTEXT N", "ASC S", "DBCS S", "JIS S", "PHONETIC S",
            "REGEXTEST S R B?", "REGEXEXTRACT S R I? B?", "REGEXREPLACE S R S I? B?", "TRANSLATE S S S?", "DETECTLANGUAGE S",
        ]),
    ];

    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: FunctionGrid gen DIR | fill DIR ANSWERS | compare DIR");
            return 2;
        }

        return args[0] switch
        {
            "gen" => Generate(args[1]),
            "fill" when args.Length > 2 => Fill(args[1], args[2]),
            "compare" => Compare(args[1]),
            _ => 2,
        };
    }

    private static IEnumerable<string> Cases(string spec)
    {
        string[] parts = spec.Split(' ');
        string name = parts[0];
        string[] doms = parts[1..];
        int required = doms.Count(d => !d.EndsWith('?'));
        var all = new List<string>();
        for (int count = required; count <= doms.Length; count++)
        {
            var lists = doms.Take(count).Select(d => Domains[d[0]]).ToArray();
            var idx = new int[count];
            while (true)
            {
                all.Add("=" + name + "(" + string.Join(",", Enumerable.Range(0, count).Select(i => lists[i][idx[i]])) + ")");
                int k = count - 1;
                while (k >= 0 && ++idx[k] == lists[k].Length)
                {
                    idx[k--] = 0;
                }

                if (k < 0)
                {
                    break;
                }
            }
        }

        int stride = Math.Max(1, (all.Count + MaxPerFunction - 1) / MaxPerFunction);
        for (int i = 0; i < all.Count; i += stride)
        {
            yield return all[i];
        }
    }

    private static string Esc(string s) => s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\t", "\\t", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);

    private static string Unesc(string s)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                char n = s[++i];
                sb.Append(n switch { 't' => '\t', 'n' => '\n', 'r' => '\r', _ => n });
            }
            else
            {
                sb.Append(s[i]);
            }
        }

        return sb.ToString();
    }

    private static int Generate(string dir)
    {
        Directory.CreateDirectory(dir);
        int total = 0;
        foreach ((string family, string[] specs) in Families)
        {
            var fgrid = new List<string>();
            var expected = new List<string>();
            int n = 0;
            foreach (string spec in specs)
            {
                foreach (string f in Cases(spec))
                {
                    string id = $"{family[..2]}{n++}";
                    fgrid.Add($"{id}\ta1\t{Esc(f)}");
                    expected.Add($"{id}\t{Esc(f)}\t");
                }
            }

            File.WriteAllLines(Path.Combine(dir, family + ".fgrid"), fgrid, new UTF8Encoding(false));
            File.WriteAllLines(Path.Combine(dir, family + ".expected.tsv"), expected, new UTF8Encoding(false));
            Console.WriteLine($"{family}: {n} formulas");
            total += n;
        }

        Console.WriteLine($"total {total}");
        return 0;
    }

    // Excel's answer cell list becomes one expected string: the single value as t:value, or the spill range as rows x cols and the items.
    private static int Fill(string dir, string answers)
    {
        var byId = new Dictionary<string, string>();
        foreach (string f in Directory.EnumerateFiles(answers, "*.fgrid.tsv"))
        {
            foreach (string line in File.ReadAllLines(f))
            {
                string[] p = line.Split('\t');
                if (p.Length > 7 && p[1] == "ok")
                {
                    byId[p[0]] = (p[5] == "1" && p[6] == "1" ? string.Empty : p[5] + "x" + p[6] + "|") + p[7];
                }
            }
        }

        foreach (string f in Directory.EnumerateFiles(dir, "*.expected.tsv"))
        {
            string[] lines = File.ReadAllLines(f);
            for (int i = 0; i < lines.Length; i++)
            {
                string[] p = lines[i].Split('\t');
                if (byId.TryGetValue(p[0], out string? v))
                {
                    lines[i] = p[0] + "\t" + p[1] + "\t" + v;
                }
            }

            File.WriteAllLines(f, lines, new UTF8Encoding(false));
        }

        return 0;
    }

    private static string Encode(CalcValue v)
    {
        static string One(CalcValue x) => x.Kind switch
        {
            ValueKind.Number => "n:" + x.Number.ToString("R", CultureInfo.InvariantCulture),
            ValueKind.Text => "s:" + Esc(x.Text),
            ValueKind.Boolean => "b:" + (x.Boolean ? "True" : "False"),
            ValueKind.Error => "e:" + CalcErrors.ToText(x.Error),
            _ => "z:",
        };

        if (v.Kind != ValueKind.Array)
        {
            return One(v);
        }

        CalcArray a = v.Array!;
        var items = new List<string>();
        for (int r = 0; r < a.Rows; r++)
        {
            for (int c = 0; c < a.Columns; c++)
            {
                items.Add(One(a[r, c]));
            }
        }

        return a.Rows + "x" + a.Columns + "|" + string.Join("\u001f", items);
    }

    private static int Compare(string dir)
    {
        var ev = new Evaluator(new Blank(), options: new EvaluatorOptions { Random = new Random(1) });
        int compared = 0;
        int bad = 0;
        foreach (string f in Directory.EnumerateFiles(dir, "*.expected.tsv").Order(StringComparer.Ordinal))
        {
            foreach (string line in File.ReadAllLines(f))
            {
                string[] p = line.Split('\t');
                if (p.Length < 3 || p[2].Length == 0)
                {
                    continue;
                }

                compared++;
                string text = Unesc(p[1]).TrimStart('=');
                CalcValue v = ev.Evaluate(FormulaParser.Parse(text, new FormulaParserOptions { FileForm = false }), new CalcCell("Sheet1", 1, 2));
                string ours = Encode(v);
                if (!Same(ours, p[2]))
                {
                    bad++;
                    Console.WriteLine($"{p[0]} {text} :: ours {ours} excel {p[2]}");
                }
            }
        }

        Console.WriteLine($"compared={compared} disagree={bad}");
        return 0;
    }

    // Numbers agree to 15 significant digits; everything else exactly. Errors are compared by Excel's numeric code in the answers, so only the kind is checked there.
    private static bool Same(string ours, string excel)
    {
        if (ours == excel)
        {
            return true;
        }

        string[] a = ours.Split('\u001f');
        string[] b = excel.Split('\u001f');
        if (a.Length != b.Length)
        {
            return false;
        }

        for (int i = 0; i < a.Length; i++)
        {
            string x = a[i][(a[i].IndexOf('|', StringComparison.Ordinal) + 1)..];
            string y = b[i][(b[i].IndexOf('|', StringComparison.Ordinal) + 1)..];
            if (x == y)
            {
                continue;
            }

            if (x.StartsWith("n:", StringComparison.Ordinal) && y.StartsWith("n:", StringComparison.Ordinal) &&
                double.TryParse(x[2..], NumberStyles.Float, CultureInfo.InvariantCulture, out double dx) && double.TryParse(y[2..], NumberStyles.Float, CultureInfo.InvariantCulture, out double dy) &&
                Math.Abs(dx - dy) <= Math.Max(Math.Abs(dx), Math.Abs(dy)) * 1e-14)
            {
                continue;
            }

            if (x.StartsWith("e:", StringComparison.Ordinal) && y.StartsWith("e:", StringComparison.Ordinal))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private sealed class Blank : ICalcResolver
    {
        public CalcValue GetCell(string? sheet, int row, int column) => CalcValue.Blank;
    }
}
