using System.Text;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Documents.Tests;

/// <summary>
/// The word-processing template corpus: each case is described here, written as a <c>.docx</c> with data and the expected output parts under
/// <c>testdata/templates/docx/</c>, and re-rendered by <see cref="DocxTemplateGoldenTests"/>. Templates carry the noise Word leaves in a document:
/// tags split across runs, proofing marks, revision-save ids, bookmarks.
/// </summary>
internal sealed record TemplateCase(string Name, string Body, string Json, Dictionary<string, string>? Parts = null, bool Strict = false, string? Note = null)
{
    public const string W = TestDocs.WNs;
    public const string R = TestDocs.RNs;
    private const string RelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";

    internal const string Root = "xmlns:w=\"" + W + "\" xmlns:r=\"" + R + "\" xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\" xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" xmlns:w14=\"http://schemas.microsoft.com/office/word/2010/wordml\" xmlns:wps=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\" xmlns:v=\"urn:schemas-microsoft-com:vml\" mc:Ignorable=\"w14\"";

    private static string Part(string root, string inner) => "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<w:" + root + " " + Root + ">" + inner + "</w:" + root + ">";

    /// <summary>Builds the template package: the body, the optional parts (header1, footer1, footnotes, comments, numbering) by short name.</summary>
    public byte[] Build()
    {
        using OpcPackage p = OpcPackage.Create();
        var rels = new List<Relationship>();
        p.SetRelationships(null, [new Relationship("rId1", RelType + "officeDocument", "word/document.xml")]);
        string sect = "<w:sectPr>";
        int n = 10;
        void Add(string name, string root, string type, string contentType, string relName)
        {
            if (Parts is null || !Parts.TryGetValue(name, out string? inner))
            {
                return;
            }

            p.AddPart("/word/" + name + ".xml", contentType, Encoding.UTF8.GetBytes(Part(root, inner)));
            string id = "rId" + (n++).ToString(System.Globalization.CultureInfo.InvariantCulture);
            rels.Add(new Relationship(id, RelType + type, name + ".xml"));
            if (relName == "header")
            {
                sect += "<w:headerReference w:type=\"default\" r:id=\"" + id + "\"/>";
            }
            else if (relName == "footer")
            {
                sect += "<w:footerReference w:type=\"default\" r:id=\"" + id + "\"/>";
            }
        }

        const string Wml = "application/vnd.openxmlformats-officedocument.wordprocessingml.";
        Add("header1", "hdr", "header", Wml + "header+xml", "header");
        Add("footer1", "ftr", "footer", Wml + "footer+xml", "footer");
        Add("footnotes", "footnotes", "footnotes", Wml + "footnotes+xml", "");
        Add("comments", "comments", "comments", Wml + "comments+xml", "");
        Add("numbering", "numbering", "numbering", Wml + "numbering+xml", "");
        Add("styles", "styles", "styles", Wml + "styles+xml", "");
        sect += "<w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/></w:sectPr>";
        p.AddPart("/word/document.xml", Wml + "document.main+xml", Encoding.UTF8.GetBytes(Part("document", "<w:body>" + Body + sect + "</w:body>")));
        p.SetRelationships("/word/document.xml", rels);
        using var ms = new MemoryStream();
        p.WriteTo(ms);
        return ms.ToArray();
    }

    // ---- building blocks -------------------------------------------------------------------------------------------------------

    /// <summary>A run.</summary>
    public static string Run(string text, string rPr = "", string rsid = "00A1B2C3") =>
        "<w:r w:rsidR=\"" + rsid + "\">" + (rPr.Length > 0 ? "<w:rPr>" + rPr + "</w:rPr>" : string.Empty) + "<w:t xml:space=\"preserve\">" + text + "</w:t></w:r>";

    /// <summary>A paragraph of one run.</summary>
    public static string P(string text, string pPr = "") =>
        "<w:p w:rsidR=\"00A1B2C3\" w:rsidRDefault=\"00A1B2C3\">" + (pPr.Length > 0 ? "<w:pPr>" + pPr + "</w:pPr>" : string.Empty) + Run(text) + "</w:p>";

    /// <summary>A paragraph whose text is cut into runs at the given pieces, with a spell-check mark between some of them, as Word writes edited text.</summary>
    public static string Split(params string[] pieces)
    {
        var sb = new StringBuilder("<w:p w:rsidR=\"00B2C3D4\" w:rsidRDefault=\"00B2C3D4\">");
        for (int i = 0; i < pieces.Length; i++)
        {
            string rsid = i % 2 == 0 ? "00B2C3D4" : "00C3D4E5";
            sb.Append(Run(pieces[i], i == 1 ? "<w:b/>" : string.Empty, rsid));
            if (i % 2 == 0 && i < pieces.Length - 1)
            {
                sb.Append("<w:proofErr w:type=\"spellStart\"/>");
            }
            else if (i % 2 == 1 && i < pieces.Length - 1)
            {
                sb.Append("<w:proofErr w:type=\"spellEnd\"/>");
            }
        }

        return sb.Append("</w:p>").ToString();
    }

    /// <summary>An empty paragraph that ends a section (ECMA-376 Part 1 §17.6.17).</summary>
    public static string SectionBreak() =>
        "<w:p><w:pPr><w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/></w:sectPr></w:pPr></w:p>";

    public static string Row(params string[] cells) =>
        "<w:tr w:rsidR=\"00A1B2C3\"><w:trPr><w:cantSplit/></w:trPr>" + string.Concat(cells.Select(c => "<w:tc><w:tcPr><w:tcW w:w=\"2400\" w:type=\"dxa\"/></w:tcPr>" + (c.StartsWith("<w:", StringComparison.Ordinal) ? c : P(c)) + "</w:tc>")) + "</w:tr>";

    public static string Table(int columns, params string[] rows) =>
        "<w:tbl><w:tblPr><w:tblStyle w:val=\"TableGrid\"/><w:tblW w:w=\"0\" w:type=\"auto\"/></w:tblPr><w:tblGrid>" + string.Concat(Enumerable.Repeat("<w:gridCol w:w=\"2400\"/>", columns)) + "</w:tblGrid>" + string.Concat(rows) + "</w:tbl>";

    private const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    public static IReadOnlyList<TemplateCase> All { get; } = BuildAll();

    private static List<TemplateCase> BuildAll() =>
    [
        new("values", P("Invoice {{ number }} for {{ customer.name }}") + P("Total: {{ TEXT(total, \"#,##0.00\") }}"),
            "{\"number\":\"INV-7\",\"customer\":{\"name\":\"Ada\"},\"total\":1234.5}"),
        new("values-split-runs", Split("Dear {{ cu", "stomer.fi", "rst }} ", "{{", " customer.last }},"), "{\"customer\":{\"first\":\"Grace\",\"last\":\"Hopper\"}}"),
        new("values-functions", P("{{ UPPER(name) }} owes {{ TEXT(amount, \"$#,##0.00\") }} {{ IF(amount > 100, \"(large)\", \"(small)\") }}"),
            "{\"name\":\"acme\",\"amount\":250}"),
        new("curly-quotes-and-whitespace", P("{{ \u201Cabc\u201D }} [{{- name -}}] {% if \u201Cx\u201D = \u201Cx\u201D %}yes{% endif %}"), "{\"name\":\"n\"}"),
        new("loop-paragraphs", P("Items:") + P("{% for item in items %}") + P("- {{ loop.index }}. {{ item.name }} x{{ item.qty }}") + P("{% endfor %}") + P("Done."),
            "{\"items\":[{\"name\":\"Pen\",\"qty\":2},{\"name\":\"Ink\",\"qty\":1},{\"name\":\"Pad\",\"qty\":5}]}"),
        new("loop-inline", P("Tags: {% for t in tags %}{{ t }}{% if not loop.last %}, {% endif %}{% endfor %}."), "{\"tags\":[\"red\",\"green\",\"blue\"]}"),
        new("loop-numbered-list", P("{% for s in steps %}") + P("{{ s }}", "<w:pStyle w:val=\"ListParagraph\"/><w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"1\"/></w:numPr>") + P("{% endfor %}"),
            "{\"steps\":[\"Mix\",\"Bake\",\"Cool\"]}",
            new() { ["numbering"] = "<w:abstractNum w:abstractNumId=\"0\"><w:multiLevelType w:val=\"hybridMultilevel\"/><w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"%1.\"/><w:lvlJc w:val=\"left\"/></w:lvl></w:abstractNum><w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num>" }),
        new("loop-nested", P("{% for g in groups %}") + P("Group {{ g.name }}:") + P("{% for m in g.members %}") + P("  * {{ m }}") + P("{% endfor %}") + P("{% endfor %}"),
            "{\"groups\":[{\"name\":\"A\",\"members\":[\"x\",\"y\"]},{\"name\":\"B\",\"members\":[]},{\"name\":\"C\",\"members\":[\"z\"]}]}"),
        new("table-rows-inferred", Table(3, Row("Item", "Qty", "Price"), Row("{% for l in lines %}{{ l.item }}", "{{ l.qty }}", "{{ l.price }}{% endfor %}"), Row("Total", "", "{{ SUM(lines.price * lines.qty) }}")) + "<w:p/>",
            "{\"lines\":[{\"item\":\"Pen\",\"qty\":2,\"price\":1.5},{\"item\":\"Ink\",\"qty\":1,\"price\":10}]}"),
        new("table-rows-tag-rows", Table(2, Row("{% for l in lines %}", ""), Row("{{ l.item }}", "{{ l.qty }}"), Row("{% endfor %}", "")) + "<w:p/>",
            "{\"lines\":[{\"item\":\"Pen\",\"qty\":2},{\"item\":\"Ink\",\"qty\":1}]}"),
        new("table-rows-empty-list", P("Before") + Table(1, Row("{% for l in lines %}{{ l }}{% endfor %}")) + P("After"), "{\"lines\":[]}"),
        new("table-row-if", Table(2, Row("Always", "x"), Row("{% if vip %}VIP discount{% endif %}", "{% if vip %}10%{% endif %}")) + "<w:p/>", "{\"vip\":false}"),
        new("table-nested", Table(1, Row(P("{% for o in orders %}Order {{ o.id }}") + Table(2, Row("{% for l in o.lines %}{{ l.sku }}", "{{ l.qty }}{% endfor %}")) + P("{% endfor %}"))) + "<w:p/>",
            "{\"orders\":[{\"id\":1,\"lines\":[{\"sku\":\"a\",\"qty\":1},{\"sku\":\"b\",\"qty\":2}]},{\"id\":2,\"lines\":[{\"sku\":\"c\",\"qty\":3}]}]}"),
        new("table-explicit-scope", Table(2, Row("{% for p in people scope=table-row %}", ""), Row("{{ p.name }}", "{{ p.age }}"), Row("{% endfor %}", "")) + "<w:p/>",
            "{\"people\":[{\"name\":\"Al\",\"age\":30},{\"name\":\"Bo\",\"age\":41}]}"),
        new("if-else", P("{% if score >= 90 %}") + P("Grade A") + P("{% elif score >= 80 %}") + P("Grade B") + P("{% else %}") + P("Grade C") + P("{% endif %}"), "{\"score\":85}"),
        new("section-repeat", P("{% for c in chapters scope=section %}") + P("Chapter {{ c }}", "<w:pStyle w:val=\"Heading1\"/>") + P("Body of {{ c }}.") + P("{% endfor %}") + SectionBreak() + P("Appendix"),
            "{\"chapters\":[\"One\",\"Two\"]}"),
        new("section-repeat-final", P("{% for c in chapters scope=section %}") + P("Chapter {{ c }}") + P("{% endfor %}"), "{\"chapters\":[\"One\",\"Two\",\"Three\"]}"),
        new("header-footer", P("Body {{ title }}"), "{\"title\":\"T\",\"company\":\"Acme\",\"year\":2026}",
            new() { ["header1"] = P("{{ company }} — {{ title }}"), ["footer1"] = Split("© {{ ye", "ar }} ", "{{ company }}") }),
        new("footnote", "<w:p><w:r><w:t xml:space=\"preserve\">Claim</w:t></w:r><w:r><w:rPr><w:rStyle w:val=\"FootnoteReference\"/></w:rPr><w:footnoteReference w:id=\"1\"/></w:r></w:p>", "{\"source\":\"Smith 2020\"}",
            new() { ["footnotes"] = "<w:footnote w:type=\"separator\" w:id=\"-1\"><w:p><w:r><w:separator/></w:r></w:p></w:footnote><w:footnote w:type=\"continuationSeparator\" w:id=\"0\"><w:p><w:r><w:continuationSeparator/></w:r></w:p></w:footnote><w:footnote w:id=\"1\"><w:p><w:r><w:rPr><w:rStyle w:val=\"FootnoteReference\"/></w:rPr><w:footnoteRef/></w:r><w:r><w:t xml:space=\"preserve\"> See {{ source }}.</w:t></w:r></w:p></w:footnote>" }),
        new("footnote-in-loop", P("{% for s in sources %}") + "<w:p><w:r><w:t xml:space=\"preserve\">Claim {{ s }}</w:t></w:r><w:r><w:rPr><w:rStyle w:val=\"FootnoteReference\"/></w:rPr><w:footnoteReference w:id=\"1\"/></w:r></w:p>" + P("{% endfor %}"),
            "{\"sources\":[\"a\",\"b\",\"c\"]}",
            new() { ["footnotes"] = "<w:footnote w:type=\"separator\" w:id=\"-1\"><w:p><w:r><w:separator/></w:r></w:p></w:footnote><w:footnote w:type=\"continuationSeparator\" w:id=\"0\"><w:p><w:r><w:continuationSeparator/></w:r></w:p></w:footnote><w:footnote w:id=\"1\"><w:p><w:r><w:rPr><w:rStyle w:val=\"FootnoteReference\"/></w:rPr><w:footnoteRef/></w:r><w:r><w:t xml:space=\"preserve\"> Note {{ \"x\" }}.</w:t></w:r></w:p></w:footnote>" }),
        new("comment-in-loop", P("{% for s in names %}") + "<w:p><w:commentRangeStart w:id=\"0\"/><w:r><w:t xml:space=\"preserve\">Hello {{ s }}</w:t></w:r><w:commentRangeEnd w:id=\"0\"/><w:r><w:rPr><w:rStyle w:val=\"CommentReference\"/></w:rPr><w:commentReference w:id=\"0\"/></w:r></w:p>" + P("{% endfor %}"),
            "{\"names\":[\"Ann\",\"Ben\"]}",
            new() { ["comments"] = "<w:comment w:id=\"0\" w:author=\"Reviewer\" w:date=\"2026-01-01T00:00:00Z\" w:initials=\"R\"><w:p><w:pPr><w:pStyle w:val=\"CommentText\"/></w:pPr><w:r><w:rPr><w:rStyle w:val=\"CommentReference\"/></w:rPr><w:annotationRef/></w:r><w:r><w:t>Check {{ \"name\" }}</w:t></w:r></w:p></w:comment>" }),
        new("text-box", "<w:p><w:r><mc:AlternateContent><mc:Choice Requires=\"wps\"><w:drawing><wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\"><wp:extent cx=\"2000000\" cy=\"600000\"/><wp:docPr id=\"1\" name=\"Text Box 1\"/><a:graphic><a:graphicData uri=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\"><wps:wsp><wps:cNvSpPr txBox=\"1\"/><wps:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"2000000\" cy=\"600000\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></wps:spPr><wps:txbx><w:txbxContent>"
            + P("{% for l in lines %}") + P("Box: {{ l }}") + P("{% endfor %}") + "</w:txbxContent></wps:txbx><wps:bodyPr/></wps:wsp></a:graphicData></a:graphic></wp:inline></w:drawing></mc:Choice><mc:Fallback><w:pict><v:shape id=\"_x0000_s1026\" type=\"#_x0000_t202\"><v:textbox><w:txbxContent>"
            + P("{% for l in lines %}") + P("Box: {{ l }}") + P("{% endfor %}") + "</w:txbxContent></v:textbox></v:shape></w:pict></mc:Fallback></mc:AlternateContent></w:r></w:p>" + P("After {{ lines.length }} lines"),
            "{\"lines\":[\"a\",\"b\"]}"),
        new("image", P("Logo: {% image logo width=2cm alt='Company logo' %}") + P("Second: {% image logo width=1cm %}"), "{\"logo\":\"" + Png + "\"}"),
        new("image-in-loop", P("{% for i in items %}") + P("{{ i.name }} {% image i.pic width=1in height=0.5in fit=stretch %}") + P("{% endfor %}"),
            "{\"items\":[{\"name\":\"a\",\"pic\":\"" + Png + "\"},{\"name\":\"b\",\"pic\":\"" + Png + "\"}]}"),
        new("hyperlink", P("Visit {{ link(url, \"our site\") }} or {{ link(\"mailto:hi@example.com\", \"mail us\") }} or {{ link(\"javascript:x()\", \"not a link\") }}."), "{\"url\":\"https://example.com/?a=1&b=2\"}"),
        new("html-injection", P("Intro") + P("{% html body %}") + P("Outro"),
            "{\"body\":\"<h2>Notes</h2><p>Plain <b>bold</b>, <i>italic</i>, <u>under</u> and <a href='https://example.com'>a link</a>.</p><ul><li>one</li><li>two<ul><li>deep</li></ul></li></ul><ol><li>first</li><li>second</li></ol><script>alert(1)</script>\"}"),
        new("markdown-injection", P("Intro") + P("{% markdown body %}") + P("Outro {% markdown inline_md %}"),
            "{\"body\":\"# Heading\\n\\nPara with **bold**, *em*, `code` and [a link](https://example.com).\\n\\n- a\\n- b\\n\\n1. x\\n2. y\\n\\n> quoted\",\"inline_md\":\"**bold** tail\"}"),
        new("missing-data-lenient", P("Hello {{ name }}, you owe {{ amount }}.") + P("{% for x in nothing %}never{% endfor %}"), "{\"name\":\"Zed\"}"),
        new("bookmarks-in-loop", "<w:p><w:bookmarkStart w:id=\"3\" w:name=\"_Toc1\"/><w:r><w:t>{% for s in xs %}{{ s }} </w:t></w:r><w:bookmarkEnd w:id=\"3\"/><w:r><w:t>{% endfor %}</w:t></w:r></w:p>"
            + P("{% for s in xs %}") + "<w:p><w:bookmarkStart w:id=\"4\" w:name=\"item\"/><w:r><w:t>{{ s }}</w:t></w:r><w:bookmarkEnd w:id=\"4\"/></w:p>" + P("{% endfor %}"), "{\"xs\":[\"p\",\"q\",\"r\"]}"),
        new("revisions-in-template", "<w:p><w:r><w:t xml:space=\"preserve\">Dear </w:t></w:r><w:ins w:id=\"21\" w:author=\"Ed\" w:date=\"2026-01-01T00:00:00Z\"><w:r><w:t>{{ na</w:t></w:r></w:ins><w:del w:id=\"22\" w:author=\"Ed\" w:date=\"2026-01-01T00:00:00Z\"><w:r><w:delText>oops</w:delText></w:r></w:del><w:ins w:id=\"23\" w:author=\"Ed\" w:date=\"2026-01-01T00:00:00Z\"><w:r><w:t>me }},</w:t></w:r></w:ins></w:p>", "{\"name\":\"Ida\"}"),
        new("content-control", "<w:p><w:sdt><w:sdtPr><w:alias w:val=\"Name\"/><w:tag w:val=\"name\"/><w:id w:val=\"1001\"/><w:text/></w:sdtPr><w:sdtContent><w:r><w:t>{{ na</w:t></w:r><w:r><w:t>me }}</w:t></w:r></w:sdtContent></w:sdt><w:r><w:t xml:space=\"preserve\"> signed.</w:t></w:r></w:p>", "{\"name\":\"Joy\"}"),
        new("mixed-runs-and-breaks", "<w:p><w:r><w:t xml:space=\"preserve\">Name:</w:t><w:tab/><w:t xml:space=\"preserve\">{{ name }}</w:t><w:br/><w:t>City:</w:t><w:tab/><w:t>{{ city }}</w:t></w:r></w:p>" + P("Lines: {{ text }}"), "{\"name\":\"Kim\",\"city\":\"Oslo\",\"text\":\"one\\ntwo\\tthree\"}"),
        new("untouched-paragraphs", P("No tags here.", "<w:jc w:val=\"center\"/>") + Split("Still", " no", " tags"), "{}"),
    ];
}
