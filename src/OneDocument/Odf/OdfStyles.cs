using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Odf;

/// <summary>
/// One style of an OpenDocument file: a named <c>style:style</c> or <c>style:default-style</c> of a family, a list style
/// (<c>text:list-style</c>), a page layout, a master page or a data style. It is a view over the markup element; nothing is copied.
/// </summary>
public sealed class OdfStyle
{
    internal OdfStyle(string family, string name, MarkupNode element, bool isAutomatic, bool isDefault)
    {
        Family = family;
        Name = name;
        Element = element;
        IsAutomatic = isAutomatic;
        IsDefault = isDefault;
    }

    /// <summary>Gets the family: <c>paragraph</c>, <c>text</c>, <c>table</c>, <c>table-column</c>, <c>table-row</c>, <c>table-cell</c>, <c>graphic</c>, <c>section</c>, <c>ruby</c>, <c>presentation</c>, <c>drawing-page</c>, <c>chart</c>, plus <c>list</c> for list styles, <c>page-layout</c>, <c>master-page</c> and <c>data</c> for the other style-like elements.</summary>
    public string Family { get; }

    /// <summary>Gets the internal name (<c>style:name</c>); empty for a default style, which is named by its family.</summary>
    public string Name { get; }

    /// <summary>Gets a value indicating whether the style is automatic (lives in <c>office:automatic-styles</c>, made by direct formatting) rather than common.</summary>
    public bool IsAutomatic { get; }

    /// <summary>Gets a value indicating whether this is the family's <c>style:default-style</c>.</summary>
    public bool IsDefault { get; }

    /// <summary>Gets the element.</summary>
    public MarkupNode Element { get; }

    /// <summary>Gets the name shown to people (<c>style:display-name</c>), or the internal name when there is none.</summary>
    public string DisplayName => OdfMarkup.Attribute(Element, OdfNamespaces.Style, "display-name") ?? Name;

    /// <summary>Gets the name of the parent style (<c>style:parent-style-name</c>), or null.</summary>
    public string? ParentName => OdfMarkup.Attribute(Element, OdfNamespaces.Style, "parent-style-name");

    /// <summary>Gets the name of the style the next paragraph takes (<c>style:next-style-name</c>), or null.</summary>
    public string? NextName => OdfMarkup.Attribute(Element, OdfNamespaces.Style, "next-style-name");

    /// <summary>Gets the name of the list style (<c>style:list-style-name</c>), or null.</summary>
    public string? ListStyleName => OdfMarkup.Attribute(Element, OdfNamespaces.Style, "list-style-name");

    /// <summary>Gets the name of the master page (<c>style:master-page-name</c>), or null.</summary>
    public string? MasterPageName => OdfMarkup.Attribute(Element, OdfNamespaces.Style, "master-page-name");

    /// <summary>Gets the default outline level (<c>style:default-outline-level</c>) or 0 when none.</summary>
    public int DefaultOutlineLevel => int.TryParse(OdfMarkup.Attribute(Element, OdfNamespaces.Style, "default-outline-level"), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int v) ? v : 0;

    /// <summary>Gets the property child of a given local name (<c>text-properties</c>, <c>paragraph-properties</c>, <c>table-cell-properties</c>…), or a null node.</summary>
    /// <param name="localName">The local name in the <c>style:</c> namespace.</param>
    /// <returns>The element, or a null node.</returns>
    public MarkupNode Properties(string localName) => OdfMarkup.Child(Element, OdfNamespaces.Style, localName);
}

/// <summary>
/// The styles of an OpenDocument file, found in <c>styles.xml</c> (common styles, default styles, master pages, page layouts, list styles,
/// automatic styles for headers and footers) and <c>content.xml</c> (automatic styles): lookup by family and name, and inheritance along
/// <c>style:parent-style-name</c> ending in the family's default style (OpenDocument 1.3 Part 1, Styles).
/// </summary>
/// <remarks>
/// A style name is looked up in the content's automatic styles first, then the common styles, then the styles part's automatic styles.
/// A parent chain that loops, or runs longer than the number of styles, ends where it repeats and reports <c>odf.style-cycle</c> once.
/// </remarks>
public sealed class OdfStyleSet
{
    private readonly Dictionary<(string Family, string Name), OdfStyle> _byName = [];
    private readonly Dictionary<string, OdfStyle> _defaults = new(StringComparer.Ordinal);
    private readonly List<OdfStyle> _all = [];
    private readonly OpenContext _context;
    private bool _cycleReported;

    private OdfStyleSet(OpenContext context)
    {
        _context = context;
    }

    /// <summary>Gets every style in the order found: content automatic styles, then the styles part.</summary>
    public IReadOnlyList<OdfStyle> All => _all;

    internal static OdfStyleSet Load(OdfPackage package)
    {
        var set = new OdfStyleSet(package.Context);
        MarkupNode content = package.ContentRoot;
        MarkupNode styles = package.StylesRoot;
        set.AddAutomatic(OdfMarkup.Child(content, OdfNamespaces.Office, "automatic-styles"));
        if (!styles.IsNull)
        {
            set.AddCommon(OdfMarkup.Child(styles, OdfNamespaces.Office, "styles"));
            set.AddMasters(OdfMarkup.Child(styles, OdfNamespaces.Office, "master-styles"));
            if (!package.IsFlat)
            {
                set.AddAutomatic(OdfMarkup.Child(styles, OdfNamespaces.Office, "automatic-styles"));
            }
        }

        return set;
    }

    private void AddAutomatic(MarkupNode container)
    {
        if (!container.IsNull)
        {
            foreach (MarkupNode e in container.ChildElements)
            {
                Add(e, automatic: true);
            }
        }
    }

    private void AddCommon(MarkupNode container)
    {
        if (!container.IsNull)
        {
            foreach (MarkupNode e in container.ChildElements)
            {
                Add(e, automatic: false);
            }
        }
    }

    private void AddMasters(MarkupNode container)
    {
        if (!container.IsNull)
        {
            foreach (MarkupNode e in container.ChildElements)
            {
                Add(e, automatic: false);
            }
        }
    }

    private void Add(MarkupNode e, bool automatic)
    {
        string ns = e.NamespaceUri;
        string local = e.LocalNameString;
        string? family;
        bool isDefault = false;
        string? name = OdfMarkup.Attribute(e, OdfNamespaces.Style, "name");
        if (ns == OdfNamespaces.Style && local == "style")
        {
            family = OdfMarkup.Attribute(e, OdfNamespaces.Style, "family");
        }
        else if (ns == OdfNamespaces.Style && local == "default-style")
        {
            family = OdfMarkup.Attribute(e, OdfNamespaces.Style, "family");
            isDefault = true;
            name = string.Empty;
        }
        else if (ns == OdfNamespaces.Text && local == "list-style")
        {
            family = "list";
        }
        else if (ns == OdfNamespaces.Text && local == "outline-style")
        {
            family = "outline";
            name ??= "Outline";
        }
        else if (ns == OdfNamespaces.Style && local == "page-layout")
        {
            family = "page-layout";
        }
        else if (ns == OdfNamespaces.Style && local == "master-page")
        {
            family = "master-page";
        }
        else if (ns == OdfNamespaces.Number)
        {
            family = "data";
        }
        else
        {
            return;
        }

        if (family is null || name is null)
        {
            return;
        }

        var style = new OdfStyle(family, name, e, automatic, isDefault);
        _all.Add(style);
        if (isDefault)
        {
            _defaults.TryAdd(family, style);
        }
        else
        {
            _byName.TryAdd((family, name), style);
        }
    }

    /// <summary>Finds a named style.</summary>
    /// <param name="family">The family (see <see cref="OdfStyle.Family"/>).</param>
    /// <param name="name">The internal name.</param>
    /// <returns>The style, or null when there is none.</returns>
    public OdfStyle? Find(string family, string name) => name is not null && _byName.TryGetValue((family, name), out OdfStyle? s) ? s : null;

    /// <summary>Gets the default style of a family (<c>style:default-style</c>), or null.</summary>
    /// <param name="family">The family.</param>
    /// <returns>The default style, or null.</returns>
    public OdfStyle? GetDefault(string family) => _defaults.TryGetValue(family, out OdfStyle? s) ? s : null;

    /// <summary>
    /// Enumerates a style and its ancestors, nearest first, ending with the family's default style when there is one. The chain stops at a
    /// parent that cannot be found.
    /// </summary>
    /// <param name="style">The style to start from.</param>
    /// <returns>The style, its parent, its parent's parent…, then the default style.</returns>
    public IEnumerable<OdfStyle> Chain(OdfStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        var seen = new HashSet<OdfStyle>();
        OdfStyle? current = style;
        while (current is not null)
        {
            if (!seen.Add(current))
            {
                if (!_cycleReported)
                {
                    _cycleReported = true;
                    _context.Defects.Report(new Defect(DefectKind.Reference, "odf.style-cycle", DefectSeverity.Repaired,
                        "The style '" + style.Name + "' has a parent chain that loops; the loop was cut.", DefectLocation.InPart("styles.xml", Math.Max(0, current.Element.SourceStart))));
                }

                yield break;
            }

            yield return current;
            string? parent = current.ParentName;
            current = parent is null ? null : Find(current.Family, parent);
        }

        OdfStyle? def = GetDefault(style.Family);
        if (def is not null && seen.Add(def))
        {
            yield return def;
        }
    }

    /// <summary>
    /// Resolves one property of a style along its inheritance chain: the first style in <see cref="Chain"/> that has the property group
    /// with the attribute wins.
    /// </summary>
    /// <param name="style">The style to start from.</param>
    /// <param name="propertiesElement">The local name of the property group in the <c>style:</c> namespace, for example <c>text-properties</c>.</param>
    /// <param name="attributeNamespace">The attribute's namespace (for example <see cref="OdfNamespaces.Fo"/>).</param>
    /// <param name="attribute">The attribute's local name, for example <c>font-size</c>.</param>
    /// <returns>The value as written, or null when no style in the chain sets it.</returns>
    public string? Resolve(OdfStyle style, string propertiesElement, string attributeNamespace, string attribute)
    {
        foreach (OdfStyle s in Chain(style))
        {
            string? v = OdfMarkup.Attribute(s.Properties(propertiesElement), attributeNamespace, attribute);
            if (v is not null)
            {
                return v;
            }
        }

        return null;
    }
}
