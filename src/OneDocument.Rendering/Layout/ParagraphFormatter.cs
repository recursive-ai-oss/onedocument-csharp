using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Documents.Numbering;
using RecursiveAI.OneDocument.Documents.Styles;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Schema;

namespace RecursiveAI.OneDocument.Layout;

internal enum TabAlign
{
    Left,
    Center,
    Right,
    Decimal,
}

internal readonly record struct TabStop(double Position, TabAlign Align, string Leader);

internal enum AlignMode
{
    Start,
    Center,
    End,
    Justify,
    Distribute,
}

// How the characters of a paragraph are formatted, resolved once per run: sizes in points, flags for the toggles.
internal sealed class RunInfo
{
    public Run? Source;
    public string? Link;
    public double Size = 10;
    public double SizeCs = 10;
    public bool Bold;
    public bool BoldCs;
    public bool Italic;
    public bool ItalicCs;
    public bool Caps;
    public bool SmallCaps;
    public bool Hidden;
    public string Ascii = "Times New Roman";
    public string HAnsi = "Times New Roman";
    public string EastAsia = "Times New Roman";
    public string Cs = "Times New Roman";
    public double Spacing;
    public double Scale = 1.0;
    public double Position;
    public int VertAlign; // 1 superscript, -1 subscript
    public bool Kerning;
    public string? Lang;
    public RunPaintInfo Paint = new();
}

/// <summary>The measurable pieces of a paragraph, built before lines are cut.</summary>
internal enum AtomKind
{
    Glyphs,
    Space,
    Tab,
    LineBreak,
    PageBreak,
    Soft,
}

// One line-breaking unit: a shaped cluster, a tab, or a break character.
internal struct Atom
{
    public int Piece;
    public int Cluster;
    public int TextStart; // offset in the paragraph text; -1 for the list label
    public int TextLength;
    public double Width;
    public byte Level;
    public bool BreakBefore;
    public AtomKind Kind;
}

internal sealed class Piece
{
    public AtomKind Kind;
    public RunInfo? Run;
    public FontMatch? Font;
    public global::OneFont.FontFace[] Faces = [];
    public double Size;
    public byte Level;
    public int Start; // text offset, -1 for the label
    public int Length;
    public string Text = string.Empty;
    public string? Shown; // what was shaped when the run is drawn in capitals, otherwise null
    public bool IsLabel;
    public int ClusterCount;
    public int[] CStart = [];
    public int[] CLength = [];
    public int[] CFace = [];
    public int[] CGlyphStart = [];
    public int[] CGlyphCount = [];
    public int[] GlyphId = [];
    public int[] GXAdv = [];
    public int[] GXOff = [];
    public int[] GYOff = [];
    public double[] CAdv = []; // advance of the cluster in points before scale and spacing
}

internal sealed class LineItem
{
    public LayoutRunKind Kind;
    public double X;
    public double Width;
    public string Text = string.Empty;
    public int TextStart;
    public Run? Source;
    public FontMatch? Font;
    public double Size;
    public int Level;
    public RunPaintInfo Paint = new();
    public GlyphData? Glyphs;
    public int[] ClusterStarts = [];
    public double[] ClusterX = [];
    }

internal sealed class FormattedLine
{
    public int TextStart;
    public int TextLength;
    public string Text = string.Empty;
    public double Ascent;
    public double Descent;
    public bool IsLast;
    public bool EndsWithPageBreak;
    public List<LineItem> Items = [];
    public double Height => Ascent + Descent;
}

internal class FormattedParagraph
{
    public required Paragraph Source;
    public string Text = string.Empty;
    public double SpaceBefore;
    public double SpaceAfter;
    public bool KeepNext;
    public bool KeepLines;
    public bool PageBreakBefore;
    public bool WidowControl = true;
    public bool ContextualSpacing;
    public string? StyleId;
    public int Heading;
    public string? Label;
    public bool Rtl;
    public double BorderTopHeight;
    public double BorderBottomHeight;
    public List<FormattedLine> Lines = [];
    public ParagraphPaintInfo Paint = new();
}

// Lays out the lines of one paragraph for a given text width. Rules are cited where they come from; the choices the specification
// leaves open (and that need Word's own output to settle) are listed in docs/layout.md.
internal static class ParagraphFormatter
{
    private const double SmallCapsScale = 0.8;
    private const double ScriptScale = 0.65;

    public static FormattedParagraph Format(Paragraph p, LayoutContext ctx, double width)
    {
        ResolvedParagraphProperties pp = p.ResolvedProperties;
        var fp = new FormattedParagraph { Source = p, StyleId = pp.StyleId };

        // ECMA-376 Part 1 §17.3.1.6 (bidi): the paragraph's base direction.
        bool rtl = pp.Flag("bidi") ?? false;
        fp.Rtl = rtl;
        fp.KeepNext = pp.KeepNext ?? false;
        fp.KeepLines = pp.KeepLines ?? false;
        fp.PageBreakBefore = pp.PageBreakBefore ?? false;
        fp.WidowControl = pp.Flag("widowControl") ?? true;
        fp.ContextualSpacing = pp.Flag("contextualSpacing") ?? false;
        fp.Heading = HeadingLevel(ctx.Doc, pp);

        ResolvedProperty? spacing = pp.Get("spacing");
        fp.SpaceBefore = AutoSpace(spacing, "beforeAutospacing", "before");
        fp.SpaceAfter = AutoSpace(spacing, "afterAutospacing", "after");

        double leftInd = (pp.LeftIndent ?? 0) / 20.0;
        double rightInd = (pp.RightIndent ?? 0) / 20.0;
        double hanging = (pp.HangingIndent ?? 0) / 20.0;
        double firstLine = (pp.FirstLineIndent ?? 0) / 20.0;
        double firstStart = hanging > 0 ? leftInd - hanging : leftInd + firstLine;

        List<(Run Run, string? Link)> runs = CollectRuns(ctx, p);
        var infos = new List<RunInfo>(runs.Count);
        var text = new System.Text.StringBuilder();
        var charRun = new List<int>();
        foreach ((Run run, string? link) in runs)
        {
            RunInfo info = ReadRun(ctx, run, link);
            if (info.Hidden)
            {
                // Hidden text takes no space (ECMA-376 Part 1 §17.3.2.41, vanish).
                continue;
            }

            int before = text.Length;
            AppendRunText(ctx, run, text, p);
            if (text.Length > before)
            {
                infos.Add(info);
                for (int i = before; i < text.Length; i++)
                {
                    charRun.Add(infos.Count - 1);
                }
            }
        }

        string paragraphText = text.ToString();
        fp.Text = paragraphText;

        var pieces = new List<Piece>();
        var atoms = new List<Atom>();

        // The list label comes first on the first line, followed by its suffix.
        ListLabel? label = ctx.LabelOf(p);
        Piece? labelPiece = null;
        if (label is not null)
        {
            labelPiece = BuildLabel(ctx, p, pp, label!, rtl);
            if (labelPiece is not null)
            {
                fp.Label = labelPiece.Text;
                pieces.Add(labelPiece);
                AddPieceAtoms(pieces, pieces.Count - 1, atoms, breakBefore: false, null);
                string suffix = label!.Suffix;
                if (suffix == "tab")
                {
                    var tabPiece = new Piece { Kind = AtomKind.Tab, Start = -1, Length = 0, Level = (byte)(rtl ? 1 : 0), Size = labelPiece.Size, IsLabel = true };
                    pieces.Add(tabPiece);
                    atoms.Add(new Atom { Piece = pieces.Count - 1, TextStart = -1, Kind = AtomKind.Tab, Level = tabPiece.Level });
                }
                else if (suffix == "space")
                {
                    Piece spacePiece = ShapeText(ctx, " ", labelPiece.Run!, labelPiece.Run!.Ascii, labelPiece.Size, (byte)(rtl ? 1 : 0), -1, labelPiece.Font, true);
                    pieces.Add(spacePiece);
                    AddPieceAtoms(pieces, pieces.Count - 1, atoms, breakBefore: false, null);
                }
            }
        }

        int labelAtoms = atoms.Count;
        BuildPieces(ctx, paragraphText, charRun, infos, rtl, pieces, atoms, p);

        TabStop[] stops = ReadTabs(pp, rtl);
        (double asc, double desc) mark = MarkMetrics(ctx, p, pp);
        AlignMode align = ReadAlignment(pp);
        string lineRule = spacing?.Attribute("lineRule") ?? "auto";
        double lineValue = (pp.LineSpacing ?? 240);
        bool wordWrap = pp.Flag("wordWrap") ?? true;

        // Indents in flow coordinates: distance from the leading edge of the text area.
        double limit = width - rightInd;
        ReadBorders(ctx, pp, fp, leftInd, rightInd, width, rtl);
        _ = labelAtoms;

        BreakLines(ctx, fp, pieces, atoms, stops, firstStart, leftInd, limit, width, rtl, align, lineRule, lineValue, mark, wordWrap, paragraphText);
        if (fp.Lines.Count > 0)
        {
            fp.Lines[^1].IsLast = true;
        }

        return fp;
    }

    // ECMA-376 Part 1 §17.3.1.33 (spacing): beforeAutospacing/afterAutospacing replace the fixed value with the automatic 14 pt Word uses
    // for paragraphs that came from HTML.
    private static double AutoSpace(ResolvedProperty? spacing, string autoName, string valueName)
    {
        if (spacing is null)
        {
            return 0;
        }

        if (spacing.Attribute(autoName) is "1" or "true" or "on")
        {
            return 14;
        }

        return (spacing.Number(valueName) ?? 0) / 20.0;
    }

    private static int HeadingLevel(Document doc, ResolvedParagraphProperties pp)
    {
        if (pp.OutlineLevel is { } lvl and >= 0 and <= 8)
        {
            return (int)Math.Min(lvl + 1, 6);
        }

        string? id = pp.StyleId;
        if (id is null)
        {
            return 0;
        }

        string name = doc.Styles.Find(id)?.Name ?? id;
        name = name.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (name.StartsWith("heading", StringComparison.OrdinalIgnoreCase) && name.Length == 8 && char.IsDigit(name[7]) && name[7] != '0')
        {
            return Math.Min(name[7] - '0', 6);
        }

        return 0;
    }

    private static AlignMode ReadAlignment(ResolvedParagraphProperties pp)
    {
        // ECMA-376 Part 1 §17.18.44 (ST_Jc): left/start, center, right/end, both and its relatives justify, distribute spreads every character.
        // Word reads left/right as start/end in right-to-left paragraphs, which flow coordinates give for free.
        return pp.Justification switch
        {
            "center" => AlignMode.Center,
            "right" or "end" => AlignMode.End,
            "both" or "mediumKashida" or "highKashida" or "lowKashida" or "thaiDistribute" => AlignMode.Justify,
            "distribute" => AlignMode.Distribute,
            _ => AlignMode.Start,
        };
    }

    private static TabStop[] ReadTabs(ResolvedParagraphProperties pp, bool rtl)
    {
        var list = new List<TabStop>();
        foreach ((string key, ResolvedProperty prop) in pp.Properties)
        {
            if (!key.StartsWith("tab/", StringComparison.Ordinal))
            {
                continue;
            }

            string val = prop.Attribute("val") ?? "left";
            if (val is "clear" or "bar")
            {
                continue;
            }

            if (prop.Number("pos") is not { } pos)
            {
                continue;
            }

            TabAlign a = val switch
            {
                "center" => TabAlign.Center,
                "right" or "end" => TabAlign.Right,
                "decimal" => TabAlign.Decimal,
                _ => TabAlign.Left,
            };
            _ = rtl;
            list.Add(new TabStop(pos / 20.0, a, prop.Attribute("leader") ?? "none"));
        }

        list.Sort(static (x, y) => x.Position.CompareTo(y.Position));
        return [.. list];
    }

    // The paragraph mark's size and font decide the height of an empty paragraph and take part in the last line's height
    // (ECMA-376 Part 1 §17.3.2.27 on the paragraph mark's run properties).
    private static (double Ascent, double Descent) MarkMetrics(LayoutContext ctx, Paragraph p, ResolvedParagraphProperties pp)
    {
        double size = 10;
        string family = "Times New Roman";
        bool bold = false;
        bool italic = false;
        StyleDefinition? style = ctx.Doc.Styles.Find(pp.StyleId) ?? ctx.Doc.Styles.Default(StyleType.Paragraph);
        if (style is not null)
        {
            ResolvedProperties rp = ctx.Doc.Styles.ResolveStyle(style, runProperties: true);
            if (rp.Number("sz") is { } sz)
            {
                size = sz / 2.0;
            }

            if (rp.Get("rFonts") is { } f)
            {
                string? fam = ctx.Doc.Theme.ResolveFont(f.Attribute("asciiTheme")) ?? f.Attribute("ascii");
                if (fam is not null)
                {
                    family = fam;
                }
            }

            bold = rp.Flag("b") ?? false;
            italic = rp.Flag("i") ?? false;
        }
        else if (ctx.Doc.Styles.DefaultRunProperties is { } d)
        {
            _ = d;
        }

        if (p.Properties?.Markup is { } pprMarkup && Xml.Child(pprMarkup, "rPr") is { } mark)
        {
            if (Xml.Child(mark, "sz") is { } s && Xml.Long(Xml.Attr(s, "val")) is { } hv)
            {
                size = hv / 2.0;
            }

            if (Xml.Child(mark, "b") is { } b)
            {
                bold = Xml.OnOff(b);
            }
        }

        FontMatch? m = ctx.ResolveFont(family, bold, italic);
        if (m is null)
        {
            return (size * 0.9, size * 0.25);
        }

        return Metrics(m.Face, size);
    }

    // Line height from the font's vertical metrics: ascender + line gap above the baseline, descender below. Word's choice between the
    // hhea and the OS/2 metrics is not settled here; the faces that matter (Calibri, Times New Roman, Arial and their open twins)
    // carry the same numbers in both.
    internal static (double Ascent, double Descent) Metrics(global::OneFont.FontFace face, double size)
    {
        global::OneFont.FontMetrics m = face.Metrics;
        double s = size / m.UnitsPerEm;
        return ((m.Ascender + m.LineGap) * s, Math.Abs(m.Descender) * s);
    }

    private static List<(Run, string?)> CollectRuns(LayoutContext ctx, Paragraph p)
    {
        var list = new List<(Run, string?)>();
        Walk(ctx, p.Inlines(), null, list, 0);
        return list;
    }

    private static void Walk(LayoutContext ctx, IEnumerable<InlineElement> inlines, string? link, List<(Run, string?)> acc, int depth)
    {
        if (depth > 32)
        {
            return;
        }

        foreach (InlineElement e in inlines)
        {
            switch (e)
            {
                case Run r:
                    acc.Add((r, link));
                    break;
                case Hyperlink h:
                    string? target = link;
                    if (h.RelationshipId is { } rid && ctx.Relationships.TryGetValue(rid, out Relationship? rel))
                    {
                        target = rel.Target;
                    }
                    else if (h.Anchor is { } anchor)
                    {
                        target = "#" + anchor;
                    }

                    Walk(ctx, h.Inlines(), target, acc, depth + 1);
                    break;
                case InlineContainer c:
                    Walk(ctx, c.Inlines(), link, acc, depth + 1);
                    break;
            }
        }
    }

    // The characters a run shows. ECMA-376 Part 1 §17.3.3: t is text, tab a tab, br a break (page and column breaks end the page),
    // noBreakHyphen and softHyphen their characters. Drawings and objects are not laid out yet and are reported once.
    private static void AppendRunText(LayoutContext ctx, Run run, System.Text.StringBuilder sb, Paragraph p)
    {
        for (MarkupNode n = run.Markup.Node.FirstChild; !n.IsNull; n = n.NextSibling)
        {
            if (n.Kind != MarkupNodeKind.Element)
            {
                continue;
            }

            var e = new MarkupRef(n);
            if (e.Namespace != SchemaNamespace.Wordprocessing)
            {
                continue;
            }

            if (Xml.Eq(e.LocalName, "t"))
            {
                sb.Append(n.InnerText);
            }
            else if (Xml.Eq(e.LocalName, "tab") || Xml.Eq(e.LocalName, "ptab"))
            {
                sb.Append('\t');
            }
            else if (Xml.Eq(e.LocalName, "br"))
            {
                string? type = Xml.Attr(e, "type");
                sb.Append(type is "page" or "column" ? '\f' : '\n');
            }
            else if (Xml.Eq(e.LocalName, "cr"))
            {
                sb.Append('\n');
            }
            else if (Xml.Eq(e.LocalName, "noBreakHyphen"))
            {
                sb.Append('\u2011');
            }
            else if (Xml.Eq(e.LocalName, "softHyphen"))
            {
                sb.Append('\u00AD');
            }
            else if (Xml.Eq(e.LocalName, "sym"))
            {
                if (Xml.Attr(e, "char") is { } hex && int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out int code))
                {
                    sb.Append(SymbolMap.Map(Xml.Attr(e, "font"), code));
                }
            }
            else if (Xml.Eq(e.LocalName, "drawing") || Xml.Eq(e.LocalName, "pict") || Xml.Eq(e.LocalName, "object"))
            {
                ctx.NoteOnce("layout.drawing-skipped", "layout.drawing-skipped", "Pictures and drawn objects in paragraphs are not laid out yet; their space is not reserved.", p);
            }
        }
    }

    private static RunInfo ReadRun(LayoutContext ctx, Run run, string? link)
    {
        ResolvedRunProperties rp = run.ResolvedProperties;
        var info = new RunInfo { Source = run, Link = link };
        double size = (rp.SizeHalfPoints ?? 20) / 2.0;
        info.Size = size;
        info.SizeCs = (rp.SizeComplexScriptHalfPoints ?? rp.SizeHalfPoints ?? 20) / 2.0;
        info.Bold = rp.Bold;
        info.BoldCs = rp.Get("bCs") is not null ? rp.BoldComplexScript : rp.Bold;
        info.Italic = rp.Italic;
        info.ItalicCs = rp.Get("iCs") is not null ? rp.ItalicComplexScript : rp.Italic;
        info.Caps = rp.Caps;
        info.SmallCaps = rp.SmallCaps && !rp.Caps;
        info.Hidden = rp.Hidden;
        ResolvedFonts f = rp.Fonts;
        info.Ascii = f.Ascii ?? "Times New Roman";
        info.HAnsi = f.HighAnsi ?? f.Ascii ?? "Times New Roman";
        info.EastAsia = f.EastAsia ?? info.HAnsi;
        info.Cs = f.ComplexScript ?? info.HAnsi;
        info.Spacing = (rp.Number("spacing") ?? 0) / 20.0;
        if (rp.Get("w") is { } w && w.Number("val") is { } pct and > 0)
        {
            info.Scale = pct / 100.0;
        }

        info.Position = (rp.Number("position") ?? 0) / 2.0;
        info.VertAlign = rp.VerticalAlignment switch
        {
            "superscript" => 1,
            "subscript" => -1,
            _ => 0,
        };

        // ECMA-376 Part 1 §17.3.2.19 (kern): kerning applies from the size in w:kern up; without the element Word does not kern.
        info.Kerning = rp.Number("kern") is { } k and > 0 && size * 2 >= k;
        info.Lang = rp.Get("lang")?.Attribute("val");

        RunPaintInfo paint = info.Paint;
        paint.Color = rp.Color is { Length: 6 } c && uint.TryParse(c, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out uint rgb) ? rgb : 0u;
        paint.Underline = rp.Underline is null or "none" ? null : rp.Underline;
        paint.Strike = rp.Strike;
        paint.DoubleStrike = rp.DoubleStrike;
        paint.Highlight = HighlightColor(rp.Highlight);
        if (rp.Get("shd") is { } shd && shd.Attribute("fill") is { Length: 6 } fill && uint.TryParse(fill, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out uint fillRgb))
        {
            paint.Shading = fillRgb;
        }

        paint.LinkTarget = link;
        paint.Language = info.Lang;
        paint.Scale = info.Scale;
        if (link is not null && paint.Underline is null && run.StyleId is "Hyperlink")
        {
            paint.Underline = "single";
        }

        return info;
    }

    // ECMA-376 Part 1 §17.18.40 (ST_HighlightColor).
    private static uint? HighlightColor(string? name) => name switch
    {
        "yellow" => 0xFFFF00,
        "green" => 0x00FF00,
        "cyan" => 0x00FFFF,
        "magenta" => 0xFF00FF,
        "blue" => 0x0000FF,
        "red" => 0xFF0000,
        "darkBlue" => 0x000080,
        "darkCyan" => 0x008080,
        "darkGreen" => 0x008000,
        "darkMagenta" => 0x800080,
        "darkRed" => 0x800000,
        "darkYellow" => 0x808000,
        "darkGray" => 0x808080,
        "lightGray" => 0xC0C0C0,
        "black" => 0x000000,
        "white" => 0xFFFFFF,
        _ => null,
    };

    private static void ReadBorders(LayoutContext ctx, ResolvedParagraphProperties pp, FormattedParagraph fp, double leftInd, double rightInd, double width, bool rtl)
    {
        _ = ctx;
        ParagraphPaintInfo paint = fp.Paint;
        paint.LeftEdge = rtl ? rightInd : leftInd;
        paint.RightEdge = width - (rtl ? leftInd : rightInd);
        if (pp.Get("shd") is { } shd && shd.Attribute("fill") is { Length: 6 } fill && uint.TryParse(fill, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out uint rgb))
        {
            paint.Shading = rgb;
        }

        paint.Top = Border(pp.Get("pBdr/top"));
        paint.Bottom = Border(pp.Get("pBdr/bottom"));
        paint.Left = Border(pp.Get(rtl ? "pBdr/right" : "pBdr/left"));
        paint.Right = Border(pp.Get(rtl ? "pBdr/left" : "pBdr/right"));
        fp.BorderTopHeight = paint.Top is { } t ? t.Width + t.Space : 0;
        fp.BorderBottomHeight = paint.Bottom is { } b ? b.Width + b.Space : 0;
    }

    // ECMA-376 Part 1 §17.3.1.24 (pBdr): sz is in eighths of a point, space in points.
    private static BorderInfo? Border(ResolvedProperty? b)
    {
        if (b is null || b.Attribute("val") is null or "nil" or "none")
        {
            return null;
        }

        double w = (b.Number("sz") ?? 4) / 8.0;
        double space = b.Number("space") ?? 0;
        uint color = b.Attribute("color") is { Length: 6 } c && uint.TryParse(c, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out uint v) ? v : 0u;
        return new BorderInfo(w, space, color, b.Attribute("val") ?? "single");
    }

    private static Piece? BuildLabel(LayoutContext ctx, Paragraph p, ResolvedParagraphProperties pp, ListLabel label, bool rtl)
    {
        _ = p;
        string labelText = label.Text;
        if (labelText.Length == 0)
        {
            return null;
        }

        // The label takes the paragraph mark's character formatting with the level's run properties on top.
        (string family, double size, bool bold, bool italic, uint color) = LabelFormat(ctx, pp, label);
        labelText = SymbolMap.Map(family, labelText, out string? replacement);
        if (replacement is not null)
        {
            family = replacement;
        }

        var info = new RunInfo { Size = size, SizeCs = size, Bold = bold, Italic = italic, Ascii = family, HAnsi = family, EastAsia = family, Cs = family };
        info.Paint.Color = color;
        Piece piece = ShapeText(ctx, labelText, info, family, size, (byte)(rtl ? 1 : 0), -1, null, true);
        piece.Text = labelText;
        return piece;
    }

    private static (string Family, double Size, bool Bold, bool Italic, uint Color) LabelFormat(LayoutContext ctx, ResolvedParagraphProperties pp, ListLabel label)
    {
        double size = 10;
        string family = "Times New Roman";
        bool bold = false;
        bool italic = false;
        uint color = 0;
        StyleDefinition? style = ctx.Doc.Styles.Find(pp.StyleId) ?? ctx.Doc.Styles.Default(StyleType.Paragraph);
        if (style is not null)
        {
            ResolvedProperties rp = ctx.Doc.Styles.ResolveStyle(style, runProperties: true);
            ApplyLabelProps(ctx, rp.Get, ref size, ref family, ref bold, ref italic, ref color);
        }

        if (label.Definition.RunProperties is { } lvlRpr)
        {
            foreach (MarkupRef e in Xml.Elements(lvlRpr))
            {
                string? val = Xml.Attr(e, "val");
                if (Xml.Eq(e.LocalName, "sz") && Xml.Long(val) is { } hv)
                {
                    size = hv / 2.0;
                }
                else if (Xml.Eq(e.LocalName, "b"))
                {
                    bold = Xml.OnOff(e);
                }
                else if (Xml.Eq(e.LocalName, "i"))
                {
                    italic = Xml.OnOff(e);
                }
                else if (Xml.Eq(e.LocalName, "color") && val is { Length: 6 } && uint.TryParse(val, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out uint c))
                {
                    color = c;
                }
                else if (Xml.Eq(e.LocalName, "rFonts"))
                {
                    string? fam = ctx.Doc.Theme.ResolveFont(Xml.Attr(e, "asciiTheme")) ?? Xml.Attr(e, "ascii") ?? Xml.Attr(e, "hAnsi");
                    if (fam is not null)
                    {
                        family = fam;
                    }
                }
            }
        }

        return (family, size, bold, italic, color);
    }

    private static void ApplyLabelProps(LayoutContext ctx, Func<string, ResolvedProperty?> get, ref double size, ref string family, ref bool bold, ref bool italic, ref uint color)
    {
        if (get("sz") is { } sz && sz.Number("val") is { } hv)
        {
            size = hv / 2.0;
        }

        if (get("rFonts") is { } f)
        {
            string? fam = ctx.Doc.Theme.ResolveFont(f.Attribute("asciiTheme")) ?? f.Attribute("ascii");
            if (fam is not null)
            {
                family = fam;
            }
        }

        if (get("b") is { } b)
        {
            bold = b.IsOn;
        }

        if (get("i") is { } i)
        {
            italic = i.IsOn;
        }

        if (get("color") is { } c && c.Attribute("val") is { Length: 6 } v && uint.TryParse(v, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out uint rgb))
        {
            color = rgb;
        }
    }

    // Character classes decide which of the four fonts a character uses. ECMA-376 Part 1 §17.3.2.26 (rFonts): ascii for Latin, hAnsi
    // for other Latin-script and symbols, eastAsia for CJK, cs for complex scripts.
    private enum Category
    {
        Latin,
        HighAnsi,
        EastAsia,
        ComplexScript,
    }

    private static Category Classify(int cp)
    {
        if (cp < 0x80)
        {
            return Category.Latin;
        }

        if ((cp is >= 0x0590 and <= 0x08FF) || (cp is >= 0x0900 and <= 0x0FFF) || (cp is >= 0x1000 and <= 0x109F) || (cp is >= 0x1780 and <= 0x17FF) || (cp is >= 0xFB1D and <= 0xFDFF) || (cp is >= 0xFE70 and <= 0xFEFF))
        {
            return Category.ComplexScript;
        }

        if ((cp is >= 0x1100 and <= 0x11FF) || (cp is >= 0x2E80 and <= 0x9FFF) || (cp is >= 0xAC00 and <= 0xD7AF) || (cp is >= 0xF900 and <= 0xFAFF) || (cp is >= 0xFF00 and <= 0xFFEF) || (cp is >= 0x20000 and <= 0x2FFFF) || (cp is >= 0x3000 and <= 0x303F))
        {
            return Category.EastAsia;
        }

        return Category.HighAnsi;
    }

    private static void BuildPieces(LayoutContext ctx, string text, List<int> charRun, List<RunInfo> infos, bool rtl, List<Piece> pieces, List<Atom> atoms, Paragraph p)
    {
        if (text.Length == 0)
        {
            return;
        }

        byte paragraphLevel = (byte)(rtl ? 1 : 0);
        byte[] levels = global::OneFont.TextShaping.GetBidiLevels(text, rtl ? global::OneFont.TextDirection.RightToLeft : global::OneFont.TextDirection.LeftToRight, out _);
        global::OneFont.LineBreakOpportunity[] opps = global::OneFont.TextSegmentation.GetLineBreakOpportunities(text);

        int i = 0;
        while (i < text.Length)
        {
            char ch = text[i];
            int runIndex = charRun[i];
            RunInfo info = infos[runIndex];
            if (ch is '\t' or '\n' or '\f' or '\u00AD')
            {
                AtomKind kind = ch switch
                {
                    '\t' => AtomKind.Tab,
                    '\n' => AtomKind.LineBreak,
                    '\f' => AtomKind.PageBreak,
                    _ => AtomKind.Soft,
                };
                var special = new Piece { Kind = kind, Run = info, Start = i, Length = 1, Level = paragraphLevel, Size = info.Size, Text = ch.ToString() };
                FontMatch? fm = ctx.ResolveFont(info.Ascii, info.Bold, info.Italic);
                special.Font = fm;
                special.Size = info.Size;
                pieces.Add(special);
                atoms.Add(new Atom { Piece = pieces.Count - 1, TextStart = i, TextLength = 1, Kind = kind, Level = paragraphLevel, BreakBefore = opps[i] != global::OneFont.LineBreakOpportunity.None, Width = 0 });
                i++;
                continue;
            }

            // Extend over characters with the same run, bidi level, font category and small-caps state.
            int cp = char.ConvertToUtf32(text, i);
            Category cat = Classify(cp);
            byte level = levels[i];
            bool lowered = info.SmallCaps && char.IsLower(ch);
            int j = i + char.ConvertFromUtf32(cp).Length;
            while (j < text.Length && charRun[j] == runIndex && levels[j] == level)
            {
                char cj = text[j];
                if (cj is '\t' or '\n' or '\f' or '\u00AD')
                {
                    break;
                }

                int cpj = char.ConvertToUtf32(text, j);
                bool lowJ = info.SmallCaps && char.IsLower(cj);
                if (Classify(cpj) != cat && !IsNeutral(cpj) || lowJ != lowered)
                {
                    break;
                }

                j += char.IsHighSurrogate(cj) ? 2 : 1;
            }

            string segment = text[i..j];
            bool bold = cat == Category.ComplexScript ? info.BoldCs : info.Bold;
            bool italic = cat == Category.ComplexScript ? info.ItalicCs : info.Italic;
            double size = cat == Category.ComplexScript ? info.SizeCs : info.Size;
            string family = cat switch
            {
                Category.Latin => info.Ascii,
                Category.HighAnsi => info.HAnsi,
                Category.EastAsia => info.EastAsia,
                _ => info.Cs,
            };
            if (info.VertAlign != 0)
            {
                size *= ScriptScale;
            }

            if (lowered)
            {
                size *= SmallCapsScale;
            }

            string shown = info.Caps || lowered ? ToUpperKeepLength(segment) : segment;
            Piece piece = ShapeText(ctx, shown, info, family, size, level, i, null, false);
            piece.Text = segment;
            piece.Shown = ReferenceEquals(shown, segment) ? null : shown;
            piece.Length = segment.Length;
            pieces.Add(piece);
            AddPieceAtoms(pieces, pieces.Count - 1, atoms, true, opps);
            i = j;
        }

        _ = p;
        _ = paragraphLevel;
    }

    private static bool IsNeutral(int cp) => cp is ' ' or '.' or ',' or ':' or ';' or '(' or ')' or '-' or '\u00A0' or '"' or '\'' || (cp is >= 0x2000 and <= 0x206F);

    private static string ToUpperKeepLength(string s)
    {
        var chars = s.ToCharArray();
        for (int k = 0; k < chars.Length; k++)
        {
            chars[k] = char.ToUpperInvariant(chars[k]);
        }

        return new string(chars);
    }

    // Shapes a stretch of text with one font (plus script fallbacks for characters it lacks) and stores clusters and glyphs on the piece.
    private static Piece ShapeText(LayoutContext ctx, string shown, RunInfo info, string family, double size, byte level, int start, FontMatch? preset, bool isLabel)
    {
        FontMatch? match = preset ?? ctx.ResolveFont(family, info.Bold, info.Italic);
        var piece = new Piece { Kind = AtomKind.Glyphs, Run = info, Font = match, Size = size, Level = level, Start = start, Length = shown.Length, Text = shown, IsLabel = isLabel };
        if (match is null || shown.Length == 0)
        {
            return piece;
        }

        var faces = new List<global::OneFont.FontFace> { match.Face };
        for (int k = 0; k < shown.Length; k++)
        {
            int cp = char.ConvertToUtf32(shown, k);
            if (char.IsHighSurrogate(shown[k]))
            {
                k++;
            }

            if (cp <= 0x20 || match.Face.HasGlyph(cp))
            {
                continue;
            }

            global::OneFont.FontFace? fb = null;
            foreach (global::OneFont.FontFace f in faces)
            {
                if (f.HasGlyph(cp))
                {
                    fb = f;
                    break;
                }
            }

            fb ??= ctx.Fonts.FindFallback(cp, info.Bold, info.Italic);
            if (fb is not null && !faces.Contains(fb) && faces.Count < global::OneFont.FontFallbackChain.MaxFaces)
            {
                faces.Add(fb);
            }
        }

        piece.Faces = [.. faces];
        var options = new global::OneFont.ShapingOptions { Kerning = info.Kerning ? global::OneFont.KerningMode.Normal : global::OneFont.KerningMode.None, Language = info.Lang };
        // Word applies ligatures only when asked to (w14:ligatures); the default is plain letters.
        options.Features.Add(global::OneFont.FontFeature.Off("liga"));
        options.Features.Add(global::OneFont.FontFeature.Off("clig"));
        bool pieceRtl = (level & 1) == 1;
        global::OneFont.ShapedRun run = ctx.Scratch;
        if (faces.Count == 1)
        {
            global::OneFont.TextShaping.ShapeRun(shown, match.Face, run, pieceRtl, options);
        }
        else
        {
            new global::OneFont.FontFallbackChain(faces).ShapeRun(shown, run, pieceRtl, options);
        }

        int n = run.ClusterCount;
        piece.ClusterCount = n;
        piece.CStart = new int[n];
        piece.CLength = new int[n];
        piece.CFace = new int[n];
        piece.CGlyphStart = new int[n];
        piece.CGlyphCount = new int[n];
        piece.CAdv = new double[n];
        ReadOnlySpan<global::OneFont.ShapedClusterRange> clusters = run.Clusters;
        for (int c = 0; c < n; c++)
        {
            global::OneFont.ShapedClusterRange cr = clusters[c];
            piece.CStart[c] = cr.Start;
            piece.CLength[c] = cr.Length;
            piece.CFace[c] = cr.FaceIndex;
            piece.CGlyphStart[c] = cr.GlyphStart;
            piece.CGlyphCount[c] = cr.GlyphCount;
            piece.CAdv[c] = (double)cr.Advance * size / faces[cr.FaceIndex].UnitsPerEm;
        }

        int g = run.GlyphCount;
        piece.GlyphId = new int[g];
        piece.GXAdv = new int[g];
        piece.GXOff = new int[g];
        piece.GYOff = new int[g];
        ReadOnlySpan<global::OneFont.ShapedGlyph> glyphs = run.Glyphs;
        for (int k = 0; k < g; k++)
        {
            piece.GlyphId[k] = glyphs[k].GlyphId;
            piece.GXAdv[k] = glyphs[k].XAdvance;
            piece.GXOff[k] = glyphs[k].XOffset;
            piece.GYOff[k] = glyphs[k].YOffset;
        }

        return piece;
    }

    private static void AddPieceAtoms(List<Piece> pieces, int pieceIndex, List<Atom> atoms, bool breakBefore, global::OneFont.LineBreakOpportunity[]? opps)
    {
        Piece piece = pieces[pieceIndex];
        RunInfo info = piece.Run!;
        for (int c = 0; c < piece.ClusterCount; c++)
        {
            int textStart = piece.Start < 0 ? -1 : piece.Start + piece.CStart[c];
            bool isSpace = piece.Text.Length > piece.CStart[c] && piece.Text[piece.CStart[c]] == ' ';
            double w = (piece.CAdv[c] * info.Scale) + info.Spacing;
            bool opp = breakBefore && opps is not null && textStart >= 0 && opps[textStart] != global::OneFont.LineBreakOpportunity.None;
            atoms.Add(new Atom
            {
                Piece = pieceIndex,
                Cluster = c,
                TextStart = textStart,
                TextLength = piece.CLength[c],
                Width = w,
                Level = piece.Level,
                BreakBefore = opp,
                Kind = isSpace ? AtomKind.Space : AtomKind.Glyphs,
            });
        }
    }

    private static void BreakLines(
        LayoutContext ctx, FormattedParagraph fp, List<Piece> pieces, List<Atom> atoms, TabStop[] stops, double firstStart, double leftInd, double limit, double width,
        bool rtl, AlignMode align, string lineRule, double lineValue, (double Ascent, double Descent) mark, bool wordWrap, string paragraphText)
    {
        int n = atoms.Count;
        int start = 0;
        bool firstLine = true;
        double defaultTab = ctx.Compat.DefaultTabStop;
        do
        {
            double lineStartLx = firstLine ? firstStart : leftInd;
            int end = FindLineEnd(atoms, pieces, start, lineStartLx, limit, stops, leftInd, defaultTab, wordWrap, ctx.Compat, out bool hardBreak, out bool pageBreak);
            if (end <= start)
            {
                end = Math.Min(start + 1, n);
            }

            var line = new FormattedLine { EndsWithPageBreak = pageBreak };
            BuildLine(ctx, fp, line, pieces, atoms, start, end, lineStartLx, limit, stops, leftInd, defaultTab, rtl, align, hardBreak, end >= n, width, firstLine, lineRule, lineValue, mark, paragraphText);
            fp.Lines.Add(line);
            start = end;
            firstLine = false;
        }
        while (start < n);
    }

    // Walks atoms from `start`, tracking the position along the flow, and returns where the line ends.
    private static int FindLineEnd(
        List<Atom> atoms, List<Piece> pieces, int start, double startLx, double limit, TabStop[] stops, double leftInd, double defaultTab, bool wordWrap,
        CompatSettings compat, out bool hardBreak, out bool pageBreak)
    {
        _ = compat;
        hardBreak = false;
        pageBreak = false;
        int n = atoms.Count;
        if (n == 0)
        {
            return 0;
        }

        double x = startLx;
        int lastOpp = -1;
        for (int i = start; i < n; i++)
        {
            Atom a = atoms[i];
            if (i > start && (a.BreakBefore || (!wordWrap && a.Kind == AtomKind.Glyphs)) && atoms[i - 1].Kind != AtomKind.Soft)
            {
                lastOpp = i;
            }
            else if (i > start && atoms[i - 1].Kind == AtomKind.Soft)
            {
                lastOpp = i;
            }

            if (a.Kind == AtomKind.LineBreak)
            {
                hardBreak = true;
                return i + 1;
            }

            if (a.Kind == AtomKind.PageBreak)
            {
                hardBreak = true;
                pageBreak = true;
                return i + 1;
            }

            double w;
            if (a.Kind == AtomKind.Tab)
            {
                w = TabWidth(atoms, pieces, i, x, stops, leftInd, defaultTab, limit);
            }
            else
            {
                w = a.Width;
            }

            double endX = x + w;
            bool hangs = a.Kind == AtomKind.Space || a.Kind == AtomKind.Tab && endX >= limit - 0.001 || a.Kind == AtomKind.Soft;
            if (!hangs && endX > limit + 0.001 && i > start)
            {
                return lastOpp > start ? lastOpp : i;
            }

            x = endX;
        }

        return n;
    }

    // ECMA-376 Part 1 §17.3.1.37 (tabs) and §17.3.3.32 (tab): the next stop after the current position; default stops fall every
    // defaultTabStop beyond the last custom stop; a hanging indent adds an implicit stop at the left indent.
    private static double TabWidth(List<Atom> atoms, List<Piece> pieces, int index, double x, TabStop[] stops, double leftInd, double defaultTab, double limit) =>
        TabWidth(atoms, pieces, index, x, stops, leftInd, defaultTab, limit, out _);

    private static double TabWidth(List<Atom> atoms, List<Piece> pieces, int index, double x, TabStop[] stops, double leftInd, double defaultTab, double limit, out string leader)
    {
        (double pos, TabAlign align, string stopLeader) = NextStop(x, stops, leftInd, defaultTab, limit);
        leader = stopLeader;
        double target = pos;
        if (align != TabAlign.Left)
        {
            double content = 0;
            double decimalOffset = -1;
            for (int j = index + 1; j < atoms.Count; j++)
            {
                Atom b = atoms[j];
                if (b.Kind is AtomKind.Tab or AtomKind.LineBreak or AtomKind.PageBreak)
                {
                    break;
                }

                if (align == TabAlign.Decimal && decimalOffset < 0 && b.TextStart >= 0)
                {
                    Piece pc = pieces[b.Piece];
                    int rel = b.TextStart - pc.Start;
                    if (rel >= 0 && rel < pc.Text.Length && pc.Text[rel] is '.' or ',')
                    {
                        decimalOffset = content;
                    }
                }

                content += b.Width;
            }

            double used = align switch
            {
                TabAlign.Center => content / 2,
                TabAlign.Decimal => decimalOffset >= 0 ? decimalOffset : content,
                _ => content,
            };
            target = Math.Max(x, pos - used);
        }

        return Math.Max(0, target - x);
    }

    private static (double Position, TabAlign Align, string Leader) NextStop(double x, TabStop[] stops, double leftInd, double defaultTab, double limit)
    {
        double implicitStop = double.NaN;
        // The hanging indent's implicit stop only applies when the text starts left of it.
        if (leftInd > x + 0.001)
        {
            implicitStop = leftInd;
        }

        double best = double.PositiveInfinity;
        TabAlign bestAlign = TabAlign.Left;
        string bestLeader = string.Empty;
        foreach (TabStop s in stops)
        {
            if (s.Position > x + 0.001)
            {
                best = s.Position;
                bestAlign = s.Align;
                bestLeader = s.Leader is "none" ? string.Empty : s.Leader;
                break;
            }
        }

        if (!double.IsNaN(implicitStop) && implicitStop < best)
        {
            return (implicitStop, TabAlign.Left, string.Empty);
        }

        if (!double.IsInfinity(best))
        {
            return (best, bestAlign, bestLeader);
        }

        // Default stops: multiples of defaultTab beyond the last custom stop.
        double lastCustom = stops.Length > 0 ? stops[^1].Position : double.NegativeInfinity;
        double next = (Math.Floor((x + 0.001) / defaultTab) + 1) * defaultTab;
        while (next <= lastCustom)
        {
            next += defaultTab;
        }

        _ = limit;
        return (next, TabAlign.Left, string.Empty);
    }

    private static void BuildLine(
        LayoutContext ctx, FormattedParagraph fp, FormattedLine line, List<Piece> pieces, List<Atom> atoms, int start, int end, double startLx, double limit, TabStop[] stops,
        double leftInd, double defaultTab, bool rtl, AlignMode align, bool hardBreak, bool lastOfParagraph, double width, bool firstLine, string lineRule, double lineValue,
        (double Ascent, double Descent) mark, string paragraphText)
    {
        int count = end - start;
        // Positions along the flow for each atom, in logical order, and the width the line's content takes.
        var widths = new double[count];
        var leaders = new string[count];
        double x = startLx;
        int lastVisible = -1;
        for (int k = 0; k < count; k++)
        {
            Atom a = atoms[start + k];
            string leader = string.Empty;
            double w = a.Kind == AtomKind.Tab ? TabWidth(atoms, pieces, start + k, x, stops, leftInd, defaultTab, limit, out leader) : a.Width;
            widths[k] = w;
            leaders[k] = leader;
            x += w;
            if (a.Kind is AtomKind.Glyphs or AtomKind.Tab)
            {
                lastVisible = k;
            }
        }

        // Trailing spaces hang: they take no part in alignment.
        double trailing = 0;
        for (int k = count - 1; k > lastVisible; k--)
        {
            if (atoms[start + k].Kind == AtomKind.Space)
            {
                trailing += widths[k];
            }
        }

        double contentEnd = x - trailing;
        double avail = limit - startLx;
        double shift = 0;
        double extraPerGap = 0;
        bool lineEndsWithBreak = hardBreak;
        bool justify = align == AlignMode.Justify && !(lastOfParagraph && !lineEndsWithBreak) && !(lineEndsWithBreak && ctx.Compat.DoNotExpandShiftReturn);
        if (align == AlignMode.Distribute)
        {
            justify = true;
        }

        switch (align)
        {
            case AlignMode.Center:
                shift = Math.Max(0, (avail - (contentEnd - startLx)) / 2);
                break;
            case AlignMode.End:
                shift = Math.Max(0, avail - (contentEnd - startLx));
                break;
        }

        if (justify && !(align == AlignMode.Justify && lineEndsWithBreak && lastOfParagraph))
        {
            int gaps = 0;
            for (int k = 0; k <= lastVisible; k++)
            {
                if (atoms[start + k].Kind == AtomKind.Space || (align == AlignMode.Distribute && atoms[start + k].Kind == AtomKind.Glyphs && k < lastVisible))
                {
                    gaps++;
                }
            }

            if (gaps > 0)
            {
                double extra = avail - (contentEnd - startLx);
                if (extra > 0)
                {
                    extraPerGap = extra / gaps;
                }
            }
        }

        // Bidi: reorder atoms into visual order (UAX #9 rule L2) by their levels.
        var levels = new byte[count];
        for (int k = 0; k < count; k++)
        {
            levels[k] = atoms[start + k].Kind is AtomKind.Tab or AtomKind.LineBreak or AtomKind.PageBreak ? (byte)(rtl ? 1 : 0) : atoms[start + k].Level;
        }

        int[] order = global::OneFont.TextShaping.GetVisualOrder(levels);
        var visualWidth = new double[count];
        double totalWidth = 0;
        for (int v = 0; v < count; v++)
        {
            int k = order[v];
            double w = widths[k];
            Atom a = atoms[start + k];
            if (extraPerGap > 0 && k <= lastVisible && (a.Kind == AtomKind.Space || (align == AlignMode.Distribute && a.Kind == AtomKind.Glyphs && k < lastVisible)))
            {
                w += extraPerGap;
            }

            visualWidth[v] = w;
            totalWidth += w;
        }

        // Visual left edge of the line. Flow coordinates run from the leading edge, which is the right edge in a right-to-left paragraph.
        double flowStart = startLx + shift;
        double leftX = rtl ? width - flowStart - totalWidth : flowStart;

        // Metrics: tallest ascent and deepest descent among the line's pieces and, on the last line, the paragraph mark.
        double asc = 0;
        double desc = 0;
        for (int k = 0; k < count; k++)
        {
            Atom a = atoms[start + k];
            Piece pc = pieces[a.Piece];
            if (pc.Font is null && pc.Faces.Length == 0)
            {
                continue;
            }

            (double ascent, double descent) = Metrics((pc.Faces.Length > 0 ? pc.Faces[0] : pc.Font!.Face), pc.Size);
            if (pc.Run is { VertAlign: 1 } || pc.Run is { Position: > 0 })
            {
                ascent += pc.Run.Position + (pc.Run.VertAlign == 1 ? pc.Size * 0.5 : 0);
            }

            asc = Math.Max(asc, ascent);
            desc = Math.Max(desc, descent);
        }

        if (lastOfParagraph || count == 0)
        {
            asc = Math.Max(asc, mark.Ascent);
            desc = Math.Max(desc, mark.Descent);
        }

        if (asc == 0 && desc == 0)
        {
            asc = mark.Ascent;
            desc = mark.Descent;
        }

        // ECMA-376 Part 1 §17.3.1.33 (spacing): auto is a multiple of the single height in 240ths; exact fixes the height; atLeast sets a floor.
        double natural = asc + desc;
        double lineHeight = natural;
        switch (lineRule)
        {
            case "exact":
                lineHeight = lineValue / 20.0;
                desc = natural > 0 ? desc * (lineHeight / natural) : 0;
                break;
            case "atLeast":
                lineHeight = Math.Max(natural, lineValue / 20.0);
                break;
            default:
                lineHeight = natural * (lineValue / 240.0);
                break;
        }

        line.Descent = desc;
        line.Ascent = lineHeight - desc;

        // Text range of the line and its string.
        int textStart = -1;
        int textEnd = -1;
        for (int k = 0; k < count; k++)
        {
            Atom a = atoms[start + k];
            if (a.TextStart < 0)
            {
                continue;
            }

            if (textStart < 0)
            {
                textStart = a.TextStart;
            }

            textEnd = a.TextStart + a.TextLength;
        }

        line.TextStart = Math.Max(textStart, 0);
        line.TextLength = textStart < 0 ? 0 : textEnd - textStart;
        line.Text = textStart < 0 ? string.Empty : paragraphText.Substring(textStart, line.TextLength);

        // Items: join visually consecutive atoms of one piece and face into one run.
        double cx = leftX;
        int v0 = 0;
        while (v0 < count)
        {
            int k0 = order[v0];
            Atom first = atoms[start + k0];
            Piece pc = pieces[first.Piece];
            if (first.Kind is AtomKind.LineBreak or AtomKind.PageBreak or AtomKind.Soft)
            {
                cx += visualWidth[v0];
                v0++;
                continue;
            }

            if (first.Kind == AtomKind.Tab)
            {
                line.Items.Add(new LineItem { Kind = LayoutRunKind.Tab, X = cx, Width = visualWidth[v0], Text = "\t", TextStart = first.TextStart, Source = pc.Run?.Source, Size = pc.Size, Level = first.Level, Paint = WithLeader(pc.Run?.Paint, leaders[k0]) });
                cx += visualWidth[v0];
                v0++;
                continue;
            }

            int face = pc.CFace.Length > first.Cluster ? pc.CFace[first.Cluster] : 0;
            int v1 = v0 + 1;
            while (v1 < count)
            {
                Atom b = atoms[start + order[v1]];
                if (b.Piece != first.Piece || b.Kind is AtomKind.Tab or AtomKind.LineBreak or AtomKind.PageBreak or AtomKind.Soft || pc.CFace[b.Cluster] != face)
                {
                    break;
                }

                v1++;
            }

            line.Items.Add(MakeRunItem(pc, atoms, start, order, v0, v1, visualWidth, cx, face));
            for (int v = v0; v < v1; v++)
            {
                cx += visualWidth[v];
            }

            v0 = v1;
        }

        _ = firstLine;
    }

    private static RunPaintInfo WithLeader(RunPaintInfo? source, string leader)
    {
        RunPaintInfo p = source is null ? new RunPaintInfo() : ClonePaint(source);
        p.Leader = leader;
        return p;
    }

    private static LineItem MakeRunItem(Piece pc, List<Atom> atoms, int start, int[] order, int v0, int v1, double[] visualWidth, double x, int faceIndex)
    {
        RunInfo info = pc.Run!;
        double size = pc.Size;
        int count = v1 - v0;
        bool rtl = (pc.Level & 1) == 1;
        int minText = int.MaxValue;
        int maxText = 0;
        double total = 0;
        for (int v = v0; v < v1; v++)
        {
            Atom a = atoms[start + order[v]];
            if (a.TextStart >= 0)
            {
                minText = Math.Min(minText, a.TextStart);
                maxText = Math.Max(maxText, a.TextStart + a.TextLength);
            }

            total += visualWidth[v];
        }

        int pieceBase = pc.Start < 0 ? 0 : pc.Start;
        int localMin = pc.Start < 0 ? 0 : minText - pieceBase;
        int localMax = pc.Start < 0 ? pc.Text.Length : maxText - pieceBase;
        string text = pc.Text.Substring(localMin, localMax - localMin);
        global::OneFont.FontFace face = pc.Faces[faceIndex];
        double unit = size / face.UnitsPerEm;

        var glyphIds = new List<int>();
        var gx = new List<float>();
        var gy = new List<float>();
        var gcl = new List<int>();
        var clusterStarts = new List<int>(count + 1);
        var clusterX = new List<double>(count + 1);
        double pen = 0;
        double scale = info.Scale;
        double shiftY = info.Position + (info.VertAlign == 1 ? pc.Size * 0.5 : info.VertAlign == -1 ? -pc.Size * 0.25 : 0);
        for (int v = v0; v < v1; v++)
        {
            Atom a = atoms[start + order[v]];
            int c = a.Cluster;
            int clusterStartInText = pc.CStart[c] - localMin;
            clusterStarts.Add(Math.Max(0, clusterStartInText));
            clusterX.Add(x + (pen * scale));
            double clusterPen = pen;
            // OneFont lists a cluster's glyphs in logical order; in a right-to-left run they draw in the opposite order, and a mark's
            // offset is relative to the pen after the glyphs drawn before it, so the glyphs must be walked in drawing order.
            bool runRtl = (pc.Level & 1) == 1;
            for (int g = 0; g < pc.CGlyphCount[c]; g++)
            {
                int gi = pc.CGlyphStart[c] + (runRtl ? pc.CGlyphCount[c] - 1 - g : g);
                glyphIds.Add(pc.GlyphId[gi]);
                gx.Add((float)(clusterPen + (pc.GXOff[gi] * unit)));
                gy.Add((float)(-pc.GYOff[gi] * unit));
                gcl.Add(Math.Max(0, clusterStartInText));
                clusterPen += pc.GXAdv[gi] * unit;
            }

            // The cluster's own advance includes kerning with its neighbour; spacing is added after each character.
            pen += pc.CAdv[c] + (info.Spacing / (scale == 0 ? 1 : scale));
            // Justification widened this cluster beyond its own advance; the extra goes after the glyphs.
            pen += Math.Max(0, visualWidth[v] - a.Width) / (scale == 0 ? 1 : scale);
        }

        clusterStarts.Add(text.Length);
        clusterX.Add(x + total);
        var item = new LineItem
        {
            Kind = pc.IsLabel ? LayoutRunKind.Label : LayoutRunKind.Text,
            X = x,
            Width = total,
            Text = text,
            TextStart = pc.IsLabel ? -1 : minText,
            Source = pc.IsLabel ? null : info.Source,
            Font = pc.Font is null ? null : pc.Font with { Face = face, ActualFamily = face.FamilyName },
            Size = size,
            Level = pc.Level,
            Paint = info.Paint,
            ClusterStarts = [.. clusterStarts],
            ClusterX = [.. clusterX],
            Glyphs = new GlyphData { Face = face, GlyphIds = [.. glyphIds], X = [.. gx], Y = [.. gy], Clusters = [.. gcl], RightToLeft = rtl, ShownText = pc.Shown?.Substring(localMin, localMax - localMin) },
        };
        if (shiftY != 0)
        {
            item.Paint = ClonePaint(info.Paint);
            item.Paint.BaselineShift = shiftY;
        }

        return item;
    }

    private static RunPaintInfo ClonePaint(RunPaintInfo p) => new()
    {
        Color = p.Color,
        Underline = p.Underline,
        UnderlineColor = p.UnderlineColor,
        Strike = p.Strike,
        DoubleStrike = p.DoubleStrike,
        Highlight = p.Highlight,
        Shading = p.Shading,
        LinkTarget = p.LinkTarget,
        Language = p.Language,
        BaselineShift = p.BaselineShift,
        Scale = p.Scale,
        IsArtifact = p.IsArtifact,
        Leader = p.Leader,
    };
}

// Maps the private-use code points that symbol fonts use (U+F0xx) to Unicode characters the open fonts carry.
internal static class SymbolMap
{
    public static string Map(string? font, int code)
    {
        string s = char.ConvertFromUtf32(code is >= 0 and <= 0x10FFFF and not (>= 0xD800 and <= 0xDFFF) ? code : 0xFFFD);
        return Map(font, s, out _);
    }

    public static string Map(string? font, string text, out string? replacementFamily)
    {
        replacementFamily = null;
        if (text.Length != 1 || text[0] is < '\uF020' or > '\uF0FF')
        {
            return text;
        }

        bool symbol = string.Equals(font, "Symbol", StringComparison.OrdinalIgnoreCase);
        bool wingdings = font is not null && font.StartsWith("Wingdings", StringComparison.OrdinalIgnoreCase);
        if (!symbol && !wingdings)
        {
            return text;
        }

        char low = (char)(text[0] & 0xFF);
        char? mapped = symbol ? SymbolFont(low) : WingdingsFont(low);
        if (mapped is null)
        {
            return text;
        }

        replacementFamily = "Noto Sans Symbols 2";
        return mapped.Value.ToString();
    }

    // The bullets Word's list gallery uses; the rest of the symbol fonts stay unmapped (their glyphs show as missing).
    private static char? SymbolFont(char c) => c switch
    {
        '\u00B7' => '\u2022',
        '\u00A8' => '\u2666',
        '\u00D8' => '\u21D2',
        '\u00B0' => '\u00B0',
        _ => null,
    };

    private static char? WingdingsFont(char c) => c switch
    {
        '\u00A7' => '\u25AA',
        '\u00D8' => '\u27A2',
        '\u00FC' => '\u2713',
        '\u0076' => '\u2756',
        '\u006F' => '\u25A1',
        '\u00A8' => '\u25FB',
        '\u006C' => '\u25CF',
        '\u00E8' => '\u2192',
        '\u0071' => '\u2751',
        _ => null,
    };
}
