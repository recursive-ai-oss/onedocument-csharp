using System.Linq;
using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Documents.Html;

// WordprocessingML -> HTML5. The document is read through its markup tree and written as a self-contained page: styles become CSS classes
// (a style that is based on another lists both classes, base first), lists become ul/ol, tables become tables with their spans, notes become
// linked end notes, pictures become data URIs or files the caller's sink stores. What HTML cannot hold is reported to the loss report.
internal sealed partial class HtmlExporter
{
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string WStrict = "http://purl.oclc.org/ooxml/wordprocessingml/main";
    private const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string RStrict = "http://purl.oclc.org/ooxml/officeDocument/relationships";

    private readonly Document _doc;
    private readonly LossReport _loss;
    private readonly HtmlExportOptions _options;
    private readonly CancellationToken _ct;
    private readonly StringBuilder _b = new();
    private string _part;
    private MarkupNode _styles;
    private MarkupNode _numbering;
    private readonly Dictionary<string, MarkupNode> _styleById = new(StringComparer.Ordinal);
    private readonly List<string> _usedStyles = [];
    private readonly HashSet<string> _usedSet = new(StringComparer.Ordinal);
    private string _defaultParagraphStyle = "Normal";
    private int _imageCount;
    private bool _usesNotes;

    private HtmlExporter(Document doc, LossReport loss, HtmlExportOptions options, CancellationToken ct)
    {
        _doc = doc;
        _loss = loss;
        _options = options;
        _ct = ct;
        _part = doc.MainPartName;
    }

    /// <summary>Writes the document as HTML (UTF-8, no byte-order mark).</summary>
    internal static void Write(Document doc, Stream destination, HtmlExportOptions? options, LossReport loss, CancellationToken cancellationToken)
    {
        var e = new HtmlExporter(doc, loss, options ?? new HtmlExportOptions(), cancellationToken);
        string html;
        try
        {
            html = e.Run();
        }
        catch (Exception ex) when (ex is not (OfficeException or OperationCanceledException or IOException))
        {
            throw new OfficeFormatException(DefectKind.Structure, "html.export-failed", "The document could not be written as HTML: " + ex.GetType().Name + ".", DefectLocation.InPart(doc.MainPartName), ex);
        }

        destination.Write(Encoding.UTF8.GetBytes(html));
    }

    // ---- helpers over the markup tree ------------------------------------------------------------------------------------------

    private static bool IsW(MarkupNode n, string local) => n.Kind == MarkupNodeKind.Element && n.LocalNameString == local && n.NamespaceUri is W or WStrict;

    private static string? A(MarkupNode n, string local) => n.GetAttribute(W, local) ?? n.GetAttribute(WStrict, local) ?? n.GetAttribute(local);

    private static string? RA(MarkupNode n, string local) => n.GetAttribute(R, local) ?? n.GetAttribute(RStrict, local);

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

    private static int Int(string? s, int fallback = 0) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;

    private static string Pt(double twips) => (twips / 20.0).ToString("0.##", CultureInfo.InvariantCulture) + "pt";

    private static string Esc(string text)
    {
        StringBuilder? sb = null;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            string? rep = c switch { '&' => "&amp;", '<' => "&lt;", '>' => "&gt;", _ => null };
            if (c < ' ' && c is not ('\t' or '\n' or '\r') || c is '\uFFFE' or '\uFFFF')
            {
                rep = string.Empty;
            }

            if (rep is not null)
            {
                sb ??= new StringBuilder(text, 0, i, text.Length + 16);
                sb.Append(rep);
            }
            else
            {
                sb?.Append(c);
            }
        }

        return sb?.ToString() ?? text;
    }

    private static string EscAttr(string text) => Esc(text).Replace("\"", "&quot;", StringComparison.Ordinal);

    private MarkupNode LoadRelated(string relName, out string? partName)
    {
        MarkupDocument? d = _doc.LoadRelatedByType(_doc.OfficeRelationship(relName), out partName);
        return d is null ? default : d.Root;
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

    private IPackagePart? Target(string relId)
    {
        Relationship? r = RelationshipOf(relId);
        return r is { TargetMode: RelationshipTargetMode.Internal } && PartUri.TryResolve(new PartUri(_part), r.Target, out PartUri uri) ? _doc.Package.FindPart(uri.Value) : null;
    }

    // ---- the page --------------------------------------------------------------------------------------------------------------

    private string Run()
    {
        MarkupNode root = _doc.MainMarkup.Root;
        MarkupNode body = Child(root, "body");
        _styles = LoadRelated("styles", out _);
        _numbering = LoadRelated("numbering", out _);
        IndexStyles();
        Blocks(body);
        CloseLists();
        Notes();
        CountSectionExtras(body);
        string content = _b.ToString();
        if (_options.Fragment)
        {
            return content;
        }

        var page = new StringBuilder("<!DOCTYPE html>\n<html");
        string? lang = DefaultLang();
        if (lang is not null)
        {
            page.Append(" lang=\"").Append(EscAttr(lang)).Append('"');
        }

        if (Child(Child(body, "sectPr"), "bidi") is { IsNull: false } bidi && On(bidi))
        {
            page.Append(" dir=\"rtl\"");
        }

        page.Append(">\n<head>\n<meta charset=\"utf-8\">\n<title>").Append(Esc(CoreProperty("title") ?? string.Empty)).Append("</title>\n");
        foreach ((string name, string key) in new[] { ("author", "creator"), ("description", "description"), ("keywords", "keywords") })
        {
            if (CoreProperty(key) is { Length: > 0 } v)
            {
                page.Append("<meta name=\"").Append(name).Append("\" content=\"").Append(EscAttr(v)).Append("\">\n");
            }
        }

        page.Append("<style>\n").Append(Css(body)).Append("</style>\n</head>\n<body>\n").Append(content).Append("\n</body>\n</html>\n");
        return page.ToString();
    }

    private string? DefaultLang()
    {
        MarkupNode dd = Child(_styles, "docDefaults");
        MarkupNode lang = Child(Child(Child(dd, "rPrDefault"), "rPr"), "lang");
        return lang.IsNull ? null : A(lang, "val");
    }

    private string? CoreProperty(string local)
    {
        foreach (Relationship r in _doc.Package.GetRelationships(null))
        {
            if (r.Type.EndsWith("/core-properties", StringComparison.Ordinal) && PartUri.TryResolve(null, r.Target, out PartUri uri))
            {
                MarkupDocument? d = _doc.Parts.Load(uri.Value, required: false);
                if (d is null)
                {
                    return null;
                }

                foreach (MarkupNode c in d.Root.ChildElements)
                {
                    if (c.LocalNameString == local)
                    {
                        string t = c.InnerText.Trim();
                        return t.Length == 0 ? null : t;
                    }
                }
            }
        }

        return null;
    }

    private void CountSectionExtras(MarkupNode body)
    {
        long headers = 0;
        foreach (MarkupNode sp in Sections(body))
        {
            foreach (MarkupNode c in sp.ChildElements)
            {
                if (IsW(c, "headerReference") || IsW(c, "footerReference"))
                {
                    headers++;
                }
            }
        }

        if (headers > 0)
        {
            _loss.Report("loss.html-headers-footers", "headers and footers", headers, "Headers and footers are not written; HTML has no pages to repeat them on.");
        }

        MarkupNode comments = LoadRelated("comments", out _);
        if (!comments.IsNull)
        {
            long n = comments.ChildElements.Count(static c => c.LocalNameString == "comment");
            if (n > 0)
            {
                _loss.Report("loss.html-comments", "comments", n, "Comments are not written.");
            }
        }
    }

    private static IEnumerable<MarkupNode> Sections(MarkupNode body)
    {
        foreach (MarkupNode c in body.ChildElements)
        {
            if (IsW(c, "sectPr"))
            {
                yield return c;
            }
            else if (IsW(c, "p") && Child(Child(c, "pPr"), "sectPr") is { IsNull: false } sp)
            {
                yield return sp;
            }
        }
    }

    // ---- blocks ----------------------------------------------------------------------------------------------------------------

    private void Blocks(MarkupNode container)
    {
        if (container.IsNull)
        {
            return;
        }

        foreach (MarkupNode c in container.ChildElements)
        {
            _ct.ThrowIfCancellationRequested();
            if (c.NamespaceUri is not (W or WStrict))
            {
                if (c.LocalNameString == "AlternateContent")
                {
                    foreach (MarkupNode choice in c.ChildElements)
                    {
                        if (choice.LocalNameString is "Choice" or "Fallback")
                        {
                            Blocks(choice);
                            break;
                        }
                    }
                }

                continue;
            }

            switch (c.LocalNameString)
            {
                case "p":
                    Paragraph(c);
                    break;
                case "tbl":
                    CloseLists();
                    Table(c);
                    break;
                case "sdt":
                    Blocks(Child(c, "sdtContent"));
                    break;
                case "ins" or "customXml" or "moveTo" or "smartTag":
                    Blocks(c);
                    break;
                case "del" or "moveFrom":
                    _loss.Report("loss.html-revisions", "tracked deletions", 1, "Deleted text is not written; inserted text is written as ordinary text.");
                    break;
                case "bookmarkStart":
                    _b.Append("<span id=\"").Append(EscAttr(AnchorId(A(c, "name") ?? string.Empty))).Append("\"></span>");
                    break;
            }
        }
    }

    private static string AnchorId(string name) => name.Replace(' ', '_');

    // ---- paragraphs ------------------------------------------------------------------------------------------------------------

    private readonly List<MarkupNode> _floating = [];
    private string? _paraTag;
    private string _paraOpen = string.Empty;

    private static string OpenTag(string tag, string classes, bool rtl, List<string> decls)
    {
        var sb = new StringBuilder("<").Append(tag);
        if (classes.Length > 0)
        {
            sb.Append(" class=\"").Append(classes).Append('"');
        }

        if (rtl)
        {
            sb.Append(" dir=\"rtl\"");
        }

        if (decls.Count > 0)
        {
            sb.Append(" style=\"").Append(EscAttr(string.Join(';', decls))).Append('"');
        }

        return sb.Append('>').ToString();
    }

    private void Paragraph(MarkupNode p)
    {
        MarkupNode ppr = Child(p, "pPr");
        string styleId = A(Child(ppr, "pStyle"), "val") ?? _defaultParagraphStyle;
        (int numId, int ilvl) = NumberingOf(ppr, styleId);
        int heading = HeadingLevel(styleId);
        bool isList = numId > 0;
        string tag = isList ? "li" : heading > 0 ? "h" + Math.Min(heading, 6).ToString(CultureInfo.InvariantCulture) : "p";
        if (isList)
        {
            ListTransition(numId, ilvl);
        }
        else
        {
            CloseLists();
        }

        string classes = ClassList(styleId);
        var decls = new List<string>();
        bool rtl = !Child(ppr, "bidi").IsNull && On(Child(ppr, "bidi"));
        if (isList)
        {
            decls.Add("margin-left:0");
            decls.Add("text-indent:0");
        }

        ParagraphDecls(ppr, decls, isList, rtl);
        _b.Append('<').Append(tag);
        if (classes.Length > 0)
        {
            _b.Append(" class=\"").Append(classes).Append('"');
        }

        if (rtl)
        {
            _b.Append(" dir=\"rtl\"");
        }

        if (decls.Count > 0)
        {
            _b.Append(" style=\"").Append(EscAttr(string.Join(';', decls))).Append('"');
        }

        _b.Append('>');
        _paraTag = isList ? null : tag;
        _paraOpen = OpenTag(tag, classes, rtl, decls);
        int start = _b.Length;
        Inlines(p);
        CloseFieldAnchors();
        if (_b.Length == start)
        {
            _b.Append("<br>");
        }

        if (!isList)
        {
            _b.Append("</").Append(tag).Append('>');
        }

        if (_floating.Count > 0)
        {
            // Text boxes inside the paragraph are written after it, as their own blocks.
            List<MarkupNode> boxes = [.. _floating];
            _floating.Clear();
            CloseLists();
            foreach (MarkupNode box in boxes)
            {
                _b.Append("<div class=\"textbox\">");
                Blocks(box);
                CloseLists();
                _b.Append("</div>");
            }
        }

        MarkupNode sect = Child(ppr, "sectPr");
        if (!sect.IsNull && A(Child(sect, "type"), "val") is not "continuous")
        {
            CloseLists();
            _b.Append("<div style=\"break-after:page\"></div>");
        }
    }

    private static void ParagraphDecls(MarkupNode ppr, List<string> d, bool isList, bool rtl)
    {
        if (ppr.IsNull)
        {
            return;
        }

        string? jc = A(Child(ppr, "jc"), "val");
        if (jc is not null)
        {
            string? ta = jc switch
            {
                "center" => "center",
                "both" or "distribute" or "thaiDistribute" or "mediumKashida" or "highKashida" or "lowKashida" => "justify",
                "right" or "end" => rtl ? null : "right",
                "left" or "start" => null,
                _ => null,
            };
            if (jc is "right" or "end" && rtl)
            {
                ta = "left";
            }

            if (ta is not null)
            {
                d.Add("text-align:" + ta);
            }
        }

        MarkupNode ind = Child(ppr, "ind");
        if (!ind.IsNull && !isList)
        {
            int left = Int(A(ind, "left") ?? A(ind, "start"));
            int right = Int(A(ind, "right") ?? A(ind, "end"));
            int first = Int(A(ind, "firstLine"));
            int hanging = Int(A(ind, "hanging"));
            if (left != 0)
            {
                d.Add("margin-" + (rtl ? "right:" : "left:") + Pt(left));
            }

            if (right != 0)
            {
                d.Add("margin-" + (rtl ? "left:" : "right:") + Pt(right));
            }

            if (first != 0)
            {
                d.Add("text-indent:" + Pt(first));
            }
            else if (hanging != 0)
            {
                d.Add("text-indent:" + Pt(-hanging));
            }
        }

        MarkupNode sp = Child(ppr, "spacing");
        if (!sp.IsNull)
        {
            if (A(sp, "before") is { } bf)
            {
                d.Add("margin-top:" + Pt(Int(bf)));
            }

            if (A(sp, "after") is { } af)
            {
                d.Add("margin-bottom:" + Pt(Int(af)));
            }

            AddLine(sp, d);
        }

        if (!Child(ppr, "pageBreakBefore").IsNull && On(Child(ppr, "pageBreakBefore")))
        {
            d.Add("break-before:page");
        }

        if (!Child(ppr, "keepNext").IsNull && On(Child(ppr, "keepNext")))
        {
            d.Add("break-after:avoid");
        }

        string? fill = A(Child(ppr, "shd"), "fill");
        if (IsColor(fill))
        {
            d.Add("background-color:#" + fill);
        }

        Borders(Child(ppr, "pBdr"), d, "border-");
    }

    private static bool IsColor(string? c) => c is { Length: 6 } && c.All(char.IsAsciiHexDigit);

    private static void AddLine(MarkupNode sp, List<string> d)
    {
        string? line = A(sp, "line");
        if (line is null)
        {
            return;
        }

        int v = Int(line);
        if (v <= 0)
        {
            return;
        }

        string rule = A(sp, "lineRule") ?? "auto";
        d.Add(rule == "auto" ? "line-height:" + (v / 240.0).ToString("0.###", CultureInfo.InvariantCulture) : "line-height:" + Pt(v));
    }

    // ECMA-376 Part 1 §17.3.1.24 pBdr / §17.4.38 tcBorders: val is the line style, sz eighths of a point, color RGB or auto.
    private static string? BorderCss(MarkupNode b)
    {
        if (b.IsNull)
        {
            return null;
        }

        string val = A(b, "val") ?? "single";
        if (val is "nil" or "none")
        {
            return "none";
        }

        double sz = Math.Max(2, Int(A(b, "sz"), 4)) / 8.0;
        string style = val switch
        {
            "dotted" => "dotted",
            "dashed" or "dashSmallGap" or "dotDash" or "dotDotDash" => "dashed",
            "double" or "triple" or "thinThickSmallGap" or "thickThinSmallGap" => "double",
            "threeDEmboss" => "ridge",
            "threeDEngrave" => "groove",
            "inset" => "inset",
            "outset" => "outset",
            _ => "solid",
        };
        string color = A(b, "color") is { } c && IsColor(c) ? "#" + c : "#000";
        return sz.ToString("0.##", CultureInfo.InvariantCulture) + "pt " + style + " " + color;
    }

    private static void Borders(MarkupNode bdr, List<string> d, string prefix)
    {
        if (bdr.IsNull)
        {
            return;
        }

        foreach ((string side, string css) in new[] { ("top", "top"), ("left", "left"), ("start", "left"), ("bottom", "bottom"), ("right", "right"), ("end", "right") })
        {
            if (BorderCss(Child(bdr, side)) is { } v)
            {
                d.Add(prefix + css + ":" + v);
            }
        }
    }

    // ---- inline content --------------------------------------------------------------------------------------------------------

    private sealed class FieldState
    {
        public readonly StringBuilder Instruction = new();
        public bool InResult;
        public bool AnchorOpen;
    }

    private readonly Stack<FieldState> _fields = new();

    private bool InInstruction => _fields.Count > 0 && !_fields.Peek().InResult;

    private void CloseFieldAnchors()
    {
        foreach (FieldState f in _fields)
        {
            if (f.AnchorOpen)
            {
                _b.Append("</a>");
                f.AnchorOpen = false;
            }
        }
    }

    private void Inlines(MarkupNode container)
    {
        if (container.IsNull)
        {
            return;
        }

        foreach (MarkupNode c in container.ChildElements)
        {
            if (c.NamespaceUri is not (W or WStrict))
            {
                switch (c.LocalNameString)
                {
                    case "oMath" or "oMathPara":
                        _loss.Report("loss.html-equations", "equations", 1, "Equations are written as their plain text.");
                        _b.Append("<span class=\"math\">").Append(Esc(Equations.MathLinearizer.Linearize(Equations.OmmlReader.Read(c, out _)))).Append("</span>");
                        break;
                    case "AlternateContent":
                        foreach (MarkupNode choice in c.ChildElements)
                        {
                            if (choice.LocalNameString is "Choice" or "Fallback")
                            {
                                Inlines(choice);
                                break;
                            }
                        }

                        break;
                }

                continue;
            }

            switch (c.LocalNameString)
            {
                case "r":
                    RunElement(c);
                    break;
                case "hyperlink":
                    Hyperlink(c);
                    break;
                case "bookmarkStart":
                    if (A(c, "name") is { } n && n != "_GoBack")
                    {
                        _b.Append("<span id=\"").Append(EscAttr(AnchorId(n))).Append("\"></span>");
                    }

                    break;
                case "fldSimple":
                    {
                        string instr = A(c, "instr") ?? string.Empty;
                        string? href = HyperlinkField(instr);
                        if (href is not null && SafeHref(href) is { } safe)
                        {
                            _b.Append("<a href=\"").Append(EscAttr(safe)).Append("\">");
                            Inlines(c);
                            _b.Append("</a>");
                        }
                        else
                        {
                            Inlines(c);
                        }

                        break;
                    }

                case "ins" or "smartTag" or "customXml" or "moveTo" or "dir" or "bdo":
                    Inlines(c);
                    break;
                case "sdt":
                    Inlines(Child(c, "sdtContent"));
                    break;
                case "del" or "moveFrom":
                    _loss.Report("loss.html-revisions", "tracked deletions", 1, "Deleted text is not written; inserted text is written as ordinary text.");
                    break;
            }
        }
    }

    private void Hyperlink(MarkupNode h)
    {
        string? href = null;
        if (A(h, "anchor") is { } anchor)
        {
            href = "#" + AnchorId(anchor);
        }
        else if (RA(h, "id") is { } rid && RelationshipOf(rid) is { } rel && rel.TargetMode == RelationshipTargetMode.External)
        {
            href = SafeHref(rel.Target);
        }

        if (href is null)
        {
            Inlines(h);
            return;
        }

        _b.Append("<a href=\"").Append(EscAttr(href)).Append("\">");
        Inlines(h);
        _b.Append("</a>");
    }

    // Links out of a document keep the same allow-list as links into one: a script-capable target is not written.
    private string? SafeHref(string target)
    {
        string t = target.Trim();
        if (t.Length == 0)
        {
            return null;
        }

        var scheme = new StringBuilder();
        foreach (char c in t)
        {
            if (c <= ' ')
            {
                continue;
            }

            if (c is ':' or '/' or '?' or '#')
            {
                if (c == ':')
                {
                    string s = scheme.ToString().ToLowerInvariant();
                    if (s is not ("http" or "https" or "mailto" or "tel" or "ftp" or "ftps"))
                    {
                        _loss.Report("loss.html-unsafe-link", "unsafe links", 1, "A link whose scheme is not http, https, mailto, tel or ftp is written as plain text.");
                        return null;
                    }
                }

                break;
            }

            scheme.Append(c);
        }

        return t;
    }

    private static string? HyperlinkField(string instr)
    {
        string s = instr.Trim();
        if (!s.StartsWith("HYPERLINK", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string rest = s[9..].Trim();
        string? url = null;
        string? anchor = null;
        for (int i = 0; i < rest.Length;)
        {
            if (rest[i] == '"')
            {
                int end = rest.IndexOf('"', i + 1);
                if (end < 0)
                {
                    end = rest.Length;
                }

                string q = rest[(i + 1)..end];
                if (anchor is null && i >= 3 && rest[..i].TrimEnd().EndsWith("\\l", StringComparison.OrdinalIgnoreCase))
                {
                    anchor = q;
                }
                else
                {
                    url ??= q;
                }

                i = end + 1;
            }
            else
            {
                i++;
            }
        }

        return anchor is not null ? (url ?? string.Empty) + "#" + AnchorId(anchor) : url;
    }

    private void RunElement(MarkupNode r)
    {
        // Field characters and instruction text drive the field state; the result runs are written as ordinary runs.
        MarkupNode rpr = Child(r, "rPr");
        var open = new StringBuilder();
        var close = new StringBuilder();
        bool opened = false;

        void Open()
        {
            if (opened)
            {
                return;
            }

            opened = true;
            RunOpen(rpr, open, close);
            _b.Append(open);
        }

        foreach (MarkupNode c in r.ChildElements)
        {
            if (c.NamespaceUri is not (W or WStrict))
            {
                if (c.LocalNameString == "AlternateContent")
                {
                    foreach (MarkupNode choice in c.ChildElements)
                    {
                        if (choice.LocalNameString is "Choice" or "Fallback")
                        {
                            foreach (MarkupNode inner in choice.ChildElements)
                            {
                                if (IsW(inner, "drawing") || IsW(inner, "pict"))
                                {
                                    Drawing(inner);
                                }
                            }

                            break;
                        }
                    }
                }

                continue;
            }

            switch (c.LocalNameString)
            {
                case "fldChar":
                    {
                        string type = A(c, "fldCharType") ?? string.Empty;
                        if (type == "begin")
                        {
                            _fields.Push(new FieldState());
                        }
                        else if (type == "separate" && _fields.Count > 0)
                        {
                            FieldState f = _fields.Peek();
                            f.InResult = true;
                            string? href = HyperlinkField(f.Instruction.ToString());
                            if (href is not null && SafeHref(href) is { } safe)
                            {
                                _b.Append("<a href=\"").Append(EscAttr(safe)).Append("\">");
                                f.AnchorOpen = true;
                            }
                        }
                        else if (type == "end" && _fields.Count > 0)
                        {
                            FieldState f = _fields.Pop();
                            if (f.AnchorOpen)
                            {
                                _b.Append("</a>");
                            }
                        }

                        break;
                    }

                case "instrText":
                    if (_fields.Count > 0 && !_fields.Peek().InResult)
                    {
                        _fields.Peek().Instruction.Append(c.InnerText);
                    }

                    break;
                case "t":
                    if (!InInstruction)
                    {
                        string text = c.InnerText;
                        if (text.Length > 0)
                        {
                            Open();
                            _b.Append(Esc(text));
                        }
                    }

                    break;
                case "tab" or "ptab":
                    if (!InInstruction)
                    {
                        Open();
                        _b.Append("<span style=\"white-space:pre\">\t</span>");
                    }

                    break;
                case "br" or "cr":
                    if (!InInstruction)
                    {
                        if (c.LocalNameString == "br" && A(c, "type") == "page")
                        {
                            // A page break ends the block it is in; the next block starts a page (CSS Fragmentation: break-after).
                            CloseFieldAnchors();
                            _b.Append(close);
                            if (_paraTag is not null)
                            {
                                _b.Append("</").Append(_paraTag).Append('>').Append("<div style=\"break-after:page\"></div>").Append(_paraOpen);
                            }
                            else
                            {
                                _b.Append("<br>");
                            }

                            opened = false;
                            open.Clear();
                            close.Clear();
                        }
                        else
                        {
                            Open();
                            _b.Append("<br>");
                        }
                    }

                    break;
                case "noBreakHyphen":
                    Open();
                    _b.Append('\u2011');
                    break;
                case "softHyphen":
                    Open();
                    _b.Append('\u00AD');
                    break;
                case "sym":
                    if (A(c, "char") is { } hex && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int cp) && cp > 0 && cp < 0xFFFE)
                    {
                        Open();
                        _b.Append(Esc(((char)cp).ToString()));
                    }

                    break;
                case "drawing" or "pict" or "object":
                    if (!InInstruction)
                    {
                        Drawing(c);
                    }

                    break;
                case "footnoteReference" or "endnoteReference":
                    NoteReference(c);
                    break;
                case "ruby" when !InInstruction:
                    // ECMA-376 Part 1 §17.3.3.25: the base text with its phonetic guide, which HTML has as ruby and rt.
                    Open();
                    _b.Append("<ruby>");
                    Inlines(Child(c, "rubyBase"));
                    _b.Append("<rt>");
                    Inlines(Child(c, "rt"));
                    _b.Append("</rt></ruby>");
                    break;
            }
        }

        if (opened)
        {
            _b.Append(close);
        }
    }

    // Character formatting as tags and a span: bold, italic, underline, strike-through and super/subscript are semantic tags (so a reader
    // of the HTML sees them without the style sheet); colour, size, font, shading and the rest go in a span's style.
    private void RunOpen(MarkupNode rpr, StringBuilder open, StringBuilder close)
    {
        if (rpr.IsNull)
        {
            return;
        }

        var d = new List<string>();
        string? cls = null;
        if (A(Child(rpr, "rStyle"), "val") is { } rs)
        {
            cls = ClassList(rs);
        }

        string? lang = A(Child(rpr, "lang"), "val");
        MarkupNode rtlNode = Child(rpr, "rtl");
        var tags = new List<string>();
        MarkupNode b = Child(rpr, "b");
        if (!b.IsNull)
        {
            if (On(b))
            {
                tags.Add("strong");
            }
            else
            {
                d.Add("font-weight:normal");
            }
        }

        MarkupNode i = Child(rpr, "i");
        if (!i.IsNull)
        {
            if (On(i))
            {
                tags.Add("em");
            }
            else
            {
                d.Add("font-style:normal");
            }
        }

        MarkupNode u = Child(rpr, "u");
        if (!u.IsNull && A(u, "val") is not ("none" or "0"))
        {
            tags.Add("u");
        }

        MarkupNode strike = Child(rpr, "strike");
        MarkupNode dstrike = Child(rpr, "dstrike");
        if (!strike.IsNull && On(strike) || !dstrike.IsNull && On(dstrike))
        {
            tags.Add("s");
        }

        string? va = A(Child(rpr, "vertAlign"), "val");
        if (va == "superscript")
        {
            tags.Add("sup");
        }
        else if (va == "subscript")
        {
            tags.Add("sub");
        }

        RunDecls(rpr, d, includeToggles: false);
        bool span = cls is { Length: > 0 } || d.Count > 0 || lang is not null && lang != DefaultLang() || !rtlNode.IsNull && On(rtlNode);
        if (span)
        {
            open.Append("<span");
            if (cls is { Length: > 0 })
            {
                open.Append(" class=\"").Append(cls).Append('"');
            }

            if (lang is not null && lang != DefaultLang())
            {
                open.Append(" lang=\"").Append(EscAttr(lang)).Append('"');
            }

            if (!rtlNode.IsNull && On(rtlNode))
            {
                open.Append(" dir=\"rtl\"");
            }

            if (d.Count > 0)
            {
                open.Append(" style=\"").Append(EscAttr(string.Join(';', d))).Append('"');
            }

            open.Append('>');
        }

        foreach (string t in tags)
        {
            open.Append('<').Append(t).Append('>');
        }

        for (int k = tags.Count - 1; k >= 0; k--)
        {
            close.Append("</").Append(tags[k]).Append('>');
        }

        if (span)
        {
            close.Append("</span>");
        }
    }

    // Run properties as CSS declarations (ECMA-376 Part 1 §17.3.2).
    private static void RunDecls(MarkupNode rpr, List<string> d, bool includeToggles)
    {
        if (rpr.IsNull)
        {
            return;
        }

        if (includeToggles)
        {
            MarkupNode b = Child(rpr, "b");
            if (!b.IsNull)
            {
                d.Add(On(b) ? "font-weight:bold" : "font-weight:normal");
            }

            MarkupNode i = Child(rpr, "i");
            if (!i.IsNull)
            {
                d.Add(On(i) ? "font-style:italic" : "font-style:normal");
            }

            var deco = new List<string>();
            MarkupNode u = Child(rpr, "u");
            if (!u.IsNull && A(u, "val") is not ("none" or "0"))
            {
                deco.Add("underline");
            }

            if (!Child(rpr, "strike").IsNull && On(Child(rpr, "strike")))
            {
                deco.Add("line-through");
            }

            if (deco.Count > 0)
            {
                d.Add("text-decoration:" + string.Join(' ', deco));
            }

            string? va = A(Child(rpr, "vertAlign"), "val");
            if (va == "superscript")
            {
                d.Add("vertical-align:super;font-size:smaller");
            }
            else if (va == "subscript")
            {
                d.Add("vertical-align:sub;font-size:smaller");
            }
        }

        MarkupNode fonts = Child(rpr, "rFonts");
        if (!fonts.IsNull && (A(fonts, "ascii") ?? A(fonts, "hAnsi")) is { Length: > 0 } fam)
        {
            d.Add("font-family:\"" + fam.Replace("\"", string.Empty, StringComparison.Ordinal).Replace(";", string.Empty, StringComparison.Ordinal) + "\"");
        }

        if (A(Child(rpr, "sz"), "val") is { } sz && Int(sz) > 0)
        {
            d.Add("font-size:" + (Int(sz) / 2.0).ToString("0.##", CultureInfo.InvariantCulture) + "pt");
        }

        string? color = A(Child(rpr, "color"), "val");
        if (IsColor(color))
        {
            d.Add("color:#" + color);
        }

        MarkupNode hl = Child(rpr, "highlight");
        if (!hl.IsNull && HighlightColor(A(hl, "val")) is { } hc)
        {
            d.Add("background-color:" + hc);
        }
        else if (IsColor(A(Child(rpr, "shd"), "fill")))
        {
            d.Add("background-color:#" + A(Child(rpr, "shd"), "fill"));
        }

        if (!Child(rpr, "caps").IsNull && On(Child(rpr, "caps")))
        {
            d.Add("text-transform:uppercase");
        }

        if (!Child(rpr, "smallCaps").IsNull && On(Child(rpr, "smallCaps")))
        {
            d.Add("font-variant:small-caps");
        }

        if (!Child(rpr, "vanish").IsNull && On(Child(rpr, "vanish")))
        {
            d.Add("display:none");
        }
    }

    private static string? HighlightColor(string? v) => v switch
    {
        "yellow" => "#FFFF00",
        "green" => "#00FF00",
        "cyan" => "#00FFFF",
        "magenta" => "#FF00FF",
        "blue" => "#0000FF",
        "red" => "#FF0000",
        "darkBlue" => "#000080",
        "darkCyan" => "#008080",
        "darkGreen" => "#008000",
        "darkMagenta" => "#800080",
        "darkRed" => "#800000",
        "darkYellow" => "#808000",
        "darkGray" => "#808080",
        "lightGray" => "#C0C0C0",
        "black" => "#000000",
        "white" => "#FFFFFF",
        _ => null,
    };
}
