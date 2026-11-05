using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Schema;

namespace RecursiveAI.OneDocument.Presentations;

/// <summary>
/// A part of a presentation that holds shapes: a slide, a slide layout, a slide master, a notes slide or the notes master. The part is
/// parsed on first use and shared with every other hub object over it.
/// </summary>
public abstract partial class PresentationPart
{
    private bool _rootChecked;
    private bool _rootValid;

    internal PresentationPart(Presentation owner, string partName)
    {
        Owner = owner;
        PartName = partName;
    }

    /// <summary>Gets the presentation this part belongs to.</summary>
    public Presentation Owner { get; }

    /// <summary>Gets the part name, for example <c>/ppt/slides/slide1.xml</c>.</summary>
    public string PartName { get; }

    /// <summary>The local name of the root element the part must start with.</summary>
    internal abstract string RootName { get; }

    /// <summary>
    /// Gets the markup tree of the part, or null when the part is not well-formed (reported once as <c>pptx.part-unreadable</c>; the part
    /// then reads as empty and is left as it is on save).
    /// </summary>
    public MarkupDocument? MarkupTree => Owner.Parts.Load(PartName, required: false);

    /// <summary>
    /// Gets the root element: the hop from the hub to the markup layer. It is an invalid reference when the part is unreadable or does
    /// not start with the expected element (reported once as <c>pptx.wrong-root</c>).
    /// </summary>
    public MarkupRef Markup
    {
        get
        {
            MarkupDocument? tree = MarkupTree;
            if (tree is null)
            {
                return default;
            }

            MarkupNode root = tree.Root;
            lock (Owner.Parts.Gate)
            {
                if (!_rootChecked)
                {
                    _rootChecked = true;
                    _rootValid = !root.IsNull && root.LocalNameString == RootName
                        && new MarkupRef(root).Namespace == SchemaNamespace.Presentation;
                    if (!_rootValid)
                    {
                        Owner.Report(
                            "pptx.wrong-root",
                            DefectSeverity.Dropped,
                            "The part does not start with p:" + RootName + "; it reads as empty.",
                            PartName,
                            root.IsNull ? -1 : root.SourceStart);
                    }
                }
            }

            return _rootValid ? new MarkupRef(root) : default;
        }
    }

    /// <summary>Gets the relationships of the part.</summary>
    public IReadOnlyList<Relationship> Relationships => Owner.Package.GetRelationships(PartName);

    /// <summary>Gets the <c>p:spTree</c> element, or an invalid reference when the part has none.</summary>
    public MarkupRef ShapeTreeMarkup
    {
        get
        {
            MarkupRef? cSld = Pml.P(Markup, "cSld");
            return cSld is null ? default : Pml.P(cSld.Value, "spTree") ?? default;
        }
    }

    /// <summary>Gets the shapes of the part's shape tree in z-order (back to front), groups not flattened.</summary>
    public IReadOnlyList<Shape> Shapes => Shape.ReadChildren(this, ShapeTreeMarkup, null, 0);

    /// <summary>Gets every shape of the part, depth first: a group comes before the shapes inside it.</summary>
    /// <returns>The shapes in document order.</returns>
    public IEnumerable<Shape> AllShapes()
    {
        IReadOnlyList<Shape> top = Shapes;
        for (int i = 0; i < top.Count; i++)
        {
            foreach (Shape s in Flatten(top[i]))
            {
                yield return s;
            }
        }

    }

    private static IEnumerable<Shape> Flatten(Shape shape)
    {
        yield return shape;
        foreach (Shape child in shape.Children)
        {
            foreach (Shape s in Flatten(child))
            {
                yield return s;
            }
        }
    }

    /// <summary>Gets the name of the part's common slide data (<c>p:cSld/@name</c>): the layout name for a layout, usually null elsewhere.</summary>
    public string? Name => Pml.P(Markup, "cSld") is { } c ? Pml.Attr(c, "name") : null;

    /// <summary>
    /// Gets the text of the part: the text of every shape that has a text body, in document order, one paragraph per line.
    /// Placeholders that are only prompts on a layout or master are included; a slide has no prompts of its own, so on a slide this is its text.
    /// </summary>
    public string Text
    {
        get
        {
            var lines = new List<string>();
            foreach (Shape shape in AllShapes())
            {
                if (shape.TextBody is { } body)
                {
                    foreach (TextParagraph p in body.Paragraphs)
                    {
                        lines.Add(p.Text);
                    }
                }
                else if (shape.Table is { } table)
                {
                    foreach (TableRow row in table.Rows)
                    {
                        foreach (TableCell cell in row.Cells)
                        {
                            if (!cell.IsMergedContinuation)
                            {
                                lines.Add(cell.Text);
                            }
                        }
                    }
                }
            }

            return string.Join('\n', lines);
        }
    }

    // The part reached by the first relationship of this part with the given type name, wrapped in its hub class.
    internal string? RelatedPartName(string typeName)
    {
        foreach (Relationship r in Relationships)
        {
            if (Pml.RelIs(r.Type, typeName) && Owner.ResolveTarget(PartName, r) is { } target)
            {
                return target;
            }
        }

        return null;
    }

    internal List<string> RelatedPartNames(string typeName)
    {
        var names = new List<string>();
        foreach (Relationship r in Relationships)
        {
            if (Pml.RelIs(r.Type, typeName) && Owner.ResolveTarget(PartName, r) is { } target)
            {
                names.Add(target);
            }
        }

        return names;
    }

    /// <summary>Gets the theme this part is formatted by (read from the theme part), or null when it has none.</summary>
    public abstract PresentationTheme? Theme { get; }

    /// <summary>Gets the colour map that turns the logical colours (<c>bg1</c>, <c>tx1</c>…) into theme slots, or null when none applies.</summary>
    public abstract ColorMap? ColorMap { get; }

    /// <summary>
    /// Resolves a scheme colour name as a shape writes it (<c>tx1</c>, <c>bg2</c>, <c>accent3</c>, <c>dk1</c>…) through the colour map and
    /// the theme. Colour transforms (<c>lumMod</c>, <c>tint</c>…) are not applied.
    /// </summary>
    /// <param name="name">The scheme colour name.</param>
    /// <returns>Six hex digits, or null when the colour cannot be resolved (<c>phClr</c>, a missing theme).</returns>
    public string? ResolveSchemeColor(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        PresentationTheme? theme = Theme;
        if (theme is null)
        {
            return null;
        }

        string? mapped = ColorMap?.Map(name);
        return theme.Color(mapped ?? name);
    }
}

/// <summary>A deterministic reading of the colour and font schemes of a DrawingML theme part.</summary>
/// <remarks>The theme part is read only.</remarks>
public sealed class PresentationTheme
{
    private readonly Dictionary<string, string> _colors;
    private readonly Dictionary<string, string> _fonts;

    private PresentationTheme(string? name, Dictionary<string, string> colors, Dictionary<string, string> fonts)
    {
        Name = name;
        _colors = colors;
        _fonts = fonts;
    }

    /// <summary>Gets the theme's name (<c>a:theme/@name</c>), or null.</summary>
    public string? Name { get; }

    /// <summary>Gets the colour of a scheme slot (<c>dk1</c>, <c>lt1</c>, <c>dk2</c>, <c>lt2</c>, <c>accent1</c>…<c>accent6</c>, <c>hlink</c>, <c>folHlink</c>) as six hex digits.</summary>
    /// <param name="slot">The slot name.</param>
    /// <returns>The colour, or null when the theme does not define it. A system colour reads as its last known value.</returns>
    public string? Color(string slot) => _colors.TryGetValue(slot, out string? v) ? v : null;

    /// <summary>Gets a theme font.</summary>
    /// <param name="major">True for the heading font, false for the body font.</param>
    /// <param name="script"><c>latin</c>, <c>ea</c> or <c>cs</c>.</param>
    /// <returns>The typeface, or null when absent or left empty to the script.</returns>
    public string? Font(bool major, string script = "latin") =>
        _fonts.TryGetValue((major ? "major/" : "minor/") + script, out string? v) && v.Length > 0 ? v : null;

    /// <summary>Resolves a theme font reference as written in a run (<c>+mj-lt</c>, <c>+mn-ea</c>…) to a typeface; any other value is returned as it is.</summary>
    /// <param name="typeface">The <c>typeface</c> attribute value.</param>
    /// <returns>The typeface, or null for an unresolvable theme reference.</returns>
    public string? ResolveTypeface(string? typeface)
    {
        if (typeface is null || typeface.Length < 6 || typeface[0] != '+' || typeface[3] != '-')
        {
            return typeface;
        }

        bool major = typeface.StartsWith("+mj", StringComparison.Ordinal);
        if (!major && !typeface.StartsWith("+mn", StringComparison.Ordinal))
        {
            return typeface;
        }

        // +mj-lt, +mj-ea, +mj-cs: the two letters after the dash name the script.
        string script = typeface[4..] switch { "lt" => "latin", var other => other };
        return Font(major, script);
    }

    internal static PresentationTheme Read(MarkupRef root)
    {
        var colors = new Dictionary<string, string>(StringComparer.Ordinal);
        var fonts = new Dictionary<string, string>(StringComparer.Ordinal);
        MarkupRef? elements = Pml.A(root, "themeElements");
        if (elements is not null)
        {
            // ECMA-376 Part 1 §20.1.4.1.1 (clrScheme): each slot holds an sRGB colour or a system colour with a last-known value.
            if (Pml.A(elements.Value, "clrScheme") is { } scheme)
            {
                foreach (MarkupRef slot in Pml.Elements(scheme))
                {
                    foreach (MarkupRef c in Pml.Elements(slot))
                    {
                        string? hex = c.LocalNameText == "srgbClr" ? Pml.Attr(c, "val") : Pml.Attr(c, "lastClr");
                        if (hex is not null)
                        {
                            colors[slot.LocalNameText] = hex.ToUpperInvariant();
                        }

                        break;
                    }
                }
            }

            // §20.1.4.1.18 (fontScheme): majorFont and minorFont each name a latin, east Asian and complex-script typeface.
            if (Pml.A(elements.Value, "fontScheme") is { } fontScheme)
            {
                foreach (MarkupRef group in Pml.Elements(fontScheme))
                {
                    string prefix = group.LocalNameText == "majorFont" ? "major" : group.LocalNameText == "minorFont" ? "minor" : string.Empty;
                    if (prefix.Length == 0)
                    {
                        continue;
                    }

                    foreach (MarkupRef face in Pml.Elements(group))
                    {
                        if (face.LocalNameText is "latin" or "ea" or "cs" && Pml.Attr(face, "typeface") is { } typeface)
                        {
                            fonts[prefix + "/" + face.LocalNameText] = typeface;
                        }
                    }
                }
            }
        }

        return new PresentationTheme(Pml.Attr(root, "name"), colors, fonts);
    }
}
