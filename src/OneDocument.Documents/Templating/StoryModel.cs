using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Templating;

namespace RecursiveAI.OneDocument.Documents.Templating;

internal enum StoryNodeKind
{
    /// <summary>A container of blocks that is not itself repeated by the template scopes: the story root, a footnote, a comment.</summary>
    Group,

    /// <summary>A table (<c>w:tbl</c>).</summary>
    Table,

    /// <summary>A table row (<c>w:tr</c>).</summary>
    Row,

    /// <summary>A table cell (<c>w:tc</c>).</summary>
    Cell,

    /// <summary>A block-level wrapper around blocks: a content control, a tracked change, a custom XML element.</summary>
    Wrapper,

    /// <summary>A paragraph: one template unit.</summary>
    Para,
}

/// <summary>
/// One block-level node of a story. A node keeps the text that opens it (start tag and property children), the text that closes it, the opaque
/// siblings that follow it, and its children; the paragraphs are the template units.
/// </summary>
internal sealed class StoryNode
{
    public StoryNodeKind Kind { get; init; }

    public StoryNode? Parent { get; init; }

    /// <summary>Start tag plus the leading property children (<c>w:tblPr</c>, <c>w:trPr</c>, <c>w:sdtPr</c>); for the story root only the leading children.</summary>
    public string Open { get; set; } = string.Empty;

    public string Close { get; set; } = string.Empty;

    /// <summary>Elements that follow this node but are not blocks (bookmark marks, range marks, <c>w:sectPr</c> after the last block): emitted after the node.</summary>
    public string After { get; set; } = string.Empty;

    public List<StoryNode> Children { get; } = [];

    public ParagraphModel? Paragraph { get; set; }

    /// <summary>The index of the paragraph in the story's units, or -1.</summary>
    public int Unit { get; set; } = -1;

    public int FirstUnit { get; set; } = -1;

    public int LastUnit { get; set; } = -1;

    /// <summary>For a row: it holds a block tag and nothing else, so it exists only to carry the tag and disappears from the output.</summary>
    public bool IsTagRow { get; set; }

    /// <summary>For a paragraph: it holds a section break (<c>w:sectPr</c> in its properties).</summary>
    public bool EndsSection { get; set; }
}

/// <summary>A story of the document read for templating: its block tree, with the paragraphs as units in document order.</summary>
internal sealed class StoryModel
{
    private StoryModel(string partName, string ns, MarkupNode root)
    {
        PartName = partName;
        Ns = ns;
        Root = root;
    }

    public string PartName { get; }

    public string Ns { get; }

    /// <summary>The element whose children make up the story.</summary>
    public MarkupNode Root { get; }

    public StoryNode Tree { get; private set; } = null!;

    /// <summary>The paragraphs in document order.</summary>
    public List<StoryNode> Units { get; } = [];

    /// <summary>The body's final section properties, or empty.</summary>
    public string FinalSectionProperties { get; private set; } = string.Empty;

    /// <summary>Reads a story.</summary>
    /// <param name="root">The element holding the blocks (<c>w:body</c>, <c>w:hdr</c>, <c>w:ftr</c>, <c>w:footnotes</c>, <c>w:txbxContent</c>…).</param>
    /// <param name="partName">The part the element is in.</param>
    /// <param name="ns">The main namespace.</param>
    public static StoryModel Read(MarkupNode root, string partName, string ns)
    {
        var model = new StoryModel(partName, ns, root);
        model.Tree = new StoryNode { Kind = StoryNodeKind.Group };
        model.ReadChildren(root, model.Tree);
        Number(model.Tree);
        return model;
    }

    private void ReadChildren(MarkupNode parent, StoryNode into)
    {
        StoryNode? last = null;
        var head = new System.Text.StringBuilder();
        foreach (MarkupNode c in parent.ChildElements)
        {
            StoryNode? node = Classify(c, into);
            if (node is null)
            {
                string raw = c.OuterXml;
                if (c.IsElement(Ns, "sectPr") && ReferenceEquals(into, Tree))
                {
                    // The body's final section properties must stay last, whether or not any block survives before them.
                    FinalSectionProperties = raw;
                    Tree.Close += raw;
                    continue;
                }

                if (last is null)
                {
                    head.Append(raw);
                }
                else
                {
                    last.After += raw;
                }

                continue;
            }

            into.Children.Add(node);
            last = node;
        }

        into.Open += head.ToString();
    }

    private StoryNode? Classify(MarkupNode c, StoryNode parent)
    {
        if (c.IsSelfClosing)
        {
            // An empty paragraph is still a unit; any other empty element is not a block.
            return c.IsElement(Ns, "p") ? ReadParagraph(c, parent) : null;
        }

        if (c.IsElement(Ns, "p"))
        {
            return ReadParagraph(c, parent);
        }

        if (c.IsElement(Ns, "tbl"))
        {
            return ReadContainer(c, parent, StoryNodeKind.Table);
        }

        if (c.IsElement(Ns, "tr"))
        {
            return ReadContainer(c, parent, StoryNodeKind.Row);
        }

        if (c.IsElement(Ns, "tc"))
        {
            return ReadContainer(c, parent, StoryNodeKind.Cell);
        }

        if (c.IsElement(Ns, "footnote") || c.IsElement(Ns, "endnote") || c.IsElement(Ns, "comment"))
        {
            return ReadContainer(c, parent, StoryNodeKind.Group);
        }

        if (c.IsElement(Ns, "sdt") || c.IsElement(Ns, "customXml") || c.IsElement(Ns, "ins") || c.IsElement(Ns, "del")
            || c.IsElement(Ns, "moveFrom") || c.IsElement(Ns, "moveTo"))
        {
            return ReadWrapper(c, parent);
        }

        return null;
    }

    private StoryNode ReadParagraph(MarkupNode p, StoryNode parent)
    {
        ParagraphModel model = ParagraphModel.Read(p, Ns);
        var node = new StoryNode
        {
            Kind = StoryNodeKind.Para,
            Parent = parent,
            Paragraph = model,
            Unit = Units.Count,
            EndsSection = model.ParagraphProperties.Contains("sectPr", StringComparison.Ordinal) && ReferenceEquals(parent, Tree),
        };
        Units.Add(node);
        return node;
    }

    private StoryNode ReadContainer(MarkupNode c, StoryNode parent, StoryNodeKind kind)
    {
        var node = new StoryNode { Kind = kind, Parent = parent };
        node.Open = XmlSnip.StartTag(c.OuterXml, out _);
        node.Close = "</" + c.Name + ">";
        ReadChildren(c, node);
        return node;
    }

    private StoryNode? ReadWrapper(MarkupNode c, StoryNode parent)
    {
        var node = new StoryNode { Kind = StoryNodeKind.Wrapper, Parent = parent };
        node.Open = XmlSnip.StartTag(c.OuterXml, out _);
        node.Close = "</" + c.Name + ">";
        MarkupNode content = c;
        if (c.IsElement(Ns, "sdt"))
        {
            // ECMA-376 Part 1 §17.5.2.34: the properties come first, the content is in w:sdtContent.
            var head = new System.Text.StringBuilder();
            MarkupNode sdtContent = default;
            foreach (MarkupNode child in c.ChildElements)
            {
                if (child.IsElement(Ns, "sdtContent"))
                {
                    sdtContent = child;
                }
                else
                {
                    head.Append(child.OuterXml);
                }
            }

            if (sdtContent.IsNull || sdtContent.IsSelfClosing)
            {
                return null;
            }

            node.Open += head + XmlSnip.StartTag(sdtContent.OuterXml, out _);
            node.Close = "</" + sdtContent.Name + ">" + node.Close;
            content = sdtContent;
        }

        ReadChildren(content, node);
        return node;
    }

    // Unit ranges up the tree.
    private static void Number(StoryNode node)
    {
        if (node.Kind == StoryNodeKind.Para)
        {
            node.FirstUnit = node.LastUnit = node.Unit;
            return;
        }

        foreach (StoryNode child in node.Children)
        {
            Number(child);
            if (child.FirstUnit < 0)
            {
                continue;
            }

            if (node.FirstUnit < 0)
            {
                node.FirstUnit = child.FirstUnit;
            }

            node.LastUnit = child.LastUnit;
        }
    }

    /// <summary>Marks the rows that hold only block tags, for the given delimiters.</summary>
    /// <param name="d">The delimiters.</param>
    public void MarkTagRows(TemplateDelimiters d) => MarkTagRows(Tree, d);

    private void MarkTagRows(StoryNode node, TemplateDelimiters d)
    {
        if (node.Kind == StoryNodeKind.Row && node.FirstUnit >= 0)
        {
            bool any = false;
            bool other = false;
            for (int u = node.FirstUnit; u <= node.LastUnit; u++)
            {
                string text = Units[u].Paragraph!.Text;
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                string rest = StripBlockTags(text, d);
                if (rest.Length != text.Length && string.IsNullOrWhiteSpace(rest))
                {
                    any = true;
                }
                else
                {
                    other = true;
                }
            }

            node.IsTagRow = any && !other;
        }

        foreach (StoryNode child in node.Children)
        {
            MarkTagRows(child, d);
        }
    }

    internal static string StripBlockTags(string text, TemplateDelimiters d)
    {
        string result = text;
        foreach ((string open, string close) in new[] { (d.BlockOpen, d.BlockClose), (d.CommentOpen, d.CommentClose) })
        {
            int from = 0;
            while (true)
            {
                int i = result.IndexOf(open, from, StringComparison.Ordinal);
                if (i < 0)
                {
                    break;
                }

                int j = result.IndexOf(close, i + open.Length, StringComparison.Ordinal);
                if (j < 0)
                {
                    break;
                }

                result = result.Remove(i, j + close.Length - i);
                from = i;
            }
        }

        return result;
    }

    /// <summary>The ancestors of a unit from the story's top down to its parent.</summary>
    /// <param name="unit">The unit.</param>
    /// <returns>The chain, outermost first.</returns>
    public List<StoryNode> Chain(int unit)
    {
        var chain = new List<StoryNode>();
        for (StoryNode? n = Units[unit].Parent; n is not null; n = n.Parent)
        {
            chain.Add(n);
        }

        chain.Reverse();
        return chain;
    }
}
