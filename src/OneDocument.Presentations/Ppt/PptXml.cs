using System.Globalization;
using System.Text;

namespace RecursiveAI.OneDocument.Ppt.Binary;

/// <summary>Small helpers shared by the writers that turn PowerPoint records into PresentationML text.</summary>
internal static class PptXml
{
    // [MS-PPT] §2.2: master units are 1/576 inch; one inch is 914,400 EMU, so one master unit is 1,587.5 EMU.
    public static long Emu(long masterUnits) => masterUnits * 3175 / 2;

    public static string Num(long value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Escape(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        StringBuilder? sb = null;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            string? replacement = c switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' => "&quot;",
                _ => null,
            };
            bool valid = c == '\t' || c == '\n' || c == '\r' || (c >= 0x20 && c < 0xD800) || (c >= 0xE000 && c <= 0xFFFD)
                || (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                || (char.IsLowSurrogate(c) && i > 0 && char.IsHighSurrogate(text[i - 1]));
            if (replacement is null && valid)
            {
                sb?.Append(c);
                continue;
            }

            if (sb is null)
            {
                sb = new StringBuilder(text.Length + 16);
                sb.Append(text, 0, i);
            }

            if (replacement is not null)
            {
                sb.Append(replacement);
            }
        }

        return sb?.ToString() ?? text;
    }

    public static string Hex(byte r, byte g, byte b) => string.Create(6, (r, g, b), static (span, c) =>
    {
        const string Digits = "0123456789ABCDEF";
        span[0] = Digits[c.r >> 4];
        span[1] = Digits[c.r & 15];
        span[2] = Digits[c.g >> 4];
        span[3] = Digits[c.g & 15];
        span[4] = Digits[c.b >> 4];
        span[5] = Digits[c.b & 15];
    });
}

/// <summary>Maps OfficeArt shape types ([MS-ODRAW] §2.4.24, MSOSPT) to DrawingML preset geometry names.</summary>
internal static class PptShapeTypes
{
    private static readonly Dictionary<int, string> Presets = new()
    {
        [1] = "rect", [2] = "roundRect", [3] = "ellipse", [4] = "diamond", [5] = "triangle", [6] = "rtTriangle",
        [7] = "parallelogram", [8] = "trapezoid", [9] = "hexagon", [10] = "octagon", [11] = "plus", [12] = "star5",
        [13] = "rightArrow", [14] = "rightArrow", [15] = "homePlate", [16] = "cube", [17] = "wedgeRoundRectCallout",
        [19] = "arc", [20] = "line", [21] = "plaque", [22] = "can", [23] = "donut",
        [32] = "straightConnector1", [33] = "bentConnector2", [34] = "bentConnector3", [35] = "bentConnector4", [36] = "bentConnector5",
        [37] = "curvedConnector2", [38] = "curvedConnector3", [39] = "curvedConnector4", [40] = "curvedConnector5",
        [41] = "callout1", [42] = "callout2", [43] = "callout3", [44] = "accentCallout1", [45] = "accentCallout2", [46] = "accentCallout3",
        [47] = "borderCallout1", [48] = "borderCallout2", [49] = "borderCallout3",
        [50] = "accentBorderCallout1", [51] = "accentBorderCallout2", [52] = "accentBorderCallout3",
        [53] = "ribbon2", [54] = "ribbon", [55] = "chevron", [56] = "pentagon", [57] = "noSmoking",
        [58] = "star8", [59] = "star16", [60] = "star32", [61] = "wedgeRectCallout", [62] = "wedgeRoundRectCallout",
        [63] = "wedgeEllipseCallout", [64] = "wave", [65] = "foldedCorner", [66] = "leftArrow", [67] = "downArrow",
        [68] = "upArrow", [69] = "leftRightArrow", [70] = "upDownArrow", [71] = "irregularSeal1", [72] = "irregularSeal2",
        [73] = "lightningBolt", [74] = "heart", [75] = "frame", [76] = "quadArrow", [77] = "leftArrowCallout",
        [78] = "rightArrowCallout", [79] = "upArrowCallout", [80] = "downArrowCallout", [81] = "leftRightArrowCallout",
        [82] = "upDownArrowCallout", [83] = "quadArrowCallout", [84] = "bevel", [85] = "leftBracket", [86] = "rightBracket",
        [87] = "leftBrace", [88] = "rightBrace", [89] = "leftUpArrow", [90] = "bentUpArrow", [91] = "bentArrow",
        [92] = "star24", [93] = "stripedRightArrow", [94] = "notchedRightArrow", [95] = "blockArc", [96] = "smileyFace",
        [97] = "verticalScroll", [98] = "horizontalScroll", [99] = "circularArrow", [100] = "circularArrow",
        [101] = "uturnArrow", [102] = "curvedRightArrow", [103] = "curvedLeftArrow", [104] = "curvedUpArrow",
        [105] = "curvedDownArrow", [106] = "cloudCallout", [107] = "ellipseRibbon2", [108] = "ellipseRibbon",
        [109] = "flowChartProcess", [110] = "flowChartDecision", [111] = "flowChartInputOutput",
        [112] = "flowChartPredefinedProcess", [113] = "flowChartInternalStorage", [114] = "flowChartDocument",
        [115] = "flowChartMultidocument", [116] = "flowChartTerminator", [117] = "flowChartPreparation",
        [118] = "flowChartManualInput", [119] = "flowChartManualOperation", [120] = "flowChartConnector",
        [121] = "flowChartPunchedCard", [122] = "flowChartPunchedTape", [123] = "flowChartSummingJunction",
        [124] = "flowChartOr", [125] = "flowChartCollate", [126] = "flowChartSort", [127] = "flowChartExtract",
        [128] = "flowChartMerge", [129] = "flowChartOfflineStorage", [130] = "flowChartOnlineStorage",
        [131] = "flowChartMagneticTape", [132] = "flowChartMagneticDisk", [133] = "flowChartMagneticDrum",
        [134] = "flowChartDisplay", [135] = "flowChartDelay",
        [202] = "rect",
    };

    /// <summary>The preset geometry name for a shape type, or null when the type has no preset.</summary>
    public static string? Preset(int shapeType) => Presets.GetValueOrDefault(shapeType);

    /// <summary>True for connector types (straight, elbow and curved connectors).</summary>
    public static bool IsConnector(int shapeType) => shapeType is 20 or (>= 32 and <= 40);

    /// <summary>The <c>ph</c> type of a PowerPoint placeholder ([MS-PPT] §2.13.21), or null when the number is unknown.</summary>
    public static string? PlaceholderType(int placeholder) => placeholder switch
    {
        1 or 13 or 17 => "title",
        2 or 6 or 12 or 14 or 18 => "body",
        3 or 15 => "ctrTitle",
        4 or 16 => "subTitle",
        5 or 11 => "sldImg",
        7 => "dt",
        8 => "sldNum",
        9 => "ftr",
        10 => "hdr",
        19 or 25 => "obj",
        20 => "chart",
        21 => "tbl",
        22 => "clipArt",
        23 => "dgm",
        24 => "media",
        26 => "pic",
        _ => null,
    };

    /// <summary>The layout <c>type</c> attribute of a <c>SlideLayoutType</c> ([MS-PPT] §2.13.25).</summary>
    public static string LayoutType(int layout) => layout switch
    {
        0 or 2 => "title",
        1 => "obj",
        7 => "titleOnly",
        8 => "twoObj",
        9 => "cust",
        10 => "objAndTwoObj",
        11 => "twoObjAndObj",
        13 => "cust",
        14 => "fourObj",
        15 => "objOnly",
        16 => "blank",
        17 => "vertTitleAndTx",
        18 => "vertTx",
        _ => "cust",
    };
}
