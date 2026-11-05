namespace RecursiveAI.OneDocument.Layout;

// Turns lines of a formatted paragraph into a layout node at a given position. Page flow and table cells both use it, so a paragraph
// in a cell is the same kind of node as one on the page.
internal static class ParagraphEmitter
{
    /// <summary>
    /// Builds the <see cref="LayoutParagraph"/> for lines <paramref name="from"/> to <paramref name="from"/> + <paramref name="count"/>.
    /// </summary>
    /// <param name="fp">The formatted paragraph.</param>
    /// <param name="from">The first line.</param>
    /// <param name="count">The number of lines.</param>
    /// <param name="left">The x of the text area's left edge in page coordinates.</param>
    /// <param name="top">The top of the block (after the space before when this is the first part).</param>
    /// <param name="first">Whether the block starts with the paragraph's first line.</param>
    /// <param name="last">Whether the block ends with the paragraph's last line.</param>
    /// <param name="bottom">Returns the y below the block.</param>
    /// <param name="breakAfter">Returns whether a line of the block ends with a page break.</param>
    public static LayoutParagraph Build(FormattedParagraph fp, int from, int count, double left, double top, bool first, bool last, out double bottom, out bool breakAfter)
    {
        double y = top + (first ? fp.BorderTopHeight : 0);
        var lp = new LayoutParagraph(fp.Source, fp.Text)
        {
            Label = first ? fp.Label : null,
            HeadingLevel = fp.Heading,
            IsRightToLeft = fp.Rtl,
            ContinuesFromPreviousPage = !first,
            ContinuesOnNextPage = !last,
            Paint = fp.Paint,
        };

        breakAfter = false;
        double lastTop = y;
        for (int i = from; i < from + count; i++)
        {
            FormattedLine fl = fp.Lines[i];
            // A line that continues the row above (text on the far side of a floating object) shares its top and baseline.
            double rowTop = fl.SameRow ? lastTop : y + fl.GapBefore;
            lastTop = rowTop;
            var line = new LayoutLine
            {
                Ascent = fl.Ascent,
                Descent = fl.Descent,
                Baseline = rowTop + fl.Ascent,
                TextStart = fl.TextStart,
                TextLength = fl.TextLength,
                Text = fl.Text,
                IsLastLine = fl.IsLast,
            };
            double minX = double.MaxValue;
            double maxX = double.MinValue;
            foreach (LineItem item in fl.Items)
            {
                var run = new LayoutRun(item.Kind)
                {
                    Text = item.Text,
                    TextStart = item.TextStart,
                    Source = item.Source,
                    Font = item.Font,
                    FontSize = item.Size,
                    BidiLevel = item.Level,
                    Bounds = new LayoutRect(left + item.X, rowTop, item.Width, fl.Height),
                    ClusterStarts = item.ClusterStarts,
                    ClusterX = [.. item.ClusterX.Select(v => v + left)],
                    Paint = item.Paint,
                    Glyphs = item.Glyphs,
                    Math = item.Math,
                    Drawing = item.Drawing,
                };
                line.Add(run);
                minX = Math.Min(minX, left + item.X);
                maxX = Math.Max(maxX, left + item.X + item.Width);
            }

            if (minX > maxX)
            {
                minX = left + fp.Paint.LeftEdge;
                maxX = minX;
            }

            line.Bounds = new LayoutRect(minX, rowTop, maxX - minX, fl.Height);
            lp.Add(line);
            y = Math.Max(y, rowTop + fl.Height);
            if (fl.EndsWithPageBreak)
            {
                breakAfter = true;
            }
        }

        if (last)
        {
            y += fp.BorderBottomHeight;
        }

        if (first)
        {
            lp.Floats.AddRange(fp.Floats);
        }

        lp.Bounds = new LayoutRect(left + fp.Paint.LeftEdge, top, Math.Max(0, fp.Paint.RightEdge - fp.Paint.LeftEdge), y - top);
        bottom = y;
        return lp;
    }
}
