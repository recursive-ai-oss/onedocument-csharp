using System.Text;

namespace RecursiveAI.OneDocument.Rendering.Tests;

// Generated table documents for the layout differential and the render tests: one family of table features per document.
public static class TableCases
{
    private const string Box =
        "<w:tblBorders><w:top w:val=\"single\" w:sz=\"4\"/><w:left w:val=\"single\" w:sz=\"4\"/><w:bottom w:val=\"single\" w:sz=\"4\"/><w:right w:val=\"single\" w:sz=\"4\"/>" +
        "<w:insideH w:val=\"single\" w:sz=\"4\"/><w:insideV w:val=\"single\" w:sz=\"4\"/></w:tblBorders>";

    private const string Words = "The quick brown fox jumps over the lazy dog. Pack my box with five dozen liquor jugs.";
    private const string Compat15 = "<w:settings xmlns:w=\"" + TestDocx.W + "\"><w:compat><w:compatSetting w:name=\"compatibilityMode\" w:uri=\"http://schemas.microsoft.com/office/word\" w:val=\"15\"/></w:compat></w:settings>";

    internal static string Tc(string content, string? tcPr = null) => "<w:tc>" + (tcPr is null ? string.Empty : "<w:tcPr>" + tcPr + "</w:tcPr>") + content + "</w:tc>";

    internal static string Tr(string cells, string? trPr = null) => "<w:tr>" + (trPr is null ? string.Empty : "<w:trPr>" + trPr + "</w:trPr>") + cells + "</w:tr>";

    internal static string Tbl(string rows, string? tblPr = null, params int[] grid) =>
        "<w:tbl><w:tblPr>" + tblPr + "</w:tblPr><w:tblGrid>" + string.Concat(grid.Select(g => "<w:gridCol w:w=\"" + g + "\"/>")) + "</w:tblGrid>" + rows + "</w:tbl>";

    private static string P(string t, string? pPr = null) => TestDocx.P(t, pPr);

    private static string Grid(int rows, int cols, Func<int, int, string> text, string? tblPr = null, int colWidth = 2000, string? trPr = null) =>
        Tbl(
            string.Concat(Enumerable.Range(0, rows).Select(r => Tr(string.Concat(Enumerable.Range(0, cols).Select(c => Tc(P(text(r, c))))), trPr))),
            Box + tblPr,
            Enumerable.Repeat(colWidth, cols).ToArray());

    /// <summary>Gets the table documents by name.</summary>
    public static IReadOnlyList<(string Name, byte[] Docx)> All()
    {
        var list = new List<(string, byte[])>();
        void Add(string name, string body, string? settings = null) => list.Add((name, TestDocx.Bytes(body, null, null, settings)));
        string Gap = P(string.Empty);

        Add("tbl-01-simple-grid", P("A plain three by four table.") + Grid(4, 3, (r, c) => "r" + r + "c" + c));
        Add("tbl-02-fixed-widths", Grid(3, 3, (r, c) => Words, "<w:tblLayout w:type=\"fixed\"/>", 3000) + Gap +
            Tbl(Tr(Tc(P(Words), "<w:tcW w:w=\"1500\" w:type=\"dxa\"/>") + Tc(P(Words), "<w:tcW w:w=\"6000\" w:type=\"dxa\"/>")), Box + "<w:tblLayout w:type=\"fixed\"/>", 3000, 3000));
        Add("tbl-03-autofit-content", Grid(3, 3, (r, c) => c == 0 ? "Short" : c == 1 ? "A medium length cell" : Words + " " + Words, "<w:tblW w:w=\"0\" w:type=\"auto\"/>") + Gap +
            Grid(2, 2, (r, c) => "Autofit to contents " + r + c, "<w:tblW w:w=\"0\" w:type=\"auto\"/>"));
        Add("tbl-04-percent-width", Grid(2, 3, (r, c) => "Cell " + r + c, "<w:tblW w:w=\"2500\" w:type=\"pct\"/>") + Gap + Grid(2, 3, (r, c) => "Cell " + r + c, "<w:tblW w:w=\"5000\" w:type=\"pct\"/>") + Gap +
            Grid(2, 3, (r, c) => "Cell " + r + c, "<w:tblW w:w=\"6000\" w:type=\"dxa\"/>"));
        Add("tbl-05-spans-and-merges",
            Tbl(
                Tr(Tc(P("Spans two"), "<w:gridSpan w:val=\"2\"/>") + Tc(P("c"))) +
                Tr(Tc(P("Merged down three rows"), "<w:vMerge w:val=\"restart\"/>") + Tc(P("b1")) + Tc(P("c1"))) +
                Tr(Tc(P(string.Empty), "<w:vMerge/>") + Tc(P("b2")) + Tc(P("c2"))) +
                Tr(Tc(P(string.Empty), "<w:vMerge/>") + Tc(P("Spans two"), "<w:gridSpan w:val=\"2\"/>")),
                Box, 2500, 2500, 2500));
        Add("tbl-06-margins-and-spacing",
            Grid(2, 3, (r, c) => "Spaced " + r + c, "<w:tblCellSpacing w:w=\"60\" w:type=\"dxa\"/><w:tblCellMar><w:top w:w=\"60\" w:type=\"dxa\"/><w:left w:w=\"200\" w:type=\"dxa\"/><w:bottom w:w=\"60\" w:type=\"dxa\"/><w:right w:w=\"200\" w:type=\"dxa\"/></w:tblCellMar>") + Gap +
            Grid(2, 3, (r, c) => "Tight " + r + c, "<w:tblCellMar><w:left w:w=\"0\" w:type=\"dxa\"/><w:right w:w=\"0\" w:type=\"dxa\"/></w:tblCellMar>"));
        Add("tbl-07-shading-and-valign",
            Tbl(
                Tr(
                    Tc(P("Top") + P("two") + P("three"), "<w:shd w:val=\"clear\" w:fill=\"FFE699\"/>") +
                    Tc(P("Centre"), "<w:vAlign w:val=\"center\"/><w:shd w:val=\"clear\" w:fill=\"C5E0B3\"/>") +
                    Tc(P("Bottom"), "<w:vAlign w:val=\"bottom\"/><w:shd w:val=\"pct25\" w:color=\"000000\" w:fill=\"FFFFFF\"/>") +
                    Tc(P("Solid"), "<w:shd w:val=\"solid\" w:color=\"BDD7EE\" w:fill=\"auto\"/>")),
                Box, 2000, 2000, 2000, 2000));
        Add("tbl-08-row-heights",
            Tbl(
                Tr(Tc(P("At least 1 inch")), "<w:trHeight w:val=\"1440\" w:hRule=\"atLeast\"/>") +
                Tr(Tc(P("Exactly half an inch") + P("clipped second line") + P("clipped third line")), "<w:trHeight w:val=\"720\" w:hRule=\"exact\"/>") +
                Tr(Tc(P(Words + Words + Words)), "<w:trHeight w:val=\"300\" w:hRule=\"atLeast\"/>"),
                Box, 4000));
        Add("tbl-09-nested",
            Tbl(
                Tr(
                    Tc(P("Outer cell") + Grid(2, 2, (r, c) => "in" + r + c, "<w:tblW w:w=\"5000\" w:type=\"pct\"/>", 1200) + P("after the nested table")) +
                    Tc(P(Words))),
                Box, 5000, 3000));
        Add("tbl-10-header-repeat",
            Tbl(
                Tr(Tc(P("No.")) + Tc(P("Description")), "<w:tblHeader/>") +
                string.Concat(Enumerable.Range(1, 120).Select(i => Tr(Tc(P(i.ToString(System.Globalization.CultureInfo.InvariantCulture))) + Tc(P("Row " + i + " " + Words))))),
                Box, 1000, 7000));
        Add("tbl-11-row-split",
            string.Concat(Enumerable.Range(0, 30).Select(i => P("Filler " + i))) +
            Tbl(Tr(Tc(string.Concat(Enumerable.Range(0, 40).Select(i => P("Splittable cell line " + i)))) + Tc(P("Short neighbour"))), Box, 5000, 3000));
        Add("tbl-12-cant-split",
            string.Concat(Enumerable.Range(0, 38).Select(i => P("Filler " + i))) +
            Tbl(Tr(Tc(string.Concat(Enumerable.Range(0, 8).Select(i => P("Row that cannot split " + i)))), "<w:cantSplit/>") + Tr(Tc(P("Next row"))), Box, 5000));
        Add("tbl-13-border-conflicts",
            Tbl(
                Tr(
                    Tc(P("thin red | thick blue"), "<w:tcBorders><w:right w:val=\"single\" w:sz=\"4\" w:color=\"FF0000\"/></w:tcBorders>") +
                    Tc(P("thick blue"), "<w:tcBorders><w:left w:val=\"single\" w:sz=\"24\" w:color=\"0000FF\"/><w:bottom w:val=\"double\" w:sz=\"6\"/></w:tcBorders>") +
                    Tc(P("table inside"))) +
                Tr(Tc(P("dashed"), "<w:tcBorders><w:top w:val=\"dashed\" w:sz=\"12\"/></w:tcBorders>") + Tc(P("none"), "<w:tcBorders><w:top w:val=\"nil\"/><w:left w:val=\"nil\"/></w:tcBorders>") + Tc(P("dotted"), "<w:tcBorders><w:bottom w:val=\"dotted\" w:sz=\"12\"/></w:tcBorders>")),
                "<w:tblBorders><w:top w:val=\"single\" w:sz=\"8\"/><w:left w:val=\"single\" w:sz=\"8\"/><w:bottom w:val=\"single\" w:sz=\"8\"/><w:right w:val=\"single\" w:sz=\"8\"/><w:insideH w:val=\"single\" w:sz=\"4\" w:color=\"808080\"/><w:insideV w:val=\"single\" w:sz=\"4\" w:color=\"808080\"/></w:tblBorders>", 2500, 2500, 2500));
        Add("tbl-14-alignment-and-indent",
            Grid(1, 2, (r, c) => "Left aligned", "<w:tblLayout w:type=\"fixed\"/>", 1500) + Gap +
            Grid(1, 2, (r, c) => "Centred", "<w:jc w:val=\"center\"/><w:tblLayout w:type=\"fixed\"/>", 1500) + Gap +
            Grid(1, 2, (r, c) => "Right aligned", "<w:jc w:val=\"right\"/><w:tblLayout w:type=\"fixed\"/>", 1500) + Gap +
            Grid(1, 2, (r, c) => "Indented 1 inch", "<w:tblInd w:w=\"1440\" w:type=\"dxa\"/><w:tblLayout w:type=\"fixed\"/>", 1500));
        Add("tbl-15-compat-15", Grid(2, 3, (r, c) => "Mode 15 " + r + c, "<w:tblLayout w:type=\"fixed\"/>") + Gap + P("Edge of the table sits on the margin in mode 15."), Compat15);
        Add("tbl-16-floating", P("Paragraph before the floating table. " + Words) + Tbl(Tr(Tc(P("Floating"))), Box + "<w:tblpPr w:horzAnchor=\"margin\" w:vertAnchor=\"page\" w:tblpX=\"4000\" w:tblpY=\"6000\"/>", 2500) + P("Paragraph after."));
        Add("tbl-17-right-to-left", Grid(2, 3, (r, c) => "Col " + c, "<w:bidiVisual/><w:tblLayout w:type=\"fixed\"/>"));
        Add("tbl-18-lists-and-paragraphs",
            Tbl(
                Tr(Tc(P("Right aligned", "<w:jc w:val=\"right\"/>") + P("Centred", "<w:jc w:val=\"center\"/>") + P("Indented", "<w:ind w:left=\"360\"/>") + P(Words, "<w:jc w:val=\"both\"/>")) +
                    Tc(P("Spacing", "<w:spacing w:before=\"240\" w:after=\"240\"/>") + P("between", "<w:spacing w:before=\"240\" w:after=\"240\"/>"))),
                Box, 4000, 4000));
        return list;
    }
}
