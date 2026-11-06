using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Rtf;

namespace RecursiveAI.OneDocument.Documents.Rtf;

// WordprocessingML -> RTF 1.9.1. The document is read through its markup tree (the same one the hub sits on) and written as the
// word processor's own RTF dialect: \ansi\ansicpg1252 with a font table, colour table, style sheet and list table, \pard\plain at the
// start of each paragraph, one group per formatted run, and \uN with an ANSI fallback for every character outside Windows-1252.
internal sealed partial class RtfExporter
{
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string WStrict = "http://purl.oclc.org/ooxml/wordprocessingml/main";
    private const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string RStrict = "http://purl.oclc.org/ooxml/officeDocument/relationships";

    private readonly Document _doc;
    private readonly LossReport _loss;
    private readonly IReadOnlyList<byte[]> _preserved;
    private readonly OpenContext _ctx;
    private readonly CancellationToken _ct;
    private RtfWriter _w = new();
    private string _part;
    private int _headerDepth;

    private RtfExporter(Document doc, LossReport loss, IReadOnlyList<byte[]> preserved, CancellationToken ct)
    {
        _doc = doc;
        _loss = loss;
        _preserved = preserved;
        _ctx = doc.Context;
        _ct = ct;
        _part = doc.MainPartName;
    }

    /// <summary>Writes the document as RTF.</summary>
    internal static void Write(Document doc, Stream destination, LossReport loss, IReadOnlyList<byte[]> preserved, CancellationToken cancellationToken)
    {
        var e = new RtfExporter(doc, loss, preserved, cancellationToken);
        try
        {
            e.Run(destination);
        }
        catch (Exception ex) when (ex is not (OfficeException or OperationCanceledException or IOException))
        {
            throw new OfficeFormatException(DefectKind.Structure, "rtf.export-failed", "The document could not be written as RTF: " + ex.GetType().Name + ".", DefectLocation.InPart(doc.MainPartName), ex);
        }
    }

    // ---- helpers over the markup tree --------------------------------------------------------------------------------------------

    private static bool IsW(MarkupNode n, string local) => n.Kind == MarkupNodeKind.Element && n.LocalNameString == local && n.NamespaceUri is W or WStrict;

    private static string? A(MarkupNode n, string local) => n.GetAttribute(W, local) ?? n.GetAttribute(WStrict, local) ?? n.GetAttribute(local);

    private static string? RA(MarkupNode n, string local) => n.GetAttribute(R, local) ?? n.GetAttribute(RStrict, local);

    private static MarkupNode Child(MarkupNode n, string local)
    {
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

    private static int Int(string? s, int fallback = 0) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;

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

    // The part a relationship id of the part being written points at.
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

    // ---- tables of definitions shared by styles, lists and the body -----------------------------------------------------------------

    private sealed class FontRec
    {
        public string Name = string.Empty;
        public int Charset;
        public string Family = "fnil";
    }

    private readonly List<FontRec> _fonts = [];
    private readonly List<int> _colors = [];
    private readonly List<string> _authors = [];
    private readonly Dictionary<string, int> _styleNumbers = new(StringComparer.Ordinal);
    private readonly List<StyleRec> _styleList = [];
    private string _defaultFont = "Times New Roman";
    private int _defaultSize = 20;
    private string _majorFont = "Calibri Light";
    private string _minorFont = "Calibri";

    private sealed class StyleRec
    {
        public MarkupNode Node;
        public string Type = "paragraph";
        public string Id = string.Empty;
        public string Name = string.Empty;
        public int Number;
        public string? BasedOn;
        public string? Next;
        public bool IsDefault;
    }

    private int FontIndex(string name, bool symbol = false)
    {
        for (int i = 0; i < _fonts.Count; i++)
        {
            if (string.Equals(_fonts[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                if (symbol)
                {
                    _fonts[i].Charset = 2;
                }

                return i;
            }
        }

        _fonts.Add(new FontRec { Name = name, Charset = symbol ? 2 : 0, Family = FamilyOf(name) });
        return _fonts.Count - 1;
    }

    private static string FamilyOf(string name) => name.ToUpperInvariant() switch
    {
        "TIMES NEW ROMAN" or "CAMBRIA" or "GEORGIA" or "GARAMOND" or "BOOK ANTIQUA" or "PALATINO LINOTYPE" or "CENTURY" or "LIBERATION SERIF" => "froman",
        "ARIAL" or "CALIBRI" or "VERDANA" or "TAHOMA" or "SEGOE UI" or "HELVETICA" or "TREBUCHET MS" or "LIBERATION SANS" or "CALIBRI LIGHT" or "ARIAL NARROW" => "fswiss",
        "COURIER NEW" or "CONSOLAS" or "LUCIDA CONSOLE" or "LIBERATION MONO" or "COURIER" => "fmodern",
        "COMIC SANS MS" or "BRUSH SCRIPT MT" => "fscript",
        _ => "fnil",
    };

    private int ColorIndex(string hex)
    {
        if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
        {
            return 0;
        }

        int at = _colors.IndexOf(rgb);
        if (at < 0)
        {
            _colors.Add(rgb);
            at = _colors.Count - 1;
        }

        return at + 1; // index 0 is "auto"
    }

    private int AuthorIndex(string? author)
    {
        author = string.IsNullOrEmpty(author) ? "Unknown" : author;
        int at = _authors.IndexOf(author);
        if (at < 0)
        {
            _authors.Add(author);
            at = _authors.Count - 1;
        }

        return at;
    }

    // ---- the document ------------------------------------------------------------------------------------------------------------

    private MarkupNode _stylesRoot;
    private MarkupNode _numberingRoot;
    private MarkupNode _footnotesRoot;
    private MarkupNode _endnotesRoot;
    private MarkupNode _commentsRoot;
    private string? _footnotesPart;
    private string? _endnotesPart;
    private string? _commentsPart;
    private bool _facing;
    private readonly List<MarkupNode> _sections = [];
    private int _sectionIndex;

    private void Run(Stream destination)
    {
        MarkupNode doc = _doc.MainMarkup.Root;
        MarkupNode body = Child(doc, "body");
        _stylesRoot = LoadRelated("styles", out _);
        _numberingRoot = LoadRelated("numbering", out _);
        _footnotesRoot = LoadRelated("footnotes", out _footnotesPart);
        _endnotesRoot = LoadRelated("endnotes", out _endnotesPart);
        _commentsRoot = LoadRelated("comments", out _commentsPart);
        MarkupNode settings = LoadRelated("settings", out _);
        _facing = !settings.IsNull && !Child(settings, "evenAndOddHeaders").IsNull;
        ReadTheme();
        ReadDefaults();
        FontIndex(_defaultFont); // \deff0
        ReadStyles();
        if (!_numberingRoot.IsNull)
        {
            ReadNumbering();
        }

        // The body first, so that the tables it needs (fonts, colours, authors) are complete when the header is written.
        var bodyWriter = new RtfWriter();
        _w = bodyWriter;
        FindSections(body);
        _sectionIndex = 0;
        if (_sections.Count == 0)
        {
            _sections.Add(default);
        }

        SectionProps(_sections[0]);
        if (!body.IsNull)
        {
            Blocks(body, 0, false);
        }

        CloseFields();

        var styles = new RtfWriter();
        _w = styles;
        WriteStyleSheet();
        var lists = new RtfWriter();
        _w = lists;
        WriteLists();

        var head = new RtfWriter();
        head.StartGroup();
        head.Word("rtf", 1);
        head.Word("ansi");
        head.Word("ansicpg", 1252);
        head.Word("uc", 1);
        head.Word("deff", 0);
        int lcid = DefaultLang();
        head.Word("deflang", lcid);
        head.Word("deflangfe", lcid);
        head.Newline();
        head.StartGroup();
        head.Word("fonttbl");
        for (int i = 0; i < _fonts.Count; i++)
        {
            head.StartGroup();
            head.Word("f", i);
            head.Word(_fonts[i].Family);
            head.Word("fcharset", _fonts[i].Charset);
            head.Text(_fonts[i].Name);
            head.Text(";");
            head.EndGroup();
        }

        head.EndGroup();
        head.Newline();
        head.StartGroup();
        head.Word("colortbl");
        head.Text(";");
        foreach (int rgb in _colors)
        {
            head.Word("red", (rgb >> 16) & 255);
            head.Word("green", (rgb >> 8) & 255);
            head.Word("blue", rgb & 255);
            head.Text(";");
        }

        head.EndGroup();
        head.Newline();
        head.Raw(styles.ToArray());
        head.Newline();
        head.Raw(lists.ToArray());
        if (_authors.Count > 0)
        {
            head.StartDestination("revtbl");
            foreach (string a in _authors)
            {
                head.StartGroup();
                head.Text(a);
                head.Text(";");
                head.EndGroup();
            }

            head.EndGroup();
        }

        WriteInfo(head);
        foreach (byte[] kept in _preserved)
        {
            head.Raw(kept);
        }

        head.Newline();
        DocumentSetup(head);
        head.Raw(bodyWriter.ToArray());
        head.EndGroup();
        head.WriteTo(destination);
    }

    private int DefaultLang()
    {
        MarkupNode styles = _stylesRoot;
        if (!styles.IsNull)
        {
            MarkupNode lang = Child(Child(Child(Child(styles, "docDefaults"), "rPrDefault"), "rPr"), "lang");
            if (!lang.IsNull && A(lang, "val") is { } v)
            {
                return LcidOf(v, 1033);
            }
        }

        return 1033;
    }

    private static int LcidOf(string tag, int fallback)
    {
        if (tag.Length == 4 && int.TryParse(tag, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int hex))
        {
            return hex;
        }

        return tag.ToUpperInvariant() switch
        {
            "EN-US" => 1033, "EN-GB" => 2057, "EN-AU" => 3081, "EN-CA" => 4105, "DE-DE" => 1031, "FR-FR" => 1036, "ES-ES" => 3082,
            "IT-IT" => 1040, "NL-NL" => 1043, "PT-BR" => 1046, "PT-PT" => 2070, "RU-RU" => 1049, "JA-JP" => 1041, "KO-KR" => 1042,
            "ZH-CN" => 2052, "ZH-TW" => 1028, "AR-SA" => 1025, "HE-IL" => 1037, "SV-SE" => 1053, "NB-NO" => 1044, "DA-DK" => 1030,
            "FI-FI" => 1035, "PL-PL" => 1045, "CS-CZ" => 1029, "HU-HU" => 1038, "TR-TR" => 1055, "EL-GR" => 1032, "TH-TH" => 1054,
            "HI-IN" => 1081, "EN" => 1033, "DE" => 1031, "FR" => 1036, "ES" => 3082, "IT" => 1040, "RU" => 1049, "JA" => 1041,
            "ZH" => 2052, "AR" => 1025, "HE" => 1037, "NL" => 1043, "PT" => 1046, "PL" => 1045,
            _ => fallback,
        };
    }

    private void ReadTheme()
    {
        MarkupNode theme = LoadRelated("theme", out _);
        if (theme.IsNull)
        {
            return;
        }

        foreach (MarkupNode n in Descendants(theme))
        {
            if (n.LocalNameString is "majorFont" or "minorFont")
            {
                foreach (MarkupNode c in n.ChildElements)
                {
                    if (c.LocalNameString == "latin" && c.GetAttribute("typeface") is { Length: > 0 } face)
                    {
                        if (n.LocalNameString == "majorFont")
                        {
                            _majorFont = face;
                        }
                        else
                        {
                            _minorFont = face;
                        }
                    }
                }
            }
        }
    }

    private static IEnumerable<MarkupNode> Descendants(MarkupNode root)
    {
        var stack = new Stack<MarkupNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            MarkupNode n = stack.Pop();
            if (n.Kind == MarkupNodeKind.Element)
            {
                yield return n;
                var kids = new List<MarkupNode>(n.ChildElements);
                for (int i = kids.Count - 1; i >= 0; i--)
                {
                    stack.Push(kids[i]);
                }
            }
        }
    }

    // docDefaults: the font and size every run starts from (ECMA-376 Part 1 §17.7.5.1).
    private void ReadDefaults()
    {
        if (_stylesRoot.IsNull)
        {
            return;
        }

        MarkupNode rpr = Child(Child(Child(_stylesRoot, "docDefaults"), "rPrDefault"), "rPr");
        if (rpr.IsNull)
        {
            return;
        }

        string? font = FontNameOf(Child(rpr, "rFonts"));
        if (font is not null)
        {
            _defaultFont = font;
        }

        MarkupNode sz = Child(rpr, "sz");
        if (!sz.IsNull)
        {
            _defaultSize = Int(A(sz, "val"), 20);
        }
    }

    private string? FontNameOf(MarkupNode rFonts)
    {
        if (rFonts.IsNull)
        {
            return null;
        }

        string? name = A(rFonts, "ascii") ?? A(rFonts, "hAnsi") ?? A(rFonts, "eastAsia") ?? A(rFonts, "cs");
        if (name is not null)
        {
            return name;
        }

        string? theme = A(rFonts, "asciiTheme") ?? A(rFonts, "hAnsiTheme");
        return theme switch
        {
            null => null,
            _ when theme.StartsWith("major", StringComparison.OrdinalIgnoreCase) => _majorFont,
            _ => _minorFont,
        };
    }

    private void DocumentSetup(RtfWriter head)
    {
        MarkupNode sect = _sections.Count > 0 ? _sections[0] : default;
        MarkupNode size = sect.IsNull ? default : Child(sect, "pgSz");
        MarkupNode mar = sect.IsNull ? default : Child(sect, "pgMar");
        head.Word("paperw", size.IsNull ? 12240 : Int(A(size, "w"), 12240));
        head.Word("paperh", size.IsNull ? 15840 : Int(A(size, "h"), 15840));
        head.Word("margl", mar.IsNull ? 1800 : Int(A(mar, "left"), 1800));
        head.Word("margr", mar.IsNull ? 1800 : Int(A(mar, "right"), 1800));
        head.Word("margt", mar.IsNull ? 1440 : Int(A(mar, "top"), 1440));
        head.Word("margb", mar.IsNull ? 1440 : Int(A(mar, "bottom"), 1440));
        if (!size.IsNull && A(size, "orient") == "landscape")
        {
            head.Word("landscape");
        }

        if (_facing)
        {
            head.Word("facingp");
        }

        MarkupNode settings = LoadRelated("settings", out _);
        MarkupNode tab = settings.IsNull ? default : Child(settings, "defaultTabStop");
        head.Word("deftab", tab.IsNull ? 720 : Int(A(tab, "val"), 720));
        if (!settings.IsNull && !Child(settings, "trackRevisions").IsNull)
        {
            head.Word("revisions");
        }

        head.Newline();
    }

    // The core properties become the {\info} group (RTF 1.9.1 "Document Area").
    private void WriteInfo(RtfWriter head)
    {
        IPackagePart? core = _doc.Package.FindPart("/docProps/core.xml");
        if (core is null)
        {
            return;
        }

        MarkupDocument? md;
        try
        {
            md = _doc.Parts.Load(core.Name, required: false);
        }
        catch (OfficeException)
        {
            return;
        }

        if (md is null)
        {
            return;
        }

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (MarkupNode n in md.Root.ChildElements)
        {
            map[n.LocalNameString] = n.InnerText;
        }

        if (map.Count == 0)
        {
            return;
        }

        head.StartGroup();
        head.Word("info");
        void Item(string word, string key)
        {
            if (map.TryGetValue(key, out string? v) && v.Length > 0)
            {
                head.StartGroup();
                head.Word(word);
                head.Text(v);
                head.EndGroup();
            }
        }

        void Time(string word, string key)
        {
            if (map.TryGetValue(key, out string? v) && DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime d))
            {
                head.StartGroup();
                head.Word(word);
                head.Word("yr", d.Year);
                head.Word("mo", d.Month);
                head.Word("dy", d.Day);
                head.Word("hr", d.Hour);
                head.Word("min", d.Minute);
                head.EndGroup();
            }
        }

        Item("title", "title");
        Item("subject", "subject");
        Item("author", "creator");
        Item("operator", "lastModifiedBy");
        Item("keywords", "keywords");
        Item("doccomm", "description");
        Item("category", "category");
        Time("creatim", "created");
        Time("revtim", "modified");
        head.EndGroup();
    }
}
