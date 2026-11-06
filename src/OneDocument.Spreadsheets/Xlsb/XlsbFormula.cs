using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Calc;

namespace RecursiveAI.OneDocument.Xlsb;

/// <summary>Where a binary formula lives, which decides how relative references are stored and what the root token's class is.</summary>
public enum XlsbFormulaKind
{
    /// <summary>A cell formula (<c>CellParsedFormula</c>): references hold absolute coordinates plus relative flags.</summary>
    Cell = 0,

    /// <summary>The master formula of a shared formula (<c>SharedParsedFormula</c>): references are stored as offsets from the cell where the formula is used.</summary>
    Shared = 1,

    /// <summary>An array formula (<c>ArrayParsedFormula</c>).</summary>
    Array = 2,

    /// <summary>A defined name, a conditional-format or validation formula: references are offsets from the origin cell, and a reference result is allowed.</summary>
    Name = 3,
}

/// <summary>
/// Converts between binary formulas ([MS-XLSB] §2.5.98: <c>Rgce</c> tokens and their <c>RgbExtra</c>) and the public formula tree
/// of <see cref="FormulaNode"/>. Decoding is tolerant: an unreadable token ends the formula as <c>#NAME?</c> and returns false.
/// </summary>
public static class XlsbFormula
{
    private const int MaxDepth = 1024;

    private static readonly string[] ErrorText = ["#NULL!", "#DIV/0!", "#VALUE!", "#REF!", "#NAME?", "#NUM!", "#N/A"];

    // [MS-XLSB] §2.5.98.2: the stored error codes.
    private static CalcError ErrorFromCode(int b) => b switch
    {
        0x00 => CalcError.Null,
        0x07 => CalcError.Div0,
        0x0F => CalcError.Value,
        0x17 => CalcError.Ref,
        0x1D => CalcError.Name,
        0x24 => CalcError.Num,
        0x2A => CalcError.NA,
        0x2B => CalcError.GettingData,
        0x2C => CalcError.Spill,
        0x2D => CalcError.Calc,
        _ => CalcError.Value,
    };

    /// <summary>Gets the stored code of an error value, as written by <c>BErr</c> fields.</summary>
    /// <param name="error">The error.</param>
    /// <param name="exact">False when the stored code is the nearest documented one, not the error itself.</param>
    /// <returns>The code.</returns>
    public static int ErrorToCode(CalcError error, out bool exact)
    {
        exact = true;
        switch (error)
        {
            case CalcError.Null: return 0x00;
            case CalcError.Div0: return 0x07;
            case CalcError.Value: return 0x0F;
            case CalcError.Ref: return 0x17;
            case CalcError.Name: return 0x1D;
            case CalcError.Num: return 0x24;
            case CalcError.NA: return 0x2A;
            case CalcError.GettingData: return 0x2B;
            case CalcError.Spill: return 0x2C;
            case CalcError.Calc: return 0x2D;
            default:
                exact = false;
                return 0x0F;
        }
    }

    /// <summary>Reads the stored error code of a cell or token.</summary>
    /// <param name="code">The <c>BErr</c> byte.</param>
    /// <returns>The error value.</returns>
    public static CalcError ErrorFromStored(int code) => ErrorFromCode(code);

    // ---------------------------------------------------------------- decoding

    /// <summary>Decodes a binary formula into the public tree.</summary>
    /// <param name="rgce">The token bytes.</param>
    /// <param name="rgcb">The extra data (array constants, shared-area lists).</param>
    /// <param name="context">The workbook facts the tokens index into.</param>
    /// <param name="kind">Where the formula lives.</param>
    /// <param name="originRow">Zero-based row of the cell the relative references are offsets from (shared formulas, names).</param>
    /// <param name="originColumn">Zero-based column of that cell.</param>
    /// <param name="formula">The tree; on failure a <c>#NAME?</c> error node.</param>
    /// <param name="problem">What went wrong, or null.</param>
    /// <returns>True when the whole formula was understood.</returns>
    public static bool TryDecode(ReadOnlySpan<byte> rgce, ReadOnlySpan<byte> rgcb, XlsbFormulaContext context, XlsbFormulaKind kind, int originRow, int originColumn, out FormulaNode formula, out string? problem)
    {
        var d = new Decoder(rgce, rgcb, context, kind, originRow, originColumn);
        bool ok = d.Run();
        formula = d.Result ?? new ErrorNode(CalcError.Name);
        problem = d.Problem;
        return ok;
    }

    private sealed class Decoder(ReadOnlySpan<byte> rgce, ReadOnlySpan<byte> rgcb, XlsbFormulaContext cx, XlsbFormulaKind kind, int originRow, int originColumn)
    {
        private readonly byte[] _rgce = rgce.ToArray();
        private readonly byte[] _rgcb = rgcb.ToArray();
        private readonly List<FormulaNode> _stack = [];
        private int _p;
        private int _extra;
        private string _pendingSpace = string.Empty;
        private char _pendingParenSpace;

        public FormulaNode? Result { get; private set; }

        public string? Problem { get; private set; }

        public bool Run()
        {
            try
            {
                if (_rgce.Length == 0)
                {
                    Problem = "empty formula";
                    return false;
                }

                if (_rgce[0] == 0x01 && kind is XlsbFormulaKind.Cell)
                {
                    // PtgExp marks a cell that shares or is part of another formula; the caller resolves it from the shared/array record.
                    Problem = "ptgexp";
                    return false;
                }

                while (_p < _rgce.Length)
                {
                    Step();
                }

                if (_stack.Count != 1)
                {
                    Problem = "stack holds " + _stack.Count.ToString(CultureInfo.InvariantCulture) + " values at the end";
                    Result = _stack.Count > 0 ? _stack[^1] : null;
                    return false;
                }

                FormulaNode root = _stack[0];
                if (_pendingSpace.Length > 0)
                {
                    root.AddTrailing(_pendingSpace);
                }

                Result = root;
                return true;
            }
            catch (FormatException ex)
            {
                Problem = ex.Message;
                Result = _stack.Count > 0 ? _stack[^1] : null;
                return false;
            }
        }

        private int U8() => _p < _rgce.Length ? _rgce[_p++] : throw new FormatException("token cut short");

        private int U16()
        {
            if (_rgce.Length - _p < 2)
            {
                throw new FormatException("token cut short");
            }

            int v = BinaryPrimitives.ReadUInt16LittleEndian(_rgce.AsSpan(_p));
            _p += 2;
            return v;
        }

        private int I32()
        {
            if (_rgce.Length - _p < 4)
            {
                throw new FormatException("token cut short");
            }

            int v = BinaryPrimitives.ReadInt32LittleEndian(_rgce.AsSpan(_p));
            _p += 4;
            return v;
        }

        private double F64()
        {
            if (_rgce.Length - _p < 8)
            {
                throw new FormatException("token cut short");
            }

            double v = BinaryPrimitives.ReadDoubleLittleEndian(_rgce.AsSpan(_p));
            _p += 8;
            return v;
        }

        private int ExtraI32()
        {
            if (_rgcb.Length - _extra < 4)
            {
                throw new FormatException("extra data cut short");
            }

            int v = BinaryPrimitives.ReadInt32LittleEndian(_rgcb.AsSpan(_extra));
            _extra += 4;
            return v;
        }

        private void Push(FormulaNode n)
        {
            if (_pendingSpace.Length > 0)
            {
                n.LeadingSpace = _pendingSpace;
                _pendingSpace = string.Empty;
            }

            _stack.Add(n);
            if (_stack.Count > MaxDepth)
            {
                throw new FormatException("formula too deep");
            }
        }

        private FormulaNode Pop()
        {
            if (_stack.Count == 0)
            {
                throw new FormatException("operand missing");
            }

            FormulaNode n = _stack[^1];
            _stack.RemoveAt(_stack.Count - 1);
            return n;
        }

        private void Binary(BinaryOperator op)
        {
            FormulaNode right = Pop();
            FormulaNode left = Pop();
            Push(new BinaryNode(op, left, right));
        }

        private void Step()
        {
            int id = U8();
            if (id >= 0x20)
            {
                Classed(id & 0x1F);
                return;
            }

            switch (id)
            {
                case 0x01:
                    throw new FormatException("ptgexp inside a formula");
                case 0x03: Binary(BinaryOperator.Add); break;
                case 0x04: Binary(BinaryOperator.Subtract); break;
                case 0x05: Binary(BinaryOperator.Multiply); break;
                case 0x06: Binary(BinaryOperator.Divide); break;
                case 0x07: Binary(BinaryOperator.Power); break;
                case 0x08: Binary(BinaryOperator.Concat); break;
                case 0x09: Binary(BinaryOperator.Less); break;
                case 0x0A: Binary(BinaryOperator.LessOrEqual); break;
                case 0x0B: Binary(BinaryOperator.Equal); break;
                case 0x0C: Binary(BinaryOperator.GreaterOrEqual); break;
                case 0x0D: Binary(BinaryOperator.Greater); break;
                case 0x0E: Binary(BinaryOperator.NotEqual); break;
                case 0x0F: Binary(BinaryOperator.Intersect); break;
                case 0x10: Binary(BinaryOperator.Union); break;
                case 0x11: Binary(BinaryOperator.Range); break;
                case 0x12: Push(new UnaryNode('+', Pop()) { IsFileForm = true }); break;
                case 0x13: Push(new UnaryNode('-', Pop()) { IsFileForm = true }); break;
                case 0x14: Push(new PercentNode(Pop())); break;
                case 0x15:
                {
                    FormulaNode inner = Pop();
                    var paren = new ParenNode(inner);
                    if (_pendingParenSpace != 0)
                    {
                        _pendingParenSpace = (char)0;
                    }

                    Push(paren);
                    break;
                }

                case 0x16: Push(new MissingNode()); break;
                case 0x17:
                {
                    int cch = U16();
                    if (_rgce.Length - _p < cch * 2)
                    {
                        throw new FormatException("string cut short");
                    }

                    string s = Encoding.Unicode.GetString(_rgce, _p, cch * 2);
                    _p += cch * 2;
                    Push(new TextNode(s));
                    break;
                }

                case 0x18: List(); break;
                case 0x19: Attr(); break;
                case 0x1C: Push(new ErrorNode(ErrorFromCode(U8()))); break;
                case 0x1D: Push(new BooleanNode(U8() != 0)); break;
                case 0x1E: Push(new NumberNode(U16())); break;
                case 0x1F: Push(new NumberNode(F64())); break;
                default:
                    throw new FormatException("unknown token 0x" + id.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        private void Attr()
        {
            int grbit = U8();
            if ((grbit & 0x04) != 0)
            {
                // PtgAttrChoose: cOffset, then cOffset + 1 offsets (control data, ignored when rebuilding text).
                int n = U16();
                _p += (n + 1) * 2;
                if (_p > _rgce.Length)
                {
                    throw new FormatException("choose table cut short");
                }

                return;
            }

            if ((grbit & 0x10) != 0)
            {
                U16();
                FormulaNode arg = Pop();
                Push(new FunctionCallNode("SUM", [arg]));
                return;
            }

            if ((grbit & 0x40) != 0)
            {
                int type = U8();
                int cch = U8();
                AddSpace(type, cch);
                return;
            }

            // Semi (volatile), IF, GoTo, Baxcel and IfError carry one 16-bit field that only guides evaluation.
            U16();
        }

        private void AddSpace(int type, int cch)
        {
            string text = new(type is 1 or 3 or 5 ? '\n' : ' ', cch);
            switch (type)
            {
                case 0:
                case 1:
                case 6:
                    _pendingSpace += text;
                    break;
                default:
                    // Spaces before the parentheses of PtgParen are kept as spaces around the inner expression.
                    if (_stack.Count > 0)
                    {
                        if (type is 4 or 5)
                        {
                            _stack[^1].AddTrailing(text);
                        }
                        else
                        {
                            _stack[^1].LeadingSpace = text + _stack[^1].LeadingSpace;
                        }
                    }

                    break;
            }
        }

        private void List()
        {
            int e = U8();
            if (e != 0x19)
            {
                throw new FormatException("bad list token");
            }

            int ixti = U16();
            int bits = U16();
            int listIndex = I32();
            int colFirst = U16();
            int colLast = U16();
            int columns = bits & 3;
            int rowType = (bits >> 2) & 0x1F;
            bool invalid = (bits & 0x1000) != 0;
            bool nonresident = (bits & 0x2000) != 0;
            if (nonresident && !invalid)
            {
                // PtgExtraList: the rectangle of an external table; not representable as a structured reference.
                throw new FormatException("external table reference");
            }

            XlsbTableInfo? table = invalid ? null : cx.TableById?.Invoke(listIndex);
            if (table is null)
            {
                Push(new ErrorNode(CalcError.Ref));
                return;
            }

            SheetPrefix? prefix = cx.PrefixOf(ixti, out _);
            var items = new List<string>();
            switch (rowType)
            {
                case 0x01: items.Add("[#All]"); break;
                case 0x02: items.Add("[#Headers]"); break;
                case 0x06: items.Add("[#Headers]"); items.Add("[#Data]"); break;
                case 0x08: items.Add("[#Totals]"); break;
                case 0x0C: items.Add("[#Data]"); items.Add("[#Totals]"); break;
                case 0x10: items.Add("[#This Row]"); break;
            }

            string? columnSpec = null;
            if (columns == 1 && colFirst < table.Columns.Count)
            {
                columnSpec = "[" + EscapeColumn(table.Columns[colFirst]) + "]";
            }
            else if (columns == 2 && colFirst < table.Columns.Count && colLast < table.Columns.Count)
            {
                columnSpec = "[" + EscapeColumn(table.Columns[colFirst]) + "]:[" + EscapeColumn(table.Columns[colLast]) + "]";
            }

            string spec;
            if (items.Count == 0 && columnSpec is null)
            {
                spec = "[#Data]";
            }
            else if (items.Count == 0 && columns == 1)
            {
                spec = columnSpec!;
            }
            else
            {
                var all = new List<string>(items);
                if (columnSpec is not null)
                {
                    all.Add(columnSpec);
                }

                spec = "[" + string.Join(",", all) + "]";
                if (all.Count == 1)
                {
                    spec = all[0];
                }
            }

            Push(new StructuredReferenceNode(prefix, table.Name, spec));
        }

        private static string EscapeColumn(string name)
        {
            // ECMA-376 Part 1 §18.17.2: a column name escapes [ ] # and ' with a single quote.
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                if (c is '[' or ']' or '#' or '\'')
                {
                    sb.Append('\'');
                }

                sb.Append(c);
            }

            return sb.ToString();
        }

        private void Classed(int b)
        {
            switch (b)
            {
                case 0x00: Array(); break;
                case 0x01: Func(fixedCount: true); break;
                case 0x02: Func(fixedCount: false); break;
                case 0x03: Push(NameNodeOf(I32(), null)); break;
                case 0x04: Push(Ref(null, relative: false)); break;
                case 0x05: Push(Area(null, relative: false)); break;
                case 0x06:
                    _p += 4 + 2;
                    SkipMem();
                    break;
                case 0x07:
                case 0x08:
                    _p += 4 + 2 + (b == 0x07 ? 0 : 0);
                    if (_p > _rgce.Length)
                    {
                        throw new FormatException("mem token cut short");
                    }

                    break;
                case 0x09:
                    U16();
                    break;
                case 0x0A: _p += 6; Push(new ErrorNode(CalcError.Ref)); break;
                case 0x0B: _p += 12; Push(new ErrorNode(CalcError.Ref)); break;
                case 0x0C: Push(Ref(null, relative: true)); break;
                case 0x0D: Push(Area(null, relative: true)); break;
                case 0x19:
                {
                    int ixti = U16();
                    int nameIndex = I32();
                    Push(NameXNode(ixti, nameIndex));
                    break;
                }

                case 0x1A:
                {
                    int ixti = U16();
                    SheetPrefix? prefix = cx.PrefixOf(ixti, out bool bad);
                    FormulaNode r = Ref(prefix, relative: kind is XlsbFormulaKind.Name or XlsbFormulaKind.Shared);
                    Push(bad ? new ErrorNode(CalcError.Ref) : r);
                    break;
                }

                case 0x1B:
                {
                    int ixti = U16();
                    SheetPrefix? prefix = cx.PrefixOf(ixti, out bool bad);
                    FormulaNode r = Area(prefix, relative: kind is XlsbFormulaKind.Name or XlsbFormulaKind.Shared);
                    Push(bad ? new ErrorNode(CalcError.Ref) : r);
                    break;
                }

                case 0x1C: U16(); _p += 6; Push(new ErrorNode(CalcError.Ref)); break;
                case 0x1D: U16(); _p += 12; Push(new ErrorNode(CalcError.Ref)); break;
                default:
                    throw new FormatException("unknown token class 0x" + b.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        private void SkipMem()
        {
            // PtgExtraMem: a count and that many 16-byte rectangles (the cached areas); only a hint.
            int count = ExtraI32();
            if (count < 0 || _rgcb.Length - _extra < (long)count * 16)
            {
                throw new FormatException("mem area cut short");
            }

            _extra += count * 16;
        }

        private void Array()
        {
            _p += 14;
            if (_p > _rgce.Length)
            {
                throw new FormatException("array token cut short");
            }

            int rows = ExtraI32();
            int cols = ExtraI32();
            if (rows <= 0 || cols <= 0 || (long)rows * cols > 65536)
            {
                throw new FormatException("array constant dimensions");
            }

            var cells = new CalcValue[rows * cols];
            for (int i = 0; i < cells.Length; i++)
            {
                if (_extra >= _rgcb.Length)
                {
                    throw new FormatException("array constant cut short");
                }

                int t = _rgcb[_extra++];
                switch (t)
                {
                    case 0:
                        if (_rgcb.Length - _extra < 8)
                        {
                            throw new FormatException("array number cut short");
                        }

                        cells[i] = CalcValue.FromNumber(BinaryPrimitives.ReadDoubleLittleEndian(_rgcb.AsSpan(_extra)));
                        _extra += 8;
                        break;
                    case 1:
                    {
                        if (_rgcb.Length - _extra < 2)
                        {
                            throw new FormatException("array string cut short");
                        }

                        int cch = BinaryPrimitives.ReadUInt16LittleEndian(_rgcb.AsSpan(_extra));
                        _extra += 2;
                        if (_rgcb.Length - _extra < cch * 2)
                        {
                            throw new FormatException("array string cut short");
                        }

                        cells[i] = CalcValue.FromText(Encoding.Unicode.GetString(_rgcb, _extra, cch * 2));
                        _extra += cch * 2;
                        break;
                    }

                    case 2:
                        cells[i] = CalcValue.FromBoolean(_rgcb[_extra++] != 0);
                        break;
                    case 4:
                        cells[i] = CalcValue.FromError(ErrorFromCode(_rgcb[_extra]));
                        _extra += 4;
                        break;
                    default:
                        throw new FormatException("array element type");
                }
            }

            Push(new ArrayConstantNode(new CalcArray(rows, cols, cells)));
        }

        private CellReferenceNode Ref(SheetPrefix? prefix, bool relative)
        {
            int row = I32();
            int col = U16();
            (RefCoord r, RefCoord c) = Coordinates(row, col, relative);
            return new CellReferenceNode(prefix, r, c);
        }

        private (RefCoord Row, RefCoord Column) Coordinates(int row, int colField, bool relativeForm)
        {
            bool colRel = (colField & 0x4000) != 0;
            bool rowRel = (colField & 0x8000) != 0;
            int col = colField & 0x3FFF;
            if (relativeForm)
            {
                // [MS-XLSB] §2.5.98.92: with a relative flag the stored value is a signed offset from the origin cell.
                if (rowRel)
                {
                    row = unchecked(originRow + row);
                    row = ((row % 1048576) + 1048576) % 1048576;
                }

                if (colRel)
                {
                    int off = (col & 0x2000) != 0 ? col - 0x4000 : col;
                    col = (((originColumn + off) % 16384) + 16384) % 16384;
                }
            }

            return (new RefCoord(row + 1, !rowRel), new RefCoord(col + 1, !colRel));
        }

        private AreaReferenceNode Area(SheetPrefix? prefix, bool relative)
        {
            int r1 = I32();
            int r2 = I32();
            int c1 = U16();
            int c2 = U16();
            (RefCoord row1, RefCoord col1) = Coordinates(r1, c1, relative);
            (RefCoord row2, RefCoord col2) = Coordinates(r2, c2, relative);
            AreaKind k = AreaKind.Cells;
            if ((c1 & 0x3FFF) == 0 && (c2 & 0x3FFF) == 16383 && !relative)
            {
                k = AreaKind.Rows;
            }
            else if (r1 == 0 && r2 == 1048575 && !relative)
            {
                k = AreaKind.Columns;
            }

            return new AreaReferenceNode(prefix, k, row1, col1, row2, col2);
        }

        private FormulaNode NameNodeOf(int index, SheetPrefix? prefix)
        {
            if (index < 1 || index > cx.Names.Count)
            {
                return new ErrorNode(CalcError.Name);
            }

            XlsbDefinedName n = cx.Names[index - 1];
            SheetPrefix? scope = n.SheetIndex >= 0 && n.SheetIndex < cx.SheetNames.Count ? new SheetPrefix(cx.SheetNames[n.SheetIndex]) : prefix;
            return new NameNode(scope, n.Name);
        }

        private FormulaNode NameXNode(int ixti, int nameIndex)
        {
            SheetPrefix? prefix = cx.PrefixOf(ixti, out bool bad);
            if (bad)
            {
                return new ErrorNode(CalcError.Ref);
            }

            // Names of another book are numbered inside that book's link part; keep the index so the formula still prints.
            if (ixti < cx.Xtis.Count && cx.SupBooks[cx.Xtis[ixti].SupBook].Kind is XlsbSupBookKind.Self or XlsbSupBookKind.Same)
            {
                return NameNodeOf(nameIndex, prefix);
            }

            return new NameNode(prefix, "Name" + nameIndex.ToString(CultureInfo.InvariantCulture));
        }

        private void Func(bool fixedCount)
        {
            int count;
            int id;
            if (fixedCount)
            {
                id = U16();
                if (!XlsbFunctionTable.TryGet(id, out _, out int min, out _))
                {
                    throw new FormatException("unknown function " + id.ToString(CultureInfo.InvariantCulture));
                }

                count = min;
            }
            else
            {
                count = U8();
                int tab = U16();
                if ((tab & 0x8000) != 0)
                {
                    throw new FormatException("macro-sheet function");
                }

                id = tab;
            }

            if (_stack.Count < count)
            {
                throw new FormatException("function arguments missing");
            }

            var args = new FormulaNode[count];
            for (int i = count - 1; i >= 0; i--)
            {
                args[i] = Pop();
            }

            string name;
            if (id == 255)
            {
                if (count == 0)
                {
                    throw new FormatException("user-defined function without a name");
                }

                if (args[0] is not NameNode nn)
                {
                    Push(new InvokeNode(args[0], args[1..]));
                    return;
                }

                name = nn.Prefix is null ? nn.Name : nn.Prefix + nn.Name;
                if (count == 2 && string.Equals(FunctionCallNode.StripPrefix(name), "SINGLE", StringComparison.OrdinalIgnoreCase))
                {
                    // Implicit intersection is stored as a call of the future function SINGLE, the spill reference as ANCHORARRAY.
                    Push(new UnaryNode('@', args[1]));
                    return;
                }

                if (count == 2 && string.Equals(FunctionCallNode.StripPrefix(name), "ANCHORARRAY", StringComparison.OrdinalIgnoreCase))
                {
                    Push(new SpillNode(args[1]));
                    return;
                }

                var call = new FunctionCallNode(name, args[1..]);
                call.LeadingSpace = nn.LeadingSpace;
                Push(call);
                return;
            }

            if (!XlsbFunctionTable.TryGet(id, out name, out _, out _))
            {
                throw new FormatException("unknown function " + id.ToString(CultureInfo.InvariantCulture));
            }

            Push(new FunctionCallNode(name, args));
        }
    }

    // ---------------------------------------------------------------- encoding

    /// <summary>Encodes a formula tree as binary tokens.</summary>
    /// <param name="formula">The tree.</param>
    /// <param name="context">The workbook facts; entries the formula needs are added to it.</param>
    /// <param name="kind">Where the formula will live.</param>
    /// <param name="originRow">Zero-based row of the cell the formula is stored for (relative forms are offsets from it).</param>
    /// <param name="originColumn">Zero-based column of that cell.</param>
    /// <param name="rgce">The token bytes.</param>
    /// <param name="rgcb">The extra data bytes.</param>
    /// <param name="problem">What could not be encoded exactly (the token stream is still valid), or null.</param>
    /// <returns>True when the formula was encoded without approximation.</returns>
    public static bool TryEncode(FormulaNode formula, XlsbFormulaContext context, XlsbFormulaKind kind, int originRow, int originColumn, out byte[] rgce, out byte[] rgcb, out string? problem)
    {
        var e = new Encoder(context, kind, originRow, originColumn);
        return e.Run(formula, out rgce, out rgcb, out problem);
    }

    private enum Cls
    {
        Ref = 1,
        Value = 2,
        Array = 3,
    }

    private static readonly HashSet<string> Volatile = new(StringComparer.OrdinalIgnoreCase) { "NOW", "TODAY", "RAND", "RANDBETWEEN", "OFFSET", "INDIRECT", "INFO", "CELL", "RANDARRAY" };

    // Parameters Excel stores with the array class, taken from how these functions evaluate their arguments; Excel settles it at the 1.0 gate.
    private static readonly Dictionary<string, int> ArrayParams = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SUMPRODUCT"] = -1, ["MMULT"] = 3, ["MDETERM"] = 1, ["MINVERSE"] = 1, ["TRANSPOSE"] = 1, ["SUMX2MY2"] = 3, ["SUMX2PY2"] = 3, ["SUMXMY2"] = 3,
        ["LINEST"] = 3, ["LOGEST"] = 3, ["TREND"] = 7, ["GROWTH"] = 7, ["FREQUENCY"] = 3, ["CORREL"] = 3, ["COVAR"] = 3, ["PEARSON"] = 3, ["RSQ"] = 3,
        ["SLOPE"] = 3, ["INTERCEPT"] = 3, ["STEYX"] = 3, ["FORECAST"] = 6, ["CHITEST"] = 3, ["FTEST"] = 3, ["TTEST"] = 3,
    };

    private sealed class Encoder(XlsbFormulaContext cx, XlsbFormulaKind kind, int originRow, int originColumn)
    {
        private readonly ByteBuf _rgce = new();
        private readonly ByteBuf _rgcb = new();
        private string? _problem;
        private bool _volatile;
        private int _depth;

        public bool Run(FormulaNode root, out byte[] rgce, out byte[] rgcb, out string? problem)
        {
            Cls rootClass = kind switch
            {
                XlsbFormulaKind.Array => Cls.Array,
                XlsbFormulaKind.Name => Cls.Ref,
                _ => Cls.Value,
            };

            Emit(root, rootClass);
            byte[] body = _rgce.ToArray();
            if (_volatile)
            {
                // PtgAttrSemi marks a formula that must be recalculated every time ([MS-XLSB] §2.5.98.29).
                byte[] withSemi = new byte[body.Length + 4];
                withSemi[0] = 0x19;
                withSemi[1] = 0x01;
                body.CopyTo(withSemi, 4);
                body = withSemi;
            }

            rgce = body;
            rgcb = _rgcb.ToArray();
            problem = _problem;
            return _problem is null;
        }

        private void Note(string text)
        {
            _problem ??= text;
            cx.ApproximatedCount++;
        }

        private void Byte(int b) => _rgce.WriteByte((byte)b);

        private void U16(int v)
        {
            Byte(v & 0xFF);
            Byte((v >> 8) & 0xFF);
        }

        private void I32(int v)
        {
            Span<byte> s = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(s, v);
            _rgce.Write(s);
        }

        private int Pos => (int)_rgce.Length;

        private void Spaces(string? text, int typeSpace, int typeBreak)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                int j = i;
                while (j < text.Length && (text[j] == '\n' || text[j] == '\r') == (c == '\n' || c == '\r'))
                {
                    j++;
                }

                int run = Math.Min(j - i, 255);
                Byte(0x19);
                Byte(0x40);
                Byte(c is '\n' or '\r' ? typeBreak : typeSpace);
                Byte(run);
                i = j;
            }
        }

        private void Emit(FormulaNode n, Cls cls)
        {
            if (++_depth > MaxDepth)
            {
                throw new FormatException("formula too deep");
            }

            Spaces(n.LeadingSpace, 0, 1);
            switch (n)
            {
                case NumberNode x:
                    EmitNumber(x.Value);
                    break;
                case TextNode x:
                    if (x.Value.Length > 255)
                    {
                        Note("text longer than 255 characters in a formula");
                    }

                    Byte(0x17);
                    U16(x.Value.Length);
                    _rgce.Write(Encoding.Unicode.GetBytes(x.Value));
                    break;
                case BooleanNode x:
                    Byte(0x1D);
                    Byte(x.Value ? 1 : 0);
                    break;
                case ErrorNode x:
                    Byte(0x1C);
                    Byte(ErrorToCode(x.Error, out bool exact));
                    if (!exact)
                    {
                        Note("error value " + x.Error + " has no stored code");
                    }

                    break;
                case MissingNode:
                    Byte(0x16);
                    break;
                case CellReferenceNode x:
                    Cell(x, cls);
                    break;
                case AreaReferenceNode x:
                    AreaToken(x, cls);
                    break;
                case NameNode x:
                    EmitName(x, cls);
                    break;
                case StructuredReferenceNode x:
                    EmitList(x, cls);
                    break;
                case ParenNode x:
                    Emit(x.Inner, cls);
                    Byte(0x15);
                    break;
                case UnaryNode x:
                    EmitUnary(x, cls);
                    break;
                case PercentNode x:
                    Emit(x.Operand, ValueClass(cls));
                    Byte(0x14);
                    break;
                case SpillNode x:
                    EmitCall("_xlfn.ANCHORARRAY", [x.Operand], cls);
                    break;
                case BinaryNode x:
                    EmitBinary(x, cls);
                    break;
                case FunctionCallNode x:
                    EmitCall(x.RawName, x.ArgumentArray, cls);
                    break;
                case ArrayConstantNode x:
                    EmitArray(x, cls);
                    break;
                case InvokeNode x:
                    // A computed callee is the first operand of a user-defined call (function number 255), as a name is.
                    Emit(x.Callee, Cls.Ref);
                    foreach (FormulaNode a in x.Arguments)
                    {
                        Emit(a, Cls.Ref);
                    }

                    Ptg(0x02, cls);
                    Byte(x.Arguments.Count + 1);
                    U16(255);
                    break;
                default:
                    Note("unsupported formula element " + n.Kind);
                    Byte(0x1C);
                    Byte(0x1D);
                    break;
            }

            Spaces(n.TrailingSpace, 6, 6);
            _depth--;
        }

        private static Cls ValueClass(Cls cls) => cls == Cls.Array ? Cls.Array : Cls.Value;

        private void EmitNumber(double v)
        {
            // PtgInt holds a whole number from 0 to 65535 ([MS-XLSB] §2.5.98.49); anything else is a PtgNum.
            if (v >= 0 && v <= 65535 && v == Math.Floor(v) && !double.IsNegative(v))
            {
                Byte(0x1E);
                U16((int)v);
                return;
            }

            Byte(0x1F);
            Span<byte> s = stackalloc byte[8];
            BinaryPrimitives.WriteDoubleLittleEndian(s, v);
            _rgce.Write(s);
        }

        private void Ptg(int baseId, Cls cls) => Byte(baseId | ((int)cls << 5));

        private bool UseRelative => kind is XlsbFormulaKind.Shared or XlsbFormulaKind.Name;

        private int ColField(RefCoord col, RefCoord row, bool relativeForm, out int rowValue, int rowIndex)
        {
            int c = col.Index - 1;
            int field;
            rowValue = rowIndex - 1;
            if (relativeForm)
            {
                if (!col.Absolute)
                {
                    c = (c - originColumn) & 0x3FFF;
                }

                if (!row.Absolute)
                {
                    rowValue = rowIndex - 1 - originRow;
                }
            }

            field = c & 0x3FFF;
            if (!col.Absolute)
            {
                field |= 0x4000;
            }

            if (!row.Absolute)
            {
                field |= 0x8000;
            }

            return field;
        }

        private void Cell(CellReferenceNode x, Cls cls)
        {
            bool rel = UseRelative;
            int field = ColField(x.Column, x.Row, rel, out int row, x.Row.Index);
            if (x.Prefix is null)
            {
                Ptg(rel ? 0x0C : 0x04, cls);
            }
            else
            {
                Ptg(0x1A, cls);
                U16(cx.XtiOf(x.Prefix));
            }

            I32(row);
            U16(field);
        }

        private void AreaToken(AreaReferenceNode x, Cls cls)
        {
            bool rel = UseRelative;
            int r1, r2, f1, f2;
            if (x.AreaKind == AreaKind.Rows)
            {
                f1 = 0 | (x.Column1.Absolute ? 0 : 0);
                f2 = 16383;
                if (!x.Row1.Absolute)
                {
                    f1 |= 0x8000;
                }

                if (!x.Row2.Absolute)
                {
                    f2 |= 0x8000;
                }

                r1 = x.Row1.Index - 1 - (!x.Row1.Absolute && rel ? originRow : 0);
                r2 = x.Row2.Index - 1 - (!x.Row2.Absolute && rel ? originRow : 0);
            }
            else if (x.AreaKind == AreaKind.Columns)
            {
                f1 = (x.Column1.Index - 1) & 0x3FFF;
                f2 = (x.Column2.Index - 1) & 0x3FFF;
                if (!x.Column1.Absolute)
                {
                    f1 |= 0x4000;
                    if (rel)
                    {
                        f1 = ((x.Column1.Index - 1 - originColumn) & 0x3FFF) | 0x4000;
                    }
                }

                if (!x.Column2.Absolute)
                {
                    f2 |= 0x4000;
                    if (rel)
                    {
                        f2 = ((x.Column2.Index - 1 - originColumn) & 0x3FFF) | 0x4000;
                    }
                }

                r1 = 0;
                r2 = 1048575;
            }
            else
            {
                f1 = ColField(x.Column1, x.Row1, rel, out r1, x.Row1.Index);
                f2 = ColField(x.Column2, x.Row2, rel, out r2, x.Row2.Index);
            }

            if (x.Prefix is null)
            {
                Ptg(rel ? 0x0D : 0x05, cls);
            }
            else
            {
                Ptg(0x1B, cls);
                U16(cx.XtiOf(x.Prefix));
            }

            I32(r1);
            I32(r2);
            U16(f1);
            U16(f2);
        }

        private void EmitName(NameNode x, Cls cls)
        {
            int sheet = -1;
            if (x.Prefix?.Sheet is { } s)
            {
                for (int i = 0; i < cx.SheetNames.Count; i++)
                {
                    if (string.Equals(cx.SheetNames[i], s, StringComparison.OrdinalIgnoreCase))
                    {
                        sheet = i;
                    }
                }
            }

            int idx = cx.FindName(x.Name, sheet);
            if (idx == 0 && sheet >= 0)
            {
                idx = cx.FindName(x.Name, -1);
            }

            if (idx == 0)
            {
                Note("name '" + x.Name + "' is not defined");
                idx = cx.EnsureName(x.Name, sheet, false);
            }

            Ptg(0x03, cls);
            I32(idx);
        }

        private void EmitList(StructuredReferenceNode x, Cls cls)
        {
            int id = x.Table is null ? 0 : cx.TableIdByName?.Invoke(x.Table) ?? 0;
            XlsbTableInfo? info = id == 0 ? null : cx.TableById?.Invoke(id);
            if (info is null)
            {
                Note("structured reference to an unknown table");
                Byte(0x1C);
                Byte(0x17);
                return;
            }

            int rowType = 0;
            int columns = 0;
            int first = 0;
            int last = 0;
            foreach (string item in x.GetItems())
            {
                switch (item.ToUpperInvariant())
                {
                    case "#ALL": rowType |= 0x01; break;
                    case "#HEADERS": rowType |= 0x02; break;
                    case "#DATA": rowType |= 0x04; break;
                    case "#TOTALS": rowType |= 0x08; break;
                    case "#THIS ROW": rowType = 0x10; break;
                    default:
                    {
                        string name = item;
                        string? second = null;
                        int colon = name.IndexOf("]:[", StringComparison.Ordinal);
                        if (colon >= 0)
                        {
                            second = name[(colon + 3)..].TrimEnd(']');
                            name = name[..colon].TrimStart('[');
                        }

                        int a = IndexOfColumn(info, name);
                        int b = second is null ? a : IndexOfColumn(info, second);
                        if (a < 0 || b < 0)
                        {
                            Note("structured reference names an unknown column");
                            Byte(0x1C);
                            Byte(0x17);
                            return;
                        }

                        if (columns == 0)
                        {
                            first = a;
                            last = b;
                            columns = a == b ? 1 : 2;
                        }

                        break;
                    }
                }
            }

            if (rowType == 0x04 || (rowType == 0 && columns != 0))
            {
                rowType = 0;
            }

            if (rowType == 0x0A)
            {
                rowType = 0x06;
            }

            int ixti = cx.XtiOf(x.Prefix);
            Byte(0x18);
            Byte(0x19);
            U16(ixti);
            int bits = columns | (rowType << 2) | ((int)(cls == Cls.Ref ? 0 : cls == Cls.Value ? 1 : 2) << 10);
            U16(bits);
            I32(id);
            U16(first);
            U16(last);
        }

        private static int IndexOfColumn(XlsbTableInfo t, string name)
        {
            name = name.Replace("'", string.Empty, StringComparison.Ordinal);
            for (int i = 0; i < t.Columns.Count; i++)
            {
                if (string.Equals(t.Columns[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private void EmitUnary(UnaryNode x, Cls cls)
        {
            if (x.Operator == '@')
            {
                EmitCall("_xlfn.SINGLE", [x.Operand], cls);
                return;
            }

            Emit(x.Operand, ValueClass(cls));
            Byte(x.Operator == '-' ? 0x13 : 0x12);
        }

        private void EmitBinary(BinaryNode x, Cls cls)
        {
            Cls child = x.Operator is BinaryOperator.Range or BinaryOperator.Intersect or BinaryOperator.Union ? Cls.Ref : ValueClass(cls);
            Emit(x.Left, child);
            Emit(x.Right, child);
            Byte(x.Operator switch
            {
                BinaryOperator.Add => 0x03,
                BinaryOperator.Subtract => 0x04,
                BinaryOperator.Multiply => 0x05,
                BinaryOperator.Divide => 0x06,
                BinaryOperator.Power => 0x07,
                BinaryOperator.Concat => 0x08,
                BinaryOperator.Less => 0x09,
                BinaryOperator.LessOrEqual => 0x0A,
                BinaryOperator.Equal => 0x0B,
                BinaryOperator.GreaterOrEqual => 0x0C,
                BinaryOperator.Greater => 0x0D,
                BinaryOperator.NotEqual => 0x0E,
                BinaryOperator.Intersect => 0x0F,
                BinaryOperator.Union => 0x10,
                _ => 0x11,
            });
        }

        private void EmitArray(ArrayConstantNode x, Cls cls)
        {
            CalcArray a = x.Value;
            Ptg(0x00, cls);
            _rgce.Write(new byte[14]);
            Span<byte> s = stackalloc byte[8];
            BinaryPrimitives.WriteInt32LittleEndian(s, a.Rows);
            _rgcb.Write(s[..4]);
            BinaryPrimitives.WriteInt32LittleEndian(s, a.Columns);
            _rgcb.Write(s[..4]);
            for (int r = 0; r < a.Rows; r++)
            {
                for (int c = 0; c < a.Columns; c++)
                {
                    CalcValue v = a[r, c];
                    switch (v.Kind)
                    {
                        case ValueKind.Number:
                            _rgcb.WriteByte(0);
                            BinaryPrimitives.WriteDoubleLittleEndian(s, v.Number);
                            _rgcb.Write(s);
                            break;
                        case ValueKind.Text:
                            _rgcb.WriteByte(1);
                            BinaryPrimitives.WriteUInt16LittleEndian(s, (ushort)Math.Min(v.Text.Length, 255));
                            _rgcb.Write(s[..2]);
                            _rgcb.Write(Encoding.Unicode.GetBytes(v.Text.AsSpan(0, Math.Min(v.Text.Length, 255)).ToString()));
                            break;
                        case ValueKind.Boolean:
                            _rgcb.WriteByte(2);
                            _rgcb.WriteByte(v.Boolean ? (byte)1 : (byte)0);
                            break;
                        case ValueKind.Error:
                            _rgcb.WriteByte(4);
                            _rgcb.WriteByte((byte)ErrorToCode(v.Error, out _));
                            _rgcb.Write(new byte[3]);
                            break;
                        default:
                            _rgcb.WriteByte(1);
                            _rgcb.Write(new byte[2]);
                            break;
                    }
                }
            }
        }

        private void EmitCall(string rawName, FormulaNode[] args, Cls cls)
        {
            string name = FunctionCallNode.StripPrefix(rawName);
            bool futureOrUser = rawName.Contains("_xl", StringComparison.OrdinalIgnoreCase) && !XlsbFunctionTable.TryGetId(name, out _);
            if (!futureOrUser && XlsbFunctionTable.TryGetId(name, out int id))
            {
                XlsbFunctionTable.TryGet(id, out string canonical, out int min, out int max);
                if (Volatile.Contains(canonical))
                {
                    _volatile = true;
                }

                if (args.Length < min || args.Length > max)
                {
                    Note("function " + canonical + " called with " + args.Length.ToString(CultureInfo.InvariantCulture) + " arguments");
                }

                switch (canonical)
                {
                    case "IF" when args.Length is 2 or 3:
                        EmitIf(args, cls);
                        return;
                    case "CHOOSE" when args.Length is >= 2 and <= 255:
                        EmitChoose(args, cls);
                        return;
                    case "IFERROR" when args.Length == 2:
                        EmitIfError(args, cls);
                        return;
                    case "SUM" when args.Length == 1:
                        EmitArg(canonical, 0, args[0]);
                        Byte(0x19);
                        Byte(0x10);
                        U16(0);
                        return;
                }

                for (int i = 0; i < args.Length; i++)
                {
                    EmitArg(canonical, i, args[i]);
                }

                if (min == max && args.Length == min)
                {
                    Ptg(0x01, cls);
                    U16(id);
                }
                else
                {
                    Ptg(0x02, cls);
                    Byte(args.Length);
                    U16(id);
                }

                return;
            }

            // A function newer than the file format, or one the workbook defines: its name is a defined name and the call is user-defined (255).
            bool future = rawName.StartsWith("_xl", StringComparison.OrdinalIgnoreCase) || FutureFunctions.PrefixFor(name).Length > 0;
            string stored = future && !rawName.StartsWith("_xl", StringComparison.OrdinalIgnoreCase) ? FutureFunctions.PrefixFor(name) + name : rawName;
            int nameIndex = cx.FindName(stored, -1);
            if (nameIndex == 0)
            {
                nameIndex = cx.EnsureName(stored, -1, future);
            }

            Ptg(0x03, Cls.Value);
            I32(nameIndex);
            foreach (FormulaNode a in args)
            {
                Emit(a, Cls.Ref);
            }

            Ptg(0x02, cls);
            Byte(args.Length + 1);
            U16(255);
        }

        private void EmitArg(string function, int index, FormulaNode arg)
        {
            Cls c = Cls.Ref;
            if (XlsbFunctionTable.TryGetId(function, out int id) && XlsbFunctionTable.TryGet(id, out _, out _, out _))
            {
                c = ParamClass(function, index);
            }

            Emit(arg, c);
        }

        private static Cls ParamClass(string function, int index)
        {
            if (ArrayParams.TryGetValue(function, out int mask) && (mask == -1 || (mask & (1 << index)) != 0))
            {
                return Cls.Array;
            }

            return ValueParams.Contains(function) ? Cls.Value : Cls.Ref;
        }

        // Functions whose parameters are plain values: a reference passed to one is read as a value.
        private static readonly HashSet<string> ValueParams = new(StringComparer.OrdinalIgnoreCase)
        {
            "ABS", "ACOS", "ASIN", "ATAN", "ATAN2", "COS", "SIN", "TAN", "EXP", "LN", "LOG", "LOG10", "SQRT", "INT", "ROUND", "ROUNDUP", "ROUNDDOWN", "MOD", "POWER", "SIGN",
            "LEFT", "RIGHT", "MID", "LEN", "UPPER", "LOWER", "PROPER", "TRIM", "TEXT", "VALUE", "CONCATENATE", "FIND", "SEARCH", "SUBSTITUTE", "REPT", "EXACT", "CHAR", "CODE",
            "DATE", "TIME", "YEAR", "MONTH", "DAY", "HOUR", "MINUTE", "SECOND", "WEEKDAY", "NOT", "ISNA", "ISERROR", "ISERR", "ISNUMBER", "ISTEXT", "ISBLANK", "ISLOGICAL", "FLOOR", "CEILING",
            "PI", "FIXED", "DOLLAR", "TRUNC", "CLEAN", "DATEVALUE", "TIMEVALUE", "EDATE", "EOMONTH", "N", "T",
        };

        private void EmitIf(FormulaNode[] args, Cls cls)
        {
            Emit(args[0], Cls.Value);
            int ifAt = Pos;
            Byte(0x19);
            Byte(0x02);
            U16(0);
            int trueStart = Pos;
            Emit(args[1], cls == Cls.Ref ? Cls.Ref : cls);
            int gotoAt1 = Pos;
            Byte(0x19);
            Byte(0x08);
            U16(0);
            int afterGoto1 = Pos;
            int gotoAt2 = -1;
            if (args.Length == 3)
            {
                Emit(args[2], cls == Cls.Ref ? Cls.Ref : cls);
                gotoAt2 = Pos;
                Byte(0x19);
                Byte(0x08);
                U16(0);
            }

            int funcAt = Pos;
            Ptg(0x02, cls);
            Byte(args.Length);
            U16(1);
            int end = Pos;
            // [MS-XLSB] §2.5.98.88: the If offset spans the true branch through its goto; each goto's offset is one less than what remains after it.
            Patch(ifAt + 2, afterGoto1 - trueStart);
            Patch(gotoAt1 + 2, end - afterGoto1 - 1);
            if (gotoAt2 >= 0)
            {
                Patch(gotoAt2 + 2, end - (gotoAt2 + 4) - 1);
            }

            _ = funcAt;
        }

        private void EmitIfError(FormulaNode[] args, Cls cls)
        {
            Emit(args[0], Cls.Value);
            int attrAt = Pos;
            Byte(0x19);
            Byte(0x80);
            U16(0);
            int start = Pos;
            Emit(args[1], Cls.Value);
            int gotoAt = Pos;
            Byte(0x19);
            Byte(0x08);
            U16(0);
            int afterGoto = Pos;
            Ptg(0x01, cls);
            U16(0x01E0);
            int end = Pos;
            Patch(attrAt + 2, afterGoto - start);
            Patch(gotoAt + 2, end - afterGoto - 1);
        }

        private void EmitChoose(FormulaNode[] args, Cls cls)
        {
            Emit(args[0], Cls.Value);
            int n = args.Length - 1;
            int chooseAt = Pos;
            Byte(0x19);
            Byte(0x04);
            U16(n);
            for (int i = 0; i <= n; i++)
            {
                U16(0);
            }

            int afterChoose = Pos;
            var gotoAts = new List<int>();
            var ends = new List<int>();
            for (int i = 1; i <= n; i++)
            {
                Emit(args[i], cls == Cls.Ref ? Cls.Ref : cls);
                gotoAts.Add(Pos);
                Byte(0x19);
                Byte(0x08);
                U16(0);
                ends.Add(Pos);
            }

            Ptg(0x02, cls);
            Byte(args.Length);
            U16(100);
            int end = Pos;
            Patch(chooseAt + 4, 2 * n + 2);
            for (int i = 0; i < n; i++)
            {
                Patch(chooseAt + 6 + (2 * i), ends[i] - afterChoose);
                Patch(gotoAts[i] + 2, end - ends[i] - 1);
            }
        }

        private void Patch(int at, int value) => _rgce.Poke(at, value);
    }

    private sealed class ByteBuf
    {
        private byte[] _a = new byte[64];
        private int _n;

        public long Length => _n;

        public void WriteByte(byte b)
        {
            if (_n == _a.Length)
            {
                Array.Resize(ref _a, _n * 2);
            }

            _a[_n++] = b;
        }

        public void Write(ReadOnlySpan<byte> s)
        {
            foreach (byte b in s)
            {
                WriteByte(b);
            }
        }

        public void Poke(int at, int value)
        {
            _a[at] = (byte)(value & 0xFF);
            _a[at + 1] = (byte)((value >> 8) & 0xFF);
        }

        public byte[] ToArray() => _a.AsSpan(0, _n).ToArray();
    }
}
