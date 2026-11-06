using System.Buffers;
using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Markup.Xml;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Documents.Xml2003;

// Converts a WordprocessingML 2003 file into a DOCX package. This file holds the shared plumbing and the package assembly; the body, the
// property bags and the styles and lists are in the other parts of the class.
internal sealed partial class WordMl2003Converter
{
    internal const string W06 = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string RNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string WpNs = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    private const string ANs = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private const string PicNs = "http://schemas.openxmlformats.org/drawingml/2006/picture";
    private const string RelBase = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private const string CtBase = "application/vnd.openxmlformats-officedocument.wordprocessingml.";

    private readonly MarkupDocument _doc;
    private readonly OpenContext _ctx;
    private readonly ILossSink _loss;
    private readonly CancellationToken _ct;
    private readonly Dictionary<string, MarkupNode> _binData = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _mediaParts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MarkupNode> _commentContent = new(StringComparer.Ordinal);
    private readonly HashSet<string> _commentMarked = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _commentIds = new(StringComparer.Ordinal);
    private readonly List<(int Id, string Aml)> _comments = [];
    private readonly List<(int Id, MarkupNode Node)> _footnotes = [];
    private readonly List<(int Id, MarkupNode Node)> _endnotes = [];
    private readonly HashSet<MarkupNode> _consumedSections = [];
    private MarkupNode _finalSection;
    private Story _main = null!;
    private int _headerCount;
    private int _footerCount;
    private int _mediaCount;
    private int _drawingId = 1;
    private int _revisionId = 1;
    private int _shapeCount;
    private bool _usesEvenOdd;

    public WordMl2003Converter(MarkupDocument doc, OpenContext ctx, ILossSink loss, CancellationToken ct)
    {
        _doc = doc;
        _ctx = ctx;
        _loss = loss;
        _ct = ct;
    }

    /// <summary>One part being written: its buffer, its XML writer and the relationships its content needs.</summary>
    private sealed class Story
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

        // True when the last block written was a paragraph; a table cell must end with one.
        public bool EndsWithParagraph { get; set; }

        public ReadOnlyMemory<byte> Bytes => _buffer.WrittenMemory;

        public string AddRel(string type, string target, bool external = false)
        {
            string id = "rId" + (Rels.Count + 1).ToString(CultureInfo.InvariantCulture);
            Rels.Add(new Relationship(id, type, target, external ? RelationshipTargetMode.External : RelationshipTargetMode.Internal));
            return id;
        }

        public void Begin(string root)
        {
            W.WriteXmlDeclaration();
            W.WriteStartElement(root);
            W.WriteNamespaceDeclaration("w", W06);
            W.WriteNamespaceDeclaration("r", RNs);
            W.WriteNamespaceDeclaration("wp", WpNs);
            W.WriteNamespaceDeclaration("a", ANs);
            W.WriteNamespaceDeclaration("pic", PicNs);
            W.WriteNamespaceDeclaration("v", Ns2003.V);
            W.WriteNamespaceDeclaration("o", Ns2003.O);
            W.WriteNamespaceDeclaration("w10", Ns2003.W10);
        }

        public void End()
        {
            W.WriteEndElement();
            W.Complete();
        }
    }

    private readonly record struct Revision(bool Deleted, int Id, string? Author, string? Date);

    public OpcPackage Convert()
    {
        var package = OpcPackage.Create(_ctx.Limits);
        try
        {
            MarkupNode root = _doc.Root;
            if (root.IsNull || Ns2003.Of(root) != Ns2003.Word || root.LocalNameString != "wordDocument")
            {
                throw new OfficeFormatException(DefectKind.Structure, "wml2003.not-wordml", "The root element is not a WordprocessingML 2003 w:wordDocument.", DefectLocation.InPart("/", Math.Max(0, root.SourceStart)));
            }

            Scan(root, 0);
            FindFinalSection(root);

            Story styles = BuildStyles(root);
            Story? numbering = BuildNumbering(root);
            _main = new Story("/word/document.xml");
            _main.Begin("w:document");
            _main.W.WriteStartElement("w:body");
            MarkupNode body = Child(root, Ns2003.Word, "body");
            if (!body.IsNull)
            {
                EmitBlocks(_main, body, null);
            }

            if (!_finalSection.IsNull)
            {
                EmitSectPr(_main, _finalSection);
            }

            _main.W.WriteEndElement();
            _main.End();

            Story? settings = BuildSettings(root);
            AssemblePackage(package, root, styles, numbering, settings);
            return package;
        }
        catch
        {
            package.Dispose();
            throw;
        }
    }

    private void AssemblePackage(OpcPackage package, MarkupNode root, Story styles, Story? numbering, Story? settings)
    {
        var rootRels = new List<Relationship> { new("rId1", RelBase + "officeDocument", "word/document.xml") };
        var parts = new List<(Story Story, string ContentType)>();

        _main.Rels.Insert(0, new Relationship("rIdStyles", RelBase + "styles", "styles.xml"));
        parts.Add((styles, CtBase + "styles+xml"));
        if (numbering is not null)
        {
            _main.Rels.Add(new Relationship("rIdNumbering", RelBase + "numbering", "numbering.xml"));
            parts.Add((numbering, CtBase + "numbering+xml"));
        }

        if (settings is not null)
        {
            _main.Rels.Add(new Relationship("rIdSettings", RelBase + "settings", "settings.xml"));
            parts.Add((settings, CtBase + "settings+xml"));
        }

        if (_footnotes.Count > 0)
        {
            Story s = BuildNotes("w:footnotes", "w:footnote", "/word/footnotes.xml", _footnotes);
            _main.Rels.Add(new Relationship("rIdFootnotes", RelBase + "footnotes", "footnotes.xml"));
            parts.Add((s, CtBase + "footnotes+xml"));
        }

        if (_endnotes.Count > 0)
        {
            Story s = BuildNotes("w:endnotes", "w:endnote", "/word/endnotes.xml", _endnotes);
            _main.Rels.Add(new Relationship("rIdEndnotes", RelBase + "endnotes", "endnotes.xml"));
            parts.Add((s, CtBase + "endnotes+xml"));
        }

        if (_comments.Count > 0)
        {
            Story s = BuildComments();
            _main.Rels.Add(new Relationship("rIdComments", RelBase + "comments", "comments.xml"));
            parts.Add((s, CtBase + "comments+xml"));
        }

        foreach (Story extra in _extraParts)
        {
            parts.Add((extra, extra.PartName.Contains("/header", StringComparison.Ordinal) ? CtBase + "header+xml" : CtBase + "footer+xml"));
        }

        package.AddPart("/word/document.xml", CtBase + "document.main+xml", _main.Bytes);
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

        AddDocumentProperties(package, root, rootRels);
        package.SetRelationships(null, rootRels);
    }

    private readonly List<Story> _extraParts = [];
    private readonly List<(string Name, string ContentType, byte[] Data)> _mediaFiles = [];

    // ---- reading helpers -------------------------------------------------------------------------------------------------

    private static MarkupNode Child(MarkupNode parent, int ns, string local)
    {
        foreach (MarkupNode c in parent.ChildElements)
        {
            if (Ns2003.Of(c) == ns && c.LocalNameString == local)
            {
                return c;
            }
        }

        return default;
    }

    // Gets an attribute by local name whatever its prefix (2003 elements use w:, aml: and unprefixed attributes without clashes).
    private static string? Attr(MarkupNode n, string local)
    {
        byte[] name = Encoding.UTF8.GetBytes(local);
        foreach (MarkupAttributeRef a in n.Attributes)
        {
            if (!a.IsNamespaceDeclaration && a.LocalName.SequenceEqual(name))
            {
                return a.Value;
            }
        }

        return null;
    }

    private static bool IsOn(string? value) => value is null or "on" or "true" or "1" or "";

    private void Defect(DefectKind kind, string code, DefectSeverity severity, string message, MarkupNode at) =>
        _ctx.Defects.Report(new Defect(kind, code, severity, message, DefectLocation.InPart("/", Math.Max(0, at.IsNull ? 0 : at.SourceStart))));

    // One loss line per kind of element, so the report says which markup had no counterpart.
    private void Unmapped(string element) =>
        _loss.Report("loss.unmapped-markup." + element, "unmapped markup (" + element + ")", 1, "Elements with no counterpart in the document model were skipped.");

    private void Lost(string code, string feature, string message, long count = 1) => _loss.Report(code, feature, count, message);

    // Pre-pass: picture data, comment bodies and the places where comment ranges begin and end, wherever they sit in the file.
    private void Scan(MarkupNode node, int depth)
    {
        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxRecursionDepth), _ctx.Limits.MaxRecursionDepth, depth, DefectLocation.InPart("/", Math.Max(0, node.SourceStart)));
        foreach (MarkupNode c in node.ChildElements)
        {
            int ns = Ns2003.Of(c);
            if (ns == Ns2003.Word && c.LocalNameString == "binData")
            {
                string? name = Attr(c, "name");
                if (name is not null)
                {
                    _binData[name] = c;
                }

                continue;
            }

            if (ns == Ns2003.Annotation && c.LocalNameString == "annotation")
            {
                string? type = Attr(c, "type");
                string id = Attr(c, "id") ?? string.Empty;
                if (type == "Word.Comment")
                {
                    _commentContent[id] = c;
                }
                else if (type is "Word.Comment.Start" or "Word.Comment.End")
                {
                    _commentMarked.Add(id);
                }
            }

            Scan(c, depth + 1);
        }
    }

    private void FindFinalSection(MarkupNode root)
    {
        MarkupNode body = Child(root, Ns2003.Word, "body");
        if (!body.IsNull)
        {
            FindFinalSectionIn(body, 0);
        }
    }

    private void FindFinalSectionIn(MarkupNode parent, int depth)
    {
        if (depth > _ctx.Limits.MaxRecursionDepth)
        {
            return;
        }

        foreach (MarkupNode c in parent.ChildElements)
        {
            int ns = Ns2003.Of(c);
            if (ns == Ns2003.Word && c.LocalNameString == "sectPr")
            {
                _finalSection = c;
            }
            else if (ns == Ns2003.Aux)
            {
                FindFinalSectionIn(c, depth + 1);
            }
        }
    }

    // ---- names ---------------------------------------------------------------------------------------------------------

    private static readonly Dictionary<string, string> NameOverrides = new(StringComparer.Ordinal)
    {
        ["fareast"] = "eastAsia",
        ["h-ansi"] = "hAnsi",
        ["auto-space-de"] = "autoSpaceDE",
        ["auto-space-dn"] = "autoSpaceDN",
        ["vmerge"] = "vMerge",
        ["hmerge"] = "hMerge",
    };

    // 2003 names are lower-case words joined by hyphens (w:first-line, w:h-ansi); the 2006 names are camel case.
    internal static string Camel(string name)
    {
        if (NameOverrides.TryGetValue(name, out string? mapped))
        {
            return mapped;
        }

        if (!name.Contains('-', StringComparison.Ordinal))
        {
            return name;
        }

        var sb = new StringBuilder(name.Length);
        bool upper = false;
        foreach (char ch in name)
        {
            if (ch == '-')
            {
                upper = true;
            }
            else
            {
                sb.Append(upper ? char.ToUpperInvariant(ch) : ch);
                upper = false;
            }
        }

        return sb.ToString();
    }

    // Elements whose w:val is an enumeration with hyphenated 2003 spellings ("dash-small-gap", "next-page", "horz-stripe").
    private static readonly HashSet<string> HyphenatedValue = new(StringComparer.Ordinal)
    {
        "shd", "u", "top", "left", "bottom", "right", "between", "bar", "insideH", "insideV", "tl2br", "tr2bl", "bdr", "type", "textDirection",
        "highlight", "em", "effect", "vertAlign", "textAlignment", "lineRule", "numFmt",
    };

    // A value as 2006 files carry it: hyphenated enumerations in camel case, and whole numbers where the schema wants an integer (LibreOffice
    // writes twips as 4320.5401).
    // Returns null for an attribute that has to be left out: an empty value where the schema wants a number, colour or name.
    private string? MapValue(MarkupNode at, string element, string attribute, string value)
    {
        if (value.Length == 0 && !(attribute == "val" && element is "lvlText" or "t"))
        {
            Defect(DefectKind.Value, "wml2003.bad-value", DefectSeverity.Dropped, "The attribute " + element + "/" + attribute + " is empty; it was left out.", at);
            return null;
        }

        if (IsFraction(value))
        {
            Defect(DefectKind.Value, "wml2003.bad-value", DefectSeverity.Repaired, "The value '" + value + "' of " + element + "/" + attribute + " is not a whole number; it was rounded.", at);
            return Math.Round(double.Parse(value, CultureInfo.InvariantCulture), MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
        }

        if (value.Contains('-', StringComparison.Ordinal)
            && (attribute is "line-rule" or "h-rule" or "h-anchor" or "v-anchor" or "drop-cap" or "offset-from" or "z-order"
                || attribute is "val" or "type" or "orient" or "pattern" && HyphenatedValue.Contains(element)))
        {
            return Camel(value);
        }

        // The 2003 list tab stop is the numbering tab of 2006.
        if (attribute == "val" && element == "tab" && value == "list")
        {
            return "num";
        }

        return value;
    }

    private static bool IsFraction(string value)
    {
        int dot = value.IndexOf('.', StringComparison.Ordinal);
        if (dot < 1 || dot == value.Length - 1)
        {
            return false;
        }

        for (int i = value[0] == '-' ? 1 : 0; i < value.Length; i++)
        {
            if (i != dot && !char.IsAsciiDigit(value[i]))
            {
                return false;
            }
        }

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
    }

    private static string FirstLower(string value) => value.Length == 0 ? value : char.ToLowerInvariant(value[0]) + value[1..];
}
