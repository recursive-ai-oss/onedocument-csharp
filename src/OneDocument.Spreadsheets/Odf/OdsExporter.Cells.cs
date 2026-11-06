using System.Globalization;
using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Calc.OpenFormula;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Markup.Xml;
using RecursiveAI.OneDocument.NumberFormats;
using RecursiveAI.OneDocument.Odf;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Spreadsheets.Odf;

// Tables: columns, rows and cells.
internal sealed partial class OdsExporter
{
    private sealed record Merge(int R1, int C1, int R2, int C2);

    private sealed record ValidationRange(string Name, int R1, int C1, int R2, int C2);

    private sealed class SheetContext
    {
        public readonly List<Merge> Merges = [];
        public readonly List<ValidationRange> Validations = [];
        public readonly Dictionary<long, (string Href, string? Tip)> Links = [];
        public readonly Dictionary<long, (string Author, string Text)> Comments = [];
        public readonly List<Merge> Arrays = [];
        public int TitleRows1;
        public int TitleRows2;
        public int TitleCols1;
        public int TitleCols2;
        public readonly Dictionary<int, (string Text, int DRow, int DCol)> SharedAnchors = [];
    }

    private static long Key(int row, int col) => ((long)row << 20) | (uint)col;

    private static MarkupNode FrameChild(Worksheet ws, string local)
    {
        foreach (MarkupNode c in ws.LayoutMarkup.ChildElements)
        {
            if (c.LocalNameString == local)
            {
                return c;
            }
        }

        return default;
    }

    private void WriteSheet(StringBuilder sb, Worksheet ws, int index)
    {
        var x = new SheetContext();
        foreach (CellRange m in ws.MergedCells)
        {
            x.Merges.Add(new Merge(m.TopLeft.Row, m.TopLeft.Column, m.BottomRight.Row, m.BottomRight.Column));
        }

        x.Merges.Sort(static (a, b) => a.R1 != b.R1 ? a.R1.CompareTo(b.R1) : a.C1.CompareTo(b.C1));
        CollectValidations(ws, x);
        CollectLinks(ws, x);
        CollectComments(ws, x);
        CollectTitles(ws, x, index);
        CollectShared(ws, x);
        ReportSheetLosses(ws);

        MarkupNode viewNode = default;
        MarkupNode views = FrameChild(ws, "sheetViews");
        if (!views.IsNull)
        {
            foreach (MarkupNode v in views.ChildElements)
            {
                viewNode = v;
                break;
            }
        }

        bool rtl = !viewNode.IsNull && viewNode.GetAttribute("rightToLeft") is "1" or "true";
        string? tab = null;
        MarkupNode sheetPr = FrameChild(ws, "sheetPr");
        if (!sheetPr.IsNull)
        {
            foreach (MarkupNode c in sheetPr.ChildElements)
            {
                if (c.LocalNameString == "tabColor" && c.GetAttribute("rgb") is { Length: 8 } rgb)
                {
                    tab = "#" + rgb[2..].ToLowerInvariant();
                }
            }
        }

        string? master = WritePageLayout(ws, index);
        string tableStyle = TableStyleOf(ws, master, rtl, tab);

        sb.Append("<table:table table:name=\"").Append(Esc(ws.Name)).Append("\" table:style-name=\"").Append(tableStyle).Append('"');
        if (_printAreas.TryGetValue(ws, out string? areas))
        {
            sb.Append(" table:print-ranges=\"").Append(Esc(areas)).Append('"');
        }

        MarkupNode protection = FrameChild(ws, "sheetProtection");
        if (!protection.IsNull && protection.GetAttribute("sheet") is "1" or "true")
        {
            sb.Append(" table:protected=\"true\"");
            if (protection.GetAttribute("hashValue") is not null || protection.GetAttribute("password") is not null)
            {
                Lost("loss.protection-password", "protection password", "The hash of a protection password cannot be carried to OpenDocument; the sheet is protected without a password.");
            }
        }

        sb.Append('>');
        WriteColumns(sb, ws, x);
        WriteRows(sb, ws, x);
        if (_localNames.TryGetValue(ws, out StringBuilder? local) && local.Length > 0)
        {
            sb.Append("<table:named-expressions>").Append(local).Append("</table:named-expressions>");
        }

        WriteConditionalFormats(sb, ws);
        WriteAutoFilter(ws);
        sb.Append("</table:table>");
    }

    private void ReportSheetLosses(Worksheet ws)
    {
        foreach (string local in new[] { "drawing", "legacyDrawingHF", "picture", "oleObjects", "controls", "tableParts" })
        {
            if (!FrameChild(ws, local).IsNull)
            {
                Lost(local == "tableParts" ? "loss.tables" : "loss.drawings", local == "tableParts" ? "tables" : "pictures, charts and shapes", local == "tableParts" ? "Tables keep their cells and styles; the table definition is not written." : "Pictures, charts, shapes and embedded objects on sheets are not written.");
            }
        }
    }

    // ---- columns ------------------------------------------------------------------------------------------------------------------------

    private void WriteColumns(StringBuilder sb, Worksheet ws, SheetContext x)
    {
        double defaultChars = 8.43;
        MarkupNode fmt = FrameChild(ws, "sheetFormatPr");
        if (!fmt.IsNull && double.TryParse(fmt.GetAttribute("defaultColWidth"), NumberStyles.Float, CultureInfo.InvariantCulture, out double dw) && dw > 0)
        {
            defaultChars = dw;
        }

        string defaultColumn = ColumnStyleOf(WidthPoints(defaultChars));
        CellRange used = ws.UsedRange();
        int lastColumn = used.IsValid ? used.BottomRight.Column : 1;
        foreach (Merge m in x.Merges)
        {
            lastColumn = Math.Max(lastColumn, m.C2);
        }

        IReadOnlyList<ColumnProperties> columns = ws.Columns;
        foreach (ColumnProperties cp in columns)
        {
            lastColumn = Math.Max(lastColumn, Math.Min(cp.Max, Math.Max(lastColumn, cp.Min)));
        }

        lastColumn = Math.Min(lastColumn, CellAddress.MaxColumns);
        bool headerOpen = false;
        int next = 1;
        void Gap(int upTo)
        {
            if (upTo >= next)
            {
                int count = upTo - next + 1;
                OpenHeader(next, upTo, ref headerOpen, x, sb);
                sb.Append("<table:table-column table:style-name=\"").Append(defaultColumn).Append("\" table:default-cell-style-name=\"Default\"");
                if (count > 1)
                {
                    sb.Append(" table:number-columns-repeated=\"").Append(count).Append('"');
                }

                sb.Append("/>");
                next = upTo + 1;
            }
        }

        foreach (ColumnProperties cp in columns.OrderBy(static c => c.Min))
        {
            int min = Math.Max(cp.Min, next);
            int max = Math.Min(cp.Max, CellAddress.MaxColumns);
            if (min > max)
            {
                continue;
            }

            Gap(min - 1);
            double chars = cp.Width > 0 ? cp.Width : defaultChars;
            string style = ColumnStyleOf(WidthPoints(chars));
            OpenHeader(min, max, ref headerOpen, x, sb);
            sb.Append("<table:table-column table:style-name=\"").Append(style).Append('"');
            if ((cp.Flags & ColumnOptions.Hidden) != 0)
            {
                sb.Append(" table:visibility=\"collapse\"");
            }

            sb.Append(" table:default-cell-style-name=\"").Append(cp.Style == 0 ? "Default" : CellStyleOf(cp.Style)).Append('"');
            int count = max - min + 1;
            if (count > 1)
            {
                sb.Append(" table:number-columns-repeated=\"").Append(count).Append('"');
            }

            sb.Append("/>");
            next = max + 1;
        }

        Gap(lastColumn);
        if (headerOpen)
        {
            sb.Append("</table:table-header-columns>");
        }
    }

    // Columns named by the print titles sit inside table:table-header-columns.
    private static void OpenHeader(int first, int last, ref bool open, SheetContext x, StringBuilder sb)
    {
        if (x.TitleCols1 == 0)
        {
            return;
        }

        bool inside = first >= x.TitleCols1 && last <= x.TitleCols2;
        if (inside && !open)
        {
            sb.Append("<table:table-header-columns>");
            open = true;
        }
        else if (!inside && open)
        {
            sb.Append("</table:table-header-columns>");
            open = false;
        }
    }

    // ---- rows -----------------------------------------------------------------------------------------------------------------------------

    private void WriteRows(StringBuilder sb, Worksheet ws, SheetContext x)
    {
        // Rows where something starts or ends: a row with cells or properties, the edges of merges and validations, the print titles.
        var points = new SortedSet<int> { 1 };
        var storeRows = new Dictionary<int, CellRowView>();
        foreach (CellRowView row in ws.Store.EnumerateRows())
        {
            storeRows[row.Row] = row;
            points.Add(row.Row);
            points.Add(row.Row + 1);
        }

        foreach (Merge m in x.Merges)
        {
            // The anchor row differs from the rows below it, which are covered.
            points.Add(m.R1);
            points.Add(m.R1 + 1);
            points.Add(m.R2 + 1);
        }

        foreach (ValidationRange v in x.Validations)
        {
            points.Add(v.R1);
            points.Add(v.R2 + 1);
        }

        if (x.TitleRows1 > 0)
        {
            points.Add(x.TitleRows1);
            points.Add(x.TitleRows2 + 1);
        }

        int last = 0;
        foreach (int r in storeRows.Keys)
        {
            last = Math.Max(last, r);
        }

        foreach (Merge m in x.Merges)
        {
            last = Math.Max(last, m.R2);
        }

        foreach (ValidationRange v in x.Validations)
        {
            last = Math.Max(last, Math.Min(v.R2, CellAddress.MaxRows));
        }

        if (x.TitleRows1 > 0)
        {
            last = Math.Max(last, x.TitleRows2);
        }

        if (last == 0)
        {
            sb.Append("<table:table-row><table:table-cell/></table:table-row>");
            return;
        }

        var ordered = points.Where(p => p <= last).ToList();
        var activeMerges = new List<Merge>();
        var activeValidations = new List<ValidationRange>();
        int nextMerge = 0;
        int nextValidation = 0;
        List<ValidationRange> validationsByStart = [.. x.Validations.OrderBy(static v => v.R1)];
        bool headerOpen = false;
        for (int i = 0; i < ordered.Count; i++)
        {
            _ct.ThrowIfCancellationRequested();
            int r = ordered[i];
            int end = i + 1 < ordered.Count ? ordered[i + 1] - 1 : last;
            while (nextMerge < x.Merges.Count && x.Merges[nextMerge].R1 <= r)
            {
                activeMerges.Add(x.Merges[nextMerge++]);
            }

            activeMerges.RemoveAll(m => m.R2 < r);
            while (nextValidation < validationsByStart.Count && validationsByStart[nextValidation].R1 <= r)
            {
                activeValidations.Add(validationsByStart[nextValidation++]);
            }

            activeValidations.RemoveAll(v => v.R2 < r);

            bool inTitles = x.TitleRows1 > 0 && r >= x.TitleRows1 && r <= x.TitleRows2;
            if (inTitles && !headerOpen)
            {
                sb.Append("<table:table-header-rows>");
                headerOpen = true;
            }
            else if (!inTitles && headerOpen)
            {
                sb.Append("</table:table-header-rows>");
                headerOpen = false;
            }

            if (storeRows.TryGetValue(r, out CellRowView view))
            {
                WriteRow(sb, ws, x, r, 1, view, true, activeMerges, activeValidations);
                if (end > r)
                {
                    WriteRow(sb, ws, x, r + 1, end - r, default, false, activeMerges, activeValidations);
                }
            }
            else
            {
                WriteRow(sb, ws, x, r, end - r + 1, default, false, activeMerges, activeValidations);
            }
        }

        if (headerOpen)
        {
            sb.Append("</table:table-header-rows>");
        }
    }

    private void WriteRow(StringBuilder sb, Worksheet ws, SheetContext x, int r, int repeat, CellRowView view, bool hasView, List<Merge> merges, List<ValidationRange> validations)
    {
        RowProperties props = ws.GetRow(r);
        double height = props.Height;
        bool hidden = props.Hidden;
        sb.Append("<table:table-row");
        if (height > 0)
        {
            sb.Append(" table:style-name=\"").Append(RowStyleOf(height, optimal: false)).Append('"');
        }
        else
        {
            sb.Append(" table:style-name=\"").Append(RowStyleOf(0, optimal: true)).Append('"');
        }

        if (hidden)
        {
            sb.Append(" table:visibility=\"collapse\"");
        }

        if (repeat > 1)
        {
            sb.Append(" table:number-rows-repeated=\"").Append(repeat).Append('"');
        }

        sb.Append('>');
        int before = sb.Length;
        WriteRowCells(sb, ws, x, r, view, hasView, merges, validations);
        if (sb.Length == before)
        {
            sb.Append("<table:table-cell/>");
        }

        sb.Append("</table:table-row>");
    }

    private void WriteRowCells(StringBuilder sb, Worksheet ws, SheetContext x, int r, CellRowView view, bool hasView, List<Merge> merges, List<ValidationRange> validations)
    {
        int n = hasView ? view.Count : 0;
        int lastCol = n > 0 ? view.ColumnAt(n - 1) : 0;
        foreach (Merge m in merges)
        {
            lastCol = Math.Max(lastCol, m.C2);
        }

        foreach (ValidationRange v in validations)
        {
            lastCol = Math.Max(lastCol, v.C2);
        }

        lastCol = Math.Min(lastCol, CellAddress.MaxColumns);
        int i = 0;
        int c = 1;
        while (c <= lastCol)
        {
            int present = i < n ? view.ColumnAt(i) : int.MaxValue;
            Merge? cover = null;
            foreach (Merge m in merges)
            {
                if (c >= m.C1 && c <= m.C2)
                {
                    cover = m;
                    break;
                }
            }

            string? validation = null;
            foreach (ValidationRange v in validations)
            {
                if (c >= v.C1 && c <= v.C2)
                {
                    validation = v.Name;
                    break;
                }
            }

            bool anchor = cover is not null && r == cover.R1 && c == cover.C1;
            bool covered = cover is not null && !anchor;
            if (present == c)
            {
                WriteCell(sb, ws, x, r, c, view, i, anchor ? cover : null, covered, validation);
                i++;
                c++;
                continue;
            }

            int next = Math.Min(present, lastCol + 1);
            foreach (Merge m in merges)
            {
                if (m.C1 > c)
                {
                    next = Math.Min(next, m.C1);
                }
                else if (c <= m.C2)
                {
                    next = Math.Min(next, m.C2 + 1);
                }
            }

            foreach (ValidationRange v in validations)
            {
                if (v.C1 > c)
                {
                    next = Math.Min(next, v.C1);
                }
                else if (c <= v.C2)
                {
                    next = Math.Min(next, v.C2 + 1);
                }
            }

            if (anchor)
            {
                next = c + 1;
            }

            int run = Math.Max(1, next - c);
            string tag = covered ? "table:covered-table-cell" : "table:table-cell";
            sb.Append('<').Append(tag);
            if (anchor)
            {
                AppendSpans(sb, cover!);
            }

            if (validation is not null && !covered)
            {
                sb.Append(" table:content-validation-name=\"").Append(validation).Append('"');
            }

            if (run > 1)
            {
                sb.Append(" table:number-columns-repeated=\"").Append(run).Append('"');
            }

            sb.Append("/>");
            c += run;
        }
    }

    private static void AppendSpans(StringBuilder sb, Merge m) =>
        sb.Append(" table:number-columns-spanned=\"").Append(m.C2 - m.C1 + 1).Append("\" table:number-rows-spanned=\"").Append(m.R2 - m.R1 + 1).Append('"');

    // ---- cells ------------------------------------------------------------------------------------------------------------------------------

    private readonly Dictionary<int, NumberFormat> _formats = [];

    private NumberFormat FormatOf(int xf)
    {
        if (!_formats.TryGetValue(xf, out NumberFormat? f))
        {
            f = _wb.Styles.GetNumberFormat(xf);
            _formats[xf] = f;
        }

        return f;
    }

    private static void CollectShared(Worksheet ws, SheetContext x)
    {
        foreach (CellRowView row in ws.Store.EnumerateRows())
        {
            for (int i = 0; i < row.Count; i++)
            {
                CellFormula? f = row.FormulaAt(i);
                if (f is { Kind: CellFormulaKind.Shared, Text.Length: > 0, SharedIndex: >= 0 })
                {
                    x.SharedAnchors[f.SharedIndex] = (f.Text, row.Row, row.ColumnAt(i));
                }
                else if (f is { Kind: CellFormulaKind.Array, Reference: { } reference } && CellRange.TryParse(reference, out CellRange range))
                {
                    x.Arrays.Add(new Merge(range.TopLeft.Row, range.TopLeft.Column, range.BottomRight.Row, range.BottomRight.Column));
                }
            }
        }
    }

    private static bool InArray(SheetContext x, int row, int col, out Merge? master)
    {
        foreach (Merge a in x.Arrays)
        {
            if (row >= a.R1 && row <= a.R2 && col >= a.C1 && col <= a.C2)
            {
                master = a;
                return true;
            }
        }

        master = null;
        return false;
    }

    private void WriteCell(StringBuilder sb, Worksheet ws, SheetContext x, int r, int c, CellRowView view, int i, Merge? anchor, bool coveredByMerge, string? validation)
    {
        CellValue value = view.ValueAt(i);
        int xf = view.StyleAt(i);
        CellFormula? formula = view.FormulaAt(i);
        bool inArray = InArray(x, r, c, out Merge? arrayArea);
        bool arrayMaster = inArray && arrayArea!.R1 == r && arrayArea.C1 == c;

        if (coveredByMerge)
        {
            if (value.Kind != CellValueKind.Empty)
            {
                Lost("loss.merged-cell-values", "values hidden under merged cells", "A value in a cell covered by a merged cell is not written.");
            }

            sb.Append("<table:covered-table-cell/>");
            return;
        }

        bool covered = inArray && !arrayMaster && formula is null;
        string tag = covered ? "table:covered-table-cell" : "table:table-cell";
        sb.Append('<').Append(tag);
        if (!covered)
        {
            sb.Append(" table:style-name=\"").Append(xf == 0 ? "Default" : CellStyleOf(xf)).Append('"');
        }

        string? text = null;
        if (formula is not null && !covered)
        {
            text = OpenFormulaText(ws, r, c, formula, x);
            if (text is not null)
            {
                sb.Append(" table:formula=\"of:=").Append(Esc(text)).Append('"');
            }
        }

        if (anchor is not null)
        {
            AppendSpans(sb, anchor);
        }

        if (arrayMaster && text is not null)
        {
            sb.Append(" table:number-matrix-columns-spanned=\"").Append(arrayArea!.C2 - arrayArea.C1 + 1).Append("\" table:number-matrix-rows-spanned=\"").Append(arrayArea.R2 - arrayArea.R1 + 1).Append('"');
        }

        if (validation is not null && !covered)
        {
            sb.Append(" table:content-validation-name=\"").Append(validation).Append('"');
        }

        string display = string.Empty;
        NumberFormat format = FormatOf(xf);
        switch (value.Kind)
        {
            case CellValueKind.Number:
                double number = value.Number;
                display = ws[r, c].FormattedText;
                AppendNumber(sb, number, format, display);
                break;
            case CellValueKind.Boolean:
                sb.Append(" office:value-type=\"boolean\" office:boolean-value=\"").Append(value.Boolean ? "true" : "false").Append("\" calcext:value-type=\"boolean\"");
                display = value.Boolean ? "TRUE" : "FALSE";
                break;
            case CellValueKind.Text:
                display = ws.GetText(value);
                if (value.StringRef >= 0 && value.StringRef < _wb.SharedStrings.Count && _wb.SharedStrings.IsRich(value.StringRef))
                {
                    Lost("loss.rich-text", "rich text in cells", "Formatting of parts of a cell's text is not written.");
                }

                sb.Append(" office:value-type=\"string\"");
                if (text is not null)
                {
                    sb.Append(" office:string-value=\"").Append(Esc(display)).Append('"');
                }

                sb.Append(" calcext:value-type=\"string\"");
                break;
            case CellValueKind.Error:
                display = ws.GetText(value);
                sb.Append(" office:value-type=\"string\" office:string-value=\"\" calcext:value-type=\"error\"");
                break;
            default:
                break;
        }

        bool hasHref = x.Links.TryGetValue(Key(r, c), out (string Href, string? Tip) link);
        bool hasNote = x.Comments.TryGetValue(Key(r, c), out (string Author, string Text) note);
        if (value.Kind == CellValueKind.Empty && !hasHref && !hasNote)
        {
            sb.Append("/>");
            return;
        }

        sb.Append('>');
        if (hasNote)
        {
            sb.Append("<office:annotation office:display=\"false\"><dc:creator>").Append(Esc(note.Author)).Append("</dc:creator>").Append(Paragraphs(note.Text)).Append("</office:annotation>");
        }

        if (value.Kind != CellValueKind.Empty || hasHref)
        {
            if (hasHref)
            {
                sb.Append("<text:p><text:a xlink:href=\"").Append(Esc(link.Href)).Append("\" xlink:type=\"simple\">").Append(Inline(display.Length > 0 ? display : link.Href)).Append("</text:a></text:p>");
            }
            else
            {
                sb.Append(Paragraphs(display));
            }
        }

        sb.Append("</").Append(tag).Append('>');
    }

    private void AppendNumber(StringBuilder sb, double number, NumberFormat format, string display)
    {
        NumberFormatCategory category = format.Category;
        if (category is NumberFormatCategory.Date or NumberFormatCategory.DateTime && number >= 0 && ExcelDate.TryFromSerial(number, _wb.DateSystem, out ExcelDateTime dt))
        {
            bool hasTime = dt.Hour != 0 || dt.Minute != 0 || dt.Second != 0 || dt.Millisecond != 0;
            string iso = dt.Year.ToString("0000", CultureInfo.InvariantCulture) + "-" + dt.Month.ToString("00", CultureInfo.InvariantCulture) + "-" + dt.Day.ToString("00", CultureInfo.InvariantCulture);
            if (hasTime)
            {
                iso += "T" + dt.Hour.ToString("00", CultureInfo.InvariantCulture) + ":" + dt.Minute.ToString("00", CultureInfo.InvariantCulture) + ":" + dt.Second.ToString("00", CultureInfo.InvariantCulture);
                if (dt.Millisecond != 0)
                {
                    iso += "." + dt.Millisecond.ToString("000", CultureInfo.InvariantCulture);
                }
            }

            sb.Append(" office:value-type=\"date\" office:date-value=\"").Append(iso).Append("\" calcext:value-type=\"date\"");
            return;
        }

        if (category == NumberFormatCategory.Time && number >= 0 && number < 1_000_000)
        {
            long ms = (long)Math.Round(number * 86_400_000);
            long hours = ms / 3_600_000;
            long minutes = ms / 60_000 % 60;
            double seconds = ms % 60_000 / 1000.0;
            sb.Append(" office:value-type=\"time\" office:time-value=\"PT").Append(hours.ToString("00", CultureInfo.InvariantCulture)).Append('H').Append(minutes.ToString("00", CultureInfo.InvariantCulture)).Append('M')
                .Append(seconds.ToString("00.###", CultureInfo.InvariantCulture)).Append("S\" calcext:value-type=\"time\"");
            return;
        }

        string type = category == NumberFormatCategory.Percentage ? "percentage" : "float";
        sb.Append(" office:value-type=\"").Append(type).Append("\" office:value=\"").Append(Inv(number)).Append("\" calcext:value-type=\"").Append(type).Append('"');
        _ = display;
    }

    // The formula as OpenFormula text, or null when OpenFormula cannot say it (the cell keeps its cached value).
    private string? OpenFormulaText(Worksheet ws, int row, int col, CellFormula f, SheetContext x)
    {
        if (f.Kind == CellFormulaKind.DataTable)
        {
            Lost("loss.formulas", "formulas", "Formulas that OpenDocument cannot express (data tables, structured references, lambdas, spills) keep their cached values.");
            return null;
        }

        string text = f.Text;
        int dr = 0;
        int dc = 0;
        if (f.Kind == CellFormulaKind.Shared && text.Length == 0)
        {
            if (!x.SharedAnchors.TryGetValue(f.SharedIndex, out (string Text, int DRow, int DCol) a))
            {
                Lost("loss.formulas", "formulas", "A shared formula without an anchor keeps its cached value.");
                return null;
            }

            text = a.Text;
            dr = row - a.DRow;
            dc = col - a.DCol;
        }

        if (text.Length == 0)
        {
            return null;
        }

        FormulaNode tree;
        try
        {
            tree = FormulaParser.Parse(text, new FormulaParserOptions { Limits = _ctx.Limits });
            if (dr != 0 || dc != 0)
            {
                tree = FormulaTranslator.Translate(tree, dr, dc);
            }
        }
        catch (Exception ex) when (ex is OfficeException or FormatException or ArgumentException or InvalidOperationException)
        {
            _ctx.Defects.Report(new Defect(DefectKind.Formula, "ods.bad-formula", DefectSeverity.Dropped, "The formula '" + (text.Length > 80 ? text[..80] : text) + "' cannot be read; its cached value is written.", DefectLocation.InCell(ws.Name, CellAddress.ColumnName(col) + I(row))));
            Lost("loss.formulas", "formulas", "Formulas that OpenDocument cannot express (data tables, structured references, lambdas, spills) keep their cached values.");
            return null;
        }

        var problems = new List<OpenFormulaProblem>();
        string printed = OpenFormulaPrinter.Print(tree, problems);
        bool faithful = true;
        foreach (OpenFormulaProblem p in problems)
        {
            if (!p.Portable)
            {
                faithful = false;
            }
            else if (p.Code == "openformula.function")
            {
                Lost("loss.function-not-portable", "functions other readers do not know", "A formula uses a function that LibreOffice does not know (" + p.Detail + "); it is written, and other readers show #NAME? after recalculating.");
            }
        }

        if (!faithful)
        {
            Lost("loss.formulas", "formulas", "Formulas that OpenDocument cannot express (data tables, structured references, lambdas, spills) keep their cached values.");
            return null;
        }

        return printed;
    }

    // ---- text ------------------------------------------------------------------------------------------------------------------------------------

    // OpenDocument 1.3 Part 1 §6.1: a paragraph holds text, with runs of spaces as text:s, tabs as text:tab and line breaks as text:line-break.
    private static string Inline(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        int i = 0;
        while (i < s.Length)
        {
            char ch = s[i];
            switch (ch)
            {
                case ' ':
                    int run = 1;
                    while (i + run < s.Length && s[i + run] == ' ')
                    {
                        run++;
                    }

                    if (run == 1 && i > 0 && i + 1 < s.Length)
                    {
                        sb.Append(' ');
                    }
                    else
                    {
                        sb.Append("<text:s text:c=\"").Append(run).Append("\"/>");
                    }

                    i += run;
                    continue;
                case '\t':
                    sb.Append("<text:tab/>");
                    break;
                case '\n':
                    sb.Append("<text:line-break/>");
                    break;
                case '\r':
                    break;
                case '&':
                    sb.Append("&amp;");
                    break;
                case '<':
                    sb.Append("&lt;");
                    break;
                case '>':
                    sb.Append("&gt;");
                    break;
                default:
                    if (ch >= ' ' || ch == '\u0009')
                    {
                        sb.Append(ch);
                    }

                    break;
            }

            i++;
        }

        return sb.ToString();
    }

    private static string Paragraphs(string text)
    {
        if (text.Length == 0)
        {
            return "<text:p/>";
        }

        var sb = new StringBuilder();
        foreach (string line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            sb.Append("<text:p>").Append(Inline(line)).Append("</text:p>");
        }

        return sb.ToString();
    }
}
