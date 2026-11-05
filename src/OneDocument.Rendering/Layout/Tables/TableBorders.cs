using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Documents.Styles;

namespace RecursiveAI.OneDocument.Layout;

// Which border wins where two cells (or a cell and the table) name the same edge. ECMA-376 Part 1 §17.4.66 (tcBorders) and
// §17.4.38 (tblBorders): a border set on the cell beats the table's; between two borders of the same origin the heavier wins (width
// times the number of lines), then the style that comes later in the precedence list below, then the darker colour (lower
// R + B + 2G). A border whose style is none or nil is a border of weight 0 that still counts as set.
internal static class TableBorders
{
    private sealed record Candidate(int Level, CellEdge? Edge);

    // Heavier-looking styles later; the list only breaks ties between equal weights.
    private static readonly string[] Precedence =
    [
        "single", "thick", "double", "dotted", "dashed", "dotDash", "dotDotDash", "triple", "thinThickSmallGap", "thickThinSmallGap",
        "thinThickThinSmallGap", "thinThickMediumGap", "thickThinMediumGap", "thinThickThinMediumGap", "thinThickLargeGap",
        "thickThinLargeGap", "thinThickThinLargeGap", "wave", "doubleWave", "dashSmallGap", "dashDotStroked", "threeDEmboss",
        "threeDEngrave", "outset", "inset",
    ];

    /// <summary>Resolves the four edges of every cell and says which cells own their right and bottom edge for painting.</summary>
    public static void Resolve(LogicalTable model, IReadOnlyDictionary<LogicalCell, ResolvedProperties> props, IList<FormattedCell> cells, bool rtl, bool separate)
    {
        var byCell = cells.ToDictionary(static c => c.Logical);
        foreach (FormattedCell fc in cells)
        {
            LogicalCell c = fc.Logical;
            Candidate? top = Own(model, props, c, "top", rtl);
            Candidate? bottom = Own(model, props, c, "bottom", rtl);
            Candidate? left = Own(model, props, c, "left", rtl);
            Candidate? right = Own(model, props, c, "right", rtl);

            LogicalCell? nTop = c.Row > 0 ? model[c.Row - 1, c.Column] : null;
            LogicalCell? nBottom = model[c.Row + c.RowSpan, c.Column];
            LogicalCell? nLeft = c.Column > 0 ? model[c.Row, c.Column - 1] : null;
            LogicalCell? nRight = model[c.Row, c.Column + c.ColumnSpan];

            if (!separate)
            {
                top = Pick(top, nTop is null ? null : Own(model, props, nTop, "bottom", rtl));
                bottom = Pick(bottom, nBottom is null ? null : Own(model, props, nBottom, "top", rtl));
                left = Pick(left, nLeft is null ? null : Own(model, props, nLeft, "right", rtl));
                right = Pick(right, nRight is null ? null : Own(model, props, nRight, "left", rtl));
            }

            fc.Top = top?.Edge;
            fc.Bottom = bottom?.Edge;
            fc.Left = left?.Edge;
            fc.Right = right?.Edge;
            fc.PaintRight = separate || nRight is null;
            fc.PaintBottom = separate || nBottom is null;
        }

        _ = byCell;
    }

    // The border a cell asks for on one side, physical left/right. Cell-level (tcBorders) first, then the table's (outer or inside).
    private static Candidate? Own(LogicalTable model, IReadOnlyDictionary<LogicalCell, ResolvedProperties> props, LogicalCell c, string side, bool rtl)
    {
        ResolvedProperties bag = props[c];
        if (Find(bag, "tcBorders", side, rtl) is { } cellLevel)
        {
            return new Candidate(2, ToEdge(cellLevel));
        }

        string table = side switch
        {
            "left" => c.Column == 0 ? "left" : "insideV",
            "right" => c.Column + c.ColumnSpan >= model.ColumnCount ? "right" : "insideV",
            "top" => c.Row == 0 ? "top" : "insideH",
            _ => c.Row + c.RowSpan >= model.RowCount ? "bottom" : "insideH",
        };
        return Find(bag, "tblBorders", table, rtl) is { } t ? new Candidate(1, ToEdge(t)) : null;
    }

    // ECMA-376 Part 1 §17.4.4 (end) and §17.4.67 (start): in a left-to-right table start is the left side; in a right-to-left one it is the right.
    private static ResolvedProperty? Find(ResolvedProperties bag, string container, string side, bool rtl)
    {
        if (bag.Get(container + "/" + side) is { } direct)
        {
            return direct;
        }

        string? alias = side switch
        {
            "left" => rtl ? "end" : "start",
            "right" => rtl ? "start" : "end",
            _ => null,
        };
        return alias is null ? null : bag.Get(container + "/" + alias);
    }

    // ECMA-376 Part 1 §17.3.4 (border): sz is in eighths of a point, 2 to 96; color is hex or "auto" (black).
    private static CellEdge? ToEdge(ResolvedProperty b)
    {
        string style = b.Attribute("val") ?? "single";
        if (style is "nil" or "none")
        {
            return null;
        }

        double w = Math.Clamp(b.Number("sz") ?? 4, 2, 96) / 8.0;
        uint color = b.Attribute("color") is { Length: 6 } c && uint.TryParse(c, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out uint v) ? v : 0u;
        return new CellEdge(w, color, style);
    }

    private static Candidate? Pick(Candidate? a, Candidate? b)
    {
        if (a is null)
        {
            return b;
        }

        if (b is null)
        {
            return a;
        }

        if (a.Level != b.Level)
        {
            return a.Level > b.Level ? a : b;
        }

        double wa = Weight(a.Edge);
        double wb = Weight(b.Edge);
        if (Math.Abs(wa - wb) > 1e-9)
        {
            return wa > wb ? a : b;
        }

        if (a.Edge is null || b.Edge is null)
        {
            return a;
        }

        int ra = Array.IndexOf(Precedence, a.Edge.Style);
        int rb = Array.IndexOf(Precedence, b.Edge.Style);
        if (ra != rb)
        {
            return ra > rb ? a : b;
        }

        int ba = Brightness(a.Edge.Color);
        int bb = Brightness(b.Edge.Color);
        return ba <= bb ? a : b;
    }

    public static double Weight(CellEdge? e) => e is null ? 0 : e.Width * Lines(e.Style);

    public static int Lines(string style) => style switch
    {
        "double" or "thinThickSmallGap" or "thickThinSmallGap" or "thinThickMediumGap" or "thickThinMediumGap" or "thinThickLargeGap" or "thickThinLargeGap" or "doubleWave" => 2,
        "triple" or "thinThickThinSmallGap" or "thinThickThinMediumGap" or "thinThickThinLargeGap" => 3,
        _ => 1,
    };

    private static int Brightness(uint rgb) => (int)(((rgb >> 16) & 0xFF) + (rgb & 0xFF) + (2 * ((rgb >> 8) & 0xFF)));

    /// <summary>Resolves a test-visible conflict between two edges of the same origin; the heavier wins, then style, then colour.</summary>
    public static CellEdge? Conflict(CellEdge? a, CellEdge? b) => Pick(new Candidate(1, a), new Candidate(1, b))?.Edge;
}
