using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Documents.Styles;
using RecursiveAI.OneDocument.Rendering.Drawing;

namespace RecursiveAI.OneDocument.Layout;

// ECMA-376 Part 1 §17.3.1.11 (framePr): a paragraph (or a run of paragraphs with the same frame properties) set in a box that sits at
// its own position and has text wrap around it, or a drop cap that the text after it wraps around.
internal sealed class FrameInfo
{
    public double? Width; // points; null: as wide as the text needs
    public double? Height;
    public string HeightRule = "auto";
    public double SpaceH; // hSpace: distance from the text to the sides
    public double SpaceV;
    public string Wrap = "auto";
    public string HAnchor = "text";
    public string VAnchor = "text";
    public double? X;
    public double? Y;
    public string? XAlign;
    public string? YAlign;
    public string DropCap = "none";
    public int Lines = 1;

    public string Key = string.Empty;

    public static FrameInfo? Read(ResolvedParagraphProperties pp)
    {
        if (pp.Get("framePr") is not { } f)
        {
            return null;
        }

        double? Tw(string name) => f.Number(name) is { } v ? v / 20.0 : null;
        var info = new FrameInfo
        {
            Width = Tw("w"),
            Height = Tw("h"),
            HeightRule = f.Attribute("hRule") ?? "auto",
            SpaceH = Tw("hSpace") ?? 0,
            SpaceV = Tw("vSpace") ?? 0,
            Wrap = f.Attribute("wrap") ?? "auto",
            HAnchor = f.Attribute("hAnchor") ?? "text",
            VAnchor = f.Attribute("vAnchor") ?? "text",
            X = Tw("x"),
            Y = Tw("y"),
            XAlign = f.Attribute("xAlign"),
            YAlign = f.Attribute("yAlign"),
            DropCap = f.Attribute("dropCap") ?? "none",
            Lines = (int)Math.Clamp(f.Number("lines") ?? 1, 1, 10),
        };
        info.Key = string.Join('|', info.Width, info.Height, info.HeightRule, info.SpaceH, info.SpaceV, info.Wrap, info.HAnchor, info.VAnchor, info.X, info.Y, info.XAlign, info.YAlign);
        return info;
    }

    public DrawingWrap WrapKind => Wrap switch
    {
        "notBeside" => DrawingWrap.TopAndBottom,
        "none" => DrawingWrap.None,
        "tight" => DrawingWrap.Tight,
        "through" => DrawingWrap.Through,
        _ => DrawingWrap.Square,
    };
}

// A run of paragraphs that make up one frame, in the flow's list of blocks. It is laid out when it is placed, because its position
// decides the column width it may take.
internal sealed class FormattedFrameBlock : FormattedParagraph
{
    public required FrameInfo Frame;
    public readonly List<Paragraph> Paragraphs = [];
}
