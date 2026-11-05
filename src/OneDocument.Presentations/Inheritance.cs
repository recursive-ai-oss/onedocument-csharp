using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Schema;

namespace RecursiveAI.OneDocument.Presentations;

public sealed partial class Shape
{
    /// <summary>
    /// Gets the placeholders this shape inherits from, nearest first: for a slide's placeholder the matching layout placeholder, then
    /// the matching master placeholder; for a layout's, the master's. Empty for a shape that is not a placeholder or finds no match.
    /// </summary>
    /// <remarks>
    /// A slide placeholder matches a layout placeholder by <c>idx</c> when it has one, else by type (<c>title</c> and <c>ctrTitle</c>
    /// count as one type); a layout placeholder matches a master placeholder by type, where every content type (<c>obj</c>, <c>tbl</c>,
    /// <c>chart</c>, <c>pic</c>…) and <c>subTitle</c> read as <c>body</c>. A notes placeholder matches the notes master's by type and index.
    /// </remarks>
    public IReadOnlyList<Shape> InheritanceChain
    {
        get
        {
            var chain = new List<Shape>();
            Shape current = this;
            while (chain.Count < 4 && current.InheritsFrom() is { } next)
            {
                chain.Add(next);
                current = next;
            }

            return chain;
        }
    }

    private Shape? InheritsFrom()
    {
        if (Placeholder is not { } ph)
        {
            return null;
        }

        switch (Part)
        {
            case Slide slide when slide.Layout is { } layout:
                return MatchSlideToLayout(ph, Placeholders(layout));
            case SlideLayout layout when layout.Master is { } master:
                return MatchByMasterType(ph, Placeholders(master));
            case NotesSlide notes when notes.NotesMaster is { } nm:
                return MatchNotes(ph, Placeholders(nm));
            default:
                return null;
        }
    }

    private static List<(Shape Shape, PlaceholderInfo Info)> Placeholders(PresentationPart part)
    {
        var list = new List<(Shape, PlaceholderInfo)>();
        foreach (Shape s in part.AllShapes())
        {
            if (s.Placeholder is { } info)
            {
                list.Add((s, info));
            }
        }

        return list;
    }

    // ECMA-376 Part 1 §19.3.1.36: idx ties a placeholder to the one on the layout; the type decides when there is no index.
    private static string SameType(string type) => type == "ctrTitle" ? "title" : type;

    private static Shape? MatchSlideToLayout(PlaceholderInfo ph, List<(Shape Shape, PlaceholderInfo Info)> candidates)
    {
        string type = SameType(ph.Type);
        if (ph.Index != 0)
        {
            Shape? byIndex = null;
            foreach ((Shape shape, PlaceholderInfo info) in candidates)
            {
                if (info.Index == ph.Index)
                {
                    if (SameType(info.Type) == type || !ph.HasType)
                    {
                        return shape;
                    }

                    byIndex ??= shape;
                }
            }

            if (byIndex is not null)
            {
                return byIndex;
            }
        }

        Shape? sameTypeAnyIndex = null;
        foreach ((Shape shape, PlaceholderInfo info) in candidates)
        {
            if (SameType(info.Type) == type)
            {
                if (info.Index == ph.Index)
                {
                    return shape;
                }

                sameTypeAnyIndex ??= shape;
            }
        }

        return sameTypeAnyIndex;
    }

    // A master has one placeholder per role: title, body (text and every content type), date, footer, slide number.
    private static string MasterRole(string type) => type switch
    {
        "title" or "ctrTitle" => "title",
        "dt" => "dt",
        "ftr" => "ftr",
        "sldNum" => "sldNum",
        "hdr" => "hdr",
        _ => "body",
    };

    private static Shape? MatchByMasterType(PlaceholderInfo ph, List<(Shape Shape, PlaceholderInfo Info)> candidates)
    {
        string role = MasterRole(ph.Type);
        foreach ((Shape shape, PlaceholderInfo info) in candidates)
        {
            if (MasterRole(info.Type) == role)
            {
                return shape;
            }
        }

        return null;
    }

    private static Shape? MatchNotes(PlaceholderInfo ph, List<(Shape Shape, PlaceholderInfo Info)> candidates)
    {
        foreach ((Shape shape, PlaceholderInfo info) in candidates)
        {
            if (info.Type == ph.Type && info.Index == ph.Index)
            {
                return shape;
            }
        }

        foreach ((Shape shape, PlaceholderInfo info) in candidates)
        {
            if (info.Type == ph.Type)
            {
                return shape;
            }
        }

        return null;
    }

    /// <summary>Gets the shape's position and size: its own, else the nearest one in <see cref="InheritanceChain"/>. Null when nothing in the chain has one.</summary>
    /// <returns>The bounds in EMU.</returns>
    public ShapeBounds? ResolveBounds()
    {
        if (Bounds is { } own)
        {
            return own;
        }

        foreach (Shape s in InheritanceChain)
        {
            if (s.Bounds is { } inherited)
            {
                return inherited;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the body properties of the shape's text after inheritance: each attribute comes from the shape's own <c>a:bodyPr</c> or, when
    /// that does not write it, the nearest placeholder in <see cref="InheritanceChain"/> that does; the rest take the schema defaults.
    /// </summary>
    /// <returns>The resolved properties; the defaults for a shape without a text body.</returns>
    public BodyFormat ResolveBodyFormat()
    {
        var sources = new List<MarkupRef>();
        if (TextBody?.BodyProperties is { IsValid: true } own)
        {
            sources.Add(own);
        }

        foreach (Shape s in InheritanceChain)
        {
            if (s.TextBody?.BodyProperties is { IsValid: true } inherited)
            {
                sources.Add(inherited);
            }
        }

        long? Long(string name) => First(sources, name, static (e, n) => Pml.AttrLong(e, n));
        string? Str(string name) => First(sources, name, static (e, n) => Pml.Attr(e, n));

        AutofitKind fit = AutofitKind.None;
        int? scale = null;
        int? reduction = null;
        foreach (MarkupRef src in sources)
        {
            if (Pml.A(src, "normAutofit") is { } norm)
            {
                fit = AutofitKind.ShrinkText;
                scale = Pml.AttrInt(norm, "fontScale");
                reduction = Pml.AttrInt(norm, "lnSpcReduction");
                break;
            }

            if (Pml.A(src, "spAutoFit") is not null)
            {
                fit = AutofitKind.ResizeShape;
                break;
            }

            if (Pml.A(src, "noAutofit") is not null)
            {
                break;
            }
        }

        // ECMA-376 Part 1 §21.1.2.1.1 (bodyPr): insets default to 0.1 in left and right, 0.05 in top and bottom; anchor t, wrap square, vert horz.
        return new BodyFormat(
            Long("lIns") ?? 91440,
            Long("tIns") ?? 45720,
            Long("rIns") ?? 91440,
            Long("bIns") ?? 45720,
            Str("anchor") ?? "t",
            Str("wrap") ?? "square",
            Str("vert") ?? "horz",
            fit,
            scale,
            reduction);
    }

    private static T? First<T>(List<MarkupRef> sources, string name, Func<MarkupRef, string, T?> read)
    {
        foreach (MarkupRef src in sources)
        {
            if (Pml.Attr(src, name) is not null && read(src, name) is { } v)
            {
                return v;
            }
        }

        return default;
    }

    // The list styles that apply to this shape's text, highest priority first (ECMA-376 Part 1 §19.3.1.51 txStyles, §19.3.1.8 defaultTextStyle).
    internal List<MarkupRef> StyleContainers()
    {
        var list = new List<MarkupRef>();
        if (TextBody?.ListStyle is { IsValid: true } own)
        {
            list.Add(own);
        }

        foreach (Shape s in InheritanceChain)
        {
            if (s.TextBody?.ListStyle is { IsValid: true } inherited)
            {
                list.Add(inherited);
            }
        }

        PlaceholderInfo? ph = Placeholder;
        switch (Part)
        {
            case Slide or SlideLayout or SlideMaster:
                SlideMaster? master = Part switch
                {
                    Slide s => s.Master,
                    SlideLayout l => l.Master,
                    SlideMaster m => m,
                    _ => null,
                };
                if (master is not null && ph is { } info)
                {
                    // Title text takes the title style, the date, footer and slide number the other style, everything else the body style.
                    MarkupRef style = MasterRole(info.Type) switch
                    {
                        "title" => master.TitleStyle,
                        "dt" or "ftr" or "sldNum" or "hdr" => master.OtherStyle,
                        _ => master.BodyStyle,
                    };
                    if (style.IsValid)
                    {
                        list.Add(style);
                    }
                }

                break;
            case NotesSlide or NotesMaster:
                NotesMaster? nm = Part is NotesSlide n ? n.NotesMaster : (NotesMaster)Part;
                if (nm?.NotesStyle is { IsValid: true } notesStyle)
                {
                    list.Add(notesStyle);
                }

                break;
        }

        if (Part.Owner.DefaultTextStyle is { IsValid: true } deflt)
        {
            list.Add(deflt);
        }

        return list;
    }
}


/// <summary>Reads paragraph and character formatting from DrawingML property elements into the resolved records, first writer wins.</summary>
internal sealed class FormatReader
{
    private string? _algn;
    private long? _marL;
    private long? _marR;
    private long? _indent;
    private Spacing? _lnSpc;
    private Spacing? _spcBef;
    private Spacing? _spcAft;
    private bool? _rtl;
    private BulletKind _bullet;
    private string? _buChar;
    private string? _buFont;
    private string? _buType;
    private int? _buStart;
    private int? _buSize;
    private ColorValue? _buColor;
    private int? _sz;
    private bool? _b;
    private bool? _i;
    private string? _u;
    private string? _strike;
    private string? _typeface;
    private ColorValue? _color;
    private string? _lang;

    // Adds one paragraph-properties element (a:pPr, a:lvlNpPr or a:defPPr); anything already set by an earlier call stays.
    public void Add(MarkupRef pPr)
    {
        if (!pPr.IsValid)
        {
            return;
        }

        _algn ??= Pml.Attr(pPr, "algn");
        _marL ??= Pml.AttrLong(pPr, "marL");
        _marR ??= Pml.AttrLong(pPr, "marR");
        _indent ??= Pml.AttrLong(pPr, "indent");
        _rtl ??= Pml.AttrBool(pPr, "rtl");
        _lnSpc ??= ReadSpacing(pPr, "lnSpc");
        _spcBef ??= ReadSpacing(pPr, "spcBef");
        _spcAft ??= ReadSpacing(pPr, "spcAft");
        foreach (MarkupRef child in Pml.Elements(pPr))
        {
            if (child.Namespace != SchemaNamespace.Drawing)
            {
                continue;
            }

            switch (child.LocalNameText)
            {
                case "buNone" when _bullet == BulletKind.Unspecified:
                    _bullet = BulletKind.None;
                    break;
                case "buChar" when _bullet == BulletKind.Unspecified:
                    _bullet = BulletKind.Character;
                    _buChar = Pml.Attr(child, "char");
                    break;
                case "buAutoNum" when _bullet == BulletKind.Unspecified:
                    _bullet = BulletKind.AutoNumber;
                    _buType = Pml.Attr(child, "type");
                    _buStart = Pml.AttrInt(child, "startAt");
                    break;
                case "buBlip" when _bullet == BulletKind.Unspecified:
                    _bullet = BulletKind.Picture;
                    break;
                case "buFont":
                    _buFont ??= Pml.Attr(child, "typeface");
                    break;
                case "buSzPct":
                    _buSize ??= Pml.AttrInt(child, "val");
                    break;
                case "buClr":
                    _buColor ??= ReadColor(child);
                    break;
            }
        }

        if (Pml.A(pPr, "defRPr") is { } def)
        {
            AddRun(def);
        }
    }

    // Adds one character-properties element (a:rPr or a:defRPr).
    public void AddRun(MarkupRef rPr)
    {
        if (!rPr.IsValid)
        {
            return;
        }

        _sz ??= Pml.AttrInt(rPr, "sz");
        _b ??= Pml.AttrBool(rPr, "b");
        _i ??= Pml.AttrBool(rPr, "i");
        _u ??= Pml.Attr(rPr, "u");
        _strike ??= Pml.Attr(rPr, "strike");
        _lang ??= Pml.Attr(rPr, "lang");
        if (Pml.A(rPr, "solidFill") is { } fill)
        {
            _color ??= ReadColor(fill);
        }

        if (Pml.A(rPr, "latin") is { } latin)
        {
            _typeface ??= Pml.Attr(latin, "typeface");
        }
    }

    private static Spacing? ReadSpacing(MarkupRef pPr, string name)
    {
        if (Pml.A(pPr, name) is not { } e)
        {
            return null;
        }

        if (Pml.A(e, "spcPct") is { } pct && Pml.AttrInt(pct, "val") is { } p)
        {
            return new Spacing(true, p);
        }

        return Pml.A(e, "spcPts") is { } pts && Pml.AttrInt(pts, "val") is { } v ? new Spacing(false, v) : null;
    }

    // The colour a fill-like element (solidFill, buClr) holds.
    internal static ColorValue? ReadColor(MarkupRef holder)
    {
        foreach (MarkupRef c in Pml.Elements(holder))
        {
            if (c.Namespace != SchemaNamespace.Drawing)
            {
                continue;
            }

            switch (c.LocalNameText)
            {
                case "srgbClr":
                    return new ColorValue(ColorKind.Srgb, (Pml.Attr(c, "val") ?? string.Empty).ToUpperInvariant());
                case "schemeClr":
                    return new ColorValue(ColorKind.Scheme, Pml.Attr(c, "val") ?? string.Empty);
                case "sysClr":
                    return Pml.Attr(c, "lastClr") is { } last
                        ? new ColorValue(ColorKind.Srgb, last.ToUpperInvariant())
                        : new ColorValue(ColorKind.Preset, Pml.Attr(c, "val") ?? string.Empty);
                case "prstClr":
                    return new ColorValue(ColorKind.Preset, Pml.Attr(c, "val") ?? string.Empty);
                case "scrgbClr" or "hslClr":
                    return new ColorValue(ColorKind.Other, string.Empty);
            }
        }

        return null;
    }

    public RunFormat Run(PresentationPart part) =>
        new(_sz, _b, _i, _u, _strike, part.Theme?.ResolveTypeface(_typeface) ?? _typeface, _color, _lang);

    public ParagraphFormat Paragraph(PresentationPart part) =>
        new(_algn, _marL, _marR, _indent, _lnSpc, _spcBef, _spcAft, _rtl,
            new BulletFormat(_bullet, _buChar, _buFont, _buType, _buStart, _buSize, _buColor), Run(part));
}

public sealed partial class TextParagraph
{
    /// <summary>
    /// Gets the paragraph's formatting after inheritance. Each property comes from the first place that writes it: the paragraph's own
    /// <c>a:pPr</c>; then, for the paragraph's level, the shape's list style, the layout and master placeholders' list styles, the
    /// master's title, body or other style (by placeholder type) and the presentation's default text style. The bullet comes as a unit
    /// from the first source that names one.
    /// </summary>
    /// <returns>The resolved formatting.</returns>
    public ParagraphFormat ResolveFormat() => ReadFormat(Level);

    internal ParagraphFormat ReadFormat(int level)
    {
        var reader = new FormatReader();
        reader.Add(Properties);
        string levelName = "lvl" + (level + 1).ToString(CultureInfo.InvariantCulture) + "pPr";
        List<MarkupRef> containers = Body.Shape?.StyleContainers() ?? DefaultContainers();
        foreach (MarkupRef container in containers)
        {
            if (Pml.A(container, levelName) is { } lvl)
            {
                reader.Add(lvl);
            }
        }

        foreach (MarkupRef container in containers)
        {
            if (Pml.A(container, "defPPr") is { } def)
            {
                reader.Add(def);
            }
        }

        return reader.Paragraph(Body.Part);
    }

    private List<MarkupRef> DefaultContainers()
    {
        var list = new List<MarkupRef>();
        if (Body.Part.Owner.DefaultTextStyle is { IsValid: true } d)
        {
            list.Add(d);
        }

        return list;
    }

    /// <summary>Gets a run's character formatting after inheritance: the run's own <c>a:rPr</c> first, then the paragraph level's defaults (see <see cref="ResolveFormat"/>).</summary>
    /// <param name="run">A run of this paragraph.</param>
    /// <returns>The resolved character formatting.</returns>
    public RunFormat ResolveRunFormat(TextRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var reader = new FormatReader();
        reader.AddRun(run.Properties);
        RunFormat defaults = ReadFormat(Level).Run;
        RunFormat own = reader.Run(Body.Part);
        return new RunFormat(
            own.Size ?? defaults.Size,
            own.Bold ?? defaults.Bold,
            own.Italic ?? defaults.Italic,
            own.Underline ?? defaults.Underline,
            own.Strike ?? defaults.Strike,
            own.Typeface ?? defaults.Typeface,
            own.Color ?? defaults.Color,
            own.Language ?? defaults.Language);
    }
}

public abstract partial class PresentationPart
{
    /// <summary>Resolves a colour to six hex digits: an sRGB colour as it is, a scheme colour through the colour map and the theme.</summary>
    /// <param name="color">The colour, or null.</param>
    /// <returns>The hex digits, or null for a null, preset or unconvertible colour or an unresolvable scheme colour.</returns>
    public string? ResolveColor(ColorValue? color) => color switch
    {
        { Kind: ColorKind.Srgb } c => c.Value,
        { Kind: ColorKind.Scheme } c => ResolveSchemeColor(c.Value),
        _ => null,
    };
}
