using System.Globalization;

namespace RecursiveAI.OneDocument.Rendering.Spreadsheets;

/// <summary>The paper sizes a worksheet's <c>pageSetup/@paperSize</c> names.</summary>
internal static class PaperSizes
{
    private const double In = 72;
    private const double Mm = 72 / 25.4;

    // ECMA-376 Part 1 §18.3.1.51 (pageSetup, paperSize): the numbered list of paper sizes, portrait, width x height. Codes without an
    // entry fall back to Letter, which is also what an absent attribute means.
    private static readonly Dictionary<int, (double W, double H)> Sizes = new()
    {
        [1] = (8.5 * In, 11 * In), [2] = (8.5 * In, 11 * In), [3] = (11 * In, 17 * In), [4] = (17 * In, 11 * In), [5] = (8.5 * In, 14 * In),
        [6] = (5.5 * In, 8.5 * In), [7] = (7.25 * In, 10.5 * In), [8] = (297 * Mm, 420 * Mm), [9] = (210 * Mm, 297 * Mm), [10] = (210 * Mm, 297 * Mm),
        [11] = (148 * Mm, 210 * Mm), [12] = (250 * Mm, 354 * Mm), [13] = (182 * Mm, 257 * Mm), [14] = (8.5 * In, 13 * In), [15] = (215 * Mm, 275 * Mm),
        [16] = (10 * In, 14 * In), [17] = (11 * In, 17 * In), [18] = (8.5 * In, 11 * In), [19] = (3.875 * In, 8.875 * In), [20] = (4.125 * In, 9.5 * In),
        [21] = (4.5 * In, 10.375 * In), [22] = (4.75 * In, 11 * In), [23] = (5 * In, 11.5 * In), [24] = (17 * In, 22 * In), [25] = (22 * In, 34 * In),
        [26] = (34 * In, 44 * In), [27] = (110 * Mm, 220 * Mm), [28] = (162 * Mm, 229 * Mm), [29] = (324 * Mm, 458 * Mm), [30] = (229 * Mm, 324 * Mm),
        [31] = (114 * Mm, 162 * Mm), [32] = (114 * Mm, 229 * Mm), [33] = (250 * Mm, 353 * Mm), [34] = (176 * Mm, 250 * Mm), [35] = (125 * Mm, 176 * Mm),
        [36] = (110 * Mm, 230 * Mm), [37] = (3.875 * In, 7.5 * In), [38] = (3.625 * In, 6.5 * In), [39] = (14.875 * In, 11 * In), [40] = (8.5 * In, 12 * In),
        [41] = (8.5 * In, 13 * In), [42] = (250 * Mm, 353 * Mm), [43] = (100 * Mm, 148 * Mm), [44] = (9 * In, 11 * In), [45] = (10 * In, 11 * In),
        [46] = (15 * In, 11 * In), [47] = (220 * Mm, 220 * Mm), [50] = (9.275 * In, 12 * In), [51] = (9.275 * In, 15 * In), [52] = (11.69 * In, 18 * In),
        [53] = (235 * Mm, 322 * Mm), [54] = (8.275 * In, 11 * In), [55] = (210 * Mm, 297 * Mm), [56] = (9.275 * In, 12 * In), [57] = (227 * Mm, 356 * Mm),
        [58] = (305 * Mm, 487 * Mm), [59] = (8.5 * In, 12.69 * In), [60] = (210 * Mm, 330 * Mm), [61] = (148 * Mm, 210 * Mm), [62] = (182 * Mm, 257 * Mm),
        [63] = (322 * Mm, 445 * Mm), [64] = (174 * Mm, 235 * Mm), [65] = (201 * Mm, 276 * Mm), [66] = (420 * Mm, 594 * Mm), [67] = (297 * Mm, 420 * Mm),
        [68] = (322 * Mm, 445 * Mm),
    };

    public static (double Width, double Height) Get(int code) => Sizes.TryGetValue(code, out (double W, double H) s) ? s : Sizes[1];

    // Universal measures ("210mm", "8.5in", "21cm", "595pt", "12pc", "1pi") as points.
    public static double? ParseLength(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length < 3)
        {
            return null;
        }

        string unit = text[^2..];
        if (!double.TryParse(text[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || !(v > 0) || !double.IsFinite(v))
        {
            return null;
        }

        return unit switch { "mm" => v * Mm, "cm" => v * Mm * 10, "in" => v * In, "pt" => v, "pc" or "pi" => v * 12, _ => null };
    }
}
