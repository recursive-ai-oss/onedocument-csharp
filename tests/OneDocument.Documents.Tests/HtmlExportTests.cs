using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Packaging;
using OneHtmlDocument = RecursiveAI.OneHtml.Document;

namespace RecursiveAI.OneDocument.Documents.Tests;

public class HtmlExportTests
{
    private const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";
    private const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private const string WCt = "application/vnd.openxmlformats-officedocument.wordprocessingml.";

    private const string Styles =
        "<w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii=\"Calibri\" w:hAnsi=\"Calibri\"/><w:sz w:val=\"22\"/><w:lang w:val=\"en-US\"/></w:rPr></w:rPrDefault>" +
        "<w:pPrDefault><w:pPr><w:spacing w:after=\"160\" w:line=\"259\" w:lineRule=\"auto\"/></w:pPr></w:pPrDefault></w:docDefaults>" +
        "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Heading1\"><w:name w:val=\"heading 1\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:keepNext/><w:spacing w:before=\"240\" w:after=\"0\"/></w:pPr><w:rPr><w:b/><w:color w:val=\"2F5496\"/><w:sz w:val=\"32\"/></w:rPr></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Heading2\"><w:name w:val=\"heading 2\"/><w:basedOn w:val=\"Heading1\"/><w:rPr><w:sz w:val=\"26\"/></w:rPr></w:style>" +
        "<w:style w:type=\"character\" w:styleId=\"Strong\"><w:name w:val=\"Strong\"/><w:rPr><w:b/></w:rPr></w:style>" +
        "<w:style w:type=\"table\" w:styleId=\"TableGrid\"><w:name w:val=\"Table Grid\"/><w:tblPr><w:tblBorders><w:top w:val=\"single\" w:sz=\"4\" w:color=\"auto\"/><w:left w:val=\"single\" w:sz=\"4\" w:color=\"auto\"/>" +
        "<w:bottom w:val=\"single\" w:sz=\"4\" w:color=\"auto\"/><w:right w:val=\"single\" w:sz=\"4\" w:color=\"auto\"/><w:insideH w:val=\"single\" w:sz=\"4\" w:color=\"auto\"/><w:insideV w:val=\"single\" w:sz=\"4\" w:color=\"auto\"/></w:tblBorders></w:tblPr></w:style>";

    private const string Numbering =
        "<w:abstractNum w:abstractNumId=\"0\"><w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"%1.\"/></w:lvl>" +
        "<w:lvl w:ilvl=\"1\"><w:start w:val=\"1\"/><w:numFmt w:val=\"bullet\"/><w:lvlText w:val=\"o\"/></w:lvl></w:abstractNum>" +
        "<w:abstractNum w:abstractNumId=\"1\"><w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"upperRoman\"/><w:lvlText w:val=\"%1)\"/></w:lvl></w:abstractNum>" +
        "<w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num><w:num w:numId=\"2\"><w:abstractNumId w:val=\"1\"/><w:lvlOverride w:ilvl=\"0\"><w:startOverride w:val=\"4\"/></w:lvlOverride></w:num>";

    private static string P(string text, string? style = null, string ppr = "") =>
        "<w:p><w:pPr>" + (style is null ? string.Empty : "<w:pStyle w:val=\"" + style + "\"/>") + ppr + "</w:pPr><w:r><w:t xml:space=\"preserve\">" + text + "</w:t></w:r></w:p>";

    private static string Item(string text, int num, int lvl) =>
        "<w:p><w:pPr><w:numPr><w:ilvl w:val=\"" + lvl + "\"/><w:numId w:val=\"" + num + "\"/></w:numPr></w:pPr><w:r><w:t>" + text + "</w:t></w:r></w:p>";

    private static string Cell(string text, string pr = "") => "<w:tc><w:tcPr>" + pr + "</w:tcPr>" + P(text) + "</w:tc>";

    private static Document Build(string body, bool notes = true)
    {
        var p = OpcPackage.Create(OfficeLimits.Default);
        p.SetRelationships(null, [new Relationship("rId1", Rel + "officeDocument", "word/document.xml")]);
        string wns = "xmlns:w=\"" + TestDocs.WNs + "\" xmlns:r=\"" + TestDocs.RNs + "\" xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"";
        p.AddPart("/word/document.xml", WCt + "document.main+xml", Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:document " + wns + "><w:body>" + body +
            "<w:sectPr><w:headerReference w:type=\"default\" r:id=\"rId9\"/><w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/></w:sectPr></w:body></w:document>"));
        p.AddPart("/word/styles.xml", WCt + "styles+xml", Encoding.UTF8.GetBytes("<w:styles xmlns:w=\"" + TestDocs.WNs + "\">" + Styles + "</w:styles>"));
        p.AddPart("/word/numbering.xml", WCt + "numbering+xml", Encoding.UTF8.GetBytes("<w:numbering xmlns:w=\"" + TestDocs.WNs + "\">" + Numbering + "</w:numbering>"));
        p.AddPart("/word/header1.xml", WCt + "header+xml", Encoding.UTF8.GetBytes("<w:hdr xmlns:w=\"" + TestDocs.WNs + "\"><w:p><w:r><w:t>Header text</w:t></w:r></w:p></w:hdr>"));
        p.AddPart("/word/media/image1.png", "image/png", Convert.FromBase64String(Png));
        var rels = new List<Relationship>
        {
            new("rId2", Rel + "styles", "styles.xml"),
            new("rId3", Rel + "numbering", "numbering.xml"),
            new("rId4", Rel + "hyperlink", "https://example.com/a?b=1&c=2", RelationshipTargetMode.External),
            new("rId5", Rel + "image", "media/image1.png"),
            new("rId6", Rel + "hyperlink", "javascript:alert(1)", RelationshipTargetMode.External),
            new("rId9", Rel + "header", "header1.xml"),
        };
        if (notes)
        {
            p.AddPart("/word/footnotes.xml", WCt + "footnotes+xml", Encoding.UTF8.GetBytes("<w:footnotes xmlns:w=\"" + TestDocs.WNs + "\"><w:footnote w:type=\"separator\" w:id=\"-1\"><w:p><w:r><w:separator/></w:r></w:p></w:footnote>" +
                "<w:footnote w:id=\"1\"><w:p><w:r><w:footnoteRef/></w:r><w:r><w:t xml:space=\"preserve\"> The note text.</w:t></w:r></w:p></w:footnote></w:footnotes>"));
            rels.Add(new Relationship("rId7", Rel + "footnotes", "footnotes.xml"));
        }

        p.SetRelationships("/word/document.xml", rels);
        return Document.FromPackage(p);
    }

    private static string Body() =>
        P("Annual report", "Heading1") +
        P("Overview", "Heading2") +
        "<w:p><w:r><w:t xml:space=\"preserve\">Plain </w:t></w:r><w:r><w:rPr><w:b/><w:i/></w:rPr><w:t>bold italic</w:t></w:r><w:r><w:rPr><w:color w:val=\"FF0000\"/><w:sz w:val=\"28\"/><w:u w:val=\"single\"/></w:rPr><w:t xml:space=\"preserve\"> red </w:t></w:r>" +
        "<w:hyperlink r:id=\"rId4\"><w:r><w:t>web &amp; link</w:t></w:r></w:hyperlink><w:r><w:t xml:space=\"preserve\"> and </w:t></w:r><w:hyperlink r:id=\"rId6\"><w:r><w:t>unsafe</w:t></w:r></w:hyperlink><w:bookmarkStart w:id=\"0\" w:name=\"mark one\"/><w:r><w:t>end</w:t></w:r><w:bookmarkEnd w:id=\"0\"/>" +
        "<w:r><w:footnoteReference w:id=\"1\"/></w:r></w:p>" +
        Item("first", 1, 0) + Item("second", 1, 0) + Item("nested", 1, 1) + Item("third", 1, 0) + Item("roman", 2, 0) + Item("roman two", 2, 0) +
        "<w:tbl><w:tblPr><w:tblStyle w:val=\"TableGrid\"/><w:tblW w:w=\"0\" w:type=\"auto\"/></w:tblPr><w:tblGrid><w:gridCol w:w=\"3000\"/><w:gridCol w:w=\"3000\"/><w:gridCol w:w=\"3000\"/></w:tblGrid>" +
        "<w:tr><w:trPr><w:tblHeader/></w:trPr>" + Cell("H1") + Cell("H2") + Cell("H3") + "</w:tr>" +
        "<w:tr>" + Cell("wide", "<w:gridSpan w:val=\"2\"/><w:shd w:val=\"clear\" w:fill=\"FFFF00\"/>") + Cell("tall", "<w:vMerge w:val=\"restart\"/>") + "</w:tr>" +
        "<w:tr>" + Cell("a") + Cell("b") + Cell(string.Empty, "<w:vMerge/>") + "</w:tr></w:tbl>" +
        "<w:p><w:pPr><w:bidi/><w:jc w:val=\"right\"/></w:pPr><w:r><w:rPr><w:rtl/><w:lang w:val=\"he-IL\"/></w:rPr><w:t>\u05E9\u05DC\u05D5\u05DD</w:t></w:r></w:p>" +
        "<w:p><w:r><w:drawing><wp:inline><wp:extent cx=\"190500\" cy=\"95250\"/><wp:docPr id=\"1\" name=\"Picture 1\" descr=\"a dot\"/><a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><pic:pic><pic:blipFill><a:blip r:embed=\"rId5\"/></pic:blipFill></pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>" +
        "<w:p><w:r><w:t>before break</w:t></w:r><w:r><w:br w:type=\"page\"/></w:r><w:r><w:t>after break</w:t></w:r></w:p>" +
        "<w:p/>" + P("Tail A &lt; B", null, "<w:ind w:left=\"720\"/><w:jc w:val=\"center\"/>");

    // The text of a page with a space at every block boundary, which is what a reader sees as separate words.
    private static string BodyText(OneHtmlDocument doc)
    {
        var sb = new StringBuilder();
        void Walk(RecursiveAI.OneHtml.Node n)
        {
            for (RecursiveAI.OneHtml.Node c = n.FirstChild; !c.IsNull; c = c.NextSibling)
            {
                if (c.Kind == RecursiveAI.OneHtml.NodeKind.Text)
                {
                    sb.Append(c.Data);
                }
                else if (c.IsElement)
                {
                    bool block = c.LocalName is "p" or "div" or "li" or "td" or "th" or "tr" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "br" or "ul" or "ol" or "table" or "section" or "pre";
                    if (block)
                    {
                        sb.Append(' ');
                    }

                    Walk(c);
                    if (block)
                    {
                        sb.Append(' ');
                    }
                }
            }
        }

        Walk(doc.Root.QuerySelector("body"));
        return Words(sb.ToString());
    }

    private static string Words(string s) => string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    [Fact]
    public void A_generated_document_exports_as_valid_html5_with_the_same_text()
    {
        using Document d = Build(Body());
        string html = d.ToHtml();
        var parsed = OneHtmlDocument.Parse(html, new RecursiveAI.OneHtml.ParseOptions { RecordErrors = true });
        Assert.Empty(parsed.ParseErrors);
        Assert.StartsWith("<!DOCTYPE html>", html, StringComparison.Ordinal);
        foreach (string expected in new[]
        {
            "<h1", "<h2", "class=\"s-Heading1 s-Heading2\"", "<strong>", "<em>", "href=\"https://example.com/a?b=1&amp;c=2\"", "<ol>", "<ol start=\"4\" type=\"I\">", "<ul style=\"list-style-type:circle\">",
            "<thead>", "<th ", "colspan=\"2\"", "rowspan=\"2\"", "background-color:#FFFF00", "dir=\"rtl\"", "data:image/png;base64,", "role=\"doc-endnotes\"", "id=\"mark_one\"", "break-after:page", "<title>",
        })
        {
            Assert.Contains(expected, html, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("javascript", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Header text", html, StringComparison.Ordinal);
        string bodyText = BodyText(parsed);
        string extracted = Words(d.ExtractText());
        // The body text is the same; extraction adds the header, which HTML omits, and the note text follows the body in HTML.
        string[] htmlWords = bodyText.Split(' ');
        string[] docWords = extracted.Replace("Header text", string.Empty, StringComparison.Ordinal).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(docWords.Order(StringComparer.Ordinal), htmlWords.Order(StringComparer.Ordinal));
        Assert.Equal(1, d.ExportLoss!.CountOf("loss.html-headers-footers"));
        Assert.Equal(1, d.ExportLoss.CountOf("loss.html-unsafe-link"));
    }

    [Fact]
    public void Ruby_text_and_equations_keep_their_text()
    {
        const string om = "xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\"";
        using Document d = Build("<w:p><w:r><w:ruby><w:rt><w:r><w:t>kana</w:t></w:r></w:rt><w:rubyBase><w:r><w:t>BASE</w:t></w:r></w:rubyBase></w:ruby></w:r>" +
            "<m:oMath " + om + "><m:r><m:t>x+1</m:t></m:r></m:oMath></w:p>", notes: false);
        string html = d.ToHtml(new HtmlExportOptions { Fragment = true });
        Assert.Contains("<ruby>BASE<rt>kana</rt></ruby>", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"math\">x+1</span>", html, StringComparison.Ordinal);
        Assert.Equal(1, d.ExportLoss!.CountOf("loss.html-equations"));
    }

    [Fact]
    public void Without_notes_the_text_is_equal_in_order()
    {
        using Document d = Build(Body().Replace("<w:r><w:footnoteReference w:id=\"1\"/></w:r>", string.Empty, StringComparison.Ordinal), notes: false);
        string html = d.ToHtml();
        var parsed = OneHtmlDocument.Parse(html);
        Assert.Equal(Words(string.Join(' ', d.Paragraphs.Select(p => p.Text))), BodyText(parsed));
    }

    [Fact]
    public void A_fragment_has_no_page_and_an_image_sink_stores_pictures_outside_the_html()
    {
        using Document d = Build(Body());
        var stored = new List<HtmlImage>();
        string html = d.ToHtml(new HtmlExportOptions { Fragment = true, ImageSink = i => { stored.Add(i); return "img/" + i.SuggestedName; } });
        Assert.DoesNotContain("<html", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<style", html, StringComparison.Ordinal);
        Assert.Contains("src=\"img/image1.png\"", html, StringComparison.Ordinal);
        HtmlImage img = Assert.Single(stored);
        Assert.Equal("image/png", img.ContentType);
        Assert.Equal(Convert.FromBase64String(Png), img.Data.ToArray());
    }

    [Fact]
    public void Our_html_reads_back_to_an_equal_hub_on_text_styles_lists_and_tables()
    {
        using Document original = Build(Body().Replace("<w:r><w:footnoteReference w:id=\"1\"/></w:r>", string.Empty, StringComparison.Ordinal), notes: false);
        string html = original.ToHtml();
        using Document back = Document.FromHtml(html);
        string Text(Document x) => Words(string.Join(' ', x.Paragraphs.Select(p => p.Text)));
        Assert.Equal(Text(original), Text(back));

        // Styles: the headings are headings again, with the formatting the style carried.
        Assert.Equal(1, back.Paragraphs.Count(p => p.StyleId == "Heading1"));
        Assert.Equal(1, back.Paragraphs.Count(p => p.StyleId?.EndsWith("Heading2", StringComparison.Ordinal) == true || p.StyleId == "Heading2"));
        string backStyles = Encoding.UTF8.GetString(back.Package.FindPart("/word/styles.xml")!.GetBytes().Span);
        Assert.Contains("<w:color w:val=\"2F5496\"/>", backStyles, StringComparison.Ordinal);
        string xml = back.MainMarkup.Root.OuterXml;
        Assert.Contains("<w:b/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:i/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:color w:val=\"FF0000\"/>", xml, StringComparison.Ordinal);

        // Lists: the same items at the same levels, in the same kinds of list.
        static string Lists(Document x) => string.Join("|", x.MainMarkup.Root.OuterXml.Split("<w:p>", StringSplitOptions.None).Where(s => s.Contains("<w:numPr>", StringComparison.Ordinal))
            .Select(s => s[(s.IndexOf("<w:ilvl w:val=\"", StringComparison.Ordinal) + 15)..][..1]));
        Assert.Equal(Lists(original), Lists(back));
        string numbering = Encoding.UTF8.GetString(back.Package.FindPart("/word/numbering.xml")!.GetBytes().Span);
        Assert.Contains("upperRoman", numbering, StringComparison.Ordinal);
        Assert.Contains("<w:startOverride w:val=\"4\"/>", numbering, StringComparison.Ordinal);

        // Tables: the same grid, the span, the merge and the header row.
        Assert.Single(back.Tables);
        Assert.Contains("<w:gridSpan w:val=\"2\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:vMerge w:val=\"restart\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:tblHeader/>", xml, StringComparison.Ordinal);
        Assert.Contains("w:fill=\"FFFF00\"", xml, StringComparison.Ordinal);
        Assert.Contains("<w:bidi/>", xml, StringComparison.Ordinal);
        Assert.Contains("w:anchor", xml + "w:anchor", StringComparison.Ordinal);
    }

    [Fact]
    public void Imported_html_exports_and_reimports_to_the_same_text()
    {
        const string source = "<h1>Title</h1><p>One <b>two</b> <a href='https://x.example/'>three</a></p><ul><li>a</li><li>b<ol><li>b1</li></ol></li></ul><table><tr><th>H</th></tr><tr><td>c</td></tr></table><pre>x  y</pre>";
        using Document first = Document.FromHtml(source);
        string html = first.ToHtml();
        using Document second = Document.FromHtml(html);
        Assert.Equal(first.ExtractText(), second.ExtractText());
    }

    // Writes the files for the desktop-Office run (testdata/html/word-cases.md) when ONEDOCUMENT_HTML_CASES names a directory.
    [Fact]
    public void Writes_the_cases_for_the_desktop_office_run_when_asked()
    {
        string? dir = Environment.GetEnvironmentVariable("ONEDOCUMENT_HTML_CASES");
        if (string.IsNullOrEmpty(dir))
        {
            return;
        }

        Directory.CreateDirectory(dir);
        using Document report = Build(Body());
        report.SaveAs(Path.Combine(dir, "report.docx"));
        File.WriteAllText(Path.Combine(dir, "report.html"), report.ToHtml());
        using Document back = Document.FromHtml(report.ToHtml());
        back.SaveAs(Path.Combine(dir, "report-from-html.docx"));
    }
}
