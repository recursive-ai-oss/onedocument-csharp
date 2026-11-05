namespace RecursiveAI.OneDocument.Layout;

internal static partial class ParagraphFormatter
{
    // Cuts the lines of a paragraph when text has to flow around floating objects. A row of the page is cut into the stretches
    // `space` leaves free; each stretch is filled like a line of its own and the stretches of one row share a baseline (SameRow), so the
    // layout tree keeps one line per stretch. The row's height is the tallest of them, and a row is cut again when its height
    // moves it into or out of an object's band.
    private static void BreakLinesAround(
        LayoutContext ctx, FormattedParagraph fp, List<Piece> pieces, List<Atom> atoms, TabStop[] stops, double firstStart, double leftInd, double limit, double width,
        bool rtl, AlignMode align, string lineRule, double lineValue, (double Ascent, double Descent) mark, bool wordWrap, string paragraphText,
        ILineSpace? space, FormattedParagraph? continuation, int keepLines)
    {
        int n = atoms.Count;
        int start = 0;
        bool firstLine = true;
        if (continuation is not null && keepLines > 0 && keepLines <= continuation.Lines.Count)
        {
            for (int i = 0; i < keepLines; i++)
            {
                fp.Lines.Add(continuation.Lines[i]);
            }

            // Resume at the first atom of the text the kept lines did not reach.
            int resumeText = keepLines < continuation.Lines.Count ? continuation.Lines[keepLines].TextStart : paragraphText.Length;
            while (start < n && (atoms[start].TextStart < resumeText))
            {
                start++;
            }

            firstLine = false;
            if (start >= n)
            {
                return;
            }
        }

        double defaultTab = ctx.Compat.DefaultTabStop;
        double yRel = space?.StartOffset ?? 0;
        double lastHeight = mark.Ascent + mark.Descent;
        var segments = new List<(double Start, double End)>();
        int guard = 0;
        do
        {
            if (++guard > 20000)
            {
                break;
            }

            double rowHeight = Math.Max(lastHeight, 1);
            double rowStart = yRel;
            List<FormattedLine>? row = null;
            int rowEnd = start;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (space is not null && !space.Segments(yRel, rowHeight, segments, out double skipTo))
                {
                    yRel = Math.Max(skipTo, yRel + 0.5);
                    attempt = -1;
                    if (++guard > 20000)
                    {
                        break;
                    }

                    continue;
                }

                if (space is null)
                {
                    segments.Clear();
                    segments.Add((0, width));
                }

                row = [];
                int s = start;
                bool first = firstLine;
                // A display equation has a row to itself and takes the widest free stretch of it.
                List<(double Start, double End)> usable = segments;
                if (start < n && atoms[start].Kind == AtomKind.Math && atoms[start].Display && segments.Count > 1)
                {
                    (double Start, double End) widest = segments[0];
                    foreach ((double Start, double End) g in segments)
                    {
                        if (g.End - g.Start > widest.End - widest.Start)
                        {
                            widest = g;
                        }
                    }

                    usable = [widest];
                }

                for (int k = 0; k < usable.Count && (s < n || (n == 0 && k == 0)); k++)
                {
                    if (k > 0 && atoms[s].Kind == AtomKind.Math && atoms[s].Display)
                    {
                        break;
                    }

                    double lineStart = k == 0 ? (first ? firstStart : leftInd) : 0;
                    double segStart = Math.Max(lineStart, usable[k].Start);
                    double segLimit = Math.Min(limit, usable[k].End);
                    if (segLimit - segStart < FloatSpace.MinimumSegment && !(usable.Count == 1 && k == 0))
                    {
                        continue;
                    }

                    int end = FindLineEnd(atoms, pieces, s, segStart, segLimit, stops, leftInd, defaultTab, wordWrap, ctx.Compat, out bool hardBreak, out bool pageBreak);
                    if (end <= s)
                    {
                        end = Math.Min(s + 1, n);
                    }

                    var line = new FormattedLine { EndsWithPageBreak = pageBreak, SameRow = row.Count > 0 };
                    BuildLine(ctx, fp, line, pieces, atoms, s, end, segStart, segLimit, stops, leftInd, defaultTab, rtl, align, hardBreak, end >= n, width, first, lineRule, lineValue, mark, paragraphText);
                    row.Add(line);
                    bool displayLine = end - s == 1 && atoms[s].Kind == AtomKind.Math && atoms[s].Display;
                    s = end;
                    first = false;
                    if (hardBreak || displayLine)
                    {
                        break;
                    }
                }

                rowEnd = s;
                if (row.Count == 0)
                {
                    // Every stretch was too narrow for text: move down and try again.
                    yRel += Math.Max(rowHeight, 1);
                    attempt = -1;
                    continue;
                }

                double h = row.Max(static l => l.Height);
                if (h > rowHeight + 0.01 && attempt < 2 && space is not null)
                {
                    rowHeight = h;
                    continue;
                }

                break;
            }

            if (row is null || row.Count == 0)
            {
                break;
            }

            row[0].GapBefore = Math.Max(0, yRel - rowStart);
            double ascent = row.Max(static l => l.Ascent), descent = row.Max(static l => l.Descent);
            foreach (FormattedLine l in row)
            {
                l.Ascent = ascent;
                l.Descent = descent;
                fp.Lines.Add(l);
            }

            lastHeight = ascent + descent;
            yRel += lastHeight;
            start = rowEnd;
            firstLine = false;
        }
        while (start < n);

        if (fp.Lines.Count == 0)
        {
            fp.Lines.Add(new FormattedLine());
        }
    }
}
