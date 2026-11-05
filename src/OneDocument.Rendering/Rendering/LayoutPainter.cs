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

        public DisplayList List { get; } = new();

        private ItemId Id(ushort part = 0, ushort sub = 0) => ItemId.Of(++_owner, part, sub);

        private bool _artifact;

        // Inside an artifact (header, footer, decoration) no structure tags are written.
        private bool Tagged => options.Tagged && !_artifact;

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

            bool listOpen = false;
            foreach (LayoutParagraph p in page.Paragraphs)
            {
                if (p.Story != LayoutStory.Body)
                {
                    // Headers and footers are pagination artifacts (OneRender has no finer role for them yet).
                    if (listOpen)
                    {
                        List.EndTag();
                        listOpen = false;
                    }

                    _artifact = true;
                    if (options.Tagged)
                    {
                        List.BeginTag(Id(), TagRole.Artifact);
                    }

                    PaintParagraph(p, false);
                    if (options.Tagged)
                    {
                        List.EndTag();
                    }

                    _artifact = false;
                    continue;
                }

                bool inList = p.Label is not null && options.Tagged;
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

            foreach (LayoutDecoration d in page.Decorations)
            {
                if (d.DecorationKind == LayoutDecorationKind.LineNumber)
                {
                    PaintDecoration(d);
                }
            }

            List.Validate();
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
                    List.FillRect(Id(20), Rect(d.Bounds.X, d.Bounds.Y, d.Bounds.Width, d.Bounds.Height), Rgb(d.Color));
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
