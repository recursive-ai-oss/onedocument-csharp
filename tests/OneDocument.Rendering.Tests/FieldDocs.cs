using System.Text;
using OneFont;
using RecursiveAI.OneDocument.Layout;

namespace RecursiveAI.OneDocument.Rendering.Tests;

// Generated documents for fields that layout resolves: contents, references, captions, running heads, indexes and the picture switches.
// They join the layout case set (LayoutCases.All), so the tests lay them out and the differential tool sends them to Word.
internal static class FieldDocs
{
    internal static readonly string Styles =
        "<w:styles xmlns:w=\"" + TestDocx.W + "\"><w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii=\"Liberation Sans\" w:hAnsi=\"Liberation Sans\" w:eastAsia=\"Noto Sans CJK SC\" w:cs=\"Noto Sans Arabic\"/><w:sz w:val=\"24\"/><w:szCs w:val=\"24\"/><w:lang w:val=\"en-US\"/></w:rPr></w:rPrDefault>" +
        "<w:pPrDefault><w:pPr><w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/></w:pPr></w:pPrDefault></w:docDefaults>" +
        "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>" +
        Heading(1) + Heading(2) + Heading(3) +
        "<w:style w:type=\"paragraph\" w:styleId=\"TOC1\"><w:name w:val=\"toc 1\"/><w:basedOn w:val=\"Normal\"/></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"TOC2\"><w:name w:val=\"toc 2\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:ind w:left=\"240\"/></w:pPr></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"TOC3\"><w:name w:val=\"toc 3\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:ind w:left=\"480\"/></w:pPr></w:style>" +
        "<w:style w:type=\"character\" w:styleId=\"LastName\"><w:name w:val=\"Last Name\"/><w:rPr><w:b/></w:rPr></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Caption\"><w:name w:val=\"caption\"/><w:basedOn w:val=\"Normal\"/><w:rPr><w:i/></w:rPr></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Index1\"><w:name w:val=\"index 1\"/><w:basedOn w:val=\"Normal\"/></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Index2\"><w:name w:val=\"index 2\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:ind w:left=\"240\"/></w:pPr></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"TableofAuthorities\"><w:name w:val=\"table of authorities\"/><w:basedOn w:val=\"Normal\"/></w:style>" +
        "</w:styles>";

    private static string Heading(int level) =>
        "<w:style w:type=\"paragraph\" w:styleId=\"Heading" + level + "\"><w:name w:val=\"heading " + level + "\"/><w:basedOn w:val=\"Normal\"/><w:next w:val=\"Normal\"/><w:pPr><w:outlineLvl w:val=\"" + (level - 1) + "\"/></w:pPr><w:rPr><w:b/></w:rPr></w:style>";

    // Two list definitions: numId 1 is 1. / 1.1 (the level text repeats the level above), numId 2 is 1. / a. / i. (each level shows only itself).
    internal const string Numbering =
        "<w:numbering xmlns:w=\"" + TestDocx.W + "\">" +
        "<w:abstractNum w:abstractNumId=\"0\"><w:multiLevelType w:val=\"multilevel\"/>" +
        "<w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"%1.\"/><w:suff w:val=\"space\"/></w:lvl>" +
        "<w:lvl w:ilvl=\"1\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"%1.%2\"/><w:suff w:val=\"space\"/></w:lvl></w:abstractNum>" +
        "<w:abstractNum w:abstractNumId=\"1\"><w:multiLevelType w:val=\"multilevel\"/>" +
        "<w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"%1.\"/><w:suff w:val=\"space\"/></w:lvl>" +
        "<w:lvl w:ilvl=\"1\"><w:start w:val=\"1\"/><w:numFmt w:val=\"lowerLetter\"/><w:lvlText w:val=\"%2.\"/><w:suff w:val=\"space\"/></w:lvl>" +
        "<w:lvl w:ilvl=\"2\"><w:start w:val=\"1\"/><w:numFmt w:val=\"lowerRoman\"/><w:lvlText w:val=\"%3.\"/><w:suff w:val=\"space\"/></w:lvl></w:abstractNum>" +
        "<w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num><w:num w:numId=\"2\"><w:abstractNumId w:val=\"1\"/></w:num></w:numbering>";

    internal const string UpdateFieldsSettings = "<w:settings xmlns:w=\"" + TestDocx.W + "\"><w:updateFields w:val=\"true\"/></w:settings>";

    internal static string Field(string instruction, string cached) => PageDocs.PageField(instruction, cached).Replace("<w:t>", "<w:t xml:space=\"preserve\">", StringComparison.Ordinal);

    internal static string StyledRun(string text, string style) => "<w:r><w:rPr><w:rStyle w:val=\"" + style + "\"/></w:rPr><w:t xml:space=\"preserve\">" + System.Security.SecurityElement.Escape(text) + "</w:t></w:r>";

    internal static string Run(string text) => "<w:r><w:t xml:space=\"preserve\">" + System.Security.SecurityElement.Escape(text) + "</w:t></w:r>";

    internal static string Bookmark(int id, string name, string inner) =>
        "<w:bookmarkStart w:id=\"" + id + "\" w:name=\"" + name + "\"/>" + inner + "<w:bookmarkEnd w:id=\"" + id + "\"/>";

    internal static string Para(string inner, string? pPr = null) => "<w:p>" + (pPr is null ? string.Empty : "<w:pPr>" + pPr + "</w:pPr>") + inner + "</w:p>";

    internal static string Head(int level, string text, string? bookmark = null, int bookmarkId = 0, string extraPPr = "") =>
        Para(bookmark is null ? Run(text) : Bookmark(bookmarkId, bookmark, Run(text)), "<w:pStyle w:val=\"Heading" + level + "\"/>" + extraPPr);

    internal static string Lines(int count, string tag = "Filler") => string.Concat(Enumerable.Range(1, count).Select(i => TestDocx.P(tag + " line " + i)));

    internal static string Numbered(string text, int numId, int level, string? bookmark = null, int bookmarkId = 0) =>
        Para(bookmark is null ? Run(text) : Bookmark(bookmarkId, bookmark, Run(text)), "<w:pStyle w:val=\"Heading" + (level + 1) + "\"/><w:numPr><w:ilvl w:val=\"" + level + "\"/><w:numId w:val=\"" + numId + "\"/></w:numPr>");

    // How many single lines of body text fit on a Letter page with one-inch margins.
    internal static int LinesPerPage
    {
        get
        {
            FontMetrics m = FontLibrary.CreateDefault().Resolve(new FontRequest("Liberation Sans"))!.Face.Metrics;
            double line = (m.Ascender + m.LineGap - m.Descender) * 12.0 / m.UnitsPerEm;
            return (int)Math.Floor(648.0 / line);
        }
    }

    internal static string Footer(string content = "") =>
        PageDocs.Story("ftr", "<w:p><w:pPr><w:jc w:val=\"center\"/></w:pPr><w:r><w:t xml:space=\"preserve\">Page </w:t></w:r>" + PageDocs.PageField("PAGE", "1") + "<w:r><w:t xml:space=\"preserve\"> of </w:t></w:r>" + PageDocs.PageField("NUMPAGES", "1") + content + "</w:p>");

    internal static byte[] Build(string body, string? numbering = null, string? settings = null, (string Name, string Xml)[]? parts = null, string? finalSect = null, string? footnotes = null)
    {
        var rels = new StringBuilder();
        var types = new StringBuilder();
        var extra = new List<(string, byte[])>();
        foreach ((string name, string xml) in parts ?? [])
        {
            string id = "rId_" + name.Replace('.', '_');
            string type = name.StartsWith("header", StringComparison.Ordinal) ? "header" : "footer";
            rels.Append("<Relationship Id=\"" + id + "\" Type=\"" + TestDocx.R + "/" + type + "\" Target=\"" + name + "\"/>");
            types.Append("<Override PartName=\"/word/" + name + "\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml." + type + "+xml\"/>");
            extra.Add(("word/" + name, Encoding.UTF8.GetBytes(xml)));
        }

        if (footnotes is not null)
        {
            rels.Append("<Relationship Id=\"rIdFootnotes\" Type=\"" + TestDocx.R + "/footnotes\" Target=\"footnotes.xml\"/>");
            types.Append("<Override PartName=\"/word/footnotes.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.footnotes+xml\"/>");
            extra.Add(("word/footnotes.xml", Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" + footnotes)));
        }

        return TestDocx.Bytes(body, Styles, numbering, settings, finalSect ?? PageDocs.Sect(parts is null ? string.Empty : HeaderFooterRefs(parts)), rels.ToString(), extra, extraTypes: types.ToString());
    }

    private static string HeaderFooterRefs((string Name, string Xml)[] parts) =>
        string.Concat(parts.Select(p => PageDocs.Ref(p.Name.StartsWith("header", StringComparison.Ordinal) ? "header" : "footer", "default", p.Name)));

    internal static string Footnotes(params string[] texts) =>
        "<w:footnotes xmlns:w=\"" + TestDocx.W + "\"><w:footnote w:type=\"separator\" w:id=\"-1\"><w:p><w:r><w:separator/></w:r></w:p></w:footnote><w:footnote w:type=\"continuationSeparator\" w:id=\"0\"><w:p><w:r><w:continuationSeparator/></w:r></w:p></w:footnote>" +
        string.Concat(texts.Select((t, i) => "<w:footnote w:id=\"" + (i + 1) + "\"><w:p><w:r><w:footnoteRef/></w:r>" + Run(" " + t) + "</w:p></w:footnote>")) + "</w:footnotes>";

    internal static string FootnoteRef(int id) => "<w:r><w:footnoteReference w:id=\"" + id + "\"/></w:r>";

    // ---- the case set ----

    public static IReadOnlyList<(string Name, byte[] Docx)> All()
    {
        var list = new List<(string, byte[])>();
        int n = LinesPerPage;
        (string, string)[] pageFooter = [("footer1.xml", Footer())];

        list.Add(("100-toc-three-levels", Build(TocBody(n, toc: true), settings: UpdateFieldsSettings, parts: pageFooter)));
        list.Add(("101-toc-numbered-headings", Build(
            Para(Field("TOC \\o \"1-2\" \\h \\z", "Update field")) +
            Numbered("Introduction", 1, 0) + Lines(n - 4) + Numbered("Background", 1, 1) + Lines(8) + Numbered("Method", 1, 0) + Lines(20) + Numbered("Results", 1, 0) + Lines(5),
            Numbering, UpdateFieldsSettings, pageFooter)));
        list.Add(("102-toc-switches", Build(
            Para(Field("TOC \\o \"1-2\" \\n 2-2 \\p \" \" \\t \"Caption,3\"", "Update field")) +
            Head(1, "One") + Lines(10) + Head(2, "One detail") + Lines(10) + Para(Run("Table 1 caption"), "<w:pStyle w:val=\"Caption\"/>") + Head(1, "Two") + Lines(5),
            null, UpdateFieldsSettings)));
        list.Add(("103-references", Build(
            Numbered("Scope", 2, 0, "scope", 1) + Numbered("Definitions", 2, 1, "defs", 2) + Numbered("Terms", 2, 2, "terms", 3) + Para(Run("Forward: ") + Field("PAGEREF later \\p", "x") + Run(" / ") + Field("REF terms \\n \\p", "x")) + Lines(n - 4) + Head(1, "Later section", "later", 4) + Lines(3) +
            Para(Run("See page ") + Field("PAGEREF later \\h", "9") + Run(", which is ") + Field("PAGEREF later \\p", "x") + Run(" this paragraph.")) +
            Para(Run("Clause ") + Field("REF terms \\n", "?") + Run(" / ") + Field("REF terms \\r", "?") + Run(" / ") + Field("REF terms \\w", "?") + Run(" in ") + Field("REF scope \\h", "stale") + Run(".")) +
            Para(Run("Page in capitals: ") + Field("PAGEREF later \\* Roman", "I") + Run(", in letters: ") + Field("PAGEREF later \\* alphabetic", "a")), Numbering, null, pageFooter)));
        list.Add(("104-captions", Build(
            Head(1, "Figures") + Para(Run("Figure ") + Field("SEQ Figure \\* ARABIC", "7"), "<w:pStyle w:val=\"Caption\"/>") + Para(Run("Figure ") + Field("SEQ Figure \\* ARABIC", "9"), "<w:pStyle w:val=\"Caption\"/>") +
            Para(Run("Table ") + Field("SEQ Table \\* ROMAN", "V"), "<w:pStyle w:val=\"Caption\"/>") + Head(1, "More figures") +
            Para(Run("Figure ") + Field("SEQ Figure \\* ARABIC \\s 1", "3"), "<w:pStyle w:val=\"Caption\"/>") + Para(Run("Figure ") + Field("SEQ Figure \\* ARABIC \\s 1", "4"), "<w:pStyle w:val=\"Caption\"/>") +
            Para(Run("Repeat: ") + Field("SEQ Figure \\c", "?") + Run(", restart: ") + Field("SEQ Figure \\r 10", "?") + Run(", next: ") + Field("SEQ Figure", "?") + Run(", hidden: [") + Field("SEQ Figure \\h", "?") + Run("]")), null, null)));
        list.Add(("105-styleref-running-head", Build(
            Head(1, "Alpha") + Lines(n - 2) + Head(1, "Beta") + Lines(n - 1) + Head(1, "Gamma") + Lines(n + 8),
            null, null, [("header1.xml", PageDocs.Story("hdr", "<w:p><w:r><w:t xml:space=\"preserve\">First: </w:t></w:r>" + PageDocs.PageField("STYLEREF \"Heading 1\"", "x") + "<w:r><w:t xml:space=\"preserve\"> / Last: </w:t></w:r>" + PageDocs.PageField("STYLEREF \"Heading 1\" \\l", "x") + "</w:p>")), ("footer1.xml", Footer())])));
        list.Add(("106-noteref", Build(
            Para(Run("First note") + FootnoteRef(1) + Run(" and ") + Bookmark(1, "second", FootnoteRef(2)) + Run(" then ") + Field("NOTEREF second", "9") + Run(" and ") + Field("NOTEREF second \\f", "9") + Run(".")),
            null, null, null, null, Footnotes("One", "Two"))));
        list.Add(("107-index", Build(
            Head(1, "Chapter") + Para(Run("Apples are red") + Field("XE \"apples\"", string.Empty) + Run(" and pears are green") + Field("XE \"pears:green\"", string.Empty) + Run(".")) + Bookmark(9, "span", Lines(n)) +
            Para(Run("Another apple") + Field("XE \"apples:red\"", string.Empty) + Run(" on the next page.")) + Para(Run("Bananas") + Field("XE \"bananas\" \\t \"See fruit\"", string.Empty)) +
            Para(Run("Ripe pears") + Field("XE \"pears:green:ripe\"", string.Empty) + Run(" and a span") + Field("XE \"span topic\" \\r span", string.Empty)) +
            Para(Field("INDEX \\h \"A\"", "Update field")), null, UpdateFieldsSettings, pageFooter)));
        list.Add(("108-authorities", Build(
            Para(Run("The court held in Smith v. Jones") + Field("TA \\l \"Smith v. Jones, 1 U.S. 1 (1900)\" \\s \"Smith\" \\c 1", string.Empty) + Run(" and the statute") + Field("TA \\l \"42 U.S.C. 1\" \\s \"Act\" \\c 2", string.Empty) + Run(".")) + Lines(n) +
            Para(Run("Again Smith") + Field("TA \\l \"Smith v. Jones, 1 U.S. 1 (1900)\" \\s \"Smith\" \\c 1", string.Empty) + Run(".")) +
            Para(Field("TOA \\h \\c 0", "Update field")), null, UpdateFieldsSettings, pageFooter)));
        list.Add(("110-index-columns", Build(
            Para(string.Concat(Enumerable.Range(1, 12).Select(i => Run("entry " + i.ToString("D2", System.Globalization.CultureInfo.InvariantCulture) + " ") + Field("XE \"item" + i.ToString("D2", System.Globalization.CultureInfo.InvariantCulture) + "\"", string.Empty)))) +
            Para(Field("INDEX \\c 2", "Update field")), null, UpdateFieldsSettings)));
        list.Add(("111-toc-tc-fields", Build(
            Para(Field("TOC \\f C \\l 1-2", "Update field")) + Para(Run("Alpha text") + Field("TC \"Alpha topic\" \\f C \\l 1", string.Empty)) + Lines(n) +
            Para(Run("Beta text") + Field("TC \"Beta sub-topic\" \\f C \\l 2", string.Empty)) + Para(Run("Ignored") + Field("TC \"Other list\" \\f D", string.Empty)), null, UpdateFieldsSettings)));
        list.Add(("112-styleref-character-style", Build(
            Para(Run("Ann ") + StyledRun("Smith", "LastName")) + Para(Run("Bob ") + StyledRun("Jones", "LastName")) + Lines(n - 2) +
            Para(Run("Cy ") + StyledRun("Wu", "LastName")) + Para(Run("Di ") + StyledRun("Xu", "LastName")),
            null, null, [("header1.xml", PageDocs.Story("hdr", "<w:p><w:r><w:t xml:space=\"preserve\">On this page: </w:t></w:r>" + PageDocs.PageField("STYLEREF \"Last Name\"", "x") + "<w:r><w:t xml:space=\"preserve\"> \u2014 </w:t></w:r>" + PageDocs.PageField("STYLEREF \"Last Name\" \\l", "x") + "</w:p>"))])));
        list.Add(("109-picture-switches", Build(
            Para(Run("Page ") + Field("PAGE \\# \"00\"", "1") + Run(" of ") + Field("NUMPAGES \\* CardText", "one") + Run(" / ") + Field("NUMPAGES \\* Ordinal", "1") + Run(" / ") + Field("PAGE \\* Hex", "1") + Run(" / ") + Field("PAGE \\* ArabicDash", "1") + Run(" / ") + Field("PAGE \\* roman \\* Upper", "i")) +
            Lines(n) + Para(Run("Second page: ") + Field("PAGE \\# \"'p.' 0\"", "1")), null, null, pageFooter)));
        return list;
    }

    // The three-level contents document: the control (toc false) has a plain line where the contents go, so the headings sit where the
    // contents leave them when they are one line long.
    internal static string TocBody(int linesPerPage, bool toc) =>
        (toc ? Para(Field("TOC \\o \"1-3\" \\h \\z \\u", "Update field")) : TestDocx.P("Contents")) +
        Head(1, "Alpha") + Lines(linesPerPage - 12) + Head(2, "Alpha detail") + Head(3, "Alpha deeper") + Lines(6) +
        Head(1, "Beta") + Lines(20) + Head(2, "Beta detail") + Lines(20) + Head(1, "Gamma") + Lines(10);
}
