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

// Cell styles, data styles and the view settings of an OpenDocument spreadsheet, as the style tables of a worksheet package.
internal sealed partial class OdsImporter
{
    private readonly Dictionary<string, int> _xfByStyle = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _xfKey = new(StringComparer.Ordinal);
    private readonly List<string> _xfs = [];
    private readonly Dictionary<string, int> _fontKey = new(StringComparer.Ordinal);
    private readonly List<string> _fonts = [];
    private readonly Dictionary<string, int> _fillKey = new(StringComparer.Ordinal);
    private readonly List<string> _fills = [];
    private readonly Dictionary<string, int> _borderKey = new(StringComparer.Ordinal);
    private readonly List<string> _borders = [];
    private readonly Dictionary<string, int> _numFmtKey = new(StringComparer.Ordinal);
    private readonly List<string> _numFmts = [];
    private readonly Dictionary<string, int> _dxfKey = new(StringComparer.Ordinal);
    private readonly List<string> _dxfs = [];
    private readonly Dictionary<string, string> _fontFaces = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _dataCodes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _reportedNumberIssues = new(StringComparer.Ordinal);

    private void PrepareStyles()
    {
        foreach (MarkupNode root in new[] { _pkg.ContentRoot, _pkg.StylesRoot })
        {
            MarkupNode decls = Child(root, Office, "font-face-decls");
            if (decls.IsNull)
            {
                continue;
            }

            foreach (MarkupNode f in decls.ChildElements)
            {
                string? name = A(f, Style, "name");
                string? family = A(f, OdfNamespaces.Svg, "font-family");
                if (name is not null && family is not null)
                {
                    _fontFaces.TryAdd(name, family.Trim('\'', '"', ' '));
                }
            }
        }

        // The default cell style is cell format 0, so a cell without a style looks the way the document's own default does.
        OdfStyle? def = OdsStyleLookup.Find(_styles, "table-cell", "Default") ?? _styles.GetDefault("table-cell");
        _ = XfOf(def, default);
        _xfByStyle["Default"] = 0;
    }

    // The cell format index for a named cell style (null or unknown: format 0).
    private int StyleIndex(string? styleName, MarkupNode at = default)
    {
        if (string.IsNullOrEmpty(styleName))
        {
            return 0;
        }

        if (_xfByStyle.TryGetValue(styleName, out int cached))
        {
            return cached;
        }

        OdfStyle? style = OdsStyleLookup.Find(_styles, "table-cell", styleName);
        if (style is null)
        {
            Defect("ods.style-missing", DefectKind.Reference, DefectSeverity.Repaired, "The cell style '" + styleName + "' does not exist; the default was used.", At(at));
            _xfByStyle[styleName] = 0;
            return 0;
        }

        int index = XfOf(style, at);
        _xfByStyle[styleName] = index;
        return index;
    }

    private string? Prop(OdfStyle? style, string group, string ns, string name) => style is null ? null : _styles.Resolve(style, group, ns, name);

    private int XfOf(OdfStyle? style, MarkupNode at)
    {
        int numFmt = 0;
        if (style is not null)
        {
            string? dataName = null;
            foreach (OdfStyle s in _styles.Chain(style))
            {
                dataName = A(s.Element, Style, "data-style-name");
                if (dataName is not null)
                {
                    break;
                }
            }

            if (dataName is not null)
            {
                numFmt = NumFmtId(DataCode(dataName, at));
            }
        }

        int font = Intern(_fontKey, _fonts, FontXml(style, dxf: false));
        string? bg = Prop(style, "table-cell-properties", Fo, "background-color");
        int fill = 0;
        if (bg is not null && Rgb(bg) is { } fillRgb)
        {
            fill = Intern(_fillKey, _fills, "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"" + fillRgb + "\"/><bgColor indexed=\"64\"/></patternFill></fill>") + 2;
        }

        string? border = BorderXml(style);
        int borderId = border is null ? 0 : Intern(_borderKey, _borders, border) + 1;
        string alignment = AlignmentXml(style);
        string? protect = Prop(style, "table-cell-properties", Style, "cell-protect");
        // OpenDocument 1.3 Part 1 style:cell-protect: none, protected, formula-hidden, or both (default: protected).
        bool locked = protect is null || protect.Contains("protected", StringComparison.Ordinal);
        bool hidden = protect is not null && (protect.Contains("formula-hidden", StringComparison.Ordinal) || protect.Contains("hidden-and-protected", StringComparison.Ordinal));

        var xf = new StringBuilder();
        xf.Append("<xf numFmtId=\"").Append(numFmt).Append("\" fontId=\"").Append(font).Append("\" fillId=\"").Append(fill)
            .Append("\" borderId=\"").Append(borderId).Append("\" xfId=\"0\" applyNumberFormat=\"1\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\"");
        if (alignment.Length > 0)
        {
            xf.Append(" applyAlignment=\"1\"");
        }

        if (!locked || hidden)
        {
            xf.Append(" applyProtection=\"1\"");
        }

        if (alignment.Length == 0 && locked && !hidden)
        {
            xf.Append("/>");
        }
        else
        {
            xf.Append('>').Append(alignment);
            if (!locked || hidden)
            {
                xf.Append("<protection locked=\"").Append(locked ? '1' : '0').Append("\" hidden=\"").Append(hidden ? '1' : '0').Append("\"/>");
            }

            xf.Append("</xf>");
        }

        return Intern(_xfKey, _xfs, xf.ToString());
    }

    private static int Intern(Dictionary<string, int> index, List<string> list, string xml)
    {
        if (!index.TryGetValue(xml, out int i))
        {
            i = list.Count;
            list.Add(xml);
            index[xml] = i;
        }

        return i;
    }

    private static string? Rgb(string? css)
    {
        if (css is { Length: 7 } && css[0] == '#' && int.TryParse(css.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _))
        {
            return "FF" + css[1..].ToUpperInvariant();
        }

        return null;
    }

    // ---- number formats -------------------------------------------------------------------------------------------------------------

    private string DataCode(string dataStyleName, MarkupNode at)
    {
        if (_dataCodes.TryGetValue(dataStyleName, out string? cached))
        {
            return cached;
        }

        OdfStyle? ds = OdsStyleLookup.Find(_styles, "data", dataStyleName);
        string code = "General";
        if (ds is null)
        {
            Defect("ods.style-missing", DefectKind.Reference, DefectSeverity.Repaired, "The data style '" + dataStyleName + "' does not exist; General was used.", At(at));
        }
        else
        {
            var issues = new List<string>();
            code = OdsDataStyles.ToCode(ds, _styles, issues);
            foreach (string issue in issues.Distinct(StringComparer.Ordinal))
            {
                if (_reportedNumberIssues.Add(issue))
                {
                    Lost("loss.number-format", "number formats", "Part of a number format has no counterpart and was left out: " + issue + ".");
                }
            }
        }

        _dataCodes[dataStyleName] = code;
        return code;
    }

    private int NumFmtId(string code)
    {
        if (code == "General")
        {
            return 0;
        }

        int builtin = NumberFormat.FindBuiltInId(code);
        if (builtin >= 0)
        {
            return builtin;
        }

        if (!_numFmtKey.TryGetValue(code, out int id))
        {
            id = 164 + _numFmts.Count;
            _numFmts.Add(code);
            _numFmtKey[code] = id;
        }

        return id;
    }

    // ---- fonts, borders, alignment ---------------------------------------------------------------------------------------------------

    private string FontXml(OdfStyle? style, bool dxf)
    {
        var sb = new StringBuilder("<font>");
        string? weight = Prop(style, "text-properties", Fo, "font-weight");
        if (weight is "bold" || (int.TryParse(weight, NumberStyles.None, CultureInfo.InvariantCulture, out int w) && w >= 600))
        {
            sb.Append("<b/>");
        }

        if (Prop(style, "text-properties", Fo, "font-style") is "italic" or "oblique")
        {
            sb.Append("<i/>");
        }

        string? strike = Prop(style, "text-properties", Style, "text-line-through-style");
        if (strike is not null and not "none")
        {
            sb.Append("<strike/>");
        }

        string? underline = Prop(style, "text-properties", Style, "text-underline-style");
        if (underline is not null and not "none")
        {
            sb.Append(Prop(style, "text-properties", Style, "text-underline-type") == "double" ? "<u val=\"double\"/>" : "<u/>");
        }

        string? position = Prop(style, "text-properties", Style, "text-position");
        if (position is not null)
        {
            if (position.StartsWith("super", StringComparison.Ordinal) || (position.Length > 0 && char.IsAsciiDigit(position[0]) && position[0] != '0'))
            {
                sb.Append("<vertAlign val=\"superscript\"/>");
            }
            else if (position.StartsWith("sub", StringComparison.Ordinal) || position.StartsWith('-'))
            {
                sb.Append("<vertAlign val=\"subscript\"/>");
            }
        }

        if (!dxf || Prop(style, "text-properties", Fo, "font-size") is not null)
        {
            string? size = Prop(style, "text-properties", Fo, "font-size");
            double points = OdfLengths.TryPoints(size, out double p) && p > 0 ? p : 10;
            sb.Append("<sz val=\"").Append(Inv(Math.Round(points, 2))).Append("\"/>");
        }

        string? color = Prop(style, "text-properties", Fo, "color");
        if (Rgb(color) is { } rgb)
        {
            sb.Append("<color rgb=\"").Append(rgb).Append("\"/>");
        }

        if (!dxf)
        {
            string? faceKey = Prop(style, "text-properties", Style, "font-name") ?? Prop(style, "text-properties", Fo, "font-family");
            string face = faceKey is not null && _fontFaces.TryGetValue(faceKey, out string? family) ? family : faceKey ?? "Arial";
            sb.Append("<name val=\"").Append(OdfLengths.Escape(face)).Append("\"/><family val=\"2\"/>");
        }

        return sb.Append("</font>").ToString();
    }

    private string? BorderXml(OdfStyle? style, bool dxf = false)
    {
        string? all = Prop(style, "table-cell-properties", Fo, "border");
        var sb = new StringBuilder();
        bool any = false;
        foreach ((string element, string side) in new[] { ("left", "left"), ("right", "right"), ("top", "top"), ("bottom", "bottom") })
        {
            string? value = Prop(style, "table-cell-properties", Fo, "border-" + side) ?? all;
            (string? line, string? color) = BorderLine(value);
            if (line is null)
            {
                sb.Append('<').Append(element).Append("/>");
                continue;
            }

            any = true;
            sb.Append('<').Append(element).Append(" style=\"").Append(line).Append("\"><color ").Append(color is null ? "auto=\"1\"" : "rgb=\"" + color + "\"").Append("/></").Append(element).Append('>');
        }

        string? up = Prop(style, "table-cell-properties", Style, "diagonal-bl-tr");
        string? down = Prop(style, "table-cell-properties", Style, "diagonal-tl-br");
        (string? upLine, string? upColor) = BorderLine(up);
        (string? downLine, string? downColor) = BorderLine(down);
        if (upLine is not null || downLine is not null)
        {
            any = true;
            string line = upLine ?? downLine!;
            string? color = upLine is not null ? upColor : downColor;
            sb.Append("<diagonal style=\"").Append(line).Append("\"><color ").Append(color is null ? "auto=\"1\"" : "rgb=\"" + color + "\"").Append("/></diagonal>");
        }
        else
        {
            sb.Append("<diagonal/>");
        }

        if (!any)
        {
            return null;
        }

        string attrs = (upLine is not null ? " diagonalUp=\"1\"" : string.Empty) + (downLine is not null ? " diagonalDown=\"1\"" : string.Empty);
        _ = dxf;
        return "<border" + attrs + ">" + sb + "</border>";
    }

    // OpenDocument 1.3 Part 1 §20.? fo:border: "width style color", e.g. "0.74pt solid #000000"; the line styles are those of CSS (plus fine-dashed and double-thin from the producer).
    private static (string? Line, string? Color) BorderLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith("none", StringComparison.Ordinal) || value.StartsWith("hidden", StringComparison.Ordinal))
        {
            return (null, null);
        }

        string[] parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        double width = 0.75;
        string style = "solid";
        string? color = null;
        foreach (string part in parts)
        {
            if (part.StartsWith('#'))
            {
                color = Rgb(part);
            }
            else if (char.IsAsciiDigit(part[0]) || part[0] == '.')
            {
                if (OdfLengths.TryPoints(part, out double pts))
                {
                    width = pts;
                }
            }
            else
            {
                style = part;
            }
        }

        string line = style switch
        {
            "double" or "double-thin" => "double",
            "dotted" => "dotted",
            "dashed" or "fine-dashed" => width > 1.2 ? "mediumDashed" : "dashed",
            "dot-dashed" => width > 1.2 ? "mediumDashDot" : "dashDot",
            "dot-dot-dashed" => width > 1.2 ? "mediumDashDotDot" : "dashDotDot",
            _ => width < 0.5 ? "hair" : width <= 1.2 ? "thin" : width <= 2.0 ? "medium" : "thick",
        };
        return (line, color);
    }

    private string AlignmentXml(OdfStyle? style)
    {
        var attrs = new StringBuilder();
        string? source = Prop(style, "table-cell-properties", Style, "text-align-source");
        string? align = Prop(style, "paragraph-properties", Fo, "text-align");
        string? horizontal = null;
        if (source != "value-type" && align is not null)
        {
            horizontal = align switch
            {
                "start" or "left" => "left",
                "end" or "right" => "right",
                "center" => "center",
                "justify" => "justify",
                _ => null,
            };
        }

        int indent = 0;
        if (OdfLengths.TryPoints(Prop(style, "paragraph-properties", Fo, "margin-left"), out double margin) && margin > 0)
        {
            indent = (int)Math.Round(margin / 7.5);
            if (indent > 0 && horizontal is null)
            {
                horizontal = "left";
            }
        }

        if (horizontal is not null)
        {
            attrs.Append(" horizontal=\"").Append(horizontal).Append('"');
        }

        string? vertical = Prop(style, "table-cell-properties", Style, "vertical-align");
        if (vertical is "top" or "middle")
        {
            attrs.Append(" vertical=\"").Append(vertical == "middle" ? "center" : "top").Append('"');
        }

        if (int.TryParse(Prop(style, "table-cell-properties", Style, "rotation-angle"), NumberStyles.None, CultureInfo.InvariantCulture, out int angle) && angle > 0)
        {
            int rotation = angle <= 90 ? angle : angle >= 270 && angle < 360 ? 90 + (360 - angle) : 0;
            if (rotation > 0)
            {
                attrs.Append(" textRotation=\"").Append(rotation).Append('"');
            }
        }

        if (Prop(style, "table-cell-properties", Fo, "wrap-option") == "wrap")
        {
            attrs.Append(" wrapText=\"1\"");
        }

        if (indent > 0 && horizontal is "left" or "right")
        {
            attrs.Append(" indent=\"").Append(indent).Append('"');
        }

        if (Prop(style, "table-cell-properties", Style, "shrink-to-fit") == "true")
        {
            attrs.Append(" shrinkToFit=\"1\"");
        }

        if (Prop(style, "table-cell-properties", Style, "direction") == "rtl")
        {
            attrs.Append(" readingOrder=\"2\"");
        }

        return attrs.Length == 0 ? string.Empty : "<alignment" + attrs + "/>";
    }

    // The differential format (ECMA-376 Part 1 §18.8.14) for the cell style a conditional format applies.
    private int DxfIndex(string? styleName, MarkupNode at)
    {
        OdfStyle? style = styleName is null ? null : OdsStyleLookup.Find(_styles, "table-cell", styleName);
        if (style is null)
        {
            Defect("ods.style-missing", DefectKind.Reference, DefectSeverity.Repaired, "The style '" + styleName + "' a conditional format applies does not exist.", At(at));
            return -1;
        }

        var sb = new StringBuilder("<dxf>");
        string font = FontXml(style, dxf: true);
        if (font != "<font></font>")
        {
            sb.Append(font);
        }

        string? dataName = null;
        foreach (OdfStyle s in _styles.Chain(style))
        {
            dataName = A(s.Element, Style, "data-style-name");
            if (dataName is not null)
            {
                break;
            }
        }

        if (dataName is not null)
        {
            string code = DataCode(dataName, at);
            if (code != "General")
            {
                sb.Append("<numFmt numFmtId=\"").Append(NumFmtId(code)).Append("\" formatCode=\"").Append(OdfLengths.Escape(code)).Append("\"/>");
            }
        }

        if (Rgb(Prop(style, "table-cell-properties", Fo, "background-color")) is { } bg)
        {
            sb.Append("<fill><patternFill patternType=\"solid\"><fgColor rgb=\"").Append(bg).Append("\"/><bgColor rgb=\"").Append(bg).Append("\"/></patternFill></fill>");
        }

        if (BorderXml(style, dxf: true) is { } border)
        {
            sb.Append(border);
        }

        string xml = sb.Append("</dxf>").ToString();
        return Intern(_dxfKey, _dxfs, xml);
    }

    private ReadOnlyMemory<byte> BuildStyles()
    {
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<styleSheet xmlns=\"" + MainNs + "\">");
        if (_numFmts.Count > 0)
        {
            sb.Append("<numFmts count=\"").Append(_numFmts.Count).Append("\">");
            for (int i = 0; i < _numFmts.Count; i++)
            {
                sb.Append("<numFmt numFmtId=\"").Append(164 + i).Append("\" formatCode=\"").Append(OdfLengths.Escape(_numFmts[i])).Append("\"/>");
            }

            sb.Append("</numFmts>");
        }

        sb.Append("<fonts count=\"").Append(_fonts.Count).Append("\">").AppendJoin(string.Empty, _fonts).Append("</fonts>");
        sb.Append("<fills count=\"").Append(_fills.Count + 2).Append("\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill>")
            .AppendJoin(string.Empty, _fills).Append("</fills>");
        sb.Append("<borders count=\"").Append(_borders.Count + 1).Append("\"><border><left/><right/><top/><bottom/><diagonal/></border>")
            .AppendJoin(string.Empty, _borders).Append("</borders>");
        sb.Append("<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>");
        sb.Append("<cellXfs count=\"").Append(_xfs.Count).Append("\">").AppendJoin(string.Empty, _xfs).Append("</cellXfs>");
        sb.Append("<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>");
        if (_dxfs.Count > 0)
        {
            sb.Append("<dxfs count=\"").Append(_dxfs.Count).Append("\">").AppendJoin(string.Empty, _dxfs).Append("</dxfs>");
        }

        sb.Append("</styleSheet>");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    // ---- view settings (settings.xml) ---------------------------------------------------------------------------------------------------

    private sealed class SheetView
    {
        public int FrozenColumns;
        public int FrozenRows;
        public bool? ShowGrid;
        public int? Zoom;
    }

    private readonly Dictionary<string, SheetView> _views = new(StringComparer.Ordinal);
    private bool? _showGrid;

    // OpenDocument 1.3 Part 1 §? settings.xml: office:settings holds config:config-item-sets; the view settings keep, per view, the active table and,
    // per table, the split positions (mode 2 = frozen).
    private void ReadSettings()
    {
        MarkupNode root = _pkg.SettingsRoot;
        MarkupNode settings = Child(root, Office, "settings");
        if (settings.IsNull)
        {
            return;
        }

        foreach (MarkupNode set in settings.ChildElements)
        {
            string? setName = A(set, OdfNamespaces.Config, "name");
            if (setName == "ooo:configuration-settings")
            {
                foreach (MarkupNode item in set.ChildElements)
                {
                    if (A(item, OdfNamespaces.Config, "name") == "ShowGrid")
                    {
                        _showGrid = item.InnerText.Trim() == "true";
                    }
                }
            }
            else if (setName == "ooo:view-settings")
            {
                foreach (MarkupNode views in set.ChildElements)
                {
                    if (A(views, OdfNamespaces.Config, "name") != "Views")
                    {
                        continue;
                    }

                    foreach (MarkupNode view in views.ChildElements)
                    {
                        ReadView(view);
                        break;
                    }
                }
            }
        }
    }

    private string? _activeTableName;

    private void ReadView(MarkupNode view)
    {
        foreach (MarkupNode item in view.ChildElements)
        {
            string? name = A(item, OdfNamespaces.Config, "name");
            if (name == "ActiveTable")
            {
                _activeTableName = item.InnerText.Trim();
            }
            else if (name == "ShowGrid")
            {
                _showGrid = item.InnerText.Trim() == "true";
            }
            else if (name == "Tables")
            {
                foreach (MarkupNode table in item.ChildElements)
                {
                    string? sheet = A(table, OdfNamespaces.Config, "name");
                    if (sheet is null)
                    {
                        continue;
                    }

                    var sv = new SheetView();
                    int hMode = 0;
                    int vMode = 0;
                    foreach (MarkupNode prop in table.ChildElements)
                    {
                        string? pn = A(prop, OdfNamespaces.Config, "name");
                        string value = prop.InnerText.Trim();
                        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n);
                        switch (pn)
                        {
                            case "HorizontalSplitMode":
                                hMode = n;
                                break;
                            case "VerticalSplitMode":
                                vMode = n;
                                break;
                            case "HorizontalSplitPosition":
                                sv.FrozenColumns = Math.Max(0, n);
                                break;
                            case "VerticalSplitPosition":
                                sv.FrozenRows = Math.Max(0, n);
                                break;
                            case "ShowGrid":
                                sv.ShowGrid = value == "true";
                                break;
                            case "ZoomValue":
                                sv.Zoom = n;
                                break;
                            default:
                                break;
                        }
                    }

                    if (hMode != 2)
                    {
                        sv.FrozenColumns = 0;
                    }

                    if (vMode != 2)
                    {
                        sv.FrozenRows = 0;
                    }

                    _views[sheet] = sv;
                }
            }
        }
    }
}
