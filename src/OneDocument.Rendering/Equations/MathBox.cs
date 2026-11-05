using OneFont;
using RecursiveAI.OneDocument.Equations;

namespace RecursiveAI.OneDocument.Rendering.Equations;

/// <summary>One drawn thing of a laid-out equation. Positions are points relative to the box's origin: x to the right, y down from the box's first baseline.</summary>
public abstract class MathItem
{
    private protected MathItem()
    {
    }

    /// <summary>Gets the colour as 0xRRGGBB.</summary>
    public uint Color { get; internal init; }

    internal abstract MathItem Moved(double dx, double dy);
}

/// <summary>A glyph drawn from a font.</summary>
public sealed class MathGlyph : MathItem
{
    internal MathGlyph(FontFace face, int glyphId, string text, double x, double y, double size, uint color)
    {
        Face = face;
        GlyphId = glyphId;
        Text = text;
        X = x;
        Y = y;
        Size = size;
        Color = color;
    }

    /// <summary>Gets the face the glyph comes from.</summary>
    public FontFace Face { get; }

    /// <summary>Gets the glyph id in <see cref="Face"/>.</summary>
    public int GlyphId { get; }

    /// <summary>Gets the character the glyph stands for (for text extraction); empty for the parts of an assembled delimiter.</summary>
    public string Text { get; }

    /// <summary>Gets the pen position's x (the glyph's origin).</summary>
    public double X { get; }

    /// <summary>Gets the baseline's y, down from the box's first baseline.</summary>
    public double Y { get; }

    /// <summary>Gets the font size in points.</summary>
    public double Size { get; }

    internal override MathItem Moved(double dx, double dy) => new MathGlyph(Face, GlyphId, Text, X + dx, Y + dy, Size, Color);
}

/// <summary>A filled rectangle: a fraction bar, a radical's rule, an overbar, a border.</summary>
public sealed class MathRule : MathItem
{
    internal MathRule(double x, double y, double width, double height, uint color)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
        Color = color;
    }

    /// <summary>Gets the left edge.</summary>
    public double X { get; }

    /// <summary>Gets the top edge, down from the box's first baseline.</summary>
    public double Y { get; }

    /// <summary>Gets the width in points.</summary>
    public double Width { get; }

    /// <summary>Gets the height (thickness) in points.</summary>
    public double Height { get; }

    internal override MathItem Moved(double dx, double dy) => new MathRule(X + dx, Y + dy, Width, Height, Color);
}

/// <summary>A straight stroke of a given thickness: the diagonal strikes of a border box and the slash of a skewed fraction's bar.</summary>
public sealed class MathStroke : MathItem
{
    internal MathStroke(double x1, double y1, double x2, double y2, double thickness, uint color)
    {
        X1 = x1;
        Y1 = y1;
        X2 = x2;
        Y2 = y2;
        Thickness = thickness;
        Color = color;
    }

    /// <summary>Gets the start x.</summary>
    public double X1 { get; }

    /// <summary>Gets the start y.</summary>
    public double Y1 { get; }

    /// <summary>Gets the end x.</summary>
    public double X2 { get; }

    /// <summary>Gets the end y.</summary>
    public double Y2 { get; }

    /// <summary>Gets the line width in points.</summary>
    public double Thickness { get; }

    internal override MathItem Moved(double dx, double dy) => new MathStroke(X1 + dx, Y1 + dy, X2 + dx, Y2 + dy, Thickness, Color);
}

/// <summary>
/// A laid-out equation: its extent and what is drawn. The origin is the left end of the first baseline; <see cref="Ascent"/> is the
/// height above it and <see cref="Descent"/> the depth below it, in points.
/// </summary>
/// <remarks>
/// For an equation that broke into several lines, <see cref="Ascent"/> is that of the first line and <see cref="Descent"/> is
/// everything below the first baseline, so the box is still one rectangle from <c>-Ascent</c> to <c>Descent</c>.
/// </remarks>
public sealed class MathBox
{
    internal MathBox(double width, double ascent, double descent, IReadOnlyList<MathItem> items, string linear, int lines, IReadOnlyList<MathNote> notes)
    {
        Width = width;
        Ascent = ascent;
        Descent = descent;
        Items = items;
        LinearText = linear;
        LineCount = lines;
        Notes = notes;
    }

    /// <summary>Gets the width in points.</summary>
    public double Width { get; }

    /// <summary>Gets the height above the first baseline in points.</summary>
    public double Ascent { get; }

    /// <summary>Gets the depth below the first baseline in points.</summary>
    public double Descent { get; }

    /// <summary>Gets the glyphs, rules and strokes in paint order.</summary>
    public IReadOnlyList<MathItem> Items { get; }

    /// <summary>Gets the equation in linear (UnicodeMath-style) text: its alternative text.</summary>
    public string LinearText { get; }

    /// <summary>Gets the number of lines the equation took (1 unless a display equation was broken).</summary>
    public int LineCount { get; }

    /// <summary>Gets what the layout approximated or could not do, such as a font without math data.</summary>
    public IReadOnlyList<MathNote> Notes { get; }
}
