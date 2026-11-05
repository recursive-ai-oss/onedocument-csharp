using System.Linq;
using System.Globalization;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Schema;
using RecursiveAI.OneDocument.Templating;

namespace RecursiveAI.OneDocument.Presentations.Templating;

/// <summary>What a template unit of a presentation stands for.</summary>
internal enum PptxUnitKind
{
    /// <summary>The start of a slide: an empty unit that lets a block repeat or drop the whole slide.</summary>
    Slide,

    /// <summary>The start of a top-level shape (of the slide or its notes page): an empty unit that lets a block repeat or drop the shape.</summary>
    Shape,

    /// <summary>The start of a table row: an empty unit that lets a block repeat or drop the row.</summary>
    Row,

    /// <summary>One paragraph of a text body.</summary>
    Paragraph,

    /// <summary>The target of an external hyperlink in the paragraph before it, when the target holds a tag.</summary>
    Link,
}

/// <summary>One unit handed to the engine, with where it came from in the presentation.</summary>
internal sealed class PptxUnit
{
    public PptxUnitKind Kind { get; init; }

    /// <summary>Index of the slide in the template's slide order.</summary>
    public int SlideIndex { get; init; }

    /// <summary>True for units of the notes page.</summary>
    public bool Notes { get; init; }

    /// <summary>Unit index of the top-level shape marker this unit sits in (itself for a shape marker); -1 for a slide marker.</summary>
    public int ShapeUnit { get; set; } = -1;

    /// <summary>Unit index of the row marker this unit sits in (itself for a row marker); -1 outside table rows.</summary>
    public int RowUnit { get; set; } = -1;

    /// <summary>The table the row or paragraph is in, as an index; -1 outside tables.</summary>
    public int TableId { get; set; } = -1;

    /// <summary>Slide: the root; Shape: the top-level shape element; Row: <c>a:tr</c>; Paragraph and Link: <c>a:p</c>.</summary>
    public MarkupNode Node { get; init; }

    /// <summary>The text body (<c>txBody</c>) of a paragraph.</summary>
    public MarkupNode Body { get; init; }

    /// <summary>The table (<c>a:tbl</c>) of a row marker.</summary>
    public MarkupNode Table { get; init; }

    /// <summary>The runs, line breaks and fields of a paragraph, one per segment.</summary>
    public List<MarkupNode> SegmentNodes { get; } = [];

    /// <summary>Link: the unit of the paragraph the link belongs to.</summary>
    public int OwnerParagraph { get; init; } = -1;

    /// <summary>Link: the relationship id of the hyperlink.</summary>
    public string? RelationshipId { get; init; }

    /// <summary>Last unit index covered by this unit's slide, shape or row (filled when the slide has been read).</summary>
    public int End { get; set; }

    /// <summary>Shape marker: the hub shape kind.</summary>
    public ShapeKind ShapeKind { get; set; }

    /// <summary>Shape marker: the shape's text holds a block tag, so an empty result removes the shape.</summary>
    public bool Tagged { get; set; }

    /// <summary>Shape marker: the shape has any text.</summary>
    public bool HasText { get; set; }

    /// <summary>Shape marker: the tables the shape holds (node and table id).</summary>
    public List<MarkupNode> Tables { get; } = [];
}

/// <summary>
/// The presentation read as template units: a marker unit per slide, per top-level shape and per table row, and a unit per paragraph (text
/// shapes, groups, table cells, notes). Markers print nothing but let the engine repeat or drop slides, shapes and rows as whole structures.
/// </summary>
internal sealed class PptxTemplateModel : ITemplateScopeResolver
{
    private readonly Presentation _deck;
    private readonly List<Defect> _warnings;
    private readonly string _blockOpen;
    private readonly List<TemplateUnit> _template = [];
    private readonly List<int> _slideStart = [];
    private int _tables;

    public PptxTemplateModel(Presentation deck, TemplateDelimiters delimiters, List<Defect> warnings)
    {
        _deck = deck;
        _warnings = warnings;
        _blockOpen = delimiters.BlockOpen;
    }

    public List<PptxUnit> Units { get; } = [];

    public IReadOnlyList<Slide> Slides { get; private set; } = [];

    /// <summary>Every text body that has paragraph units; the writer rebuilds exactly these.</summary>
    public HashSet<MarkupNode> Bodies { get; } = [];

    /// <summary>Elements that hold a body or a table below them (up to their top-level shape): the writer walks into these and copies the rest.</summary>
    public HashSet<MarkupNode> Affected { get; } = [];

    /// <summary>Every table (<c>a:tbl</c>) the template holds.</summary>
    public HashSet<MarkupNode> TableNodes { get; } = [];

    public TemplateSource Source => new(_template);

    public void Collect(CancellationToken ct)
    {
        Slides = _deck.Slides;
        for (int i = 0; i < Slides.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            Slide slide = Slides[i];
            MarkupRef root = slide.Markup;
            int slideUnit = Add(new PptxUnit { Kind = PptxUnitKind.Slide, SlideIndex = i, Node = root.IsValid ? root.Node : default }, string.Empty, slide.PartName);
            _slideStart.Add(slideUnit);
            if (root.IsValid)
            {
                CollectPart(slide, i, notes: false);
                if (slide.Notes is { } notes)
                {
                    CollectPart(notes, i, notes: true);
                }
            }

            Units[slideUnit].End = Units.Count - 1;
        }
    }

    private int Add(PptxUnit unit, string text, string part, string? detail = null)
    {
        Units.Add(unit);
        _template.Add(new TemplateUnit(text, detail is null ? part : part + " (" + detail + ")"));
        return Units.Count - 1;
    }

    private int AddParagraph(PptxUnit unit, List<TemplateSegment> segments, string part, string detail)
    {
        Units.Add(unit);
        if (segments.Count == 0)
        {
            segments.Add(new TemplateSegment(string.Empty));
        }

        _template.Add(new TemplateUnit(segments, part + " (" + detail + ")"));
        return Units.Count - 1;
    }

    private void CollectPart(PresentationPart part, int slideIndex, bool notes)
    {
        MarkupRef tree = part.ShapeTreeMarkup;
        if (!tree.IsValid)
        {
            return;
        }

        int ordinal = 0;
        foreach (MarkupNode child in tree.Node.ChildElements)
        {
            string local = child.LocalNameString;
            if (local is "nvGrpSpPr" or "grpSpPr" or "extLst")
            {
                continue;
            }

            ordinal++;
            ShapeKind kind = local switch
            {
                "sp" => ShapeKind.Shape,
                "pic" => ShapeKind.Picture,
                "grpSp" => ShapeKind.Group,
                "graphicFrame" => ShapeKind.GraphicFrame,
                "cxnSp" => ShapeKind.Connector,
                "AlternateContent" => ShapeKind.AlternateContent,
                _ => ShapeKind.Other,
            };
            string name = ShapeName(child);
            int shapeUnit = Units.Count;
            var marker = new PptxUnit { Kind = PptxUnitKind.Shape, SlideIndex = slideIndex, Notes = notes, Node = child, ShapeKind = kind, ShapeUnit = shapeUnit };
            Add(marker, string.Empty, part.PartName, "shape '" + name + "'");
            int paragraphs = 0;
            Visit(child, part, marker, shapeUnit, -1, -1, default, notes, slideIndex, name, ref paragraphs);
            marker.End = Units.Count - 1;
            MarkAffected(child, marker);
        }
    }

    // A node is affected when a text body or a table lies below it (inside the same top-level shape): the writer walks through affected nodes and copies the rest.
    private void MarkAffected(MarkupNode top, PptxUnit shape)
    {
        var starts = new List<MarkupNode>(shape.Tables);
        starts.AddRange(_shapeBodies);
        _shapeBodies.Clear();
        foreach (MarkupNode start in starts)
        {
            MarkupNode n = start;
            while (!n.IsNull)
            {
                Affected.Add(n);
                if (n.Equals(top))
                {
                    break;
                }

                n = n.Parent;
            }
        }
    }

    private readonly List<MarkupNode> _shapeBodies = [];

    private static string ShapeName(MarkupNode shape)
    {
        foreach (MarkupNode n in Descendants(shape, 6))
        {
            if (n.LocalNameString == "cNvPr")
            {
                return n.GetAttribute("name") ?? string.Empty;
            }
        }

        return string.Empty;
    }

    // The first few levels below a shape, depth first: enough to find the name of a shape or of an alternate-content wrapper.
    private static IEnumerable<MarkupNode> Descendants(MarkupNode node, int depth)
    {
        if (depth == 0)
        {
            yield break;
        }

        foreach (MarkupNode c in node.ChildElements)
        {
            yield return c;
            foreach (MarkupNode d in Descendants(c, depth - 1))
            {
                yield return d;
            }
        }
    }

    private void Visit(MarkupNode node, PresentationPart part, PptxUnit shape, int shapeUnit, int rowUnit, int tableId, MarkupNode table, bool notes, int slideIndex, string shapeName, ref int paragraphs)
    {
        foreach (MarkupNode child in node.ChildElements)
        {
            string local = child.LocalNameString;
            var r = new MarkupRef(child);
            if (MarkupCompatibility.IsAlternateContent(child))
            {
                foreach (MarkupNode branch in child.ChildElements)
                {
                    if (MarkupCompatibility.IsChoice(branch))
                    {
                        Visit(branch, part, shape, shapeUnit, rowUnit, tableId, table, notes, slideIndex, shapeName, ref paragraphs);
                        break;
                    }
                }
                continue;
            }

            if (r.Namespace == SchemaNamespace.Drawing && local == "tbl")
            {
                int id = _tables++;
                shape.Tables.Add(child);
                TableNodes.Add(child);
                foreach (MarkupNode tr in child.ChildElements)
                {
                    if (tr.LocalNameString != "tr")
                    {
                        continue;
                    }

                    int rowIndex = Units.Count;
                    var row = new PptxUnit { Kind = PptxUnitKind.Row, SlideIndex = slideIndex, Notes = notes, Node = tr, Table = child, ShapeUnit = shapeUnit, RowUnit = rowIndex, TableId = id };
                    Add(row, string.Empty, part.PartName, "shape '" + shapeName + "', table row");
                    foreach (MarkupNode tc in tr.ChildElements)
                    {
                        if (tc.LocalNameString != "tc")
                        {
                            continue;
                        }

                        foreach (MarkupNode body in tc.ChildElements)
                        {
                            if (body.LocalNameString == "txBody")
                            {
                                CollectBody(body, part, shape, shapeUnit, rowIndex, id, notes, slideIndex, shapeName, ref paragraphs);
                            }
                        }
                    }

                    row.End = Units.Count - 1;
                }


                continue;
            }

            if (local == "txBody")
            {
                CollectBody(child, part, shape, shapeUnit, rowUnit, tableId, notes, slideIndex, shapeName, ref paragraphs);
                continue;
            }

            Visit(child, part, shape, shapeUnit, rowUnit, tableId, table, notes, slideIndex, shapeName, ref paragraphs);
        }
    }

    private void CollectBody(MarkupNode body, PresentationPart part, PptxUnit shape, int shapeUnit, int rowUnit, int tableId, bool notes, int slideIndex, string shapeName, ref int paragraphs)
    {
        bool any = false;
        foreach (MarkupNode p in body.ChildElements)
        {
            if (p.LocalNameString != "p" || new MarkupRef(p).Namespace != SchemaNamespace.Drawing)
            {
                continue;
            }

            any = true;
            paragraphs++;
            var unit = new PptxUnit { Kind = PptxUnitKind.Paragraph, SlideIndex = slideIndex, Notes = notes, Node = p, Body = body, ShapeUnit = shapeUnit, RowUnit = rowUnit, TableId = tableId };
            var segments = new List<TemplateSegment>();
            foreach (MarkupNode run in p.ChildElements)
            {
                string rl = run.LocalNameString;
                if (rl is not ("r" or "br" or "fld") || new MarkupRef(run).Namespace != SchemaNamespace.Drawing)
                {
                    continue;
                }

                unit.SegmentNodes.Add(run);
                if (rl == "br")
                {
                    segments.Add(new TemplateSegment("\v"));
                }
                else
                {
                    MarkupRef? t = Pml.A(new MarkupRef(run), "t");
                    segments.Add(new TemplateSegment(t is { } tt ? TextParagraph.DecodeText(tt.Node.InnerText) : string.Empty));
                }
            }

            string text = string.Concat(segments.Select(static s => s.Text));
            if (text.Length > 0 && !string.IsNullOrWhiteSpace(text))
            {
                shape.HasText = true;
                if (text.Contains(_blockOpen, StringComparison.Ordinal))
                {
                    shape.Tagged = true;
                }
            }

            int index = AddParagraph(unit, segments, part.PartName, "shape '" + shapeName + "', paragraph " + paragraphs.ToString(CultureInfo.InvariantCulture));
            foreach ((string relId, string target) in LinkTargets(part, p))
            {
                Add(new PptxUnit { Kind = PptxUnitKind.Link, SlideIndex = slideIndex, Notes = notes, Node = p, Body = body, ShapeUnit = shapeUnit, RowUnit = rowUnit, TableId = tableId, OwnerParagraph = index, RelationshipId = relId }, target, part.PartName, "shape '" + shapeName + "', hyperlink target");
            }
        }

        if (any)
        {
            Bodies.Add(body);
            _shapeBodies.Add(body);
        }
    }

    // The external hyperlinks of a paragraph whose target holds a tag: only these become units.
    private static IEnumerable<(string RelId, string Target)> LinkTargets(PresentationPart part, MarkupNode paragraph)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (MarkupNode run in paragraph.ChildElements)
        {
            foreach (MarkupNode rPr in run.ChildElements)
            {
                foreach (MarkupNode link in rPr.ChildElements)
                {
                    if (link.LocalNameString != "hlinkClick")
                    {
                        continue;
                    }

                    string? id = Pml.RelAttr(new MarkupRef(link), "id");
                    if (string.IsNullOrEmpty(id) || !seen.Add(id))
                    {
                        continue;
                    }

                    foreach (Relationship rel in part.Relationships)
                    {
                        if (rel.Id == id && rel.TargetMode == RelationshipTargetMode.External && Uri.UnescapeDataString(rel.Target).Contains('{', StringComparison.Ordinal))
                        {
                            yield return (id, Uri.UnescapeDataString(rel.Target));
                        }
                    }
                }
            }
        }
    }

    // ---- scopes -------------------------------------------------------------------------------------------------------------------------

    /// <inheritdoc />
    public TemplateUnitRange? Resolve(in TemplateBlockInfo block)
    {
        PptxUnit a = Units[block.OpenUnit];
        PptxUnit c = Units[block.CloseUnit];
        switch (block.Scope)
        {
            case "slide":
                return SlideRange(a, c);
            case "table-row":
                if (RowRange(a, c) is { } rows)
                {
                    return rows;
                }

                Unsupported(block, "scope=table-row needs both tags in cells of one table");
                break;
            case "table":
                if (a.ShapeUnit >= 0 && c.ShapeUnit >= 0 && Units[a.ShapeUnit].Tables.Count > 0)
                {
                    return new TemplateUnitRange(a.ShapeUnit, Units[c.ShapeUnit].End);
                }

                Unsupported(block, "scope=table needs the tags in a table");
                break;
            case "paragraph" or "list-item":
                if (a.Kind == PptxUnitKind.Paragraph && c.Kind == PptxUnitKind.Paragraph && a.ShapeUnit == c.ShapeUnit)
                {
                    return new TemplateUnitRange(block.OpenUnit, TrailingLinks(block.CloseUnit));
                }

                break;
            case "inline":
                return null;
            case null:
                break;
            default:
                Unsupported(block, "scope=" + block.Scope + " does not apply to presentations");
                break;
        }

        return Infer(block, a, c);
    }

    private int TrailingLinks(int paragraphUnit)
    {
        int last = paragraphUnit;
        while (last + 1 < Units.Count && Units[last + 1].Kind == PptxUnitKind.Link && Units[last + 1].OwnerParagraph == paragraphUnit)
        {
            last++;
        }

        return last;
    }

    private TemplateUnitRange SlideRange(PptxUnit a, PptxUnit c) =>
        new(_slideStart[a.SlideIndex], Units[_slideStart[c.SlideIndex]].End);

    private TemplateUnitRange? RowRange(PptxUnit a, PptxUnit c) =>
        a.RowUnit >= 0 && c.RowUnit >= 0 && a.TableId == c.TableId ? new TemplateUnitRange(a.RowUnit, Units[c.RowUnit].End) : null;

    // Without a scope the structure decides: tags in different slides (or a slide and its notes) repeat slides, in different shapes repeat shapes,
    // in different cells or rows of a table repeat rows, and tags in one text body repeat the paragraphs between them.
    private TemplateUnitRange? Infer(in TemplateBlockInfo block, PptxUnit a, PptxUnit c)
    {
        if (block.OpenUnit == block.CloseUnit)
        {
            return null;
        }

        if (a.SlideIndex != c.SlideIndex || a.Notes != c.Notes)
        {
            return SlideRange(a, c);
        }

        if (a.ShapeUnit != c.ShapeUnit)
        {
            return new TemplateUnitRange(a.ShapeUnit, Units[c.ShapeUnit].End);
        }

        if (a.RowUnit >= 0 && c.RowUnit >= 0 && (a.RowUnit != c.RowUnit || !a.Body.Equals(c.Body)))
        {
            return RowRange(a, c);
        }

        return null;
    }

    private void Unsupported(in TemplateBlockInfo block, string why) =>
        _warnings.Add(new Defect(DefectKind.Unsupported, "pptx.template-scope-unsupported", DefectSeverity.Dropped, why + "; the scope the structure implies is used.", DefectLocation.InPart(PartOf(block.OpenUnit))));

    public string PartOf(int unit)
    {
        PptxUnit u = Units[unit];
        Slide slide = Slides[u.SlideIndex];
        return u.Notes ? slide.Notes?.PartName ?? slide.PartName : slide.PartName;
    }

}
