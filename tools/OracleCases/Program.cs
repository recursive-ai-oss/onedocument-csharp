using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RecursiveAI.OneOffice;
using RecursiveAI.OneOffice.Documents;
using RecursiveAI.OneOffice.Documents.Comments;
using RecursiveAI.OneOffice.Documents.ContentControls;
using RecursiveAI.OneOffice.Documents.Fields;
using RecursiveAI.OneOffice.Documents.Notes;
using RecursiveAI.OneOffice.Documents.Revisions;
using RecursiveAI.OneOffice.Markup;
using RecursiveAI.OneOffice.Packaging;

namespace RecursiveAI.OneOffice.Tools.OracleCases;

/// <summary>
/// Writes documents that OneOffice itself created and edited, for a desktop application to open and judge ("does it ask to repair
/// the file?"). <c>OracleCases DIR</c> writes one small file per edit kind into DIR: table edits, sections and headers, comments,
/// notes, field results, authored and resolved revisions, content-control values and the output formats. Every input is generated
/// here from nothing; no downloaded file is read.
/// </summary>
internal static class Program
{
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string W14 = "http://schemas.microsoft.com/office/word/2010/wordml";
    private static readonly DateTimeOffset s_date = new(2024, 3, 1, 10, 0, 0, TimeSpan.Zero);
    private static string s_dir = string.Empty;

    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--authoring")
        {
            return AuthoringCases.Write(args[1]);
        }

        if (args.Length == 3 && args[0] == "--resolved")
        {
            return AuthoringCases.WriteResolved(args[1], args[2]);
        }

        if (args.Length == 3 && args[0] == "--check-tables")
        {
            return AuthoringCases.CheckTableProperties(args[1], args[2]);
        }

        if (args.Length == 2 && args[0] == "--check-probe")
        {
            return AuthoringCases.CheckProbe(args[1]);
        }

        if (args.Length == 3 && args[0] == "--check")
        {
            return AuthoringCases.Check(args[1], args[2]);
        }

        if (args.Length != 1)
        {
            Console.Error.WriteLine("usage: OracleCases DIR | --authoring DIR | --check DIR RESULTS");
            return 2;
        }

        s_dir = args[0];
        Directory.CreateDirectory(s_dir);
        var cases = new List<(string Name, Action Run)>
        {
            ("tbl-set-text", () => Table(t => t.SetCellText(1, 1, "changed"), merged: false)),
            ("tbl-insert-row", () => Table(t => t.InsertRow(1), merged: false)),
            ("tbl-delete-row", () => Table(t => t.DeleteRow(1), merged: false)),
            ("tbl-insert-col", () => Table(t => t.InsertColumn(1), merged: false)),
            ("tbl-delete-col", () => Table(t => t.DeleteColumn(1), merged: false)),
            ("tbl-merged-set-text", () => Table(t => t.SetCellText(0, 0, "changed"), merged: true)),
            ("tbl-merged-insert-row", () => Table(t => t.InsertRow(2), merged: true)),
            ("tbl-merged-delete-row", () => Table(t => t.DeleteRow(1), merged: true)),
            ("tbl-merged-insert-col", () => Table(t => t.InsertColumn(1), merged: true)),
            ("tbl-merged-delete-col", () => Table(t => t.DeleteColumn(0), merged: true)),
            ("sect-headers", SectionsAndHeaders),
            ("sect-even-odd", EvenOdd),
            ("cmt-add", CommentAdd),
            ("cmt-reply", CommentReply),
            ("cmt-edit-existing", CommentEditExisting),
            ("cmt-root-without-w14", () => CommentRootWithoutW14(localDeclaration: false)),
            ("cmt-local-w14-edited", () => CommentRootWithoutW14(localDeclaration: true)),
            ("note-footnote", () => Notes(endnote: false)),
            ("note-endnote", () => Notes(endnote: true)),
            ("fld-results", FieldResults),
            ("rev-insert-text", () => Authored(a => a.InsertText(P(0), 6, "NEW "))),
            ("rev-delete-text", () => Authored(a => a.DeleteText(P(0), 6, 5))),
            ("rev-replace-text", () => Authored(a => a.ReplaceText(P(0), 0, 5, "ALPHA"))),
            ("rev-move-text", () => Authored(a => a.MoveText(P(0), 6, 5, P(2), 0))),
            ("rev-insert-paragraph", () => Authored(a => a.InsertParagraph(P(1), "inserted paragraph"))),
            ("rev-delete-paragraph", () => Authored(a => a.DeleteParagraph(P(1)))),
            ("rev-move-paragraph", () => Authored(a => a.MoveParagraph(P(0), P(2)))),
            ("rev-format-text", () => Authored(a => a.FormatText(P(0), 0, 5, "<w:b/><w:color w:val=\"FF0000\"/>"))),
            ("rev-paragraph-properties", () => Authored(a => a.ChangeParagraphProperties(P(0), "<w:jc w:val=\"center\"/>"))),
            ("rev-table-marks", RevisionTable),
            ("rev-everything-accepted", () => Resolved(all: true, accept: true)),
            ("rev-everything-rejected", () => Resolved(all: true, accept: false)),
            ("rev-first-accepted", () => Resolved(all: false, accept: true)),
            ("rev-first-rejected", () => Resolved(all: false, accept: false)),
            ("rev-move-one-end-accepted", () => MoveOneEnd(accept: true)),
            ("rev-move-one-end-rejected", () => MoveOneEnd(accept: false)),
            ("cc-values", ContentControlValues),
            ("tb-dml-insert", () => Boxed(drawing: true, (a, box) => a.InsertText(box.Paragraphs().First().Markup.Node, 5, " NEW"))),
            ("tb-dml-delete", () => Boxed(drawing: true, (a, box) => a.DeleteText(box.Paragraphs().First().Markup.Node, 0, 5))),
            ("tb-dml-replace-move", () => Boxed(drawing: true, (a, box) => { a.ReplaceText(box.Paragraphs().First().Markup.Node, 0, 5, "ALPHA"); a.InsertParagraph(box.Paragraphs().Last().Markup.Node, "added in box"); })),
            ("tb-dml-settext", () => Boxed(drawing: true, (_, box) => box.Paragraphs().First().SetText("set by OneOffice"))),
            ("tb-dml-accept", () => Boxed(drawing: true, (a, box) => a.InsertText(box.Paragraphs().First().Markup.Node, 5, " NEW"), accept: true)),
            ("tb-vml-insert", () => Boxed(drawing: false, (a, box) => a.InsertText(box.Paragraphs().First().Markup.Node, 5, " NEW"))),
            ("tb-vml-delete-paragraph", () => Boxed(drawing: false, (a, box) => a.DeleteParagraph(box.Paragraphs().Last().Markup.Node))),
            ("tb-vml-settext", () => Boxed(drawing: false, (_, box) => box.Paragraphs().First().SetText("set by OneOffice"))),
            ("tb-hub-dml", () => Boxed(drawing: true, HubEdits)),
            ("tb-hub-vml", () => Boxed(drawing: false, HubEdits)),
            ("props-core", PropertiesCore),
            ("props-custom", PropertiesCustom),
            ("props-created-all", PropertiesCreatedFromNothing),
            ("default-typed-main", DefaultTypedMain),
            ("create-docx", () => Created(OfficeFormat.Docx, ".docx")),
            ("create-dotx", () => Created(OfficeFormat.Dotx, ".dotx")),
            ("create-docm", () => Created(OfficeFormat.Docm, ".docm")),
            ("create-flat-opc", () => Created(OfficeFormat.FlatOpcDocument, ".xml")),
        };
        int failed = 0;
        foreach ((string name, Action run) in cases)
        {
            CurrentName = name;
            try
            {
                run();
                Console.WriteLine("ok   " + name);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                failed++;
                Console.WriteLine("FAIL " + name + ": " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        return failed == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------ helpers

    [ThreadStatic]
    private static Document? t_current;

    private static MarkupNode P(int index) => t_current!.Body.Paragraphs().ElementAt(index).Markup.Node;

    private static void Save(Document doc, string name, OfficeFormat format = OfficeFormat.Docx, string extension = ".docx")
    {
        doc.CommitChanges();
        doc.SaveAs(Path.Combine(s_dir, name + extension), format);
    }

    // The case being written; the loop sets it so the helpers stay small.
    private static string CurrentName = string.Empty;

    private static Document Base(params string[] paragraphs)
    {
        Document doc = Document.Create();
        t_current = doc;
        if (paragraphs.Length > 0)
        {
            doc.Body.Paragraphs().First().SetText(paragraphs[0]);
            for (int i = 1; i < paragraphs.Length; i++)
            {
                doc.Body.AddParagraph(paragraphs[i]);
            }
        }

        return doc;
    }

    private static void Raw(Document doc, string xml) =>
        doc.Body.SectionProperties!.Value.Node.InsertXmlBefore(Encoding.UTF8.GetBytes(xml));

    private static string Cell(string text, string props = "") =>
        "<w:tc><w:tcPr><w:tcW w:w=\"2400\" w:type=\"dxa\"/>" + props + "</w:tcPr><w:p><w:r><w:t>" + text + "</w:t></w:r></w:p></w:tc>";

    private static string TableXml(bool merged)
    {
        var sb = new StringBuilder("<w:tbl><w:tblPr><w:tblW w:w=\"0\" w:type=\"auto\"/><w:tblBorders>");
        foreach (string side in new[] { "top", "left", "bottom", "right", "insideH", "insideV" })
        {
            sb.Append("<w:").Append(side).Append(" w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"auto\"/>");
        }

        sb.Append("</w:tblBorders></w:tblPr><w:tblGrid><w:gridCol w:w=\"2400\"/><w:gridCol w:w=\"2400\"/><w:gridCol w:w=\"2400\"/></w:tblGrid>");
        if (merged)
        {
            sb.Append("<w:tr>" + Cell("a1", "<w:vMerge w:val=\"restart\"/>") + Cell("b1", "") + Cell("c1") + "</w:tr>");
            sb.Append("<w:tr>" + Cell("", "<w:vMerge/>") + Cell("b2", "<w:gridSpan w:val=\"2\"/>").Replace("w:w=\"2400\"", "w:w=\"4800\"", StringComparison.Ordinal) + "</w:tr>");
            sb.Append("<w:tr>" + Cell("a3") + Cell("b3") + Cell("c3") + "</w:tr>");
        }
        else
        {
            for (int r = 1; r <= 3; r++)
            {
                sb.Append("<w:tr>" + Cell("a" + r) + Cell("b" + r) + Cell("c" + r) + "</w:tr>");
            }
        }

        return sb.Append("</w:tbl><w:p/>").ToString();
    }

    private static Table FirstTable(Document doc) => doc.Tables.First();

    // ------------------------------------------------------------------ cases

    private static void Table(Action<Table> edit, bool merged)
    {
        using Document doc = Base("before the table");
        Raw(doc, TableXml(merged));
        edit(FirstTable(doc));
        Save(doc, Current());
    }

    private static string Current() => CurrentName;

    private static void SectionsAndHeaders()
    {
        using Document doc = Base("section one");
        Raw(doc, "<w:p><w:pPr><w:sectPr><w:type w:val=\"nextPage\"/><w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/></w:sectPr></w:pPr><w:r><w:t>end of section one</w:t></w:r></w:p><w:p><w:r><w:t>section two</w:t></w:r></w:p>");
        IReadOnlyList<Section> sections = doc.Sections;
        HeaderFooter header = sections[0].AddOwn(HeaderFooterKind.Header, HeaderFooterType.Default);
        header.Paragraphs().First().SetText("header one");
        HeaderFooter first = sections[0].AddOwn(HeaderFooterKind.Header, HeaderFooterType.First);
        first.Paragraphs().First().SetText("first page header");
        sections[0].SetTitlePage(true);
        HeaderFooter footer = sections[1].AddOwn(HeaderFooterKind.Footer, HeaderFooterType.Default);
        footer.Paragraphs().First().SetText("footer two");
        sections[1].SetPageNumbering(new PageNumbering(5, null, null, null));
        sections[1].SetBreakType(SectionBreakType.Continuous);
        Save(doc, Current());
    }

    private static void EvenOdd()
    {
        using Document doc = Base("page one");
        Raw(doc, "<w:p><w:r><w:br w:type=\"page\"/></w:r></w:p><w:p><w:r><w:t>page two</w:t></w:r></w:p>");
        doc.EvenAndOddHeaders = true;
        Section s = doc.Sections[0];
        s.AddOwn(HeaderFooterKind.Header, HeaderFooterType.Default).Paragraphs().First().SetText("odd header");
        s.AddOwn(HeaderFooterKind.Header, HeaderFooterType.Even).Paragraphs().First().SetText("even header");
        Save(doc, Current());
    }

    private static void CommentAdd()
    {
        using Document doc = Base("comment on this text");
        Run run = doc.Body.Paragraphs().First().Runs().First();
        doc.Comments.Add(run, run, "Ann", "First comment\nsecond line", "A", s_date);
        Save(doc, Current());
    }

    private static void CommentReply()
    {
        using Document doc = Base("comment on this text");
        Run run = doc.Body.Paragraphs().First().Runs().First();
        Comment c = doc.Comments.Add(run, run, "Ann", "Question?", "A", s_date);
        Comment r = doc.Comments.Reply(c, "Bob", "Answer.", "B", s_date.AddHours(1));
        doc.Comments.Reply(c, "Cy", "Another.", "C", s_date.AddHours(2));
        c.IsDone = true;
        r.SetText("Answer, edited.");
        Save(doc, Current());
    }

    private static void CommentEditExisting()
    {
        string tmp = Path.Combine(s_dir, ".cmt-first.docx");
        using (Document first = Base("comment on this text"))
        {
            Run run = first.Body.Paragraphs().First().Runs().First();
            first.Comments.Add(run, run, "Ann", "Original", "A", s_date);
            first.CommitChanges();
            first.SaveAs(tmp, OfficeFormat.Docx);
        }

        using Document doc = Document.Open(tmp);
        Comment c = doc.Comments.Find(0)!;
        c.SetText("Edited");
        doc.Comments.Reply(c, "Bob", "Reply to edited", "B", s_date.AddHours(1));
        Save(doc, Current());
        File.Delete(tmp);
    }

    // A comments part whose root declares only the main namespace, the way many writers produce it.
    private static void CommentRootWithoutW14(bool localDeclaration)
    {
        var context = new OpenContext();
        OpcPackage package = OpcPackage.Create(context.Limits);
        package.SetRelationships(null, [new Relationship("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument", "word/document.xml")]);
        package.AddPart("/word/document.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml", Encoding.UTF8.GetBytes(
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<w:document xmlns:w=\"" + W + "\"><w:body><w:p><w:commentRangeStart w:id=\"0\"/><w:r><w:t>text</w:t></w:r><w:commentRangeEnd w:id=\"0\"/><w:r><w:commentReference w:id=\"0\"/></w:r></w:p>" +
            "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/></w:sectPr></w:body></w:document>"));
        package.AddPart("/word/comments.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.comments+xml", Encoding.UTF8.GetBytes(
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<w:comments xmlns:w=\"" + W + "\"" + (localDeclaration ? " xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" mc:Ignorable=\"w14\"" : string.Empty) + "><w:comment w:id=\"0\" w:author=\"Ann\" w:initials=\"A\"><w:p" +
            (localDeclaration ? " xmlns:w14=\"" + W14 + "\" w14:paraId=\"0A0A0A01\"" : string.Empty) + "><w:r><w:t>old</w:t></w:r></w:p></w:comment></w:comments>"));
        package.SetRelationships("/word/document.xml", [new Relationship("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments", "comments.xml")]);
        using Document doc = Document.FromPackage(package, context, ownsPackage: true);
        t_current = doc;
        Comment c = doc.Comments.Find(0)!;
        c.SetText("edited");
        Run run = doc.Body.Paragraphs().First().Runs().First();
        Comment added = doc.Comments.Add(run, run, "Bob", "added", "B", s_date);
        doc.Comments.Reply(added, "Cy", "reply", "C", s_date);
        Save(doc, Current());
    }

    private static void Notes(bool endnote)
    {
        using Document doc = Base("first paragraph with a note", "second paragraph with another");
        NoteCollection notes = endnote ? doc.Endnotes : doc.Footnotes;
        Note a = notes.Add("The first note.");
        a.AppendReference(doc.Body.Paragraphs().ElementAt(0));
        Note b = notes.Add("The second note,\nwith two lines.");
        b.AppendReference(doc.Body.Paragraphs().ElementAt(1));
        a.SetText("The first note, edited.");
        Save(doc, Current());
    }

    private static void FieldResults()
    {
        using Document doc = Base("fields");
        Raw(doc,
            "<w:p><w:r><w:t xml:space=\"preserve\">Page </w:t></w:r><w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> PAGE </w:instrText></w:r>" +
            "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>1</w:t></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r></w:p>" +
            "<w:p><w:fldSimple w:instr=\" DATE \\@ &quot;yyyy&quot; \"><w:r><w:t>2000</w:t></w:r></w:fldSimple></w:p>" +
            "<w:p><w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText> NUMPAGES </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>9</w:t></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r></w:p>");
        foreach (Field f in doc.Fields())
        {
            f.SetResult(f.Type switch { "PAGE" => "7", "DATE" => "2024", _ => "12" });
        }

        doc.Fields().First(f => f.Type == "NUMPAGES").SetInstruction(" NUMWORDS ");
        Save(doc, Current());
    }

    private static Document Revisable()
    {
        Document doc = Base("alpha bravo charlie", "delta echo foxtrot", "golf hotel india");
        return doc;
    }

    private static void Authored(Action<RevisionAuthor> edit)
    {
        using Document doc = Revisable();
        edit(doc.Revisions.CreateAuthor("Reviewer", s_date));
        Save(doc, Current());
    }

    private static void RevisionTable()
    {
        using Document doc = Base("table with revisions");
        Raw(doc, TableXml(merged: false));
        RevisionAuthor a = doc.Revisions.CreateAuthor("Reviewer", s_date);
        Table t = FirstTable(doc);
        TableRow added = t.InsertRow(3);
        a.MarkRowInserted(added.Markup.Node);
        a.MarkRowDeleted(t.Rows().ElementAt(0).Markup.Node);
        a.ChangeTableProperties(t.Markup.Node, "<w:tblW w:w=\"0\" w:type=\"auto\"/><w:jc w:val=\"center\"/>");
        a.MergeCells(t.Rows().ElementAt(1).Cells().ElementAt(2).Container.Node, "restart", null);
        Save(doc, Current());
    }

    private static void Everything(Document doc)
    {
        RevisionAuthor a = doc.Revisions.CreateAuthor("Reviewer", s_date);
        a.InsertText(P(0), 6, "NEW ");
        a.DeleteText(P(1), 6, 5);
        a.FormatText(P(2), 0, 4, "<w:b/>");
        a.InsertParagraph(P(2), "tail paragraph");
        a.ChangeParagraphProperties(P(0), "<w:jc w:val=\"center\"/>");
    }

    private static void Resolved(bool all, bool accept)
    {
        using Document doc = Revisable();
        Everything(doc);
        RevisionSet set = doc.Revisions;
        if (all)
        {
            _ = accept ? set.AcceptAll() : set.RejectAll();
        }
        else
        {
            Revision first = set.Revisions[0];
            _ = accept ? set.Accept(first) : set.Reject(first);
        }

        Save(doc, Current());
    }

    private static void MoveOneEnd(bool accept)
    {
        using Document doc = Revisable();
        doc.Revisions.CreateAuthor("Reviewer", s_date).MoveText(P(0), 6, 5, P(2), 0);
        RevisionSet set = doc.Revisions;
        Revision end = set.Revisions.First(r => r.Kind == RevisionKind.MoveTo);
        _ = accept ? set.Accept(end) : set.Reject(end);
        Save(doc, Current());
    }

    private static void ContentControlValues()
    {
        using Document doc = Base("controls");
        Raw(doc,
            "<w:p><w:sdt><w:sdtPr><w:alias w:val=\"Name\"/><w:tag w:val=\"name\"/><w:id w:val=\"101\"/><w:text/></w:sdtPr><w:sdtContent><w:r><w:t>placeholder</w:t></w:r></w:sdtContent></w:sdt></w:p>" +
            "<w:p><w:sdt><w:sdtPr><w:id w:val=\"102\"/><w14:checkbox xmlns:w14=\"" + W14 + "\"><w14:checked w14:val=\"0\"/><w14:checkedState w14:val=\"2612\" w14:font=\"MS Gothic\"/><w14:uncheckedState w14:val=\"2610\" w14:font=\"MS Gothic\"/></w14:checkbox></w:sdtPr><w:sdtContent><w:r><w:rPr><w:rFonts w:ascii=\"MS Gothic\" w:eastAsia=\"MS Gothic\" w:hAnsi=\"MS Gothic\"/></w:rPr><w:t>\u2610</w:t></w:r></w:sdtContent></w:sdt></w:p>" +
            "<w:p><w:sdt><w:sdtPr><w:id w:val=\"103\"/><w:dropDownList><w:listItem w:displayText=\"One\" w:value=\"1\"/><w:listItem w:displayText=\"Two\" w:value=\"2\"/></w:dropDownList></w:sdtPr><w:sdtContent><w:r><w:t>One</w:t></w:r></w:sdtContent></w:sdt></w:p>" +
            "<w:p><w:sdt><w:sdtPr><w:id w:val=\"104\"/><w:date><w:dateFormat w:val=\"yyyy-MM-dd\"/><w:lid w:val=\"en-US\"/><w:storeMappedDataAs w:val=\"dateTime\"/><w:calendar w:val=\"gregorian\"/></w:date></w:sdtPr><w:sdtContent><w:r><w:t>click</w:t></w:r></w:sdtContent></w:sdt></w:p>" +
            "<w:sdt><w:sdtPr><w:id w:val=\"105\"/><w:richText/></w:sdtPr><w:sdtContent><w:p><w:r><w:t>block rich</w:t></w:r></w:p></w:sdtContent></w:sdt>");
        foreach (ContentControl c in doc.ContentControls().ToList())
        {
            switch (c.Type)
            {
                case ContentControlType.PlainText:
                case ContentControlType.RichText:
                    c.SetText("set by OneOffice");
                    break;
                case ContentControlType.Checkbox:
                    c.SetChecked(true);
                    break;
                case ContentControlType.DropDownList:
                    c.SelectItem(1);
                    break;
                default:
                    break;
            }
        }

        Save(doc, Current());
    }

    // ------------------------------------------------------------------ text boxes, properties, content types

    private const string BoxNamespaces = "xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
        "xmlns:wps=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\" xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" xmlns:v=\"urn:schemas-microsoft-com:vml\"";

    private static string BoxContent() =>
        "<w:txbxContent><w:p><w:r><w:t>Inside the box</w:t></w:r></w:p><w:p><w:r><w:t>Second line</w:t></w:r></w:p></w:txbxContent>";

    internal static string BoxXml(bool drawing) => drawing
        ? "<w:p " + BoxNamespaces + "><w:r><w:t>Anchor paragraph</w:t></w:r><w:r><mc:AlternateContent><mc:Choice Requires=\"wps\"><w:drawing>" +
          "<wp:anchor distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\" simplePos=\"0\" relativeHeight=\"1\" behindDoc=\"0\" locked=\"0\" layoutInCell=\"1\" allowOverlap=\"1\">" +
          "<wp:simplePos x=\"0\" y=\"0\"/><wp:positionH relativeFrom=\"column\"><wp:posOffset>3000000</wp:posOffset></wp:positionH><wp:positionV relativeFrom=\"paragraph\"><wp:posOffset>0</wp:posOffset></wp:positionV>" +
          "<wp:extent cx=\"2000000\" cy=\"1000000\"/><wp:wrapSquare wrapText=\"bothSides\"/><wp:docPr id=\"1\" name=\"Box 1\"/><wp:cNvGraphicFramePr/>" +
          "<a:graphic><a:graphicData uri=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\"><wps:wsp><wps:cNvSpPr txBox=\"1\"/>" +
          "<wps:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"2000000\" cy=\"1000000\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></wps:spPr>" +
          "<wps:txbx>" + BoxContent() + "</wps:txbx><wps:bodyPr/></wps:wsp></a:graphicData></a:graphic></wp:anchor></w:drawing></mc:Choice>" +
          "<mc:Fallback><w:pict><v:shape id=\"fb1\" type=\"#_x0000_t202\" style=\"position:absolute;margin-left:236pt;margin-top:0;width:157pt;height:79pt;z-index:1\"><v:textbox>" +
          BoxContent() + "</v:textbox></v:shape></w:pict></mc:Fallback></mc:AlternateContent></w:r></w:p>"
        : "<w:p " + BoxNamespaces + "><w:r><w:t>Anchor paragraph</w:t></w:r><w:r><w:pict><v:shape id=\"vb1\" type=\"#_x0000_t202\" style=\"position:absolute;margin-left:236pt;margin-top:0;width:157pt;height:79pt;z-index:1\"><v:textbox>" +
          BoxContent() + "</v:textbox></v:shape></w:pict></w:r></w:p>";

    private static void Boxed(bool drawing, Action<RevisionAuthor, TextBox> edit, bool accept = false)
    {
        using Document doc = Base("before");
        Raw(doc, BoxXml(drawing));
        TextBox box = doc.TextBoxes().Single();
        edit(doc.Revisions.CreateAuthor("Reviewer", s_date), box);
        if (accept)
        {
            doc.Revisions.AcceptAll();
        }

        Save(doc, Current());
    }

    // The hub-level authoring calls on the paragraphs of a text box.
    private static void HubEdits(RevisionAuthor a, TextBox box)
    {
        Paragraph first = box.Paragraphs().First();
        a.ReplaceText(first, 0, 6, "Within");
        a.FormatText(first, 7, 3, new RunPropertiesDelta { Bold = true, Color = "FF0000" });
        a.ChangeParagraphProperties(box.Paragraphs().Last(), new ParagraphPropertiesDelta { Alignment = ParagraphAlignment.Center });
        a.InsertParagraph(first, "inserted through the hub");
    }

    private static void PropertiesCore()
    {
        using Document doc = Base("properties");
        doc.Properties.Core.Title = "A title & more";
        doc.Properties.Core.Subject = "Subject";
        doc.Properties.Core.Creator = "Ann";
        doc.Properties.Core.Keywords = "one, two";
        doc.Properties.Core.Description = "Comments";
        doc.Properties.Core.LastModifiedBy = "Bob";
        doc.Properties.Core.Revision = "4";
        doc.Properties.Core.Category = "Cat";
        doc.Properties.Core.ContentStatus = "Draft";
        doc.Properties.Core.Language = "en-US";
        doc.Properties.Core.Created = s_date;
        doc.Properties.Core.Modified = s_date.AddDays(2);
        doc.Properties.Core.LastPrinted = s_date.AddDays(1);
        doc.Properties.Extended.Company = "Example Co";
        doc.Properties.Extended.Manager = "Cy";
        doc.Properties.Extended.Application = "Microsoft Office Word";
        doc.Properties.Extended.ApplicationVersion = "16.0000";
        doc.Properties.Extended.Pages = 1;
        doc.Properties.Extended.Words = 2;
        Save(doc, Current());
        using Document again = Document.Open(Path.Combine(s_dir, Current() + ".docx"));
        again.Properties.Core.Title = "Edited again";
        again.Properties.Core.Keywords = null;
        again.Properties.Extended.Company = null;
        Save(again, Current() + "-edited");
    }

    private static void PropertiesCustom()
    {
        using Document doc = Base("custom");
        doc.Properties.Custom.SetString("Project", "Apollo & Co");
        doc.Properties.Custom.SetInt32("Count", 7);
        doc.Properties.Custom.SetInt64("Big", 5_000_000_000);
        doc.Properties.Custom.SetDouble("Ratio", 0.1);
        doc.Properties.Custom.SetBoolean("Reviewed", true);
        doc.Properties.Custom.SetDateTime("Due", s_date);
        Save(doc, Current());
        using Document again = Document.Open(Path.Combine(s_dir, Current() + ".docx"));
        again.Properties.Custom.SetString("Project", "Gemini");
        again.Properties.Custom.Remove("Count");
        again.Properties.Custom.SetBoolean("Added", false);
        Save(again, Current() + "-edited");
    }

    private static void PropertiesCreatedFromNothing()
    {
        // A package with no docProps at all gets every part created on the first write.
        using Document doc = Base("bare");
        doc.Properties.Core.Title = "Bare";
        doc.Properties.Custom.SetString("K", "V");
        doc.Properties.Extended.Company = "Co";
        Save(doc, Current());
    }

    private static void DefaultTypedMain()
    {
        using Document doc = Base("default typed main part");
        doc.SaveAs(Path.Combine(s_dir, Current() + ".tmp"), OfficeFormat.Docx);
        string source = Path.Combine(s_dir, Current() + ".tmp");
        string target = Path.Combine(s_dir, Current() + ".docx");
        using (var inZip = System.IO.Compression.ZipFile.OpenRead(source))
        using (var outZip = System.IO.Compression.ZipFile.Open(target, System.IO.Compression.ZipArchiveMode.Create))
        {
            foreach (System.IO.Compression.ZipArchiveEntry e in inZip.Entries)
            {
                System.IO.Compression.ZipArchiveEntry n = outZip.CreateEntry(e.FullName);
                using Stream r = e.Open();
                using Stream w = n.Open();
                if (e.FullName == "[Content_Types].xml")
                {
                    string text = new StreamReader(r, Encoding.UTF8).ReadToEnd();
                    // The main part keeps its type through the xml default only; every other part keeps its override.
                    text = System.Text.RegularExpressions.Regex.Replace(text, "<Override PartName=\"/word/document.xml\"[^>]*/>", string.Empty);
                    text = System.Text.RegularExpressions.Regex.Replace(text, "<Default Extension=\"xml\"[^>]*/>",
                        "<Default Extension=\"xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>");
                    w.Write(Encoding.UTF8.GetBytes(text));
                }
                else
                {
                    r.CopyTo(w);
                }
            }
        }

        File.Delete(source);
        using Document back = Document.Open(target);
        if (!back.Paragraphs.Any())
        {
            throw new InvalidOperationException("The default-typed package did not open.");
        }
    }

    private static void Created(OfficeFormat format, string extension)
    {
        using Document doc = Base("created by OneOffice", "second paragraph");
        Raw(doc, TableXml(merged: false));
        FirstTable(doc).SetCellText(0, 0, "edited");
        Run run = doc.Body.Paragraphs().First().Runs().First();
        doc.Comments.Add(run, run, "Ann", "A comment", "A", s_date);
        doc.Footnotes.Add("A footnote.").AppendReference(doc.Body.Paragraphs().ElementAt(1));
        Save(doc, Current(), format, extension);
    }
}
