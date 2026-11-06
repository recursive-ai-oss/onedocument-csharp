using System.Buffers;
using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Markup.Xml;
using RecursiveAI.OneDocument.Odf;
using RecursiveAI.OneDocument.Packaging;
using System.Linq;
using RecursiveAI.OneDocument.Properties;

namespace RecursiveAI.OneDocument.Documents.Odf;

// Converts an OpenDocument text document (ODT, OTT, FODT) into an in-memory DOCX package that the document hub wraps like any other. This
// file holds the shared plumbing and the package assembly; styles, lists, the body, tables, sections and drawings are in the other parts.
internal sealed partial class OdtImporter
{
    internal const string W06 = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string RNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string WpNs = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    private const string ANs = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private const string PicNs = "http://schemas.openxmlformats.org/drawingml/2006/picture";
    private const string VNs = "urn:schemas-microsoft-com:vml";
    private const string ONs = "urn:schemas-microsoft-com:office:office";
    private const string W10Ns = "urn:schemas-microsoft-com:office:word";
    private const string RelBase = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private const string CtBase = "application/vnd.openxmlformats-officedocument.wordprocessingml.";

    private readonly OdfPackage _pkg;
    private readonly OpenContext _ctx;
    private readonly ILossSink _loss;
    private readonly CancellationToken _ct;
    private readonly OdfStyleSet _styles;
    private readonly bool _template;
    private Story _main = null!;
    private readonly List<(Story Story, string ContentType)> _extraParts = [];
    private readonly List<(string Name, string ContentType, byte[] Data)> _mediaFiles = [];
    private readonly Dictionary<string, string> _mediaByHref = new(StringComparer.Ordinal);
    private int _drawingId = 1;
    private int _revisionId = 1;
    private int _bookmarkId = 1;
    private int _mediaCount;
    private int _depth;

    public OdtImporter(OdfPackage package, OpenContext context, ILossSink loss, bool template, CancellationToken cancellationToken)
    {
        _pkg = package;
        _ctx = context;
        _loss = loss;
        _template = template;
        _ct = cancellationToken;
        _styles = package.Styles;
    }

    /// <summary>One part being written: its buffer, its XML writer and the relationships its content needs.</summary>
    internal sealed class Story
    {
        private readonly ArrayBufferWriter<byte> _buffer = new();

        public Story(string partName)
        {
            PartName = partName;
            W = new XmlWriter(_buffer);
        }

        public string PartName { get; }

        public XmlWriter W { get; }

        public List<Relationship> Rels { get; } = [];

        public bool EndsWithParagraph { get; set; }

        public ReadOnlyMemory<byte> Bytes => _buffer.WrittenMemory;

        public string AddRel(string type, string target, bool external = false)
        {
            string id = "rId" + (Rels.Count + 1).ToString(CultureInfo.InvariantCulture);
            Rels.Add(new Relationship(id, type, target, external ? RelationshipTargetMode.External : RelationshipTargetMode.Internal));
            return id;
        }

        public void Raw(string xml) => W.WriteRaw(Encoding.UTF8.GetBytes(xml));

        public void Begin(string root)
        {
            W.WriteXmlDeclaration();
            W.WriteStartElement(root);
            W.WriteNamespaceDeclaration("w", W06);
            W.WriteNamespaceDeclaration("r", RNs);
            W.WriteNamespaceDeclaration("wp", WpNs);
            W.WriteNamespaceDeclaration("a", ANs);
            W.WriteNamespaceDeclaration("pic", PicNs);
            W.WriteNamespaceDeclaration("v", VNs);
            W.WriteNamespaceDeclaration("o", ONs);
            W.WriteNamespaceDeclaration("w10", W10Ns);
        }

        public void End()
        {
            W.WriteEndElement();
            W.Complete();
        }
    }

    public OpcPackage Convert()
    {
        var package = OpcPackage.Create(_ctx.Limits);
        try
        {
            MarkupNode content = _pkg.ContentRoot;
            if (content.IsNull)
            {
                throw new OfficeFormatException(DefectKind.Structure, "odf.no-content", "The OpenDocument file has no content.xml.", DefectLocation.InPart("content.xml", 0));
            }

            MarkupNode body = OdfMarkup.Child(OdfMarkup.Child(content, OdfNamespaces.Office, "body"), OdfNamespaces.Office, "text");
            if (body.IsNull)
            {
                string actual = OdfMediaTypes.FromBody(_pkg.Body);
                throw new OfficeFormatException(DefectKind.Structure, "odf.not-text", actual.Length > 0
                    ? "The OpenDocument file holds " + actual + ", not a text document."
                    : "The OpenDocument file has no office:text body.", DefectLocation.InPart("content.xml", Math.Max(0, content.SourceStart)));
            }

            ScanDocument(body);
            PrepareFonts();
            PrepareStyleIds();
            _main = new Story("/word/document.xml");
            _main.Begin("w:document");
            _main.W.WriteStartElement("w:body");
            EmitBody(body);
            _main.W.WriteEndElement();
            _main.End();

            AssemblePackage(package);
            return package;
        }
        catch
        {
            package.Dispose();
            throw;
        }
    }

    private void AssemblePackage(OpcPackage package)
    {
        var rootRels = new List<Relationship> { new("rId1", RelBase + "officeDocument", "word/document.xml") };
        var parts = new List<(Story Story, string ContentType)>();

        Story styles = BuildStyles();
        _main.Rels.Insert(0, new Relationship("rIdStyles", RelBase + "styles", "styles.xml"));
        parts.Add((styles, CtBase + "styles+xml"));

        Story? numbering = BuildNumbering();
        if (numbering is not null)
        {
            _main.Rels.Add(new Relationship("rIdNumbering", RelBase + "numbering", "numbering.xml"));
            parts.Add((numbering, CtBase + "numbering+xml"));
        }

        Story settings = BuildSettings();
        _main.Rels.Add(new Relationship("rIdSettings", RelBase + "settings", "settings.xml"));
        parts.Add((settings, CtBase + "settings+xml"));

        if (_footnotes.Count > 0)
        {
            parts.Add((BuildNotes("w:footnotes", "w:footnote", "/word/footnotes.xml", _footnotes, "FootnoteReference"), CtBase + "footnotes+xml"));
            _main.Rels.Add(new Relationship("rIdFootnotes", RelBase + "footnotes", "footnotes.xml"));
        }

        if (_endnotes.Count > 0)
        {
            parts.Add((BuildNotes("w:endnotes", "w:endnote", "/word/endnotes.xml", _endnotes, "EndnoteReference"), CtBase + "endnotes+xml"));
            _main.Rels.Add(new Relationship("rIdEndnotes", RelBase + "endnotes", "endnotes.xml"));
        }

        if (_comments.Count > 0)
        {
            parts.Add((BuildComments(), CtBase + "comments+xml"));
            _main.Rels.Add(new Relationship("rIdComments", RelBase + "comments", "comments.xml"));
        }

        foreach ((Story story, string ct) in _extraParts)
        {
            parts.Add((story, ct));
        }

        string mainType = CtBase + (_template ? "template.main+xml" : "document.main+xml");
        package.AddPart("/word/document.xml", mainType, _main.Bytes);
        foreach ((Story story, string ct) in parts)
        {
            package.AddPart(story.PartName, ct, story.Bytes);
            if (story.Rels.Count > 0)
            {
                package.SetRelationships(story.PartName, story.Rels);
            }
        }

        package.SetRelationships("/word/document.xml", _main.Rels);
        foreach ((string name, string contentType, byte[] data) in _mediaFiles)
        {
            package.AddPart(name, contentType, data);
        }

        AddDocumentProperties(package, rootRels);
        package.SetRelationships(null, rootRels);
    }

    // ---- reading helpers -------------------------------------------------------------------------------------------------------

    private static string? A(MarkupNode n, string ns, string local) => n.GetAttribute(ns, local);

    private static bool Is(MarkupNode n, string ns, string local) => n.Kind == MarkupNodeKind.Element && n.IsElement(ns, local);

    private static bool IsTrue(string? v) => v is "true" or "1";

    private void Defect(DefectKind kind, string code, DefectSeverity severity, string message, MarkupNode at, string part = "content.xml") =>
        _ctx.Defects.Report(new Defect(kind, code, severity, message, DefectLocation.InPart(part, Math.Max(0, at.IsNull ? 0 : at.SourceStart))));

    private void Lost(string code, string feature, string message, long count = 1) => _loss.Report(code, feature, count, message);

    private void Unmapped(MarkupNode n) =>
        _loss.Report("loss.unmapped-markup." + n.LocalNameString, "unmapped markup (" + n.LocalNameString + ")", 1, "Elements with no counterpart in the document model were skipped.");

    private void CheckDepth(MarkupNode at)
    {
        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxRecursionDepth), _ctx.Limits.MaxRecursionDepth, _depth, DefectLocation.InPart("content.xml", Math.Max(0, at.SourceStart)));
    }

    // ---- document properties ----------------------------------------------------------------------------------------------------

    private static ReadOnlyMemory<byte> Plain(Action<XmlWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new XmlWriter(buffer);
        w.WriteXmlDeclaration();
        write(w);
        w.Complete();
        return buffer.WrittenMemory;
    }

    private void AddDocumentProperties(OpcPackage package, List<Relationship> rootRels)
    {
        OdfMetadata meta = _pkg.Metadata;
        var core = new List<(string Element, string Value, bool Date)>();
        void Add(string element, string? value, bool date = false)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            if (date && !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _))
            {
                _ctx.Defects.Report(new Defect(DefectKind.Value, "odf.bad-date", DefectSeverity.Dropped, "The date '" + value + "' in the document properties is not a date.", DefectLocation.InPart("meta.xml")));
                return;
            }

            core.Add((element, value, date));
        }

        Add("dc:title", meta.Title);
        Add("dc:subject", meta.Subject);
        Add("dc:creator", meta.Creator);
        Add("cp:keywords", string.Join(", ", meta.Keywords));
        Add("dc:description", meta.Description);
        Add("cp:lastModifiedBy", meta.LastModifiedBy);
        Add("dcterms:created", NormalizeDate(meta.Created), date: true);
        Add("dcterms:modified", NormalizeDate(meta.Modified), date: true);
        Add("dc:language", meta.Language);
        if (core.Count > 0)
        {
            package.AddPart("/docProps/core.xml", "application/vnd.openxmlformats-package.core-properties+xml", Plain(w =>
            {
                w.WriteStartElement("cp:coreProperties");
                w.WriteNamespaceDeclaration("cp", "http://schemas.openxmlformats.org/package/2006/metadata/core-properties");
                w.WriteNamespaceDeclaration("dc", "http://purl.org/dc/elements/1.1/");
                w.WriteNamespaceDeclaration("dcterms", "http://purl.org/dc/terms/");
                w.WriteNamespaceDeclaration("xsi", "http://www.w3.org/2001/XMLSchema-instance");
                // The schema fixes the order of the core properties.
                string[] order = ["dc:title", "dc:subject", "dc:creator", "cp:keywords", "dc:description", "cp:lastModifiedBy", "dcterms:created", "dcterms:modified", "dc:language"];
                foreach (string name in order)
                {
                    foreach ((string element, string value, bool date) in core)
                    {
                        if (element != name)
                        {
                            continue;
                        }

                        w.WriteStartElement(element);
                        if (date)
                        {
                            w.WriteAttribute("xsi:type", "dcterms:W3CDTF");
                        }

                        w.WriteText(value);
                        w.WriteEndElement();
                    }
                }

                w.WriteEndElement();
            }));
            rootRels.Add(new Relationship("rId2", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties", "docProps/core.xml"));
        }

        var app = new List<(string Element, string Value)>();
        foreach ((string attr, string element) in new[] { ("page-count", "Pages"), ("word-count", "Words"), ("character-count", "CharactersWithSpaces"), ("paragraph-count", "Paragraphs") })
        {
            string? v = meta.Statistic(attr);
            if (v is not null && int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            {
                app.Add((element, v));
            }
        }

        if (app.Count > 0)
        {
            package.AddPart("/docProps/app.xml", "application/vnd.openxmlformats-officedocument.extended-properties+xml", Plain(w =>
            {
                w.WriteStartElement("Properties");
                w.WriteNamespaceDeclaration(null, "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties");
                // The extended properties are a sequence in the schema's order.
                foreach (string name in AppOrder)
                {
                    foreach ((string element, string value) in app)
                    {
                        if (element == name)
                        {
                            w.WriteStartElement(element);
                            w.WriteText(value);
                            w.WriteEndElement();
                        }
                    }
                }

                w.WriteEndElement();
            }));
            rootRels.Add(new Relationship("rId3", RelBase + "extended-properties", "docProps/app.xml"));
        }

        IReadOnlyList<KeyValuePair<string, string>> custom = meta.UserDefined;
        if (custom.Count > 0)
        {
            package.AddPart("/docProps/custom.xml", "application/vnd.openxmlformats-officedocument.custom-properties+xml", Plain(w =>
            {
                w.WriteStartElement("Properties");
                w.WriteNamespaceDeclaration(null, "http://schemas.openxmlformats.org/officeDocument/2006/custom-properties");
                w.WriteNamespaceDeclaration("vt", "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes");
                int pid = 2;
                foreach (KeyValuePair<string, string> kv in custom)
                {
                    w.WriteStartElement("property");
                    w.WriteAttribute("fmtid", "{D5CDD505-2E9C-101B-9397-08002B2CF9AE}");
                    w.WriteAttribute("pid", (pid++).ToString(CultureInfo.InvariantCulture));
                    w.WriteAttribute("name", kv.Key);
                    w.WriteStartElement("vt:lpwstr");
                    w.WriteText(kv.Value);
                    w.WriteEndElement();
                    w.WriteEndElement();
                }

                w.WriteEndElement();
            }));
            rootRels.Add(new Relationship("rId4", RelBase + "custom-properties", "docProps/custom.xml"));
        }
    }

    private static readonly string[] AppOrder = ["Pages", "Words", "Paragraphs", "CharactersWithSpaces"];

    // ODF dates are ISO 8601 date-times without a zone (local time) or with one; the core properties want W3CDTF with a zone.
    private static string? NormalizeDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset d))
        {
            return d.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }

        return value;
    }

    // ---- settings --------------------------------------------------------------------------------------------------------------

    private Story BuildSettings()
    {
        var story = new Story("/word/settings.xml");
        story.Begin("w:settings");
        XmlWriter w = story.W;
        OdfStyle? def = _styles.GetDefault("paragraph");
        string? stop = def is null ? null : OdfMarkup.Attribute(def.Properties("paragraph-properties"), OdfNamespaces.Style, "tab-stop-distance");
        if (stop is not null && OdfUnits.TryTwips(stop, out int twips) && twips > 0)
        {
            w.WriteStartElement("w:defaultTabStop");
            w.WriteAttribute("w:val", OdfUnits.I(twips));
            w.WriteEndElement();
        }

        if (_usesEvenOdd)
        {
            w.WriteStartElement("w:evenAndOddHeaders");
            w.WriteEndElement();
        }

        story.End();
        return story;
    }

    private bool _usesEvenOdd;
}
