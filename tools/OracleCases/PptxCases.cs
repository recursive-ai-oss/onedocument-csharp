using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RecursiveAI.OneDocument;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Presentations;
using RecursiveAI.OneDocument.Schema;

namespace RecursiveAI.OneDocument.Tools.OracleCases;

/// <summary>
/// Presentations that OneDocument created and edited, for a desktop application to open and judge ("does it ask to repair the
/// file?"). <c>OracleCases --pptx DIR</c> writes one small file per creation or edit kind: each layout, text edits, slide add,
/// duplicate, delete and move (with sections, notes, a picture, comments and a table), hidden slides and the template, slide show and
/// Flat OPC outputs. Every input is generated here from nothing.
/// </summary>
internal static class PptxCases
{
    private const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private const string P = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private const string A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private const string P14 = "http://schemas.microsoft.com/office/powerpoint/2010/main";

    // A 1 x 1 pixel PNG.
    private static readonly byte[] s_png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    public static int Write(string directory)
    {
        Directory.CreateDirectory(directory);
        var cases = new List<(string Name, Func<Presentation> Build, OfficeFormat Format)>
        {
            ("pp-create-empty", () => Presentation.Create(), OfficeFormat.Pptx),
            ("pp-create-title", () => Layouts(0), OfficeFormat.Pptx),
            ("pp-create-content", () => Layouts(1), OfficeFormat.Pptx),
            ("pp-create-blank", () => Layouts(2), OfficeFormat.Pptx),
            ("pp-create-all", () => Layouts(0, 1, 2), OfficeFormat.Pptx),
            ("pp-text-edit", TextEdit, OfficeFormat.Pptx),
            ("pp-text-special", TextSpecial, OfficeFormat.Pptx),
            ("pp-add-first", () => { Presentation d = Filled(); d.AddSlide(d.Masters[0].Layouts[0], 0); return d; }, OfficeFormat.Pptx),
            ("pp-add-middle", () => { Presentation d = Filled(); d.AddSlide(d.Masters[0].Layouts[2], 1); return d; }, OfficeFormat.Pptx),
            ("pp-dup-plain", () => { Presentation d = Filled(); d.DuplicateSlide(d.Slides[1]); return d; }, OfficeFormat.Pptx),
            ("pp-dup-first", () => { Presentation d = Filled(); d.DuplicateSlide(d.Slides[0], 0); return d; }, OfficeFormat.Pptx),
            ("pp-dup-rich", () => { Presentation d = Rich(); d.DuplicateSlide(d.Slides[0]); return d; }, OfficeFormat.Pptx),
            ("pp-delete-middle", () => { Presentation d = Filled(); d.DeleteSlide(d.Slides[1]); return d; }, OfficeFormat.Pptx),
            ("pp-delete-rich", () => { Presentation d = Rich(); d.DuplicateSlide(d.Slides[0]); d.DeleteSlide(d.Slides[0]); return d; }, OfficeFormat.Pptx),
            ("pp-delete-all", () => { Presentation d = Filled(); foreach (Slide s in d.Slides) { d.DeleteSlide(s); } return d; }, OfficeFormat.Pptx),
            ("pp-move", () => { Presentation d = Filled(); d.MoveSlide(d.Slides[0], 2); return d; }, OfficeFormat.Pptx),
            ("pp-sections-dup", () => { Presentation d = Sectioned(); d.DuplicateSlide(d.Slides[1]); return d; }, OfficeFormat.Pptx),
            ("pp-sections-move", () => { Presentation d = Sectioned(); d.MoveSlide(d.Slides[0], 3); return d; }, OfficeFormat.Pptx),
            ("pp-sections-delete", () => { Presentation d = Sectioned(); d.DeleteSlide(d.Slides[2]); return d; }, OfficeFormat.Pptx),
            ("pp-sections-add", () => { Presentation d = Sectioned(); d.AddSlide(d.Masters[0].Layouts[2], 2); return d; }, OfficeFormat.Pptx),
            ("pp-notes-add", () => { Presentation d = WithNotesMaster(Filled()); d.Slides[0].SetNotesText("First line\nSecond line"); return d; }, OfficeFormat.Pptx),
            ("pp-notes-dup", () => { Presentation d = WithNotesMaster(Filled()); d.Slides[0].SetNotesText("Speaker notes"); d.DuplicateSlide(d.Slides[0]); return d; }, OfficeFormat.Pptx),
            ("pp-table-edit", TableEdit, OfficeFormat.Pptx),
            ("pp-hidden", () => { Presentation d = Filled(); d.Slides[1].IsHidden = true; return d; }, OfficeFormat.Pptx),
            ("pp-template", Filled, OfficeFormat.Potx),
            ("pp-slideshow", Filled, OfficeFormat.Ppsx),
            ("pp-flat-opc", Filled, OfficeFormat.FlatOpcPresentation),
        };
        foreach ((string name, Func<Presentation> build, OfficeFormat format) in cases)
        {
            using Presentation deck = build();
            string extension = format switch
            {
                OfficeFormat.Potx => ".potx",
                OfficeFormat.Ppsx => ".ppsx",
                OfficeFormat.FlatOpcPresentation => ".xml",
                _ => ".pptx",
            };
            deck.SaveAs(Path.Combine(directory, name + extension), format);
            Console.WriteLine(name + extension);
        }

        return 0;
    }

    private static Presentation Layouts(params int[] layouts)
    {
        Presentation deck = Presentation.Create();
        foreach (int i in layouts)
        {
            Slide s = deck.AddSlide(deck.Masters[0].Layouts[i]);
            foreach (Shape shape in s.Shapes)
            {
                shape.TextBody?.SetText("Layout " + i + " " + shape.Name);
            }
        }

        return deck;
    }

    // Three slides: title, content, content.
    private static Presentation Filled()
    {
        Presentation deck = Presentation.Create();
        Slide a = deck.AddSlide(deck.Masters[0].Layouts[0]);
        a.Shapes[0].TextBody!.SetText("Opening");
        a.Shapes[1].TextBody!.SetText("A subtitle");
        for (int i = 2; i <= 3; i++)
        {
            Slide s = deck.AddSlide(deck.Masters[0].Layouts[1]);
            s.Shapes[0].TextBody!.SetText("Slide " + i);
            s.Shapes[1].TextBody!.SetText("Point one\nPoint two\nPoint three");
            s.Shapes[1].TextBody!.Paragraphs[1].Level = 1;
        }

        return deck;
    }

    private static Presentation TextEdit()
    {
        Presentation deck = Filled();
        deck.Slides[1].Shapes[0].TextBody!.SetText("A much longer replacement title for the second slide");
        deck.Slides[1].Shapes[1].TextBody!.SetText("Only one line now");
        deck.Slides[2].Shapes[1].TextBody!.SetText("1\n2\n3\n4\n5\n6");
        deck.Slides[2].Shapes[1].TextBody!.AddParagraph("appended");
        deck.Slides[0].Shapes[1].TextBody!.Paragraphs[0].SetText("line one\vline two after a break");
        return deck;
    }

    private static Presentation TextSpecial()
    {
        Presentation deck = Presentation.Create();
        Slide s = deck.AddSlide(deck.Masters[0].Layouts[1]);
        s.Shapes[0].TextBody!.SetText("a < b & c > d \"quoted\" {a} {p} caf\u00e9 \u65e5\u672c\u8a9e \U0001F600");
        s.Shapes[1].TextBody!.SetText("control \u0001 char\n  leading and trailing  \n\ttab");
        return deck;
    }

    // A slide with a picture, a legacy comment and a table, plus a second plain slide.
    private static Presentation Rich()
    {
        Presentation deck = Presentation.Create();
        Slide s = deck.AddSlide(deck.Masters[0].Layouts[1]);
        s.Shapes[0].TextBody!.SetText("Rich slide");
        s.Shapes[1].TextBody!.SetText("with a picture, a comment and a table");
        string image = Attach(deck, s.PartName, "image", "/ppt/media/image1.png", "image/png", s_png);
        s.ShapeTreeMarkup.Node.AppendXml(Encoding.UTF8.GetBytes(
            "<p:pic xmlns:p=\"" + P + "\" xmlns:a=\"" + A + "\" xmlns:r=\"" + Rel.TrimEnd('/') + "\"><p:nvPicPr><p:cNvPr id=\"10\" name=\"Picture 10\" descr=\"a pixel\"/><p:cNvPicPr><a:picLocks noChangeAspect=\"1\"/></p:cNvPicPr><p:nvPr/></p:nvPicPr>" +
            "<p:blipFill><a:blip r:embed=\"" + image + "\"/><a:stretch><a:fillRect/></a:stretch></p:blipFill><p:spPr><a:xfrm><a:off x=\"8000000\" y=\"500000\"/><a:ext cx=\"1000000\" cy=\"1000000\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></p:spPr></p:pic>"));
        AddTable(s);
        Attach(deck, deck.MainPartName, "commentAuthors", "/ppt/commentAuthors.xml", "application/vnd.openxmlformats-officedocument.presentationml.commentAuthors+xml",
            Encoding.UTF8.GetBytes("<p:cmAuthorLst xmlns:p=\"" + P + "\"><p:cmAuthor id=\"0\" name=\"Reviewer\" initials=\"R\" lastIdx=\"1\" clrIdx=\"0\"/></p:cmAuthorLst>"));
        Attach(deck, s.PartName, "comments", "/ppt/comments/comment1.xml", "application/vnd.openxmlformats-officedocument.presentationml.comments+xml",
            Encoding.UTF8.GetBytes("<p:cmLst xmlns:p=\"" + P + "\"><p:cm authorId=\"0\" dt=\"2024-05-01T10:00:00.000\" idx=\"1\"><p:pos x=\"10\" y=\"10\"/><p:text>Please check</p:text></p:cm></p:cmLst>"));
        deck.AddSlide(deck.Masters[0].Layouts[2]);
        return deck;
    }

    private static void AddTable(Slide slide)
    {
        static string Cell(string t, string attrs = "") =>
            "<a:tc " + attrs + "><a:txBody><a:bodyPr/><a:lstStyle/><a:p>" + (t.Length == 0 ? "<a:endParaRPr lang=\"en-US\"/>" : "<a:r><a:rPr lang=\"en-US\"/><a:t>" + t + "</a:t></a:r>") + "</a:p></a:txBody><a:tcPr/></a:tc>";
        slide.ShapeTreeMarkup.Node.AppendXml(Encoding.UTF8.GetBytes(
            "<p:graphicFrame xmlns:p=\"" + P + "\" xmlns:a=\"" + A + "\"><p:nvGraphicFramePr><p:cNvPr id=\"11\" name=\"Table 11\"/><p:cNvGraphicFramePr><a:graphicFrameLocks noGrp=\"1\"/></p:cNvGraphicFramePr><p:nvPr/></p:nvGraphicFramePr>" +
            "<p:xfrm><a:off x=\"838200\" y=\"4500000\"/><a:ext cx=\"6096000\" cy=\"1200000\"/></p:xfrm><a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/table\"><a:tbl><a:tblPr firstRow=\"1\" bandRow=\"1\"/>" +
            "<a:tblGrid><a:gridCol w=\"2032000\"/><a:gridCol w=\"2032000\"/><a:gridCol w=\"2032000\"/></a:tblGrid>" +
            "<a:tr h=\"400000\">" + Cell("Header", "gridSpan=\"2\"") + Cell(string.Empty, "hMerge=\"1\"") + Cell("H3") + "</a:tr>" +
            "<a:tr h=\"400000\">" + Cell("Tall", "rowSpan=\"2\"") + Cell("B2") + Cell("B3") + "</a:tr>" +
            "<a:tr h=\"400000\">" + Cell(string.Empty, "vMerge=\"1\"") + Cell("C2") + Cell("C3") + "</a:tr>" +
            "</a:tbl></a:graphicData></a:graphic></p:graphicFrame>"));
    }

    private static Presentation TableEdit()
    {
        Presentation deck = Presentation.Create();
        Slide s = deck.AddSlide(deck.Masters[0].Layouts[2]);
        AddTable(s);
        Table table = s.Shapes[0].Table!;
        table[0, 0].SetText("Merged header, edited");
        table[2, 2].SetText("C3 edited");
        table[1, 1].SetText("two\nlines");
        return deck;
    }

    private static Presentation Sectioned()
    {
        Presentation deck = Presentation.Create();
        for (int i = 1; i <= 4; i++)
        {
            Slide s = deck.AddSlide(deck.Masters[0].Layouts[1]);
            s.Shapes[0].TextBody!.SetText("Slide " + i);
        }

        IReadOnlyList<Slide> slides = deck.Slides;
        string Ids(int a, int b) => "<p14:sldId id=\"" + slides[a].Id + "\"/><p14:sldId id=\"" + slides[b].Id + "\"/>";
        deck.Markup.Node.AppendXml(Encoding.UTF8.GetBytes(
            "<p:extLst xmlns:p14=\"" + P14 + "\"><p:ext uri=\"{521415D9-36F7-43E2-AB2F-B90AF26B5E84}\"><p14:sectionLst>" +
            "<p14:section name=\"Intro\" id=\"{A1B2C3D4-0000-4000-8000-000000000001}\"><p14:sldIdLst>" + Ids(0, 1) + "</p14:sldIdLst></p14:section>" +
            "<p14:section name=\"Body\" id=\"{A1B2C3D4-0000-4000-8000-000000000002}\"><p14:sldIdLst>" + Ids(2, 3) + "</p14:sldIdLst></p14:section>" +
            "</p14:sectionLst></p:ext></p:extLst>"));
        return deck;
    }

    // A notes master needs its own theme part, so PowerPoint finds one theme per master.
    private static Presentation WithNotesMaster(Presentation deck)
    {
        byte[] theme = deck.Package.FindPart("/ppt/theme/theme1.xml")!.GetBytes().ToArray();
        deck.Package.AddPart("/ppt/theme/theme2.xml", "application/vnd.openxmlformats-officedocument.theme+xml", theme);
        const string name = "/ppt/notesMasters/notesMaster1.xml";
        string xml = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<p:notesMaster xmlns:a=\"" + A + "\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" xmlns:p=\"" + P + "\"><p:cSld><p:bg><p:bgRef idx=\"1001\"><a:schemeClr val=\"bg1\"/></p:bgRef></p:bg><p:spTree>" +
            "<p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/><a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"0\" cy=\"0\"/></a:xfrm></p:grpSpPr>" +
            "<p:sp><p:nvSpPr><p:cNvPr id=\"2\" name=\"Slide Image Placeholder 1\"/><p:cNvSpPr><a:spLocks noGrp=\"1\" noRot=\"1\" noChangeAspect=\"1\"/></p:cNvSpPr><p:nvPr><p:ph type=\"sldImg\" idx=\"2\"/></p:nvPr></p:nvSpPr>" +
            "<p:spPr><a:xfrm><a:off x=\"685800\" y=\"1143000\"/><a:ext cx=\"5486400\" cy=\"3086100\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></p:spPr></p:sp>" +
            "<p:sp><p:nvSpPr><p:cNvPr id=\"3\" name=\"Notes Placeholder 2\"/><p:cNvSpPr><a:spLocks noGrp=\"1\"/></p:cNvSpPr><p:nvPr><p:ph type=\"body\" sz=\"quarter\" idx=\"3\"/></p:nvPr></p:nvSpPr>" +
            "<p:spPr><a:xfrm><a:off x=\"685800\" y=\"4400550\"/><a:ext cx=\"5486400\" cy=\"3600450\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></p:spPr>" +
            "<p:txBody><a:bodyPr vert=\"horz\" lIns=\"91440\" tIns=\"45720\" rIns=\"91440\" bIns=\"45720\" rtlCol=\"0\"/><a:lstStyle/><a:p><a:pPr lvl=\"0\"/><a:r><a:rPr lang=\"en-US\"/><a:t>Click to edit Master text styles</a:t></a:r></a:p></p:txBody></p:sp>" +
            "</p:spTree></p:cSld><p:clrMap bg1=\"lt1\" tx1=\"dk1\" bg2=\"lt2\" tx2=\"dk2\" accent1=\"accent1\" accent2=\"accent2\" accent3=\"accent3\" accent4=\"accent4\" accent5=\"accent5\" accent6=\"accent6\" hlink=\"hlink\" folHlink=\"folHlink\"/>" +
            "<p:notesStyle><a:lvl1pPr marL=\"0\" algn=\"l\" defTabSz=\"914400\" rtl=\"0\" eaLnBrk=\"1\" latinLnBrk=\"0\" hangingPunct=\"1\"><a:defRPr sz=\"1200\" kern=\"1200\"><a:solidFill><a:schemeClr val=\"tx1\"/></a:solidFill><a:latin typeface=\"+mn-lt\"/><a:ea typeface=\"+mn-ea\"/><a:cs typeface=\"+mn-cs\"/></a:defRPr></a:lvl1pPr></p:notesStyle></p:notesMaster>";
        deck.Package.AddPart(name, "application/vnd.openxmlformats-officedocument.presentationml.notesMaster+xml", Encoding.UTF8.GetBytes(xml));
        deck.Package.SetRelationships(name, [new Relationship("rId1", Rel + "theme", "../theme/theme2.xml")]);
        var rels = new List<Relationship>(deck.Package.GetRelationships(deck.MainPartName)) { new Relationship("rId90", Rel + "notesMaster", "notesMasters/notesMaster1.xml") };
        deck.Package.SetRelationships(deck.MainPartName, rels);
        // ECMA-376 Part 1 §19.2.1.17: notesMasterIdLst follows sldMasterIdLst.
        deck.Markup.FindChild(SchemaNamespace.Presentation, "sldMasterIdLst"u8)!.Value.Node.InsertXmlAfter(
            Encoding.UTF8.GetBytes("<p:notesMasterIdLst><p:notesMasterId r:id=\"rId90\"/></p:notesMasterIdLst>"));
        return deck;
    }

    private static string Attach(Presentation deck, string source, string type, string partName, string contentType, byte[] content)
    {
        if (deck.Package.FindPart(partName) is null)
        {
            deck.Package.AddPart(partName, contentType, content);
        }

        var rels = new List<Relationship>(deck.Package.GetRelationships(source));
        string id = "rId" + (rels.Count + 1);
        rels.Add(new Relationship(id, Rel + type, RelativeTarget(source, partName)));
        deck.Package.SetRelationships(source, rels);
        return id;
    }

    private static string RelativeTarget(string source, string target)
    {
        string[] from = source.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string[] to = target.Split('/', StringSplitOptions.RemoveEmptyEntries);
        int common = 0;
        while (common < from.Length - 1 && common < to.Length - 1 && string.Equals(from[common], to[common], StringComparison.OrdinalIgnoreCase))
        {
            common++;
        }

        return string.Concat(Enumerable.Repeat("../", from.Length - 1 - common)) + string.Join('/', to[common..]);
    }
}
