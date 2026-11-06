using System.Linq;
using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneHtml;
using RecursiveAI.OneHtml.Css;
using OneHtmlDocument = RecursiveAI.OneHtml.Document;
using OneHtmlNode = RecursiveAI.OneHtml.Node;

namespace RecursiveAI.OneDocument.Documents.Html;

// Reads a page with OneHtml's parser and cascade into the importer's model. The cascade supplies computed styles (user-agent sheet, <style>,
// style attributes); the reader maps the ones a word-processing document can hold and reports the rest as loss. Nothing here can run code
// or open a URL: scripts and handlers are never read, and images come only from data: URIs or the caller's resolver.
internal sealed partial class HtmlReader
{
    private const int PageWidthTwips = 9360; // US Letter, one-inch margins: the width a browser window's body maps to.

    private readonly HtmlImportOptions _options;
    private readonly OpenContext _context;
    private readonly LossReport _loss;
    private readonly CancellationToken _ct;
    private readonly HtmlContent _out = new();
    private OneHtmlDocument _doc = null!;
    private DocumentStyles _styles = null!;
    private readonly HashSet<string> _anchorTargets = new(StringComparer.Ordinal);
    private readonly HashSet<string> _anchorsPlaced = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _bookmarkNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> _bookmarkUsed = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _pendingBookmarks = [];
    private readonly Dictionary<int, (HRunProps Core, string WhiteSpace, string Transform)> _textStyles = [];
    private readonly int _maxDepth;
    private readonly HashSet<string> _pendingBases = new(StringComparer.Ordinal);

    private List<HBlock> _target = null!;
    private HPara? _cur;
    private OneHtmlNode _source;
    private bool _atStart = true;
    private int _gap;
    private int _indentLeft;
    private int _indentRight;
    private int _avail = PageWidthTwips;
    private bool _pageBreakNext;
    private int _depth;
    private int _nextListId = 1;
    private readonly Stack<ListScope> _lists = new();
    private HList? _liList;
    private bool _liUsed;

    private sealed class ListScope(HList list)
    {
        public HList List { get; } = list;
    }

    private readonly record struct Format(bool Underline, bool Strike, string? Shade, string? Link, string? Lang);

    public HtmlReader(HtmlImportOptions options, OpenContext context, LossReport loss, CancellationToken cancellationToken)
    {
        _options = options;
        _context = context;
        _loss = loss;
        _ct = cancellationToken;
        _maxDepth = Math.Clamp(context.Limits.MaxXmlDepth, 16, 512);
    }

    public HtmlContent Read(ReadOnlyMemory<byte> html)
    {
        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxPartSize), _context.Limits.MaxPartSize, html.Length, DefectLocation.InRecord("html", 0));
        try
        {
            _doc = OneHtmlDocument.Parse(html, new ParseOptions { Limits = new ParseLimits { MaxInputLength = (int)Math.Min(int.MaxValue, _context.Limits.MaxPartSize), MaxOpenElements = _context.Limits.MaxXmlDepth, MaxAttributesPerTag = _context.Limits.MaxXmlAttributeCount } }, _ct);
            _styles = DocumentStyles.Create(_doc, null, null, _ct);
        }
        catch (HtmlLimitExceededException ex)
        {
            // The parser's limits are set from ours, so each one maps to the limit that set it.
            string name = ex.LimitName switch
            {
                "MaxOpenElements" => nameof(OfficeLimits.MaxXmlDepth),
                "MaxAttributesPerTag" or "MaxTotalAttributes" => nameof(OfficeLimits.MaxXmlAttributeCount),
                "MaxInputLength" => nameof(OfficeLimits.MaxPartSize),
                _ => nameof(OfficeLimits.MaxRecordCount),
            };
            throw new OfficeLimitException(name, ex.Limit, ex.Actual, DefectLocation.InRecord("html", 0), ex);
        }

        OneHtmlNode root = _doc.Root;
        OneHtmlNode htmlEl = root.QuerySelector("html");
        OneHtmlNode head = root.QuerySelector("head");
        OneHtmlNode body = root.QuerySelector("body");
        string? lang = htmlEl.IsNull ? null : Attr(htmlEl, "lang") ?? Attr(htmlEl, "xml:lang");
        _out.Lang = string.IsNullOrWhiteSpace(lang) ? _options.DefaultLanguage : lang.Trim();
        if (!htmlEl.IsNull && IsRtl(htmlEl))
        {
            _out.Rtl = true;
        }

        if (!head.IsNull)
        {
            ReadHead(head);
        }

        foreach (OneHtmlNode a in root.QuerySelectorAll("a[href]"))
        {
            string? h = a.GetAttribute("href")?.Trim();
            if (h is { Length: > 1 } && h[0] == '#')
            {
                _anchorTargets.Add(Decode(h[1..]));
            }
        }

        if (body.IsNull)
        {
            return _out;
        }

        ComputedStyle bodyStyle = _styles.GetStyle(body);
        HRunProps def = RunCore(bodyStyle);
        _out.DefaultRun = def;
        if (bodyStyle.IsRightToLeft)
        {
            _out.Rtl = true;
        }

        _target = _out.Blocks;
        _source = body;
        Format f = new(false, false, null, null, NormalLang(body, null));
        PlaceAnchor(body);
        Walk(body, f);
        FlushPara();
        FlushPendingBookmarks();
        if (_out.Blocks.Count == 0)
        {
            _out.Blocks.Add(new HPara());
        }

        // A base style that no paragraph used by itself still has to exist for the styles that name it.
        foreach (string id in _pendingBases)
        {
            if (!_out.Styles.ContainsKey(id))
            {
                _out.Styles[id] = new HStyle(id, id, null) { Run = def };
            }
        }

        return _out;
    }

    private void ReadHead(OneHtmlNode head)
    {
        for (OneHtmlNode n = head.FirstChild; !n.IsNull; n = n.NextSibling)
        {
            if (!n.IsElement)
            {
                continue;
            }

            switch (n.LocalName)
            {
                case "title":
                    {
                        string t = (n.TextContent ?? string.Empty).Trim();
                        if (t.Length > 0 && _out.Title is null)
                        {
                            _out.Title = Collapse(t);
                        }

                        break;
                    }

                case "meta":
                    {
                        string? name = Attr(n, "name");
                        string? content = Attr(n, "content");
                        if (name is not null && content is not null && name.Length <= 64)
                        {
                            _out.Meta[name.Trim()] = content.Length > 4096 ? content[..4096] : content;
                        }

                        break;
                    }

                case "link":
                    if (Attr(n, "rel") is { } rel && rel.Contains("stylesheet", StringComparison.OrdinalIgnoreCase))
                    {
                        Lose("loss.html-linked-stylesheet", "linked style sheets", "A linked style sheet is never fetched, so its rules are not applied.", n);
                    }

                    break;
                case "script":
                    Lose("loss.html-script", "scripts", "Scripts are never read or run.", n);
                    break;
            }
        }
    }

    // ---- loss ------------------------------------------------------------------------------------------------------------------

    private void Lose(string code, string feature, string message, OneHtmlNode at = default)
    {
        _loss.Report(code, feature, 1, message);
        if (!at.IsNull && at.IsElement)
        {
            _context.Defects.Report(new Defect(DefectKind.Unsupported, "html.dropped", DefectSeverity.Dropped, feature + ": " + message, DefectLocation.AtText(Math.Max(0, at.Source.Range.Start))));
        }
    }

    // ---- small helpers ---------------------------------------------------------------------------------------------------------

    private static string? Attr(OneHtmlNode e, string name) => e.GetAttribute(name);

    private static string Collapse(string s)
    {
        var sb = new StringBuilder(s.Length);
        bool space = false;
        foreach (char c in s)
        {
            if (char.IsWhiteSpace(c))
            {
                space = true;
                continue;
            }

            if (space && sb.Length > 0)
            {
                sb.Append(' ');
            }

            space = false;
            sb.Append(c);
        }

        return sb.ToString();
    }

    private static string Decode(string s)
    {
        try
        {
            return Uri.UnescapeDataString(s);
        }
        catch (UriFormatException)
        {
            return s;
        }
    }

    private bool IsRtl(OneHtmlNode e) => string.Equals(Attr(e, "dir"), "rtl", StringComparison.OrdinalIgnoreCase) || _styles.GetStyle(e).IsRightToLeft;

    private static string? NormalLang(OneHtmlNode e, string? inherited)
    {
        string? l = Attr(e, "lang") ?? Attr(e, "xml:lang");
        if (string.IsNullOrWhiteSpace(l))
        {
            return inherited;
        }

        l = l.Trim();
        return l.Length <= 35 ? l : inherited;
    }

    private string BookmarkName(string id)
    {
        if (_bookmarkNames.TryGetValue(id, out string? known))
        {
            return known;
        }

        // ECMA-376 Part 1 §17.13.6.2: a bookmark name is at most 40 characters; Word accepts letters, digits and underscores, starting with a letter.
        var sb = new StringBuilder(40);
        foreach (char c in id)
        {
            sb.Append(char.IsAsciiLetterOrDigit(c) || c == '_' ? c : '_');
            if (sb.Length >= 36)
            {
                break;
            }
        }

        if (sb.Length == 0 || !char.IsAsciiLetter(sb[0]))
        {
            sb.Insert(0, "b_");
        }

        string name = sb.ToString();
        int n = 1;
        while (!_bookmarkUsed.Add(name))
        {
            name = sb + "_" + (n++).ToString(CultureInfo.InvariantCulture);
        }

        _bookmarkNames[id] = name;
        return name;
    }

    private void PlaceAnchor(OneHtmlNode e)
    {
        if (_anchorTargets.Count == 0)
        {
            return;
        }

        string? id = Attr(e, "id");
        if (string.IsNullOrEmpty(id))
        {
            id = e.LocalName == "a" ? Attr(e, "name") : null;
        }

        if (!string.IsNullOrEmpty(id) && _anchorTargets.Contains(id) && _anchorsPlaced.Add(id))
        {
            string name = BookmarkName(id);
            if (_cur is not null)
            {
                _cur.Inlines.Add(new HBookmark(name));
            }
            else
            {
                _pendingBookmarks.Add(name);
            }
        }
    }

    private void FlushPendingBookmarks()
    {
        if (_pendingBookmarks.Count == 0)
        {
            return;
        }

        FlushPara();
        var p = new HPara { Props = new HParaProps { Before = 0 } };
        foreach (string b in _pendingBookmarks)
        {
            p.Inlines.Add(new HBookmark(b));
        }

        _pendingBookmarks.Clear();
        _target.Add(p);
    }

    // ---- computed style mapping ------------------------------------------------------------------------------------------------

    private static string Prop(ComputedStyle s, string name) => s.GetPropertyValue(name).Trim();

    // The character formatting a computed style implies, without the parts that depend on the enclosing elements (underline, strike, shading, language).
    private static HRunProps RunCore(ComputedStyle s)
    {
        string fw = Prop(s, "font-weight");
        bool bold = fw is "bold" or "bolder" || (int.TryParse(fw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int w) && w >= 600);
        string fs = Prop(s, "font-style");
        bool italic = fs.StartsWith("italic", StringComparison.Ordinal) || fs.StartsWith("oblique", StringComparison.Ordinal);
        string va = Prop(s, "vertical-align");
        int vert = va is "super" or "sup" ? 1 : va is "sub" ? 2 : 0;
        double pts = vert != 0 && s.Parent is not null ? HtmlCss.FontPoints(s.Parent) : HtmlCss.FontPoints(s);
        string caps = Prop(s, "font-variant-caps");
        if (caps.Length == 0 || caps == "normal")
        {
            caps = Prop(s, "font-variant");
        }

        return new HRunProps(
            Bold: bold,
            Italic: italic,
            Color: HtmlCss.Color(Prop(s, "color")),
            SizeHalfPoints: (int)Math.Clamp(Math.Round(pts * 2), 2, 3276),
            Font: HtmlCss.Family(Prop(s, "font-family")),
            VertAlign: vert,
            Caps: caps.Contains("small-caps", StringComparison.Ordinal) ? 2 : 0);
    }

    private static (bool Underline, bool Strike) Decoration(ComputedStyle s)
    {
        string d = s.GetPropertyValue("text-decoration-line");
        return (d.Contains("underline", StringComparison.Ordinal), d.Contains("line-through", StringComparison.Ordinal));
    }

    private (HRunProps Core, string WhiteSpace, string Transform) TextStyle(OneHtmlNode el)
    {
        if (_textStyles.TryGetValue(el.Index, out var cached))
        {
            return cached;
        }

        ComputedStyle s = _styles.GetStyle(el);
        var v = (RunCore(s), Prop(s, "white-space"), Prop(s, "text-transform"));
        _textStyles[el.Index] = v;
        return v;
    }

    private static HBorder? BorderOf(ComputedStyle s, string side)
    {
        string style = Prop(s, "border-" + side + "-style");
        if (style.Length == 0 || style is "none" or "hidden")
        {
            return null;
        }

        double em = HtmlCss.FontPoints(s);
        int eighths = HtmlCss.BorderEighths(Prop(s, "border-" + side + "-width"), em);
        if (eighths <= 0)
        {
            return null;
        }

        return new HBorder(style, eighths, HtmlCss.Color(Prop(s, "border-" + side + "-color")));
    }

    private static string? OwnBackground(ComputedStyle s) => s.IsDeclared("background-color") ? HtmlCss.Color(Prop(s, "background-color")) : null;

    private static string? AlignOf(ComputedStyle s, bool rtl)
    {
        string a = Prop(s, "text-align");
        if (a.Contains("center", StringComparison.Ordinal))
        {
            return "center";
        }

        return a switch
        {
            "justify" or "justify-all" => "both",
            "right" or "end" => rtl ? null : "right",
            "left" => rtl ? "right" : null,
            _ => null,
        };
    }

    // The properties of a block's paragraphs that do not depend on where the paragraph sits.
    private static HParaProps ParaCore(OneHtmlNode el, ComputedStyle s)
    {
        bool rtl = s.IsRightToLeft;
        double em = HtmlCss.FontPoints(s);
        string lh = Prop(s, "line-height");
        int line = 0;
        string? rule = null;
        if (lh.Length > 0 && lh != "normal")
        {
            if (double.TryParse(lh, NumberStyles.Float, CultureInfo.InvariantCulture, out double mult) && mult > 0)
            {
                line = (int)Math.Clamp(Math.Round(mult * 240), 20, 31680);
                rule = "auto";
            }
            else if (lh.EndsWith('%') && double.TryParse(lh[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out double pct) && pct > 0)
            {
                line = (int)Math.Clamp(Math.Round(pct * 2.4), 20, 31680);
                rule = "auto";
            }
            else if (HtmlCss.Twips(lh, em) is int tw && tw > 0)
            {
                line = tw;
                rule = "atLeast";
            }
        }

        bool boxed = el.LocalName is not ("body" or "html" or "td" or "th");
        return new HParaProps(
            Align: el.LocalName == "center" ? "center" : AlignAttr(el) ?? AlignOf(s, rtl),
            Line: line,
            LineRule: rule,
            Rtl: rtl,
            KeepNext: el.LocalName is "h1" or "h2" or "h3" or "h4" or "h5" or "h6",
            Shade: boxed ? OwnBackground(s) : null,
            BorderTop: boxed ? BorderOf(s, "top") : null,
            BorderRight: boxed ? BorderOf(s, "right") : null,
            BorderBottom: boxed ? BorderOf(s, "bottom") : null,
            BorderLeft: boxed ? BorderOf(s, "left") : null);
    }

    // The legacy align attribute (p, div, headings, cells, rows) that a style sheet did not override.
    private static string? AlignAttr(OneHtmlNode el)
    {
        if (Attr(el, "align")?.Trim().ToLowerInvariant() is not { } a || el.LocalName is "table" or "img" or "hr")
        {
            return null;
        }

        return a switch { "center" or "middle" => "center", "right" => "right", "justify" => "both", _ => null };
    }

    private static bool WantsBreakBefore(ComputedStyle s)
    {
        string a = Prop(s, "break-before");
        string b = Prop(s, "page-break-before");
        return a is "page" or "left" or "right" or "always" or "recto" or "verso" || b is "always" or "left" or "right";
    }

    private static bool WantsBreakAfter(ComputedStyle s)
    {
        string a = Prop(s, "break-after");
        string b = Prop(s, "page-break-after");
        return a is "page" or "left" or "right" or "always" or "recto" or "verso" || b is "always" or "left" or "right";
    }

    // ---- styles for paragraphs -------------------------------------------------------------------------------------------------

    private static (string Id, string Name)? TagStyle(string tag) => tag switch
    {
        "h1" => ("Heading1", "heading 1"),
        "h2" => ("Heading2", "heading 2"),
        "h3" => ("Heading3", "heading 3"),
        "h4" => ("Heading4", "heading 4"),
        "h5" => ("Heading5", "heading 5"),
        "h6" => ("Heading6", "heading 6"),
        "pre" => ("HTMLPreformatted", "HTML Preformatted"),
        "blockquote" => ("Quote", "Quote"),
        "caption" => ("Caption", "caption"),
        _ => null,
    };

    private static string StyleIdFor(string text)
    {
        var sb = new StringBuilder();
        foreach (char c in text)
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                sb.Append(c);
            }
            else if (c is '-' or '_' or ' ')
            {
                // Dropped: style ids are letters and digits, and "my-class" and "myclass" would collide only rarely.
            }

            if (sb.Length >= 40)
            {
                break;
            }
        }

        return sb.Length == 0 ? "Style" : char.IsAsciiDigit(sb[0]) ? "C" + sb : sb.ToString();
    }

    // A named style for a block: the tag's own (headings, preformatted, quotes) and/or the first class on it. Where a class repeats, its
    // formatting is written once in a style and each paragraph refers to it; the first paragraph with the class defines what the style holds.
    private string StyleFor(OneHtmlNode el, ComputedStyle s)
    {
        (string Id, string Name)? tag = TagStyle(el.LocalName);
        string[] classes = Attr(el, "class")?.Split([' ', '\t', '\n', '\r', '\f'], StringSplitOptions.RemoveEmptyEntries) ?? [];
        if (classes.Length > 0 && classes.All(static c => c.Length > 2 && c.StartsWith("s-", StringComparison.Ordinal)))
        {
            // The exporter's own output: every class is a style id, base first, so the last is the style and the one before it is its base.
            string last = StyleIdFor(classes[^1][2..]);
            string? chainBase = classes.Length > 1 ? StyleIdFor(classes[^2][2..]) : null;
            if (chainBase is not null)
            {
                _pendingBases.Add(chainBase);
            }

            if (tag is { } t && t.Id == last)
            {
                if (!_out.Styles.ContainsKey(last))
                {
                    DefineStyle(last, t.Name, chainBase, el, s);
                }
            }
            else if (!_out.Styles.ContainsKey(last))
            {
                DefineStyle(last, last, chainBase, el, s);
            }

            return last;
        }

        string? cls = classes.FirstOrDefault();
        if (tag is null && cls is null)
        {
            return "Normal";
        }

        string id;
        string name;
        string? basedOn = null;
        if (cls is null)
        {
            (id, name) = tag!.Value;
        }
        else
        {
            string stem = StyleIdFor(cls);
            id = tag is null ? stem : stem + "_" + tag.Value.Id;
            name = tag is null ? cls : cls + " (" + tag.Value.Name + ")";
            basedOn = tag?.Id;
            if (tag is not null && !_out.Styles.ContainsKey(tag.Value.Id))
            {
                DefineStyle(tag.Value.Id, tag.Value.Name, null, el, s);
            }
        }

        if (!_out.Styles.ContainsKey(id))
        {
            DefineStyle(id, name, basedOn, el, s);
        }

        return id;
    }

    private void DefineStyle(string id, string name, string? basedOn, OneHtmlNode el, ComputedStyle s)
    {
        if (_out.Styles.Count >= 4096)
        {
            return;
        }

        _out.Styles[id] = new HStyle(id, name, basedOn) { Para = ParaCore(el, s), Run = RunCore(s) };
    }

    // ---- paragraphs ------------------------------------------------------------------------------------------------------------

    private HPara EnsurePara()
    {
        if (_cur is not null)
        {
            return _cur;
        }

        OneHtmlNode src = _source;
        ComputedStyle s = _styles.GetStyle(src);
        string styleId = StyleFor(src, s);
        HParaProps core = ParaCore(src, s);
        HStyle? style = styleId == "Normal" ? null : _out.Styles[styleId];
        HParaProps baseline = style?.Para ?? default;

        // Only what differs from the style is written on the paragraph itself.
        HParaProps direct = core with
        {
            Align = core.Align == baseline.Align ? null : core.Align ?? "left",
            Line = core.Line == baseline.Line && core.LineRule == baseline.LineRule ? 0 : core.Line,
            LineRule = core.Line == baseline.Line && core.LineRule == baseline.LineRule ? null : core.LineRule,
            KeepNext = core.KeepNext && !baseline.KeepNext,
            Shade = core.Shade == baseline.Shade ? null : core.Shade,
            BorderTop = core.BorderTop == baseline.BorderTop ? null : core.BorderTop,
            BorderRight = core.BorderRight == baseline.BorderRight ? null : core.BorderRight,
            BorderBottom = core.BorderBottom == baseline.BorderBottom ? null : core.BorderBottom,
            BorderLeft = core.BorderLeft == baseline.BorderLeft ? null : core.BorderLeft,
        };

        double em = HtmlCss.FontPoints(s);
        direct = direct with
        {
            IndentLeft = _indentLeft,
            IndentRight = _indentRight,
            IndentFirst = HtmlCss.Twips(Prop(s, "text-indent"), em) ?? 0,
            Before = _target.Count == 0 ? 0 : _gap,
            PageBreakBefore = _pageBreakNext,
        };
        _pageBreakNext = false;
        _gap = 0;

        var p = new HPara { StyleId = styleId, Props = direct };
        if (_liList is not null)
        {
            int left = _indentLeft > 0 ? _indentLeft + (Math.Min(_liList.Depth, 8) + 1) * 720 : 0;
            if (!_liUsed)
            {
                p.List = _liList;
                _liUsed = true;
                if (left > 0)
                {
                    p.Props = p.Props with { IndentLeft = left, IndentFirst = -360 };
                }
            }
            else
            {
                p.ListContinuation = true;
                p.Props = p.Props with { IndentLeft = _indentLeft + (Math.Min(_liList.Depth, 8) + 1) * 720 };
            }
        }

        foreach (string b in _pendingBookmarks)
        {
            p.Inlines.Add(new HBookmark(b));
        }

        _pendingBookmarks.Clear();
        _cur = p;
        _target.Add(p);
        return p;
    }

    private void FlushPara()
    {
        if (_cur is null)
        {
            return;
        }

        // Trailing collapsible space and line breaks at the end of a block do not show.
        List<HInline> inl = _cur.Inlines;
        while (inl.Count > 0)
        {
            if (inl[^1] is HText t)
            {
                string trimmed = t.Text.TrimEnd(' ');
                if (trimmed.Length == 0)
                {
                    inl.RemoveAt(inl.Count - 1);
                    continue;
                }

                t.Text = trimmed;
                break;
            }

            if (inl[^1] is HBreak { Page: false })
            {
                inl.RemoveAt(inl.Count - 1);
                continue;
            }

            break;
        }

        _cur = null;
        _atStart = true;
    }

    // ---- the walk --------------------------------------------------------------------------------------------------------------

    private void Walk(OneHtmlNode parent, Format f)
    {
        if (_depth >= _maxDepth)
        {
            Lose("loss.html-depth", "deeply nested content", "Content nested deeper than " + _maxDepth.ToString(CultureInfo.InvariantCulture) + " levels was left out.", parent);
            return;
        }

        _depth++;
        for (OneHtmlNode n = parent.FirstChild; !n.IsNull; n = n.NextSibling)
        {
            _ct.ThrowIfCancellationRequested();
            if (n.Kind == NodeKind.Text)
            {
                AddText(n.Data.ToString(), parent, f);
            }
            else if (n.IsElement)
            {
                Element(n, f);
            }
        }

        _depth--;
    }

    private static bool IsBlockDisplay(string display) =>
        display is "block" or "list-item" or "flex" or "grid" or "flow-root" or "table" or "table-row" or "table-cell" or "table-row-group"
            or "table-header-group" or "table-footer-group" or "table-caption" or "table-column" or "table-column-group" || display.StartsWith("block ", StringComparison.Ordinal);

    private void Element(OneHtmlNode e, Format f)
    {
        string name = e.LocalName;
        if (e.Namespace != ElementNamespace.Html)
        {
            Lose(e.Namespace == ElementNamespace.Svg ? "loss.html-svg" : "loss.html-math", e.Namespace == ElementNamespace.Svg ? "inline SVG" : "MathML",
                "Inline vector and math islands are not carried into the document.", e);
            return;
        }

        CountHandlers(e);
        switch (name)
        {
            case "script":
                Lose("loss.html-script", "scripts", "Scripts are never read or run.", e);
                return;
            case "link":
                if (Attr(e, "rel") is { } rel && rel.Contains("stylesheet", StringComparison.OrdinalIgnoreCase))
                {
                    Lose("loss.html-linked-stylesheet", "linked style sheets", "A linked style sheet is never fetched, so its rules are not applied.", e);
                }

                return;
            case "style" or "template" or "head" or "title" or "meta" or "base" or "datalist" or "param" or "source" or "track" or "area" or "map":
                return;
            case "iframe" or "frame" or "frameset" or "object" or "embed" or "applet":
                Lose("loss.html-embedded-content", "frames and embedded objects", "Frames, objects, embeds and applets are not carried into the document.", e);
                if (_options.Sanitization == HtmlSanitization.KeepInert && name is "object" or "iframe" or "applet")
                {
                    Walk(e, f);
                }

                return;
            case "canvas" or "audio" or "video":
                Lose("loss.html-media", "canvas and media elements", "Canvas, audio and video content is not carried into the document.", e);
                return;
            case "input" or "button" or "select" or "textarea" or "option" or "optgroup" or "output" or "meter" or "progress":
                Lose("loss.html-form-control", "form controls", "Form controls are not carried into the document.", e);
                if (_options.Sanitization == HtmlSanitization.KeepInert)
                {
                    InertControlText(e, f);
                }

                return;
            case "form":
                Lose("loss.html-form", "forms", "The form itself is dropped; the text and blocks inside it stay.", e);
                break;
            case "noscript":
                break;
        }

        ComputedStyle s = _styles.GetStyle(e);
        string display = Prop(s, "display");
        if (display == "none" || e.HasAttribute("hidden") && name != "input")
        {
            Lose("loss.html-hidden", "hidden content", "Elements that are not displayed (display: none, hidden) are left out.", e);
            return;
        }

        switch (name)
        {
            case "br":
                // A <br> that starts a block is an empty line.
                EnsurePara().Inlines.Add(new HBreak(false));
                _atStart = true;
                return;
            case "wbr":
                return;
            case "hr":
                Rule(e, s);
                return;
            case "img":
                Image(e, s, f);
                return;
            case "table":
                Table(e, s, f);
                return;
            case "ul" or "ol" or "menu" or "dir":
                ListElement(e, s, f);
                return;
        }

        if (name == "li" || IsBlockDisplay(display) || name is "p" or "div" or "section" or "article" or "header" or "footer" or "main" or "nav" or "aside" or "figure" or "figcaption" or "address"
            or "details" or "summary" or "dl" or "dt" or "dd" or "center" or "blockquote" or "pre" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "form" or "fieldset" or "legend" or "caption")
        {
            BlockElement(e, s, f, display);
            return;
        }

        InlineElement(e, s, f);
    }

    private void CountHandlers(OneHtmlNode e)
    {
        AttributeList attrs = e.Attributes;
        for (int i = 0; i < attrs.Count; i++)
        {
            string an = attrs[i].LocalName;
            if (an.Length > 2 && an[0] is 'o' or 'O' && an[1] is 'n' or 'N')
            {
                Lose("loss.html-event-handler", "event handler attributes", "Attributes such as onclick are never read; they cannot run in a document.", e);
                return;
            }
        }
    }

    private void InertControlText(OneHtmlNode e, Format f)
    {
        string text = e.LocalName switch
        {
            "input" => Attr(e, "type") is "hidden" or "password" ? string.Empty : Attr(e, "value") ?? Attr(e, "placeholder") ?? string.Empty,
            _ => Collapse(e.TextContent ?? string.Empty),
        };
        if (text.Length > 0)
        {
            AddText(text, e, f);
        }
    }

    private static Format InlineFormat(OneHtmlNode e, ComputedStyle s, Format f)
    {
        (bool u, bool st) = Decoration(s);
        string? shade = OwnBackground(s) ?? f.Shade;
        return f with { Underline = f.Underline || u, Strike = f.Strike || st, Shade = shade, Lang = NormalLang(e, f.Lang) };
    }

    private void InlineElement(OneHtmlNode e, ComputedStyle s, Format f)
    {
        Format f2 = InlineFormat(e, s, f);
        PlaceAnchor(e);
        if (e.LocalName == "a")
        {
            string? href = Attr(e, "href")?.Trim();
            if (!string.IsNullOrEmpty(href))
            {
                string? target = LinkTarget(href, e);
                if (target is not null)
                {
                    f2 = f2 with { Link = target };
                }
            }
        }

        Walk(e, f2);
    }

    // The link target to keep, or null when it is not allowed (the text stays).
    private string? LinkTarget(string href, OneHtmlNode at)
    {
        if (href.Length > 8192)
        {
            Lose("loss.html-unsafe-link", "unsafe links", "A link target that is too long, or whose scheme can run code or reach local files, is dropped; its text stays.", at);
            return null;
        }

        if (href[0] == '#')
        {
            return href.Length > 1 ? "#" + BookmarkName(Decode(href[1..])) : null;
        }

        // Browsers ignore tabs, line feeds and spaces inside the scheme, so the check does too.
        var sb = new StringBuilder(Math.Min(href.Length, 32));
        foreach (char c in href)
        {
            if (c <= ' ' || c == '\u007F')
            {
                continue;
            }

            sb.Append(c);
            if (c is ':' or '/' or '?' or '#' || sb.Length >= 32)
            {
                break;
            }
        }

        string head = sb.ToString();
        int colon = head.IndexOf(':');
        bool hasControl = false;
        foreach (char c in href)
        {
            if (c < ' ' && c is not ('\t' or '\n' or '\r'))
            {
                hasControl = true;
                break;
            }
        }

        if (hasControl)
        {
            Lose("loss.html-unsafe-link", "unsafe links", "A link target that is too long, or whose scheme can run code or reach local files, is dropped; its text stays.", at);
            return null;
        }

        if (colon < 0)
        {
            return href;
        }

        string scheme = head[..colon].ToLowerInvariant();
        bool ok = scheme is "http" or "https" or "mailto" or "tel" or "ftp" or "ftps" or "sms";
        if (!ok && _options.Sanitization == HtmlSanitization.KeepInert && scheme is not ("javascript" or "vbscript" or "livescript" or "data" or "blob" or "file" or "about" or "jar" or "view-source"))
        {
            ok = scheme.Length > 0 && scheme.All(static c => char.IsAsciiLetterOrDigit(c) || c is '+' or '-' or '.');
        }

        if (!ok)
        {
            Lose("loss.html-unsafe-link", "unsafe links", "A link target that is too long, or whose scheme can run code or reach local files, is dropped; its text stays.", at);
            return null;
        }

        return href;
    }
}
