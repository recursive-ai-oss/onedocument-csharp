using BorderSide = RecursiveAI.OneDocument.Spreadsheets.BorderSide;
using OneRender.Painting;
using RecursiveAI.OneDocument.NumberFormats;
using RecursiveAI.OneDocument.Spreadsheets;

namespace RecursiveAI.OneDocument.Rendering.Spreadsheets;

/// <summary>A cell format resolved for drawing: colours as RGB, the number format parsed, the font as a text style.</summary>
internal sealed class StyleInfo
{
    public required ResolvedCellStyle Style;
    public required TextStyle Text;
    public required NumberFormat Format;
    public uint? Fill;               // a flat colour, for solid and approximated pattern fills
    public GradientFill? Gradient;
    public uint[] GradientColors = [];
    public FillPattern Pattern;
    public uint PatternForeground;
    public bool HasBorder;
    public uint[] BorderColors = new uint[5]; // left, right, top, bottom, diagonal
    public bool IsGeneral;
    public bool Visible => Fill is not null || Gradient is not null || HasBorder;
}

/// <summary>Caches the drawing form of a workbook's cell formats and converts colours.</summary>
internal sealed class StyleCache(Workbook workbook)
{
    private readonly Dictionary<int, StyleInfo> _cache = [];

    public Workbook Workbook => workbook;

    public StyleInfo Get(int index)
    {
        if (_cache.TryGetValue(index, out StyleInfo? s))
        {
            return s;
        }

        StyleSheet sheet = workbook.Styles;
        ResolvedCellStyle r = sheet.Resolve(index);
        CellFont f = r.Font;
        var info = new StyleInfo
        {
            Style = r,
            Text = ToTextStyle(f, Rgb(f.Color, 0x000000u)),
            Format = sheet.GetNumberFormat(index),
            Pattern = r.Fill.Pattern,
        };
        info.IsGeneral = string.Equals(r.NumberFormatCode, "General", StringComparison.OrdinalIgnoreCase);
        CellFill fill = r.Fill;
        if (fill.Gradient is { Stops.Count: > 0 } g)
        {
            info.Gradient = g;
            info.GradientColors = [.. g.Stops.Select(st => Rgb(st.Color, 0xFFFFFFu))];
        }
        else if (fill.Pattern == FillPattern.Solid)
        {
            info.Fill = Rgb(fill.Foreground, 0x000000u);
        }
        else if (fill.Pattern != FillPattern.None)
        {
            // A hatch is approximated by its average colour: the pattern colour over the background at the pattern's ink density.
            uint fg = Rgb(fill.Foreground, 0x000000u);
            uint bg = Rgb(fill.Background, 0xFFFFFFu);
            info.PatternForeground = fg;
            info.Fill = Blend(bg, fg, Density(fill.Pattern));
        }

        CellBorder b = r.Border;
        BorderSide[] sides = [b.Left, b.Right, b.Top, b.Bottom, b.Diagonal];
        for (int i = 0; i < 5; i++)
        {
            if (sides[i].Style != BorderLineStyle.None && (i < 4 || b.DiagonalUp || b.DiagonalDown))
            {
                info.HasBorder = true;
                info.BorderColors[i] = Rgb(sides[i].Color, 0x000000u);
            }
        }

        _cache[index] = info;
        return info;
    }

    public static TextStyle ToTextStyle(CellFont f, uint rgb) =>
        new(string.IsNullOrEmpty(f.Name) ? "Calibri" : f.Name, f.Size > 0 ? f.Size : 11, f.Bold, f.Italic, f.Strike, f.Underline, f.VerticalAlignment, rgb);

    // Automatic colours print black for text and borders.
    public uint Rgb(StyleColor c, uint auto)
    {
        if (c.Kind is ColorKind.None or ColorKind.Auto)
        {
            return auto;
        }

        return (uint)workbook.Styles.ResolveColor(c) & 0xFFFFFFu;
    }

    public static uint Blend(uint under, uint over, double alpha)
    {
        static uint Ch(uint a, uint b, double t) => (uint)Math.Clamp((int)Math.Round((a * (1 - t)) + (b * t)), 0, 255);
        uint r = Ch((under >> 16) & 255, (over >> 16) & 255, alpha);
        uint g = Ch((under >> 8) & 255, (over >> 8) & 255, alpha);
        uint bl = Ch(under & 255, over & 255, alpha);
        return (r << 16) | (g << 8) | bl;
    }

    private static double Density(FillPattern p) => p switch
    {
        FillPattern.MediumGray => 0.5,
        FillPattern.DarkGray => 0.75,
        FillPattern.LightGray => 0.25,
        FillPattern.Gray125 => 0.125,
        FillPattern.Gray0625 => 0.0625,
        FillPattern.DarkHorizontal or FillPattern.DarkVertical or FillPattern.DarkDown or FillPattern.DarkUp => 0.5,
        FillPattern.DarkGrid or FillPattern.DarkTrellis => 0.65,
        _ => 0.3,
    };

    public static PaintColor Paint(uint rgb) => PaintColor.FromBytes((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    public static PaintColor Paint(uint rgb, byte alpha) => PaintColor.FromBytes((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, alpha);

    // Grey level used when a sheet prints in black and white.
    public static uint Gray(uint rgb)
    {
        int y = (int)Math.Round((0.299 * ((rgb >> 16) & 255)) + (0.587 * ((rgb >> 8) & 255)) + (0.114 * (rgb & 255)));
        return (uint)((y << 16) | (y << 8) | y);
    }
}
