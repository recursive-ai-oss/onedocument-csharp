using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Schema;

namespace RecursiveAI.OneDocument.Presentations;

/// <summary>The map from logical colour names (<c>bg1</c>, <c>tx1</c>, <c>bg2</c>, <c>tx2</c>, <c>accent1</c>…) to theme slots (<c>lt1</c>, <c>dk1</c>…).</summary>
public sealed class ColorMap
{
    private readonly Dictionary<string, string> _map;

    private ColorMap(Dictionary<string, string> map) => _map = map;

    /// <summary>Maps a logical colour name to a theme slot.</summary>
    /// <param name="name">The logical name.</param>
    /// <returns>The slot, or null when the map does not mention the name.</returns>
    public string? Map(string name) => _map.TryGetValue(name, out string? v) ? v : null;

    // ECMA-376 Part 1 §20.1.6.5 (clrMap) and §20.1.6.6 (clrMapOvr): the twelve attributes bg1…folHlink name the slot each logical colour reads.
    internal static ColorMap? Read(MarkupRef element)
    {
        if (!element.IsValid)
        {
            return null;
        }

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (MarkupAttributeRef a in element.Node.Attributes)
        {
            if (!a.IsNamespaceDeclaration && a.Prefix.Length == 0)
            {
                map[System.Text.Encoding.UTF8.GetString(a.LocalName)] = a.Value;
            }
        }

        return map.Count == 0 ? null : new ColorMap(map);
    }
}

/// <summary>The header and footer switches of a master or layout (<c>p:hf</c>); each is null when <c>p:hf</c> is absent.</summary>
/// <param name="SlideNumber">Whether slide numbers show.</param>
/// <param name="Header">Whether the header shows.</param>
/// <param name="Footer">Whether the footer shows.</param>
/// <param name="DateTime">Whether the date shows.</param>
public readonly record struct HeaderFooterFlags(bool? SlideNumber, bool? Header, bool? Footer, bool? DateTime)
{
    // ECMA-376 Part 1 §19.3.1.25 (hf): every attribute defaults to true when the element is present.
    internal static HeaderFooterFlags Read(MarkupRef part)
    {
        MarkupRef? hf = Pml.P(part, "hf");
        return hf is null
            ? default
            : new HeaderFooterFlags(
                Pml.AttrBool(hf.Value, "sldNum") ?? true,
                Pml.AttrBool(hf.Value, "hdr") ?? true,
                Pml.AttrBool(hf.Value, "ftr") ?? true,
                Pml.AttrBool(hf.Value, "dt") ?? true);
    }
}

/// <summary>One slide: its shapes, the layout and master it inherits from, notes, comments and the switches that matter to a reader.</summary>
public sealed partial class Slide : PresentationPart
{
    internal Slide(Presentation owner, string partName)
        : base(owner, partName)
    {
    }

    /// <summary>Gets the typed view of the part's root element, or null when the part is unreadable: the hop to the generated views.</summary>
    public global::RecursiveAI.OneDocument.Schema.Presentation.Slide? View => Markup.IsValid ? global::RecursiveAI.OneDocument.Schema.Presentation.Slide.Create(Markup) : null;

    internal override string RootName => "sld";

    /// <summary>Gets the slide's id (<c>p:sldId/@id</c>): at least 256, unique in the presentation, stable when slides move. Null when the presentation part does not list the slide.</summary>
    public long? Id => Owner.EntryOf(this)?.Id;

    /// <summary>Gets the relationship id the presentation part uses for the slide, or null when it does not list it.</summary>
    public string? RelationshipId => Owner.EntryOf(this)?.RelationshipId;

    /// <summary>Gets the zero-based position of the slide in the presentation, or -1 when the presentation part does not list it.</summary>
    public int Index => Owner.EntryOf(this)?.Index ?? -1;

    /// <summary>Gets the layout the slide is based on, or null when the slide has no layout relationship (reported as <c>pptx.layout-missing</c>); placeholders then inherit nothing.</summary>
    public SlideLayout? Layout
    {
        get
        {
            string? name = RelatedPartName("slideLayout");
            if (name is null)
            {
                lock (Owner.Parts.Gate)
                {
                    if (Owner.ReportOnce(PartName + "#layout"))
                    {
                        Owner.Report("pptx.layout-missing", DefectSeverity.Repaired, "The slide has no layout; its placeholders inherit nothing.", PartName);
                    }
                }

                return null;
            }

            return Owner.GetPart(name, static (p, n) => new SlideLayout(p, n));
        }
    }

    /// <summary>Gets the master of the slide's layout, or null.</summary>
    public SlideMaster? Master => Layout?.Master;

    /// <inheritdoc />
    public override PresentationTheme? Theme => Master?.Theme;

    /// <inheritdoc />
    /// <remarks>The slide's own override (<c>p:clrMapOvr/a:overrideClrMapping</c>) when it has one, else the master's map.</remarks>
    public override ColorMap? ColorMap
    {
        get
        {
            if (Pml.P(Markup, "clrMapOvr") is { } ovr && Pml.A(ovr, "overrideClrMapping") is { } over)
            {
                return ColorMap.Read(over) ?? Master?.ColorMap;
            }

            return Master?.ColorMap;
        }
    }

    /// <summary>Gets or sets whether the slide is hidden in the slide show (<c>show="0"</c>). Setting false removes the attribute.</summary>
    public bool IsHidden
    {
        get => Pml.AttrBool(Markup, "show") == false;
        set
        {
            MarkupRef root = Markup;
            if (!root.IsValid)
            {
                return;
            }

            if (value)
            {
                root.Node.SetAttribute("show", "0");
            }
            else if (Pml.Attr(root, "show") is not null)
            {
                root.Node.RemoveAttribute("show");
            }
        }
    }

    /// <summary>Gets whether the layout's and master's shapes show on the slide (<c>showMasterSp</c>, true when absent).</summary>
    public bool ShowMasterShapes => Pml.AttrBool(Markup, "showMasterSp") ?? true;

    /// <summary>Gets whether the slide has a transition (<c>p:transition</c>, also inside <c>mc:AlternateContent</c>). Transitions are preserved, never interpreted.</summary>
    public bool HasTransition => HasChildElement("transition");

    /// <summary>Gets whether the slide has animations (<c>p:timing</c>). Animations are preserved, never interpreted.</summary>
    public bool HasAnimations => HasChildElement("timing");

    private bool HasChildElement(string local)
    {
        MarkupRef root = Markup;
        if (!root.IsValid)
        {
            return false;
        }

        foreach (MarkupNode child in root.Node.ChildElements)
        {
            var r = new MarkupRef(child);
            if (r.Namespace == SchemaNamespace.Presentation && child.LocalNameString == local)
            {
                return true;
            }

            if (MarkupCompatibility.IsAlternateContent(child))
            {
                foreach (MarkupNode branch in child.ChildElements)
                {
                    foreach (MarkupNode inner in branch.ChildElements)
                    {
                        if (new MarkupRef(inner).Namespace == SchemaNamespace.Presentation && inner.LocalNameString == local)
                        {
                            return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    /// <summary>Gets the title of the slide: the text of its title placeholder (<c>title</c> or <c>ctrTitle</c>), or null when it has none.</summary>
    public string? Title
    {
        get
        {
            foreach (Shape shape in AllShapes())
            {
                if (shape.Placeholder is { } ph && ph.Type is "title" or "ctrTitle" && shape.TextBody is { } body)
                {
                    return body.Text;
                }
            }

            return null;
        }
    }

    /// <summary>Gets the notes slide, or null when the slide has none.</summary>
    public NotesSlide? Notes =>
        RelatedPartName("notesSlide") is { } name ? Owner.GetPart(name, static (p, n) => new NotesSlide(p, n)) : null;

    /// <summary>Gets the comments on the slide, legacy and modern, in part order. Empty when there are none.</summary>
    public IReadOnlyList<SlideComment> Comments => SlideComment.Read(this);
}

/// <summary>A slide layout: the placeholders and fixed shapes a slide based on it starts from.</summary>
public sealed class SlideLayout : PresentationPart
{
    internal SlideLayout(Presentation owner, string partName)
        : base(owner, partName)
    {
    }

    /// <summary>Gets the typed view of the part's root element, or null when the part is unreadable: the hop to the generated views.</summary>
    public global::RecursiveAI.OneDocument.Schema.Presentation.SlideLayout? View => Markup.IsValid ? global::RecursiveAI.OneDocument.Schema.Presentation.SlideLayout.Create(Markup) : null;

    internal override string RootName => "sldLayout";

    /// <summary>Gets the layout's master, or null when it has no master relationship (reported as <c>pptx.master-missing</c>).</summary>
    public SlideMaster? Master
    {
        get
        {
            string? name = RelatedPartName("slideMaster");
            if (name is null)
            {
                lock (Owner.Parts.Gate)
                {
                    if (Owner.ReportOnce(PartName + "#master"))
                    {
                        Owner.Report("pptx.master-missing", DefectSeverity.Repaired, "The layout has no master; its placeholders inherit nothing.", PartName);
                    }
                }

                return null;
            }

            return Owner.GetPart(name, static (p, n) => new SlideMaster(p, n));
        }
    }

    /// <summary>Gets the layout type (<c>type</c>: <c>title</c>, <c>obj</c>, <c>blank</c>…); <c>cust</c> when absent.</summary>
    public string Type => Pml.Attr(Markup, "type") ?? "cust";

    /// <summary>Gets whether the layout's master shapes show (<c>showMasterSp</c>, true when absent).</summary>
    public bool ShowMasterShapes => Pml.AttrBool(Markup, "showMasterSp") ?? true;

    /// <summary>Gets the header and footer switches (<c>p:hf</c>).</summary>
    public HeaderFooterFlags HeaderFooter => HeaderFooterFlags.Read(Markup);

    /// <inheritdoc />
    public override PresentationTheme? Theme => Master?.Theme;

    /// <inheritdoc />
    /// <remarks>The layout's own override when it has one, else the master's map.</remarks>
    public override ColorMap? ColorMap =>
        Pml.P(Markup, "clrMapOvr") is { } ovr && Pml.A(ovr, "overrideClrMapping") is { } over ? ColorMap.Read(over) ?? Master?.ColorMap : Master?.ColorMap;
}

/// <summary>A slide master: the shapes, text styles, colour map and theme every layout and slide under it inherits.</summary>
public sealed class SlideMaster : PresentationPart
{
    internal SlideMaster(Presentation owner, string partName)
        : base(owner, partName)
    {
    }

    /// <summary>Gets the typed view of the part's root element, or null when the part is unreadable: the hop to the generated views.</summary>
    public global::RecursiveAI.OneDocument.Schema.Presentation.SlideMaster? View => Markup.IsValid ? global::RecursiveAI.OneDocument.Schema.Presentation.SlideMaster.Create(Markup) : null;

    internal override string RootName => "sldMaster";

    /// <summary>Gets the layouts of the master in the order <c>p:sldLayoutIdLst</c> lists them. A layout entry whose relationship or part is missing is reported (<c>pptx.layout-missing</c>) and skipped.</summary>
    public IReadOnlyList<SlideLayout> Layouts
    {
        get
        {
            var list = new List<SlideLayout>();
            MarkupRef? ids = Pml.P(Markup, "sldLayoutIdLst");
            if (ids is null)
            {
                return list;
            }

            foreach (MarkupRef entry in Pml.Elements(ids.Value, SchemaNamespace.Presentation, "sldLayoutId"))
            {
                string? rid = Pml.RelAttr(entry, "id");
                Relationship? rel = rid is null ? null : Owner.FindRelationship(PartName, rid);
                string? target = rel is null ? null : Owner.ResolveTarget(PartName, rel);
                if (target is null)
                {
                    lock (Owner.Parts.Gate)
                    {
                        if (Owner.ReportOnce(PartName + "#layout:" + rid))
                        {
                            Owner.Report("pptx.layout-missing", DefectSeverity.Dropped, "A layout listed by the master has no part (relationship " + (rid ?? "?") + "); it is skipped.", PartName, entry.Node.SourceStart);
                        }
                    }

                    continue;
                }

                list.Add(Owner.GetPart(target, static (p, n) => new SlideLayout(p, n)));
            }

            return list;
        }
    }

    /// <summary>Gets the master's id in <c>p:sldMasterIdLst</c> (at least 2,147,483,648), or null when unlisted.</summary>
    public long? Id => Owner.MasterId(this);

    /// <summary>Gets the header and footer switches (<c>p:hf</c>).</summary>
    public HeaderFooterFlags HeaderFooter => HeaderFooterFlags.Read(Markup);

    /// <inheritdoc />
    public override PresentationTheme? Theme => Owner.LoadTheme(RelatedPartName("theme"));

    /// <inheritdoc />
    public override ColorMap? ColorMap => Pml.P(Markup, "clrMap") is { } map ? ColorMap.Read(map) : null;

    /// <summary>Gets <c>p:txStyles/p:titleStyle</c>, the list style of title placeholders, or an invalid reference.</summary>
    public MarkupRef TitleStyle => TextStyle("titleStyle");

    /// <summary>Gets <c>p:txStyles/p:bodyStyle</c>, the list style of body, subtitle and object placeholders, or an invalid reference.</summary>
    public MarkupRef BodyStyle => TextStyle("bodyStyle");

    /// <summary>Gets <c>p:txStyles/p:otherStyle</c>, the list style of the date, footer and slide number placeholders, or an invalid reference.</summary>
    public MarkupRef OtherStyle => TextStyle("otherStyle");

    private MarkupRef TextStyle(string local) =>
        Pml.P(Markup, "txStyles") is { } styles ? Pml.P(styles, local) ?? default : default;
}

/// <summary>The notes page of a slide.</summary>
public sealed class NotesSlide : PresentationPart
{
    internal NotesSlide(Presentation owner, string partName)
        : base(owner, partName)
    {
    }

    /// <summary>Gets the typed view of the part's root element, or null when the part is unreadable: the hop to the generated views.</summary>
    public global::RecursiveAI.OneDocument.Schema.Presentation.NotesSlide? View => Markup.IsValid ? global::RecursiveAI.OneDocument.Schema.Presentation.NotesSlide.Create(Markup) : null;

    internal override string RootName => "notes";

    /// <summary>Gets the notes master, or null.</summary>
    public NotesMaster? NotesMaster =>
        RelatedPartName("notesMaster") is { } name ? Owner.GetPart(name, static (p, n) => new NotesMaster(p, n)) : null;

    /// <summary>Gets the slide the notes belong to, or null.</summary>
    public Slide? Slide =>
        RelatedPartName("slide") is { } name ? Owner.GetPart(name, static (p, n) => new Slide(p, n)) : null;

    /// <summary>Gets the notes text: the paragraphs of the body placeholder (<c>type="body"</c>), one per line; empty when it has none.</summary>
    public string NotesText
    {
        get
        {
            foreach (Shape shape in AllShapes())
            {
                if (shape.Placeholder is { Type: "body" } && shape.TextBody is { } body)
                {
                    return body.Text;
                }
            }

            return string.Empty;
        }
    }

    /// <summary>Gets the body placeholder's text body, or null when the notes slide has none.</summary>
    public TextBody? Body
    {
        get
        {
            foreach (Shape shape in AllShapes())
            {
                if (shape.Placeholder is { Type: "body" } && shape.TextBody is { } body)
                {
                    return body;
                }
            }

            return null;
        }
    }

    /// <inheritdoc />
    public override PresentationTheme? Theme => NotesMaster?.Theme;

    /// <inheritdoc />
    public override ColorMap? ColorMap =>
        Pml.P(Markup, "clrMapOvr") is { } ovr && Pml.A(ovr, "overrideClrMapping") is { } over ? ColorMap.Read(over) ?? NotesMaster?.ColorMap : NotesMaster?.ColorMap;
}

/// <summary>The notes master: the shapes, text style and theme of every notes page.</summary>
public sealed class NotesMaster : PresentationPart
{
    internal NotesMaster(Presentation owner, string partName)
        : base(owner, partName)
    {
    }

    /// <summary>Gets the typed view of the part's root element, or null when the part is unreadable: the hop to the generated views.</summary>
    public global::RecursiveAI.OneDocument.Schema.Presentation.NotesMaster? View => Markup.IsValid ? global::RecursiveAI.OneDocument.Schema.Presentation.NotesMaster.Create(Markup) : null;

    internal override string RootName => "notesMaster";

    /// <summary>Gets <c>p:notesStyle</c>, the list style of notes text, or an invalid reference.</summary>
    public MarkupRef NotesStyle => Pml.P(Markup, "notesStyle") ?? default;

    /// <inheritdoc />
    public override PresentationTheme? Theme => Owner.LoadTheme(RelatedPartName("theme"));

    /// <inheritdoc />
    public override ColorMap? ColorMap => Pml.P(Markup, "clrMap") is { } map ? ColorMap.Read(map) : null;
}
