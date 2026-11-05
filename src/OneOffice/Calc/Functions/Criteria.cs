using System;
using System.Collections.Generic;

namespace RecursiveAI.OneOffice.Calc;

/// <summary>
/// The matching rule of <c>SUMIF</c>, <c>COUNTIF</c> and the rest of the <c>*IF(S)</c> family, built once per criterion and applied to many cells.
/// A criterion is a number, a boolean, an error, or text that may start with a comparison operator
/// (<c>&gt;=5</c>, <c>&lt;&gt;apple</c>, <c>=</c>) and, for equality, may use the wildcards <c>?</c> and <c>*</c> with <c>~</c> as the escape.
/// </summary>
public sealed class CalcCriteria
{
    private enum Op : byte
    {
        Equal,
        NotEqual,
        Less,
        LessOrEqual,
        Greater,
        GreaterOrEqual,
    }

    private enum Operand : byte
    {
        Number,
        Boolean,
        Error,
        Text,
        Blank,
        AnyEmpty,
    }

    private readonly Op _op;
    private readonly Operand _operand;
    private readonly double _number;
    private readonly CalcError _error;
    private readonly string _text = string.Empty;
    private readonly WildcardPattern? _pattern;

    private CalcCriteria(Op op, Operand operand, double number, CalcError error, string text)
    {
        _op = op;
        _operand = operand;
        _number = number;
        _error = error;
        _text = text;
        if (operand == Operand.Text && (op is Op.Equal or Op.NotEqual) && WildcardPattern.HasWildcards(text))
        {
            _pattern = new WildcardPattern(text);
        }
    }

    /// <summary>Builds the matcher for a criterion value.</summary>
    /// <param name="criterion">A scalar. A number or boolean means equality; an error matches that error; a blank counts as the number 0; text is parsed for an operator.</param>
    /// <returns>The matcher.</returns>
    public static CalcCriteria Parse(in CalcValue criterion)
    {
        switch (criterion.Kind)
        {
            case ValueKind.Number:
                return new CalcCriteria(Op.Equal, Operand.Number, criterion.Number, CalcError.None, string.Empty);
            case ValueKind.Boolean:
                return new CalcCriteria(Op.Equal, Operand.Boolean, criterion.Boolean ? 1 : 0, CalcError.None, string.Empty);
            case ValueKind.Error:
                return new CalcCriteria(Op.Equal, Operand.Error, 0, criterion.Error, string.Empty);
            case ValueKind.Blank:
                return new CalcCriteria(Op.Equal, Operand.Number, 0, CalcError.None, string.Empty);
            default:
                break;
        }

        string t = criterion.Kind == ValueKind.Text ? criterion.Text : CalcConvert.ToText(criterion);
        Op op = Op.Equal;
        bool hasOp = true;
        string rest;
        if (t.StartsWith("<=", StringComparison.Ordinal))
        {
            op = Op.LessOrEqual;
            rest = t[2..];
        }
        else if (t.StartsWith(">=", StringComparison.Ordinal))
        {
            op = Op.GreaterOrEqual;
            rest = t[2..];
        }
        else if (t.StartsWith("<>", StringComparison.Ordinal))
        {
            op = Op.NotEqual;
            rest = t[2..];
        }
        else if (t.StartsWith('<'))
        {
            op = Op.Less;
            rest = t[1..];
        }
        else if (t.StartsWith('>'))
        {
            op = Op.Greater;
            rest = t[1..];
        }
        else if (t.StartsWith('='))
        {
            rest = t[1..];
        }
        else
        {
            rest = t;
            hasOp = false;
        }

        if (rest.Length == 0)
        {
            // "" matches blank cells and empty text; "=" only blank cells; "<>" every non-blank cell.
            return new CalcCriteria(op, !hasOp ? Operand.AnyEmpty : Operand.Blank, 0, CalcError.None, string.Empty);
        }

        if (CalcErrors.TryParse(rest, out CalcError err))
        {
            return new CalcCriteria(op, Operand.Error, 0, err, string.Empty);
        }

        if (rest.Equals("TRUE", StringComparison.OrdinalIgnoreCase) || rest.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
        {
            return new CalcCriteria(op, Operand.Boolean, rest.Length == 4 ? 1 : 0, CalcError.None, string.Empty);
        }

        if (CalcConvert.TextToNumber(rest, out double n))
        {
            return new CalcCriteria(op, Operand.Number, n, CalcError.None, string.Empty);
        }

        return new CalcCriteria(op, Operand.Text, 0, CalcError.None, rest);
    }

    /// <summary>Tests one cell value.</summary>
    /// <param name="cell">A scalar from the range: a number, text, boolean, error or blank.</param>
    /// <returns>True when the cell satisfies the criterion.</returns>
    public bool Matches(in CalcValue cell)
    {
        switch (_operand)
        {
            case Operand.AnyEmpty:
                return cell.IsBlank || (cell.Kind == ValueKind.Text && cell.Text.Length == 0);
            case Operand.Blank:
                return _op == Op.NotEqual ? !cell.IsBlank : _op == Op.Equal && cell.IsBlank;
            case Operand.Error:
                {
                    bool same = cell.Kind == ValueKind.Error && cell.Error == _error;
                    return _op == Op.NotEqual ? !same : _op == Op.Equal && same;
                }

            case Operand.Boolean:
                {
                    bool same = cell.Kind == ValueKind.Boolean && cell.Number == _number;
                    return _op == Op.NotEqual ? !same : _op == Op.Equal && same;
                }

            case Operand.Number:
                return MatchNumber(cell);
            default:
                return MatchText(cell);
        }
    }

    private bool MatchNumber(in CalcValue cell)
    {
        double x;
        if (cell.Kind == ValueKind.Number)
        {
            x = cell.Number;
        }
        else if (cell.Kind == ValueKind.Text && _op is Op.Equal or Op.NotEqual && CalcConvert.TextToNumber(cell.Text, out x))
        {
            // Equality also accepts text that reads as the same number, as COUNTIF(range, "1") counts the text "1".
        }
        else
        {
            return _op == Op.NotEqual;
        }

        int cmp = Evaluator.Compare(CalcValue.FromNumber(x), CalcValue.FromNumber(_number));
        return Test(cmp);
    }

    private bool MatchText(in CalcValue cell)
    {
        if (cell.Kind != ValueKind.Text)
        {
            return _op == Op.NotEqual;
        }

        if (_pattern is not null)
        {
            bool m = _pattern.IsMatch(cell.Text);
            return _op == Op.NotEqual ? !m : m;
        }

        string target = _op is Op.Equal or Op.NotEqual && _text.Contains('~', StringComparison.Ordinal) ? WildcardPattern.Unescape(_text) : _text;
        if (_op is Op.Equal or Op.NotEqual)
        {
            bool same = string.Equals(cell.Text, target, StringComparison.OrdinalIgnoreCase);
            return _op == Op.NotEqual ? !same : same;
        }

        return Test(TextCollation.Compare(cell.Text, target));
    }

    private bool Test(int cmp) => _op switch
    {
        Op.Equal => cmp == 0,
        Op.NotEqual => cmp != 0,
        Op.Less => cmp < 0,
        Op.LessOrEqual => cmp <= 0,
        Op.Greater => cmp > 0,
        _ => cmp >= 0,
    };
}

/// <summary>A text pattern with <c>?</c> (one character), <c>*</c> (any run) and <c>~</c> (escape), compared ignoring case.</summary>
internal sealed class WildcardPattern
{
    private readonly char[] _chars;
    private readonly byte[] _kind; // 0 literal, 1 any one, 2 any run

    public WildcardPattern(string pattern)
    {
        var chars = new List<char>(pattern.Length);
        var kinds = new List<byte>(pattern.Length);
        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];
            if (c == '~' && i + 1 < pattern.Length && pattern[i + 1] is '?' or '*' or '~')
            {
                chars.Add(pattern[++i]);
                kinds.Add(0);
            }
            else if (c == '?')
            {
                chars.Add(c);
                kinds.Add(1);
            }
            else if (c == '*')
            {
                chars.Add(c);
                kinds.Add(2);
            }
            else
            {
                chars.Add(c);
                kinds.Add(0);
            }
        }

        _chars = [.. chars];
        _kind = [.. kinds];
    }

    public static bool HasWildcards(string s) => s.AsSpan().IndexOfAny('?', '*', '~') >= 0;

    public static string Unescape(string s) => new WildcardPattern(s) is { } p ? new string(p._chars) : s;

    /// <summary>True when the whole text matches.</summary>
    public bool IsMatch(string text) => Match(text, 0, true) >= 0;

    /// <summary>Finds the first position at which the pattern matches a prefix of the rest of the text; -1 when none.</summary>
    public int Find(string text, int start)
    {
        for (int i = start; i <= text.Length; i++)
        {
            if (Match(text, i, false) >= 0)
            {
                return i;
            }
        }

        return -1;
    }

    private int Match(string text, int from, bool whole)
    {
        int p = 0;
        int t = from;
        int starP = -1;
        int starT = 0;
        while (true)
        {
            if (p < _chars.Length && _kind[p] == 2)
            {
                starP = p++;
                starT = t;
                continue;
            }

            if (p == _chars.Length)
            {
                if (!whole || t == text.Length)
                {
                    return t;
                }
            }
            else if (t < text.Length && (_kind[p] == 1 || char.ToUpperInvariant(_chars[p]) == char.ToUpperInvariant(text[t])))
            {
                p++;
                t++;
                continue;
            }

            if (starP < 0 || starT >= text.Length)
            {
                return -1;
            }

            p = starP + 1;
            t = ++starT;
        }
    }
}
