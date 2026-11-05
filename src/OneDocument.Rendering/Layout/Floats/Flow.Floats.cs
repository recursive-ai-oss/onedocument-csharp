using RecursiveAI.OneDocument.Rendering.Drawing;

namespace RecursiveAI.OneDocument.Layout;

public static partial class LayoutEngine
{
    private sealed partial class Flow
    {
        // Whether text of this paragraph has to be cut around something: an object already on the page, or one this paragraph anchors.
        private bool NeedsFloatSpace(FormattedParagraph fp)
        {
            if (_story != LayoutStory.Body || _page is null)
            {
                return false;
            }

            if (fp.DropCap is not null)
            {
                return true;
            }

            foreach (InlineDrawing d in fp.Floats)
            {
                if (d.Info.Anchor is { Wrap: not DrawingWrap.None })
                {
                    return true;
                }
            }

            foreach (FloatBox b in _page.FloatBoxes)
            {
                if (b.WrapsBody && b.Wrap != DrawingWrap.None)
                {
                    return true;
                }
            }

            return false;
        }

        private FloatContext ContextFor(FormattedParagraph fp, double y0, InlineDrawing? d)
        {
            LayoutPage page = _page!;
            double paraHeight = 0;
            double lineTop = y0, lineHeight = 0;
            bool found = false;
            foreach (FormattedLine l in fp.Lines)
            {
                if (!found && d is not null && l.TextStart + l.TextLength > d.TextOffset)
                {
                    lineTop = y0 + paraHeight;
                    lineHeight = l.Height;
                    found = true;
                }

                paraHeight += l.Advance;
            }

            if (!found && fp.Lines.Count > 0)
            {
                FormattedLine l = fp.Lines[^1];
                lineTop = y0 + paraHeight - l.Height;
                lineHeight = l.Height;
            }

            double anchorX = _colX + (fp.Rtl ? _textWidth : 0);
            return new FloatContext(page.Width, page.Height, page.ContentArea, _colX, _textWidth, y0, paraHeight, lineTop, lineHeight, anchorX, (page.PrintedNumber & 1) == 1 || page.PrintedNumber == 0);
        }

        // Positions the drawings a paragraph anchors, for a paragraph whose lines start at y0, without adding anything to the page.
        private List<(InlineDrawing Drawing, LayoutRect Rect, FloatBox Box)> PlaceCandidates(FormattedParagraph fp, double y0)
        {
            var list = new List<(InlineDrawing, LayoutRect, FloatBox)>(fp.Floats.Count);
            LayoutPage page = _page!;
            var placed = new List<FloatBox>();
            foreach (InlineDrawing d in fp.Floats)
            {
                FloatContext c = ContextFor(fp, y0, d);
                bool relativeToText = d.Info.RelativeFromV is "paragraph" or "line" or null;
                LayoutRect rect = FloatPlacement.Place(d.Info, c, clampToPage: relativeToText);
                if (d.Info.Anchor is { AllowOverlap: false })
                {
                    // Word moves an object that may not overlap below the ones already there, as far as the page allows.
                    for (int guard = 0; guard < 16; guard++)
                    {
                        FloatBox? hit = null;
                        foreach (FloatBox b in page.FloatBoxes.Concat(placed))
                        {
                            if (!b.AllowOverlap && b.WrapsBody && rect.X < b.Rect.Right && rect.Right > b.Rect.X && rect.Y < b.Rect.Bottom && rect.Bottom > b.Rect.Y)
                            {
                                hit = b;
                                break;
                            }
                        }

                        if (hit is null || hit.Rect.Bottom + rect.Height > page.Height)
                        {
                            break;
                        }

                        rect = rect with { Y = hit.Rect.Bottom + hit.DistanceBottom };
                    }
                }

                FloatBox box = FloatPlacement.Box(d, rect, wrapsBody: _story == LayoutStory.Body);
                placed.Add(box);
                list.Add((d, rect, box));
            }

            return list;
        }

        // The free space for the lines of a paragraph that start at y0, around the objects on the page and the ones the paragraph anchors.
        private FloatSpace BuildSpace(FormattedParagraph fp, double y0, double startOffset, bool includeOwn)
        {
            var boxes = new List<FloatBox>(_page!.FloatBoxes);
            if (includeOwn)
            {
                if (DropCapBox(fp, y0, out _, out _) is { } capBox)
                {
                    boxes.Add(capBox);
                }

                foreach ((_, _, FloatBox box) in PlaceCandidates(fp, y0))
                {
                    boxes.Add(box);
                }
            }

            return new FloatSpace(boxes, y0, startOffset, _colX, _colX + _textWidth, fp.Rtl);
        }

        // Puts the drawings a paragraph anchors on the page the paragraph starts on.
        private void CommitFloats(FormattedParagraph fp, LayoutParagraph lp, double y0)
        {
            if (fp.Floats.Count == 0)
            {
                return;
            }

            LayoutPage page = _page!;
            foreach ((InlineDrawing d, LayoutRect rect, FloatBox box) in PlaceCandidates(fp, y0))
            {
                d.Placed = true;
                var node = new LayoutFloat(d, rect, _story) { AnchorParagraph = lp, Order = page.FloatBoxes.Count };
                page.Add(node);
                page.FloatBoxes.Add(box);
            }
        }
    }
}
