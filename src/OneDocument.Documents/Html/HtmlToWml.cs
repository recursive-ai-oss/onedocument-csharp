using System.Linq;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using RecursiveAI.OneDocument.Documents.Rtf;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Documents.Html;

// Writes the importer's model as a word-processing package. The part order, relationship ids and every id are fixed by the model's order, so the
// same page always gives the same bytes.
internal sealed class HtmlToWml
{
    private const string WNs = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private const string Decl = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n";
    private const string RootNs =
        " xmlns:w=\"" + WNs + "\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"" +
        " xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"" +
        " xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"";
    private const string CtPrefix = "application/vnd.openxmlformats-officedocument.wordprocessingml.";

    private readonly HtmlContent _c;
    private readonly OpenContext _ctx;
    private readonly RelSet _rels = new();
    private readonly List<(string Name, string ContentType, byte[] Data)> _media = [];
    private readonly Dictionary<string, string> _mediaByHash = new(StringComparer.Ordinal);
    private readonly HashSet<string> _hyperlinkIds = [];
    private int _bookmarkId = 1;
    private int _drawingId = 1;
    private readonly Dictionary<string, int> _abstractByKey = new(StringComparer.Ordinal);
    private readonly List<string> _abstractXml = [];

    public HtmlToWml(HtmlContent content, OpenContext context)
    {
        _c = content;
        _ctx = context;
    }

    public OpcPackage Build()
    {
        OpcPackage package = OpcPackage.Create(_ctx.Limits);
        try
        {
            package.SetRelationships(null,
            [
                new Relationship("rId1", RelNs + "officeDocument", "word/document.xml"),
                new Relationship("rId2", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties", "docProps/core.xml"),
            ]);

            // The fixed relationships come first, so their ids do not depend on how many links or pictures a page has.
            _rels.Add(RelNs + "styles", "styles.xml");
            if (_c.Lists.Count > 0)
            {
                _rels.Add(RelNs + "numbering", "numbering.xml");
            }

            _rels.Add(RelNs + "settings", "settings.xml");

            var body = new StringBuilder();
            Blocks(body, _c.Blocks, 0);
            if (_c.Blocks.Count == 0 || _c.Blocks[^1] is HTable)
            {
                body.Append("<w:p/>");
            }

            body.Append("<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/>");
            if (_c.Rtl)
            {
                body.Append("<w:bidi/>");
            }

            body.Append("</w:sectPr>");
            package.AddPart("/word/document.xml", CtPrefix + "document.main+xml", Encoding.UTF8.GetBytes(Decl + "<w:document" + RootNs + "><w:body>" + body + "</w:body></w:document>"));
            package.AddPart("/word/styles.xml", CtPrefix + "styles+xml", Encoding.UTF8.GetBytes(Decl + StylesXml()));
            if (_c.Lists.Count > 0)
            {
                package.AddPart("/word/numbering.xml", CtPrefix + "numbering+xml", Encoding.UTF8.GetBytes(Decl + NumberingXml()));
            }

            package.AddPart("/word/settings.xml", CtPrefix + "settings+xml", Encoding.UTF8.GetBytes(Decl +
                "<w:settings xmlns:w=\"" + WNs + "\"><w:zoom w:percent=\"100\"/><w:defaultTabStop w:val=\"720\"/>" +
                "<w:compat><w:compatSetting w:name=\"compatibilityMode\" w:uri=\"http://schemas.microsoft.com/office/word\" w:val=\"15\"/></w:compat></w:settings>"));
            foreach ((string name, string ct, byte[] data) in _media)
            {
                package.AddPart(name, ct, data);
            }

            package.AddPart("/docProps/core.xml", "application/vnd.openxmlformats-package.core-properties+xml", Encoding.UTF8.GetBytes(CoreXml()));
            var rels = new List<Relationship>();
            foreach ((string id, string type, string target, bool external) in _rels.Items)
            {
                rels.Add(new Relationship(id, type, target, external ? RelationshipTargetMode.External : RelationshipTargetMode.Internal));
            }

            package.SetRelationships("/word/document.xml", rels);
            return package;
        }
        catch
        {
            package.Dispose();
            throw;
        }
    }

    private string CoreXml()
    {
        var sb = new StringBuilder(Decl).Append("<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">");
        void Elem(string tag, string? v)
        {
            if (!string.IsNullOrWhiteSpace(v))
            {
                sb.Append('<').Append(tag).Append('>').Append(X.Esc(Clean(v!))).Append("</").Append(tag).Append('>');
            }
        }

        _c.Meta.TryGetValue("keywords", out string? keywords);
        _c.Meta.TryGetValue("description", out string? description);
        _c.Meta.TryGetValue("author", out string? author);
        Elem("dc:creator", author);
        Elem("dc:description", description);
        Elem("cp:keywords", keywords);
        Elem("dc:title", _c.Title);
        return sb.Append("</cp:coreProperties>").ToString();
    }

    private static string Clean(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            if (c >= ' ' || c is '\t' or '\n' or '\r')
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    // ---- blocks ----------------------------------------------------------------------------------------------------------------

    private void Blocks(StringBuilder sb, List<HBlock> blocks, int depth)
    {
        foreach (HBlock b in blocks)
        {
            if (b is HPara p)
            {
                Paragraph(sb, p);
            }
            else if (b is HTable t)
            {
                Table(sb, t, depth);
            }
        }
    }

    private void Paragraph(StringBuilder sb, HPara p)
    {
        sb.Append("<w:p>");
        PPr(sb, p);
        Inlines(sb, p);
        sb.Append("</w:p>");
    }

    private static string Num(int v) => v.ToString(CultureInfo.InvariantCulture);

    private static void PPr(StringBuilder sb, HPara p)
    {
        HParaProps pp = p.Props;
        var x = new StringBuilder();
        if (p.StyleId != "Normal")
        {
            x.Append("<w:pStyle w:val=\"").Append(p.StyleId).Append("\"/>");
        }

        if (pp.KeepNext)
        {
            x.Append("<w:keepNext/>");
        }

        if (pp.PageBreakBefore)
        {
            x.Append("<w:pageBreakBefore/>");
        }

        if (p.List is { } list)
        {
            x.Append("<w:numPr><w:ilvl w:val=\"").Append(Num(Math.Min(list.Depth, 8))).Append("\"/><w:numId w:val=\"").Append(Num(list.Id)).Append("\"/></w:numPr>");
        }

        Borders(x, "pBdr", pp.BorderTop, pp.BorderLeft, pp.BorderBottom, pp.BorderRight, 1);
        if (pp.Shade is not null)
        {
            x.Append("<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"").Append(pp.Shade).Append("\"/>");
        }

        if (pp.Rtl)
        {
            x.Append("<w:bidi/>");
        }

        Spacing(x, pp);
        if (pp.IndentLeft != 0 || pp.IndentRight != 0 || pp.IndentFirst != 0)
        {
            x.Append("<w:ind");
            if (pp.IndentLeft != 0)
            {
                x.Append(" w:left=\"").Append(Num(pp.IndentLeft)).Append('"');
            }

            if (pp.IndentRight != 0)
            {
                x.Append(" w:right=\"").Append(Num(pp.IndentRight)).Append('"');
            }

            if (pp.IndentFirst < 0)
            {
                x.Append(" w:hanging=\"").Append(Num(-pp.IndentFirst)).Append('"');
            }
            else if (pp.IndentFirst > 0)
            {
                x.Append(" w:firstLine=\"").Append(Num(pp.IndentFirst)).Append('"');
            }

            x.Append("/>");
        }

        if (pp.Align is not null)
        {
            x.Append("<w:jc w:val=\"").Append(pp.Align).Append("\"/>");
        }

        if (x.Length > 0)
        {
            sb.Append("<w:pPr>").Append(x).Append("</w:pPr>");
        }
    }

    private static void Spacing(StringBuilder x, HParaProps pp)
    {
        if (pp.Before == 0 && pp.Line == 0)
        {
            return;
        }

        x.Append("<w:spacing");
        if (pp.Before != 0)
        {
            x.Append(" w:before=\"").Append(Num(Math.Clamp(pp.Before, 0, 31680))).Append('"');
        }

        if (pp.Line != 0)
        {
            x.Append(" w:line=\"").Append(Num(pp.Line)).Append("\" w:lineRule=\"").Append(pp.LineRule ?? "auto").Append('"');
        }

        x.Append("/>");
    }

    private static void Borders(StringBuilder x, string element, HBorder? top, HBorder? left, HBorder? bottom, HBorder? right, int space)
    {
        if (top is null && left is null && bottom is null && right is null)
        {
            return;
        }

        x.Append("<w:").Append(element).Append('>');
        Side(x, "top", top, space);
        Side(x, "left", left, space);
        Side(x, "bottom", bottom, space);
        Side(x, "right", right, space);
        x.Append("</w:").Append(element).Append('>');
    }

    private static void Side(StringBuilder x, string name, HBorder? b, int space)
    {
        if (b is not { } v)
        {
            return;
        }

        x.Append("<w:").Append(name).Append(" w:val=\"").Append(HtmlCss.BorderValue(v.Style)).Append("\" w:sz=\"").Append(Num(Math.Clamp(v.Eighths, 2, 96)))
            .Append("\" w:space=\"").Append(Num(space)).Append("\" w:color=\"").Append(v.Color ?? "auto").Append("\"/>");
    }

    // ---- runs ------------------------------------------------------------------------------------------------------------------

    private void Inlines(StringBuilder sb, HPara p)
    {
        HRunProps baseline = p.StyleId == "Normal" || !_c.Styles.TryGetValue(p.StyleId, out HStyle? st) ? _c.DefaultRun : st.Run;
        int i = 0;
        List<HInline> list = p.Inlines;
        while (i < list.Count)
        {
            string? link = list[i] switch { HText t => t.Link, HImage im => im.Link, _ => null };
            if (link is null)
            {
                Inline(sb, list[i], baseline);
                i++;
                continue;
            }

            int j = i;
            var inner = new StringBuilder();
            while (j < list.Count && (list[j] is HText t2 && t2.Link == link || list[j] is HImage im2 && im2.Link == link))
            {
                Inline(inner, list[j], baseline);
                j++;
            }

            if (link[0] == '#')
            {
                sb.Append("<w:hyperlink w:anchor=\"").Append(X.Esc(link[1..])).Append("\" w:history=\"1\">").Append(inner).Append("</w:hyperlink>");
            }
            else
            {
                string id = _rels.Add(RelNs + "hyperlink", link, external: true);
                sb.Append("<w:hyperlink r:id=\"").Append(id).Append("\" w:history=\"1\">").Append(inner).Append("</w:hyperlink>");
            }

            i = j;
        }
    }

    private void Inline(StringBuilder sb, HInline item, HRunProps baseline)
    {
        switch (item)
        {
            case HText t:
                sb.Append("<w:r>");
                RPr(sb, t.Props, baseline);
                sb.Append("<w:t xml:space=\"preserve\">").Append(X.Esc(t.Text)).Append("</w:t></w:r>");
                break;
            case HBreak br:
                sb.Append(br.Page ? "<w:r><w:br w:type=\"page\"/></w:r>" : "<w:r><w:br/></w:r>");
                break;
            case HTab:
                sb.Append("<w:r><w:tab/></w:r>");
                break;
            case HBookmark bm:
                {
                    int id = _bookmarkId++;
                    sb.Append("<w:bookmarkStart w:id=\"").Append(Num(id)).Append("\" w:name=\"").Append(X.Esc(bm.Name)).Append("\"/><w:bookmarkEnd w:id=\"").Append(Num(id)).Append("\"/>");
                    break;
                }

            case HImage img:
                Picture(sb, img);
                break;
        }
    }

    private static void RPr(StringBuilder sb, HRunProps r, HRunProps b)
    {
        var x = new StringBuilder();
        if (r.Font is not null && r.Font != b.Font)
        {
            string f = X.Esc(r.Font);
            x.Append("<w:rFonts w:ascii=\"").Append(f).Append("\" w:hAnsi=\"").Append(f).Append("\" w:cs=\"").Append(f).Append("\"/>");
        }

        if (r.Bold != b.Bold)
        {
            x.Append(r.Bold ? "<w:b/>" : "<w:b w:val=\"0\"/>");
            if (r.Rtl)
            {
                x.Append(r.Bold ? "<w:bCs/>" : "<w:bCs w:val=\"0\"/>");
            }
        }

        if (r.Italic != b.Italic)
        {
            x.Append(r.Italic ? "<w:i/>" : "<w:i w:val=\"0\"/>");
            if (r.Rtl)
            {
                x.Append(r.Italic ? "<w:iCs/>" : "<w:iCs w:val=\"0\"/>");
            }
        }

        if (r.Caps != b.Caps)
        {
            x.Append(r.Caps == 2 ? "<w:smallCaps/>" : "<w:smallCaps w:val=\"0\"/>");
        }

        if (r.Strike != b.Strike)
        {
            x.Append(r.Strike ? "<w:strike/>" : "<w:strike w:val=\"0\"/>");
        }

        if (r.Color != b.Color)
        {
            x.Append("<w:color w:val=\"").Append(r.Color ?? "auto").Append("\"/>");
        }

        if (r.SizeHalfPoints > 0 && r.SizeHalfPoints != b.SizeHalfPoints)
        {
            x.Append("<w:sz w:val=\"").Append(Num(r.SizeHalfPoints)).Append("\"/>");
            if (r.Rtl)
            {
                x.Append("<w:szCs w:val=\"").Append(Num(r.SizeHalfPoints)).Append("\"/>");
            }
        }

        if (r.Underline != b.Underline)
        {
            x.Append(r.Underline ? "<w:u w:val=\"single\"/>" : "<w:u w:val=\"none\"/>");
        }

        if (r.Shade != b.Shade && r.Shade is not null)
        {
            x.Append("<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"").Append(r.Shade).Append("\"/>");
        }

        if (r.VertAlign != b.VertAlign)
        {
            x.Append("<w:vertAlign w:val=\"").Append(r.VertAlign == 1 ? "superscript" : r.VertAlign == 2 ? "subscript" : "baseline").Append("\"/>");
        }

        if (r.Rtl && !b.Rtl)
        {
            x.Append("<w:rtl/>");
        }

        if (r.Lang is not null && r.Lang != b.Lang)
        {
            x.Append("<w:lang w:val=\"").Append(X.Esc(r.Lang)).Append('"').Append(r.Rtl ? " w:bidi=\"" + X.Esc(r.Lang) + "\"" : string.Empty).Append("/>");
        }

        if (x.Length > 0)
        {
            sb.Append("<w:rPr>").Append(x).Append("</w:rPr>");
        }
    }

    private void Picture(StringBuilder sb, HImage img)
    {
        string hash = Convert.ToHexString(SHA256.HashData(img.Data));
        if (!_mediaByHash.TryGetValue(hash, out string? target))
        {
            string name = "image" + Num(_media.Count + 1) + "." + img.Extension;
            _media.Add(("/word/media/" + name, img.ContentType, img.Data));
            target = "media/" + name;
            _mediaByHash[hash] = target;
        }

        string rid = _rels.Add(RelNs + "image", target);
        int id = _drawingId++;
        string alt = X.Esc(Clean(img.Alt));
        string cx = img.WidthEmu.ToString(CultureInfo.InvariantCulture);
        string cy = img.HeightEmu.ToString(CultureInfo.InvariantCulture);
        sb.Append("<w:r><w:drawing><wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\"><wp:extent cx=\"").Append(cx).Append("\" cy=\"").Append(cy)
            .Append("\"/><wp:docPr id=\"").Append(Num(id)).Append("\" name=\"Picture ").Append(Num(id)).Append("\" descr=\"").Append(alt)
            .Append("\"/><wp:cNvGraphicFramePr><a:graphicFrameLocks noChangeAspect=\"1\"/></wp:cNvGraphicFramePr><a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\">")
            .Append("<pic:pic><pic:nvPicPr><pic:cNvPr id=\"").Append(Num(id)).Append("\" name=\"").Append(X.Esc(target[6..])).Append("\"/><pic:cNvPicPr/></pic:nvPicPr><pic:blipFill><a:blip r:embed=\"")
            .Append(rid).Append("\"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill><pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"").Append(cx).Append("\" cy=\"").Append(cy)
            .Append("\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr></pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r>");
    }

    // ---- tables ----------------------------------------------------------------------------------------------------------------

    private void Table(StringBuilder sb, HTable t, int depth)
    {
        sb.Append("<w:tbl><w:tblPr>");
        if (t.Rtl)
        {
            sb.Append("<w:bidiVisual/>");
        }

        if (t.WidthPercent50 > 0)
        {
            sb.Append("<w:tblW w:w=\"").Append(Num(t.WidthPercent50)).Append("\" w:type=\"pct\"/>");
        }
        else if (t.WidthTwips > 0)
        {
            sb.Append("<w:tblW w:w=\"").Append(Num(t.WidthTwips)).Append("\" w:type=\"dxa\"/>");
        }
        else
        {
            sb.Append("<w:tblW w:w=\"0\" w:type=\"auto\"/>");
        }

        if (t.Align is not null)
        {
            sb.Append("<w:jc w:val=\"").Append(t.Align).Append("\"/>");
        }

        if (t.IndentTwips > 0)
        {
            sb.Append("<w:tblInd w:w=\"").Append(Num(t.IndentTwips)).Append("\" w:type=\"dxa\"/>");
        }

        if (t.Border is { } bd)
        {
            sb.Append("<w:tblBorders>");
            foreach (string side in new[] { "top", "left", "bottom", "right" })
            {
                Side(sb, side, bd, 0);
            }

            sb.Append("</w:tblBorders>");
        }

        if (t.Shade is not null)
        {
            sb.Append("<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"").Append(t.Shade).Append("\"/>");
        }

        // Cell margins: the first cell's padding is the table default; a cell that differs says so itself.
        HCell? first = t.Rows.SelectMany(static r => r.Cells).FirstOrDefault(static c => !c.Continuation);
        int dt = first?.PadTop ?? 0, dr = first?.PadRight ?? 0, db = first?.PadBottom ?? 0, dl = first?.PadLeft ?? 0;
        sb.Append("<w:tblLayout w:type=\"").Append(t.WidthTwips > 0 || t.WidthPercent50 > 0 ? "fixed" : "autofit").Append("\"/>");
        sb.Append("<w:tblCellMar><w:top w:w=\"").Append(Num(dt)).Append("\" w:type=\"dxa\"/><w:left w:w=\"").Append(Num(dl)).Append("\" w:type=\"dxa\"/><w:bottom w:w=\"").Append(Num(db))
            .Append("\" w:type=\"dxa\"/><w:right w:w=\"").Append(Num(dr)).Append("\" w:type=\"dxa\"/></w:tblCellMar>");
        sb.Append("<w:tblLook w:val=\"04A0\" w:firstRow=\"1\" w:lastRow=\"0\" w:firstColumn=\"1\" w:lastColumn=\"0\" w:noHBand=\"0\" w:noVBand=\"1\"/></w:tblPr><w:tblGrid>");
        foreach (int g in t.Grid)
        {
            sb.Append("<w:gridCol w:w=\"").Append(Num(g)).Append("\"/>");
        }

        sb.Append("</w:tblGrid>");
        for (int r = 0; r < t.Rows.Count; r++)
        {
            HRow row = t.Rows[r];
            sb.Append("<w:tr>");
            if (row.Header || row.HeightTwips > 0)
            {
                sb.Append("<w:trPr>");
                if (row.HeightTwips > 0)
                {
                    sb.Append("<w:trHeight w:val=\"").Append(Num(row.HeightTwips)).Append("\"/>");
                }

                if (row.Header)
                {
                    sb.Append("<w:tblHeader/>");
                }

                sb.Append("</w:trPr>");
            }

            int col = 0;
            foreach (HCell cell in row.Cells)
            {
                int w = 0;
                for (int c = col; c < col + cell.ColSpan && c < t.Grid.Count; c++)
                {
                    w += t.Grid[c];
                }

                col += cell.ColSpan;
                sb.Append("<w:tc><w:tcPr><w:tcW w:w=\"").Append(Num(w)).Append("\" w:type=\"dxa\"/>");
                if (cell.ColSpan > 1)
                {
                    sb.Append("<w:gridSpan w:val=\"").Append(Num(cell.ColSpan)).Append("\"/>");
                }

                if (cell.Continuation)
                {
                    sb.Append("<w:vMerge/>");
                }
                else if (cell.RowSpan > 1)
                {
                    sb.Append("<w:vMerge w:val=\"restart\"/>");
                }

                if (!cell.Continuation)
                {
                    Borders(sb, "tcBorders", cell.BorderTop, cell.BorderLeft, cell.BorderBottom, cell.BorderRight, 0);
                    if (cell.Shade is not null)
                    {
                        sb.Append("<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"").Append(cell.Shade).Append("\"/>");
                    }

                    if (cell.PadTop >= 0 && (cell.PadTop != dt || cell.PadRight != dr || cell.PadBottom != db || cell.PadLeft != dl))
                    {
                        sb.Append("<w:tcMar><w:top w:w=\"").Append(Num(cell.PadTop)).Append("\" w:type=\"dxa\"/><w:left w:w=\"").Append(Num(cell.PadLeft)).Append("\" w:type=\"dxa\"/><w:bottom w:w=\"")
                            .Append(Num(cell.PadBottom)).Append("\" w:type=\"dxa\"/><w:right w:w=\"").Append(Num(cell.PadRight)).Append("\" w:type=\"dxa\"/></w:tcMar>");
                    }

                    if (cell.VAlign is not null)
                    {
                        sb.Append("<w:vAlign w:val=\"").Append(cell.VAlign).Append("\"/>");
                    }
                }

                sb.Append("</w:tcPr>");
                if (cell.Blocks.Count == 0)
                {
                    sb.Append("<w:p/>");
                }
                else
                {
                    Blocks(sb, cell.Blocks, depth + 1);
                }

                sb.Append("</w:tc>");
            }

            sb.Append("</w:tr>");
        }

        sb.Append("</w:tbl>");
    }

    // ---- styles and numbering --------------------------------------------------------------------------------------------------

    private string StylesXml()
    {
        HRunProps d = _c.DefaultRun;
        string font = X.Esc(d.Font ?? "Times New Roman");
        int size = d.SizeHalfPoints > 0 ? d.SizeHalfPoints : 24;
        var sb = new StringBuilder("<w:styles xmlns:w=\"" + WNs + "\"><w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii=\"").Append(font).Append("\" w:hAnsi=\"").Append(font).Append("\" w:cs=\"").Append(font)
            .Append("\"/>");
        if (d.Color is not null)
        {
            sb.Append("<w:color w:val=\"").Append(d.Color).Append("\"/>");
        }

        sb.Append("<w:sz w:val=\"").Append(Num(size)).Append("\"/><w:szCs w:val=\"").Append(Num(size)).Append("\"/>");
        if (_c.Lang is not null)
        {
            sb.Append("<w:lang w:val=\"").Append(X.Esc(_c.Lang)).Append("\"/>");
        }

        sb.Append("</w:rPr></w:rPrDefault><w:pPrDefault><w:pPr><w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/></w:pPr></w:pPrDefault></w:docDefaults>");
        sb.Append("<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/><w:qFormat/></w:style>");
        sb.Append("<w:style w:type=\"character\" w:default=\"1\" w:styleId=\"DefaultParagraphFont\"><w:name w:val=\"Default Paragraph Font\"/><w:uiPriority w:val=\"1\"/><w:semiHidden/><w:unhideWhenUsed/></w:style>");
        sb.Append("<w:style w:type=\"table\" w:default=\"1\" w:styleId=\"TableNormal\"><w:name w:val=\"Normal Table\"/><w:uiPriority w:val=\"99\"/><w:semiHidden/><w:unhideWhenUsed/><w:tblPr><w:tblInd w:w=\"0\" w:type=\"dxa\"/><w:tblCellMar><w:top w:w=\"0\" w:type=\"dxa\"/><w:left w:w=\"108\" w:type=\"dxa\"/><w:bottom w:w=\"0\" w:type=\"dxa\"/><w:right w:w=\"108\" w:type=\"dxa\"/></w:tblCellMar></w:tblPr></w:style>");
        sb.Append("<w:style w:type=\"numbering\" w:default=\"1\" w:styleId=\"NoList\"><w:name w:val=\"No List\"/><w:uiPriority w:val=\"99\"/><w:semiHidden/><w:unhideWhenUsed/></w:style>");
        foreach (HStyle s in _c.Styles.Values.OrderBy(static v => v.BasedOn is null ? 0 : 1).ThenBy(static v => v.Id, StringComparer.Ordinal))
        {
            sb.Append("<w:style w:type=\"paragraph\" w:styleId=\"").Append(s.Id).Append("\"><w:name w:val=\"").Append(X.Esc(s.Name)).Append("\"/><w:basedOn w:val=\"").Append(s.BasedOn ?? "Normal")
                .Append("\"/><w:next w:val=\"Normal\"/><w:qFormat/>");
            var pp = new StringBuilder();
            HParaProps p = s.Para;
            if (p.KeepNext)
            {
                pp.Append("<w:keepNext/>");
            }

            Borders(pp, "pBdr", p.BorderTop, p.BorderLeft, p.BorderBottom, p.BorderRight, 1);
            if (p.Shade is not null)
            {
                pp.Append("<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"").Append(p.Shade).Append("\"/>");
            }

            Spacing(pp, p with { Before = 0 });
            if (p.Align is not null)
            {
                pp.Append("<w:jc w:val=\"").Append(p.Align).Append("\"/>");
            }

            if (s.Id.StartsWith("Heading", StringComparison.Ordinal) && s.Id.Length == 8 && char.IsAsciiDigit(s.Id[7]))
            {
                pp.Append("<w:outlineLvl w:val=\"").Append(Num(s.Id[7] - '1')).Append("\"/>");
            }

            if (pp.Length > 0)
            {
                sb.Append("<w:pPr>").Append(pp).Append("</w:pPr>");
            }

            RPr(sb, s.Run, d);
            sb.Append("</w:style>");
        }

        return sb.Append("</w:styles>").ToString();
    }

    private static string BulletChar(string bullet, int level)
    {
        string kind = bullet is "circle" or "square" or "none" ? bullet : level % 3 == 1 ? "circle" : level % 3 == 2 ? "square" : "disc";
        return kind;
    }

    private string NumberingXml()
    {
        var nums = new StringBuilder();
        foreach (HList l in _c.Lists)
        {
            string key = l.Format + "|" + l.Bullet + "|" + Num(l.Depth);
            if (!_abstractByKey.TryGetValue(key, out int aid))
            {
                aid = _abstractXml.Count;
                _abstractByKey[key] = aid;
                _abstractXml.Add(AbstractNum(aid, l));
            }

            nums.Append("<w:num w:numId=\"").Append(Num(l.Id)).Append("\"><w:abstractNumId w:val=\"").Append(Num(aid)).Append("\"/>");
            if (l.Ordered && l.Start != 1)
            {
                nums.Append("<w:lvlOverride w:ilvl=\"").Append(Num(Math.Min(l.Depth, 8))).Append("\"><w:startOverride w:val=\"").Append(Num(l.Start)).Append("\"/></w:lvlOverride>");
            }

            nums.Append("</w:num>");
        }

        return "<w:numbering xmlns:w=\"" + WNs + "\">" + string.Concat(_abstractXml) + nums + "</w:numbering>";
    }

    private static string AbstractNum(int id, HList l)
    {
        var sb = new StringBuilder("<w:abstractNum w:abstractNumId=\"").Append(Num(id)).Append("\"><w:multiLevelType w:val=\"hybridMultilevel\"/>");
        string[] ordered = ["decimal", "lowerLetter", "lowerRoman"];
        for (int i = 0; i < 9; i++)
        {
            string fmt;
            string text;
            string? font = null;
            bool own = i == l.Depth;
            bool isOrdered = own ? l.Ordered : l.Ordered;
            if (isOrdered)
            {
                fmt = own ? l.Format : ordered[i % 3];
                text = "%" + Num(i + 1) + ".";
            }
            else
            {
                fmt = "bullet";
                string kind = BulletChar(own ? l.Bullet : "disc", i);
                (text, font) = kind switch
                {
                    "circle" => ("o", "Courier New"),
                    "square" => ("\uF0A7", "Wingdings"),
                    "none" => (string.Empty, null),
                    _ => ("\uF0B7", "Symbol"),
                };
                if (kind == "none")
                {
                    fmt = "none";
                }
            }

            sb.Append("<w:lvl w:ilvl=\"").Append(Num(i)).Append("\"><w:start w:val=\"1\"/><w:numFmt w:val=\"").Append(fmt).Append("\"/><w:lvlText w:val=\"").Append(X.Esc(text))
                .Append("\"/><w:lvlJc w:val=\"left\"/><w:pPr><w:ind w:left=\"").Append(Num(720 * (i + 1))).Append("\" w:hanging=\"360\"/></w:pPr>");
            if (font is not null)
            {
                sb.Append("<w:rPr><w:rFonts w:ascii=\"").Append(font).Append("\" w:hAnsi=\"").Append(font).Append("\" w:hint=\"default\"/></w:rPr>");
            }

            sb.Append("</w:lvl>");
        }

        return sb.Append("</w:abstractNum>").ToString();
    }
}
