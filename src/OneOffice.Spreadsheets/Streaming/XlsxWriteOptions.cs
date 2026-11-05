using System.IO.Compression;

namespace RecursiveAI.OneOffice.Spreadsheets.Streaming;

/// <summary>Where the streaming writer puts cell text.</summary>
public enum StreamStringStorage
{
    /// <summary>In a shared string table, one entry per distinct string (what Excel writes). The table is held in memory until the end, so it is capped by <see cref="XlsxWriteOptions.MaxSharedStrings"/>; later new strings go inline.</summary>
    Shared = 0,

    /// <summary>Inline in the cell (<c>t="inlineStr"</c>). Nothing is held; the file is larger when strings repeat.</summary>
    Inline = 1,
}

/// <summary>How the streaming writer builds the file. All values have defaults.</summary>
public sealed record XlsxWriteOptions
{
    /// <summary>Gets where cell text goes. Default <see cref="StreamStringStorage.Shared"/>.</summary>
    public StreamStringStorage StringStorage { get; init; } = StreamStringStorage.Shared;

    /// <summary>Gets the most distinct strings (and about 16 million characters in all) the shared table may hold before further new strings are written inline. Default 100,000.</summary>
    public int MaxSharedStrings { get; init; } = 100_000;

    /// <summary>Gets a value indicating whether parts may exceed 4 GiB (zip64 records). Default false; a part that grows past 4 GiB then raises <see cref="OfficeUnsupportedException"/>.</summary>
    public bool Zip64 { get; init; }

    /// <summary>Gets the deflate level. Default <see cref="System.IO.Compression.CompressionLevel.Fastest"/>.</summary>
    public CompressionLevel CompressionLevel { get; init; } = CompressionLevel.Fastest;

    /// <summary>Gets a value indicating whether dates count from 1904 (<c>workbookPr date1904</c>), which <see cref="SheetStreamWriter.WriteDate"/> then follows. Default false.</summary>
    public bool Date1904 { get; init; }

    /// <summary>Gets a complete style sheet part to write instead of the one built from <see cref="XlsxStreamWriter.AddFormat"/>; style indexes then mean what that part says. Default empty (build one).</summary>
    public ReadOnlyMemory<byte> StylesXml { get; init; }
}

/// <summary>How a sheet is set up. It is fixed when the sheet is added, because the elements that carry it come before the cells.</summary>
public sealed record SheetWriteOptions
{
    /// <summary>Gets the column widths and column styles (<c>cols</c>). Default none.</summary>
    public IReadOnlyList<ColumnProperties>? Columns { get; init; }

    /// <summary>Gets the number of rows frozen at the top. Default 0.</summary>
    public int FreezeRows { get; init; }

    /// <summary>Gets the number of columns frozen at the left. Default 0.</summary>
    public int FreezeColumns { get; init; }

    /// <summary>Gets the sheet's state. Default visible.</summary>
    public SheetState State { get; init; } = SheetState.Visible;

    /// <summary>Gets a value indicating whether gridlines show. Default true.</summary>
    public bool ShowGridLines { get; init; } = true;
}

/// <summary>Border line styles of <see cref="StreamCellFormat"/> (ECMA-376 Part 1 ST_BorderStyle, the common ones).</summary>
public enum StreamBorder
{
    /// <summary>No border.</summary>
    None = 0,

    /// <summary>A thin line.</summary>
    Thin,

    /// <summary>A medium line.</summary>
    Medium,

    /// <summary>A thick line.</summary>
    Thick,

    /// <summary>A dashed line.</summary>
    Dashed,

    /// <summary>A dotted line.</summary>
    Dotted,

    /// <summary>A double line.</summary>
    DoubleLine,

    /// <summary>A hairline.</summary>
    Hair,
}

/// <summary>Horizontal alignment of <see cref="StreamCellFormat"/>.</summary>
public enum StreamHorizontalAlignment
{
    /// <summary>By the kind of value.</summary>
    General = 0,

    /// <summary>Left.</summary>
    Left,

    /// <summary>Centred.</summary>
    Center,

    /// <summary>Right.</summary>
    Right,

    /// <summary>Fill.</summary>
    Fill,

    /// <summary>Justified.</summary>
    Justify,
}

/// <summary>Vertical alignment of <see cref="StreamCellFormat"/>.</summary>
public enum StreamVerticalAlignment
{
    /// <summary>Bottom (Excel's default).</summary>
    Bottom = 0,

    /// <summary>Top.</summary>
    Top,

    /// <summary>Centred.</summary>
    Center,

    /// <summary>Justified.</summary>
    Justify,
}

/// <summary>
/// A cell format for the streaming writer: the few things most generated files set. <see cref="XlsxStreamWriter.AddFormat"/> turns it into
/// the style index that cells carry. Equal formats share one index.
/// </summary>
public sealed record StreamCellFormat
{
    /// <summary>Gets a format for dates (<c>yyyy-mm-dd</c>).</summary>
    public static StreamCellFormat Date { get; } = new() { NumberFormat = "yyyy-mm-dd" };

    /// <summary>Gets a format for dates with a time (<c>yyyy-mm-dd hh:mm:ss</c>).</summary>
    public static StreamCellFormat DateTime { get; } = new() { NumberFormat = "yyyy-mm-dd hh:mm:ss" };

    /// <summary>Gets the number format code, such as <c>#,##0.00</c>; null for General. A code that is a built-in one uses its built-in id.</summary>
    public string? NumberFormat { get; init; }

    /// <summary>Gets a value indicating whether the font is bold.</summary>
    public bool Bold { get; init; }

    /// <summary>Gets a value indicating whether the font is italic.</summary>
    public bool Italic { get; init; }

    /// <summary>Gets a value indicating whether the text is underlined.</summary>
    public bool Underline { get; init; }

    /// <summary>Gets the font name; null for the default (Calibri).</summary>
    public string? FontName { get; init; }

    /// <summary>Gets the font size in points; 0 for the default (11).</summary>
    public double FontSize { get; init; }

    /// <summary>Gets the font colour as 0xRRGGBB; -1 for the default.</summary>
    public int FontColor { get; init; } = -1;

    /// <summary>Gets the solid fill colour as 0xRRGGBB; -1 for none.</summary>
    public int FillColor { get; init; } = -1;

    /// <summary>Gets the border line style on all four sides.</summary>
    public StreamBorder Border { get; init; }

    /// <summary>Gets the horizontal alignment.</summary>
    public StreamHorizontalAlignment Horizontal { get; init; }

    /// <summary>Gets the vertical alignment.</summary>
    public StreamVerticalAlignment Vertical { get; init; }

    /// <summary>Gets a value indicating whether long text wraps.</summary>
    public bool WrapText { get; init; }
}
