using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Schema;

namespace RecursiveAI.OneDocument.Documents;

/// <summary>One cell of the logical grid: a merged region or an unmerged cell, anchored at its top-left grid position.</summary>
public sealed class LogicalCell
{
    internal LogicalCell(TableCell cell, int row, int column, int columnSpan)
    {
        Cell = cell;
        Row = row;
        Column = column;
        ColumnSpan = columnSpan;
        RowSpan = 1;
        Covered = [];
    }

    /// <summary>Gets the physical cell that holds the content: the top-left cell of the region.</summary>
    public TableCell Cell { get; }

    /// <summary>Gets the zero-based row of the top-left grid position.</summary>
    public int Row { get; }

    /// <summary>Gets the zero-based grid column of the top-left position.</summary>
    public int Column { get; }

    /// <summary>Gets the number of grid columns covered: the <c>w:gridSpan</c> plus any legacy <c>w:hMerge</c> continuation cells.</summary>
    public int ColumnSpan { get; internal set; }

    /// <summary>Gets the number of rows covered: this one plus the rows whose cells continue the region with <c>w:vMerge</c>.</summary>
    public int RowSpan { get; internal set; }

    /// <summary>Gets the physical cells swallowed by the region (vertical and legacy horizontal continuation cells), in document order; empty for an unmerged cell.</summary>
    public List<TableCell> Covered { get; }

    /// <summary>Gets a value indicating whether the cell covers more than one grid position.</summary>
    public bool IsMerged => ColumnSpan > 1 || RowSpan > 1;
}

/// <summary>How many merges a table holds, for reports.</summary>
/// <param name="GridSpans">Cells with a <c>w:gridSpan</c> above 1.</param>
/// <param name="VerticalRegions">Vertical merge regions that cover at least two rows.</param>
/// <param name="VerticalContinuations">Cells that continue a region above.</param>
/// <param name="LegacyHorizontalContinuations">Cells that continue a legacy <c>w:hMerge</c> region.</param>
/// <param name="Orphans">Merge continuations without a restart: merged into the cell above when there is one (as Word does), otherwise read as plain cells; each is reported as a defect.</param>
public readonly record struct MergeCounts(int GridSpans, int VerticalRegions, int VerticalContinuations, int LegacyHorizontalContinuations, int Orphans);

/// <summary>
/// The logical cell model of a table: rows × grid columns, with spans and merges resolved. Built from the markup when asked for and
/// not kept in sync with later edits; ask the table again after an edit.
/// </summary>
/// <remarks>
/// Malformed merges are repaired the way the reader treats them and reported as defects: <c>docx.table-merge-orphan</c> (a continuation
/// without a restart: merged upward when a cell is above, else a plain cell; a horizontal one with nothing to its left is a plain cell), <c>docx.table-merge-span-mismatch</c> (a continuation whose span differs from the cell above),
/// <c>docx.table-grid-mismatch</c> (rows wider than <c>w:tblGrid</c>) and <c>docx.table-span-invalid</c> (a <c>w:gridSpan</c> below 1 or
/// above 16384).
/// </remarks>
public sealed class LogicalTable
{
    private readonly LogicalCell?[][] _grid;

    private LogicalTable(Table table, int rowCount, int columnCount, LogicalCell?[][] grid, List<LogicalCell> cells, MergeCounts merges)
    {
        Table = table;
        RowCount = rowCount;
        ColumnCount = columnCount;
        _grid = grid;
        Cells = cells;
        Merges = merges;
    }

    /// <summary>Gets the table.</summary>
    public Table Table { get; }

    /// <summary>Gets the number of rows.</summary>
    public int RowCount { get; }

    /// <summary>Gets the number of grid columns: the larger of the table grid and the widest row.</summary>
    public int ColumnCount { get; }

    /// <summary>Gets the logical cells (merged regions count once) in row-major order of their top-left positions.</summary>
    public IReadOnlyList<LogicalCell> Cells { get; }

    /// <summary>Gets the merge counts.</summary>
    public MergeCounts Merges { get; }

    /// <summary>Gets the logical cell covering a grid position.</summary>
    /// <param name="row">The zero-based row.</param>
    /// <param name="column">The zero-based grid column.</param>
    /// <returns>The cell, or null when the position is outside the table or is left empty (<c>w:gridBefore</c>, <c>w:gridAfter</c>, a short row).</returns>
    public LogicalCell? this[int row, int column] =>
        (uint)row < (uint)RowCount && (uint)column < (uint)ColumnCount ? _grid[row][column] : null;

    internal static LogicalTable Build(Table table, bool report)
    {
        Document doc = table.Owner;
        List<TableRow> rows = [.. table.Rows()];
        int gridColumns = table.GridColumns.Count;

        // Pass 1: where each physical cell starts in the grid and how wide it is.
        var phys = new List<(TableCell Cell, int Start, int Span, CellMerge V, CellMerge H)>[rows.Count];
        int columns = gridColumns;
        for (int r = 0; r < rows.Count; r++)
        {
            phys[r] = [];
            int col = rows[r].GridBefore;
            foreach (TableCell tc in rows[r].Cells())
            {
                int span = tc.GridSpan;
                long rawSpan = Wml.Long(Wml.Attr(Wml.Child(tc.PropertiesElement ?? default, "gridSpan"u8) ?? default, "val")) ?? 1;
                if (report && (rawSpan < 1 || rawSpan > 16384))
                {
                    doc.ReportDefect("docx.table-span-invalid", DefectKind.Value, DefectSeverity.Repaired,
                        "A cell has a gridSpan of " + rawSpan + "; it is read as 1.", tc.Container);
                }

                phys[r].Add((tc, col, span, tc.VerticalMerge, tc.HorizontalMerge));
                col += span;
            }

            columns = Math.Max(columns, col + rows[r].GridAfter);
            if (report && gridColumns > 0 && col + rows[r].GridAfter > gridColumns)
            {
                doc.ReportDefect("docx.table-grid-mismatch", DefectKind.Structure, DefectSeverity.Repaired,
                    "Row " + r + " covers " + (col + rows[r].GridAfter) + " grid columns but the table grid has " + gridColumns + "; the grid is extended.", rows[r].Markup);
            }
        }

        // Pass 2: merges.
        var grid = new LogicalCell?[rows.Count][];
        var cells = new List<LogicalCell>();
        int gridSpans = 0, vRegions = 0, vCont = 0, hCont = 0, orphans = 0;
        for (int r = 0; r < rows.Count; r++)
        {
            grid[r] = new LogicalCell?[columns];
            LogicalCell? previous = null;
            foreach ((TableCell cell, int start, int span, CellMerge v, CellMerge h) in phys[r])
            {
                if (span > 1)
                {
                    gridSpans++;
                }

                // Legacy horizontal merge: a continuation cell widens the cell to its left.
                if (h == CellMerge.Continue)
                {
                    if (previous is not null && previous.Column + previous.ColumnSpan == start)
                    {
                        hCont++;
                        previous.ColumnSpan += span;
                        previous.Covered.Add(cell);
                        for (int c = start; c < start + span && c < columns; c++)
                        {
                            grid[r][c] = previous;
                        }

                        continue;
                    }

                    orphans++;
                    if (report)
                    {
                        doc.ReportDefect("docx.table-merge-orphan", DefectKind.Structure, DefectSeverity.Repaired,
                            "A horizontal merge continuation has no cell to its left to continue; it is read as a plain cell.", cell.Container);
                    }
                }

                if (v == CellMerge.Continue && h != CellMerge.Continue)
                {
                    LogicalCell? above = r > 0 && start < columns ? grid[r - 1][start] : null;
                    if (above is not null && above.Column == start)
                    {
                        // Observed in Word 365 (16.0.20527): a continuation under a cell that is not part of a merge still merges
                        // upward, so the cell above becomes the start of the region. The file is malformed, so it is reported.
                        if (above.RowSpan == 1 && !PhysicalIsMerged(phys[above.Row], start))
                        {
                            orphans++;
                            if (report)
                            {
                                doc.ReportDefect("docx.table-merge-orphan", DefectKind.Structure, DefectSeverity.Repaired,
                                    "A vertical merge continuation has no restart above it; it is merged into the cell above, as Word does.", cell.Container);
                            }
                        }

                        if (above.ColumnSpan != span && report)
                        {
                            doc.ReportDefect("docx.table-merge-span-mismatch", DefectKind.Structure, DefectSeverity.Repaired,
                                "A vertically merged cell spans " + span + " grid columns but the cell above spans " + above.ColumnSpan + "; the region keeps the upper cell's width.", cell.Container);
                        }

                        vCont++;
                        if (above.RowSpan == 1)
                        {
                            vRegions++;
                        }

                        above.RowSpan = r - above.Row + 1;
                        above.Covered.Add(cell);
                        for (int c = start; c < start + span && c < columns; c++)
                        {
                            grid[r][c] = above;
                        }

                        previous = above;
                        continue;
                    }

                    orphans++;
                    if (report)
                    {
                        doc.ReportDefect("docx.table-merge-orphan", DefectKind.Structure, DefectSeverity.Repaired,
                            "A vertical merge continuation has no cell above it to continue; it is read as a plain cell.", cell.Container);
                    }
                }

                var logical = new LogicalCell(cell, r, start, span);
                cells.Add(logical);
                for (int c = start; c < start + span && c < columns; c++)
                {
                    grid[r][c] = logical;
                }

                previous = logical;
            }
        }

        return new LogicalTable(table, rows.Count, columns, grid, cells, new MergeCounts(gridSpans, vRegions, vCont, hCont, orphans));
    }

    // A cell can be continued from below when it starts a region (restart) or is itself a continuation.
    private static bool PhysicalIsMerged(List<(TableCell Cell, int Start, int Span, CellMerge V, CellMerge H)> row, int start)
    {
        foreach ((TableCell _, int s, int _, CellMerge v, CellMerge _) in row)
        {
            if (s == start)
            {
                return v != CellMerge.None;
            }
        }

        return false;
    }
}

public sealed partial class Table
{
    /// <summary>
    /// Builds the logical cell model: rows × grid columns with <c>gridSpan</c>, <c>vMerge</c> and <c>hMerge</c> resolved. Malformed merges are
    /// repaired and reported as <c>docx.table-*</c> defects; nothing throws.
    /// </summary>
    /// <returns>The model, valid until the table is edited.</returns>
    public LogicalTable Logical() => LogicalTable.Build(this, report: true);
}
