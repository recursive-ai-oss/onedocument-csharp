using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace RecursiveAI.OneOffice.Calc;

internal static partial class TextFunctions
{
    private static void RegisterRegex(FunctionRegistry r)
    {
        r.Add(FunctionDefinition.Create("REGEXTEST", 2, 3, V, RegexTest));
        r.Add(FunctionDefinition.Create("REGEXEXTRACT", 2, 4, V, RegexExtract));
        r.Add(FunctionDefinition.Create("REGEXREPLACE", 3, 5, V, RegexReplace));
    }

    // Builds the .NET expression for a PCRE2 pattern. Supported: everything .NET reads the same way, plus the PCRE2 spellings translated
    // here (ASCII-only \d \w \s, \h, \R, \Q..\E, \x{..}, (?P<name>..), POSIX classes). Possessive quantifiers and other PCRE2-only
    // constructs are rejected by the parser and become #VALUE!.
    internal static string TranslatePattern(string p)
    {
        var sb = new StringBuilder(p.Length + 16);
        bool inClass = false;
        for (int i = 0; i < p.Length; i++)
        {
            char c = p[i];
            if (c == '\\' && i + 1 < p.Length)
            {
                char n = p[++i];
                switch (n)
                {
                    case 'd':
                        sb.Append(inClass ? "0-9" : "[0-9]");
                        break;
                    case 'D':
                        sb.Append(inClass ? "\\D" : "[^0-9]");
                        break;
                    case 'w':
                        sb.Append(inClass ? "A-Za-z0-9_" : "[A-Za-z0-9_]");
                        break;
                    case 'W':
                        sb.Append(inClass ? "\\W" : "[^A-Za-z0-9_]");
                        break;
                    case 's':
                        sb.Append(inClass ? " \\t\\n\\r\\f\\v" : "[ \\t\\n\\r\\f\\v]");
                        break;
                    case 'S':
                        sb.Append(inClass ? "\\S" : "[^ \\t\\n\\r\\f\\v]");
                        break;
                    case 'h':
                        sb.Append(inClass ? " \\t" : "[ \\t]");
                        break;
                    case 'H':
                        sb.Append(inClass ? "\\S" : "[^ \\t]");
                        break;
                    case 'R' when !inClass:
                        sb.Append("(?:\\r\\n|\\n|\\r|\\f|\\v)");
                        break;
                    case 'z':
                        sb.Append("\\z");
                        break;
                    case 'Q':
                        {
                            int end = p.IndexOf("\\E", i + 1, StringComparison.Ordinal);
                            string lit = end < 0 ? p[(i + 1)..] : p[(i + 1)..end];
                            sb.Append(Regex.Escape(lit));
                            i = end < 0 ? p.Length : end + 1;
                            break;
                        }

                    case 'x' when i + 1 < p.Length && p[i + 1] == '{':
                        {
                            int end = p.IndexOf('}', i + 1);
                            if (end < 0)
                            {
                                sb.Append("\\x");
                                break;
                            }

                            string hex = p[(i + 2)..end];
                            if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out int cp) && cp <= 0x10FFFF)
                            {
                                sb.Append(Regex.Escape(char.ConvertFromUtf32(cp)));
                            }

                            i = end;
                            break;
                        }

                    default:
                        sb.Append('\\').Append(n);
                        break;
                }

                continue;
            }

            if (inClass)
            {
                if (c == '[' && i + 1 < p.Length && p[i + 1] == ':')
                {
                    int end = p.IndexOf(":]", i + 2, StringComparison.Ordinal);
                    if (end > 0)
                    {
                        string name = p[(i + 2)..end];
                        string? repl = name switch
                        {
                            "alpha" => "A-Za-z",
                            "digit" => "0-9",
                            "alnum" => "A-Za-z0-9",
                            "upper" => "A-Z",
                            "lower" => "a-z",
                            "space" => " \\t\\n\\r\\f\\v",
                            "blank" => " \\t",
                            "punct" => "!-/:-@\\[-`{-~",
                            "xdigit" => "0-9A-Fa-f",
                            "word" => "A-Za-z0-9_",
                            "cntrl" => "\\x00-\\x1F\\x7F",
                            "print" => " -~",
                            "graph" => "!-~",
                            _ => null,
                        };
                        if (repl is not null)
                        {
                            sb.Append(repl);
                            i = end + 1;
                            continue;
                        }
                    }
                }

                if (c == ']')
                {
                    inClass = false;
                }

                sb.Append(c);
                continue;
            }

            if (c == '[')
            {
                inClass = true;
                sb.Append(c);
                if (i + 1 < p.Length && p[i + 1] == '^')
                {
                    sb.Append('^');
                    i++;
                }

                // A leading ] is a literal member of the class.
                if (i + 1 < p.Length && p[i + 1] == ']')
                {
                    sb.Append("\\]");
                    i++;
                }

                continue;
            }

            if (c == '(' && i + 2 < p.Length && p[i + 1] == '?' && p[i + 2] == 'P' && i + 3 < p.Length && p[i + 3] == '<')
            {
                sb.Append("(?<");
                i += 3;
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    private static Regex? Build(Evaluator ev, string pattern, bool ignoreCase, out CalcValue error)
    {
        error = default;
        try
        {
            RegexOptions opts = RegexOptions.CultureInvariant;
            if (ignoreCase)
            {
                opts |= RegexOptions.IgnoreCase;
            }

            return new Regex(TranslatePattern(pattern), opts, TimeSpan.FromMilliseconds(ev.Limits.RegexMatchTimeoutMilliseconds));
        }
        catch (ArgumentException)
        {
            error = Fx.Err(CalcError.Value);
            return null;
        }
    }

    // case_sensitivity: 0 (default) matches case, 1 ignores it.
    private static bool CaseArg(ReadOnlySpan<CalcValue> a, int index, out bool ignoreCase, out CalcValue error)
    {
        ignoreCase = false;
        error = default;
        if (Fx.Omitted(a, index))
        {
            return true;
        }

        if (!Fx.TryInt(a[index], out double d, out error))
        {
            return false;
        }

        if (d is not (0 or 1))
        {
            error = Fx.Err(CalcError.Value);
            return false;
        }

        ignoreCase = d == 1;
        return true;
    }

    private static CalcValue RegexTest(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        if (!CaseArg(a, 2, out bool ic, out CalcValue e))
        {
            return e;
        }

        Regex? re = Build(ev, Str(a[1]), ic, out e);
        if (re is null)
        {
            return e;
        }

        try
        {
            return CalcValue.FromBoolean(re.IsMatch(Str(a[0])));
        }
        catch (RegexMatchTimeoutException)
        {
            return Fx.Err(CalcError.Value);
        }
    }

    // REGEXEXTRACT(text, pattern, return_mode = 0, case_sensitivity = 0): mode 0 the first match, 1 every match (a column), 2 the capture groups of the
    // first match (a row). No match is #N/A.
    private static CalcValue RegexExtract(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        double mode = 0;
        if (!Fx.Omitted(a, 2) && !Fx.TryInt(a[2], out mode, out CalcValue e))
        {
            return e;
        }

        if (mode is < 0 or > 2)
        {
            return Fx.Err(CalcError.Value);
        }

        if (!CaseArg(a, 3, out bool ic, out e))
        {
            return e;
        }

        Regex? re = Build(ev, Str(a[1]), ic, out e);
        if (re is null)
        {
            return e;
        }

        string text = Str(a[0]);
        try
        {
            if (mode == 0)
            {
                Match m = re.Match(text);
                return m.Success ? CalcValue.FromText(m.Value) : Fx.Err(CalcError.NA);
            }

            if (mode == 1)
            {
                var all = new List<CalcValue>();
                for (Match m = re.Match(text); m.Success; m = m.NextMatch())
                {
                    all.Add(CalcValue.FromText(m.Value));
                }

                return all.Count == 0 ? Fx.Err(CalcError.NA) : CalcValue.FromArray(new CalcArray(all.Count, 1, [.. all]));
            }

            Match first = re.Match(text);
            if (!first.Success)
            {
                return Fx.Err(CalcError.NA);
            }

            if (first.Groups.Count == 1)
            {
                return CalcValue.FromText(first.Value);
            }

            var groups = new CalcValue[first.Groups.Count - 1];
            for (int i = 1; i < first.Groups.Count; i++)
            {
                groups[i - 1] = CalcValue.FromText(first.Groups[i].Value);
            }

            return CalcValue.FromArray(new CalcArray(1, groups.Length, groups));
        }
        catch (RegexMatchTimeoutException)
        {
            return Fx.Err(CalcError.Value);
        }
    }

    // REGEXREPLACE(text, pattern, replacement, occurrence = 0, case_sensitivity = 0): occurrence 0 replaces every match, n the nth, -n the nth from the end.
    // $1 and $& in the replacement insert a group or the whole match.
    private static CalcValue RegexReplace(Evaluator ev, ReadOnlySpan<CalcValue> a)
    {
        double occ = 0;
        if (!Fx.Omitted(a, 3) && !Fx.TryInt(a[3], out occ, out CalcValue e))
        {
            return e;
        }

        if (!CaseArg(a, 4, out bool ic, out e))
        {
            return e;
        }

        Regex? re = Build(ev, Str(a[1]), ic, out e);
        if (re is null)
        {
            return e;
        }

        string text = Str(a[0]);
        string repl = Str(a[2]);
        try
        {
            if (occ == 0)
            {
                string all = re.Replace(text, repl);
                return all.Length > MaxLength ? Fx.Err(CalcError.Value) : CalcValue.FromText(all);
            }

            MatchCollection ms = re.Matches(text);
            int idx = occ > 0 ? (int)occ - 1 : ms.Count + (int)occ;
            if (idx < 0 || idx >= ms.Count)
            {
                return CalcValue.FromText(text);
            }

            Match m = ms[idx];
            string res = string.Concat(text.AsSpan(0, m.Index), m.Result(repl), text.AsSpan(m.Index + m.Length));
            return res.Length > MaxLength ? Fx.Err(CalcError.Value) : CalcValue.FromText(res);
        }
        catch (RegexMatchTimeoutException)
        {
            return Fx.Err(CalcError.Value);
        }
    }
}
