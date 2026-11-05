using System.Numerics;
using OneRender.Painting;
using RecursiveAI.OneDocument.Layout;

namespace RecursiveAI.OneDocument.Rendering;

/// <summary>Options for painting a page into a display list.</summary>
public sealed class PaintOptions
{
    /// <summary>Gets or sets whether semantic tags (paragraphs, headings, lists, links) are written. Default true.</summary>
    public bool Tagged { get; set; } = true;

    /// <summary>
    /// Gets or sets the document's default language (BCP 47). Spans are tagged with a language only where a run differs from it.
    /// Default <c>en-US</c>.
    /// </summary>
    public string Language { get; set; } = "en-US";
}

/// <summary>
/// Paints laid-out pages into OneRender display lists. The list's units are CSS pixels (1/96 inch): one point of layout is 4/3 px.
/// The origin is the page's top-left corner, so the list is as large as the page.
/// </summary>
/// <remarks>
/// Semantic tags written when <see cref="PaintOptions.Tagged"/> is on: a paragraph is <see cref="TagRole.Paragraph"/> or a heading
/// <c>H1</c> to <c>H6</c>; consecutive numbered paragraphs share a <see cref="TagRole.List"/> whose items hold a
/// <see cref="TagRole.ListLabel"/> and a <see cref="TagRole.ListBody"/>; a run with a hyperlink is a <see cref="TagRole.Link"/>; a run
/// whose language differs from the document's is a <see cref="TagRole.Span"/> with that language. Fills, borders, underlines and tab
/// leaders are decoration and become artifacts in the output.
/// </remarks>
public static class LayoutPainter
{
    /// <summary>Points to CSS pixels.</summary>
    public const double PixelsPerPoint = 96.0 / 72.0;

    /// <summary>Paints one page.</summary>
    /// <param name="page">The page from <see cref="LayoutEngine.Layout"/>.</param>
    /// <param name="options">Tagging and language; null for the defaults.</param>
    /// <returns>A finished display list the size of the page in CSS pixels.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="page"/> is null.</exception>
    public static DisplayList Paint(LayoutPage page, PaintOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        var painter = new PagePainter(options ?? new PaintOptions());
        painter.Paint(page);
        return painter.List;
    }

    private sealed class PagePainter(PaintOptions options)
    {
        private const double K = PixelsPerPoint;
        private uint _owner;
        private int _untagged; // above 0 while painting content that is an artifact (repeated header rows)

        private bool Tagged => options.Tagged && _untagged == 0;

        public DisplayList List { get; } = new();

        private ItemId Id(ushort part = 0, ushort sub = 0) => ItemId.Of(++_owner, part, sub);

        public void Paint(LayoutPage page)
        {
            // Behind the text: page borders, column separators and watermarks. Line numbers go on top, after the text.
            foreach (LayoutDecoration d in page.Decorations)
            {
                if (d.DecorationKind != LayoutDecorationKind.LineNumber)
                {
                    PaintDecoration(d);
                }
            }

            PaintBlocks(page.Children);

            foreach (LayoutDecoration d in page.Decorations)
            {
                if (d.DecorationKind == LayoutDecorationKind.LineNumber)
                {
                    PaintDecoration(d);
                }
            }

            List.Validate();
        }

        // The blocks of a page or of a table cell: paragraphs (consecutive numbered ones share a list) and tables.
        private void PaintBlocks(IEnumerable<LayoutNode> blocks)
        {
            bool listOpen = false;
            foreach (LayoutNode node in blocks)
            {
                if (node is LayoutTable table)
                {
                    if (listOpen)
                    {
                        List.EndTag();
                        listOpen = false;
                    }

                    PaintTable(table);
                    continue;
                }

                if (node is not LayoutParagraph p)
                {
                    continue;
                }

                if (p.Story != LayoutStory.Body)
                {
                    // Headers and footers are pagination artifacts (OneRender has no finer role for them yet).
                    if (listOpen)
                    {
                        List.EndTag();
                        listOpen = false;
                    }

                    _untagged++;
                    if (options.Tagged)
                    {
                        List.BeginTag(Id(), TagRole.Artifact);
                    }

                    PaintParagraph(p, false);
                    if (options.Tagged)
                    {
                        List.EndTag();
                    }

                    _untagged--;
                    continue;
                }

                bool inList = p.Label is not null && Tagged;
                if (listOpen && !inList)
                {
                    List.EndTag();
                    listOpen = false;
                }

                if (inList && !listOpen)
                {
                    List.BeginTag(Id(), TagRole.List);
                    listOpen = true;
                }

                PaintParagraph(p, inList);
            }

            if (listOpen)
            {
                List.EndTag();
            }
        }

        // Tables: Table > TR > TH/TD > content. Header rows repeated from an earlier page are artifacts. Shading goes under the cell's
        // content; borders are drawn last so the winning border of a shared edge lies on top of both cells' shading.
        private void PaintTable(LayoutTable table)
        {
            if (Tagged)
            {
                List.BeginTag(Id(), TagRole.Table);
            }

            List<LayoutTableRow> rows = [.. table.Rows];
            foreach (LayoutTableRow row in rows)
            {
                bool artifact = row.IsRepeatedHeader && options.Tagged && _untagged == 0;
                if (artifact)
                {
                    List.BeginTag(Id(), TagRole.Artifact);
                    _untagged++;
                }
                else if (Tagged)
                {
                    List.BeginTag(Id(), TagRole.TableRow);
                }

                foreach (LayoutTableCell cell in row.Cells)
                {
                    if (cell.Paint.Shading is { } fill)
                    {
                        LayoutRect cb = cell.Bounds;
                        List.FillRect(Id(20), Rect(cb.X, cb.Y, cb.Width, cb.Height), Rgb(fill));
                    }

                    if (Tagged)
                    {
                        int last = cell.Row + cell.RowSpan - 1;
                        int span = Math.Max(1, rows.Count(r => r.Index >= cell.Row && r.Index <= last));
                        List.BeginTag(Id(), cell.IsHeader ? TagRole.TableHeaderCell : TagRole.TableDataCell, columnSpan: cell.ColumnSpan, rowSpan: span);
                    }

                    PaintBlocks(cell.Children);
                    if (Tagged)
                    {
                        List.EndTag();
                    }
                }

                if (artifact)
                {
                    _untagged--;
                    List.EndTag();
                }
                else if (Tagged)
                {
                    List.EndTag();
                }
            }

            if (Tagged)
            {
                List.EndTag();
            }

            foreach (LayoutTableRow row in rows)
            {
                foreach (LayoutTableCell cell in row.Cells)
                {
                    PaintCellBorders(cell);
                }
            }
        }

        // Collapsed borders straddle the cell edge; with cell spacing each cell's borders sit inside its own box.
        private void PaintCellBorders(LayoutTableCell cell)
        {
            CellPaintInfo info = cell.Paint;
            LayoutRect b = cell.Bounds;
            double tw = info.Top?.Width ?? 0;
            double bw = info.Bottom?.Width ?? 0;
            double lw = info.Left?.Width ?? 0;
            double rw = info.Right?.Width ?? 0;
            if (info.Separate)
            {
                if (info.Top is { } t) { Edge(t, b.X, b.Y, b.Width, t.Width, true); }
                if (info.Bottom is { } bt) { Edge(bt, b.X, b.Bottom - bt.Width, b.Width, bt.Width, true); }
                if (info.Left is { } l) { Edge(l, b.X, b.Y, b.Height, l.Width, false); }
                if (info.Right is { } r) { Edge(r, b.Right - r.Width, b.Y, b.Height, r.Width, false); }
                return;
            }

            if (info.Top is { } top)
            {
                Edge(top, b.X - (lw / 2), b.Y - (tw / 2), b.Width + (lw / 2) + (rw / 2), tw, true);
            }

            if (info.Left is { } left)
            {
                Edge(left, b.X - (lw / 2), b.Y - (tw / 2), b.Height + (tw / 2) + (bw / 2), lw, false);
            }

            if (info.PaintBottom && info.Bottom is { } bottom)
            {
                Edge(bottom, b.X - (lw / 2), b.Bottom - (bw / 2), b.Width + (lw / 2) + (rw / 2), bw, true);
            }

            if (info.PaintRight && info.Right is { } right)
            {
                Edge(right, b.Right - (rw / 2), b.Y - (tw / 2), b.Height + (tw / 2) + (bw / 2), rw, false);
            }
        }

        // One border line: (x, y) is the top-left of a box that is len long and thick across. Double and triple styles draw alternate
        // strips; dotted and dashed draw segments; everything else is solid.
        private void Edge(CellEdge e, double x, double y, double len, double thick, bool horizontal)
        {
            PaintColor color = Rgb(e.Color);
            void Piece(double along, double length, double across, double width)
            {
                if (length <= 0 || width <= 0)
                {
                    return;
                }

                RectF rect = horizontal ? Rect(x + along, y + across, length, width) : Rect(x + across, y + along, width, length);
                List.FillRect(Id(21), rect, color);
            }

            int lines = TableBorders.Lines(e.Style);
            if (lines > 1)
            {
                double strip = thick / ((2 * lines) - 1);
                for (int i = 0; i < lines; i++)
                {
                    Piece(0, len, i * 2 * strip, strip);
                }

                return;
            }

            if (e.Style is "dotted" or "dashed" or "dotDash" or "dotDotDash" or "dashSmallGap")
            {
                double seg = Math.Max(thick, 1) * (e.Style == "dotted" ? 1 : 3);
                for (double a = 0; a < len; a += seg * 2)
                {
                    Piece(a, Math.Min(seg, len - a), 0, thick);
                }

                return;
            }

            Piece(0, len, 0, thick);
        }

        private void PaintParagraph(LayoutParagraph p, bool inList)
        {
            PaintDecoration(p);
            bool tagged = Tagged;
            if (tagged && inList && !p.ContinuesFromPreviousPage)
            {
                List.BeginTag(Id(), TagRole.ListItem);
            }

            // The label sits on the first line; its runs are written inside ListLabel, the rest inside ListBody > Paragraph.
            bool bodyOpen = false;
            bool paragraphOpen = false;
            if (tagged && !inList)
            {
                List.BeginTag(Id(), RoleOf(p));
                paragraphOpen = true;
            }

            foreach (LayoutLine line in p.Lines)
            {
                foreach (LayoutRun run in line.Runs)
                {
                    if (run.RunKind == LayoutRunKind.Label)
                    {
                        if (tagged)
                        {
                            List.BeginTag(Id(), TagRole.ListLabel);
                        }

                        PaintRun(run, line);
                        if (tagged)
                        {
                            List.EndTag();
                        }

                        continue;
                    }

                    if (tagged && inList && !bodyOpen)
                    {
                        List.BeginTag(Id(), TagRole.ListBody);
                        List.BeginTag(Id(), RoleOf(p));
                        bodyOpen = true;
                    }

                    PaintRun(run, line);
                }
            }

            if (tagged && inList && !bodyOpen && !p.ContinuesFromPreviousPage)
            {
                // A numbered paragraph with no text still has a body.
                List.BeginTag(Id(), TagRole.ListBody);
                List.BeginTag(Id(), RoleOf(p));
                bodyOpen = true;
            }

            if (bodyOpen)
            {
                List.EndTag();
                List.EndTag();
            }

            if (paragraphOpen)
            {
                List.EndTag();
            }

            if (tagged && inList && !p.ContinuesFromPreviousPage)
            {
                List.EndTag();
            }
        }

        // Borders, separators, line numbers and watermarks are page furniture, not content: artifacts.
        private void PaintDecoration(LayoutDecoration d)
        {
            switch (d.DecorationKind)
            {
                case LayoutDecorationKind.PageBorder:
                    PaintBorder(d);
                    break;
                case LayoutDecorationKind.ColumnSeparator:
                    List.FillRect(Id(40), Rect(d.Bounds.X, d.Bounds.Y, d.Bounds.Width, d.Bounds.Height), Rgb(d.Color));
                    break;
                case LayoutDecorationKind.LineNumber or LayoutDecorationKind.Watermark when d.Glyphs is { } g && d.Face is { } face:
                    PaintDecorationText(d, g, face);
                    break;
            }
        }

        private void PaintDecorationText(LayoutDecoration d, GlyphData g, OneFont.FontFace face)
        {
            if (options.Tagged)
            {
                List.BeginTag(Id(), TagRole.Artifact);
            }

            var x = new float[g.X.Length];
            var y = new float[g.Y.Length];
            for (int i = 0; i < x.Length; i++)
            {
                x[i] = (float)(g.X[i] * K);
            }

            for (int i = 0; i < y.Length; i++)
            {
                y[i] = (float)(g.Y[i] * K);
            }

            bool watermark = d.DecorationKind == LayoutDecorationKind.Watermark;
            float originX = (float)(d.Bounds.X * K);
            float originY = (float)(d.Baseline * K);
            if (watermark)
            {
                // VML rotation is clockwise degrees about the shape's centre.
                float cx = (float)((d.Bounds.X + (d.Bounds.Width / 2)) * K);
                float cy = (float)((d.Bounds.Y + (d.Bounds.Height / 2)) * K);
                List.PushGroup(Id(22), (float)d.Opacity);
                List.PushTransform(Id(23), Matrix3x2.CreateRotation((float)(d.Rotation * Math.PI / 180), new Vector2(cx, cy)));
            }

            List.DrawGlyphRun(Id(21), face, (float)(d.FontSize * K), originX, originY, Rgb(d.Color), g.GlyphIds, x, y, g.Clusters, d.Text, false);
            if (watermark)
            {
                List.PopTransform();
                List.PopGroup();
            }

            if (options.Tagged)
            {
                List.EndTag();
            }
        }

        // The style gives the pattern along the longer side: solid for single and thick, parallel lines for double and triple,
        // dashes for dashed and dots for dotted. Art borders arrive here as single lines.
        private void PaintBorder(LayoutDecoration d)
        {
            LayoutRect b = d.Bounds;
            bool horizontal = b.Width >= b.Height;
            double thick = horizontal ? b.Height : b.Width;
            double length = horizontal ? b.Width : b.Height;
            PaintColor color = Rgb(d.Color);

            void Segments(double x, double y, double len, double t, bool horiz, int part)
            {
                bool dashed = d.Style.Contains("dash", StringComparison.OrdinalIgnoreCase) || d.Style.Contains("dot", StringComparison.OrdinalIgnoreCase);
                if (!dashed)
                {
                    List.FillRect(Id(24, (ushort)part), horiz ? Rect(x, y, len, t) : Rect(x, y, t, len), color);
                    return;
                }

                bool dot = d.Style.Contains("dot", StringComparison.OrdinalIgnoreCase) && !d.Style.Contains("dash", StringComparison.OrdinalIgnoreCase);
                double on = dot ? Math.Max(t, 1) : Math.Max(t * 3, 3);
                double off = dot ? on : on * 0.6;
                int n = 0;
                for (double p = 0; p < len && n < 60000; p += on + off, n++)
                {
                    double seg = Math.Min(on, len - p);
                    List.FillRect(Id(25, (ushort)Math.Min(n, 65535)), horiz ? Rect(x + p, y, seg, t) : Rect(x, y + p, t, seg), color);
                }
            }

            void Line(double offset, double t, int part)
            {
                if (horizontal)
                {
                    Segments(b.X, b.Y + offset, length, t, true, part);
                }
                else
                {
                    Segments(b.X + offset, b.Y, length, t, false, part);
                }
            }

            if (d.Style is "double")
            {
                Line(0, thick / 3, 0);
                Line(thick * 2 / 3, thick / 3, 1);
            }
            else if (d.Style is "triple" or "thinThickSmallGap" or "thickThinSmallGap")
            {
                Line(0, thick / 5, 0);
                Line(thick * 2 / 5, thick / 5, 1);
                Line(thick * 4 / 5, thick / 5, 2);
            }
            else
            {
                Line(0, thick, 0);
            }
        }

        private static TagRole RoleOf(LayoutParagraph p) => p.HeadingLevel switch
        {
            1 => TagRole.Heading1,
            2 => TagRole.Heading2,
            3 => TagRole.Heading3,
            4 => TagRole.Heading4,
            5 => TagRole.Heading5,
            6 => TagRole.Heading6,
            _ => TagRole.Paragraph,
        };

        private static PaintColor Rgb(uint rgb) => PaintColor.FromBytes((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

        private static RectF Rect(double x, double y, double w, double h) => new((float)(x * K), (float)(y * K), (float)(w * K), (float)(h * K));

        // Paragraph shading and borders, drawn under the text.
        private void PaintDecoration(LayoutParagraph p)
        {
            ParagraphPaintInfo info = p.Paint;
            LayoutRect b = p.Bounds;
            if (info.Shading is { } fill)
            {
                List.FillRect(Id(1), Rect(b.X, b.Y, b.Width, b.Height), Rgb(fill));
            }

            if (!p.ContinuesFromPreviousPage && info.Top is { } t)
            {
                List.FillRect(Id(2), Rect(b.X, b.Y, b.Width, t.Width), Rgb(t.Color));
            }

            if (!p.ContinuesOnNextPage && info.Bottom is { } bt)
            {
                List.FillRect(Id(3), Rect(b.X, b.Bottom - bt.Width, b.Width, bt.Width), Rgb(bt.Color));
            }

            if (info.Left is { } l)
            {
                List.FillRect(Id(4), Rect(b.X - l.Space - l.Width, b.Y, l.Width, b.Height), Rgb(l.Color));
            }

            if (info.Right is { } r)
            {
                List.FillRect(Id(5), Rect(b.Right + r.Space, b.Y, r.Width, b.Height), Rgb(r.Color));
            }
        }

        private void PaintRun(LayoutRun run, LayoutLine line)
        {
            RunPaintInfo paint = run.Paint;
            double top = line.Baseline - line.Ascent;
            double height = line.Ascent + line.Descent;
            LayoutRect b = run.Bounds;
            if (paint.Shading is { } shd)
            {
                List.FillRect(Id(6), Rect(b.X, top, b.Width, height), Rgb(shd));
            }

            if (paint.Highlight is { } hl)
            {
                List.FillRect(Id(7), Rect(b.X, top, b.Width, height), Rgb(hl));
            }

            if (run.RunKind == LayoutRunKind.Tab)
            {
                PaintLeader(run, line);
                return;
            }

            if (run.RunKind == LayoutRunKind.Math)
            {
                PaintMath(run, line);
                return;
            }

            if (run.Glyphs is not { } g || run.Font is null)
            {
                return;
            }

            bool link = Tagged && paint.LinkTarget is not null && run.RunKind == LayoutRunKind.Text;
            bool span = Tagged && !link && paint.Language is { Length: > 0 } lang && !string.Equals(lang, options.Language, StringComparison.OrdinalIgnoreCase);
            if (link)
            {
                List.BeginTag(Id(), TagRole.Link, linkTarget: paint.LinkTarget, language: null);
            }
            else if (span)
            {
                List.BeginTag(Id(), TagRole.Span, language: paint.Language);
            }

            double baseline = line.Baseline - paint.BaselineShift;
            var x = new float[g.X.Length];
            var y = new float[g.Y.Length];
            for (int i = 0; i < x.Length; i++)
            {
                x[i] = (float)(g.X[i] * K);
            }

            for (int i = 0; i < y.Length; i++)
            {
                y[i] = (float)(g.Y[i] * K);
            }

            bool scaled = Math.Abs(paint.Scale - 1.0) > 1e-6;
            float originX = (float)(b.X * K);
            float originY = (float)(baseline * K);
            if (scaled)
            {
                List.PushTransform(Id(), Matrix3x2.CreateScale((float)paint.Scale, 1f) * Matrix3x2.CreateTranslation(originX, originY));
                originX = 0;
                originY = 0;
            }

            // RTL runs keep the glyph order visual; the clusters decrease along the glyphs.
            List.DrawGlyphRun(Id(8), g.Face, (float)(run.FontSize * K), originX, originY, Rgb(paint.Color), g.GlyphIds, x, y, g.Clusters, g.ShownText ?? run.Text, g.RightToLeft);
            if (scaled)
            {
                List.PopTransform();
            }

            PaintLines(run, paint, baseline);
            if (link || span)
            {
                List.EndTag();
            }
        }

        // An equation: glyphs and rules in the box's own coordinates (first baseline, y down), tagged as one figure whose alternative
        // text is the equation's linear form (OneRender has no formula role).
        private void PaintMath(LayoutRun run, LayoutLine line)
        {
            if (run.Math is not { } box)
            {
                return;
            }

            bool tagged = Tagged;
            if (tagged)
            {
                List.BeginTag(Id(), TagRole.Figure, altText: box.LinearText);
            }

            double ox = run.Bounds.X;
            double oy = line.Baseline;
            ushort index = 0;
            foreach (global::RecursiveAI.OneDocument.Rendering.Equations.MathItem item in box.Items)
            {
                ushort sub = index++;
                switch (item)
                {
                    case global::RecursiveAI.OneDocument.Rendering.Equations.MathGlyph g:
                        List.DrawGlyphRun(Id(12, sub), g.Face, (float)(g.Size * K), (float)((ox + g.X) * K), (float)((oy + g.Y) * K), Rgb(g.Color), [g.GlyphId], [0f], [0f], [0], g.Text, false);
                        break;
                    case global::RecursiveAI.OneDocument.Rendering.Equations.MathRule r:
                        List.FillRect(Id(13, sub), Rect(ox + r.X, oy + r.Y, r.Width, r.Height), Rgb(r.Color));
                        break;
                    case global::RecursiveAI.OneDocument.Rendering.Equations.MathStroke st:
                        PaintStroke(Id(14, sub), ox, oy, st);
                        break;
                }
            }

            if (tagged)
            {
                List.EndTag();
            }
        }

        // A straight line of some thickness as a filled quadrilateral.
        private void PaintStroke(ItemId id, double ox, double oy, global::RecursiveAI.OneDocument.Rendering.Equations.MathStroke st)
        {
            double dx = st.X2 - st.X1;
            double dy = st.Y2 - st.Y1;
            double len = Math.Sqrt((dx * dx) + (dy * dy));
            if (len < 1e-9)
            {
                return;
            }

            double nx = -dy / len * st.Thickness / 2;
            double ny = dx / len * st.Thickness / 2;
            var path = new PathBuffer();
            path.MoveTo((float)((ox + st.X1 + nx) * K), (float)((oy + st.Y1 + ny) * K));
            path.LineTo((float)((ox + st.X2 + nx) * K), (float)((oy + st.Y2 + ny) * K));
            path.LineTo((float)((ox + st.X2 - nx) * K), (float)((oy + st.Y2 - ny) * K));
            path.LineTo((float)((ox + st.X1 - nx) * K), (float)((oy + st.Y1 - ny) * K));
            path.Close();
            List.FillPath(id, path, Rgb(st.Color));
        }

        // Underline, strike-through. ECMA-376 Part 1 §17.3.2.40 (u) and §17.3.2.37 (strike): thin rules at the font's own positions.
        private void PaintLines(LayoutRun run, RunPaintInfo paint, double baseline)
        {
            if (run.Font is null)
            {
                return;
            }

            global::OneFont.FontMetrics m = run.Font.Face.Metrics;
            double unit = run.FontSize / m.UnitsPerEm;
            double thickness = Math.Max(0.5, m.UnderlineThickness * unit);
            LayoutRect b = run.Bounds;
            PaintColor color = Rgb(paint.Color);
            if (paint.Underline is { } u)
            {
                double yPos = baseline - (m.UnderlinePosition * unit);
                bool thick = u.StartsWith("thick", StringComparison.Ordinal) || u == "double";
                List.FillRect(Id(9), Rect(b.X, yPos, b.Width, thick ? thickness * 2 : thickness), color);
                if (u == "double")
                {
                    List.FillRect(Id(9, 1), Rect(b.X, yPos + (thickness * 2), b.Width, thickness), color);
                }
            }

            if (paint.Strike || paint.DoubleStrike)
            {
                double yPos = baseline - (m.StrikeoutPosition * unit);
                double t = Math.Max(0.5, m.StrikeoutSize * unit);
                List.FillRect(Id(10), Rect(b.X, yPos - (paint.DoubleStrike ? t : 0), b.Width, t), color);
                if (paint.DoubleStrike)
                {
                    List.FillRect(Id(10, 1), Rect(b.X, yPos + t, b.Width, t), color);
                }
            }
        }

        // ECMA-376 Part 1 §17.18.81 (ST_TabTlc): dot, hyphen, underscore, heavy and middleDot leaders fill the tab.
        private void PaintLeader(LayoutRun run, LayoutLine line)
        {
            string leader = run.Paint.Leader;
            if (leader.Length == 0 || run.Bounds.Width < 2)
            {
                return;
            }

            LayoutRect b = run.Bounds;
            double baseline = line.Baseline;
            PaintColor color = Rgb(run.Paint.Color);
            double size = Math.Max(run.FontSize, 8);
            switch (leader)
            {
                case "underscore" or "heavy":
                    List.FillRect(Id(11), Rect(b.X, baseline + 1, b.Width, leader == "heavy" ? 1.5 : 0.5), color);
                    break;
                case "hyphen":
                    for (double x = b.X + 1; x + 2.5 < b.Right; x += 4.5)
                    {
                        List.FillRect(Id(11, (ushort)Math.Min(65535, (int)((x - b.X) / 4.5))), Rect(x, baseline - (size * 0.3), 2.5, 0.5), color);
                    }

                    break;
                default:
                    double yy = leader == "middleDot" ? baseline - (size * 0.3) : baseline - 0.6;
                    for (double x = b.X + 1; x + 0.8 < b.Right; x += size * 0.3)
                    {
                        List.FillRect(Id(11, (ushort)Math.Min(65535, (int)((x - b.X) / (size * 0.3)))), Rect(x, yy, 0.8, 0.8), color);
                    }

                    break;
            }
        }
    }
}
