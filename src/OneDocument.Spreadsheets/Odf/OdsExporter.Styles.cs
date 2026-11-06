using System.Globalization;
using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Markup.Xml;
using RecursiveAI.OneDocument.NumberFormats;
using RecursiveAI.OneDocument.Odf;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Spreadsheets.Odf;

// Cell, column, row and table styles; data styles; page layouts and master pages.
internal sealed partial class OdsExporter
{
    private readonly StringBuilder _auto = new();
    private readonly StringBuilder _pageLayouts = new();
    private readonly StringBuilder _masterPages = new();
    private readonly Dictionary<int, string> _cellStyles = [];
    private readonly Dictionary<string, string> _dataStyles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _columnStyles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _rowStyles = new(StringComparer.Ordinal);
    private readonly SortedSet<string> _fontNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> _reportedIssues = new(StringComparer.Ordinal);
    private int _tableStyleCount;
    private int _pageLayoutCount;

    private void PrepareStyles()
    {
        // Cell format 0 is the default look of the workbook.
        _cellStyles[0] = "Default";
        ResolvedCellStyle zero = _wb.Styles.Resolve(0);
        if (zero.Font.Name is { Length: > 0 } name)
        {
            _fontNames.Add(name);
        }
    }

    private string RgbOf(StyleColor color)
    {
        if (color.Kind is ColorKind.None or ColorKind.Auto)
        {
            return string.Empty;
        }

        int argb = _wb.Styles.ResolveColor(color);
        return "#" + (argb & 0xFFFFFF).ToString("x6", CultureInfo.InvariantCulture);
    }

    // The data style for a number-format code (null for General).
    private string? DataStyleOf(string code)
    {
        if (string.IsNullOrEmpty(code) || code.Equals("General", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (_dataStyles.TryGetValue(code, out string? existing))
        {
            return existing;
        }

        string name = "N" + I(_dataStyles.Count + 1);
        var issues = new List<string>();
        if (!OdsDataStyleWriter.Write(code, name, _auto, issues))
        {
            return null;
        }

        _dataStyles[code] = name;
        foreach (string issue in issues.Distinct(StringComparer.Ordinal))
        {
            if (_reportedIssues.Add(issue))
            {
                Lost("loss.number-format", "number formats", "Part of a number format has no counterpart in OpenDocument and was left out: " + issue + ".");
            }
        }

        return name;
    }

    private static string BorderValue(BorderSide side, string color, out string? lineWidth)
    {
        lineWidth = null;
        (string width, string style) = side.Style switch
        {
            BorderLineStyle.Hair => ("0.26pt", "solid"),
            BorderLineStyle.Thin => ("0.75pt", "solid"),
            BorderLineStyle.Medium => ("1.5pt", "solid"),
            BorderLineStyle.Thick => ("2.25pt", "solid"),
            BorderLineStyle.Doubled => ("2.25pt", "double"),
            BorderLineStyle.Dotted => ("0.75pt", "dotted"),
            BorderLineStyle.Dashed or BorderLineStyle.DashDot or BorderLineStyle.DashDotDot or BorderLineStyle.SlantDashDot => ("0.75pt", "dashed"),
            BorderLineStyle.MediumDashed or BorderLineStyle.MediumDashDot or BorderLineStyle.MediumDashDotDot => ("1.5pt", "dashed"),
            _ => ("0.75pt", "solid"),
        };
        if (style == "double")
        {
            lineWidth = "0.0007in 0.0007in 0.0007in";
        }

        return width + " " + style + " " + (color.Length == 0 ? "#000000" : color);
    }

    private string CellStyleOf(int xf)
    {
        if (_cellStyles.TryGetValue(xf, out string? existing))
        {
            return existing;
        }

        ResolvedCellStyle r = _wb.Styles.Resolve(xf);
        string name = "ce" + I(_cellStyles.Count);
        var cell = new StringBuilder();
        var par = new StringBuilder();
        var text = new StringBuilder();

        if (r.Fill.Pattern == FillPattern.Solid && RgbOf(r.Fill.Foreground) is { Length: > 0 } bg)
        {
            cell.Append(" fo:background-color=\"").Append(bg).Append('"');
        }
        else if (r.Fill.Pattern != FillPattern.None || r.Fill.Gradient is not null)
        {
            if (r.Fill.Gradient is null && RgbOf(r.Fill.Foreground) is { Length: > 0 } approx)
            {
                cell.Append(" fo:background-color=\"").Append(approx).Append('"');
            }

            if (_reportedIssues.Add("pattern-fill"))
            {
                Lost("loss.fills", "pattern and gradient fills", "Pattern fills are written as a solid colour and gradient fills are dropped.");
            }
        }

        CellBorder b = r.Border;
        foreach ((string side, BorderSide s, string lineAttr) in new[] { ("left", b.Left, "style:border-line-width-left"), ("right", b.Right, "style:border-line-width-right"), ("top", b.Top, "style:border-line-width-top"), ("bottom", b.Bottom, "style:border-line-width-bottom") })
        {
            if (s.Style == BorderLineStyle.None)
            {
                continue;
            }

            cell.Append(" fo:border-").Append(side).Append("=\"").Append(BorderValue(s, RgbOf(s.Color), out string? lw)).Append('"');
            if (lw is not null)
            {
                cell.Append(' ').Append(lineAttr).Append("=\"").Append(lw).Append('"');
            }
        }

        if (b.Diagonal.Style != BorderLineStyle.None)
        {
            string diag = BorderValue(b.Diagonal, RgbOf(b.Diagonal.Color), out _);
            if (b.DiagonalUp)
            {
                cell.Append(" style:diagonal-bl-tr=\"").Append(diag).Append('"');
            }

            if (b.DiagonalDown)
            {
                cell.Append(" style:diagonal-tl-br=\"").Append(diag).Append('"');
            }
        }

        CellAlignment a = r.Alignment;
        if (a.WrapText)
        {
            cell.Append(" fo:wrap-option=\"wrap\"");
        }

        switch (a.Vertical)
        {
            case CellVerticalAlignment.Top:
                cell.Append(" style:vertical-align=\"top\"");
                break;
            case CellVerticalAlignment.Center:
                cell.Append(" style:vertical-align=\"middle\"");
                break;
            case CellVerticalAlignment.Bottom:
                cell.Append(" style:vertical-align=\"bottom\"");
                break;
            default:
                break;
        }

        if (a.ShrinkToFit)
        {
            cell.Append(" style:shrink-to-fit=\"true\"");
        }

        if (a.TextRotation is > 0 and <= 90)
        {
            cell.Append(" style:rotation-angle=\"").Append(a.TextRotation).Append("\" style:rotation-align=\"none\"");
        }
        else if (a.TextRotation is > 90 and <= 180)
        {
            cell.Append(" style:rotation-angle=\"").Append(450 - a.TextRotation).Append("\" style:rotation-align=\"none\"");
        }

        if (a.ReadingOrder == 2)
        {
            cell.Append(" style:direction=\"rtl\"");
        }

        string? align = a.Horizontal switch
        {
            CellHorizontalAlignment.Left or CellHorizontalAlignment.Fill => "start",
            CellHorizontalAlignment.Right => "end",
            CellHorizontalAlignment.Center or CellHorizontalAlignment.CenterContinuous => "center",
            CellHorizontalAlignment.Justify or CellHorizontalAlignment.Distributed => "justify",
            _ => null,
        };
        if (align is not null)
        {
            cell.Append(" style:text-align-source=\"fix\"");
            par.Append(" fo:text-align=\"").Append(align).Append('"');
        }

        if (a.Indent > 0)
        {
            par.Append(" fo:margin-left=\"").Append(OdfLengths.Pt(a.Indent * 7.5)).Append('"');
        }

        CellProtection p = r.Protection;
        if (!p.Locked || p.Hidden)
        {
            cell.Append(" style:cell-protect=\"").Append(p.Locked ? "protected formula-hidden" : p.Hidden ? "formula-hidden" : "none").Append('"');
        }

        CellFont f = r.Font;
        if (f.Name is { Length: > 0 } fontName)
        {
            _fontNames.Add(fontName);
            text.Append(" style:font-name=\"").Append(Esc(fontName)).Append('"');
        }

        if (f.Size > 0)
        {
            text.Append(" fo:font-size=\"").Append(OdfLengths.Pt(f.Size)).Append('"');
        }

        text.Append(" fo:font-weight=\"").Append(f.Bold ? "bold" : "normal").Append("\" fo:font-style=\"").Append(f.Italic ? "italic" : "normal").Append('"');
        if (f.Underline != FontUnderline.None)
        {
            text.Append(" style:text-underline-style=\"solid\" style:text-underline-type=\"").Append(f.Underline is FontUnderline.Doubled or FontUnderline.DoubleAccounting ? "double" : "single").Append("\" style:text-underline-width=\"auto\" style:text-underline-color=\"font-color\"");
        }

        if (f.Strike)
        {
            text.Append(" style:text-line-through-style=\"solid\" style:text-line-through-type=\"single\"");
        }

        if (f.VerticalAlignment != FontVerticalAlignment.Baseline)
        {
            text.Append(" style:text-position=\"").Append(f.VerticalAlignment == FontVerticalAlignment.Superscript ? "super 58%" : "sub 58%").Append('"');
        }

        if (RgbOf(f.Color) is { Length: > 0 } color)
        {
            text.Append(" fo:color=\"").Append(color).Append('"');
        }

        string? data = DataStyleOf(r.NumberFormatCode);
        _auto.Append("<style:style style:name=\"").Append(name).Append("\" style:family=\"table-cell\" style:parent-style-name=\"Default\"");
        if (data is not null)
        {
            _auto.Append(" style:data-style-name=\"").Append(data).Append('"');
        }

        _auto.Append('>');
        if (cell.Length > 0)
        {
            _auto.Append("<style:table-cell-properties").Append(cell).Append("/>");
        }

        if (par.Length > 0)
        {
            _auto.Append("<style:paragraph-properties").Append(par).Append("/>");
        }

        _auto.Append("<style:text-properties").Append(text).Append("/></style:style>");
        _cellStyles[xf] = name;
        return name;
    }

    private string ColumnStyleOf(double points)
    {
        string width = OdfLengths.Cm(points);
        if (!_columnStyles.TryGetValue(width, out string? name))
        {
            name = "co" + I(_columnStyles.Count + 1);
            _columnStyles[width] = name;
            _auto.Append("<style:style style:name=\"").Append(name).Append("\" style:family=\"table-column\"><style:table-column-properties fo:break-before=\"auto\" style:column-width=\"").Append(width).Append("\"/></style:style>");
        }

        return name;
    }

    private string RowStyleOf(double points, bool optimal)
    {
        string key = (points > 0 ? OdfLengths.Cm(points) : "-") + (optimal ? "o" : "f");
        if (!_rowStyles.TryGetValue(key, out string? name))
        {
            name = "ro" + I(_rowStyles.Count + 1);
            _rowStyles[key] = name;
            _auto.Append("<style:style style:name=\"").Append(name).Append("\" style:family=\"table-row\"><style:table-row-properties");
            if (points > 0)
            {
                _auto.Append(" style:row-height=\"").Append(OdfLengths.Cm(points)).Append('"');
            }

            _auto.Append(" fo:break-before=\"auto\" style:use-optimal-row-height=\"").Append(optimal ? "true" : "false").Append("\"/></style:style>");
        }

        return name;
    }

    // Excel's column width is in characters of the default font's digit: at 96 dpi a 7 pixel digit with 5 pixels of padding.
    private static double WidthPoints(double characters) => ((characters * 7) + 5) * 72 / 96;

    private string FontDecls()
    {
        var sb = new StringBuilder();
        foreach (string f in _fontNames)
        {
            string family = f.Contains(' ', StringComparison.Ordinal) ? "'" + f + "'" : f;
            sb.Append("<style:font-face style:name=\"").Append(Esc(f)).Append("\" svg:font-family=\"").Append(Esc(family)).Append("\"/>");
        }

        return sb.ToString();
    }

    private string BuildStyles()
    {
        ResolvedCellStyle zero = _wb.Styles.Resolve(0);
        var font = new StringBuilder();
        if (zero.Font.Name is { Length: > 0 } fn)
        {
            font.Append(" style:font-name=\"").Append(Esc(fn)).Append('"');
        }

        if (zero.Font.Size > 0)
        {
            font.Append(" fo:font-size=\"").Append(OdfLengths.Pt(zero.Font.Size)).Append('"');
        }

        var sb = new StringBuilder(Decl);
        sb.Append("<office:document-styles").Append(Ns).Append(" office:version=\"1.3\">");
        sb.Append("<office:font-face-decls>").Append(FontDecls()).Append("</office:font-face-decls>");
        sb.Append("<office:styles>");
        sb.Append("<style:default-style style:family=\"table-cell\"><style:table-cell-properties style:rotation-align=\"none\"/><style:text-properties").Append(font).Append("/></style:default-style>");
        sb.Append("<style:style style:name=\"Default\" style:family=\"table-cell\"/>");
        sb.Append("</office:styles>");
        sb.Append("<office:automatic-styles>").Append(_pageLayouts).Append("</office:automatic-styles>");
        sb.Append("<office:master-styles>").Append(_masterPages).Append("</office:master-styles>");
        sb.Append("</office:document-styles>");
        return sb.ToString();
    }

    private string TableStyleOf(Worksheet ws, string? masterPage, bool rtl, string? tabColor)
    {
        string name = "ta" + I(++_tableStyleCount);
        _auto.Append("<style:style style:name=\"").Append(name).Append("\" style:family=\"table\"");
        if (masterPage is not null)
        {
            _auto.Append(" style:master-page-name=\"").Append(masterPage).Append('"');
        }

        _auto.Append("><style:table-properties table:display=\"").Append(ws.State == SheetState.Visible ? "true" : "false").Append("\" style:writing-mode=\"").Append(rtl ? "rl-tb" : "lr-tb").Append('"');
        if (tabColor is not null)
        {
            _auto.Append(" tableooo:tab-color=\"").Append(tabColor).Append('"');
        }

        _auto.Append("/></style:style>");
        return name;
    }
}
