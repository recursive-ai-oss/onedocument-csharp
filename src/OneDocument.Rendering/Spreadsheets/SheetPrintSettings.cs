using System.Globalization;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Spreadsheets;

namespace RecursiveAI.OneDocument.Rendering.Spreadsheets;

/// <summary>The order in which the pages of a printed range are numbered.</summary>
public enum SheetPageOrder
{
    /// <summary>Down the first strip of columns, then over to the next strip (the default).</summary>
    DownThenOver,

    /// <summary>Across the first strip of rows, then down to the next strip.</summary>
    OverThenDown,
}

/// <summary>How cells that hold an error are printed (<c>pageSetup/@errors</c>).</summary>
public enum SheetErrorPrinting
{
    /// <summary>The error is printed as shown on screen.</summary>
    Displayed,

    /// <summary>The cell prints empty.</summary>
    Blank,

    /// <summary>The cell prints <c>--</c>.</summary>
    Dash,

    /// <summary>The cell prints <c>#N/A</c>.</summary>
    NotAvailable,
}

/// <summary>The page headers and footers of a sheet (<c>headerFooter</c>), as the raw codes Excel stores.</summary>
/// <param name="DifferentOddEven">Even pages use <see cref="EvenHeader"/> and <see cref="EvenFooter"/>.</param>
/// <param name="DifferentFirst">The first page uses <see cref="FirstHeader"/> and <see cref="FirstFooter"/>.</param>
/// <param name="ScaleWithDocument">The header and footer text scales with the page scale.</param>
/// <param name="AlignWithMargins">The header and footer line up with the page margins instead of the paper edge.</param>
/// <param name="OddHeader">The header code string for odd pages (and all pages unless the others are on).</param>
/// <param name="OddFooter">The footer code string for odd pages.</param>
/// <param name="EvenHeader">The header for even pages.</param>
/// <param name="EvenFooter">The footer for even pages.</param>
/// <param name="FirstHeader">The header for the first page.</param>
/// <param name="FirstFooter">The footer for the first page.</param>
public sealed record SheetHeaderFooter(
    bool DifferentOddEven,
    bool DifferentFirst,
    bool ScaleWithDocument,
    bool AlignWithMargins,
    string? OddHeader,
    string? OddFooter,
    string? EvenHeader,
    string? EvenFooter,
    string? FirstHeader,
    string? FirstFooter)
{
    /// <summary>Gets the header code string for a page.</summary>
    /// <param name="pageInSheet">The 1-based page number within the printed sheet (the first page of a sheet is 1).</param>
    /// <param name="pageNumber">The printed page number, which decides odd and even.</param>
    /// <returns>The code string, or null for none.</returns>
    public string? HeaderFor(int pageInSheet, int pageNumber) =>
        DifferentFirst && pageInSheet == 1 ? FirstHeader : DifferentOddEven && pageNumber % 2 == 0 ? EvenHeader : OddHeader;

    /// <summary>Gets the footer code string for a page; see <see cref="HeaderFor"/>.</summary>
    /// <param name="pageInSheet">The 1-based page number within the printed sheet.</param>
    /// <param name="pageNumber">The printed page number.</param>
    /// <returns>The code string, or null for none.</returns>
    public string? FooterFor(int pageInSheet, int pageNumber) =>
        DifferentFirst && pageInSheet == 1 ? FirstFooter : DifferentOddEven && pageNumber % 2 == 0 ? EvenFooter : OddFooter;
}

/// <summary>
/// What a worksheet says about printing: paper, orientation, margins, scale, fit-to-page, breaks, centring, gridlines and headings,
/// error display, headers and footers, print area and print titles. Read from the sheet's markup and the workbook's defined names;
/// nothing is written. Lengths are points.
/// </summary>
public sealed class SheetPrintSettings
{
    /// <summary>Gets the paper size code (<c>pageSetup/@paperSize</c>, ECMA-376 Part 1 §18.3.1.51); 1 (Letter) when absent.</summary>
    public int PaperSize { get; init; } = 1;

    /// <summary>Gets the paper width in points, orientation applied.</summary>
    public double PageWidth { get; init; } = 612;

    /// <summary>Gets the paper height in points, orientation applied.</summary>
    public double PageHeight { get; init; } = 792;

    /// <summary>Gets whether the paper is turned to landscape.</summary>
    public bool Landscape { get; init; }

    /// <summary>Gets the scale in percent (10 to 400) used when <see cref="FitToPage"/> is off. Default 100.</summary>
    public int Scale { get; init; } = 100;

    /// <summary>Gets whether the range is shrunk to <see cref="FitToWidth"/> by <see cref="FitToHeight"/> pages (<c>sheetPr/pageSetUpPr/@fitToPage</c>).</summary>
    public bool FitToPage { get; init; }

    /// <summary>Gets the pages across for fit-to-page; 0 means as many as needed. Default 1.</summary>
    public int FitToWidth { get; init; } = 1;

    /// <summary>Gets the pages down for fit-to-page; 0 means as many as needed. Default 1.</summary>
    public int FitToHeight { get; init; } = 1;

    /// <summary>Gets the number of the sheet's first page when <see cref="UseFirstPageNumber"/> is on.</summary>
    public int FirstPageNumber { get; init; } = 1;

    /// <summary>Gets whether the sheet starts its own numbering at <see cref="FirstPageNumber"/>; otherwise numbering continues from the previous sheet.</summary>
    public bool UseFirstPageNumber { get; init; }

    /// <summary>Gets the page order.</summary>
    public SheetPageOrder PageOrder { get; init; }

    /// <summary>Gets how error cells print.</summary>
    public SheetErrorPrinting Errors { get; init; }

    /// <summary>Gets the left margin in points. Default 0.7 inch.</summary>
    public double MarginLeft { get; init; } = 50.4;

    /// <summary>Gets the right margin in points. Default 0.7 inch.</summary>
    public double MarginRight { get; init; } = 50.4;

    /// <summary>Gets the top margin in points. Default 0.75 inch.</summary>
    public double MarginTop { get; init; } = 54;

    /// <summary>Gets the bottom margin in points. Default 0.75 inch.</summary>
    public double MarginBottom { get; init; } = 54;

    /// <summary>Gets the distance of the header from the top edge of the paper, in points. Default 0.3 inch.</summary>
    public double MarginHeader { get; init; } = 21.6;

    /// <summary>Gets the distance of the footer from the bottom edge of the paper, in points. Default 0.3 inch.</summary>
    public double MarginFooter { get; init; } = 21.6;

    /// <summary>Gets whether the range is centred between the left and right margins.</summary>
    public bool CenterHorizontally { get; init; }

    /// <summary>Gets whether the range is centred between the top and bottom margins.</summary>
    public bool CenterVertically { get; init; }

    /// <summary>Gets whether gridlines print (<c>printOptions/@gridLines</c>).</summary>
    public bool PrintGridlines { get; init; }

    /// <summary>Gets whether row and column headings print (<c>printOptions/@headings</c>).</summary>
    public bool PrintHeadings { get; init; }

    /// <summary>Gets whether the sheet prints in black and white (colours become grey levels).</summary>
    public bool BlackAndWhite { get; init; }

    /// <summary>Gets the headers and footers.</summary>
    public SheetHeaderFooter HeaderFooter { get; init; } = new(false, false, true, true, null, null, null, null, null, null);

    /// <summary>Gets the manual row breaks: each value is the last row (1-based) of a page.</summary>
    public IReadOnlyList<int> RowBreaks { get; init; } = [];

    /// <summary>Gets the manual column breaks: each value is the last column (1-based) of a page.</summary>
    public IReadOnlyList<int> ColumnBreaks { get; init; } = [];

    /// <summary>Gets the print area ranges (<c>_xlnm.Print_Area</c>); empty when none is set, so the used range prints.</summary>
    public IReadOnlyList<CellRange> PrintArea { get; init; } = [];

    /// <summary>Gets the first and last rows repeated at the top of every page (<c>_xlnm.Print_Titles</c>), or (0, 0).</summary>
    public (int First, int Last) TitleRows { get; init; }

    /// <summary>Gets the first and last columns repeated at the left of every page, or (0, 0).</summary>
    public (int First, int Last) TitleColumns { get; init; }

    /// <summary>Gets the default row height in points, or 0 when the sheet does not say.</summary>
    public double DefaultRowHeight { get; init; }

    /// <summary>Gets the default column width in characters of the default font, or 0 when the sheet does not say.</summary>
    public double DefaultColumnWidth { get; init; }

    /// <summary>Gets the base column width in characters (<c>sheetFormatPr/@baseColWidth</c>). Default 8.</summary>
    public int BaseColumnWidth { get; init; } = 8;

    /// <summary>Gets whether rows are hidden unless they say otherwise (<c>sheetFormatPr/@zeroHeight</c>).</summary>
    public bool RowsHiddenByDefault { get; init; }

    /// <summary>
    /// Reads the settings of a worksheet.
    /// </summary>
    /// <param name="sheet">The worksheet.</param>
    /// <returns>The settings; absent elements give Excel's defaults.</returns>
    /// <exception cref="NotSupportedException">The sheet is not a worksheet.</exception>
    public static SheetPrintSettings Read(Worksheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        MarkupNode root = sheet.LayoutMarkup;
        MarkupNode page = X.Child(root, "pageSetup");
        MarkupNode margins = X.Child(root, "pageMargins");
        MarkupNode options = X.Child(root, "printOptions");
        MarkupNode hf = X.Child(root, "headerFooter");
        MarkupNode fmt = X.Child(root, "sheetFormatPr");
        MarkupNode pr = X.Child(root, "sheetPr");

        int paper = X.Int(page.GetAttribute("paperSize"), 1);
        bool landscape = page.GetAttribute("orientation") == "landscape";
        (double w, double h) = PaperSizes.Get(paper);
        if (PaperSizes.ParseLength(page.GetAttribute("paperWidth")) is { } pw && PaperSizes.ParseLength(page.GetAttribute("paperHeight")) is { } ph
            && pw is >= 18 and <= 14400 && ph is >= 18 and <= 14400)
        {
            // ECMA-376 Part 1 §18.3.1.51: paperWidth and paperHeight give a custom size and override paperSize.
            (w, h) = (pw, ph);
        }

        if (landscape)
        {
            (w, h) = (h, w);
        }

        bool fit = X.Flag(X.Child(pr, "pageSetUpPr").GetAttribute("fitToPage"));
        int scale = X.Int(page.GetAttribute("scale"), 100);
        var settings = new SheetPrintSettings
        {
            PaperSize = paper,
            PageWidth = w,
            PageHeight = h,
            Landscape = landscape,
            Scale = Math.Clamp(scale, 10, 400),
            FitToPage = fit,
            FitToWidth = Math.Max(0, X.Int(page.GetAttribute("fitToWidth"), 1)),
            FitToHeight = Math.Max(0, X.Int(page.GetAttribute("fitToHeight"), 1)),
            FirstPageNumber = X.Int(page.GetAttribute("firstPageNumber"), 1),
            UseFirstPageNumber = X.Flag(page.GetAttribute("useFirstPageNumber")),
            PageOrder = page.GetAttribute("pageOrder") == "overThenDown" ? SheetPageOrder.OverThenDown : SheetPageOrder.DownThenOver,
            Errors = page.GetAttribute("errors") switch { "blank" => SheetErrorPrinting.Blank, "dash" => SheetErrorPrinting.Dash, "NA" => SheetErrorPrinting.NotAvailable, _ => SheetErrorPrinting.Displayed },
            BlackAndWhite = X.Flag(page.GetAttribute("blackAndWhite")),
            MarginLeft = Margin(margins, "left", 0.7, w, 0.7, "right"),
            MarginRight = Margin(margins, "right", 0.7, w, 0.7, "left"),
            MarginTop = Margin(margins, "top", 0.75, h, 0.75, "bottom"),
            MarginBottom = Margin(margins, "bottom", 0.75, h, 0.75, "top"),
            MarginHeader = Math.Clamp(X.Double(margins.GetAttribute("header"), 0.3), 0, 100) * 72,
            MarginFooter = Math.Clamp(X.Double(margins.GetAttribute("footer"), 0.3), 0, 100) * 72,
            CenterHorizontally = X.Flag(options.GetAttribute("horizontalCentered")),
            CenterVertically = X.Flag(options.GetAttribute("verticalCentered")),
            PrintGridlines = X.Flag(options.GetAttribute("gridLines")),
            PrintHeadings = X.Flag(options.GetAttribute("headings")),
            HeaderFooter = ReadHeaderFooter(hf),
            RowBreaks = ReadBreaks(X.Child(root, "rowBreaks")),
            ColumnBreaks = ReadBreaks(X.Child(root, "colBreaks")),
            // Excel's own limits: a row is at most 409.5 pt high and a column 255 characters wide.
            DefaultRowHeight = Math.Clamp(X.Double(fmt.GetAttribute("defaultRowHeight"), 0), 0, 409.5),
            DefaultColumnWidth = Math.Clamp(X.Double(fmt.GetAttribute("defaultColWidth"), 0), 0, 255),
            BaseColumnWidth = Math.Clamp(X.Int(fmt.GetAttribute("baseColWidth"), 8), 1, 255),
            RowsHiddenByDefault = X.Flag(fmt.GetAttribute("zeroHeight")),
        };

        return ApplyNames(settings, sheet);
    }

    // A margin in points. One that leaves under half an inch of the page between it and the opposite margin is not believable; both fall back to the default.
    private static double Margin(MarkupNode margins, string name, double fallback, double pageExtent, double oppositeFallback, string opposite)
    {
        double v = X.Double(margins.GetAttribute(name), fallback);
        double o = X.Double(margins.GetAttribute(opposite), oppositeFallback);
        if (v < 0 || o < 0 || (v + o) * 72 > pageExtent - 36)
        {
            return fallback * 72;
        }

        return v * 72;
    }

    private static SheetPrintSettings ApplyNames(SheetPrintSettings s, Worksheet sheet)
    {
        var areas = new List<CellRange>();
        (int, int) titleRows = default;
        (int, int) titleColumns = default;
        foreach (DefinedName name in sheet.Workbook.Names.ForSheet(sheet))
        {
            if (name.BuiltIn == BuiltInName.PrintArea)
            {
                foreach (string part in SplitReferences(name.Formula))
                {
                    if (TryParseRange(part, sheet.Name, out CellRange r))
                    {
                        areas.Add(r);
                    }
                }
            }
            else if (name.BuiltIn == BuiltInName.PrintTitles)
            {
                foreach (string part in SplitReferences(name.Formula))
                {
                    string? text = StripSheet(part, sheet.Name);
                    if (text is null)
                    {
                        continue;
                    }

                    text = text.Replace("$", string.Empty, StringComparison.Ordinal);
                    int colon = text.IndexOf(':', StringComparison.Ordinal);
                    string a = colon < 0 ? text : text[..colon];
                    string b = colon < 0 ? text : text[(colon + 1)..];
                    if (int.TryParse(a, NumberStyles.Integer, CultureInfo.InvariantCulture, out int r1) && int.TryParse(b, NumberStyles.Integer, CultureInfo.InvariantCulture, out int r2))
                    {
                        titleRows = (Math.Min(r1, r2), Math.Max(r1, r2));
                    }
                    else if (CellAddress.TryParseColumn(a, out int c1) && CellAddress.TryParseColumn(b, out int c2))
                    {
                        titleColumns = (Math.Min(c1, c2), Math.Max(c1, c2));
                    }
                }
            }
        }

        return new SheetPrintSettings
        {
            PaperSize = s.PaperSize, PageWidth = s.PageWidth, PageHeight = s.PageHeight, Landscape = s.Landscape, Scale = s.Scale, FitToPage = s.FitToPage,
            FitToWidth = s.FitToWidth, FitToHeight = s.FitToHeight, FirstPageNumber = s.FirstPageNumber, UseFirstPageNumber = s.UseFirstPageNumber,
            PageOrder = s.PageOrder, Errors = s.Errors, MarginLeft = s.MarginLeft, MarginRight = s.MarginRight, MarginTop = s.MarginTop,
            MarginBottom = s.MarginBottom, MarginHeader = s.MarginHeader, MarginFooter = s.MarginFooter, CenterHorizontally = s.CenterHorizontally,
            CenterVertically = s.CenterVertically, PrintGridlines = s.PrintGridlines, PrintHeadings = s.PrintHeadings, BlackAndWhite = s.BlackAndWhite,
            HeaderFooter = s.HeaderFooter, RowBreaks = s.RowBreaks, ColumnBreaks = s.ColumnBreaks, DefaultRowHeight = s.DefaultRowHeight,
            DefaultColumnWidth = s.DefaultColumnWidth, BaseColumnWidth = s.BaseColumnWidth, RowsHiddenByDefault = s.RowsHiddenByDefault,
            PrintArea = areas, TitleRows = titleRows, TitleColumns = titleColumns,
        };
    }

    // A defined name's formula is a comma-separated union of references; sheet names may be quoted and may hold commas.
    internal static List<string> SplitReferences(string formula)
    {
        var parts = new List<string>();
        int start = 0;
        bool quoted = false;
        for (int i = 0; i < formula.Length; i++)
        {
            char c = formula[i];
            if (c == '\'')
            {
                quoted = !quoted;
            }
            else if (c == ',' && !quoted)
            {
                parts.Add(formula[start..i].Trim());
                start = i + 1;
            }
        }

        parts.Add(formula[start..].Trim());
        return parts;
    }

    // Returns the reference with its sheet prefix removed, or null when the prefix names a different sheet.
    private static string? StripSheet(string reference, string sheetName)
    {
        int bang = reference.LastIndexOf('!');
        if (bang < 0)
        {
            return reference;
        }

        string prefix = reference[..bang];
        if (prefix.Length >= 2 && prefix[0] == '\'' && prefix[^1] == '\'')
        {
            prefix = prefix[1..^1].Replace("''", "'", StringComparison.Ordinal);
        }

        return string.Equals(prefix, sheetName, StringComparison.OrdinalIgnoreCase) ? reference[(bang + 1)..] : null;
    }

    private static bool TryParseRange(string reference, string sheetName, out CellRange range)
    {
        range = default;
        string? text = StripSheet(reference, sheetName);
        if (text is null)
        {
            return false;
        }

        text = text.Replace("$", string.Empty, StringComparison.Ordinal);
        if (CellRange.TryParse(text, out range))
        {
            return true;
        }

        // Whole rows (1:5) and whole columns (A:C) as print areas.
        int colon = text.IndexOf(':', StringComparison.Ordinal);
        if (colon > 0)
        {
            string a = text[..colon];
            string b = text[(colon + 1)..];
            if (int.TryParse(a, NumberStyles.Integer, CultureInfo.InvariantCulture, out int r1) && int.TryParse(b, NumberStyles.Integer, CultureInfo.InvariantCulture, out int r2))
            {
                range = new CellRange(new CellAddress(Math.Min(r1, r2), 1), new CellAddress(Math.Max(r1, r2), CellAddress.MaxColumns));
                return true;
            }

            if (CellAddress.TryParseColumn(a, out int c1) && CellAddress.TryParseColumn(b, out int c2))
            {
                range = new CellRange(new CellAddress(1, Math.Min(c1, c2)), new CellAddress(CellAddress.MaxRows, Math.Max(c1, c2)));
                return true;
            }
        }

        return false;
    }

    private static SheetHeaderFooter ReadHeaderFooter(MarkupNode hf)
    {
        string? Text(string name) => X.Child(hf, name) is { IsNull: false } n ? n.InnerText : null;
        return new SheetHeaderFooter(
            X.Flag(hf.GetAttribute("differentOddEven")),
            X.Flag(hf.GetAttribute("differentFirst")),
            hf.GetAttribute("scaleWithDoc") is not ("0" or "false"),
            hf.GetAttribute("alignWithMargins") is not ("0" or "false"),
            Text("oddHeader"), Text("oddFooter"), Text("evenHeader"), Text("evenFooter"), Text("firstHeader"), Text("firstFooter"));
    }

    // ECMA-376 Part 1 §18.3.1.1: a brk's id is the row (or column) after which the break falls, so it is the last one on its page. Only manual breaks count.
    private static List<int> ReadBreaks(MarkupNode node)
    {
        var list = new List<int>();
        if (node.IsNull)
        {
            return list;
        }

        foreach (MarkupNode b in node.ChildElements)
        {
            if (b.LocalNameString == "brk" && X.Flag(b.GetAttribute("man")) && X.Int(b.GetAttribute("id"), 0) is > 0 and int id)
            {
                list.Add(id);
            }
        }

        list.Sort();
        return list;
    }
}

internal static class X
{
    public static MarkupNode Child(MarkupNode parent, string local)
    {
        if (parent.IsNull)
        {
            return default;
        }

        foreach (MarkupNode c in parent.ChildElements)
        {
            if (c.LocalNameString == local)
            {
                return c;
            }
        }

        return default;
    }

    public static IEnumerable<MarkupNode> Descendants(MarkupNode node)
    {
        foreach (MarkupNode c in node.ChildElements)
        {
            yield return c;
            foreach (MarkupNode d in Descendants(c))
            {
                yield return d;
            }
        }
    }

    public static bool Flag(string? s) => s is "1" or "true";

    public static int Int(string? s, int fallback) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;

    public static double Double(string? s, double fallback) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && double.IsFinite(v) ? v : fallback;
}
