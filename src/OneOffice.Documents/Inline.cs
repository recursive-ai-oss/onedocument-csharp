using System.Text;
using RecursiveAI.OneOffice.Markup;
using RecursiveAI.OneOffice.Schema;
using W = RecursiveAI.OneOffice.Schema.Wordprocessing;

namespace RecursiveAI.OneOffice.Documents;

/// <summary>What an inline element is.</summary>
public enum InlineKind
{
    /// <summary>A run (<c>w:r</c>): text and other content with one set of character properties.</summary>
    Run = 0,

    /// <summary>A hyperlink (<c>w:hyperlink</c>).</summary>
    Hyperlink = 1,

    /// <summary>A simple field (<c>w:fldSimple</c>).</summary>
    SimpleField = 2,

    /// <summary>An inline structured document tag (<c>w:sdt</c> inside a paragraph).</summary>
    ContentControl = 3,

    /// <summary>A wrapper that holds more inline content: <c>w:customXml</c>, <c>w:smartTag</c>, <c>w:dir</c>, <c>w:bdo</c> and the revision wrappers <c>w:ins</c>, <c>w:del</c>, <c>w:moveFrom</c>, <c>w:moveTo</c>.</summary>
    Group = 4,

    /// <summary>Anything else: bookmark and range markers, proofing marks, math, <c>mc:AlternateContent</c>, unknown markup. Kept, never dropped.</summary>
    Other = 5,
}

/// <summary>An inline element of a paragraph: a thin wrapper over its markup element.</summary>
public abstract class InlineElement : IEquatable<InlineElement>
{
    private protected InlineElement(Document owner, MarkupRef markup)
    {
        Owner = owner;
        Markup = markup;
    }

    /// <summary>Gets the document the element belongs to.</summary>
    public Document Owner { get; }

    /// <summary>Gets the element in the markup tree.</summary>
    public MarkupRef Markup { get; }

    /// <summary>Gets what kind of inline element this is.</summary>
    public abstract InlineKind Kind { get; }

    /// <summary>
    /// Enumerates the runs in this element, in reading order: the run itself for a run, the runs of the content for a container.
    /// Elements that hold no runs yield nothing.
    /// </summary>
    /// <returns>The runs.</returns>
    public IEnumerable<Run> DescendantRuns()
    {
        if (this is Run run)
        {
            yield return run;
            yield break;
        }

        if (this is InlineContainer container)
        {
            foreach (InlineElement child in container.Inlines())
            {
                foreach (Run r in child.DescendantRuns())
                {
                    yield return r;
                }
            }
        }
    }

    /// <inheritdoc/>
    public bool Equals(InlineElement? other) => other is not null && Markup == other.Markup;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as InlineElement);

    /// <inheritdoc/>
    public override int GetHashCode() => Markup.GetHashCode();

    internal static IEnumerable<InlineElement> Children(Document owner, MarkupRef parent, string? skipLocalName = null)
    {
        for (MarkupNode n = parent.Node.FirstChild; !n.IsNull; n = n.NextSibling)
        {
            if (n.Kind != MarkupNodeKind.Element)
            {
                continue;
            }

            var element = new MarkupRef(n);
            if (skipLocalName is not null && element.Namespace == SchemaNamespace.Wordprocessing && element.LocalNameText == skipLocalName)
            {
                continue;
            }

            yield return Wrap(owner, element);
        }
    }

    private static InlineElement Wrap(Document owner, MarkupRef element)
    {
        if (element.Namespace == SchemaNamespace.Wordprocessing)
        {
            ReadOnlySpan<byte> n = element.LocalName;
            if (n.SequenceEqual("r"u8))
            {
                return new Run(owner, element);
            }

            if (n.SequenceEqual("hyperlink"u8))
            {
                return new Hyperlink(owner, element);
            }

            if (n.SequenceEqual("fldSimple"u8))
            {
                return new SimpleField(owner, element);
            }

            if (n.SequenceEqual("sdt"u8))
            {
                return new InlineContentControl(owner, element);
            }

            if (n.SequenceEqual("customXml"u8) || n.SequenceEqual("smartTag"u8) || n.SequenceEqual("dir"u8) || n.SequenceEqual("bdo"u8)
                || n.SequenceEqual("ins"u8) || n.SequenceEqual("del"u8) || n.SequenceEqual("moveFrom"u8) || n.SequenceEqual("moveTo"u8))
            {
                return new InlineGroup(owner, element);
            }
        }

        return new OtherInline(owner, element);
    }
}

/// <summary>An inline element that holds more inline content.</summary>
public abstract class InlineContainer : InlineElement
{
    private protected InlineContainer(Document owner, MarkupRef markup)
        : base(owner, markup)
    {
    }

    /// <summary>Enumerates the content in order. Property elements (<c>w:rPr</c>, <c>w:sdtPr</c>, …) are not content.</summary>
    /// <returns>The inline elements.</returns>
    public IEnumerable<InlineElement> Inlines()
    {
        foreach (InlineElement e in Children(Owner, ContentElement))
        {
            if (!IsProperty(e.Markup))
            {
                yield return e;
            }
        }
    }

    /// <summary>Gets the element whose children are the content: the container itself, or <c>w:sdtContent</c> for a content control.</summary>
    private protected virtual MarkupRef ContentElement => Markup;

    private static bool IsProperty(MarkupRef e)
    {
        if (e.Namespace != SchemaNamespace.Wordprocessing)
        {
            return false;
        }

        ReadOnlySpan<byte> n = e.LocalName;
        return n.SequenceEqual("sdtPr"u8) || n.SequenceEqual("sdtEndPr"u8) || n.SequenceEqual("customXmlPr"u8) || n.SequenceEqual("smartTagPr"u8);
    }
}

/// <summary>A run (<c>w:r</c>).</summary>
public sealed partial class Run : InlineElement
{
    internal Run(Document owner, MarkupRef markup)
        : base(owner, markup)
    {
    }

    /// <inheritdoc/>
    public override InlineKind Kind => InlineKind.Run;

    /// <summary>Gets the typed view of the run.</summary>
    public W.Run View => W.Run.Create(Markup);

    /// <summary>Gets the direct run properties (<c>w:rPr</c>), or null.</summary>
    public W.RunProperties? Properties => View.RunProperties;

    /// <summary>
    /// Gets or sets the id of the character style applied directly (<c>w:rStyle</c>); null when none is. Setting null removes the
    /// element. The id is not checked against the style sheet here.
    /// </summary>
    public string? StyleId
    {
        get => Properties?.RunStyle?.Val;
        set
        {
            if (value is null)
            {
                Properties?.RemoveRunStyle();
                return;
            }

            View.GetOrAddRunProperties().GetOrAddRunStyle().Val = value;
        }
    }

    /// <summary>
    /// Gets the text of the run: <c>w:t</c> as written, <c>w:tab</c> as <c>\t</c>, <c>w:br</c> and <c>w:cr</c> as <c>\n</c>,
    /// <c>w:noBreakHyphen</c> as U+2011 and <c>w:softHyphen</c> as U+00AD. Deleted text, field instructions and drawings contribute nothing.
    /// </summary>
    public string Text
    {
        get
        {
            var sb = new StringBuilder();
            AppendText(sb);
            return sb.ToString();
        }
    }

    internal void AppendText(StringBuilder sb)
    {
        for (MarkupNode n = Markup.Node.FirstChild; !n.IsNull; n = n.NextSibling)
        {
            if (n.Kind != MarkupNodeKind.Element)
            {
                continue;
            }

            var e = new MarkupRef(n);
            if (e.Namespace != SchemaNamespace.Wordprocessing)
            {
                continue;
            }

            ReadOnlySpan<byte> name = e.LocalName;
            if (name.SequenceEqual("t"u8))
            {
                sb.Append(n.InnerText);
            }
            else if (name.SequenceEqual("tab"u8))
            {
                sb.Append('\t');
            }
            else if (name.SequenceEqual("br"u8) || name.SequenceEqual("cr"u8))
            {
                sb.Append('\n');
            }
            else if (name.SequenceEqual("noBreakHyphen"u8))
            {
                sb.Append('\u2011');
            }
            else if (name.SequenceEqual("softHyphen"u8))
            {
                sb.Append('\u00AD');
            }
        }
    }
}

/// <summary>A hyperlink (<c>w:hyperlink</c>): runs that link to a bookmark in the document or to a relationship target.</summary>
public sealed partial class Hyperlink : InlineContainer
{
    internal Hyperlink(Document owner, MarkupRef markup)
        : base(owner, markup)
    {
    }

    /// <inheritdoc/>
    public override InlineKind Kind => InlineKind.Hyperlink;

    /// <summary>Gets the typed view of the hyperlink.</summary>
    public W.Hyperlink View => W.Hyperlink.Create(Markup);

    /// <summary>Gets the bookmark name the link jumps to inside the document, or null.</summary>
    public string? Anchor => View.Anchor;

    /// <summary>Gets the relationship id (<c>r:id</c>) of an external or part target, or null. Nothing is fetched or followed.</summary>
    public string? RelationshipId => View.Id;
}

/// <summary>A simple field (<c>w:fldSimple</c>): one instruction and the runs of its cached result.</summary>
public sealed partial class SimpleField : InlineContainer
{
    internal SimpleField(Document owner, MarkupRef markup)
        : base(owner, markup)
    {
    }

    /// <inheritdoc/>
    public override InlineKind Kind => InlineKind.SimpleField;

    /// <summary>Gets the typed view of the field.</summary>
    public W.SimpleField View => W.SimpleField.Create(Markup);

    /// <summary>Gets the field instruction (<c>w:instr</c>), or null.</summary>
    public string? Instruction => View.Instr;
}

/// <summary>An inline content control (<c>w:sdt</c> in a paragraph). The property model belongs to the content-control part of the hub.</summary>
public sealed partial class InlineContentControl : InlineContainer
{
    internal InlineContentControl(Document owner, MarkupRef markup)
        : base(owner, markup)
    {
    }

    /// <inheritdoc/>
    public override InlineKind Kind => InlineKind.ContentControl;

    /// <summary>Gets the <c>w:sdtPr</c> element, or null.</summary>
    public MarkupRef? PropertiesElement => Markup.FindChild(SchemaNamespace.Wordprocessing, "sdtPr"u8);

    private protected override MarkupRef ContentElement => Markup.FindChild(SchemaNamespace.Wordprocessing, "sdtContent"u8) ?? default;
}

/// <summary>A wrapper around inline content: <c>w:customXml</c>, <c>w:smartTag</c>, <c>w:dir</c>, <c>w:bdo</c> or a revision wrapper (<c>w:ins</c>, <c>w:del</c>, <c>w:moveFrom</c>, <c>w:moveTo</c>).</summary>
public sealed partial class InlineGroup : InlineContainer
{
    internal InlineGroup(Document owner, MarkupRef markup)
        : base(owner, markup)
    {
    }

    /// <inheritdoc/>
    public override InlineKind Kind => InlineKind.Group;

    /// <summary>Gets the local name of the wrapper element, such as <c>ins</c> or <c>smartTag</c>.</summary>
    public string ElementName => Markup.LocalNameText;
}

/// <summary>Inline markup that is none of the above, kept so enumeration never hides content.</summary>
public sealed class OtherInline : InlineElement
{
    internal OtherInline(Document owner, MarkupRef markup)
        : base(owner, markup)
    {
    }

    /// <inheritdoc/>
    public override InlineKind Kind => InlineKind.Other;
}
