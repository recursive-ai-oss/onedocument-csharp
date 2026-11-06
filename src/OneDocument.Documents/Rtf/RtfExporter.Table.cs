using System.Globalization;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Documents.Rtf;

internal sealed partial class RtfExporter
{
    // ---- tables (ECMA-376 Part 1 §17.4 -> RTF 1.9.1 "Table Definitions") ------------------------------------------------------------

    private sealed class CellInfo
    {
        public MarkupNode Node;
        public int Span = 1;
        public int StartCol;
        public string? VMerge; // restart, continue
    }

    private void Table(MarkupNode tbl, int depth)
    {
        var grid = new List<int>();
        MarkupNode tg = Child(tbl, "tblGrid");
        if (!tg.IsNull)
        {
            foreach (MarkupNode gc in tg.ChildElements)
            {
                if (IsW(gc, "gridCol"))
                {
                    grid.Add(Int(A(gc, "w"), 1440));
                }
            }
        }

        MarkupNode tblPr = Child(tbl, "tblPr");
        int gaph = 108;
        int indent = 0;
        string jc = "left";
        MarkupNode borders = default;
        MarkupNode tblShd = default;
        bool fixedLayout = false;
        if (!tblPr.IsNull)
        {
            MarkupNode mar = Child(tblPr, "tblCellMar");
            if (!mar.IsNull)
            {
                MarkupNode l = Child(mar, "left");
                if (l.IsNull)
                {
                    l = Child(mar, "start");
                }

                if (!l.IsNull)
                {
                    gaph = Int(A(l, "w"), 108);
                }
            }

            MarkupNode ind = Child(tblPr, "tblInd");
            if (!ind.IsNull)
            {
                indent = Int(A(ind, "w"));
            }

            MarkupNode j = Child(tblPr, "jc");
            if (!j.IsNull)
            {
                jc = A(j, "val") ?? "left";
            }

            borders = Child(tblPr, "tblBorders");
            tblShd = Child(tblPr, "shd");
            MarkupNode lay = Child(tblPr, "tblLayout");
            fixedLayout = !lay.IsNull && A(lay, "type") == "fixed";
        }

        _ = fixedLayout;
        var rows = new List<MarkupNode>();
        foreach (MarkupNode r in tbl.ChildElements)
        {
            if (IsW(r, "tr"))
            {
                rows.Add(r);
            }
            else if (IsW(r, "sdt") && Child(r, "sdtContent") is { IsNull: false } sc)
            {
                foreach (MarkupNode rr in sc.ChildElements)
                {
                    if (IsW(rr, "tr"))
                    {
                        rows.Add(rr);
                    }
                }
            }
        }

        foreach (MarkupNode row in rows)
        {
            _ct.ThrowIfCancellationRequested();
            TableRow(row, grid, gaph, indent, jc, borders, tblShd, depth);
        }
    }

    private void TableRow(MarkupNode row, List<int> grid, int gaph, int indent, string jc, MarkupNode borders, MarkupNode tblShd, int depth)
    {
        MarkupNode trPr = Child(row, "trPr");
        int before = 0, after = 0;
        bool header = false, cantSplit = false;
        int height = 0;
        string? hRule = null;
        if (!trPr.IsNull)
        {
            foreach (MarkupNode e in trPr.ChildElements)
            {
                switch (e.LocalNameString)
                {
                    case "gridBefore": before = Int(A(e, "val")); break;
                    case "gridAfter": after = Int(A(e, "val")); break;
                    case "tblHeader": header = On(e); break;
                    case "cantSplit": cantSplit = On(e); break;
                    case "trHeight":
                        height = Int(A(e, "val"));
                        hRule = A(e, "hRule");
                        break;
                }
            }
        }

        var cells = new List<CellInfo>();
        int col = before;
        foreach (MarkupNode c in row.ChildElements)
        {
            if (!IsW(c, "tc"))
            {
                continue;
            }

            MarkupNode tcPr = Child(c, "tcPr");
            int span = 1;
            string? vm = null;
            if (!tcPr.IsNull)
            {
                MarkupNode gs = Child(tcPr, "gridSpan");
                span = gs.IsNull ? 1 : Math.Max(1, Int(A(gs, "val"), 1));
                MarkupNode v = Child(tcPr, "vMerge");
                if (!v.IsNull)
                {
                    vm = A(v, "val") == "restart" ? "restart" : "continue";
                }
            }

            cells.Add(new CellInfo { Node = c, Span = span, StartCol = col, VMerge = vm });
            col += span;
        }

        int ColWidth(int index) => index < grid.Count ? grid[index] : 1440;
        int left = indent - gaph;
        for (int i = 0; i < before; i++)
        {
            left += ColWidth(i);
        }

        void Definition()
        {
            _w.Word("trowd");
            _w.Word("trgaph", gaph);
            _w.Word("trleft", left);
            if (jc == "center")
            {
                _w.Word("trqc");
            }
            else if (jc is "right" or "end")
            {
                _w.Word("trqr");
            }

            if (height != 0)
            {
                _w.Word("trrh", hRule == "exact" ? -height : height);
            }

            if (header)
            {
                _w.Word("trhdr");
            }

            if (cantSplit)
            {
                _w.Word("trkeep");
            }

            if (!borders.IsNull)
            {
                foreach (MarkupNode b in borders.ChildElements)
                {
                    switch (b.LocalNameString)
                    {
                        case "top": BorderWords("trbrdrt", b); break;
                        case "left" or "start": BorderWords("trbrdrl", b); break;
                        case "bottom": BorderWords("trbrdrb", b); break;
                        case "right" or "end": BorderWords("trbrdrr", b); break;
                        case "insideH": BorderWords("trbrdrh", b); break;
                        case "insideV": BorderWords("trbrdrv", b); break;
                    }
                }
            }

            if (!tblShd.IsNull && A(tblShd, "fill") is { } tf && tf != "auto")
            {
                _w.Word("trcbpat", ColorIndex(tf));
            }

            int x = left;
            foreach (CellInfo ci in cells)
            {
                MarkupNode tcPr = Child(ci.Node, "tcPr");
                for (int k = 0; k < ci.Span; k++)
                {
                    if (k == 0 && ci.Span > 1)
                    {
                        _w.Word("clmgf");
                    }
                    else if (k > 0)
                    {
                        _w.Word("clmrg");
                    }

                    if (k == 0)
                    {
                        CellWords(ci, tcPr);
                    }
                    else if (ci.VMerge == "continue")
                    {
                        _w.Word("clvmrg");
                    }
                    else if (ci.VMerge == "restart")
                    {
                        _w.Word("clvmgf");
                    }

                    x += ColWidth(ci.StartCol + k);
                    _w.Word("cellx", x);
                }
            }

            _w.Newline();
        }

        // A top-level row's definition comes first; a nested row's comes after its cells, in {\*\nesttableprops}.
        if (depth == 1)
        {
            Definition();
        }

        for (int i = 0; i < cells.Count; i++)
        {
            CellInfo ci = cells[i];
            Blocks(ci.Node, depth, true);
            for (int k = 1; k < ci.Span; k++)
            {
                _w.Word("pard");
                _w.Word("plain");
                _w.Word("intbl");
                if (depth > 1)
                {
                    _w.Word("itap", depth);
                }

                _w.Word(depth > 1 ? "nestcell" : "cell");
            }
        }

        _ = after;
        if (depth == 1)
        {
            _w.Word("row");
        }
        else
        {
            _w.StartDestination("nesttableprops");
            Definition();
            _w.Word("nestrow");
            _w.EndGroup();
            _w.StartGroup();
            _w.Word("nonesttables");
            _w.Word("par");
            _w.EndGroup();
        }

        _w.Newline();
    }

    private void CellWords(CellInfo ci, MarkupNode tcPr)
    {
        if (ci.VMerge == "restart")
        {
            _w.Word("clvmgf");
        }
        else if (ci.VMerge == "continue")
        {
            _w.Word("clvmrg");
        }

        if (tcPr.IsNull)
        {
            return;
        }

        MarkupNode va = Child(tcPr, "vAlign");
        if (!va.IsNull)
        {
            _w.Word(A(va, "val") switch { "center" => "clvertalc", "bottom" => "clvertalb", _ => "clvertalt" });
        }

        MarkupNode b = Child(tcPr, "tcBorders");
        if (!b.IsNull)
        {
            foreach (MarkupNode e in b.ChildElements)
            {
                switch (e.LocalNameString)
                {
                    case "top": BorderWords("clbrdrt", e); break;
                    case "left" or "start": BorderWords("clbrdrl", e); break;
                    case "bottom": BorderWords("clbrdrb", e); break;
                    case "right" or "end": BorderWords("clbrdrr", e); break;
                }
            }
        }

        MarkupNode shd = Child(tcPr, "shd");
        if (!shd.IsNull && A(shd, "fill") is { } fill && fill != "auto")
        {
            _w.Word("clcbpat", ColorIndex(fill));
        }

        if (!Child(tcPr, "noWrap").IsNull)
        {
            _w.Word("clNoWrap");
        }

        MarkupNode td = Child(tcPr, "textDirection");
        if (!td.IsNull)
        {
            _w.Word(A(td, "val") switch { "tbRl" => "cltxtbrl", "btLr" => "cltxbtlr", _ => "cltxlrtb" });
        }
    }

    // ---- pictures (RTF 1.9.1 "Pictures") ------------------------------------------------------------------------------------------

    private void Drawing(MarkupNode node)
    {
        string? embed = null;
        long cx = 0, cy = 0;
        string? descr = null;
        bool anchored = false, shape = false, other = false;
        foreach (MarkupNode d in Descendants(node))
        {
            switch (d.LocalNameString)
            {
                case "blip":
                    embed ??= RA(d, "embed") ?? RA(d, "link");
                    break;
                case "imagedata":
                    embed ??= RA(d, "id");
                    break;
                case "extent":
                    if (cx == 0)
                    {
                        _ = long.TryParse(d.GetAttribute("cx"), NumberStyles.Integer, CultureInfo.InvariantCulture, out cx);
                        _ = long.TryParse(d.GetAttribute("cy"), NumberStyles.Integer, CultureInfo.InvariantCulture, out cy);
                    }

                    break;
                case "docPr":
                    descr ??= d.GetAttribute("descr");
                    break;
                case "anchor":
                    anchored = true;
                    break;
                case "txbxContent" or "wsp" or "chart" or "relIds" or "sp" or "shape" or "rect" or "oval" or "group" or "wgp":
                    shape = true;
                    break;
                case "OLEObject":
                    other = true;
                    break;
            }
        }

        if (other)
        {
            _loss.Report("loss.rtf-ole-object", "embedded objects", 1, "Embedded objects are written as their preview picture only.");
        }

        if (embed is null)
        {
            if (shape)
            {
                _loss.Report("loss.rtf-shape", "shapes, text boxes and charts", 1, "Drawing shapes, text boxes, charts and diagrams are not written.");
            }

            return;
        }

        if (anchored)
        {
            _loss.Report("loss.rtf-floating-picture", "floating pictures", 1, "Floating pictures are written inline: position and text wrapping are not carried.");
        }

        IPackagePart? part = Target(embed);
        if (part is null)
        {
            _loss.Report("loss.rtf-picture", "pictures", 1, "A picture whose data is linked or missing is not written.");
            return;
        }

        byte[] data = part.GetBytes().ToArray();
        string kind;
        int skip = 0;
        if (data.Length > 8 && data[0] == 0x89 && data[1] == 'P')
        {
            kind = "pngblip";
        }
        else if (data.Length > 4 && data[0] == 0xFF && data[1] == 0xD8)
        {
            kind = "jpegblip";
        }
        else if (data.Length > 44 && data[40] == ' ' && data[41] == 'E' && data[42] == 'M' && data[43] == 'F')
        {
            kind = "emfblip";
        }
        else if (data.Length > 22 && data[0] == 0xD7 && data[1] == 0xCD && data[2] == 0xC6 && data[3] == 0x9A)
        {
            kind = "wmetafile8";
            skip = 22; // RTF holds a bare metafile; its size is in \picw and \pich
        }
        else if (data.Length > 54 && data[0] == 'B' && data[1] == 'M')
        {
            kind = "dibitmap0";
            skip = 14; // a DIB is a .bmp without the file header
        }
        else
        {
            _loss.Report("loss.rtf-picture-format", "pictures in formats RTF cannot hold", 1, "Pictures other than PNG, JPEG, EMF, WMF and BMP are not written.");
            return;
        }

        if (cx <= 0 || cy <= 0)
        {
            cx = cy = 914400;
        }

        int wGoal = (int)Math.Min(cx / 635, 31680 * 4);
        int hGoal = (int)Math.Min(cy / 635, 31680 * 4);
        bool metafile = kind is "emfblip" or "wmetafile8";
        int pw = metafile ? (int)Math.Min(cx / 360, int.MaxValue) : wGoal / 15;
        int ph = metafile ? (int)Math.Min(cy / 360, int.MaxValue) : hGoal / 15;
        _w.StartGroup();
        _w.Word("pict");
        if (descr is { Length: > 0 })
        {
            _w.StartDestination("picprop");
            _w.StartGroup();
            _w.Word("sp");
            _w.StartGroup();
            _w.Word("sn");
            _w.Text("wzDescription");
            _w.EndGroup();
            _w.StartGroup();
            _w.Word("sv");
            _w.Text(descr);
            _w.EndGroup();
            _w.EndGroup();
            _w.EndGroup();
        }

        _w.Word(kind);
        _w.Word("picw", Math.Max(1, pw));
        _w.Word("pich", Math.Max(1, ph));
        _w.Word("picwgoal", Math.Max(1, wGoal));
        _w.Word("pichgoal", Math.Max(1, hGoal));
        _w.Newline();
        _w.HexData(data.AsSpan(skip));
        _w.EndGroup();
    }
}
