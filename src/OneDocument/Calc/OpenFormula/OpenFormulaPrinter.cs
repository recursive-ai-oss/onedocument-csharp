using System.Globalization;
using System.Text;

namespace RecursiveAI.OneDocument.Calc.OpenFormula;

/// <summary>One construct of a formula that OpenFormula cannot say, found while printing.</summary>
/// <param name="Code">A stable code: <c>openformula.spill</c>, <c>openformula.invoke</c>, <c>openformula.structured-reference</c>, <c>openformula.member-path</c>,
/// <c>openformula.external-reference</c>, <c>openformula.function</c> (a function LibreOffice does not know), <c>openformula.error</c> (an error value past the seven OpenFormula has).</param>
/// <param name="Detail">The function or error name, or empty.</param>
/// <param name="Portable">True when the printed text is still a faithful formula (a function other readers may not know); false when the construct was changed or dropped, so the formula should not be written.</param>
public readonly record struct OpenFormulaProblem(string Code, string Detail, bool Portable);

/// <summary>
/// Prints the public formula AST as OpenFormula (OpenDocument 1.3 Part 4): bracketed references with a <c>.</c> before the cell
/// (<c>[.A1]</c>, <c>[$'My Sheet'.A1:.B2]</c>), <c>;</c> between arguments, <c>{1;2|3;4}</c> arrays, <c>TRUE()</c> for booleans, and function names as
/// <see cref="OpenFormulaFunctions"/> spells them. The text has no leading <c>=</c>; a file stores it as <c>of:=</c> followed by this text.
/// </summary>
public static class OpenFormulaPrinter
{
    /// <summary>Prints a formula.</summary>
    /// <param name="node">The tree.</param>
    /// <param name="problems">Receives each construct OpenFormula cannot carry; null to ignore them. A problem with <see cref="OpenFormulaProblem.Portable"/> false means the text is not a faithful formula.</param>
    /// <returns>The expression text. A construct without an equivalent is printed as <c>#REF!</c> (a reference-like one) or as the closest form, and listed in <paramref name="problems"/>.</returns>
    public static string Print(FormulaNode node, ICollection<OpenFormulaProblem>? problems = null)
    {
        ArgumentNullException.ThrowIfNull(node);
        var sb = new StringBuilder();
        Write(sb, node, problems);
        return sb.ToString();
    }

    private static void Write(StringBuilder sb, FormulaNode n, ICollection<OpenFormulaProblem>? problems)
    {
        switch (n)
        {
            case NumberNode num:
                sb.Append(num.Value.ToString("R", CultureInfo.InvariantCulture));
                break;
            case TextNode t:
                sb.Append('"').Append(t.Value.Replace("\"", "\"\"", StringComparison.Ordinal)).Append('"');
                break;
            case BooleanNode b:
                sb.Append(b.Value ? "TRUE()" : "FALSE()");
                break;
            case ErrorNode e:
                WriteError(sb, e, problems);
                break;
            case MissingNode:
                break;
            case CellReferenceNode c:
                WriteCell(sb, c, problems);
                break;
            case AreaReferenceNode a:
                WriteArea(sb, a, problems);
                break;
            case NameNode name:
                if (name.Prefix?.Workbook is not null)
                {
                    problems?.Add(new OpenFormulaProblem("openformula.external-reference", name.Name, false));
                    sb.Append("#REF!");
                }
                else
                {
                    sb.Append(name.BareName);
                }

                break;
            case StructuredReferenceNode s:
                problems?.Add(new OpenFormulaProblem("openformula.structured-reference", (s.Table ?? string.Empty) + s.Specifier, false));
                sb.Append("#REF!");
                break;
            case FunctionCallNode f:
                WriteCall(sb, f, problems);
                break;
            case InvokeNode:
                problems?.Add(new OpenFormulaProblem("openformula.invoke", string.Empty, false));
                sb.Append("#NAME?");
                break;
            case UnaryNode u:
                if (u.Operator == '@')
                {
                    // A range in a scalar position is intersected implicitly in OpenFormula, so the operator has nothing to add.
                    Write(sb, u.Operand, problems);
                }
                else
                {
                    sb.Append(u.Operator);
                    Write(sb, u.Operand, problems);
                }

                break;
            case PercentNode p:
                Write(sb, p.Operand, problems);
                sb.Append('%');
                break;
            case SpillNode sp:
                problems?.Add(new OpenFormulaProblem("openformula.spill", string.Empty, false));
                Write(sb, sp.Operand, problems);
                break;
            case BinaryNode bin:
                Write(sb, bin.Left, problems);
                sb.Append(bin.Operator switch
                {
                    BinaryOperator.Intersect => "!",
                    BinaryOperator.Union => "~",
                    _ => BinaryNode.Text(bin.Operator),
                });
                Write(sb, bin.Right, problems);
                break;
            case ParenNode paren:
                sb.Append('(');
                Write(sb, paren.Inner, problems);
                sb.Append(')');
                break;
            case ArrayConstantNode arr:
                WriteArray(sb, arr.Value);
                break;
            case MemberPathNode m:
                problems?.Add(new OpenFormulaProblem("openformula.member-path", string.Join('.', m.Segments), false));
                sb.Append("#NAME?");
                break;
            default:
                problems?.Add(new OpenFormulaProblem("openformula.unknown-node", n.Kind.ToString(), false));
                sb.Append("#NAME?");
                break;
        }
    }

    private static void WriteError(StringBuilder sb, ErrorNode e, ICollection<OpenFormulaProblem>? problems)
    {
        string text = CalcErrors.ToText(e.Error);
        if (e.Error is CalcError.Spill or CalcError.Calc or CalcError.Blocked or CalcError.Connect or CalcError.Busy or CalcError.Field or CalcError.Unknown or CalcError.GettingData)
        {
            problems?.Add(new OpenFormulaProblem("openformula.error", text, true));
        }

        if (e.Prefix?.Sheet is { } sheet && e.Error == CalcError.Ref)
        {
            sb.Append("[$").Append(SheetName(sheet)).Append(".#REF!]");
        }
        else
        {
            sb.Append(text);
        }
    }

    internal static string SheetName(string sheet)
    {
        bool plain = sheet.Length > 0 && !char.IsAsciiDigit(sheet[0]);
        foreach (char ch in sheet)
        {
            if (!(char.IsLetterOrDigit(ch) || ch == '_' || ch > 127))
            {
                plain = false;
                break;
            }
        }

        return plain ? sheet : "'" + sheet.Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    private static bool ExternalPrefix(SheetPrefix? prefix, ICollection<OpenFormulaProblem>? problems)
    {
        if (prefix?.Workbook is null)
        {
            return false;
        }

        problems?.Add(new OpenFormulaProblem("openformula.external-reference", prefix.Workbook, false));
        return true;
    }

    private static string Col(RefCoord c) => (c.Absolute ? "$" : string.Empty) + RefSyntax.ColumnName(c.Index);

    private static string Row(RefCoord r) => (r.Absolute ? "$" : string.Empty) + r.Index.ToString(CultureInfo.InvariantCulture);

    private static void WriteCell(StringBuilder sb, CellReferenceNode c, ICollection<OpenFormulaProblem>? problems)
    {
        if (ExternalPrefix(c.Prefix, problems))
        {
            sb.Append("#REF!");
            return;
        }

        sb.Append('[');
        if (c.Prefix?.Sheet is { } sheet)
        {
            sb.Append('$').Append(SheetName(sheet));
        }

        sb.Append('.').Append(Col(c.Column)).Append(Row(c.Row));
        if (c.Prefix?.LastSheet is { } last)
        {
            sb.Append(":$").Append(SheetName(last)).Append('.').Append(Col(c.Column)).Append(Row(c.Row));
        }

        sb.Append(']');
    }

    private static void WriteArea(StringBuilder sb, AreaReferenceNode a, ICollection<OpenFormulaProblem>? problems)
    {
        if (ExternalPrefix(a.Prefix, problems))
        {
            sb.Append("#REF!");
            return;
        }

        sb.Append('[');
        if (a.Prefix?.Sheet is { } sheet)
        {
            sb.Append('$').Append(SheetName(sheet));
        }

        sb.Append('.');
        switch (a.AreaKind)
        {
            case AreaKind.Columns:
                sb.Append(Col(a.Column1));
                break;
            case AreaKind.Rows:
                sb.Append(Row(a.Row1));
                break;
            default:
                sb.Append(Col(a.Column1)).Append(Row(a.Row1));
                break;
        }

        sb.Append(':');
        if (a.Prefix?.LastSheet is { } last)
        {
            sb.Append(SheetName(last));
        }

        sb.Append('.');
        switch (a.AreaKind)
        {
            case AreaKind.Columns:
                sb.Append(Col(a.Column2));
                break;
            case AreaKind.Rows:
                sb.Append(Row(a.Row2));
                break;
            default:
                sb.Append(Col(a.Column2)).Append(Row(a.Row2));
                break;
        }

        sb.Append(']');
    }

    private static void WriteCall(StringBuilder sb, FunctionCallNode f, ICollection<OpenFormulaProblem>? problems)
    {
        string name = OpenFormulaFunctions.ToFileName(f.Name, out bool portable);
        if (!portable)
        {
            problems?.Add(new OpenFormulaProblem("openformula.function", f.Name.ToUpperInvariant(), true));
        }

        sb.Append(name).Append('(');
        for (int i = 0; i < f.Arguments.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(';');
            }

            Write(sb, f.Arguments[i], problems);
        }

        sb.Append(')');
    }

    private static void WriteArray(StringBuilder sb, CalcArray array)
    {
        sb.Append('{');
        for (int r = 0; r < array.Rows; r++)
        {
            if (r > 0)
            {
                sb.Append('|');
            }

            for (int c = 0; c < array.Columns; c++)
            {
                if (c > 0)
                {
                    sb.Append(';');
                }

                CalcValue v = array[r, c];
                switch (v.Kind)
                {
                    case ValueKind.Number:
                        sb.Append(v.Number.ToString("R", CultureInfo.InvariantCulture));
                        break;
                    case ValueKind.Text:
                        sb.Append('"').Append(v.Text.Replace("\"", "\"\"", StringComparison.Ordinal)).Append('"');
                        break;
                    case ValueKind.Boolean:
                        sb.Append(v.Boolean ? "TRUE()" : "FALSE()");
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
