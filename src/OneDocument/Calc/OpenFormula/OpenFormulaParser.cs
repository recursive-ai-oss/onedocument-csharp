using System.Globalization;
using System.Text;

namespace RecursiveAI.OneDocument.Calc.OpenFormula;

/// <summary>Options for <see cref="OpenFormulaParser"/>.</summary>
public sealed class OpenFormulaParseOptions
{
    /// <summary>The limits; formula length and nesting depth are checked. Default: <see cref="OfficeLimits.Default"/>.</summary>
    public OfficeLimits Limits { get; init; } = OfficeLimits.Default;

    /// <summary>
    /// True (the default) to map function names to the calc engine's names (<c>COM.MICROSOFT.XLOOKUP</c> to <c>XLOOKUP</c>, <c>LEGACY.CHIDIST</c>
    /// to <c>CHIDIST</c>). False keeps every name as written.
    /// </summary>
    public bool MapFunctionNames { get; init; } = true;
}

/// <summary>
/// Reads OpenFormula (OpenDocument 1.3 Part 4 "Recalculated Formula (OpenFormula) Format"; ODF 1.2 Part 2) into the public formula AST:
/// references in brackets (<c>[.A1]</c>, <c>[$Sheet1.A1:.B2]</c>), <c>;</c> between arguments, inline arrays <c>{1;2|3;4}</c>, the
/// reference operators <c>!</c> (intersection) and <c>~</c> (union), and <c>COM.MICROSOFT.</c>, <c>ORG.OPENOFFICE.</c> and <c>ORG.LIBREOFFICE.</c> prefixes on
/// function names. The text is the part after the <c>of:=</c> / <c>=</c> a file stores.
/// </summary>
public static class OpenFormulaParser
{
    /// <summary>Parses an OpenFormula expression.</summary>
    /// <param name="text">The expression, with or without a leading <c>=</c> and a namespace prefix such as <c>of:=</c>.</param>
    /// <param name="node">The tree, or null.</param>
    /// <param name="error">The located error when parsing failed. Codes: <c>openformula.syntax</c>, <c>openformula.too-long</c>, <c>openformula.too-deep</c>,
    /// <c>openformula.external-reference</c> (a reference into another document, which is never followed), <c>openformula.bad-reference</c>.</param>
    /// <param name="options">Options, or null for the defaults.</param>
    /// <returns>True when parsed. Bad text never throws.</returns>
    public static bool TryParse(string text, out FormulaNode? node, out FormulaSyntaxError error, OpenFormulaParseOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        options ??= new OpenFormulaParseOptions();
        node = null;
        string body = StripPrefix(text);
        if (body.Length > options.Limits.MaxFormulaLength)
        {
            error = new FormulaSyntaxError("openformula.too-long", "The formula is longer than the limit.", options.Limits.MaxFormulaLength, 1);
            return false;
        }

        var reader = new Reader(body, options);
        try
        {
            node = reader.ParseAll();
            error = default;
            return true;
        }
        catch (Reader.Fail f)
        {
            error = f.Error;
            return false;
        }
    }

    /// <summary>Removes the namespace prefix and the equals sign a file stores in front of the expression (<c>of:=</c>, <c>oooc:=</c>, <c>=</c>).</summary>
    /// <param name="text">The attribute value.</param>
    /// <returns>The expression.</returns>
    public static string StripPrefix(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string s = text.TrimStart();
        int colon = s.IndexOf(':', StringComparison.Ordinal);
        int equals = s.IndexOf('=', StringComparison.Ordinal);
        if (colon > 0 && equals == colon + 1)
        {
            bool word = true;
            for (int i = 0; i < colon; i++)
            {
                word &= char.IsAsciiLetterOrDigit(s[i]);
            }

            if (word)
            {
                return s[(equals + 1)..];
            }
        }

        return s.StartsWith('=') ? s[1..] : s;
    }

    private sealed class Reader
    {
        internal sealed class Fail(FormulaSyntaxError error) : Exception(error.Message)
        {
            public FormulaSyntaxError Error { get; } = error;
        }

        private readonly string _s;
        private readonly OpenFormulaParseOptions _o;
        private int _p;
        private int _depth;

        public Reader(string s, OpenFormulaParseOptions o)
        {
            _s = s;
            _o = o;
        }

        private Fail Error(string message, string code = "openformula.syntax", int length = 1) => new(new FormulaSyntaxError(code, message, _p, length));

        public FormulaNode ParseAll()
        {
            FormulaNode n = Comparison();
            Skip();
            if (_p < _s.Length)
            {
                throw Error("Unexpected '" + _s[_p] + "'.");
            }

            return n;
        }

        private void Skip()
        {
            while (_p < _s.Length && char.IsWhiteSpace(_s[_p]))
            {
                _p++;
            }
        }

        private bool Peek(char c)
        {
            Skip();
            return _p < _s.Length && _s[_p] == c;
        }

        private bool Take(char c)
        {
            if (Peek(c))
            {
                _p++;
                return true;
            }

            return false;
        }

        private void Enter()
        {
            if (++_depth > _o.Limits.MaxFormulaDepth)
            {
                throw new Fail(new FormulaSyntaxError("openformula.too-deep", "The formula nests deeper than the limit.", _p, 1));
            }
        }

        private FormulaNode Comparison()
        {
            Enter();
            FormulaNode left = Concat();
            while (true)
            {
                Skip();
                BinaryOperator op;
                if (Starts("<>"))
                {
                    op = BinaryOperator.NotEqual;
                    _p += 2;
                }
                else if (Starts("<="))
                {
                    op = BinaryOperator.LessOrEqual;
                    _p += 2;
                }
                else if (Starts(">="))
                {
                    op = BinaryOperator.GreaterOrEqual;
                    _p += 2;
                }
                else if (Take('='))
                {
                    op = BinaryOperator.Equal;
                }
                else if (Take('<'))
                {
                    op = BinaryOperator.Less;
                }
                else if (Take('>'))
                {
                    op = BinaryOperator.Greater;
                }
                else
                {
                    break;
                }

                left = new BinaryNode(op, left, Concat());
            }

            _depth--;
            return left;
        }

        private bool Starts(string t) => string.CompareOrdinal(_s, _p, t, 0, t.Length) == 0;

        private FormulaNode Concat()
        {
            FormulaNode left = Additive();
            while (Take('&'))
            {
                left = new BinaryNode(BinaryOperator.Concat, left, Additive());
            }

            return left;
        }

        private FormulaNode Additive()
        {
            FormulaNode left = Multiplicative();
            while (true)
            {
                if (Take('+'))
                {
                    left = new BinaryNode(BinaryOperator.Add, left, Multiplicative());
                }
                else if (Take('-'))
                {
                    left = new BinaryNode(BinaryOperator.Subtract, left, Multiplicative());
                }
                else
                {
                    return left;
                }
            }
        }

        private FormulaNode Multiplicative()
        {
            FormulaNode left = Power();
            while (true)
            {
                if (Take('*'))
                {
                    left = new BinaryNode(BinaryOperator.Multiply, left, Power());
                }
                else if (Take('/'))
                {
                    left = new BinaryNode(BinaryOperator.Divide, left, Power());
                }
                else
                {
                    return left;
                }
            }
        }

        // OpenFormula 1.3 Part 4 §5.8 (operator precedence): the reference operators bind tightest, then unary + and -, then %, then ^ (left to right).
        private FormulaNode Power()
        {
            FormulaNode left = Unary();
            while (Take('^'))
            {
                left = new BinaryNode(BinaryOperator.Power, left, Unary());
            }

            return left;
        }

        private FormulaNode Unary()
        {
            Skip();
            if (_p < _s.Length && (_s[_p] == '-' || _s[_p] == '+'))
            {
                char op = _s[_p++];
                Enter();
                FormulaNode operand = Unary();
                _depth--;
                return new UnaryNode(op, operand);
            }

            if (_p < _s.Length && _s[_p] == '@')
            {
                _p++;
                return new UnaryNode('@', Unary()) { IsFileForm = false };
            }

            return Percent();
        }

        private FormulaNode Percent()
        {
            FormulaNode n = Union();
            while (Take('%'))
            {
                n = new PercentNode(n);
            }

            return n;
        }

        private FormulaNode Union()
        {
            FormulaNode left = Intersection();
            while (Take('~'))
            {
                left = new BinaryNode(BinaryOperator.Union, left, Intersection());
            }

            return left;
        }

        private FormulaNode Intersection()
        {
            FormulaNode left = RangeOp();
            while (Take('!'))
            {
                left = new BinaryNode(BinaryOperator.Intersect, left, RangeOp());
            }

            return left;
        }

        private FormulaNode RangeOp()
        {
            FormulaNode left = Primary();
            while (true)
            {
                Skip();
                if (_p < _s.Length && _s[_p] == ':')
                {
                    _p++;
                    left = new BinaryNode(BinaryOperator.Range, left, Primary());
                }
                else if (_p < _s.Length && _s[_p] == '#' && (left.Kind is NodeKind.Cell or NodeKind.Area or NodeKind.Name))
                {
                    _p++;
                    left = new SpillNode(left);
                }
                else
                {
                    return left;
                }
            }
        }

        private FormulaNode Primary()
        {
            Skip();
            if (_p >= _s.Length)
            {
                throw Error("The formula ends where an operand was expected.");
            }

            char c = _s[_p];
            if (c == '(')
            {
                _p++;
                Enter();
                FormulaNode inner = Comparison();
                _depth--;
                if (!Take(')'))
                {
                    throw Error("A closing parenthesis is missing.");
                }

                return new ParenNode(inner);
            }

            if (c == '"')
            {
                return new TextNode(ReadString());
            }

            if (c == '{')
            {
                return ReadArray();
            }

            if (c == '[')
            {
                return ReadReference();
            }

            if (c == '#')
            {
                return ReadError(null);
            }

            if (char.IsAsciiDigit(c) || c == '.')
            {
                return ReadNumber();
            }

            if (c == '\'')
            {
                // A quoted name is a defined name with unusual characters.
                string quoted = ReadQuoted();
                return new NameNode(null, quoted);
            }

            if (char.IsLetter(c) || c == '_' || c == '\\' || c > 127)
            {
                return ReadIdentifier();
            }

            throw Error("Unexpected '" + c + "'.");
        }

        private string ReadString()
        {
            int start = _p;
            _p++;
            var sb = new StringBuilder();
            while (true)
            {
                if (_p >= _s.Length)
                {
                    _p = start;
                    throw Error("A text literal is not closed.");
                }

                if (_s[_p] == '"')
                {
                    if (_p + 1 < _s.Length && _s[_p + 1] == '"')
                    {
                        sb.Append('"');
                        _p += 2;
                        continue;
                    }

                    _p++;
                    return sb.ToString();
                }

                sb.Append(_s[_p++]);
            }
        }

        private string ReadQuoted()
        {
            int start = _p;
            _p++;
            var sb = new StringBuilder();
            while (true)
            {
                if (_p >= _s.Length)
                {
                    _p = start;
                    throw Error("A quoted name is not closed.");
                }

                if (_s[_p] == '\'')
                {
                    if (_p + 1 < _s.Length && _s[_p + 1] == '\'')
                    {
                        sb.Append('\'');
                        _p += 2;
                        continue;
                    }

                    _p++;
                    return sb.ToString();
                }

                sb.Append(_s[_p++]);
            }
        }

        private NumberNode ReadNumber()
        {
            int start = _p;
            while (_p < _s.Length && char.IsAsciiDigit(_s[_p]))
            {
                _p++;
            }

            if (_p < _s.Length && _s[_p] == '.')
            {
                _p++;
                while (_p < _s.Length && char.IsAsciiDigit(_s[_p]))
                {
                    _p++;
                }
            }

            if (_p < _s.Length && (_s[_p] == 'E' || _s[_p] == 'e'))
            {
                int save = _p;
                _p++;
                if (_p < _s.Length && (_s[_p] == '+' || _s[_p] == '-'))
                {
                    _p++;
                }

                if (_p < _s.Length && char.IsAsciiDigit(_s[_p]))
                {
                    while (_p < _s.Length && char.IsAsciiDigit(_s[_p]))
                    {
                        _p++;
                    }
                }
                else
                {
                    _p = save;
                }
            }

            ReadOnlySpan<char> text = _s.AsSpan(start, _p - start);
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || !double.IsFinite(v))
            {
                _p = start;
                throw Error("'" + text.ToString() + "' is not a number.", length: Math.Max(1, text.Length));
            }

            return new NumberNode(v);
        }

        private ErrorNode ReadError(SheetPrefix? prefix)
        {
            int start = _p;
            _p++; // '#'
            while (_p < _s.Length && (char.IsAsciiLetterOrDigit(_s[_p]) || _s[_p] is '/' or '!' or '?' or '_'))
            {
                _p++;
                if (_s[_p - 1] is '!' or '?')
                {
                    break;
                }
            }

            string t = _s[start.._p];
            if (!CalcErrors.TryParse(t, out CalcError e))
            {
                _p = start;
                throw Error("'" + t + "' is not an error value.", length: Math.Max(1, t.Length));
            }

            return new ErrorNode(e, prefix);
        }

        private ArrayConstantNode ReadArray()
        {
            int start = _p;
            _p++;
            var rows = new List<List<CalcValue>>();
            var row = new List<CalcValue>();
            while (true)
            {
                row.Add(ArrayElement());
                Skip();
                if (_p >= _s.Length)
                {
                    _p = start;
                    throw Error("An array constant is not closed.");
                }

                char c = _s[_p++];
                if (c == ';')
                {
                    continue;
                }

                if (c == '|')
                {
                    rows.Add(row);
                    row = [];
                    continue;
                }

                if (c == '}')
                {
                    rows.Add(row);
                    break;
                }

                _p--;
                throw Error("Unexpected '" + c + "' in an array constant.");
            }

            int cols = rows[0].Count;
            foreach (List<CalcValue> r in rows)
            {
                if (r.Count != cols)
                {
                    _p = start;
                    throw Error("The rows of an array constant differ in length.");
                }
            }

            var cells = new CalcValue[rows.Count * cols];
            for (int i = 0; i < rows.Count; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    cells[(i * cols) + j] = rows[i][j];
                }
            }

            return new ArrayConstantNode(new CalcArray(rows.Count, cols, cells));
        }

        private CalcValue ArrayElement()
        {
            Skip();
            if (_p >= _s.Length)
            {
                throw Error("An array constant is not closed.");
            }

            char c = _s[_p];
            if (c == '"')
            {
                return CalcValue.FromText(ReadString());
            }

            if (c == '#')
            {
                return CalcValue.FromError(ReadError(null).Error);
            }

            bool negative = false;
            if (c is '-' or '+')
            {
                negative = c == '-';
                _p++;
                Skip();
            }

            if (_p < _s.Length && (char.IsAsciiDigit(_s[_p]) || _s[_p] == '.'))
            {
                double v = ReadNumber().Value;
                return CalcValue.FromNumber(negative ? -v : v);
            }

            if (!negative && c != '+' && _p < _s.Length && char.IsLetter(_s[_p]))
            {
                int start = _p;
                while (_p < _s.Length && char.IsLetter(_s[_p]))
                {
                    _p++;
                }

                string word = _s[start.._p];
                if (word.Equals("TRUE", StringComparison.OrdinalIgnoreCase) || word.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
                {
                    if (Peek('('))
                    {
                        _p++;
                        if (!Take(')'))
                        {
                            throw Error("A closing parenthesis is missing.");
                        }
                    }

                    return CalcValue.FromBoolean(word.Equals("TRUE", StringComparison.OrdinalIgnoreCase));
                }
            }

            throw Error("An array constant holds only numbers, text, booleans and errors.");
        }

        private FormulaNode ReadIdentifier()
        {
            int start = _p;
            while (_p < _s.Length && (char.IsLetterOrDigit(_s[_p]) || _s[_p] is '_' or '.' or '\\' || _s[_p] > 127))
            {
                _p++;
            }

            string name = _s[start.._p];
            Skip();
            if (_p < _s.Length && _s[_p] == '(')
            {
                _p++;
                return ReadCall(name);
            }

            if (name.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
            {
                return new BooleanNode(true);
            }

            if (name.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
            {
                return new BooleanNode(false);
            }

            return new NameNode(null, name);
        }

        private FormulaNode ReadCall(string rawName)
        {
            var args = new List<FormulaNode>();
            Skip();
            if (Take(')'))
            {
                return Call(rawName, args);
            }

            while (true)
            {
                Skip();
                if (_p < _s.Length && (_s[_p] == ';' || _s[_p] == ')'))
                {
                    args.Add(new MissingNode());
                }
                else
                {
                    args.Add(Comparison());
                }

                Skip();
                if (Take(';'))
                {
                    continue;
                }

                if (Take(')'))
                {
                    break;
                }

                throw Error(_p >= _s.Length ? "A closing parenthesis is missing." : "Unexpected '" + _s[_p] + "' in an argument list.");
            }

            return Call(rawName, args);
        }

        private FormulaNode Call(string rawName, List<FormulaNode> args)
        {
            string name = _o.MapFunctionNames ? OpenFormulaFunctions.ToCalcName(rawName) : rawName;
            if (_o.MapFunctionNames && args.Count == 0)
            {
                // TRUE() and FALSE() are how OpenFormula writes the two booleans.
                if (name.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
                {
                    return new BooleanNode(true);
                }

                if (name.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
                {
                    return new BooleanNode(false);
                }
            }

            return new FunctionCallNode(name, args) { NameIsFileForm = false };
        }

        // ---- references ----------------------------------------------------------------------------------------------------------------

        private readonly record struct Corner(string? Sheet, bool HasDot, int Row, bool RowAbs, int Column, bool ColumnAbs, bool IsError);

        private FormulaNode ReadReference()
        {
            int start = _p;
            _p++; // '['
            Skip();
            if (_p < _s.Length && _s[_p] == '#')
            {
                ErrorNode e = ReadError(null);
                ExpectClose(start);
                return e;
            }

            Corner a = ReadCorner(start);
            Corner? b = null;
            Skip();
            if (_p < _s.Length && _s[_p] == ':')
            {
                _p++;
                b = ReadCorner(start);
            }

            ExpectClose(start);
            if (a.IsError || (b?.IsError ?? false))
            {
                SheetPrefix? ep = a.Sheet is null ? null : new SheetPrefix(a.Sheet);
                return new ErrorNode(CalcError.Ref, ep);
            }

            SheetPrefix? prefix = null;
            if (a.Sheet is not null)
            {
                string? last = b is { Sheet: { } bs } && !string.Equals(bs, a.Sheet, StringComparison.Ordinal) ? bs : null;
                prefix = new SheetPrefix(a.Sheet, last);
            }

            if (b is null)
            {
                if (a.Row == 0 || a.Column == 0)
                {
                    _p = start;
                    throw Error("A single reference must name a cell.", "openformula.bad-reference");
                }

                return new CellReferenceNode(prefix, new RefCoord(a.Row, a.RowAbs), new RefCoord(a.Column, a.ColumnAbs));
            }

            Corner z = b.Value;
            if (a.Row != 0 && a.Column != 0 && z.Row != 0 && z.Column != 0)
            {
                if (prefix is { Is3D: true } && a.Row == z.Row && a.Column == z.Column && a.RowAbs == z.RowAbs && a.ColumnAbs == z.ColumnAbs)
                {
                    return new CellReferenceNode(prefix, new RefCoord(a.Row, a.RowAbs), new RefCoord(a.Column, a.ColumnAbs));
                }

                return new AreaReferenceNode(prefix, AreaKind.Cells, new RefCoord(a.Row, a.RowAbs), new RefCoord(a.Column, a.ColumnAbs), new RefCoord(z.Row, z.RowAbs), new RefCoord(z.Column, z.ColumnAbs));
            }

            if (a.Row == 0 && z.Row == 0 && a.Column != 0 && z.Column != 0)
            {
                return new AreaReferenceNode(prefix, AreaKind.Columns, new RefCoord(1, true), new RefCoord(a.Column, a.ColumnAbs), new RefCoord(CellArea.MaxRow, true), new RefCoord(z.Column, z.ColumnAbs));
            }

            if (a.Column == 0 && z.Column == 0 && a.Row != 0 && z.Row != 0)
            {
                return new AreaReferenceNode(prefix, AreaKind.Rows, new RefCoord(a.Row, a.RowAbs), new RefCoord(1, true), new RefCoord(z.Row, z.RowAbs), new RefCoord(CellArea.MaxColumn, true));
            }

            _p = start;
            throw Error("The two ends of a range name different kinds of reference.", "openformula.bad-reference");
        }

        private void ExpectClose(int start)
        {
            Skip();
            if (_p < _s.Length && _s[_p] == ']')
            {
                _p++;
                return;
            }

            _p = start;
            throw Error("A reference is not closed.");
        }

        // OpenDocument 1.3 Part 4 §5.7 (references): an optional '$' (the sheet is absolute), the sheet name (quoted with single
        // quotes when it holds special characters), a '.', then the cell address or a whole column or row.
        private Corner ReadCorner(int refStart)
        {
            Skip();
            string? sheet = null;
            bool hasDot = false;
            if (_p < _s.Length && _s[_p] == '$' && _p + 1 < _s.Length && (_s[_p + 1] == '\'' || char.IsLetter(_s[_p + 1]) || _s[_p + 1] == '_' || _s[_p + 1] > 127 || char.IsAsciiDigit(_s[_p + 1])))
            {
                // "$A$1" without a sheet starts with a column letter after the dot; a '$' directly before a sheet name is the sheet marker.
                int save = _p;
                _p++;
                sheet = ReadSheetName(refStart);
                if (_p >= _s.Length || _s[_p] != '.')
                {
                    _p = save;
                    sheet = null;
                }
            }
            else if (_p < _s.Length && _s[_p] == '\'')
            {
                sheet = ReadSheetName(refStart);
            }
            else if (_p < _s.Length && _s[_p] != '.')
            {
                sheet = ReadSheetName(refStart);
            }

            if (_p < _s.Length && _s[_p] == '#' && sheet is not null)
            {
                // ['file'#$Sheet.A1]: a reference into another document.
                _p = refStart;
                throw Error("A reference into another document is not followed.", "openformula.external-reference");
            }

            if (_p >= _s.Length || _s[_p] != '.')
            {
                _p = refStart;
                throw Error("A reference needs a '.' before the cell address.", "openformula.bad-reference");
            }

            _p++;
            hasDot = true;
            if (_p < _s.Length && _s[_p] == '#')
            {
                ReadError(null);
                return new Corner(sheet, hasDot, 0, false, 0, false, true);
            }

            bool colAbs = false;
            if (_p < _s.Length && _s[_p] == '$')
            {
                colAbs = true;
                _p++;
            }

            int ls = _p;
            while (_p < _s.Length && char.IsAsciiLetter(_s[_p]))
            {
                _p++;
            }

            int column = 0;
            if (_p > ls)
            {
                column = RefSyntax.ColumnNumber(_s.AsSpan(ls, _p - ls));
                if (column == 0)
                {
                    _p = refStart;
                    throw Error("The column is beyond the last column of a sheet.", "openformula.bad-reference");
                }
            }

            bool rowAbs = false;
            if (_p < _s.Length && _s[_p] == '$')
            {
                rowAbs = true;
                _p++;
            }
            else if (column == 0 && colAbs)
            {
                // "$1": the dollar belonged to the row.
                rowAbs = true;
                colAbs = false;
            }

            int ds = _p;
            while (_p < _s.Length && char.IsAsciiDigit(_s[_p]))
            {
                _p++;
            }

            int row = 0;
            if (_p > ds)
            {
                if (!int.TryParse(_s.AsSpan(ds, _p - ds), NumberStyles.None, CultureInfo.InvariantCulture, out row) || row < 1 || row > CellArea.MaxRow)
                {
                    _p = refStart;
                    throw Error("The row is beyond the last row of a sheet.", "openformula.bad-reference");
                }
            }

            if (column == 0 && row == 0)
            {
                _p = refStart;
                throw Error("A reference holds no column or row.", "openformula.bad-reference");
            }

            return new Corner(sheet, hasDot, row, rowAbs, column, colAbs, false);
        }

        private string ReadSheetName(int refStart)
        {
            if (_p < _s.Length && _s[_p] == '\'')
            {
                return ReadQuoted();
            }

            int start = _p;
            while (_p < _s.Length && _s[_p] is not ('.' or ':' or ']' or '#'))
            {
                _p++;
            }

            if (_p == start)
            {
                _p = refStart;
                throw Error("A reference names no sheet.", "openformula.bad-reference");
            }

            return _s[start.._p];
        }
    }
}
