using System.Linq;
using System.Globalization;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Xlsb;

/// <summary>A colour as the binary format stores it (<c>BrtColor</c>, [MS-XLSB] §2.4.337).</summary>
internal readonly record struct XlsbColor(int Type, int Index, short Tint, uint Argb)
{
    public bool IsDefault => Type == 0 && Tint == 0;

    public static XlsbColor Read(ref XlsbRecordFields f)
    {
        int b0 = f.U8();
        int index = f.U8();
        short tint = (short)f.I16();
        int r = f.U8();
        int g = f.U8();
        int b = f.U8();
        int a = f.U8();
        int type = (b0 >> 1) & 0x7F;
        return new XlsbColor(type, index, tint, ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | (uint)b);
    }

    public void Write(XlsbRecordData d)
    {
        bool validRgb = Type == 2;
        d.U8((Type << 1) | (validRgb ? 1 : 0));
        d.U8(Index);
        d.U16((ushort)Tint);
        d.U8((int)((Argb >> 16) & 0xFF));
        d.U8((int)((Argb >> 8) & 0xFF));
        d.U8((int)(Argb & 0xFF));
        d.U8(Type == 2 ? (int)(Argb >> 24) : 0xFF);
    }

    /// <summary>Writes the attributes of an XLSX colour element.</summary>
    public void Attributes(XmlSink x)
    {
        switch (Type)
        {
            case 1: x.Attr("indexed", Index); break;
            case 2: x.Attr("rgb", Argb.ToString("X8", CultureInfo.InvariantCulture)); break;
            case 3: x.Attr("theme", Index); break;
            default: x.Attr("auto", 1); break;
        }

        if (Tint != 0)
        {
            x.Attr("tint", Tint / 32767.0);
        }
    }

    public static XlsbColor FromXml(MarkupNode n)
    {
        short tint = n.GetAttribute("tint") is { } t && double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
            ? (short)Math.Clamp(Math.Round(d * 32767.0), -32767, 32767) : (short)0;
        if (n.GetAttribute("rgb") is { } rgb && uint.TryParse(rgb, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint argb))
        {
            return new XlsbColor(2, 0, tint, rgb.Length <= 6 ? 0xFF000000 | argb : argb);
        }

        if (n.GetAttribute("theme") is { } th && int.TryParse(th, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ti))
        {
            return new XlsbColor(3, ti, tint, 0);
        }

        if (n.GetAttribute("indexed") is { } ix && int.TryParse(ix, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ii))
        {
            return new XlsbColor(1, ii, tint, 0);
        }

        return new XlsbColor(0, 0, tint, 0);
    }
}

/// <summary>The fonts of the style sheet, kept in order because rich-text runs and cell formats index them.</summary>
internal sealed class XlsbFontTable
{
    public List<XlsbFont> Fonts { get; } = [];

    /// <summary>Gets or sets a value indicating whether fonts were added after the style sheet was read, so the style sheet must be written again.</summary>
    public bool Grew { get; set; }

    public int Find(XlsbFont f)
    {
        for (int i = 0; i < Fonts.Count; i++)
        {
            if (Fonts[i].SameAs(f))
            {
                return i;
            }
        }

        return -1;
    }
}

internal sealed class XlsbFont
{
    public int Height = 220;
    public bool Bold;
    public bool Italic;
    public bool Strike;
    public bool Outline;
    public bool Shadow;
    public bool Condense;
    public bool Extend;
    public int Sss;
    public int Uls;
    public int Family;
    public int Charset = 1;
    public XlsbColor Color = new(0, 0, 0, 0);
    public bool HasColor;
    public int Scheme;
    public string Name = string.Empty;

    public bool SameAs(XlsbFont o) =>
        Height == o.Height && Bold == o.Bold && Italic == o.Italic && Strike == o.Strike && Outline == o.Outline && Shadow == o.Shadow && Condense == o.Condense
        && Extend == o.Extend && Sss == o.Sss && Uls == o.Uls && Family == o.Family && Charset == o.Charset && Color == o.Color && HasColor == o.HasColor
        && Scheme == o.Scheme && string.Equals(Name, o.Name, StringComparison.Ordinal);

    public static XlsbFont Read(ReadOnlySpan<byte> data)
    {
        var f = new XlsbRecordFields(data);
        var font = new XlsbFont { Height = f.U16() };
        int flags = f.U16();
        int bls = f.U16();
        font.Sss = f.U16();
        font.Uls = f.U8();
        font.Family = f.U8();
        font.Charset = f.U8();
        f.U8();
        font.Color = XlsbColor.Read(ref f);
        font.HasColor = true;
        font.Scheme = f.U8();
        font.Name = f.WideString();
        font.Italic = (flags & 0x02) != 0;
        font.Strike = (flags & 0x08) != 0;
        font.Outline = (flags & 0x10) != 0;
        font.Shadow = (flags & 0x20) != 0;
        font.Condense = (flags & 0x40) != 0;
        font.Extend = (flags & 0x80) != 0;
        font.Bold = bls >= 0x2BC;
        return font;
    }

    public void Write(XlsbRecordData d)
    {
        d.U16(Height);
        d.U16((Italic ? 0x02 : 0) | (Strike ? 0x08 : 0) | (Outline ? 0x10 : 0) | (Shadow ? 0x20 : 0) | (Condense ? 0x40 : 0) | (Extend ? 0x80 : 0));
        d.U16(Bold ? 0x2BC : 0x190);
        d.U16(Sss);
        d.U8(Uls);
        d.U8(Family);
        d.U8(Charset);
        d.U8(0);
        (HasColor ? Color : new XlsbColor(1, 64, 0, 0)).Write(d);
        d.U8(Scheme);
        d.WideString(Name);
    }

    /// <summary>Writes the font's properties; <paramref name="run"/> selects the run-property element names (<c>rFont</c>) instead of the font ones (<c>name</c>).</summary>
    public void Xml(XmlSink x, bool run)
    {
        if (run)
        {
            x.Raw("<rFont").Attr("val", Name).Raw("/>");
            if (Charset != 1)
            {
                x.Raw("<charset").Attr("val", Charset).Raw("/>");
            }

            if (Family != 0)
            {
                x.Raw("<family").Attr("val", Family).Raw("/>");
            }
        }

        if (Bold)
        {
            x.Raw("<b/>");
        }

        if (Italic)
        {
            x.Raw("<i/>");
        }

        if (Strike)
        {
            x.Raw("<strike/>");
        }

        if (Outline)
        {
            x.Raw("<outline/>");
        }

        if (Shadow)
        {
            x.Raw("<shadow/>");
        }

        if (Condense)
        {
            x.Raw("<condense/>");
        }

        if (Extend)
        {
            x.Raw("<extend/>");
        }

        if (run)
        {
            if (HasColor)
            {
                x.Raw("<color");
                Color.Attributes(x);
                x.Raw("/>");
            }

            x.Raw("<sz").Attr("val", Height / 20.0).Raw("/>");
            UnderlineAndScript(x);
            if (Scheme != 0)
            {
                x.Raw("<scheme").Attr("val", Scheme == 1 ? "major" : "minor").Raw("/>");
            }

            return;
        }

        UnderlineAndScript(x);
        x.Raw("<sz").Attr("val", Height / 20.0).Raw("/>");
        if (HasColor)
        {
            x.Raw("<color");
            Color.Attributes(x);
            x.Raw("/>");
        }

        x.Raw("<name").Attr("val", Name).Raw("/>");
        if (Family != 0)
        {
            x.Raw("<family").Attr("val", Family).Raw("/>");
        }

        if (Charset != 1)
        {
            x.Raw("<charset").Attr("val", Charset).Raw("/>");
        }

        if (Scheme != 0)
        {
            x.Raw("<scheme").Attr("val", Scheme == 1 ? "major" : "minor").Raw("/>");
        }
    }

    private void UnderlineAndScript(XmlSink x)
    {
        switch (Uls)
        {
            case 0: break;
            case 1: x.Raw("<u/>"); break;
            case 2: x.Raw("<u val=\"double\"/>"); break;
            case 0x21: x.Raw("<u val=\"singleAccounting\"/>"); break;
            case 0x22: x.Raw("<u val=\"doubleAccounting\"/>"); break;
        }

        if (Sss != 0)
        {
            x.Raw("<vertAlign").Attr("val", Sss == 1 ? "superscript" : "subscript").Raw("/>");
        }
    }

    /// <summary>Reads a <c>font</c> or <c>rPr</c> element.</summary>
    public static XlsbFont FromXml(MarkupNode n)
    {
        var f = new XlsbFont { Charset = 1 };
        foreach (MarkupNode c in n.ChildElements)
        {
            switch (c.LocalNameString)
            {
                case "b": f.Bold = c.Switch(); break;
                case "i": f.Italic = c.Switch(); break;
                case "strike": f.Strike = c.Switch(); break;
                case "outline": f.Outline = c.Switch(); break;
                case "shadow": f.Shadow = c.Switch(); break;
                case "condense": f.Condense = c.Switch(); break;
                case "extend": f.Extend = c.Switch(); break;
                case "sz": f.Height = (int)Math.Round(c.Dbl("val", 11) * 20); break;
                case "color": f.Color = XlsbColor.FromXml(c); f.HasColor = true; break;
                case "name":
                case "rFont": f.Name = c.Str("val"); break;
                case "family": f.Family = c.Int("val"); break;
                case "charset": f.Charset = c.Int("val", 1); break;
                case "scheme": f.Scheme = c.Str("val") == "major" ? 1 : c.Str("val") == "minor" ? 2 : 0; break;
                case "vertAlign": f.Sss = c.Str("val") == "superscript" ? 1 : c.Str("val") == "subscript" ? 2 : 0; break;
                case "u":
                    f.Uls = c.Str("val", "single") switch { "double" => 2, "singleAccounting" => 0x21, "doubleAccounting" => 0x22, "none" => 0, _ => 1 };
                    break;
            }
        }

        return f;
    }
}

/// <summary>Translates the styles part between <c>styles.bin</c> and <c>styles.xml</c> ([MS-XLSB] §2.1.7.50).</summary>
internal static class XlsbStylesPart
{
    private static readonly string[] PatternNames =
    [
        "none", "solid", "mediumGray", "darkGray", "lightGray", "darkHorizontal", "darkVertical", "darkDown", "darkUp", "darkGrid", "darkTrellis",
        "lightHorizontal", "lightVertical", "lightDown", "lightUp", "lightGrid", "lightTrellis", "gray125", "gray0625",
    ];

    private static readonly string[] BorderNames =
        ["none", "thin", "medium", "dashed", "dotted", "thick", "double", "hair", "mediumDashed", "dashDot", "mediumDashDot", "dashDotDot", "mediumDashDotDot", "slantDashDot"];

    private static readonly string[] HorizontalNames = ["general", "left", "center", "right", "fill", "justify", "centerContinuous", "distributed"];

    private static readonly string[] VerticalNames = ["top", "center", "bottom", "justify", "distributed"];

    /// <summary>Reads <c>styles.bin</c> into XLSX markup and the font table that rich-text runs refer to.</summary>
    public static byte[] ToXml(ReadOnlyMemory<byte> bin, OpenContext context, string partName, XlsbFontTable fonts)
    {
        var xml = new XmlSink(8192);
        var fmts = new List<(int Id, string Code)>();
        var fills = new XmlSink();
        var borders = new XmlSink();
        var styleXfs = new XmlSink();
        var cellXfs = new XmlSink();
        var cellStyles = new XmlSink();
        var dxfs = new XmlSink();
        var tableStyles = new XmlSink();
        var colors = new XmlSink();
        int fillCount = 0;
        int borderCount = 0;
        int styleXfCount = 0;
        int cellXfCount = 0;
        int cellStyleCount = 0;
        int dxfCount = 0;
        string defaultTable = "TableStyleMedium2";
        string defaultPivot = "PivotStyleLight16";
        int section = 0; // 0 none, 1 cellStyleXfs, 2 cellXfs
        bool inColors = false;
        var indexed = new List<uint>();
        var mru = new List<XlsbColor>();
        var tableRaw = new XmlSink();
        var extra = new XmlSink();
        var r = new XlsbRecordReader(bin, context, partName);
        while (r.Read())
        {
            ReadOnlySpan<byte> d = r.Data;
            switch (r.Type)
            {
                case XlsbRecordType.Fmt:
                {
                    var f = new XlsbRecordFields(d);
                    int id = f.U16();
                    fmts.Add((id, f.WideString()));
                    break;
                }

                case XlsbRecordType.Font:
                    fonts.Fonts.Add(XlsbFont.Read(d));
                    break;
                case XlsbRecordType.Fill:
                    FillXml(fills, d);
                    fillCount++;
                    break;
                case XlsbRecordType.Border:
                    BorderXml(borders, d);
                    borderCount++;
                    break;
                case XlsbRecordType.BeginCellStyleXFs:
                    section = 1;
                    break;
                case XlsbRecordType.EndCellStyleXFs:
                case XlsbRecordType.EndCellXFs:
                    section = 0;
                    break;
                case XlsbRecordType.BeginCellXFs:
                    section = 2;
                    break;
                case XlsbRecordType.XF:
                    if (section == 1)
                    {
                        XfXml(styleXfs, d, isStyle: true);
                        styleXfCount++;
                    }
                    else
                    {
                        XfXml(cellXfs, d, isStyle: false);
                        cellXfCount++;
                    }

                    break;
                case XlsbRecordType.Style:
                {
                    var f = new XlsbRecordFields(d);
                    int xf = (int)f.U32();
                    int flags = f.U16();
                    int builtin = f.U8();
                    int level = f.U8();
                    string name = f.WideString();
                    cellStyles.Raw("<cellStyle").Attr("name", name).Attr("xfId", xf);
                    if ((flags & 1) != 0)
                    {
                        cellStyles.Attr("builtinId", builtin);
                        if (builtin is 1 or 2)
                        {
                            cellStyles.Attr("iLevel", level);
                        }
                    }

                    if ((flags & 2) != 0)
                    {
                        cellStyles.Attr("hidden", 1);
                    }

                    if ((flags & 4) != 0)
                    {
                        cellStyles.Attr("customBuiltin", 1);
                    }

                    cellStyles.Raw("/>");
                    cellStyleCount++;
                    break;
                }

                case XlsbRecordType.DXF:
                    DxfXml(dxfs, d);
                    dxfCount++;
                    break;
                case XlsbRecordType.BeginTableStyles:
                {
                    var f = new XlsbRecordFields(d);
                    f.U32();
                    defaultTable = f.WideString();
                    defaultPivot = f.WideString();
                    break;
                }

                case XlsbRecordType.BeginTableStyle:
                case XlsbRecordType.TableStyleElement:
                case XlsbRecordType.EndTableStyle:
                    XlsbMarkup.Raw(tableRaw, r.Type, d);
                    break;
                case XlsbRecordType.BeginColorPalette:
                    inColors = true;
                    break;
                case XlsbRecordType.IndexedColor:
                {
                    var f = new XlsbRecordFields(d);
                    int rr = f.U8();
                    int gg = f.U8();
                    int bb = f.U8();
                    int aa = f.U8();
                    indexed.Add(((uint)aa << 24) | ((uint)rr << 16) | ((uint)gg << 8) | (uint)bb);
                    break;
                }

                case XlsbRecordType.MRUColor:
                {
                    var f = new XlsbRecordFields(d);
                    mru.Add(XlsbColor.Read(ref f));
                    break;
                }

                case XlsbRecordType.BeginStyleSheet:
                case XlsbRecordType.EndStyleSheet:
                case XlsbRecordType.BeginFmts:
                case XlsbRecordType.EndFmts:
                case XlsbRecordType.BeginFonts:
                case XlsbRecordType.EndFonts:
                case XlsbRecordType.BeginFills:
                case XlsbRecordType.EndFills:
                case XlsbRecordType.BeginBorders:
                case XlsbRecordType.EndBorders:
                case XlsbRecordType.BeginStyles:
                case XlsbRecordType.EndStyles:
                case XlsbRecordType.BeginDXFs:
                case XlsbRecordType.EndDXFs:
                case XlsbRecordType.EndTableStyles:
                case XlsbRecordType.EndColorPalette:
                case XlsbRecordType.BeginIndexedColors:
                case XlsbRecordType.EndIndexedColors:
                case XlsbRecordType.BeginMRUColors:
                case XlsbRecordType.EndMRUColors:
                case XlsbRecordType.ACBegin:
                case XlsbRecordType.ACEnd:
                    break;
                default:
                    XlsbMarkup.Raw(extra, r.Type, d);
                    break;
            }
        }

        _ = inColors;
        xml.Raw("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<styleSheet xmlns=\"").Raw(XlsbMarkup.MainNamespace).Raw("\" xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" xmlns:xb=\"")
            .Raw(XlsbMarkup.Namespace).Raw("\" mc:Ignorable=\"xb\">");
        if (fmts.Count > 0)
        {
            xml.Raw("<numFmts").Attr("count", fmts.Count).Raw(">");
            foreach ((int id, string code) in fmts)
            {
                xml.Raw("<numFmt").Attr("numFmtId", id).Attr("formatCode", code).Raw("/>");
            }

            xml.Raw("</numFmts>");
        }

        xml.Raw("<fonts").Attr("count", fonts.Fonts.Count).Raw(">");
        foreach (XlsbFont f in fonts.Fonts)
        {
            xml.Raw("<font>");
            f.Xml(xml, run: false);
            xml.Raw("</font>");
        }

        xml.Raw("</fonts><fills").Attr("count", fillCount).Raw(">").Raw(fills.Memory.Span).Raw("</fills><borders").Attr("count", borderCount).Raw(">")
            .Raw(borders.Memory.Span).Raw("</borders><cellStyleXfs").Attr("count", styleXfCount).Raw(">").Raw(styleXfs.Memory.Span).Raw("</cellStyleXfs><cellXfs")
            .Attr("count", cellXfCount).Raw(">").Raw(cellXfs.Memory.Span).Raw("</cellXfs>");
        if (cellStyleCount > 0)
        {
            xml.Raw("<cellStyles").Attr("count", cellStyleCount).Raw(">").Raw(cellStyles.Memory.Span).Raw("</cellStyles>");
        }

        xml.Raw("<dxfs").Attr("count", dxfCount).Raw(">").Raw(dxfs.Memory.Span).Raw("</dxfs>");
        xml.Raw("<tableStyles count=\"0\"").Attr("defaultTableStyle", defaultTable).Attr("defaultPivotStyle", defaultPivot);
        if (tableRaw.Length > 0)
        {
            xml.Raw(">").Raw(tableRaw.Memory.Span).Raw("</tableStyles>");
        }
        else
        {
            xml.Raw("/>");
        }

        if (indexed.Count > 0 || mru.Count > 0)
        {
            xml.Raw("<colors>");
            if (indexed.Count > 0)
            {
                xml.Raw("<indexedColors>");
                foreach (uint c in indexed)
                {
                    xml.Raw("<rgbColor").Attr("rgb", c.ToString("X8", CultureInfo.InvariantCulture)).Raw("/>");
                }

                xml.Raw("</indexedColors>");
            }

            if (mru.Count > 0)
            {
                xml.Raw("<mruColors>");
                foreach (XlsbColor c in mru)
                {
                    xml.Raw("<color");
                    c.Attributes(xml);
                    xml.Raw("/>");
                }

                xml.Raw("</mruColors>");
            }

            xml.Raw("</colors>");
        }

        xml.Raw(extra.Memory.Span);
        xml.Raw("</styleSheet>");
        return xml.ToArray();
    }

    private static void ColorElement(XmlSink x, string name, XlsbColor c)
    {
        x.Raw("<").Raw(name);
        c.Attributes(x);
        x.Raw("/>");
    }

    private static void FillXml(XmlSink x, ReadOnlySpan<byte> d)
    {
        var f = new XlsbRecordFields(d);
        int fls = (int)f.U32();
        XlsbColor fore = XlsbColor.Read(ref f);
        XlsbColor back = XlsbColor.Read(ref f);
        int gradientType = (int)f.U32();
        double degree = f.Number();
        double left = f.Number();
        double right = f.Number();
        double top = f.Number();
        double bottom = f.Number();
        int stops = (int)f.U32();
        x.Raw("<fill>");
        if (fls == 0x28)
        {
            x.Raw("<gradientFill");
            if (gradientType == 1)
            {
                x.Attr("type", "path");
            }

            if (degree != 0)
            {
                x.Attr("degree", degree);
            }

            if (left != 0)
            {
                x.Attr("left", left);
            }

            if (right != 0)
            {
                x.Attr("right", right);
            }

            if (top != 0)
            {
                x.Attr("top", top);
            }

            if (bottom != 0)
            {
                x.Attr("bottom", bottom);
            }

            x.Raw(">");
            for (int i = 0; i < stops && !f.Overrun; i++)
            {
                XlsbColor c = XlsbColor.Read(ref f);
                double pos = f.Number();
                x.Raw("<stop").Attr("position", pos).Raw(">");
                ColorElement(x, "color", c);
                x.Raw("</stop>");
            }

            x.Raw("</gradientFill>");
        }
        else
        {
            x.Raw("<patternFill").Attr("patternType", fls >= 0 && fls < PatternNames.Length ? PatternNames[fls] : "none");
            if (fls == 0)
            {
                x.Raw("/>");
            }
            else
            {
                x.Raw(">");
                ColorElement(x, "fgColor", fore);
                ColorElement(x, "bgColor", back);
                x.Raw("</patternFill>");
            }
        }

        x.Raw("</fill>");
    }

    private static void BorderXml(XmlSink x, ReadOnlySpan<byte> d)
    {
        var f = new XlsbRecordFields(d);
        int flags = f.U8();
        x.Raw("<border");
        if ((flags & 2) != 0)
        {
            x.Attr("diagonalUp", 1);
        }

        if ((flags & 1) != 0)
        {
            x.Raw(" diagonalDown=\"1\"");
        }

        x.Raw(">");
        (string Name, int Index)[] order = [("top", 0), ("bottom", 1), ("left", 2), ("right", 3), ("diagonal", 4)];
        var sides = new (int Dg, XlsbColor Color)[5];
        for (int i = 0; i < 5; i++)
        {
            int dg = f.U8();
            f.U8();
            sides[i] = (dg, XlsbColor.Read(ref f));
        }

        // CT_Border child order (ECMA-376 Part 1 §18.8.4): left, right, top, bottom, diagonal.
        foreach (string name in new[] { "left", "right", "top", "bottom", "diagonal" })
        {
            int idx = Array.FindIndex(order, o => o.Name == name);
            (int dg, XlsbColor color) = sides[order[idx].Index];
            x.Raw("<").Raw(name);
            if (dg > 0 && dg < BorderNames.Length)
            {
                x.Attr("style", BorderNames[dg]).Raw(">");
                ColorElement(x, "color", color);
                x.Raw("</").Raw(name).Raw(">");
            }
            else
            {
                x.Raw("/>");
            }
        }

        x.Raw("</border>");
    }

    private static void XfXml(XmlSink x, ReadOnlySpan<byte> d, bool isStyle)
    {
        var f = new XlsbRecordFields(d);
        int parent = f.U16();
        int fmt = f.U16();
        int font = f.U16();
        int fill = f.U16();
        int border = f.U16();
        int trot = f.U8();
        int indent = f.U8();
        int align = f.U16();
        int atr = f.U16() & 0x3F;
        x.Raw("<xf").Attr("numFmtId", fmt).Attr("fontId", font).Attr("fillId", fill).Attr("borderId", border);
        if (!isStyle)
        {
            x.Attr("xfId", parent);
        }

        // [MS-XLSB] §2.4.878: in a cell XF a set bit means the property is not inherited from the cell style (applyX = 1); in a style XF a set bit means it is ignored.
        bool Apply(int bit) => isStyle ? (atr & (1 << bit)) == 0 : (atr & (1 << bit)) != 0;
        x.Flag("applyNumberFormat", Apply(0)).Flag("applyFont", Apply(1)).Flag("applyAlignment", Apply(2)).Flag("applyBorder", Apply(3)).Flag("applyFill", Apply(4)).Flag("applyProtection", Apply(5));
        if ((align & 0x8000) != 0)
        {
            x.Attr("quotePrefix", 1);
        }

        if ((align & 0x4000) != 0)
        {
            x.Attr("pivotButton", 1);
        }

        int alc = align & 7;
        int alcv = (align >> 3) & 7;
        bool wrap = (align & 0x40) != 0;
        bool justLast = (align & 0x80) != 0;
        bool shrink = (align & 0x100) != 0;
        int reading = (align >> 10) & 3;
        bool locked = (align & 0x1000) != 0;
        bool hidden = (align & 0x2000) != 0;
        x.Raw(">");
        if (alc != 0 || alcv != 2 || wrap || justLast || shrink || reading != 0 || trot != 0 || indent != 0)
        {
            x.Raw("<alignment");
            if (alc != 0 && alc < HorizontalNames.Length)
            {
                x.Attr("horizontal", HorizontalNames[alc]);
            }

            if (alcv != 2 && alcv < VerticalNames.Length)
            {
                x.Attr("vertical", VerticalNames[alcv]);
            }

            if (trot != 0)
            {
                x.Attr("textRotation", trot);
            }

            if (wrap)
            {
                x.Attr("wrapText", 1);
            }

            if (indent != 0)
            {
                x.Attr("indent", indent);
            }

            if (justLast)
            {
                x.Attr("justifyLastLine", 1);
            }

            if (shrink)
            {
                x.Attr("shrinkToFit", 1);
            }

            if (reading != 0)
            {
                x.Attr("readingOrder", reading);
            }

            x.Raw("/>");
        }

        if (!locked || hidden)
        {
            x.Raw("<protection");
            if (!locked)
            {
                x.Attr("locked", 0);
            }

            if (hidden)
            {
                x.Attr("hidden", 1);
            }

            x.Raw("/>");
        }

        x.Raw("</xf>");
    }

    private static void DxfXml(XmlSink x, ReadOnlySpan<byte> d)
    {
        var f = new XlsbRecordFields(d);
        f.U16();
        f.U16();
        int count = f.U16();
        var font = new XmlSink();
        var fill = new XmlSink();
        var align = new XmlSink();
        var numFmt = new XmlSink();
        var prot = new XmlSink();
        var borderParts = new Dictionary<int, (int Style, XlsbColor Color)>();
        int pattern = -1;
        XlsbColor? fore = null;
        XlsbColor? back = null;
        XlsbColor? textColor = null;
        string? fontName = null;
        int bold = -1;
        int uls = -1;
        int script = -1;
        int italic = -1;
        int strike = -1;
        int outline = -1;
        int shadow = -1;
        int condense = -1;
        int extend = -1;
        int size = -1;
        int h = -1;
        int v = -1;
        int rot = -1;
        int indentAbs = -1;
        int reading = -1;
        int wrap = -1;
        int shrink = -1;
        int justify = -1;
        int locked = -1;
        int hidden = -1;
        int numFmtId = -1;
        string? numFmtCode = null;
        int diagUp = -1;
        int diagDown = -1;
        for (int i = 0; i < count && !f.Overrun; i++)
        {
            int type = f.U16();
            int cb = f.U16();
            if (cb < 4 || cb - 4 > f.Remaining)
            {
                break;
            }

            var p = new XlsbRecordFields(f.Bytes(cb - 4));
            switch (type)
            {
                case 0x00: pattern = p.U16(); break;
                case 0x01: fore = ReadPropColor(ref p); break;
                case 0x02: back = ReadPropColor(ref p); break;
                case 0x05: textColor = ReadPropColor(ref p); break;
                case 0x06: case 0x07: case 0x08: case 0x09: case 0x0A: case 0x0B: case 0x0C:
                {
                    XlsbColor c = ReadPropColor(ref p);
                    borderParts[type] = (p.U16(), c);
                    break;
                }

                case 0x0D: diagUp = p.U8(); break;
                case 0x0E: diagDown = p.U8(); break;
                case 0x0F: h = p.U8(); break;
                case 0x10: v = p.U8(); break;
                case 0x11: rot = p.U8(); break;
                case 0x12: indentAbs = p.U16(); break;
                case 0x13: reading = p.U8(); break;
                case 0x14: wrap = p.U8(); break;
                case 0x15: justify = p.U8(); break;
                case 0x16: shrink = p.U8(); break;
                case 0x18:
                {
                    int cch = p.U16();
                    fontName = System.Text.Encoding.Unicode.GetString(p.Bytes(cch * 2));
                    break;
                }

                case 0x19: bold = p.U16(); break;
                case 0x1A: uls = p.U8(); break;
                case 0x1B: script = p.U8(); break;
                case 0x1C: italic = p.U8(); break;
                case 0x1D: strike = p.U8(); break;
                case 0x1E: outline = p.U8(); break;
                case 0x1F: shadow = p.U8(); break;
                case 0x20: condense = p.U8(); break;
                case 0x21: extend = p.U8(); break;
                case 0x24: size = (int)p.U32(); break;
                case 0x26:
                {
                    // [MS-XLS] §2.5.126: a number format given as text; the id is the next property or assigned by the reader.
                    int cch = p.U16();
                    numFmtCode = System.Text.Encoding.Unicode.GetString(p.Bytes(cch * 2));
                    break;
                }

                case 0x29: numFmtId = p.U16(); break;
                case 0x2B: locked = p.U8(); break;
                case 0x2C: hidden = p.U8(); break;
            }
        }

        x.Raw("<dxf>");
        if (bold >= 0 || italic >= 0 || strike >= 0 || outline >= 0 || shadow >= 0 || condense >= 0 || extend >= 0 || uls >= 0 || script >= 0 || size >= 0 || textColor is not null || fontName is not null)
        {
            font.Clear();
            if (bold >= 0)
            {
                font.Raw(bold >= 0x2BC ? "<b/>" : "<b val=\"0\"/>");
            }

            if (italic >= 0)
            {
                font.Raw(italic != 0 ? "<i/>" : "<i val=\"0\"/>");
            }

            if (strike >= 0)
            {
                font.Raw(strike != 0 ? "<strike/>" : "<strike val=\"0\"/>");
            }

            if (condense >= 0)
            {
                font.Raw(condense != 0 ? "<condense/>" : "<condense val=\"0\"/>");
            }

            if (extend >= 0)
            {
                font.Raw(extend != 0 ? "<extend/>" : "<extend val=\"0\"/>");
            }

            if (outline >= 0)
            {
                font.Raw(outline != 0 ? "<outline/>" : "<outline val=\"0\"/>");
            }

            if (shadow >= 0)
            {
                font.Raw(shadow != 0 ? "<shadow/>" : "<shadow val=\"0\"/>");
            }

            if (uls >= 0)
            {
                font.Raw(uls switch { 0 => "<u val=\"none\"/>", 2 => "<u val=\"double\"/>", 0x21 => "<u val=\"singleAccounting\"/>", 0x22 => "<u val=\"doubleAccounting\"/>", _ => "<u/>" });
            }

            if (script >= 0)
            {
                font.Raw(script switch { 1 => "<vertAlign val=\"superscript\"/>", 2 => "<vertAlign val=\"subscript\"/>", _ => "<vertAlign val=\"baseline\"/>" });
            }

            if (size >= 0)
            {
                font.Raw("<sz").Attr("val", size / 20.0).Raw("/>");
            }

            if (textColor is { } tc)
            {
                ColorElement(font, "color", tc);
            }

            if (fontName is not null)
            {
                font.Raw("<name").Attr("val", fontName).Raw("/>");
            }

            x.Raw("<font>").Raw(font.Memory.Span).Raw("</font>");
        }

        if (numFmtId >= 0 || numFmtCode is not null)
        {
            numFmt.Raw("<numFmt").Attr("numFmtId", numFmtId >= 0 ? numFmtId : 164);
            numFmt.Attr("formatCode", numFmtCode ?? string.Empty).Raw("/>");
            x.Raw(numFmt.Memory.Span);
        }

        if (pattern >= 0 || fore is not null || back is not null)
        {
            fill.Raw("<fill><patternFill");
            if (pattern >= 0 && pattern < PatternNames.Length)
            {
                fill.Attr("patternType", PatternNames[pattern]);
            }

            fill.Raw(">");
            if (fore is { } fg)
            {
                ColorElement(fill, "fgColor", fg);
            }

            if (back is { } bg)
            {
                ColorElement(fill, "bgColor", bg);
            }

            fill.Raw("</patternFill></fill>");
            x.Raw(fill.Memory.Span);
        }

        if (h >= 0 || v >= 0 || rot >= 0 || indentAbs >= 0 || reading >= 0 || wrap >= 0 || shrink >= 0 || justify >= 0)
        {
            align.Raw("<alignment");
            if (h >= 0 && h < HorizontalNames.Length)
            {
                align.Attr("horizontal", HorizontalNames[h]);
            }

            if (v >= 0 && v < VerticalNames.Length)
            {
                align.Attr("vertical", VerticalNames[v]);
            }

            if (rot >= 0)
            {
                align.Attr("textRotation", rot);
            }

            if (wrap >= 0)
            {
                align.Attr("wrapText", wrap);
            }

            if (indentAbs >= 0)
            {
                align.Attr("indent", indentAbs);
            }

            if (justify >= 0)
            {
                align.Attr("justifyLastLine", justify);
            }

            if (shrink >= 0)
            {
                align.Attr("shrinkToFit", shrink);
            }

            if (reading >= 0)
            {
                align.Attr("readingOrder", reading);
            }

            align.Raw("/>");
            x.Raw(align.Memory.Span);
        }

        if (borderParts.Count > 0 || diagUp >= 0 || diagDown >= 0)
        {
            x.Raw("<border");
            if (diagUp > 0)
            {
                x.Attr("diagonalUp", 1);
            }

            if (diagDown > 0)
            {
                x.Attr("diagonalDown", 1);
            }

            x.Raw(">");
            (int Type, string Name)[] sides = [(0x08, "left"), (0x09, "right"), (0x06, "top"), (0x07, "bottom"), (0x0A, "diagonal"), (0x0B, "vertical"), (0x0C, "horizontal")];
            foreach ((int t, string name) in sides)
            {
                if (!borderParts.TryGetValue(t, out (int Style, XlsbColor Color) b))
                {
                    continue;
                }

                x.Raw("<").Raw(name);
                if (b.Style > 0 && b.Style < BorderNames.Length)
                {
                    x.Attr("style", BorderNames[b.Style]);
                }

                x.Raw(">");
                ColorElement(x, "color", b.Color);
                x.Raw("</").Raw(name).Raw(">");
            }

            x.Raw("</border>");
        }

        if (locked >= 0 || hidden >= 0)
        {
            prot.Raw("<protection");
            if (locked >= 0)
            {
                prot.Attr("locked", locked);
            }

            if (hidden >= 0)
            {
                prot.Attr("hidden", hidden);
            }

            prot.Raw("/>");
            x.Raw(prot.Memory.Span);
        }

        x.Raw("</dxf>");
    }

    private static XlsbColor ReadPropColor(ref XlsbRecordFields p)
    {
        // [MS-XLSB] §2.5.161: XFPropColor is a type byte (valid flag, 7-bit type), an index, a tint and the colour as red, green, blue, alpha bytes.
        int b0 = p.U8();
        int index = p.U8();
        short tint = (short)p.I16();
        int r = p.U8();
        int g = p.U8();
        int b = p.U8();
        int a = p.U8();
        return new XlsbColor((b0 >> 1) & 0x7F, index, tint, ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | (uint)b);
    }

    private static void WritePropColor(XlsbRecordData b, XlsbColor c)
    {
        b.U8((c.Type << 1) | (c.Type == 2 ? 1 : 0));
        b.U8(c.Index);
        b.U16((ushort)c.Tint);
        b.U8((int)((c.Argb >> 16) & 0xFF));
        b.U8((int)((c.Argb >> 8) & 0xFF));
        b.U8((int)(c.Argb & 0xFF));
        b.U8(c.Type == 2 ? (int)(c.Argb >> 24) : 0xFF);
    }

    // ------------------------------------------------------------ XML to binary

    /// <summary>Writes <c>styles.xml</c> as the records of <c>styles.bin</c>.</summary>
    public static byte[] FromXml(ReadOnlyMemory<byte> xml, OpenContext context, string partName, XlsbFontTable fonts, ILossSink loss)
    {
        MarkupDocument doc = MarkupDocument.Parse(xml, context, partName);
        MarkupNode root = doc.Root;
        using var ms = new MemoryStream();
        var w = new XlsbRecordWriter(ms);
        var d = new XlsbRecordData();
        w.Write(XlsbRecordType.BeginStyleSheet);
        MarkupNode numFmts = root.Element("numFmts");
        if (!numFmts.IsNull)
        {
            var list = numFmts.Elements("numFmt").ToList();
            if (list.Count > 0)
            {
                d.U32((uint)list.Count);
                w.Write(XlsbRecordType.BeginFmts, d);
                foreach (MarkupNode n in list)
                {
                    d.U16(n.Int("numFmtId"));
                    d.WideString(n.Str("formatCode"));
                    w.Write(XlsbRecordType.Fmt, d);
                }

                w.Write(XlsbRecordType.EndFmts);
            }
        }

        MarkupNode fontsNode = root.Element("fonts");
        if (!fontsNode.IsNull)
        {
            var list = fontsNode.Elements("font").ToList();
            d.U32((uint)list.Count);
            w.Write(XlsbRecordType.BeginFonts, d);
            fonts.Fonts.Clear();
            foreach (MarkupNode n in list)
            {
                XlsbFont font = XlsbFont.FromXml(n);
                fonts.Fonts.Add(font);
                font.Write(d);
                w.Write(XlsbRecordType.Font, d);
            }

            w.Write(XlsbRecordType.EndFonts);
        }

        MarkupNode fillsNode = root.Element("fills");
        if (!fillsNode.IsNull)
        {
            var list = fillsNode.Elements("fill").ToList();
            d.U32((uint)list.Count);
            w.Write(XlsbRecordType.BeginFills, d);
            foreach (MarkupNode n in list)
            {
                WriteFill(d, n);
                w.Write(XlsbRecordType.Fill, d);
            }

            w.Write(XlsbRecordType.EndFills);
        }

        MarkupNode bordersNode = root.Element("borders");
        if (!bordersNode.IsNull)
        {
            var list = bordersNode.Elements("border").ToList();
            d.U32((uint)list.Count);
            w.Write(XlsbRecordType.BeginBorders, d);
            foreach (MarkupNode n in list)
            {
                WriteBorder(d, n);
                w.Write(XlsbRecordType.Border, d);
            }

            w.Write(XlsbRecordType.EndBorders);
        }

        WriteXfs(w, d, root.Element("cellStyleXfs"), isStyle: true, XlsbRecordType.BeginCellStyleXFs, XlsbRecordType.EndCellStyleXFs);
        WriteXfs(w, d, root.Element("cellXfs"), isStyle: false, XlsbRecordType.BeginCellXFs, XlsbRecordType.EndCellXFs);
        MarkupNode styles = root.Element("cellStyles");
        if (!styles.IsNull)
        {
            var list = styles.Elements("cellStyle").ToList();
            d.U32((uint)list.Count);
            w.Write(XlsbRecordType.BeginStyles, d);
            foreach (MarkupNode n in list)
            {
                d.U32((uint)n.Int("xfId"));
                int builtin = n.Int("builtinId", -1);
                d.U16((builtin >= 0 ? 1 : 0) | (n.Bool("hidden") ? 2 : 0) | (n.Bool("customBuiltin") ? 4 : 0));
                d.U8(Math.Max(builtin, 0));
                d.U8(n.Int("iLevel"));
                d.WideString(n.Str("name"));
                w.Write(XlsbRecordType.Style, d);
            }

            w.Write(XlsbRecordType.EndStyles);
        }

        MarkupNode dxfsNode = root.Element("dxfs");
        {
            var list = dxfsNode.IsNull ? [] : dxfsNode.Elements("dxf").ToList();
            d.U32((uint)list.Count);
            w.Write(XlsbRecordType.BeginDXFs, d);
            foreach (MarkupNode n in list)
            {
                WriteDxf(d, n, loss);
                w.Write(XlsbRecordType.DXF, d);
            }

            w.Write(XlsbRecordType.EndDXFs);
        }

        MarkupNode ts = root.Element("tableStyles");
        d.U32(0);
        d.WideString(ts.IsNull ? "TableStyleMedium2" : ts.Str("defaultTableStyle", "TableStyleMedium2"));
        d.WideString(ts.IsNull ? "PivotStyleLight16" : ts.Str("defaultPivotStyle", "PivotStyleLight16"));
        w.Write(XlsbRecordType.BeginTableStyles, d);
        if (!ts.IsNull)
        {
            foreach (MarkupNode n in ts.ChildElements)
            {
                if (n.TryRaw(out int t, out byte[] data))
                {
                    w.Write(t, data);
                }
            }
        }

        w.Write(XlsbRecordType.EndTableStyles);
        MarkupNode colors = root.Element("colors");
        if (!colors.IsNull)
        {
            w.Write(XlsbRecordType.BeginColorPalette);
            MarkupNode ic = colors.Element("indexedColors");
            if (!ic.IsNull)
            {
                w.Write(XlsbRecordType.BeginIndexedColors);
                foreach (MarkupNode n in ic.Elements("rgbColor"))
                {
                    uint.TryParse(n.Str("rgb"), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint argb);
                    d.U8((int)((argb >> 16) & 0xFF));
                    d.U8((int)((argb >> 8) & 0xFF));
                    d.U8((int)(argb & 0xFF));
                    d.U8((int)(argb >> 24));
                    w.Write(XlsbRecordType.IndexedColor, d);
                }

                w.Write(XlsbRecordType.EndIndexedColors);
            }

            MarkupNode mc = colors.Element("mruColors");
            if (!mc.IsNull)
            {
                w.Write(XlsbRecordType.BeginMRUColors);
                foreach (MarkupNode n in mc.Elements("color"))
                {
                    XlsbColor.FromXml(n).Write(d);
                    w.Write(XlsbRecordType.MRUColor, d);
                }

                w.Write(XlsbRecordType.EndMRUColors);
            }

            w.Write(XlsbRecordType.EndColorPalette);
        }

        foreach (MarkupNode n in root.ChildElements)
        {
            if (n.TryRaw(out int t, out byte[] data))
            {
                w.Write(t, data);
            }
        }

        w.Write(XlsbRecordType.EndStyleSheet);
        return ms.ToArray();
    }

    private static void WriteFill(XlsbRecordData d, MarkupNode fill)
    {
        MarkupNode pf = fill.Element("patternFill");
        MarkupNode gf = fill.Element("gradientFill");
        if (!gf.IsNull)
        {
            d.U32(0x28);
            new XlsbColor(0, 0, 0, 0).Write(d);
            new XlsbColor(0, 0, 0, 0).Write(d);
            d.U32(gf.Str("type") == "path" ? 1u : 0u);
            d.Number(gf.Dbl("degree"));
            d.Number(gf.Dbl("left"));
            d.Number(gf.Dbl("right"));
            d.Number(gf.Dbl("top"));
            d.Number(gf.Dbl("bottom"));
            var stops = gf.Elements("stop").ToList();
            d.U32((uint)stops.Count);
            foreach (MarkupNode s in stops)
            {
                MarkupNode c = s.Element("color");
                (c.IsNull ? new XlsbColor(0, 0, 0, 0) : XlsbColor.FromXml(c)).Write(d);
                d.Number(s.Dbl("position"));
            }

            return;
        }

        int fls = Array.IndexOf(PatternNames, pf.IsNull ? "none" : pf.Str("patternType", "none"));
        if (fls < 0)
        {
            fls = 0;
        }

        MarkupNode fg = pf.IsNull ? default : pf.Element("fgColor");
        MarkupNode bg = pf.IsNull ? default : pf.Element("bgColor");
        d.U32((uint)fls);
        (fg.IsNull ? new XlsbColor(1, 64, 0, 0) : XlsbColor.FromXml(fg)).Write(d);
        (bg.IsNull ? new XlsbColor(1, 65, 0, 0) : XlsbColor.FromXml(bg)).Write(d);
        d.U32(0);
        d.Zeros(40);
        d.U32(0);
    }

    private static void WriteBorder(XlsbRecordData d, MarkupNode border)
    {
        d.U8((border.Bool("diagonalDown") ? 1 : 0) | (border.Bool("diagonalUp") ? 2 : 0));
        foreach (string name in new[] { "top", "bottom", "left", "right", "diagonal" })
        {
            MarkupNode side = border.Element(name);
            int style = side.IsNull ? 0 : Math.Max(0, Array.IndexOf(BorderNames, side.Str("style", "none")));
            MarkupNode c = side.IsNull ? default : side.Element("color");
            d.U8(style);
            d.U8(0);
            (c.IsNull ? new XlsbColor(0, 0, 0, 0) : XlsbColor.FromXml(c)).Write(d);
        }
    }

    private static void WriteXfs(XlsbRecordWriter w, XlsbRecordData d, MarkupNode node, bool isStyle, int begin, int end)
    {
        if (node.IsNull)
        {
            return;
        }

        var list = node.Elements("xf").ToList();
        d.U32((uint)list.Count);
        w.Write(begin, d);
        foreach (MarkupNode xf in list)
        {
            d.U16(isStyle ? 0xFFFF : xf.Int("xfId"));
            d.U16(xf.Int("numFmtId"));
            d.U16(xf.Int("fontId"));
            d.U16(xf.Int("fillId"));
            d.U16(xf.Int("borderId"));
            MarkupNode al = xf.Element("alignment");
            MarkupNode pr = xf.Element("protection");
            d.U8(al.IsNull ? 0 : al.Int("textRotation"));
            d.U8(al.IsNull ? 0 : al.Int("indent"));
            int alc = al.IsNull ? 0 : Math.Max(0, Array.IndexOf(HorizontalNames, al.Str("horizontal", "general")));
            int alcv = al.IsNull ? 2 : al.GetAttribute("vertical") is { } vs ? Math.Max(0, Array.IndexOf(VerticalNames, vs)) : 2;
            int word = alc | (alcv << 3) | (al.Bool("wrapText") ? 0x40 : 0) | (al.Bool("justifyLastLine") ? 0x80 : 0) | (al.Bool("shrinkToFit") ? 0x100 : 0)
                | (al.Int("readingOrder") << 10) | (pr.IsNull || pr.Bool("locked", true) ? 0x1000 : 0) | (!pr.IsNull && pr.Bool("hidden") ? 0x2000 : 0)
                | (xf.Bool("pivotButton") ? 0x4000 : 0) | (xf.Bool("quotePrefix") ? 0x8000 : 0);
            d.U16(word);
            string[] attrs = ["applyNumberFormat", "applyFont", "applyAlignment", "applyBorder", "applyFill", "applyProtection"];
            int atr = 0;
            for (int b = 0; b < 6; b++)
            {
                bool apply = xf.Bool(attrs[b], !isStyle ? false : true);
                bool bit = isStyle ? !apply : apply;
                if (bit)
                {
                    atr |= 1 << b;
                }
            }

            d.U16(atr);
            w.Write(XlsbRecordType.XF, d);
        }

        w.Write(end);
    }

    private static void WriteDxf(XlsbRecordData d, MarkupNode dxf, ILossSink loss)
    {
        var props = new List<(int Type, byte[] Data)>();
        void Add(int t, Action<XlsbRecordData> body)
        {
            var b = new XlsbRecordData();
            body(b);
            props.Add((t, b.Span.ToArray()));
        }

        void Color(int t, MarkupNode c)
        {
            XlsbColor col = XlsbColor.FromXml(c);
            Add(t, b => WritePropColor(b, col));
        }

        MarkupNode font = dxf.Element("font");
        MarkupNode fill = dxf.Element("fill");
        MarkupNode align = dxf.Element("alignment");
        MarkupNode border = dxf.Element("border");
        MarkupNode prot = dxf.Element("protection");
        MarkupNode nf = dxf.Element("numFmt");
        if (!fill.IsNull)
        {
            MarkupNode pf = fill.Element("patternFill");
            if (!pf.IsNull)
            {
                if (pf.GetAttribute("patternType") is { } pt)
                {
                    int idx = Array.IndexOf(PatternNames, pt);
                    Add(0x00, b => b.U16(Math.Max(idx, 0)));
                }

                if (!pf.Element("fgColor").IsNull)
                {
                    Color(0x01, pf.Element("fgColor"));
                }

                if (!pf.Element("bgColor").IsNull)
                {
                    Color(0x02, pf.Element("bgColor"));
                }
            }
            else
            {
                loss.Report("xlsb.dxf-gradient", "gradient fill in a differential format", 1, "A gradient fill in a differential format is not written.");
            }
        }

        if (!font.IsNull)
        {
            if (!font.Element("color").IsNull)
            {
                Color(0x05, font.Element("color"));
            }

            foreach (MarkupNode c in font.ChildElements)
            {
                switch (c.LocalNameString)
                {
                    case "name": { string nm = c.Str("val"); Add(0x18, b => { b.U16(nm.Length); b.Bytes(System.Text.Encoding.Unicode.GetBytes(nm)); }); break; }
                    case "b": { bool on = c.Switch(); Add(0x19, b => b.U16(on ? 0x2BC : 0x190)); break; }
                    case "u": { int u = c.Str("val", "single") switch { "double" => 2, "singleAccounting" => 0x21, "doubleAccounting" => 0x22, "none" => 0, _ => 1 }; Add(0x1A, b => b.U8(u)); break; }
                    case "vertAlign": { int s = c.Str("val") == "superscript" ? 1 : c.Str("val") == "subscript" ? 2 : 0; Add(0x1B, b => b.U8(s)); break; }
                    case "i": { bool on = c.Switch(); Add(0x1C, b => b.U8(on ? 1 : 0)); break; }
                    case "strike": { bool on = c.Switch(); Add(0x1D, b => b.U8(on ? 1 : 0)); break; }
                    case "outline": { bool on = c.Switch(); Add(0x1E, b => b.U8(on ? 1 : 0)); break; }
                    case "shadow": { bool on = c.Switch(); Add(0x1F, b => b.U8(on ? 1 : 0)); break; }
                    case "condense": { bool on = c.Switch(); Add(0x20, b => b.U8(on ? 1 : 0)); break; }
                    case "extend": { bool on = c.Switch(); Add(0x21, b => b.U8(on ? 1 : 0)); break; }
                    case "sz": { int sz = (int)Math.Round(c.Dbl("val", 11) * 20); Add(0x24, b => b.U32((uint)sz)); break; }
                }
            }
        }

        if (!border.IsNull)
        {
            if (border.Bool("diagonalUp"))
            {
                Add(0x0D, b => b.U8(1));
            }

            if (border.Bool("diagonalDown"))
            {
                Add(0x0E, b => b.U8(1));
            }

            (string Name, int Type)[] sides = [("top", 0x06), ("bottom", 0x07), ("left", 0x08), ("right", 0x09), ("diagonal", 0x0A), ("vertical", 0x0B), ("horizontal", 0x0C)];
            foreach ((string name, int t) in sides)
            {
                MarkupNode side = border.Element(name);
                if (side.IsNull)
                {
                    continue;
                }

                int style = Math.Max(0, Array.IndexOf(BorderNames, side.Str("style", "none")));
                MarkupNode c = side.Element("color");
                XlsbColor col = c.IsNull ? new XlsbColor(0, 0, 0, 0) : XlsbColor.FromXml(c);
                Add(t, b =>
                {
                    WritePropColor(b, col);
                    b.U16(style);
                });
            }
        }

        if (!align.IsNull)
        {
            if (align.GetAttribute("horizontal") is { } hz)
            {
                Add(0x0F, b => b.U8(Math.Max(0, Array.IndexOf(HorizontalNames, hz))));
            }

            if (align.GetAttribute("vertical") is { } vt)
            {
                Add(0x10, b => b.U8(Math.Max(0, Array.IndexOf(VerticalNames, vt))));
            }

            if (align.GetAttribute("textRotation") is not null)
            {
                int rot = align.Int("textRotation");
                Add(0x11, b => b.U8(rot));
            }

            if (align.GetAttribute("indent") is not null)
            {
                int ind = align.Int("indent");
                Add(0x12, b => b.U16(ind));
            }

            if (align.GetAttribute("readingOrder") is not null)
            {
                int ro = align.Int("readingOrder");
                Add(0x13, b => b.U8(ro));
            }

            if (align.GetAttribute("wrapText") is not null)
            {
                int wv = align.Bool("wrapText") ? 1 : 0;
                Add(0x14, b => b.U8(wv));
            }

            if (align.GetAttribute("justifyLastLine") is not null)
            {
                int jv = align.Bool("justifyLastLine") ? 1 : 0;
                Add(0x15, b => b.U8(jv));
            }

            if (align.GetAttribute("shrinkToFit") is not null)
            {
                int sv = align.Bool("shrinkToFit") ? 1 : 0;
                Add(0x16, b => b.U8(sv));
            }
        }

        if (!nf.IsNull)
        {
            int id = nf.Int("numFmtId");
            Add(0x29, b => b.U16(id));
        }

        if (!prot.IsNull)
        {
            if (prot.GetAttribute("locked") is not null)
            {
                int lv = prot.Bool("locked") ? 1 : 0;
                Add(0x2B, b => b.U8(lv));
            }

            if (prot.GetAttribute("hidden") is not null)
            {
                int hv = prot.Bool("hidden") ? 1 : 0;
                Add(0x2C, b => b.U8(hv));
            }
        }

        props.Sort((a, b) => a.Type.CompareTo(b.Type));
        d.U16(0x8000);
        d.U16(0);
        d.U16(props.Count);
        foreach ((int t, byte[] data) in props)
        {
            d.U16(t);
            d.U16(data.Length + 4);
            d.Bytes(data);
        }
    }
}
