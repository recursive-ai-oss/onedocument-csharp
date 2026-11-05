namespace RecursiveAI.OneDocument.Presentations;

/// <summary>How a colour is named in DrawingML.</summary>
public enum ColorKind
{
    /// <summary>A colour model the hub does not convert (<c>scrgbClr</c>, <c>hslClr</c>); <see cref="ColorValue.Value"/> is empty.</summary>
    Other = 0,

    /// <summary>Six hex digits (<c>srgbClr</c>, or a system colour's last known value).</summary>
    Srgb = 1,

    /// <summary>A theme colour name (<c>schemeClr</c>): <c>tx1</c>, <c>bg1</c>, <c>accent2</c>… Resolve it with <see cref="PresentationPart.ResolveColor"/>.</summary>
    Scheme = 2,

    /// <summary>A preset colour name (<c>prstClr</c>: <c>red</c>, <c>black</c>…).</summary>
    Preset = 3,
}

/// <summary>A colour as a shape writes it. Colour transforms (<c>lumMod</c>, <c>tint</c>, <c>alpha</c>…) are not applied.</summary>
/// <param name="Kind">How the colour is named.</param>
/// <param name="Value">The hex digits, scheme name or preset name.</param>
public readonly record struct ColorValue(ColorKind Kind, string Value);

/// <summary>A line or paragraph spacing value.</summary>
/// <param name="IsPercent">True when <paramref name="Value"/> is a percentage in 1,000ths of a percent (100,000 is 100%); false when it is points in 100ths.</param>
/// <param name="Value">The value in those units.</param>
public readonly record struct Spacing(bool IsPercent, int Value);

/// <summary>What kind of bullet a paragraph level has.</summary>
public enum BulletKind
{
    /// <summary>No source in the chain says; the paragraph has no bullet.</summary>
    Unspecified = 0,

    /// <summary>Explicitly no bullet (<c>a:buNone</c>).</summary>
    None = 1,

    /// <summary>A character bullet (<c>a:buChar</c>).</summary>
    Character = 2,

    /// <summary>An automatic number (<c>a:buAutoNum</c>).</summary>
    AutoNumber = 3,

    /// <summary>A picture bullet (<c>a:buBlip</c>).</summary>
    Picture = 4,
}

/// <summary>The bullet of a paragraph level after inheritance.</summary>
/// <param name="Kind">The kind of bullet.</param>
/// <param name="Character">The bullet character of a <see cref="BulletKind.Character"/> bullet.</param>
/// <param name="Font">The bullet font (<c>a:buFont/@typeface</c>), or null to follow the text.</param>
/// <param name="AutoNumberType">The numbering scheme of an <see cref="BulletKind.AutoNumber"/> bullet (<c>arabicPeriod</c>…).</param>
/// <param name="StartAt">The first number of an automatic number, or null for 1.</param>
/// <param name="SizePercent">The bullet size as a percentage of the text in 1,000ths of a percent (<c>a:buSzPct</c>), or null.</param>
/// <param name="Color">The bullet colour (<c>a:buClr</c>), or null to follow the text.</param>
public sealed record BulletFormat(BulletKind Kind, string? Character, string? Font, string? AutoNumberType, int? StartAt, int? SizePercent, ColorValue? Color);

/// <summary>Character formatting after inheritance. A null member is not set anywhere in the chain.</summary>
/// <param name="Size">The size in 100ths of a point (<c>sz</c>: 1800 is 18 pt).</param>
/// <param name="Bold"><c>b</c>.</param>
/// <param name="Italic"><c>i</c>.</param>
/// <param name="Underline"><c>u</c> (<c>sng</c>, <c>dbl</c>…; <c>none</c> for none).</param>
/// <param name="Strike"><c>strike</c> (<c>sngStrike</c>, <c>dblStrike</c>, <c>noStrike</c>).</param>
/// <param name="Typeface">The Latin typeface with theme references (<c>+mn-lt</c>) resolved through the theme when it has them.</param>
/// <param name="Color">The text colour (<c>a:solidFill</c>).</param>
/// <param name="Language"><c>lang</c>.</param>
public sealed record RunFormat(int? Size, bool? Bold, bool? Italic, string? Underline, string? Strike, string? Typeface, ColorValue? Color, string? Language);

/// <summary>Paragraph formatting after inheritance from the slide, layout, master and presentation defaults. A null member is not set anywhere in the chain.</summary>
/// <param name="Alignment"><c>algn</c> (<c>l</c>, <c>ctr</c>, <c>r</c>, <c>just</c>…).</param>
/// <param name="MarginLeft"><c>marL</c> in EMU.</param>
/// <param name="MarginRight"><c>marR</c> in EMU.</param>
/// <param name="Indent"><c>indent</c> in EMU (negative for a hanging indent).</param>
/// <param name="LineSpacing"><c>a:lnSpc</c>.</param>
/// <param name="SpaceBefore"><c>a:spcBef</c>.</param>
/// <param name="SpaceAfter"><c>a:spcAft</c>.</param>
/// <param name="RightToLeft"><c>rtl</c>.</param>
/// <param name="Bullet">The bullet.</param>
/// <param name="Run">The default character formatting of the paragraph's level (<c>a:defRPr</c>).</param>
public sealed record ParagraphFormat(
    string? Alignment,
    long? MarginLeft,
    long? MarginRight,
    long? Indent,
    Spacing? LineSpacing,
    Spacing? SpaceBefore,
    Spacing? SpaceAfter,
    bool? RightToLeft,
    BulletFormat Bullet,
    RunFormat Run);

/// <summary>How a text body copes with text that does not fit.</summary>
public enum AutofitKind
{
    /// <summary>No automatic fitting (<c>a:noAutofit</c>, or no element anywhere in the chain).</summary>
    None = 0,

    /// <summary>The text shrinks to fit (<c>a:normAutofit</c>).</summary>
    ShrinkText = 1,

    /// <summary>The shape grows to fit the text (<c>a:spAutoFit</c>).</summary>
    ResizeShape = 2,
}

/// <summary>Body properties after inheritance from the layout and master placeholders.</summary>
/// <param name="LeftInset"><c>lIns</c> in EMU (91,440 when nothing sets it).</param>
/// <param name="TopInset"><c>tIns</c> in EMU (45,720 by default).</param>
/// <param name="RightInset"><c>rIns</c> in EMU (91,440 by default).</param>
/// <param name="BottomInset"><c>bIns</c> in EMU (45,720 by default).</param>
/// <param name="Anchor"><c>anchor</c> (<c>t</c>, <c>ctr</c>, <c>b</c>…; <c>t</c> by default).</param>
/// <param name="Wrap"><c>wrap</c> (<c>square</c> or <c>none</c>; <c>square</c> by default).</param>
/// <param name="Vertical"><c>vert</c> (<c>horz</c> by default).</param>
/// <param name="Autofit">The autofit mode.</param>
/// <param name="FontScale">A shrink-to-fit font scale in 1,000ths of a percent (<c>normAutofit/@fontScale</c>), or null.</param>
/// <param name="LineSpacingReduction">A shrink-to-fit line spacing reduction in 1,000ths of a percent, or null.</param>
public sealed record BodyFormat(
    long LeftInset,
    long TopInset,
    long RightInset,
    long BottomInset,
    string Anchor,
    string Wrap,
    string Vertical,
    AutofitKind Autofit,
    int? FontScale,
    int? LineSpacingReduction);
