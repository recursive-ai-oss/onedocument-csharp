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

        public void Paint(LayoutPage page)
        {
            bool listOpen = false;
            foreach (LayoutParagraph p in page.Paragraphs)
            {
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

            List.Validate();
        }

        private void PaintParagraph(LayoutParagraph p, bool inList)
        {
            PaintDecoration(p);
            bool tagged = options.Tagged;
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

            bool link = options.Tagged && paint.LinkTarget is not null && run.RunKind == LayoutRunKind.Text;
            bool span = options.Tagged && !link && paint.Language is { Length: > 0 } lang && !string.Equals(lang, options.Language, StringComparison.OrdinalIgnoreCase);
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
