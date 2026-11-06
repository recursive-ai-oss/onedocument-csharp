using System.Globalization;
using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Calc.OpenFormula;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Markup.Xml;
using RecursiveAI.OneDocument.NumberFormats;
using RecursiveAI.OneDocument.Odf;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Properties;

namespace RecursiveAI.OneDocument.Spreadsheets.Odf;

// The workbook hub -> OpenDocument spreadsheet. The hub is read through its public model (cells, styles, names, validation, conditional formats)
// and the markup of each sheet's frame; the result is the four parts of an OdfPackage, which the caller writes as a zip (ODS, OTS) or one flat file (FODS).
internal sealed partial class OdsExporter
{
    private const string Ns =
        " xmlns:office=\"" + OdfNamespaces.Office + "\" xmlns:style=\"" + OdfNamespaces.Style + "\" xmlns:text=\"" + OdfNamespaces.Text +
        "\" xmlns:table=\"" + OdfNamespaces.Table + "\" xmlns:draw=\"" + OdfNamespaces.Draw + "\" xmlns:fo=\"" + OdfNamespaces.Fo +
        "\" xmlns:xlink=\"" + OdfNamespaces.XLink + "\" xmlns:dc=\"" + OdfNamespaces.DublinCore + "\" xmlns:meta=\"" + OdfNamespaces.Meta +
        "\" xmlns:number=\"" + OdfNamespaces.Number + "\" xmlns:svg=\"" + OdfNamespaces.Svg + "\" xmlns:config=\"" + OdfNamespaces.Config +
        "\" xmlns:calcext=\"urn:org:documentfoundation:names:experimental:calc:xmlns:calcext:1.0\" xmlns:loext=\"" + OdfNamespaces.LibreOfficeExtension + "\" xmlns:tableooo=\"http://openoffice.org/2009/table\"";

    private const string Decl = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n";

    private readonly Workbook _wb;
    private readonly LossReport _loss;
    private readonly OpenContext _ctx;
    private readonly CancellationToken _ct;
    private readonly List<Worksheet> _sheets = [];
    private OdfPackage _out = null!;

    private OdsExporter(Workbook wb, LossReport loss, CancellationToken ct)
    {
        _wb = wb;
        _loss = loss;
        _ctx = wb.OpenContext;
        _ct = ct;
        foreach (Worksheet ws in wb.Sheets)
        {
            if (ws.Kind == SheetKind.Worksheet)
            {
                _sheets.Add(ws);
            }
            else
            {
                _loss.Report("loss.sheet-kinds", "chart and dialog sheets", 1, "Chart sheets and dialog sheets are not written.");
            }
        }
    }

    /// <summary>Writes the workbook.</summary>
    /// <param name="wb">The workbook.</param>
    /// <param name="format">ODS, OTS or flat ODS.</param>
    /// <param name="destination">The stream; left open.</param>
    /// <param name="loss">Receives what OpenDocument cannot carry.</param>
    /// <param name="ct">Cancels the write.</param>
    public static void Write(Workbook wb, OfficeFormat format, Stream destination, LossReport loss, CancellationToken ct)
    {
        try
        {
            new OdsExporter(wb, loss, ct).Run(format, destination);
        }
        catch (Exception ex) when (ex is not (OfficeException or OperationCanceledException or IOException))
        {
            throw new OfficeFormatException(DefectKind.Structure, "odf.export-failed", "The workbook could not be written as OpenDocument: " + ex.GetType().Name + ".", DefectLocation.InPart(wb.MainPartName), ex);
        }
    }

    private static string Esc(string s) => OdfLengths.Escape(s);

    private static string I(long v) => v.ToString(CultureInfo.InvariantCulture);

    private static string Inv(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    private void Lost(string code, string feature, string message, long count = 1) => _loss.Report(code, feature, count, message);

    private void Run(OfficeFormat format, Stream destination)
    {
        string mime = format == OfficeFormat.Ots ? OdfMediaTypes.SpreadsheetTemplate : OdfMediaTypes.Spreadsheet;
        _out = OdfPackage.Create(mime, _ctx);
        if (_sheets.Count == 0)
        {
            throw new OfficeUnsupportedException("writing a workbook without a worksheet as OpenDocument");
        }

        PrepareStyles();
        PrepareNames();
        var body = new StringBuilder();
        for (int i = 0; i < _sheets.Count; i++)
        {
            _ct.ThrowIfCancellationRequested();
            WriteSheet(body, _sheets[i], i);
        }

        string content = BuildContent(body.ToString());
        _out.SetEntry(OdfPackage.ContentName, Encoding.UTF8.GetBytes(content), "text/xml");
        _out.SetEntry(OdfPackage.StylesName, Encoding.UTF8.GetBytes(BuildStyles()), "text/xml");
        _out.SetEntry(OdfPackage.MetaName, Encoding.UTF8.GetBytes(BuildMeta()), "text/xml");
        _out.SetEntry(OdfPackage.SettingsName, Encoding.UTF8.GetBytes(BuildSettings()), "text/xml");

        if (format == OfficeFormat.Fods)
        {
            _out.WriteFlatTo(destination, _ct);
        }
        else
        {
            _out.WriteTo(destination, _ct);
        }
    }

    private string BuildContent(string body)
    {
        var sb = new StringBuilder(Decl);
        sb.Append("<office:document-content").Append(Ns).Append(" office:version=\"1.3\">");
        sb.Append("<office:scripts/>");
        sb.Append("<office:font-face-decls>").Append(FontDecls()).Append("</office:font-face-decls>");
        sb.Append("<office:automatic-styles>").Append(_auto).Append("</office:automatic-styles>");
        sb.Append("<office:body><office:spreadsheet>");
        sb.Append("<table:calculation-settings table:case-sensitive=\"false\" table:automatic-find-labels=\"false\" table:use-regular-expressions=\"false\" table:use-wildcards=\"true\">");
        sb.Append(_wb.DateSystem == DateSystem.Date1904 ? "<table:null-date table:value-type=\"date\" table:date-value=\"1904-01-01\"/>" : "<table:null-date table:value-type=\"date\" table:date-value=\"1899-12-30\"/>");
        if (_wb.Markup.Root.IsNull is false && Calc() is { } calc && calc.GetAttribute("iterate") is "1" or "true")
        {
            sb.Append("<table:iteration table:status=\"enable\" table:steps=\"").Append(calc.GetAttribute("iterateCount") ?? "100").Append("\" table:maximum-difference=\"").Append(calc.GetAttribute("iterateDelta") ?? "0.001").Append("\"/>");
        }

        sb.Append("</table:calculation-settings>");
        if (_validationDefs.Length > 0)
        {
            sb.Append("<table:content-validations>").Append(_validationDefs).Append("</table:content-validations>");
        }

        sb.Append(body);
        if (_globalNames.Length > 0)
        {
            sb.Append("<table:named-expressions>").Append(_globalNames).Append("</table:named-expressions>");
        }

        if (_databaseRanges.Length > 0)
        {
            sb.Append("<table:database-ranges>").Append(_databaseRanges).Append("</table:database-ranges>");
        }

        sb.Append("</office:spreadsheet></office:body></office:document-content>");
        return sb.ToString();
    }

    private MarkupNode? Calc()
    {
        foreach (MarkupNode c in _wb.Markup.Root.ChildElements)
        {
            if (c.LocalNameString == "calcPr")
            {
                return c;
            }
        }

        return null;
    }

    // ---- meta ------------------------------------------------------------------------------------------------------------------------------

    private string BuildMeta()
    {
        var sb = new StringBuilder(Decl);
        sb.Append("<office:document-meta").Append(Ns).Append(" office:version=\"1.3\"><office:meta>");
        var props = new DocumentProperties(_wb.Package, _ctx);
        CoreProperties core = props.Core;
        sb.Append("<meta:generator>OneDocument</meta:generator>");
        if (core.Title is { Length: > 0 } t)
        {
            sb.Append("<dc:title>").Append(Esc(t)).Append("</dc:title>");
        }

        if (core.Description is { Length: > 0 } d)
        {
            sb.Append("<dc:description>").Append(Esc(d)).Append("</dc:description>");
        }

        if (core.Subject is { Length: > 0 } s)
        {
            sb.Append("<dc:subject>").Append(Esc(s)).Append("</dc:subject>");
        }

        if (core.Keywords is { Length: > 0 } k)
        {
            foreach (string kw in k.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                sb.Append("<meta:keyword>").Append(Esc(kw)).Append("</meta:keyword>");
            }
        }

        if (core.Creator is { Length: > 0 } c)
        {
            sb.Append("<meta:initial-creator>").Append(Esc(c)).Append("</meta:initial-creator>");
        }

        if (core.Created is { } created)
        {
            sb.Append("<meta:creation-date>").Append(created.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)).Append("</meta:creation-date>");
        }

        if (core.LastModifiedBy is { Length: > 0 } lm)
        {
            sb.Append("<dc:creator>").Append(Esc(lm)).Append("</dc:creator>");
        }

        if (core.Modified is { } modified)
        {
            sb.Append("<dc:date>").Append(modified.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)).Append("</dc:date>");
        }

        if (core.Language is { Length: > 0 } lang)
        {
            sb.Append("<dc:language>").Append(Esc(lang)).Append("</dc:language>");
        }

        foreach (CustomProperty p in props.Custom.List())
        {
            sb.Append("<meta:user-defined meta:name=\"").Append(Esc(p.Name)).Append("\">").Append(Esc(p.Text)).Append("</meta:user-defined>");
        }

        sb.Append("</office:meta></office:document-meta>");
        return sb.ToString();
    }

    // ---- settings (view state) -----------------------------------------------------------------------------------------------------------------

    private static string ConfigItem(string name, string type, string value) =>
        "<config:config-item config:name=\"" + name + "\" config:type=\"" + type + "\">" + Esc(value) + "</config:config-item>";

    // OpenDocument 1.3 Part 1 settings.xml: per-table split positions (mode 2 = frozen) under the view settings.
    private string BuildSettings()
    {
        var tables = new StringBuilder();
        foreach (Worksheet ws in _sheets)
        {
            ws.TryGetFreezePane(out int rows, out int columns);
            tables.Append("<config:config-item-map-entry config:name=\"").Append(Esc(ws.Name)).Append("\">");
            tables.Append(ConfigItem("HorizontalSplitMode", "short", columns > 0 ? "2" : "0"));
            tables.Append(ConfigItem("VerticalSplitMode", "short", rows > 0 ? "2" : "0"));
            tables.Append(ConfigItem("HorizontalSplitPosition", "int", I(columns)));
            tables.Append(ConfigItem("VerticalSplitPosition", "int", I(rows)));
            tables.Append(ConfigItem("ActiveSplitRange", "short", rows > 0 && columns > 0 ? "3" : rows > 0 ? "2" : "2"));
            tables.Append(ConfigItem("PositionLeft", "int", "0"));
            tables.Append(ConfigItem("PositionRight", "int", I(columns)));
            tables.Append(ConfigItem("PositionTop", "int", "0"));
            tables.Append(ConfigItem("PositionBottom", "int", I(rows)));
            MarkupNode sheetViews = FrameChild(ws, "sheetViews");
            foreach (MarkupNode sv in sheetViews.IsNull ? [] : sheetViews.ChildElements)
            {
                if (sv.GetAttribute("showGridLines") is "0" or "false")
                {
                    tables.Append(ConfigItem("ShowGrid", "boolean", "false"));
                }

                break;
            }
            tables.Append("</config:config-item-map-entry>");
        }

        var sb = new StringBuilder(Decl);
        sb.Append("<office:document-settings").Append(Ns).Append(" office:version=\"1.3\"><office:settings>");
        sb.Append("<config:config-item-set config:name=\"ooo:view-settings\"><config:config-item-map-indexed config:name=\"Views\"><config:config-item-map-entry>");
        sb.Append(ConfigItem("ViewId", "string", "view1"));
        sb.Append("<config:config-item-map-named config:name=\"Tables\">").Append(tables).Append("</config:config-item-map-named>");
        sb.Append(ConfigItem("ActiveTable", "string", ActiveSheetName()));
        sb.Append("</config:config-item-map-entry></config:config-item-map-indexed></config:config-item-set>");
        sb.Append("</office:settings></office:document-settings>");
        return sb.ToString();
    }

    private string ActiveSheetName()
    {
        foreach (MarkupNode v in Descendants(_wb.Markup.Root, "workbookView"))
        {
            if (int.TryParse(v.GetAttribute("activeTab"), NumberStyles.None, CultureInfo.InvariantCulture, out int active) && active >= 0 && active < _wb.Sheets.Count && _wb.Sheets[active].Kind == SheetKind.Worksheet)
            {
                return _wb.Sheets[active].Name;
            }
        }

        return _sheets[0].Name;
    }

    private static IEnumerable<MarkupNode> Descendants(MarkupNode root, string local)
    {
        foreach (MarkupNode c in root.ChildElements)
        {
            if (c.LocalNameString == local)
            {
                yield return c;
            }
            else
            {
                foreach (MarkupNode d in Descendants(c, local))
                {
                    yield return d;
                }
            }
        }
    }
}
