using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RecursiveAI.OneDocument.Calc;

/// <summary>Text functions: extraction, search and replace, case and cleanup, conversion between text and numbers, character codes, joining.</summary>
internal static partial class TextFunctions
{
    private static readonly ArgKind[] V = [ArgKind.Value];
    private static readonly ArgKind[] A = [ArgKind.Array];

    /// <summary>The longest text a cell can hold.</summary>
    internal const int MaxLength = 32767;

    public static void Register(FunctionRegistry r)
    {
        RegisterBasic(r);
        RegisterSearch(r);
        RegisterConvert(r);
        RegisterSplit(r);
        RegisterRegex(r);
        RegisterEastAsian(r);
    }

    private static string Str(in CalcValue v) => CalcConvert.ToText(v);

    private static void RegisterBasic(FunctionRegistry r)
    {
        // The *B forms count bytes in a double-byte language; with any other editing language Excel counts one per character, which is what is done here.
        foreach (string suffix in new[] { string.Empty, "B" })
        {
            r.Add(FunctionDefinition.Create("LEFT" + suffix, 1, 2, V, (_, a) => Edge(a, true)));
            r.Add(FunctionDefinition.Create("RIGHT" + suffix, 1, 2, V, (_, a) => Edge(a, false)));
            r.Add(FunctionDefinition.Create("MID" + suffix, 3, 3, V, Mid));
            r.Add(FunctionDefinition.Create("LEN" + suffix, 1, 1, V, (_, a) => CalcValue.FromNumber(Str(a[0]).Length)));
            r.Add(FunctionDefinition.Create("REPLACE" + suffix, 4, 4, V, Replace));
        }

        r.Add(FunctionDefinition.Create("REPT", 2, 2, V, Rept));
        r.Add(FunctionDefinition.Create("TRIM", 1, 1, V, (_, a) => CalcValue.FromText(Trim(Str(a[0])))));
        r.Add(FunctionDefinition.Create("CLEAN", 1, 1, V, (_, a) => CalcValue.FromText(Clean(Str(a[0])))));
        r.Add(FunctionDefinition.Create("UPPER", 1, 1, V, (_, a) => CalcValue.FromText(Str(a[0]).ToUpperInvariant())));
        r.Add(FunctionDefinition.Create("LOWER", 1, 1, V, (_, a) => CalcValue.FromText(Str(a[0]).ToLowerInvariant())));
        r.Add(FunctionDefinition.Create("PROPER", 1, 1, V, (_, a) => CalcValue.FromText(Proper(Str(a[0])))));
        r.Add(FunctionDefinition.Create("EXACT", 2, 2, V, (_, a) => CalcValue.FromBoolean(string.Equals(Str(a[0]), Str(a[1]), StringComparison.Ordinal))));
        r.Add(FunctionDefinition.Create("CONCATENATE", 1, 255, V, Concatenate, repeatLast: true));
        r.Add(FunctionDefinition.Create("TEXTJOIN", 3, 255, [ArgKind.Array, ArgKind.Value, ArgKind.Array], TextJoin, repeatLast: true));
        r.Add(FunctionDefinition.Create("ARRAYTOTEXT", 1, 2, [ArgKind.Array, ArgKind.Value], ArrayToText));
        r.Add(FunctionDefinition.Create("VALUETOTEXT", 1, 2, V, ValueToText));
        r.Add(FunctionDefinition.Create("CHAR", 1, 1, V, CharFn));
        r.Add(FunctionDefinition.Create("CODE", 1, 1, V, Code));
        r.Add(FunctionDefinition.Create("UNICHAR", 1, 1, V, UniChar));
        r.Add(FunctionDefinition.Create("UNICODE", 1, 1, V, Unicode));
    }

    // LEFT and RIGHT take a count of characters (default 1); a negative count is #VALUE! and a count past the end gives all of the text.
    private static CalcValue Edge(ReadOnlySpan<CalcValue> a, bool left)
    {
        string t = Str(a[0]);
        double n = 1;
        if (!Fx.Omitted(a, 1) && !Fx.TryInt(a[1], out n, out CalcValue e))
        {
            return e;
        }

        if (n < 0)
        {
            return Fx.Err(CalcError.Value);
        }

        int k = (int)Math.Min(n, t.Length);
        return CalcValue.FromText(left ? t[..k] : t[^k..]);
    }

    private static CalcValue Mid(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        string t = Str(a[0]);
        if (!Fx.TryInt(a[1], out double start, out CalcValue e) || !Fx.TryInt(a[2], out double n, out e))
        {
            return e;
        }

        if (start < 1 || n < 0)
        {
            return Fx.Err(CalcError.Value);
        }

        if (start > t.Length)
        {
            return CalcValue.EmptyText;
        }

        int s = (int)start - 1;
        int k = (int)Math.Min(n, t.Length - s);
        return CalcValue.FromText(t.Substring(s, k));
    }

    // REPLACE(old, start, count, new): a start past the end appends; start below 1 or a negative count is #VALUE!.
    private static CalcValue Replace(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        string t = Str(a[0]);
        if (!Fx.TryInt(a[1], out double start, out CalcValue e) || !Fx.TryInt(a[2], out double n, out e))
        {
            return e;
        }

        string repl = Str(a[3]);
        if (start < 1 || n < 0)
        {
            return Fx.Err(CalcError.Value);
        }

        int s = (int)Math.Min(start - 1, t.Length);
        int k = (int)Math.Min(n, t.Length - s);
        string res = string.Concat(t.AsSpan(0, s), repl, t.AsSpan(s + k));
        return res.Length > MaxLength ? Fx.Err(CalcError.Value) : CalcValue.FromText(res);
    }

    private static CalcValue Rept(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        string t = Str(a[0]);
        if (!Fx.TryInt(a[1], out double n, out CalcValue e))
        {
            return e;
        }

        if (n < 0 || (t.Length > 0 && n * t.Length > MaxLength))
        {
            return Fx.Err(CalcError.Value);
        }

        return t.Length == 0 || n == 0 ? CalcValue.EmptyText : CalcValue.FromText(new StringBuilder(t.Length * (int)n).Insert(0, t, (int)n).ToString());
    }

    // TRIM removes the ordinary space (U+0020) from both ends and reduces every run inside to one space.
    internal static string Trim(string s)
    {
        var sb = new StringBuilder(s.Length);
        bool pending = false;
        foreach (char c in s)
        {
            if (c == ' ')
            {
                pending = sb.Length > 0;
                continue;
            }

            if (pending)
            {
                sb.Append(' ');
                pending = false;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    private static string Clean(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            if (c >= ' ')
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    private static string Proper(string s)
    {
        var sb = new StringBuilder(s.Length);
        bool inWord = false;
        foreach (char c in s)
        {
            if (char.IsLetter(c))
            {
                sb.Append(inWord ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c));
                inWord = true;
            }
            else
            {
                sb.Append(c);
                inWord = false;
            }
        }

        return sb.ToString();
    }

    private static CalcValue Concatenate(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        var sb = new StringBuilder();
        foreach (CalcValue v in a)
        {
            sb.Append(Str(v));
        }

        return sb.Length > MaxLength ? Fx.Err(CalcError.Value) : CalcValue.FromText(sb.ToString());
    }

    // TEXTJOIN(delimiter, ignore_empty, text1, ...): the delimiter may be a range, used in turn; with ignore_empty empty strings and blanks are left out.
    private static CalcValue TextJoin(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        CalcArray? delims = Fx.AsArray(ev, a[0], out CalcValue e);
        if (delims is null)
        {
            return e;
        }

        CalcValue ie = ev.ToScalar(a[1]);
        if (ie.IsError)
        {
            return ie;
        }

        if (!Fx.TryBool(ie, out bool ignoreEmpty, out e))
        {
            return e;
        }

        var items = new List<CalcValue>();
        for (int i = 2; i < a.Length; i++)
        {
            CalcError fe = Fx.Flatten(ev, a[i], items);
            if (fe != CalcError.None)
            {
                return Fx.Err(fe);
            }
        }

        var ds = new List<string>();
        for (int r = 0; r < delims.Rows; r++)
        {
            for (int c = 0; c < delims.Columns; c++)
            {
                CalcValue d = delims[r, c];
                if (d.IsError)
                {
                    return d;
                }

                ds.Add(Str(d));
            }
        }

        var sb = new StringBuilder();
        int used = 0;
        foreach (CalcValue v in items)
        {
            if (v.IsError)
            {
                return v;
            }

            string s = Str(v);
            if (ignoreEmpty && s.Length == 0)
            {
                continue;
            }

            if (used > 0 && ds.Count > 0)
            {
                sb.Append(ds[(used - 1) % ds.Count]);
            }

            sb.Append(s);
            used++;
            if (sb.Length > MaxLength)
            {
                return Fx.Err(CalcError.Value);
            }
        }

        return CalcValue.FromText(sb.ToString());
    }

    private static string Quote(string s) => "\"" + s.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static string Item(in CalcValue v, bool strict) => strict && v.Kind == ValueKind.Text ? Quote(v.Text) : Str(v);

    // ARRAYTOTEXT format 0 is the readable list "a, b, c"; format 1 is the strict form {"a","b";1,2} that can be pasted back as an array constant.
    private static CalcValue ArrayToText(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        double f = 0;
        if (!Fx.Omitted(a, 1) && !Fx.TryInt(a[1], out f, out CalcValue e))
        {
            return e;
        }

        if (f is not (0 or 1))
        {
            return Fx.Err(CalcError.Value);
        }

        CalcArray? arr = Fx.AsArray(ev, a[0], out CalcValue err);
        if (arr is null)
        {
            return err;
        }

        bool strict = f == 1;
        var sb = new StringBuilder();
        if (strict)
        {
            sb.Append('{');
        }

        for (int r = 0; r < arr.Rows; r++)
        {
            if (r > 0)
            {
                sb.Append(strict ? ";" : ", ");
            }

            for (int c = 0; c < arr.Columns; c++)
            {
                if (c > 0)
                {
                    sb.Append(strict ? "," : ", ");
                }

                sb.Append(Item(arr[r, c], strict));
            }
        }

        if (strict)
        {
            sb.Append('}');
        }

        return CalcValue.FromText(sb.ToString());
    }

    private static CalcValue ValueToText(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        double f = 0;
        if (!Fx.Omitted(a, 1) && !Fx.TryInt(a[1], out f, out CalcValue e))
        {
            return e;
        }

        if (f is not (0 or 1))
        {
            return Fx.Err(CalcError.Value);
        }

        return CalcValue.FromText(Item(a[0], f == 1));
    }

    // ---- character codes -------------------------------------------------------------------------------------------------------

    // CHAR and CODE use the Windows ANSI code page (1252): the bytes 128-159 are the typographic characters, not control codes.
    private static readonly char[] Ansi80 =
    [
        '\u20AC', '\u0081', '\u201A', '\u0192', '\u201E', '\u2026', '\u2020', '\u2021', '\u02C6', '\u2030', '\u0160', '\u2039', '\u0152', '\u008D', '\u017D', '\u008F',
        '\u0090', '\u2018', '\u2019', '\u201C', '\u201D', '\u2022', '\u2013', '\u2014', '\u02DC', '\u2122', '\u0161', '\u203A', '\u0153', '\u009D', '\u017E', '\u0178',
    ];

    private static CalcValue CharFn(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        if (!Fx.TryInt(a[0], out double n, out CalcValue e))
        {
            return e;
        }

        if (n < 1 || n > 255)
        {
            return Fx.Err(CalcError.Value);
        }

        int b = (int)n;
        return CalcValue.FromText(((b is >= 128 and <= 159) ? Ansi80[b - 128] : (char)b).ToString());
    }

    private static CalcValue Code(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        string t = Str(a[0]);
        if (t.Length == 0)
        {
            return Fx.Err(CalcError.Value);
        }

        char c = t[0];
        int idx = Array.IndexOf(Ansi80, c);
        if (idx >= 0)
        {
            return CalcValue.FromNumber(128 + idx);
        }

        return CalcValue.FromNumber(c < 256 ? c : '?');
    }

    private static CalcValue UniChar(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        if (!Fx.TryInt(a[0], out double n, out CalcValue e))
        {
            return e;
        }

        if (n < 1 || n > 0x10FFFF || (n >= 0xD800 && n <= 0xDFFF))
        {
            return Fx.Err(CalcError.Value);
        }

        return CalcValue.FromText(char.ConvertFromUtf32((int)n));
    }

    private static CalcValue Unicode(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        string t = Str(a[0]);
        if (t.Length == 0)
        {
            return Fx.Err(CalcError.Value);
        }

        return CalcValue.FromNumber(char.IsHighSurrogate(t[0]) && t.Length > 1 && char.IsLowSurrogate(t[1]) ? char.ConvertToUtf32(t[0], t[1]) : t[0]);
    }
}
