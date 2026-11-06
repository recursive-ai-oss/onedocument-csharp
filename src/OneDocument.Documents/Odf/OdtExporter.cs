using System.Buffers;
using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Odf;
using RecursiveAI.OneDocument.Packaging;
using System.Linq;
using RecursiveAI.OneDocument.Properties;

namespace RecursiveAI.OneDocument.Documents.Odf;

// The document hub -> OpenDocument text. The word-processing markup is read through its tree (the one the hub sits on) and written as
// content.xml, styles.xml and meta.xml of an OdfPackage, which the caller then writes as a zip (ODT, OTT) or as one flat XML file (FODT).
internal sealed partial class OdtExporter
{
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string WStrict = "http://purl.oclc.org/ooxml/wordprocessingml/main";
    private const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string RStrict = "http://purl.oclc.org/ooxml/officeDocument/relationships";
    private const string Wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    private const string WpStrict = "http://purl.oclc.org/ooxml/drawingml/wordprocessingDrawing";
    private const string ANs = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private const string ANsStrict = "http://purl.oclc.org/ooxml/drawingml/main";
    private const string Mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private const string VNs = "urn:schemas-microsoft-com:vml";
    private const string MNs = "http://schemas.openxmlformats.org/officeDocument/2006/math";

    private readonly Document _doc;
    private readonly LossReport _loss;
    private readonly OpenContext _ctx;
    private readonly CancellationToken _ct;
    private OdfPackage _out = null!;
    private string _part;
    private int _depth;

    private OdtExporter(Document doc, LossReport loss, CancellationToken ct)
    {
        _doc = doc;
        _loss = loss;
        _ctx = doc.Context;
        _ct = ct;
        _part = doc.MainPartName;
    }

    /// <summary>Writes the document as ODT, OTT or FODT.</summary>
    internal static void Write(Document doc, OfficeFormat format, Stream destination, LossReport loss, CancellationToken cancellationToken)
    {
        var e = new OdtExporter(doc, loss, cancellationToken);
        try
        {
            e.Run(format, destination);
        }
        catch (Exception ex) when (ex is not (OfficeException or OperationCanceledException or IOException))
        {
            throw new OfficeFormatException(DefectKind.Structure, "odf.export-failed", "The document could not be written as OpenDocument: " + ex.GetType().Name + ".", DefectLocation.InPart(doc.MainPartName), ex);
        }
    }

    // ---- helpers over the markup tree ---------------------------------------------------------------------------------------------

    private static bool IsW(MarkupNode n, string local) => n.Kind == MarkupNodeKind.Element && n.LocalNameString == local && n.NamespaceUri is W or WStrict;

    private static string? A(MarkupNode n, string local) => n.IsNull ? null : n.GetAttribute(W, local) ?? n.GetAttribute(WStrict, local) ?? n.GetAttribute(local);

    private static string? RA(MarkupNode n, string local) => n.IsNull ? null : n.GetAttribute(R, local) ?? n.GetAttribute(RStrict, local);

    private static MarkupNode Child(MarkupNode n, string local)
    {
        if (n.IsNull)
        {
            return default;
        }

        foreach (MarkupNode c in n.ChildElements)
        {
            if (IsW(c, local))
            {
                return c;
            }
        }

        return default;
    }

    private static bool On(MarkupNode e)
    {
        string? v = A(e, "val");
        return v is null || !(v is "0" or "false" or "off");
    }

    private static bool OnChild(MarkupNode parent, string local)
    {
        MarkupNode c = Child(parent, local);
        return !c.IsNull && On(c);
    }

    private static int Int(string? s, int fallback = 0) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;

    private static string Esc(string s) => OdfUnits.Esc(s);

    private static string Cm(long twips) => OdfUnits.TwipsToCm(twips);

    private MarkupNode LoadPart(string name)
    {
        MarkupDocument? d = _doc.Parts.Load(name, required: false);
        return d is null ? default : d.Root;
    }

    private MarkupNode LoadRelated(string relName, out string? partName)
    {
        MarkupDocument? d = _doc.LoadRelatedByType(_doc.OfficeRelationship(relName), out partName);
        return d is null ? default : d.Root;
    }

    private IPackagePart? Target(string relId)
    {
        foreach (Relationship r in _doc.Package.GetRelationships(_part))
        {
            if (r.Id == relId && r.TargetMode == RelationshipTargetMode.Internal && PartUri.TryResolve(new PartUri(_part), r.Target, out PartUri uri))
            {
                return _doc.Package.FindPart(uri.Value);
            }
        }

        return null;
    }

    private Relationship? RelationshipOf(string relId)
    {
        foreach (Relationship r in _doc.Package.GetRelationships(_part))
        {
            if (r.Id == relId)
            {
                return r;
            }
        }

        return null;
    }

    private void Lost(string code, string feature, string message, long count = 1) => _loss.Report(code, feature, count, message);

    // ---- run ------------------------------------------------------------------------------------------------------------------

    private MarkupNode _stylesRoot, _numberingRoot, _footnotesRoot, _endnotesRoot, _commentsRoot, _settingsRoot;
    private bool _evenAndOdd;

    private void Run(OfficeFormat format, Stream destination)
    {
        string mime = format switch
        {
            OfficeFormat.Ott => OdfMediaTypes.TextTemplate,
            _ => OdfMediaTypes.Text,
        };
        _out = OdfPackage.Create(mime, _ctx);
        MarkupNode docRoot = _doc.MainMarkup.Root;
        MarkupNode body = Child(docRoot, "body");
        _stylesRoot = LoadRelated("styles", out _);
        _numberingRoot = LoadRelated("numbering", out _);
        _footnotesRoot = LoadRelated("footnotes", out _);
        _endnotesRoot = LoadRelated("endnotes", out _);
        _commentsRoot = LoadRelated("comments", out _);
        _settingsRoot = LoadRelated("settings", out _);
        _evenAndOdd = !_settingsRoot.IsNull && !Child(_settingsRoot, "evenAndOddHeaders").IsNull;

        ReadStyles();
        FindSections(body);
        var content = new StringBuilder();
        if (!body.IsNull)
        {
            BodyBlocks(content, body);
        }

        CloseLists(content);
        string stylesXml = BuildStyles();
        string contentXml = BuildContent(content.ToString());
        _out.SetEntry(OdfPackage.ContentName, Encoding.UTF8.GetBytes(contentXml), "text/xml");
        _out.SetEntry(OdfPackage.StylesName, Encoding.UTF8.GetBytes(stylesXml), "text/xml");
        _out.SetEntry(OdfPackage.MetaName, Encoding.UTF8.GetBytes(BuildMeta()), "text/xml");
        foreach ((string name, byte[] data, string mediaType) in _media)
        {
            _out.SetEntry(name, data, mediaType);
        }

        if (format == OfficeFormat.Fodt)
        {
            _out.WriteFlatTo(destination, _ct);
        }
        else
        {
            _out.WriteTo(destination, _ct);
        }
    }

    private const string Namespaces =
        " xmlns:office=\"" + OdfNamespaces.Office + "\" xmlns:style=\"" + OdfNamespaces.Style + "\" xmlns:text=\"" + OdfNamespaces.Text +
        "\" xmlns:table=\"" + OdfNamespaces.Table + "\" xmlns:draw=\"" + OdfNamespaces.Draw + "\" xmlns:fo=\"" + OdfNamespaces.Fo +
        "\" xmlns:xlink=\"" + OdfNamespaces.XLink + "\" xmlns:dc=\"" + OdfNamespaces.DublinCore + "\" xmlns:meta=\"" + OdfNamespaces.Meta +
        "\" xmlns:number=\"" + OdfNamespaces.Number + "\" xmlns:svg=\"" + OdfNamespaces.Svg + "\"";

    private static readonly char[] KeywordSeparators = [',', ';'];

    private const string XmlDecl = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n";

    private string BuildContent(string body)
    {
        var sb = new StringBuilder(XmlDecl);
        sb.Append("<office:document-content").Append(Namespaces).Append(" office:version=\"1.3\">");
        sb.Append("<office:font-face-decls>").Append(FontDecls()).Append("</office:font-face-decls>");
        sb.Append("<office:automatic-styles>").Append(_contentAuto.Xml).Append("</office:automatic-styles>");
        sb.Append("<office:body><office:text>");
        if (_changeRegions.Length > 0)
        {
            sb.Append("<text:tracked-changes text:track-changes=\"false\">").Append(_changeRegions).Append("</text:tracked-changes>");
        }

        sb.Append(body).Append("</office:text></office:body></office:document-content>");
        return sb.ToString();
    }

    private string BuildMeta()
    {
        var sb = new StringBuilder(XmlDecl);
        sb.Append("<office:document-meta").Append(Namespaces).Append(" office:version=\"1.3\"><office:meta>");
        CoreProperties core = _doc.Properties.Core;
        sb.Append("<meta:generator>OneDocument</meta:generator>");
        if (core.Title is { Length: > 0 } t)
        {
            sb.Append("<dc:title>").Append(Esc(t)).Append("</dc:title>");
        }

        if (core.Description is { Length: > 0 } d)
        {
            sb.Append("<dc:description>").Append(Esc(d)).Append("</dc:description>");
        }

        if (core.Subject is { Length: > 0 } s)
        {
            sb.Append("<dc:subject>").Append(Esc(s)).Append("</dc:subject>");
        }

        if (core.Keywords is { Length: > 0 } k)
        {
            foreach (string kw in k.Split(KeywordSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                sb.Append("<meta:keyword>").Append(Esc(kw)).Append("</meta:keyword>");
            }
        }

        if (core.Creator is { Length: > 0 } c)
        {
            sb.Append("<meta:initial-creator>").Append(Esc(c)).Append("</meta:initial-creator>");
        }

        if (core.Created is { } created)
        {
            sb.Append("<meta:creation-date>").Append(created.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)).Append("</meta:creation-date>");
        }

        if (core.LastModifiedBy is { Length: > 0 } lm)
        {
            sb.Append("<dc:creator>").Append(Esc(lm)).Append("</dc:creator>");
        }

        if (core.Modified is { } modified)
        {
            sb.Append("<dc:date>").Append(modified.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)).Append("</dc:date>");
        }

        if (core.Language is { Length: > 0 } lang)
        {
            sb.Append("<dc:language>").Append(Esc(lang)).Append("</dc:language>");
        }

        foreach (CustomProperty p in _doc.Properties.Custom.List())
        {
            sb.Append("<meta:user-defined meta:name=\"").Append(Esc(p.Name)).Append("\">").Append(Esc(p.Text)).Append("</meta:user-defined>");
        }

        sb.Append("</office:meta></office:document-meta>");
        return sb.ToString();
    }

    // ---- fonts -------------------------------------------------------------------------------------------------------------------

    private readonly SortedSet<string> _fonts = new(StringComparer.Ordinal);

    private string UseFont(string name)
    {
        _fonts.Add(name);
        return name;
    }

    private string FontDecls()
    {
        var sb = new StringBuilder();
        foreach (string f in _fonts)
        {
            string family = f.Contains(' ', StringComparison.Ordinal) ? "'" + f + "'" : f;
            sb.Append("<style:font-face style:name=\"").Append(Esc(f)).Append("\" svg:font-family=\"").Append(Esc(family)).Append("\"/>");
        }

        return sb.ToString();
    }

    // ---- automatic styles ----------------------------------------------------------------------------------------------------------

    private sealed class AutoStyles
    {
        public readonly StringBuilder Xml = new();
        public readonly Dictionary<string, string> Cache = new(StringComparer.Ordinal);
    }

    private readonly AutoStyles _contentAuto = new();
    private readonly AutoStyles _stylesAuto = new();
    private AutoStyles _auto = null!;
    private readonly Dictionary<string, int> _autoCounters = new(StringComparer.Ordinal);
    private readonly StringBuilder _changeRegions = new();

    private string AddAuto(string family, string prefix, string? parent, string attrs, string children)
    {
        _auto ??= _contentAuto;
        string key = family + "|" + parent + "|" + attrs + "|" + children;
        if (_auto.Cache.TryGetValue(key, out string? existing))
        {
            return existing;
        }

        _autoCounters.TryGetValue(prefix, out int n);
        _autoCounters[prefix] = ++n;
        string name = prefix + OdfUnits.I(n);
        _auto.Cache[key] = name;
        _auto.Xml.Append("<style:style style:name=\"").Append(name).Append("\" style:family=\"").Append(family).Append('"');
        if (parent is not null)
        {
            _auto.Xml.Append(" style:parent-style-name=\"").Append(Esc(parent)).Append('"');
        }

        _auto.Xml.Append(attrs).Append(children.Length == 0 ? "/>" : ">" + children + "</style:style>");
        return name;
    }

    private readonly List<(string Name, byte[] Data, string MediaType)> _media = [];
}
