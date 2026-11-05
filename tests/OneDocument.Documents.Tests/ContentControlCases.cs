using System.Text;

namespace RecursiveAI.OneDocument.Documents.Tests;

/// <summary>
/// The golden content-control binding corpus: main documents with <c>w:dataBinding</c> controls of every type, a custom XML part, and
/// the operation (bind data, refresh from the part, push to the part). Committed under <c>testdata/templates/contentcontrols</c>;
/// <c>ONEDOCUMENT_UPDATE_GOLDEN=1</c> regenerates from the descriptions here.
/// </summary>
internal static class ContentControlCases
{
    public const string StoreId = "{4F6A0B1C-2D3E-4F50-8A9B-0C1D2E3F4A5B}";
    private const string Prefixes = "xmlns:ns0='urn:acme'";
    private const string Prolog = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n";
    private const string RelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private static int _id;

    internal sealed record Case(string Name, string Body, string Store, string Operation, string? Data = null, string DataKind = "json");

    public const string Order =
        "<ns0:order xmlns:ns0=\"urn:acme\" id=\"A-1\"><ns0:customer>Acme Corp</ns0:customer><ns0:due>2024-03-09T00:00:00Z</ns0:due><ns0:paid>0</ns0:paid><ns0:colour>g</ns0:colour>"
        + "<ns0:notes>First line</ns0:notes><ns0:lines><ns0:line><ns0:name>Widget</ns0:name><ns0:qty>1</ns0:qty></ns0:line></ns0:lines></ns0:order>";

    private static string Bind(string xpath) =>
        "<w:dataBinding w:prefixMappings=\"" + Prefixes + "\" w:xpath=\"" + xpath + "\" w:storeItemID=\"" + StoreId + "\"/>";

    /// <summary>An inline content control with a binding, the way Word writes one.</summary>
    private static string Inline(string tag, string xpath, string type, string content, string rPr = "") =>
        "<w:sdt><w:sdtPr><w:alias w:val=\"" + tag + "\"/><w:tag w:val=\"" + tag + "\"/><w:id w:val=\"" + (1000 + _id++) + "\"/>" + Bind(xpath) + type + "</w:sdtPr><w:sdtContent>"
        + "<w:r><w:rPr>" + rPr + "</w:rPr><w:t>" + content + "</w:t></w:r></w:sdtContent></w:sdt>";

    private static string P(string inner) => "<w:p w:rsidR=\"00A11000\">" + inner + "</w:p>";

    private static string Row(string inner) => "<w:p>" + inner + "</w:p>";

    private static string Repeating(string xpath, string itemParagraph) =>
        "<w:sdt><w:sdtPr><w:alias w:val=\"lines\"/><w:tag w:val=\"lines\"/><w:id w:val=\"900\"/>" + Bind(xpath) + "<w15:repeatingSection/></w:sdtPr><w:sdtContent>"
        + "<w:sdt><w:sdtPr><w:id w:val=\"901\"/><w15:repeatingSectionItem/></w:sdtPr><w:sdtContent>" + itemParagraph + "</w:sdtContent></w:sdt></w:sdtContent></w:sdt>";

    private static string LineItem(int index) =>
        P(Inline("name", "/ns0:order[1]/ns0:lines[1]/ns0:line[" + index + "]/ns0:name[1]", "<w:text/>", "Item name") + "<w:r><w:t xml:space=\"preserve\"> x </w:t></w:r>"
          + Inline("qty", "/ns0:order[1]/ns0:lines[1]/ns0:line[" + index + "]/ns0:qty[1]", "<w:text/>", "0"));

    private static string Checkbox(string tag, string xpath, bool isChecked) =>
        "<w:sdt><w:sdtPr><w:alias w:val=\"" + tag + "\"/><w:tag w:val=\"" + tag + "\"/><w:id w:val=\"" + (1000 + _id++) + "\"/>" + Bind(xpath)
        + "<w14:checkbox><w14:checked w14:val=\"" + (isChecked ? 1 : 0) + "\"/><w14:checkedState w14:val=\"2612\" w14:font=\"MS Gothic\"/><w14:uncheckedState w14:val=\"2610\" w14:font=\"MS Gothic\"/></w14:checkbox></w:sdtPr><w:sdtContent>"
        + "<w:r><w:rPr><w:rFonts w:ascii=\"MS Gothic\" w:eastAsia=\"MS Gothic\" w:hAnsi=\"MS Gothic\" w:hint=\"eastAsia\"/></w:rPr><w:t>" + (isChecked ? "\u2612" : "\u2610") + "</w:t></w:r></w:sdtContent></w:sdt>";

    public static IReadOnlyList<Case> All()
    {
        _id = 0;
        string customer = "/ns0:order[1]/ns0:customer[1]";
        const string DateType = "<w:date><w:dateFormat w:val=\"d MMMM yyyy\"/><w:lid w:val=\"en-US\"/><w:storeMappedDataType w:val=\"dateTime\"/><w:calendar w:val=\"gregorian\"/></w:date>";
        const string Colours = "<w:dropDownList><w:listItem w:displayText=\"Red\" w:value=\"r\"/><w:listItem w:displayText=\"Green\" w:value=\"g\"/><w:listItem w:displayText=\"Blue\" w:value=\"b\"/></w:dropDownList>";
        const string ComboColours = "<w:comboBox><w:listItem w:displayText=\"Red\" w:value=\"r\"/><w:listItem w:displayText=\"Green\" w:value=\"g\"/></w:comboBox>";
        const string Data = """{"customer":"Globex & Sons","due":"2025-07-04","paid":true,"colour":"b","notes":"Line one\nLine two","lines":[{"name":"Bolt","qty":10},{"name":"Nut","qty":20},{"name":"Washer","qty":30}]}""";
        const string SmallData = """{"customer":"Initech","lines":[{"name":"Stapler","qty":1}]}""";
        return
        [
            new("plain-text-bind", P(Inline("customer", customer, "<w:text/>", "Click to enter name")), Order, "bind", Data),
            new("plain-text-refresh", P(Inline("customer", customer, "<w:text/>", "stale")), Order, "refresh"),
            new("rich-text-bind", P("<w:r><w:t xml:space=\"preserve\">Customer: </w:t></w:r>" + Inline("customer", customer, string.Empty, "x", "<w:b/>")), Order, "bind", Data),
            new("multiline-text", P(Inline("notes", "/ns0:order[1]/ns0:notes[1]", "<w:text w:multiLine=\"1\"/>", "x")), Order, "bind", Data),
            new("date-bind", P(Inline("due", "/ns0:order[1]/ns0:due[1]", DateType, "1 January 2000")), Order, "bind", Data),
            new("date-refresh", P(Inline("due", "/ns0:order[1]/ns0:due[1]", DateType, "stale")), Order, "refresh"),
            new("checkbox-bind", P(Checkbox("paid", "/ns0:order[1]/ns0:paid[1]", false)), Order, "bind", Data),
            new("checkbox-refresh", P(Checkbox("paid", "/ns0:order[1]/ns0:paid[1]", true)), Order, "refresh"),
            new("dropdown-bind", P(Inline("colour", "/ns0:order[1]/ns0:colour[1]", Colours, "Red")), Order, "bind", Data),
            new("dropdown-refresh", P(Inline("colour", "/ns0:order[1]/ns0:colour[1]", Colours, "Red")), Order, "refresh"),
            new("combobox-bind", P(Inline("colour", "/ns0:order[1]/ns0:colour[1]", ComboColours, "Red")), Order, "bind", """{"colour":"Magenta"}"""),
            new("attribute-binding", P(Inline("id", "/ns0:order[1]/@id", "<w:text/>", "x")), Order, "bind", """{"id":"B-77"}"""),
            new("descendant-xpath", P(Inline("customer", "//ns0:customer", "<w:text/>", "x")), Order, "refresh"),
            new("predicate-xpath", P(Inline("qty", "/ns0:order[1]/ns0:lines[1]/ns0:line[ns0:name='Widget']/ns0:qty", "<w:text/>", "x")), Order, "refresh"),
            new("repeating-grow", Repeating("/ns0:order[1]/ns0:lines[1]/ns0:line", LineItem(1)), Order, "bind", Data),
            new("repeating-trim", Repeating("/ns0:order[1]/ns0:lines[1]/ns0:line", LineItem(1)), OrderWithLines(3), "bind", SmallData),
            new("repeating-refresh", Repeating("/ns0:order[1]/ns0:lines[1]/ns0:line", LineItem(1)), OrderWithLines(3), "refresh"),
            new("repeating-from-xml", Repeating("/ns0:order[1]/ns0:lines[1]/ns0:line", LineItem(1)), Order, "bind",
                "<order><customer>X</customer><lines><line><name>Alpha</name><qty>5</qty></line><line><name>Beta</name><qty>6</qty></line></lines></order>", "xml"),
            new("push-to-store", P(Inline("customer", customer, "<w:text/>", "Typed by the user") + "<w:r><w:t xml:space=\"preserve\"> </w:t></w:r>" + Checkbox("paid", "/ns0:order[1]/ns0:paid[1]", true) + Inline("colour", "/ns0:order[1]/ns0:colour[1]", Colours, "Blue")
                + Inline("due", "/ns0:order[1]/ns0:due[1]", DateType.Replace("<w:date>", "<w:date w:fullDate=\"2026-05-05T00:00:00Z\">", StringComparison.Ordinal), "5 May 2026")), Order, "push"),
            new("push-creates-nodes", P(Inline("ship", "/ns0:order[1]/ns0:ship[1]/ns0:city[1]", "<w:text/>", "Oslo")), Order, "push"),
            new("no-data-keeps-control", P(Inline("missing", "/ns0:order[1]/ns0:nothing[1]", "<w:text/>", "kept")), Order, "bind", Data),
            new("unsupported-path-warns", P(Inline("x", "/ns0:order[1]/ns0:customer[1] | /ns0:order[1]/ns0:notes[1]", "<w:text/>", "kept")), Order, "refresh"),
        ];
    }

    private static string OrderWithLines(int n)
    {
        var sb = new StringBuilder();
        for (int i = 1; i <= n; i++)
        {
            sb.Append("<ns0:line><ns0:name>Part ").Append(i).Append("</ns0:name><ns0:qty>").Append(i * 10).Append("</ns0:qty></ns0:line>");
        }

        return "<ns0:order xmlns:ns0=\"urn:acme\" id=\"A-1\"><ns0:customer>Acme Corp</ns0:customer><ns0:lines>" + sb + "</ns0:lines></ns0:order>";
    }

    public static void Write(Case c, string path)
    {
        const string W = MailMergeCases.W;
        string doc = Prolog + "<w:document xmlns:w=\"" + W + "\" xmlns:r=\"" + MailMergeCases.R + "\" xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" "
            + "xmlns:w14=\"http://schemas.microsoft.com/office/word/2010/wordml\" xmlns:w15=\"http://schemas.microsoft.com/office/word/2012/wordml\" mc:Ignorable=\"w14 w15\"><w:body>"
            + c.Body + "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/></w:sectPr></w:body></w:document>";
        var entries = new List<(string, string)>
        {
            ("[Content_Types].xml", Prolog + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>"
                + "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>"
                + "<Override PartName=\"/customXml/itemProps1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.customXmlProperties+xml\"/></Types>"),
            ("_rels/.rels", Prolog + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"" + RelType + "officeDocument\" Target=\"word/document.xml\"/></Relationships>"),
            ("word/_rels/document.xml.rels", Prolog + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId20\" Type=\"" + RelType + "customXml\" Target=\"../customXml/item1.xml\"/></Relationships>"),
            ("word/document.xml", doc),
            ("customXml/item1.xml", Prolog + c.Store),
            ("customXml/_rels/item1.xml.rels", Prolog + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"" + RelType + "customXmlProps\" Target=\"itemProps1.xml\"/></Relationships>"),
            ("customXml/itemProps1.xml", Prolog + "<ds:datastoreItem ds:itemID=\"" + StoreId + "\" xmlns:ds=\"http://schemas.openxmlformats.org/officeDocument/2006/customXml\"><ds:schemaRefs><ds:schemaRef ds:uri=\"urn:acme\"/></ds:schemaRefs></ds:datastoreItem>"),
        };
        using FileStream fs = File.Create(path);
        using var zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create);
        foreach ((string name, string content) in entries.OrderBy(e => e.Item1 == "[Content_Types].xml" ? string.Empty : e.Item1, StringComparer.Ordinal))
        {
            System.IO.Compression.ZipArchiveEntry e = zip.CreateEntry(name, System.IO.Compression.CompressionLevel.Optimal);
            e.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
            w.Write(content);
        }
    }
}
