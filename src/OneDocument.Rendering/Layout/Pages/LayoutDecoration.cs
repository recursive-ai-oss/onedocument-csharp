using OneFont;

namespace RecursiveAI.OneDocument.Layout;

/// <summary>What a <see cref="LayoutDecoration"/> is.</summary>
public enum LayoutDecorationKind
{
    /// <summary>One side of a page border (<c>w:pgBorders</c>), a thin rectangle.</summary>
    PageBorder = 0,

    /// <summary>The line between two text columns (<c>w:cols w:sep</c>).</summary>
    ColumnSeparator = 1,

    /// <summary>A line number in the margin (<c>w:lnNumType</c>).</summary>
    LineNumber = 2,

    /// <summary>Watermark text from a header (a WordArt shape with a text path).</summary>
    Watermark = 3,

    /// <summary>The rule above the notes of a page (<c>w:separator</c>), a short line.</summary>
    NoteSeparator = 4,

    /// <summary>The rule above notes that continue from the previous page (<c>w:continuationSeparator</c>), a line across the column.</summary>
    NoteContinuationSeparator = 5,
}

/// <summary>
/// Something on a page that is not part of the text flow. Borders and separators are rectangles in <see cref="LayoutNode.Bounds"/>;
/// line numbers and watermarks also carry <see cref="Text"/>. Decorations are painted as artifacts.
/// </summary>
public sealed class LayoutDecoration : LayoutNode
{
    internal LayoutDecoration(LayoutDecorationKind kind)
        : base(LayoutNodeKind.Decoration)
    {
        DecorationKind = kind;
    }

    /// <summary>Gets what the decoration is.</summary>
    public LayoutDecorationKind DecorationKind { get; }

    /// <summary>Gets the text of a line number or watermark; empty for borders and separators.</summary>
    public string Text { get; internal set; } = string.Empty;

    /// <summary>Gets the border line style as written (<c>single</c>, <c>double</c>, <c>dashed</c>, …); empty for the others.</summary>
    public string Style { get; internal set; } = string.Empty;

    /// <summary>Gets the line number's baseline in page coordinates, in points; 0 for the others.</summary>
    public double Baseline { get; internal set; }

    /// <summary>Gets the clockwise rotation of a watermark in degrees; 0 for the others.</summary>
    public double Rotation { get; internal set; }

    /// <summary>Gets the font size in points of a line number or watermark.</summary>
    public double FontSize { get; internal set; }

    internal uint Color { get; set; } // 0xRRGGBB

    internal double Opacity { get; set; } = 1;

    internal FontFace? Face { get; set; }

    internal GlyphData? Glyphs { get; set; }
}
