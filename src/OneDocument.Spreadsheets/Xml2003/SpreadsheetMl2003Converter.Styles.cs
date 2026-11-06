using System.Linq;
using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Spreadsheets.Xml2003;

internal sealed partial class SpreadsheetMl2003Converter
{
    // ---- styles ---------------------------------------------------------------------------------------------------------------------

    private static readonly string[] BorderPositions = ["Left", "Right", "Top", "Bottom", "DiagonalLeft", "DiagonalRight"];

    private void ReadStyles(MarkupNode root)
    {
        MarkupNode styles = Child(root, "Styles");
        if (styles.IsNull)
        {
            return;
        }

        int named = 0;
        foreach (MarkupNode st in styles.ChildElements)
        {
            if (Name(st) != "Style")
            {
                continue;
            }

            string? id = Attr(st, "ID");
            if (id is null)
            {
                continue;
            }

            if (_styleDefs.ContainsKey(id))
            {
                // Files exist that repeat an ID (a bare entry, then the full one). The first definition is kept.
                Defect("sml2003.duplicate-style", DefectSeverity.Repaired, "The style ID '" + id + "' is defined more than once; the first definition is used.", st);
                continue;
            }

            var bag = new Dictionary<string, string>(StringComparer.Ordinal);
            if (Attr(st, "Parent") is { } parent)
            {
                bag["_parent"] = parent;
            }

            if (Attr(st, "Name") is { Length: > 0 } name && id != "Default")
            {
                named++;
                _ = name;
            }

            foreach (MarkupNode part in st.ChildElements)
            {
                string kind = Name(part);
                switch (kind)
                {
                    case "Font" or "Interior" or "Alignment" or "Protection" or "NumberFormat":
                        CopyAttributes(part, kind + ".", bag);
                        break;
                    case "Borders":
                        foreach (MarkupNode b in part.ChildElements)
                        {
                            if (Name(b) == "Border" && Attr(b, "Position") is { } position)
                            {
                                CopyAttributes(b, "Border." + position + ".", bag);
                            }
                        }

                        break;
                }
            }

            _styleDefs[id] = bag;
        }

        if (named > 0)
        {
            Lost("loss.cell-style-names", "named cell styles", "Named cell styles became plain cell formats; their formatting was kept on each cell.", named);
        }
    }

    private static void CopyAttributes(MarkupNode n, string prefix, Dictionary<string, string> into)
    {
        foreach (MarkupAttributeRef a in n.Attributes)
        {
            if (!a.IsNamespaceDeclaration)
            {
                into[prefix + Encoding.UTF8.GetString(a.LocalName)] = a.Value;
            }
        }
    }

    private Dictionary<string, string> Resolve(string id, HashSet<string> visiting, MarkupNode at)
    {
        var merged = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!_styleDefs.TryGetValue(id, out Dictionary<string, string>? own) || !visiting.Add(id))
        {
            if (visiting.Contains(id))
            {
                Defect("sml2003.bad-style-ref", DefectSeverity.Repaired, "The style '" + id + "' is its own ancestor; the loop was cut.", at);
            }

            return merged;
        }

        string? parent = own.TryGetValue("_parent", out string? p) ? p : (id != "Default" && _styleDefs.ContainsKey("Default") ? "Default" : null);
        if (parent is not null)
        {
            if (_styleDefs.ContainsKey(parent))
            {
                foreach (KeyValuePair<string, string> kv in Resolve(parent, visiting, at))
                {
                    merged[kv.Key] = kv.Value;
                }
            }
            else
            {
                Defect("sml2003.bad-style-ref", DefectSeverity.Repaired, "The style '" + id + "' has the parent '" + parent + "', which does not exist.", at);
            }
        }

        foreach (KeyValuePair<string, string> kv in own)
        {
            if (kv.Key != "_parent")
            {
                merged[kv.Key] = kv.Value;
            }
        }

        visiting.Remove(id);
        return merged;
    }

    // Gets the cell format (xf) index for a style ID: 0 for the default style, and for an ID the file never defines.
    private int StyleIndex(string? id, MarkupNode at = default)
    {
        id ??= "Default";
        if (!_styleDefs.ContainsKey(id))
        {
            if (id != "Default")
            {
                Defect("sml2003.bad-style-ref", DefectSeverity.Repaired, "The style ID '" + id + "' is not defined; the default style was used.", at);
            }

            id = "Default";
        }

        Dictionary<string, string> resolved = Resolve(id, [], at);
        var key = new StringBuilder();
        foreach (KeyValuePair<string, string> kv in resolved.OrderBy(static kv => kv.Key, StringComparer.Ordinal))
        {
            key.Append(kv.Key).Append('=').Append(kv.Value).Append('\n');
        }

        string k = key.ToString();
        if (_xfIndex.TryGetValue(k, out int index))
        {
            return index;
        }

        index = _xfs.Count;
        _xfs.Add(resolved);
        _xfIndex[k] = index;
        return index;
    }

    private static string? Rgb(string? css)
    {
        if (css is { Length: 7 } && css[0] == '#' && uint.TryParse(css.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _))
        {
            return "FF" + css[1..].ToUpperInvariant();
        }

        return null;
    }

    private static string? Get(Dictionary<string, string> d, string key) => d.TryGetValue(key, out string? v) ? v : null;

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

    private static string Esc(string s) => s.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal);

    private static string FontXml(Dictionary<string, string> d, bool inDxf)
    {
        var sb = new StringBuilder("<font>");
        if (IsTrue(Get(d, "Font.Bold")))
        {
            sb.Append("<b/>");
        }

        if (IsTrue(Get(d, "Font.Italic")))
        {
            sb.Append("<i/>");
        }

        if (IsTrue(Get(d, "Font.StrikeThrough")))
        {
            sb.Append("<strike/>");
        }

        switch (Get(d, "Font.Underline"))
        {
            case "Single":
                sb.Append("<u/>");
                break;
            case "Double":
                sb.Append("<u val=\"double\"/>");
                break;
            case "SingleAccounting":
                sb.Append("<u val=\"singleAccounting\"/>");
                break;
            case "DoubleAccounting":
                sb.Append("<u val=\"doubleAccounting\"/>");
                break;
        }

        switch (Get(d, "Font.VerticalAlign"))
        {
            case "Superscript":
                sb.Append("<vertAlign val=\"superscript\"/>");
                break;
            case "Subscript":
                sb.Append("<vertAlign val=\"subscript\"/>");
                break;
        }

        if (!inDxf)
        {
            double size = double.TryParse(Get(d, "Font.Size"), NumberStyles.Float, CultureInfo.InvariantCulture, out double s) && s is > 0 and <= 409 ? s : 10;
            sb.Append("<sz val=\"").Append(Inv(size)).Append("\"/>");
        }

        if (Rgb(Get(d, "Font.Color")) is { } color)
        {
            sb.Append("<color rgb=\"").Append(color).Append("\"/>");
        }

        if (!inDxf)
        {
            sb.Append("<name val=\"").Append(Esc(Get(d, "Font.FontName") ?? "Arial")).Append("\"/>");
            if (Get(d, "Font.Family") is { } family)
            {
                int code = family switch { "Roman" => 1, "Swiss" => 2, "Modern" => 3, "Script" => 4, "Decorative" => 5, _ => 0 };
                if (code > 0)
                {
                    sb.Append("<family val=\"").Append(code).Append("\"/>");
                }
            }

            if (int.TryParse(Get(d, "Font.CharSet"), NumberStyles.None, CultureInfo.InvariantCulture, out int charset) && charset is >= 0 and <= 255)
            {
                sb.Append("<charset val=\"").Append(charset).Append("\"/>");
            }
        }

        return sb.Append("</font>").ToString();
    }

    private static string? PatternName(string? pattern) => pattern switch
    {
        null or "None" => null,
        "Solid" => "solid",
        "Gray75" => "darkGray",
        "Gray50" => "mediumGray",
        "Gray25" => "lightGray",
        "Gray125" => "gray125",
        "Gray0625" => "gray0625",
        "HorzStripe" => "darkHorizontal",
        "VertStripe" => "darkVertical",
        "ReverseDiagStripe" => "darkDown",
        "DiagStripe" => "darkUp",
        "DiagCross" => "darkGrid",
        "ThickDiagCross" => "darkTrellis",
        "ThinHorzStripe" => "lightHorizontal",
        "ThinVertStripe" => "lightVertical",
        "ThinReverseDiagStripe" => "lightDown",
        "ThinDiagStripe" => "lightUp",
        "ThinHorzCross" => "lightGrid",
        "ThinDiagCross" => "lightTrellis",
        _ => null,
    };

    private static string? FillXml(Dictionary<string, string> d)
    {
        string? pattern = PatternName(Get(d, "Interior.Pattern"));
        string? color = Rgb(Get(d, "Interior.Color"));
        if (pattern is null && color is not null)
        {
            pattern = "solid"; // a colour with no pattern is a solid fill
        }

        if (pattern is null)
        {
            return null;
        }

        string? patternColor = Rgb(Get(d, "Interior.PatternColor"));
        var sb = new StringBuilder("<fill><patternFill patternType=\"").Append(pattern).Append("\">");
        if (pattern == "solid")
        {
            if (color is not null)
            {
                sb.Append("<fgColor rgb=\"").Append(color).Append("\"/>");
            }
        }
        else
        {
            if (patternColor is not null)
            {
                sb.Append("<fgColor rgb=\"").Append(patternColor).Append("\"/>");
            }

            if (color is not null)
            {
                sb.Append("<bgColor rgb=\"").Append(color).Append("\"/>");
            }
        }

        return sb.Append("</patternFill></fill>").ToString();
    }

    // ECMA-376 Part 1 §18.18.3 (ST_BorderStyle); the 2003 weight (0 hairline, 1 thin, 2 medium, 3 thick) picks the variant.
    private static string? BorderStyleName(string? line, string? weight)
    {
        int w = int.TryParse(weight, NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : 1;
        return line switch
        {
            null or "None" => null,
            "Continuous" => w switch { 0 => "hair", 1 => "thin", 2 => "medium", _ => "thick" },
            "Dash" => w >= 2 ? "mediumDashed" : "dashed",
            "Dot" => w == 0 ? "hair" : "dotted",
            "DashDot" => w >= 2 ? "mediumDashDot" : "dashDot",
            "DashDotDot" => w >= 2 ? "mediumDashDotDot" : "dashDotDot",
            "SlantDashDot" => "slantDashDot",
            "Double" => "double",
            _ => null,
        };
    }

    private static string? BorderXml(Dictionary<string, string> d)
    {
        var sb = new StringBuilder();
        bool any = false;
        foreach ((string position, string element) in new[] { ("Left", "left"), ("Right", "right"), ("Top", "top"), ("Bottom", "bottom") })
        {
            string? style = BorderStyleName(Get(d, "Border." + position + ".LineStyle"), Get(d, "Border." + position + ".Weight"));
            if (style is null)
            {
                sb.Append('<').Append(element).Append("/>");
                continue;
            }

            any = true;
            AppendBorderSide(sb, element, style, Rgb(Get(d, "Border." + position + ".Color")));
        }

        // One diagonal line style serves both directions in XLSX; the down diagonal's style wins when both are set.
        string? down = BorderStyleName(Get(d, "Border.DiagonalLeft.LineStyle"), Get(d, "Border.DiagonalLeft.Weight"));
        string? up = BorderStyleName(Get(d, "Border.DiagonalRight.LineStyle"), Get(d, "Border.DiagonalRight.Weight"));
        if (down is not null || up is not null)
        {
            any = true;
            string position = down is not null ? "DiagonalLeft" : "DiagonalRight";
            AppendBorderSide(sb, "diagonal", down ?? up!, Rgb(Get(d, "Border." + position + ".Color")));
        }
        else
        {
            sb.Append("<diagonal/>");
        }

        if (!any)
        {
            return null;
        }

        string flags = (down is not null ? " diagonalDown=\"1\"" : string.Empty) + (up is not null ? " diagonalUp=\"1\"" : string.Empty);
        return "<border" + flags + ">" + sb + "</border>";
    }

    private static void AppendBorderSide(StringBuilder sb, string element, string style, string? color)
    {
        sb.Append('<').Append(element).Append(" style=\"").Append(style).Append('"');
        sb.Append(color is null ? "/>" : "><color rgb=\"" + color + "\"/></" + element + ">");
    }

    private static readonly Dictionary<string, string> NamedFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        ["General Number"] = "General",
        ["General Date"] = "m/d/yyyy h:mm",
        ["Long Date"] = "dddd, mmmm dd, yyyy",
        ["Medium Date"] = "d-mmm-yy",
        ["Short Date"] = "m/d/yyyy",
        ["Long Time"] = "h:mm:ss AM/PM",
        ["Medium Time"] = "h:mm AM/PM",
        ["Short Time"] = "h:mm",
        ["Currency"] = "\"$\"#,##0.00",
        ["Euro Currency"] = "[$\u20ac-2] #,##0.00",
        ["Fixed"] = "0.00",
        ["Standard"] = "#,##0.00",
        ["Percent"] = "0.00%",
        ["Scientific"] = "0.00E+00",
        ["Yes/No"] = "\"Yes\";\"Yes\";\"No\"",
        ["True/False"] = "\"True\";\"True\";\"False\"",
        ["On/Off"] = "\"On\";\"On\";\"Off\"",
    };

    private static readonly Dictionary<string, int> BuiltInFormatIds = new(StringComparer.Ordinal)
    {
        ["General"] = 0, ["0"] = 1, ["0.00"] = 2, ["#,##0"] = 3, ["#,##0.00"] = 4, ["0%"] = 9, ["0.00%"] = 10, ["0.00E+00"] = 11,
    };

    private int NumFmtId(string? format)
    {
        if (string.IsNullOrEmpty(format))
        {
            return 0;
        }

        if (NamedFormats.TryGetValue(format, out string? code))
        {
            format = code;
        }

        if (BuiltInFormatIds.TryGetValue(format, out int builtin))
        {
            return builtin;
        }

        if (!_numFmtIndex.TryGetValue(format, out int id))
        {
            id = 164 + _numFmts.Count;
            _numFmts.Add(format);
            _numFmtIndex[format] = id;
        }

        return id;
    }

    private ReadOnlyMemory<byte> BuildStyles()
    {
        var xfXml = new StringBuilder();
        foreach (Dictionary<string, string> d in _xfs)
        {
            int numFmt = NumFmtId(Get(d, "NumberFormat.Format"));
            int font = Intern(_fontIndex, _fonts, FontXml(d, false));
            string? fillXml = FillXml(d);
            int fill = fillXml is null ? 0 : Intern(_fillIndex, _fills, fillXml) + 2; // 0 and 1 are the two fills every file starts with
            string? borderXml = BorderXml(d);
            int border = borderXml is null ? 0 : Intern(_borderIndex, _borders, borderXml) + 1;
            xfXml.Append("<xf numFmtId=\"").Append(numFmt).Append("\" fontId=\"").Append(font).Append("\" fillId=\"").Append(fill)
                .Append("\" borderId=\"").Append(border).Append("\" xfId=\"0\" applyNumberFormat=\"1\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\"");
            string alignment = AlignmentXml(d);
            bool locked = Get(d, "Protection.Protected") != "0";
            bool hidden = IsTrue(Get(d, "Protection.HideFormula"));
            if (alignment.Length > 0)
            {
                xfXml.Append(" applyAlignment=\"1\"");
            }

            if (!locked || hidden)
            {
                xfXml.Append(" applyProtection=\"1\"");
            }

            if (alignment.Length == 0 && locked && !hidden)
            {
                xfXml.Append("/>");
                continue;
            }

            xfXml.Append('>').Append(alignment);
            if (!locked || hidden)
            {
                xfXml.Append("<protection locked=\"").Append(locked ? '1' : '0').Append("\" hidden=\"").Append(hidden ? '1' : '0').Append("\"/>");
            }

            xfXml.Append("</xf>");
        }

        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<styleSheet xmlns=\"" + MainNs + "\">");
        if (_numFmts.Count > 0)
        {
            sb.Append("<numFmts count=\"").Append(_numFmts.Count).Append("\">");
            for (int i = 0; i < _numFmts.Count; i++)
            {
                sb.Append("<numFmt numFmtId=\"").Append(164 + i).Append("\" formatCode=\"").Append(Esc(_numFmts[i])).Append("\"/>");
            }

            sb.Append("</numFmts>");
        }

        sb.Append("<fonts count=\"").Append(_fonts.Count).Append("\">").AppendJoin(string.Empty, _fonts).Append("</fonts>");
        sb.Append("<fills count=\"").Append(_fills.Count + 2).Append("\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill>")
            .AppendJoin(string.Empty, _fills).Append("</fills>");
        sb.Append("<borders count=\"").Append(_borders.Count + 1).Append("\"><border><left/><right/><top/><bottom/><diagonal/></border>")
            .AppendJoin(string.Empty, _borders).Append("</borders>");
        sb.Append("<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>");
        sb.Append("<cellXfs count=\"").Append(_xfs.Count).Append("\">").Append(xfXml).Append("</cellXfs>");
        sb.Append("<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>");
        if (_dxfs.Count > 0)
        {
            sb.Append("<dxfs count=\"").Append(_dxfs.Count).Append("\">").AppendJoin(string.Empty, _dxfs).Append("</dxfs>");
        }

        sb.Append("</styleSheet>");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string AlignmentXml(Dictionary<string, string> d)
    {
        var sb = new StringBuilder();
        string? horizontal = Get(d, "Alignment.Horizontal") switch
        {
            "Left" => "left",
            "Center" => "center",
            "Right" => "right",
            "Fill" => "fill",
            "Justify" => "justify",
            "CenterAcrossSelection" => "centerContinuous",
            "Distributed" or "JustifyDistributed" => "distributed",
            _ => null,
        };
        string? vertical = Get(d, "Alignment.Vertical") switch
        {
            "Top" => "top",
            "Center" => "center",
            "Justify" => "justify",
            "Distributed" or "JustifyDistributed" => "distributed",
            _ => null,
        };
        if (horizontal is not null)
        {
            sb.Append(" horizontal=\"").Append(horizontal).Append('"');
        }

        if (vertical is not null)
        {
            sb.Append(" vertical=\"").Append(vertical).Append('"');
        }

        if (int.TryParse(Get(d, "Alignment.Rotate"), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int rotate) && rotate is >= -90 and <= 90 && rotate != 0)
        {
            sb.Append(" textRotation=\"").Append(rotate > 0 ? rotate : 90 - rotate).Append('"'); // negative angles are stored as 90 plus their size
        }
        else if (IsTrue(Get(d, "Alignment.VerticalText")))
        {
            sb.Append(" textRotation=\"255\"");
        }

        if (IsTrue(Get(d, "Alignment.WrapText")))
        {
            sb.Append(" wrapText=\"1\"");
        }

        if (int.TryParse(Get(d, "Alignment.Indent"), NumberStyles.None, CultureInfo.InvariantCulture, out int indent) && indent > 0)
        {
            sb.Append(" indent=\"").Append(indent).Append('"');
        }

        if (IsTrue(Get(d, "Alignment.ShrinkToFit")))
        {
            sb.Append(" shrinkToFit=\"1\"");
        }

        switch (Get(d, "Alignment.ReadingOrder"))
        {
            case "RightToLeft":
                sb.Append(" readingOrder=\"2\"");
                break;
            case "LeftToRight":
                sb.Append(" readingOrder=\"1\"");
                break;
        }

        return sb.Length == 0 ? string.Empty : "<alignment" + sb + "/>";
    }

    // A conditional-format style ("font-weight:700;color:#FF0000;background:yellow") as a differential format, interned.
    private int DxfIndex(string? css)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        string? fill = null;
        foreach (string decl in (css ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int colon = decl.IndexOf(':', StringComparison.Ordinal);
            if (colon < 0)
            {
                continue;
            }

            string key = decl[..colon].Trim().ToLowerInvariant();
            string value = decl[(colon + 1)..].Trim();
            switch (key)
            {
                case "font-weight":
                    if (value is "bold" or "700" or "800" or "900")
                    {
                        d["Font.Bold"] = "1";
                    }

                    break;
                case "font-style":
                    if (value is "italic")
                    {
                        d["Font.Italic"] = "1";
                    }

                    break;
                case "text-decoration":
                    if (value.Contains("underline", StringComparison.Ordinal))
                    {
                        d["Font.Underline"] = "Single";
                    }

                    if (value.Contains("line-through", StringComparison.Ordinal))
                    {
                        d["Font.StrikeThrough"] = "1";
                    }

                    break;
                case "color":
                    d["Font.Color"] = value.Length == 7 ? value.ToUpperInvariant() : value;
                    break;
                case "background" or "background-color":
                    fill = Rgb(value.Length == 7 ? value.ToUpperInvariant() : value);
                    break;
            }
        }

        string fontXml = FontXml(d, true);
        var sb = new StringBuilder("<dxf>");
        if (fontXml != "<font></font>")
        {
            sb.Append(fontXml);
        }

        if (fill is not null)
        {
            sb.Append("<fill><patternFill patternType=\"solid\"><fgColor rgb=\"").Append(fill).Append("\"/><bgColor rgb=\"").Append(fill).Append("\"/></patternFill></fill>");
        }

        sb.Append("</dxf>");
        return Intern(_dxfIndex, _dxfs, sb.ToString());
    }
}
