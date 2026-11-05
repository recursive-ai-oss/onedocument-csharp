using System.IO.Compression;
using System.Text;
using OneRender.Image;
using OneRender.Painting;
using OneRender.Pdf;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Layout;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Rendering.Drawing;

namespace RecursiveAI.OneDocument.Rendering.Tests;

// Generated documents that exercise every DrawingML painting feature. The same generator writes the files committed under
// testdata/drawingml (set ONEDOCUMENT_SAMPLE_DIR to write them, together with the PDF and PNG renderings, to a folder).
internal static class DrawingSamples
{
    private const string Decl =
        "xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
        "xmlns:wps=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\" xmlns:wpg=\"http://schemas.microsoft.com/office/word/2010/wordprocessingGroup\" " +
        "xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"";

    private const string Emu = "12700";
    private static int _id;

    private static string E(double pt) => ((long)Math.Round(pt * 12700)).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string Docpr(string name, string? descr, bool decorative) =>
        $"<wp:docPr id=\"{++_id}\" name=\"{name}\"" + (descr is null ? string.Empty : $" descr=\"{descr}\"") + (decorative
            ? "><a:extLst><a:ext uri=\"{C183D7F6-B498-43B3-948B-1728B52AA6E4}\"><adec:decorative xmlns:adec=\"http://schemas.microsoft.com/office/drawing/2017/decorative\" val=\"1\"/></a:ext></a:extLst></wp:docPr>"
            : "/>");

    private static string Xfrm(double w, double h, string attrs = "") => $"<a:xfrm{attrs}><a:off x=\"0\" y=\"0\"/><a:ext cx=\"{E(w)}\" cy=\"{E(h)}\"/></a:xfrm>";

    internal static string Wsp(string geometry, double w, double h, string paint, string xfrmAttrs = "") =>
        $"<wps:wsp><wps:cNvSpPr/><wps:spPr>{Xfrm(w, h, xfrmAttrs)}{geometry}{paint}</wps:spPr><wps:bodyPr/></wps:wsp>";

    internal static string Prst(string name, string adj = "") => $"<a:prstGeom prst=\"{name}\"><a:avLst>{adj}</a:avLst></a:prstGeom>";

    internal static string Solid(string rgb) => $"<a:solidFill><a:srgbClr val=\"{rgb}\"/></a:solidFill>";

    internal static string Ln(double w, string rgb = "1F3864", string extra = "") => $"<a:ln w=\"{E(w)}\"><a:solidFill><a:srgbClr val=\"{rgb}\"/></a:solidFill>{extra}</a:ln>";

    internal static string Inline(string data, double w, double h, string uri, string name, string? descr = null, bool decorative = false) =>
        $"<w:r><w:drawing><wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\" {Decl}><wp:extent cx=\"{E(w)}\" cy=\"{E(h)}\"/>{Docpr(name, descr, decorative)}<a:graphic><a:graphicData uri=\"{uri}\">{data}</a:graphicData></a:graphic></wp:inline></w:drawing></w:r>";

    private const string WpsUri = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape";
    private const string PicUri = "http://schemas.openxmlformats.org/drawingml/2006/picture";
    private const string GrpUri = "http://schemas.microsoft.com/office/word/2010/wordprocessingGroup";

    private static string ShapeRun(string geometry, double w, double h, string paint, string name, string? descr = null, string xfrmAttrs = "") =>
        Inline(Wsp(geometry, w, h, paint, xfrmAttrs), w, h, WpsUri, name, descr);

    private static string Row(params string[] runs) => "<w:p><w:pPr><w:spacing w:after=\"120\"/></w:pPr>" + string.Join("<w:r><w:t xml:space=\"preserve\">   </w:t></w:r>", runs) + "</w:p>";

    private static string Title(string text) => TestDocx.P(text, "<w:keepNext/><w:spacing w:before=\"120\" w:after=\"60\"/>", "<w:b/>");

    internal static byte[] Png(int w = 64, int h = 48)
    {
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = ((y * w) + x) * 4;
                bool right = x >= w / 2, bottom = y >= h / 2;
                px[i] = (byte)(right ? 40 + (x * 3 % 200) : 230);
                px[i + 1] = (byte)(bottom ? 60 + (y * 4 % 180) : 110);
                px[i + 2] = (byte)(right == bottom ? 200 : 40);
                px[i + 3] = 255;
            }
        }

        return new RgbaImage(w, h, px).ToPng();
    }

    private static string PicRun(string blipChildren, string srcRect, double w, double h, string geometry, string name, string? descr = null, bool decorative = false, string embed = "rIdImg", string fill = "<a:stretch><a:fillRect/></a:stretch>") =>
        Inline($"<pic:pic><pic:nvPicPr><pic:cNvPr id=\"0\" name=\"{name}\"/><pic:cNvPicPr/></pic:nvPicPr><pic:blipFill><a:blip xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" r:embed=\"{embed}\">{blipChildren}</a:blip>{srcRect}{fill}</pic:blipFill><pic:spPr>{Xfrm(w, h)}{geometry}</pic:spPr></pic:pic>", w, h, PicUri, name, descr, decorative);

    internal static IReadOnlyList<(string Name, byte[] Docx)> All()
    {
        _id = 0;
        var list = new List<(string, byte[])>();

        // Every preset geometry, 8 to a row.
        {
            var sb = new StringBuilder(Title("All 186 preset geometries (ECMA-376 Part 1, 20.1.9.18)"));
            var runs = new List<string>();
            foreach (string name in GeometryDefinition.PresetNames)
            {
                runs.Add(ShapeRun(Prst(name), 48, 36, Solid("9DC3E6") + Ln(1), name, name));
                if (runs.Count == 8)
                {
                    sb.Append(Row([.. runs]));
                    runs.Clear();
                }
            }

            if (runs.Count > 0)
            {
                sb.Append(Row([.. runs]));
            }

            list.Add(("presets", Build(sb.ToString())));
        }

        // Adjust values and custom geometry.
        {
            var sb = new StringBuilder(Title("Adjust values and custom geometry"));
            sb.Append(Row(
                ShapeRun(Prst("roundRect", "<a:gd name=\"adj\" fmla=\"val 50000\"/>"), 80, 40, Solid("A9D18E") + Ln(1), "roundRect pill", "pill"),
                ShapeRun(Prst("star5", "<a:gd name=\"adj\" fmla=\"val 20000\"/><a:gd name=\"hf\" fmla=\"val 105146\"/><a:gd name=\"vf\" fmla=\"val 110557\"/>"), 60, 60, Solid("FFD966") + Ln(1), "star5 thin", "star"),
                ShapeRun(Prst("rightArrow", "<a:gd name=\"adj1\" fmla=\"val 20000\"/><a:gd name=\"adj2\" fmla=\"val 80000\"/>"), 90, 40, Solid("F4B183") + Ln(1), "arrow", "arrow"),
                ShapeRun(Prst("pie", "<a:gd name=\"adj1\" fmla=\"val 0\"/><a:gd name=\"adj2\" fmla=\"val 16200000\"/>"), 60, 60, Solid("C5E0B3") + Ln(1), "pie", "pie"),
                ShapeRun(Prst("wedgeRoundRectCallout", "<a:gd name=\"adj1\" fmla=\"val -20833\"/><a:gd name=\"adj2\" fmla=\"val 90000\"/><a:gd name=\"adj3\" fmla=\"val 16667\"/>"), 90, 50, Solid("DEEBF7") + Ln(1), "callout", "callout"),
                ShapeRun("<a:custGeom><a:avLst/><a:gdLst><a:gd name=\"mx\" fmla=\"*/ w 1 2\"/></a:gdLst><a:ahLst/><a:cxnLst/><a:rect l=\"0\" t=\"0\" r=\"r\" b=\"b\"/><a:pathLst><a:path w=\"100\" h=\"100\"><a:moveTo><a:pt x=\"0\" y=\"100\"/></a:moveTo><a:cubicBezTo><a:pt x=\"0\" y=\"0\"/><a:pt x=\"100\" y=\"0\"/><a:pt x=\"100\" y=\"100\"/></a:cubicBezTo><a:arcTo wR=\"50\" hR=\"20\" stAng=\"0\" swAng=\"10800000\"/><a:close/></a:path></a:pathLst></a:custGeom>", 80, 60, Solid("D9B3FF") + Ln(1.5), "custom", "custom path")));
            list.Add(("adjust-custom", Build(sb.ToString())));
        }

        // Fills.
        {
            var sb = new StringBuilder(Title("Solid, transparent and theme colours"));
            sb.Append(Row(
                ShapeRun(Prst("rect"), 60, 40, Solid("C00000"), "red", "red"),
                ShapeRun(Prst("rect"), 60, 40, "<a:solidFill><a:srgbClr val=\"0070C0\"><a:alpha val=\"40000\"/></a:srgbClr></a:solidFill>" + Ln(1), "alpha", "alpha 40%"),
                ShapeRun(Prst("rect"), 60, 40, "<a:solidFill><a:schemeClr val=\"accent1\"/></a:solidFill>", "accent1", "accent 1"),
                ShapeRun(Prst("rect"), 60, 40, "<a:solidFill><a:schemeClr val=\"accent1\"><a:lumMod val=\"20000\"/><a:lumOff val=\"80000\"/></a:schemeClr></a:solidFill>", "lighter80", "accent 1 lighter 80%"),
                ShapeRun(Prst("rect"), 60, 40, "<a:solidFill><a:schemeClr val=\"accent1\"><a:lumMod val=\"50000\"/></a:schemeClr></a:solidFill>", "darker50", "accent 1 darker 50%"),
                ShapeRun(Prst("rect"), 60, 40, "<a:solidFill><a:schemeClr val=\"accent2\"><a:tint val=\"40000\"/></a:schemeClr></a:solidFill>", "tint", "tint"),
                ShapeRun(Prst("rect"), 60, 40, "<a:solidFill><a:schemeClr val=\"accent2\"><a:shade val=\"50000\"/></a:schemeClr></a:solidFill>", "shade", "shade"),
                ShapeRun(Prst("rect"), 60, 40, "<a:solidFill><a:srgbClr val=\"FF0000\"><a:hueOff val=\"7200000\"/><a:satMod val=\"60000\"/></a:srgbClr></a:solidFill>", "hue", "hue")));
            sb.Append(Title("Gradients: linear angles, multiple stops, path"));
            string Stops(string a, string b) => $"<a:gsLst><a:gs pos=\"0\"><a:srgbClr val=\"{a}\"/></a:gs><a:gs pos=\"100000\"><a:srgbClr val=\"{b}\"/></a:gs></a:gsLst>";
            var grads = new List<string>();
            foreach (int ang in new[] { 0, 2700000, 5400000, 8100000, 10800000 })
            {
                grads.Add(ShapeRun(Prst("rect"), 70, 45, $"<a:gradFill>{Stops("FFFFFF", "1F4E79")}<a:lin ang=\"{ang}\" scaled=\"0\"/></a:gradFill>", "lin" + ang, "linear " + (ang / 60000) + " degrees"));
            }

            sb.Append(Row([.. grads]));
            sb.Append(Row(
                ShapeRun(Prst("rect"), 70, 45, "<a:gradFill><a:gsLst><a:gs pos=\"0\"><a:srgbClr val=\"FF0000\"/></a:gs><a:gs pos=\"50000\"><a:srgbClr val=\"FFFF00\"/></a:gs><a:gs pos=\"100000\"><a:srgbClr val=\"00B050\"/></a:gs></a:gsLst><a:lin ang=\"0\"/></a:gradFill>", "three", "three stops"),
                ShapeRun(Prst("ellipse"), 70, 45, $"<a:gradFill>{Stops("FFFFFF", "C00000")}<a:path path=\"circle\"><a:fillToRect l=\"50000\" t=\"50000\" r=\"50000\" b=\"50000\"/></a:path></a:gradFill>", "circle", "circle path"),
                ShapeRun(Prst("rect"), 70, 45, $"<a:gradFill>{Stops("FFFFFF", "7030A0")}<a:path path=\"rect\"><a:fillToRect l=\"0\" t=\"0\" r=\"100000\" b=\"100000\"/></a:path></a:gradFill>", "rectpath", "rect path"),
                ShapeRun(Prst("rect"), 70, 45, "<a:gradFill><a:gsLst><a:gs pos=\"0\"><a:srgbClr val=\"0070C0\"><a:alpha val=\"100000\"/></a:srgbClr></a:gs><a:gs pos=\"100000\"><a:srgbClr val=\"0070C0\"><a:alpha val=\"0\"/></a:srgbClr></a:gs></a:gsLst><a:lin ang=\"0\"/></a:gradFill>" + Ln(1), "fade", "fading blue")));
            sb.Append(Title("Pattern fills"));
            var pats = new List<string>();
            foreach (string pr in new[] { "pct5", "pct25", "pct50", "pct75", "horz", "vert", "ltHorz", "dkVert", "upDiag", "dnDiag", "ltUpDiag", "wdDnDiag", "cross", "diagCross", "smallGrid", "largeGrid", "dotGrid", "smallCheck", "largeCheck", "smallConfetti", "horzBrick", "plaid", "weave", "zigZag" })
            {
                pats.Add(ShapeRun(Prst("rect"), 44, 30, $"<a:pattFill prst=\"{pr}\"><a:fgClr><a:srgbClr val=\"1F3864\"/></a:fgClr><a:bgClr><a:srgbClr val=\"FFF2CC\"/></a:bgClr></a:pattFill>" + Ln(0.5, "7F7F7F"), pr, pr));
                if (pats.Count == 8)
                {
                    sb.Append(Row([.. pats]));
                    pats.Clear();
                }
            }

            sb.Append(Row([.. pats]));
            list.Add(("fills", Build(sb.ToString())));
        }

        // Lines.
        {
            var sb = new StringBuilder(Title("Line widths and dash presets"));
            foreach (double w in new[] { 0.25, 0.75, 2, 4.5, 9 })
            {
                sb.Append(Row(ShapeRun(Prst("line"), 200, 0, Ln(w, "000000"), "w" + w, "line " + w + " pt")));
            }

            foreach (string dash in new[] { "solid", "dot", "dash", "lgDash", "dashDot", "lgDashDot", "lgDashDotDot", "sysDash", "sysDot", "sysDashDot", "sysDashDotDot" })
            {
                sb.Append(Row(ShapeRun(Prst("line"), 200, 0, Ln(2.25, "7030A0", $"<a:prstDash val=\"{dash}\"/>"), dash, dash)));
            }

            sb.Append(Row(ShapeRun(Prst("line"), 200, 0, Ln(2.25, "C00000", "<a:custDash><a:ds d=\"300000\" sp=\"100000\"/><a:ds d=\"100000\" sp=\"100000\"/></a:custDash>"), "custDash", "custom dash")));
            sb.Append(Title("Caps and joins"));
            string Zig(string join, string cap) => "<a:custGeom><a:avLst/><a:gdLst/><a:ahLst/><a:cxnLst/><a:rect l=\"0\" t=\"0\" r=\"r\" b=\"b\"/><a:pathLst><a:path w=\"100\" h=\"60\" fill=\"none\"><a:moveTo><a:pt x=\"5\" y=\"55\"/></a:moveTo><a:lnTo><a:pt x=\"35\" y=\"5\"/></a:lnTo><a:lnTo><a:pt x=\"65\" y=\"55\"/></a:lnTo><a:lnTo><a:pt x=\"95\" y=\"15\"/></a:lnTo></a:path></a:pathLst></a:custGeom>";
            var joins = new List<string>();
            foreach (string cap in new[] { "flat", "rnd", "sq" })
            {
                foreach (string join in new[] { "round", "bevel", "miter" })
                {
                    string j = join == "miter" ? "<a:miter lim=\"800000\"/>" : $"<a:{join}/>";
                    joins.Add(ShapeRun(Zig(join, cap), 70, 45, $"<a:ln w=\"{E(8)}\" cap=\"{cap}\"><a:solidFill><a:srgbClr val=\"2E75B6\"/></a:solidFill>{j}</a:ln>", $"{cap}-{join}", $"cap {cap} join {join}"));
                    if (joins.Count == 5)
                    {
                        sb.Append(Row([.. joins]));
                        joins.Clear();
                    }
                }
            }

            sb.Append(Row([.. joins]));
            sb.Append(Title("Compound lines"));
            foreach (string c in new[] { "sng", "dbl", "thickThin", "thinThick", "tri" })
            {
                sb.Append(Row(ShapeRun(Prst("line"), 200, 0, $"<a:ln w=\"{E(7)}\" cmpd=\"{c}\"><a:solidFill><a:srgbClr val=\"000000\"/></a:solidFill></a:ln>", c, c)));
            }

            sb.Append(Title("Arrowheads (head and tail, small, medium, large)"));
            foreach (string t in new[] { "triangle", "stealth", "diamond", "oval", "arrow" })
            {
                sb.Append(Row(
                    ShapeRun(Prst("line"), 140, 0, Ln(2, "C55A11", $"<a:headEnd type=\"{t}\" w=\"sm\" len=\"sm\"/><a:tailEnd type=\"{t}\" w=\"lg\" len=\"lg\"/>"), t, t),
                    ShapeRun(Prst("line"), 140, 0, Ln(1, "C55A11", $"<a:tailEnd type=\"{t}\" w=\"med\" len=\"med\"/>"), t + "-thin", t + " thin")));
            }

            sb.Append(Row(ShapeRun(Prst("bentConnector3"), 120, 50, Ln(1.5, "385723", "<a:tailEnd type=\"triangle\"/>"), "bent", "elbow connector"), ShapeRun(Prst("curvedConnector3"), 120, 50, Ln(1.5, "385723", "<a:tailEnd type=\"triangle\"/>"), "curved", "curved connector")));
            list.Add(("lines", Build(sb.ToString())));
        }

        // Effects, transforms and groups.
        {
            var sb = new StringBuilder(Title("Shadow, glow, soft edge (reported, not drawn)"));
            sb.Append(Row(
                ShapeRun(Prst("rect"), 80, 50, Solid("5B9BD5") + "<a:effectLst><a:outerShdw blurRad=\"63500\" dist=\"76200\" dir=\"2700000\" algn=\"tl\" rotWithShape=\"0\"><a:srgbClr val=\"000000\"><a:alpha val=\"50000\"/></a:srgbClr></a:outerShdw></a:effectLst>", "shadow", "shadow"),
                ShapeRun(Prst("ellipse"), 80, 50, Solid("ED7D31") + "<a:effectLst><a:glow rad=\"101600\"><a:srgbClr val=\"FFC000\"><a:alpha val=\"40000\"/></a:srgbClr></a:glow></a:effectLst>", "glow", "glow"),
                ShapeRun(Prst("star5"), 70, 70, Solid("70AD47") + "<a:effectLst><a:softEdge rad=\"63500\"/></a:effectLst>", "soft", "soft edge")));
            sb.Append(Title("Rotation and flips"));
            var rot = new List<string>();
            foreach (string attrs in new[] { "", " rot=\"1800000\"", " rot=\"5400000\"", " flipH=\"1\"", " flipV=\"1\"", " rot=\"2700000\" flipH=\"1\"" })
            {
                rot.Add(ShapeRun(Prst("rtTriangle"), 60, 40, Solid("FFD966") + Ln(1.5, "7F6000"), "t" + rot.Count, "right triangle " + attrs.Trim().Replace("\"", string.Empty), attrs));
            }

            sb.Append(Row([.. rot]));
            sb.Append(Title("Groups: child space stretched onto the group, nested, rotated"));
            string Child(int x, int y, int w, int h, string prst, string rgb) =>
                $"<wps:wsp><wps:cNvSpPr/><wps:spPr><a:xfrm><a:off x=\"{x}\" y=\"{y}\"/><a:ext cx=\"{w}\" cy=\"{h}\"/></a:xfrm>{Prst(prst)}{Solid(rgb)}{Ln(1)}</wps:spPr><wps:bodyPr/></wps:wsp>";
            string group = $"<wpg:wgp><wpg:cNvGrpSpPr/><wpg:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"{E(120)}\" cy=\"{E(80)}\"/><a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"1200\" cy=\"800\"/></a:xfrm></wpg:grpSpPr>" +
                Child(0, 0, 1200, 800, "roundRect", "DEEBF7") + Child(100, 100, 400, 600, "ellipse", "9DC3E6") + Child(600, 100, 500, 250, "triangle", "F4B183") +
                $"<wpg:grpSp><wpg:grpSpPr><a:xfrm rot=\"1200000\"><a:off x=\"600\" y=\"400\"/><a:ext cx=\"500\" cy=\"300\"/><a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"100\" cy=\"100\"/></a:xfrm></wpg:grpSpPr>{Child(0, 0, 100, 100, "star5", "FFD966")}{Child(20, 20, 60, 60, "ellipse", "C00000")}</wpg:grpSp></wpg:wgp>";
            sb.Append(Row(Inline(group, 120, 80, GrpUri, "group", "nested group")));
            list.Add(("effects-groups", Build(sb.ToString())));
        }

        // Pictures.
        {
            var sb = new StringBuilder(Title("Pictures: plain, cropped, in a shape, recoloured, tiled"));
            sb.Append(Row(
                PicRun(string.Empty, string.Empty, 96, 72, Prst("rect"), "plain", "A four-colour test picture"),
                PicRun(string.Empty, "<a:srcRect l=\"25000\" t=\"25000\" r=\"25000\" b=\"25000\"/>", 96, 72, Prst("rect"), "crop", "centre crop"),
                PicRun(string.Empty, string.Empty, 96, 72, Prst("ellipse"), "ellipse", "picture in an ellipse"),
                PicRun(string.Empty, string.Empty, 96, 72, Prst("roundRect"), "round", "picture in a rounded rectangle"),
                PicRun(string.Empty, "<a:srcRect l=\"-20000\" r=\"-20000\"/>", 96, 72, Prst("rect"), "pad", "negative crop pads the picture")));
            sb.Append(Row(
                PicRun("<a:grayscl/>", string.Empty, 96, 72, Prst("rect"), "gray", "grayscale"),
                PicRun("<a:duotone><a:srgbClr val=\"1F3864\"/><a:srgbClr val=\"FFE699\"/></a:duotone>", string.Empty, 96, 72, Prst("rect"), "duo", "duotone"),
                PicRun("<a:lum bright=\"30000\" contrast=\"-30000\"/>", string.Empty, 96, 72, Prst("rect"), "lum", "brighter, flatter"),
                PicRun("<a:alphaModFix amt=\"40000\"/>", string.Empty, 96, 72, Prst("rect"), "alpha", "40% opaque"),
                PicRun("<a:biLevel thresh=\"50000\"/>", string.Empty, 96, 72, Prst("rect"), "bilevel", "black and white")));
            sb.Append(Row(
                PicRun(string.Empty, string.Empty, 96, 72, Prst("rect"), "tile", "tiled picture", fill: "<a:tile tx=\"0\" ty=\"0\" sx=\"50000\" sy=\"50000\" flip=\"none\" algn=\"tl\"/>"),
                PicRun(string.Empty, string.Empty, 96, 72, Prst("rect"), "decor", null, decorative: true),
                PicRun(string.Empty, string.Empty, 96, 72, Prst("rect"), "missing", "picture whose part is missing", embed: "rIdMissing"),
                PicRun(string.Empty, string.Empty, 96, 72, Prst("rect"), "emf", "a metafile this build cannot decode", embed: "rIdEmf")));
            sb.Append(TestDocx.P("A picture in the text line "));
            sb.Append("<w:p><w:r><w:t xml:space=\"preserve\">Inline picture </w:t></w:r>" + PicRun(string.Empty, string.Empty, 48, 36, Prst("rect"), "inl", "inline in text") + "<w:r><w:t xml:space=\"preserve\"> sits on the baseline and the line grows to hold it.</w:t></w:r></w:p>");
            sb.Append("<w:p><w:r><w:t>Anchored behind the text:</w:t></w:r><w:r><w:drawing><wp:anchor behindDoc=\"1\" distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\" simplePos=\"0\" relativeHeight=\"1\" locked=\"0\" layoutInCell=\"1\" allowOverlap=\"1\" " + Decl + ">" +
                $"<wp:simplePos x=\"0\" y=\"0\"/><wp:positionH relativeFrom=\"page\"><wp:posOffset>{E(300)}</wp:posOffset></wp:positionH><wp:positionV relativeFrom=\"paragraph\"><wp:posOffset>{E(0)}</wp:posOffset></wp:positionV><wp:extent cx=\"{E(120)}\" cy=\"{E(90)}\"/><wp:wrapNone/>{Docpr("anchored", "anchored picture", false)}" +
                $"<a:graphic><a:graphicData uri=\"{PicUri}\"><pic:pic><pic:nvPicPr><pic:cNvPr id=\"0\" name=\"a\"/><pic:cNvPicPr/></pic:nvPicPr><pic:blipFill><a:blip xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" r:embed=\"rIdImg\"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill><pic:spPr>{Xfrm(120, 90)}{Prst("rect")}</pic:spPr></pic:pic></a:graphicData></a:graphic></wp:anchor></w:drawing></w:r></w:p>");
            byte[] docx = Build(sb.ToString(), pictures: true);
            list.Add(("pictures", docx));
        }

        return list;
    }

    // The test packages: styles from TestDocx plus a PNG part and a bogus metafile part so the picture cases have something to point at.
    private static byte[] Build(string body, bool pictures = false)
    {
        string rels = pictures
            ? "<Relationship Id=\"rIdImg\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"media/image1.png\"/><Relationship Id=\"rIdEmf\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"media/image2.emf\"/>"
            : string.Empty;
        byte[] raw = TestDocx.Bytes(body, extraRels: rels, extraParts: pictures ? [("word/media/image1.png", Png()), ("word/media/image2.emf", new byte[] { 1, 0, 0, 0, 0x6C, 0, 0, 0 })] : null);
        using var ms = new MemoryStream();
        ms.Write(raw);
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Update, leaveOpen: true))
        {
            ZipArchiveEntry ct = zip.GetEntry("[Content_Types].xml")!;
            string xml;
            using (var r = new StreamReader(ct.Open(), Encoding.UTF8))
            {
                xml = r.ReadToEnd();
            }

            ct.Delete();
            xml = xml.Replace("<Default Extension=\"xml\"", "<Default Extension=\"png\" ContentType=\"image/png\"/><Default Extension=\"emf\" ContentType=\"image/x-emf\"/><Default Extension=\"xml\"");
            using var w = new StreamWriter(zip.CreateEntry("[Content_Types].xml").Open(), new UTF8Encoding(false));
            w.Write(xml);
        }

        return ms.ToArray();
    }

    // A one-slide presentation's shapes, for the shared shape painter (slide composition itself is not part of this).
    internal static string SlideTree() =>
        "<p:spTree xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr/>" +
        Slide("roundRect", 40, 40, 300, 120, "<a:gradFill><a:gsLst><a:gs pos=\"0\"><a:schemeClr val=\"accent1\"/></a:gs><a:gs pos=\"100000\"><a:schemeClr val=\"accent1\"><a:lumMod val=\"50000\"/></a:schemeClr></a:gs></a:gsLst><a:lin ang=\"5400000\"/></a:gradFill>", " rot=\"0\"") +
        Slide("star5", 380, 40, 140, 140, Solid("FFC000") + "<a:ln w=\"38100\"><a:solidFill><a:srgbClr val=\"7F6000\"/></a:solidFill></a:ln><a:effectLst><a:outerShdw blurRad=\"50800\" dist=\"38100\" dir=\"2700000\"><a:srgbClr val=\"000000\"><a:alpha val=\"40000\"/></a:srgbClr></a:outerShdw></a:effectLst>", " rot=\"1200000\"") +
        Slide("rightArrow", 40, 200, 260, 90, Solid("70AD47"), " flipH=\"1\"") +
        Slide("cloud", 340, 220, 200, 130, "<a:pattFill prst=\"wdUpDiag\"><a:fgClr><a:srgbClr val=\"2E75B6\"/></a:fgClr><a:bgClr><a:srgbClr val=\"DEEBF7\"/></a:bgClr></a:pattFill><a:ln w=\"19050\"><a:solidFill><a:srgbClr val=\"1F4E79\"/></a:solidFill><a:prstDash val=\"dash\"/></a:ln>", string.Empty) +
        "<p:cxnSp><p:nvCxnSpPr><p:cNvPr id=\"9\" name=\"c\"/><p:cNvCxnSpPr/><p:nvPr/></p:nvCxnSpPr><p:spPr><a:xfrm><a:off x=\"" + E(40) + "\" y=\"" + E(330) + "\"/><a:ext cx=\"" + E(260) + "\" cy=\"0\"/></a:xfrm><a:prstGeom prst=\"line\"><a:avLst/></a:prstGeom></p:spPr><p:style><a:lnRef idx=\"2\"><a:schemeClr val=\"accent2\"/></a:lnRef><a:fillRef idx=\"0\"><a:schemeClr val=\"accent2\"/></a:fillRef><a:effectRef idx=\"0\"><a:schemeClr val=\"accent2\"/></a:effectRef></p:style></p:cxnSp>" +
        "</p:spTree>";

    private static string Slide(string prst, double x, double y, double w, double h, string paint, string xfrmAttrs) =>
        $"<p:sp><p:nvSpPr><p:cNvPr id=\"2\" name=\"{prst}\"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr><p:spPr><a:xfrm{xfrmAttrs}><a:off x=\"{E(x)}\" y=\"{E(y)}\"/><a:ext cx=\"{E(w)}\" cy=\"{E(h)}\"/></a:xfrm><a:prstGeom prst=\"{prst}\"><a:avLst/></a:prstGeom>{paint}</p:spPr></p:sp>";
}

public sealed class DrawingDocumentTests
{
    private static string? SampleDir => Environment.GetEnvironmentVariable("ONEDOCUMENT_SAMPLE_DIR");

    public static IEnumerable<object[]> Names() => DrawingSamples.All().Select(s => new object[] { s.Name });

    [Theory]
    [MemberData(nameof(Names))]
    public void Sample_documents_render_to_tagged_pdf_and_png(string name)
    {
        byte[] docx = DrawingSamples.All().First(s => s.Name == name).Docx;
        using Document doc = Document.Open(new MemoryStream(docx), leaveOpen: false);
        using var pdf = new MemoryStream();
        LayoutDocument layout = DocumentRenderer.WritePdf(doc, pdf, new PdfRenderOptions { Layout = new LayoutOptions { Fonts = TestFonts.Library } });
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(pdf.ToArray(), 0, 4));
        Assert.NotEmpty(layout.Pages);
        byte[] png = DocumentRenderer.RenderPng(layout.Pages[0]);
        if (SampleDir is { } dir)
        {
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name + ".docx"), docx);
            File.WriteAllBytes(Path.Combine(dir, name + ".pdf"), pdf.ToArray());
            for (int i = 0; i < Math.Min(layout.Pages.Count, 4); i++)
            {
                File.WriteAllBytes(Path.Combine(dir, $"{name}-p{i + 1}.png"), DocumentRenderer.RenderPng(layout.Pages[i]));
            }
        }

        Assert.True(png.Length > 2000, "first page is not blank");
    }

    [Fact]
    public void An_inline_picture_takes_its_place_in_the_line_and_raises_it()
    {
        byte[] docx = DrawingSamples.All().First(s => s.Name == "pictures").Docx;
        using Document doc = Document.Open(new MemoryStream(docx), leaveOpen: false);
        LayoutDocument layout = LayoutEngine.Layout(doc, new LayoutOptions { Fonts = TestFonts.Library });
        LayoutLine line = layout.Pages.SelectMany(p => p.Paragraphs).SelectMany(p => p.Lines).First(l => l.Text.StartsWith("Inline picture", StringComparison.Ordinal));
        LayoutRun pic = line.Runs.Single(r => r.RunKind == LayoutRunKind.Drawing);
        Assert.Equal(48, pic.Width, 3);
        Assert.True(line.Ascent >= 36 - 1e-6, "the line is at least as tall as the picture above the baseline");
        Assert.Contains('\uFFFC', line.Text);
        // The text before and after the picture is laid out on the same line, on either side of it.
        double picX = pic.Bounds.X;
        Assert.Contains(line.Runs, r => r.RunKind == LayoutRunKind.Text && r.Bounds.Right <= picX + 1e-6);
        Assert.Contains(line.Runs, r => r.RunKind == LayoutRunKind.Text && r.Bounds.X >= picX + 48 - 1e-6);
    }

    [Fact]
    public void Figures_are_tagged_with_alt_text_and_decorative_pictures_are_artifacts()
    {
        byte[] docx = DrawingSamples.All().First(s => s.Name == "pictures").Docx;
        using Document doc = Document.Open(new MemoryStream(docx), leaveOpen: false);
        LayoutDocument layout = LayoutEngine.Layout(doc, new LayoutOptions { Fonts = TestFonts.Library });
        var notes = new List<DrawingNote>();
        DisplayList list = LayoutPainter.Paint(layout.Pages[0], new PaintOptions { Images = new DrawingImages(), DrawingNotes = notes });
        var figures = new List<string?>();
        int artifacts = 0;
        for (int i = 0; i < list.Count; i++)
        {
            DisplayItem item = list[i];
            if (item.Kind == ItemKind.BeginTag && item.Role == TagRole.Figure)
            {
                figures.Add(item.AltText);
            }
            else if (item.Kind == ItemKind.BeginTag && item.Role == TagRole.Artifact)
            {
                artifacts++;
            }
        }

        Assert.Contains("centre crop", figures);
        Assert.Contains("grayscale", figures);
        Assert.True(artifacts >= 1, "the decorative picture is an artifact");
        Assert.Contains(notes, n => n.Code == "drawing.picture-missing");
        Assert.Contains(notes, n => n.Code == "drawing.picture-undecodable");
    }

    [Fact]
    public void Layout_stays_correct_when_a_drawing_cannot_be_painted()
    {
        // A drawing without extent, and an inline shape of zero size, must not stop the page.
        string body = "<w:p><w:r><w:drawing><wp:inline xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\"/></w:drawing></w:r><w:r><w:t>after</w:t></w:r></w:p>";
        using Document doc = Document.Open(new MemoryStream(TestDocx.Bytes(body)), leaveOpen: false);
        LayoutDocument layout = LayoutEngine.Layout(doc, new LayoutOptions { Fonts = TestFonts.Library });
        Assert.Contains(layout.Notes, n => n.Code == "layout.drawing-skipped");
        Assert.Contains("after", layout.Pages[0].Paragraphs.First().Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_slide_shape_tree_paints_through_the_shared_painter()
    {
        MarkupDocument tree = GeometryTests.Parse(DrawingSamples.SlideTree());
        var list = new DisplayList();
        uint n = 0;
        var ctx = new DrawingContext();
        int shapes = DrawingPainter.DrawShapes(list, () => ItemId.Of(++n), tree.Root, 0, 0, ctx);
        Assert.Equal(5, shapes);
        list.Validate();
        RgbaImage img = DisplayListRasterizer.Render(list, 720, 480, new RasterOptions { Images = ctx.Images });
        Assert.NotEqual(255, img.Pixels[(100 * 720 * 4) + (100 * 4)]);
        using var pdf = new MemoryStream();
        using (var w = new DisplayListPdfWriter(pdf, new PdfOutputOptions { Title = "slide", Language = "en-US", Tagged = true, LeaveOpen = true }, ctx.Images))
        {
            w.AddPage(list, new PdfPageSetup(720, 480));
            w.Finish();
        }

        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(pdf.ToArray(), 0, 4));
        if (Environment.GetEnvironmentVariable("ONEDOCUMENT_SAMPLE_DIR") is { } dir)
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "slide-shapes.xml"), DrawingSamples.SlideTree());
            File.WriteAllBytes(Path.Combine(dir, "slide-shapes.pdf"), pdf.ToArray());
            File.WriteAllBytes(Path.Combine(dir, "slide-shapes.png"), img.ToPng());
        }
    }
}
