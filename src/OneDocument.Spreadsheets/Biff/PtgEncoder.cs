using System.Globalization;
using RecursiveAI.OneDocument.Calc;

namespace RecursiveAI.OneDocument.Spreadsheets.Biff;

/// <summary>
/// Encodes a formula tree as BIFF8 tokens ([MS-XLS] §2.5.198), the inverse of <see cref="PtgDecoder"/>. References are written in the
/// reference class and no evaluation hints (<c>PtgAttrIf</c>, <c>PtgAttrGoto</c>, <c>PtgAttrSpace</c>) are written; the tree does not carry them.
/// Anything the encoder cannot express (structured references, lambdas, spill) makes <see cref="TryEncode"/> return false.
/// </summary>
internal sealed class PtgEncoder
{
    private readonly FormulaContext _ctx;
    private readonly List<byte> _rgce = [];
    private readonly List<byte> _rgcb = [];
    private string? _why;

    public PtgEncoder(FormulaContext ctx)
    {
        _ctx = ctx;
    }

    /// <summary>The reason the last <see cref="TryEncode"/> failed.</summary>
    public string? Reason => _why;

    /// <summary>Encodes a formula.</summary>
    /// <param name="node">The tree.</param>
    /// <param name="rgce">The tokens.</param>
    /// <param name="rgcb">The extra data.</param>
    /// <returns>True on success.</returns>
    public bool TryEncode(FormulaNode node, out byte[] rgce, out byte[] rgcb)
    {
        _rgce.Clear();
        _rgcb.Clear();
        _why = null;
        try
        {
            Emit(node);
        }
        catch (NotSupportedException ex)
        {
            _why = ex.Message;
            rgce = [];
            rgcb = [];
            return false;
        }

        rgce = [.. _rgce];
        rgcb = [.. _rgcb];
        return true;
    }

    private void B(int b) => _rgce.Add((byte)b);

    private void W(int v)
    {
        B(v & 0xFF);
        B((v >> 8) & 0xFF);
    }

    private void D(uint v)
    {
        W((int)(v & 0xFFFF));
        W((int)(v >> 16));
    }

    private void Emit(FormulaNode n)
    {
        switch (n)
        {
            case NumberNode x:
                if (x.Value >= 0 && x.Value <= 65535 && x.Value == Math.Floor(x.Value) && !(x.Value == 0 && double.IsNegative(x.Value)))
                {
                    B(0x1E);
                    W((int)x.Value);
                }
                else
                {
                    B(0x1F);
                    long bits = BitConverter.DoubleToInt64Bits(x.Value);
                    for (int i = 0; i < 8; i++)
                    {
                        B((int)((bits >> (8 * i)) & 0xFF));
                    }
                }

                break;
            case TextNode x:
                if (x.Value.Length > 255)
                {
                    throw new NotSupportedException("A text constant longer than 255 characters cannot be a PtgStr.");
                }

                B(0x17);
                WriteShortString(x.Value);
                break;
            case BooleanNode x:
                B(0x1D);
                B(x.Value ? 1 : 0);
                break;
            case ErrorNode x:
                EmitError(x);
                break;
            case MissingNode:
                B(0x16);
                break;
            case CellReferenceNode x:
                if (x.Prefix is null)
                {
                    B(0x24);
                }
                else
                {
                    B(0x3A);
                    W(Ixti(x.Prefix));
                }

                W(x.Row.Index - 1);
                W(ColWord(x.Column, x.Row));
                break;
            case AreaReferenceNode x:
                EmitArea(x);
                break;
            case NameNode x:
                EmitName(x);
                break;
            case FunctionCallNode x:
                EmitCall(x);
                break;
            case UnaryNode x when x.Operator is '-' or '+':
                Emit(x.Operand);
                B(x.Operator == '-' ? 0x13 : 0x12);
                break;
            case PercentNode x:
                Emit(x.Operand);
                B(0x14);
                break;
            case ParenNode x:
                Emit(x.Inner);
                B(0x15);
                break;
            case BinaryNode x:
                Emit(x.Left);
                Emit(x.Right);
                B(BinaryToken(x.Operator));
                break;
            case ArrayConstantNode x:
                EmitArray(x);
                break;
            default:
                throw new NotSupportedException("A " + n.Kind + " node has no BIFF8 token.");
        }
    }

    private static int BinaryToken(BinaryOperator op) => op switch
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
        BinaryOperator.Range => 0x11,
        _ => throw new NotSupportedException("Unknown operator."),
    };

    private void WriteShortString(string s)
    {
        bool wide = false;
        foreach (char c in s)
        {
            if (c > 0xFF)
            {
                wide = true;
                break;
            }
        }

        B(s.Length);
        B(wide ? 1 : 0);
        foreach (char c in s)
        {
            B(c & 0xFF);
            if (wide)
            {
                B(c >> 8);
            }
        }
    }

    private static int ColWord(RefCoord col, RefCoord row) =>
        ((col.Index - 1) & 0x3FFF) | (col.Absolute ? 0 : 0x4000) | (row.Absolute ? 0 : 0x8000);

    private void EmitError(ErrorNode x)
    {
        if (x.Error == CalcError.Ref && x.Prefix is not null)
        {
            B(0x3C);
            W(Ixti(x.Prefix));
            D(0);
            return;
        }

        B(0x1C);
        B(x.Error switch
        {
            CalcError.Null => 0x00,
            CalcError.Div0 => 0x07,
            CalcError.Value => 0x0F,
            CalcError.Ref => 0x17,
            CalcError.Name => 0x1D,
            CalcError.Num => 0x24,
            CalcError.NA => 0x2A,
            CalcError.GettingData => 0x2B,
            _ => throw new NotSupportedException("The error " + x.Error + " has no BIFF8 code."),
        });
    }

    private void EmitArea(AreaReferenceNode x)
    {
        if (x.Prefix is null)
        {
            B(0x25);
        }
        else
        {
            B(0x3B);
            W(Ixti(x.Prefix));
        }

        int r1 = x.Row1.Index - 1;
        int r2 = x.Row2.Index - 1;
        int c1 = x.Column1.Index - 1;
        int c2 = x.Column2.Index - 1;
        switch (x.AreaKind)
        {
            case AreaKind.Columns:
                r1 = 0;
                r2 = 0xFFFF;
                break;
            case AreaKind.Rows:
                c1 = 0;
                c2 = 0xFF;
                break;
            default:
                break;
        }

        if (r1 > 0xFFFF || r2 > 0xFFFF || c1 > 0xFF || c2 > 0xFF)
        {
            throw new NotSupportedException("A reference lies outside the 65,536 x 256 grid of BIFF8.");
        }

        W(r1);
        W(r2);
        W((c1 & 0x3FFF) | (x.Column1.Absolute ? 0 : 0x4000) | (x.Row1.Absolute ? 0 : 0x8000));
        W((c2 & 0x3FFF) | (x.Column2.Absolute ? 0 : 0x4000) | (x.Row2.Absolute ? 0 : 0x8000));
    }

    private void EmitName(NameNode x)
    {
        int index = NameIndex(x.Name, x.Prefix?.Sheet);
        if (index < 0)
        {
            throw new NotSupportedException("The name " + x.Name + " is not defined in the workbook's name table.");
        }

        B(0x23);
        W(index);
        W(0);
    }

    private int NameIndex(string name, string? sheet)
    {
        int scope = sheet is null ? -1 : _ctx.SheetNames.FindIndex(s => string.Equals(s, sheet, StringComparison.OrdinalIgnoreCase));
        for (int i = 0; i < _ctx.Names.Count; i++)
        {
            DefinedNameInfo n = _ctx.Names[i];
            if (string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase) && n.SheetScope == scope)
            {
                return i + 1;
            }
        }

        return -1;
    }

    private int Ixti(SheetPrefix p)
    {
        if (p.Workbook is not null || p.Sheet is null)
        {
            throw new NotSupportedException("External references have no internal EXTERNSHEET entry.");
        }

        int first = _ctx.SheetNames.FindIndex(s => string.Equals(s, p.Sheet, StringComparison.OrdinalIgnoreCase));
        int last = p.LastSheet is null ? first : _ctx.SheetNames.FindIndex(s => string.Equals(s, p.LastSheet, StringComparison.OrdinalIgnoreCase));
        if (first < 0 || last < 0)
        {
            throw new NotSupportedException("The sheet " + p.Sheet + " is not in the workbook.");
        }

        int book = _ctx.Books.FindIndex(b => b.IsInternal);
        if (book < 0)
        {
            _ctx.Books.Add(new SupBook { IsInternal = true });
            book = _ctx.Books.Count - 1;
        }

        int at = _ctx.Xti.FindIndex(t => t.Book == book && t.First == first && t.Last == last);
        if (at < 0)
        {
            _ctx.Xti.Add((book, first, last));
            at = _ctx.Xti.Count - 1;
        }

        return at;
    }

    private void EmitCall(FunctionCallNode x)
    {
        int tab = FunctionTable.IndexOf(x.Name);
        int fixedArgs = tab >= 0 ? FunctionTable.FixedArgs(tab) : -1;
        if (tab >= 0 && x.RawName.Length == x.Name.Length)
        {
            foreach (FormulaNode a in x.Arguments)
            {
                Emit(a);
            }

            if (fixedArgs == x.Arguments.Count)
            {
                B(0x21);
                W(tab);
            }
            else
            {
                if (x.Arguments.Count > 127)
                {
                    throw new NotSupportedException("A call has more than 127 arguments.");
                }

                B(0x22);
                B(x.Arguments.Count);
                W(tab);
            }

            return;
        }

        // A user-defined or newer function: its name is the first argument of function 255.
        // The workbook file stores such a function as a hidden name with the _xlfn. prefix and calls it through function 255.
        string stored = x.RawName.StartsWith("_xlfn.", StringComparison.OrdinalIgnoreCase) ? x.RawName : "_xlfn." + x.Name;
        int idx = NameIndex(stored, null);
        if (idx < 0)
        {
            _ctx.Names.Add(new DefinedNameInfo { Name = stored, IsFunction = true, Hidden = true });
            idx = _ctx.Names.Count;
        }

        B(0x23);
        W(idx);
        W(0);
        foreach (FormulaNode a in x.Arguments)
        {
            Emit(a);
        }

        B(0x22);
        B(x.Arguments.Count + 1);
        W(255);
    }

    private void EmitArray(ArrayConstantNode x)
    {
        CalcArray a = x.Value;
        if (a.Columns > 256 || a.Rows > 65536)
        {
            throw new NotSupportedException("An array constant is larger than BIFF8 allows.");
        }

        B(0x20);
        for (int i = 0; i < 7; i++)
        {
            B(0);
        }

        _rgcb.Add((byte)(a.Columns - 1));
        _rgcb.Add((byte)((a.Rows - 1) & 0xFF));
        _rgcb.Add((byte)((a.Rows - 1) >> 8));
        for (int r = 0; r < a.Rows; r++)
        {
            for (int c = 0; c < a.Columns; c++)
            {
                CalcValue v = a[r, c];
                switch (v.Kind)
                {
                    case ValueKind.Number:
                        _rgcb.Add(1);
                        AddBits(BitConverter.DoubleToInt64Bits(v.Number));
                        break;
                    case ValueKind.Text:
                        {
                            string s = v.Text;
                            bool wide = false;
                            foreach (char wc in s)
                            {
                                wide |= wc > 0xFF;
                            }

                            _rgcb.Add(2);
                            _rgcb.Add((byte)(s.Length & 0xFF));
                            _rgcb.Add((byte)(s.Length >> 8));
                            _rgcb.Add((byte)(wide ? 1 : 0));
                            foreach (char ch in s)
                            {
                                _rgcb.Add((byte)(ch & 0xFF));
                                if (wide)
                                {
                                    _rgcb.Add((byte)(ch >> 8));
                                }
                            }

                            break;
                        }

                    case ValueKind.Boolean:
                        _rgcb.Add(4);
                        _rgcb.Add((byte)(v.Boolean ? 1 : 0));
                        _rgcb.AddRange(new byte[7]);
                        break;
                    case ValueKind.Error:
                        _rgcb.Add(0x10);
                        _rgcb.Add(v.Error switch
                        {
                            CalcError.Null => 0x00,
                            CalcError.Div0 => 0x07,
                            CalcError.Value => 0x0F,
                            CalcError.Ref => 0x17,
                            CalcError.Name => 0x1D,
                            CalcError.Num => 0x24,
                            CalcError.NA => 0x2A,
                            _ => 0x0F,
                        });
                        _rgcb.AddRange(new byte[7]);
                        break;
                    default:
                        _rgcb.Add(0);
                        _rgcb.AddRange(new byte[8]);
                        break;
                }
            }
        }
    }

    private void AddBits(long bits)
    {
        for (int i = 0; i < 8; i++)
        {
            _rgcb.Add((byte)((bits >> (8 * i)) & 0xFF));
        }
    }

    /// <summary>Describes a failure for reports.</summary>
    public override string ToString() => _why ?? "ok";
}
