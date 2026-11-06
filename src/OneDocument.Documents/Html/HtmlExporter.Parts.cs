using System.Linq;
using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Documents.Html;

internal sealed partial class HtmlExporter
{
    // ---- styles ----------------------------------------------------------------------------------------------------------------

    private void IndexStyles()
    {
        if (_styles.IsNull)
        {
            return;
        }

        foreach (MarkupNode s in _styles.ChildElements)
        {
            if (!IsW(s, "style") || A(s, "styleId") is not { } id)
            {
                continue;
            }

            _styleById[id] = s;
            if (A(s, "type") == "paragraph" && A(s, "default") is "1" or "true")
            {
                _defaultParagraphStyle = id;
            }
        }
    }

    private static string ClassName(string styleId)
    {
        var sb = new StringBuilder("s-");
        foreach (char c in styleId)
        {
            sb.Append(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_');
        }

        return sb.ToString();
    }

    // A style based on another lists both classes, the base first, so the style sheet cascades the way the styles inherit.
    private string ClassList(string styleId)
    {
        var chain = new List<string>();
        string? id = styleId;
        for (int i = 0; i < 24 && id is not null && _styleById.TryGetValue(id, out MarkupNode s); i++)
        {
            if (chain.Contains(id))
            {
                break;
            }

            chain.Add(id);
            id = A(Child(s, "basedOn"), "val");
        }

        var sb = new StringBuilder();
        for (int i = chain.Count - 1; i >= 0; i--)
        {
            if (chain[i] == _defaultParagraphStyle)
            {
                continue;
            }

            if (sb.Length > 0)
            {
                sb.Append(' ');
            }

            sb.Append(ClassName(chain[i]));
            if (_usedSet.Add(chain[i]))
            {
                _usedStyles.Add(chain[i]);
            }
        }

        return sb.ToString();
    }

    private int HeadingLevel(string styleId)
    {
        string? id = styleId;
        for (int i = 0; i < 24 && id is not null && _styleById.TryGetValue(id, out MarkupNode s); i++)
        {
            string name = A(Child(s, "name"), "val") ?? string.Empty;
            if (name.StartsWith("heading ", StringComparison.OrdinalIgnoreCase) && name.Length == 9 && char.IsAsciiDigit(name[8]) && name[8] != '0')
            {
                return name[8] - '0';
            }

            id = A(Child(s, "basedOn"), "val");
        }

        return 0;
    }

    private string Css(MarkupNode body)
    {
        var sb = new StringBuilder();
        var bodyDecls = new List<string>();
        var paraDecls = new List<string>();
        MarkupNode dd = Child(_styles, "docDefaults");
        RunDecls(Child(Child(dd, "rPrDefault"), "rPr"), bodyDecls, includeToggles: true);
        MarkupNode normal = _styleById.TryGetValue(_defaultParagraphStyle, out MarkupNode ns) ? ns : default;
        RunDecls(Child(normal, "rPr"), bodyDecls, includeToggles: true);
        ParagraphDecls(Child(Child(dd, "pPrDefault"), "pPr"), paraDecls, isList: false, rtl: false);
        ParagraphDecls(Child(normal, "pPr"), paraDecls, isList: false, rtl: false);
        MarkupNode sect = Sections(body).LastOrDefault();
        MarkupNode size = Child(sect, "pgSz");
        MarkupNode mar = Child(sect, "pgMar");
        if (!size.IsNull && !mar.IsNull)
        {
            int width = Int(A(size, "w"), 12240) - Int(A(mar, "left"), 1440) - Int(A(mar, "right"), 1440);
            if (width > 1440)
            {
                bodyDecls.Add("max-width:" + Pt(width));
                bodyDecls.Add("margin:1em auto");
            }
        }

        sb.Append("body{").Append(string.Join(';', bodyDecls)).Append("}\n");
        bool hasSpacing = paraDecls.Any(static x => x.StartsWith("margin-", StringComparison.Ordinal));
        sb.Append("p,h1,h2,h3,h4,h5,h6,li{").Append(hasSpacing ? string.Empty : "margin:0;").Append("white-space:pre-wrap");
        foreach (string x in paraDecls)
        {
            sb.Append(';').Append(x);
        }

        sb.Append("}\nh1,h2,h3,h4,h5,h6{font-size:inherit;font-weight:normal}\n");
        sb.Append("ul,ol{margin:0;padding-left:36pt}\n");
        sb.Append("table{border-collapse:collapse}\ntd,th{padding:0 5.4pt;vertical-align:top;font-weight:inherit;text-align:inherit}\n");
        if (_usesNotes)
        {
            sb.Append("body{counter-reset:note}\n.noteref{counter-increment:note}\n.noteref a::after{content:counter(note)}\n.footnotes{border-top:1px solid #888;margin-top:1em}\n");
            sb.Append("a[role=doc-backlink]::after{content:\"\\21A9\"}\n");
        }

        // Styles are written in the order they were first used; a base always comes before the style that lists it second.
        foreach (string id in _usedStyles)
        {
            if (!_styleById.TryGetValue(id, out MarkupNode s))
            {
                continue;
            }

            var d = new List<string>();
            ParagraphDecls(Child(s, "pPr"), d, isList: false, rtl: false);
            RunDecls(Child(s, "rPr"), d, includeToggles: true);
            if (d.Count > 0)
            {
                sb.Append('.').Append(ClassName(id)).Append('{').Append(string.Join(';', d)).Append("}\n");
            }
        }

        return sb.ToString();
    }

    // ---- lists -----------------------------------------------------------------------------------------------------------------

    private sealed class OpenList(int numId, int level)
    {
        public int NumId { get; } = numId;

        public int Level { get; } = level;

        public bool LiOpen { get; set; }

        public bool Ordered { get; set; }
    }

    private List<OpenList> _lists = [];
    private readonly Dictionary<(int NumId, int Level), int> _counters = [];

    private (int NumId, int Level) NumberingOf(MarkupNode ppr, string styleId)
    {
        if (_numbering.IsNull)
        {
            return (0, 0);
        }

        MarkupNode np = Child(ppr, "numPr");
        int numId = np.IsNull ? -1 : Int(A(Child(np, "numId"), "val"), -1);
        int ilvl = np.IsNull ? -1 : Int(A(Child(np, "ilvl"), "val"), -1);
        string? id = styleId;
        for (int i = 0; i < 24 && (numId < 0 || ilvl < 0) && id is not null && _styleById.TryGetValue(id, out MarkupNode s); i++)
        {
            MarkupNode sn = Child(Child(s, "pPr"), "numPr");
            if (!sn.IsNull)
            {
                if (numId < 0)
                {
                    numId = Int(A(Child(sn, "numId"), "val"), -1);
                }

                if (ilvl < 0)
                {
                    ilvl = Int(A(Child(sn, "ilvl"), "val"), -1);
                }
            }

            id = A(Child(s, "basedOn"), "val");
        }

        if (numId <= 0 || FindNum(numId).IsNull)
        {
            return (0, 0);
        }

        return (numId, Math.Clamp(ilvl < 0 ? 0 : ilvl, 0, 8));
    }

    private MarkupNode FindNum(int numId)
    {
        foreach (MarkupNode n in _numbering.ChildElements)
        {
            if (IsW(n, "num") && Int(A(n, "numId"), -1) == numId)
            {
                return n;
            }
        }

        return default;
    }

    // The level definition a (numId, level) pair resolves to: the num's own level override, else the abstract numbering's level.
    private (string Format, int Start, string Text) LevelOf(int numId, int level)
    {
        MarkupNode num = FindNum(numId);
        int abstractId = Int(A(Child(num, "abstractNumId"), "val"), -1);
        MarkupNode lvl = default;
        int startOverride = -1;
        foreach (MarkupNode o in num.ChildElements)
        {
            if (IsW(o, "lvlOverride") && Int(A(o, "ilvl"), -1) == level)
            {
                if (Child(o, "startOverride") is { IsNull: false } so)
                {
                    startOverride = Int(A(so, "val"), 1);
                }

                if (Child(o, "lvl") is { IsNull: false } ol)
                {
                    lvl = ol;
                }
            }
        }

        if (lvl.IsNull)
        {
            foreach (MarkupNode an in _numbering.ChildElements)
            {
                if (IsW(an, "abstractNum") && Int(A(an, "abstractNumId"), -2) == abstractId)
                {
                    foreach (MarkupNode l in an.ChildElements)
                    {
                        if (IsW(l, "lvl") && Int(A(l, "ilvl"), -1) == level)
                        {
                            lvl = l;
                        }
                    }
                }
            }
        }

        string fmt = A(Child(lvl, "numFmt"), "val") ?? "decimal";
        int start = startOverride >= 0 ? startOverride : Int(A(Child(lvl, "start"), "val"), 1);
        return (fmt, start, A(Child(lvl, "lvlText"), "val") ?? string.Empty);
    }

    private void ListTransition(int numId, int level)
    {
        while (_lists.Count > 0 && (_lists[^1].Level > level || _lists[^1].Level == level && _lists[^1].NumId != numId))
        {
            CloseTop();
        }

        if (_lists.Count > 0 && _lists[^1].Level == level)
        {
            if (_lists[^1].LiOpen)
            {
                _b.Append("</li>");
                _lists[^1].LiOpen = false;
            }
        }
        else
        {
            int from = _lists.Count > 0 ? _lists[^1].Level + 1 : 0;
            for (int l = from; l <= level; l++)
            {
                OpenListLevel(numId, l);
                if (l < level)
                {
                    _b.Append("<li style=\"list-style:none\">");
                    _lists[^1].LiOpen = true;
                }
            }
        }

        _lists[^1].LiOpen = true;
        _counters[(numId, level)] = _counters.GetValueOrDefault((numId, level)) + 1;
        for (int deeper = level + 1; deeper <= 8; deeper++)
        {
            _counters.Remove((numId, deeper));
        }
    }

    private void OpenListLevel(int numId, int level)
    {
        (string fmt, int start, string text) = LevelOf(numId, level);
        bool ordered = fmt is not ("bullet" or "none");
        var entry = new OpenList(numId, level) { Ordered = ordered };
        int seen = _counters.GetValueOrDefault((numId, level));
        if (ordered)
        {
            string? type = fmt switch { "lowerLetter" => "a", "upperLetter" => "A", "lowerRoman" => "i", "upperRoman" => "I", _ => null };
            _b.Append("<ol");
            int s = start + seen;
            if (s != 1)
            {
                _b.Append(" start=\"").Append(s.ToString(CultureInfo.InvariantCulture)).Append('"');
            }

            if (type is not null)
            {
                _b.Append(" type=\"").Append(type).Append('"');
            }

            _b.Append('>');
        }
        else
        {
            string style = fmt == "none" ? "list-style:none" : text switch
            {
                "o" => "list-style-type:circle",
                "\uF0A7" or "\u00A7" or "\u25AA" or "\u25A0" => "list-style-type:square",
                _ => string.Empty,
            };
            _b.Append("<ul").Append(style.Length > 0 ? " style=\"" + style + "\"" : string.Empty).Append('>');
        }

        _lists.Add(entry);
    }

    private void CloseTop()
    {
        OpenList top = _lists[^1];
        if (top.LiOpen)
        {
            _b.Append("</li>");
        }

        _b.Append(top.Ordered ? "</ol>" : "</ul>");
        _lists.RemoveAt(_lists.Count - 1);
    }

    private void CloseLists()
    {
        while (_lists.Count > 0)
        {
            CloseTop();
        }
    }

    // ---- tables ----------------------------------------------------------------------------------------------------------------

    private sealed class CellInfo
    {
        public MarkupNode Node;
        public int Col;
        public int Span = 1;
        public bool Continue;
        public bool Restart;
        public int RowSpan = 1;
    }

    private MarkupNode StyleTableBorders(string? styleId)
    {
        string? id = styleId;
        for (int i = 0; i < 24 && id is not null && _styleById.TryGetValue(id, out MarkupNode s); i++)
        {
            MarkupNode b = Child(Child(s, "tblPr"), "tblBorders");
            if (!b.IsNull)
            {
                return b;
            }

            id = A(Child(s, "basedOn"), "val");
        }

        return default;
    }

    private void Table(MarkupNode tbl)
    {
        MarkupNode tpr = Child(tbl, "tblPr");
        MarkupNode grid = Child(tbl, "tblGrid");
        var gridCols = new List<int>();
        foreach (MarkupNode g in grid.ChildElements)
        {
            if (IsW(g, "gridCol"))
            {
                gridCols.Add(Int(A(g, "w")));
            }
        }

        MarkupNode direct = Child(tpr, "tblBorders");
        MarkupNode fromStyle = StyleTableBorders(A(Child(tpr, "tblStyle"), "val"));
        string? Side(string name)
        {
            string alt = name == "left" ? "start" : name == "right" ? "end" : name;
            MarkupNode n = Child(direct, name);
            if (n.IsNull)
            {
                n = Child(direct, alt);
            }

            if (n.IsNull)
            {
                n = Child(fromStyle, name);
            }

            if (n.IsNull)
            {
                n = Child(fromStyle, alt);
            }

            return BorderCss(n);
        }

        string?[] tb = [Side("top"), Side("left"), Side("bottom"), Side("right"), Side("insideH"), Side("insideV")];

        // Rows and their cells on the grid.
        var rows = new List<(MarkupNode Row, List<CellInfo> Cells, bool Header)>();
        foreach (MarkupNode row in tbl.ChildElements)
        {
            if (!IsW(row, "tr"))
            {
                continue;
            }

            MarkupNode trpr = Child(row, "trPr");
            var cells = new List<CellInfo>();
            int col = Int(A(Child(trpr, "gridBefore"), "val"));
            foreach (MarkupNode tc in row.ChildElements)
            {
                MarkupNode cn = tc;
                if (IsW(tc, "sdt"))
                {
                    cn = Child(tc, "sdtContent");
                    foreach (MarkupNode inner in cn.ChildElements)
                    {
                        if (IsW(inner, "tc"))
                        {
                            cn = inner;
                            break;
                        }
                    }
                }

                if (!IsW(cn, "tc"))
                {
                    continue;
                }

                MarkupNode tcpr = Child(cn, "tcPr");
                MarkupNode vm = Child(tcpr, "vMerge");
                var ci = new CellInfo { Node = cn, Col = col, Span = Math.Max(1, Int(A(Child(tcpr, "gridSpan"), "val"), 1)) };
                if (!vm.IsNull)
                {
                    ci.Continue = A(vm, "val") is not "restart";
                    ci.Restart = !ci.Continue;
                }

                cells.Add(ci);
                col += ci.Span;
            }

            bool header = !Child(trpr, "tblHeader").IsNull && On(Child(trpr, "tblHeader"));
            rows.Add((row, cells, header));
        }

        // A vertical merge covers the cells that continue it in the rows below.
        for (int r = 0; r < rows.Count; r++)
        {
            foreach (CellInfo ci in rows[r].Cells)
            {
                if (!ci.Restart)
                {
                    continue;
                }

                int span = 1;
                for (int k = r + 1; k < rows.Count; k++)
                {
                    CellInfo? below = rows[k].Cells.FirstOrDefault(x => x.Col == ci.Col);
                    if (below is { Continue: true })
                    {
                        span++;
                    }
                    else
                    {
                        break;
                    }
                }

                ci.RowSpan = span;
            }
        }

        var tdecls = new List<string>();
        MarkupNode tw = Child(tpr, "tblW");
        string? twType = A(tw, "type");
        int twVal = Int(A(tw, "w"));
        if (twType == "pct" && twVal > 0)
        {
            tdecls.Add("width:" + (twVal / 50.0).ToString("0.##", CultureInfo.InvariantCulture) + "%");
        }
        else if (twType == "dxa" && twVal > 0)
        {
            tdecls.Add("width:" + Pt(twVal));
        }

        string? jc = A(Child(tpr, "jc"), "val");
        if (jc == "center")
        {
            tdecls.Add("margin-left:auto;margin-right:auto");
        }
        else if (jc is "right" or "end")
        {
            tdecls.Add("margin-left:auto");
        }
        else if (Int(A(Child(tpr, "tblInd"), "w")) is int ind and > 0)
        {
            tdecls.Add("margin-left:" + Pt(ind));
        }

        _b.Append("<table");
        if (!Child(tpr, "bidiVisual").IsNull && On(Child(tpr, "bidiVisual")))
        {
            _b.Append(" dir=\"rtl\"");
        }

        if (tdecls.Count > 0)
        {
            _b.Append(" style=\"").Append(EscAttr(string.Join(';', tdecls))).Append('"');
        }

        _b.Append('>');
        if (gridCols.Count > 0 && gridCols.Any(static g => g > 0))
        {
            _b.Append("<colgroup>");
            foreach (int g in gridCols)
            {
                _b.Append("<col style=\"width:").Append(Pt(g)).Append("\">");
            }

            _b.Append("</colgroup>");
        }

        bool inHead = false;
        bool inBody = false;
        List<OpenList> savedLists = _lists;
        for (int r = 0; r < rows.Count; r++)
        {
            (MarkupNode row, List<CellInfo> cells, bool header) = rows[r];
            bool headRow = header && (r == 0 || inHead);
            if (headRow && !inHead)
            {
                _b.Append("<thead>");
                inHead = true;
            }
            else if (!headRow && !inBody)
            {
                if (inHead)
                {
                    _b.Append("</thead>");
                    inHead = false;
                }

                _b.Append("<tbody>");
                inBody = true;
            }

            _b.Append("<tr");
            int height = Int(A(Child(Child(row, "trPr"), "trHeight"), "val"));
            if (height > 0)
            {
                _b.Append(" style=\"height:").Append(Pt(height)).Append('"');
            }

            _b.Append('>');
            int lastCol = gridCols.Count;
            foreach (CellInfo ci in cells)
            {
                if (ci.Continue)
                {
                    continue;
                }

                MarkupNode tcpr = Child(ci.Node, "tcPr");
                string el = headRow ? "th" : "td";
                _b.Append('<').Append(el);
                if (ci.Span > 1)
                {
                    _b.Append(" colspan=\"").Append(ci.Span.ToString(CultureInfo.InvariantCulture)).Append('"');
                }

                if (ci.RowSpan > 1)
                {
                    _b.Append(" rowspan=\"").Append(ci.RowSpan.ToString(CultureInfo.InvariantCulture)).Append('"');
                }

                var d = new List<string>();
                MarkupNode cb = Child(tcpr, "tcBorders");
                int endCol = ci.Col + ci.Span;
                bool lastRowOfCell = r + ci.RowSpan >= rows.Count;
                string? top = BorderCss(Child(cb, "top")) ?? (r == 0 ? tb[0] : tb[4]);
                string? left = BorderCss(Child(cb, "left").IsNull ? Child(cb, "start") : Child(cb, "left")) ?? (ci.Col == 0 ? tb[1] : tb[5]);
                string? bottom = BorderCss(Child(cb, "bottom")) ?? (lastRowOfCell ? tb[2] : tb[4]);
                string? right = BorderCss(Child(cb, "right").IsNull ? Child(cb, "end") : Child(cb, "right")) ?? (endCol >= lastCol ? tb[3] : tb[5]);
                if (top is not null)
                {
                    d.Add("border-top:" + top);
                }

                if (left is not null)
                {
                    d.Add("border-left:" + left);
                }

                if (bottom is not null)
                {
                    d.Add("border-bottom:" + bottom);
                }

                if (right is not null)
                {
                    d.Add("border-right:" + right);
                }

                string? fill = A(Child(tcpr, "shd"), "fill");
                if (IsColor(fill))
                {
                    d.Add("background-color:#" + fill);
                }

                string? va = A(Child(tcpr, "vAlign"), "val");
                if (va is "center" or "bottom")
                {
                    d.Add("vertical-align:" + (va == "center" ? "middle" : "bottom"));
                }

                MarkupNode mar = Child(tcpr, "tcMar");
                if (!mar.IsNull)
                {
                    int t = Int(A(Child(mar, "top"), "w")), rr = Int(A(Child(mar, "right") is { IsNull: false } rn ? rn : Child(mar, "end"), "w"), 108), bt = Int(A(Child(mar, "bottom"), "w")),
                        l = Int(A(Child(mar, "left") is { IsNull: false } ln ? ln : Child(mar, "start"), "w"), 108);
                    d.Add("padding:" + Pt(t) + " " + Pt(rr) + " " + Pt(bt) + " " + Pt(l));
                }

                if (d.Count > 0)
                {
                    _b.Append(" style=\"").Append(EscAttr(string.Join(';', d))).Append('"');
                }

                _b.Append('>');
                _lists = [];
                int mark = _b.Length;
                Blocks(ci.Node);
                CloseLists();
                if (_b.Length == mark)
                {
                    _b.Append("<p><br></p>");
                }

                _b.Append("</").Append(el).Append('>');
            }

            _b.Append("</tr>");
        }

        _lists = savedLists;
        if (inHead)
        {
            _b.Append("</thead>");
        }

        if (inBody)
        {
            _b.Append("</tbody>");
        }

        _b.Append("</table>");
    }

    // ---- pictures and text boxes -----------------------------------------------------------------------------------------------

    private static IEnumerable<MarkupNode> Descendants(MarkupNode n)
    {
        foreach (MarkupNode c in n.ChildElements)
        {
            yield return c;
            foreach (MarkupNode d in Descendants(c))
            {
                yield return d;
            }
        }
    }

    private void Drawing(MarkupNode d)
    {
        string? rid = null;
        string alt = string.Empty;
        long cx = 0, cy = 0;
        bool anchored = false;
        foreach (MarkupNode n in Descendants(d))
        {
            string ln = n.LocalNameString;
            switch (ln)
            {
                case "blip":
                    rid ??= RA(n, "embed") ?? RA(n, "link");
                    break;
                case "imagedata":
                    rid ??= RA(n, "id");
                    break;
                case "docPr":
                    alt = n.GetAttribute("descr") ?? n.GetAttribute("title") ?? n.GetAttribute("name") ?? alt;
                    break;
                case "extent" when cx == 0:
                    _ = long.TryParse(n.GetAttribute("cx"), NumberStyles.Integer, CultureInfo.InvariantCulture, out cx);
                    _ = long.TryParse(n.GetAttribute("cy"), NumberStyles.Integer, CultureInfo.InvariantCulture, out cy);
                    break;
                case "anchor":
                    anchored = true;
                    break;
                case "txbxContent":
                    _floating.Add(n);
                    _loss.Report("loss.html-text-boxes", "text boxes", 1, "Text boxes are written as blocks after the paragraph they were in.");
                    break;
                case "chart":
                    _loss.Report("loss.html-charts", "charts", 1, "Charts are not written.");
                    break;
            }
        }

        if (rid is null)
        {
            if (!d.IsNull && Descendants(d).All(static n => n.LocalNameString != "txbxContent") && !(d.LocalNameString == "pict" && Descendants(d).Any(static n => n.LocalNameString == "textbox")))
            {
                _loss.Report("loss.html-drawings", "drawings and shapes", 1, "Drawing shapes, SmartArt and OLE objects without a picture are not written.");
            }

            return;
        }

        IPackagePart? part = Target(rid);
        if (part is null)
        {
            _loss.Report("loss.html-images", "pictures", 1, "A picture whose data is not in the package was left out.");
            return;
        }

        string ct = part.ContentType;
        if (ct is not ("image/png" or "image/jpeg" or "image/gif" or "image/bmp" or "image/svg+xml" or "image/webp"))
        {
            _loss.Report("loss.html-metafile", "metafile and TIFF pictures", 1, "Pictures a browser cannot show (EMF, WMF, TIFF) are not written; their alternate text is.");
            if (alt.Length > 0)
            {
                _b.Append(Esc(alt));
            }

            return;
        }

        if (anchored)
        {
            _loss.Report("loss.html-floating-pictures", "floating pictures", 1, "A picture with a text wrap is written in the text line, not floated.");
        }

        ReadOnlyMemory<byte> data = part.GetBytes();
        string src;
        _imageCount++;
        string ext = ct switch { "image/png" => "png", "image/jpeg" => "jpg", "image/gif" => "gif", "image/bmp" => "bmp", "image/svg+xml" => "svg", _ => "webp" };
        string? stored = _options.ImageSink?.Invoke(new HtmlImage("image" + _imageCount.ToString(CultureInfo.InvariantCulture) + "." + ext, ct, data));
        src = stored ?? "data:" + ct + ";base64," + Convert.ToBase64String(data.Span);
        _b.Append("<img src=\"").Append(EscAttr(src)).Append("\" alt=\"").Append(EscAttr(alt)).Append('"');
        if (cx > 0 && cy > 0)
        {
            // 9525 EMU to a CSS pixel.
            _b.Append(" width=\"").Append((cx / 9525).ToString(CultureInfo.InvariantCulture)).Append("\" height=\"").Append((cy / 9525).ToString(CultureInfo.InvariantCulture)).Append('"');
        }

        _b.Append('>');
    }

    // ---- notes -----------------------------------------------------------------------------------------------------------------

    private readonly List<(int Number, bool Endnote, int Id)> _notes = [];

    private void NoteReference(MarkupNode c)
    {
        bool end = c.LocalNameString == "endnoteReference";
        int id = Int(A(c, "id"), -1);
        int number = _notes.Count + 1;
        _notes.Add((number, end, id));
        _usesNotes = true;
        string n = number.ToString(CultureInfo.InvariantCulture);
        _b.Append("<sup class=\"noteref\"><a id=\"fnref-").Append(n).Append("\" href=\"#fn-").Append(n).Append("\" role=\"doc-noteref\" aria-label=\"Note ").Append(n).Append("\"></a></sup>");
    }

    private void Notes()
    {
        if (_notes.Count == 0)
        {
            return;
        }

        MarkupNode footnotes = LoadRelated("footnotes", out string? footnotesPart);
        MarkupNode endnotes = LoadRelated("endnotes", out string? endnotesPart);
        _b.Append("<section class=\"footnotes\" role=\"doc-endnotes\"><ol>");
        string savedPart = _part;
        List<OpenList> savedLists = _lists;
        for (int i = 0; i < _notes.Count; i++)
        {
            (int number, bool endnote, int id) = _notes[i];
            MarkupNode root = endnote ? endnotes : footnotes;
            _part = (endnote ? endnotesPart : footnotesPart) ?? savedPart;
            _b.Append("<li id=\"fn-").Append(number.ToString(CultureInfo.InvariantCulture)).Append("\">");
            foreach (MarkupNode note in root.ChildElements)
            {
                if ((IsW(note, "footnote") || IsW(note, "endnote")) && Int(A(note, "id"), -99) == id && A(note, "type") is null or "normal")
                {
                    _lists = [];
                    Blocks(note);
                    CloseLists();
                }
            }

            _b.Append("<a href=\"#fnref-").Append(number.ToString(CultureInfo.InvariantCulture)).Append("\" role=\"doc-backlink\" aria-label=\"Back to the text\"></a></li>");
        }

        _part = savedPart;
        _lists = savedLists;
        _b.Append("</ol></section>");
    }
}
