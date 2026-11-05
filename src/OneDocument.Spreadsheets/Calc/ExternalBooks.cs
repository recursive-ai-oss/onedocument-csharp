using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Spreadsheets.Calc;

/// <summary>
/// Answers references to other workbooks for one calculation. A reference reads the live workbook when the caller supplied a resolver and it
/// returns one, and otherwise the values cached in the file; where neither has the cell the answer is <c>#REF!</c>. Values read live are
/// written back to the link's cache, and <see cref="Flush"/> saves them to the part. Nothing here opens a path or an address.
/// </summary>
internal sealed class ExternalBooks(Workbook workbook, IExternalWorkbookResolver? resolver)
{
    // Areas up to this many cells record their blank cells in the cache, so a later offline read answers blank rather than #REF!.
    private const int RecordBlanksUpTo = 1024;

    private readonly object _gate = new();
    private readonly Dictionary<int, Workbook?> _live = [];
    private IReadOnlyList<ExternalLink>? _links;

    public int Resolved { get; private set; }

    public int Missing { get; private set; }

    public CalcValue Read(SheetPrefix prefix, FormulaNode node)
    {
        lock (_gate)
        {
            _links ??= workbook.ExternalLinks;
            if (!int.TryParse(prefix.Workbook, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int index)
                || index < 1 || index > _links.Count || prefix.Sheet is null || prefix.LastSheet is not null)
            {
                return NotThere();
            }

            ExternalLink link = _links[index - 1];
            int r1, c1, r2, c2;
            bool wholeRows = false, wholeColumns = false, single = false;
            switch (node)
            {
                case CellReferenceNode c:
                    r1 = r2 = c.Row.Index;
                    c1 = c2 = c.Column.Index;
                    single = true;
                    break;
                case AreaReferenceNode a:
                    wholeColumns = a.AreaKind == AreaKind.Columns;
                    wholeRows = a.AreaKind == AreaKind.Rows;
                    int ar1 = wholeColumns ? 1 : a.Row1.Index;
                    int ar2 = wholeColumns ? CellArea.MaxRow : a.Row2.Index;
                    int ac1 = wholeRows ? 1 : a.Column1.Index;
                    int ac2 = wholeRows ? CellArea.MaxColumn : a.Column2.Index;
                    r1 = Math.Min(ar1, ar2);
                    r2 = Math.Max(ar1, ar2);
                    c1 = Math.Min(ac1, ac2);
                    c2 = Math.Max(ac1, ac2);
                    break;
                default:
                    return NotThere(); // external defined names are not evaluated
            }

            int sheet = link.SheetIndex(prefix.Sheet);
            Worksheet? live = LiveSheet(link, prefix.Sheet);
            if (wholeColumns || wholeRows)
            {
                int lastRow, lastColumn;
                if (live is not null)
                {
                    CellRange used = live.UsedRange();
                    lastRow = used.IsValid ? used.BottomRight.Row : 1;
                    lastColumn = used.IsValid ? used.BottomRight.Column : 1;
                }
                else if (sheet >= 0)
                {
                    link.CachedExtent(sheet, out lastRow, out lastColumn);
                }
                else
                {
                    return NotThere();
                }

                r2 = wholeColumns ? Math.Max(r1, Math.Min(r2, lastRow)) : r2;
                c2 = wholeRows ? Math.Max(c1, Math.Min(c2, lastColumn)) : c2;
            }

            int rows = r2 - r1 + 1;
            int columns = c2 - c1 + 1;
            long count = (long)rows * columns;
            if (count > int.MaxValue / 2)
            {
                throw new OfficeLimitException(nameof(OfficeLimits.MaxAllocationBytes), int.MaxValue, count);
            }

            var cells = new CalcValue[(int)count];
            if (live is not null)
            {
                bool recordBlanks = count <= RecordBlanksUpTo;
                for (int r = r1; r <= r2; r++)
                {
                    for (int c = c1; c <= c2; c++)
                    {
                        CalcValue v = live.Store.TryGetCell(r, c, out CellValue cv, out _) ? CalcEngine.ToCalc(live, cv) : CalcValue.Blank;
                        cells[((r - r1) * columns) + (c - c1)] = v;
                        if (sheet >= 0 && (!v.IsBlank || recordBlanks || link.TryGetCached(sheet, r, c, out _)))
                        {
                            link.SetCached(sheet, r, c, v);
                        }
                    }
                }

                Resolved++;
                return single ? cells[0] : CalcValue.FromArray(new CalcArray(rows, columns, cells));
            }

            if (sheet < 0)
            {
                return NotThere();
            }

            bool any = false;
            for (int r = r1; r <= r2; r++)
            {
                for (int c = c1; c <= c2; c++)
                {
                    if (link.TryGetCached(sheet, r, c, out CalcValue v))
                    {
                        cells[((r - r1) * columns) + (c - c1)] = v;
                        any = true;
                    }
                }
            }

            if (!any)
            {
                return NotThere();
            }

            return single ? cells[0] : CalcValue.FromArray(new CalcArray(rows, columns, cells));
        }
    }

    /// <summary>Writes cached values refreshed from live workbooks back into the link parts.</summary>
    public void Flush()
    {
        lock (_gate)
        {
            if (_links is null)
            {
                return;
            }

            foreach (ExternalLink link in _links)
            {
                link.Commit(workbook.Package);
            }
        }
    }

    private CalcValue NotThere()
    {
        Missing++;
        return CalcValue.FromError(CalcError.Ref);
    }

    private Worksheet? LiveSheet(ExternalLink link, string sheetName)
    {
        if (resolver is null)
        {
            return null;
        }

        if (!_live.TryGetValue(link.Index, out Workbook? book))
        {
            try
            {
                book = resolver.Resolve(link);
            }
            catch (Exception e) when (e is OfficeException or IOException)
            {
                workbook.OpenContext.Defects.Report(new Defect(DefectKind.Reference, "calc.external-resolve-failed", DefectSeverity.Repaired,
                    "The resolver could not supply external workbook [" + link.Index + "]; its cached values are used.", DefectLocation.InPart(link.PartName)));
                book = null;
            }

            _live[link.Index] = book;
        }

        return book is not null && !ReferenceEquals(book, workbook) && book.Sheets.TryGet(sheetName, out Worksheet? ws) && ws.Kind == SheetKind.Worksheet ? ws : null;
    }
}
