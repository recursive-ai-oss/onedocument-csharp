using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Documents;

namespace RecursiveAI.OneDocument.Documents.Tests;

public class HtmlImportTests
{
    private static Document Import(string html, HtmlImportOptions? options = null) => Document.FromHtml(html, options);

    private static string Xml(Document d) => d.MainMarkup.Root.OuterXml;

    private static string Styles(Document d) => Encoding.UTF8.GetString(d.Package.FindPart("/word/styles.xml")!.GetBytes().Span);

    private static string Numbering(Document d) => Encoding.UTF8.GetString(d.Package.FindPart("/word/numbering.xml")!.GetBytes().Span);

    private static string Text(Document d) => d.ExtractText().Replace("\r\n", "\n", StringComparison.Ordinal);

    [Fact]
    public void Headings_paragraphs_and_inline_formatting_map_to_styles_and_runs()
    {
        using Document d = Import("<h1>Title</h1><p>Plain <b>bold</b> <i>it</i> <u>und</u> <s>gone</s> <span style='color:#ff0000;font-size:20pt;font-family:Arial'>red</span></p><h2>Sub</h2>");
        string xml = Xml(d);
        Assert.Contains("<w:pStyle w:val=\"Heading1\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:pStyle w:val=\"Heading2\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:b/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:i/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:u w:val=\"single\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:strike/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:color w:val=\"FF0000\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:sz w:val=\"40\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("w:ascii=\"Arial\"", xml, StringComparison.Ordinal);
        string styles = Styles(d);
        Assert.Contains("w:styleId=\"Heading1\"", styles, StringComparison.Ordinal);
        Assert.Contains("<w:outlineLvl w:val=\"0\"/>", styles, StringComparison.Ordinal);
        Assert.Equal("Title\nPlain bold it und gone red\nSub\n", Text(d));
        Assert.Equal(OfficeFormat.Html, d.Format);
    }

    [Fact]
    public void Whitespace_collapses_and_pre_keeps_it()
    {
        using Document d = Import("<p>  a \n\t b   <b> c </b>  d  </p><pre>x  y\n  z\tw</pre><p><b>x</b> y <i>z</i></p>");
        Assert.Equal("a b c d\nx  y\n  z\tw\nx y z\n", Text(d));
    }

    [Fact]
    public void Lists_nest_and_keep_start_and_type()
    {
        using Document d = Import("<ul><li>one<ul><li>deep</li></ul></li><li>two</li></ul><ol start='5' type='a'><li>x</li><li>y</li></ol><ol><li>r</li></ol>");
        string xml = Xml(d);
        Assert.Equal(6, Count(xml, "<w:numPr>"));
        Assert.Contains("<w:ilvl w:val=\"1\"/>", xml, StringComparison.Ordinal);
        string num = Numbering(d);
        Assert.Contains("<w:numFmt w:val=\"bullet\"/>", num, StringComparison.Ordinal);
        Assert.Contains("<w:numFmt w:val=\"lowerLetter\"/>", num, StringComparison.Ordinal);
        Assert.Contains("<w:startOverride w:val=\"5\"/>", num, StringComparison.Ordinal);
        Assert.Equal("one\ndeep\ntwo\nx\ny\nr\n", Text(d));
    }

    [Fact]
    public void Tables_keep_spans_header_rows_widths_borders_and_shading()
    {
        using Document d = Import("<table border='1' style='width:400px'><thead><tr><th>H1</th><th>H2</th><th>H3</th></tr></thead><tbody>" +
            "<tr><td colspan='2' style='background-color:#ffff00'>wide</td><td rowspan='2'>tall</td></tr><tr><td>a</td><td>b</td></tr></tbody></table>");
        string xml = Xml(d);
        Assert.Contains("<w:tblHeader/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:gridSpan w:val=\"2\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:vMerge w:val=\"restart\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:vMerge/>", xml, StringComparison.Ordinal);
        Assert.Contains("w:fill=\"FFFF00\"", xml, StringComparison.Ordinal);
        Assert.Contains("<w:tblBorders>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:tblW w:w=\"6000\" w:type=\"dxa\"/>", xml, StringComparison.Ordinal);
        Assert.Equal(3, Count(xml, "<w:gridCol "));
        Assert.Single(d.Tables);
    }

    [Fact]
    public void Links_anchors_and_images()
    {
        const string png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";
        using Document d = Import("<p id='top'><a href='https://example.com/x?a=1&b=2'>web</a> <a href='#sec'>jump</a></p><h2 id='sec'>Here</h2><p><img src='data:image/png;base64," + png + "' alt='dot' width='20' height='10'></p>");
        string xml = Xml(d);
        Assert.Contains(d.Package.GetRelationships("/word/document.xml"), r => r.Target == "https://example.com/x?a=1&b=2" && r.TargetMode == Packaging.RelationshipTargetMode.External);
        Assert.Contains("w:anchor=\"sec\"", xml, StringComparison.Ordinal);
        Assert.Contains("<w:bookmarkStart w:id=\"1\" w:name=\"sec\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("<wp:extent cx=\"190500\" cy=\"95250\"/>", xml, StringComparison.Ordinal);
        Assert.NotNull(d.Package.FindPart("/word/media/image1.png"));
        Assert.Contains("descr=\"dot\"", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Quotes_code_breaks_rules_page_breaks_direction_and_language()
    {
        using Document d = Import("<html lang='fr'><body><blockquote>quoted</blockquote><p>a<br>b</p><hr><p style='page-break-before:always'>next</p><p dir='rtl'>\u05E9\u05DC\u05D5\u05DD</p><p>use <code>x = 1</code></p></body></html>");
        string xml = Xml(d);
        Assert.Contains("<w:pStyle w:val=\"Quote\"/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:br/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:pBdr><w:bottom ", xml, StringComparison.Ordinal);
        Assert.Contains("<w:pageBreakBefore/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:bidi/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:rtl/>", xml, StringComparison.Ordinal);
        Assert.Contains("w:ascii=\"Courier New\"", xml, StringComparison.Ordinal);
        Assert.Contains("<w:lang w:val=\"fr\"", Styles(d), StringComparison.Ordinal);
    }

    [Fact]
    public void A_class_that_repeats_becomes_a_named_style()
    {
        using Document d = Import("<style>.note{color:#336699;font-style:italic;text-align:center}</style><p class='note'>one</p><p class='note'>two</p><p>three</p>");
        string styles = Styles(d);
        Assert.Contains("w:styleId=\"note\"", styles, StringComparison.Ordinal);
        string xml = Xml(d);
        Assert.Equal(2, Count(xml, "<w:pStyle w:val=\"note\"/>"));
        Assert.DoesNotContain("<w:i/>", xml, StringComparison.Ordinal);
        Assert.Contains("<w:color w:val=\"336699\"/>", styles, StringComparison.Ordinal);
        Assert.Contains("<w:jc w:val=\"center\"/>", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void Hostile_markup_never_reaches_the_document_and_is_reported()
    {
        const string html = "<p onclick='evil()'>Hi <b onmouseover='x()'>there</b></p><script>alert(1)</script><a href='javascript:alert(1)'>bad</a> <a href=' JaVa\tScRiPt:alert(2)'>bad2</a>" +
            "<a href='vbscript:x'>bad3</a><a href='data:text/html;base64,AAAA'>bad4</a><form action='/x'><input name='q' value='secret'><button>Go</button>label</form>" +
            "<iframe src='https://evil.example/'>fallback</iframe><object data='x.swf'>obj</object><embed src='y'><img src='https://evil.example/pixel.png' alt='tracker'>" +
            "<svg><text>vector</text></svg><style>p{background:url(https://evil.example/bg.png)}</style><link rel='stylesheet' href='https://evil.example/s.css'>";
        var defects = new DefectCollection();
        using Document d = Document.FromHtml(html, null, new OpenContext(defects: defects));
        Assert.Contains(defects, x => x.Code == "html.dropped" && x.Severity == DefectSeverity.Dropped && x.Location.Offset >= 0);
        string xml = Xml(d);
        string all = xml + Styles(d);
        foreach (string bad in new[] { "script", "javascript", "vbscript", "onclick", "onmouseover", "evil", "alert", "<w:object", "secret", "fallback", "vector" })
        {
            Assert.DoesNotContain(bad, all, StringComparison.OrdinalIgnoreCase);
        }

        foreach (Packaging.Relationship r in d.Package.GetRelationships("/word/document.xml"))
        {
            Assert.DoesNotContain("evil", r.Target, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("script", r.Target, StringComparison.OrdinalIgnoreCase);
        }

        string text = Text(d);
        Assert.Contains("Hi there", text, StringComparison.Ordinal);
        Assert.Contains("bad4", text, StringComparison.Ordinal);
        Assert.Contains("label", text, StringComparison.Ordinal);
        Assert.Contains("tracker", text, StringComparison.Ordinal);
        LossReport loss = d.ImportLoss!;
        foreach (string code in new[]
        {
            "loss.html-script", "loss.html-event-handler", "loss.html-unsafe-link", "loss.html-form", "loss.html-form-control", "loss.html-embedded-content",
            "loss.html-external-image", "loss.html-svg", "loss.html-linked-stylesheet",
        })
        {
            Assert.True(loss.CountOf(code) > 0, code);
        }

        Assert.Equal(4, loss.CountOf("loss.html-unsafe-link"));
    }

    [Fact]
    public void KeepInert_keeps_form_text_and_unknown_schemes_but_never_script_targets()
    {
        using Document d = Import("<form><input value='kept'><button>Press</button></form><a href='skype:someone'>call</a><a href='javascript:x'>no</a><iframe>frame text</iframe>",
            new HtmlImportOptions { Sanitization = HtmlSanitization.KeepInert });
        string xml = Xml(d);
        Assert.Contains("kept", xml, StringComparison.Ordinal);
        Assert.Contains("frame text", xml, StringComparison.Ordinal);
        Assert.Contains(d.Package.GetRelationships("/word/document.xml"), r => r.Target == "skype:someone");
        Assert.DoesNotContain(d.Package.GetRelationships("/word/document.xml"), r => r.Target.Contains("javascript", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void An_image_resolver_supplies_external_images_and_nothing_else_is_fetched()
    {
        const string png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";
        var asked = new List<string>();
        using Document d = Import("<img src='pic.png'><img src='http://other/x.png' alt='no'>", new HtmlImportOptions
        {
            ImageResolver = src =>
            {
                asked.Add(src);
                return src == "pic.png" ? new HtmlResource(Convert.FromBase64String(png)) : null;
            },
        });
        Assert.Equal(["pic.png", "http://other/x.png"], asked);
        Assert.Single(d.Package.Parts, p => p.Name.StartsWith("/word/media/", StringComparison.Ordinal));
        Assert.Equal(1, d.ImportLoss!.CountOf("loss.html-external-image"));
    }

    [Fact]
    public void Deep_nesting_and_huge_inputs_are_bounded()
    {
        using (Document d = Import(string.Concat(Enumerable.Repeat("<div>", 200)) + "x"))
        {
            Assert.Contains('x', Text(d));
        }

        OfficeLimitException deep = Assert.Throws<OfficeLimitException>(() => Import(string.Concat(Enumerable.Repeat("<div>", 5000)) + "x"));
        Assert.Equal(nameof(OfficeLimits.MaxXmlDepth), deep.LimitName);
        var ctx = new OpenContext(new OfficeLimits { MaxPartSize = 1000 });
        Assert.Throws<OfficeLimitException>(() => Document.FromHtml(new string('a', 5000), null, ctx));
    }

    [Fact]
    public void Html_opens_by_content_and_converts_through_the_statics()
    {
        string dir = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath(), "html-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string html = Path.Combine(dir, "a.dat");
            File.WriteAllText(html, "<!DOCTYPE html><html><head><title>T</title></head><body><h1>Hello</h1><p>World</p></body></html>");
            DocumentFormats.Register(RecursiveAI.OneDocument.Formats.FormatRegistry.Default);
            using (Document d = Document.Open(html))
            {
                Assert.Equal(OfficeFormat.Html, d.Format);
                Assert.Equal("Hello\nWorld\n", Text(d));
            }

            string docx = Path.Combine(dir, "a.docx");
            Office.Convert(html, docx);
            using (Document d = Document.Open(docx))
            {
                Assert.Equal(OfficeFormat.Docx, d.Format);
                Assert.Equal("Hello\nWorld\n", Text(d));
            }

            string back = Path.Combine(dir, "b.html");
            Office.Convert(docx, back);
            string text = File.ReadAllText(back);
            Assert.StartsWith("<!DOCTYPE html>", text, StringComparison.Ordinal);
            Assert.Contains("<h1", text, StringComparison.Ordinal);
            Assert.Equal("Hello\nWorld\n", Office.ExtractText(back).Replace("\r\n", "\n", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static int Count(string s, string needle)
    {
        int n = 0;
        for (int i = s.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = s.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            n++;
        }

        return n;
    }
}
