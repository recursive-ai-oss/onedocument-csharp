using System;
using System.Collections.Generic;

namespace RecursiveAI.OneDocument.Calc;

/// <summary>The kind of a <see cref="FormulaNode"/>, for switching without type tests.</summary>
public enum NodeKind : byte
{
    /// <summary>A number literal.</summary>
    Number,

    /// <summary>A text literal.</summary>
    Text,

    /// <summary>TRUE or FALSE.</summary>
    Boolean,

    /// <summary>An error literal.</summary>
    Error,

    /// <summary>An omitted argument.</summary>
    Missing,

    /// <summary>A single-cell reference.</summary>
    Cell,

    /// <summary>A rectangular, whole-row or whole-column reference.</summary>
    Area,

    /// <summary>A defined name.</summary>
    Name,

    /// <summary>A table (structured) reference.</summary>
    Structured,

    /// <summary>A function call by name.</summary>
    Call,

    /// <summary>A call of a computed callee, such as <c>LAMBDA(x,x)(1)</c>.</summary>
    Invoke,

    /// <summary>A prefix operator.</summary>
    Unary,

    /// <summary>The postfix percent operator.</summary>
    Percent,

    /// <summary>The spill operator <c>#</c>.</summary>
    Spill,

    /// <summary>A binary operator, including the reference operators.</summary>
    Binary,

    /// <summary>Parentheses.</summary>
    Paren,

    /// <summary>An array constant.</summary>
    ArrayConstant,

    /// <summary>A dotted member path (template extension).</summary>
    MemberPath,
}

/// <summary>
/// The sheet part of a reference: <c>Sheet1!</c>, <c>'My Sheet'!</c>, <c>Sheet1:Sheet3!</c>, <c>[1]Sheet1!</c>. Kept as the text that was
/// read as well as its parts, so printing gives the same bytes.
/// </summary>
public sealed class SheetPrefix
{
    /// <summary>Creates a prefix from parts; the text is built on demand.</summary>
    /// <param name="sheet">The first sheet name (unquoted), or null for <c>[1]!Name</c>.</param>
    /// <param name="lastSheet">The last sheet of a 3-D reference, or null.</param>
    /// <param name="workbook">The text between <c>[</c> and <c>]</c> for an external reference, or null.</param>
    public SheetPrefix(string? sheet, string? lastSheet = null, string? workbook = null)
    {
        Sheet = sheet;
        LastSheet = lastSheet;
        Workbook = workbook;
    }

    internal SheetPrefix(string? sheet, string? lastSheet, string? workbook, string raw)
        : this(sheet, lastSheet, workbook)
    {
        Raw = raw;
    }

    /// <summary>The first sheet name without quotes, or null (an external defined name has none).</summary>
    public string? Sheet { get; }

    /// <summary>The last sheet of a 3-D reference, or null.</summary>
    public string? LastSheet { get; }

    /// <summary>The external workbook index or name (what stands between <c>[</c> and <c>]</c>), or null.</summary>
    public string? Workbook { get; }

    /// <summary>True for a 3-D reference.</summary>
    public bool Is3D => LastSheet is not null;

    /// <summary>The text as read, including the closing <c>!</c>; null for a prefix built from parts.</summary>
    public string? Raw { get; }

    /// <summary>The prefix as it is written in a formula, including the <c>!</c>: quotes added where the sheet name needs them.</summary>
    /// <returns>The text.</returns>
    public override string ToString()
    {
        if (Raw is not null)
        {
            return Raw;
        }

        string body;
        if (LastSheet is not null)
        {
            string both = Sheet + ":" + LastSheet;
            body = NeedsQuotes(Sheet!) || NeedsQuotes(LastSheet) ? "'" + both.Replace("'", "''", StringComparison.Ordinal) + "'" : both;
        }
        else
        {
            body = Sheet is null ? string.Empty : NeedsQuotes(Sheet) ? "'" + Sheet.Replace("'", "''", StringComparison.Ordinal) + "'" : Sheet;
        }

        return (Workbook is null ? string.Empty : "[" + Workbook + "]") + body + "!";
    }

    internal static bool NeedsQuotes(string name)
    {
        if (name.Length == 0)
        {
            return true;
        }

        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (!(char.IsLetterOrDigit(c) || c == '_' || c == '.' || c > 127))
            {
                return true;
            }
        }

        // A name that would read as a cell reference, a number or a boolean must be quoted (ECMA-376 Part 1 §18.17.2).
        return char.IsDigit(name[0]) || name[0] == '.' || RefSyntax.LooksLikeCell(name) || RefSyntax.LooksLikeR1C1(name)
            || name.Equals("TRUE", StringComparison.OrdinalIgnoreCase) || name.Equals("FALSE", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>One coordinate of a reference: a 1-based index and whether it is absolute (<c>$</c>). In the tree a relative index is the position in the formula's own cell frame, so the A1 text and the R1C1 text are both derived from it.</summary>
/// <param name="Index">1-based row or column.</param>
/// <param name="Absolute">True when written with <c>$</c> (A1) or without brackets (R1C1).</param>
public readonly record struct RefCoord(int Index, bool Absolute);

/// <summary>A node of a parsed formula. Immutable; nodes keep the spaces and the text spellings they were read with, so a tree that was not edited prints back to the same bytes.</summary>
public abstract class FormulaNode
{
    internal FormulaNode(NodeKind kind) => Kind = kind;

    /// <summary>What the node is.</summary>
    public NodeKind Kind { get; }

    /// <summary>Offset in the source text of the node's first character (after its leading space), or -1 for a node built by code.</summary>
    public int Offset { get; internal set; } = -1;

    /// <summary>Length in the source text, not counting the node's spaces, or 0 for a node built by code.</summary>
    public int Length { get; internal set; }

    /// <summary>Spaces and line breaks read before the node, or null.</summary>
    public string? LeadingSpace { get; internal set; }

    /// <summary>Spaces and line breaks read after the node (before an operator, comma or parenthesis), or null.</summary>
    public string? TrailingSpace { get; internal set; }

    /// <summary>The child nodes in source order.</summary>
    public virtual IReadOnlyList<FormulaNode> Children => [];

    internal void AddTrailing(string space) => TrailingSpace = TrailingSpace is null ? space : TrailingSpace + space;

    /// <summary>The number of nodes in the tree below and including this one.</summary>
    /// <returns>The count.</returns>
    public int CountNodes()
    {
        int n = 1;
        foreach (FormulaNode c in Children)
        {
            n += c.CountNodes();
        }

        return n;
    }
}

/// <summary>A number literal.</summary>
public sealed class NumberNode : FormulaNode
{
    /// <summary>Creates a number literal.</summary>
    /// <param name="value">The number.</param>
    /// <param name="raw">The text as written, or null to print the shortest round-trip form.</param>
    public NumberNode(double value, string? raw = null)
        : base(NodeKind.Number)
    {
        Value = value;
        Raw = raw;
        Cached = CalcValue.FromNumber(value);
    }

    /// <summary>The number.</summary>
    public double Value { get; }

    /// <summary>The text as written, or null.</summary>
    public string? Raw { get; }

    internal CalcValue Cached { get; }
}

/// <summary>A text literal. Printed with quotes doubled.</summary>
public sealed class TextNode : FormulaNode
{
    /// <summary>Creates a text literal.</summary>
    /// <param name="value">The text without quotes.</param>
    public TextNode(string value)
        : base(NodeKind.Text)
    {
        Value = value;
        Cached = CalcValue.FromText(value);
    }

    /// <summary>The text.</summary>
    public string Value { get; }

    internal CalcValue Cached { get; }
}

/// <summary>TRUE or FALSE as a literal.</summary>
public sealed class BooleanNode : FormulaNode
{
    /// <summary>Creates a boolean literal.</summary>
    /// <param name="value">The value.</param>
    /// <param name="raw">The spelling as written (<c>true</c>), or null for upper case.</param>
    public BooleanNode(bool value, string? raw = null)
        : base(NodeKind.Boolean)
    {
        Value = value;
        Raw = raw;
    }

    /// <summary>The value.</summary>
    public bool Value { get; }

    /// <summary>The spelling as written, or null.</summary>
    public string? Raw { get; }
}

/// <summary>An error literal such as <c>#N/A</c>, or a broken reference with its sheet (<c>Sheet1!#REF!</c>).</summary>
public sealed class ErrorNode : FormulaNode
{
    /// <summary>Creates an error literal.</summary>
    /// <param name="error">The error.</param>
    /// <param name="prefix">A sheet prefix in front of <c>#REF!</c>, or null.</param>
    /// <param name="raw">The spelling as written, or null.</param>
    public ErrorNode(CalcError error, SheetPrefix? prefix = null, string? raw = null)
        : base(NodeKind.Error)
    {
        Error = error;
        Prefix = prefix;
        Raw = raw;
    }

    /// <summary>The error.</summary>
    public CalcError Error { get; }

    /// <summary>The sheet prefix, or null.</summary>
    public SheetPrefix? Prefix { get; }

    /// <summary>The spelling as written, or null.</summary>
    public string? Raw { get; }
}

/// <summary>An omitted argument: <c>IF(A1,,2)</c> has one in the middle.</summary>
public sealed class MissingNode : FormulaNode
{
    /// <summary>Creates a missing argument.</summary>
    public MissingNode()
        : base(NodeKind.Missing)
    {
    }
}

/// <summary>A reference to one cell.</summary>
public sealed class CellReferenceNode : FormulaNode
{
    /// <summary>Creates a cell reference.</summary>
    /// <param name="prefix">The sheet prefix, or null.</param>
    /// <param name="row">The row.</param>
    /// <param name="column">The column.</param>
    /// <param name="raw">The text as written (without the prefix), or null.</param>
    public CellReferenceNode(SheetPrefix? prefix, RefCoord row, RefCoord column, string? raw = null)
        : base(NodeKind.Cell)
    {
        Prefix = prefix;
        Row = row;
        Column = column;
        Raw = raw;
    }

    /// <summary>The sheet prefix, or null for the formula's own sheet.</summary>
    public SheetPrefix? Prefix { get; }

    /// <summary>The row.</summary>
    public RefCoord Row { get; }

    /// <summary>The column.</summary>
    public RefCoord Column { get; }

    /// <summary>The text as written without the prefix, or null.</summary>
    public string? Raw { get; }

    internal CalcValue Cached { get; set; }
}

/// <summary>What an <see cref="AreaReferenceNode"/> covers.</summary>
public enum AreaKind : byte
{
    /// <summary><c>A1:B2</c>.</summary>
    Cells,

    /// <summary><c>1:3</c>: whole rows.</summary>
    Rows,

    /// <summary><c>A:C</c>: whole columns.</summary>
    Columns,
}

/// <summary>A reference to a block: <c>A1:B2</c>, <c>$A:$C</c> (whole columns) or <c>1:3</c> (whole rows).</summary>
public sealed class AreaReferenceNode : FormulaNode
{
    /// <summary>Creates an area reference.</summary>
    /// <param name="prefix">The sheet prefix, or null.</param>
    /// <param name="areaKind">What it covers.</param>
    /// <param name="row1">First row (ignored for whole columns).</param>
    /// <param name="column1">First column (ignored for whole rows).</param>
    /// <param name="row2">Last row.</param>
    /// <param name="column2">Last column.</param>
    /// <param name="raw">The text as written without the prefix, or null.</param>
    public AreaReferenceNode(SheetPrefix? prefix, AreaKind areaKind, RefCoord row1, RefCoord column1, RefCoord row2, RefCoord column2, string? raw = null)
        : base(NodeKind.Area)
    {
        Prefix = prefix;
        AreaKind = areaKind;
        Row1 = row1;
        Column1 = column1;
        Row2 = row2;
        Column2 = column2;
        Raw = raw;
    }

    /// <summary>The sheet prefix, or null.</summary>
    public SheetPrefix? Prefix { get; }

    /// <summary>What it covers.</summary>
    public AreaKind AreaKind { get; }

    /// <summary>First row.</summary>
    public RefCoord Row1 { get; }

    /// <summary>First column.</summary>
    public RefCoord Column1 { get; }

    /// <summary>Last row.</summary>
    public RefCoord Row2 { get; }

    /// <summary>Last column.</summary>
    public RefCoord Column2 { get; }

    /// <summary>The text as written without the prefix, or null.</summary>
    public string? Raw { get; }

    internal CalcValue Cached { get; set; }
}

/// <summary>A defined name, possibly sheet-scoped or external (<c>Sheet1!Total</c>, <c>[1]!Total</c>).</summary>
public sealed class NameNode : FormulaNode
{
    /// <summary>Creates a name.</summary>
    /// <param name="prefix">The scope, or null.</param>
    /// <param name="name">The name as written.</param>
    public NameNode(SheetPrefix? prefix, string name)
        : base(NodeKind.Name)
    {
        Prefix = prefix;
        Name = name;
    }

    /// <summary>The scope prefix, or null.</summary>
    public SheetPrefix? Prefix { get; }

    /// <summary>The name as written, including any <c>_xlpm.</c> prefix of a LET or LAMBDA parameter.</summary>
    public string Name { get; }

    /// <summary>The name without the <c>_xlpm.</c> prefix.</summary>
    public string BareName => Name.StartsWith("_xlpm.", StringComparison.OrdinalIgnoreCase) ? Name[6..] : Name;
}

/// <summary>A table reference: <c>Table1[Col]</c>, <c>Table1[[#This Row],[Col]]</c>, <c>[@Col]</c>. The specifier is kept as written; binding it to a table is the workbook's job.</summary>
public sealed class StructuredReferenceNode : FormulaNode
{
    /// <summary>Creates a table reference.</summary>
    /// <param name="prefix">An external or sheet prefix, or null.</param>
    /// <param name="table">The table name, or null for a reference inside the table (<c>[@Col]</c>).</param>
    /// <param name="specifier">The bracketed text, including the outer brackets.</param>
    public StructuredReferenceNode(SheetPrefix? prefix, string? table, string specifier)
        : base(NodeKind.Structured)
    {
        Prefix = prefix;
        Table = table;
        Specifier = specifier;
    }

    /// <summary>The prefix, or null.</summary>
    public SheetPrefix? Prefix { get; }

    /// <summary>The table name, or null.</summary>
    public string? Table { get; }

    /// <summary>The bracketed specifier as written.</summary>
    public string Specifier { get; }

    /// <summary>The items of the specifier: <c>#This Row</c>, <c>Col</c>, <c>Col2</c>; a leading <c>@</c> reads as <c>#This Row</c>. Special items keep their <c>#</c>.</summary>
    /// <returns>The items.</returns>
    public IReadOnlyList<string> GetItems() => RefSyntax.SplitStructuredItems(Specifier);
}

/// <summary>A call of a function by name.</summary>
public sealed class FunctionCallNode : FormulaNode
{
    private readonly FormulaNode[] _args;

    /// <summary>Creates a call.</summary>
    /// <param name="rawName">The name as written, with any <c>_xlfn.</c>, <c>_xlws.</c> or <c>_xlfn._xlws.</c> prefix.</param>
    /// <param name="args">The arguments; an omitted one is a <see cref="MissingNode"/>.</param>
    public FunctionCallNode(string rawName, IEnumerable<FormulaNode> args)
        : base(NodeKind.Call)
    {
        RawName = rawName;
        Name = StripPrefix(rawName);
        _args = [.. args];
    }

    /// <summary>The name as written, including prefixes.</summary>
    public string RawName { get; }

    /// <summary>The name without prefixes, as written (case kept).</summary>
    public string Name { get; }

    /// <summary>The arguments.</summary>
    public IReadOnlyList<FormulaNode> Arguments => _args;

    /// <summary>Spaces between <c>(</c> and <c>)</c> when there are no arguments.</summary>
    public string? InnerSpace { get; internal set; }

    /// <inheritdoc/>
    public override IReadOnlyList<FormulaNode> Children => _args;

    internal FormulaNode[] ArgumentArray => _args;

    /// <summary>True when <see cref="RawName"/> is exactly the stored (file form) name, so printing keeps it; false for text typed by a user, where printing adds the prefix a future function needs.</summary>
    public bool NameIsFileForm { get; init; } = true;

    internal static string StripPrefix(string rawName)
    {
        int i = 0;
        while (true)
        {
            if (rawName.AsSpan(i).StartsWith("_xlfn.", StringComparison.OrdinalIgnoreCase) || rawName.AsSpan(i).StartsWith("_xlws.", StringComparison.OrdinalIgnoreCase))
            {
                i += 6;
            }
            else
            {
                break;
            }
        }

        return i == 0 ? rawName : rawName[i..];
    }
}

/// <summary>A call whose callee is computed: <c>LAMBDA(x,x+1)(3)</c> or <c>(f)(3)</c>.</summary>
public sealed class InvokeNode : FormulaNode
{
    private readonly FormulaNode[] _args;
    private readonly FormulaNode[] _children;

    /// <summary>Creates the node.</summary>
    /// <param name="callee">The expression that yields a lambda.</param>
    /// <param name="args">The arguments.</param>
    public InvokeNode(FormulaNode callee, IEnumerable<FormulaNode> args)
        : base(NodeKind.Invoke)
    {
        Callee = callee;
        _args = [.. args];
        _children = [callee, .. _args];
    }

    /// <summary>The callee.</summary>
    public FormulaNode Callee { get; }

    /// <summary>The arguments.</summary>
    public IReadOnlyList<FormulaNode> Arguments => _args;

    /// <summary>Spaces inside empty parentheses.</summary>
    public string? InnerSpace { get; internal set; }

    /// <inheritdoc/>
    public override IReadOnlyList<FormulaNode> Children => _children;
}

/// <summary>A prefix operator: <c>-</c>, <c>+</c>, or <c>@</c> (implicit intersection, display form).</summary>
public sealed class UnaryNode : FormulaNode
{
    /// <summary>Creates the node.</summary>
    /// <param name="op">One of <c>-</c>, <c>+</c>, <c>@</c>.</param>
    /// <param name="operand">The operand.</param>
    public UnaryNode(char op, FormulaNode operand)
        : base(NodeKind.Unary)
    {
        Operator = op;
        Operand = operand;
    }

    /// <summary>The operator character.</summary>
    public char Operator { get; }

    /// <summary>The operand.</summary>
    public FormulaNode Operand { get; }

    /// <summary>True when the <c>@</c> was read from file text as written, so file form prints it back as <c>@</c> instead of <c>_xlfn.SINGLE(...)</c>.</summary>
    public bool IsFileForm { get; init; }

    /// <summary>Spaces between the operator and the operand are the operand's <see cref="FormulaNode.LeadingSpace"/>.</summary>
    public override IReadOnlyList<FormulaNode> Children => [Operand];
}

/// <summary>The postfix percent operator.</summary>
public sealed class PercentNode : FormulaNode
{
    /// <summary>Creates the node.</summary>
    /// <param name="operand">The operand.</param>
    public PercentNode(FormulaNode operand)
        : base(NodeKind.Percent)
    {
        Operand = operand;
    }

    /// <summary>The operand.</summary>
    public FormulaNode Operand { get; }

    /// <inheritdoc/>
    public override IReadOnlyList<FormulaNode> Children => [Operand];
}

/// <summary>The spill operator: <c>A1#</c> is the whole dynamic-array result anchored at A1.</summary>
public sealed class SpillNode : FormulaNode
{
    /// <summary>Creates the node.</summary>
    /// <param name="operand">The anchor expression (a reference or a name).</param>
    public SpillNode(FormulaNode operand)
        : base(NodeKind.Spill)
    {
        Operand = operand;
    }

    /// <summary>The anchor.</summary>
    public FormulaNode Operand { get; }

    /// <summary>True when the <c>#</c> was read from file text as written, so file form prints it back as <c>#</c> instead of <c>_xlfn.ANCHORARRAY(...)</c>.</summary>
    public bool IsFileForm { get; init; }

    /// <inheritdoc/>
    public override IReadOnlyList<FormulaNode> Children => [Operand];
}

/// <summary>The binary operators, in the order Excel's precedence table lists them (lowest first).</summary>
public enum BinaryOperator : byte
{
    /// <summary><c>=</c>.</summary>
    Equal,

    /// <summary><c>&lt;&gt;</c>.</summary>
    NotEqual,

    /// <summary><c>&lt;</c>.</summary>
    Less,

    /// <summary><c>&lt;=</c>.</summary>
    LessOrEqual,

    /// <summary><c>&gt;</c>.</summary>
    Greater,

    /// <summary><c>&gt;=</c>.</summary>
    GreaterOrEqual,

    /// <summary><c>&amp;</c>.</summary>
    Concat,

    /// <summary><c>+</c>.</summary>
    Add,

    /// <summary><c>-</c>.</summary>
    Subtract,

    /// <summary><c>*</c>.</summary>
    Multiply,

    /// <summary><c>/</c>.</summary>
    Divide,

    /// <summary><c>^</c>.</summary>
    Power,

    /// <summary><c>:</c>, the range operator between computed references.</summary>
    Range,

    /// <summary>A space: the intersection of two references.</summary>
    Intersect,

    /// <summary><c>,</c> inside parentheses: the union of two references.</summary>
    Union,
}

/// <summary>A binary operator expression.</summary>
public sealed class BinaryNode : FormulaNode
{
    /// <summary>Creates the node.</summary>
    /// <param name="op">The operator.</param>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <param name="operatorText">The operator text as written; for an intersection, the spaces. Null to print the standard text.</param>
    public BinaryNode(BinaryOperator op, FormulaNode left, FormulaNode right, string? operatorText = null)
        : base(NodeKind.Binary)
    {
        Operator = op;
        Left = left;
        Right = right;
        OperatorText = operatorText;
    }

    /// <summary>The operator.</summary>
    public BinaryOperator Operator { get; }

    /// <summary>Left operand.</summary>
    public FormulaNode Left { get; }

    /// <summary>Right operand.</summary>
    public FormulaNode Right { get; }

    /// <summary>The operator as written, or null.</summary>
    public string? OperatorText { get; }

    /// <inheritdoc/>
    public override IReadOnlyList<FormulaNode> Children => [Left, Right];

    /// <summary>The standard text of an operator.</summary>
    /// <param name="op">The operator.</param>
    /// <returns>The text.</returns>
    public static string Text(BinaryOperator op) => op switch
    {
        BinaryOperator.Equal => "=",
        BinaryOperator.NotEqual => "<>",
        BinaryOperator.Less => "<",
        BinaryOperator.LessOrEqual => "<=",
        BinaryOperator.Greater => ">",
        BinaryOperator.GreaterOrEqual => ">=",
        BinaryOperator.Concat => "&",
        BinaryOperator.Add => "+",
        BinaryOperator.Subtract => "-",
        BinaryOperator.Multiply => "*",
        BinaryOperator.Divide => "/",
        BinaryOperator.Power => "^",
        BinaryOperator.Range => ":",
        BinaryOperator.Intersect => " ",
        _ => ",",
    };
}

/// <summary>A parenthesised expression. Parentheses matter to the result (a parenthesised sum is not snapped to zero) and to printing.</summary>
public sealed class ParenNode : FormulaNode
{
    /// <summary>Creates the node.</summary>
    /// <param name="inner">The inner expression.</param>
    public ParenNode(FormulaNode inner)
        : base(NodeKind.Paren)
    {
        Inner = inner;
    }

    /// <summary>The inner expression.</summary>
    public FormulaNode Inner { get; }

    /// <inheritdoc/>
    public override IReadOnlyList<FormulaNode> Children => [Inner];
}

/// <summary>An array constant: <c>{1,2;3,4}</c>. Elements are numbers, text, booleans and errors.</summary>
public sealed class ArrayConstantNode : FormulaNode
{
    /// <summary>Creates the node.</summary>
    /// <param name="array">The values.</param>
    /// <param name="raw">The text as written including braces, or null.</param>
    public ArrayConstantNode(CalcArray array, string? raw = null)
        : base(NodeKind.ArrayConstant)
    {
        Value = array;
        Raw = raw;
    }

    /// <summary>The values.</summary>
    public CalcArray Value { get; }

    /// <summary>The text as written, or null.</summary>
    public string? Raw { get; }
}

/// <summary>
/// A dotted member path such as <c>item.price</c> (template extension; only produced when <see cref="FormulaParserOptions.TemplateExtensions"/> is on).
/// The first segment is looked up as a name, the rest are members of it. Over a collection the path lifts: it yields the array of members.
/// </summary>
public sealed class MemberPathNode : FormulaNode
{
    private readonly string[] _segments;

    /// <summary>Creates the node.</summary>
    /// <param name="segments">At least two segments.</param>
    public MemberPathNode(IEnumerable<string> segments)
        : base(NodeKind.MemberPath)
    {
        _segments = [.. segments];
    }

    /// <summary>The path segments, root first.</summary>
    public IReadOnlyList<string> Segments => _segments;
}
