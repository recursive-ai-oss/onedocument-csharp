using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Properties;

/// <summary>
/// One of the three property parts of a package (core, extended, custom): found through the package relationships, parsed on first
/// use, created on first write, and written back only when edited.
/// </summary>
internal sealed class PropertyPart
{
    private readonly IOpcPackage _package;
    private readonly OpenContext _context;
    private readonly string _relationshipSuffix;
    private readonly string _defaultName;
    private readonly string _contentType;
    private readonly string _relationshipType;
    private readonly string _template;
    private MarkupDocument? _document;
    private string? _partName;
    private bool _loaded;
    private bool _unreadable;

    private readonly string _rootNamespace;
    private readonly string _rootLocalName;

    public PropertyPart(IOpcPackage package, OpenContext context, string relationshipSuffix, string relationshipType, string defaultName, string contentType, string template,
        string rootNamespace, string rootLocalName)
    {
        _rootNamespace = rootNamespace;
        _rootLocalName = rootLocalName;
        _package = package;
        _context = context;
        _relationshipSuffix = relationshipSuffix;
        _relationshipType = relationshipType;
        _defaultName = defaultName;
        _contentType = contentType;
        _template = template;
    }

    public string PartName => _partName ?? _defaultName;

    /// <summary>The root element when the part exists and can be read; null otherwise (and a defect reported once for an unreadable part).</summary>
    public MarkupNode Root
    {
        get
        {
            Load();
            return _document is null ? default : _document.Root;
        }
    }

    private void Load()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        foreach (Relationship r in _package.GetRelationships())
        {
            if (r.TargetMode != RelationshipTargetMode.Internal || !r.Type.EndsWith(_relationshipSuffix, StringComparison.Ordinal)
                || !PartUri.TryResolve(null, r.Target, out PartUri uri) || _package.FindPart(uri.Value) is not { } part)
            {
                continue;
            }

            _partName = part.Name;
            try
            {
                _document = MarkupDocument.Parse(part.GetBytes(), _context, part.Name);
                // A part without its root element holds no properties; writing into it would make a document that means nothing.
                if (_document.Root.IsNull || !_document.Root.IsElement(_rootNamespace, _rootLocalName))
                {
                    _document = null;
                    throw new OfficeFormatException(DefectKind.Structure, "props.part-unreadable", "the root element is not " + _rootLocalName, DefectLocation.InPart(part.Name));
                }
            }
            catch (OfficeException ex) when (ex is not OfficeLimitException)
            {
                _unreadable = true;
                _context.Defects.Report(new Defect(DefectKind.Packaging, "props.part-unreadable", DefectSeverity.Dropped,
                    "The properties part could not be read and reads as empty: " + ex.Defect.Message, DefectLocation.InPart(part.Name, ex.Location.Offset)));
            }

            return;
        }
    }

    /// <summary>The root element for writing: the existing one, or a new part created from the template.</summary>
    /// <exception cref="OfficeFormatException">The part exists but cannot be read, so nothing can be written into it (<c>props.part-unreadable</c>).</exception>
    public MarkupNode RootForWrite()
    {
        Load();
        if (_unreadable)
        {
            throw new OfficeFormatException(DefectKind.Packaging, "props.part-unreadable",
                "The properties part " + PartName + " cannot be read, so it cannot be changed.", DefectLocation.InPart(PartName));
        }

        if (_document is null)
        {
            string name = _defaultName;
            for (int i = 2; _package.FindPart(name) is not null; i++)
            {
                name = _defaultName.Insert(_defaultName.LastIndexOf('.'), i.ToString(CultureInfo.InvariantCulture));
            }

            byte[] bytes = Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n" + _template);
            _package.AddPart(name, _contentType, bytes);
            var rels = new List<Relationship>(_package.GetRelationships());
            int n = 1;
            while (rels.Exists(r => r.Id == "rId" + n.ToString(CultureInfo.InvariantCulture)))
            {
                n++;
            }

            rels.Add(new Relationship("rId" + n.ToString(CultureInfo.InvariantCulture), _relationshipType, name["/".Length..]));
            _package.SetRelationships(null, rels);
            _document = MarkupDocument.Parse(bytes, _context, name);
            _partName = name;
        }

        return _document.Root;
    }

    public void Commit()
    {
        if (_document is { IsModified: true } && _partName is not null && _package.FindPart(_partName) is { } part)
        {
            part.SetBytes(_document.ToArray());
        }
    }

    public bool IsModified => _document is { IsModified: true };

    // --- shared element helpers ---

    public static MarkupNode Find(MarkupNode root, string ns, string local)
    {
        if (root.IsNull)
        {
            return default;
        }

        for (MarkupNode n = root.FirstChild; !n.IsNull; n = n.NextSibling)
        {
            if (n.Kind == MarkupNodeKind.Element && n.IsElement(ns, local))
            {
                return n;
            }
        }

        return default;
    }

    /// <summary>Writes the text of the child <paramref name="local"/> of the root, creating or removing the element; null removes it.</summary>
    public void SetChildText(string ns, string local, string preferredPrefix, string? text, string? extraAttributes = null)
    {
        MarkupNode root = RootForWrite();
        MarkupNode existing = Find(root, ns, local);
        if (text is null)
        {
            if (!existing.IsNull)
            {
                existing.Remove();
            }

            return;
        }

        CheckXmlText(text);
        if (!existing.IsNull)
        {
            existing.SetText(text);
            return;
        }

        string name = Qualified(root, ns, local, preferredPrefix, out string declaration);
        root.AppendXml(Encoding.UTF8.GetBytes("<" + name + declaration + (extraAttributes ?? string.Empty) + ">" + Escape(text) + "</" + name + ">"));
    }

    /// <summary>The element name to write for <paramref name="ns"/> below <paramref name="root"/>, plus a namespace declaration when none is in scope.</summary>
    public static string Qualified(MarkupNode root, string ns, string local, string preferredPrefix, out string declaration)
    {
        declaration = string.Empty;
        if (root.Prefix.IsEmpty && root.NamespaceUri == ns)
        {
            return local;
        }

        if (root.TryGetPrefixInScope(ns, out string prefix))
        {
            return prefix + ":" + local;
        }

        declaration = " xmlns:" + preferredPrefix + "=\"" + ns + "\"";
        return preferredPrefix + ":" + local;
    }

    public static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length + 8);
        foreach (char c in text)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                default: sb.Append(c); break;
            }
        }

        return sb.ToString();
    }

    public static void CheckXmlText(string text)
    {
        foreach (char c in text)
        {
            if ((c < 0x20 && c is not ('\t' or '\n' or '\r')) || c is '\uFFFE' or '\uFFFF')
            {
                throw new ArgumentException("The text has a character that XML cannot carry.", nameof(text));
            }
        }
    }

    // W3CDTF (the profile of ISO 8601 that the core-properties dates of ECMA-376 Part 2 use): year, year-month, date, or date and time with a zone.
    private static readonly string[] s_dateFormats =
    [
        "yyyy", "yyyy-MM", "yyyy-MM-dd", "yyyy-MM-ddTHH:mmK", "yyyy-MM-ddTHH:mm:ssK", "yyyy-MM-ddTHH:mm:ss.FFFFFFFK",
        "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ss.FFFFFFF",
    ];

    public static DateTimeOffset? ParseDate(string? text)
    {
        if (text is null)
        {
            return null;
        }

        return DateTimeOffset.TryParseExact(text.Trim(), s_dateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset value) ? value : null;
    }

    public static string FormatDate(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
