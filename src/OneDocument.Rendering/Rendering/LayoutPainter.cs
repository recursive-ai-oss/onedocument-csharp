using System.Numerics;
using OneRender.Painting;
using RecursiveAI.OneDocument.Layout;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Schema;
using RecursiveAI.OneDocument.Rendering.Drawing;

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

    /// <summary>
    /// Gets or sets the pictures of the rendering. Pass the same object to every page and to the PDF or image backend as its image resolver
    /// (<see cref="DrawingImages"/> is one); with null, pictures are decoded per page and a backend cannot draw them. Default null.
    /// </summary>
    public DrawingImages? Images { get; set; }

    /// <summary>Gets or sets a list that receives notes about drawing features that were approximated or left out (each code once per page). Default null.</summary>
    public IList<DrawingNote>? DrawingNotes { get; set; }
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

        private readonly Dictionary<DrawingSources, DrawingContext> _drawingContexts = [];

        public DisplayList List { get; } = new();

        private DrawingContext ContextFor(DrawingSources sources)
        {
            if (!_drawingContexts.TryGetValue(sources, out DrawingContext? c))
            {
                c = new DrawingContext(new ColorResolver(sources.Theme, sources.Map), options.Images, sources.ImageBytes, sources.Scope) { Tagged = options.Tagged };
                DrawingContext captured = c;
                c.TextPainter = request => PaintShapeText(request, sources, captured);
                c.Fonts = sources.Layout?.Fonts;
                c.ChartPainter = (rel, list, x, y, w, h) =>
                {
                    if (sources.Chart(rel) is not { } chart)
                    {
                        return false;
                    }

                    RecursiveAI.OneDocument.Charts.ChartHost.Paint(list, chart.Chart, x, y, w, h, chart.Theme, sources.Layout?.Fonts);
                    return true;
                };
                _drawingContexts[sources] = c;
            }

            return c;
        }

        private void FlushDrawingNotes()
        {
            if (options.DrawingNotes is null)
            {
                return;
            }

            foreach (DrawingContext c in _drawingContexts.Values)
            {
                foreach (DrawingNote n in c.Notes)
                {
                    if (!options.DrawingNotes.Any(x => x.Code == n.Code))
                    {
                        options.DrawingNotes.Add(n);
                    }
                }
            }
        }

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

            FlushDrawingNotes();
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

                if (node is LayoutNoteBlock noteBlock)
                {
                    if (listOpen)
                    {
                        List.EndTag();
                        listOpen = false;
                    }

                    PaintNoteBlock(noteBlock);
                    continue;
                }

                if (node is not LayoutParagraph p)
                {
                    continue;
                }

                if (p.Story is LayoutStory.Header or LayoutStory.Footer)
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

        // A footnote is a Div (OneRender has no Note role yet); separators and the continuation notice are page furniture, so artifacts.
        private void PaintNoteBlock(LayoutNoteBlock block)
        {
            bool furniture = block.Role is LayoutNoteRole.Separator or LayoutNoteRole.ContinuationSeparator or LayoutNoteRole.ContinuationNotice;
            if (furniture)
            {
                _untagged++;
            }

            if (options.Tagged)
            {
                List.BeginTag(Id(), furniture ? TagRole.Artifact : TagRole.Div);
            }

            PaintBlocks(block.Children);
            if (options.Tagged)
            {
                List.EndTag();
            }

            if (furniture)
            {
                _untagged--;
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
            foreach (InlineDrawing f in p.Floats)
            {
                if (f.Info.BehindText && !p.ContinuesFromPreviousPage)
                {
                    PaintAnchored(p, f);
                }
            }

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

            foreach (InlineDrawing f in p.Floats)
            {
                if (!f.Info.BehindText && !p.ContinuesFromPreviousPage)
                {
                    PaintAnchored(p, f);
                }
            }
        }

        // An anchored drawing sits at its own position: an offset or an alignment inside the page, the margins or the paragraph.
        // ECMA-376 Part 1 §20.4.2.10 (positionH) and §20.4.2.11 (positionV). Text does not wrap around it yet.
        private void PaintAnchored(LayoutParagraph p, InlineDrawing d)
        {
            LayoutPage? page = p.Parent as LayoutPage;
            if (page is null)
            {
                return;
            }

            DrawingInfo i = d.Info;
            LayoutRect area = page.ContentArea;
            (double bx, double bw) = i.RelativeFromH switch
            {
                "page" => (0.0, page.Width),
                "character" => (p.Bounds.X, p.Bounds.Width),
                _ => (area.X, area.Width),
            };
            double lineTop = p.Lines.FirstOrDefault()?.Bounds.Y ?? p.Bounds.Y;
            (double by, double bh) = i.RelativeFromV switch
            {
                "page" => (0.0, page.Height),
                "paragraph" => (p.Bounds.Y, p.Bounds.Height),
                "line" => (lineTop, 0.0),
                _ => (area.Y, area.Height),
            };
            double x = i.AlignH switch
            {
                "center" => bx + ((bw - i.Width) / 2),
                "right" or "outside" => bx + bw - i.Width,
                "left" or "inside" => bx,
                _ => bx + (i.OffsetX ?? 0),
            };
            double y = i.AlignV switch
            {
                "center" => by + ((bh - i.Height) / 2),
                "bottom" or "outside" => by + bh - i.Height,
                "top" or "inside" => by,
                _ => by + (i.OffsetY ?? 0),
            };
            DrawingPainter.Draw(List, () => Id(), d.Node, new DrawingBox(x, y, i.Width, i.Height), ContextFor(d.Sources), i.AltText, i.Decorative);
        }

        private int _textDepth;

        // Text inside a Word shape (wps:txbx). ECMA-376 Part 1 §20.1.2.1.1 (bodyPr): the lines are laid out at the text rectangle's width
        // (its height for vertical text), stacked from the top, the middle or the bottom (anchor), and drawn through the shape's transform.
        // Text taller than the box is not clipped; Word clips it at the box, which is reported as a note.
        private void PaintShapeText(ShapeTextRequest req, DrawingSources sources, DrawingContext dctx)
        {
            MarkupNode content = DrawingTheme.Child(DrawingTheme.Child(req.Shape, "txbx"), "txbxContent");
            if (content.IsNull)
            {
                dctx.Note("drawing.text-body-not-painted", "The text body of a presentation or worksheet shape is not painted by the document renderer.");
                return;
            }

            if (sources.Layout is not { } lc || _textDepth > 4)
            {
                return;
            }

            string vert = req.BodyProperties.IsNull ? "horz" : req.BodyProperties.GetAttribute("vert") ?? "horz";
            bool v90 = vert is "vert" or "eaVert" or "mongolianVert" or "wordArtVert";
            bool v270 = vert is "vert270" or "wordArtVertRtl";
            if (vert is "mongolianVert" or "wordArtVert" or "wordArtVertRtl")
            {
                dctx.Note("drawing.text-vertical-approximated", "Word-art and Mongolian vertical text is drawn as ordinary rotated text.");
            }

            bool rotated = v90 || v270;
            double lineWidth = rotated ? req.Height : req.Width;
            double avail = rotated ? req.Width : req.Height;
            if (lineWidth < 1)
            {
                return;
            }

            if (!req.BodyProperties.IsNull && req.BodyProperties.GetAttribute("wrap") == "none")
            {
                dctx.Note("drawing.text-wrap-none-approximated", "Text in a shape that does not wrap is wrapped at the shape's width.");
            }

            var laid = new List<LayoutParagraph>();
            double y = 0, pendingAfter = 0;
            bool first = true;
            foreach (MarkupNode child in content.ChildElements)
            {
                if (child.LocalNameString != "p")
                {
                    if (child.LocalNameString is "tbl" or "sdt")
                    {
                        dctx.Note("drawing.text-block-skipped", "A table or content control inside a text box is not laid out.");
                    }

                    continue;
                }

                FormattedParagraph fp = ParagraphFormatter.Format(lc.Doc.WrapParagraph(new MarkupRef(child)), lc, lineWidth);
                double gap = first ? fp.SpaceBefore : pendingAfter + fp.SpaceBefore;
                laid.Add(ParagraphEmitter.Build(fp, 0, fp.Lines.Count, 0, y + gap, true, true, out double end, out _));
                y = end;
                pendingAfter = fp.SpaceAfter;
                first = false;
            }

            if (laid.Count == 0)
            {
                return;
            }

            if (y > avail + 0.5)
            {
                dctx.Note("drawing.text-overflow", "Text taller than its shape is drawn past the shape's edge; Word clips it.");
            }

            string anchor = req.BodyProperties.IsNull ? "t" : req.BodyProperties.GetAttribute("anchor") ?? "t";
            double shift = anchor switch { "ctr" => Math.Max(0, (avail - y) / 2), "b" => Math.Max(0, avail - y), _ => 0 };
            Matrix3x2 rot = Matrix3x2.Identity;
            if (v90)
            {
                // Lines run top to bottom and stack right to left: (x, y) in the line frame lands at (width - y, x).
                rot = new Matrix3x2(0, 1, -1, 0, (float)req.Width, 0);
            }
            else if (v270)
            {
                rot = new Matrix3x2(0, -1, 1, 0, 0, (float)req.Height);
            }

            Matrix3x2 m = Matrix3x2.CreateScale((float)(1 / K)) * Matrix3x2.CreateTranslation(0, (float)shift) * rot * req.Transform;
            List.PushTransform(Id(), m);
            _textDepth++;
            foreach (LayoutParagraph lp in laid)
            {
                PaintParagraph(lp, false);
            }

            _textDepth--;
            List.PopTransform();
        }

        private void PaintInlineObject(LayoutRun run, LayoutLine line)
        {
            if (run.Drawing is not { } d)
            {
                return;
            }

            var box = new DrawingBox(run.Bounds.X, line.Baseline - d.Info.Height - run.Paint.BaselineShift, d.Info.Width, d.Info.Height);
            DrawingPainter.Draw(List, () => Id(), d.Node, box, ContextFor(d.Sources), d.Info.AltText, d.Info.Decorative);
        }

        // Borders, separators, line numbers and watermarks are page furniture, not content: artifacts.
        private void PaintDecoration(LayoutDecoration d)
        {
            switch (d.DecorationKind)
            {
                case LayoutDecorationKind.PageBorder:
                    PaintBorder(d);
                    break;
                case LayoutDecorationKind.ColumnSeparator or LayoutDecorationKind.NoteSeparator or LayoutDecorationKind.NoteContinuationSeparator:
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

            if (run.RunKind == LayoutRunKind.Drawing)
            {
                PaintInlineObject(run, line);
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
