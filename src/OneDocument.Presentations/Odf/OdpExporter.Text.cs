using System.Globalization;
using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Schema;

namespace RecursiveAI.OneDocument.Presentations.Odf;

// Text: paragraphs, lists, runs, fields and links, with the formatting the hub resolves for each.
internal sealed partial class OdpExporter
{
    private sealed class ListDef
    {
        public required string Name;
        public required BulletFormat Bullet;
        public readonly Dictionary<int, (long MarL, long Indent)> Geometry = [];
    }

    private readonly List<ListDef> _lists = [];

    private ListDef ListFor(BulletFormat bullet, int level, long marL, long indent)
    {
        foreach (ListDef l in _lists)
        {
            if (l.Bullet == bullet && (!l.Geometry.TryGetValue(level, out var g) || g == (marL, indent)))
            {
                l.Geometry[level] = (marL, indent);
                return l;
            }
        }

        var def = new ListDef { Name = "L" + OdpUnits.I(_lists.Count + 1), Bullet = bullet };
        def.Geometry[level] = (marL, indent);
        _lists.Add(def);
        return def;
    }

    private void AppendListStyles(StringBuilder sb)
    {
        foreach (ListDef l in _lists)
        {
            sb.Append("<text:list-style style:name=\"").Append(l.Name).Append("\">");
            for (int level = 1; level <= 10; level++)
            {
                (long marL, long indent) = l.Geometry.TryGetValue(level, out var g) ? g : (342900L + 457200L * (level - 1), -342900L);
                BulletFormat b = l.Bullet;
                string props = "<style:list-level-properties text:list-level-position-and-space-mode=\"label-alignment\"><style:list-level-label-alignment text:label-followed-by=\"listtab\" fo:text-indent=\""
                    + OdpUnits.Cm(indent) + "\" fo:margin-left=\"" + OdpUnits.Cm(marL) + "\"/></style:list-level-properties>";
                var text = new StringBuilder();
                if (b.Font is { Length: > 0 } font && b.Kind == BulletKind.Character)
                {
                    text.Append(" fo:font-family=\"").Append(OdpUnits.Esc(font)).Append('"');
                }

                // The bullet is drawn at the size of the text unless the paragraph says otherwise; writing it out keeps readers from shrinking it.
                text.Append(" fo:font-size=\"").Append(OdpUnits.I(b.SizePercent is { } sz && sz > 0 ? sz / 1000 : 100)).Append("%\"");

                string tp = text.Length > 0 ? "<style:text-properties" + text + "/>" : "";
                if (b.Kind == BulletKind.AutoNumber)
                {
                    (string format, string prefix, string suffix) = NumberFormat(b.AutoNumberType);
                    sb.Append("<text:list-level-style-number text:level=\"").Append(level).Append("\" style:num-format=\"").Append(format).Append('"');
                    if (prefix.Length > 0)
                    {
                        sb.Append(" style:num-prefix=\"").Append(prefix).Append('"');
                    }

                    if (suffix.Length > 0)
                    {
                        sb.Append(" style:num-suffix=\"").Append(suffix).Append('"');
                    }

                    if (b.StartAt is { } start && start > 1)
                    {
                        sb.Append(" text:start-value=\"").Append(start).Append('"');
                    }

                    sb.Append('>').Append(props).Append(tp).Append("</text:list-level-style-number>");
                }
                else
                {
                    string ch = b.Character is { Length: > 0 } c ? c : "\u2022";
                    sb.Append("<text:list-level-style-bullet text:level=\"").Append(level).Append("\" text:bullet-char=\"").Append(OdpUnits.Esc(ch.Length > 1 ? ch[..1] : ch)).Append("\">").Append(props).Append(tp).Append("</text:list-level-style-bullet>");
                }
            }

            sb.Append("</text:list-style>");
        }
    }

    // ECMA-376 Part 1 §20.1.10.61 (ST_TextAutonumberScheme) to a number format, prefix and suffix.
    private (string Format, string Prefix, string Suffix) NumberFormat(string? type)
    {
        type ??= "arabicPeriod";
        string format = type.StartsWith("arabic", StringComparison.Ordinal) ? "1"
            : type.StartsWith("alphaLc", StringComparison.Ordinal) ? "a"
            : type.StartsWith("alphaUc", StringComparison.Ordinal) ? "A"
            : type.StartsWith("romanLc", StringComparison.Ordinal) ? "i"
            : type.StartsWith("romanUc", StringComparison.Ordinal) ? "I"
            : "";
        if (format.Length == 0)
        {
            Lost("loss.odp-export-numbering-schemes", "numbering schemes with no OpenDocument counterpart", "Numbering schemes other than Arabic, letters and Roman numerals are written as Arabic numbers.");
            format = "1";
        }

        return type switch
        {
            _ when type.EndsWith("ParenBoth", StringComparison.Ordinal) => (format, "(", ")"),
            _ when type.EndsWith("ParenR", StringComparison.Ordinal) => (format, "", ")"),
            _ when type.EndsWith("Plain", StringComparison.Ordinal) => (format, "", ""),
            _ => (format, "", "."),
        };
    }

    // ---- paragraphs -----------------------------------------------------------------------------------------------------------

    private void WriteTextParagraphs(StringBuilder sb, TextBody body, Shape? shape)
    {
        int openDepth = 0;
        string? openName = null;
        var usedSinceBreak = new HashSet<string>(StringComparer.Ordinal);

        void CloseAll()
        {
            while (openDepth > 0)
            {
                sb.Append("</text:list-item></text:list>");
                openDepth--;
            }

            openName = null;
        }

        foreach (TextParagraph p in body.Paragraphs)
        {
            _ct.ThrowIfCancellationRequested();
            ParagraphFormat pf = p.ResolveFormat();
            bool bulleted = pf.Bullet.Kind is BulletKind.Character or BulletKind.AutoNumber or BulletKind.Picture;
            if (pf.Bullet.Kind == BulletKind.Picture)
            {
                Lost("loss.odp-export-picture-bullets", "picture bullets", "Picture bullets are written as round bullets.");
                pf = pf with { Bullet = pf.Bullet with { Kind = BulletKind.Character, Character = "\u2022" } };
            }

            if (bulleted)
            {
                int depth = Math.Clamp(p.Level, 0, 8) + 1;
                ListDef def = ListFor(pf.Bullet, depth, pf.MarginLeft ?? 342900 + 457200L * (depth - 1), pf.Indent ?? -342900);
                if (openName != def.Name)
                {
                    CloseAll();
                    openName = def.Name;
                }

                if (openDepth == 0)
                {
                    for (int i = 1; i <= depth; i++)
                    {
                        // A numbered list that comes back after only other lists in between goes on counting where it stopped.
                        sb.Append(i == 1 ? "<text:list text:style-name=\"" + def.Name + "\"" + (def.Bullet.Kind == BulletKind.AutoNumber && usedSinceBreak.Contains(def.Name) && pf.Bullet.StartAt is null ? " text:continue-numbering=\"true\"" : "") + "><text:list-item>" : "<text:list><text:list-item>");
                    }

                    openDepth = depth;
                    usedSinceBreak.Add(def.Name);
                }
                else if (depth > openDepth)
                {
                    for (int i = openDepth + 1; i <= depth; i++)
                    {
                        sb.Append("<text:list><text:list-item>");
                    }

                    openDepth = depth;
                }
                else
                {
                    while (openDepth > depth)
                    {
                        sb.Append("</text:list-item></text:list>");
                        openDepth--;
                    }

                    sb.Append("</text:list-item><text:list-item>");
                }
            }
            else
            {
                CloseAll();
                usedSinceBreak.Clear();
            }

            WriteParagraph(sb, p, pf, bulleted, shape, body.Part);
        }

        CloseAll();
    }

    private void WriteParagraph(StringBuilder sb, TextParagraph p, ParagraphFormat pf, bool inList, Shape? shape, PresentationPart part)
    {
        var attrs = new StringBuilder();
        attrs.Append(pf.Alignment switch
        {
            "ctr" => " fo:text-align=\"center\"",
            "r" => " fo:text-align=\"end\"",
            "just" or "justLow" or "dist" or "thaiDist" => " fo:text-align=\"justify\"",
            _ => " fo:text-align=\"start\"",
        });
        if (!inList)
        {
            attrs.Append(" fo:margin-left=\"").Append(OdpUnits.Cm(pf.MarginLeft ?? 0)).Append("\" fo:text-indent=\"").Append(OdpUnits.Cm(pf.Indent ?? 0)).Append('"');
        }

        if (pf.MarginRight is { } mr && mr != 0)
        {
            attrs.Append(" fo:margin-right=\"").Append(OdpUnits.Cm(mr)).Append('"');
        }

        if (pf.LineSpacing is { } ls)
        {
            attrs.Append(ls.IsPercent ? " fo:line-height=\"" + OdpUnits.I(ls.Value / 1000) + "%\"" : " fo:line-height=\"" + OdpUnits.Pt(ls.Value / 100.0) + "\"");
        }

        if (pf.SpaceBefore is { IsPercent: false } sb4)
        {
            attrs.Append(" fo:margin-top=\"").Append(OdpUnits.Pt(sb4.Value / 100.0)).Append('"');
        }

        if (pf.SpaceAfter is { IsPercent: false } sa)
        {
            attrs.Append(" fo:margin-bottom=\"").Append(OdpUnits.Pt(sa.Value / 100.0)).Append('"');
        }

        if (pf.RightToLeft == true)
        {
            attrs.Append(" style:writing-mode=\"rl-tb\"");
        }

        string children = "<style:paragraph-properties" + attrs + "/>";
        var runs = p.Runs;
        if (runs.Count == 0 && pf.Run.Size is { } size)
        {
            // An empty paragraph keeps its line height through the paragraph mark's size.
            children += "<style:text-properties fo:font-size=\"" + OdpUnits.Pt(size / 100.0) + "\"/>";
        }

        string style = AddStyle("paragraph", "P", "", children);
        sb.Append("<text:p text:style-name=\"").Append(style).Append("\">");
        foreach (TextRun run in runs)
        {
            WriteRun(sb, p, run, shape, part);
        }

        sb.Append("</text:p>");
    }

    private void WriteRun(StringBuilder sb, TextParagraph p, TextRun run, Shape? shape, PresentationPart part)
    {
        if (run.Kind == TextRunKind.LineBreak)
        {
            sb.Append("<text:line-break/>");
            return;
        }

        RunFormat rf = p.ResolveRunFormat(run);
        string span = SpanStyle(rf, run, shape, part);
        string? link = LinkOf(run, part);
        if (link is not null)
        {
            sb.Append("<text:a xlink:type=\"simple\" xlink:href=\"").Append(OdpUnits.Esc(link)).Append("\">");
        }

        sb.Append("<text:span text:style-name=\"").Append(span).Append("\">");
        string? field = run.FieldType;
        if (field == "slidenum")
        {
            sb.Append("<text:page-number>").Append(OdpUnits.Esc(run.Text)).Append("</text:page-number>");
        }
        else if (field is not null && field.StartsWith("datetime", StringComparison.Ordinal))
        {
            sb.Append("<text:date text:fixed=\"true\">").Append(OdpUnits.Esc(run.Text)).Append("</text:date>");
        }
        else
        {
            AppendText(sb, run.Text);
        }

        sb.Append("</text:span>");
        if (link is not null)
        {
            sb.Append("</text:a>");
        }
    }

    // OpenDocument 1.3 Part 1 §6.1.2: white space is collapsed unless written as text:s, text:tab and text:line-break.
    private static void AppendText(StringBuilder sb, string text)
    {
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == ' ')
            {
                int n = 1;
                while (i + n < text.Length && text[i + n] == ' ')
                {
                    n++;
                }

                bool lone = n == 1 && i > 0 && i + 1 < text.Length;
                if (lone)
                {
                    sb.Append(' ');
                }
                else
                {
                    sb.Append("<text:s text:c=\"").Append(n).Append("\"/>");
                }

                i += n;
            }
            else if (c == '\t')
            {
                sb.Append("<text:tab/>");
                i++;
            }
            else if (c is '\n' or '\r' or '\v')
            {
                sb.Append("<text:line-break/>");
                i += c == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
            }
            else
            {
                int start = i;
                while (i < text.Length && text[i] is not (' ' or '\t' or '\n' or '\r' or '\v'))
                {
                    i++;
                }

                sb.Append(OdpUnits.Esc(text[start..i]));
            }
        }
    }

    private string? LinkOf(TextRun run, PresentationPart part)
    {
        if (run.Properties is not { IsValid: true } rPr || Pml.A(rPr, "hlinkClick") is not { } click)
        {
            return null;
        }

        string? rid = Pml.RelAttr(click, "id");
        Relationship? rel = rid is null ? null : part.Relationships.FirstOrDefault(r => r.Id == rid);
        if (rel is { TargetMode: RelationshipTargetMode.External })
        {
            return rel.Target;
        }

        Lost("loss.odp-export-slide-links", "links to slides", "Links to other slides and actions are not written; the text stays.");
        return null;
    }

    private string SpanStyle(RunFormat rf, TextRun run, Shape? shape, PresentationPart part)
    {
        var a = new StringBuilder();
        if (rf.Size is { } size)
        {
            a.Append(" fo:font-size=\"").Append(OdpUnits.Pt(size / 100.0)).Append('"');
        }

        if (rf.Bold is { } b)
        {
            a.Append(" fo:font-weight=\"").Append(b ? "bold" : "normal").Append('"');
        }

        if (rf.Italic is { } i)
        {
            a.Append(" fo:font-style=\"").Append(i ? "italic" : "normal").Append('"');
        }

        if (rf.Underline is { } u && u != "none")
        {
            a.Append(" style:text-underline-style=\"solid\" style:text-underline-type=\"").Append(u == "dbl" ? "double" : "single").Append("\" style:text-underline-width=\"auto\" style:text-underline-color=\"font-color\"");
        }

        if (rf.Strike is { } s && s != "noStrike")
        {
            a.Append(" style:text-line-through-style=\"solid\" style:text-line-through-type=\"").Append(s == "dblStrike" ? "double" : "single").Append('"');
        }

        if (rf.Typeface is { Length: > 0 } face && !face.StartsWith('+'))
        {
            _fonts.Add(face);
            a.Append(" style:font-name=\"").Append(OdpUnits.Esc(face)).Append('"');
        }

        string? hex = rf.Color is { } c ? c.Kind == ColorKind.Preset ? PresetHex(c.Value) : part.ResolveColor(c) : null;
        if (hex is null && shape is not null && Pml.P(shape.Markup, "style") is { } st && Pml.A(st, "fontRef") is { } fr)
        {
            hex = HexOf(part, fr, out _);
        }

        if (hex is not null)
        {
            a.Append(" fo:color=\"#").Append(hex.ToLowerInvariant()).Append('"');
        }

        if (rf.Language is { Length: > 0 } lang)
        {
            int dash = lang.IndexOf('-', StringComparison.Ordinal);
            a.Append(" fo:language=\"").Append(OdpUnits.Esc(dash > 0 ? lang[..dash] : lang)).Append('"');
            if (dash > 0)
            {
                a.Append(" fo:country=\"").Append(OdpUnits.Esc(lang[(dash + 1)..])).Append('"');
            }
        }

        if (run.Properties is { IsValid: true } rPr)
        {
            if (Pml.AttrInt(rPr, "baseline") is { } baseline && baseline != 0)
            {
                a.Append(" style:text-position=\"").Append(baseline > 0 ? "super 58%" : "sub 58%").Append('"');
            }

            switch (Pml.Attr(rPr, "cap"))
            {
                case "all":
                    a.Append(" fo:text-transform=\"uppercase\"");
                    break;
                case "small":
                    a.Append(" fo:font-variant=\"small-caps\"");
                    break;
            }

            if (Pml.AttrInt(rPr, "spc") is { } spc && spc != 0)
            {
                a.Append(" fo:letter-spacing=\"").Append(OdpUnits.Pt(spc / 100.0)).Append('"');
            }

            if (Pml.A(rPr, "highlight") is { } hl && HexOf(part, hl, out _) is { } hh)
            {
                a.Append(" fo:background-color=\"#").Append(hh.ToLowerInvariant()).Append('"');
            }
        }

        return AddStyle("text", "T", "", "<style:text-properties" + a + "/>");
    }

    // ---- tables ---------------------------------------------------------------------------------------------------------------

    private void WriteTable(StringBuilder sb, Shape shape, Table table, Box b)
    {
        string graphic = AddStyle("graphic", "gr", "", "<style:graphic-properties/>");
        sb.Append("<draw:frame").Append(NameAttr(shape)).Append(" draw:style-name=\"").Append(graphic).Append("\" draw:layer=\"layout\"").Append(Position(b, allowRotate: false)).Append("><table:table>");
        if (table.StyleId is not null)
        {
            Lost("loss.odp-export-table-styles", "table styles", "The formatting a table style gives (banding, header colours) is not written; formatting written on the cells is.");
        }

        foreach (long w in table.ColumnWidths)
        {
            string cs = AddStyle("table-column", "co", "", "<style:table-column-properties style:column-width=\"" + OdpUnits.Cm(w) + "\"/>");
            sb.Append("<table:table-column table:style-name=\"").Append(cs).Append("\"/>");
        }

        if (table.HasHeaderRow)
        {
            // The header row is an ordinary row: the pinned LibreOffice build drops the rows of a table:table-header-rows when it opens a presentation.
            Lost("loss.odp-export-header-rows", "table header rows", "A table's header row is written as an ordinary row, so the flag that marks it is not kept.");
        }

        foreach (TableRow row in table.Rows)
        {
            string rs = AddStyle("table-row", "ro", "", "<style:table-row-properties style:row-height=\"" + OdpUnits.Cm(row.Height > 0 ? row.Height : 370840) + "\"/>");
            sb.Append("<table:table-row table:style-name=\"").Append(rs).Append("\">");
            foreach (TableCell cell in row.Cells)
            {
                if (cell.IsMergedContinuation)
                {
                    sb.Append("<table:covered-table-cell/>");
                    continue;
                }

                string cellStyle = CellStyle(cell, shape.Part);
                sb.Append("<table:table-cell table:style-name=\"").Append(cellStyle).Append('"');
                if (cell.ColumnSpan > 1)
                {
                    sb.Append(" table:number-columns-spanned=\"").Append(cell.ColumnSpan).Append('"');
                }

                if (cell.RowSpan > 1)
                {
                    sb.Append(" table:number-rows-spanned=\"").Append(cell.RowSpan).Append('"');
                }

                sb.Append(" office:value-type=\"string\">");
                if (cell.TextBody is { } tb)
                {
                    WriteTextParagraphs(sb, tb, null);
                }
                else
                {
                    sb.Append("<text:p/>");
                }

                sb.Append("</table:table-cell>");
            }

            sb.Append("</table:table-row>");
        }

        sb.Append("</table:table></draw:frame>");
    }

    private string CellStyle(TableCell cell, PresentationPart part)
    {
        var a = new StringBuilder();
        if (cell.Properties is { IsValid: true } tcPr)
        {
            foreach ((string tag, string side) in new[] { ("lnL", "left"), ("lnR", "right"), ("lnT", "top"), ("lnB", "bottom") })
            {
                if (Pml.A(tcPr, tag) is not { } ln)
                {
                    continue;
                }

                string? hex = null;
                bool none = false;
                foreach (MarkupRef c in Pml.Elements(ln))
                {
                    if (c.LocalNameText == "noFill")
                    {
                        none = true;
                    }
                    else if (c.LocalNameText == "solidFill")
                    {
                        hex = HexOf(part, c, out _);
                    }
                }

                long w = Pml.AttrLong(ln, "w") ?? 12700;
                a.Append(" fo:border-").Append(side).Append("=\"").Append(none || hex is null ? "none" : OdpUnits.Pt(w / 12700.0) + " solid #" + hex.ToLowerInvariant()).Append('"');
            }

            foreach ((string attr, string side) in new[] { ("marL", "left"), ("marR", "right"), ("marT", "top"), ("marB", "bottom") })
            {
                if (Pml.AttrLong(tcPr, attr) is { } m)
                {
                    a.Append(" fo:padding-").Append(side).Append("=\"").Append(OdpUnits.Cm(Math.Max(0, m))).Append('"');
                }
            }

            a.Append(Pml.Attr(tcPr, "anchor") switch { "ctr" => " style:vertical-align=\"middle\"", "b" => " style:vertical-align=\"bottom\"", "t" => " style:vertical-align=\"top\"", _ => "" });
            string fill = FillProperties(tcPr, part, out _);
            if (fill.Contains("draw:fill=\"solid\"", StringComparison.Ordinal))
            {
                int at = fill.IndexOf("draw:fill-color=\"", StringComparison.Ordinal) + "draw:fill-color=\"".Length;
                a.Append(" fo:background-color=\"").Append(fill.AsSpan(at, 7)).Append('"');
            }
            else if (fill.Contains("draw:fill=\"none\"", StringComparison.Ordinal))
            {
                a.Append(" fo:background-color=\"transparent\"");
            }
        }

        if (!a.ToString().Contains("fo:background-color", StringComparison.Ordinal))
        {
            // Without a fill of its own a cell is see-through; readers would otherwise colour it with their own default table design.
            a.Append(" fo:background-color=\"transparent\"");
        }

        return AddStyle("table-cell", "ce", "", "<style:table-cell-properties" + a + "/>");
    }
}
