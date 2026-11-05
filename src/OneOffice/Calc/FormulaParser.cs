using System;
using System.Collections.Generic;
using System.Globalization;
using RecursiveAI.OneOffice.Diagnostics;

namespace RecursiveAI.OneOffice.Calc;

/// <summary>The text notation of cell references.</summary>
public enum FormulaSyntax : byte
{
    /// <summary>Column letters and row numbers: <c>$A$1</c>, <c>B2</c>.</summary>
    A1,

    /// <summary>Row and column numbers, relative to the formula's cell: <c>R1C1</c>, <c>R[-1]C[2]</c>.</summary>
    R1C1,
}

/// <summary>The separators and function names of a formula language (en-US by default). Names and separators are tables in the library, never the host's culture.</summary>
public sealed class FormulaLocale
{
    private readonly Dictionary<string, string> _toInvariant = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _toLocal = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates a locale.</summary>
    /// <param name="decimalSeparator">The decimal separator in number literals.</param>
    /// <param name="listSeparator">The argument separator and union operator.</param>
    /// <param name="arrayColumnSeparator">The separator between the elements of an array constant's row.</param>
    /// <param name="arrayRowSeparator">The separator between the rows of an array constant.</param>
    /// <param name="functionNames">Pairs of invariant (English) name and local name, or null.</param>
    public FormulaLocale(char decimalSeparator, char listSeparator, char arrayColumnSeparator, char arrayRowSeparator, IEnumerable<KeyValuePair<string, string>>? functionNames = null)
    {
        DecimalSeparator = decimalSeparator;
        ListSeparator = listSeparator;
        ArrayColumnSeparator = arrayColumnSeparator;
        ArrayRowSeparator = arrayRowSeparator;
        if (functionNames is not null)
        {
            foreach (KeyValuePair<string, string> kv in functionNames)
            {
                _toLocal[kv.Key] = kv.Value;
                _toInvariant[kv.Value] = kv.Key;
            }
        }
    }

    /// <summary>The en-US formula language: <c>.</c> decimal, <c>,</c> arguments, <c>,</c> and <c>;</c> in array constants.</summary>
    public static FormulaLocale Invariant { get; } = new('.', ',', ',', ';');

    /// <summary>The decimal separator.</summary>
    public char DecimalSeparator { get; }

    /// <summary>The argument separator.</summary>
    public char ListSeparator { get; }

    /// <summary>The separator between array-constant columns.</summary>
    public char ArrayColumnSeparator { get; }

    /// <summary>The separator between array-constant rows.</summary>
    public char ArrayRowSeparator { get; }

    /// <summary>A locale with the separators the number-format service knows for a culture: a comma decimal separator brings <c>;</c> arguments and <c>.</c> array columns, as in Excel.</summary>
    /// <param name="decimalSeparator">The culture's decimal separator.</param>
    /// <returns>The locale (no function names).</returns>
    public static FormulaLocale ForDecimalSeparator(string decimalSeparator)
    {
        ArgumentNullException.ThrowIfNull(decimalSeparator);
        return decimalSeparator == "," ? new FormulaLocale(',', ';', '.', ';') : Invariant;
    }

    internal string ToInvariantName(string local) => _toInvariant.TryGetValue(local, out string? n) ? n : local;

    internal string ToLocalName(string invariant) => _toLocal.TryGetValue(invariant, out string? n) ? n : invariant;
}

/// <summary>Options for parsing a formula.</summary>
public sealed class FormulaParserOptions
{
    /// <summary>The reference notation. Default A1.</summary>
    public FormulaSyntax Syntax { get; init; } = FormulaSyntax.A1;

    /// <summary>For R1C1: the row of the cell holding the formula, which relative offsets count from. Default 1.</summary>
    public int OriginRow { get; init; } = 1;

    /// <summary>For R1C1: the column of the cell holding the formula. Default 1.</summary>
    public int OriginColumn { get; init; } = 1;

    /// <summary>
    /// True (default) when the text is file form: function names carry their <c>_xlfn.</c> prefixes exactly as stored. False for text typed by a
    /// user, where printing to file form adds the prefixes the function registry knows.
    /// </summary>
    public bool FileForm { get; init; } = true;

    /// <summary>True to accept one leading <c>=</c> (as in a cell's text). Default false: file form has none.</summary>
    public bool LeadingEquals { get; init; }

    /// <summary>True to turn on the template extensions: dotted member paths (<c>item.price</c>). Default false.</summary>
    public bool TemplateExtensions { get; init; }

    /// <summary>Separators and function names. Default <see cref="FormulaLocale.Invariant"/>.</summary>
    public FormulaLocale Locale { get; init; } = FormulaLocale.Invariant;

    /// <summary>Limits for length and nesting. Default <see cref="OfficeLimits.Default"/>.</summary>
    public OfficeLimits Limits { get; init; } = OfficeLimits.Default;
}

/// <summary>A located problem in formula text.</summary>
/// <param name="Code">A stable code, <c>formula.*</c>.</param>
/// <param name="Message">What is wrong.</param>
/// <param name="Offset">The offset of the first character involved.</param>
/// <param name="Length">The number of characters involved (at least 1 except at the end of the text).</param>
public readonly record struct FormulaSyntaxError(string Code, string Message, int Offset, int Length)
{
    /// <summary>The error as one line.</summary>
    /// <returns>The text.</returns>
    public override string ToString() => $"{Code} at {Offset.ToString(CultureInfo.InvariantCulture)}+{Length.ToString(CultureInfo.InvariantCulture)}: {Message}";
}

/// <summary>Formula text could not be parsed. <see cref="Error"/> locates the problem.</summary>
public sealed class FormulaParseException : OfficeFormatException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="error">The located error.</param>
    public FormulaParseException(FormulaSyntaxError error)
        : base(DefectKind.Formula, error.Code, error.Message + " (offset " + error.Offset.ToString(CultureInfo.InvariantCulture) + ")")
    {
        Error = error;
    }

    /// <summary>The located error.</summary>
    public FormulaSyntaxError Error { get; }
}

/// <summary>Reads formula text into a <see cref="FormulaNode"/> tree. Reading is tolerant of the spaces Excel allows and strict about everything else.</summary>
public static class FormulaParser
{
    /// <summary>Parses a formula.</summary>
    /// <param name="text">The formula text.</param>
    /// <param name="options">Options, or null for A1 file form.</param>
    /// <returns>The tree.</returns>
    /// <exception cref="FormulaParseException">The text is not a formula (<c>formula.*</c> codes; the offset is in <see cref="FormulaParseException.Error"/>).</exception>
    /// <exception cref="OfficeLimitException">The text is longer than <see cref="OfficeLimits.MaxFormulaLength"/> or nests deeper than <see cref="OfficeLimits.MaxFormulaDepth"/>.</exception>
    public static FormulaNode Parse(string text, FormulaParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        options ??= new FormulaParserOptions();
        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxFormulaLength), options.Limits.MaxFormulaLength, text.Length);
        var reader = new FormulaReader(text, options);
        try
        {
            return reader.ParseAll();
        }
        catch (FormulaReader.Fail f)
        {
            if (f.Error.Code == "formula.too-deep")
            {
                throw new OfficeLimitException(nameof(OfficeLimits.MaxFormulaDepth), options.Limits.MaxFormulaDepth);
            }

            throw new FormulaParseException(f.Error);
        }
    }

    /// <summary>Parses a formula without throwing for bad text.</summary>
    /// <param name="text">The formula text.</param>
    /// <param name="node">The tree, or null.</param>
    /// <param name="error">The located error when parsing failed.</param>
    /// <param name="options">Options, or null for A1 file form.</param>
    /// <returns>True when parsed. Text past the length or depth limit is a <c>formula.too-long</c> or <c>formula.too-deep</c> error here, not an exception.</returns>
    public static bool TryParse(string text, out FormulaNode? node, out FormulaSyntaxError error, FormulaParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        options ??= new FormulaParserOptions();
        node = null;
        if (text.Length > options.Limits.MaxFormulaLength)
        {
            error = new FormulaSyntaxError("formula.too-long", "The formula is longer than the limit.", options.Limits.MaxFormulaLength, 1);
            return false;
        }

        try
        {
            node = new FormulaReader(text, options).ParseAll();
            error = default;
            return true;
        }
        catch (FormulaReader.Fail f)
        {
            error = f.Error;
            return false;
        }
    }
}

internal sealed class FormulaReader
{
    internal sealed class Fail(FormulaSyntaxError error) : Exception(error.Message)
    {
        public FormulaSyntaxError Error { get; } = error;
    }

    private readonly string _s;
    private readonly FormulaParserOptions _o;
    private readonly FormulaLocale _loc;
    private readonly bool _r1c1;
    private int _p;
    private int _depth;
    private int _arrayDepth;

    public FormulaReader(string s, FormulaParserOptions o)
    {
        _s = s;
        _o = o;
        _loc = o.Locale;
        _r1c1 = o.Syntax == FormulaSyntax.R1C1;
    }

    private char Cur => _p < _s.Length ? _s[_p] : '\0';

    private bool AtEnd => _p >= _s.Length;

    private Fail Error(string code, string message, int offset, int length = 1) => new(new FormulaSyntaxError(code, message, offset, Math.Max(length, _p >= _s.Length ? 0 : 1)));

    public FormulaNode ParseAll()
    {
        if (_o.LeadingEquals && Cur == '=')
        {
            _p++;
        }

        FormulaNode n = ParseComparison();
        string? ws = EatWs();
        if (ws is not null)
        {
            n.AddTrailing(ws);
        }

        if (!AtEnd)
        {
            throw Cur == ')'
                ? Error("formula.unbalanced-parenthesis", "A closing parenthesis has no opening one.", _p)
                : Error("formula.unexpected-character", $"Unexpected '{Cur}'.", _p);
        }

        return n;
    }

    private string? EatWs()
    {
        int s = _p;
        while (_p < _s.Length && (_s[_p] == ' ' || _s[_p] == '\t' || _s[_p] == '\n' || _s[_p] == '\r'))
        {
            _p++;
        }

        return _p == s ? null : _s.Substring(s, _p - s);
    }

    private static void Span(FormulaNode n, int start, int end)
    {
        n.Offset = start;
        n.Length = end - start;
    }

    private void Enter()
    {
        if (++_depth > _o.Limits.MaxFormulaDepth)
        {
            throw Error("formula.too-deep", "The formula nests deeper than the limit.", _p);
        }
    }

    private void Leave() => _depth--;

    // Excel's precedence, lowest to highest: comparison, &, + -, * /, ^, % (postfix), prefix + -, then the reference operators (range, intersection, union).
    private FormulaNode ParseComparison()
    {
        FormulaNode left = ParseConcat();
        while (true)
        {
            int save = _p;
            string? ws = EatWs();
            BinaryOperator op;
            int len = 1;
            switch (Cur)
            {
                case '=':
                    op = BinaryOperator.Equal;
                    break;
                case '<':
                    if (Peek(1) == '>')
                    {
                        op = BinaryOperator.NotEqual;
                        len = 2;
                    }
                    else if (Peek(1) == '=')
                    {
                        op = BinaryOperator.LessOrEqual;
                        len = 2;
                    }
                    else
                    {
                        op = BinaryOperator.Less;
                    }

                    break;
                case '>':
                    if (Peek(1) == '=')
                    {
                        op = BinaryOperator.GreaterOrEqual;
                        len = 2;
                    }
                    else
                    {
                        op = BinaryOperator.Greater;
                    }

                    break;
                default:
                    _p = save;
                    return left;
            }

            left = Combine(left, ws, op, len, ParseConcat);
        }
    }

    private FormulaNode ParseConcat()
    {
        FormulaNode left = ParseAdditive();
        while (true)
        {
            int save = _p;
            string? ws = EatWs();
            if (Cur != '&')
            {
                _p = save;
                return left;
            }

            left = Combine(left, ws, BinaryOperator.Concat, 1, ParseAdditive);
        }
    }

    private FormulaNode ParseAdditive()
    {
        FormulaNode left = ParseMultiplicative();
        while (true)
        {
            int save = _p;
            string? ws = EatWs();
            if (Cur is not ('+' or '-'))
            {
                _p = save;
                return left;
            }

            left = Combine(left, ws, Cur == '+' ? BinaryOperator.Add : BinaryOperator.Subtract, 1, ParseMultiplicative);
        }
    }

    private FormulaNode ParseMultiplicative()
    {
        FormulaNode left = ParsePower();
        while (true)
        {
            int save = _p;
            string? ws = EatWs();
            if (Cur is not ('*' or '/'))
            {
                _p = save;
                return left;
            }

            left = Combine(left, ws, Cur == '*' ? BinaryOperator.Multiply : BinaryOperator.Divide, 1, ParsePower);
        }
    }

    private FormulaNode ParsePower()
    {
        FormulaNode left = ParseUnary();
        while (true)
        {
            int save = _p;
            string? ws = EatWs();
            if (Cur != '^')
            {
                _p = save;
                return left;
            }

            left = Combine(left, ws, BinaryOperator.Power, 1, ParseUnary);
        }
    }

    private BinaryNode Combine(FormulaNode left, string? ws, BinaryOperator op, int len, Func<FormulaNode> rightParser)
    {
        if (ws is not null)
        {
            left.AddTrailing(ws);
        }

        string text = _s.Substring(_p, len);
        _p += len;
        FormulaNode right = rightParser();
        var n = new BinaryNode(op, left, right, text);
        Span(n, left.Offset, right.Offset + right.Length);
        return n;
    }

    private char Peek(int k) => _p + k < _s.Length ? _s[_p + k] : '\0';

    private FormulaNode ParseUnary()
    {
        Enter();
        try
        {
            string? lead = EatWs();
            int start = _p;
            if (Cur is '-' or '+' or '@')
            {
                char op = Cur;
                _p++;
                FormulaNode operand = ParseUnary();
                var u = new UnaryNode(op, operand);
                Span(u, start, operand.Offset + operand.Length);
                u.LeadingSpace = lead;
                return u;
            }

            _p = start;
            FormulaNode n = ParsePercent();
            if (lead is not null)
            {
                n.LeadingSpace = lead + n.LeadingSpace;
            }

            return n;
        }
        finally
        {
            Leave();
        }
    }

    private FormulaNode ParsePercent()
    {
        FormulaNode n = ParseIntersect();
        while (Cur == '%')
        {
            int start = n.Offset;
            _p++;
            string? lead = n.LeadingSpace;
            n.LeadingSpace = null;
            var pn = new PercentNode(n);
            Span(pn, start, _p);
            pn.LeadingSpace = lead;
            n = pn;
        }

        return n;
    }

    private static bool IsRefLike(FormulaNode n) => n.Kind is NodeKind.Cell or NodeKind.Area or NodeKind.Name or NodeKind.Structured or NodeKind.Spill or NodeKind.Paren or NodeKind.Call or NodeKind.Invoke
        || (n is BinaryNode b && b.Operator is BinaryOperator.Range or BinaryOperator.Intersect or BinaryOperator.Union);

    private bool AtOperandStart()
    {
        char c = Cur;
        return RefSyntax.IsIdentStart(c) || c == '\'' || c == '$' || c == '(' || c == '[';
    }

    private FormulaNode ParseIntersect()
    {
        FormulaNode left = ParseRange();
        while (true)
        {
            int save = _p;
            string? ws = EatWs();
            if (ws is not null && ws.Length > 0 && !AtEnd && AtOperandStart() && IsRefLike(left) && ws.Contains(' ', StringComparison.Ordinal))
            {
                FormulaNode right = ParseRange();
                var n = new BinaryNode(BinaryOperator.Intersect, left, right, ws);
                Span(n, left.Offset, right.Offset + right.Length);
                left = n;
                continue;
            }

            _p = save;
            return left;
        }
    }

    private FormulaNode ParseRange()
    {
        FormulaNode left = ParseAtom();
        while (Cur == ':')
        {
            int colon = _p;
            _p++;
            FormulaNode right = ParseAtom();
            FormulaNode? merged = TryMergeArea(left, right, colon);
            if (merged is not null)
            {
                left = merged;
                continue;
            }

            var n = new BinaryNode(BinaryOperator.Range, left, right, ":");
            Span(n, left.Offset, right.Offset + right.Length);
            left = n;
        }

        return left;
    }

    private AreaReferenceNode? TryMergeArea(FormulaNode left, FormulaNode right, int colon)
    {
        if (left is not CellReferenceNode a || right is not CellReferenceNode b || b.Prefix is not null || a.TrailingSpace is not null || b.LeadingSpace is not null)
        {
            return null;
        }

        string? raw = !_r1c1 && a.Raw is not null && b.Raw is not null ? a.Raw + ":" + b.Raw : null;
        var area = new AreaReferenceNode(a.Prefix, AreaKind.Cells, a.Row, a.Column, b.Row, b.Column, raw);
        Span(area, a.Offset, b.Offset + b.Length);
        area.LeadingSpace = a.LeadingSpace;
        return area;
    }

    private FormulaNode ParseAtom()
    {
        FormulaNode n = ParsePrimary();
        while (true)
        {
            if (Cur == '#' && n.Kind is NodeKind.Cell or NodeKind.Area or NodeKind.Name or NodeKind.Call or NodeKind.Invoke or NodeKind.Structured or NodeKind.Paren && n.TrailingSpace is null)
            {
                int start = n.Offset;
                string? lead = n.LeadingSpace;
                n.LeadingSpace = null;
                _p++;
                var sp = new SpillNode(n);
                Span(sp, start, _p);
                sp.LeadingSpace = lead;
                n = sp;
                continue;
            }

            if (Cur == '(' && n.Kind is NodeKind.Paren or NodeKind.Invoke or NodeKind.Call && n.TrailingSpace is null)
            {
                int start = n.Offset;
                string? lead = n.LeadingSpace;
                n.LeadingSpace = null;
                _p++;
                List<FormulaNode> args = ParseArgs(out string? inner);
                var inv = new InvokeNode(n, args) { InnerSpace = inner };
                Span(inv, start, _p);
                inv.LeadingSpace = lead;
                n = inv;
                continue;
            }

            return n;
        }
    }

    // Reads arguments after '(' up to and including ')'.
    private List<FormulaNode> ParseArgs(out string? innerSpace)
    {
        var args = new List<FormulaNode>();
        innerSpace = null;
        int open = _p - 1;
        int save = _p;
        string? ws0 = EatWs();
        if (Cur == ')')
        {
            _p++;
            innerSpace = ws0;
            return args;
        }

        _p = save;
        char sep = _loc.ListSeparator;
        while (true)
        {
            int argStart = _p;
            string? ws = EatWs();
            if (Cur == sep || Cur == ')')
            {
                var missing = new MissingNode { LeadingSpace = ws };
                Span(missing, argStart, argStart);
                args.Add(missing);
            }
            else
            {
                _p = argStart;
                FormulaNode a = ParseComparison();
                string? tail = EatWs();
                if (tail is not null)
                {
                    a.AddTrailing(tail);
                }

                args.Add(a);
            }

            if (Cur == sep)
            {
                _p++;
                continue;
            }

            if (Cur == ')')
            {
                _p++;
                return args;
            }

            throw AtEnd
                ? Error("formula.unbalanced-parenthesis", "A parenthesis is not closed.", open)
                : Error("formula.unexpected-character", $"Expected '{sep}' or ')' but found '{Cur}'.", _p);
        }
    }

    private FormulaNode ParsePrimary()
    {
        string? lead = EatWs();
        int start = _p;
        FormulaNode n = ParsePrimaryCore();
        if (n.Offset < 0)
        {
            Span(n, start, _p);
        }

        if (lead is not null)
        {
            n.LeadingSpace = lead + n.LeadingSpace;
        }

        return n;
    }

    private FormulaNode ParsePrimaryCore()
    {
        if (AtEnd)
        {
            throw Error("formula.unexpected-end", "The formula ends where an operand is expected.", _p, 0);
        }

        char c = Cur;
        int start = _p;
        if (c == '(')
        {
            _p++;
            FormulaNode inner = ParseComparison();
            string? ws = EatWs();
            if (ws is not null)
            {
                inner.AddTrailing(ws);
            }

            // Inside parentheses a list separator is the union operator (ECMA-376 Part 1 §18.17.2.2).
            while (Cur == _loc.ListSeparator)
            {
                _p++;
                FormulaNode right = ParseComparison();
                string? w2 = EatWs();
                if (w2 is not null)
                {
                    right.AddTrailing(w2);
                }

                var u = new BinaryNode(BinaryOperator.Union, inner, right, _loc.ListSeparator.ToString());
                Span(u, inner.Offset, right.Offset + right.Length);
                inner = u;
            }

            if (Cur != ')')
            {
                throw AtEnd ? Error("formula.unbalanced-parenthesis", "A parenthesis is not closed.", start) : Error("formula.unexpected-character", $"Expected ')' but found '{Cur}'.", _p);
            }

            _p++;
            var p = new ParenNode(inner);
            Span(p, start, _p);
            return p;
        }

        if (c == '"')
        {
            return ParseString();
        }

        if (c == '{')
        {
            return ParseArrayConstant();
        }

        if (c == '#')
        {
            return ParseErrorLiteral(null);
        }

        if (char.IsAsciiDigit(c) || (c == _loc.DecimalSeparator && char.IsAsciiDigit(Peek(1))))
        {
            return ParseNumberOrRowRange();
        }

        if (_r1c1 && (c == 'R' || c == 'r' || c == 'C' || c == 'c') && TryParseR1C1(null) is { } rc)
        {
            return rc;
        }

        if (c == '\'' || c == '[' || c == '$' || RefSyntax.IsIdentStart(c))
        {
            return ParseNameOrReference();
        }

        throw Error("formula.unexpected-character", $"Unexpected '{c}'.", _p);
    }

    private TextNode ParseString()
    {
        int start = _p;
        _p++;
        var sb = new System.Text.StringBuilder();
        while (true)
        {
            if (AtEnd)
            {
                throw Error("formula.unterminated-string", "A text literal is not closed.", start, _p - start);
            }

            char c = _s[_p++];
            if (c == '"')
            {
                if (Cur == '"')
                {
                    sb.Append('"');
                    _p++;
                    continue;
                }

                break;
            }

            sb.Append(c);
        }

        var n = new TextNode(sb.ToString());
        Span(n, start, _p);
        return n;
    }

    private ErrorNode ParseErrorLiteral(SheetPrefix? prefix)
    {
        int start = _p;
        // Longest first: error literals share prefixes (#NUM! and #N/A).
        for (int i = 1; i <= 15; i++)
        {
            string t = CalcErrors.ToText((CalcError)i);
            if (string.CompareOrdinal(_s, _p, t, 0, t.Length) == 0 || (_p + t.Length <= _s.Length && _s.AsSpan(_p, t.Length).Equals(t, StringComparison.OrdinalIgnoreCase)))
            {
                _p += t.Length;
                var n = new ErrorNode((CalcError)i, prefix, _s.Substring(start, _p - start));
                Span(n, start, _p);
                return n;
            }
        }

        throw Error("formula.bad-error-literal", "'#' does not start a known error value.", start);
    }

    private FormulaNode ParseNumberOrRowRange()
    {
        int start = _p;
        if (!_r1c1 && RefSyntax.TryScanRow(_s, _p, out int e1, out RefCoord r1) && e1 < _s.Length && _s[e1] == ':' &&
            RefSyntax.TryScanRow(_s, e1 + 1, out int e2, out RefCoord r2))
        {
            _p = e2;
            var a = new AreaReferenceNode(null, AreaKind.Rows, r1, new RefCoord(1, true), r2, new RefCoord(CellArea.MaxColumn, true), _s.Substring(start, e2 - start));
            Span(a, start, e2);
            return a;
        }

        return ParseNumber();
    }

    private NumberNode ParseNumber()
    {
        int start = _p;
        int p = _p;
        bool digits = false;
        while (p < _s.Length && char.IsAsciiDigit(_s[p]))
        {
            p++;
            digits = true;
        }

        if (p < _s.Length && _s[p] == _loc.DecimalSeparator)
        {
            p++;
            while (p < _s.Length && char.IsAsciiDigit(_s[p]))
            {
                p++;
                digits = true;
            }
        }

        if (!digits)
        {
            throw Error("formula.bad-number", "A number was expected.", start);
        }

        if (p < _s.Length && (_s[p] == 'E' || _s[p] == 'e'))
        {
            int q = p + 1;
            if (q < _s.Length && (_s[q] == '+' || _s[q] == '-'))
            {
                q++;
            }

            if (q < _s.Length && char.IsAsciiDigit(_s[q]))
            {
                while (q < _s.Length && char.IsAsciiDigit(_s[q]))
                {
                    q++;
                }

                p = q;
            }
        }

        if (p < _s.Length && (RefSyntax.IsIdentStart(_s[p])))
        {
            throw Error("formula.bad-number", "A number is followed by letters.", start, p - start + 1);
        }

        string raw = _s.Substring(start, p - start);
        string invariant = _loc.DecimalSeparator == '.' ? raw : raw.Replace(_loc.DecimalSeparator, '.');
        // Excel keeps 15 significant digits of a number typed into a formula and drops the rest (123456789.123456789 is 123456789.123456).
        if (!double.TryParse(TruncateTo15Digits(invariant), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || double.IsInfinity(v))
        {
            throw Error("formula.bad-number", "The number is too large.", start, p - start);
        }

        _p = p;
        var n = new NumberNode(v, invariant);
        Span(n, start, p);
        return n;
    }

    private static string TruncateTo15Digits(string number)
    {
        int e = number.IndexOfAny(['E', 'e']);
        string mantissa = e < 0 ? number : number[..e];
        string exponent = e < 0 ? string.Empty : number[e..];
        int significant = 0;
        bool started = false;
        var sb = new System.Text.StringBuilder(mantissa.Length);
        foreach (char c in mantissa)
        {
            if (c == '.')
            {
                sb.Append(c);
                continue;
            }

            if (c != '0')
            {
                started = true;
            }

            if (started)
            {
                significant++;
            }

            sb.Append(significant > 15 ? '0' : c);
        }

        return sb.Append(exponent).ToString();
    }

    private ArrayConstantNode ParseArrayConstant()
    {
        int start = _p;
        if (_arrayDepth > 0)
        {
            throw Error("formula.nested-array", "An array constant cannot hold another.", _p);
        }

        _arrayDepth++;
        _p++;
        var cells = new List<CalcValue>();
        int cols = -1;
        int curCols = 0;
        int rows = 1;
        while (true)
        {
            EatWs();
            cells.Add(ParseArrayElement());
            curCols++;
            EatWs();
            if (Cur == _loc.ArrayColumnSeparator)
            {
                _p++;
                continue;
            }

            if (Cur == _loc.ArrayRowSeparator)
            {
                if (cols >= 0 && curCols != cols)
                {
                    throw Error("formula.array-ragged", "The rows of an array constant differ in length.", _p);
                }

                cols = curCols;
                curCols = 0;
                rows++;
                _p++;
                continue;
            }

            if (Cur == '}')
            {
                if (cols >= 0 && curCols != cols)
                {
                    throw Error("formula.array-ragged", "The rows of an array constant differ in length.", _p);
                }

                cols = curCols;
                _p++;
                break;
            }

            throw AtEnd ? Error("formula.unbalanced-brace", "An array constant is not closed.", start) : Error("formula.unexpected-character", $"Unexpected '{Cur}' in an array constant.", _p);
        }

        _arrayDepth--;
        var arr = new CalcArray(rows, cols, [.. cells]);
        var n = new ArrayConstantNode(arr, ReferenceEquals(_loc, FormulaLocale.Invariant) ? _s.Substring(start, _p - start) : null);
        Span(n, start, _p);
        return n;
    }

    private CalcValue ParseArrayElement()
    {
        char c = Cur;
        if (c == '{')
        {
            throw Error("formula.nested-array", "An array constant cannot hold another.", _p);
        }

        if (c == '"')
        {
            return CalcValue.FromText(ParseString().Value);
        }

        if (c == '#')
        {
            return CalcValue.FromError(((ErrorNode)ParseErrorLiteral(null)).Error);
        }

        bool neg = false;
        bool signed = false;
        if (c == '-' || c == '+')
        {
            neg = c == '-';
            signed = true;
            _p++;
            c = Cur;
        }

        if (char.IsAsciiDigit(c) || c == _loc.DecimalSeparator)
        {
            double v = ParseNumber().Value;
            return CalcValue.FromNumber(neg ? -v : v);
        }

        if (!signed && RefSyntax.IsIdentStart(c))
        {
            int s = _p;
            while (!AtEnd && RefSyntax.IsIdentChar(Cur))
            {
                _p++;
            }

            string w = _s.Substring(s, _p - s);
            if (w.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
            {
                return CalcValue.True;
            }

            if (w.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
            {
                return CalcValue.False;
            }

            throw Error("formula.bad-array-element", "An array constant holds numbers, text, TRUE, FALSE and errors.", s, w.Length);
        }

        throw Error("formula.bad-array-element", "An array constant holds numbers, text, TRUE, FALSE and errors.", _p);
    }

    private FormulaNode ParseNameOrReference()
    {
        int start = _p;
        SheetPrefix? prefix = TryScanPrefix();
        if (prefix is not null)
        {
            if (Cur == '#' && Peek(1) != '\0' && prefix is not null)
            {
                if (_s.AsSpan(_p).StartsWith("#REF!", StringComparison.OrdinalIgnoreCase))
                {
                    return ParseErrorLiteral(prefix);
                }
            }

            return ParseReferenceBody(prefix, start);
        }

        return ParseReferenceBody(null, start);
    }

    // Reads Sheet1!, 'My Sheet'!, Sheet1:Sheet3!, [1]Sheet1!, [1]! and returns null (position unchanged) when none is there.
    private SheetPrefix? TryScanPrefix()
    {
        int start = _p;
        int p = _p;
        string? workbook = null;
        if (p < _s.Length && _s[p] == '[')
        {
            int q = p + 1;
            while (q < _s.Length && _s[q] != ']' && _s[q] != '[')
            {
                q++;
            }

            if (q >= _s.Length || _s[q] != ']' || q == p + 1)
            {
                return null;
            }

            string inner = _s.Substring(p + 1, q - p - 1);

            // [1]Sheet1!A1 is an external reference; [Col] and [@Col] are table specifiers, which never have a name or '!' straight after.
            if (inner[0] is '@' or '#' || q + 1 >= _s.Length || !(_s[q + 1] == '!' || RefSyntax.IsIdentStart(_s[q + 1]) || _s[q + 1] == '\''))
            {
                return null;
            }

            workbook = inner;
            p = q + 1;
        }

        string? sheet = null;
        string? last = null;
        if (p < _s.Length && _s[p] == '\'')
        {
            int q = p + 1;
            var sb = new System.Text.StringBuilder();
            while (true)
            {
                if (q >= _s.Length)
                {
                    return null;
                }

                if (_s[q] == '\'')
                {
                    if (q + 1 < _s.Length && _s[q + 1] == '\'')
                    {
                        sb.Append('\'');
                        q += 2;
                        continue;
                    }

                    break;
                }

                sb.Append(_s[q++]);
            }

            q++;
            if (q >= _s.Length || _s[q] != '!')
            {
                return null;
            }

            string all = sb.ToString();
            int colon = workbook is null ? all.IndexOf(':', StringComparison.Ordinal) : -1;
            if (colon > 0)
            {
                sheet = all[..colon];
                last = all[(colon + 1)..];
            }
            else
            {
                sheet = all;
            }

            p = q + 1;
        }
        else
        {
            int q = p;
            while (q < _s.Length && (RefSyntax.IsIdentChar(_s[q])))
            {
                q++;
            }

            if (q < _s.Length && _s[q] == '!' && (q > p || workbook is not null))
            {
                sheet = q > p ? _s.Substring(p, q - p) : null;
                p = q + 1;
            }
            else if (q < _s.Length && _s[q] == ':' && q > p && workbook is null)
            {
                int q2 = q + 1;
                while (q2 < _s.Length && RefSyntax.IsIdentChar(_s[q2]))
                {
                    q2++;
                }

                if (q2 < _s.Length && _s[q2] == '!' && q2 > q + 1)
                {
                    sheet = _s.Substring(p, q - p);
                    last = _s.Substring(q + 1, q2 - q - 1);
                    p = q2 + 1;
                }
                else
                {
                    return null;
                }
            }
            else
            {
                return null;
            }
        }

        _p = p;
        return new SheetPrefix(sheet, last, workbook, _s.Substring(start, p - start));
    }

    private FormulaNode ParseReferenceBody(SheetPrefix? prefix, int nodeStart)
    {
        int start = _p;
        if (_r1c1 && TryParseR1C1(prefix) is { } rc)
        {
            return rc;
        }

        if (!_r1c1)
        {
            if (RefSyntax.TryScanCell(_s, _p, out int e, out RefCoord row, out RefCoord col) && !(e < _s.Length && _s[e] == '['))
            {
                _p = e;
                var n = new CellReferenceNode(prefix, row, col, _s.Substring(start, e - start));
                Span(n, nodeStart, e);
                return n;
            }

            if (RefSyntax.TryScanColumn(_s, _p, out int c1e, out RefCoord c1) && c1e < _s.Length && _s[c1e] == ':' &&
                RefSyntax.TryScanColumn(_s, c1e + 1, out int c2e, out RefCoord c2))
            {
                _p = c2e;
                var n = new AreaReferenceNode(prefix, AreaKind.Columns, new RefCoord(1, true), c1, new RefCoord(CellArea.MaxRow, true), c2, _s.Substring(start, c2e - start));
                Span(n, nodeStart, c2e);
                return n;
            }

            if (RefSyntax.TryScanRow(_s, _p, out int r1e, out RefCoord r1) && r1e < _s.Length && _s[r1e] == ':' &&
                RefSyntax.TryScanRow(_s, r1e + 1, out int r2e, out RefCoord r2))
            {
                _p = r2e;
                var n = new AreaReferenceNode(prefix, AreaKind.Rows, r1, new RefCoord(1, true), r2, new RefCoord(CellArea.MaxColumn, true), _s.Substring(start, r2e - start));
                Span(n, nodeStart, r2e);
                return n;
            }
        }

        if (Cur == '[')
        {
            // [@Col] or [Col]: a table specifier without a table name.
            string spec = ScanBracket();
            var sn = new StructuredReferenceNode(prefix, null, spec);
            Span(sn, nodeStart, _p);
            return sn;
        }

        if (Cur == '$' || !RefSyntax.IsIdentStart(Cur))
        {
            throw Error("formula.bad-reference", "A reference or name was expected.", _p);
        }

        int ns = _p;
        while (!AtEnd && RefSyntax.IsIdentChar(Cur))
        {
            _p++;
        }

        string word = _s.Substring(ns, _p - ns);
        if (Cur == '(' && prefix is null)
        {
            _p++;
            List<FormulaNode> args = ParseArgs(out string? inner);
            string callName = word;
            if (!ReferenceEquals(_loc, FormulaLocale.Invariant))
            {
                string bare = FunctionCallNode.StripPrefix(word);
                string inv = _loc.ToInvariantName(bare);
                callName = inv == bare ? word : word[..(word.Length - bare.Length)] + inv;
            }

            var call = new FunctionCallNode(callName, args) { InnerSpace = inner, NameIsFileForm = _o.FileForm || word.StartsWith("_xl", StringComparison.OrdinalIgnoreCase) };
            Span(call, nodeStart, _p);
            return call;
        }

        if (Cur == '[')
        {
            string spec = ScanBracket();
            var sn = new StructuredReferenceNode(prefix, word, spec);
            Span(sn, nodeStart, _p);
            return sn;
        }

        if (prefix is null)
        {
            if (word.Equals("TRUE", StringComparison.OrdinalIgnoreCase) || word.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
            {
                var b = new BooleanNode(word.Length == 4, word);
                Span(b, nodeStart, _p);
                return b;
            }

            if (_o.TemplateExtensions && word.Contains('.', StringComparison.Ordinal) && !word.StartsWith("_xl", StringComparison.OrdinalIgnoreCase))
            {
                string[] segs = word.Split('.');
                bool ok = true;
                foreach (string sg in segs)
                {
                    ok &= sg.Length > 0;
                }

                if (ok)
                {
                    var mp = new MemberPathNode(segs);
                    Span(mp, nodeStart, _p);
                    return mp;
                }
            }
        }

        var name = new NameNode(prefix, word);
        Span(name, nodeStart, _p);
        return name;
    }

    // Reads a balanced [...] group; a single quote escapes the next character (ECMA-376 Part 1 §18.17.2).
    private string ScanBracket()
    {
        int start = _p;
        int depth = 0;
        while (!AtEnd)
        {
            char c = _s[_p];
            if (c == '\'' && _p + 1 < _s.Length)
            {
                _p += 2;
                continue;
            }

            if (c == '[')
            {
                depth++;
            }
            else if (c == ']')
            {
                depth--;
                if (depth == 0)
                {
                    _p++;
                    return _s.Substring(start, _p - start);
                }
            }

            _p++;
        }

        throw Error("formula.unbalanced-bracket", "A table specifier is not closed.", start, _p - start);
    }

    private FormulaNode? TryParseR1C1(SheetPrefix? prefix)
    {
        int start = _p;
        if (!RefSyntax.TryScanR1C1(_s, _p, out int len, out RefSyntax.R1C1Kind kind, out RefSyntax.Rc row, out RefSyntax.Rc col))
        {
            return null;
        }

        int end = _p + len;
        int oR = _o.OriginRow;
        int oC = _o.OriginColumn;
        if (kind == RefSyntax.R1C1Kind.Cell)
        {
            _p = end;
            var n = new CellReferenceNode(prefix, ToCoord(row, oR, CellArea.MaxRow), ToCoord(col, oC, CellArea.MaxColumn));
            Span(n, start, end);
            return n;
        }

        // R1:R3 and C2:C4 are whole rows and columns; a lone R or C is the whole row or column of the formula's cell.
        if (end < _s.Length && _s[end] == ':' && RefSyntax.TryScanR1C1(_s, end + 1, out int len2, out RefSyntax.R1C1Kind kind2, out RefSyntax.Rc row2, out RefSyntax.Rc col2) && kind2 == kind)
        {
            _p = end + 1 + len2;
            AreaReferenceNode a = kind == RefSyntax.R1C1Kind.Row
                ? new AreaReferenceNode(prefix, AreaKind.Rows, ToCoord(row, oR, CellArea.MaxRow), new RefCoord(1, true), ToCoord(row2, oR, CellArea.MaxRow), new RefCoord(CellArea.MaxColumn, true))
                : new AreaReferenceNode(prefix, AreaKind.Columns, new RefCoord(1, true), ToCoord(col, oC, CellArea.MaxColumn), new RefCoord(CellArea.MaxRow, true), ToCoord(col2, oC, CellArea.MaxColumn));
            Span(a, start, _p);
            return a;
        }

        _p = end;
        AreaReferenceNode single = kind == RefSyntax.R1C1Kind.Row
            ? new AreaReferenceNode(prefix, AreaKind.Rows, ToCoord(row, oR, CellArea.MaxRow), new RefCoord(1, true), ToCoord(row, oR, CellArea.MaxRow), new RefCoord(CellArea.MaxColumn, true))
            : new AreaReferenceNode(prefix, AreaKind.Columns, new RefCoord(1, true), ToCoord(col, oC, CellArea.MaxColumn), new RefCoord(CellArea.MaxRow, true), ToCoord(col, oC, CellArea.MaxColumn));
        Span(single, start, end);
        return single;
    }

    private static RefCoord ToCoord(RefSyntax.Rc rc, int origin, int max)
    {
        if (rc.Absolute)
        {
            return new RefCoord(Math.Min(rc.Value, max), true);
        }

        // Relative offsets wrap around the sheet edge as Excel does.
        int v = (((origin + rc.Value - 1) % max) + max) % max + 1;
        return new RefCoord(v, false);
    }
}
