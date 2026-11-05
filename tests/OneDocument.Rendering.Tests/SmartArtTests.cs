using OneRender.Image;
using OneRender.Painting;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Layout;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Rendering.Drawing;
using RecursiveAI.OneDocument.Rendering.Presentations;
using RecursiveAI.OneDocument.Rendering.SmartArt;
using static RecursiveAI.OneDocument.Rendering.Tests.SmartArtSamples;

namespace RecursiveAI.OneDocument.Rendering.Tests;

public sealed class SmartArtTests
{
    private const double W = 432, H = 288;
    private static readonly DiagramReferences Refs = new("rIdD1", "rIdL1", "rIdQ1", "rIdC1");

    private static DiagramLayoutResult Generate(string family, IReadOnlyList<Node> roots, double w = W, double h = H, string? docVars = null, string? layoutXml = null)
    {
        byte[] docx = Docx(family, roots, w, h, docVars: docVars, layoutXml: layoutXml);
        using Document doc = Document.Open(new MemoryStream(docx), leaveOpen: false);
        return DiagramHost.Generate(doc.Package, doc.MainPartName, Refs, w, h, fonts: TestFonts.Library) ?? throw new InvalidOperationException("no layout");
    }

    private static List<DiagramShape> Boxes(DiagramLayoutResult r) => [.. r.Shapes.Where(s => !s.IsConnector)];

    private static (double X, double Y) Center(DiagramShape s) => (s.X + (s.Width / 2), s.Y + (s.Height / 2));

    private static bool Overlap(DiagramShape a, DiagramShape b) =>
        a.X < b.X + b.Width - 0.01 && b.X < a.X + a.Width - 0.01 && a.Y < b.Y + b.Height - 0.01 && b.Y < a.Y + a.Height - 0.01;

    private static void InsideFrame(IEnumerable<DiagramShape> shapes, double w = W, double h = H)
    {
        foreach (DiagramShape s in shapes)
        {
            Assert.True(s.X >= -0.01 && s.Y >= -0.01 && s.X + s.Width <= w + 0.01 && s.Y + s.Height <= h + 0.01, $"{s.Name} {s.X},{s.Y} {s.Width}x{s.Height} leaves the frame");
        }
    }

    // ---------------------------------------------------------------- lists (snake)

    [Fact]
    public void A_block_list_places_every_node_once_inside_the_frame_without_overlap()
    {
        DiagramLayoutResult r = Generate("list", Flat("Plan", "Design", "Build", "Test", "Ship"));
        List<DiagramShape> boxes = Boxes(r);
        Assert.Equal(5, boxes.Count);
        Assert.Equal(["Plan", "Design", "Build", "Test", "Ship"], boxes.Select(b => b.Text));
        InsideFrame(boxes);
        for (int i = 0; i < boxes.Count; i++)
        {
            for (int j = i + 1; j < boxes.Count; j++)
            {
                Assert.False(Overlap(boxes[i], boxes[j]), $"{i} overlaps {j}");
            }
        }

        // Every box has the layout's 0.6 aspect and the same size; the group is centred in the frame.
        Assert.All(boxes, b => Assert.Equal(0.6, b.Height / b.Width, 3));
        Assert.All(boxes, b => Assert.Equal(boxes[0].Width, b.Width, 3));
        double minX = boxes.Min(b => b.X), maxX = boxes.Max(b => b.X + b.Width), minY = boxes.Min(b => b.Y), maxY = boxes.Max(b => b.Y + b.Height);
        Assert.Equal((W - maxX), minX, 2);
        Assert.Equal((H - maxY), minY, 2);
        // Reading order: rows go left to right, top to bottom.
        for (int i = 1; i < boxes.Count; i++)
        {
            (double px, double py) = Center(boxes[i - 1]);
            (double cx, double cy) = Center(boxes[i]);
            Assert.True(cy > py + 0.5 || (Math.Abs(cy - py) < 0.5 && cx > px), $"box {i} is out of reading order");
        }
    }

    [Fact]
    public void A_single_node_takes_the_largest_box_of_the_layout_aspect_that_fits()
    {
        DiagramShape only = Assert.Single(Boxes(Generate("list", Flat("Only"))));
        Assert.Equal(432, only.Width, 2);
        Assert.Equal(259.2, only.Height, 2);
        Assert.Equal(W / 2, Center(only).X, 2);
        Assert.Equal(H / 2, Center(only).Y, 2);

        // In a flatter frame the same box is scaled down until its height fits.
        DiagramShape flat = Assert.Single(Boxes(Generate("list", Flat("Only"), 432, 150)));
        Assert.Equal(150, flat.Height, 2);
        Assert.Equal(250, flat.Width, 2);
    }

    [Fact]
    public void The_dir_variable_of_the_data_model_mirrors_a_list()
    {
        List<DiagramShape> normal = Boxes(Generate("list", Flat("A", "B", "C")));
        List<DiagramShape> reversed = Boxes(Generate("list", Flat("A", "B", "C"), docVars: "<dgm:dir val=\"rev\"/>"));
        Assert.Equal(3, reversed.Count);
        Assert.True(Center(normal[0]).X < Center(normal[^1]).X || Center(normal[0]).Y < Center(normal[^1]).Y);
        Assert.Equal(W - Center(normal[0]).X, Center(reversed[0]).X, 2);
    }

    [Fact]
    public void Text_is_fitted_to_the_boxes_with_one_size_for_all_nodes()
    {
        DiagramLayoutResult shortText = Generate("list", Flat("A", "B", "C"));
        DiagramLayoutResult longText = Generate("list", [new Node("A considerably longer sentence that has to shrink to fit in its box"), new Node("Short"), new Node("Medium length text")]);
        // Short text is limited by the box (a 65 pt line would not fit its height), long text by the box's area.
        Assert.All(Boxes(shortText), b => Assert.Equal(Boxes(shortText)[0].FontSize, b.FontSize));
        Assert.InRange(Boxes(shortText)[0].FontSize, 30, 65);
        List<DiagramShape> boxes = Boxes(longText);
        double size = boxes[0].FontSize;
        Assert.InRange(size, 5, Boxes(shortText)[0].FontSize - 5);
        Assert.All(boxes, b => Assert.Equal(size, b.FontSize));

        // The chosen size fits and one point more would not (so the text is as large as the rule allows).
        DiagramShape biggest = boxes[0];
        Assert.True(Fits(biggest, size), "the chosen size fits");
        Assert.False(Fits(biggest, size + 1), "one point more overflows the long text's box");
    }

    // The text rectangle of a rect box less 0.3 x size margins on each side, against the DrawingML formatter the painter uses.
    private static bool Fits(DiagramShape box, double size)
    {
        double margin = 0.3 * size;
        double width = box.Width - (2 * margin), height = box.Height - (2 * margin);
        var para = new ParaSpec { Align = "ctr", LineSpacing = 0.9 };
        para.Runs.Add(new RunSpec { Text = box.Text, Size = size });
        LaidText laid = DiagramText.Layout([para], width, TestFonts.Library, "Calibri");
        return laid.Height <= height + 0.5;
    }

    [Fact]
    public void Nodes_with_children_show_them_as_bullets_one_level_in()
    {
        DiagramLayoutResult r = Generate("list", [new Node("Phase one", new Node("Scope"), new Node("Budget")), new Node("Phase two", new Node("Build"))]);
        List<DiagramShape> boxes = Boxes(r);
        Assert.Equal(2, boxes.Count);
        Assert.Equal("Phase one\nScope\nBudget", boxes[0].Text);
        Assert.Equal("Phase two\nBuild", boxes[1].Text);
    }

    // ---------------------------------------------------------------- process (lin)

    [Fact]
    public void A_process_is_a_row_of_boxes_joined_by_arrows_and_scaled_to_the_frame()
    {
        DiagramLayoutResult r = Generate("process", Flat("Collect", "Process", "Report"));
        List<DiagramShape> boxes = Boxes(r);
        List<DiagramShape> arrows = [.. r.Shapes.Where(s => s.IsConnector)];
        Assert.Equal(3, boxes.Count);
        Assert.Equal(2, arrows.Count);
        Assert.All(arrows, a => Assert.Equal("rightArrow", a.Geometry));
        InsideFrame(r.Shapes);
        Assert.All(boxes, b => Assert.Equal(Center(boxes[0]).Y, Center(b).Y, 2));
        for (int i = 0; i < 2; i++)
        {
            Assert.True(boxes[i].X + boxes[i].Width <= arrows[i].X + 0.01, "arrow starts after its box");
            Assert.True(arrows[i].X + arrows[i].Width <= boxes[i + 1].X + 0.01, "arrow ends before the next box");
        }

        // Three boxes and two arrows 0.4 box wide fill the frame's width exactly.
        Assert.Equal(W, (boxes[^1].X + boxes[^1].Width) - boxes[0].X, 1);
        Assert.Equal(boxes[0].X, W - (boxes[^1].X + boxes[^1].Width), 2);
    }

    [Fact]
    public void A_longer_process_shrinks_its_boxes()
    {
        List<DiagramShape> three = Boxes(Generate("process", Flat("A", "B", "C")));
        List<DiagramShape> six = Boxes(Generate("process", Flat("A", "B", "C", "D", "E", "F")));
        Assert.Equal(6, six.Count);
        Assert.True(six[0].Width < three[0].Width * 0.6);
        InsideFrame(six);
    }

    // ---------------------------------------------------------------- cycle

    [Fact]
    public void A_cycle_puts_its_nodes_on_a_circle_with_the_first_on_top_and_an_arrow_between_neighbours()
    {
        DiagramLayoutResult r = Generate("cycle", Flat("Plan", "Do", "Check", "Act"));
        List<DiagramShape> nodes = Boxes(r);
        List<DiagramShape> arrows = [.. r.Shapes.Where(s => s.IsConnector)];
        Assert.Equal(4, nodes.Count);
        Assert.Equal(4, arrows.Count); // hideLastTrans="0" closes the ring
        InsideFrame(nodes);
        double cx = W / 2, cy = H / 2;
        double[] radii = [.. nodes.Select(n => Math.Sqrt(Math.Pow(Center(n).X - cx, 2) + Math.Pow(Center(n).Y - cy, 2)))];
        Assert.All(radii, rr => Assert.Equal(radii[0], rr, 2));
        Assert.Equal(cx, Center(nodes[0]).X, 2);
        Assert.True(Center(nodes[0]).Y < cy);
        // Clockwise: the second node is to the right of the centre, the third below, the fourth to the left.
        Assert.True(Center(nodes[1]).X > cx && Center(nodes[2]).Y > cy && Center(nodes[3]).X < cx);
        for (int i = 0; i < nodes.Count; i++)
        {
            for (int j = i + 1; j < nodes.Count; j++)
            {
                Assert.False(Overlap(nodes[i], nodes[j]));
            }
        }

        // Each arrow lies between two neighbours: closer to both than to the node across the ring.
        for (int i = 0; i < 4; i++)
        {
            (double ax, double ay) = Center(arrows[i]);
            double toFrom = Dist(Center(nodes[i]), (ax, ay)), toNext = Dist(Center(nodes[(i + 1) % 4]), (ax, ay)), toOther = Dist(Center(nodes[(i + 2) % 4]), (ax, ay));
            Assert.True(toFrom < toOther && toNext < toOther, $"arrow {i}");
        }
    }

    private static double Dist((double X, double Y) a, (double X, double Y) b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    // ---------------------------------------------------------------- hierarchy

    [Fact]
    public void A_hierarchy_puts_parents_above_their_children_and_joins_them_with_connectors()
    {
        var tree = new Node("Director", new Node("Sales", new Node("North"), new Node("South")), new Node("Support", new Node("Tier 1"), new Node("Tier 2")));
        DiagramLayoutResult r = Generate("hierarchy", [tree]);
        List<DiagramShape> nodes = Boxes(r);
        List<DiagramShape> links = [.. r.Shapes.Where(s => s.IsConnector)];
        Assert.Equal(7, nodes.Count);
        Assert.Equal(6, links.Count);
        InsideFrame(r.Shapes);
        DiagramShape By(string text) => nodes.Single(n => n.Text == text);
        DiagramShape director = By("Director"), sales = By("Sales"), support = By("Support");
        Assert.True(director.Y + director.Height < sales.Y);
        Assert.Equal(sales.Y, support.Y, 2);
        Assert.True(sales.Y + sales.Height < By("North").Y);
        Assert.Equal(By("North").Y, By("Tier 2").Y, 2);
        // Each parent sits over the middle of its children.
        Assert.Equal((Center(sales).X + Center(support).X) / 2, Center(director).X, 1);
        Assert.Equal((Center(By("North")).X + Center(By("South")).X) / 2, Center(sales).X, 1);
        for (int i = 0; i < nodes.Count; i++)
        {
            for (int j = i + 1; j < nodes.Count; j++)
            {
                Assert.False(Overlap(nodes[i], nodes[j]), $"{nodes[i].Text} overlaps {nodes[j].Text}");
            }
        }

        // A connector runs from the bottom of a parent to the top of a child.
        Assert.Contains(links, l => Math.Abs(l.Y - (director.Y + director.Height)) < 0.5);
    }

    [Fact]
    public void A_wide_tree_is_scaled_down_to_the_frame()
    {
        var kids = Enumerable.Range(1, 8).Select(i => new Node("Child " + i)).ToArray();
        DiagramLayoutResult r = Generate("hierarchy", [new Node("Root", kids)]);
        InsideFrame(r.Shapes);
        Assert.Equal(9, Boxes(r).Count);
        List<DiagramShape> leaves = [.. Boxes(r).Where(b => b.Text.StartsWith("Child", StringComparison.Ordinal))];
        for (int i = 1; i < leaves.Count; i++)
        {
            Assert.True(leaves[i].X >= leaves[i - 1].X + leaves[i - 1].Width - 0.01);
        }
    }

    // ---------------------------------------------------------------- pyramid and relationship

    [Fact]
    public void A_pyramid_stacks_a_triangle_and_trapezoids_that_continue_its_sides()
    {
        DiagramLayoutResult r = Generate("pyramid", Flat("Vision", "Strategy", "Tactics", "Operations"));
        List<DiagramShape> levels = Boxes(r);
        Assert.Equal(4, levels.Count);
        Assert.Equal("triangle", levels[0].Geometry);
        Assert.All(levels.Skip(1), l => Assert.Equal("trapezoid", l.Geometry));
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(W * (i + 1) / 4, levels[i].Width, 2);
            Assert.Equal(H / 4, levels[i].Height, 2);
            Assert.Equal(i * H / 4, levels[i].Y, 2);
            Assert.Equal(W / 2, Center(levels[i]).X, 2);
        }

        Assert.All(levels, l => Assert.True(l.FontSize >= 5));
    }

    [Fact]
    public void A_composite_places_three_overlapping_circles_by_their_constraints()
    {
        DiagramLayoutResult r = Generate("venn", Flat("Design", "Code", "Test"));
        List<DiagramShape> c = Boxes(r);
        Assert.Equal(3, c.Count);
        Assert.All(c, s => Assert.Equal("ellipse", s.Geometry));
        Assert.All(c, s => Assert.Equal(0.55 * H, s.Width, 2));
        Assert.All(c, s => Assert.Equal(s.Width, s.Height, 2));
        Assert.Equal(0.5 * W, Center(c[0]).X, 2);
        Assert.Equal(0.3 * H, Center(c[0]).Y, 2);
        Assert.Equal(0.38 * W, Center(c[1]).X, 2);
        Assert.Equal(0.62 * W, Center(c[2]).X, 2);
        Assert.True(Overlap(c[0], c[1]) && Overlap(c[0], c[2]) && Overlap(c[1], c[2]));
        Assert.Equal(["Design", "Code", "Test"], c.Select(s => s.Text));
        InsideFrame(c);
    }

    // ---------------------------------------------------------------- colours, definitions

    [Fact]
    public void A_span_colour_list_blends_across_the_shapes_and_repeat_gives_one_colour()
    {
        DiagramLayoutResult plain = Generate("list", Flat("A", "B", "C"));
        string[] fills = [.. Fills(Encoding(plain))];
        Assert.Equal(3, fills.Length);
        Assert.Single(fills.Distinct());

        byte[] docx = Docx("list", Flat("A", "B", "C"), span: true);
        using Document doc = Document.Open(new MemoryStream(docx), leaveOpen: false);
        DiagramLayoutResult span = DiagramHost.Generate(doc.Package, doc.MainPartName, Refs, W, H, fonts: TestFonts.Library)!;
        string[] blended = [.. Fills(Encoding(span))];
        Assert.Equal(3, blended.Distinct().Count());
    }

    private static string Encoding(DiagramLayoutResult r) => System.Text.Encoding.UTF8.GetString(r.Drawing);

    private static IEnumerable<string> Fills(string xml)
    {
        MarkupDocument d = MarkupDocument.Parse(System.Text.Encoding.UTF8.GetBytes(xml));
        foreach (MarkupNode sp in d.Root.ChildElements.First().ChildElements.Where(e => e.LocalNameString == "sp"))
        {
            MarkupNode spPr = sp.ChildElements.First(e => e.LocalNameString == "spPr");
            MarkupNode fill = spPr.ChildElements.FirstOrDefault(e => e.LocalNameString == "solidFill");
            if (!fill.IsNull)
            {
                yield return fill.InnerText + fill.FirstChild.OuterXml;
            }
        }
    }

    [Fact]
    public void A_layout_definition_with_a_forEach_loop_or_unknown_algorithm_never_throws()
    {
        string bad = ListWith("<dgm:forEach name=\"a\" ref=\"a\"/><dgm:forEach name=\"b\" ref=\"c\"/><dgm:forEach name=\"c\" ref=\"b\"/>");
        DiagramLayoutResult r = Generate("list", Flat("A", "B"), layoutXml: bad);
        Assert.NotNull(r);
        string unknown = List().Replace("type=\"snake\"", "type=\"zigzag\"", StringComparison.Ordinal);
        Assert.NotNull(Generate("list", Flat("A", "B"), layoutXml: unknown));
    }

    private static string ListWith(string extraInRoot) => List().Replace("<dgm:ruleLst/>\n          <dgm:forEach name=\"n\"", "<dgm:ruleLst/>" + extraInRoot + "\n          <dgm:forEach name=\"n\"", StringComparison.Ordinal);

    // ---------------------------------------------------------------- the cached path

    private static (DisplayList List, List<DrawingNote> Notes, List<DiagramReport> Reports) PaintDocx(byte[] docx, DiagramRegeneration mode = DiagramRegeneration.WhenMissingOrStale)
    {
        using Document doc = Document.Open(new MemoryStream(docx), leaveOpen: false);
        LayoutDocument layout = LayoutEngine.Layout(doc, new LayoutOptions { Fonts = TestFonts.Library });
        var notes = new List<DrawingNote>();
        var reports = new List<DiagramReport>();
        DisplayList list = LayoutPainter.Paint(layout.Pages[0], new PaintOptions { Images = new DrawingImages(), DrawingNotes = notes, Diagrams = new DiagramOptions { Regeneration = mode, Report = reports.Add } });
        return (list, notes, reports);
    }

    private static int Count(DisplayList list, ItemKind kind)
    {
        int n = 0;
        for (int i = 0; i < list.Count; i++)
        {
            n += list[i].Kind == kind ? 1 : 0;
        }

        return n;
    }

    [Fact]
    public void A_document_with_a_diagram_paints_its_shapes_and_text_as_a_tagged_figure()
    {
        (DisplayList list, List<DrawingNote> notes, List<DiagramReport> reports) = PaintDocx(Docx("list", Flat("Plan", "Design", "Build")));
        DiagramReport report = Assert.Single(reports);
        Assert.Equal(DiagramSource.Regenerated, report.Source);
        Assert.Equal("missing", report.Reason);
        Assert.Equal(3, report.ShapeCount);
        Assert.DoesNotContain(notes, n => n.Code is "drawing.graphic-frame" or "drawing.diagram-not-painted" or "smartart.data-unreadable");
        Assert.True(Count(list, ItemKind.GlyphRun) >= 3, "each node's text is drawn");
        bool figure = false;
        for (int i = 0; i < list.Count; i++)
        {
            figure |= list[i].Kind == ItemKind.BeginTag && list[i].Role == TagRole.Figure && list[i].AltText == "A diagram";
        }

        Assert.True(figure, "the frame is a figure with the frame's alt text");
        list.Validate();
    }

    [Fact]
    public void A_stored_drawing_is_painted_as_it_is_and_a_regenerated_one_looks_the_same()
    {
        Node[] roots = Flat("Plan", "Design", "Build", "Test");
        string cache = CacheFor("list", roots, W, H);
        (DisplayList cachedList, _, List<DiagramReport> cachedReports) = PaintDocx(Docx("list", roots, W, H, cache));
        DiagramReport cr = Assert.Single(cachedReports);
        Assert.Equal(DiagramSource.Cached, cr.Source);
        Assert.Null(cr.Reason);

        (DisplayList forcedList, _, List<DiagramReport> forcedReports) = PaintDocx(Docx("list", roots, W, H, cache), DiagramRegeneration.Always);
        Assert.Equal(DiagramSource.Regenerated, Assert.Single(forcedReports).Source);
        Assert.Equal("forced", forcedReports[0].Reason);
        Assert.Equal(Count(cachedList, ItemKind.GlyphRun), Count(forcedList, ItemKind.GlyphRun));
        Assert.Equal(Count(cachedList, ItemKind.FillPath), Count(forcedList, ItemKind.FillPath));
        Assert.Equal(Raster(cachedList), Raster(forcedList));
    }

    private static byte[] Raster(DisplayList list) => DisplayListRasterizer.Render(list, 816, 600, new RasterOptions()).Pixels.ToArray();

    [Fact]
    public void A_stored_drawing_whose_text_no_longer_matches_the_data_is_regenerated()
    {
        Node[] roots = Flat("Plan", "Design", "Build");
        string cache = CacheFor("list", roots, W, H);
        byte[] edited = Docx("list", Flat("Plan", "Design", "Build and ship"), W, H, cache);
        (_, _, List<DiagramReport> reports) = PaintDocx(edited);
        Assert.Equal(DiagramSource.Regenerated, Assert.Single(reports).Source);
        Assert.Equal("stale", reports[0].Reason);

        (_, List<DrawingNote> notes, List<DiagramReport> never) = PaintDocx(edited, DiagramRegeneration.Never);
        Assert.Equal(DiagramSource.Cached, Assert.Single(never).Source);
        Assert.DoesNotContain(notes, n => n.Code == "smartart.not-painted");
    }

    [Fact]
    public void Without_a_stored_drawing_and_with_layout_off_nothing_is_painted_and_it_is_noted()
    {
        (DisplayList list, List<DrawingNote> notes, List<DiagramReport> reports) = PaintDocx(Docx("list", Flat("A")), DiagramRegeneration.Never);
        Assert.Equal(DiagramSource.None, Assert.Single(reports).Source);
        Assert.Contains(notes, n => n.Code == "smartart.not-painted");
        Assert.Equal(0, Count(list, ItemKind.GlyphRun));
    }

    [Fact]
    public void A_stored_drawing_paints_the_same_pixels_as_the_same_plain_shapes()
    {
        const string Dsp = "http://schemas.microsoft.com/office/drawing/2008/diagram";
        string Sp(string tag, string prst, long x, long y, long w, long h, string fill) =>
            $"<{tag}:sp xmlns:a=\"{A}\"{(tag == "dsp" ? " modelId=\"1\"" : string.Empty)}><{tag}:nvSpPr><{tag}:cNvPr id=\"0\" name=\"\"/><{tag}:cNvSpPr/></{tag}:nvSpPr><{tag}:spPr><a:xfrm><a:off x=\"{x}\" y=\"{y}\"/><a:ext cx=\"{w}\" cy=\"{h}\"/></a:xfrm><a:prstGeom prst=\"{prst}\"><a:avLst/></a:prstGeom><a:solidFill><a:srgbClr val=\"{fill}\"/></a:solidFill><a:ln w=\"25400\"><a:solidFill><a:srgbClr val=\"FFFFFF\"/></a:solidFill></a:ln></{tag}:spPr></{tag}:sp>";
        string cache = $"<dsp:drawing xmlns:dsp=\"{Dsp}\" xmlns:a=\"{A}\"><dsp:spTree><dsp:nvGrpSpPr><dsp:cNvPr id=\"0\" name=\"\"/><dsp:cNvGrpSpPr/></dsp:nvGrpSpPr><dsp:grpSpPr/>" +
            Sp("dsp", "roundRect", 100000, 200000, 1800000, 900000, "4472C4") + Sp("dsp", "ellipse", 2500000, 300000, 1200000, 1200000, "ED7D31") + "</dsp:spTree></dsp:drawing>";
        string plain = $"<p:spTree xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" xmlns:a=\"{A}\"><p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr/>" +
            Sp("p", "roundRect", 100000, 200000, 1800000, 900000, "4472C4") + Sp("p", "ellipse", 2500000, 300000, 1200000, 1200000, "ED7D31") + "</p:spTree>";

        var viaDiagram = new DisplayList();
        uint id = 0;
        var host = new DrawingContext();
        using Document doc = Document.Open(new MemoryStream(Docx("list", Flat("A"), W, H, cache)), leaveOpen: false);
        Assert.True(DiagramHost.Paint(doc.Package, doc.MainPartName, new DiagramPaintRequest(Refs, viaDiagram, () => ItemId.Of(++id), host, 96, 96, 576, 384, null, false), new DiagramOptions { Regeneration = DiagramRegeneration.Never }));
        var viaShapes = new DisplayList();
        uint id2 = 0;
        // The stored drawing is positioned by its frame: its origin is the frame's top-left, here (72, 72) points.
        MarkupDocument tree = MarkupDocument.Parse(System.Text.Encoding.UTF8.GetBytes(plain));
        DrawingPainter.DrawShapes(viaShapes, () => ItemId.Of(++id2), tree.Root, 72, 72, new DrawingContext());
        Assert.Equal(Raster(viaShapes), Raster(viaDiagram));
        Assert.Contains(Raster(viaDiagram), b => b != 255);
    }

    [Fact]
    public void Damaged_diagram_parts_are_reported_and_the_page_still_renders()
    {
        byte[] good = Docx("list", Flat("A", "B"));
        using Document doc = Document.Open(new MemoryStream(good), leaveOpen: false);
        Assert.NotNull(DiagramHost.Generate(doc.Package, doc.MainPartName, Refs, W, H));
        Assert.Null(DiagramHost.Generate(doc.Package, doc.MainPartName, new DiagramReferences("rIdNope", "rIdL1", null, null), W, H));
        Assert.Null(DiagramHost.Generate(doc.Package, doc.MainPartName, new DiagramReferences("rIdD1", "rIdNope", null, null), W, H));
        Assert.Null(DiagramHost.Generate(doc.Package, doc.MainPartName, Refs, 0, 0));
        // The layout part holding something else, or no colours and style: shapes still come out with default colours.
        Assert.NotNull(DiagramHost.Generate(doc.Package, doc.MainPartName, new DiagramReferences("rIdD1", "rIdL1", null, null), W, H));
        Assert.Null(DiagramHost.Generate(doc.Package, doc.MainPartName, new DiagramReferences("rIdL1", "rIdL1", null, null), W, H));

        (_, List<DrawingNote> notes, List<DiagramReport> reports) = PaintDocx(Docx("list", Flat("A"), layoutXml: "<dgm:layoutDef xmlns:dgm=\"" + Dgm + "\"/>"));
        Assert.Equal(DiagramSource.None, Assert.Single(reports).Source);
        Assert.Contains(notes, n => n.Code == "smartart.layout-unreadable");
    }

    [Fact]
    public void Diagrams_are_found_in_a_package()
    {
        using Document doc = Document.Open(new MemoryStream(Docx("cycle", Flat("A", "B", "C"))), leaveOpen: false);
        DiagramLocation loc = Assert.Single(DiagramHost.Find(doc.Package));
        Assert.Equal(doc.MainPartName, loc.HostPart);
        Assert.Equal(Refs, loc.References);
    }

    [Fact]
    public void Every_generated_family_renders_to_a_valid_display_list()
    {
        foreach ((string name, string family, Node[] roots, bool span) in Cases())
        {
            (DisplayList list, List<DrawingNote> notes, List<DiagramReport> reports) = PaintDocx(Docx(family, roots, W, H, null, name, span));
            DiagramReport r = Assert.Single(reports);
            Assert.True(r.Source == DiagramSource.Regenerated && r.ShapeCount >= roots.Length, name);
            Assert.DoesNotContain(notes, n => n.Code.StartsWith("smartart.", StringComparison.Ordinal));
            list.Validate();
        }
    }

    // ---------------------------------------------------------------- slides and sheets

    [Fact]
    public void A_slide_frame_is_painted_through_the_shape_tree_hook_with_the_frames_alt_text()
    {
        byte[] pptx = Pptx("process", Flat("Collect", "Process", "Report"), 648, 300);
        using OpcPackage package = OpcPackage.Open(new MemoryStream(pptx), null, false);
        DiagramLocation loc = Assert.Single(DiagramHost.Find(package));
        Assert.Equal("/ppt/slides/slide1.xml", loc.HostPart);
        Assert.Equal(648, loc.WidthPoints);
        Assert.Equal(300, loc.HeightPoints);

        MarkupDocument slide = MarkupDocument.Parse(package.FindPart(loc.HostPart)!.GetBytes());
        var reports = new List<DiagramReport>();
        var ctx = new DrawingContext { DiagramPainter = DiagramHost.CreatePainter(package, loc.HostPart, new DiagramOptions { Report = reports.Add }) };
        var list = new DisplayList();
        uint id = 0;
        MarkupNode tree = slide.Root.ChildElements.First().ChildElements.First();
        Assert.Equal(1, DrawingPainter.DrawShapes(list, () => ItemId.Of(++id), tree, 0, 0, ctx));
        DiagramReport r = Assert.Single(reports);
        Assert.Equal(DiagramSource.Regenerated, r.Source);
        Assert.Equal(5, r.ShapeCount); // three boxes and two arrows
        Assert.DoesNotContain(ctx.Notes, n => n.Code.StartsWith("drawing.", StringComparison.Ordinal));
        list.Validate();
        bool figure = false;
        for (int i = 0; i < list.Count; i++)
        {
            figure |= list[i].Kind == ItemKind.BeginTag && list[i].Role == TagRole.Figure && list[i].AltText == "A diagram";
        }

        Assert.True(figure);
        // The frame sits at (36, 54) points: nothing is painted left of or above it.
        RgbaImage img = DisplayListRasterizer.Render(list, 960, 720, new RasterOptions());
        for (int y = 0; y < 70; y++)
        {
            for (int x = 0; x < 960; x++)
            {
                Assert.Equal(255, img.Pixels[((y * 960) + x) * 4]);
            }
        }
    }

    [Fact]
    public void The_slide_renderer_paints_stored_drawings_and_regenerates_missing_or_forced_ones()
    {
        Node[] roots = Flat("Collect", "Process", "Report");
        string cache = CacheFor("process", roots, 648, 400);

        PresentationRenderReport Render(byte[] pptx, DiagramRegeneration mode, out int glyphs)
        {
            using RecursiveAI.OneDocument.Presentations.Presentation p = RecursiveAI.OneDocument.Presentations.Presentation.Open(new MemoryStream(pptx), leaveOpen: false);
            using var ms = new MemoryStream();
            PresentationRenderReport report = PresentationRenderer.WritePdf(p, ms, new PresentationRenderOptions { Fonts = TestFonts.Library, Compress = false, Diagrams = new DiagramOptions { Regeneration = mode } });
            string pdf = System.Text.Encoding.Latin1.GetString(ms.ToArray());
            glyphs = pdf.Split("Tj").Length + pdf.Split("TJ").Length - 2;
            return report;
        }

        PresentationRenderReport missing = Render(Pptx("process", roots), DiagramRegeneration.WhenMissingOrStale, out int missingText);
        Assert.Contains(missing.Notes, n => n.Code == "slide.smartart-regenerated");
        Assert.DoesNotContain(missing.Notes, n => n.Code is "slide.smartart-not-painted" or "drawing.graphic-frame");

        PresentationRenderReport cached = Render(Pptx("process", roots, 648, 400, cache), DiagramRegeneration.WhenMissingOrStale, out int cachedText);
        Assert.Contains(cached.Notes, n => n.Code == "slide.smartart-cached");
        Assert.DoesNotContain(cached.Notes, n => n.Code == "slide.smartart-regenerated");

        PresentationRenderReport forced = Render(Pptx("process", roots, 648, 400, cache), DiagramRegeneration.Always, out int forcedText);
        Assert.Contains(forced.Notes, n => n.Code == "slide.smartart-regenerated");
        Assert.Equal(cachedText, forcedText);
        Assert.Equal(missingText, cachedText);
        Assert.True(cachedText >= 3, "each node's text is in the page");
    }

    [Fact]
    public void Without_a_diagram_painter_a_diagram_frame_is_noted_and_the_rest_still_paints()
    {
        byte[] pptx = Pptx("list", Flat("A"));
        using OpcPackage package = OpcPackage.Open(new MemoryStream(pptx), null, false);
        MarkupDocument slide = MarkupDocument.Parse(package.FindPart("/ppt/slides/slide1.xml")!.GetBytes());
        var ctx = new DrawingContext();
        var list = new DisplayList();
        uint id = 0;
        DrawingPainter.DrawShapes(list, () => ItemId.Of(++id), slide.Root.ChildElements.First().ChildElements.First(), 0, 0, ctx);
        Assert.Contains(ctx.Notes, n => n.Code == "drawing.graphic-frame");
    }

    // Writes the generated documents and their renderings (PDF is the page list's tagged output; PNG the first page) to a folder.
    [Fact]
    public void Samples_are_written_when_a_folder_is_given()
    {
        if (Environment.GetEnvironmentVariable("ONEDOCUMENT_SAMPLE_DIR") is not { Length: > 0 } dir)
        {
            return;
        }

        Directory.CreateDirectory(dir);
        foreach ((string name, byte[] docx) in All())
        {
            File.WriteAllBytes(Path.Combine(dir, name + ".docx"), docx);
            (DisplayList list, _, _) = PaintDocx(docx);
            RgbaImage img = DisplayListRasterizer.Render(list, 816, 560, new RasterOptions { Images = new DrawingImages() });
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), img.ToPng());
        }
    }
}
