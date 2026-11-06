using System.Globalization;
using RecursiveAI.OneDocument.Calc;

namespace RecursiveAI.OneDocument.Spreadsheets.Biff;

/// <summary>What the Ptg decoder and encoder need to know about the workbook a formula sits in.</summary>
internal sealed class FormulaContext
{
    /// <summary>The BIFF version: 2, 3, 4, 5 or 8.</summary>
    public int Version { get; init; } = 8;

    /// <summary>Sheet names in workbook order, for internal 3-D references.</summary>
    public List<string> SheetNames { get; init; } = [];

    /// <summary>BIFF8 EXTERNSHEET entries (SUPBOOK index, first sheet, last sheet).</summary>
    public List<(int Book, int First, int Last)> Xti { get; init; } = [];

    /// <summary>BIFF8 SUPBOOK records.</summary>
    public List<SupBook> Books { get; init; } = [];

    /// <summary>BIFF5 EXTERNSHEET entries: the sheet name inside this workbook, or null for an external one.</summary>
    public List<string?> Biff5Externs { get; init; } = [];

    /// <summary>Names (NAME records) in file order, the target of PtgName.</summary>
    public List<DefinedNameInfo> Names { get; init; } = [];

    /// <summary>Called for each repair or loss: code and message.</summary>
    public Action<string, string>? Report { get; init; }
}

/// <summary>A SUPBOOK record ([MS-XLS] §2.4.271).</summary>
internal sealed class SupBook
{
    public bool IsInternal;
    public bool IsAddIn;
    public string Url = string.Empty;
    public List<string> Sheets = [];
    public List<string> ExternNames = [];
}

/// <summary>A defined name as read from a NAME record, with its formula once decoded.</summary>
internal sealed class DefinedNameInfo
{
    public string Name = string.Empty;
    public int SheetScope = -1;
    public bool Hidden;
    public bool IsFunction;
    public bool IsMacro;
    public bool IsBuiltIn;
    public int BuiltIn;
    public string Comment = string.Empty;
    public byte[] Rgce = [];
    public byte[] Rgcb = [];
    public string? Formula;
}

/// <summary>
/// Decodes BIFF formula tokens (Ptg) into the public formula tree ([MS-XLS] §2.5.198). BIFF8 and BIFF5 layouts differ in the width of
/// references, strings and 3-D prefixes; BIFF2–4 use the BIFF5 reference layout with narrower function and name tokens. The tree is
/// built on a value stack the way the tokens are evaluated, so the result prints back to the formula text Excel shows.
/// </summary>
internal sealed class PtgDecoder
{
    private readonly FormulaContext _ctx;
    private readonly System.Text.Encoding _enc8;
    private ReadOnlyMemory<byte> _rgce;
    private ReadOnlyMemory<byte> _rgcb;
    private int _p;
    private int _b;
    private int _row;
    private int _col;
    private readonly List<FormulaNode> _stack = [];

    public PtgDecoder(FormulaContext ctx, System.Text.Encoding enc8)
    {
        _ctx = ctx;
        _enc8 = enc8;
    }

    private bool Wide => _ctx.Version >= 8;

    /// <summary>Decodes one formula.</summary>
    /// <param name="rgce">The token bytes.</param>
    /// <param name="rgcb">The extra data after the tokens (array constants, area lists); may be empty.</param>
    /// <param name="row">0-based row of the cell the formula belongs to, for relative tokens.</param>
    /// <param name="column">0-based column of that cell.</param>
    /// <param name="node">The tree.</param>
    /// <returns>False when a token is unknown or the tokens are inconsistent; the reason went to <see cref="FormulaContext.Report"/>.</returns>
    public bool TryDecode(ReadOnlyMemory<byte> rgce, ReadOnlyMemory<byte> rgcb, int row, int column, out FormulaNode node)
    {
        _rgce = rgce;
        _rgcb = rgcb;
        _p = 0;
        _b = 0;
        _row = row;
        _col = column;
        _stack.Clear();
        node = null!;
        try
        {
            while (_p < _rgce.Length)
            {
                if (!Step())
                {
                    return false;
                }
            }
        }
        catch (FormatException ex)
        {
            _ctx.Report?.Invoke("biff.formula-token", ex.Message);
            return false;
        }

        if (_stack.Count != 1)
        {
            _ctx.Report?.Invoke("biff.formula-stack", "The formula tokens leave " + _stack.Count.ToString(CultureInfo.InvariantCulture) + " values instead of one.");
            return false;
        }

        node = _stack[0];
        return true;
    }

    private byte U8()
    {
        if (_p >= _rgce.Length)
        {
            throw new FormatException("A formula token runs past the end of the formula.");
        }

        return _rgce.Span[_p++];
    }

    private int U16() => U8() | (U8() << 8);

    private uint U32() => (uint)(U16() | (U16() << 16));

    private double F64()
    {
        long v = 0;
        for (int i = 0; i < 8; i++)
        {
            v |= (long)U8() << (8 * i);
        }

        return BitConverter.Int64BitsToDouble(v);
    }

    private FormulaNode Pop()
    {
        if (_stack.Count == 0)
        {
            throw new FormatException("A formula operator has no operand.");
        }

        FormulaNode n = _stack[^1];
        _stack.RemoveAt(_stack.Count - 1);
        return n;
    }

    private bool Step()
    {
        int id = U8();
        if (id == 0x18 || id == 0x19)
        {
            return Extended(id);
        }

        // ECMA-style class bits: tokens from 0x20 up carry a value class in bits 5-6; the operation is in the low five bits plus 0x20.
        int ptg = id >= 0x20 ? (id & 0x1F) | 0x20 : id;
        switch (ptg)
        {
            case 0x01: // PtgExp: the cell is part of a shared or array formula; handled by the caller before decoding.
                U16();
                U16();
                _ctx.Report?.Invoke("biff.formula-exp", "A shared or array formula token reached the decoder.");
                return false;
            case 0x02:
                U16();
                U16();
                _ctx.Report?.Invoke("biff.formula-table", "A data-table token reached the decoder.");
                return false;
            case 0x03: Binary(BinaryOperator.Add); return true;
            case 0x04: Binary(BinaryOperator.Subtract); return true;
            case 0x05: Binary(BinaryOperator.Multiply); return true;
            case 0x06: Binary(BinaryOperator.Divide); return true;
            case 0x07: Binary(BinaryOperator.Power); return true;
            case 0x08: Binary(BinaryOperator.Concat); return true;
            case 0x09: Binary(BinaryOperator.Less); return true;
            case 0x0A: Binary(BinaryOperator.LessOrEqual); return true;
            case 0x0B: Binary(BinaryOperator.Equal); return true;
            case 0x0C: Binary(BinaryOperator.GreaterOrEqual); return true;
            case 0x0D: Binary(BinaryOperator.Greater); return true;
            case 0x0E: Binary(BinaryOperator.NotEqual); return true;
            case 0x0F: Binary(BinaryOperator.Intersect); return true;
            case 0x10: Binary(BinaryOperator.Union); return true;
            case 0x11: Binary(BinaryOperator.Range); return true;
            case 0x12: _stack.Add(new UnaryNode('+', Pop())); return true;
            case 0x13: _stack.Add(new UnaryNode('-', Pop())); return true;
            case 0x14: _stack.Add(new PercentNode(Pop())); return true;
            case 0x15: _stack.Add(new ParenNode(Pop())); return true;
            case 0x16: _stack.Add(new MissingNode()); return true;
            case 0x17: _stack.Add(new TextNode(ReadStr())); return true;
            case 0x1C: _stack.Add(new ErrorNode(MapError(U8()))); return true;
            case 0x1D: _stack.Add(new BooleanNode(U8() != 0)); return true;
            case 0x1E: _stack.Add(new NumberNode(U16())); return true;
            case 0x1F: _stack.Add(new NumberNode(F64())); return true;
            case 0x20: SkipArrayHeader(); _stack.Add(ReadArray()); return true;
            case 0x21: Function(fixedCount: true); return true;
            case 0x22: Function(fixedCount: false); return true;
            case 0x23: Name(); return true;
            case 0x24: Ref(relative: false); return true;
            case 0x25: Area(relative: false); return true;
            case 0x26: // PtgMemArea: a cache of the referenced ranges; the tokens that follow are the real expression.
                U16();
                U16();
                U16();
                SkipExtraMem();
                return true;
            case 0x27:
            case 0x28:
                U16();
                U16();
                U16();
                return true;
            case 0x29: // PtgMemFunc: a marker for the sub-expression that follows
                U16();
                return true;
            case 0x2A: RefErr(area: false, three: false); return true;
            case 0x2B: RefErr(area: true, three: false); return true;
            case 0x2C: Ref(relative: true); return true;
            case 0x2D: Area(relative: true); return true;
            case 0x39: NameX(); return true;
            case 0x3A: Ref3d(); return true;
            case 0x3B: Area3d(); return true;
            case 0x3C: RefErr(area: false, three: true); return true;
            case 0x3D: RefErr(area: true, three: true); return true;
            default:
                _ctx.Report?.Invoke("biff.formula-token", "Unknown formula token 0x" + id.ToString("X2", CultureInfo.InvariantCulture) + ".");
                return false;
        }
    }

    private void Binary(BinaryOperator op)
    {
        FormulaNode r = Pop();
        FormulaNode l = Pop();
        _stack.Add(new BinaryNode(op, l, r));
    }

    // [MS-XLS] §2.5.198.25 and the PtgAttr* sections: control tokens that carry evaluation hints, skipped; PtgAttrSum is SUM(x).
    private bool Extended(int id)
    {
        int sub = U8();
        if (id == 0x19)
        {
            switch (sub)
            {
                case 0x01: // Semi: volatile marker
                case 0x20: // Baxcel
                case 0x21:
                    U16();
                    return true;
                case 0x02: // If: offset to the false branch
                case 0x08: // Goto
                    U16();
                    return true;
                case 0x04: // Choose: cOffset then cOffset+1 offsets
                    {
                        int c = U16();
                        for (int i = 0; i <= c; i++)
                        {
                            U16();
                        }

                        return true;
                    }

                case 0x10: // Sum: SUM(arg)
                    U16();
                    _stack.Add(new FunctionCallNode("SUM", [Pop()]));
                    return true;
                case 0x40: // Space: whitespace the author typed; it has no effect on the result and the tree does not keep it
                case 0x41:
                    U16();
                    return true;
                default:
                    break;
            }
        }
        else
        {
            switch (sub)
            {
                case 0x1D: // PtgSxName
                    U32();
                    _ctx.Report?.Invoke("biff.formula-pivot-name", "A pivot-table name token is replaced by #REF!.");
                    _stack.Add(new ErrorNode(CalcError.Ref));
                    return true;
                default:
                    break;
            }
        }

        _ctx.Report?.Invoke("biff.formula-token", "Unsupported formula token 0x" + id.ToString("X2", CultureInfo.InvariantCulture) + sub.ToString("X2", CultureInfo.InvariantCulture) + ".");
        return false;
    }

    // [MS-XLS] §2.5.198.89 PtgStr: BIFF8 is a ShortXLUnicodeString; BIFF2-5 a length byte and 8-bit characters.
    private string ReadStr()
    {
        int cch = U8();
        bool wide = false;
        if (Wide)
        {
            wide = (U8() & 1) != 0;
        }

        var sb = new System.Text.StringBuilder(cch);
        if (wide)
        {
            for (int i = 0; i < cch; i++)
            {
                sb.Append((char)U16());
            }
        }
        else
        {
            byte[] raw = new byte[cch];
            for (int i = 0; i < cch; i++)
            {
                raw[i] = U8();
            }

            sb.Append(Wide ? System.Text.Encoding.Latin1.GetString(raw) : _enc8.GetString(raw));
        }

        return sb.ToString();
    }

    private static CalcError MapError(int code) => code switch
    {
        0x00 => CalcError.Null,
        0x07 => CalcError.Div0,
        0x0F => CalcError.Value,
        0x17 => CalcError.Ref,
        0x1D => CalcError.Name,
        0x24 => CalcError.Num,
        0x2A => CalcError.NA,
        0x2B => CalcError.GettingData,
        _ => CalcError.Value,
    };

    // ---- references ----

    private SheetPrefix? PrefixFor(int ixti, out bool broken)
    {
        broken = false;
        if (ixti < 0 || ixti >= _ctx.Xti.Count)
        {
            broken = true;
            return null;
        }

        (int book, int first, int last) = _ctx.Xti[ixti];
        if (book < 0 || book >= _ctx.Books.Count)
        {
            broken = true;
            return null;
        }

        SupBook sb = _ctx.Books[book];
        if (!sb.IsInternal)
        {
            // An external workbook: the formula keeps its cached value, because writing the reference needs an external link part.
            _ctx.Report?.Invoke("loss.external-links", "A reference into another workbook (" + sb.Url + ") is not carried as a formula.");
            broken = true;
            return null;
        }

        if (first is 0xFFFF or -1 || last is 0xFFFF or -1 || first >= _ctx.SheetNames.Count || last >= _ctx.SheetNames.Count || first < 0 || last < 0)
        {
            broken = true;
            return null;
        }

        return first == last ? new SheetPrefix(_ctx.SheetNames[first]) : new SheetPrefix(_ctx.SheetNames[first], _ctx.SheetNames[last]);
    }

    private static RefCoord Row(int index0, bool relative) => new(index0 + 1, !relative);

    private static RefCoord Col(int index0, bool relative) => new(index0 + 1, !relative);

    private int ResolveRow(int raw, bool relative)
    {
        if (!relative)
        {
            return raw;
        }

        return (_row + (short)raw) & 0xFFFF;
    }

    private int ResolveCol(int raw, bool relative, int width)
    {
        if (!relative)
        {
            return raw;
        }

        int off = width == 14 ? ((raw ^ 0x2000) - 0x2000) : (sbyte)raw;
        return (_col + off) & 0xFF;
    }

    // [MS-XLS] §2.5.198.84 PtgRef / §2.5.198.88 PtgRefN: BIFF8 stores a 16-bit row and a 16-bit word holding a 14-bit column and the
    // two relative flags (bit 14 column, bit 15 row). BIFF2-5 store the flags in the row word and an 8-bit column.
    private (int Row, int Col, bool RowRel, bool ColRel) ReadLoc(bool relativeEncoding)
    {
        int rowWord = U16();
        int colWord;
        bool rowRel;
        bool colRel;
        int row;
        int col;
        if (Wide)
        {
            colWord = U16();
            rowRel = (colWord & 0x8000) != 0;
            colRel = (colWord & 0x4000) != 0;
            row = rowRel && relativeEncoding ? ResolveRow(rowWord, true) : rowWord;
            col = colRel && relativeEncoding ? ResolveCol(colWord & 0x3FFF, true, 14) : colWord & 0x3FFF;
        }
        else
        {
            colWord = U8();
            rowRel = (rowWord & 0x8000) != 0;
            colRel = (rowWord & 0x4000) != 0;
            int rr = rowWord & 0x3FFF;
            row = rowRel && relativeEncoding ? ResolveRow((rr ^ 0x2000) - 0x2000, true) : rr;
            col = colRel && relativeEncoding ? ResolveCol(colWord, true, 8) : colWord;
        }

        return (row, col, rowRel, colRel);
    }

    private void Ref(bool relative)
    {
        var l = ReadLoc(relative);
        _stack.Add(new CellReferenceNode(null, Row(l.Row, l.RowRel), Col(l.Col, l.ColRel)));
    }

    private void ReadAreaLocs(bool relativeEncoding, out (int Row, int Col, bool RowRel, bool ColRel) a, out (int Row, int Col, bool RowRel, bool ColRel) b)
    {
        if (Wide)
        {
            int r1 = U16();
            int r2 = U16();
            int c1 = U16();
            int c2 = U16();
            a = Decode8(r1, c1, relativeEncoding);
            b = Decode8(r2, c2, relativeEncoding);
        }
        else
        {
            int r1 = U16();
            int r2 = U16();
            int c1 = U8();
            int c2 = U8();
            a = Decode5(r1, c1, relativeEncoding);
            b = Decode5(r2, c2, relativeEncoding);
        }
    }

    private (int Row, int Col, bool RowRel, bool ColRel) Decode8(int rowWord, int colWord, bool relativeEncoding)
    {
        bool rowRel = (colWord & 0x8000) != 0;
        bool colRel = (colWord & 0x4000) != 0;
        int row = rowRel && relativeEncoding ? ResolveRow(rowWord, true) : rowWord;
        int col = colRel && relativeEncoding ? ResolveCol(colWord & 0x3FFF, true, 14) : colWord & 0x3FFF;
        return (row, col, rowRel, colRel);
    }

    private (int Row, int Col, bool RowRel, bool ColRel) Decode5(int rowWord, int colByte, bool relativeEncoding)
    {
        bool rowRel = (rowWord & 0x8000) != 0;
        bool colRel = (colByte >= 0) && (rowWord & 0x4000) != 0;
        int rr = rowWord & 0x3FFF;
        int row = rowRel && relativeEncoding ? ResolveRow((rr ^ 0x2000) - 0x2000, true) : rr;
        int col = colRel && relativeEncoding ? ResolveCol(colByte, true, 8) : colByte;
        return (row, col, rowRel, colRel);
    }

    private AreaReferenceNode MakeArea(SheetPrefix? prefix, (int Row, int Col, bool RowRel, bool ColRel) a, (int Row, int Col, bool RowRel, bool ColRel) b)
    {
        int maxCol = 0xFF;
        int maxRow = Wide ? 0xFFFF : 0x3FFF;
        // Whole columns and whole rows are stored as full-height or full-width areas.
        bool fullCols = a.Row == 0 && b.Row >= maxRow;
        bool fullRows = a.Col == 0 && b.Col >= maxCol;
        if (fullCols && !fullRows)
        {
            return new AreaReferenceNode(prefix, AreaKind.Columns, Row(0, false), Col(a.Col, a.ColRel), Row(1048575, false), Col(b.Col, b.ColRel));
        }

        if (fullRows && !fullCols)
        {
            return new AreaReferenceNode(prefix, AreaKind.Rows, Row(a.Row, a.RowRel), Col(0, false), Row(b.Row, b.RowRel), Col(16383, false));
        }

        return new AreaReferenceNode(prefix, AreaKind.Cells, Row(a.Row, a.RowRel), Col(a.Col, a.ColRel), Row(b.Row, b.RowRel), Col(b.Col, b.ColRel));
    }

    private void Area(bool relative)
    {
        ReadAreaLocs(relative, out var a, out var b);
        _stack.Add(MakeArea(null, a, b));
    }

    private void RefErr(bool area, bool three)
    {
        SheetPrefix? prefix = null;
        if (three)
        {
            prefix = ReadPrefix(out bool broken);
            if (broken)
            {
                prefix = null;
            }
        }

        int n = Wide ? (area ? 8 : 4) : (area ? 6 : 3);
        for (int i = 0; i < n; i++)
        {
            U8();
        }

        _stack.Add(new ErrorNode(CalcError.Ref, prefix));
    }

    // The 3-D prefix: BIFF8 has an index into EXTERNSHEET; BIFF5 has a signed index into its EXTERNSHEET records, 8 unused bytes and
    // two sheet numbers.
    private SheetPrefix? ReadPrefix(out bool broken)
    {
        if (Wide)
        {
            return PrefixFor(U16(), out broken);
        }

        short ixals = (short)U16();
        for (int i = 0; i < 8; i++)
        {
            U8();
        }

        int first = (short)U16();
        int last = (short)U16();
        broken = false;
        if (ixals < 0)
        {
            if (first < 0 || last < 0 || first >= _ctx.SheetNames.Count || last >= _ctx.SheetNames.Count)
            {
                broken = true;
                return null;
            }

            return first == last ? new SheetPrefix(_ctx.SheetNames[first]) : new SheetPrefix(_ctx.SheetNames[first], _ctx.SheetNames[last]);
        }

        int idx = ixals - 1;
        if (idx >= 0 && idx < _ctx.Biff5Externs.Count && _ctx.Biff5Externs[idx] is { } own)
        {
            return new SheetPrefix(own);
        }

        _ctx.Report?.Invoke("loss.external-links", "A reference into another workbook is not carried as a formula.");
        broken = true;
        return null;
    }

    private void Ref3d()
    {
        SheetPrefix? prefix = ReadPrefix(out bool broken);
        var l = ReadLoc(relativeEncoding: false);
        _stack.Add(broken ? new ErrorNode(CalcError.Ref) : new CellReferenceNode(prefix, Row(l.Row, l.RowRel), Col(l.Col, l.ColRel)));
    }

    private void Area3d()
    {
        SheetPrefix? prefix = ReadPrefix(out bool broken);
        ReadAreaLocs(false, out var a, out var b);
        _stack.Add(broken ? new ErrorNode(CalcError.Ref) : MakeArea(prefix, a, b));
    }

    // ---- names and functions ----

    private void Name()
    {
        int index = U16();
        if (Wide)
        {
            U16();
        }
        else if (_ctx.Version >= 5)
        {
            for (int i = 0; i < 12; i++)
            {
                U8();
            }
        }

        _stack.Add(NameNodeFor(index));
    }

    private FormulaNode NameNodeFor(int index1)
    {
        if (index1 < 1 || index1 > _ctx.Names.Count)
        {
            _ctx.Report?.Invoke("biff.formula-name", "A formula uses name " + index1.ToString(CultureInfo.InvariantCulture) + ", which the file does not define.");
            return new ErrorNode(CalcError.Name);
        }

        DefinedNameInfo n = _ctx.Names[index1 - 1];
        string text = n.IsBuiltIn && n.Name.Length == 0 ? BuiltInName(n.BuiltIn) : n.Name;
        if (n.SheetScope >= 0 && n.SheetScope < _ctx.SheetNames.Count)
        {
            return new NameNode(new SheetPrefix(_ctx.SheetNames[n.SheetScope]), text);
        }

        return new NameNode(null, text);
    }

    internal static string BuiltInName(int code) => code switch
    {
        0x00 => "_xlnm.Consolidate_Area",
        0x01 => "_xlnm.Auto_Open",
        0x02 => "_xlnm.Auto_Close",
        0x03 => "_xlnm.Extract",
        0x04 => "_xlnm.Database",
        0x05 => "_xlnm.Criteria",
        0x06 => "_xlnm.Print_Area",
        0x07 => "_xlnm.Print_Titles",
        0x08 => "_xlnm.Recorder",
        0x09 => "_xlnm.Data_Form",
        0x0A => "_xlnm.Auto_Activate",
        0x0B => "_xlnm.Auto_Deactivate",
        0x0C => "_xlnm.Sheet_Title",
        0x0D => "_xlnm._FilterDatabase",
        _ => "_xlnm.Unknown" + code.ToString(CultureInfo.InvariantCulture),
    };

    // PtgNameX: an add-in or external name. BIFF8 holds the EXTERNSHEET index and a 4-byte name index; BIFF5 is the 24-byte form.
    private void NameX()
    {
        string? name = null;
        if (Wide)
        {
            int ixti = U16();
            long idx = U32();
            if (ixti < _ctx.Xti.Count && _ctx.Xti[ixti].Book is var b and >= 0 && b < _ctx.Books.Count)
            {
                SupBook sb = _ctx.Books[b];
                if (idx >= 1 && idx <= sb.ExternNames.Count)
                {
                    name = sb.ExternNames[(int)idx - 1];
                }
            }
        }
        else
        {
            for (int i = 0; i < 24 - 0; i++)
            {
                U8();
            }
        }

        if (name is null)
        {
            _ctx.Report?.Invoke("biff.formula-name", "A formula uses an external name that the file does not define.");
            _stack.Add(new ErrorNode(CalcError.Name));
            return;
        }

        _stack.Add(new NameNode(null, name));
    }

    private void Function(bool fixedCount)
    {
        int tab;
        int count;
        if (fixedCount)
        {
            tab = _ctx.Version >= 3 ? U16() : U8();
            count = FunctionTable.FixedArgs(tab);
            if (count < 0)
            {
                throw new FormatException("Function " + tab.ToString(CultureInfo.InvariantCulture) + " is used without an argument count.");
            }
        }
        else
        {
            count = U8() & 0x7F;
            if (_ctx.Version >= 3)
            {
                tab = U16();
                if ((tab & 0x8000) != 0)
                {
                    _ctx.Report?.Invoke("biff.formula-macro-function", "A macro-sheet command function is replaced by #NAME?.");
                    PopArgs(count);
                    _stack.Add(new ErrorNode(CalcError.Name));
                    return;
                }
            }
            else
            {
                tab = U8();
                if ((tab & 0x80) != 0)
                {
                    tab &= 0x7F;
                }
            }
        }

        FormulaNode[] args = PopArgs(count);
        if (tab == 255)
        {
            // A user-defined or add-in function: the first argument is the name.
            if (args.Length == 0)
            {
                throw new FormatException("A user-defined function call has no name.");
            }

            string fname = args[0] switch
            {
                NameNode nn => nn.Prefix is null ? nn.Name : nn.Name,
                ErrorNode => "#NAME?",
                _ => throw new FormatException("A user-defined function call names its function by an unsupported token."),
            };
            // A function added after the first edition is stored as a name with the _xlfn. prefix; the workbook format only needs the prefix for some.
            string bare = StripFutureFunctionPrefix(fname);
            _stack.Add(new FunctionCallNode(FutureFunctions.PrefixFor(bare) + bare, args[1..]));
            return;
        }

        string? known = FunctionTable.Name(tab);
        if (known is null)
        {
            throw new FormatException("Unknown function number " + tab.ToString(CultureInfo.InvariantCulture) + ".");
        }

        _stack.Add(new FunctionCallNode(known, args));
    }

    private static string StripFutureFunctionPrefix(string name)
    {
        while (name.StartsWith("_xlfn.", StringComparison.OrdinalIgnoreCase) || name.StartsWith("_xlws.", StringComparison.OrdinalIgnoreCase))
        {
            name = name[6..];
        }

        return name;
    }

    private FormulaNode[] PopArgs(int count)
    {
        if (_stack.Count < count)
        {
            throw new FormatException("A function call has fewer operands than arguments.");
        }

        var args = new FormulaNode[count];
        for (int i = count - 1; i >= 0; i--)
        {
            args[i] = Pop();
        }

        return args;
    }

    // ---- extra data ----

    private void SkipArrayHeader()
    {
        // [MS-XLS] §2.5.198.32 PtgArray: 7 unused bytes; the values are in the extra data.
        for (int i = 0; i < 7; i++)
        {
            U8();
        }
    }

    private int BU8()
    {
        if (_b >= _rgcb.Length)
        {
            throw new FormatException("The extra data of an array constant is truncated.");
        }

        return _rgcb.Span[_b++];
    }

    private int BU16() => BU8() | (BU8() << 8);

    private double BF64()
    {
        long v = 0;
        for (int i = 0; i < 8; i++)
        {
            v |= (long)BU8() << (8 * i);
        }

        return BitConverter.Int64BitsToDouble(v);
    }

    // [MS-XLS] §2.5.198.59 PtgExtraArray and §2.5.267 SerAr: columns-1 (1 byte), rows-1 (2 bytes), then the values row by row.
    private ArrayConstantNode ReadArray()
    {
        int cols = BU8() + 1;
        int rows = BU16() + 1;
        long total = (long)rows * cols;
        if (total > 1 << 20)
        {
            throw new FormatException("An array constant is too large.");
        }

        var cells = new CalcValue[total];
        for (int i = 0; i < cells.Length; i++)
        {
            int type = BU8();
            switch (type)
            {
                case 0x00:
                    for (int k = 0; k < 8; k++)
                    {
                        BU8();
                    }

                    cells[i] = CalcValue.Blank;
                    break;
                case 0x01:
                    cells[i] = CalcValue.FromNumber(BF64());
                    break;
                case 0x02:
                    {
                        int cch = Wide ? BU16() : BU8();
                        bool wide = false;
                        if (Wide)
                        {
                            wide = (BU8() & 1) != 0;
                        }

                        var sb = new System.Text.StringBuilder(cch);
                        byte[]? raw = wide ? null : new byte[cch];
                        for (int k = 0; k < cch; k++)
                        {
                            if (wide)
                            {
                                sb.Append((char)BU16());
                            }
                            else
                            {
                                raw![k] = (byte)BU8();
                            }
                        }

                        if (!wide)
                        {
                            sb.Append(Wide ? System.Text.Encoding.Latin1.GetString(raw!) : _enc8.GetString(raw!));
                        }

                        cells[i] = CalcValue.FromText(sb.ToString());
                        break;
                    }

                case 0x04:
                    {
                        int v = BU8();
                        for (int k = 0; k < 7; k++)
                        {
                            BU8();
                        }

                        cells[i] = CalcValue.FromBoolean(v != 0);
                        break;
                    }

                case 0x10:
                    {
                        int v = BU8();
                        for (int k = 0; k < 7; k++)
                        {
                            BU8();
                        }

                        cells[i] = CalcValue.FromError(MapError(v));
                        break;
                    }

                default:
                    throw new FormatException("An array constant holds a value of unknown type " + type.ToString(CultureInfo.InvariantCulture) + ".");
            }
        }

        return new ArrayConstantNode(new CalcArray(rows, cols, cells));
    }

    // [MS-XLS] §2.5.198.61 PtgExtraMem: a count and that many Ref8U (8 bytes each); only a cache, so skipped.
    private void SkipExtraMem()
    {
        int count = BU16();
        for (int i = 0; i < count * 8; i++)
        {
            BU8();
        }
    }
}
