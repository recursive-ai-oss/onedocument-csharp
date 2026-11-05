using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Schema;
using RecursiveAI.OneDocument.Templating;

namespace RecursiveAI.OneDocument.Presentations.Templating;

/// <summary>A rendered paragraph: the pieces the engine produced for one copy of a source paragraph, and the hyperlink targets it printed.</summary>
internal sealed class ParagraphOut(int unit, IReadOnlyList<TemplatePiece> pieces)
{
    public int Unit { get; } = unit;

    public IReadOnlyList<TemplatePiece> Pieces { get; } = pieces;

    /// <summary>Relationship id of a hyperlink in the paragraph, to the target the template printed for this copy.</summary>
    public Dictionary<string, string>? Links { get; set; }
}

/// <summary>One copy of a table row.</summary>
internal sealed class RowOut(int unit)
{
    public int Unit { get; } = unit;

    public Dictionary<MarkupNode, List<ParagraphOut>> Bodies { get; } = [];
}

/// <summary>One copy of a top-level shape.</summary>
internal sealed class ShapeOut(int unit)
{
    public int Unit { get; } = unit;

    public Dictionary<MarkupNode, List<ParagraphOut>> Bodies { get; } = [];

    public List<RowOut> Rows { get; } = [];
}

/// <summary>One slide of the output: copies of the template slide's shapes and notes shapes.</summary>
internal sealed class SlideOut(int source)
{
    public int Source { get; } = source;

    public List<ShapeOut> Shapes { get; } = [];

    public List<ShapeOut> NotesShapes { get; } = [];

    public Slide? Target { get; set; }
}

/// <summary>The relationships being added to one output part while its shapes are written.</summary>
internal sealed class PartContext
{
    private readonly Presentation _deck;
    private readonly List<Relationship> _rels;
    private int _next = 1;

    public PartContext(Presentation deck, string partName, MarkupNode root)
    {
        _deck = deck;
        PartName = partName;
        _rels = [.. deck.Package.GetRelationships(partName)];
        NextShapeId = MaxShapeId(root) + 1;
    }

    public string PartName { get; }

    public int NextShapeId { get; set; }

    public bool Changed { get; private set; }

    /// <summary>Relationship ids of template hyperlinks that were replaced by per-copy ones.</summary>
    public HashSet<string> Rewritten { get; } = [];

    public Dictionary<int, int> Occurrences { get; } = [];

    // ECMA-376 Part 1 §19.3.1.12 (cNvPr): the id identifies the shape within the slide.
    private static int MaxShapeId(MarkupNode root)
    {
        int max = 0;
        string xml = root.OuterXml;
        int at = 0;
        while ((at = xml.IndexOf("cNvPr", at, StringComparison.Ordinal)) >= 0)
        {
            int end = xml.IndexOf('>', at);
            if (end < 0)
            {
                break;
            }

            int id = xml.IndexOf(" id=\"", at, end - at, StringComparison.Ordinal);
            if (id >= 0)
            {
                int q = xml.IndexOf('"', id + 5);
                if (q > id && int.TryParse(xml.AsSpan(id + 5, q - id - 5), NumberStyles.None, CultureInfo.InvariantCulture, out int v) && v > max)
                {
                    max = v;
                }
            }

            at = end;
        }

        return max;
    }

    public string AddRelationship(string type, string target, RelationshipTargetMode mode)
    {
        while (_rels.Exists(r => r.Id == "rId" + _next.ToString(CultureInfo.InvariantCulture)))
        {
            _next++;
        }

        string id = "rId" + _next.ToString(CultureInfo.InvariantCulture);
        _rels.Add(new Relationship(id, type, target, mode));
        Changed = true;
        return id;
    }

    public void Flush(MarkupNode root)
    {
        if (Rewritten.Count > 0)
        {
            // A template hyperlink that no copy uses any more carries tag text as its target; it goes.
            string xml = root.OuterXml;
            foreach (string id in Rewritten)
            {
                if (!xml.Contains("\"" + id + "\"", StringComparison.Ordinal) && _rels.RemoveAll(r => r.Id == id) > 0)
                {
                    Changed = true;
                }
            }
        }

        if (Changed)
        {
            _deck.Package.SetRelationships(PartName, _rels);
        }
    }
}

/// <summary>Where the rows and bodies of one shape or row copy are found while its markup is rewritten.</summary>
internal sealed class EmitContext
{
    public Dictionary<MarkupNode, List<ParagraphOut>> Bodies { get; init; } = [];

    public Dictionary<MarkupNode, List<RowOut>> Rows { get; init; } = [];

    public required PartContext Part { get; init; }
}

/// <summary>
/// Rewrites the shapes of a template slide for each copy the engine produced. Everything the template did not change is copied as written;
/// paragraphs whose text came out as it went in are copied verbatim, others are rebuilt from the pieces with the run properties of the run each
/// piece came from.
/// </summary>
internal sealed class PptxSlideWriter
{
    private readonly PptxTemplateModel _model;
    private readonly Presentation _deck;
    private readonly List<Defect> _warnings;
    private readonly Dictionary<MarkupNode, string> _startTags = [];
    private readonly Dictionary<string, string> _media = new(StringComparer.Ordinal);

    public PptxSlideWriter(PptxTemplateModel model, Presentation deck, List<Defect> warnings)
    {
        _model = model;
        _deck = deck;
        _warnings = warnings;
    }

    // ---- shapes ------------------------------------------------------------------------------------------------------------------------

    /// <summary>The XML of the shapes of one output part, in order, or an empty string when none remain.</summary>
    public string EmitShapes(List<ShapeOut> shapes, PartContext part)
    {
        var sb = new StringBuilder();
        foreach (ShapeOut so in shapes)
        {
            PptxUnit su = _model.Units[so.Unit];
            part.Occurrences.TryGetValue(so.Unit, out int occurrence);
            part.Occurrences[so.Unit] = occurrence + 1;
            sb.Append(EmitShape(su, so, occurrence, part));
        }

        return sb.ToString();
    }

    private string EmitShape(PptxUnit su, ShapeOut so, int occurrence, PartContext part)
    {
        var rows = new Dictionary<MarkupNode, List<RowOut>>();
        foreach (RowOut row in so.Rows)
        {
            MarkupNode table = _model.Units[row.Unit].Table;
            if (!rows.TryGetValue(table, out List<RowOut>? list))
            {
                rows[table] = list = [];
            }

            list.Add(row);
        }

        foreach (MarkupNode table in su.Tables)
        {
            if (!rows.ContainsKey(table))
            {
                // A table with no rows left has nothing to show: it goes away with them.
                return string.Empty;
            }
        }

        if (su.Tables.Count == 0)
        {
            if (TryPicture(su, so, part) is { } picture)
            {
                return occurrence > 0 ? Renumber(picture, part) : picture;
            }

            if (su.Tagged && su.HasText && !HasText(so) && !HasInsertion(so))
            {
                // The shape held only the content of a block that came out empty: it goes with it.
                return string.Empty;
            }
        }

        var cx = new EmitContext { Bodies = so.Bodies, Rows = rows, Part = part };
        string xml = Emit(su.Node, cx);
        return occurrence > 0 ? Renumber(xml, part) : xml;
    }

    private static bool HasText(ShapeOut so)
    {
        foreach (List<ParagraphOut> paragraphs in so.Bodies.Values)
        {
            foreach (ParagraphOut p in paragraphs)
            {
                foreach (TemplatePiece piece in p.Pieces)
                {
                    if (piece.Kind == TemplatePieceKind.Text && !string.IsNullOrWhiteSpace(piece.Text))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool HasInsertion(ShapeOut so)
    {
        foreach (List<ParagraphOut> paragraphs in so.Bodies.Values)
        {
            foreach (ParagraphOut p in paragraphs)
            {
                foreach (TemplatePiece piece in p.Pieces)
                {
                    if (piece.Kind != TemplatePieceKind.Text)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    // A copy of a shape needs ids of its own (ECMA-376 Part 1 §19.3.1.12): the next free ones, in output order, so output is deterministic.
    private static string Renumber(string xml, PartContext part)
    {
        var sb = new StringBuilder(xml.Length + 16);
        int at = 0;
        while (true)
        {
            int c = xml.IndexOf("cNvPr", at, StringComparison.Ordinal);
            if (c < 0)
            {
                break;
            }

            int end = xml.IndexOf('>', c);
            int id = end < 0 ? -1 : xml.IndexOf(" id=\"", c, end - c, StringComparison.Ordinal);
            if (id < 0)
            {
                sb.Append(xml, at, c + 5 - at);
                at = c + 5;
                continue;
            }

            int q = xml.IndexOf('"', id + 5);
            sb.Append(xml, at, id + 5 - at);
            sb.Append(part.NextShapeId.ToString(CultureInfo.InvariantCulture));
            part.NextShapeId++;
            at = q;
        }

        sb.Append(xml, at, xml.Length - at);
        string result = sb.ToString();
        return RenumberRowIds(result, part.NextShapeId);
    }

    // [MS-PPTX] a16:rowId: copies of a row carry the same id unless it is changed; the offset keeps it unique and deterministic.
    private static string RenumberRowIds(string xml, int seed)
    {
        int at = 0;
        var sb = new StringBuilder();
        while (true)
        {
            int r = xml.IndexOf("rowId", at, StringComparison.Ordinal);
            if (r < 0)
            {
                break;
            }

            int v = xml.IndexOf(" val=\"", r, StringComparison.Ordinal);
            int end = xml.IndexOf('>', r);
            if (v < 0 || end < 0 || v > end)
            {
                sb.Append(xml, at, r + 5 - at);
                at = r + 5;
                continue;
            }

            int q = xml.IndexOf('"', v + 6);
            if (q > v && long.TryParse(xml.AsSpan(v + 6, q - v - 6), NumberStyles.None, CultureInfo.InvariantCulture, out long old))
            {
                sb.Append(xml, at, v + 6 - at);
                sb.Append(((old + seed * 7919L) % 4294967295L).ToString(CultureInfo.InvariantCulture));
                at = q;
            }
            else
            {
                sb.Append(xml, at, r + 5 - at);
                at = r + 5;
            }
        }

        sb.Append(xml, at, xml.Length - at);
        return sb.ToString();
    }

    // ---- generic walk ------------------------------------------------------------------------------------------------------------------

    private string Emit(MarkupNode n, EmitContext cx)
    {
        if (_model.Bodies.Contains(n))
        {
            return EmitBody(n, cx.Bodies.TryGetValue(n, out List<ParagraphOut>? paragraphs) ? paragraphs : [], cx);
        }

        if (_model.TableNodes.Contains(n))
        {
            return EmitTable(n, cx.Rows.TryGetValue(n, out List<RowOut>? rows) ? rows : [], cx);
        }

        if (!_model.Affected.Contains(n))
        {
            return n.OuterXml;
        }

        var sb = new StringBuilder(StartTag(n));
        foreach (MarkupNode child in n.Children)
        {
            sb.Append(child.Kind == MarkupNodeKind.Element ? Emit(child, cx) : child.OuterXml);
        }

        sb.Append("</").Append(n.Name).Append('>');
        return sb.ToString();
    }

    private string EmitTable(MarkupNode table, List<RowOut> rows, EmitContext cx)
    {
        var sb = new StringBuilder(StartTag(table));
        foreach (MarkupNode child in table.ChildElements)
        {
            if (child.LocalNameString != "tr")
            {
                sb.Append(child.OuterXml);
            }
        }

        var seen = new Dictionary<int, int>();
        foreach (RowOut row in rows)
        {
            PptxUnit ru = _model.Units[row.Unit];
            var inner = new EmitContext { Bodies = row.Bodies, Part = cx.Part };
            string xml = Emit(ru.Node, inner);
            seen.TryGetValue(row.Unit, out int times);
            seen[row.Unit] = times + 1;
            sb.Append(times > 0 ? RenumberRowIds(xml, cx.Part.NextShapeId + times) : xml);
        }

        sb.Append("</").Append(table.Name).Append('>');
        return sb.ToString();
    }

    private string EmitBody(MarkupNode body, List<ParagraphOut> paragraphs, EmitContext cx)
    {
        var sb = new StringBuilder(StartTag(body));
        string prefix = string.Empty;
        foreach (MarkupNode child in body.Children)
        {
            if (child.Kind == MarkupNodeKind.Element)
            {
                if (child.LocalNameString == "p")
                {
                    if (prefix.Length == 0)
                    {
                        prefix = Encoding.UTF8.GetString(child.Prefix);
                    }

                    continue;
                }

                sb.Append(child.OuterXml);
            }
            else if (!IsBlank(child.OuterXml))
            {
                sb.Append(child.OuterXml);
            }
        }

        foreach (ParagraphOut p in paragraphs)
        {
            sb.Append(EmitParagraph(p, cx));
        }

        if (paragraphs.Count == 0)
        {
            // ECMA-376 Part 1 §21.1.2.1 (txBody): a text body holds at least one paragraph.
            string q = prefix.Length == 0 ? string.Empty : prefix + ":";
            sb.Append('<').Append(q).Append("p/>");
        }

        sb.Append("</").Append(body.Name).Append('>');
        return sb.ToString();
    }

    private static bool IsBlank(string s)
    {
        foreach (char c in s)
        {
            if (!char.IsWhiteSpace(c))
            {
                return false;
            }
        }

        return true;
    }

    private string StartTag(MarkupNode n)
    {
        if (_startTags.TryGetValue(n, out string? cached))
        {
            return cached;
        }

        string x = n.OuterXml;
        char quote = '\0';
        int i = 0;
        for (; i < x.Length; i++)
        {
            char c = x[i];
            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }
            }
            else if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c == '>')
            {
                break;
            }
        }

        string tag = x[..Math.Min(i + 1, x.Length)];
        if (tag.EndsWith("/>", StringComparison.Ordinal))
        {
            tag = tag[..^2] + ">";
        }

        _startTags[n] = tag;
        return tag;
    }

    // ---- paragraphs --------------------------------------------------------------------------------------------------------------------

    private string EmitParagraph(ParagraphOut po, EmitContext cx)
    {
        PptxUnit pu = _model.Units[po.Unit];
        string xml = IsUnchanged(po, pu) ? pu.Node.OuterXml : BuildParagraph(po, pu);
        if (po.Links is { Count: > 0 })
        {
            foreach ((string relId, string target) in po.Links)
            {
                if (target.Length == 0)
                {
                    continue;
                }

                string id = cx.Part.AddRelationship(HyperlinkType, target, RelationshipTargetMode.External);
                cx.Part.Rewritten.Add(relId);
                xml = xml.Replace(":id=\"" + relId + "\"", ":id=\"" + id + "\"", StringComparison.Ordinal);
            }
        }

        return xml;
    }

    private string HyperlinkType => (_deck.IsStrict ? "http://purl.oclc.org/ooxml/officeDocument/relationships/" : "http://schemas.openxmlformats.org/officeDocument/2006/relationships/") + "hyperlink";

    private string ImageType => (_deck.IsStrict ? "http://purl.oclc.org/ooxml/officeDocument/relationships/" : "http://schemas.openxmlformats.org/officeDocument/2006/relationships/") + "image";

    private static string SegmentText(PptxUnit pu, int segment)
    {
        MarkupNode node = pu.SegmentNodes[segment];
        if (node.LocalNameString == "br")
        {
            return "\v";
        }

        MarkupRef? t = Pml.A(new MarkupRef(node), "t");
        return t is { } tt ? TextParagraph.DecodeText(tt.Node.InnerText) : string.Empty;
    }

    // True when the paragraph came out as it went in: the same text in the same segments, nothing inserted.
    private static bool IsUnchanged(ParagraphOut po, PptxUnit pu)
    {
        int s = 0;
        foreach (TemplatePiece piece in po.Pieces)
        {
            if (piece.Kind != TemplatePieceKind.Text)
            {
                return false;
            }

            if (piece.Text.Length == 0)
            {
                continue;
            }

            while (s < pu.SegmentNodes.Count && SegmentText(pu, s).Length == 0 && s != piece.Segment)
            {
                s++;
            }

            if (piece.Segment != s || s >= pu.SegmentNodes.Count || SegmentText(pu, s) != piece.Text)
            {
                return false;
            }

            s++;
        }

        for (; s < pu.SegmentNodes.Count; s++)
        {
            if (SegmentText(pu, s).Length != 0)
            {
                return false;
            }
        }

        return true;
    }

    private string BuildParagraph(ParagraphOut po, PptxUnit pu)
    {
        MarkupNode p = pu.Node;
        string prefix = Encoding.UTF8.GetString(p.Prefix);
        string q = prefix.Length == 0 ? string.Empty : prefix + ":";
        var sb = new StringBuilder(StartTag(p));
        foreach (MarkupNode child in p.ChildElements)
        {
            if (child.LocalNameString == "pPr")
            {
                sb.Append(child.OuterXml);
            }
        }

        var perSegment = new Dictionary<int, int>();
        foreach (TemplatePiece piece in po.Pieces)
        {
            if (piece.Text.Length > 0)
            {
                perSegment.TryGetValue(piece.Segment, out int n);
                perSegment[piece.Segment] = n + 1;
            }
        }

        foreach (TemplatePiece piece in po.Pieces)
        {
            if (piece.Text.Length == 0)
            {
                continue;
            }

            MarkupNode node = piece.Segment >= 0 && piece.Segment < pu.SegmentNodes.Count ? pu.SegmentNodes[piece.Segment] : default;
            if (!node.IsNull && perSegment[piece.Segment] == 1 && SegmentText(pu, piece.Segment) == piece.Text)
            {
                sb.Append(node.OuterXml);
                continue;
            }

            string rPr = string.Empty;
            if (!node.IsNull)
            {
                foreach (MarkupNode c in node.ChildElements)
                {
                    if (c.LocalNameString == "rPr")
                    {
                        rPr = c.OuterXml;
                    }
                }
            }

            AppendRuns(sb, q, rPr, piece.Text);
        }

        foreach (MarkupNode child in p.ChildElements)
        {
            if (child.LocalNameString == "endParaRPr")
            {
                sb.Append(child.OuterXml);
            }
        }

        sb.Append("</").Append(p.Name).Append('>');
        return sb.ToString();
    }

    // Text with line feeds or vertical tabs becomes runs separated by line breaks (ECMA-376 Part 1 §21.1.2.2, a:br).
    private static void AppendRuns(StringBuilder sb, string q, string rPr, string text)
    {
        int start = 0;
        for (int i = 0; i <= text.Length; i++)
        {
            bool end = i == text.Length;
            if (!end && text[i] is not ('\n' or '\v' or '\r'))
            {
                continue;
            }

            if (i > start)
            {
                sb.Append('<').Append(q).Append('r').Append('>').Append(rPr).Append('<').Append(q).Append("t>").Append(Pml.Escape(text[start..i])).Append("</").Append(q).Append("t></").Append(q).Append("r>");
            }

            if (!end)
            {
                if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                sb.Append('<').Append(q).Append("br>").Append(rPr).Append("</").Append(q).Append("br>");
            }

            start = i + 1;
        }
    }

    // ---- pictures ----------------------------------------------------------------------------------------------------------------------

    // An image tag that is all a text shape says replaces the shape by a picture with the shape's id, name, placeholder link and geometry.
    private string? TryPicture(PptxUnit su, ShapeOut so, PartContext part)
    {
        TemplatePiece? image = null;
        bool otherText = false;
        foreach (List<ParagraphOut> paragraphs in so.Bodies.Values)
        {
            foreach (ParagraphOut p in paragraphs)
            {
                foreach (TemplatePiece piece in p.Pieces)
                {
                    if (piece.Kind == TemplatePieceKind.Image)
                    {
                        image ??= piece;
                    }
                    else if (!string.IsNullOrWhiteSpace(piece.Text))
                    {
                        otherText = true;
                    }
                }
            }
        }

        if (image is null)
        {
            return null;
        }

        string location = _model.PartOf(so.Unit);
        if (su.ShapeKind != ShapeKind.Shape || su.Notes || otherText || su.Node.LocalNameString != "sp")
        {
            _warnings.Add(new Defect(DefectKind.Unsupported, "pptx.template-image-position", DefectSeverity.Dropped, "An image tag must be the only content of a shape on a slide; it is left out.", DefectLocation.InPart(location)));
            return null;
        }

        byte[]? bytes = ImageBytes(image.Insertion!.Value.Raw);
        if (bytes is null || !PptxImage.TryRead(bytes, out PptxImage info))
        {
            _warnings.Add(new Defect(DefectKind.Value, "pptx.template-image-invalid", DefectSeverity.Dropped, "The image value is not PNG, JPEG, GIF or BMP data (bytes, or base64 text); the shape is left empty.", DefectLocation.InPart(location)));
            return string.Empty;
        }

        string rid = AddImage(bytes, info, part);
        image.Insertion.Options.TryGetValue("fit", out string? fit);
        image.Insertion.Options.TryGetValue("alt", out string? alt);
        return BuildPicture(su, info, rid, fit ?? "contain", alt);
    }

    private static byte[]? ImageBytes(object? raw)
    {
        switch (raw)
        {
            case byte[] b:
                return b;
            case string s:
                int comma = s.IndexOf(',', StringComparison.Ordinal);
                string b64 = s.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma > 0 ? s[(comma + 1)..] : s;
                Span<byte> buffer = new byte[(b64.Length * 3 / 4) + 3];
                return Convert.TryFromBase64String(b64.Trim(), buffer, out int written) ? buffer[..written].ToArray() : null;
            default:
                return null;
        }
    }

    private string AddImage(byte[] bytes, PptxImage info, PartContext part)
    {
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        if (!_media.TryGetValue(hash, out string? name))
        {
            for (int i = 1; ; i++)
            {
                name = "/ppt/media/image" + i.ToString(CultureInfo.InvariantCulture) + info.Extension;
                if (_deck.Package.FindPart(name) is null)
                {
                    break;
                }
            }

            _deck.Package.AddPart(name, info.ContentType, bytes);
            _media[hash] = name;
        }

        return part.AddRelationship(ImageType, Presentation.RelativeTarget(part.PartName, name), RelationshipTargetMode.Internal);
    }

    private string BuildPicture(PptxUnit su, PptxImage image, string rid, string fit, string? alt)
    {
        MarkupNode sp = su.Node;
        var prefixes = new Prefixes(sp);
        MarkupRef? nv = Pml.P(new MarkupRef(sp), "nvSpPr");
        string cNvPr = nv is { } n && Pml.P(n, "cNvPr") is { } c ? c.Node.OuterXml : "<{p}:cNvPr id=\"0\" name=\"Picture\"/>";
        if (alt is not null && nv is not null)
        {
            cNvPr = SetDescription(cNvPr, alt);
        }

        string nvPr = nv is { } n3 && Pml.P(n3, "nvPr") is { } pr ? pr.Node.OuterXml : "<{p}:nvPr/>";
        MarkupRef? spPr = Pml.P(new MarkupRef(sp), "spPr");
        MarkupRef? xfrm = spPr is { } sr ? Pml.A(sr, "xfrm") : null;
        long? offX = null, offY = null, cx = null, cy = null;
        string xfrmAttrs = string.Empty;
        if (xfrm is { } x && Pml.A(x, "off") is { } off && Pml.A(x, "ext") is { } ext)
        {
            offX = Pml.AttrLong(off, "x");
            offY = Pml.AttrLong(off, "y");
            cx = Pml.AttrLong(ext, "cx");
            cy = Pml.AttrLong(ext, "cy");
            foreach (string a in new[] { "rot", "flipH", "flipV" })
            {
                if (Pml.Attr(x, a) is { } v)
                {
                    xfrmAttrs += " " + a + "=\"" + v + "\"";
                }
            }
        }
        else if (_model.Slides.Count > 0)
        {
            // A placeholder without a transform takes its box from the layout; the picture is given the same box explicitly so it can be fitted.
            foreach (Shape shape in _model.Slides[su.SlideIndex].Shapes)
            {
                if (shape.Markup.Node.Equals(sp) && shape.ResolveBounds() is { } b)
                {
                    offX = b.X;
                    offY = b.Y;
                    cx = b.Width;
                    cy = b.Height;
                    break;
                }
            }
        }

        string srcRect = string.Empty;
        if (offX is { } ox && offY is { } oy && cx is { } w && cy is { } h && w > 0 && h > 0 && image.Width > 0 && image.Height > 0)
        {
            double boxAspect = (double)w / h;
            double imageAspect = (double)image.Width / image.Height;
            if (fit == "contain")
            {
                if (imageAspect > boxAspect)
                {
                    long nh = (long)Math.Round(w / imageAspect);
                    oy += (h - nh) / 2;
                    h = nh;
                }
                else
                {
                    long nw = (long)Math.Round(h * imageAspect);
                    ox += (w - nw) / 2;
                    w = nw;
                }
            }
            else if (fit == "cover")
            {
                if (imageAspect > boxAspect)
                {
                    long crop = (long)Math.Round((1 - (boxAspect / imageAspect)) / 2 * 100000);
                    srcRect = "<{a}:srcRect l=\"" + crop.ToString(CultureInfo.InvariantCulture) + "\" r=\"" + crop.ToString(CultureInfo.InvariantCulture) + "\"/>";
                }
                else
                {
                    long crop = (long)Math.Round((1 - (imageAspect / boxAspect)) / 2 * 100000);
                    srcRect = "<{a}:srcRect t=\"" + crop.ToString(CultureInfo.InvariantCulture) + "\" b=\"" + crop.ToString(CultureInfo.InvariantCulture) + "\"/>";
                }
            }

            offX = ox;
            offY = oy;
            cx = w;
            cy = h;
        }

        var sb = new StringBuilder("<{p}:pic><{p}:nvPicPr>");
        sb.Append(cNvPr);
        sb.Append("<{p}:cNvPicPr><{a}:picLocks noChangeAspect=\"1\"/></{p}:cNvPicPr>");
        sb.Append(nvPr);
        sb.Append("</{p}:nvPicPr><{p}:blipFill><{a}:blip {r}:embed=\"").Append(rid).Append("\"/>").Append(srcRect).Append("<{a}:stretch><{a}:fillRect/></{a}:stretch></{p}:blipFill><{p}:spPr>");
        if (offX is not null)
        {
            sb.Append("<{a}:xfrm").Append(xfrmAttrs).Append("><{a}:off x=\"").Append(offX.Value.ToString(CultureInfo.InvariantCulture)).Append("\" y=\"").Append(offY!.Value.ToString(CultureInfo.InvariantCulture))
                .Append("\"/><{a}:ext cx=\"").Append(cx!.Value.ToString(CultureInfo.InvariantCulture)).Append("\" cy=\"").Append(cy!.Value.ToString(CultureInfo.InvariantCulture)).Append("\"/></{a}:xfrm>");
        }

        string geometry = "<{a}:prstGeom prst=\"rect\"><{a}:avLst/></{a}:prstGeom>";
        if (spPr is { } sr2)
        {
            foreach (MarkupNode g in sr2.Node.ChildElements)
            {
                if (g.LocalNameString is "prstGeom" or "custGeom")
                {
                    geometry = g.OuterXml;
                }
            }
        }

        sb.Append(geometry).Append("</{p}:spPr></{p}:pic>");
        return TextBody.Fill(sb.ToString(), prefixes);
    }

    private static string SetDescription(string cNvPr, string alt)
    {
        string escaped = Pml.Escape(alt);
        int d = cNvPr.IndexOf(" descr=\"", StringComparison.Ordinal);
        int end = cNvPr.IndexOf('>');
        if (d >= 0 && d < end)
        {
            int q = cNvPr.IndexOf('"', d + 8);
            return cNvPr[..(d + 8)] + escaped + cNvPr[q..];
        }

        int insert = cNvPr.IndexOf(' ');
        return insert < 0 || insert > end ? cNvPr : cNvPr.Insert(insert, " descr=\"" + escaped + "\"");
    }

    // ---- slide parts -------------------------------------------------------------------------------------------------------------------

    /// <summary>Writes the new shapes into a part's shape tree in place of the template's shapes.</summary>
    public static void ReplaceShapes(MarkupRef tree, string xml)
    {
        MarkupNode? first = null;
        var old = new List<MarkupNode>();
        MarkupNode? extLst = null;
        foreach (MarkupNode child in tree.Node.ChildElements)
        {
            string local = child.LocalNameString;
            if (local is "nvGrpSpPr" or "grpSpPr")
            {
                continue;
            }

            if (local == "extLst")
            {
                extLst = child;
                continue;
            }

            first ??= child;
            old.Add(child);
        }

        byte[] bytes = Encoding.UTF8.GetBytes(xml);
        if (first is { } f)
        {
            if (bytes.Length > 0)
            {
                f.InsertXmlBefore(bytes);
            }
        }
        else if (extLst is { } e)
        {
            if (bytes.Length > 0)
            {
                e.InsertXmlBefore(bytes);
            }
        }
        else if (bytes.Length > 0)
        {
            tree.Node.AppendXml(bytes);
        }

        foreach (MarkupNode o in old)
        {
            o.Remove();
        }
    }
}

/// <summary>The kind, size and media type of an image, read from its header (the pixels are never decoded).</summary>
internal readonly record struct PptxImage(string Extension, string ContentType, int Width, int Height)
{
    public static bool TryRead(ReadOnlySpan<byte> b, out PptxImage image)
    {
        image = default;
        if (b.Length >= 24 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47)
        {
            // PNG: the IHDR chunk follows the signature; width and height are big-endian.
            image = new PptxImage(".png", "image/png", (int)ReadBe(b, 16), (int)ReadBe(b, 20));
            return true;
        }

        if (b.Length >= 10 && b[0] == 'G' && b[1] == 'I' && b[2] == 'F')
        {
            image = new PptxImage(".gif", "image/gif", b[6] | (b[7] << 8), b[8] | (b[9] << 8));
            return true;
        }

        if (b.Length >= 26 && b[0] == 'B' && b[1] == 'M')
        {
            image = new PptxImage(".bmp", "image/bmp", BitConverter.ToInt32(b[18..22]), Math.Abs(BitConverter.ToInt32(b[22..26])));
            return true;
        }

        if (b.Length >= 4 && b[0] == 0xFF && b[1] == 0xD8)
        {
            // JPEG: the first start-of-frame segment holds the size.
            int i = 2;
            while (i + 9 < b.Length)
            {
                if (b[i] != 0xFF)
                {
                    i++;
                    continue;
                }

                byte marker = b[i + 1];
                if (marker is 0xFF)
                {
                    i++;
                    continue;
                }

                if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC))
                {
                    image = new PptxImage(".jpeg", "image/jpeg", (b[i + 7] << 8) | b[i + 8], (b[i + 5] << 8) | b[i + 6]);
                    return true;
                }

                i += 2 + ((b[i + 2] << 8) | b[i + 3]);
            }

            image = new PptxImage(".jpeg", "image/jpeg", 0, 0);
            return true;
        }

        return false;
    }

    private static uint ReadBe(ReadOnlySpan<byte> b, int at) => ((uint)b[at] << 24) | ((uint)b[at + 1] << 16) | ((uint)b[at + 2] << 8) | b[at + 3];
}
