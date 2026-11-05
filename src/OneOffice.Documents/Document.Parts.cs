using System.Globalization;
using System.Text;
using RecursiveAI.OneOffice.Documents.Numbering;
using RecursiveAI.OneOffice.Documents.Styles;
using RecursiveAI.OneOffice.Markup;
using RecursiveAI.OneOffice.Packaging;

namespace RecursiveAI.OneOffice.Documents;

public sealed partial class Document
{
    private StyleSheet? _styles;
    private NumberingDefinitions? _numbering;
    private ThemeData? _theme;

    /// <summary>
    /// Gets the style sheet (<c>/word/styles.xml</c>). A document without one has an empty sheet; adding a style creates the part.
    /// A styles part that cannot be read is reported (<c>docx.part-unreadable</c>) and reads as empty.
    /// </summary>
    public StyleSheet Styles
    {
        get
        {
            if (Volatile.Read(ref _styles) is { } existing)
            {
                return existing;
            }

            lock (Parts.Gate)
            {
                if (_styles is null)
                {
                    MarkupDocument? doc = Parts.LoadRelated(MainPartName, "styles", out string? name);
                    _styles = new StyleSheet(this, doc, name);
                }

                return _styles;
            }
        }
    }

    /// <summary>
    /// Gets the numbering definitions (<c>/word/numbering.xml</c>). A document without one has an empty set; adding a definition
    /// creates the part.
    /// </summary>
    public NumberingDefinitions Numbering
    {
        get
        {
            if (Volatile.Read(ref _numbering) is { } existing)
            {
                return existing;
            }

            lock (Parts.Gate)
            {
                if (_numbering is null)
                {
                    MarkupDocument? doc = Parts.LoadRelated(MainPartName, "numbering", out string? name);
                    _numbering = new NumberingDefinitions(this, doc, name);
                }

                return _numbering;
            }
        }
    }

    /// <summary>Gets the theme the document's fonts and colours refer to (read only); <see cref="ThemeData.Empty"/> when there is none.</summary>
    public ThemeData Theme
    {
        get
        {
            if (Volatile.Read(ref _theme) is { } existing)
            {
                return existing;
            }

            lock (Parts.Gate)
            {
                if (_theme is null)
                {
                    MarkupDocument? doc = Parts.LoadRelated(MainPartName, "theme", out _);
                    _theme = doc is null ? ThemeData.Empty : ThemeData.Read(new RecursiveAI.OneOffice.Schema.MarkupRef(doc.Root));
                }

                return _theme;
            }
        }
    }

    /// <summary>Adds a new empty part related to the main part and returns its parsed tree.</summary>
    internal MarkupDocument CreateRelatedPart(string relationshipName, string preferredName, string contentType, string rootName, out string partName)
    {
        bool strict = IsStrict;
        string ns = strict ? "http://purl.oclc.org/ooxml/wordprocessingml/main" : "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        string relBase = strict ? "http://purl.oclc.org/ooxml/officeDocument/relationships/" : "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";

        partName = preferredName;
        for (int i = 2; Package.FindPart(partName) is not null; i++)
        {
            partName = preferredName.Insert(preferredName.LastIndexOf('.'), i.ToString(CultureInfo.InvariantCulture));
        }

        byte[] content = Encoding.UTF8.GetBytes(
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<w:" + rootName + " xmlns:w=\"" + ns + "\"/>");
        Package.AddPart(partName, contentType, content);

        var rels = new List<Relationship>(Package.GetRelationships(MainPartName));
        int n = 1;
        while (rels.Exists(r => r.Id == "rId" + n.ToString(CultureInfo.InvariantCulture)))
        {
            n++;
        }

        string target = partName.StartsWith("/word/", StringComparison.OrdinalIgnoreCase) ? partName[6..] : partName;
        rels.Add(new Relationship("rId" + n.ToString(CultureInfo.InvariantCulture), relBase + relationshipName, target));
        Package.SetRelationships(MainPartName, rels);
        return Parts.Load(partName, required: true)!;
    }
}
