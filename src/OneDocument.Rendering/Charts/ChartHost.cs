using RecursiveAI.OneDocument.Diagnostics;
using OneFont;
using OneRender.Image;
using OneRender.Painting;
using OneRender.Pdf;
using RecursiveAI.OneDocument.Layout;
using RecursiveAI.OneDocument.NumberFormats;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Spreadsheets;

namespace RecursiveAI.OneDocument.Charts;

/// <summary>A chart read from a package together with the theme it should be drawn in.</summary>
/// <param name="PartName">The chart part's name.</param>
/// <param name="Chart">The model.</param>
/// <param name="Theme">The chart's own theme override, or null to use the host document's theme.</param>
public sealed record ChartPart(string PartName, ChartSpace Chart, ChartTheme? Theme);

// The one place the chart namespace meets the rest of OneDocument: packages, fonts, the number-format service and workbook cells.
// Everything else in RecursiveAI.OneDocument.Charts depends only on the chart model, the markup layer and the display list.

/// <summary>Connects charts to packages, the font library, number formats and workbooks, and renders a chart on its own.</summary>
public static class ChartHost
{
    private const string ChartContentType = "application/vnd.openxmlformats-officedocument.drawingml.chart+xml";
    private const string ChartExContentType = "application/vnd.ms-office.chartex+xml";

    private static readonly Lazy<FontLibrary> DefaultFonts = new(FontLibrary.CreateDefault);

    /// <summary>Loads the chart a graphic frame refers to.</summary>
    /// <param name="package">The package.</param>
    /// <param name="sourcePartName">The part holding the frame (slide, drawing part or document part).</param>
    /// <param name="relationshipId">The <c>r:id</c> of the frame's <c>c:chart</c> or <c>cx:chart</c> element.</param>
    /// <returns>The chart, or null when the relationship, the part or its XML is missing or unreadable.</returns>
    public static ChartPart? Load(OpcPackage package, string sourcePartName, string relationshipId)
    {
        ArgumentNullException.ThrowIfNull(package);
        foreach (Relationship r in package.GetRelationships(sourcePartName))
        {
            if (r.Id != relationshipId || !OpcPackage.TryResolveTarget(sourcePartName, r, out PartUri target))
            {
                continue;
            }

            return LoadPart(package, target.Value);
        }

        return null;
    }

    /// <summary>Loads a chart part by name.</summary>
    /// <param name="package">The package.</param>
    /// <param name="partName">The chart part's name.</param>
    /// <returns>The chart, or null when the part is missing or is not a readable chart.</returns>
    public static ChartPart? LoadPart(OpcPackage package, string partName)
    {
        ArgumentNullException.ThrowIfNull(package);
        IPackagePart? part = package.FindPart(partName);
        if (part is null || !ChartReader.TryRead(part.GetBytes(), out ChartSpace? chart, partName) || chart is null)
        {
            return null;
        }

        ChartTheme? theme = null;
        foreach (Relationship r in package.GetRelationships(partName))
        {
            if (r.Type.EndsWith("/themeOverride", StringComparison.Ordinal) && OpcPackage.TryResolveTarget(partName, r, out PartUri t) && package.FindPart(t.Value) is { } tp)
            {
                theme = ChartTheme.TryParse(tp.GetBytes());
            }
        }

        return new ChartPart(partName, chart, theme);
    }

    /// <summary>Lists the chart parts of a package (classic and chartex), by content type.</summary>
    /// <param name="package">The package.</param>
    /// <returns>The part names in package order.</returns>
    public static IReadOnlyList<string> FindChartParts(OpcPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        var list = new List<string>();
        foreach (IPackagePart p in package.Parts)
        {
            if (p.ContentType is ChartContentType or ChartExContentType)
            {
                list.Add(p.Name);
            }
        }

        return list;
    }

    /// <summary>Adapts a font provider to chart text.</summary>
    /// <param name="provider">The provider.</param>
    /// <returns>The adapter.</returns>
    public static IChartFonts Fonts(IFontProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return new FontBridge(provider);
    }

    /// <summary>Gets the number formatter backed by the number-format service.</summary>
    public static IChartNumberFormatter Numbers { get; } = new NumberBridge();

    /// <summary>Reads chart references from a workbook's cells (values as stored; nothing is recalculated).</summary>
    /// <param name="workbook">The workbook.</param>
    /// <returns>The data source for <see cref="ChartRefresh.Refresh"/>.</returns>
    public static IChartDataSource WorkbookSource(Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        return new WorkbookBridge(workbook);
    }

    /// <summary>Paints a chart with the default fonts (Carlito for Calibri and friends, from the font packages).</summary>
    /// <param name="list">The display list (CSS pixels).</param>
    /// <param name="chart">The chart.</param>
    /// <param name="x">Left of the box in px.</param>
    /// <param name="y">Top of the box in px.</param>
    /// <param name="width">Width in px.</param>
    /// <param name="height">Height in px.</param>
    /// <param name="theme">The theme, or null for the default.</param>
    /// <param name="fonts">The font provider, or null for the default library.</param>
    /// <returns>The layout used.</returns>
    public static ChartLayoutInfo Paint(DisplayList list, ChartSpace chart, double x, double y, double width, double height, ChartTheme? theme = null, IFontProvider? fonts = null) =>
        ChartPainter.Paint(list, chart, x, y, width, height, new ChartPaintOptions { Fonts = Fonts(fonts ?? DefaultFonts.Value), Numbers = Numbers, Theme = theme });

    /// <summary>Writes a chart as a one-page tagged PDF.</summary>
    /// <param name="chart">The chart.</param>
    /// <param name="destination">The stream.</param>
    /// <param name="widthPoints">Page width in points.</param>
    /// <param name="heightPoints">Page height in points.</param>
    /// <param name="theme">The theme, or null.</param>
    /// <param name="fonts">The font provider, or null.</param>
    /// <exception cref="OfficeFormatException">The PDF could not be written (code <c>render.failed</c>).</exception>
    public static void WritePdf(ChartSpace chart, Stream destination, double widthPoints, double heightPoints, ChartTheme? theme = null, IFontProvider? fonts = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var list = new DisplayList();
        double w = widthPoints * ChartPainter.PixelsPerPoint;
        double h = heightPoints * ChartPainter.PixelsPerPoint;
        Paint(list, chart, 0, 0, w, h, theme, fonts);
        try
        {
            using var writer = new DisplayListPdfWriter(destination, new PdfOutputOptions { Title = chart.DescribeForAccessibility(), Language = "en-US", Tagged = true, Creator = "OneDocument", LeaveOpen = true });
            writer.AddPage(list, new PdfPageSetup(w, h), default);
            writer.Finish(default);
        }
        catch (Exception ex) when (ex is PdfRenderException or DisplayListException or ArgumentException)
        {
            throw new OfficeFormatException(DefectKind.Structure, "render.failed", "The chart could not be written as PDF: " + ex.Message, default, ex);
        }
    }

    /// <summary>Renders a chart as a PNG at 144 dpi.</summary>
    /// <param name="chart">The chart.</param>
    /// <param name="widthPoints">Width in points.</param>
    /// <param name="heightPoints">Height in points.</param>
    /// <param name="theme">The theme, or null.</param>
    /// <param name="fonts">The font provider, or null.</param>
    /// <returns>The PNG bytes.</returns>
    /// <exception cref="OfficeFormatException">The chart could not be drawn (code <c>render.failed</c>).</exception>
    public static byte[] RenderPng(ChartSpace chart, double widthPoints, double heightPoints, ChartTheme? theme = null, IFontProvider? fonts = null)
    {
        var list = new DisplayList();
        double w = widthPoints * ChartPainter.PixelsPerPoint;
        double h = heightPoints * ChartPainter.PixelsPerPoint;
        Paint(list, chart, 0, 0, w, h, theme, fonts);
        const double Ratio = 1.5;
        try
        {
            RgbaImage image = DisplayListRasterizer.Render(list, (int)Math.Ceiling(w * Ratio), (int)Math.Ceiling(h * Ratio), new RasterOptions { DevicePixelRatio = (float)Ratio });
            return image.ToPng();
        }
        catch (Exception ex) when (ex is RasterizationException or DisplayListException or ArgumentException)
        {
            throw new OfficeFormatException(DefectKind.Structure, "render.failed", "The chart could not be drawn: " + ex.Message, default, ex);
        }
    }

    private sealed class FontBridge(IFontProvider provider) : IChartFonts
    {
        public FontFace? Resolve(string family, bool bold, bool italic) => provider.Resolve(new FontRequest(family, bold, italic))?.Face;
    }

    private sealed class NumberBridge : IChartNumberFormatter
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, NumberFormat> _cache = new(StringComparer.Ordinal);

        public string Format(double value, string? formatCode)
        {
            string code = string.IsNullOrWhiteSpace(formatCode) ? "General" : formatCode;
            NumberFormat f = _cache.GetOrAdd(code, static c => NumberFormat.Parse(c));
            return f.Format(value);
        }
    }

    private sealed class WorkbookBridge(Workbook workbook) : IChartDataSource
    {
        public bool TryGetValues(string reference, out IReadOnlyList<object?> values)
        {
            values = [];
            int bang = reference.LastIndexOf('!');
            if (bang <= 0 || reference.Contains(',', StringComparison.Ordinal) || reference.StartsWith('('))
            {
                return false;
            }

            string sheet = reference[..bang].Trim('\'').Replace("''", "'", StringComparison.Ordinal);
            string range = reference[(bang + 1)..].Replace("$", string.Empty, StringComparison.Ordinal);
            if (!workbook.Sheets.TryGet(sheet, out Worksheet? ws) || ws is null || !CellRange.TryParse(range, out CellRange r))
            {
                return false;
            }

            var list = new List<object?>();
            for (int row = r.TopLeft.Row; row <= r.BottomRight.Row && list.Count < 1_000_000; row++)
            {
                for (int col = r.TopLeft.Column; col <= r.BottomRight.Column; col++)
                {
                    CellValue v = ws[new CellAddress(row, col)].Value;
                    list.Add(v.Kind switch
                    {
                        CellValueKind.Number => v.Number,
                        CellValueKind.Boolean => v.Boolean,
                        CellValueKind.Text => ws.GetText(v),
                        _ => null,
                    });
                }
            }

            values = list;
            return true;
        }
    }
}
