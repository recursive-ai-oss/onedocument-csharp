using System.Globalization;
using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.NumberFormats;
using RecursiveAI.OneDocument.Spreadsheets.Csv;
using RecursiveAI.OneDocument.TextExtraction;

namespace RecursiveAI.OneDocument.Spreadsheets.Markdown;

/// <summary>
/// Writes a workbook as GitHub Flavored Markdown: each worksheet is a second-level heading with its name and a pipe table of the used range, cells shown as
/// their number format displays them (the same text the CSV writer produces). The first non-empty row is the table header. A column whose cells are all numbers is
/// right-aligned. Rows with no cell are left out, since a blank line would end the table.
/// </summary>
internal static class MarkdownWorkbook
{
    public static void Write(Workbook workbook, Stream destination, ILossSink? loss, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        var sb = new StringBuilder();
        int sheets = 0;
        long formulas = 0;
        long styled = 0;
        long merged = 0;
        long hidden = 0;
        long skippedKinds = 0;
        foreach (Worksheet sheet in workbook.Sheets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sheet.Kind != SheetKind.Worksheet)
            {
                skippedKinds++;
                continue;
            }

            if (sheet.State != SheetState.Visible)
            {
                hidden++;
            }

            if (sheets++ > 0)
            {
                sb.Append('\n');
            }

            sb.Append("## ").Append(MarkdownWriting.EscapeText(sheet.Name, inTable: false, lineStart: false).Replace('\n', ' ')).Append("\n\n");
            WriteSheet(sheet, sb, ref formulas, ref styled, cancellationToken);
            merged += sheet.MergedCells.Count;
        }

        if (loss is not null)
        {
            if (formulas > 0)
            {
                loss.Report("loss.formulas", "formulas", formulas, "Markdown holds text; formula cells are written as their cached results.");
            }

            if (styled > 0)
            {
                loss.Report("loss.cell-styles", "cell styles", styled, "Fonts, fills, borders and alignment are not part of the tables; numbers are shown as their number format displays them.");
            }

            if (merged > 0)
            {
                loss.Report("loss.merged-cells", "merged cells", merged, "Only the top left cell of a merged range holds a value in the table.");
            }

            if (hidden > 0)
            {
                loss.Report("loss.hidden-sheets", "hidden sheets", hidden, "Hidden sheets are written like the others.");
            }

            if (skippedKinds > 0)
            {
                loss.Report("loss.sheet-kinds", "chart and macro sheets", skippedKinds, "Sheets that are not worksheets have no table and are left out.");
            }

            long objects = 0;
            foreach (Packaging.IPackagePart part in workbook.Package.Parts)
            {
                string t = part.ContentType;
                if (t.Contains("drawing", StringComparison.Ordinal) || t.Contains("chart", StringComparison.Ordinal) || t.Contains("spreadsheetml.table+", StringComparison.Ordinal)
                    || t.Contains("spreadsheetml.comments", StringComparison.Ordinal) || t.Contains("pivot", StringComparison.Ordinal))
                {
                    objects++;
                }
            }

            if (objects > 0)
            {
                loss.Report("loss.embedded-objects", "drawings, charts, tables, comments and pivot tables", objects, "Markdown tables hold cell text only; these parts are not written.");
            }
        }

        destination.Write(new UTF8Encoding(false).GetBytes(sb.ToString()));
    }

    private static void WriteSheet(Worksheet sheet, StringBuilder sb, ref long formulas, ref long styled, CancellationToken cancellationToken)
    {
        CellStore store = sheet.Store;
        if (store.CellCount == 0)
        {
            sb.Append("(empty)\n");
            return;
        }

        // First pass: how wide the table is, and which columns hold only numbers.
        int maxCol = 0;
        var numeric = new Dictionary<int, bool>();
        bool firstRow = true;
        foreach (CellRowView row in store.EnumerateRows())
        {
            for (int i = 0; i < row.Count; i++)
            {
                int c = row.ColumnAt(i);
                maxCol = Math.Max(maxCol, c);
                CellValue v = row.ValueAt(i);
                if (!firstRow && v.Kind != CellValueKind.Empty)
                {
                    numeric[c] = (!numeric.TryGetValue(c, out bool all) || all) && v.Kind == CellValueKind.Number;
                }
            }

            firstRow = false;
        }

        if (maxCol == 0)
        {
            sb.Append("(empty)\n");
            return;
        }

        var formats = new StyleFormats(sheet.Workbook);
        DateSystem dateSystem = StyleFormats.IsDate1904(sheet.Workbook) ? DateSystem.Date1904 : DateSystem.Date1900;
        var formatOptions = new FormatOptions(dateSystem, null, 20, 0);
        var cells = new string[maxCol];
        bool header = true;
        foreach (CellRowView row in store.EnumerateRows())
        {
            cancellationToken.ThrowIfCancellationRequested();
            Array.Fill(cells, string.Empty);
            for (int i = 0; i < row.Count; i++)
            {
                int c = row.ColumnAt(i);
                int style = row.StyleAt(i);
                if (style != 0)
                {
                    styled++;
                }

                if (row.HasFormulaAt(i))
                {
                    formulas++;
                }

                cells[c - 1] = CellText(sheet, row.ValueAt(i), style, formats, formatOptions);
            }

            if (cells.All(static s => s.Length == 0))
            {
                continue;
            }

            sb.Append('|');
            foreach (string c in cells)
            {
                sb.Append(' ').Append(c.Length == 0 ? " " : c).Append(" |");
            }

            sb.Append('\n');
            if (header)
            {
                header = false;
                sb.Append('|');
                for (int c = 1; c <= maxCol; c++)
                {
                    sb.Append(numeric.TryGetValue(c, out bool allNumbers) && allNumbers ? " ---: |" : " --- |");
                }

                sb.Append('\n');
            }
        }
    }

    // The text of one cell as the table shows it: numbers by their number format, text and errors as they are, line ends as <br>.
    private static string CellText(Worksheet sheet, CellValue v, int style, StyleFormats formats, FormatOptions formatOptions)
    {
        string text;
        switch (v.Kind)
        {
            case CellValueKind.Empty:
                return string.Empty;
            case CellValueKind.Text or CellValueKind.Error:
                text = sheet.GetText(v);
                break;
            case CellValueKind.Boolean:
                return v.Boolean ? "TRUE" : "FALSE";
            default:
                {
                    NumberFormat nf = formats.Get(style) ?? NumberFormat.General;
                    return MarkdownWriting.EscapeText(nf.Format(v.Number, formatOptions), inTable: true, lineStart: false);
                }
        }

        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        return string.Join("<br>", lines.Select(static l => MarkdownWriting.EscapeText(l, inTable: true, lineStart: false)));
    }
}
