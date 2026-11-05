using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Documents.Revisions;

/// <summary>Underline styles for <see cref="RunPropertiesDelta.Underline"/> (ECMA-376 Part 1 §17.18.99, the common ones).</summary>
public enum RunUnderline
{
    /// <summary>No underline (written as <c>none</c>, which overrides a style's underline).</summary>
    None = 0,

    /// <summary>A single line.</summary>
    SingleLine = 1,

    /// <summary>A double line.</summary>
    DoubleLine = 2,

    /// <summary>A thick line.</summary>
    Thick = 3,

    /// <summary>A dotted line.</summary>
    Dotted = 4,

    /// <summary>A dashed line.</summary>
    Dashed = 5,

    /// <summary>A wavy line.</summary>
    Wave = 6,

    /// <summary>A single line under words only, not spaces.</summary>
    Words = 7,
}

/// <summary>Paragraph alignment for <see cref="ParagraphPropertiesDelta.Alignment"/> (§17.18.44).</summary>
public enum ParagraphAlignment
{
    /// <summary>Aligned to the start edge (left in left-to-right text).</summary>
    Left = 0,

    /// <summary>Centred.</summary>
    Center = 1,

    /// <summary>Aligned to the end edge (right in left-to-right text).</summary>
    Right = 2,

    /// <summary>Justified (<c>both</c>).</summary>
    Justified = 3,

    /// <summary>Distributed: spaces and letters are spread over the full line.</summary>
    Distributed = 4,
}

/// <summary>
/// A set of run formatting changes to apply as a tracked change: each property that is not null is set, each that is null is left as it
/// is. A false boolean is written explicitly (<c>w:val="0"</c>) so that it overrides a style that turns the property on.
/// </summary>
public sealed record RunPropertiesDelta
{
    /// <summary>Gets the character style id (<c>w:rStyle</c>); an empty string removes the style.</summary>
    public string? StyleId { get; init; }

    /// <summary>Gets the font family name, applied to ASCII, high-ANSI, complex-script and East Asian text (<c>w:rFonts</c>).</summary>
    public string? Font { get; init; }

    /// <summary>Gets the size in points (<c>w:sz</c> and <c>w:szCs</c>, which count half-points); at most 1638 and a multiple of 0.5.</summary>
    public double? SizePoints { get; init; }

    /// <summary>Gets whether the text is bold.</summary>
    public bool? Bold { get; init; }

    /// <summary>Gets whether the text is italic.</summary>
    public bool? Italic { get; init; }

    /// <summary>Gets whether the text is struck through.</summary>
    public bool? Strike { get; init; }

    /// <summary>Gets whether the text is in small capitals.</summary>
    public bool? SmallCaps { get; init; }

    /// <summary>Gets whether the text is in all capitals.</summary>
    public bool? AllCaps { get; init; }

    /// <summary>Gets the underline style.</summary>
    public RunUnderline? Underline { get; init; }

    /// <summary>Gets the text colour as six hex digits (<c>FF0000</c>) or <c>auto</c>.</summary>
    public string? Color { get; init; }

    /// <summary>Gets the highlight colour name (<c>yellow</c>, <c>green</c>, …, or <c>none</c>), per §17.18.40.</summary>
    public string? Highlight { get; init; }

    /// <summary>Merges the delta over the existing children of a <c>w:rPr</c> (null for none) and returns the new content.</summary>
    internal string Merge(MarkupNode rPr, WmlWriter w, string[] excluded)
    {
        var m = new PropertyMerge(rPr, Order, w, excluded);
        foreach ((string local, string? xml) in Elements(w))
        {
            m.Set(local, xml);
        }

        return m.Result();
    }

    private List<(string Local, string? Xml)> Elements(WmlWriter w)
    {
        var l = new List<(string, string?)>();
        if (StyleId is { } s)
        {
            l.Add(("rStyle", s.Length == 0 ? null : w.Empty("rStyle", w.Attr("val", Text(s)))));
        }

        if (Font is { } f)
        {
            string a = w.Attr("ascii", Text(f)) + w.Attr("hAnsi", f) + w.Attr("eastAsia", f) + w.Attr("cs", f);
            l.Add(("rFonts", w.Empty("rFonts", a)));
        }

        if (Bold is { } b)
        {
            l.Add(("b", Toggle(w, "b", b)));
            l.Add(("bCs", Toggle(w, "bCs", b)));
        }

        if (Italic is { } i)
        {
            l.Add(("i", Toggle(w, "i", i)));
            l.Add(("iCs", Toggle(w, "iCs", i)));
        }

        if (AllCaps is { } ac)
        {
            l.Add(("caps", Toggle(w, "caps", ac)));
        }

        if (SmallCaps is { } sc)
        {
            l.Add(("smallCaps", Toggle(w, "smallCaps", sc)));
        }

        if (Strike is { } st)
        {
            l.Add(("strike", Toggle(w, "strike", st)));
        }

        if (Color is { } c)
        {
            if (!string.Equals(c, "auto", StringComparison.Ordinal) && !PropertyMerge.IsHex6(c))
            {
                throw new ArgumentException("Color must be six hex digits or 'auto'.");
            }

            l.Add(("color", w.Empty("color", w.Attr("val", c))));
        }

        if (SizePoints is { } pt)
        {
            double half = pt * 2;
            if (!(half >= 1 && half <= 3276) || half != Math.Floor(half))
            {
                throw new ArgumentException("SizePoints must be a multiple of 0.5 between 0.5 and 1638.");
            }

            string v = ((int)half).ToString(CultureInfo.InvariantCulture);
            l.Add(("sz", w.Empty("sz", w.Attr("val", v))));
            l.Add(("szCs", w.Empty("szCs", w.Attr("val", v))));
        }

        if (Highlight is { } h)
        {
            l.Add(("highlight", w.Empty("highlight", w.Attr("val", Text(h)))));
        }

        if (Underline is { } u)
        {
            string name = u switch
            {
                RunUnderline.None => "none",
                RunUnderline.SingleLine => "single",
                RunUnderline.DoubleLine => "double",
                RunUnderline.Thick => "thick",
                RunUnderline.Dotted => "dotted",
                RunUnderline.Dashed => "dash",
                RunUnderline.Wave => "wave",
                RunUnderline.Words => "words",
                _ => throw new ArgumentOutOfRangeException(null, "The value is out of range."),
            };
            l.Add(("u", w.Empty("u", w.Attr("val", name))));
        }

        return l;
    }

    private static string Text(string s)
    {
        WordXml.RequireXmlText(s, "value");
        return s;
    }

    internal static string Toggle(WmlWriter w, string local, bool on) =>
        on ? w.Empty(local) : w.Empty(local, w.Attr("val", "0"));

    /// <summary>The order of the run property elements (§17.3.2.28).</summary>
    internal static readonly string[] Order =
    [
        "rStyle", "rFonts", "b", "bCs", "i", "iCs", "caps", "smallCaps", "strike", "dstrike", "outline", "shadow", "emboss", "imprint",
        "noProof", "snapToGrid", "vanish", "webHidden", "color", "spacing", "w", "kern", "position", "sz", "szCs", "highlight", "u",
        "effect", "bdr", "shd", "fitText", "vertAlign", "rtl", "cs", "em", "lang", "eastAsianLayout", "specVanish", "oMath",
    ];
}

/// <summary>
/// A set of paragraph formatting changes to apply as a tracked change: each property that is not null is set, each that is null is left as
/// it is. Lengths are in twentieths of a point (twips) as the file stores them.
/// </summary>
public sealed record ParagraphPropertiesDelta
{
    /// <summary>Gets the paragraph style id (<c>w:pStyle</c>); an empty string removes the style.</summary>
    public string? StyleId { get; init; }

    /// <summary>Gets the alignment.</summary>
    public ParagraphAlignment? Alignment { get; init; }

    /// <summary>Gets the left (start) indent in twips.</summary>
    public int? LeftIndent { get; init; }

    /// <summary>Gets the right (end) indent in twips.</summary>
    public int? RightIndent { get; init; }

    /// <summary>Gets the first-line indent in twips; setting it clears a hanging indent.</summary>
    public int? FirstLineIndent { get; init; }

    /// <summary>Gets the hanging indent in twips; setting it clears a first-line indent.</summary>
    public int? HangingIndent { get; init; }

    /// <summary>Gets the space before the paragraph, in twips.</summary>
    public int? SpacingBefore { get; init; }

    /// <summary>Gets the space after the paragraph, in twips.</summary>
    public int? SpacingAfter { get; init; }

    /// <summary>Gets the line spacing as a multiple of single spacing (1.5 is one and a half lines); written as <c>w:line</c> in 240ths with the <c>auto</c> rule.</summary>
    public double? LineSpacingMultiple { get; init; }

    /// <summary>Gets whether the paragraph stays on the page with the next one.</summary>
    public bool? KeepNext { get; init; }

    /// <summary>Gets whether the paragraph's lines stay together.</summary>
    public bool? KeepLines { get; init; }

    /// <summary>Gets whether a page break comes before the paragraph.</summary>
    public bool? PageBreakBefore { get; init; }

    /// <summary>Gets the numbering definition instance (<c>w:numId</c>); 0 takes the paragraph out of its list. This is how a numbering change is recorded: as a paragraph property change.</summary>
    public int? NumberingId { get; init; }

    /// <summary>Gets the list level (<c>w:ilvl</c>), 0 to 8.</summary>
    public int? NumberingLevel { get; init; }

    internal static readonly string[] Order =
    [
        "pStyle", "keepNext", "keepLines", "pageBreakBefore", "framePr", "widowControl", "numPr", "suppressLineNumbers", "pBdr", "shd",
        "tabs", "suppressAutoHyphens", "kinsoku", "wordWrap", "overflowPunct", "topLinePunct", "autoSpaceDE", "autoSpaceDN", "bidi",
        "adjustRightInd", "snapToGrid", "spacing", "ind", "contextualSpacing", "mirrorIndents", "suppressOverlap", "jc", "textDirection",
        "textAlignment", "textboxTightWrap", "outlineLvl", "divId", "cnfStyle", "rPr", "sectPr", "pPrChange",
    ];

    /// <summary>Merges the delta over the existing children of a <c>w:pPr</c> (given as element nodes) and returns the new content.</summary>
    internal string Merge(MarkupNode pPr, WmlWriter w, string[] excluded)
    {
        if (NumberingLevel is { } lvl && (lvl < 0 || lvl > 8))
        {
            throw new ArgumentOutOfRangeException(null, "The value is out of range.");
        }

        if (NumberingId is < 0)
        {
            throw new ArgumentOutOfRangeException(null, "The value is out of range.");
        }

        var m = new PropertyMerge(pPr, Order, w, excluded);
        if (StyleId is { } s)
        {
            m.Set("pStyle", s.Length == 0 ? null : w.Empty("pStyle", w.Attr("val", Checked(s))));
        }

        if (KeepNext is { } kn)
        {
            m.Set("keepNext", RunPropertiesDelta.Toggle(w, "keepNext", kn));
        }

        if (KeepLines is { } kl)
        {
            m.Set("keepLines", RunPropertiesDelta.Toggle(w, "keepLines", kl));
        }

        if (PageBreakBefore is { } pb)
        {
            m.Set("pageBreakBefore", RunPropertiesDelta.Toggle(w, "pageBreakBefore", pb));
        }

        if (NumberingId is not null || NumberingLevel is not null)
        {
            MarkupNode existing = m.Existing("numPr");
            string ilvl = NumberingLevel is { } l ? l.ToString(CultureInfo.InvariantCulture) : ValueOf(existing, "ilvl");
            string numId = NumberingId is { } n ? n.ToString(CultureInfo.InvariantCulture) : ValueOf(existing, "numId");
            m.Set("numPr", w.Wrap("numPr", string.Empty, w.Empty("ilvl", w.Attr("val", ilvl)) + w.Empty("numId", w.Attr("val", numId))));
        }

        if (SpacingBefore is not null || SpacingAfter is not null || LineSpacingMultiple is not null)
        {
            var a = new List<(string, string?)>();
            if (SpacingBefore is { } b)
            {
                a.Add(("before", b.ToString(CultureInfo.InvariantCulture)));
            }

            if (SpacingAfter is { } af)
            {
                a.Add(("after", af.ToString(CultureInfo.InvariantCulture)));
            }

            if (LineSpacingMultiple is { } lm)
            {
                if (!(lm > 0 && lm < 130))
                {
                    throw new ArgumentOutOfRangeException(null, "The value is out of range.");
                }

                a.Add(("line", Math.Round(lm * 240).ToString(CultureInfo.InvariantCulture)));
                a.Add(("lineRule", "auto"));
            }

            m.SetAttributes("spacing", a);
        }

        if (LeftIndent is not null || RightIndent is not null || FirstLineIndent is not null || HangingIndent is not null)
        {
            if (FirstLineIndent is not null && HangingIndent is not null)
            {
                throw new ArgumentException("FirstLineIndent and HangingIndent cannot both be set.");
            }

            var a = new List<(string, string?)>();
            if (LeftIndent is { } li)
            {
                a.Add((w.IsStrict ? "start" : "left", li.ToString(CultureInfo.InvariantCulture)));
            }

            if (RightIndent is { } ri)
            {
                a.Add((w.IsStrict ? "end" : "right", ri.ToString(CultureInfo.InvariantCulture)));
            }

            if (FirstLineIndent is { } fi)
            {
                a.Add(("firstLine", fi.ToString(CultureInfo.InvariantCulture)));
                a.Add(("hanging", null));
            }

            if (HangingIndent is { } hi)
            {
                a.Add(("hanging", hi.ToString(CultureInfo.InvariantCulture)));
                a.Add(("firstLine", null));
            }

            m.SetAttributes("ind", a);
        }

        if (Alignment is { } al)
        {
            string v = al switch
            {
                ParagraphAlignment.Left => w.IsStrict ? "start" : "left",
                ParagraphAlignment.Center => "center",
                ParagraphAlignment.Right => w.IsStrict ? "end" : "right",
                ParagraphAlignment.Justified => "both",
                ParagraphAlignment.Distributed => "distribute",
                _ => throw new ArgumentOutOfRangeException(null, "The value is out of range."),
            };
            m.Set("jc", w.Empty("jc", w.Attr("val", v)));
        }

        return m.Result();
    }

    private static string ValueOf(MarkupNode numPr, string child)
    {
        if (numPr.IsNull)
        {
            return "0";
        }

        MarkupNode c = WordXml.Child(numPr, child);
        return c.IsNull ? "0" : WordXml.Attr(c, "val") ?? "0";
    }

    private static string Checked(string s)
    {
        WordXml.RequireXmlText(s, "value");
        return s;
    }
}

/// <summary>A set of table formatting changes (<c>w:tblPr</c>) to apply as a tracked change; null properties are left as they are.</summary>
public sealed record TablePropertiesDelta
{
    /// <summary>Gets the table style id (<c>w:tblStyle</c>); an empty string removes it.</summary>
    public string? StyleId { get; init; }

    /// <summary>Gets the preferred table width in twips (<c>w:tblW</c> of type <c>dxa</c>).</summary>
    public int? WidthTwips { get; init; }

    /// <summary>Gets the table's alignment on the page (<c>w:jc</c>).</summary>
    public ParagraphAlignment? Alignment { get; init; }

    /// <summary>Gets whether column widths are fixed (<c>w:tblLayout w:type="fixed"</c>) or follow the content.</summary>
    public bool? FixedLayout { get; init; }

    internal static readonly string[] Order =
    [
        "tblStyle", "tblpPr", "tblOverlap", "bidiVisual", "tblStyleRowBandSize", "tblStyleColBandSize", "tblW", "jc", "tblCellSpacing",
        "tblInd", "tblBorders", "shd", "tblLayout", "tblCellMar", "tblLook", "tblCaption", "tblDescription", "tblPrChange",
    ];

    internal string Merge(MarkupNode tblPr, WmlWriter w, string[] excluded)
    {
        var m = new PropertyMerge(tblPr, Order, w, excluded);
        if (StyleId is { } s)
        {
            m.Set("tblStyle", s.Length == 0 ? null : w.Empty("tblStyle", w.Attr("val", s)));
        }

        if (WidthTwips is { } wd)
        {
            m.Set("tblW", w.Empty("tblW", w.Attr("w", wd.ToString(CultureInfo.InvariantCulture)) + w.Attr("type", "dxa")));
        }

        if (Alignment is { } al)
        {
            m.Set("jc", w.Empty("jc", w.Attr("val", PropertyMerge.TableAlignment(al, w.IsStrict))));
        }

        if (FixedLayout is { } f)
        {
            m.Set("tblLayout", w.Empty("tblLayout", w.Attr("type", f ? "fixed" : "autofit")));
        }

        return m.Result();
    }
}

/// <summary>A set of table row formatting changes (<c>w:trPr</c>) to apply as a tracked change; null properties are left as they are.</summary>
public sealed record RowPropertiesDelta
{
    /// <summary>Gets the row height in twips (<c>w:trHeight</c>, rule <c>atLeast</c>).</summary>
    public int? HeightTwips { get; init; }

    /// <summary>Gets whether the row repeats as a header row on each page (<c>w:tblHeader</c>).</summary>
    public bool? RepeatAsHeader { get; init; }

    /// <summary>Gets whether the row may not break across pages (<c>w:cantSplit</c>).</summary>
    public bool? CannotSplit { get; init; }

    internal string Merge(MarkupNode trPr, WmlWriter w, string[] excluded)
    {
        var m = new PropertyMerge(trPr, [], w, excluded);
        if (CannotSplit is { } c)
        {
            m.Set("cantSplit", RunPropertiesDelta.Toggle(w, "cantSplit", c));
        }

        if (HeightTwips is { } h)
        {
            m.Set("trHeight", w.Empty("trHeight", w.Attr("val", h.ToString(CultureInfo.InvariantCulture)) + w.Attr("hRule", "atLeast")));
        }

        if (RepeatAsHeader is { } r)
        {
            m.Set("tblHeader", RunPropertiesDelta.Toggle(w, "tblHeader", r));
        }

        return m.Result();
    }
}

/// <summary>A set of table cell formatting changes (<c>w:tcPr</c>) to apply as a tracked change; null properties are left as they are.</summary>
public sealed record CellPropertiesDelta
{
    /// <summary>Gets the preferred cell width in twips (<c>w:tcW</c> of type <c>dxa</c>).</summary>
    public int? WidthTwips { get; init; }

    /// <summary>Gets the background fill as six hex digits or <c>auto</c> (<c>w:shd</c> with a clear pattern).</summary>
    public string? ShadingFill { get; init; }

    /// <summary>Gets the vertical alignment: <c>top</c>, <c>center</c> or <c>bottom</c>.</summary>
    public string? VerticalAlignment { get; init; }

    internal static readonly string[] Order =
    [
        "cnfStyle", "tcW", "gridSpan", "hMerge", "vMerge", "tcBorders", "shd", "noWrap", "tcMar", "textDirection", "tcFitText", "vAlign",
        "hideMark", "headers", "cellIns", "cellDel", "cellMerge", "tcPrChange",
    ];

    internal string Merge(MarkupNode tcPr, WmlWriter w, string[] excluded)
    {
        var m = new PropertyMerge(tcPr, Order, w, excluded);
        if (WidthTwips is { } wd)
        {
            m.Set("tcW", w.Empty("tcW", w.Attr("w", wd.ToString(CultureInfo.InvariantCulture)) + w.Attr("type", "dxa")));
        }

        if (ShadingFill is { } f)
        {
            if (!string.Equals(f, "auto", StringComparison.Ordinal) && !PropertyMerge.IsHex6(f))
            {
                throw new ArgumentException("ShadingFill must be six hex digits or 'auto'.");
            }

            m.Set("shd", w.Empty("shd", w.Attr("val", "clear") + w.Attr("color", "auto") + w.Attr("fill", f)));
        }

        if (VerticalAlignment is { } v)
        {
            if (v is not ("top" or "center" or "bottom"))
            {
                throw new ArgumentException("VerticalAlignment must be 'top', 'center' or 'bottom'.");
            }

            m.Set("vAlign", w.Empty("vAlign", w.Attr("val", v)));
        }

        return m.Result();
    }
}

/// <summary>Merges new property elements into the existing children of a property container, keeping the schema's order and everything not touched.</summary>
internal sealed class PropertyMerge
{
    private readonly string[] _order;
    private readonly WmlWriter _w;
    private readonly List<(string Local, string Xml, MarkupNode Node)> _items = [];

    internal PropertyMerge(MarkupNode container, string[] order, WmlWriter w, string[] excluded)
    {
        _order = order;
        _w = w;
        if (!container.IsNull)
        {
            foreach (MarkupNode c in container.ChildElements)
            {
                if (WordXml.InMain(c) && Array.IndexOf(excluded, c.LocalNameString) >= 0)
                {
                    continue;
                }

                _items.Add((WordXml.InMain(c) ? c.LocalNameString : "\0", WordXml.Portable(c), c));
            }
        }
    }

    internal MarkupNode Existing(string local)
    {
        foreach ((string l, _, MarkupNode n) in _items)
        {
            if (l == local)
            {
                return n;
            }
        }

        return default;
    }

    /// <summary>Sets an element: replaces the one of that name, or inserts it where the schema order puts it. Null removes it.</summary>
    internal void Set(string local, string? xml)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            if (_items[i].Local == local)
            {
                if (xml is null)
                {
                    _items.RemoveAt(i);
                }
                else
                {
                    _items[i] = (local, xml, default);
                }

                return;
            }
        }

        if (xml is null)
        {
            return;
        }

        int rank = Array.IndexOf(_order, local);
        int at = _items.Count;
        if (rank >= 0)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                int r = Array.IndexOf(_order, _items[i].Local);
                if (r > rank)
                {
                    at = i;
                    break;
                }
            }
        }

        _items.Insert(at, (local, xml, default));
    }

    /// <summary>Sets attributes of an element, keeping its other attributes; a null value removes the attribute.</summary>
    internal void SetAttributes(string local, List<(string Name, string? Value)> changes)
    {
        MarkupNode existing = Existing(local);
        var attrs = new List<(string Raw, string Local, string Value)>();
        if (!existing.IsNull)
        {
            foreach (MarkupAttributeRef a in existing.Attributes)
            {
                if (a.IsNamespaceDeclaration)
                {
                    continue;
                }

                attrs.Add((Encoding.UTF8.GetString(a.RawName), Encoding.UTF8.GetString(a.LocalName), a.Value));
            }
        }

        foreach ((string name, string? value) in changes)
        {
            attrs.RemoveAll(a => a.Local == name);
            if (value is not null)
            {
                attrs.Add((_w.Name(name), name, value));
            }
        }

        var sb = new StringBuilder();
        foreach ((string raw, _, string value) in attrs)
        {
            sb.Append(' ').Append(raw).Append("=\"").Append(WordXml.EscapeAttribute(value)).Append('"');
        }

        Set(local, attrs.Count == 0 ? null : _w.Empty(local, sb.ToString()));
    }

    internal string Result()
    {
        var sb = new StringBuilder();
        foreach ((_, string xml, _) in _items)
        {
            sb.Append(xml);
        }

        return sb.ToString();
    }

    internal static bool IsHex6(string s)
    {
        if (s.Length != 6)
        {
            return false;
        }

        foreach (char c in s)
        {
            if (!(c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F'))
            {
                return false;
            }
        }

        return true;
    }

    internal static string TableAlignment(ParagraphAlignment a, bool strict) => a switch
    {
        ParagraphAlignment.Left => strict ? "start" : "left",
        ParagraphAlignment.Center => "center",
        ParagraphAlignment.Right => strict ? "end" : "right",
        _ => throw new ArgumentException("A table can be left, centre or right aligned."),
    };
}
