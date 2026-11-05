using System.Globalization;
using RecursiveAI.OneDocument.Calc;

namespace RecursiveAI.OneDocument.Spreadsheets.Calc;

/// <summary>A table as calculation sees it: a frozen copy of its name, place and column names.</summary>
internal sealed class TableInfo
{
    public required string Name { get; init; }

    public required string DisplayName { get; init; }

    public required int Sheet { get; init; }

    public required int Row1 { get; init; }

    public required int Column1 { get; init; }

    public required int Row2 { get; init; }

    public required int Column2 { get; init; }

    public required int HeaderRows { get; init; }

    public required int TotalsRows { get; init; }

    public required string[] Columns { get; init; }
}

/// <summary>A defined name as calculation sees it. The parsed formula is read on first use.</summary>
internal sealed class NameInfo
{
    private readonly object _gate = new();
    private FormulaNode? _tree;
    private bool _parsed;

    public required string Name { get; init; }

    public required int Scope { get; init; }

    public required string Formula { get; init; }

    /// <summary>The parsed formula, or null when the text does not parse.</summary>
    public FormulaNode? GetTree(CalcBook book)
    {
        lock (_gate)
        {
            if (!_parsed)
            {
                _parsed = true;
                // Names are written in file form with absolute references (ECMA-376 Part 1 §18.2.5).
                if (!FormulaParser.TryParse(Formula, out _tree, out _, new FormulaParserOptions { Limits = book.Workbook.Limits }))
                {
                    _tree = null;
                }
            }

            return _tree;
        }
    }
}

/// <summary>The sheets, names and tables of a workbook, frozen for one calculation graph.</summary>
internal sealed class CalcBook
{
    private readonly Dictionary<string, int> _sheetIndex;
    private readonly Dictionary<string, TableInfo> _tables = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<TableInfo> _tableList = [];
    private readonly Dictionary<(int Scope, string Name), NameInfo> _names = [];

    public CalcBook(Workbook workbook)
    {
        Workbook = workbook;
        Sheets = new Worksheet[workbook.Sheets.Count];
        _sheetIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < Sheets.Length; i++)
        {
            Sheets[i] = workbook.Sheets[i];
            _sheetIndex[Sheets[i].Name] = i;
        }

        for (int i = 0; i < Sheets.Length; i++)
        {
            if (Sheets[i].Kind != SheetKind.Worksheet)
            {
                continue;
            }

            foreach (Table t in Sheets[i].Tables)
            {
                CellRange r = t.Range;
                if (!r.IsValid)
                {
                    continue;
                }

                var cols = new List<string>();
                foreach (TableColumn c in t.Columns)
                {
                    cols.Add(c.Name);
                }

                var info = new TableInfo
                {
                    Name = t.Name,
                    DisplayName = t.DisplayName,
                    Sheet = i,
                    Row1 = r.TopLeft.Row,
                    Column1 = r.TopLeft.Column,
                    Row2 = r.BottomRight.Row,
                    Column2 = r.BottomRight.Column,
                    HeaderRows = t.HeaderRowCount,
                    TotalsRows = t.TotalsRowCount,
                    Columns = [.. cols],
                };
                _tableList.Add(info);
                _tables[info.DisplayName] = info;
                _tables.TryAdd(info.Name, info);
            }
        }

        foreach (DefinedName d in workbook.Names)
        {
            if (d.IsBuiltIn || d.IsFunction)
            {
                continue;
            }

            int scope = d.LocalSheetId is { } s && s >= 0 && s < Sheets.Length ? s : -1;
            _names[(scope, d.Name.ToUpperInvariant())] = new NameInfo { Name = d.Name, Scope = scope, Formula = d.Formula };
        }
    }

    public Workbook Workbook { get; }

    public Worksheet[] Sheets { get; }

    public IReadOnlyList<TableInfo> Tables => _tableList;

    /// <summary>Finds a sheet by name, case-insensitively; -1 when there is none.</summary>
    public int FindSheet(string? name, int fallback) => name is null ? fallback : _sheetIndex.TryGetValue(name, out int i) ? i : -1;

    public TableInfo? FindTable(string name) => _tables.TryGetValue(name, out TableInfo? t) ? t : null;

    /// <summary>Finds the table whose cells include a position.</summary>
    public TableInfo? TableAt(int sheet, int row, int column)
    {
        foreach (TableInfo t in _tableList)
        {
            if (t.Sheet == sheet && row >= t.Row1 && row <= t.Row2 && column >= t.Column1 && column <= t.Column2)
            {
                return t;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds a name the way a formula on a sheet sees it: the sheet's own name first, then the workbook's. A name written with a sheet
    /// prefix is looked up on that sheet, then in the workbook.
    /// </summary>
    public NameInfo? FindName(string name, int sheet)
    {
        string key = name.ToUpperInvariant();
        if (sheet >= 0 && _names.TryGetValue((sheet, key), out NameInfo? local))
        {
            return local;
        }

        return _names.TryGetValue((-1, key), out NameInfo? global) ? global : null;
    }

    /// <summary>A text that changes when a sheet, name or table that formulas depend on changes.</summary>
    public static string Signature(Workbook wb)
    {
        var sb = new System.Text.StringBuilder();
        foreach (Worksheet ws in wb.Sheets)
        {
            sb.Append(ws.Name).Append('|');
            if (ws.Kind == SheetKind.Worksheet)
            {
                foreach (Table t in ws.Tables)
                {
                    sb.Append('T').Append(t.DisplayName).Append(t.Range.ToString()).Append(t.HeaderRowCount).Append(t.TotalsRowCount);
                    foreach (TableColumn c in t.Columns)
                    {
                        sb.Append(',').Append(c.Name);
                    }
                }
            }
        }

        foreach (DefinedName d in wb.Names)
        {
            sb.Append('N').Append(d.Name).Append(':').Append(d.LocalSheetId?.ToString(CultureInfo.InvariantCulture)).Append('=').Append(d.Formula).Append(';');
        }

        return sb.ToString();
    }
}

/// <summary>Turns table references into cell areas.</summary>
internal static class StructuredRefs
{
    /// <summary>Resolves a table reference.</summary>
    /// <param name="book">The workbook.</param>
    /// <param name="node">The parsed reference.</param>
    /// <param name="originSheet">The sheet of the cell holding the formula.</param>
    /// <param name="originRow">Its row.</param>
    /// <param name="originColumn">Its column.</param>
    /// <param name="areas">The areas on success (sheet names filled in).</param>
    /// <returns>No error on success, otherwise the error the reference evaluates to.</returns>
    public static CalcError Resolve(CalcBook book, StructuredReferenceNode node, int originSheet, int originRow, int originColumn, out CellArea[] areas)
    {
        areas = [];
        TableInfo? t = node.Table is null ? book.TableAt(originSheet, originRow, originColumn) : book.FindTable(node.Table);
        if (t is null)
        {
            return CalcError.Ref;
        }

        // ECMA-376 Part 1 §18.17.2.3 (structured references): the special items select rows, column names select columns.
        bool headers = false, data = false, totals = false, thisRow = false, all = false;
        var columnItems = new List<string>();
        foreach (string item in node.GetItems())
        {
            switch (item.ToUpperInvariant())
            {
                case "#HEADERS":
                    headers = true;
                    break;
                case "#DATA":
                    data = true;
                    break;
                case "#TOTALS":
                    totals = true;
                    break;
                case "#THIS ROW":
                    thisRow = true;
                    break;
                case "#ALL":
                    all = true;
                    break;
                default:
                    columnItems.Add(item);
                    break;
            }
        }

        int top = t.Row1 + t.HeaderRows;
        int bottom = t.Row2 - t.TotalsRows;
        int r1, r2;
        if (thisRow)
        {
            if (originSheet != t.Sheet || originRow < top || originRow > bottom)
            {
                return CalcError.Value;
            }

            r1 = r2 = originRow;
        }
        else if (all)
        {
            r1 = t.Row1;
            r2 = t.Row2;
        }
        else if (!headers && !data && !totals)
        {
            r1 = top;
            r2 = bottom;
        }
        else
        {
            // A combination must be contiguous: headers+data, data+totals, or all three.
            if (headers && totals && !data)
            {
                return CalcError.Ref;
            }

            r1 = headers ? t.Row1 : data ? top : t.Row2 - t.TotalsRows + 1;
            r2 = totals ? t.Row2 : data ? bottom : t.Row1 + t.HeaderRows - 1;
        }

        if (r2 < r1)
        {
            return CalcError.Ref;
        }

        int c1 = t.Column1, c2 = t.Column2;
        if (columnItems.Count > 0)
        {
            int first = ColumnOffset(t, columnItems[0]);
            int last = ColumnOffset(t, columnItems[^1]);
            if (first < 0 || last < 0)
            {
                return CalcError.Ref;
            }

            c1 = t.Column1 + Math.Min(first, last);
            c2 = t.Column1 + Math.Max(first, last);
        }

        string sheetName = book.Sheets[t.Sheet].Name;
        areas = [new CellArea(sheetName, null, r1, c1, r2, c2)];
        return CalcError.None;
    }

    private static int ColumnOffset(TableInfo t, string name)
    {
        for (int i = 0; i < t.Columns.Length; i++)
        {
            if (string.Equals(t.Columns[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }
}
