namespace RecursiveAI.OneDocument.Documents.Html;

// The importer reads HTML into this small model first and writes WordprocessingML from it second. Templating flattens the same model
// into its own paragraphs, so there is one HTML path.

/// <summary>Character formatting of a run. Compared by value, so a run only carries what differs from its paragraph style.</summary>
internal readonly record struct HRunProps(
    bool Bold = false,
    bool Italic = false,
    bool Underline = false,
    bool Strike = false,
    string? Color = null,
    string? Shade = null,
    int SizeHalfPoints = 0,
    string? Font = null,
    int VertAlign = 0,
    int Caps = 0,
    bool Rtl = false,
    string? Lang = null);

/// <summary>A paragraph border side: a CSS style word, a width in eighths of a point and an RGB colour (or null for automatic).</summary>
internal readonly record struct HBorder(string Style, int Eighths, string? Color)
{
    public bool IsNone => Style is "none" or "hidden" || Eighths <= 0;
}

/// <summary>Paragraph formatting in twips.</summary>
internal readonly record struct HParaProps(
    string? Align = null,
    int IndentLeft = 0,
    int IndentRight = 0,
    int IndentFirst = 0,
    int Before = 0,
    int After = 0,
    int Line = 0,
    string? LineRule = null,
    bool Rtl = false,
    bool PageBreakBefore = false,
    bool KeepNext = false,
    string? Shade = null,
    HBorder? BorderTop = null,
    HBorder? BorderRight = null,
    HBorder? BorderBottom = null,
    HBorder? BorderLeft = null);

internal abstract class HInline
{
}

internal sealed class HText(string text, HRunProps props, string? link) : HInline
{
    public string Text { get; set; } = text;

    public HRunProps Props { get; } = props;

    /// <summary>An external URL, or "#name" for a place in this document; null for no link.</summary>
    public string? Link { get; } = link;
}

internal sealed class HBreak(bool page) : HInline
{
    public bool Page { get; } = page;
}

internal sealed class HTab : HInline
{
}

internal sealed class HBookmark(string name) : HInline
{
    public string Name { get; } = name;
}

internal sealed class HImage(byte[] data, string extension, string contentType, long widthEmu, long heightEmu, string alt, string? link) : HInline
{
    public byte[] Data { get; } = data;

    public string Extension { get; } = extension;

    public string ContentType { get; } = contentType;

    public long WidthEmu { get; } = widthEmu;

    public long HeightEmu { get; } = heightEmu;

    public string Alt { get; } = alt;

    public string? Link { get; } = link;
}

internal abstract class HBlock
{
}

/// <summary>A numbered or bulleted list a paragraph belongs to: one per ul/ol element.</summary>
internal sealed class HList(int id, bool ordered, string format, int start, int depth, string bullet)
{
    public int Id { get; } = id;

    public bool Ordered { get; } = ordered;

    /// <summary>The WordprocessingML number format (decimal, lowerLetter, upperLetter, lowerRoman, upperRoman, bullet).</summary>
    public string Format { get; } = format;

    public int Start { get; } = start;

    public int Depth { get; } = depth;

    /// <summary>For bullet lists: disc, circle or square.</summary>
    public string Bullet { get; } = bullet;
}

internal sealed class HPara : HBlock
{
    public string StyleId { get; set; } = "Normal";

    public HParaProps Props { get; set; }

    public HList? List { get; set; }

    /// <summary>True for the second and later paragraphs of a list item: indented like the item, not numbered.</summary>
    public bool ListContinuation { get; set; }

    public List<HInline> Inlines { get; } = [];

    /// <summary>A horizontal rule: an empty paragraph with a bottom border.</summary>
    public bool Rule { get; set; }
}

internal sealed class HCell
{
    public List<HBlock> Blocks { get; } = [];

    public int ColSpan { get; set; } = 1;

    public int RowSpan { get; set; } = 1;

    public bool Header { get; set; }

    /// <summary>Set on the cells that a rowspan above covers: written as a vertical merge continuation.</summary>
    public bool Continuation { get; set; }

    public int WidthTwips { get; set; }

    public string? Shade { get; set; }

    public HBorder? BorderTop { get; set; }

    public HBorder? BorderRight { get; set; }

    public HBorder? BorderBottom { get; set; }

    public HBorder? BorderLeft { get; set; }

    public string? VAlign { get; set; }

    public int PadTop { get; set; } = -1;

    public int PadRight { get; set; } = -1;

    public int PadBottom { get; set; } = -1;

    public int PadLeft { get; set; } = -1;
}

internal sealed class HRow
{
    public List<HCell> Cells { get; } = [];

    public bool Header { get; set; }

    public int HeightTwips { get; set; }
}

internal sealed class HTable : HBlock
{
    public List<HRow> Rows { get; } = [];

    public List<int> Grid { get; } = [];

    /// <summary>Table width in twips; 0 for automatic.</summary>
    public int WidthTwips { get; set; }

    /// <summary>Table width in fiftieths of a percent; 0 when not a percentage.</summary>
    public int WidthPercent50 { get; set; }

    public int IndentTwips { get; set; }

    public string? Align { get; set; }

    public bool Rtl { get; set; }

    public HBorder? Border { get; set; }

    public string? Shade { get; set; }

    public int CellSpacing { get; set; }
}

internal sealed class HStyle(string id, string name, string? basedOn)
{
    public string Id { get; } = id;

    public string Name { get; } = name;

    public string? BasedOn { get; } = basedOn;

    public HParaProps Para { get; set; }

    public HRunProps Run { get; set; }
}

/// <summary>Everything the reader found in a page.</summary>
internal sealed class HtmlContent
{
    public List<HBlock> Blocks { get; } = [];

    public Dictionary<string, HStyle> Styles { get; } = new(StringComparer.Ordinal);

    public List<HList> Lists { get; } = [];

    public HRunProps DefaultRun { get; set; }

    public string? Title { get; set; }

    public string? Lang { get; set; }

    public bool Rtl { get; set; }

    public Dictionary<string, string> Meta { get; } = new(StringComparer.OrdinalIgnoreCase);
}
