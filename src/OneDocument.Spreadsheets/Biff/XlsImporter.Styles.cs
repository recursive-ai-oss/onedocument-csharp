using System.Globalization;
using System.Linq;
using System.Text;

namespace RecursiveAI.OneDocument.Spreadsheets.Biff;

internal sealed partial class XlsImporter
{
    private const string MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private readonly List<string> _dxfs = [];

    // ---- XF ----

    // [MS-XLS] §2.4.353 XF. BIFF8 and BIFF5 pack alignment, borders and fill into bit fields; BIFF3 and BIFF4 only the first few
    // properties are read (no specification for them is public, so the rest is dropped and reported).
    private void ReadXf(BiffRecord r)
    {
        var x = new XfInfo();
        switch (_version)
        {
            case 8:
                x.Font = U16(r, 0);
                x.Format = U16(r, 2);
                ReadXfFlags(x, U16(r, 4));
                {
                    int b6 = U8(r, 6);
                    x.HAlign = b6 & 7;
                    x.Wrap = (b6 & 8) != 0;
                    x.VAlign = (b6 >> 4) & 7;
                    x.Rotation = U8(r, 7);
                    int b8 = U8(r, 8);
                    x.Indent = b8 & 0xF;
                    x.Shrink = (b8 & 0x10) != 0;
                    x.ReadingOrder = (b8 >> 6) & 3;
                    uint border1 = U32(r, 10);
                    uint border2 = U32(r, 14);
                    x.BorderStyle[0] = (int)(border1 & 0xF);
                    x.BorderStyle[1] = (int)((border1 >> 4) & 0xF);
                    x.BorderStyle[2] = (int)((border1 >> 8) & 0xF);
                    x.BorderStyle[3] = (int)((border1 >> 12) & 0xF);
                    x.BorderColor[0] = (int)((border1 >> 16) & 0x7F);
                    x.BorderColor[1] = (int)((border1 >> 23) & 0x7F);
                    x.DiagDown = (border1 & (1u << 30)) != 0;
                    x.DiagUp = (border1 & (1u << 31)) != 0;
                    x.BorderColor[2] = (int)(border2 & 0x7F);
                    x.BorderColor[3] = (int)((border2 >> 7) & 0x7F);
                    x.DiagColor = (int)((border2 >> 14) & 0x7F);
                    x.DiagStyle = (int)((border2 >> 21) & 0xF);
                    x.Pattern = (int)((border2 >> 26) & 0x3F);
                    int fill = U16(r, 18);
                    x.Fore = fill & 0x7F;
                    x.Back = (fill >> 7) & 0x7F;
                }

                break;
            case 5:
                x.Font = U16(r, 0);
                x.Format = U16(r, 2);
                ReadXfFlags(x, U16(r, 4));
                {
                    int b6 = U8(r, 6);
                    x.HAlign = b6 & 7;
                    x.Wrap = (b6 & 8) != 0;
                    x.VAlign = (b6 >> 4) & 7;
                    int orient = U8(r, 7) & 3;
                    x.Rotation = orient switch { 1 => 255, 2 => 90, 3 => 180, _ => 0 };
                    uint area = U32(r, 8);
                    x.Fore = (int)(area & 0x7F);
                    x.Back = (int)((area >> 7) & 0x7F);
                    x.Pattern = (int)((area >> 16) & 0x3F);
                    uint border = U32(r, 12);
                    x.BorderStyle[2] = (int)(border & 7);
                    x.BorderColor[2] = (int)((border >> 3) & 0x1F);
                    x.BorderStyle[0] = (int)((border >> 8) & 7);
                    x.BorderColor[0] = (int)((border >> 11) & 0x1F);
                    x.BorderStyle[3] = (int)((border >> 16) & 7);
                    x.BorderColor[3] = (int)((border >> 19) & 0x1F);
                    x.BorderStyle[1] = (int)((border >> 24) & 7);
                    x.BorderColor[1] = (int)((border >> 27) & 0x1F);
                }

                break;
            default:
                // BIFF3 and BIFF4: font and format are single bytes, then protection/style flags and the alignment byte.
                x.Font = U8(r, 0);
                x.Format = U8(r, 1);
                ReadXfFlags(x, U16(r, 2));
                x.HAlign = U8(r, 4) & 7;
                x.Wrap = (U8(r, 4) & 8) != 0;
                _lossCounts["loss.legacy-cell-borders"] = _lossCounts.GetValueOrDefault("loss.legacy-cell-borders");
                break;
        }

        _xfs.Add(x);
    }

    private static void ReadXfFlags(XfInfo x, int flags)
    {
        x.Locked = (flags & 1) != 0;
        x.Hidden = (flags & 2) != 0;
        x.IsStyle = (flags & 4) != 0;
        x.Parent = (flags >> 4) & 0xFFF;
    }

    private static string Esc(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        foreach (char c in s)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                default:
                    if (c < 0x20 && c != '\t' && c != '\n' && c != '\r')
                    {
                        break; // a control character cannot be written in XML 1.0
                    }

                    if (c == '\n' || c == '\r' || c == '\t')
                    {
                        sb.Append("&#").Append((int)c).Append(';');
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        return sb.ToString();
    }

    private static string I(int v) => v.ToString(CultureInfo.InvariantCulture);

    // ---- styles.xml ----

    private static readonly string[] LineStyles =
        ["", "thin", "medium", "dashed", "dotted", "thick", "double", "hair", "mediumDashed", "dashDot", "mediumDashDot", "dashDotDot", "mediumDashDotDot", "slantDashDot"];

    private static readonly string[] Patterns =
    [
        "none", "solid", "mediumGray", "darkGray", "lightGray", "darkHorizontal", "darkVertical", "darkDown", "darkUp", "darkGrid", "darkTrellis",
        "lightHorizontal", "lightVertical", "lightDown", "lightUp", "lightGrid", "lightTrellis", "gray125", "gray0625",
    ];

    private static readonly string[] HAligns = ["", "left", "center", "right", "fill", "justify", "centerContinuous", "distributed"];

    private static readonly string[] VAligns = ["top", "center", "", "justify", "distributed"];

    private static string Color(string tag, int icv)
    {
        if (icv == 0x7FFF || icv < 0)
        {
            return "<" + tag + " auto=\"1\"/>";
        }

        return "<" + tag + " indexed=\"" + I(icv) + "\"/>";
    }

    private byte[] BuildStyles()
    {
        EnsureDefaultXf();
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<styleSheet xmlns=\"").Append(MainNs).Append("\">");

        // Formats whose identifier is below 164 (the built-in range) are renumbered, because a file may redefine them locale by locale.
        var remap = new Dictionary<int, int>();
        int next = 164;
        foreach (int id in _formats.Keys)
        {
            if (id >= next)
            {
                next = id + 1;
            }
        }

        var custom = new List<(int Id, string Code)>();
        foreach (KeyValuePair<int, string> kv in _formats.OrderBy(k => k.Key))
        {
            if (kv.Key >= 164)
            {
                custom.Add((kv.Key, kv.Value));
            }
            else if (!string.Equals(kv.Value, "General", StringComparison.OrdinalIgnoreCase) && kv.Value.Length > 0)
            {
                remap[kv.Key] = next;
                custom.Add((next++, kv.Value));
            }
        }

        if (custom.Count > 0)
        {
            sb.Append("<numFmts count=\"").Append(I(custom.Count)).Append("\">");
            foreach ((int id, string code) in custom)
            {
                sb.Append("<numFmt numFmtId=\"").Append(I(id)).Append("\" formatCode=\"").Append(Esc(code)).Append("\"/>");
            }

            sb.Append("</numFmts>");
        }

        // fonts
        if (_fonts.Count == 0)
        {
            _fonts.Add(new FontInfo { Height = 200, Name = "Arial" });
        }

        sb.Append("<fonts count=\"").Append(I(_fonts.Count)).Append("\">");
        foreach (FontInfo f in _fonts)
        {
            AppendFont(sb, f);
        }

        sb.Append("</fonts>");

        // fills and borders are shared between XFs
        var fills = new List<string>
        {
            "<fill><patternFill patternType=\"none\"/></fill>",
            "<fill><patternFill patternType=\"gray125\"/></fill>",
        };
        var fillIndex = new Dictionary<string, int>();
        var borders = new List<string>();
        var borderIndex = new Dictionary<string, int>();
        var xfXml = new StringBuilder();
        var styleXfXml = new StringBuilder();
        var styleXfPos = new Dictionary<int, int>();
        int styleXfCount = 0;
        for (int i = 0; i < _xfs.Count; i++)
        {
            if (_xfs[i].IsStyle)
            {
                styleXfPos[i] = styleXfCount++;
            }
        }

        bool syntheticStyle = styleXfCount == 0;
        if (syntheticStyle)
        {
            styleXfCount = 1;
        }

        for (int i = 0; i < _xfs.Count; i++)
        {
            XfInfo x = _xfs[i];
            int fillId = FillId(x, fills, fillIndex);
            int borderId = BorderId(x, borders, borderIndex);
            int fmt = remap.TryGetValue(x.Format, out int m) ? m : x.Format;
            int font = x.Font < _fonts.Count ? x.Font : 0;
            int parent = 0;
            if (!x.IsStyle && x.Parent != 0xFFF && styleXfPos.TryGetValue(x.Parent, out int pp))
            {
                parent = pp;
            }

            var one = new StringBuilder();
            one.Append("<xf numFmtId=\"").Append(I(fmt)).Append("\" fontId=\"").Append(I(font)).Append("\" fillId=\"").Append(I(fillId))
                .Append("\" borderId=\"").Append(I(borderId)).Append('"');
            if (!x.IsStyle)
            {
                one.Append(" xfId=\"").Append(I(parent)).Append('"');
            }

            if (fmt != 0)
            {
                one.Append(" applyNumberFormat=\"1\"");
            }

            one.Append(" applyFont=\"1\"");
            if (fillId != 0)
            {
                one.Append(" applyFill=\"1\"");
            }

            if (borderId != 0)
            {
                one.Append(" applyBorder=\"1\"");
            }

            string align = AlignmentXml(x);
            string prot = !x.Locked || x.Hidden ? "<protection locked=\"" + (x.Locked ? "1" : "0") + "\" hidden=\"" + (x.Hidden ? "1" : "0") + "\"/>" : string.Empty;
            if (align.Length > 0)
            {
                one.Append(" applyAlignment=\"1\"");
            }

            if (prot.Length > 0)
            {
                one.Append(" applyProtection=\"1\"");
            }

            if (align.Length == 0 && prot.Length == 0)
            {
                one.Append("/>");
            }
            else
            {
                one.Append('>').Append(align).Append(prot).Append("</xf>");
            }

            xfXml.Append(one);
            if (x.IsStyle)
            {
                styleXfXml.Append(one);
            }
        }

        sb.Append("<fills count=\"").Append(I(fills.Count)).Append("\">").AppendJoin(string.Empty, fills).Append("</fills>");
        if (borders.Count == 0)
        {
            borders.Add("<border><left/><right/><top/><bottom/><diagonal/></border>");
        }

        sb.Append("<borders count=\"").Append(I(borders.Count)).Append("\">").AppendJoin(string.Empty, borders).Append("</borders>");
        sb.Append("<cellStyleXfs count=\"").Append(I(styleXfCount)).Append("\">");
        if (syntheticStyle)
        {
            sb.Append("<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/>");
        }
        else
        {
            sb.Append(styleXfXml);
        }

        sb.Append("</cellStyleXfs>");
        sb.Append("<cellXfs count=\"").Append(I(_xfs.Count)).Append("\">").Append(xfXml).Append("</cellXfs>");

        // named styles
        var named = new StringBuilder();
        int namedCount = 0;
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool hasNormal = false;
        foreach (StyleInfo s in _styleInfos)
        {
            string? name = s.BuiltIn ? BuiltInStyleName(s.BuiltInId, s.Level) : s.Name;
            int pos = styleXfPos.TryGetValue(s.Xf, out int sp) ? sp : -1;
            if (name is null || pos < 0 || !seenNames.Add(name))
            {
                continue;
            }

            named.Append("<cellStyle name=\"").Append(Esc(name)).Append("\" xfId=\"").Append(I(pos)).Append('"');
            if (s.BuiltIn)
            {
                named.Append(" builtinId=\"").Append(I(s.BuiltInId)).Append('"');
                hasNormal |= s.BuiltInId == 0;
            }

            named.Append("/>");
            namedCount++;
        }

        if (!hasNormal && seenNames.Add("Normal"))
        {
            named.Append("<cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/>");
            namedCount++;
        }

        sb.Append("<cellStyles count=\"").Append(I(namedCount)).Append("\">").Append(named).Append("</cellStyles>");
        if (_dxfs.Count > 0)
        {
            sb.Append("<dxfs count=\"").Append(I(_dxfs.Count)).Append("\">").AppendJoin(string.Empty, _dxfs).Append("</dxfs>");
        }

        if (_palette is { } pal)
        {
            sb.Append("<colors><indexedColors>");
            uint[] first8 = [0x000000, 0xFFFFFF, 0xFF0000, 0x00FF00, 0x0000FF, 0xFFFF00, 0xFF00FF, 0x00FFFF];
            foreach (uint c in first8)
            {
                sb.Append("<rgbColor rgb=\"FF").Append(c.ToString("X6", CultureInfo.InvariantCulture)).Append("\"/>");
            }

            foreach (uint c in pal)
            {
                sb.Append("<rgbColor rgb=\"FF").Append(c.ToString("X6", CultureInfo.InvariantCulture)).Append("\"/>");
            }

            sb.Append("</indexedColors></colors>");
        }

        sb.Append("</styleSheet>");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private void EnsureDefaultXf()
    {
        if (_xfs.Count == 0)
        {
            _xfs.Add(new XfInfo());
        }
    }

    private static void AppendFont(StringBuilder sb, FontInfo f)
    {
        sb.Append("<font>");
        if (f.Weight >= 700)
        {
            sb.Append("<b/>");
        }

        if ((f.Flags & 2) != 0)
        {
            sb.Append("<i/>");
        }

        if ((f.Flags & 8) != 0)
        {
            sb.Append("<strike/>");
        }

        if ((f.Flags & 0x10) != 0)
        {
            sb.Append("<outline/>");
        }

        if ((f.Flags & 0x20) != 0)
        {
            sb.Append("<shadow/>");
        }

        switch (f.Underline)
        {
            case 1:
            case 0x21:
                sb.Append(f.Underline == 1 ? "<u/>" : "<u val=\"singleAccounting\"/>");
                break;
            case 2:
            case 0x22:
                sb.Append(f.Underline == 2 ? "<u val=\"double\"/>" : "<u val=\"doubleAccounting\"/>");
                break;
            default:
                break;
        }

        if (f.Script == 1)
        {
            sb.Append("<vertAlign val=\"superscript\"/>");
        }
        else if (f.Script == 2)
        {
            sb.Append("<vertAlign val=\"subscript\"/>");
        }

        sb.Append("<sz val=\"").Append((f.Height / 20.0).ToString("0.##", CultureInfo.InvariantCulture)).Append("\"/>");
        if (f.Color != 0x7FFF && f.Color != 0x40)
        {
            sb.Append(Color("color", f.Color));
        }

        sb.Append("<name val=\"").Append(Esc(f.Name)).Append("\"/>");
        if (f.Family is > 0 and < 6)
        {
            sb.Append("<family val=\"").Append(I(f.Family)).Append("\"/>");
        }

        if (f.Charset != 0)
        {
            sb.Append("<charset val=\"").Append(I(f.Charset)).Append("\"/>");
        }

        sb.Append("</font>");
    }

    private static int FillId(XfInfo x, List<string> fills, Dictionary<string, int> index)
    {
        if (x.Pattern == 0)
        {
            return 0;
        }

        string pat = x.Pattern < Patterns.Length ? Patterns[x.Pattern] : "solid";
        string xml = "<fill><patternFill patternType=\"" + pat + "\">" + Color("fgColor", x.Fore) + Color("bgColor", x.Back) + "</patternFill></fill>";
        if (!index.TryGetValue(xml, out int id))
        {
            id = fills.Count;
            fills.Add(xml);
            index[xml] = id;
        }

        return id;
    }

    private const string EmptyBorder = "<border><left/><right/><top/><bottom/><diagonal/></border>";

    private static int BorderId(XfInfo x, List<string> borders, Dictionary<string, int> index)
    {
        // Slots are left, right, top, bottom.
        string[] names = ["left", "right", "top", "bottom"];
        var sb = new StringBuilder("<border");
        if (x.DiagDown)
        {
            sb.Append(" diagonalDown=\"1\"");
        }

        if (x.DiagUp)
        {
            sb.Append(" diagonalUp=\"1\"");
        }

        sb.Append('>');
        for (int i = 0; i < 4; i++)
        {
            AppendEdge(sb, names[i], x.BorderStyle[i], x.BorderColor[i]);
        }

        AppendEdge(sb, "diagonal", x.DiagUp || x.DiagDown ? x.DiagStyle : 0, x.DiagColor);
        sb.Append("</border>");
        string xml = sb.ToString();
        if (borders.Count == 0)
        {
            borders.Add(EmptyBorder);
            index[EmptyBorder] = 0;
        }

        if (!index.TryGetValue(xml, out int id))
        {
            id = borders.Count;
            borders.Add(xml);
            index[xml] = id;
        }

        return id;
    }

    private static void AppendEdge(StringBuilder sb, string name, int style, int color)
    {
        if (style <= 0 || style >= LineStyles.Length)
        {
            sb.Append('<').Append(name).Append("/>");
            return;
        }

        sb.Append('<').Append(name).Append(" style=\"").Append(LineStyles[style]).Append("\">").Append(Color("color", color)).Append("</").Append(name).Append('>');
    }

    private static string AlignmentXml(XfInfo x)
    {
        var sb = new StringBuilder();
        if (x.HAlign is > 0 and < 8)
        {
            sb.Append(" horizontal=\"").Append(HAligns[x.HAlign]).Append('"');
        }

        if (x.VAlign is >= 0 and < 5 && x.VAlign != 2)
        {
            sb.Append(" vertical=\"").Append(VAligns[x.VAlign]).Append('"');
        }

        if (x.Wrap)
        {
            sb.Append(" wrapText=\"1\"");
        }

        if (x.Rotation != 0 && x.Rotation <= 180 || x.Rotation == 255)
        {
            sb.Append(" textRotation=\"").Append(I(x.Rotation)).Append('"');
        }

        if (x.Indent > 0)
        {
            sb.Append(" indent=\"").Append(I(x.Indent)).Append('"');
        }

        if (x.Shrink)
        {
            sb.Append(" shrinkToFit=\"1\"");
        }

        if (x.ReadingOrder is 1 or 2)
        {
            sb.Append(" readingOrder=\"").Append(I(x.ReadingOrder)).Append('"');
        }

        return sb.Length == 0 ? string.Empty : "<alignment" + sb + "/>";
    }

    private static string? BuiltInStyleName(int id, int level) => id switch
    {
        0 => "Normal",
        1 => "RowLevel_" + I(level + 1),
        2 => "ColLevel_" + I(level + 1),
        3 => "Comma",
        4 => "Currency",
        5 => "Percent",
        6 => "Comma [0]",
        7 => "Currency [0]",
        8 => "Hyperlink",
        9 => "Followed Hyperlink",
        10 => "Note",
        11 => "Warning Text",
        15 => "Title",
        16 => "Heading 1",
        17 => "Heading 2",
        18 => "Heading 3",
        19 => "Heading 4",
        20 => "Input",
        21 => "Output",
        22 => "Calculation",
        23 => "Check Cell",
        24 => "Linked Cell",
        25 => "Total",
        26 => "Good",
        27 => "Bad",
        28 => "Neutral",
        53 => "Explanatory Text",
        _ => null,
    };

    private void SetDate1904() => _wb.InsertWorkbookChild("workbookPr", "<" + _wb.Pc() + "workbookPr date1904=\"1\"/>");

    // ---- conditional-format differential formats ----

    /// <summary>Reads a DXFN structure ([MS-XLS] §2.5.95) of known length and returns a <c>dxf</c> element.</summary>
    private string DxfXml(ReadOnlySpan<byte> d)
    {
        if (d.Length < 6)
        {
            return "<dxf/>";
        }

        uint flags = (uint)(d[0] | (d[1] << 8) | (d[2] << 16) | (d[3] << 24));
        int flags2 = d[4] | (d[5] << 8);
        bool atrNum = (flags & (1u << 25)) != 0;
        bool atrFnt = (flags & (1u << 26)) != 0;
        bool atrAlc = (flags & (1u << 27)) != 0;
        bool atrBdr = (flags & (1u << 28)) != 0;
        bool atrPat = (flags & (1u << 29)) != 0;
        int p = 6;
        var font = new StringBuilder();
        var fill = new StringBuilder();
        var border = new StringBuilder();
        string numFmt = string.Empty;

        if (atrNum && p + 2 <= d.Length)
        {
            if ((flags2 & 1) != 0)
            {
                int cb = d[p] | (d[p + 1] << 8);
                if (p + 2 + 3 <= d.Length)
                {
                    int cch = d[p + 2] | (d[p + 3] << 8);
                    bool wide = (d[p + 4] & 1) != 0;
                    int byteLen = wide ? cch * 2 : cch;
                    if (p + 5 + byteLen <= d.Length)
                    {
                        string code = wide ? Encoding.Unicode.GetString(d.Slice(p + 5, byteLen)) : Encoding.Latin1.GetString(d.Slice(p + 5, byteLen));
                        numFmt = "<numFmt numFmtId=\"" + I(200 + _dxfs.Count) + "\" formatCode=\"" + Esc(code) + "\"/>";
                    }
                }

                p += Math.Max(cb, 2) + 0;
            }
            else
            {
                int ifmt = d[p + 1];
                numFmt = "<numFmt numFmtId=\"" + I(ifmt) + "\" formatCode=\"" + Esc(BuiltInFormatText(ifmt)) + "\"/>";
                p += 2;
            }
        }

        if (atrFnt && p + 118 <= d.Length)
        {
            ReadOnlySpan<byte> f = d.Slice(p, 118);
            int ts = f[68];
            int bls = (short)(f[72] | (f[73] << 8));
            int uls = f[76];
            int icv = f[80] | (f[81] << 8) | (f[82] << 16) | (f[83] << 24);
            bool tsNinchItalic = (f[88] & 2) != 0;
            bool tsNinchStrike = (f[88] & 0x80) != 0;
            bool blsNinch = f[100] != 0;
            bool ulsNinch = f[96] != 0;
            if (!blsNinch && bls >= 600)
            {
                font.Append("<b/>");
            }

            if (!tsNinchItalic && (ts & 2) != 0)
            {
                font.Append("<i/>");
            }

            if (!tsNinchStrike && (ts & 0x80) != 0)
            {
                font.Append("<strike/>");
            }

            if (!ulsNinch && uls != 0)
            {
                font.Append(uls is 2 or 0x22 ? "<u val=\"double\"/>" : "<u/>");
            }

            if (icv >= 0 && icv != 0x7FFF)
            {
                font.Append(Color("color", icv));
            }

            p += 118;
        }

        if (atrAlc)
        {
            p += 8;
        }

        if (atrBdr && p + 8 <= d.Length)
        {
            uint b1 = (uint)(d[p] | (d[p + 1] << 8) | (d[p + 2] << 16) | (d[p + 3] << 24));
            uint b2 = (uint)(d[p + 4] | (d[p + 5] << 8) | (d[p + 6] << 16) | (d[p + 7] << 24));
            int[] st = [(int)(b1 & 0xF), (int)((b1 >> 4) & 0xF), (int)((b1 >> 8) & 0xF), (int)((b1 >> 12) & 0xF)];
            int[] co = [(int)((b1 >> 16) & 0x7F), (int)((b1 >> 23) & 0x7F), (int)(b2 & 0x7F), (int)((b2 >> 7) & 0x7F)];
            string[] names = ["left", "right", "top", "bottom"];
            for (int k = 0; k < 4; k++)
            {
                if (st[k] > 0)
                {
                    AppendEdge(border, names[k], st[k], co[k]);
                }
            }

            p += 8;
        }

        if (atrPat && p + 4 <= d.Length)
        {
            uint pat = (uint)(d[p] | (d[p + 1] << 8) | (d[p + 2] << 16) | (d[p + 3] << 24));
            int fls = (int)((pat >> 10) & 0x3F);
            int fore = (int)((pat >> 16) & 0x7F);
            int back = (int)((pat >> 23) & 0x7F);
            if (fls > 0)
            {
                // In a differential format a solid fill keeps its colour in the background slot.
                string name = fls < Patterns.Length ? Patterns[fls] : "solid";
                fill.Append("<fill><patternFill");
                if (fls != 1)
                {
                    fill.Append(" patternType=\"").Append(name).Append('"');
                }

                fill.Append('>');
                if (fls != 1)
                {
                    fill.Append(Color("fgColor", fore));
                }

                fill.Append(Color("bgColor", fls == 1 ? back : back)).Append("</patternFill></fill>");
            }
        }

        var sb = new StringBuilder("<dxf>");
        if (font.Length > 0)
        {
            sb.Append("<font>").Append(font).Append("</font>");
        }

        if (numFmt.Length > 0)
        {
            sb.Append(numFmt);
        }

        sb.Append(fill);
        if (border.Length > 0)
        {
            sb.Append("<border>").Append(border).Append("</border>");
        }

        return sb.Append("</dxf>").ToString();
    }

    // The built-in format texts a differential format may name by identifier.
    private static string BuiltInFormatText(int id) => id switch
    {
        0 => "General",
        1 => "0",
        2 => "0.00",
        3 => "#,##0",
        4 => "#,##0.00",
        9 => "0%",
        10 => "0.00%",
        11 => "0.00E+00",
        12 => "# ?/?",
        13 => "# ??/??",
        14 => "m/d/yyyy",
        15 => "d-mmm-yy",
        16 => "d-mmm",
        17 => "mmm-yy",
        18 => "h:mm AM/PM",
        19 => "h:mm:ss AM/PM",
        20 => "h:mm",
        21 => "h:mm:ss",
        22 => "m/d/yyyy h:mm",
        37 => "#,##0_);(#,##0)",
        38 => "#,##0_);[Red](#,##0)",
        39 => "#,##0.00_);(#,##0.00)",
        40 => "#,##0.00_);[Red](#,##0.00)",
        45 => "mm:ss",
        46 => "[h]:mm:ss",
        47 => "mm:ss.0",
        48 => "##0.0E+0",
        49 => "@",
        _ => "General",
    };
}
