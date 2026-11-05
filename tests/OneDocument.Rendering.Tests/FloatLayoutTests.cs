using OneRender.Image;
using OneRender.Painting;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Layout;
using RecursiveAI.OneDocument.Rendering.Drawing;

namespace RecursiveAI.OneDocument.Rendering.Tests;

public sealed class FloatLayoutTests
{
    private static LayoutDocument Lay(string name)
    {
        byte[] docx = FloatSamples.All().First(s => s.Name == name).Docx;
        using Document doc = Document.Open(new MemoryStream(docx), leaveOpen: false);
        return LayoutEngine.Layout(doc, new LayoutOptions { Fonts = TestFonts.Library });
    }

    private static IEnumerable<(LayoutLine Line, LayoutRun Run)> TextRuns(LayoutPage page) =>
        page.BodyParagraphs.SelectMany(p => p.Lines).SelectMany(l => l.Runs.Where(r => r.RunKind == LayoutRunKind.Text && r.Text.Trim().Length > 0).Select(r => (l, r)));

    private static IEnumerable<LayoutNode> Walk(LayoutNode n) => n.Children.SelectMany(c => Walk(c).Prepend(c));

    private static bool Overlaps(LayoutRect a, LayoutRect b) => a.X < b.Right - 0.01 && a.Right > b.X + 0.01 && a.Y < b.Bottom - 0.01 && a.Bottom > b.Y + 0.01;

    private static LayoutRect LineBand(LayoutLine l) => new(l.Bounds.X, l.Baseline - l.Ascent, Math.Max(l.Bounds.Width, 1), l.Ascent + l.Descent);

    [Theory]
    [InlineData("bothSides")]
    [InlineData("left")]
    [InlineData("right")]
    [InlineData("largest")]
    public void Square_wrap_keeps_text_out_of_the_object_and_fills_the_permitted_sides(string side)
    {
        LayoutDocument layout = Lay("wrap-square-" + side);
        LayoutPage page = layout.Pages[0];
        LayoutFloat f = Assert.Single(page.Floats);
        Assert.Equal(DrawingWrap.Square, f.Wrap);
        Assert.Equal(212, f.Bounds.X, 1);
        LayoutRect r = f.Bounds;
        var inBand = TextRuns(page).Where(t => t.Line.Baseline - t.Line.Ascent < r.Bottom && t.Line.Baseline + t.Line.Descent > r.Y).ToList();
        Assert.NotEmpty(inBand);
        foreach ((LayoutLine line, LayoutRun run) in inBand)
        {
            Assert.False(Overlaps(run.Bounds with { Y = line.Baseline - line.Ascent, Height = line.Ascent + line.Descent }, r), "text overlaps the object: " + run.Text);
        }

        bool left = inBand.Any(t => t.Run.Bounds.Right <= r.X - 5);
        bool right = inBand.Any(t => t.Run.Bounds.X >= r.Right + 5);
        switch (side)
        {
            case "bothSides":
                Assert.True(left && right);
                Assert.Contains(page.BodyParagraphs.SelectMany(p => p.Lines), l => l.Bounds.Y < r.Bottom && IsContinuation(page, l));
                break;
            case "left":
                Assert.True(left);
                Assert.False(right, "no text to the right of the object");
                break;
            case "right":
                Assert.True(right);
                Assert.False(left, "no text to the left of the object");
                break;
            default:
                // The left side is 140 pt wide, the right side 468 - 140 - 150 = 178 pt.
                Assert.True(right);
                Assert.False(left);
                break;
        }

        // Below the object text takes the full width again.
        Assert.Contains(TextRuns(page), t => t.Line.Baseline - t.Line.Ascent >= r.Bottom + 6 - 0.01 && t.Run.Bounds.Right > r.Right + 60);
    }

    private static bool IsContinuation(LayoutPage page, LayoutLine line) =>
        page.BodyParagraphs.SelectMany(p => p.Lines).Any(o => !ReferenceEquals(o, line) && Math.Abs(o.Baseline - line.Baseline) < 0.01 && o.Bounds.X > line.Bounds.X);

    [Fact]
    public void Top_and_bottom_wrap_leaves_the_band_empty_and_resumes_below()
    {
        LayoutPage page = Lay("wrap-top-bottom").Pages[0];
        LayoutRect r = page.Floats.Single().Bounds;
        Assert.Equal(DrawingWrap.TopAndBottom, page.Floats.Single().Wrap);
        foreach ((LayoutLine line, _) in TextRuns(page).Where(t => t.Line.Baseline > 100))
        {
            double top = line.Baseline - line.Ascent, bottom = line.Baseline + line.Descent;
            Assert.True(bottom <= r.Y - 6 + 0.01 || top >= r.Bottom + 6 - 0.01, $"line {top}-{bottom} is inside the band {r.Y - 6}-{r.Bottom + 6}");
        }

        Assert.Contains(TextRuns(page), t => t.Line.Baseline - t.Line.Ascent >= r.Bottom + 6 - 0.01);
    }

    [Fact]
    public void Tight_wrap_follows_the_polygon_so_the_gap_grows_toward_the_base_of_a_triangle()
    {
        LayoutPage page = Lay("wrap-tight-triangle").Pages[0];
        LayoutRect r = page.Floats.Single().Bounds;
        double centre = r.X + (r.Width / 2);
        var rightRuns = TextRuns(page).Where(t => t.Run.Bounds.X > centre && t.Line.Baseline > r.Y && t.Line.Baseline < r.Bottom).OrderBy(t => t.Line.Baseline).ToList();
        Assert.True(rightRuns.Count >= 3, "text right of the apex");
        double firstStart = rightRuns[0].Run.Bounds.X, lastStart = rightRuns[^1].Run.Bounds.X;
        Assert.True(lastStart > firstStart + 20, $"text starts further right near the base ({firstStart} then {lastStart})");
        // Rectangle wrap would keep every line right of the box.
        Assert.True(firstStart < r.Right - 20, "the first right-hand line tucks in beside the apex");
    }

    [Fact]
    public void A_wrapThrough_object_without_a_polygon_wraps_to_its_own_outline()
    {
        LayoutPage page = Lay("wrap-through-ellipse-no-polygon").Pages[0];
        LayoutRect r = page.Floats.Single().Bounds;
        var rows = TextRuns(page).Where(t => t.Run.Bounds.X > r.X + (r.Width / 2) && t.Line.Baseline > r.Y && t.Line.Baseline < r.Bottom).OrderBy(t => t.Line.Baseline).ToList();
        Assert.True(rows.Count >= 4);
        double top = rows[0].Run.Bounds.X, middle = rows[rows.Count / 2].Run.Bounds.X;
        Assert.True(middle > top + 10, "the ellipse is widest in the middle, so the text is furthest right there");
    }

    [Fact]
    public void Objects_without_wrap_do_not_move_text_and_stack_behind_and_in_front()
    {
        LayoutDocument layout = Lay("wrap-none-behind-front");
        LayoutPage page = layout.Pages[0];
        Assert.Equal(2, page.Floats.Count());
        Assert.All(page.Floats, f => Assert.Equal(DrawingWrap.None, f.Wrap));
        Assert.Contains(TextRuns(page), t => Math.Abs(t.Run.Bounds.X - 72) < 0.5 && t.Line.Baseline > 100);
        Assert.DoesNotContain(page.BodyParagraphs.SelectMany(p => p.Lines), l => IsContinuation(page, l));
        DisplayList list = LayoutPainter.Paint(page, new PaintOptions { Images = new DrawingImages() });
        int behind = -1, front = -1, firstGlyphAfterTitle = -1, lastGlyph = -1;
        for (int i = 0; i < list.Count; i++)
        {
            DisplayItem it = list[i];
            if (it.Kind == ItemKind.BeginTag && it.AltText == "behind")
            {
                behind = i;
            }
            else if (it.Kind == ItemKind.BeginTag && it.AltText == "front")
            {
                front = i;
            }
            else if (it.Kind == ItemKind.GlyphRun)
            {
                firstGlyphAfterTitle = firstGlyphAfterTitle < 0 ? i : firstGlyphAfterTitle;
                lastGlyph = i;
            }
        }

        Assert.True(behind >= 0 && behind < firstGlyphAfterTitle, "the object behind the text is painted before any text");
        Assert.True(front > lastGlyph, "the object in front is painted after all the text");
    }

    [Fact]
    public void Every_base_places_the_object_where_the_standard_says()
    {
        LayoutPage page = Lay("bases").Pages[0];
        LayoutFloat Get(string name) => page.Floats.Single(f => f.AltText == name);
        // Letter page, one-inch margins; page 1 is a right-hand page, so inside is the left margin.
        Assert.Equal(10, Get("h-page").Bounds.X, 1);
        Assert.Equal(82, Get("h-margin").Bounds.X, 1);
        Assert.Equal(82, Get("h-column").Bounds.X, 1);
        Assert.Equal(82, Get("h-character").Bounds.X, 1);
        Assert.Equal(10, Get("h-leftMargin").Bounds.X, 1);
        Assert.Equal(550, Get("h-rightMargin").Bounds.X, 1);
        Assert.Equal(10, Get("h-insideMargin").Bounds.X, 1);
        Assert.Equal(550, Get("h-outsideMargin").Bounds.X, 1);
        Assert.Equal(10, Get("v-page").Bounds.Y, 1);
        Assert.Equal(82, Get("v-margin").Bounds.Y, 1);
        LayoutFloat vp = Get("v-paragraph");
        Assert.Equal(vp.AnchorParagraph!.Bounds.Y + 10, vp.Bounds.Y, 1);
        LayoutFloat vl = Get("v-line");
        Assert.Equal(vl.AnchorParagraph!.Lines.First().Baseline - vl.AnchorParagraph.Lines.First().Ascent + 10, vl.Bounds.Y, 1);
        Assert.Equal(10, Get("v-topMargin").Bounds.Y, 1);
        Assert.Equal(730, Get("v-bottomMargin").Bounds.Y, 1);
        Assert.Equal(10, Get("v-insideMargin").Bounds.Y, 1);
        Assert.Equal(730, Get("v-outsideMargin").Bounds.Y, 1);
    }

    [Fact]
    public void Alignments_percentages_and_simple_positions()
    {
        LayoutPage page = Lay("align-percent-simple").Pages[0];
        LayoutFloat Get(string name) => page.Floats.Single(f => f.AltText == name);
        Assert.Equal(480, Get("right-bottom").Bounds.X, 1);
        Assert.Equal(690, Get("right-bottom").Bounds.Y, 1);
        Assert.Equal(276, Get("page-centre").Bounds.X, 1);
        Assert.Equal(381, Get("page-centre").Bounds.Y, 1);
        Assert.Equal(306, Get("half").Bounds.X, 1);
        Assert.Equal(172, Get("half").Bounds.Y, 1);
        Assert.Equal(20, Get("simple").Bounds.X, 1);
        Assert.Equal(20, Get("simple").Bounds.Y, 1);
    }

    [Fact]
    public void An_object_follows_its_anchor_paragraph_to_the_next_page()
    {
        LayoutDocument layout = Lay("anchor-on-next-page");
        Assert.Equal(2, layout.Pages.Count);
        Assert.Empty(layout.Pages[0].Floats);
        LayoutFloat f = Assert.Single(layout.Pages[1].Floats);
        Assert.True(f.Bounds.Y < 120, "the object sits at the top of the second page beside its paragraph");
        Assert.Contains(TextRuns(layout.Pages[1]), t => t.Run.Bounds.Right <= f.Bounds.X - 5);
    }

    [Fact]
    public void An_object_anchored_in_the_second_column_is_placed_and_wrapped_in_that_column()
    {
        LayoutDocument layout = Lay("two-columns");
        LayoutPage page = layout.Pages[0];
        LayoutFloat f = page.Floats.Single(x => x.AltText == "col2");
        LayoutRect col2 = page.Columns[1];
        Assert.Equal(col2.X + 10, f.Bounds.X, 1);
        Assert.All(TextRuns(page).Where(t => t.Run.Bounds.X > col2.X - 1 && t.Line.Baseline - t.Line.Ascent < f.Bounds.Bottom && t.Line.Baseline > f.Bounds.Y), t => Assert.False(Overlaps(t.Run.Bounds with { Y = t.Line.Baseline - t.Line.Ascent, Height = t.Line.Ascent + t.Line.Descent }, f.Bounds)));
    }

    [Fact]
    public void Text_runs_round_a_floating_table()
    {
        LayoutPage page = Lay("floating-table").Pages[0];
        LayoutTable t = page.Tables.Single();
        var beside = TextRuns(page).Where(x => x.Line.Baseline - x.Line.Ascent < t.Bounds.Bottom + 5 && x.Line.Baseline > t.Bounds.Y).ToList();
        Assert.NotEmpty(beside);
        foreach ((LayoutLine line, LayoutRun run) in beside)
        {
            Assert.False(Overlaps(run.Bounds with { Y = line.Baseline - line.Ascent, Height = line.Ascent + line.Descent }, t.Bounds), "text inside the table's box: " + run.Text);
        }

        Assert.Contains(beside, x => x.Run.Bounds.Right <= t.Bounds.X);
    }

    [Fact]
    public void An_object_in_a_table_cell_is_placed_from_the_cell_unless_layoutInCell_is_off()
    {
        LayoutPage page = Lay("float-in-cell").Pages[0];
        DisplayList list = LayoutPainter.Paint(page, new PaintOptions { Images = new DrawingImages() });
        RgbaImage img = DisplayListRasterizer.Render(list, 816, 1056, new RasterOptions());
        // Fill colour of the sample objects is 9DC3E6.
        (double X0, double Y0, double X1, double Y1)? Find(double xFrom, double xTo)
        {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            for (int y = 0; y < img.Height; y++)
            {
                for (int x = (int)xFrom; x < Math.Min(img.Width, (int)xTo); x++)
                {
                    int i = ((y * img.Width) + x) * 4;
                    if (Math.Abs(img.Pixels[i] - 0x9D) < 4 && Math.Abs(img.Pixels[i + 1] - 0xC3) < 4 && Math.Abs(img.Pixels[i + 2] - 0xE6) < 4)
                    {
                        minX = Math.Min(minX, x);
                        maxX = Math.Max(maxX, x);
                        minY = Math.Min(minY, y);
                        maxY = Math.Max(maxY, y);
                    }
                }
            }

            return maxX < 0 ? null : (minX * 0.75, minY * 0.75, (maxX + 1) * 0.75, (maxY + 1) * 0.75);
        }

        LayoutTable table = page.Tables.Single();
        LayoutTableCell first = Walk(table).OfType<LayoutTableCell>().First();
        var inCell = Find(0, 816 * 0.5);
        Assert.NotNull(inCell);
        // layoutInCell: the margin base is the cell, so the object is 20 pt right of the cell's left edge and 10 pt below its top.
        Assert.InRange(inCell!.Value.X0, first.Bounds.X + 20, first.Bounds.X + 22);
        Assert.InRange(inCell.Value.Y0, first.Bounds.Y + 10, first.Bounds.Y + 12);
        var toPage = Find(816 * 0.5, 816);
        Assert.NotNull(toPage);
        Assert.InRange(toPage!.Value.X0, 400, 402);
        Assert.InRange(toPage.Value.Y0, 300, 302);
    }

    [Fact]
    public void A_drop_cap_sits_left_of_the_first_lines_and_the_text_goes_round_it()
    {
        LayoutPage page = Lay("dropcap").Pages[0];
        LayoutParagraph cap = page.BodyParagraphs.First(p => p.Text == "L");
        LayoutParagraph para = page.BodyParagraphs.First(p => p.Text.StartsWith("orem", StringComparison.Ordinal));
        Assert.Equal(72, cap.Bounds.X, 1);
        var lines = para.Lines.ToList();
        double capRight = cap.Lines.First().Runs.Max(r => r.Bounds.Right);
        // The first three lines start right of the capital, the fourth is back at the margin.
        for (int i = 0; i < 3; i++)
        {
            Assert.True(lines[i].Bounds.X >= capRight - 0.5, $"line {i} starts at {lines[i].Bounds.X}, the capital ends at {capRight}");
        }

        Assert.Equal(72, lines[3].Bounds.X, 1);
        // The capital's baseline is the baseline of the third line.
        Assert.Equal(lines[2].Baseline, cap.Lines.First().Baseline, 1);
        // In the margin: the text is not moved, the capital stands left of the margin.
        LayoutParagraph marginCap = page.BodyParagraphs.Last(p => p.Text == "L");
        Assert.True(marginCap.Lines.First().Runs.Max(r => r.Bounds.Right) <= 72 + 0.5);
    }

    [Fact]
    public void A_text_frame_sits_at_its_position_and_text_wraps_round_it()
    {
        LayoutPage page = Lay("frame").Pages[0];
        var frameParas = page.BodyParagraphs.Where(p => p.Text.StartsWith("Frame", StringComparison.Ordinal) || p.Text.StartsWith("Paragraph two", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, frameParas.Count);
        Assert.Equal(72 + 300, frameParas[0].Bounds.X, 1);
        Assert.True(frameParas[1].Bounds.Y > frameParas[0].Bounds.Y);
        LayoutParagraph body = page.BodyParagraphs.Single(p => p.Text.StartsWith("alpha", StringComparison.Ordinal) || p.Text.Length > 300);
        var frameRect = new LayoutRect(372, frameParas[0].Bounds.Y, 120, 60);
        foreach (LayoutLine l in body.Lines.Where(l => l.Baseline > frameRect.Y && l.Baseline - l.Ascent < frameRect.Bottom))
        {
            Assert.All(l.Runs, r => Assert.True(r.Bounds.Right <= frameRect.X - 8.9 || r.Bounds.X >= frameRect.Right - 0.01 || r.Text.Trim().Length == 0));
        }
    }

    [Fact]
    public void VML_shapes_are_laid_out_painted_and_wrapped_like_drawings()
    {
        LayoutDocument layout = Lay("vml");
        LayoutPage page = layout.Pages[0];
        LayoutFloat f = Assert.Single(page.Floats);
        Assert.Equal(DrawingWrap.Square, f.Wrap);
        Assert.Equal(72 + 160, f.Bounds.X, 1);
        Assert.Equal(130, f.Bounds.Width, 1);
        foreach ((LayoutLine line, LayoutRun run) in TextRuns(page).Where(t => t.Line.Baseline > f.Bounds.Y && t.Line.Baseline - t.Line.Ascent < f.Bounds.Bottom && t.Run.Bounds.Y > 0))
        {
            Assert.False(Overlaps(run.Bounds with { Y = line.Baseline - line.Ascent, Height = line.Ascent + line.Descent }, f.Bounds));
        }

        // The inline shape stands in a line and raises it.
        LayoutLine inlineLine = page.BodyParagraphs.SelectMany(p => p.Lines).First(l => l.Runs.Any(r => r.RunKind == LayoutRunKind.Drawing));
        Assert.True(inlineLine.Ascent >= 40 - 0.01);
        var notes = new List<DrawingNote>();
        DisplayList list = LayoutPainter.Paint(page, new PaintOptions { Images = new DrawingImages(), DrawingNotes = notes });
        int glyphRuns = 0;
        for (int i = 0; i < list.Count; i++)
        {
            glyphRuns += list[i].Kind == ItemKind.GlyphRun ? 1 : 0;
        }

        Assert.True(glyphRuns > 10, "text in both shapes and in the body");
        Assert.DoesNotContain(notes, n => n.Code is "drawing.text-not-painted" or "drawing.text-body-not-painted");
    }

    [Fact]
    public void A_display_equation_beside_an_object_takes_a_row_of_its_own_in_the_widest_stretch()
    {
        string math = "<m:oMathPara xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\"><m:oMath><m:r><m:t>x=1</m:t></m:r></m:oMath></m:oMathPara>";
        string body = "<w:p>" + FloatSamples.Anchor(150, 100, FloatSamples.H("margin", 140), FloatSamples.V("paragraph", 0), FloatSamples.Square()) +
            "<w:r><w:t xml:space=\"preserve\">" + FloatSamples.Words(20) + "</w:t></w:r>" + math + "<w:r><w:t xml:space=\"preserve\">" + FloatSamples.Words(60) + "</w:t></w:r></w:p>";
        using Document doc = Document.Open(new MemoryStream(TestDocx.Bytes(body)), leaveOpen: false);
        LayoutPage page = LayoutEngine.Layout(doc, new LayoutOptions { Fonts = TestFonts.Library }).Pages[0];
        LayoutFloat f = page.Floats.Single();
        LayoutLine eq = page.BodyParagraphs.SelectMany(p => p.Lines).Single(l => l.Runs.Any(r => r.RunKind == LayoutRunKind.Math));
        Assert.Single(eq.Runs);
        Assert.DoesNotContain(page.BodyParagraphs.SelectMany(p => p.Lines), l => !ReferenceEquals(l, eq) && Math.Abs(l.Baseline - eq.Baseline) < 0.01);
        LayoutRun m = eq.Runs.First();
        Assert.False(Overlaps(m.Bounds with { Y = eq.Baseline - eq.Ascent, Height = eq.Ascent + eq.Descent }, f.Bounds) && eq.Baseline > f.Bounds.Y);
    }

    [Fact]
    public void An_object_positioned_from_the_text_stays_above_the_footnote_area()
    {
        // The object would reach the page's bottom margin; the footnote on the page takes the bottom of the column.
        string body = "<w:p>" + FloatSamples.Anchor(100, 60, FloatSamples.H("margin", 300), FloatSamples.V("paragraph", 650), FloatSamples.Square()) +
            "<w:r><w:t>Reference line</w:t></w:r>" + NoteDocs.Ref(1) + "</w:p>";
        byte[] docx = NoteDocs.Build(body, NoteDocs.Fn(1, "A footnote that takes a line or two at the bottom of the page, long enough to need two lines of the narrow text area it is set in."));
        using Document doc = Document.Open(new MemoryStream(docx), leaveOpen: false);
        LayoutPage page = LayoutEngine.Layout(doc, new LayoutOptions { Fonts = TestFonts.Library }).Pages[0];
        LayoutFloat f = page.Floats.Single();
        double noteTop = page.NoteBlocks.Min(b => b.Bounds.Y);
        Assert.True(f.Bounds.Bottom <= noteTop + 0.01, $"object bottom {f.Bounds.Bottom} is below the note area top {noteTop}");
    }

    [Fact]
    public void A_table_in_a_text_box_is_laid_out_and_painted()
    {
        LayoutDocument layout = Lay("textbox-table");
        var notes = new List<DrawingNote>();
        DisplayList list = LayoutPainter.Paint(layout.Pages[0], new PaintOptions { Images = new DrawingImages(), DrawingNotes = notes });
        Assert.DoesNotContain(notes, n => n.Code == "drawing.text-block-skipped");
        int glyphRuns = 0, tables = 0;
        for (int i = 0; i < list.Count; i++)
        {
            glyphRuns += list[i].Kind == ItemKind.GlyphRun ? 1 : 0;
            tables += list[i].Kind == ItemKind.BeginTag && list[i].Role == TagRole.Table ? 1 : 0;
        }

        Assert.True(glyphRuns >= 6, "the title, the paragraph and four cells are text");
        Assert.Equal(1, tables);
    }

    [Fact]
    public void The_samples_render_to_pdf()
    {
        foreach ((string name, byte[] docx) in FloatSamples.All())
        {
            using Document doc = Document.Open(new MemoryStream(docx), leaveOpen: false);
            using var pdf = new MemoryStream();
            LayoutDocument layout = DocumentRenderer.WritePdf(doc, pdf, new PdfRenderOptions { Layout = new LayoutOptions { Fonts = TestFonts.Library } });
            Assert.True(pdf.Length > 500, name);
            Assert.NotEmpty(layout.Pages);
            if (Environment.GetEnvironmentVariable("ONEDOCUMENT_SAMPLE_DIR") is { } dir)
            {
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, "floats-" + name + ".docx"), docx);
                File.WriteAllBytes(Path.Combine(dir, "floats-" + name + ".pdf"), pdf.ToArray());
            }
        }
    }
}
