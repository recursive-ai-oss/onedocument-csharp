using RecursiveAI.OneDocument.Calc;

namespace RecursiveAI.OneDocument.Spreadsheets.Calc;

/// <summary>Receives the cells a formula reads.</summary>
internal interface IPrecedentSink
{
    /// <summary>A block of cells (one cell when the corners are equal) on a sheet.</summary>
    void Area(int sheet, int row1, int column1, int row2, int column2);
}

/// <summary>
/// Finds what a parsed formula reads: cell and area references (3-D ones on every sheet they cross), the references inside the defined
/// names it uses, the areas of table references, and whether it calls a volatile function or one that builds references at run time.
/// </summary>
internal sealed class DependencyWalker(CalcBook book, FunctionRegistry registry)
{
    private const int MaxNameDepth = 16;

    private int _sheet;
    private int _row;
    private int _column;
    private IPrecedentSink _sink = null!;

    /// <summary>The formula calls a volatile function.</summary>
    public bool Volatile { get; private set; }

    /// <summary>The formula builds references at run time (<c>INDIRECT</c>, <c>OFFSET</c>), so its precedents are not all known.</summary>
    public bool Dynamic { get; private set; }

    /// <summary>The formula reads another workbook.</summary>
    public bool External { get; private set; }

    /// <summary>Walks a formula.</summary>
    /// <param name="tree">The formula.</param>
    /// <param name="sheet">The sheet index of the cell holding it.</param>
    /// <param name="row">Its row.</param>
    /// <param name="column">Its column.</param>
    /// <param name="sink">Where the cells go.</param>
    public void Walk(FormulaNode tree, int sheet, int row, int column, IPrecedentSink sink)
    {
        _sheet = sheet;
        _row = row;
        _column = column;
        _sink = sink;
        Volatile = false;
        Dynamic = false;
        External = false;
        Visit(tree, 0);
    }

    private void Visit(FormulaNode n, int nameDepth)
    {
        switch (n.Kind)
        {
            case NodeKind.Cell:
                {
                    var c = (CellReferenceNode)n;
                    Add(c.Prefix, c.Row.Index, c.Column.Index, c.Row.Index, c.Column.Index);
                    return;
                }

            case NodeKind.Area:
                {
                    var a = (AreaReferenceNode)n;
                    int r1 = a.AreaKind == AreaKind.Columns ? 1 : a.Row1.Index;
                    int r2 = a.AreaKind == AreaKind.Columns ? CellArea.MaxRow : a.Row2.Index;
                    int c1 = a.AreaKind == AreaKind.Rows ? 1 : a.Column1.Index;
                    int c2 = a.AreaKind == AreaKind.Rows ? CellArea.MaxColumn : a.Column2.Index;
                    Add(a.Prefix, Math.Min(r1, r2), Math.Min(c1, c2), Math.Max(r1, r2), Math.Max(c1, c2));
                    return;
                }

            case NodeKind.Name:
                {
                    var nn = (NameNode)n;
                    VisitName(nn.Prefix, nn.BareName, nameDepth);
                    return;
                }

            case NodeKind.Structured:
                {
                    var s = (StructuredReferenceNode)n;
                    if (StructuredRefs.Resolve(book, s, _sheet, _row, _column, out CellArea[] areas) == CalcError.None)
                    {
                        foreach (CellArea a in areas)
                        {
                            Add(a.Sheet is null ? null : new SheetPrefix(a.Sheet), a.Row1, a.Column1, a.Row2, a.Column2);
                        }
                    }

                    return;
                }

            case NodeKind.Call:
                {
                    var f = (FunctionCallNode)n;
                    if (f.Name.Equals("WEBSERVICE", StringComparison.OrdinalIgnoreCase) || f.Name.Equals("FILTERXML", StringComparison.OrdinalIgnoreCase))
                    {
                        External = true;
                    }

                    if (registry.TryGet(f.Name, out FunctionDefinition? def) && def is not null)
                    {
                        if (def.IsVolatile)
                        {
                            Volatile = true;
                        }

                        if ((def.Flags & FunctionTraits.DynamicReference) != 0)
                        {
                            Dynamic = true;
                        }
                    }
                    else if (f.Name.Equals("INDIRECT", StringComparison.OrdinalIgnoreCase) || f.Name.Equals("OFFSET", StringComparison.OrdinalIgnoreCase))
                    {
                        Dynamic = true;
                    }
                    else
                    {
                        // A call of a name that holds a lambda.
                        VisitName(null, f.Name.StartsWith("_xlpm.", StringComparison.OrdinalIgnoreCase) ? f.Name[6..] : f.Name, nameDepth);
                    }

                    break;
                }
        }

        foreach (FormulaNode child in n.Children)
        {
            Visit(child, nameDepth);
        }
    }

    private void VisitName(SheetPrefix? prefix, string name, int depth)
    {
        if (depth >= MaxNameDepth || prefix?.Workbook is not null)
        {
            External |= prefix?.Workbook is not null;
            return;
        }

        int scope = prefix?.Sheet is { } sn ? book.FindSheet(sn, _sheet) : _sheet;
        NameInfo? info = book.FindName(name, scope);
        if (info?.GetTree(book) is { } tree)
        {
            Visit(tree, depth + 1);
        }
    }

    private void Add(SheetPrefix? prefix, int r1, int c1, int r2, int c2)
    {
        if (prefix?.Workbook is not null)
        {
            External = true; // answered from the file's cache or a resolver, so there is nothing to wait for
            return;
        }

        if (prefix?.LastSheet is { } last && prefix.Sheet is { } first)
        {
            int a = book.FindSheet(first, -1);
            int b = book.FindSheet(last, -1);
            if (a < 0 || b < 0)
            {
                return;
            }

            for (int s = Math.Min(a, b); s <= Math.Max(a, b); s++)
            {
                _sink.Area(s, r1, c1, r2, c2);
            }

            return;
        }

        int sheet = prefix?.Sheet is { } name ? book.FindSheet(name, -1) : _sheet;
        if (sheet >= 0)
        {
            _sink.Area(sheet, r1, c1, r2, c2);
        }
    }
}
