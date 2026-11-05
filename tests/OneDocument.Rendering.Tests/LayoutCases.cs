using System.Text;

namespace RecursiveAI.OneDocument.Rendering.Tests;

// The generated layout test set: small documents that each exercise one family of paragraph and line features. The tests render all
// of them, and the differential tool compares them with Word's own export.
public static class LayoutCases
{
    private const string Lorem = "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat.";
    private const string Fox = "The quick brown fox jumps over the lazy dog. Pack my box with five dozen liquor jugs. How vexingly quick daft zebras jump!";

    private const string Numbering =
        "<w:numbering xmlns:w=\"" + TestDocx.W + "\">" +
        "<w:abstractNum w:abstractNumId=\"0\"><w:multiLevelType w:val=\"multilevel\"/>" +
        "<w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"%1.\"/><w:lvlJc w:val=\"left\"/><w:pPr><w:ind w:left=\"720\" w:hanging=\"360\"/></w:pPr></w:lvl>" +
        "<w:lvl w:ilvl=\"1\"><w:start w:val=\"1\"/><w:numFmt w:val=\"lowerLetter\"/><w:lvlText w:val=\"%2)\"/><w:lvlJc w:val=\"left\"/><w:pPr><w:ind w:left=\"1440\" w:hanging=\"360\"/></w:pPr></w:lvl>" +
        "<w:lvl w:ilvl=\"2\"><w:start w:val=\"1\"/><w:numFmt w:val=\"lowerRoman\"/><w:lvlText w:val=\"%3.\"/><w:lvlJc w:val=\"right\"/><w:pPr><w:ind w:left=\"2160\" w:hanging=\"180\"/></w:pPr></w:lvl>" +
        "</w:abstractNum>" +
        "<w:abstractNum w:abstractNumId=\"1\"><w:multiLevelType w:val=\"hybridMultilevel\"/>" +
        "<w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"bullet\"/><w:lvlText w:val=\"\u2022\"/><w:lvlJc w:val=\"left\"/><w:pPr><w:ind w:left=\"720\" w:hanging=\"360\"/></w:pPr></w:lvl>" +
        "</w:abstractNum>" +
        "<w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num><w:num w:numId=\"2\"><w:abstractNumId w:val=\"1\"/></w:num>" +
        "</w:numbering>";

    private static string Num(int id, int level) => "<w:numPr><w:ilvl w:val=\"" + level + "\"/><w:numId w:val=\"" + id + "\"/></w:numPr>";

    private static string Repeat(string s, int n) => string.Join(' ', Enumerable.Repeat(s, n));

    private static string R(string text, string? rPr = null) =>
        "<w:r>" + (rPr is null ? string.Empty : "<w:rPr>" + rPr + "</w:rPr>") + "<w:t xml:space=\"preserve\">" + System.Security.SecurityElement.Escape(text) + "</w:t></w:r>";

    private static string Fonts(string family) => "<w:rFonts w:ascii=\"" + family + "\" w:hAnsi=\"" + family + "\" w:cs=\"" + family + "\" w:eastAsia=\"" + family + "\"/>";

    /// <summary>Gets the documents of the set by name.</summary>
    public static IReadOnlyList<(string Name, byte[] Docx)> All()
    {
        var list = new List<(string, byte[])>();
        void Add(string name, string body, string? numbering = null, string? settings = null, string? sectPr = null, string? rels = null, string? styles = null, string? fontTable = null) =>
            list.Add((name, TestDocx.Bytes(body, styles, numbering, settings, sectPr, rels, null, fontTable)));

        Add("01-plain", TestDocx.P("Hello, world."));
        Add("02-wrap", TestDocx.P(Repeat(Lorem, 3)));
        Add("03-justify", TestDocx.P(Repeat(Lorem, 3), "<w:jc w:val=\"both\"/>"));
        Add("04-center-right", TestDocx.P(Lorem, "<w:jc w:val=\"center\"/>") + TestDocx.P(Lorem, "<w:jc w:val=\"right\"/>"));
        Add("05-indents", TestDocx.P(Repeat(Fox, 3), "<w:ind w:left=\"1440\" w:right=\"720\" w:firstLine=\"720\"/>") + TestDocx.P(Repeat(Fox, 3), "<w:ind w:left=\"1440\" w:hanging=\"720\"/>"));
        Add("06-tabs", TestDocx.P("a\tb\tc\td") + TestDocx.P("Name\tQty\tPrice", "<w:tabs><w:tab w:val=\"left\" w:pos=\"2880\"/><w:tab w:val=\"right\" w:pos=\"7200\"/></w:tabs>") + TestDocx.P("Total\t\t12.50", "<w:tabs><w:tab w:val=\"right\" w:leader=\"dot\" w:pos=\"9360\"/></w:tabs>") + TestDocx.P("Item\t1,234.56\t3.14", "<w:tabs><w:tab w:val=\"decimal\" w:pos=\"4320\"/><w:tab w:val=\"center\" w:pos=\"7200\"/></w:tabs>"));
        Add("07-leaders", TestDocx.P("Chapter one\t1", "<w:tabs><w:tab w:val=\"right\" w:leader=\"dot\" w:pos=\"9360\"/></w:tabs>") + TestDocx.P("Chapter two\t23", "<w:tabs><w:tab w:val=\"right\" w:leader=\"hyphen\" w:pos=\"9360\"/></w:tabs>") + TestDocx.P("Chapter three\t456", "<w:tabs><w:tab w:val=\"right\" w:leader=\"underscore\" w:pos=\"9360\"/></w:tabs>"));
        Add("08-line-spacing", TestDocx.P(Repeat(Fox, 2), "<w:spacing w:line=\"360\" w:lineRule=\"auto\"/>") + TestDocx.P(Repeat(Fox, 2), "<w:spacing w:line=\"480\" w:lineRule=\"exact\"/>") + TestDocx.P(Repeat(Fox, 2), "<w:spacing w:line=\"400\" w:lineRule=\"atLeast\"/>"));
        Add("09-paragraph-spacing", TestDocx.P("First", "<w:spacing w:before=\"240\" w:after=\"240\"/>") + TestDocx.P("Second", "<w:spacing w:before=\"480\" w:after=\"120\"/>") + TestDocx.P("Third", "<w:spacing w:before=\"0\" w:after=\"600\"/>") + TestDocx.P("Fourth"));
        Add("10-character-formats", "<w:p>" + R("bold ", "<w:b/>") + R("italic ", "<w:i/>") + R("underline ", "<w:u w:val=\"single\"/>") + R("double ", "<w:u w:val=\"double\"/>") + R("strike ", "<w:strike/>") + R("dstrike ", "<w:dstrike/>") + R("all ", "<w:b/><w:i/><w:u w:val=\"single\"/>") + "</w:p>");
        Add("11-colors-highlight", "<w:p>" + R("red ", "<w:color w:val=\"FF0000\"/>") + R("green ", "<w:color w:val=\"00AA00\"/>") + R("highlight ", "<w:highlight w:val=\"yellow\"/>") + R("shaded ", "<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"CCE5FF\"/>") + R("blue on yellow", "<w:color w:val=\"0000FF\"/><w:highlight w:val=\"yellow\"/>") + "</w:p>");
        Add("12-caps-smallcaps", "<w:p>" + R("all caps text ", "<w:caps/>") + R("Small Caps Text ", "<w:smallCaps/>") + R("normal") + "</w:p>");
        Add("13-super-sub", "<w:p>" + R("E=mc") + R("2", "<w:vertAlign w:val=\"superscript\"/>") + R(" and H") + R("2", "<w:vertAlign w:val=\"subscript\"/>") + R("O") + "</w:p>");
        Add("14-spacing-scale-position", "<w:p>" + R("expanded ", "<w:spacing w:val=\"60\"/>") + R("condensed ", "<w:spacing w:val=\"-20\"/>") + R("wide ", "<w:w w:val=\"150\"/>") + R("narrow ", "<w:w w:val=\"60\"/>") + R("raised ", "<w:position w:val=\"8\"/>") + R("lowered", "<w:position w:val=\"-8\"/>") + "</w:p>");
        Add("15-sizes", "<w:p>" + R("8pt ", "<w:sz w:val=\"16\"/>") + R("12pt ") + R("18pt ", "<w:sz w:val=\"36\"/>") + R("30pt", "<w:sz w:val=\"60\"/>") + "</w:p>" + TestDocx.P("next line after the large one"));
        Add("16-headings", "<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr>" + R("Chapter heading") + "</w:p>" + TestDocx.P(Lorem) + "<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr>" + R("Second heading") + "</w:p>" + TestDocx.P(Lorem));
        Add("17-numbered-list", TestDocx.P("First item", Num(1, 0)) + TestDocx.P("Second item with more text so that it wraps onto a second line to show the hanging indent working properly", Num(1, 0)) + TestDocx.P("Nested a", Num(1, 1)) + TestDocx.P("Nested b", Num(1, 1)) + TestDocx.P("Deep", Num(1, 2)) + TestDocx.P("Third item", Num(1, 0)), Numbering);
        Add("18-bullets", TestDocx.P("Apples", Num(2, 0)) + TestDocx.P("Oranges", Num(2, 0)) + TestDocx.P("Pears with a longer description that wraps across the line to check the label hang", Num(2, 0)) + TestDocx.P("Body text after the list."), Numbering);
        Add("19-arabic-rtl", TestDocx.P("\u0645\u0631\u062D\u0628\u0627 \u0628\u0643\u0645 \u0641\u064A \u0647\u0630\u0627 \u0627\u0644\u0645\u0633\u062A\u0646\u062F \u0627\u0644\u062A\u062C\u0631\u064A\u0628\u064A. \u0647\u0630\u0627 \u0646\u0635 \u0639\u0631\u0628\u064A \u0637\u0648\u064A\u0644 \u064A\u0644\u062A\u0641 \u0639\u0644\u0649 \u0639\u062F\u0629 \u0623\u0633\u0637\u0631 \u0644\u0627\u062E\u062A\u0628\u0627\u0631 \u0627\u0644\u0627\u062A\u062C\u0627\u0647 \u0645\u0646 \u0627\u0644\u064A\u0645\u064A\u0646 \u0625\u0644\u0649 \u0627\u0644\u064A\u0633\u0627\u0631 \u0648\u0643\u0633\u0631 \u0627\u0644\u0623\u0633\u0637\u0631.", "<w:bidi/>", "<w:rtl/>"));
        Add("20-hebrew-rtl", TestDocx.P("\u05E9\u05DC\u05D5\u05DD \u05E2\u05D5\u05DC\u05DD, \u05D6\u05D4\u05D5 \u05DE\u05E1\u05DE\u05DA \u05D1\u05D3\u05D9\u05E7\u05D4 \u05DC\u05D1\u05D3\u05D9\u05E7\u05EA \u05D4\u05DB\u05D9\u05D5\u05D5\u05DF \u05D4\u05D9\u05DE\u05E0\u05D9 \u05E9\u05DC \u05D4\u05D8\u05E7\u05E1\u05D8 \u05D1\u05E9\u05D5\u05E8\u05D5\u05EA \u05D0\u05E8\u05D5\u05DB\u05D5\u05EA.", "<w:bidi/>", "<w:rtl/>"));
        Add("21-mixed-bidi", TestDocx.P("English text then \u0645\u0631\u062D\u0628\u0627 \u0628\u0627\u0644\u0639\u0627\u0644\u0645 and numbers 12345 then \u05E9\u05DC\u05D5\u05DD back to English text again for a long line that wraps around the page."));
        Add("22-chinese", TestDocx.P("\u8FD9\u662F\u4E00\u4E2A\u7528\u4E8E\u6D4B\u8BD5\u6392\u7248\u7684\u4E2D\u6587\u6BB5\u843D\u3002\u5B83\u5305\u542B\u5E38\u7528\u6C49\u5B57\u3001\u6807\u70B9\u7B26\u53F7\uFF0C\u4EE5\u53CA\u8DE8\u8D8A\u591A\u884C\u7684\u957F\u53E5\u5B50\uFF0C\u7528\u6765\u68C0\u67E5\u6362\u884C\u89C4\u5219\u3002\u201C\u5F15\u53F7\u201D\u548C\uFF08\u62EC\u53F7\uFF09\u4E0D\u80FD\u51FA\u73B0\u5728\u884C\u9996\u6216\u884C\u5C3E\u3002\u8FD9\u91CC\u8FD8\u6709\u4E00\u4E9B\u82F1\u6587 English words \u6DF7\u5408\u5728\u4E2D\u6587\u4E2D\u3002", null, "<w:rFonts w:eastAsia=\"SimSun\"/><w:lang w:eastAsia=\"zh-CN\"/>"));
        Add("23-japanese", TestDocx.P("\u3053\u308C\u306F\u30EC\u30A4\u30A2\u30A6\u30C8\u3092\u78BA\u8A8D\u3059\u308B\u305F\u3081\u306E\u65E5\u672C\u8A9E\u306E\u6BB5\u843D\u3067\u3059\u3002\u3072\u3089\u304C\u306A\u3001\u30AB\u30BF\u30AB\u30CA\u3001\u6F22\u5B57\u304C\u6DF7\u5728\u3057\u3066\u3044\u307E\u3059\u3002\u53E5\u8AAD\u70B9\u3084\u62EC\u5F27\u300C\u3053\u308C\u300D\u306E\u6271\u3044\u3082\u78BA\u304B\u3081\u307E\u3059\u3002\u9577\u3044\u6587\u7AE0\u304C\u8907\u6570\u884C\u306B\u308F\u305F\u308A\u307E\u3059\u3002", null, "<w:rFonts w:eastAsia=\"MS Mincho\"/><w:lang w:eastAsia=\"ja-JP\"/>"));
        Add("24-korean", TestDocx.P("\uC774\uAC83\uC740 \uB808\uC774\uC544\uC6C3\uC744 \uD655\uC778\uD558\uAE30 \uC704\uD55C \uD55C\uAD6D\uC5B4 \uBB38\uB2E8\uC785\uB2C8\uB2E4. \uB744\uC5B4\uC4F0\uAE30\uC640 \uC904 \uBC14\uAFC8\uC774 \uC62C\uBC14\uB974\uAC8C \uB3D9\uC791\uD558\uB294\uC9C0 \uD655\uC778\uD569\uB2C8\uB2E4.", null, "<w:rFonts w:eastAsia=\"Malgun Gothic\"/><w:lang w:eastAsia=\"ko-KR\"/>"));
        Add("25-multipage", string.Concat(Enumerable.Range(0, 60).Select(i => TestDocx.P("Paragraph " + i + ". " + Lorem, "<w:spacing w:after=\"120\"/>"))));
        Add("26-keep-and-widow", string.Concat(Enumerable.Range(0, 44).Select(i => TestDocx.P("Filler line " + i))) + TestDocx.P("Heading kept with next", "<w:keepNext/>") + TestDocx.P(Repeat(Fox, 3)) + TestDocx.P(Repeat(Fox, 6), "<w:keepLines/>"));
        Add("27-page-breaks", TestDocx.P("Page one") + "<w:p><w:r><w:br w:type=\"page\"/></w:r></w:p>" + TestDocx.P("Page two") + TestDocx.P("Page three", "<w:pageBreakBefore/>"));
        Add("28-sections", "<w:p><w:pPr><w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/></w:sectPr></w:pPr>" + R("Portrait section") + "</w:p>" + TestDocx.P(Lorem), null, null, "<w:sectPr><w:pgSz w:w=\"15840\" w:h=\"12240\" w:orient=\"landscape\"/><w:pgMar w:top=\"1080\" w:right=\"1080\" w:bottom=\"1080\" w:left=\"1080\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/></w:sectPr>");
        Add("29-borders-shading", TestDocx.P("Boxed paragraph with shading and borders on all sides", "<w:pBdr><w:top w:val=\"single\" w:sz=\"8\" w:space=\"4\" w:color=\"000000\"/><w:left w:val=\"single\" w:sz=\"8\" w:space=\"4\" w:color=\"000000\"/><w:bottom w:val=\"single\" w:sz=\"8\" w:space=\"4\" w:color=\"000000\"/><w:right w:val=\"single\" w:sz=\"8\" w:space=\"4\" w:color=\"000000\"/></w:pBdr><w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"EEEEEE\"/><w:ind w:left=\"360\" w:right=\"360\"/>") + TestDocx.P("Following paragraph."));
        Add("30-hyperlink", "<w:p>" + R("See ") + "<w:hyperlink r:id=\"rIdL\"><w:r><w:rPr><w:rStyle w:val=\"Hyperlink\"/></w:rPr><w:t>the project page</w:t></w:r></w:hyperlink>" + R(" for details.") + "</w:p>", null, null, null, "<Relationship Id=\"rIdL\" Type=\"" + TestDocx.R + "/hyperlink\" Target=\"https://example.org/\" TargetMode=\"External\"/>");
        Add("31-font-substitution", TestDocx.P("Calibri body text", null, Fonts("Calibri")) + TestDocx.P("Cambria serif text", null, Fonts("Cambria")) + TestDocx.P("Arial sans text", null, Fonts("Arial")) + TestDocx.P("Times New Roman text", null, Fonts("Times New Roman")) + TestDocx.P("Courier New mono text", null, Fonts("Courier New")) + TestDocx.P("Aptos default text", null, Fonts("Aptos")) + TestDocx.P("Unknown face text", null, Fonts("Zzyzx Display")));
        Add("32-line-breaks", "<w:p>" + R("first line") + "<w:r><w:br/></w:r>" + R("second line") + "<w:r><w:br/></w:r>" + R("third line, which is long enough to wrap around when the paragraph is justified in the page", null) + "</w:p>");
        Add("33-empty-paragraphs", "<w:p/>" + TestDocx.P("after one empty") + "<w:p/><w:p/>" + TestDocx.P("after two empty") + "<w:p><w:pPr><w:rPr><w:sz w:val=\"48\"/></w:rPr></w:pPr></w:p>" + TestDocx.P("after a large empty paragraph"));
        Add("34-special-characters", TestDocx.P("Non\u00A0breaking\u00A0spaces and a non\u2011breaking hyphen, an em\u2014dash, an ellipsis\u2026 and \u201Ccurly quotes\u201D \u00A9 \u00AE \u2122 \u00E9 \u00F1 \u00FC \u00DF"));
        Add("35-long-words", TestDocx.P("Supercalifragilisticexpialidocious_and_a_very_long_unbreakable_identifier_that_exceeds_the_line_width_of_the_page_by_a_wide_margin followed by short words."));
        Add("36-distribute-and-last-line", TestDocx.P("distribute these few words", "<w:jc w:val=\"distribute\"/>") + TestDocx.P(Repeat(Fox, 2), "<w:jc w:val=\"both\"/>"));
        Add("37-kerning", TestDocx.P("AVATAR To Wa Ty AVAVAV (unkerned)") + TestDocx.P("AVATAR To Wa Ty AVAVAV (kerned)", null, "<w:kern w:val=\"2\"/>"));
        Add("38-default-tab-stop", TestDocx.P("a\tb\tc\td"), null, "<w:settings xmlns:w=\"" + TestDocx.W + "\"><w:defaultTabStop w:val=\"1440\"/><w:compat><w:compatSetting w:name=\"compatibilityMode\" w:uri=\"http://schemas.microsoft.com/office/word\" w:val=\"15\"/></w:compat></w:settings>");
        Add("39-wide-document", string.Concat(Enumerable.Range(0, 200).Select(i => TestDocx.P(i + ": " + Fox))));
        Add("40-thai", TestDocx.P("\u0E19\u0E35\u0E48\u0E04\u0E37\u0E2D\u0E22\u0E48\u0E2D\u0E2B\u0E19\u0E49\u0E32\u0E20\u0E32\u0E29\u0E32\u0E44\u0E17\u0E22\u0E2A\u0E33\u0E2B\u0E23\u0E31\u0E1A\u0E17\u0E14\u0E2A\u0E2D\u0E1A\u0E01\u0E32\u0E23\u0E41\u0E1A\u0E48\u0E07\u0E1A\u0E23\u0E23\u0E17\u0E31\u0E14"));
        list.AddRange(TableCases.All());
        return list;
    }
}
