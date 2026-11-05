using System.Text;
using RecursiveAI.OneDocument.Tools.ChartProbe;
using RecursiveAI.OneDocument.Rendering.Tests;
using static RecursiveAI.OneDocument.Rendering.Tests.TestPptx;

namespace RecursiveAI.OneDocument.Rendering.Tests;

// The generated slide cases: one deck per feature. The tests render them; the layout differential asks PowerPoint for its own PDF of the same files.
internal static class SlideCases
{
    private static IReadOnlyList<(string Name, byte[] Pptx)>? _all;

    internal static IReadOnlyList<(string Name, byte[] Pptx)> All() => _all ??= Build();

    internal static byte[] Get(string name) => All().First(c => c.Name == name).Pptx;

    private static string Title(string text) => Placeholder(2, "type=\"title\"", P(text));

    private static string Body(params string[] paragraphs) => Placeholder(3, "idx=\"1\"", string.Concat(paragraphs));

    internal static string Frame(int id, double x, double y, double w, double h, string table) =>
        $"<p:graphicFrame><p:nvGraphicFramePr><p:cNvPr id=\"{id}\" name=\"Table {id}\"/><p:cNvGraphicFramePr><a:graphicFrameLocks noGrp=\"1\"/></p:cNvGraphicFramePr><p:nvPr/></p:nvGraphicFramePr><p:xfrm><a:off x=\"{E(x)}\" y=\"{E(y)}\"/><a:ext cx=\"{E(w)}\" cy=\"{E(h)}\"/></p:xfrm><a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/table\">{table}</a:graphicData></a:graphic></p:graphicFrame>";

    internal static string Cell(string text, string attrs = "", string tcAttrs = "", string tcInner = "", string rPr = "") =>
        $"<a:tc{attrs}><a:txBody><a:bodyPr/><a:lstStyle/>{(text.Length == 0 ? "<a:p><a:endParaRPr lang=\"en-US\"/></a:p>" : P(text, "", rPr))}</a:txBody><a:tcPr{tcAttrs}>{tcInner}</a:tcPr></a:tc>";

    private static string Lines(string prefix, int count) =>
        string.Concat(Enumerable.Range(1, count).Select(i => P(prefix + " line " + i + " with a few words to fill the width of the box")));

    private static List<(string, byte[])> Build()
    {
        var cases = new List<(string, byte[])>();
        void Add(string name, Deck d) => cases.Add((name, TestPptx.Build(d)));
        SlideSpec S(string shapes, string? notes = null, bool hidden = false) => new() { Shapes = shapes, Notes = notes, Hidden = hidden };

        Add("inherit-position", new Deck
        {
            Slides =
            [
                S(Title("Inherited title position") + Body(P("First level text"), PP(" lvl=\"1\"", string.Empty, R("Second level text")), PP(" lvl=\"2\"", string.Empty, R("Third level text")))),
            ],
        });

        Add("bullets", new Deck
        {
            Slides =
            [
                S(Title("Bullets") + Body(
                    P("Round bullet from the master"),
                    PP(" marL=\"342900\" indent=\"-342900\"", "<a:buFont typeface=\"Wingdings\"/><a:buChar char=\"&#167;\"/>", R("Square bullet in Wingdings")),
                    PP(" marL=\"342900\" indent=\"-342900\"", "<a:buClr><a:srgbClr val=\"C00000\"/></a:buClr><a:buSzPct val=\"150000\"/><a:buChar char=\"–\"/>", R("Red dash at 150 percent")),
                    PP(" marL=\"342900\" indent=\"-342900\"", "<a:buNone/>", R("No bullet here")),
                    PP(" marL=\"342900\" indent=\"-342900\"", "<a:buFont typeface=\"+mj-lt\"/><a:buAutoNum type=\"arabicPeriod\"/>", R("First number")),
                    PP(" marL=\"342900\" indent=\"-342900\"", "<a:buFont typeface=\"+mj-lt\"/><a:buAutoNum type=\"arabicPeriod\"/>", R("Second number")),
                    PP(" marL=\"742950\" indent=\"-285750\" lvl=\"1\"", "<a:buFont typeface=\"+mj-lt\"/><a:buAutoNum type=\"alphaLcParenR\"/>", R("Lower letter inside")),
                    PP(" marL=\"742950\" indent=\"-285750\" lvl=\"1\"", "<a:buFont typeface=\"+mj-lt\"/><a:buAutoNum type=\"alphaLcParenR\"/>", R("Another letter")),
                    PP(" marL=\"342900\" indent=\"-342900\"", "<a:buFont typeface=\"+mj-lt\"/><a:buAutoNum type=\"arabicPeriod\"/>", R("Third number continues")),
                    PP(" marL=\"342900\" indent=\"-342900\"", "<a:buNone/>", R("Interruption")),
                    PP(" marL=\"342900\" indent=\"-342900\"", "<a:buFont typeface=\"+mj-lt\"/><a:buAutoNum type=\"arabicPeriod\"/>", R("Restarted at one")),
                    PP(" marL=\"342900\" indent=\"-342900\"", "<a:buFont typeface=\"+mj-lt\"/><a:buAutoNum type=\"romanUcPeriod\" startAt=\"4\"/>", R("Roman from four")),
                    PP(" marL=\"342900\" indent=\"-342900\"", "<a:buFont typeface=\"+mj-lt\"/><a:buAutoNum type=\"romanUcPeriod\" startAt=\"4\"/>", R("Roman five")))),
            ],
        });

        string longText = Lines("Overflowing", 8);
        Add("autofit", new Deck
        {
            Slides =
            [
                S(Box(2, 30, 30, 320, 200, longText, "<a:bodyPr wrap=\"square\"><a:normAutofit fontScale=\"62500\" lnSpcReduction=\"20000\"/></a:bodyPr>", Solid("F2F2F2")) +
                  Box(3, 370, 30, 320, 200, longText, "<a:bodyPr wrap=\"square\"><a:normAutofit/></a:bodyPr>", Solid("F2F2F2")) +
                  Box(4, 30, 280, 320, 200, longText, "<a:bodyPr wrap=\"square\"><a:noAutofit/></a:bodyPr>", Solid("F2F2F2")) +
                  Box(5, 370, 280, 320, 40, P("Shape grows to hold three lines of text because the shape resizes with its text") , "<a:bodyPr wrap=\"square\"><a:spAutoFit/></a:bodyPr>", Solid("DDEBF7"))),
            ],
        });

        Add("vertical-and-columns", new Deck
        {
            Slides =
            [
                S(Box(2, 30, 30, 90, 300, P("Vertical text reads downward"), "<a:bodyPr vert=\"vert\" wrap=\"square\"><a:noAutofit/></a:bodyPr>", Solid("FFF2CC")) +
                  Box(3, 140, 30, 90, 300, P("Vertical 270 reads upward"), "<a:bodyPr vert=\"vert270\" wrap=\"square\"><a:noAutofit/></a:bodyPr>", Solid("E2F0D9")) +
                  Box(4, 260, 30, 430, 150, Lines("Column", 4), "<a:bodyPr numCol=\"2\" spcCol=\"228600\" wrap=\"square\"><a:noAutofit/></a:bodyPr>", Solid("F2F2F2")) +
                  Box(5, 260, 220, 200, 100, P("Rotated text 90"), "<a:bodyPr rot=\"5400000\" wrap=\"square\"><a:noAutofit/></a:bodyPr>", Solid("FCE4D6")) +
                  Box(6, 500, 220, 190, 100, P("Upright in a rotated shape"), "<a:bodyPr upright=\"1\" wrap=\"square\"><a:noAutofit/></a:bodyPr>", Solid("D9E1F2"), rot: " rot=\"1800000\"")),
            ],
        });

        Add("anchors-wrap", new Deck
        {
            Slides =
            [
                S(Box(2, 30, 30, 200, 120, P("Top"), fill: Solid("F2F2F2")) +
                  Box(3, 250, 30, 200, 120, P("Middle"), "<a:bodyPr anchor=\"ctr\" wrap=\"square\"><a:noAutofit/></a:bodyPr>", Solid("F2F2F2")) +
                  Box(4, 470, 30, 200, 120, P("Bottom"), "<a:bodyPr anchor=\"b\" wrap=\"square\"><a:noAutofit/></a:bodyPr>", Solid("F2F2F2")) +
                  Box(5, 30, 180, 200, 60, P("No wrap keeps this long line on one line"), "<a:bodyPr wrap=\"none\" anchor=\"ctr\"><a:noAutofit/></a:bodyPr>", Solid("DDEBF7")) +
                  Box(6, 250, 180, 200, 60, P("Centred block", " algn=\"ctr\""), "<a:bodyPr anchor=\"ctr\" anchorCtr=\"1\" wrap=\"none\"><a:noAutofit/></a:bodyPr>", Solid("DDEBF7")) +
                  Box(7, 470, 180, 200, 60, P("Large insets"), "<a:bodyPr lIns=\"457200\" tIns=\"228600\" rIns=\"0\" bIns=\"0\" wrap=\"square\"><a:noAutofit/></a:bodyPr>", Solid("DDEBF7"))),
            ],
        });

        string sentence = "The quick brown fox jumps over the lazy dog and keeps running across the field until the end of the line is reached again. ";
        Add("alignment-spacing", new Deck
        {
            Slides =
            [
                S(Box(2, 30, 20, 330, 500,
                    P(sentence, " algn=\"l\"") + P(sentence, " algn=\"ctr\"") + P(sentence, " algn=\"r\"") + P(sentence, " algn=\"just\"") + P(sentence, " algn=\"dist\"") +
                    PP(" marL=\"457200\" indent=\"-228600\"", string.Empty, R(sentence)) +
                    PP(string.Empty, "<a:lnSpc><a:spcPct val=\"150000\"/></a:lnSpc>", R(sentence)) +
                    PP(string.Empty, "<a:lnSpc><a:spcPts val=\"1800\"/></a:lnSpc><a:spcBef><a:spcPts val=\"1200\"/></a:spcBef><a:spcAft><a:spcPts val=\"600\"/></a:spcAft>", R("Exact 18 point lines with space around " + sentence))) +
                  Box(3, 380, 20, 310, 200,
                    PP(string.Empty, "<a:tabLst><a:tab pos=\"1270000\" algn=\"l\"/></a:tabLst>", R("Tab\tstop at one hundred")) + P("Default\ttab\tstops") + "<a:p>" + R("Line one") + "<a:br><a:rPr lang=\"en-US\"/></a:br>" + R("line two after a break") + "</a:p>") +
                  Box(4, 380, 240, 310, 200, P("שלום עולם مرحبا", " rtl=\"1\" algn=\"r\"") + P("日本語のテキストと English mixed") + P("Left and right"))),
            ],
        });

        Add("runs", new Deck
        {
            Slides =
            [
                new SlideSpec
                {
                    Shapes = Box(2, 30, 30, 660, 300,
                        "<a:p>" + R("Bold ", " b=\"1\"") + R("italic ", " i=\"1\"") + R("underline ", " u=\"sng\"") + R("strike ", " strike=\"sngStrike\"") + R("CAPS ", " cap=\"all\"") + R("small caps", " cap=\"small\"") + "</a:p>" +
                        "<a:p>" + R("sub", " baseline=\"-25000\"") + R("normal", string.Empty) + R("super", " baseline=\"30000\"") + R(" spaced", " spc=\"300\"") + R(" red", "<a:solidFill><a:srgbClr val=\"C00000\"/></a:solidFill>") + R(" highlight", "<a:highlight><a:srgbClr val=\"FFFF00\"/></a:highlight>") + "</a:p>" +
                        "<a:p>" + R("Sizes ", " sz=\"1000\"") + R("twenty ", " sz=\"2000\"") + R("forty", " sz=\"4000\"") + "</a:p>" +
                        "<a:p>" + R("A link", "<a:hlinkClick r:id=\"rId9\"/>") + R(" and plain text after it.") + "</a:p>"),
                    Relationships = [("rId9", "hyperlink", "https://example.org/target", true)],
                },
            ],
        });

        Add("table", new Deck
        {
            Slides =
            [
                S(Title("Tables") + Frame(4, 40, 140, 640, 200,
                    "<a:tbl><a:tblPr firstRow=\"1\" bandRow=\"1\"><a:tableStyleId>{5C22544A-7EE6-4342-B048-85BDC9FD1C3A}</a:tableStyleId></a:tblPr><a:tblGrid><a:gridCol w=\"2540000\"/><a:gridCol w=\"2540000\"/><a:gridCol w=\"3048000\"/></a:tblGrid>" +
                    "<a:tr h=\"381000\">" + Cell("Name") + Cell("Value") + Cell("Notes") + "</a:tr>" +
                    "<a:tr h=\"381000\">" + Cell("Alpha") + Cell("1") + Cell("Banded row") + "</a:tr>" +
                    "<a:tr h=\"381000\">" + Cell("Merged across two columns", " gridSpan=\"2\"") + Cell(string.Empty, " hMerge=\"1\"") + Cell("Right") + "</a:tr>" +
                    "<a:tr h=\"381000\">" + Cell("Tall cell with a very long text that has to wrap inside the narrow first column of the table", string.Empty, " anchor=\"ctr\"") + Cell("Vertical", " rowSpan=\"2\"", " anchor=\"ctr\"", Solid("FFE699")) + Cell("x") + "</a:tr>" +
                    "<a:tr h=\"381000\">" + Cell("Last") + Cell(string.Empty, " vMerge=\"1\"") + Cell("y") + "</a:tr></a:tbl>") +
                  Frame(5, 40, 380, 400, 80,
                    "<a:tbl><a:tblPr/><a:tblGrid><a:gridCol w=\"2540000\"/><a:gridCol w=\"2540000\"/></a:tblGrid><a:tr h=\"381000\">" +
                    Cell("Plain", string.Empty, string.Empty, "<a:lnB w=\"25400\"><a:solidFill><a:srgbClr val=\"C00000\"/></a:solidFill></a:lnB>") + Cell("No style") + "</a:tr></a:tbl>")),
            ],
        });

        Add("slides-hidden-notes", new Deck
        {
            NotesMaster = true,
            FirstSlideNumber = 3,
            Slides =
            [
                S(Title("First slide") + Body(P("Visible content")) + Placeholder(7, "type=\"sldNum\" sz=\"quarter\" idx=\"12\"", "<a:p><a:fld id=\"{B6F15528-21DE-4FAA-801E-634DDDAF4B2B}\" type=\"slidenum\"><a:rPr lang=\"en-US\"/><a:t>‹#›</a:t></a:fld></a:p>"), "Speaker notes for the first slide."),
                S(Title("Hidden slide") + Body(P("Never shown")), hidden: true, notes: "Notes of the hidden slide."),
                S(Title("Third slide") + Body(P("More content")) + Box(8, 30, 480, 300, 30, "<a:p><a:r><a:rPr lang=\"en-US\"/><a:t>Today: </a:t></a:r><a:fld id=\"{11111111-2222-3333-4444-555555555555}\" type=\"datetime1\"><a:rPr lang=\"en-US\"/><a:t>1/1/2000</a:t></a:fld></a:p>"), "Speaker notes for the third slide."),
            ],
        });

        Add("shapes-groups", new Deck
        {
            Slides =
            [
                S(Rect(2, 40, 40, 300, 200, "4472C4") + Rect(3, 140, 100, 300, 200, "ED7D31") +
                  "<p:grpSp><p:nvGrpSpPr><p:cNvPr id=\"10\" name=\"Group\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x=\"" + E(400) + "\" y=\"" + E(300) + "\"/><a:ext cx=\"" + E(280) + "\" cy=\"" + E(160) + "\"/><a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"" + E(140) + "\" cy=\"" + E(80) + "\"/></a:xfrm></p:grpSpPr>" +
                    Box(11, 0, 0, 140, 80, P("Grouped text"), fill: Solid("C6E0B4")) + "</p:grpSp>" +
                  Box(12, 40, 380, 300, 60, P("Over everything", string.Empty, "<a:solidFill><a:srgbClr val=\"FFFFFF\"/></a:solidFill>"), fill: Solid("7030A0"), geometry: "roundRect")),
            ],
        });

        Add("backgrounds", new Deck
        {
            MasterShapes = DefaultMasterShapes + Rect(20, 0, 0, 720, 30, "1F3864"),
            LayoutBackground = "<p:bg><p:bgPr><a:gradFill rotWithShape=\"1\"><a:gsLst><a:gs pos=\"0\"><a:srgbClr val=\"DEEBF7\"/></a:gs><a:gs pos=\"100000\"><a:srgbClr val=\"FFFFFF\"/></a:gs></a:gsLst><a:lin ang=\"5400000\" scaled=\"0\"/></a:gradFill><a:effectLst/></p:bgPr></p:bg>",
            Slides =
            [
                S(Title("Layout background and master shape")),
                new SlideSpec { Shapes = Title("Own background, no master graphics"), Background = "<p:bg><p:bgPr><a:solidFill><a:srgbClr val=\"FFF2CC\"/></a:solidFill><a:effectLst/></p:bgPr></p:bg>", ShowMasterShapes = false },
            ],
        });

        // Questions the standard leaves open, one box each, for PowerPoint to answer: space before the first paragraph, percentage and
        // exact line spacing at one size, a bullet wider than its hanging space, a right-to-left paragraph with margins, a tab stop that
        // is not left aligned.
        Add("spacing-checks", new Deck
        {
            Slides =
            [
                S(Box(2, 20, 20, 160, 130, PP(string.Empty, "<a:spcBef><a:spcPts val=\"2400\"/></a:spcBef>", R("Space before the first paragraph")) + P("Second paragraph"), fill: Solid("F2F2F2")) +
                  Box(3, 200, 20, 160, 200, PP(string.Empty, "<a:lnSpc><a:spcPct val=\"80000\"/></a:lnSpc>", R("Eighty percent line spacing on two lines")) + PP(string.Empty, "<a:lnSpc><a:spcPct val=\"100000\"/></a:lnSpc>", R("One hundred percent on two lines")) + PP(string.Empty, "<a:lnSpc><a:spcPct val=\"150000\"/></a:lnSpc>", R("One hundred fifty on two lines")) + PP(string.Empty, "<a:lnSpc><a:spcPts val=\"3000\"/></a:lnSpc>", R("Exact thirty points here")), fill: Solid("F2F2F2")) +
                  Box(4, 380, 20, 160, 130, PP(" marL=\"228600\" indent=\"-228600\"", "<a:buFont typeface=\"+mj-lt\"/><a:buAutoNum type=\"arabicPeriod\" startAt=\"10\"/>", R("Wide number label")) + PP(" marL=\"228600\" indent=\"-228600\"", "<a:buFont typeface=\"+mj-lt\"/><a:buAutoNum type=\"romanUcPeriod\" startAt=\"18\"/>", R("Wider roman label")), fill: Solid("F2F2F2")) +
                  Box(5, 20, 260, 240, 110, PP(" rtl=\"1\" algn=\"r\" marL=\"457200\" marR=\"91440\"", string.Empty, R("Right to left with margins")) + PP(" algn=\"l\" marL=\"457200\" marR=\"91440\"", string.Empty, R("Left to right with the same margins")), fill: Solid("F2F2F2")) +
                  Box(6, 280, 260, 260, 110, PP(string.Empty, "<a:tabLst><a:tab pos=\"1270000\" algn=\"r\"/><a:tab pos=\"2540000\" algn=\"ctr\"/></a:tabLst>", R("Right\ttab\tcentre")), fill: Solid("F2F2F2"))),
            ],
        });

        // A picture, a chart and a SmartArt diagram with its saved drawing, each in its own graphic frame or shape.
        byte[] red = new byte[4 * 4 * 4];
        for (int i = 0; i < red.Length; i += 4)
        {
            red[i] = 255;
            red[i + 3] = 255;
        }

        const string dgm = "http://schemas.openxmlformats.org/drawingml/2006/diagram";
        const string dsp = "http://schemas.microsoft.com/office/drawing/2008/diagram";
        string dataPart = $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><dgm:dataModel xmlns:dgm=\"{dgm}\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><dgm:ptLst/><dgm:extLst><a:ext uri=\"http://schemas.microsoft.com/office/drawing/2008/diagram\"><dsp:dataModelExt xmlns:dsp=\"{dsp}\" relId=\"rId21\" minVer=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\"/></a:ext></dgm:extLst></dgm:dataModel>";
        string drawingPart = $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><dsp:drawing xmlns:dgm=\"{dgm}\" xmlns:dsp=\"{dsp}\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><dsp:spTree><dsp:nvGrpSpPr><dsp:cNvPr id=\"0\" name=\"\"/><dsp:cNvGrpSpPr/></dsp:nvGrpSpPr><dsp:grpSpPr/>" +
            $"<dsp:sp modelId=\"{{00000000-0000-0000-0000-000000000001}}\"><dsp:nvSpPr><dsp:cNvPr id=\"0\" name=\"\"/><dsp:cNvSpPr/></dsp:nvSpPr><dsp:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"{E(180)}\" cy=\"{E(90)}\"/></a:xfrm><a:prstGeom prst=\"roundRect\"><a:avLst/></a:prstGeom><a:solidFill><a:srgbClr val=\"4472C4\"/></a:solidFill></dsp:spPr><dsp:txBody><a:bodyPr anchor=\"ctr\"/><a:lstStyle/><a:p><a:pPr algn=\"ctr\"/><a:r><a:rPr lang=\"en-US\" sz=\"2000\"><a:solidFill><a:srgbClr val=\"FFFFFF\"/></a:solidFill></a:rPr><a:t>Cached node</a:t></a:r></a:p></dsp:txBody></dsp:sp></dsp:spTree></dsp:drawing>";
        string pic = $"<p:pic><p:nvPicPr><p:cNvPr id=\"20\" name=\"Picture\" descr=\"A red square\"/><p:cNvPicPr/><p:nvPr/></p:nvPicPr><p:blipFill><a:blip r:embed=\"rId30\"/><a:stretch><a:fillRect/></a:stretch></p:blipFill><p:spPr><a:xfrm><a:off x=\"{E(40)}\" y=\"{E(40)}\"/><a:ext cx=\"{E(100)}\" cy=\"{E(100)}\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></p:spPr></p:pic>";
        string chart = $"<p:graphicFrame><p:nvGraphicFramePr><p:cNvPr id=\"21\" name=\"Chart\" descr=\"A line chart\"/><p:cNvGraphicFramePr/><p:nvPr/></p:nvGraphicFramePr><p:xfrm><a:off x=\"{E(200)}\" y=\"{E(40)}\"/><a:ext cx=\"{E(460)}\" cy=\"{E(240)}\"/></p:xfrm><a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/chart\"><c:chart xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\" r:id=\"rId31\"/></a:graphicData></a:graphic></p:graphicFrame>";
        string diagram = $"<p:graphicFrame><p:nvGraphicFramePr><p:cNvPr id=\"22\" name=\"Diagram\" descr=\"A diagram\"/><p:cNvGraphicFramePr/><p:nvPr/></p:nvGraphicFramePr><p:xfrm><a:off x=\"{E(100)}\" y=\"{E(330)}\"/><a:ext cx=\"{E(180)}\" cy=\"{E(90)}\"/></p:xfrm><a:graphic><a:graphicData uri=\"{dgm}\"><dgm:relIds xmlns:dgm=\"{dgm}\" r:dm=\"rId20\" r:lo=\"rId20\" r:qs=\"rId20\" r:cs=\"rId20\"/></a:graphicData></a:graphic></p:graphicFrame>";
        var objects = new SlideSpec
        {
            Shapes = pic + chart + diagram,
            Relationships =
            [
                ("rId30", "image", "../media/image1.png", false),
                ("rId31", "chart", "../charts/chart1.xml", false),
                ("rId20", "diagramData", "../diagrams/data1.xml", false),
                ("rId21", "http://schemas.microsoft.com/office/2007/relationships/diagramDrawing", "../diagrams/drawing1.xml", false),
            ],
        };
        var objectDeck = new Deck { Slides = [objects] };
        objectDeck.Media["ppt/media/image1.png"] = new OneRender.Image.RgbaImage(4, 4, red).ToPng();
        objectDeck.Media["ppt/charts/chart1.xml"] = Encoding.UTF8.GetBytes(ChartSamples.All().First(s => s.Name == "line").Xml);
        objectDeck.Media["ppt/diagrams/data1.xml"] = Encoding.UTF8.GetBytes(dataPart);
        objectDeck.Media["ppt/diagrams/drawing1.xml"] = Encoding.UTF8.GetBytes(drawingPart);
        Add("pictures-charts-diagrams", objectDeck);

        return cases;
    }
}
