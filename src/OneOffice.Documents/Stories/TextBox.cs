using System.Globalization;
using System.Linq;
using RecursiveAI.OneOffice.Markup;
using RecursiveAI.OneOffice.Schema;

namespace RecursiveAI.OneOffice.Documents;

/// <summary>Which markup dialect a text box is written in.</summary>
public enum TextBoxKind
{
    /// <summary>A DrawingML text box: <c>w:txbxContent</c> inside <c>wps:txbx</c> of a <c>wps:wsp</c> shape.</summary>
    DrawingMl = 0,

    /// <summary>A VML text box: <c>w:txbxContent</c> inside <c>v:textbox</c> of a <c>v:shape</c>, <c>v:rect</c> and the like.</summary>
    Vml = 1,
}

/// <summary>The part of a document a story lives in.</summary>
public enum StoryKind
{
    /// <summary>The main story (<c>w:body</c>).</summary>
    Body = 0,

    /// <summary>A header part.</summary>
    Header = 1,

    /// <summary>A footer part.</summary>
    Footer = 2,

    /// <summary>The footnotes part, taken as one running story.</summary>
    Footnotes = 3,

    /// <summary>The endnotes part, taken as one running story.</summary>
    Endnotes = 4,

    /// <summary>The comments part, taken as one running story.</summary>
    Comments = 5,

    /// <summary>The content of a text box.</summary>
    TextBox = 6,
}

/// <summary>One shape of a text-box chain: the shape, the paragraph it is anchored in, and the content it holds when it holds any.</summary>
/// <param name="Shape">The shape element (<c>wps:wsp</c>, <c>v:shape</c>, <c>v:rect</c>, …).</param>
/// <param name="Anchor">The paragraph that holds the shape, or null when it sits outside any paragraph.</param>
/// <param name="Story">The text box whose content this shape shows. A DrawingML chain keeps all its text in the first shape, so the
/// following shapes have none (null); a VML chain gives every shape its own content.</param>
public readonly record struct TextBoxFrame(MarkupRef Shape, Paragraph? Anchor, TextBox? Story);

/// <summary>
/// The content of a text box: the blocks inside one <c>w:txbxContent</c>. It is a <see cref="BlockContainer"/> like a table cell,
/// so paragraphs, tables, edits and tracked changes work on it as they do in the body. Paragraphs of a text box are never part of
/// the paragraphs of the container that holds the shape; reach them here.
/// </summary>
/// <remarks>
/// <para>Word writes a DrawingML text box twice: inside <c>mc:AlternateContent</c> a <c>mc:Choice</c> with the DrawingML shape and
/// a <c>mc:Fallback</c> with a VML copy for readers that do not know DrawingML. This class exposes the story of the branch a modern
/// reader takes (the first <c>mc:Choice</c> whose required namespaces are understood, ECMA-376 Part 3 §10.2.4) and keeps the other
/// copy as <see cref="Alternate"/>. When the exposed story is edited, <see cref="Document.CommitChanges"/> copies it over the
/// alternate so both branches stay equal; <see cref="SyncAlternate"/> does the same at once.</para>
/// </remarks>
public sealed class TextBox : BlockContainer
{
    private MarkupRef _alternate;
    private string? _snapshot;

    internal TextBox(Document owner, MarkupRef content, MarkupRef alternate)
        : base(owner, content)
    {
        _alternate = alternate;
        MarkupNode parent = content.Node.Parent;
        Kind = parent.IsElement(TextBoxScanner.VmlNs, "textbox") ? TextBoxKind.Vml : TextBoxKind.DrawingMl;
        bool wrapped = Kind == TextBoxKind.Vml || parent.IsElement(TextBoxScanner.WpsNs, "txbx");
        Shape = new MarkupRef(parent.IsNull ? default : wrapped ? parent.Parent : parent);
        Anchor = TextBoxScanner.AnchorOf(owner, content.Node);
        if (alternate.IsValid)
        {
            _snapshot = content.Node.OuterXml;
        }
    }

    /// <summary>Gets whether the box is DrawingML or VML.</summary>
    public TextBoxKind Kind { get; }

    /// <summary>Gets the shape that carries the text box (<c>wps:wsp</c>, <c>v:shape</c>, <c>v:rect</c>, …); invalid when the content has no recognisable shape.</summary>
    public MarkupRef Shape { get; }

    /// <summary>Gets the paragraph that holds the shape, or null when none does (for example a shape in a header's drawing layer).</summary>
    public Paragraph? Anchor { get; }

    /// <summary>
    /// Gets the story the shape's anchor paragraph sits in: the body, a header or footer, the footnotes, endnotes or comments.
    /// </summary>
    public StoryKind HostStory => TextBoxScanner.HostOf(Owner, Container.Node);

    /// <summary>Gets the name of the part the text box is stored in.</summary>
    public string PartName => Owner.Parts.PartNameOf(Container.Node) ?? Owner.MainPartName;

    /// <summary>
    /// Gets the same story as the other branch of the enclosing <c>mc:AlternateContent</c> (the VML copy of a DrawingML box), or an
    /// invalid reference when the box is not inside alternate content or the other branch has no matching box.
    /// </summary>
    public MarkupRef Alternate
    {
        get
        {
            // Edits such as accepting a change can replace the alternate's elements; look the copy up again then.
            if (_alternate.IsValid && !TextBoxScanner.IsAttached(_alternate.Node) && TextBoxScanner.IsAttached(Container.Node))
            {
                MarkupNode again = TextBoxScanner.AlternateOf(Container.Node);
                _alternate = again.IsNull ? default : new MarkupRef(again);
            }

            return _alternate;
        }
    }

    /// <summary>Gets the text of the box's paragraphs, joined by line feeds.</summary>
    public string Text => string.Join('\n', Paragraphs().Select(p => p.Text));

    /// <summary>
    /// Gets the shapes of the chain this box belongs to, in reading order; a box that is not linked has one frame, its own.
    /// DrawingML chains are read from <c>wps:linkedTxbx</c> ([MS-ODRAWXML]), VML chains from the
    /// <c>mso-next-textbox</c> style property of <c>v:textbox</c>.
    /// </summary>
    public IReadOnlyList<TextBoxFrame> Chain => TextBoxScanner.ChainOf(this);

    /// <summary>Gets the id that ties the shapes of a DrawingML chain together (<c>id</c> of <c>wps:txbx</c>); null for VML boxes and for DrawingML boxes without the attribute.</summary>
    public long? ChainId =>
        Kind == TextBoxKind.DrawingMl && Container.Node.Parent is { IsNull: false } p && p.IsElement(TextBoxScanner.WpsNs, "txbx")
        && long.TryParse(p.GetAttribute("id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long id) ? id : null;

    /// <summary>
    /// Copies the exposed story over its <see cref="Alternate"/> so both branches say the same. Nothing happens when there is no
    /// alternate or when the story was not changed since the box was found. Call it after editing; <see cref="Document.CommitChanges"/> calls it for every box found.
    /// </summary>
    /// <returns>True when the alternate was rewritten.</returns>
    public bool SyncAlternate()
    {
        if (!_alternate.IsValid || _snapshot is null || !TextBoxScanner.IsAttached(Container.Node))
        {
            return false;
        }

        MarkupRef alternate = Alternate;
        if (!alternate.IsValid)
        {
            return false;
        }

        string now = Container.Node.OuterXml;
        if (now == _snapshot)
        {
            return false;
        }

        alternate.Node.ReplaceWithXml(HubXml.Bytes(now));
        _snapshot = now;
        return true;
    }
}

/// <summary>Finds text boxes below an element.</summary>
internal static class TextBoxScanner
{
    public const string WpsNs = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape";
    public const string VmlNs = "urn:schemas-microsoft-com:vml";

    private readonly record struct Found(MarkupNode Content, MarkupNode Alternate);

    public static List<TextBox> In(Document owner, MarkupNode parent)
    {
        var found = new List<Found>();
        Collect(parent, found);
        var boxes = new List<TextBox>(found.Count);
        foreach (Found f in found)
        {
            boxes.Add(owner.TextBoxFor(new MarkupRef(f.Content), f.Alternate.IsNull ? default : new MarkupRef(f.Alternate)));
        }

        return boxes;
    }

    private static void Collect(MarkupNode parent, List<Found> into)
    {
        for (MarkupNode n = parent.FirstChild; !n.IsNull; n = n.NextSibling)
        {
            if (n.Kind != MarkupNodeKind.Element)
            {
                continue;
            }

            if (IsContent(n))
            {
                // A box inside this box belongs to this box's own TextBoxes().
                into.Add(new Found(n, default));
            }
            else if (MarkupCompatibility.IsAlternateContent(n))
            {
                MarkupNode primary = MarkupCompatibility.SelectAlternateContent(n, IsUnderstood);
                var chosen = new List<Found>();
                var others = new List<Found>();
                foreach (MarkupNode branch in n.ChildElements)
                {
                    if (MarkupCompatibility.IsChoice(branch) || MarkupCompatibility.IsFallback(branch))
                    {
                        Collect(branch, branch == primary ? chosen : others);
                    }
                }

                for (int i = 0; i < chosen.Count; i++)
                {
                    into.Add(chosen[i].Alternate.IsNull && i < others.Count ? chosen[i] with { Alternate = others[i].Content } : chosen[i]);
                }
            }
            else
            {
                Collect(n, into);
            }
        }
    }

    private static bool IsContent(MarkupNode n) => HubXml.IsWordNamespace(n.NamespaceUri) && n.LocalName.SequenceEqual("txbxContent"u8);

    // The namespaces Word 2010 and later read: the standard's, VML and Microsoft's Office extensions. Same rule as text extraction.
    private static bool IsUnderstood(string uri) =>
        uri.StartsWith("http://schemas.microsoft.com/office/", StringComparison.Ordinal)
        || uri.StartsWith("http://schemas.openxmlformats.org/", StringComparison.Ordinal)
        || uri.StartsWith("http://purl.oclc.org/ooxml/", StringComparison.Ordinal)
        || uri == VmlNs;

    /// <summary>Tells whether the node is still part of its tree (removed and replaced nodes are not).</summary>
    public static bool IsAttached(MarkupNode node)
    {
        MarkupNode n = node;
        while (!n.Parent.IsNull)
        {
            n = n.Parent;
        }

        return n.Kind == MarkupNodeKind.Document;
    }

    /// <summary>Finds the copy of a box in the other branch of its enclosing <c>mc:AlternateContent</c>, pairing by order.</summary>
    public static MarkupNode AlternateOf(MarkupNode content)
    {
        for (MarkupNode branch = content.Parent; !branch.IsNull; branch = branch.Parent)
        {
            MarkupNode ac = branch.Parent;
            if (ac.IsNull || !MarkupCompatibility.IsAlternateContent(ac))
            {
                continue;
            }

            var mine = new List<Found>();
            Collect(branch, mine);
            int index = mine.FindIndex(f => f.Content == content);
            if (index < 0)
            {
                return default;
            }

            var others = new List<Found>();
            foreach (MarkupNode other in ac.ChildElements)
            {
                if (other != branch && (MarkupCompatibility.IsChoice(other) || MarkupCompatibility.IsFallback(other)))
                {
                    Collect(other, others);
                }
            }

            return index < others.Count ? others[index].Content : default;
        }

        return default;
    }

    public static Paragraph? AnchorOf(Document owner, MarkupNode from)
    {
        for (MarkupNode n = from.Parent; !n.IsNull; n = n.Parent)
        {
            if (n.Kind == MarkupNodeKind.Element && HubXml.IsWordNamespace(n.NamespaceUri) && n.LocalName.SequenceEqual("p"u8))
            {
                return new Paragraph(owner, new MarkupRef(n));
            }
        }

        return null;
    }

    public static StoryKind HostOf(Document owner, MarkupNode node)
    {
        MarkupNode root = node.Document is { } d ? d.Root : default;
        if (root.IsNull)
        {
            return StoryKind.Body;
        }

        return root.LocalNameString switch
        {
            "hdr" => StoryKind.Header,
            "ftr" => StoryKind.Footer,
            "footnotes" => StoryKind.Footnotes,
            "endnotes" => StoryKind.Endnotes,
            "comments" => StoryKind.Comments,
            _ => StoryKind.Body,
        };
    }

    public static IReadOnlyList<TextBoxFrame> ChainOf(TextBox box)
    {
        var self = new TextBoxFrame(box.Shape, box.Anchor, box);
        MarkupNode root = box.Container.Node.Document is { } d ? d.Root : default;
        if (root.IsNull)
        {
            return [self];
        }

        return box.Kind == TextBoxKind.DrawingMl ? DrawingChain(box, self, root) : VmlChain(box, root);
    }

    private static List<TextBoxFrame> DrawingChain(TextBox box, TextBoxFrame self, MarkupNode root)
    {
        if (box.ChainId is not { } id)
        {
            return [self];
        }

        // [MS-ODRAWXML] wps:linkedTxbx: shapes after the first name the chain by id and their place by seq (the first shape is seq 0).
        var followers = new List<(long Seq, MarkupNode Shape)>();
        Walk(root);
        followers.Sort((a, b) => a.Seq.CompareTo(b.Seq));
        var list = new List<TextBoxFrame> { self };
        foreach ((_, MarkupNode shape) in followers)
        {
            list.Add(new TextBoxFrame(new MarkupRef(shape), AnchorOf(box.Owner, shape), null));
        }

        return list;

        void Walk(MarkupNode parent)
        {
            for (MarkupNode n = parent.FirstChild; !n.IsNull; n = n.NextSibling)
            {
                if (n.Kind != MarkupNodeKind.Element)
                {
                    continue;
                }

                if (n.IsElement(WpsNs, "linkedTxbx"))
                {
                    if (long.TryParse(n.GetAttribute("id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long cid) && cid == id
                        && !n.Parent.IsNull && n.Parent.Parent is { IsNull: false } shape)
                    {
                        long.TryParse(n.GetAttribute("seq"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long seq);
                        followers.Add((seq, shape));
                    }
                }
                else
                {
                    Walk(n);
                }
            }
        }
    }

    private static List<TextBoxFrame> VmlChain(TextBox box, MarkupNode root)
    {
        // VML links boxes with "mso-next-textbox:#<shape id>" in the style of v:textbox.
        var byShapeId = new Dictionary<string, MarkupNode>(StringComparer.Ordinal);
        var next = new Dictionary<MarkupNode, string>();
        Walk(root);
        MarkupNode start = box.Container.Node.Parent;
        if (start.IsNull)
        {
            return [new TextBoxFrame(box.Shape, box.Anchor, box)];
        }

        // Walk back to the first box, then forward; a visited set stops a cyclic chain.
        var seen = new HashSet<MarkupNode> { start };
        MarkupNode head = start;
        while (true)
        {
            MarkupNode prev = default;
            string? shapeId = head.Parent.IsNull ? null : head.Parent.GetAttribute("id");
            if (shapeId is not null)
            {
                foreach (KeyValuePair<MarkupNode, string> kv in next)
                {
                    if (kv.Value == shapeId && !seen.Contains(kv.Key))
                    {
                        prev = kv.Key;
                        break;
                    }
                }
            }

            if (prev.IsNull)
            {
                break;
            }

            seen.Add(prev);
            head = prev;
        }

        var frames = new List<TextBoxFrame>();
        var visited = new HashSet<MarkupNode>();
        for (MarkupNode cur = head; !cur.IsNull && visited.Add(cur);)
        {
            MarkupNode content = default;
            for (MarkupNode c = cur.FirstChild; !c.IsNull; c = c.NextSibling)
            {
                if (c.Kind == MarkupNodeKind.Element && IsContent(c))
                {
                    content = c;
                    break;
                }
            }

            TextBox? story = content.IsNull ? null : box.Owner.TextBoxFor(new MarkupRef(content), default);
            MarkupNode shape = cur.Parent;
            frames.Add(new TextBoxFrame(new MarkupRef(shape), AnchorOf(box.Owner, cur), story));
            cur = next.TryGetValue(cur, out string? target) && byShapeId.TryGetValue(target, out MarkupNode nxt) ? nxt.FirstChild.IsNull ? default : TextboxOf(nxt) : default;
        }

        return frames;

        void Walk(MarkupNode parent)
        {
            for (MarkupNode n = parent.FirstChild; !n.IsNull; n = n.NextSibling)
            {
                if (n.Kind != MarkupNodeKind.Element)
                {
                    continue;
                }

                if (n.IsElement(VmlNs, "textbox"))
                {
                    if (n.Parent.GetAttribute("id") is { } sid)
                    {
                        byShapeId[sid] = n.Parent;
                    }

                    if (n.GetAttribute("style") is { } style && NextTarget(style) is { } t)
                    {
                        next[n] = t;
                    }
                }

                Walk(n);
            }
        }
    }

    private static MarkupNode TextboxOf(MarkupNode shape)
    {
        for (MarkupNode c = shape.FirstChild; !c.IsNull; c = c.NextSibling)
        {
            if (c.Kind == MarkupNodeKind.Element && c.IsElement(VmlNs, "textbox"))
            {
                return c;
            }
        }

        return default;
    }

    private static string? NextTarget(string style)
    {
        const string Key = "mso-next-textbox:";
        int i = style.IndexOf(Key, StringComparison.Ordinal);
        if (i < 0)
        {
            return null;
        }

        string rest = style[(i + Key.Length)..].TrimStart();
        int end = rest.IndexOf(';');
        string v = (end < 0 ? rest : rest[..end]).Trim();
        return v.StartsWith('#') ? v[1..] : v.Length > 0 ? v : null;
    }
}
