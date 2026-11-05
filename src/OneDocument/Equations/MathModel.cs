namespace RecursiveAI.OneDocument.Equations;

/// <summary>The kinds of node in an equation tree (ECMA-376 Part 1 §22.1.2).</summary>
public enum MathKind
{
    /// <summary>One equation (<c>m:oMath</c>). Its children are math objects and runs.</summary>
    OMath = 0,

    /// <summary>A display group (<c>m:oMathPara</c>): equations, each on its own line, with a justification.</summary>
    OMathPara,

    /// <summary>An argument slot of an object (<c>m:e</c>, <c>m:num</c>, <c>m:den</c>, <c>m:sub</c>, <c>m:sup</c>, <c>m:deg</c>, <c>m:lim</c>, <c>m:fName</c>); see <see cref="MathNode.Role"/>.</summary>
    Argument,

    /// <summary>A run of math text (<c>m:r</c>).</summary>
    Run,

    /// <summary>An accent over the base (<c>m:acc</c>).</summary>
    Accent,

    /// <summary>An over- or underbar (<c>m:bar</c>).</summary>
    Bar,

    /// <summary>A box that groups content (<c>m:box</c>).</summary>
    Box,

    /// <summary>A box with borders and strike lines (<c>m:borderBox</c>).</summary>
    BorderBox,

    /// <summary>Delimiters with separators (<c>m:d</c>).</summary>
    Delimiter,

    /// <summary>An equation array: rows aligned at their alignment points (<c>m:eqArr</c>).</summary>
    EquationArray,

    /// <summary>A fraction: bar, skewed, linear or without a bar (<c>m:f</c>).</summary>
    Fraction,

    /// <summary>A function application: a name and its argument (<c>m:func</c>).</summary>
    Function,

    /// <summary>A stretchy character above or below its base (<c>m:groupChr</c>).</summary>
    GroupCharacter,

    /// <summary>A base with a limit below it (<c>m:limLow</c>).</summary>
    LowerLimit,

    /// <summary>A base with a limit above it (<c>m:limUpp</c>).</summary>
    UpperLimit,

    /// <summary>A matrix (<c>m:m</c>); its children are <see cref="MatrixRow"/> nodes.</summary>
    Matrix,

    /// <summary>One row of a matrix (<c>m:mr</c>); its children are the cells (<see cref="MathRole.Element"/> arguments).</summary>
    MatrixRow,

    /// <summary>An n-ary operator with limits and an operand (<c>m:nary</c>).</summary>
    NAry,

    /// <summary>A phantom, which can be invisible and can have its size zeroed (<c>m:phant</c>).</summary>
    Phantom,

    /// <summary>A radical with an optional degree (<c>m:rad</c>).</summary>
    Radical,

    /// <summary>Scripts before the base (<c>m:sPre</c>).</summary>
    PreScript,

    /// <summary>A subscript (<c>m:sSub</c>).</summary>
    SubScript,

    /// <summary>A superscript (<c>m:sSup</c>).</summary>
    SuperScript,

    /// <summary>A subscript and a superscript (<c>m:sSubSup</c>).</summary>
    SubSuperScript,
}

/// <summary>What an <see cref="MathKind.Argument"/> node is for.</summary>
public enum MathRole
{
    /// <summary>Not an argument.</summary>
    None = 0,

    /// <summary>The base or operand (<c>m:e</c>) of an object with one argument.</summary>
    Base,

    /// <summary>An element of a delimiter, equation array or matrix cell (<c>m:e</c>).</summary>
    Element,

    /// <summary>A fraction numerator (<c>m:num</c>).</summary>
    Numerator,

    /// <summary>A fraction denominator (<c>m:den</c>).</summary>
    Denominator,

    /// <summary>A subscript or n-ary lower limit (<c>m:sub</c>).</summary>
    Subscript,

    /// <summary>A superscript or n-ary upper limit (<c>m:sup</c>).</summary>
    Superscript,

    /// <summary>A radical's degree (<c>m:deg</c>).</summary>
    Degree,

    /// <summary>A limit (<c>m:lim</c>) of <see cref="MathKind.LowerLimit"/> and <see cref="MathKind.UpperLimit"/>.</summary>
    Limit,

    /// <summary>A function name (<c>m:fName</c>).</summary>
    FunctionName,
}

/// <summary>The shape of a fraction (<c>m:f/m:fPr/m:type</c>, ECMA-376 Part 1 §22.1.2.112).</summary>
public enum FractionStyle
{
    /// <summary>Numerator over denominator with a bar (<c>bar</c>, the default).</summary>
    Bar = 0,

    /// <summary>Slanted (<c>skw</c>).</summary>
    Skewed,

    /// <summary>On one line with a slash (<c>lin</c>).</summary>
    Linear,

    /// <summary>Stacked without a bar (<c>noBar</c>).</summary>
    NoBar,
}

/// <summary>Where the limits of an n-ary operator or the lower/upper limit objects sit (<c>m:limLoc</c>, §22.1.2.64).</summary>
public enum LimitLocation
{
    /// <summary>Not stated; the document's default (<see cref="MathSettings"/>) applies.</summary>
    Default = 0,

    /// <summary>Above and below the operator (<c>undOvr</c>).</summary>
    UnderOver,

    /// <summary>As subscript and superscript beside it (<c>subSup</c>).</summary>
    SubSup,
}

/// <summary>Horizontal justification of display math or a matrix column (<c>m:jc</c>, §22.1.2.57).</summary>
public enum MathJustification
{
    /// <summary>Centered (<c>center</c>).</summary>
    Center = 0,

    /// <summary>Left (<c>left</c>).</summary>
    Left,

    /// <summary>Right (<c>right</c>).</summary>
    Right,

    /// <summary>Centered as a group (<c>centerGroup</c>, the default of a display group).</summary>
    CenterGroup,
}

/// <summary>Vertical alignment against the surrounding line (<c>m:baseJc</c> and <c>m:vertJc</c>).</summary>
public enum MathVerticalAlignment
{
    /// <summary>Center on the math axis (<c>center</c>).</summary>
    Center = 0,

    /// <summary>Top (<c>top</c>).</summary>
    Top,

    /// <summary>Bottom (<c>bot</c>).</summary>
    Bottom,

    /// <summary>Base line (<c>baseline</c>).</summary>
    Baseline,
}

/// <summary>Script of a math run's text (<c>m:scr</c>, §22.1.2.88).</summary>
public enum MathScript
{
    /// <summary>Roman (the default).</summary>
    Roman = 0,

    /// <summary>Script (calligraphic).</summary>
    Script,

    /// <summary>Fraktur.</summary>
    Fraktur,

    /// <summary>Double-struck.</summary>
    DoubleStruck,

    /// <summary>Sans-serif.</summary>
    SansSerif,

    /// <summary>Monospace.</summary>
    Monospace,
}

/// <summary>Style of a math run's text (<c>m:sty</c>, §22.1.2.93).</summary>
public enum MathStyle
{
    /// <summary>Not stated: letters that stand for variables are italic, everything else is upright.</summary>
    Default = 0,

    /// <summary>Plain (upright).</summary>
    Plain,

    /// <summary>Bold.</summary>
    Bold,

    /// <summary>Italic.</summary>
    Italic,

    /// <summary>Bold italic.</summary>
    BoldItalic,
}

/// <summary>Where a bar or group character sits relative to its base.</summary>
public enum MathPlacement
{
    /// <summary>Above the base.</summary>
    Top = 0,

    /// <summary>Below the base.</summary>
    Bottom,
}

/// <summary>A column group of a matrix (<c>m:mc</c>): how many columns and how they are justified.</summary>
/// <param name="Count">The number of columns the group describes.</param>
/// <param name="Justification">The justification of those columns.</param>
public readonly record struct MatrixColumnSpec(int Count, MathJustification Justification);

/// <summary>How row or column spacing is given (<c>m:rSpRule</c>, <c>m:cGpRule</c>, §22.1.2.84).</summary>
public enum MathSpacingRule
{
    /// <summary>Single spacing (<c>0</c>).</summary>
    SingleLine = 0,

    /// <summary>One and a half times (<c>1</c>).</summary>
    OneAndHalf,

    /// <summary>Double (<c>2</c>).</summary>
    DoubleLine,

    /// <summary>Exactly the stated number of points (<c>3</c>).</summary>
    Exactly,

    /// <summary>A multiple of single spacing, the stated number in half lines (<c>4</c>).</summary>
    Multiple,
}

/// <summary>Where a binary operator goes when a display equation breaks after it (<c>m:brkBin</c>, §22.1.2.12).</summary>
public enum BinaryBreak
{
    /// <summary>The operator starts the next line (the default).</summary>
    Before = 0,

    /// <summary>The operator ends the line.</summary>
    After,

    /// <summary>The operator ends the line and starts the next one.</summary>
    Repeat,
}

/// <summary>What happens to a minus sign at a break (<c>m:brkBinSub</c>, §22.1.2.13).</summary>
public enum BinarySubtractBreak
{
    /// <summary>The operator and the following minus stay together (<c>--</c>, the default).</summary>
    Minus = 0,

    /// <summary>The minus is repeated (<c>+-</c>).</summary>
    PlusMinus,

    /// <summary>Minus and plus break as plus then minus (<c>-+</c>).</summary>
    MinusPlus,
}

/// <summary>
/// The formatting of a math run: its <c>m:rPr</c> and the character formatting it carries in <c>w:rPr</c>. Unset values are null.
/// </summary>
public sealed class MathRunProperties
{
    /// <summary>Gets the script (<c>m:scr</c>).</summary>
    public MathScript Script { get; internal set; }

    /// <summary>Gets the style (<c>m:sty</c>).</summary>
    public MathStyle Style { get; internal set; }

    /// <summary>Gets whether the text is a literal (<c>m:lit</c>): not reinterpreted as operators or functions and shown in normal text.</summary>
    public bool Literal { get; internal set; }

    /// <summary>Gets whether the text is normal text (<c>m:nor</c>): the document font, not the math font and not italicised.</summary>
    public bool NormalText { get; internal set; }

    /// <summary>Gets whether the run starts at an alignment point (<c>m:aln</c>).</summary>
    public bool AlignmentPoint { get; internal set; }

    /// <summary>Gets whether a manual line break comes before the run (<c>m:brk</c>).</summary>
    public bool Break { get; internal set; }

    /// <summary>Gets the alignment point a manual break aligns the next line to, 1-based (<c>m:alnAt</c>); 0 when not stated.</summary>
    public int BreakAlignAt { get; internal set; }

    /// <summary>Gets the font size in points from <c>w:rPr/w:sz</c>, or null to use the surrounding size.</summary>
    public double? Size { get; internal set; }

    /// <summary>Gets the colour as 0xRRGGBB from <c>w:rPr/w:color</c>, or null for the surrounding colour.</summary>
    public uint? Color { get; internal set; }

    /// <summary>Gets the font family of <c>w:rPr/w:rFonts</c> (the ASCII slot), or null.</summary>
    public string? FontFamily { get; internal set; }

    /// <summary>Gets whether <c>w:rPr</c> makes the text bold, or null when it does not say.</summary>
    public bool? Bold { get; internal set; }

    /// <summary>Gets whether <c>w:rPr</c> makes the text italic, or null when it does not say.</summary>
    public bool? Italic { get; internal set; }
}

/// <summary>
/// The properties of a math object (the <c>*Pr</c> element of each object). Every member applies to the kinds named in its summary
/// and is ignored by the others; unset values are null or the documented default.
/// </summary>
public sealed class MathProperties
{
    /// <summary>Gets the operator character: accent (<c>chr</c>, default U+0302), n-ary (default U+222B), group character (default U+23DF). Null = default; empty = none.</summary>
    public string? Character { get; internal set; }

    /// <summary>Gets a delimiter's opening character (<c>begChr</c>, default "("); empty = none.</summary>
    public string? OpenCharacter { get; internal set; }

    /// <summary>Gets a delimiter's closing character (<c>endChr</c>, default ")"); empty = none.</summary>
    public string? CloseCharacter { get; internal set; }

    /// <summary>Gets a delimiter's separator (<c>sepChr</c>, default "|"); empty = none.</summary>
    public string? SeparatorCharacter { get; internal set; }

    /// <summary>Gets whether delimiters or an n-ary operator grow with their content (<c>m:grow</c>); null = the object's default (true for delimiters, false for n-ary).</summary>
    public bool? Grow { get; internal set; }

    /// <summary>Gets a fraction's shape (<c>m:type</c>).</summary>
    public FractionStyle FractionStyle { get; internal set; }

    /// <summary>Gets the limit location of an n-ary operator (<c>m:limLoc</c>).</summary>
    public LimitLocation LimitLocation { get; internal set; }

    /// <summary>Gets whether an n-ary operator's lower limit is hidden (<c>m:subHide</c>).</summary>
    public bool HideSubscript { get; internal set; }

    /// <summary>Gets whether an n-ary operator's upper limit is hidden (<c>m:supHide</c>).</summary>
    public bool HideSuperscript { get; internal set; }

    /// <summary>Gets whether a radical's degree is hidden (<c>m:degHide</c>).</summary>
    public bool HideDegree { get; internal set; }

    /// <summary>Gets where a bar or group character sits (<c>m:pos</c>); null when not stated (bar: below, group character: below).</summary>
    public MathPlacement? Placement { get; internal set; }

    /// <summary>Gets a group character's vertical justification (<c>m:vertJc</c>).</summary>
    public MathVerticalAlignment? VerticalAlignment { get; internal set; }

    /// <summary>Gets the vertical alignment of a matrix or equation array against the line (<c>m:baseJc</c>).</summary>
    public MathVerticalAlignment? BaseJustification { get; internal set; }

    /// <summary>Gets the justification of a display group (<c>m:jc</c> in <c>m:oMathParaPr</c>).</summary>
    public MathJustification? Justification { get; internal set; }

    /// <summary>Gets a matrix's column groups (<c>m:mcs</c>).</summary>
    public IReadOnlyList<MatrixColumnSpec> Columns { get; internal set; } = [];

    /// <summary>Gets the row spacing rule (<c>m:rSpRule</c>).</summary>
    public MathSpacingRule RowSpacingRule { get; internal set; }

    /// <summary>Gets the row spacing value (<c>m:rSp</c>), meaning per <see cref="RowSpacingRule"/>.</summary>
    public int RowSpacing { get; internal set; }

    /// <summary>Gets the column gap rule (<c>m:cGpRule</c>).</summary>
    public MathSpacingRule ColumnGapRule { get; internal set; }

    /// <summary>Gets the column gap value (<c>m:cGp</c>).</summary>
    public int ColumnGap { get; internal set; }

    /// <summary>Gets the column spacing value (<c>m:cSp</c>) in twips.</summary>
    public int ColumnSpacing { get; internal set; }

    /// <summary>Gets whether a phantom is drawn (<c>m:show</c>; default true).</summary>
    public bool Show { get; internal set; } = true;

    /// <summary>Gets whether a phantom takes no width (<c>m:zeroWid</c>).</summary>
    public bool ZeroWidth { get; internal set; }

    /// <summary>Gets whether a phantom takes no ascent (<c>m:zeroAsc</c>).</summary>
    public bool ZeroAscent { get; internal set; }

    /// <summary>Gets whether a phantom takes no descent (<c>m:zeroDesc</c>).</summary>
    public bool ZeroDescent { get; internal set; }

    /// <summary>Gets whether a border box hides its top edge (<c>m:hideTop</c>).</summary>
    public bool HideTop { get; internal set; }

    /// <summary>Gets whether a border box hides its bottom edge (<c>m:hideBot</c>).</summary>
    public bool HideBottom { get; internal set; }

    /// <summary>Gets whether a border box hides its left edge (<c>m:hideLeft</c>).</summary>
    public bool HideLeft { get; internal set; }

    /// <summary>Gets whether a border box hides its right edge (<c>m:hideRight</c>).</summary>
    public bool HideRight { get; internal set; }

    /// <summary>Gets whether a border box draws a horizontal strike (<c>m:strikeH</c>).</summary>
    public bool StrikeHorizontal { get; internal set; }

    /// <summary>Gets whether a border box draws a vertical strike (<c>m:strikeV</c>).</summary>
    public bool StrikeVertical { get; internal set; }

    /// <summary>Gets whether a border box draws the diagonal from bottom left to top right (<c>m:strikeBLTR</c>).</summary>
    public bool StrikeBottomLeftToTopRight { get; internal set; }

    /// <summary>Gets whether a border box draws the diagonal from top left to bottom right (<c>m:strikeTLBR</c>).</summary>
    public bool StrikeTopLeftToBottomRight { get; internal set; }

    /// <summary>Gets the size in points of the control character (<c>m:ctrlPr/w:rPr/w:sz</c>), or null.</summary>
    public double? ControlSize { get; internal set; }

    /// <summary>Gets whether this is a differential (<c>m:diff</c> on a box).</summary>
    public bool Differential { get; internal set; }

    /// <summary>Gets whether the box is an operator emulation (<c>m:opEmu</c>): its content is spaced as an operator.</summary>
    public bool OperatorEmulation { get; internal set; }

    /// <summary>Gets whether the box prevents line breaks inside it (<c>m:noBreak</c>).</summary>
    public bool NoBreak { get; internal set; }
}

/// <summary>
/// One node of an equation tree read from OMML. The tree is a view: it is built from the markup and never written back, and
/// markup the model does not know (extensions, <c>mc:AlternateContent</c> branches, bookmarks) is skipped and counted in
/// <see cref="OmmlReader"/>'s notes, never dropped from the document.
/// </summary>
public sealed class MathNode
{
    private readonly List<MathNode> _children = [];

    internal MathNode(MathKind kind, MathRole role = MathRole.None)
    {
        Kind = kind;
        Role = role;
        Properties = Empty;
    }

    internal static MathProperties Empty { get; } = new();

    /// <summary>Gets what the node is.</summary>
    public MathKind Kind { get; }

    /// <summary>Gets the role of an argument node; <see cref="MathRole.None"/> for other nodes.</summary>
    public MathRole Role { get; }

    /// <summary>Gets the object's properties. Never null; a shared empty instance when the object had none.</summary>
    public MathProperties Properties { get; internal set; }

    /// <summary>Gets a run's text (<c>m:t</c>), or an empty string for other nodes.</summary>
    public string Text { get; internal set; } = string.Empty;

    /// <summary>Gets a run's formatting, or null for other nodes.</summary>
    public MathRunProperties? Run { get; internal set; }

    /// <summary>Gets the children in document order.</summary>
    public IReadOnlyList<MathNode> Children => _children;

    /// <summary>Gets the offset of the node's element in its part, or -1 when the node was read from a stream.</summary>
    public int SourceOffset { get; internal set; } = -1;

    internal void Add(MathNode child) => _children.Add(child);

    /// <summary>Finds the argument with a role.</summary>
    /// <param name="role">The role.</param>
    /// <returns>The first child argument with that role, or null.</returns>
    public MathNode? Argument(MathRole role)
    {
        foreach (MathNode c in _children)
        {
            if (c.Kind == MathKind.Argument && c.Role == role)
            {
                return c;
            }
        }

        return null;
    }
}

/// <summary>
/// The document-wide math settings (<c>m:mathPr</c> in the settings part, ECMA-376 Part 1 §22.1.2.62). Defaults are those of an
/// absent element.
/// </summary>
public sealed class MathSettings
{
    /// <summary>Gets the math font (<c>m:mathFont</c>), "Cambria Math" by default.</summary>
    public string MathFont { get; init; } = "Cambria Math";

    /// <summary>Gets where a break at a binary operator puts the operator (<c>m:brkBin</c>).</summary>
    public BinaryBreak BreakBinary { get; init; }

    /// <summary>Gets how a break at a subtraction behaves (<c>m:brkBinSub</c>).</summary>
    public BinarySubtractBreak BreakSubtract { get; init; }

    /// <summary>Gets whether fractions are drawn small (<c>m:smallFrac</c>).</summary>
    public bool SmallFractions { get; init; }

    /// <summary>Gets whether equations in their own paragraph use the display style (<c>m:dispDef</c>); default true.</summary>
    public bool DisplayDefault { get; init; } = true;

    /// <summary>Gets the left margin of display math in points (<c>m:lMargin</c>), 0 by default.</summary>
    public double LeftMargin { get; init; }

    /// <summary>Gets the right margin of display math in points (<c>m:rMargin</c>), 0 by default.</summary>
    public double RightMargin { get; init; }

    /// <summary>Gets the default justification of display math (<c>m:defJc</c>), centered as a group by default.</summary>
    public MathJustification DefaultJustification { get; init; } = MathJustification.CenterGroup;

    /// <summary>Gets the indent of the continuation lines of a broken display equation in points (<c>m:wrapIndent</c>), 72 by default.</summary>
    public double WrapIndent { get; init; } = 72;

    /// <summary>Gets whether continuation lines are right-justified (<c>m:wrapRight</c>).</summary>
    public bool WrapRight { get; init; }

    /// <summary>Gets where integral limits go (<c>m:intLim</c>): beside the sign by default.</summary>
    public LimitLocation IntegralLimits { get; init; } = LimitLocation.SubSup;

    /// <summary>Gets where the limits of other n-ary operators go (<c>m:naryLim</c>): above and below by default.</summary>
    public LimitLocation NaryLimits { get; init; } = LimitLocation.UnderOver;

    /// <summary>Gets the settings of a document that has no <c>m:mathPr</c>.</summary>
    public static MathSettings Default { get; } = new();
}

/// <summary>A thing the reader skipped or limited while reading an equation. Never silent.</summary>
/// <param name="Code">A stable code of the form <c>math.problem</c>.</param>
/// <param name="Message">A one-line description.</param>
public sealed record MathNote(string Code, string Message);
