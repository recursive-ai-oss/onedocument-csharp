using System.Globalization;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Spreadsheets;

namespace RecursiveAI.OneDocument.Rendering.Spreadsheets;

/// <summary>A drawing anchor read from the sheet's drawing part, in cell coordinates.</summary>
internal sealed record Anchor(SheetObjectKind Kind, string Name, MarkupNode Node, int FromCol, double FromColOff, int FromRow, double FromRowOff, int ToCol, double ToColOff, int ToRow, double ToRowOff, double ExtX, double ExtY, bool Absolute, string DrawingPart);

/// <summary>A sheet that is going to be printed: its settings, print areas, sizes, merges, drawing anchors and conditional-format results.</summary>
internal sealed class SheetPlan
{
    private const double EmuPerPoint = 12700;

    private SheetPlan(Worksheet sheet, int index, SheetPrintSettings settings)
    {
        Sheet = sheet;
        Index = index;
        Settings = settings;
    }

    public Worksheet Sheet { get; }

    public int Index { get; }

    public SheetPrintSettings Settings { get; }

    public SheetGeometry Geo { get; private set; } = null!;

    public List<CellRange> Areas { get; } = [];

    public List<Anchor> Anchors { get; } = [];

    // Merged ranges by the cell they start at, and for every covered cell the range that covers it.
    public Dictionary<long, CellRange> MergeAt { get; } = [];

    public Dictionary<long, CellRange> MergeCover { get; } = [];

    public double HeadingWidth { get; private set; }

    public double HeadingHeight { get; private set; }

    public ConditionalResults? Conditional { get; set; }

    public static long Key(int row, int col) => ((long)row << 15) | (uint)col;

    public static SheetPlan? Create(RenderContext ctx, Worksheet sheet, int index)
    {
        var plan = new SheetPlan(sheet, index, SheetPrintSettings.Read(sheet));
        SheetPrintSettings s = plan.Settings;
        foreach (CellRange m in sheet.MergedCells)
        {
            if (!m.IsValid || (m.RowCount == 1 && m.ColumnCount == 1))
            {
                continue;
            }

            plan.MergeAt[Key(m.TopLeft.Row, m.TopLeft.Column)] = m;
            if ((long)m.RowCount * m.ColumnCount <= 1_000_000)
            {
                for (int r = m.TopLeft.Row; r <= m.BottomRight.Row; r++)
                {
                    for (int c = m.TopLeft.Column; c <= m.BottomRight.Column; c++)
                    {
                        plan.MergeCover[Key(r, c)] = m;
                    }
                }
            }
        }

        plan.ReadAnchors(ctx);
        (int usedRow, int usedCol) = plan.UsedExtent(ctx);
        if (s.PrintArea.Count > 0)
        {
            foreach (CellRange a in s.PrintArea)
            {
                int r2 = a.BottomRight.Row;
                int c2 = a.BottomRight.Column;
                // A whole-row or whole-column area stops at the used cells; an explicit rectangle prints as given.
                if (r2 >= CellAddress.MaxRows)
                {
                    r2 = Math.Max(usedRow, a.TopLeft.Row);
                }

                if (c2 >= CellAddress.MaxColumns)
                {
                    c2 = Math.Max(usedCol, a.TopLeft.Column);
                }

                plan.Areas.Add(new CellRange(a.TopLeft, new CellAddress(r2, c2)));
            }
        }
        else if (usedRow > 0 && usedCol > 0)
        {
            plan.Areas.Add(new CellRange(new CellAddress(1, 1), new CellAddress(usedRow, usedCol)));
        }
        else
        {
            return null;
        }

        int r0 = plan.Areas.Min(a => a.TopLeft.Row);
        int r1 = plan.Areas.Max(a => a.BottomRight.Row);
        int c0 = plan.Areas.Min(a => a.TopLeft.Column);
        int c1 = plan.Areas.Max(a => a.BottomRight.Column);
        if (s.TitleRows.First > 0)
        {
            r0 = Math.Min(r0, s.TitleRows.First);
            r1 = Math.Max(r1, s.TitleRows.Last);
        }

        if (s.TitleColumns.First > 0)
        {
            c0 = Math.Min(c0, s.TitleColumns.First);
            c1 = Math.Max(c1, s.TitleColumns.Last);
        }

        plan.BuildGeometry(ctx, r0, r1, c0, c1);
        if (s.PrintHeadings)
        {
            int digits = Math.Max(2, r1.ToString(CultureInfo.InvariantCulture).Length);
            plan.HeadingWidth = ((digits * ctx.MaxDigitWidth) + 5) * 0.75;
            plan.HeadingHeight = ctx.DefaultRowHeightPoints;
        }

        return plan;
    }

    // What prints when there is no print area: the cells with content or visible formatting, merged ranges and drawings.
    private (int Row, int Col) UsedExtent(RenderContext ctx)
    {
        int maxR = 0;
        int maxC = 0;
        CellStore store = Sheet.Store;
        foreach (CellRowView row in store.EnumerateRows())
        {
            for (int i = 0; i < row.Count; i++)
            {
                CellValue v = row.ValueAt(i);
                if (v.Kind != CellValueKind.Empty || ctx.Styles.Get(row.StyleAt(i)).Visible)
                {
                    maxR = Math.Max(maxR, row.Row);
                    maxC = Math.Max(maxC, row.ColumnAt(i));
                }
            }
        }

        foreach (CellRange m in MergeAt.Values)
        {
            maxR = Math.Max(maxR, m.BottomRight.Row);
            maxC = Math.Max(maxC, m.BottomRight.Column);
        }

        foreach (Anchor a in Anchors)
        {
            if (!a.Absolute)
            {
                maxR = Math.Max(maxR, a.ToRow + 1);
                maxC = Math.Max(maxC, a.ToCol + 1);
            }
        }

        return (maxR, maxC);
    }

    private void BuildGeometry(RenderContext ctx, int r0, int r1, int c0, int c1)
    {
        SheetPrintSettings s = Settings;
        int nc = c1 - c0 + 1;
        var widths = new double[nc];
        double defaultWidth = ctx.DefaultColumnWidthPoints(s);
        Array.Fill(widths, defaultWidth);
        var hidden = new bool[nc];
        var colStyle = new int[nc];
        foreach (ColumnProperties cp in Sheet.Columns)
        {
            int a = Math.Max(cp.Min, c0);
            int b = Math.Min(cp.Max, c1);
            for (int c = a; c <= b; c++)
            {
                if (cp.Width >= 0)
                {
                    widths[c - c0] = ctx.ColumnWidthPoints(cp.Width);
                }

                hidden[c - c0] = (cp.Flags & ColumnOptions.Hidden) != 0;
                colStyle[c - c0] = cp.Style;
            }
        }

        for (int i = 0; i < nc; i++)
        {
            if (hidden[i])
            {
                widths[i] = 0;
            }
        }

        ColumnStyles = colStyle;
        ColumnStylesFirst = c0;
        int nr = r1 - r0 + 1;
        var heights = new double[nr];
        double defaultHeight = s.DefaultRowHeight > 0 ? s.DefaultRowHeight : ctx.DefaultRowHeightPoints;
        CellStore store = Sheet.Store;
        RowStyles = new int[nr];
        // The geometry is needed to wrap text while fitting rows, so columns go in first with rows still at the default.
        var colGeometry = new SheetGeometry(r0, r1, c0, c1, heights, widths);
        Geo = colGeometry;
        for (int r = r0; r <= r1; r++)
        {
            RowProperties props = Sheet.GetRow(r);
            double h = props.Height;
            bool exists = store.TryGetRow(r, out CellRowView view);
            RowStyles[r - r0] = props.StyleIndex;
            if (props.Hidden || (s.RowsHiddenByDefault && h < 0 && !exists))
            {
                heights[r - r0] = 0;
                continue;
            }

            if (h >= 0)
            {
                heights[r - r0] = h;
                continue;
            }

            heights[r - r0] = exists ? AutoFit(ctx, view, defaultHeight) : defaultHeight;
        }

        Geo = new SheetGeometry(r0, r1, c0, c1, heights, widths);
    }

    public int[] ColumnStyles { get; private set; } = [];

    public int ColumnStylesFirst { get; private set; }

    public int[] RowStyles { get; private set; } = [];

    // A row without a stored height takes the height its tallest cell needs: the font's line height, times the lines of wrapped text.
    private double AutoFit(RenderContext ctx, CellRowView row, double defaultHeight)
    {
        double best = defaultHeight;
        for (int i = 0; i < row.Count; i++)
        {
            CellValue v = row.ValueAt(i);
            int col = row.ColumnAt(i);
            if (v.Kind == CellValueKind.Empty || MergeCover.ContainsKey(Key(row.Row, col)))
            {
                continue;
            }

            StyleInfo st = ctx.Styles.Get(row.StyleAt(i));
            double line = LineHeight(ctx, st.Text);
            CellAlignment al = st.Style.Alignment;
            int lines = 1;
            if (al.WrapText && v.Kind == CellValueKind.Text && Geo.Covers(row.Row, col) && al.TextRotation == 0)
            {
                string text = Sheet.GetText(v);
                double width = Math.Max(1, Geo.ColWidth(col) - 3.5 - (al.Indent * ctx.MaxDigitWidth * 0.75));
                lines = Math.Max(1, ctx.Text.Layout([(text, st.Text)], width, false, null).Lines.Count);
            }
            else if (v.Kind == CellValueKind.Text && Sheet.GetText(v).Contains('\n', StringComparison.Ordinal) && al.WrapText)
            {
                lines = Sheet.GetText(v).Split('\n').Length;
            }

            double need = lines * line;
            if (al.TextRotation is > 0 and < 255 or > 90 && al.TextRotation != 255 && v.Kind == CellValueKind.Text)
            {
                double w = ctx.Text.Measure(Sheet.GetText(v), st.Text);
                double angle = (al.TextRotation <= 90 ? al.TextRotation : al.TextRotation - 90) * Math.PI / 180;
                need = Math.Max(need, (w * Math.Sin(angle)) + (line * Math.Cos(angle)));
            }
            else if (al.TextRotation == 255 && v.Kind == CellValueKind.Text)
            {
                need = Math.Max(need, Sheet.GetText(v).Length * line);
            }

            best = Math.Max(best, need);
        }

        return best;
    }

    private static double LineHeight(RenderContext ctx, in TextStyle style)
    {
        (double asc, double desc, double gap) = ctx.Text.LineMetrics(style with { Vertical = FontVerticalAlignment.Baseline });
        return (Math.Round(asc * 96 / 72) + Math.Round(desc * 96 / 72) + Math.Round(gap * 96 / 72) + 2) * 0.75;
    }

    // ---- drawings -------------------------------------------------------------------------------------------------------------

    private void ReadAnchors(RenderContext ctx)
    {
        MarkupNode drawing = X.Child(Sheet.LayoutMarkup, "drawing");
        if (drawing.IsNull || Sheet.PartName is null)
        {
            return;
        }

        string? rid = null;
        foreach (MarkupAttributeRef a in drawing.Attributes)
        {
            if (!a.IsNamespaceDeclaration && a.LocalName.SequenceEqual("id"u8))
            {
                rid = a.Value;
            }
        }

        if (rid is null)
        {
            return;
        }

        IOpcPackage package = Sheet.Workbook.Package;
        foreach (Relationship rel in package.GetRelationships(Sheet.PartName))
        {
            if (rel.Id != rid || !OpcPackage.TryResolveTarget(Sheet.PartName, rel, out PartUri target))
            {
                continue;
            }

            IPackagePart? part = package.FindPart(target.Value);
            if (part is null || !MarkupDocument.TryParse(part.GetBytes(), out MarkupDocument? doc, null, target.Value) || doc is null)
            {
                ctx.Note("sheet-layout.drawing-unreadable", "A drawing part could not be read; its objects are not placed.", Sheet.Name);
                continue;
            }

            foreach (MarkupNode anchor in doc.Root.ChildElements)
            {
                if (ReadAnchor(anchor, target.Value) is { } a)
                {
                    Anchors.Add(a);
                }
            }
        }
    }

    private static Anchor? ReadAnchor(MarkupNode node, string drawingPart)
    {
        string kind = node.LocalNameString;
        if (kind is not ("twoCellAnchor" or "oneCellAnchor" or "absoluteAnchor"))
        {
            return null;
        }

        int fc = 0, fr = 0, tc = 0, tr = 0;
        double fco = 0, fro = 0, tco = 0, tro = 0, ex = 0, ey = 0;
        SheetObjectKind objectKind = SheetObjectKind.Other;
        string name = string.Empty;
        foreach (MarkupNode c in node.ChildElements)
        {
            switch (c.LocalNameString)
            {
                case "from":
                    ReadMarker(c, out fc, out fco, out fr, out fro);
                    break;
                case "to":
                    ReadMarker(c, out tc, out tco, out tr, out tro);
                    break;
                case "ext":
                    ex = X.Double(c.GetAttribute("cx"), 0) / EmuPerPoint;
                    ey = X.Double(c.GetAttribute("cy"), 0) / EmuPerPoint;
                    break;
                case "pos":
                    fco = X.Double(c.GetAttribute("x"), 0) / EmuPerPoint;
                    fro = X.Double(c.GetAttribute("y"), 0) / EmuPerPoint;
                    break;
                case "pic": objectKind = SheetObjectKind.Picture; name = Name(c); break;
                case "graphicFrame": objectKind = SheetObjectKind.Chart; name = Name(c); break;
                case "sp": objectKind = SheetObjectKind.Shape; name = Name(c); break;
                case "grpSp": objectKind = SheetObjectKind.Group; name = Name(c); break;
                case "cxnSp": objectKind = SheetObjectKind.Connector; name = Name(c); break;
            }
        }

        if (kind == "oneCellAnchor")
        {
            return new Anchor(objectKind, name, node, fc, fco, fr, fro, fc, fco, fr, fro, ex, ey, false, drawingPart);
        }

        if (kind == "absoluteAnchor")
        {
            return new Anchor(objectKind, name, node, 0, fco, 0, fro, 0, fco, 0, fro, ex, ey, true, drawingPart);
        }

        return new Anchor(objectKind, name, node, fc, fco, fr, fro, tc, tco, tr, tro, 0, 0, false, drawingPart);
    }

    private static void ReadMarker(MarkupNode m, out int col, out double colOff, out int row, out double rowOff)
    {
        col = 0;
        row = 0;
        colOff = 0;
        rowOff = 0;
        foreach (MarkupNode c in m.ChildElements)
        {
            switch (c.LocalNameString)
            {
                case "col": col = X.Int(c.InnerText, 0); break;
                case "colOff": colOff = X.Double(c.InnerText, 0) / EmuPerPoint; break;
                case "row": row = X.Int(c.InnerText, 0); break;
                case "rowOff": rowOff = X.Double(c.InnerText, 0) / EmuPerPoint; break;
            }
        }
    }

    private static string Name(MarkupNode c)
    {
        foreach (MarkupNode d in X.Descendants(c))
        {
            if (d.LocalNameString == "cNvPr")
            {
                return d.GetAttribute("name") ?? string.Empty;
            }
        }

        return string.Empty;
    }

    // An anchor's box in the geometry's own coordinates (points from the top-left of the first covered row and column), or null when it is outside.
    public (double X, double Y, double W, double H)? AnchorBox(Anchor a)
    {
        SheetGeometry g = Geo;
        double x1, y1, x2, y2;
        if (a.Absolute)
        {
            return null;
        }

        int col = a.FromCol + 1;
        int row = a.FromRow + 1;
        if (col < g.Col0 || col > g.Col1 + 1 || row < g.Row0 || row > g.Row1 + 1)
        {
            return null;
        }

        x1 = g.ColLeft(col) + a.FromColOff;
        y1 = g.RowTop(row) + a.FromRowOff;
        if (a.ExtX > 0 || a.ExtY > 0)
        {
            x2 = x1 + a.ExtX;
            y2 = y1 + a.ExtY;
        }
        else
        {
            x2 = g.ColLeft(a.ToCol + 1) + a.ToColOff;
            y2 = g.RowTop(a.ToRow + 1) + a.ToRowOff;
        }

        return (x1, y1, Math.Max(0, x2 - x1), Math.Max(0, y2 - y1));
    }
}
