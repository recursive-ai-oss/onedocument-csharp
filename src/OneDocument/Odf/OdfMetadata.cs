using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Odf;

/// <summary>
/// The document properties of <c>meta.xml</c> (<c>office:meta</c>, OpenDocument 1.3 Part 1, Metadata): title, creator, dates, keywords and
/// user-defined properties. Read-only; the markup tree of the part is the place to edit them.
/// </summary>
public sealed class OdfMetadata
{
    private readonly MarkupNode _meta;

    internal OdfMetadata(MarkupNode metaRoot)
    {
        _meta = OdfMarkup.Child(metaRoot, OdfNamespaces.Office, "meta");
    }

    /// <summary>Gets the title (<c>dc:title</c>), or null.</summary>
    public string? Title => Text(OdfNamespaces.DublinCore, "title");

    /// <summary>Gets the description (<c>dc:description</c>), or null.</summary>
    public string? Description => Text(OdfNamespaces.DublinCore, "description");

    /// <summary>Gets the subject (<c>dc:subject</c>), or null.</summary>
    public string? Subject => Text(OdfNamespaces.DublinCore, "subject");

    /// <summary>Gets the language (<c>dc:language</c>), or null.</summary>
    public string? Language => Text(OdfNamespaces.DublinCore, "language");

    /// <summary>Gets the person who last saved the document (<c>dc:creator</c>), or null.</summary>
    public string? LastModifiedBy => Text(OdfNamespaces.DublinCore, "creator");

    /// <summary>Gets the person who created the document (<c>meta:initial-creator</c>), or null.</summary>
    public string? Creator => Text(OdfNamespaces.Meta, "initial-creator");

    /// <summary>Gets the creation time (<c>meta:creation-date</c>) as written, an ISO 8601 date-time, or null.</summary>
    public string? Created => Text(OdfNamespaces.Meta, "creation-date");

    /// <summary>Gets the modification time (<c>dc:date</c>) as written, or null.</summary>
    public string? Modified => Text(OdfNamespaces.DublinCore, "date");

    /// <summary>Gets the producing application (<c>meta:generator</c>), or null.</summary>
    public string? Generator => Text(OdfNamespaces.Meta, "generator");

    /// <summary>Gets the keywords (each <c>meta:keyword</c>).</summary>
    public IReadOnlyList<string> Keywords
    {
        get
        {
            var list = new List<string>();
            if (!_meta.IsNull)
            {
                foreach (MarkupNode c in _meta.ChildElements)
                {
                    if (c.IsElement(OdfNamespaces.Meta, "keyword"))
                    {
                        list.Add(c.InnerText);
                    }
                }
            }

            return list;
        }
    }

    /// <summary>Gets the user-defined properties (<c>meta:user-defined</c>) as name and text value, in file order.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> UserDefined
    {
        get
        {
            var list = new List<KeyValuePair<string, string>>();
            if (!_meta.IsNull)
            {
                foreach (MarkupNode c in _meta.ChildElements)
                {
                    if (c.IsElement(OdfNamespaces.Meta, "user-defined") && OdfMarkup.Attribute(c, OdfNamespaces.Meta, "name") is { } name)
                    {
                        list.Add(new KeyValuePair<string, string>(name, c.InnerText));
                    }
                }
            }

            return list;
        }
    }

    /// <summary>Gets an attribute of <c>meta:document-statistic</c> (for example <c>page-count</c>, <c>word-count</c>), or null.</summary>
    /// <param name="name">The attribute's local name.</param>
    /// <returns>The value as written, or null.</returns>
    public string? Statistic(string name) =>
        OdfMarkup.Attribute(OdfMarkup.Child(_meta, OdfNamespaces.Meta, "document-statistic"), OdfNamespaces.Meta, name);

    private string? Text(string ns, string local)
    {
        MarkupNode c = OdfMarkup.Child(_meta, ns, local);
        return c.IsNull ? null : c.InnerText;
    }
}
