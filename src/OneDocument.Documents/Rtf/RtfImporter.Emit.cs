using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Documents.Rtf;

internal sealed partial class RtfImporter
{
    // ECMA-376 Part 1 §17.3.2 (run properties): children in the order of the schema.
    private string RunProps(St st)
    {
        var sb = new StringBuilder();
        AppendRunProps(sb, st, includeStyle: true);
        return sb.ToString();
    }

    private void AppendRunProps(StringBuilder sb, St st, bool includeStyle)
    {
        if (includeStyle && st.CharStyle is int cs && _styles.TryGetValue(StyleKey("character", cs), out StyleDef? sd))
        {
            sb.Append("<w:rStyle w:val=\"").Append(X.Esc(sd.Id)).Append("\"/>");
        }

        if (st.Font is int f && _fonts.TryGetValue(f, out FontEntry? fe) && fe.Name.Length > 0)
        {
            string name = X.Esc(fe.Name);
            string cname = st.AF is int af && _fonts.TryGetValue(af, out FontEntry? ae) && ae.Name.Length > 0 ? X.Esc(ae.Name) : name;
            sb.Append("<w:rFonts w:ascii=\"").Append(name).Append("\" w:hAnsi=\"").Append(name).Append("\" w:eastAsia=\"").Append(name).Append("\" w:cs=\"").Append(cname).Append("\"/>");
        }

        Toggle(sb, "b", st.B);
        Toggle(sb, "bCs", st.AB ?? st.B);
        Toggle(sb, "i", st.I);
        Toggle(sb, "iCs", st.AI ?? st.I);
        Toggle(sb, "caps", st.Caps);
        Toggle(sb, "smallCaps", st.SmallCaps);
        Toggle(sb, "strike", st.Strike);
        Toggle(sb, "dstrike", st.Dstrike);
        Toggle(sb, "outline", st.Outline);
        Toggle(sb, "shadow", st.Shadow);
        Toggle(sb, "emboss", st.Emboss);
        Toggle(sb, "imprint", st.Imprint);
        Toggle(sb, "vanish", st.Hidden);
        if (st.Cf is int cf)
        {
            sb.Append("<w:color w:val=\"").Append(ColorHex(cf) ?? "auto").Append("\"/>");
        }

        if (st.Expand is int ex && ex != 0)
        {
            sb.Append("<w:spacing w:val=\"").Append(X.N(ex)).Append("\"/>");
        }

        if (st.ScaleX is int sx && sx > 0 && sx != 100)
        {
            sb.Append("<w:w w:val=\"").Append(X.N(Math.Min(sx, 600))).Append("\"/>");
        }

        if (st.Kern is int k && k > 0)
        {
            sb.Append("<w:kern w:val=\"").Append(X.N(k)).Append("\"/>");
        }

        if (st.Position is int pos && pos != 0)
        {
            sb.Append("<w:position w:val=\"").Append(X.N(pos)).Append("\"/>");
        }

        if (st.Fs is int fs && fs > 0)
        {
            sb.Append("<w:sz w:val=\"").Append(X.N(fs)).Append("\"/>");
            sb.Append("<w:szCs w:val=\"").Append(X.N(st.AFs is int afs && afs > 0 ? afs : fs)).Append("\"/>");
        }

        if (st.Highlight is int hl && hl > 0)
        {
            string? hex = ColorHex(hl);
            string? named = hex is null ? null : HighlightName(hex);
            if (named is not null)
            {
                sb.Append("<w:highlight w:val=\"").Append(named).Append("\"/>");
            }
        }

        if (st.Ul is not null)
        {
            sb.Append("<w:u w:val=\"").Append(st.Ul).Append("\"/>");
        }

        string? fill = st.ShadeFill is int sf && sf > 0 ? ColorHex(sf) : null;
        if (fill is null && st.Highlight is int hl2 && hl2 > 0 && ColorHex(hl2) is { } hx && HighlightName(hx) is null)
        {
            fill = hx;
        }

        if (fill is not null)
        {
            sb.Append("<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"").Append(fill).Append("\"/>");
        }

        if (st.Script is int sc && sc != 0)
        {
            sb.Append(sc == 1 ? "<w:vertAlign w:val=\"subscript\"/>" : "<w:vertAlign w:val=\"superscript\"/>");
        }

        if (st.Rtl)
        {
            sb.Append("<w:rtl/>");
        }

        if (st.Lang is int lang && lang > 0 && lang < 65536)
        {
            sb.Append("<w:lang w:val=\"").Append(LangTag(lang)).Append('"');
            if (st.LangFe is int lfe && lfe > 0 && lfe < 65536)
            {
                sb.Append(" w:eastAsia=\"").Append(LangTag(lfe)).Append('"');
            }

            sb.Append("/>");
        }
    }

    private static void Toggle(StringBuilder sb, string name, int? value)
    {
        if (value is int v)
        {
            sb.Append("<w:").Append(name).Append(v == 0 ? " w:val=\"0\"/>" : "/>");
        }
    }

    // ECMA-376 Part 1 §17.18.40 (ST_HighlightColor): the sixteen named colours.
    private static string? HighlightName(string hex) => hex switch
    {
        "000000" => "black",
        "0000FF" => "blue",
        "00FFFF" => "cyan",
        "00FF00" => "green",
        "FF00FF" => "magenta",
        "FF0000" => "red",
        "FFFF00" => "yellow",
        "FFFFFF" => "white",
        "000080" => "darkBlue",
        "008080" => "darkCyan",
        "008000" => "darkGreen",
        "800080" => "darkMagenta",
        "800000" => "darkRed",
        "808000" => "darkYellow",
        "808080" => "darkGray",
        "C0C0C0" => "lightGray",
        _ => null,
    };

    // RTF \langN is a Windows LCID; ST_Lang accepts the language tag. The common ones are named, the rest are written as the four-digit hex ST_LangCode.
    private static string LangTag(int lcid) => lcid switch
    {
        1033 => "en-US", 2057 => "en-GB", 3081 => "en-AU", 4105 => "en-CA", 1031 => "de-DE", 1036 => "fr-FR", 1034 => "es-ES",
        3082 => "es-ES", 1040 => "it-IT", 1043 => "nl-NL", 1046 => "pt-BR", 2070 => "pt-PT", 1049 => "ru-RU", 1041 => "ja-JP",
        1042 => "ko-KR", 2052 => "zh-CN", 1028 => "zh-TW", 1025 => "ar-SA", 1037 => "he-IL", 1053 => "sv-SE", 1044 => "nb-NO",
        1030 => "da-DK", 1035 => "fi-FI", 1045 => "pl-PL", 1029 => "cs-CZ", 1038 => "hu-HU", 1055 => "tr-TR", 1032 => "el-GR",
        1054 => "th-TH", 1081 => "hi-IN", 1024 => "zxx",
        _ => lcid.ToString("X4", CultureInfo.InvariantCulture),
    };

    private string BorderXml(string element, Brdr b)
    {
        string? val = b.Val;
        if (val is null)
        {
            return string.Empty;
        }

        // ST_EighthPointMeasure 2..96; RTF widths are twips (1/20 pt), so eighths are twips * 2 / 5. A double-width style (\brdrth) is thick.
        int sz = val == "nil" ? 0 : Math.Clamp((b.Width * 2 + 2) / 5, 2, 96);
        int space = Math.Clamp(b.Space / 20, 0, 31);
        return "<w:" + element + " w:val=\"" + val + "\" w:sz=\"" + X.N(sz) + "\" w:space=\"" + X.N(space) + "\" w:color=\"" + (ColorHexOrAuto(b.Color)) + "\"/>";
    }

    private string ColorHexOrAuto(int index) => ColorHex(index) ?? "auto";

    // ECMA-376 Part 1 §17.3.1 (paragraph properties): children in schema order.
    private string ParaProps(St st, ParaFmt p, bool forStyle = false)
    {
        var sb = new StringBuilder();
        if (!forStyle && p.Style > 0 && _styles.TryGetValue(StyleKey("paragraph", p.Style), out StyleDef? sd))
        {
            sb.Append("<w:pStyle w:val=\"").Append(X.Esc(sd.Id)).Append("\"/>");
        }

        if (p.KeepNext)
        {
            sb.Append("<w:keepNext/>");
        }

        if (p.KeepLines)
        {
            sb.Append("<w:keepLines/>");
        }

        if (p.PageBreakBefore)
        {
            sb.Append("<w:pageBreakBefore/>");
        }

        if (p.WidowCtl is bool wc)
        {
            sb.Append(wc ? "<w:widowControl/>" : "<w:widowControl w:val=\"0\"/>");
        }

        if (p.Ls > 0 && HasOverride(p.Ls))
        {
            sb.Append("<w:numPr><w:ilvl w:val=\"").Append(X.N(Math.Clamp(p.Ilvl, 0, 8))).Append("\"/><w:numId w:val=\"").Append(X.N(p.Ls)).Append("\"/></w:numPr>");
        }
        else if (p.Ls > 0)
        {
            Defect("rtf.undefined-reference", DefectKind.Reference, DefectSeverity.Dropped, "paragraph uses list override " + X.N(p.Ls) + " which the list override table does not define", 0);
        }

        string borders = BorderXml("top", p.Top) + BorderXml("left", p.Left) + BorderXml("bottom", p.Bottom) + BorderXml("right", p.Right) + BorderXml("between", p.Between) + BorderXml("bar", p.Bar);
        if (borders.Length > 0)
        {
            sb.Append("<w:pBdr>").Append(borders).Append("</w:pBdr>");
        }

        if (p.ShadeFill > 0 && ColorHex(p.ShadeFill) is { } fill)
        {
            sb.Append("<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"").Append(fill).Append("\"/>");
        }

        if (p.Tabs.Count > 0)
        {
            sb.Append("<w:tabs>");
            foreach (TabStop t in p.Tabs)
            {
                sb.Append("<w:tab w:val=\"").Append(t.Align).Append('"');
                if (t.Leader is not null)
                {
                    sb.Append(" w:leader=\"").Append(t.Leader).Append('"');
                }

                sb.Append(" w:pos=\"").Append(X.N(t.Pos)).Append("\"/>");
            }

            sb.Append("</w:tabs>");
        }

        if (p.Rtl)
        {
            sb.Append("<w:bidi/>");
        }

        if (p.Sb is not null || p.Sa is not null || p.Sl is not null || p.SbAuto || p.SaAuto)
        {
            sb.Append("<w:spacing");
            if (p.Sb is int sbv)
            {
                sb.Append(" w:before=\"").Append(X.N(Math.Max(0, sbv))).Append('"');
            }

            if (p.SbAuto)
            {
                sb.Append(" w:beforeAutospacing=\"1\"");
            }

            if (p.Sa is int sav)
            {
                sb.Append(" w:after=\"").Append(X.N(Math.Max(0, sav))).Append('"');
            }

            if (p.SaAuto)
            {
                sb.Append(" w:afterAutospacing=\"1\"");
            }

            if (p.Sl is int sl)
            {
                // RTF 1.9.1 \sl: with \slmult1 the value is in 240ths of a line; otherwise it is twips, at least if positive, exactly if negative.
                if (p.SlMult == 1 || sl == 0)
                {
                    sb.Append(" w:line=\"").Append(X.N(sl == 0 ? 240 : Math.Abs(sl))).Append("\" w:lineRule=\"auto\"");
                }
                else
                {
                    sb.Append(" w:line=\"").Append(X.N(Math.Abs(sl))).Append("\" w:lineRule=\"").Append(sl > 0 ? "atLeast" : "exact").Append('"');
                }
            }

            sb.Append("/>");
        }

        if (p.Li is not null || p.Ri is not null || p.Fi is not null)
        {
            sb.Append("<w:ind");
            if (p.Li is int li)
            {
                sb.Append(" w:left=\"").Append(X.N(li)).Append('"');
            }

            if (p.Ri is int ri)
            {
                sb.Append(" w:right=\"").Append(X.N(ri)).Append('"');
            }

            if (p.Fi is int fi && fi != 0)
            {
                sb.Append(fi > 0 ? " w:firstLine=\"" + X.N(fi) + "\"" : " w:hanging=\"" + X.N(-fi) + "\"");
            }

            sb.Append("/>");
        }

        if (p.ContextSpacing)
        {
            sb.Append("<w:contextualSpacing/>");
        }

        if (p.Jc is not null)
        {
            sb.Append("<w:jc w:val=\"").Append(p.Jc).Append("\"/>");
        }

        if (p.OutlineLevel is >= 0 and <= 9)
        {
            sb.Append("<w:outlineLvl w:val=\"").Append(X.N(p.OutlineLevel)).Append("\"/>");
        }

        return sb.ToString();
    }

    private bool HasOverride(int ls)
    {
        foreach (ListOverrideDef o in _overrides)
        {
            if (o.Ls == ls && _lists.Exists(l => l.Id == o.ListId))
            {
                return true;
            }
        }

        return false;
    }

    // ECMA-376 Part 1 §17.6.17 (sectPr): references first, then the properties in schema order.
    private static string SectPr(SectionDef s)
    {
        var sb = new StringBuilder("<w:sectPr>");
        foreach ((string kind, string type, string rid) in s.Refs)
        {
            sb.Append("<w:").Append(kind).Append("Reference w:type=\"").Append(type).Append("\" r:id=\"").Append(rid).Append("\"/>");
        }

        if (s.BreakType != "nextPage")
        {
            sb.Append("<w:type w:val=\"").Append(s.BreakType).Append("\"/>");
        }

        sb.Append("<w:pgSz w:w=\"").Append(X.N(Math.Max(s.PaperW, 1))).Append("\" w:h=\"").Append(X.N(Math.Max(s.PaperH, 1))).Append('"');
        if (s.Landscape)
        {
            sb.Append(" w:orient=\"landscape\"");
        }

        sb.Append("/><w:pgMar w:top=\"").Append(X.N(s.MarT)).Append("\" w:right=\"").Append(X.N(Math.Max(0, s.MarR))).Append("\" w:bottom=\"").Append(X.N(s.MarB))
            .Append("\" w:left=\"").Append(X.N(Math.Max(0, s.MarL))).Append("\" w:header=\"").Append(X.N(Math.Max(0, s.HeaderY))).Append("\" w:footer=\"").Append(X.N(Math.Max(0, s.FooterY)))
            .Append("\" w:gutter=\"").Append(X.N(Math.Max(0, s.Gutter))).Append("\"/>");
        if (s.PgnFmt is not null || s.PgnStart >= 0)
        {
            sb.Append("<w:pgNumType");
            if (s.PgnFmt is not null)
            {
                sb.Append(" w:fmt=\"").Append(s.PgnFmt).Append('"');
            }

            if (s.PgnStart >= 0)
            {
                sb.Append(" w:start=\"").Append(X.N(s.PgnStart)).Append('"');
            }

            sb.Append("/>");
        }

        if (s.Cols > 1)
        {
            sb.Append("<w:cols w:num=\"").Append(X.N(s.Cols)).Append("\" w:space=\"").Append(X.N(s.ColSpace)).Append('"');
            if (s.LineBetween)
            {
                sb.Append(" w:sep=\"1\"");
            }

            sb.Append("/>");
        }

        if (s.TitlePg)
        {
            sb.Append("<w:titlePg/>");
        }

        if (s.Rtl)
        {
            sb.Append("<w:bidi/>");
        }

        return sb.Append("</w:sectPr>").ToString();
    }

    // ---- tables ------------------------------------------------------------------------------------------------------------------

    // ECMA-376 Part 1 §17.4 (tables). Cells sit on a grid made of every cell edge any row uses; a cell that covers several grid columns has
    // gridSpan, and a row that does not start or end at the table's edges has gridBefore/gridAfter.
    private string TableXml(TableFrame t)
    {
        var rows = new List<(RowFrame Row, RowDef Def, List<(int Left, int Right)> Spans)>();
        var edges = new SortedSet<int>();
        foreach (RowFrame row in t.Rows)
        {
            RowDef def = row.Def ?? DefaultRow(row.Cells.Count);
            if (def.Cells.Count == 0)
            {
                def = DefaultRow(row.Cells.Count, def);
            }

            var spans = new List<(int, int)>();
            int left = def.Left;
            edges.Add(left);
            for (int k = 0; k < row.Cells.Count; k++)
            {
                CellDef? cd = k < def.Cells.Count ? def.Cells[k] : null;
                int right = cd is not null && cd.RightX > left ? cd.RightX : left + 1440;
                spans.Add((left, right));
                edges.Add(right);
                left = right;
            }

            rows.Add((row, def, spans));
        }

        int[] e = [.. edges];
        if (e.Length < 2)
        {
            e = [e.Length == 1 ? e[0] : 0, (e.Length == 1 ? e[0] : 0) + 1440];
        }

        RowDef first = rows[0].Def;
        var sb = new StringBuilder("<w:tbl><w:tblPr>");
        sb.Append("<w:tblW w:w=\"").Append(X.N(e[^1] - e[0])).Append("\" w:type=\"dxa\"/>");
        if (first.Jc != 0)
        {
            sb.Append(first.Jc == 1 ? "<w:jc w:val=\"center\"/>" : "<w:jc w:val=\"right\"/>");
        }

        // RTF's \trleft is the edge of the first cell; the text starts one cell padding (\trgaph) further in, which is where tblInd measures to.
        sb.Append("<w:tblInd w:w=\"").Append(X.N(first.Left + first.Gaph)).Append("\" w:type=\"dxa\"/>");
        string tb = BorderXml("top", first.Top) + BorderXml("left", first.Left_) + BorderXml("bottom", first.Bottom) + BorderXml("right", first.Right) +
            BorderXml("insideH", first.Horiz) + BorderXml("insideV", first.Vert);
        if (tb.Length > 0)
        {
            sb.Append("<w:tblBorders>").Append(tb).Append("</w:tblBorders>");
        }

        if (first.ShadeFill > 0 && ColorHex(first.ShadeFill) is { } tfill)
        {
            sb.Append("<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"").Append(tfill).Append("\"/>");
        }

        sb.Append(first.AutoFit ? "<w:tblLayout w:type=\"autofit\"/>" : "<w:tblLayout w:type=\"fixed\"/>");
        sb.Append("<w:tblCellMar>");
        if (first.PadT >= 0)
        {
            sb.Append("<w:top w:w=\"").Append(X.N(first.PadT)).Append("\" w:type=\"dxa\"/>");
        }

        sb.Append("<w:left w:w=\"").Append(X.N(first.PadL >= 0 ? first.PadL : first.Gaph)).Append("\" w:type=\"dxa\"/>");
        if (first.PadB >= 0)
        {
            sb.Append("<w:bottom w:w=\"").Append(X.N(first.PadB)).Append("\" w:type=\"dxa\"/>");
        }

        sb.Append("<w:right w:w=\"").Append(X.N(first.PadR >= 0 ? first.PadR : first.Gaph)).Append("\" w:type=\"dxa\"/></w:tblCellMar>");
        sb.Append("</w:tblPr><w:tblGrid>");
        for (int i = 0; i + 1 < e.Length; i++)
        {
            sb.Append("<w:gridCol w:w=\"").Append(X.N(e[i + 1] - e[i])).Append("\"/>");
        }

        sb.Append("</w:tblGrid>");
        int totalCols = e.Length - 1;
        foreach ((RowFrame row, RowDef def, List<(int Left, int Right)> spans) in rows)
        {
            sb.Append("<w:tr>");
            int startCol = spans.Count > 0 ? Array.IndexOf(e, spans[0].Left) : 0;
            int endCol = spans.Count > 0 ? Array.IndexOf(e, spans[^1].Right) : totalCols;
            var trPr = new StringBuilder();
            if (startCol > 0)
            {
                trPr.Append("<w:gridBefore w:val=\"").Append(X.N(startCol)).Append("\"/>");
            }

            if (endCol < totalCols)
            {
                trPr.Append("<w:gridAfter w:val=\"").Append(X.N(totalCols - endCol)).Append("\"/>");
            }

            if (def.CantSplit)
            {
                trPr.Append("<w:cantSplit/>");
            }

            if (def.Height != 0)
            {
                trPr.Append("<w:trHeight w:val=\"").Append(X.N(Math.Abs(def.Height))).Append("\" w:hRule=\"").Append(def.Height < 0 ? "exact" : "atLeast").Append("\"/>");
            }

            if (def.Header)
            {
                trPr.Append("<w:tblHeader/>");
            }

            if (trPr.Length > 0)
            {
                sb.Append("<w:trPr>").Append(trPr).Append("</w:trPr>");
            }

            for (int k = 0; k < row.Cells.Count; k++)
            {
                CellDef? cd = k < def.Cells.Count ? def.Cells[k] : null;
                if (cd is { HMerge: 2 })
                {
                    continue; // covered by the cell to its left
                }

                int left = spans[k].Left;
                int right = spans[k].Right;
                if (cd is { HMerge: 1 })
                {
                    for (int m = k + 1; m < row.Cells.Count && m < def.Cells.Count && def.Cells[m].HMerge == 2; m++)
                    {
                        right = spans[m].Right;
                    }
                }

                int span = Array.IndexOf(e, right) - Array.IndexOf(e, left);
                sb.Append("<w:tc><w:tcPr><w:tcW w:w=\"").Append(X.N(right - left)).Append("\" w:type=\"dxa\"/>");
                if (span > 1)
                {
                    sb.Append("<w:gridSpan w:val=\"").Append(X.N(span)).Append("\"/>");
                }

                if (cd is not null)
                {
                    if (cd.VMerge == 1)
                    {
                        sb.Append("<w:vMerge w:val=\"restart\"/>");
                    }
                    else if (cd.VMerge == 2)
                    {
                        sb.Append("<w:vMerge/>");
                    }

                    string cb = BorderXml("top", cd.Top) + BorderXml("left", cd.Left) + BorderXml("bottom", cd.Bottom) + BorderXml("right", cd.Right);
                    if (cb.Length > 0)
                    {
                        sb.Append("<w:tcBorders>").Append(cb).Append("</w:tcBorders>");
                    }

                    if (cd.ShadeFill > 0 && ColorHex(cd.ShadeFill) is { } cfill)
                    {
                        sb.Append("<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"").Append(cfill).Append("\"/>");
                    }

                    if (cd.NoWrap)
                    {
                        sb.Append("<w:noWrap/>");
                    }

                    if (cd.Direction is not null)
                    {
                        sb.Append("<w:textDirection w:val=\"").Append(cd.Direction).Append("\"/>");
                    }

                    if (cd.VAlign is not null)
                    {
                        sb.Append("<w:vAlign w:val=\"").Append(cd.VAlign).Append("\"/>");
                    }
                }

                sb.Append("</w:tcPr>");
                List<Block> blocks = row.Cells[k].Blocks;
                foreach (Block b in blocks)
                {
                    sb.Append(b.ToXml());
                }

                if (blocks.Count == 0 || blocks[^1].IsTable)
                {
                    sb.Append("<w:p/>");
                }

                sb.Append("</w:tc>");
            }

            sb.Append("</w:tr>");
        }

        return sb.Append("</w:tbl>").ToString();
    }

    // A row without its own definition (or without cell edges) gets equal cells across a 6-inch text width.
    private static RowDef DefaultRow(int cells, RowDef? basis = null)
    {
        RowDef def = basis ?? new RowDef();
        int n = Math.Max(1, cells);
        def.Cells = [];
        for (int i = 1; i <= n; i++)
        {
            def.Cells.Add(new CellDef { RightX = def.Left + (8640 * i / n) });
        }

        return def;
    }
}
