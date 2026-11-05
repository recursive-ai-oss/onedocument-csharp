using System.Linq;
using RecursiveAI.OneDocument.Schema;
using W = RecursiveAI.OneDocument.Schema.Wordprocessing;

namespace RecursiveAI.OneDocument.Documents;

/// <summary>How a cell takes part in a merge, for <c>w:vMerge</c> and <c>w:hMerge</c> (ECMA-376 Part 1 §17.4.85, §17.4.84).</summary>
public enum CellMerge
{
    /// <summary>The cell is not merged in this direction.</summary>
    None = 0,

    /// <summary>The cell starts a merged region (<c>w:val="restart"</c>).</summary>
    Restart = 1,

    /// <summary>The cell continues the region of the cell above (vertical) or to the left (horizontal); an element without a value means this.</summary>
    Continue = 2,
}

/// <summary>A width as the table properties state it: a number and its unit (<c>dxa</c> twentieths of a point, <c>pct</c> fiftieths of a percent, <c>auto</c>, <c>nil</c>).</summary>
/// <param name="Value">The number; 0 for <c>auto</c> and <c>nil</c>.</param>
/// <param name="Type">The unit as written; <c>dxa</c> when the element gives none.</param>
public readonly record struct TableMeasure(long Value, string Type);

/// <summary>Which conditional regions of a table style are switched on (<c>w:tblLook</c>, ECMA-376 Part 1 §17.4.56).</summary>
/// <param name="FirstRow">Special formatting for the first row.</param>
/// <param name="LastRow">Special formatting for the last row.</param>
/// <param name="FirstColumn">Special formatting for the first column.</param>
/// <param name="LastColumn">Special formatting for the last column.</param>
/// <param name="BandedRows">Row bands are shown (the inverse of <c>noHBand</c>).</param>
/// <param name="BandedColumns">Column bands are shown (the inverse of <c>noVBand</c>).</param>
public readonly record struct TableLookFlags(bool FirstRow, bool LastRow, bool FirstColumn, bool LastColumn, bool BandedRows, bool BandedColumns);

public sealed partial class Table
{
    /// <summary>Gets the table properties element (<c>w:tblPr</c>), or null.</summary>
    public MarkupRef? PropertiesElement => Wml.Child(Markup, "tblPr"u8);

    /// <summary>
    /// Gets or sets the id of the table style (<c>w:tblStyle</c>); null when the table names none. Setting null removes the element. The id
    /// is not checked against the style sheet here.
    /// </summary>
    public string? StyleId
    {
        get => Wml.Attr(Wml.Child(PropertiesElement ?? default, "tblStyle"u8) ?? default, "val");
        set
        {
            W.TableProperties props = View.GetOrAddTableProperties();
            if (value is null)
            {
                props.RemoveTableStyle();
            }
            else
            {
                WmlEdit.SetAttr(props.GetOrAddTableStyle().Markup, "val", value);
            }
        }
    }

    /// <summary>Gets the table's own width (<c>w:tblW</c>), or null when unstated.</summary>
    public TableMeasure? Width => ReadMeasure(Wml.Child(PropertiesElement ?? default, "tblW"u8));

    /// <summary>Gets the table alignment (<c>w:jc</c> of the table properties) as written, or null.</summary>
    public string? Justification => Wml.Attr(Wml.Child(PropertiesElement ?? default, "jc"u8) ?? default, "val");

    /// <summary>Gets a value indicating whether the table uses a fixed layout (<c>w:tblLayout w:type="fixed"</c>); autofit otherwise.</summary>
    public bool IsFixedLayout => Wml.Attr(Wml.Child(PropertiesElement ?? default, "tblLayout"u8) ?? default, "type") == "fixed";

    /// <summary>Gets a value indicating whether the table is visually right to left (<c>w:bidiVisual</c>).</summary>
    public bool IsRightToLeft => Wml.Child(PropertiesElement ?? default, "bidiVisual"u8) is { } e && Wml.OnOffElement(e);

    /// <summary>Gets a value indicating whether the table sits inside a table cell.</summary>
    public bool IsNested => Wml.AncestorOrSelf(new MarkupRef(Markup.Node.Parent), "tc"u8) is not null;

    /// <summary>
    /// Gets the table-look flags (<c>w:tblLook</c>): the six attributes, or the hexadecimal mask in <c>w:val</c> (0x20 first row, 0x40
    /// last row, 0x80 first column, 0x100 last column, 0x200 no row bands, 0x400 no column bands). Attributes win over the mask. An absent
    /// element switches every region off.
    /// </summary>
    public TableLookFlags Look
    {
        get
        {
            MarkupRef? look = Wml.Child(PropertiesElement ?? default, "tblLook"u8);
            if (look is not { } l)
            {
                return default;
            }

            long mask = long.TryParse(Wml.Attr(l, "val"), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out long m) ? m : 0;
            bool Flag(string attr, long bit) => Wml.OnOff(Wml.Attr(l, attr)) ?? ((mask & bit) != 0);
            return new TableLookFlags(Flag("firstRow", 0x20), Flag("lastRow", 0x40), Flag("firstColumn", 0x80), Flag("lastColumn", 0x100), !Flag("noHBand", 0x200), !Flag("noVBand", 0x400));
        }
    }

    /// <summary>Gets the preferred widths of the grid columns in twentieths of a point (<c>w:tblGrid/w:gridCol</c>); empty when the table has no grid. A column without a width reads as 0.</summary>
    public IReadOnlyList<long> GridColumns
    {
        get
        {
            var list = new List<long>();
            if (Wml.Child(Markup, "tblGrid"u8) is { } grid)
            {
                foreach (MarkupRef c in Wml.Elements(grid))
                {
                    if (Wml.Is(c, "gridCol"u8))
                    {
                        list.Add(Wml.Long(Wml.Attr(c, "w")) ?? 0);
                    }
                }
            }

            return list;
        }
    }

    /// <summary>
    /// Enumerates the rows in order. Rows wrapped in a content control or custom XML element are included, so nothing is hidden; other
    /// markup between rows (bookmark markers, revision range markers) is not a row.
    /// </summary>
    /// <returns>The rows.</returns>
    public IEnumerable<TableRow> Rows()
    {
        foreach (MarkupRef tr in ThroughWrappers(Markup, "tr"u8))
        {
            yield return new TableRow(Owner, tr, this);
        }
    }

    /// <summary>Gets the number of rows.</summary>
    public int RowCount => Rows().Count();

    internal static IEnumerable<MarkupRef> ThroughWrappers(MarkupRef parent, ReadOnlySpan<byte> local)
    {
        // The span cannot cross the iterator boundary, so the match is done on a copy.
        byte[] name = local.ToArray();
        return Walk(parent, name);
    }

    private static IEnumerable<MarkupRef> Walk(MarkupRef parent, byte[] local)
    {
        foreach (MarkupRef child in Wml.Elements(parent))
        {
            if (Wml.Is(child, local))
            {
                yield return child;
            }
            else if (Wml.Is(child, "customXml"u8))
            {
                foreach (MarkupRef inner in Walk(child, local))
                {
                    yield return inner;
                }
            }
            else if (Wml.Is(child, "sdt"u8) && Wml.Child(child, "sdtContent"u8) is { } content)
            {
                foreach (MarkupRef inner in Walk(content, local))
                {
                    yield return inner;
                }
            }
        }
    }

    internal static TableMeasure? ReadMeasure(MarkupRef? e)
    {
        if (e is not { } m || !m.IsValid)
        {
            return null;
        }

        string type = Wml.Attr(m, "type") ?? "dxa";
        string? w = Wml.Attr(m, "w");
        // ST_MeasurementOrPercent allows "50%" in the value; read it as fiftieths of a percent like the pct unit.
        if (w is not null && w.EndsWith('%') && double.TryParse(w.AsSpan(0, w.Length - 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double pct))
        {
            return new TableMeasure((long)Math.Round(pct * 50), "pct");
        }

        return new TableMeasure(Wml.Long(w) ?? 0, type);
    }
}

/// <summary>A table row (<c>w:tr</c>).</summary>
public sealed class TableRow
{
    internal TableRow(Document owner, MarkupRef markup, Table table)
    {
        Owner = owner;
        Markup = markup;
        Table = table;
    }

    /// <summary>Gets the document the row belongs to.</summary>
    public Document Owner { get; }

    /// <summary>Gets the row element: the hop to the typed view and the markup layer.</summary>
    public MarkupRef Markup { get; }

    /// <summary>Gets the table the row is in.</summary>
    public Table Table { get; }

    /// <summary>Gets the typed view of the row.</summary>
    public W.TableRow View => W.TableRow.Create(Markup);

    /// <summary>Gets the row properties element (<c>w:trPr</c>), or null.</summary>
    public MarkupRef? PropertiesElement => Wml.Child(Markup, "trPr"u8);

    /// <summary>Gets the number of grid columns left empty before the first cell (<c>w:gridBefore</c>).</summary>
    public int GridBefore => ReadCount("gridBefore");

    /// <summary>Gets the number of grid columns left empty after the last cell (<c>w:gridAfter</c>).</summary>
    public int GridAfter => ReadCount("gridAfter");

    /// <summary>Gets a value indicating whether the row repeats as a header at the top of each page (<c>w:tblHeader</c>).</summary>
    public bool IsHeader => Wml.Child(PropertiesElement ?? default, "tblHeader"u8) is { } e && Wml.OnOffElement(e);

    /// <summary>Gets a value indicating whether the row may not break across pages (<c>w:cantSplit</c>).</summary>
    public bool CannotSplit => Wml.Child(PropertiesElement ?? default, "cantSplit"u8) is { } e && Wml.OnOffElement(e);

    /// <summary>Gets the row height in twentieths of a point (<c>w:trHeight</c>), or null.</summary>
    public long? Height => Wml.Long(Wml.Attr(Wml.Child(PropertiesElement ?? default, "trHeight"u8) ?? default, "val"));

    /// <summary>Gets the height rule (<c>atLeast</c>, <c>exact</c> or <c>auto</c>); <c>atLeast</c> when the element gives none.</summary>
    public string HeightRule => Wml.Attr(Wml.Child(PropertiesElement ?? default, "trHeight"u8) ?? default, "hRule") ?? "atLeast";

    /// <summary>Enumerates the cells in order, including cells inside content controls or custom XML wrappers.</summary>
    /// <returns>The cells.</returns>
    public IEnumerable<TableCell> Cells()
    {
        foreach (MarkupRef tc in Table.ThroughWrappers(Markup, "tc"u8))
        {
            yield return new TableCell(Owner, tc);
        }
    }

    private int ReadCount(string name)
    {
        long? v = Wml.Long(Wml.Attr(Wml.Child(PropertiesElement ?? default, System.Text.Encoding.UTF8.GetBytes(name)) ?? default, "val"));
        return v is > 0 and < 16384 ? (int)v.Value : 0;
    }
}

/// <summary>
/// A table cell (<c>w:tc</c>): a container of blocks. A cell always ends with a paragraph in a valid file; the cell properties
/// element is not a block.
/// </summary>
public sealed class TableCell : BlockContainer
{
    internal TableCell(Document owner, MarkupRef markup)
        : base(owner, markup)
    {
    }

    /// <summary>Gets the typed view of the cell.</summary>
    public W.TableCell View => W.TableCell.Create(Container);

    /// <summary>Gets the cell properties element (<c>w:tcPr</c>), or null.</summary>
    public MarkupRef? PropertiesElement => Wml.Child(Container, "tcPr"u8);

    /// <summary>Gets the number of grid columns the cell spans (<c>w:gridSpan</c>); 1 when absent or invalid.</summary>
    public int GridSpan
    {
        get
        {
            long? v = Wml.Long(Wml.Attr(Wml.Child(PropertiesElement ?? default, "gridSpan"u8) ?? default, "val"));
            return v is >= 1 and <= 16384 ? (int)v.Value : 1;
        }
    }

    /// <summary>Gets how the cell takes part in a vertical merge (<c>w:vMerge</c>).</summary>
    public CellMerge VerticalMerge => ReadMerge("vMerge"u8);

    /// <summary>Gets how the cell takes part in a horizontal merge (<c>w:hMerge</c>, the legacy form; current files use <see cref="GridSpan"/>).</summary>
    public CellMerge HorizontalMerge => ReadMerge("hMerge"u8);

    /// <summary>Gets the cell width (<c>w:tcW</c>), or null.</summary>
    public TableMeasure? Width => Table.ReadMeasure(Wml.Child(PropertiesElement ?? default, "tcW"u8));

    /// <summary>Gets the vertical alignment of the content (<c>top</c>, <c>center</c>, <c>bottom</c>, <c>both</c>) as written, or null.</summary>
    public string? VerticalAlignment => Wml.Attr(Wml.Child(PropertiesElement ?? default, "vAlign"u8) ?? default, "val");

    /// <summary>Gets the text of the cell's paragraphs, joined by line feeds. Nested tables contribute their paragraphs too.</summary>
    public string Text => string.Join("\n", Paragraphs().Select(p => p.Text));

    private CellMerge ReadMerge(ReadOnlySpan<byte> name)
    {
        if (Wml.Child(PropertiesElement ?? default, name) is not { } e)
        {
            return CellMerge.None;
        }

        // §17.4.85: an element without a value, or with "continue", continues; "restart" starts a region.
        return Wml.Attr(e, "val") == "restart" ? CellMerge.Restart : CellMerge.Continue;
    }
}
