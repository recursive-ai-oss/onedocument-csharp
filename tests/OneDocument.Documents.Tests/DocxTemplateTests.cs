using System.Text.Json;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.Templating;

namespace RecursiveAI.OneDocument.Documents.Tests;

public class DocxTemplateTests
{
    private const string W = TestDocs.WNs;

    private static string Render(string body, object? data, TemplateOptions? options = null, Action<Document>? check = null)
    {
        DocumentFormats.Register(FormatRegistry.Default);
        using Document doc = TestDocs.Open(body);
        Template.Render(doc, data, options);
        check?.Invoke(doc);
        return doc.MainMarkup.Root.OuterXml;
    }

    private static JsonDocument Json(string json) => JsonDocument.Parse(json);

    private static string P(string text) => "<w:p><w:r><w:t xml:space=\"preserve\">" + text + "</w:t></w:r></w:p>";

    private static string Texts(string xml)
    {
        var sb = new System.Text.StringBuilder();
        int i = 0;
        while (true)
        {
            int p = xml.IndexOf("<w:p>", i, StringComparison.Ordinal);
            int p2 = xml.IndexOf("<w:p ", i, StringComparison.Ordinal);
            if (p < 0 || (p2 >= 0 && p2 < p))
            {
                p = p2;
            }

            if (p < 0)
            {
                break;
            }

            int end = xml.IndexOf("</w:p>", p, StringComparison.Ordinal);
            string para = xml[p..end];
            var text = new System.Text.StringBuilder();
            int t = 0;
            while ((t = para.IndexOf("<w:t", t, StringComparison.Ordinal)) >= 0)
            {
                int gt = para.IndexOf('>', t);
                int close = para.IndexOf("</w:t>", gt, StringComparison.Ordinal);
                text.Append(para[(gt + 1)..close]);
                t = close;
            }

            sb.Append(text).Append('|');
            i = end;
        }

        return sb.ToString();
    }

    [Fact]
    public void A_tag_split_across_runs_binds_and_prints_in_the_first_runs_formatting()
    {
        string body = "<w:p><w:r><w:t xml:space=\"preserve\">Hello {{ na</w:t></w:r><w:proofErr w:type=\"spellStart\"/><w:r><w:rPr><w:b/></w:rPr><w:t>me }}!</w:t></w:r></w:p>";
        string xml = Render(body, Json("{\"name\":\"Bob\"}"));
        Assert.Equal("Hello Bob!|", Texts(xml));
        Assert.Contains("<w:r><w:t xml:space=\"preserve\">Hello Bob</w:t></w:r><w:proofErr w:type=\"spellStart\"/><w:r><w:rPr><w:b/></w:rPr><w:t xml:space=\"preserve\">!</w:t></w:r>", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("{{", xml, StringComparison.Ordinal);
        Assert.Contains("<w:proofErr", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void A_loop_over_paragraphs_repeats_them_and_the_tag_paragraphs_vanish()
    {
        string body = P("{% for x in items %}") + P("Item {{ x }}") + P("{% endfor %}") + P("end");
        string xml = Render(body, Json("{\"items\":[\"a\",\"b\",\"c\"]}"));
        Assert.Equal("Item a|Item b|Item c|end|", Texts(xml));
    }

    [Fact]
    public void A_loop_whose_tags_sit_in_one_table_row_repeats_the_row()
    {
        string row(string a, string b) => "<w:tr><w:tc><w:tcPr><w:tcW w:w=\"100\" w:type=\"dxa\"/></w:tcPr>" + P(a) + "</w:tc><w:tc>" + P(b) + "</w:tc></w:tr>";
        string body = "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"100\"/><w:gridCol w:w=\"100\"/></w:tblGrid>" + row("Name", "Qty") + row("{% for l in lines %}{{ l.n }}", "{{ l.q }}{% endfor %}") + "</w:tbl><w:p/>";
        string xml = Render(body, Json("{\"lines\":[{\"n\":\"A\",\"q\":1},{\"n\":\"B\",\"q\":2}]}"));
        Assert.Equal("Name|Qty|A|1|B|2|", Texts(xml));
        Assert.Equal(3, Count(xml, "<w:tr>"));
    }

    [Fact]
    public void A_condition_around_a_table_row_drops_the_row_and_an_empty_table()
    {
        string row(string a) => "<w:tr><w:tc>" + P(a) + "</w:tc></w:tr>";
        string body = "<w:tbl><w:tblGrid><w:gridCol w:w=\"1\"/></w:tblGrid>" + row("{% if show %}shown{% endif %}") + "</w:tbl><w:p/>";
        string on = Render(body, Json("{\"show\":true}"));
        Assert.Contains("shown", on, StringComparison.Ordinal);
        string off = Render(body, Json("{\"show\":false}"));
        Assert.DoesNotContain("<w:tbl>", off, StringComparison.Ordinal);
    }

    [Fact]
    public void Untouched_paragraphs_are_kept_verbatim()
    {
        string untouched = "<w:p w:rsidR=\"00AB12CD\"><w:pPr><w:jc w:val=\"center\"/></w:pPr><w:r><w:rPr><w:i/></w:rPr><w:t>plain</w:t></w:r></w:p>";
        string xml = Render(untouched + P("{{ a }}"), Json("{\"a\":\"x\"}"));
        Assert.Contains(untouched, xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_data_is_a_warning_and_strict_mode_throws()
    {
        DocumentFormats.Register(FormatRegistry.Default);
        using Document doc = TestDocs.Open(P("a{{ nope }}b"));
        var warnings = Template.Render(doc, Json("{}"));
        Assert.Contains(warnings, d => d.Code == "template.missing-data");
        using Document doc2 = TestDocs.Open(P("a{{ nope }}b"));
        Assert.Throws<TemplateException>(() => Template.Render(doc2, Json("{}"), new TemplateOptions { Strict = true }));
    }

    [Fact]
    public void Validate_reports_located_errors_without_changing_the_document()
    {
        DocumentFormats.Register(FormatRegistry.Default);
        using Document doc = TestDocs.Open(P("ok") + P("{% for x in %}") + P("{{ unclosed"));
        string before = doc.MainMarkup.Root.OuterXml;
        var errors = Template.Validate(doc);
        Assert.Contains(errors, d => d.Code == "template.empty-expression" && d.Location.Container == "/word/document.xml#body/p2");
        Assert.Contains(errors, d => d.Code == "template.unclosed-tag");
        Assert.Equal(before, doc.MainMarkup.Root.OuterXml);
    }

    [Fact]
    public void Repeated_bookmarks_get_new_ids_and_names()
    {
        string p = "<w:p><w:bookmarkStart w:id=\"7\" w:name=\"b\"/><w:r><w:t>{{ x }}</w:t></w:r><w:bookmarkEnd w:id=\"7\"/></w:p>";
        string xml = Render(P("{% for x in xs %}") + p + P("{% endfor %}"), Json("{\"xs\":[1,2,3]}"));
        Assert.Equal(3, Count(xml, "<w:bookmarkStart"));
        Assert.Contains("w:id=\"7\" w:name=\"b\"", xml, StringComparison.Ordinal);
        Assert.Contains("w:name=\"b_2\"", xml, StringComparison.Ordinal);
        Assert.Contains("w:name=\"b_3\"", xml, StringComparison.Ordinal);
        Assert.Equal(3, xml.Split("<w:bookmarkEnd").Length - 1);
        var ids = new HashSet<string>();
        int i = 0;
        while ((i = xml.IndexOf("<w:bookmarkStart w:id=\"", i, StringComparison.Ordinal)) >= 0)
        {
            i += 23;
            Assert.True(ids.Add(xml[i..xml.IndexOf('"', i)]));
        }
    }

    private static int Count(string s, string needle)
    {
        int n = 0;
        int i = 0;
        while ((i = s.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
        {
            n++;
            i += needle.Length;
        }

        return n;
    }

    [Fact]
    public void The_link_function_makes_a_hyperlink()
    {
        string xml = Render(P("See {{ link(\"https://example.com/a?b=1&amp;c=2\", \"the site\") }}."), Json("{}"), check: doc =>
        {
            Assert.True(doc.Package.GetRelationships("/word/document.xml").Count > 0, string.Join(";", doc.Package.GetRelationships("/word/document.xml")));
            Assert.True(doc.Package.GetRelationships("/word/document.xml").Any(r => r.Target == "https://example.com/a?b=1&c=2" && r.TargetMode == RecursiveAI.OneDocument.Packaging.RelationshipTargetMode.External), string.Join(";", doc.Package.GetRelationships("/word/document.xml").Select(r => r.Target)));
        });
        Assert.Contains("<w:hyperlink", xml, StringComparison.Ordinal);
        Assert.Contains("the site", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Link_targets_with_unsafe_schemes_print_as_text()
    {
        string xml = Render(P("{{ link(\"javascript:alert(1)\", \"click\") }}"), Json("{}"));
        Assert.DoesNotContain("<w:hyperlink", xml, StringComparison.Ordinal);
        Assert.Contains("click", xml, StringComparison.Ordinal);
    }
}
