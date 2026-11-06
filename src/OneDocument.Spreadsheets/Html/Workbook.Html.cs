using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.NumberFormats;

namespace RecursiveAI.OneDocument.Spreadsheets;

public sealed partial class Workbook
{
    /// <summary>
    /// Writes the workbook as an HTML5 page with one table per visible worksheet. Cells show what Excel shows: numbers and dates go through the
    /// cell's number format (the shared number-format service), text as it is, booleans and errors as their text. Merged cells become
    /// <c>colspan</c> and <c>rowspan</c>. Fonts, fills, borders, column widths, formulas (their cached results are written), charts, pictures and
    /// comments are not written and are listed in <see cref="ExportLoss"/>.
    /// </summary>
    /// <param name="options">A fragment instead of a page; null for a page.</param>
    /// <param name="cancellationToken">Cancels the export.</param>
    /// <returns>The HTML.</returns>
    public string ToHtml(HtmlExportOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        WriteHtml(buffer, options, cancellationToken);
        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    /// <summary>Writes the workbook as HTML to a stream; see <see cref="ToHtml(HtmlExportOptions?, CancellationToken)"/>.</summary>
    /// <param name="destination">The stream.</param>
    /// <param name="options">A fragment instead of a page; null for a page.</param>
    /// <param name="cancellationToken">Cancels the export.</param>
    public void SaveAsHtml(Stream destination, HtmlExportOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        WriteHtml(destination, options, cancellationToken);
    }

    internal void WriteHtml(Stream destination, HtmlExportOptions? options, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        CommitChanges();
        var loss = new LossReport();
        var sb = new StringBuilder();
        bool fragment = options?.Fragment ?? false;
        if (!fragment)
        {
            sb.Append("<!DOCTYPE html>\n<html>\n<head>\n<meta charset=\"utf-8\">\n<title></title>\n<style>\ntable{border-collapse:collapse}\ntd,th{border:1px solid #bbb;padding:2px 6px;vertical-align:top}\ntd.n{text-align:right}\n</style>\n</head>\n<body>\n");
        }

        var formats = new Csv.StyleFormats(this);
        DateSystem dateSystem = Csv.StyleFormats.IsDate1904(this) ? DateSystem.Date1904 : DateSystem.Date1900;
        var formatOptions = new FormatOptions(dateSystem, null, 20, 0);
        long hidden = 0;
        long formulas = 0;
        long styled = 0;
        long cells = 0;
        foreach (Worksheet sheet in Sheets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sheet.Kind != SheetKind.Worksheet)
            {
                loss.Report("loss.html-chart-sheets", "chart and dialog sheets", 1, "Only worksheets are written.");
                continue;
            }

            if (sheet.State != SheetState.Visible)
            {
                hidden++;
                continue;
            }

            sb.Append("<section class=\"sheet\">\n<h2>").Append(Esc(sheet.Name)).Append("</h2>\n");
            WriteSheet(sheet, sb, formats, formatOptions, ref formulas, ref styled, ref cells, cancellationToken);
            sb.Append("</section>\n");
        }

        if (!fragment)
        {
            sb.Append("</body>\n</html>\n");
        }

        if (hidden > 0)
        {
            loss.Report("loss.html-hidden-sheets", "hidden sheets", hidden, "Hidden sheets are not written.");
        }

        if (formulas > 0)
        {
            loss.Report("loss.formulas", "formulas", formulas, "HTML holds values; formula cells are written as their cached results.");
        }

        if (styled > 0)
        {
            loss.Report("loss.cell-styles", "cell styles", styled, "Fonts, fills, borders and alignment are not written; numbers are right-aligned and shown through their number format.");
        }

        long objects = 0;
        foreach (Packaging.IPackagePart part in Package.Parts)
        {
            string t = part.ContentType;
            if (t.Contains("drawing", StringComparison.Ordinal) || t.Contains("chart", StringComparison.Ordinal) || t.Contains("spreadsheetml.comments", StringComparison.Ordinal) || t.Contains("pivot", StringComparison.Ordinal))
            {
                objects++;
            }
        }

        if (objects > 0)
        {
            loss.Report("loss.embedded-objects", "drawings, charts, comments and pivot tables", objects, "These parts are not written.");
        }

        destination.Write(Encoding.UTF8.GetBytes(sb.ToString()));
        ExportLoss = loss;
    }

    private void WriteSheet(Worksheet sheet, StringBuilder sb, Csv.StyleFormats formats, FormatOptions formatOptions, ref long formulas, ref long styled, ref long cellCount, CancellationToken ct)
    {
        CellStore store = sheet.Store;
        if (store.CellCount == 0)
        {
            sb.Append("<table></table>\n");
            return;
        }

        // A merged range is written at its top left cell; the cells it covers are skipped.
        var spans = new Dictionary<(int Row, int Col), (int Rows, int Cols)>();
        var covered = new HashSet<(int Row, int Col)>();
        foreach (CellRange m in sheet.MergedCells)
        {
            int rows = m.BottomRight.Row - m.TopLeft.Row + 1;
            int cols = m.BottomRight.Column - m.TopLeft.Column + 1;
            if (rows * cols <= 1 || rows * cols > 100_000)
            {
                continue;
            }

            spans[(m.TopLeft.Row, m.TopLeft.Column)] = (rows, cols);
            for (int r = m.TopLeft.Row; r <= m.BottomRight.Row; r++)
            {
                for (int c = m.TopLeft.Column; c <= m.BottomRight.Column; c++)
                {
                    if (r != m.TopLeft.Row || c != m.TopLeft.Column)
                    {
                        covered.Add((r, c));
                    }
                }
            }
        }

        sb.Append("<table>\n");
        int nextRow = 1;
        foreach (CellRowView row in store.EnumerateRows())
        {
            ct.ThrowIfCancellationRequested();
            OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxRecordCount), Limits.MaxRecordCount, cellCount += row.Count, Diagnostics.DefectLocation.None);
            for (; nextRow < row.Row; nextRow++)
            {
                sb.Append("<tr></tr>\n");
            }

            sb.Append("<tr>");
            int col = 1;
            for (int i = 0; i < row.Count; i++)
            {
                int c = row.ColumnAt(i);
                for (; col < c; col++)
                {
                    if (!covered.Contains((row.Row, col)))
                    {
                        sb.Append("<td></td>");
                    }
                }

                if (!covered.Contains((row.Row, c)))
                {
                    CellValue v = row.ValueAt(i);
                    int style = row.StyleAt(i);
                    if (style != 0)
                    {
                        styled++;
                    }

                    if (row.HasFormulaAt(i))
                    {
                        formulas++;
                    }

                    sb.Append("<td");
                    if (spans.TryGetValue((row.Row, c), out (int Rows, int Cols) span))
                    {
                        if (span.Cols > 1)
                        {
                            sb.Append(" colspan=\"").Append(span.Cols.ToString(CultureInfo.InvariantCulture)).Append('"');
                        }

                        if (span.Rows > 1)
                        {
                            sb.Append(" rowspan=\"").Append(span.Rows.ToString(CultureInfo.InvariantCulture)).Append('"');
                        }
                    }

                    string text = DisplayText(sheet, v, style, formats, formatOptions, out bool numeric);
                    sb.Append(numeric ? " class=\"n\">" : ">").Append(Esc(text)).Append("</td>");
                }

                col = c + 1;
            }

            sb.Append("</tr>\n");
            nextRow = row.Row + 1;
        }

        sb.Append("</table>\n");
    }

    private static string DisplayText(Worksheet sheet, CellValue v, int style, Csv.StyleFormats formats, FormatOptions formatOptions, out bool numeric)
    {
        numeric = false;
        switch (v.Kind)
        {
            case CellValueKind.Empty:
                return string.Empty;
            case CellValueKind.Text or CellValueKind.Error:
                return sheet.GetText(v);
            case CellValueKind.Boolean:
                return v.Boolean ? "TRUE" : "FALSE";
            default:
                {
                    numeric = true;
                    NumberFormat? nf = formats.Get(style);
                    return (nf ?? NumberFormat.General).Format(v.Number, formatOptions);
                }
        }
    }

    private static string Esc(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                default:
                    if (c >= ' ' || c is '\t' or '\n' or '\r')
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        return sb.ToString();
    }
}
