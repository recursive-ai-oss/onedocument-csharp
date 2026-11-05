using OneFont;

namespace RecursiveAI.OneDocument.Layout;

// What the painter needs to know about a run beyond its position. Kept internal: the public tree describes geometry and text, the
// painter reads formatting from here so the tree's shape stays small while later layout stages add to it.
internal sealed class RunPaintInfo
{
    public uint Color; // 0xRRGGBB
    public string? Underline; // w:u value, null for none
    public uint? UnderlineColor;
    public bool Strike;
    public bool DoubleStrike;
    public uint? Highlight;
    public uint? Shading;
    public string? LinkTarget;
    public string? Language;
    public double BaselineShift; // points, positive raises
    public double Scale = 1.0; // horizontal glyph scale (w:w / 100)
    public bool IsArtifact;
    public string Leader = string.Empty; // tab leader: dot, hyphen, underscore, middleDot, heavy; empty for none
}

internal sealed class GlyphData
{
    public required FontFace Face;
    public required int[] GlyphIds;
    public required float[] X; // pen x of each glyph relative to the run's left edge, unscaled by RunPaintInfo.Scale, visual order
    public required float[] Y; // offsets from the baseline in points, positive down; may be empty
    public required int[] Clusters; // UTF-16 index into the run text of each glyph's cluster start
    public bool RightToLeft;
    public string? ShownText; // the characters the glyphs show when they differ from the run text (capitals), same length
}

internal readonly record struct BorderInfo(double Width, double Space, uint Color, string Style);

internal sealed class ParagraphPaintInfo
{
    public uint? Shading;
    public BorderInfo? Top;
    public BorderInfo? Bottom;
    public BorderInfo? Left;
    public BorderInfo? Right;
    public double LeftEdge; // x of the paragraph's left text boundary relative to the page's content area
    public double RightEdge;
}
