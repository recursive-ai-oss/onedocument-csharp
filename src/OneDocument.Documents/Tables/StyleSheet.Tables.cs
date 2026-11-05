using System.Linq;
using RecursiveAI.OneDocument.Schema;

namespace RecursiveAI.OneDocument.Documents.Styles;

public sealed partial class StyleSheet
{
    private static readonly string[] CellSkip = ["tblStyle", "tblLook", "tblStyleRowBandSize", "tblStyleColBandSize", "cnfStyle", "tblPrChange", "tcPrChange", "tblpPr", "tblOverlap"];

    private static (int Index, int Count) PhysicalPosition(LogicalTable model, LogicalCell cell)
    {
        int index = -1, count = 0;
        foreach (TableCell c in model.Table.Rows().ElementAt(cell.Row).Cells())
        {
            if (c.Container.Node.Equals(cell.Cell.Container.Node))
            {
                index = count;
            }

            count++;
        }

        return (index, count);
    }

    /// <summary>
    /// Names the conditional regions of the table's style that apply to a logical cell, in the order they are applied (ECMA-376 Part 1
    /// §17.7.6): <c>wholeTable</c>, the bands, <c>firstCol</c>, <c>lastCol</c>, <c>firstRow</c>, <c>lastRow</c> and the four corners. The
    /// regions switched on by <c>w:tblLook</c> and the cell's position decide; a merged region counts by its top-left cell for banding, and
    /// as last row or column when it reaches the last one. The header row is not banded and banding counts from the row after it.
    /// </summary>
    /// <param name="table">The table.</param>
    /// <param name="model">The table's logical model.</param>
    /// <param name="cell">A cell of the model.</param>
    /// <returns>The region names.</returns>
    public IReadOnlyList<string> GetTableRegions(Table table, LogicalTable model, LogicalCell cell)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(cell);
        TableLookFlags look = table.Look;
        IReadOnlyList<StyleDefinition> lineage = TableStyleOf(table)?.Lineage ?? [];
        MarkupRef? tblPr = table.PropertiesElement;
        long rowBand = BandSize(lineage, tblPr, "tblStyleRowBandSize");
        long colBand = BandSize(lineage, tblPr, "tblStyleColBandSize");
        bool isFirstRow = look.FirstRow && cell.Row == 0;
        bool isLastRow = look.LastRow && cell.Row + cell.RowSpan == model.RowCount;
        // Observed in Word 365 (16.0.20527): the first and last column regions follow the row's first and last physical cell, so a row
        // that starts after gridBefore still has its first cell in the first column. Banding by column stays on the grid position
        // (bands are not verified against Word yet).
        (int physical, int count) = PhysicalPosition(model, cell);
        bool isFirstCol = look.FirstColumn && physical == 0;
        bool isLastCol = look.LastColumn && physical == count - 1;
        long hIndex = cell.Row - (look.FirstRow ? 1 : 0);
        long vIndex = cell.Column - (look.FirstColumn ? 1 : 0);
        bool hBanded = look.BandedRows && !isFirstRow && !isLastRow && hIndex >= 0;
        bool vBanded = look.BandedColumns && !isFirstCol && !isLastCol && vIndex >= 0;
        var result = new List<string>();
        foreach (string region in RegionOrder)
        {
            bool applies = region switch
            {
                "wholeTable" => true,
                "band1Horz" => hBanded && (hIndex / rowBand) % 2 == 0,
                "band2Horz" => hBanded && (hIndex / rowBand) % 2 == 1,
                "band1Vert" => vBanded && (vIndex / colBand) % 2 == 0,
                "band2Vert" => vBanded && (vIndex / colBand) % 2 == 1,
                "firstRow" => isFirstRow,
                "lastRow" => isLastRow,
                "firstCol" => isFirstCol,
                "lastCol" => isLastCol,
                "nwCell" => isFirstRow && isFirstCol,
                "neCell" => isFirstRow && isLastCol,
                "swCell" => isLastRow && isFirstCol,
                "seCell" => isLastRow && isLastCol,
                _ => false,
            };
            if (applies)
            {
                result.Add(region);
            }
        }

        return result;
    }

    /// <summary>
    /// Resolves the cell-level formatting a logical cell gets: the table style's table and cell properties, the style's conditional
    /// regions that apply (<see cref="GetTableRegions"/>), then the table's own properties and the cell's own, each layer over the last.
    /// Borders and margins are flattened one entry per side with the container as prefix (<c>tblBorders/top</c>, <c>tblBorders/insideH</c>,
    /// <c>tcBorders/left</c>, <c>tblCellMar/start</c>), so a side set in a later layer replaces only that side. Shading is <c>shd</c>,
    /// the width <c>tcW</c>, and so on by element name. A table style that is not defined falls back to the default table style and is
    /// reported (<c>docx.style-unknown</c>).
    /// </summary>
    /// <param name="table">The table.</param>
    /// <param name="model">The table's logical model.</param>
    /// <param name="cell">A cell of the model.</param>
    /// <returns>The effective properties; absent entries are unset in every layer.</returns>
    public ResolvedProperties ResolveTableCell(Table table, LogicalTable model, LogicalCell cell)
    {
        ArgumentNullException.ThrowIfNull(table);
        var bag = new SortedDictionary<string, ResolvedProperty>(StringComparer.Ordinal);
        StyleDefinition? style = TableStyleOf(table);
        IReadOnlyList<StyleDefinition> lineage = style?.Lineage ?? [];
        foreach (StyleDefinition s in lineage)
        {
            MergeCellContainer(bag, s.TablePropertiesElement, PropertySource.TableStyle, s.Id);
            MergeCellContainer(bag, Wml.Child(s.Markup, "tcPr"u8), PropertySource.TableStyle, s.Id);
        }

        foreach (string region in GetTableRegions(table, model, cell))
        {
            foreach (StyleDefinition s in lineage)
            {
                foreach (TableStyleRegion r in s.TableRegions)
                {
                    if (r.Type == region)
                    {
                        MergeCellContainer(bag, Wml.Child(r.Markup, "tblPr"u8), PropertySource.TableStyle, s.Id);
                        MergeCellContainer(bag, Wml.Child(r.Markup, "tcPr"u8), PropertySource.TableStyle, s.Id);
                    }
                }
            }
        }

        MergeCellContainer(bag, table.PropertiesElement, PropertySource.DirectFormatting, null);
        MergeCellContainer(bag, cell.Cell.PropertiesElement, PropertySource.DirectFormatting, null);
        return new ResolvedProperties(bag, Theme);
    }

    private StyleDefinition? TableStyleOf(Table table)
    {
        string? id = table.StyleId;
        StyleDefinition? style = id is null ? Default(StyleType.Table) : Find(id);
        if (style is null && id is not null)
        {
            Report("docx.style-unknown", Diagnostics.DefectKind.Reference, Diagnostics.DefectSeverity.Repaired, "t:" + id,
                "The table style '" + id + "' is not defined; the default table style applies.", table.Markup);
            style = Default(StyleType.Table);
        }

        return style;
    }

    private static void MergeCellContainer(SortedDictionary<string, ResolvedProperty> bag, MarkupRef? container, PropertySource source, string? styleId)
    {
        if (container is not { } c || !c.IsValid)
        {
            return;
        }

        foreach (MarkupRef e in Wml.Elements(c))
        {
            if (e.Namespace != SchemaNamespace.Wordprocessing)
            {
                continue;
            }

            string name = e.LocalNameText;
            if (Array.IndexOf(CellSkip, name) >= 0)
            {
                continue;
            }

            if (name is "tblBorders" or "tcBorders" or "tblCellMar" or "tcMar")
            {
                foreach (MarkupRef side in Wml.Elements(e))
                {
                    string key = name + "/" + side.LocalNameText;
                    var origin = new PropertyOrigin(source, styleId, side);
                    bag[key] = new ResolvedProperty(key, side, origin, ReadAttributes(side, origin));
                }

                continue;
            }

            var o = new PropertyOrigin(source, styleId, e);
            bag[name] = new ResolvedProperty(name, e, o, ReadAttributes(e, o));
        }
    }
}
