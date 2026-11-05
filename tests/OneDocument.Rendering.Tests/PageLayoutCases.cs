using System.Text;

namespace RecursiveAI.OneDocument.Rendering.Tests;

// Generated documents for the page model: sections, columns, headers and footers, page numbers and page decoration. They join the
// layout case set (LayoutCases.All), so the tests render them and the differential tool sends them to Word.
internal static class PageLayoutCases
{
    private const string Lorem = "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat. Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur.";

    private static string Long(int paragraphs, string tag) =>
        string.Concat(Enumerable.Range(0, paragraphs).Select(i => TestDocx.P(tag + " " + (i + 1) + ". " + Lorem, "<w:spacing w:after=\"120\"/>")));

    private static string Head(string text) => TestDocx.P(text, "<w:keepNext/><w:spacing w:before=\"240\" w:after=\"120\"/>", "<w:b/><w:sz w:val=\"28\"/>");

    private static string Footer(bool total = true) =>
        PageDocs.Story("ftr", "<w:p><w:pPr><w:jc w:val=\"center\"/></w:pPr><w:r><w:t xml:space=\"preserve\">Page </w:t></w:r>" + PageDocs.PageField("PAGE", "1") +
            (total ? "<w:r><w:t xml:space=\"preserve\"> of </w:t></w:r>" + PageDocs.PageField("NUMPAGES", "1") : string.Empty) + "</w:p>");

    private static string Header(string text, string? jc = null) =>
        PageDocs.Story("hdr", TestDocx.P(text, jc is null ? null : "<w:jc w:val=\"" + jc + "\"/>"));

    private static string Hf(string header = "header1.xml", string footer = "footer1.xml") =>
        PageDocs.Ref("header", "default", header) + PageDocs.Ref("footer", "default", footer);

    public static IReadOnlyList<(string Name, byte[] Docx)> All()
    {
        var list = new List<(string, byte[])>();
        void Add(string name, string body, string finalSect, (string, string)[]? parts = null, string? settings = null) =>
            list.Add((name, PageDocs.Build(body, finalSect, parts, settings)));

        string landscape = "<w:pgSz w:w=\"15840\" w:h=\"12240\" w:orient=\"landscape\"/><w:pgMar w:top=\"1080\" w:right=\"1080\" w:bottom=\"1080\" w:left=\"1080\" w:header=\"540\" w:footer=\"540\" w:gutter=\"0\"/>";
        string cols2 = "<w:cols w:num=\"2\" w:space=\"720\"/>";
        (string, string)[] hf = [("header1.xml", Header("Running header")), ("footer1.xml", Footer())];
        string stdHf = Hf();

        Add("70-section-orientation", Head("Portrait") + Long(3, "A") + PageDocs.End(PageDocs.Sect(stdHf)) + Head("Landscape") + Long(4, "B") +
            PageDocs.End(PageDocs.Sect(stdHf, "nextPage", landscape)) + Head("Portrait again") + Long(2, "C"), PageDocs.Sect(stdHf, "nextPage"), hf);

        Add("71-continuous-columns", Head("One column") + Long(2, "A") + PageDocs.End(PageDocs.Sect(string.Empty)) + Long(4, "B") +
            PageDocs.End(PageDocs.Sect(cols2, "continuous")) + Head("One column again") + Long(2, "C"), PageDocs.Sect(string.Empty, "continuous"));

        Add("72-odd-even-breaks", Head("Chapter one") + Long(2, "A") + PageDocs.End(PageDocs.Sect(stdHf)) + Head("Chapter two (odd page)") + Long(2, "B") +
            PageDocs.End(PageDocs.Sect(stdHf, "oddPage")) + Head("Chapter three (even page)") + Long(2, "C"), PageDocs.Sect(stdHf, "evenPage"), hf);

        Add("73-columns-equal-separator", Head("Two columns with a rule") + Long(14, "A"), PageDocs.Sect("<w:cols w:num=\"2\" w:space=\"540\" w:sep=\"1\"/>"));

        Add("74-columns-unequal", Head("Unequal columns") + Long(10, "A"),
            PageDocs.Sect("<w:cols w:num=\"2\" w:equalWidth=\"0\"><w:col w:w=\"2880\" w:space=\"720\"/><w:col w:w=\"5760\"/></w:cols>"));

        Add("75-column-break", TestDocx.P("Left column text before the break.") + "<w:p><w:r><w:t>Text before</w:t></w:r><w:r><w:br w:type=\"column\"/></w:r><w:r><w:t>and after the break.</w:t></w:r></w:p>" +
            Long(2, "Right"), PageDocs.Sect(cols2));

        Add("76-columns-balanced", Head("Balanced columns") + PageDocs.End(PageDocs.Sect(string.Empty)) + Long(5, "A") +
            PageDocs.End(PageDocs.Sect(cols2, "continuous")) + Long(2, "Below"), PageDocs.Sect(string.Empty, "continuous"));

        Add("77-header-footer-numbers", Head("Header and footer") + Long(30, "A"), PageDocs.Sect(stdHf), hf);

        (string, string)[] four =
        [
            ("header1.xml", Header("Default header")),
            ("header2.xml", Header("First page header", "center")),
            ("header3.xml", Header("Even page header", "right")),
            ("footer1.xml", Footer(false)),
        ];
        Add("78-title-page-even-odd", Head("First page differs") + Long(24, "A"),
            PageDocs.Sect(PageDocs.Ref("header", "default", "header1.xml") + PageDocs.Ref("header", "first", "header2.xml") + PageDocs.Ref("header", "even", "header3.xml") + PageDocs.Ref("footer", "default", "footer1.xml") + "<w:titlePg/>"),
            four, "<w:settings xmlns:w=\"" + TestDocx.W + "\"><w:evenAndOddHeaders/></w:settings>");

        (string, string)[] link = [("header1.xml", Header("Part one")), ("header2.xml", Header("Part three")), ("footer1.xml", Footer(false))];
        Add("79-header-linked", Head("Section one") + Long(2, "A") + PageDocs.End(PageDocs.Sect(stdHf)) + Head("Section two inherits") + Long(2, "B") +
            PageDocs.End(PageDocs.Sect(string.Empty, "nextPage")) + Head("Section three has its own") + Long(2, "C"), PageDocs.Sect(Hf("header2.xml"), "nextPage"), link);

        (string, string)[] tall = [("header1.xml", PageDocs.Story("hdr", PageDocs.Paras(6, "Header line"))), ("footer1.xml", PageDocs.Story("ftr", PageDocs.Paras(5, "Footer line")))];
        Add("80-tall-header-footer", Long(14, "A"), PageDocs.Sect(stdHf), tall);

        Add("81-page-number-restart", Head("Front matter") + Long(3, "A") + PageDocs.End(PageDocs.Sect(stdHf + "<w:pgNumType w:fmt=\"lowerRoman\"/>")) + Head("Body restarts at 1") + Long(5, "B"),
            PageDocs.Sect(stdHf + "<w:pgNumType w:start=\"1\"/>", "nextPage"), hf);

        string borders = "<w:pgBorders w:offsetFrom=\"text\"><w:top w:val=\"single\" w:sz=\"12\" w:space=\"12\" w:color=\"1F3864\"/><w:left w:val=\"single\" w:sz=\"12\" w:space=\"12\" w:color=\"1F3864\"/><w:bottom w:val=\"double\" w:sz=\"6\" w:space=\"12\" w:color=\"1F3864\"/><w:right w:val=\"single\" w:sz=\"12\" w:space=\"12\" w:color=\"1F3864\"/></w:pgBorders>";
        Add("82-page-borders", Head("Bordered page") + Long(4, "A"), PageDocs.Sect(borders));

        string mark = "<w:p><w:r><w:rPr><w:noProof/></w:rPr><w:pict><v:shape id=\"PowerPlusWaterMarkObject1\" o:spid=\"_x0000_s2049\" type=\"#_x0000_t136\" style=\"position:absolute;margin-left:0;margin-top:0;width:412.4pt;height:247.45pt;rotation:315;z-index:-251658752;mso-position-horizontal:center;mso-position-horizontal-relative:margin;mso-position-vertical:center;mso-position-vertical-relative:margin\" fillcolor=\"silver\" stroked=\"f\"><v:fill opacity=\".5\"/><v:textpath style=\"font-family:&quot;Calibri&quot;;font-size:1pt\" string=\"DRAFT\"/></v:shape></w:pict></w:r></w:p>";
        Add("83-watermark", Head("Watermarked") + Long(6, "A"), PageDocs.Sect(PageDocs.Ref("header", "default", "header1.xml")), [("header1.xml", PageDocs.Story("hdr", mark).Replace("xmlns:v=", "xmlns:o=\"urn:schemas-microsoft-com:office:office\" xmlns:v=", StringComparison.Ordinal))]);

        Add("84-line-numbers", Head("Numbered lines") + Long(5, "A"), PageDocs.Sect("<w:lnNumType w:countBy=\"1\" w:restart=\"newPage\" w:distance=\"360\"/>"));

        Add("85-vertical-align", Head("Centered on the page") + Long(1, "A"), PageDocs.Sect("<w:vAlign w:val=\"center\"/>"));

        string gutter = "<w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"1440\" w:right=\"1080\" w:bottom=\"1440\" w:left=\"1800\" w:header=\"720\" w:footer=\"720\" w:gutter=\"720\"/>";
        Add("86-mirror-margins-gutter", Head("Mirrored margins") + Long(16, "A"), PageDocs.Sect(stdHf, null, gutter), hf, "<w:settings xmlns:w=\"" + TestDocx.W + "\"><w:mirrorMargins/></w:settings>");

        Add("87-document-grid", Head("Line grid") + Long(4, "A"), PageDocs.Sect("<w:docGrid w:type=\"lines\" w:linePitch=\"480\"/>"));

        return list;
    }
}
