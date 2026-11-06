using System.Globalization;
using RecursiveAI.OneDocument.Accessibility;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Properties;

namespace RecursiveAI.OneDocument.Spreadsheets;

/// <summary>Checks a workbook against the accessibility rules, sheet by sheet: names, tables, merged cells, drawings, links and cell formats.</summary>
internal sealed class WorkbookAccessibilityChecker
{
    private readonly Workbook _wb;
    private readonly AccessibilityCollector _c;
    private readonly CancellationToken _ct;

    public WorkbookAccessibilityChecker(Workbook wb, CancellationToken ct)
    {
        _wb = wb;
        _c = new AccessibilityCollector(OfficeKind.Workbook, wb.Limits.MaxDefects);
        _ct = ct;
    }

    public AccessibilityReport Run()
    {
        foreach (Worksheet sheet in _wb.Sheets)
        {
            _ct.ThrowIfCancellationRequested();
            CheckSheet(sheet);
        }

        string? title = null;
        try
        {
            title = new DocumentProperties(_wb.Package, _wb.OpenContext).Core.Title;
        }
        catch (OfficeException)
        {
            // An unreadable properties part is a defect of the open, not a finding.
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            _c.Add(new AccessibilityIssue(AccessibilityRules.DocumentTitle, new AccessibilityLocation(), null, "The workbook has no title property.",
                "Set the title in the file properties; screen readers announce it when the file opens."));
        }

        return _c.ToReport();
    }

    private static AccessibilityLocation Loc(Worksheet s, string? range = null) => new() { Part = s.PartName, Sheet = s.Name, Range = range };

    private void CheckSheet(Worksheet sheet)
    {
        AccessibilityLocation sheetLoc = Loc(sheet);
        if (AccessibilityText.IsDefaultSheetName(sheet.Name))
        {
            _c.Add(new AccessibilityIssue(AccessibilityRules.SheetDefaultName, sheetLoc, sheet.Name, "The sheet “" + sheet.Name + "” keeps its default name.",
                "Rename the sheet to say what it holds, for example “October sales totals”."));
        }

        bool hasDrawing = false;
        if (sheet.PartName is { } part)
        {
            foreach (Relationship r in _wb.Package.GetRelationships(part))
            {
                if (r.Type.EndsWith("/drawing", StringComparison.Ordinal) && r.TargetMode == RelationshipTargetMode.Internal
                    && PartUri.TryResolve(new PartUri(part), r.Target, out PartUri uri) && _wb.Package.FindPart(uri.Value) is { } dp)
                {
                    hasDrawing = true;
                    CheckDrawing(sheet, dp);
                }
            }
        }

        if (sheet.Kind != SheetKind.Worksheet)
        {
            return;
        }

        CellStore store = sheet.Store;
        if (store.CellCount == 0 && !hasDrawing && sheet.Tables.Count == 0)
        {
            _c.Add(new AccessibilityIssue(AccessibilityRules.SheetBlank, sheetLoc, sheet.Name, "The sheet “" + sheet.Name + "” is blank.",
                "Delete the sheet, or put the content it is meant to hold in it."));
        }

        foreach (Table table in sheet.Tables)
        {
            string range = table.Range.IsValid ? table.Range.ToString() : "";
            if (table.HeaderRowCount == 0)
            {
                _c.Add(new AccessibilityIssue(AccessibilityRules.TableHeaderRow, Loc(sheet, range), table.Name, "The table “" + table.Name + "” has no header row.",
                    "Turn on “Header Row” for the table so assistive technology can name its columns."));
            }

            if (AccessibilityText.IsDefaultTableName(table.Name))
            {
                _c.Add(new AccessibilityIssue(AccessibilityRules.TableDefaultName, Loc(sheet, range), table.Name, "The table keeps its default name “" + table.Name + "”.",
                    "Give the table a name that says what it holds (Table Design, Table Name)."));
            }
        }

        // One finding per sheet: a sheet can hold tens of thousands of merged ranges, and they are one problem.
        CellRange firstMerged = default;
        int mergedCount = 0;
        foreach (CellRange merged in sheet.MergedCells)
        {
            if (merged.IsValid && (merged.RowCount > 1 || merged.ColumnCount > 1))
            {
                if (mergedCount++ == 0)
                {
                    firstMerged = merged;
                }
            }
        }

        if (mergedCount > 0)
        {
            _c.Add(new AccessibilityIssue(AccessibilityRules.TableMergedCells, Loc(sheet, firstMerged.ToString()), null,
                mergedCount == 1 ? "The range " + firstMerged + " is merged." : mergedCount.ToString(CultureInfo.InvariantCulture) + " ranges are merged, the first of them " + firstMerged + ".",
                "Unmerge the cells; repeat the value or use \u201cCenter Across Selection\u201d for layout."));
        }

        CheckHyperlinks(sheet);
        CheckCells(sheet, store);
    }

    // ---- hyperlinks --------------------------------------------------------------------------------------------------------------------------

    private void CheckHyperlinks(Worksheet sheet)
    {
        MarkupNode root = sheet.LayoutMarkup;
        MarkupNode links = default;
        foreach (MarkupNode c in root.ChildElements)
        {
            if (c.LocalNameString == "hyperlinks")
            {
                links = c;
            }
        }

        if (links.IsNull)
        {
            return;
        }

        IReadOnlyList<Relationship> rels = sheet.PartName is { } p ? _wb.Package.GetRelationships(p) : [];
        foreach (MarkupNode link in links.ChildElements)
        {
            if (link.LocalNameString != "hyperlink" || !CellRange.TryParse(link.GetAttribute("ref"), out CellRange range))
            {
                continue;
            }

            string? target = link.GetAttribute("location");
            foreach (MarkupAttributeRef a in link.Attributes)
            {
                if (!a.IsNamespaceDeclaration && a.Prefix.Length > 0 && a.LocalName.SequenceEqual("id"u8))
                {
                    foreach (Relationship r in rels)
                    {
                        if (r.Id == a.Value)
                        {
                            target = r.Target;
                        }
                    }
                }
            }

            string text = sheet[range.TopLeft].Text;
            if (text.Length == 0)
            {
                text = link.GetAttribute("display") ?? string.Empty;
            }

            if (AccessibilityText.MeaninglessLinkReason(text, target) is { } reason)
            {
                _c.Add(new AccessibilityIssue(AccessibilityRules.HyperlinkText, Loc(sheet, range.ToString()), null, "Hyperlink text is not meaningful: " + reason + ".",
                    "Write link text that says where the link goes or what it opens."));
            }
        }
    }

    // ---- drawings ----------------------------------------------------------------------------------------------------------------------------

    private void CheckDrawing(Worksheet sheet, IPackagePart part)
    {
        MarkupDocument doc;
        if (!MarkupDocument.TryParse(part.GetBytes(), out MarkupDocument? parsed, _wb.OpenContext, part.Name) || parsed is null)
        {
            _c.Skip(AccessibilityRules.AltTextMissing, Loc(sheet), "The drawing part " + part.Name + " could not be read.");
            return;
        }

        doc = parsed;
        foreach (MarkupNode anchor in doc.Root.ChildElements)
        {
            string a = anchor.LocalNameString;
            if (a is not ("twoCellAnchor" or "oneCellAnchor" or "absoluteAnchor"))
            {
                continue;
            }

            string? cell = null;
            foreach (MarkupNode from in anchor.ChildElements)
            {
                if (from.LocalNameString == "from" && int.TryParse(Text(from, "col"), NumberStyles.None, CultureInfo.InvariantCulture, out int col)
                    && int.TryParse(Text(from, "row"), NumberStyles.None, CultureInfo.InvariantCulture, out int row))
                {
                    cell = new CellAddress(row + 1, col + 1).ToString();
                }
            }

            foreach (MarkupNode shape in Resolved(anchor))
            {
                CheckDrawingObject(sheet, shape, cell, groupCovers: false, 0);
            }
        }
    }

    private static string? Text(MarkupNode parent, string local)
    {
        foreach (MarkupNode c in parent.ChildElements)
        {
            if (c.LocalNameString == local)
            {
                return c.InnerText;
            }
        }

        return null;
    }

    private static IEnumerable<MarkupNode> Resolved(MarkupNode parent)
    {
        foreach (MarkupNode c in parent.ChildElements)
        {
            if (MarkupCompatibility.IsAlternateContent(c))
            {
                MarkupNode pick = default;
                foreach (MarkupNode b in c.ChildElements)
                {
                    if (pick.IsNull && (MarkupCompatibility.IsChoice(b) || MarkupCompatibility.IsFallback(b)))
                    {
                        pick = b;
                    }
                }

                if (!pick.IsNull)
                {
                    foreach (MarkupNode inner in Resolved(pick))
                    {
                        yield return inner;
                    }
                }
            }
            else
            {
                yield return c;
            }
        }
    }

    private void CheckDrawingObject(Worksheet sheet, MarkupNode shape, string? cell, bool groupCovers, int depth)
    {
        if (depth > 16)
        {
            return;
        }

        string kind = shape.LocalNameString;
        MarkupNode nv = default;
        foreach (MarkupNode c in shape.ChildElements)
        {
            if (c.LocalNameString.StartsWith("nv", StringComparison.Ordinal))
            {
                nv = c;
            }
        }

        MarkupNode props = default;
        if (!nv.IsNull)
        {
            foreach (MarkupNode c in nv.ChildElements)
            {
                if (c.LocalNameString == "cNvPr")
                {
                    props = c;
                }
            }
        }

        string? what = kind switch
        {
            "pic" => "picture",
            "grpSp" => "group",
            "graphicFrame" => IsChart(shape) ? "chart" : "object",
            "sp" => HasText(shape) ? null : "shape",
            _ => null,
        };

        long? id = long.TryParse(props.IsNull ? null : props.GetAttribute("id"), NumberStyles.None, CultureInfo.InvariantCulture, out long idv) ? idv : null;
        if (what is not null && !groupCovers && !props.IsNull)
        {
            DrawingAccessibility.Check(_c, props, Loc(sheet, cell) with { ShapeId = id }, what, canBeDecorative: false);
        }

        if (kind == "grpSp")
        {
            foreach (MarkupNode child in Resolved(shape))
            {
                if (child.LocalNameString is "sp" or "pic" or "grpSp" or "graphicFrame")
                {
                    CheckDrawingObject(sheet, child, cell, groupCovers: true, depth + 1);
                }
            }
        }
    }

    private static bool IsChart(MarkupNode frame)
    {
        foreach (MarkupNode g in frame.ChildElements)
        {
            if (g.LocalNameString == "graphic")
            {
                foreach (MarkupNode d in g.ChildElements)
                {
                    if (d.GetAttribute("uri") is { } uri && (uri.EndsWith("/chart", StringComparison.Ordinal) || uri.EndsWith("/chartex", StringComparison.Ordinal)))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool HasText(MarkupNode sp)
    {
        foreach (MarkupNode c in sp.ChildElements)
        {
            if (c.LocalNameString == "txBody" && c.InnerText.Trim().Length > 0)
            {
                return true;
            }
        }

        return false;
    }

    // ---- cells: contrast and red-only negative numbers -----------------------------------------------------------------------

    private sealed class Aggregate
    {
        public int MinRow = int.MaxValue, MaxRow, MinCol = int.MaxValue, MaxCol;
        public long Count;

        public void Add(int row, int col)
        {
            MinRow = Math.Min(MinRow, row);
            MaxRow = Math.Max(MaxRow, row);
            MinCol = Math.Min(MinCol, col);
            MaxCol = Math.Max(MaxCol, col);
            Count++;
        }

        public string Range => new CellRange(new CellAddress(MinRow, MinCol), new CellAddress(MaxRow, MaxCol)).ToString();
    }

    private sealed record StyleVerdict(bool RedOnly, bool ContrastFails, int Fg, int Bg, double Ratio, double Size, bool Unknown);

    private void CheckCells(Worksheet sheet, CellStore store)
    {
        var verdicts = new Dictionary<int, StyleVerdict>();
        var red = new Dictionary<int, Aggregate>();
        var low = new Dictionary<int, Aggregate>();
        var order = new List<(bool Red, int Style)>();
        bool unknownReported = false;

        foreach (CellRowView row in store.EnumerateRows())
        {
            _ct.ThrowIfCancellationRequested();
            for (int i = 0; i < row.Count; i++)
            {
                CellValue value = row.ValueAt(i);
                if (value.IsEmpty)
                {
                    continue;
                }

                int style = row.StyleAt(i);
                if (style == 0)
                {
                    continue;
                }

                if (!verdicts.TryGetValue(style, out StyleVerdict? v))
                {
                    v = Evaluate(sheet.Workbook.Styles.Resolve(style), sheet.Workbook);
                    verdicts[style] = v;
                }

                int col = row.ColumnAt(i);
                if (v.RedOnly && value.Kind == CellValueKind.Number)
                {
                    if (!red.TryGetValue(style, out Aggregate? ra))
                    {
                        red[style] = ra = new Aggregate();
                        order.Add((true, style));
                    }

                    ra.Add(row.Row, col);
                }

                if ((v.ContrastFails || v.Unknown) && (value.Kind != CellValueKind.Text || sheet.GetText(value).Trim().Length > 0))
                {
                    if (v.Unknown)
                    {
                        if (!unknownReported)
                        {
                            unknownReported = true;
                            _c.Skip(AccessibilityRules.ContrastLow, Loc(sheet, new CellAddress(row.Row, col).ToString()), "Cells filled with a gradient or pattern have a contrast that is not evaluated.");
                        }

                        continue;
                    }

                    if (!low.TryGetValue(style, out Aggregate? la))
                    {
                        low[style] = la = new Aggregate();
                        order.Add((false, style));
                    }

                    la.Add(row.Row, col);
                }
            }
        }

        foreach ((bool isRed, int style) in order)
        {
            if (isRed)
            {
                Aggregate a = red[style];
                _c.Add(new AccessibilityIssue(AccessibilityRules.NumberFormatRedOnly, Loc(sheet, a.Range), null,
                    a.Count.ToString(CultureInfo.InvariantCulture) + " cell(s) show negative numbers in red with no minus sign or parentheses.",
                    "Change the number format so negative numbers also show a minus sign or parentheses (for example 0.00;[Red]-0.00)."));
            }
            else
            {
                Aggregate a = low[style];
                StyleVerdict v = verdicts[style];
                _c.Add(new AccessibilityIssue(AccessibilityRules.ContrastLow, Loc(sheet, a.Range), null,
                    a.Count.ToString(CultureInfo.InvariantCulture) + " cell(s) have text colour " + v.Fg.ToString("X6", CultureInfo.InvariantCulture) + " on " + v.Bg.ToString("X6", CultureInfo.InvariantCulture)
                    + ", a contrast ratio of " + v.Ratio.ToString("0.0#", CultureInfo.InvariantCulture) + ":1.",
                    "Use a darker or lighter font colour, or change the fill, so the ratio is at least " + (v.Size >= 18 ? "3" : "4.5") + ":1."));
            }
        }
    }

    private static StyleVerdict Evaluate(ResolvedCellStyle style, Workbook wb)
    {
        bool redOnly = IsRedOnlyNegative(style.NumberFormatCode);
        if (style.Font.Color.Kind == ColorKind.Auto)
        {
            return new StyleVerdict(redOnly, false, 0, 0, 21, 0, false);
        }

        CellFill fill = style.Fill;
        int fg = wb.Styles.ResolveColor(style.Font.Color) & 0xFFFFFF;
        int bg;
        if (fill.Gradient is not null)
        {
            return new StyleVerdict(redOnly, false, fg, 0, 0, 0, true);
        }

        if (fill.Pattern == FillPattern.None)
        {
            bg = 0xFFFFFF;
        }
        else if (fill.Pattern == FillPattern.Solid)
        {
            bg = wb.Styles.ResolveColor(fill.Foreground) & 0xFFFFFF;
        }
        else
        {
            return new StyleVerdict(redOnly, false, fg, 0, 0, 0, true);
        }

        double ratio = Contrast.Ratio(fg, bg);
        double size = style.Font.Size > 0 ? style.Font.Size : 11;
        return new StyleVerdict(redOnly, !Contrast.Passes(ratio, size, style.Font.Bold), fg, bg, ratio, size, false);
    }

    /// <summary>
    /// Tells whether a number format shows negative numbers in red and nothing else marks them. [Number format codes]: with two or more sections
    /// the second is for negative numbers, and it shows the number without a sign unless it writes one.
    /// </summary>
    internal static bool IsRedOnlyNegative(string? code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return false;
        }

        var sections = new List<string>();
        var current = new System.Text.StringBuilder();
        bool quoted = false;
        for (int i = 0; i < code.Length; i++)
        {
            char ch = code[i];
            if (ch == '"')
            {
                quoted = !quoted;
                current.Append(ch);
            }
            else if (ch == '\\' && i + 1 < code.Length && !quoted)
            {
                current.Append(ch).Append(code[++i]);
            }
            else if (ch == ';' && !quoted)
            {
                sections.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        sections.Add(current.ToString());
        if (sections.Count < 2)
        {
            return false;
        }

        string negative = sections[1];
        if (!negative.Contains("[Red]", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        bool q = false;
        foreach (char ch in negative)
        {
            if (ch == '"')
            {
                q = !q;
            }
            else if (!q && ch is '-' or '(' or ')')
            {
                return false;
            }
        }

        return true;
    }
}
