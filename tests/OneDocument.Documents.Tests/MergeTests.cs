using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.Operations;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Documents.Tests;

public class MergeTests
{
    private const string W = TestDocs.WNs;
    private const string R = TestDocs.RNs;
    private static readonly byte[] Png1 = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
    private static readonly byte[] Png2 = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 9, 9, 9];

    internal static string P(string text, string? style = null, string extra = "") =>
        "<w:p>" + (style is null && extra.Length == 0 ? string.Empty : "<w:pPr>" + (style is null ? string.Empty : $"<w:pStyle w:val=\"{style}\"/>") + extra + "</w:pPr>")
        + $"<w:r><w:t>{text}</w:t></w:r></w:p>";

    internal static string Style(string id, string name, string rpr, string? based = null) =>
        $"<w:style w:type=\"paragraph\" w:styleId=\"{id}\"><w:name w:val=\"{name}\"/>" + (based is null ? string.Empty : $"<w:basedOn w:val=\"{based}\"/>") + $"<w:rPr>{rpr}</w:rPr></w:style>";

    internal static string Abstract(int id, string fmt, string text) =>
        $"<w:abstractNum w:abstractNumId=\"{id}\"><w:nsid w:val=\"0000000{id}\"/><w:multiLevelType w:val=\"hybridMultilevel\"/><w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"{fmt}\"/><w:lvlText w:val=\"{text}\"/></w:lvl></w:abstractNum>";

    internal static void AddImage(InMemoryPackage p, string partName, string relId, byte[] bytes)
    {
        p.AddPart(partName, "image/png", bytes);
        var rels = new List<Relationship>(p.GetRelationships("/word/document.xml"))
        {
            new Relationship(relId, PartHelper.Rel + "image", partName["/word/".Length..]),
        };
        p.SetRelationships("/word/document.xml", rels);
    }

    internal static string Pic(string relId, int docPrId) =>
        $"<w:p><w:r><w:drawing><wp:inline xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><wp:extent cx=\"10\" cy=\"10\"/><wp:docPr id=\"{docPrId}\" name=\"Picture\"/>" +
        $"<a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><a:blip r:embed=\"{relId}\"/></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>";

    // A real zip-backed package with the parts of an in-memory one, so that it can be saved and opened again.
    internal static Document Doc(InMemoryPackage m)
    {
        OpcPackage p = OpcPackage.Create();
        foreach (IPackagePart part in m.Parts)
        {
            p.AddPart(part.Name, part.ContentType, part.GetBytes());
        }

        p.SetRelationships(null, m.GetRelationships(null));
        foreach (IPackagePart part in m.Parts)
        {
            if (m.GetRelationships(part.Name) is { Count: > 0 } rels)
            {
                p.SetRelationships(part.Name, rels);
            }
        }

        return Document.FromPackage(p);
    }

    internal static Document Reopen(Document d)
    {
        d.CommitChanges();
        var ms = new MemoryStream();
        d.SaveAs(ms, OfficeFormat.Docx);
        ms.Position = 0;
        return Document.Open(ms);
    }

    internal static string[] Texts(Document d) => [.. d.Body.Paragraphs().Select(p => p.Text)];

    [Fact]
    public void Append_AddsTheSourceBodyAfterTheDestinationsAndKeepsUntouchedPartsIdentical()
    {
        InMemoryPackage dp = TestDocs.Package(P("one"), "<w:style w:type=\"paragraph\" w:styleId=\"Normal\" w:default=\"1\"><w:name w:val=\"Normal\"/></w:style>");
        byte[] stylesBefore = dp.FindPart("/word/styles.xml")!.GetBytes().ToArray();
        using var dst = Doc(dp);
        using var src = Doc(TestDocs.Package(P("two") + P("three")));
        MergeReport report = dst.Append(src);
        Assert.Equal(["one", "two", "three"], Texts(dst));
        Assert.Equal(2, report.CountOf("blocks"));
        Assert.Empty(report.Problems);
        dst.CommitChanges();
        Assert.Equal(stylesBefore, dst.Package.FindPart("/word/styles.xml")!.GetBytes().ToArray());
        Assert.Equal(["one", "two", "three"], Texts(Reopen(dst)));
    }

    [Fact]
    public void Insert_PutsTheRangeBeforeTheBlock()
    {
        using var dst = Doc(TestDocs.Package(P("a") + P("d")));
        using var src = Doc(TestDocs.Package(P("b") + P("c") + P("x")));
        dst.Insert(1, src, new MergeOptions { SourceStart = 0, SourceCount = 2 });
        Assert.Equal(["a", "b", "c", "d"], Texts(dst));
    }

    [Fact]
    public void Insert_RangeOutsideTheBodyIsRefused()
    {
        using var dst = Doc(TestDocs.Package(P("a")));
        using var src = Doc(TestDocs.Package(P("b")));
        Assert.Throws<ArgumentOutOfRangeException>(() => dst.Insert(5, src));
        Assert.Throws<ArgumentOutOfRangeException>(() => dst.Insert(0, src, new MergeOptions { SourceStart = 9 }));
    }

    private static (Document Dst, Document Src) StyleConflictDocs()
    {
        string dstStyles = Style("Heading1", "heading 1", "<w:sz w:val=\"28\"/>") + Style("Same", "Same", "<w:b/>");
        string srcStyles = Style("Heading1", "heading 1", "<w:sz w:val=\"40\"/>") + Style("Same", "Same", "<w:b/>")
            + Style("Quote2", "Quote", "<w:i/>") + Style("Child", "Child", "<w:u w:val=\"single\"/>", based: "Quote2");
        var dst = Doc(TestDocs.Package(P("d", "Heading1"), dstStyles));
        var src = Doc(TestDocs.Package(P("s1", "Heading1") + P("s2", "Same") + P("s3", "Child"), srcStyles));
        return (dst, src);
    }

    [Fact]
    public void Styles_KeepDestination_TheSourceTakesTheDestinationsDefinitionAndNewStylesAreCopied()
    {
        (Document dst, Document src) = StyleConflictDocs();
        using (dst)
        using (src)
        {
            MergeReport r = dst.Append(src);
            MergeResolution c = Assert.Single(r.ResolutionsOf("merge.style-conflict"));
            Assert.Equal("heading 1", c.Item);
            Assert.Equal("kept destination", c.Resolution);
            Assert.Contains("w:val=\"28\"", dst.Styles.Find("Heading1")!.Markup.Node.OuterXml);
            Assert.NotNull(dst.Styles.Find("Child"));
            Assert.Equal("Quote2", dst.Styles.Find("Child")!.BasedOnId);
            Assert.Equal(2, r.CountOf("styles"));
            Assert.Equal(["Heading1", "Same", "Child"], dst.Body.Paragraphs().Skip(1).Select(p => p.StyleId).ToArray());
        }
    }

    [Fact]
    public void Styles_UseSource_ReplacesTheDestinationsDefinition()
    {
        (Document dst, Document src) = StyleConflictDocs();
        using (dst)
        using (src)
        {
            MergeReport r = dst.Append(src, new MergeOptions { Styles = StyleConflict.UseSource });
            Assert.Equal("used source", Assert.Single(r.ResolutionsOf("merge.style-conflict")).Resolution);
            Assert.Contains("w:val=\"40\"", dst.Styles.Find("Heading1")!.Markup.Node.OuterXml);
            Assert.Single(dst.Styles.Styles, s => s.Name == "heading 1");
        }
    }

    [Fact]
    public void Styles_KeepBoth_AddsTheSourceStyleUnderANewNameAndTheContentUsesIt()
    {
        (Document dst, Document src) = StyleConflictDocs();
        using (dst)
        using (src)
        {
            MergeReport r = dst.Append(src, new MergeOptions { Styles = StyleConflict.KeepBoth });
            Assert.Equal("kept both", Assert.Single(r.ResolutionsOf("merge.style-conflict")).Resolution);
            Assert.Contains("w:val=\"28\"", dst.Styles.Find("Heading1")!.Markup.Node.OuterXml);
            string? copiedId = dst.Body.Paragraphs().ElementAt(1).StyleId;
            Assert.NotEqual("Heading1", copiedId);
            Assert.Equal("heading 1 (2)", dst.Styles.Find(copiedId)!.Name);
            Assert.Contains("w:val=\"40\"", dst.Styles.Find(copiedId)!.Markup.Node.OuterXml);
        }
    }

    [Fact]
    public void Styles_SameNameDifferentId_IsMatchedByName()
    {
        using var dst = Doc(TestDocs.Package(P("d"), Style("Titel", "Title", "<w:b/>")));
        using var src = Doc(TestDocs.Package(P("s", "T1"), Style("T1", "Title", "<w:b/>")));
        MergeReport r = dst.Append(src);
        Assert.Equal("Titel", dst.Body.Paragraphs().Last().StyleId);
        Assert.Empty(r.Resolutions);
        Assert.Equal(0, r.CountOf("styles"));
    }

    private static string NumberedParagraph(string text, int numId) =>
        P(text, null, $"<w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"{numId}\"/></w:numPr>");

    [Fact]
    public void Numbering_EqualDefinitionContinuesTheDestinationsList_ADifferentOneIsCopied()
    {
        string dstNum = Abstract(0, "decimal", "%1.") + "<w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num>";
        string srcNum = Abstract(5, "decimal", "%1.") + Abstract(6, "upperRoman", "%1)")
            + "<w:num w:numId=\"3\"><w:abstractNumId w:val=\"5\"/></w:num><w:num w:numId=\"4\"><w:abstractNumId w:val=\"6\"/></w:num>";
        using var dst = Doc(TestDocs.Package(NumberedParagraph("d", 1), null, dstNum));
        using var src = Doc(TestDocs.Package(NumberedParagraph("s1", 3) + NumberedParagraph("s2", 4), null, srcNum));
        MergeReport r = dst.Append(src);
        Assert.Equal("list 3", Assert.Single(r.ResolutionsOf("merge.list-continued")).Item);
        long[] ids = [.. System.Text.RegularExpressions.Regex.Matches(dst.Body.Container.Node.OuterXml, "w:numId w:val=\"(\\d+)\"").Select(x => long.Parse(x.Groups[1].Value))];
        Assert.Equal(1, ids[0]);
        Assert.Equal(1, ids[1]);
        Assert.NotEqual(1, ids[2]);
        Assert.Equal(2, dst.Numbering.AbstractDefinitions.Count);
        Assert.Equal(2, dst.Numbering.Instances.Count);
        Assert.Equal(["1.", "2.", "I)"], dst.Numbering.LabelBody().Select(l => l.Text).ToArray());
    }

    [Fact]
    public void Numbering_Restart_StartsANewListFromTheFirstNumber()
    {
        string num = Abstract(0, "decimal", "%1.") + "<w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num>";
        using var dst = Doc(TestDocs.Package(NumberedParagraph("d1", 1) + NumberedParagraph("d2", 1), null, num));
        using var src = Doc(TestDocs.Package(NumberedParagraph("s1", 1), null, num));
        MergeReport r = dst.Append(src, new MergeOptions { Numbering = NumberingContinuation.Restart });
        Assert.Single(r.ResolutionsOf("merge.list-restarted"));
        var labels = dst.Numbering.LabelBody();
        Assert.Equal(["1.", "2.", "1."], labels.Select(l => l.Text).ToArray());
        Assert.Single(dst.Numbering.AbstractDefinitions);
    }

    [Fact]
    public void Bookmarks_CollidingNamesAreRenamedWithTheirReferences()
    {
        string dstBody = "<w:p><w:bookmarkStart w:id=\"0\" w:name=\"intro\"/><w:r><w:t>d</w:t></w:r><w:bookmarkEnd w:id=\"0\"/></w:p>";
        string srcBody = "<w:p><w:bookmarkStart w:id=\"0\" w:name=\"intro\"/><w:r><w:t>s</w:t></w:r><w:bookmarkEnd w:id=\"0\"/></w:p>"
            + "<w:p><w:hyperlink w:anchor=\"intro\"><w:r><w:t>go</w:t></w:r></w:hyperlink><w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> PAGEREF intro \\h </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>1</w:t></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r>"
            + "<w:fldSimple w:instr=\" REF intro \\h \"><w:r><w:t>s</w:t></w:r></w:fldSimple></w:p>";
        using var dst = Doc(TestDocs.Package(dstBody));
        using var src = Doc(TestDocs.Package(srcBody));
        MergeReport r = dst.Append(src);
        MergeResolution m = Assert.Single(r.ResolutionsOf("merge.bookmark-renamed"));
        Assert.Equal("renamed to intro_2", m.Resolution);
        string xml = dst.Body.Container.Node.OuterXml;
        Assert.Contains("w:name=\"intro_2\"", xml);
        Assert.Contains("w:anchor=\"intro_2\"", xml);
        Assert.Contains("PAGEREF intro_2 \\h", xml);
        Assert.Contains("REF intro_2 \\h", xml);
        string[] ids = [.. System.Text.RegularExpressions.Regex.Matches(xml, "bookmarkStart w:id=\"(\\d+)\"").Select(x => x.Groups[1].Value)];
        Assert.Equal(2, ids.Distinct().Count());
    }

    [Fact]
    public void Notes_AreCopiedWithNewIdsAndTheirText()
    {
        InMemoryPackage dp = TestDocs.Package("<w:p><w:r><w:t>d</w:t></w:r><w:r><w:footnoteReference w:id=\"1\"/></w:r></w:p>");
        PartHelper.Add(dp, "/word/footnotes.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.footnotes+xml", PartHelper.Rel + "footnotes",
            $"<w:footnotes xmlns:w=\"{W}\"><w:footnote w:type=\"separator\" w:id=\"-1\"><w:p><w:r><w:separator/></w:r></w:p></w:footnote><w:footnote w:type=\"continuationSeparator\" w:id=\"0\"><w:p><w:r><w:continuationSeparator/></w:r></w:p></w:footnote>" +
            "<w:footnote w:id=\"1\"><w:p><w:r><w:t>dest note</w:t></w:r></w:p></w:footnote></w:footnotes>");
        InMemoryPackage sp = TestDocs.Package("<w:p><w:r><w:t>s</w:t></w:r><w:r><w:footnoteReference w:id=\"1\"/></w:r></w:p>");
        PartHelper.Add(sp, "/word/footnotes.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.footnotes+xml", PartHelper.Rel + "footnotes",
            $"<w:footnotes xmlns:w=\"{W}\"><w:footnote w:type=\"separator\" w:id=\"-1\"><w:p><w:r><w:separator/></w:r></w:p></w:footnote>" +
            "<w:footnote w:id=\"1\"><w:p><w:r><w:t>source note</w:t></w:r></w:p></w:footnote></w:footnotes>");
        using var dst = Doc(dp);
        using var src = Doc(sp);
        MergeReport r = dst.Append(src);
        Assert.Equal(1, r.CountOf("footnotes"));
        using Document again = Reopen(dst);
        Assert.Equal([1L, 2L], again.NoteReferences(Notes.NoteKind.Footnote).Select(x => x.Id).ToArray());
        Assert.Equal("source note", again.Footnotes.Find(2)!.Text);
        Assert.Equal("dest note", again.Footnotes.Find(1)!.Text);
    }

    [Fact]
    public void Notes_CreateThePartWhenTheDestinationHasNone()
    {
        InMemoryPackage sp = TestDocs.Package("<w:p><w:r><w:footnoteReference w:id=\"1\"/></w:r></w:p>");
        PartHelper.Add(sp, "/word/footnotes.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.footnotes+xml", PartHelper.Rel + "footnotes",
            $"<w:footnotes xmlns:w=\"{W}\"><w:footnote w:id=\"1\"><w:p><w:r><w:t>note</w:t></w:r></w:p></w:footnote></w:footnotes>");
        using var dst = Doc(TestDocs.Package(P("d")));
        using var src = Doc(sp);
        dst.Append(src);
        using Document again = Reopen(dst);
        Assert.Equal("note", again.Footnotes.Find(1)!.Text);
    }

    [Fact]
    public void Comments_AreCopiedWithNewIds()
    {
        string body = "<w:p><w:commentRangeStart w:id=\"0\"/><w:r><w:t>x</w:t></w:r><w:commentRangeEnd w:id=\"0\"/><w:r><w:commentReference w:id=\"0\"/></w:r></w:p>";
        string comments(string who, string text) => $"<w:comments xmlns:w=\"{W}\"><w:comment w:id=\"0\" w:author=\"{who}\"><w:p><w:r><w:t>{text}</w:t></w:r></w:p></w:comment></w:comments>";
        InMemoryPackage dp = TestDocs.Package(body);
        PartHelper.Add(dp, "/word/comments.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.comments+xml", PartHelper.Rel + "comments", comments("Ann", "from dest"));
        InMemoryPackage sp = TestDocs.Package(body);
        PartHelper.Add(sp, "/word/comments.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.comments+xml", PartHelper.Rel + "comments", comments("Bob", "from source"));
        using var dst = Doc(dp);
        using var src = Doc(sp);
        MergeReport r = dst.Append(src);
        Assert.Equal(1, r.CountOf("comments"));
        using Document again = Reopen(dst);
        Assert.Equal(2, again.Comments.Count);
        Assert.Equal("from source", again.Comments.Find(1)!.Text);
        Assert.Equal("Bob", again.Comments.Find(1)!.Author);
        Assert.NotNull(again.Comments.Find(1)!.Anchor.Start);
        Assert.NotNull(again.Comments.Find(1)!.Anchor.Reference);
    }

    [Fact]
    public void Revisions_GetNewIdsAndTheirAuthorsAreReported()
    {
        string dstBody = "<w:p><w:ins w:id=\"1\" w:author=\"Ann\" w:date=\"2024-01-01T00:00:00Z\"><w:r><w:t>a</w:t></w:r></w:ins></w:p>";
        string srcBody = "<w:p><w:ins w:id=\"1\" w:author=\"Bob\" w:date=\"2024-01-01T00:00:00Z\"><w:r><w:t>b</w:t></w:r></w:ins><w:del w:id=\"2\" w:author=\"Ann\" w:date=\"2024-01-01T00:00:00Z\"><w:r><w:delText>c</w:delText></w:r></w:del></w:p>";
        using var dst = Doc(TestDocs.Package(dstBody));
        using var src = Doc(TestDocs.Package(srcBody));
        MergeReport r = dst.Append(src);
        Assert.Equal(2, r.CountOf("revisions"));
        Assert.Equal("Bob", Assert.Single(r.ResolutionsOf("merge.revision-author")).Item);
        string xml = dst.Body.Container.Node.OuterXml;
        string[] ids = [.. System.Text.RegularExpressions.Regex.Matches(xml, "<w:(?:ins|del) w:id=\"(\\d+)\"").Select(x => x.Groups[1].Value)];
        Assert.Equal(3, ids.Distinct().Count());
    }

    [Fact]
    public void ContentControlsAndDrawings_GetUniqueIds()
    {
        string sdt(int id) => $"<w:sdt><w:sdtPr><w:id w:val=\"{id}\"/><w:tag w:val=\"t\"/></w:sdtPr><w:sdtContent><w:p><w:r><w:t>c</w:t></w:r></w:p></w:sdtContent></w:sdt>";
        InMemoryPackage dp = TestDocs.Package(sdt(7) + Pic("rIdA", 4));
        AddImage(dp, "/word/media/image1.png", "rIdA", Png1);
        InMemoryPackage sp = TestDocs.Package(sdt(7) + Pic("rIdB", 4));
        AddImage(sp, "/word/media/image1.png", "rIdB", Png1);
        using var dst = Doc(dp);
        using var src = Doc(sp);
        dst.Append(src);
        string xml = dst.Body.Container.Node.OuterXml;
        string[] sdtIds = [.. System.Text.RegularExpressions.Regex.Matches(xml, "<w:id w:val=\"(\\d+)\"").Select(x => x.Groups[1].Value)];
        Assert.Equal(2, sdtIds.Distinct().Count());
        string[] drawIds = [.. System.Text.RegularExpressions.Regex.Matches(xml, "<wp:docPr id=\"(\\d+)\"").Select(x => x.Groups[1].Value)];
        Assert.Equal(2, drawIds.Distinct().Count());
    }

    [Fact]
    public void Images_AreSharedByContentAndCopiedWhenDifferent()
    {
        InMemoryPackage dp = TestDocs.Package(Pic("rIdA", 1));
        AddImage(dp, "/word/media/image1.png", "rIdA", Png1);
        InMemoryPackage sp = TestDocs.Package(Pic("rIdB", 1) + Pic("rIdC", 2));
        AddImage(sp, "/word/media/image1.png", "rIdB", Png1);
        AddImage(sp, "/word/media/image2.png", "rIdC", Png2);
        using var dst = Doc(dp);
        using var src = Doc(sp);
        MergeReport r = dst.Append(src);
        Assert.Equal(1, r.CountOf("images"));
        Assert.Equal(1, r.CountOf("parts-shared"));
        Assert.Equal(2, dst.Package.Parts.Count(p => p.ContentType == "image/png"));
        string xml = dst.Body.Container.Node.OuterXml;
        string[] embeds = [.. System.Text.RegularExpressions.Regex.Matches(xml, "r:embed=\"([^\"]+)\"").Select(x => x.Groups[1].Value)];
        Assert.Equal(3, embeds.Length);
        Assert.Equal(embeds[0], embeds[1]);
        Assert.NotEqual(embeds[0], embeds[2]);
        foreach (string id in embeds)
        {
            Assert.Contains(dst.Package.GetRelationships("/word/document.xml"), x => x.Id == id);
        }
    }

    [Fact]
    public void Sections_KeepSource_TakesTheSourcesPageSetupAndItsHeader()
    {
        string sect(string extra) => $"<w:sectPr>{extra}<w:pgSz w:w=\"12240\" w:h=\"15840\"/></w:sectPr>";
        InMemoryPackage dp = TestDocs.Package(P("d") + sect(string.Empty));
        InMemoryPackage sp = TestDocs.Package(P("s") + "<w:sectPr xmlns:r=\"" + R + "\"><w:headerReference w:type=\"default\" r:id=\"rIdH\"/><w:pgSz w:w=\"16838\" w:h=\"11906\" w:orient=\"landscape\"/></w:sectPr>");
        PartHelper.Add(sp, "/word/header1.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml", PartHelper.Rel + "header",
            $"<w:hdr xmlns:w=\"{W}\"><w:p><w:r><w:t>source header</w:t></w:r></w:p></w:hdr>");
        var rels = new List<Relationship>(sp.GetRelationships("/word/document.xml"));
        rels[^1] = rels[^1] with { Id = "rIdH" };
        sp.SetRelationships("/word/document.xml", rels);
        using var dst = Doc(dp);
        using var src = Doc(sp);
        MergeReport r = dst.Append(src, new MergeOptions { Sections = SectionHandling.KeepSource });
        using Document again = Reopen(dst);
        Assert.Equal(2, again.Sections.Count);
        Assert.Equal(12240, again.Sections[0].PageSize.Width);
        Assert.Equal(16838, again.Sections[1].PageSize.Width);
        Assert.Equal("source header", again.Sections[1].GetOwn(HeaderFooterKind.Header, HeaderFooterType.Default)!.Text);
        Assert.Equal(1, r.CountOf("headers"));
        Assert.DoesNotContain(r.Loss.Items, i => i.Code == "merge.section-properties");
    }

    [Fact]
    public void Sections_KeepSource_LeavesTheDestinationsOwnHeaderReferenceAlone()
    {
        // Both documents name their header relationship rIdH; the destination's break must still point at the destination's header.
        InMemoryPackage Build(string text, string sectExtra)
        {
            InMemoryPackage p = TestDocs.Package(P(text) + "<w:sectPr xmlns:r=\"" + R + "\"><w:headerReference w:type=\"default\" r:id=\"rIdH\"/><w:pgSz w:w=\"" + sectExtra + "\" w:h=\"15840\"/></w:sectPr>");
            PartHelper.Add(p, "/word/header1.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml", PartHelper.Rel + "header",
                $"<w:hdr xmlns:w=\"{W}\"><w:p><w:r><w:t>{text} header</w:t></w:r></w:p></w:hdr>");
            var rels = new List<Relationship>(p.GetRelationships("/word/document.xml"));
            rels[^1] = rels[^1] with { Id = "rIdH" };
            p.SetRelationships("/word/document.xml", rels);
            return p;
        }

        using var dst = Doc(Build("d", "12240"));
        using var src = Doc(Build("s", "16838"));
        dst.Append(src, new MergeOptions { Sections = SectionHandling.KeepSource });
        using Document again = Reopen(dst);
        Assert.Equal(2, again.Sections.Count);
        Assert.Equal("d header", again.Sections[0].GetOwn(HeaderFooterKind.Header, HeaderFooterType.Default)!.Text);
        Assert.Equal("s header", again.Sections[1].GetOwn(HeaderFooterKind.Header, HeaderFooterType.Default)!.Text);
    }

    [Fact]
    public void Sections_UseDestination_ReportsTheLossOfTheSourcePageSetup()
    {
        string sect(int w) => $"<w:sectPr><w:pgSz w:w=\"{w}\" w:h=\"15840\"/></w:sectPr>";
        using var dst = Doc(TestDocs.Package(P("d") + sect(12240)));
        using var src = Doc(TestDocs.Package(P("s") + sect(16838)));
        MergeReport r = dst.Append(src);
        Assert.Equal(1, r.Loss.CountOf("merge.section-properties"));
        Assert.Single(dst.Sections);
        Assert.Equal(12240, dst.Sections[0].PageSize.Width);
    }

    [Fact]
    public void Environment_DifferingDefaultsThemeAndFontsAreLossItems()
    {
        InMemoryPackage dp = TestDocs.Package(P("d"), "<w:docDefaults><w:rPrDefault><w:rPr><w:sz w:val=\"22\"/></w:rPr></w:rPrDefault></w:docDefaults>", themeXml: "<a:theme xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" name=\"A\"/>");
        InMemoryPackage sp = TestDocs.Package(P("s"), "<w:docDefaults><w:rPrDefault><w:rPr><w:sz w:val=\"28\"/></w:rPr></w:rPrDefault></w:docDefaults>", themeXml: "<a:theme xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" name=\"B\"/>");
        using var dst = Doc(dp);
        using var src = Doc(sp);
        MergeReport r = dst.Append(src);
        Assert.Equal(1, r.Loss.CountOf("merge.doc-defaults"));
        Assert.Equal(1, r.Loss.CountOf("merge.theme"));
    }

    [Fact]
    public void AppendingADocumentToItself_DoublesTheBodyWithoutConflicts()
    {
        using var d = Doc(TestDocs.Package(P("a") + P("b"), Style("S", "S", "<w:b/>")));
        MergeReport r = d.Append(d);
        Assert.Equal(["a", "b", "a", "b"], Texts(d));
        Assert.Empty(r.Resolutions);
    }

    [Fact]
    public void StrictAndTransitionalCannotBeCombined()
    {
        string strict = "http://purl.oclc.org/ooxml/wordprocessingml/main";
        var p = new InMemoryPackage();
        p.SetRelationships(null, [new Relationship("rId1", "http://purl.oclc.org/ooxml/officeDocument/relationships/officeDocument", "word/document.xml")]);
        p.AddPart("/word/document.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml",
            Encoding.UTF8.GetBytes($"<w:document xmlns:w=\"{strict}\"><w:body><w:p><w:r><w:t>x</w:t></w:r></w:p></w:body></w:document>"));
        using var dst = Doc(TestDocs.Package(P("d")));
        using var src = Doc(p);
        Assert.Throws<OfficeUnsupportedException>(() => dst.Append(src));
    }

    [Fact]
    public void MissingReferencesAreProblemsNotCrashes()
    {
        using var dst = Doc(TestDocs.Package(P("d")));
        using var src = Doc(TestDocs.Package("<w:p><w:r><w:footnoteReference w:id=\"4\"/></w:r></w:p><w:p><w:r><w:drawing><a:blip xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" r:embed=\"rIdNone\"/></w:drawing></w:r></w:p>"));
        MergeReport r = dst.Append(src);
        Assert.Contains(r.Problems, p => p.Code == "merge.note-missing");
        Assert.Contains(r.Problems, p => p.Code == "merge.relationship-missing");
        Assert.DoesNotContain("footnoteReference", dst.Body.Container.Node.OuterXml);
    }

    private static InMemoryPackage WithRoot(string rootExtra, string bodyXml)
    {
        InMemoryPackage p = TestDocs.Package(bodyXml);
        string xml = Encoding.UTF8.GetString(p.FindPart("/word/document.xml")!.GetBytes().Span);
        xml = xml.Replace("<w:document ", "<w:document " + rootExtra + " ", StringComparison.Ordinal);
        p.FindPart("/word/document.xml")!.SetBytes(Encoding.UTF8.GetBytes(xml));
        return p;
    }

    private const string McDecl = "xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\"";
    private const string W15 = "http://schemas.microsoft.com/office/word/2012/wordml";

    [Fact]
    public void IgnorableNamespacesOfCopiedContentAreDeclaredAndListedOnTheDestinationRoot()
    {
        using var dst = Doc(TestDocs.Package(P("d")));
        using var src = Doc(WithRoot($"{McDecl} xmlns:w15=\"{W15}\" mc:Ignorable=\"w15\"", "<w:p w15:foo=\"1\"><w:r><w:t>x</w:t></w:r></w:p>"));
        MergeReport r = dst.Append(src);
        Assert.Empty(r.Problems);
        string root = dst.MainMarkup.Root.OuterXml;
        Assert.Contains($"xmlns:w15=\"{W15}\"", root[..root.IndexOf('>', StringComparison.Ordinal)], StringComparison.Ordinal);
        Assert.Contains("mc:Ignorable=\"w15\"", root, StringComparison.Ordinal);
        using Document again = Reopen(dst);
        Assert.Equal(["d", "x"], Texts(again));
    }

    [Fact]
    public void APrefixTheDestinationBindsElsewhereIsAProblem()
    {
        using var dst = Doc(WithRoot("xmlns:w15=\"urn:other\"", P("d")));
        using var src = Doc(WithRoot($"{McDecl} xmlns:w15=\"{W15}\" mc:Ignorable=\"w15\"", "<w:p w15:foo=\"1\"><w:r><w:t>x</w:t></w:r></w:p>"));
        MergeReport r = dst.Append(src);
        Assert.Contains(r.Problems, p => p.Code == "merge.namespace-prefix-clash");
    }

    [Fact]
    public void ReferencesToThingsTheSourceLacksAreProblems()
    {
        using var dst = Doc(TestDocs.Package(P("d")));
        InMemoryPackage sp = TestDocs.Package("<w:p><w:pPr><w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"5\"/></w:numPr></w:pPr><w:commentRangeStart w:id=\"3\"/><w:r><w:t>x</w:t></w:r><w:r><w:commentReference w:id=\"3\"/></w:r></w:p>");
        sp.AddPart("/word/media/gone.png", "image/png", Png1);
        sp.SetRelationships("/word/document.xml", [new Relationship("rIdGone", PartHelper.Rel + "image", "media/absent.png")]);
        using var src = Doc(sp);
        MergeReport r = dst.Append(src);
        Assert.Contains(r.Problems, p => p.Code == "merge.numbering-missing");
        Assert.Contains(r.Problems, p => p.Code == "merge.comment-missing");
        Assert.DoesNotContain("commentReference", dst.Body.Container.Node.OuterXml, StringComparison.Ordinal);
    }

    [Fact]
    public void ARelationshipToAMissingPartIsAProblem()
    {
        using var dst = Doc(TestDocs.Package(P("d")));
        InMemoryPackage sp = TestDocs.Package(Pic("rIdGone", 1));
        sp.SetRelationships("/word/document.xml", [new Relationship("rIdGone", PartHelper.Rel + "image", "media/absent.png")]);
        using var src = Doc(sp);
        MergeReport r = dst.Append(src);
        Assert.Contains(r.Problems, p => p.Code == "merge.part-missing");
    }

    [Fact]
    public void Merge_OfFilesWritesOneDocument()
    {
        string dir = Path.Combine(Path.GetTempPath(), "merge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string[] inputs = [Path.Combine(dir, "a.docx"), Path.Combine(dir, "b.docx"), Path.Combine(dir, "c.docx")];
            string[] words = ["one", "two", "three"];
            for (int i = 0; i < 3; i++)
            {
                using Document d = Doc(TestDocs.Package(P(words[i])));
                d.SaveAs(inputs[i]);
            }

            string output = Path.Combine(dir, "out.docx");
            IReadOnlyList<MergeReport> reports = Document.Merge(output, inputs);
            Assert.Equal(2, reports.Count);
            using Document merged = Document.Open(output);
            Assert.Equal(words, Texts(merged));
            Assert.Throws<ArgumentException>(() => Document.Merge(output));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
