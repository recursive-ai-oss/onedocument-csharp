using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RecursiveAI.OneOffice.Calc;

/// <summary>Which text form a printed formula takes.</summary>
public enum FormulaForm : byte
{
    /// <summary>The form stored in files: en-US separators and function names, <c>_xlfn.</c> prefixes on newer functions, <c>_xlfn.SINGLE</c> and <c>_xlfn.ANCHORARRAY</c> for <c>@</c> and <c>#</c>.</summary>
    File,

    /// <summary>The form a user sees: no prefixes, <c>@</c> and <c>#</c> operators, local separators and function names when a locale is given.</summary>
    Display,
}

/// <summary>Options for printing a formula.</summary>
public sealed class FormulaPrintOptions
{
    /// <summary>File or display form. Default <see cref="FormulaForm.File"/>.</summary>
    public FormulaForm Form { get; init; } = FormulaForm.File;

    /// <summary>A1 (default) or R1C1 references.</summary>
    public FormulaSyntax Syntax { get; init; } = FormulaSyntax.A1;

    /// <summary>For R1C1: the row of the formula's cell. Default 1.</summary>
    public int OriginRow { get; init; } = 1;

    /// <summary>For R1C1: the column of the formula's cell. Default 1.</summary>
    public int OriginColumn { get; init; } = 1;

    /// <summary>Separators and function names for display form. File form always prints en-US. Default <see cref="FormulaLocale.Invariant"/>.</summary>
    public FormulaLocale Locale { get; init; } = FormulaLocale.Invariant;
}

/// <summary>Writes a <see cref="FormulaNode"/> tree as formula text. A tree read from text and not edited prints back to the same characters in file form.</summary>
public static class FormulaPrinter
{
    /// <summary>Prints a formula.</summary>
    /// <param name="node">The tree.</param>
    /// <param name="options">Options, or null for A1 file form.</param>
    /// <returns>The text, without a leading <c>=</c>.</returns>
    public static string Print(FormulaNode node, FormulaPrintOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(node);
        var sb = new StringBuilder();
        new Writer(sb, options ?? new FormulaPrintOptions()).Write(node);
        return sb.ToString();
    }

    private readonly struct Writer(StringBuilder sb, FormulaPrintOptions o)
    {
        private bool File => o.Form == FormulaForm.File;

        private FormulaLocale Loc => File ? FormulaLocale.Invariant : o.Locale;

        public void Write(FormulaNode n)
        {
            if (n.LeadingSpace is not null)
            {
                sb.Append(n.LeadingSpace);
            }

            WriteBody(n);
            if (n.TrailingSpace is not null)
            {
                sb.Append(n.TrailingSpace);
            }
        }

        private void WriteBody(FormulaNode n)
        {
            switch (n)
            {
                case NumberNode x:
                    sb.Append(Number(x));
                    break;
                case TextNode x:
                    sb.Append('"').Append(x.Value.Replace("\"", "\"\"", StringComparison.Ordinal)).Append('"');
                    break;
                case BooleanNode x:
                    sb.Append(x.Raw ?? (x.Value ? "TRUE" : "FALSE"));
                    break;
                case ErrorNode x:
                    if (x.Prefix is not null)
                    {
                        sb.Append(x.Prefix.ToString());
                    }

                    sb.Append(x.Raw ?? CalcErrors.ToText(x.Error));
                    break;
                case MissingNode:
                    break;
                case CellReferenceNode x:
                    WritePrefix(x.Prefix);
                    if (x.Raw is not null && o.Syntax == FormulaSyntax.A1)
                    {
                        sb.Append(x.Raw);
                    }
                    else
                    {
                        Cell(x.Row, x.Column);
                    }

                    break;
                case AreaReferenceNode x:
                    WritePrefix(x.Prefix);
                    Area(x);
                    break;
                case NameNode x:
                    WritePrefix(x.Prefix);
                    sb.Append(x.Name);
                    break;
                case StructuredReferenceNode x:
                    WritePrefix(x.Prefix);
                    sb.Append(x.Table).Append(x.Specifier);
                    break;
                case FunctionCallNode x:
                    Call(x);
                    break;
                case InvokeNode x:
                    Write(x.Callee);
                    sb.Append('(');
                    Args(x.Arguments, x.InnerSpace);
                    sb.Append(')');
                    break;
                case UnaryNode x:
                    if (x.Operator == '@' && File)
                    {
                        sb.Append("_xlfn.SINGLE(");
                        Write(x.Operand);
                        sb.Append(')');
                    }
                    else
                    {
                        sb.Append(x.Operator);
                        Write(x.Operand);
                    }

                    break;
                case PercentNode x:
                    Write(x.Operand);
                    sb.Append('%');
                    break;
                case SpillNode x:
                    if (File)
                    {
                        sb.Append("_xlfn.ANCHORARRAY(");
                        Write(x.Operand);
                        sb.Append(')');
                    }
                    else
                    {
                        Write(x.Operand);
                        sb.Append('#');
                    }

                    break;
                case BinaryNode x:
                    Write(x.Left);
                    sb.Append(x.Operator == BinaryOperator.Union ? Loc.ListSeparator.ToString() : x.OperatorText ?? BinaryNode.Text(x.Operator));
                    Write(x.Right);
                    break;
                case ParenNode x:
                    sb.Append('(');
                    Write(x.Inner);
                    sb.Append(')');
                    break;
                case ArrayConstantNode x:
                    ArrayConstant(x);
                    break;
                case MemberPathNode x:
                    sb.Append(string.Join('.', x.Segments));
                    break;
                default:
                    throw new ArgumentException("Unknown node " + n.GetType().Name, nameof(n));
            }
        }

        private string Number(NumberNode x)
        {
            string t = x.Raw ?? x.Value.ToString("R", CultureInfo.InvariantCulture);
            return Loc.DecimalSeparator == '.' ? t : t.Replace('.', Loc.DecimalSeparator);
        }

        private void WritePrefix(SheetPrefix? p)
        {
            if (p is not null)
            {
                sb.Append(p.ToString());
            }
        }

        private void Cell(RefCoord row, RefCoord col)
        {
            if (o.Syntax == FormulaSyntax.A1)
            {
                if (col.Absolute)
                {
                    sb.Append('$');
                }

                sb.Append(RefSyntax.ColumnName(col.Index));
                if (row.Absolute)
                {
                    sb.Append('$');
                }

                sb.Append(row.Index.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                Rc('R', row, o.OriginRow);
                Rc('C', col, o.OriginColumn);
            }
        }

        private void Rc(char letter, RefCoord c, int origin)
        {
            sb.Append(letter);
            if (c.Absolute)
            {
                sb.Append(c.Index.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                int off = c.Index - origin;
                if (off != 0)
                {
                    sb.Append('[').Append(off.ToString(CultureInfo.InvariantCulture)).Append(']');
                }
            }
        }

        private void Area(AreaReferenceNode x)
        {
            if (x.Raw is not null && o.Syntax == FormulaSyntax.A1)
            {
                sb.Append(x.Raw);
                return;
            }

            switch (x.AreaKind)
            {
                case AreaKind.Cells:
                    Cell(x.Row1, x.Column1);
                    sb.Append(':');
                    Cell(x.Row2, x.Column2);
                    break;
                case AreaKind.Rows:
                    if (o.Syntax == FormulaSyntax.A1)
                    {
                        sb.Append(x.Row1.Absolute ? "$" : string.Empty).Append(x.Row1.Index.ToString(CultureInfo.InvariantCulture)).Append(':');
                        sb.Append(x.Row2.Absolute ? "$" : string.Empty).Append(x.Row2.Index.ToString(CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        Rc('R', x.Row1, o.OriginRow);
                        sb.Append(':');
                        Rc('R', x.Row2, o.OriginRow);
                    }

                    break;
                default:
                    if (o.Syntax == FormulaSyntax.A1)
                    {
                        sb.Append(x.Column1.Absolute ? "$" : string.Empty).Append(RefSyntax.ColumnName(x.Column1.Index)).Append(':');
                        sb.Append(x.Column2.Absolute ? "$" : string.Empty).Append(RefSyntax.ColumnName(x.Column2.Index));
                    }
                    else
                    {
                        Rc('C', x.Column1, o.OriginColumn);
                        sb.Append(':');
                        Rc('C', x.Column2, o.OriginColumn);
                    }

                    break;
            }
        }

        private void Call(FunctionCallNode x)
        {
            if (!File && x.Arguments.Count == 1)
            {
                if (x.Name.Equals("ANCHORARRAY", StringComparison.OrdinalIgnoreCase))
                {
                    Write(x.Arguments[0]);
                    sb.Append('#');
                    return;
                }

                if (x.Name.Equals("SINGLE", StringComparison.OrdinalIgnoreCase))
                {
                    sb.Append('@');
                    Write(x.Arguments[0]);
                    return;
                }
            }

            if (File)
            {
                sb.Append(x.NameIsFileForm ? x.RawName : FutureFunctions.PrefixFor(x.Name) + x.Name);
            }
            else
            {
                sb.Append(Loc.ToLocalName(x.Name));
            }

            sb.Append('(');
            Args(x.Arguments, x.InnerSpace);
            sb.Append(')');
        }

        private void Args(IReadOnlyList<FormulaNode> args, string? innerSpace)
        {
            if (args.Count == 0)
            {
                sb.Append(innerSpace);
                return;
            }

            for (int i = 0; i < args.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(Loc.ListSeparator);
                }

                Write(args[i]);
            }
        }

        private void ArrayConstant(ArrayConstantNode x)
        {
            if (x.Raw is not null && Loc == FormulaLocale.Invariant)
            {
                sb.Append(x.Raw);
                return;
            }

            CalcArray a = x.Value;
            sb.Append('{');
            for (int r = 0; r < a.Rows; r++)
            {
                if (r > 0)
                {
                    sb.Append(Loc.ArrayRowSeparator);
                }

                for (int c = 0; c < a.Columns; c++)
                {
                    if (c > 0)
                    {
                        sb.Append(Loc.ArrayColumnSeparator);
                    }

                    CalcValue v = a[r, c];
                    switch (v.Kind)
                    {
                        case ValueKind.Number:
                            string t = v.Number.ToString("R", CultureInfo.InvariantCulture);
                            sb.Append(Loc.DecimalSeparator == '.' ? t : t.Replace('.', Loc.DecimalSeparator));
                            break;
                        case ValueKind.Text:
                            sb.Append('"').Append(v.Text.Replace("\"", "\"\"", StringComparison.Ordinal)).Append('"');
                            break;
                        case ValueKind.Boolean:
                            sb.Append(v.Boolean ? "TRUE" : "FALSE");
                            break;
                        case ValueKind.Error:
                            sb.Append(CalcErrors.ToText(v.Error));
                            break;
                        default:
                            break;
                    }
                }
            }

            sb.Append('}');
        }
    }
}

/// <summary>Moves a formula to another cell, as a shared formula or a copy does.</summary>
public static class FormulaTranslator
{
    /// <summary>Returns the formula as it reads in a cell <paramref name="rowDelta"/> rows and <paramref name="columnDelta"/> columns away: relative coordinates move, absolute ones stay.</summary>
    /// <param name="node">The formula at the anchor cell.</param>
    /// <param name="rowDelta">Rows to move (negative moves up).</param>
    /// <param name="columnDelta">Columns to move.</param>
    /// <returns>A new tree. A reference that would leave the sheet becomes <c>#REF!</c>, as in Excel. Unchanged subtrees are shared with the input.</returns>
    public static FormulaNode Translate(FormulaNode node, int rowDelta, int columnDelta)
    {
        ArgumentNullException.ThrowIfNull(node);
        return rowDelta == 0 && columnDelta == 0 ? node : Shift(node, rowDelta, columnDelta);
    }

    private static bool Move(RefCoord c, int delta, int max, out RefCoord moved)
    {
        if (c.Absolute || delta == 0)
        {
            moved = c;
            return true;
        }

        int v = c.Index + delta;
        moved = new RefCoord(v, false);
        return v >= 1 && v <= max;
    }

    private static T Trivia<T>(T copy, FormulaNode from)
        where T : FormulaNode
    {
        copy.LeadingSpace = from.LeadingSpace;
        copy.TrailingSpace = from.TrailingSpace;
        copy.Offset = from.Offset;
        copy.Length = from.Length;
        return copy;
    }

    private static FormulaNode Shift(FormulaNode n, int dr, int dc)
    {
        switch (n)
        {
            case CellReferenceNode x:
                if (!Move(x.Row, dr, CellArea.MaxRow, out RefCoord r) || !Move(x.Column, dc, CellArea.MaxColumn, out RefCoord c))
                {
                    return Trivia(new ErrorNode(CalcError.Ref, x.Prefix), x);
                }

                return Trivia(new CellReferenceNode(x.Prefix, r, c), x);
            case AreaReferenceNode x:
                {
                    RefCoord r1 = x.Row1;
                    RefCoord r2 = x.Row2;
                    RefCoord c1 = x.Column1;
                    RefCoord c2 = x.Column2;
                    bool ok = true;
                    if (x.AreaKind != AreaKind.Columns)
                    {
                        ok &= Move(x.Row1, dr, CellArea.MaxRow, out r1);
                        ok &= Move(x.Row2, dr, CellArea.MaxRow, out r2);
                    }

                    if (x.AreaKind != AreaKind.Rows)
                    {
                        ok &= Move(x.Column1, dc, CellArea.MaxColumn, out c1);
                        ok &= Move(x.Column2, dc, CellArea.MaxColumn, out c2);
                    }

                    return ok ? Trivia(new AreaReferenceNode(x.Prefix, x.AreaKind, r1, c1, r2, c2), x) : Trivia(new ErrorNode(CalcError.Ref, x.Prefix), x);
                }

            case FunctionCallNode x:
                return Trivia(new FunctionCallNode(x.RawName, MapAll(x.Arguments, dr, dc)) { InnerSpace = x.InnerSpace, NameIsFileForm = x.NameIsFileForm }, x);
            case InvokeNode x:
                return Trivia(new InvokeNode(Shift(x.Callee, dr, dc), MapAll(x.Arguments, dr, dc)) { InnerSpace = x.InnerSpace }, x);
            case UnaryNode x:
                return Trivia(new UnaryNode(x.Operator, Shift(x.Operand, dr, dc)), x);
            case PercentNode x:
                return Trivia(new PercentNode(Shift(x.Operand, dr, dc)), x);
            case SpillNode x:
                return Trivia(new SpillNode(Shift(x.Operand, dr, dc)), x);
            case BinaryNode x:
                return Trivia(new BinaryNode(x.Operator, Shift(x.Left, dr, dc), Shift(x.Right, dr, dc), x.OperatorText), x);
            case ParenNode x:
                return Trivia(new ParenNode(Shift(x.Inner, dr, dc)), x);
            default:
                return n;
        }
    }

    private static List<FormulaNode> MapAll(IReadOnlyList<FormulaNode> args, int dr, int dc)
    {
        var l = new List<FormulaNode>(args.Count);
        foreach (FormulaNode a in args)
        {
            l.Add(Shift(a, dr, dc));
        }

        return l;
    }
}
