using System.Globalization;
using System.Text;
using RecursiveAI.OneOffice.NumberFormats;

namespace RecursiveAI.OneOffice.Spreadsheets.Streaming;

/// <summary>
/// Builds the style sheet part for the streaming writer from the cell formats the caller adds. Fonts, fills, borders and number
/// formats are shared between formats. Index 0 is the default format, as every workbook has one.
/// </summary>
/// <remarks>ECMA-376 Part 1 CT_Stylesheet: numFmts, fonts, fills (the first two are fixed: none and gray125), borders, cellStyleXfs, cellXfs, cellStyles.</remarks>
internal sealed class StyleSheetBuilder
{
    private const int MaxFormats = 64_000; // Excel's limit on cell formats

    private readonly List<string> _fonts = ["<font><sz val=\"11\"/><color theme=\"1\"/><name val=\"Calibri\"/><family val=\"2\"/><scheme val=\"minor\"/></font>"];
    private readonly Dictionary<string, int> _fontIndex = [];
    private readonly List<string> _fills = ["<fill><patternFill patternType=\"none\"/></fill>", "<fill><patternFill patternType=\"gray125\"/></fill>"];
    private readonly Dictionary<string, int> _fillIndex = [];
    private readonly List<string> _borders = ["<border><left/><right/><top/><bottom/><diagonal/></border>"];
    private readonly Dictionary<string, int> _borderIndex = [];
    private readonly List<string> _numFmts = [];
    private readonly Dictionary<string, int> _numFmtIndex = [];
    private readonly List<string> _xfs = ["<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>"];
    private readonly Dictionary<string, int> _xfIndex = [];

    public int Count => _xfs.Count;

    public int Add(StreamCellFormat f)
    {
        int numFmt = 0;
        if (f.NumberFormat is { Length: > 0 } code && code != "General")
        {
            int builtIn = NumberFormat.FindBuiltInId(code);
            if (builtIn >= 0)
            {
                numFmt = builtIn;
            }
            else if (!_numFmtIndex.TryGetValue(code, out numFmt))
            {
                numFmt = 164 + _numFmts.Count;
                _numFmts.Add("<numFmt numFmtId=\"" + numFmt.ToString(CultureInfo.InvariantCulture) + "\" formatCode=\"" + Esc(code) + "\"/>");
                _numFmtIndex[code] = numFmt;
            }
        }

        int font = 0;
        if (f.Bold || f.Italic || f.Underline || f.FontName is not null || f.FontSize > 0 || f.FontColor >= 0)
        {
            var sb = new StringBuilder("<font>");
            if (f.Bold)
            {
                sb.Append("<b/>");
            }

            if (f.Italic)
            {
                sb.Append("<i/>");
            }

            if (f.Underline)
            {
                sb.Append("<u/>");
            }

            sb.Append("<sz val=\"").Append((f.FontSize > 0 ? f.FontSize : 11).ToString("R", CultureInfo.InvariantCulture)).Append("\"/>");
            if (f.FontColor >= 0)
            {
                sb.Append("<color rgb=\"FF").Append((f.FontColor & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture)).Append("\"/>");
            }
            else
            {
                sb.Append("<color theme=\"1\"/>");
            }

            sb.Append("<name val=\"").Append(Esc(f.FontName ?? "Calibri")).Append("\"/><family val=\"2\"/>");
            if (f.FontName is null)
            {
                sb.Append("<scheme val=\"minor\"/>");
            }

            sb.Append("</font>");
            font = Intern(_fonts, _fontIndex, sb.ToString());
        }

        int fill = 0;
        if (f.FillColor >= 0)
        {
            string rgb = "FF" + (f.FillColor & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture);
            fill = Intern(_fills, _fillIndex, "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"" + rgb + "\"/><bgColor indexed=\"64\"/></patternFill></fill>", 2);
        }

        int border = 0;
        if (f.Border != StreamBorder.None)
        {
            string style = f.Border == StreamBorder.DoubleLine ? "double" : f.Border.ToString().ToLowerInvariant();
            string side = " style=\"" + style + "\"><color auto=\"1\"/>";
            string xml = "<border><left" + side + "</left><right" + side + "</right><top" + side + "</top><bottom" + side + "</bottom><diagonal/></border>";
            border = Intern(_borders, _borderIndex, xml);
        }

        var x = new StringBuilder("<xf numFmtId=\"").Append(numFmt).Append("\" fontId=\"").Append(font).Append("\" fillId=\"").Append(fill)
            .Append("\" borderId=\"").Append(border).Append("\" xfId=\"0\"");
        if (numFmt != 0)
        {
            x.Append(" applyNumberFormat=\"1\"");
        }

        if (font != 0)
        {
            x.Append(" applyFont=\"1\"");
        }

        if (fill != 0)
        {
            x.Append(" applyFill=\"1\"");
        }

        if (border != 0)
        {
            x.Append(" applyBorder=\"1\"");
        }

        bool align = f.Horizontal != StreamHorizontalAlignment.General || f.Vertical != StreamVerticalAlignment.Bottom || f.WrapText;
        if (align)
        {
            x.Append(" applyAlignment=\"1\"><alignment");
            if (f.Horizontal != StreamHorizontalAlignment.General)
            {
                x.Append(" horizontal=\"").Append(f.Horizontal.ToString().ToLowerInvariant()).Append('"');
            }

            if (f.Vertical != StreamVerticalAlignment.Bottom)
            {
                x.Append(" vertical=\"").Append(f.Vertical.ToString().ToLowerInvariant()).Append('"');
            }

            if (f.WrapText)
            {
                x.Append(" wrapText=\"1\"");
            }

            x.Append("/></xf>");
        }
        else
        {
            x.Append("/>");
        }

        string key = x.ToString();
        if (_xfIndex.TryGetValue(key, out int existing))
        {
            return existing;
        }

        if (_xfs.Count >= MaxFormats)
        {
            throw new InvalidOperationException("A workbook holds at most " + MaxFormats + " cell formats.");
        }

        _xfs.Add(key);
        _xfIndex[key] = _xfs.Count - 1;
        return _xfs.Count - 1;
    }

    private static int Intern(List<string> list, Dictionary<string, int> index, string xml, int firstFree = 1)
    {
        if (!index.TryGetValue(xml, out int i))
        {
            i = list.Count;
            list.Add(xml);
            index[xml] = i;
        }

        _ = firstFree;
        return i;
    }

    private static string Esc(string s) => s.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal);

    public void Write(Utf8Buffer w, string mainNamespace)
    {
        w.Ascii("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<styleSheet xmlns=\"" + mainNamespace + "\">");
        if (_numFmts.Count > 0)
        {
            w.Ascii("<numFmts count=\"" + _numFmts.Count.ToString(CultureInfo.InvariantCulture) + "\">");
            Block(w, _numFmts);
            w.Ascii("</numFmts>");
        }

        Section(w, "fonts", _fonts);
        Section(w, "fills", _fills);
        Section(w, "borders", _borders);
        w.Ascii("<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>");
        Section(w, "cellXfs", _xfs);
        w.Ascii("<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles><dxfs count=\"0\"/></styleSheet>");
    }

    private static void Section(Utf8Buffer w, string name, List<string> items)
    {
        w.Ascii("<" + name + " count=\"" + items.Count.ToString(CultureInfo.InvariantCulture) + "\">");
        Block(w, items);
        w.Ascii("</" + name + ">");
    }

    private static void Block(Utf8Buffer w, List<string> items)
    {
        foreach (string s in items)
        {
            w.Raw(Encoding.UTF8.GetBytes(s));
        }
    }
}
