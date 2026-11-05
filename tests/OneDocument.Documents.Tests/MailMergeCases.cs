using System.Text;

namespace RecursiveAI.OneDocument.Documents.Tests;

/// <summary>
/// The golden mail-merge corpus: each case describes a main document the way Word writes one (fields split over runs, proofing
/// marks, revision-session ids), its data and the merge options. The templates, data and expected parts are committed under
/// <c>testdata/templates/mailmerge</c>; setting <c>ONEDOCUMENT_UPDATE_GOLDEN=1</c> regenerates them from the descriptions here.
/// </summary>
internal static class MailMergeCases
{
    public const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    public const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    public const string WP = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    private const string RelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";

    internal sealed record Case(string Name, string Body, string Data, bool Single = false, string[]? Answers = null, string? Header = null, bool Settings = false);

    internal const string People = """
        [
          {"First":"Ada","Last":"Lovelace","First Name":"Ada","City":"London","State":"UK","Balance":1234.5,"Born":"1815-12-10","Qty":3,"Email":"ada@example.org","Note":""},
          {"First":"grace","Last":"HOPPER","First Name":"Grace","City":"New York","State":"NY","Balance":99,"Born":"1906-12-09","Qty":12,"Email":"grace@example.org","Note":"Rear Admiral"},
          {"First":"Linus","Last":"Torvalds","First Name":"Linus","City":"Portland","State":"OR","Balance":-15.255,"Born":"1969-12-28","Qty":0,"Email":"linus@example.org","Note":"Kernel"}
        ]
        """;

    private static int _rsid;

    private static string Rsid() => "0000" + (0xA10 + _rsid++).ToString("X4", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A text run with a revision-session id, as Word leaves them.</summary>
    public static string Run(string text, string rPr = "") =>
        "<w:r w:rsidR=\"" + Rsid() + "\">" + (rPr.Length > 0 ? "<w:rPr>" + rPr + "</w:rPr>" : string.Empty) + "<w:t xml:space=\"preserve\">" + text + "</w:t></w:r>";

    public static string P(string inner, string pPr = "") =>
        "<w:p w:rsidR=\"" + Rsid() + "\" w:rsidRDefault=\"00B20001\">" + (pPr.Length > 0 ? "<w:pPr>" + pPr + "</w:pPr>" : string.Empty) + inner + "</w:p>";

    /// <summary>A complex field. Code parts starting with a tag are nested fields; other parts are instruction text, one run each.</summary>
    public static string Cf(string result, string resultRPr, params string[] code)
    {
        var sb = new StringBuilder();
        sb.Append("<w:r w:rsidR=\"").Append(Rsid()).Append("\"><w:fldChar w:fldCharType=\"begin\"/></w:r>");
        foreach (string part in code)
        {
            if (part.StartsWith('<'))
            {
                sb.Append(part);
            }
            else
            {
                sb.Append("<w:r w:rsidR=\"").Append(Rsid()).Append("\"><w:instrText xml:space=\"preserve\">").Append(part).Append("</w:instrText></w:r>");
            }
        }

        sb.Append("<w:r w:rsidR=\"").Append(Rsid()).Append("\"><w:fldChar w:fldCharType=\"separate\"/></w:r>");
        sb.Append("<w:r w:rsidR=\"").Append(Rsid()).Append("\"><w:rPr>").Append(resultRPr).Append("<w:noProof/></w:rPr><w:t>").Append(result).Append("</w:t></w:r>");
        sb.Append("<w:r w:rsidR=\"").Append(Rsid()).Append("\"><w:fldChar w:fldCharType=\"end\"/></w:r>");
        return sb.ToString();
    }

    /// <summary>A merge field the way Word writes it: <c>MERGEFIELD name</c> with a «name» placeholder result.</summary>
    public static string Mf(string name, string switches = "", string resultRPr = "") =>
        Cf("«" + name + "»", resultRPr, " MERGEFIELD ", " " + name + " " + switches);

    public static string Sf(string instr, string result, string rPr = "") =>
        "<w:fldSimple w:instr=\"" + instr.Replace("\"", "&quot;", StringComparison.Ordinal) + "\"><w:r w:rsidR=\"" + Rsid() + "\"><w:rPr>" + rPr + "<w:noProof/></w:rPr><w:t>" + result + "</w:t></w:r></w:fldSimple>";

    private static string Proof(string kind) => "<w:proofErr w:type=\"" + kind + "\"/>";

    public static IReadOnlyList<Case> All()
    {
        _rsid = 0;
        var list = new List<Case>
        {
            new("simple-fldsimple", P(Run("Dear ") + Sf(" MERGEFIELD First ", "«First»") + Run(",")) + P(Run("City: ") + Sf(" MERGEFIELD City \\* MERGEFORMAT ", "«City»", "<w:b/>")), People),

            new("complex-split-runs", P(Run("Name: ") + Proof("spellStart") + Cf("«Last»", "<w:b/>", " MERGEFIELD ", " L", "ast ", "\\* MERGEFORMAT ") + Proof("spellEnd") + Run(" end")), People),

            new("case-switches", P(Mf("First", "\\* Upper") + Run(" | ") + Mf("Last", "\\* Lower") + Run(" | ") + Mf("Last", "\\* FirstCap") + Run(" | ") + Mf("Last", "\\* Caps")), People),

            new("before-after", P(Run("Name: ") + Mf("First") + Mf("Note", "\\b \" (\" \\f \")\"") + Run(".")), People),

            new("number-pictures", P(Mf("Balance", "\\# \"#,##0.00\"") + Run(" | ") + Mf("Balance", "\\# \"$#,##0.00;($#,##0.00)\"") + Run(" | ") + Mf("Qty", "\\# \"000\"") + Run(" | ") + Mf("Balance", "\\# \"0.0%\"")), People),

            new("number-pictures-word-items", P(Mf("Qty", "\\# \"$###\"") + Run(" | ") + Mf("Qty", "\\# \"x##\"") + Run(" | ") + Mf("Balance", "\\# \"0.0x\"") + Run(" | ") + Mf("Balance", "\\# \"-#,##0.00\"") + Run(" | ") + Mf("Qty", "\\# \"+##\"") + Run(" | ") + Mf("Qty", "\\# \"$#,##0.00;($#,##0.00);'none'\"") + Run(" | ") + Mf("Qty", "\\# \"##%\"")), People),

            new("date-pictures", P(Mf("Born", "\\@ \"MMMM d, yyyy\"") + Run(" | ") + Mf("Born", "\\@ \"dd/MM/yy\"") + Run(" | ") + Mf("Born", "\\@ \"ddd, d MMM yyyy h:mm AM/PM\"")), People),

            new("if-text", P(Run("Region: ") + Cf("Other", string.Empty, " IF ", Mf("State"), " = \"UK\" \"British\" \"Other\" ")), People),

            new("if-numeric", P(Cf("Standard", string.Empty, " IF ", Mf("Balance"), " >= 1000 \"Gold\" \"Standard\" ")), People),

            new("if-wildcard", P(Cf("no", string.Empty, " IF ", Mf("Last"), " = \"L*\" \"starts with L\" \"no\" ") + Run(" / ") + Cf("no", string.Empty, " IF ", Mf("Last"), " = \"?orvalds\" \"seven letters\" \"no\" ")), People),

            new("nested-if", P(Cf("x", string.Empty, " IF ", Mf("Qty"), " > 0 \"", Cf("y", string.Empty, " IF ", Mf("Qty"), " > 5 \"many\" \"few\" "), "\" \"none\" ")), People),

            new("next-two-up", P(Mf("First") + Run(" ") + Mf("Last") + Cf(string.Empty, string.Empty, " NEXT ") + Run(" and ") + Mf("First") + Run(" ") + Mf("Last")),
                """[{"First":"A","Last":"One"},{"First":"B","Last":"Two"},{"First":"C","Last":"Three"},{"First":"D","Last":"Four"},{"First":"E","Last":"Five"}]"""),

            new("nextif", P(Mf("First") + Cf(string.Empty, string.Empty, " NEXTIF ", Mf("Qty"), " > 0 ") + Run(" then ") + Mf("First")), People),

            new("skipif", P(Cf(string.Empty, string.Empty, " SKIPIF ", Mf("Qty"), " = 0 ") + Run("Order for ") + Mf("First")), People),

            new("mergerec-seq", P(Cf(string.Empty, string.Empty, " SKIPIF ", Mf("Qty"), " = 0 ") + Run("Record ") + Cf("1", string.Empty, " MERGEREC ") + Run(", letter ") + Cf("1", string.Empty, " MERGESEQ ") + Run(" of ") + Mf("First")), People),

            new("set-ref", P(Cf(string.Empty, string.Empty, " SET greeting \"Hello\" ") + Cf("Hello", string.Empty, " REF greeting ") + Run(", ") + Mf("First") + Run(". ") + Cf("Hello", string.Empty, " REF greeting \\* Upper ")), People),

            new("ask-fillin", P(Cf(string.Empty, string.Empty, " ASK dept \"Which department?\" \\d \"Sales\" ") + Cf("Sales", string.Empty, " REF dept ") + Run(": ") + Cf("x", string.Empty, " FILLIN \"Signed by\" \\d \"Anon\" ") + Run(" / ") + Mf("First")),
                People, Answers: ["Signed by=J. Smith"]),

            new("header-footer-settings",
                P(Run("Body of the letter to ") + Mf("First") + Run(".")), People,
                Header: P(Run("To: ") + Mf("Last") + Run(" – page ") + Cf("1", string.Empty, " PAGE ")), Settings: true),

            new("single-document", P("<w:bookmarkStart w:id=\"0\" w:name=\"top\"/>" + Run("Letter to ") + Mf("First") + "<w:bookmarkEnd w:id=\"0\"/>") + P(Run("From ") + Mf("City")) + P("<w:r><w:drawing><wp:inline><wp:extent cx=\"100\" cy=\"100\"/><wp:docPr id=\"1\" name=\"Picture 1\"/><a:graphic xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><a:graphicData uri=\"urn:x\"/></a:graphic></wp:inline></w:drawing></w:r>"),
                People, Single: true),

            new("single-table-last", P(Run("List")) + "<w:tbl><w:tblPr><w:tblW w:w=\"0\" w:type=\"auto\"/></w:tblPr><w:tblGrid><w:gridCol w:w=\"3000\"/></w:tblGrid><w:tr><w:tc><w:tcPr><w:tcW w:w=\"3000\" w:type=\"dxa\"/></w:tcPr>" + P(Mf("First")) + "</w:tc></w:tr></w:tbl>",
                People, Single: true),

            new("hyperlink-nested", P(Cf("mail", "<w:rStyle w:val=\"Hyperlink\"/>", " HYPERLINK \"mailto:", Mf("Email"), "\" ") + Run(" or ") + Cf("«Email»", string.Empty, " HYPERLINK \"http://example.org\" ")), People),

            new("unsupported-fields", P(Cf("«AddressBlock»", string.Empty, " ADDRESSBLOCK \\f \"&lt;Title&gt; &lt;First&gt; &lt;Last&gt;\" ") + Run(" – ") + Cf("2024", string.Empty, " DATE \\@ \"yyyy\" ") + Run(" – ") + Mf("First")), People),

            new("missing-field", P(Mf("Nickname") + Run("|") + Mf("First")), People),

            new("underscore-names", P(Mf("First_Name") + Run(" ") + Mf("Last")), People),

            new("empty-value-keeps-text", P(Run("A", "<w:i/>") + Mf("Note", string.Empty, "<w:b/>") + Run("B", "<w:i/>") + Mf("Note", "\\f \"!\"") + Run("C")), People),

            new("table-cells", "<w:tbl><w:tblPr><w:tblW w:w=\"0\" w:type=\"auto\"/></w:tblPr><w:tblGrid><w:gridCol w:w=\"3000\"/><w:gridCol w:w=\"3000\"/></w:tblGrid><w:tr><w:tc><w:tcPr><w:tcW w:w=\"3000\" w:type=\"dxa\"/></w:tcPr>" + P(Mf("First")) + "</w:tc><w:tc><w:tcPr><w:tcW w:w=\"3000\" w:type=\"dxa\"/></w:tcPr>" + P(Sf(" MERGEFIELD Last ", "«Last»")) + "</w:tc></w:tr></w:tbl>" + P(string.Empty),
                People),

            new("curly-quotes-if", P(Cf("no", string.Empty, " IF ", Mf("City"), " = \u201CNew York\u201D \u201Cyes\u201D \u201Cno\u201D ")), People),
        };
        return list;
    }

    private const string Prolog = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n";

    /// <summary>Writes the template package for a case as a zip (fixed timestamps, so the bytes are the same every time).</summary>
    public static void Write(Case c, string path)
    {
        string sect = "<w:sectPr>" + (c.Header is not null ? "<w:headerReference w:type=\"default\" r:id=\"rId10\"/>" : string.Empty) + "<w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/></w:sectPr>";
        var overrides = new StringBuilder("<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>");
        var docRels = new StringBuilder();
        var entries = new List<(string Name, string Content)>();
        if (c.Header is not null)
        {
            overrides.Append("<Override PartName=\"/word/header1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml\"/>");
            docRels.Append("<Relationship Id=\"rId10\" Type=\"" + RelType + "header\" Target=\"header1.xml\"/>");
            entries.Add(("word/header1.xml", Prolog + "<w:hdr xmlns:w=\"" + W + "\" xmlns:r=\"" + R + "\">" + c.Header + "</w:hdr>"));
        }

        if (c.Settings)
        {
            overrides.Append("<Override PartName=\"/word/settings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml\"/>");
            docRels.Append("<Relationship Id=\"rId11\" Type=\"" + RelType + "settings\" Target=\"settings.xml\"/>");
            entries.Add(("word/settings.xml", Prolog + "<w:settings xmlns:w=\"" + W + "\" xmlns:r=\"" + R + "\"><w:zoom w:percent=\"100\"/>"
                + "<w:mailMerge><w:mainDocumentType w:val=\"formLetters\"/><w:linkToQuery/><w:dataType w:val=\"native\"/>"
                + "<w:connectString w:val=\"Provider=Microsoft.ACE.OLEDB.12.0;Data Source=C:\\data\\people.xlsx\"/><w:query w:val=\"SELECT * FROM `Sheet1$`\"/>"
                + "<w:dataSource r:id=\"rId1\"/><w:viewMergedData/></w:mailMerge><w:defaultTabStop w:val=\"720\"/></w:settings>"));
            entries.Add(("word/_rels/settings.xml.rels", Prolog + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"" + RelType + "mailMergeSource\" Target=\"file:///C:/data/people.xlsx\" TargetMode=\"External\"/></Relationships>"));
        }

        entries.Add(("[Content_Types].xml", Prolog + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>" + overrides + "</Types>"));
        entries.Add(("_rels/.rels", Prolog + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"" + RelType + "officeDocument\" Target=\"word/document.xml\"/></Relationships>"));
        if (docRels.Length > 0)
        {
            entries.Add(("word/_rels/document.xml.rels", Prolog + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" + docRels + "</Relationships>"));
        }

        entries.Add(("word/document.xml", Prolog + "<w:document xmlns:w=\"" + W + "\" xmlns:r=\"" + R + "\" xmlns:wp=\"" + WP + "\"><w:body>" + c.Body + sect + "</w:body></w:document>"));
        entries.Sort((a, b) => string.CompareOrdinal(a.Name == "[Content_Types].xml" ? "" : a.Name, b.Name == "[Content_Types].xml" ? "" : b.Name));
        using FileStream fs = File.Create(path);
        using var zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create);
        foreach ((string name, string content) in entries)
        {
            System.IO.Compression.ZipArchiveEntry e = zip.CreateEntry(name, System.IO.Compression.CompressionLevel.Optimal);
            e.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
            w.Write(content);
        }
    }
}
