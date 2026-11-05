using System.Text;
using RecursiveAI.OneDocument.Schema;
using W = RecursiveAI.OneDocument.Schema.Wordprocessing;

namespace RecursiveAI.OneDocument.Documents;

/// <summary>What a block-level element is.</summary>
public enum BlockKind
{
    /// <summary>A paragraph (<c>w:p</c>).</summary>
    Paragraph = 0,

    /// <summary>A table (<c>w:tbl</c>).</summary>
    Table = 1,

    /// <summary>A block-level structured document tag (<c>w:sdt</c>), also called a content control.</summary>
    ContentControl = 2,

    /// <summary>Anything else that sits between blocks: bookmark and range markers, <c>w:customXml</c>, revision wrappers, <c>mc:AlternateContent</c>, unknown markup. It is kept, never dropped.</summary>
    Other = 3,
}

/// <summary>
/// A block-level element of a body, table cell, header, footer, note or content control: a thin wrapper over its markup element.
/// Wrappers are cheap and carry no state beyond the element, so two wrappers over one element are equal.
/// </summary>
public abstract class BlockElement : IEquatable<BlockElement>
{
    private protected BlockElement(Document owner, MarkupRef markup)
    {
        Owner = owner;
        Markup = markup;
    }

    /// <summary>Gets the document the element belongs to.</summary>
    public Document Owner { get; }

    /// <summary>Gets the element in the markup tree: the hop to the typed views and to the markup layer.</summary>
    public MarkupRef Markup { get; }

    /// <summary>Gets what kind of block this is.</summary>
    public abstract BlockKind Kind { get; }

    /// <inheritdoc/>
    public bool Equals(BlockElement? other) => other is not null && Markup == other.Markup;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as BlockElement);

    /// <inheritdoc/>
    public override int GetHashCode() => Markup.GetHashCode();

    /// <summary>Wraps a child element of a block container according to its name.</summary>
    internal static BlockElement Wrap(Document owner, MarkupRef element)
    {
        if (element.Namespace == SchemaNamespace.Wordprocessing)
        {
            ReadOnlySpan<byte> n = element.LocalName;
            if (n.SequenceEqual("p"u8))
            {
                return new Paragraph(owner, element);
            }

            if (n.SequenceEqual("tbl"u8))
            {
                return new Table(owner, element);
            }

            if (n.SequenceEqual("sdt"u8))
            {
                return new BlockContentControl(owner, element);
            }
        }

        return new OtherBlock(owner, element);
    }
}

/// <summary>
/// Something that holds blocks: the body, a content control's content, and (as the other hub units add them) table cells,
/// headers, footers and notes. Enumeration is lazy and live over the markup tree.
/// </summary>
public abstract partial class BlockContainer
{
    private protected BlockContainer(Document owner, MarkupRef container)
    {
        Owner = owner;
        Container = container;
    }

    /// <summary>Gets the document the container belongs to.</summary>
    public Document Owner { get; }

    /// <summary>Gets the element whose children are the blocks (<c>w:body</c>, <c>w:sdtContent</c>, <c>w:tc</c>, …); invalid when the container is absent from the file.</summary>
    public MarkupRef Container { get; }

    /// <summary>
    /// Enumerates the blocks in document order. Every child element is returned: paragraphs, tables and content controls as their
    /// own types and anything else (bookmark markers, revision wrappers, unknown markup) as <see cref="OtherBlock"/>. A
    /// <c>w:sectPr</c> that closes a body or content is not a block and is left out; property elements of a cell are left out too.
    /// </summary>
    /// <returns>The blocks; empty when the container is absent.</returns>
    public IEnumerable<BlockElement> Blocks()
    {
        if (!Container.IsValid)
        {
            yield break;
        }

        var node = Container.Node.FirstChild;
        while (!node.IsNull)
        {
            if (node.Kind == Markup.MarkupNodeKind.Element)
            {
                var element = new MarkupRef(node);
                if (!IsContainerProperty(element))
                {
                    yield return BlockElement.Wrap(Owner, element);
                }
            }

            node = node.NextSibling;
        }
    }

    /// <summary>Enumerates the paragraphs of this container in document order, reaching into tables, content controls and <c>w:customXml</c> wrappers.</summary>
    /// <returns>The paragraphs.</returns>
    public IEnumerable<Paragraph> Paragraphs()
    {
        foreach (BlockElement block in Blocks())
        {
            foreach (Paragraph p in ParagraphsOf(block))
            {
                yield return p;
            }
        }
    }

    private static IEnumerable<Paragraph> ParagraphsOf(BlockElement block)
    {
        switch (block)
        {
            case Paragraph p:
                yield return p;
                break;
            case BlockContentControl sdt:
                foreach (Paragraph inner in sdt.Content.Paragraphs())
                {
                    yield return inner;
                }

                break;
            case Table or OtherBlock:
                // Tables hold rows and cells (a hub unit of its own); the generic walk finds the paragraphs in them and in wrappers.
                foreach (Paragraph inner in DescendantParagraphs(block.Owner, block.Markup))
                {
                    yield return inner;
                }

                break;
        }
    }

    private static IEnumerable<Paragraph> DescendantParagraphs(Document owner, MarkupRef element)
    {
        foreach (MarkupRef child in Elements(element))
        {
            if (child.Namespace == SchemaNamespace.Wordprocessing && child.LocalName.SequenceEqual("p"u8))
            {
                yield return new Paragraph(owner, child);
            }
            else if (child.Namespace == SchemaNamespace.Wordprocessing && child.LocalName.SequenceEqual("txbxContent"u8))
            {
                // Text boxes are inline content of a run, not part of the block flow of this container.
                continue;
            }
            else
            {
                foreach (Paragraph p in DescendantParagraphs(owner, child))
                {
                    yield return p;
                }
            }
        }
    }

    private static IEnumerable<MarkupRef> Elements(MarkupRef parent)
    {
        for (var n = parent.Node.FirstChild; !n.IsNull; n = n.NextSibling)
        {
            if (n.Kind == Markup.MarkupNodeKind.Element)
            {
                yield return new MarkupRef(n);
            }
        }
    }

    private static bool IsContainerProperty(MarkupRef element)
    {
        if (element.Namespace != SchemaNamespace.Wordprocessing)
        {
            return false;
        }

        ReadOnlySpan<byte> n = element.LocalName;
        return n.SequenceEqual("sectPr"u8) || n.SequenceEqual("tcPr"u8) || n.SequenceEqual("sdtPr"u8) || n.SequenceEqual("sdtEndPr"u8);
    }
}

/// <summary>The body of a document: the blocks of the main story and its closing section properties.</summary>
public sealed partial class Body : BlockContainer
{
    internal Body(Document owner, MarkupRef element)
        : base(owner, element)
    {
    }

    /// <summary>Gets the typed view of <c>w:body</c>, or null when the document has none.</summary>
    public W.Body? View => Container.IsValid ? W.Body.Create(Container) : null;

    /// <summary>Gets the section properties that close the body (the last section's), or null when the body has none.</summary>
    public MarkupRef? SectionProperties => Container.IsValid ? Container.FindChild(SchemaNamespace.Wordprocessing, "sectPr"u8) : null;
}

/// <summary>A paragraph (<c>w:p</c>): properties, then inline content.</summary>
public sealed partial class Paragraph : BlockElement
{
    internal Paragraph(Document owner, MarkupRef markup)
        : base(owner, markup)
    {
    }

    /// <inheritdoc/>
    public override BlockKind Kind => BlockKind.Paragraph;

    /// <summary>Gets the typed view of the paragraph.</summary>
    public W.Paragraph View => W.Paragraph.Create(Markup);

    /// <summary>Gets the direct paragraph properties (<c>w:pPr</c>), or null when the paragraph has none.</summary>
    public W.ParagraphProperties? Properties => View.ParagraphProperties;

    /// <summary>
    /// Gets or sets the id of the paragraph style applied directly (<c>w:pStyle</c>); null when none is. Setting null removes the
    /// element. The id is not checked against the style sheet here.
    /// </summary>
    public string? StyleId
    {
        get => Properties?.ParagraphStyle?.Val;
        set
        {
            if (value is null)
            {
                if (Properties is { } props)
                {
                    props.RemoveParagraphStyle();
                }

                return;
            }

            View.GetOrAddParagraphProperties().GetOrAddParagraphStyle().Val = value;
        }
    }

    /// <summary>
    /// Enumerates the inline content in order: runs, hyperlinks, simple fields, content controls and grouping wrappers, and (as
    /// <see cref="OtherInline"/>) everything else, including markers and math. The properties element is not inline content.
    /// </summary>
    /// <returns>The inline elements.</returns>
    public IEnumerable<InlineElement> Inlines() => InlineElement.Children(Owner, Markup, "pPr");

    /// <summary>Enumerates every run of the paragraph in reading order, reaching into hyperlinks, fields, content controls and wrappers.</summary>
    /// <returns>The runs.</returns>
    public IEnumerable<Run> Runs()
    {
        foreach (InlineElement inline in Inlines())
        {
            foreach (Run r in inline.DescendantRuns())
            {
                yield return r;
            }
        }
    }

    /// <summary>
    /// Gets the text of the paragraph's runs, concatenated. Tabs and line breaks read as <c>\t</c> and <c>\n</c>, deleted text
    /// (<c>w:delText</c>) is not part of it, and fields contribute their cached result text and nothing of their instruction.
    /// </summary>
    public string Text
    {
        get
        {
            var sb = new StringBuilder();
            foreach (Run r in Runs())
            {
                r.AppendText(sb);
            }

            return sb.ToString();
        }
    }
}

/// <summary>A table (<c>w:tbl</c>). The row, cell and grid model is added by the tables part of the hub; this is the block-level handle.</summary>
public sealed partial class Table : BlockElement
{
    internal Table(Document owner, MarkupRef markup)
        : base(owner, markup)
    {
    }

    /// <inheritdoc/>
    public override BlockKind Kind => BlockKind.Table;

    /// <summary>Gets the typed view of the table.</summary>
    public W.Table View => W.Table.Create(Markup);
}

/// <summary>A block-level content control (<c>w:sdt</c>). Binding and the property model belong to the content-control part of the hub; this is the handle and its content.</summary>
public sealed partial class BlockContentControl : BlockElement
{
    internal BlockContentControl(Document owner, MarkupRef markup)
        : base(owner, markup)
    {
    }

    /// <inheritdoc/>
    public override BlockKind Kind => BlockKind.ContentControl;

    /// <summary>Gets the <c>w:sdtPr</c> element, or null.</summary>
    public MarkupRef? PropertiesElement => Markup.FindChild(SchemaNamespace.Wordprocessing, "sdtPr"u8);

    /// <summary>Gets the blocks inside <c>w:sdtContent</c>; empty when the element is absent.</summary>
    public SdtBlockContent Content => new(Owner, Markup.FindChild(SchemaNamespace.Wordprocessing, "sdtContent"u8) ?? default);
}

/// <summary>The blocks inside a block-level content control.</summary>
public sealed class SdtBlockContent : BlockContainer
{
    internal SdtBlockContent(Document owner, MarkupRef element)
        : base(owner, element)
    {
    }
}

/// <summary>Block-level markup that is not a paragraph, table or content control, kept so enumeration never hides content.</summary>
public sealed class OtherBlock : BlockElement
{
    internal OtherBlock(Document owner, MarkupRef markup)
        : base(owner, markup)
    {
    }

    /// <inheritdoc/>
    public override BlockKind Kind => BlockKind.Other;
}
