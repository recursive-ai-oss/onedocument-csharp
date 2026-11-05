using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Rendering.Drawing;

namespace RecursiveAI.OneDocument.Layout;

public static partial class LayoutEngine
{
    private sealed partial class Flow
    {
        // Sets a frame at its position and registers its box so the text after it wraps. The frame takes no room in the flow.
        // ECMA-376 Part 1 §17.3.1.11: hAnchor and vAnchor say what x and y are measured from; wrap says how text goes round it.
        private void PlaceFrame(FormattedFrameBlock block)
        {
            if (NeedsPage)
            {
                StartPage();
                if (Stopped)
                {
                    return;
                }
            }

            FrameInfo fi = block.Frame;
            LayoutPage page = _page!;
            double colWidth = _textWidth;
            var formatted = new List<FormattedParagraph>();
            double width = fi.Width ?? 0;
            if (fi.Width is null)
            {
                // Without a stated width the frame is as wide as its text wants to be, up to the column.
                foreach (Paragraph p in block.Paragraphs)
                {
                    FormattedParagraph natural = ParagraphFormatter.Format(p, ctx, 30000);
                    foreach (FormattedLine l in natural.Lines)
                    {
                        foreach (LineItem it in l.Items)
                        {
                            width = Math.Max(width, it.X + it.Width);
                        }
                    }
                }

                width = Math.Min(colWidth, Math.Max(width + 0.5, 1));
            }

            double after = 0, height = 0;
            foreach (Paragraph p in block.Paragraphs)
            {
                FormattedParagraph fp = ParagraphFormatter.Format(p, ctx, width);
                SnapToGrid(fp);
                height += (formatted.Count == 0 ? fp.SpaceBefore : after + fp.SpaceBefore) + Total(fp, 0, fp.Lines.Count);
                after = fp.SpaceAfter;
                formatted.Add(fp);
            }

            if (fi.Height is { } h && fi.HeightRule == "exact")
            {
                height = h;
            }
            else if (fi.Height is { } hmin && fi.HeightRule == "atLeast")
            {
                height = Math.Max(height, hmin);
            }

            LayoutRect area = page.ContentArea;
            (double bx, double bw) = fi.HAnchor switch
            {
                "page" => (0.0, page.Width),
                "margin" => (area.X, area.Width),
                _ => (_colX, _textWidth),
            };
            (double by, double bh) = fi.VAnchor switch
            {
                "page" => (0.0, page.Height),
                "margin" => (area.Y, area.Height),
                _ => (_y + Gap(formatted[0]), 0.0),
            };
            bool odd = (page.PrintedNumber & 1) == 1 || page.PrintedNumber == 0;
            double x = fi.XAlign switch
            {
                "center" => bx + ((bw - width) / 2),
                "right" => bx + bw - width,
                "outside" => odd ? bx + bw - width : bx,
                "inside" => odd ? bx : bx + bw - width,
                "left" => bx,
                _ => bx + (fi.X ?? 0),
            };
            double y = fi.YAlign switch
            {
                "center" => by + ((bh - height) / 2),
                "bottom" or "outside" => by + bh - height,
                "top" or "inside" => by,
                _ => by + (fi.Y ?? 0),
            };
            if (y + height > page.Height)
            {
                y = Math.Max(0, page.Height - height);
            }

            double cursor = y;
            after = 0;
            for (int i = 0; i < formatted.Count; i++)
            {
                FormattedParagraph fp = formatted[i];
                double top = cursor + (i == 0 ? fp.SpaceBefore : after + fp.SpaceBefore);
                LayoutParagraph lp = ParagraphEmitter.Build(fp, 0, fp.Lines.Count, x, top, true, true, out double bottom, out _);
                lp.Story = _story;
                lp.Column = _colIndex;
                page.Add(lp);
                cursor = bottom;
                after = fp.SpaceAfter;
            }

            var rect = new LayoutRect(x, y, width, height);
            if (_story == LayoutStory.Body && fi.WrapKind != DrawingWrap.None)
            {
                page.FloatBoxes.Add(new FloatBox { Rect = rect, Wrap = fi.WrapKind, DistanceLeft = fi.SpaceH, DistanceRight = fi.SpaceH, DistanceTop = fi.SpaceV, DistanceBottom = fi.SpaceV });
            }

            _pageHasContent = true;
            _honourSpaceBefore = true;
        }

        // A drop cap: the capital is set at the left of the paragraph that follows it, its box is something the first lines go round, and
        // its baseline is the baseline of the last of the lines it spans. ECMA-376 Part 1 §17.3.1.11 (dropCap: drop, margin; lines).
        private FloatBox? DropCapBox(FormattedParagraph fp, double y0, out LayoutRect capRect, out double capTop)
        {
            capRect = default;
            capTop = y0;
            if (fp.DropCap is not { } cap)
            {
                return null;
            }

            FormattedLine capLine = cap.Paragraph.Lines.Count > 0 ? cap.Paragraph.Lines[0] : new FormattedLine();
            double capWidth = 0;
            foreach (LineItem it in capLine.Items)
            {
                capWidth = Math.Max(capWidth, it.X + it.Width);
            }

            FormattedLine first = fp.Lines.Count > 0 ? fp.Lines[0] : capLine;
            double lineHeight = first.Height > 0 ? first.Height : capLine.Height;
            capTop = y0 + (((cap.Info.Lines - 1) * lineHeight) + first.Ascent) - capLine.Ascent;
            double left = cap.Info.DropCap == "margin" ? _colX - capWidth - cap.Info.SpaceH : _colX;
            capRect = new LayoutRect(left, capTop, capWidth, capLine.Height);
            if (cap.Info.DropCap == "margin")
            {
                return null;
            }

            double bottom = y0 + (cap.Info.Lines * lineHeight);
            return new FloatBox { Rect = new LayoutRect(left, y0, capWidth, bottom - y0), Wrap = DrawingWrap.Square, DistanceRight = cap.Info.SpaceH, WrapText = "right" };
        }
    }
}
