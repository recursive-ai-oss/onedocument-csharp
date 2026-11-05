using System.Globalization;
using System.Text;

namespace RecursiveAI.OneDocument.Rendering.Tests;

// Generated documents with anchored objects: every relative-from base, every wrap type, tight wrap round a triangle, z-order and
// anchors near page and column breaks. The same generator writes the files under testdata/floats for the layout differential.
internal static class FloatSamples
{
    private const string Decl =
        "xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
        "xmlns:wps=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\" xmlns:wp14=\"http://schemas.microsoft.com/office/word/2010/wordprocessingDrawing\"";

    private static int _id;

    private static string E(double pt) => ((long)Math.Round(pt * 12700)).ToString(CultureInfo.InvariantCulture);

    internal static string H(string rel, double offset) => $"<wp:positionH relativeFrom=\"{rel}\"><wp:posOffset>{E(offset)}</wp:posOffset></wp:positionH>";

    internal static string HAlign(string rel, string align) => $"<wp:positionH relativeFrom=\"{rel}\"><wp:align>{align}</wp:align></wp:positionH>";

    internal static string V(string rel, double offset) => $"<wp:positionV relativeFrom=\"{rel}\"><wp:posOffset>{E(offset)}</wp:posOffset></wp:positionV>";

    internal static string VAlign(string rel, string align) => $"<wp:positionV relativeFrom=\"{rel}\"><wp:align>{align}</wp:align></wp:positionV>";

    internal static string Square(string text = "bothSides", double dist = 6) => $"<wp:wrapSquare wrapText=\"{text}\" distT=\"{E(dist)}\" distB=\"{E(dist)}\" distL=\"{E(dist)}\" distR=\"{E(dist)}\"/>";

    internal static string Tight(string polygon, string text = "bothSides", double dist = 4) =>
        $"<wp:wrapTight wrapText=\"{text}\" distL=\"{E(dist)}\" distR=\"{E(dist)}\"><wp:wrapPolygon edited=\"0\">{polygon}</wp:wrapPolygon></wp:wrapTight>";

    internal const string TopBottom = "<wp:wrapTopAndBottom distT=\"76200\" distB=\"76200\"/>";

    internal const string NoWrap = "<wp:wrapNone/>";

    // A drawing anchored in the text; `shape` is the shape's preset geometry, `extra` more attributes of wp:anchor.
    internal static string Anchor(double w, double h, string posH, string posV, string wrap, string shape = "rect", string fill = "9DC3E6", bool behind = false, int z = 1, string extra = "", string name = "float", string sizeRel = "")
    {
        int id = ++_id;
        return $"<w:r><w:drawing><wp:anchor {Decl} distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\" simplePos=\"0\" relativeHeight=\"{z}\" behindDoc=\"{(behind ? 1 : 0)}\" locked=\"0\" layoutInCell=\"1\" allowOverlap=\"1\" {extra}>" +
            $"<wp:simplePos x=\"0\" y=\"0\"/>{posH}{posV}<wp:extent cx=\"{E(w)}\" cy=\"{E(h)}\"/><wp:effectExtent l=\"0\" t=\"0\" r=\"0\" b=\"0\"/>{wrap}<wp:docPr id=\"{id}\" name=\"{name}\" descr=\"{name}\"/><wp:cNvGraphicFramePr/>" +
            "<a:graphic><a:graphicData uri=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\">" +
            $"<wps:wsp><wps:cNvSpPr/><wps:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"{E(w)}\" cy=\"{E(h)}\"/></a:xfrm><a:prstGeom prst=\"{shape}\"><a:avLst/></a:prstGeom><a:solidFill><a:srgbClr val=\"{fill}\"/></a:solidFill><a:ln w=\"9525\"><a:solidFill><a:srgbClr val=\"1F3864\"/></a:solidFill></a:ln></wps:spPr><wps:bodyPr/></wps:wsp>" +
            $"</a:graphicData></a:graphic>{sizeRel}</wp:anchor></w:drawing></w:r>";
    }

    internal static string Words(int count, string seed = "alpha")
    {
        string[] w = ["lorem", "ipsum", "dolor", "sit", "amet", "consectetur", "adipiscing", "elit", "sed", "do", "eiusmod", "tempor", "incididunt", "ut", "labore", "et", "dolore", "magna", "aliqua"];
        var sb = new StringBuilder(seed);
        for (int i = 0; i < count; i++)
        {
            sb.Append(' ').Append(w[(i * 7 + 3) % w.Length]);
        }

        return sb.ToString();
    }

    // One paragraph whose text starts after the anchor run, so the object is anchored in its first line.
    internal static string Para(string anchorRun, int words, string pPr = "") =>
        "<w:p>" + (pPr.Length > 0 ? "<w:pPr>" + pPr + "</w:pPr>" : string.Empty) + anchorRun + "<w:r><w:t xml:space=\"preserve\">" + Words(words) + "</w:t></w:r></w:p>";

    private static string Filler(int count) => string.Concat(Enumerable.Range(0, count).Select(i => TestDocx.P("Filler paragraph " + i)));

    private static string Title(string t) => TestDocx.P(t, "<w:keepNext/>", "<w:b/>");

    internal static string Triangle => "<wp:start x=\"10800\" y=\"0\"/><wp:lineTo x=\"21600\" y=\"21600\"/><wp:lineTo x=\"0\" y=\"21600\"/><wp:lineTo x=\"10800\" y=\"0\"/>";

    internal static IReadOnlyList<(string Name, byte[] Docx)> All()
    {
        _id = 0;
        var list = new List<(string, byte[])>();
        string twoCols = "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/><w:cols w:num=\"2\" w:space=\"720\"/></w:sectPr>";

        // Square wrapping, each side setting.
        foreach (string side in new[] { "bothSides", "left", "right", "largest" })
        {
            list.Add(("wrap-square-" + side, TestDocx.Bytes(Title("wrapSquare " + side) + Para(Anchor(150, 100, H("margin", 140), V("paragraph", 0), Square(side)), 140))));
        }

        list.Add(("wrap-top-bottom", TestDocx.Bytes(Title("wrapTopAndBottom") + Para(Anchor(200, 80, H("margin", 100), V("paragraph", 20), TopBottom), 140))));
        list.Add(("wrap-none-behind-front", TestDocx.Bytes(Title("wrapNone behind and in front") + Para(
            Anchor(120, 80, H("margin", 40), V("paragraph", 10), NoWrap, fill: "F4B183", behind: true, z: 1, name: "behind") +
            Anchor(120, 80, H("margin", 90), V("paragraph", 30), NoWrap, fill: "A9D18E", behind: false, z: 5, name: "front"), 120))));
        list.Add(("wrap-tight-triangle", TestDocx.Bytes(Title("wrapTight around a triangle") + Para(Anchor(180, 160, H("margin", 144), V("paragraph", 0), Tight(Triangle), "triangle", "FFD966"), 220))));
        list.Add(("wrap-through-ellipse-no-polygon", TestDocx.Bytes(Title("wrapThrough with the shape's own outline") + Para(Anchor(170, 170, H("margin", 150), V("paragraph", 0), "<wp:wrapThrough wrapText=\"bothSides\" distL=\"76200\" distR=\"76200\"/>", "ellipse", "C5E0B3"), 220))));

        // Relative-from bases: one object per base, each in its own page so the numbers are easy to read.
        var bases = new StringBuilder(Title("Horizontal and vertical bases"));
        foreach (string rel in new[] { "page", "margin", "column", "character", "leftMargin", "rightMargin", "insideMargin", "outsideMargin" })
        {
            bases.Append(Para(Anchor(40, 20, H(rel, 10), V("paragraph", 0), NoWrap, name: "h-" + rel), 3));
        }

        foreach (string rel in new[] { "page", "margin", "paragraph", "line", "topMargin", "bottomMargin", "insideMargin", "outsideMargin" })
        {
            bases.Append(Para(Anchor(40, 20, H("margin", 0), V(rel, 10), NoWrap, name: "v-" + rel), 3));
        }

        list.Add(("bases", TestDocx.Bytes(bases.ToString())));
        list.Add(("align-percent-simple", TestDocx.Bytes(Title("Alignment, percentages, simple position") + Para(
            Anchor(60, 30, HAlign("margin", "right"), VAlign("margin", "bottom"), NoWrap, name: "right-bottom") +
            Anchor(60, 30, HAlign("page", "center"), VAlign("page", "center"), NoWrap, name: "page-centre") +
            Anchor(60, 30, "<wp:positionH relativeFrom=\"margin\"><wp14:pctPosHOffset>50000</wp14:pctPosHOffset></wp:positionH>", V("margin", 100), NoWrap, name: "half") +
            Anchor(60, 30, H("page", 0), V("page", 0), NoWrap, extra: "", name: "simple").Replace("simplePos=\"0\"", "simplePos=\"1\"").Replace("<wp:simplePos x=\"0\" y=\"0\"/>", "<wp:simplePos x=\"254000\" y=\"254000\"/>"), 10))));

        // Anchors near breaks: the anchor paragraph lands on page 2, so its object does too.
        list.Add(("anchor-on-next-page", TestDocx.Bytes(Title("Anchor moves with its paragraph") + Filler(40) + Para(Anchor(120, 90, H("margin", 300), V("paragraph", 0), Square()), 120, "<w:keepLines/>"))));
        list.Add(("two-columns", TestDocx.Bytes(Title("Float in the second column") + Para(Anchor(100, 80, H("column", 10), V("paragraph", 0), Square()), 60) +
            "<w:p><w:r><w:br w:type=\"column\"/></w:r></w:p>" + Para(Anchor(100, 80, H("column", 10), V("paragraph", 0), Square(), name: "col2"), 120), sectPr: twoCols)));
        // A floating table with the text running round it.
        {
            string cell(string t) => "<w:tc><w:tcPr><w:tcW w:w=\"1800\" w:type=\"dxa\"/></w:tcPr><w:p><w:r><w:t>" + t + "</w:t></w:r></w:p></w:tc>";
            string tbl = "<w:tbl><w:tblPr><w:tblpPr w:leftFromText=\"144\" w:rightFromText=\"144\" w:topFromText=\"72\" w:bottomFromText=\"72\" w:vertAnchor=\"text\" w:horzAnchor=\"margin\" w:tblpX=\"4500\" w:tblpY=\"1\"/><w:tblW w:w=\"3600\" w:type=\"dxa\"/><w:tblLayout w:type=\"fixed\"/><w:tblBorders><w:top w:val=\"single\" w:sz=\"4\"/><w:left w:val=\"single\" w:sz=\"4\"/><w:bottom w:val=\"single\" w:sz=\"4\"/><w:right w:val=\"single\" w:sz=\"4\"/><w:insideH w:val=\"single\" w:sz=\"4\"/><w:insideV w:val=\"single\" w:sz=\"4\"/></w:tblBorders></w:tblPr><w:tblGrid><w:gridCol w:w=\"1800\"/><w:gridCol w:w=\"1800\"/></w:tblGrid>" +
                "<w:tr>" + cell("one") + cell("two") + "</w:tr><w:tr>" + cell("three") + cell("four") + "</w:tr><w:tr>" + cell("five") + cell("six") + "</w:tr></w:tbl>";
            list.Add(("floating-table", TestDocx.Bytes(Title("A floating table") + tbl + Para(string.Empty, 200))));
        }

        // Drop caps and a text frame.
        {
            string cap(string mode, int lines) => "<w:p><w:pPr><w:framePr w:dropCap=\"" + mode + "\" w:lines=\"" + lines + "\" w:wrap=\"around\" w:vAnchor=\"text\" w:hAnchor=\"text\"/><w:spacing w:line=\"240\" w:lineRule=\"auto\"/></w:pPr><w:r><w:rPr><w:sz w:val=\"" + (lines * 24) + "\"/></w:rPr><w:t>L</w:t></w:r></w:p>";
            list.Add(("dropcap", TestDocx.Bytes(Title("A drop cap over three lines") + cap("drop", 3) + "<w:p><w:r><w:t xml:space=\"preserve\">" + Words(130, "orem") + "</w:t></w:r></w:p>" + Title("A capital in the margin") + cap("margin", 2) + "<w:p><w:pPr><w:ind w:left=\"720\"/></w:pPr><w:r><w:t xml:space=\"preserve\">" + Words(40, "orem") + "</w:t></w:r></w:p>")));
            string frame = "<w:framePr w:w=\"2400\" w:h=\"1200\" w:hRule=\"exact\" w:hSpace=\"180\" w:vSpace=\"90\" w:wrap=\"around\" w:hAnchor=\"margin\" w:vAnchor=\"text\" w:x=\"6000\" w:y=\"1\"/>";
            list.Add(("frame", TestDocx.Bytes(Title("A text frame") +
                "<w:p><w:pPr>" + frame + "<w:shd w:val=\"clear\" w:fill=\"FFF2CC\"/></w:pPr><w:r><w:t>Frame paragraph one, in a narrow box.</w:t></w:r></w:p>" +
                "<w:p><w:pPr>" + frame + "<w:shd w:val=\"clear\" w:fill=\"FFF2CC\"/></w:pPr><w:r><w:t>Paragraph two of the same frame.</w:t></w:r></w:p>" +
                Para(string.Empty, 200))));
        }

        // An object anchored in a table cell, positioned from the cell (layoutInCell) and, in a second cell, from the page.
        {
            string cellOf(string body, int w) => "<w:tc><w:tcPr><w:tcW w:w=\"" + w + "\" w:type=\"dxa\"/></w:tcPr>" + body + "</w:tc>";
            string tbl = "<w:tbl><w:tblPr><w:tblW w:w=\"7200\" w:type=\"dxa\"/><w:tblLayout w:type=\"fixed\"/><w:tblBorders><w:top w:val=\"single\" w:sz=\"4\"/><w:left w:val=\"single\" w:sz=\"4\"/><w:bottom w:val=\"single\" w:sz=\"4\"/><w:right w:val=\"single\" w:sz=\"4\"/><w:insideV w:val=\"single\" w:sz=\"4\"/></w:tblBorders></w:tblPr><w:tblGrid><w:gridCol w:w=\"3600\"/><w:gridCol w:w=\"3600\"/></w:tblGrid><w:tr>" +
                cellOf(Para(Anchor(40, 30, H("margin", 20), V("margin", 10), NoWrap, name: "in-cell"), 20), 3600) +
                cellOf(Para(Anchor(40, 30, H("page", 400), V("page", 300), NoWrap, extra: string.Empty, name: "to-page").Replace("layoutInCell=\"1\"", "layoutInCell=\"0\""), 20), 3600) + "</w:tr></w:tbl>";
            list.Add(("float-in-cell", TestDocx.Bytes(Title("Objects anchored in table cells") + tbl + TestDocx.P("after"))));
        }

        // A table inside a text box.
        {
            string cell(string t) => "<w:tc><w:tcPr><w:tcW w:w=\"1500\" w:type=\"dxa\"/></w:tcPr><w:p><w:r><w:t>" + t + "</w:t></w:r></w:p></w:tc>";
            string tbl = "<w:tbl><w:tblPr><w:tblW w:w=\"3000\" w:type=\"dxa\"/><w:tblBorders><w:top w:val=\"single\" w:sz=\"4\"/><w:left w:val=\"single\" w:sz=\"4\"/><w:bottom w:val=\"single\" w:sz=\"4\"/><w:right w:val=\"single\" w:sz=\"4\"/><w:insideH w:val=\"single\" w:sz=\"4\"/><w:insideV w:val=\"single\" w:sz=\"4\"/></w:tblBorders></w:tblPr><w:tblGrid><w:gridCol w:w=\"1500\"/><w:gridCol w:w=\"1500\"/></w:tblGrid>" +
                "<w:tr>" + cell("alpha") + cell("beta") + "</w:tr><w:tr>" + cell("gamma") + cell("delta") + "</w:tr></w:tbl>";
            string box = $"<wps:wsp><wps:cNvSpPr txBox=\"1\"/><wps:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"{E(160)}\" cy=\"{E(90)}\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom><a:solidFill><a:srgbClr val=\"FFF2CC\"/></a:solidFill></wps:spPr><wps:txbx><w:txbxContent xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:p><w:r><w:t>Above the table</w:t></w:r></w:p>{tbl}</w:txbxContent></wps:txbx><wps:bodyPr/></wps:wsp>";
            list.Add(("textbox-table", TestDocx.Bytes(Title("A table in a text box") + "<w:p>" + DrawingSamples.Inline(box, 160, 90, "http://schemas.microsoft.com/office/word/2010/wordprocessingShape", "box", "text box with a table") + "</w:p>")));
        }

        return list;
    }
}
