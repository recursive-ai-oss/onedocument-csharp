using RecursiveAI.OneOffice.Documents.Revisions;
using RecursiveAI.OneOffice.Markup;
using static RecursiveAI.OneOffice.Documents.Tests.Fixture;

namespace RecursiveAI.OneOffice.Documents.Tests;

public class RevisionResolveTests
{
    private static int Left(RevisionSet set) { set.Refresh(); return set.Revisions.Count; }

    [Fact]
    public void AcceptAllKeepsInsertionsDropsDeletionsAndLeavesNoMarkup()
    {
        RevisionSet set = Set("<w:p>" + R("A ") + Ins(1, "Ann", R("new ")) + Del(2, "Bob", "old ") + R("end") + "</w:p>");
        string accepted = Text(set, RevisionView.Accepted);
        Assert.Equal(2, set.AcceptAll());
        Assert.Equal(0, Left(set));
        Assert.Equal(accepted, Text(set, RevisionView.Accepted));
        Assert.Equal("A new end\n", Text(set, RevisionView.Rejected));
        Assert.DoesNotContain("w:ins", Xml(set));
        Assert.DoesNotContain("delText", Xml(set));
    }

    [Fact]
    public void RejectAllRestoresDeletedTextAsPlainText()
    {
        RevisionSet set = Set("<w:p>" + R("A ") + Ins(1, "Ann", R("new ")) + Del(2, "Bob", "old ") + R("end") + "</w:p>");
        Assert.Equal(2, set.RejectAll());
        Assert.Equal("A old end\n", Text(set, RevisionView.Accepted));
        Assert.DoesNotContain("delText", Xml(set));
        Assert.Contains("<w:t>old </w:t>", Xml(set).Replace(" xml:space=\"preserve\"", string.Empty));
    }

    [Fact]
    public void ParagraphMarksJoinParagraphs()
    {
        string body = "<w:p><w:pPr><w:rPr><w:del w:id=\"1\" w:author=\"A\"/></w:rPr></w:pPr>" + R("one ") + "</w:p><w:p><w:pPr><w:jc w:val=\"right\"/></w:pPr>" + R("two") + "</w:p>";
        RevisionSet accept = Set(body);
        accept.AcceptAll();
        Assert.Equal("one two\n", Text(accept, RevisionView.Accepted));
        Assert.Contains("<w:jc w:val=\"right\"/>", Xml(accept));
        RevisionSet reject = Set(body);
        reject.RejectAll();
        Assert.Equal("one \ntwo\n", Text(reject, RevisionView.Accepted));
        Assert.DoesNotContain("w:del", Xml(reject));
        Assert.DoesNotContain("<w:pPr><w:rPr/>", Xml(reject));
    }

    [Fact]
    public void InsertedParagraphVanishesOnReject()
    {
        string body = "<w:p>" + R("keep") + "</w:p><w:p><w:pPr><w:rPr><w:ins w:id=\"1\" w:author=\"A\"/></w:rPr></w:pPr>" + Ins(2, "A", R("extra")) + "</w:p><w:p>" + R("tail") + "</w:p>";
        RevisionSet set = Set(body);
        Assert.Equal("keep\nextra\ntail\n", Text(set, RevisionView.Accepted));
        Assert.Equal("keep\ntail\n", Text(set, RevisionView.Rejected));
        set.RejectAll();
        Assert.Equal("keep\ntail\n", Text(set, RevisionView.Accepted));
        Assert.Throws<InvalidOperationException>(() => Paragraph(set, 2));
    }

    [Fact]
    public void PropertyChangesAcceptByDroppingTheRecordAndRejectByRestoring()
    {
        string body = "<w:p><w:pPr><w:jc w:val=\"center\"/><w:rPr><w:b/></w:rPr><w:pPrChange w:id=\"1\" w:author=\"A\"><w:pPr><w:jc w:val=\"left\"/></w:pPr></w:pPrChange></w:pPr>" +
            "<w:r><w:rPr><w:b/><w:i/><w:rPrChange w:id=\"2\" w:author=\"A\"><w:rPr><w:b/></w:rPr></w:rPrChange></w:rPr><w:t>x</w:t></w:r></w:p>";
        RevisionSet a = Set(body);
        a.AcceptAll();
        Assert.Equal("<w:p><w:pPr><w:jc w:val=\"center\"/><w:rPr><w:b/></w:rPr></w:pPr><w:r><w:rPr><w:b/><w:i/></w:rPr><w:t>x</w:t></w:r></w:p>", Paragraph(a, 0).OuterXml);
        RevisionSet r = Set(body);
        r.RejectAll();
        Assert.Equal("<w:p><w:pPr><w:jc w:val=\"left\"/><w:rPr><w:b/></w:rPr></w:pPr><w:r><w:rPr><w:b/></w:rPr><w:t>x</w:t></w:r></w:p>", Paragraph(r, 0).OuterXml);
    }

    [Fact]
    public void RejectingFormattingThatHadNoPropertiesRemovesTheContainer()
    {
        RevisionSet set = Set("<w:p><w:r><w:rPr><w:b/><w:rPrChange w:id=\"1\" w:author=\"A\"><w:rPr/></w:rPrChange></w:rPr><w:t>x</w:t></w:r></w:p>");
        set.RejectAll();
        Assert.Equal("<w:p><w:r><w:t>x</w:t></w:r></w:p>", Paragraph(set, 0).OuterXml);
    }

    [Fact]
    public void SectionAndTableChangesRestoreOldProperties()
    {
        string body = "<w:tbl><w:tblPr><w:jc w:val=\"center\"/><w:tblPrChange w:id=\"1\" w:author=\"A\"><w:tblPr><w:jc w:val=\"left\"/></w:tblPr></w:tblPrChange></w:tblPr>" +
            "<w:tblGrid><w:gridCol w:w=\"200\"/><w:tblGridChange w:id=\"2\" w:author=\"A\"><w:tblGrid><w:gridCol w:w=\"100\"/></w:tblGrid></w:tblGridChange></w:tblGrid>" +
            "<w:tr><w:trPr><w:cantSplit/><w:trPrChange w:id=\"3\" w:author=\"A\"><w:trPr/></w:trPrChange></w:trPr><w:tc><w:tcPr><w:tcW w:w=\"9\"/><w:tcPrChange w:id=\"4\" w:author=\"A\"><w:tcPr><w:tcW w:w=\"5\"/></w:tcPr></w:tcPrChange></w:tcPr><w:p/></w:tc></w:tr></w:tbl>" +
            "<w:sectPr><w:headerReference w:type=\"default\" r:id=\"rId1\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"/><w:pgSz w:w=\"20000\"/><w:sectPrChange w:id=\"5\" w:author=\"A\"><w:sectPr><w:pgSz w:w=\"12240\"/></w:sectPr></w:sectPrChange></w:sectPr>";
        RevisionSet set = Set(body);
        Assert.Equal(5, set.Revisions.Count);
        set.RejectAll();
        string x = Xml(set);
        Assert.Contains("<w:tblPr><w:jc w:val=\"left\"/></w:tblPr>", x);
        Assert.Contains("<w:tblGrid><w:gridCol w:w=\"100\"/></w:tblGrid>", x);
        Assert.Contains("<w:tcPr><w:tcW w:w=\"5\"/></w:tcPr>", x);
        Assert.DoesNotContain("trPr", x);
        Assert.Contains("<w:pgSz w:w=\"12240\"/>", x);
        Assert.Contains("headerReference", x);
        Assert.DoesNotContain("Change", x);
    }

    [Fact]
    public void RowAndCellRevisions()
    {
        string cell(string t, string pr) => "<w:tc><w:tcPr>" + pr + "</w:tcPr><w:p>" + R(t) + "</w:p></w:tc>";
        string body = "<w:tbl><w:tblPr/><w:tblGrid/>" +
            "<w:tr><w:trPr><w:ins w:id=\"1\" w:author=\"A\"/></w:trPr>" + cell("newrow", "") + "</w:tr>" +
            "<w:tr><w:trPr><w:del w:id=\"2\" w:author=\"A\"/></w:trPr>" + cell("oldrow", "") + "</w:tr>" +
            "<w:tr>" + cell("c1", "<w:cellIns w:id=\"3\" w:author=\"A\"/>") + cell("c2", "<w:cellDel w:id=\"4\" w:author=\"A\"/>") + cell("c3", "") + "</w:tr></w:tbl>";
        RevisionSet a = Set(body);
        string acceptedView = Text(a, RevisionView.Accepted);
        string rejectedView = Text(a, RevisionView.Rejected);
        a.AcceptAll();
        Assert.Equal("newrow\nc1\nc3\n", acceptedView);
        Assert.Equal(acceptedView, Text(a, RevisionView.Accepted));
        RevisionSet r = Set(body);
        r.RejectAll();
        Assert.Equal("oldrow\nc2\nc3\n", rejectedView);
        Assert.Equal(rejectedView, Text(r, RevisionView.Accepted));
        Assert.Equal(0, Left(r));
    }

    [Fact]
    public void RejectingAnInsertedOnlyRowRemovesTheTable()
    {
        RevisionSet set = Set("<w:tbl><w:tblPr/><w:tblGrid/><w:tr><w:trPr><w:ins w:id=\"1\" w:author=\"A\"/></w:trPr><w:tc><w:p/></w:tc></w:tr></w:tbl><w:p>" + R("after") + "</w:p>");
        set.RejectAll();
        Assert.DoesNotContain("w:tbl", Xml(set));
        Assert.Equal("after\n", Text(set, RevisionView.Accepted));
    }

    [Fact]
    public void CellMergeAppliesNewOrOriginalState()
    {
        string body = "<w:tbl><w:tblPr/><w:tblGrid/><w:tr><w:tc><w:tcPr><w:vMerge/><w:cellMerge w:id=\"1\" w:author=\"A\" w:vMerge=\"cont\" w:vMergeOrig=\"rest\"/></w:tcPr><w:p/></w:tc></w:tr></w:tbl>";
        RevisionSet a = Set(body);
        a.AcceptAll();
        Assert.Contains("<w:vMerge/>", Xml(a));
        RevisionSet r = Set(body);
        r.RejectAll();
        Assert.Contains("<w:vMerge w:val=\"restart\"/>", Xml(r));
        Assert.DoesNotContain("cellMerge", Xml(r));
    }

    [Fact]
    public void MovesResolveBothEndsAndTheirRangeMarkers()
    {
        string body = "<w:p>" + R("a") + "<w:moveFromRangeStart w:id=\"1\" w:name=\"m\" w:author=\"A\"/><w:moveFrom w:id=\"2\" w:author=\"A\"><w:r><w:delText>MOVED</w:delText></w:r></w:moveFrom><w:moveFromRangeEnd w:id=\"1\"/>" + R("b") + "</w:p>" +
            "<w:p>" + R("c") + "<w:moveToRangeStart w:id=\"3\" w:name=\"m\" w:author=\"A\"/><w:moveTo w:id=\"4\" w:author=\"A\"><w:r><w:t>MOVED</w:t></w:r></w:moveTo><w:moveToRangeEnd w:id=\"3\"/></w:p>";
        RevisionSet set = Set(body);
        Assert.Equal("ab\ncMOVED\n", Text(set, RevisionView.Accepted));
        Assert.Equal("aMOVEDb\nc\n", Text(set, RevisionView.Rejected));
        Assert.Equal("m", set.Revisions[1].MoveName);
        RevisionSet a = Set(body);
        a.AcceptAll();
        Assert.DoesNotContain("Range", Xml(a));
        Assert.Equal("ab\ncMOVED\n", Text(a, RevisionView.Accepted));
        RevisionSet r = Set(body);
        r.RejectAll();
        Assert.DoesNotContain("Range", Xml(r));
        Assert.Equal("aMOVEDb\nc\n", Text(r, RevisionView.Accepted));
    }

    [Fact]
    public void ByAuthorAndSingleRevisionLeaveTheRestAlone()
    {
        RevisionSet set = Set("<w:p>" + Ins(1, "Ann", R("one ")) + Ins(2, "Bob", R("two ")) + Del(3, "Ann", "three") + "</w:p>");
        Assert.Equal(2, set.Accept(set.ByAuthor("Ann")));
        Assert.Single(set.Revisions);
        Assert.Equal("Bob", set.Revisions[0].Author);
        Assert.Equal("one two \n", Text(set, RevisionView.Accepted));
        Assert.True(set.Reject(set.Revisions[0]));
        Assert.Equal("one \n", Text(set, RevisionView.Accepted));
        Assert.Empty(set.Revisions);
    }

    [Fact]
    public void ChangesThatCannotBeResolvedStayAndAreReported()
    {
        var sink = new RecursiveAI.OneOffice.Diagnostics.DefectCollection();
        RevisionPart part = Part("<w:p><w:trPr><w:ins w:id=\"1\" w:author=\"A\"/></w:trPr>" + R("x") + "</w:p>");
        RevisionSet set = RevisionSet.Scan([part], new RecursiveAI.OneOffice.OpenContext(defects: sink));
        Assert.Equal(0, set.RejectAll());
        Assert.Single(set.Revisions);
        RecursiveAI.OneOffice.Diagnostics.Defect d = Assert.Single(sink);
        Assert.Equal("docx.revision-unresolvable", d.Code);
        Assert.True(d.Location.Offset > 0);
    }

    [Fact]
    public void StaleRevisionsAreRefused()
    {
        RevisionSet set = Set("<w:p>" + Ins(1, "Ann", R("one")) + "</w:p>");
        Revision old = set.Revisions[0];
        set.AcceptAll();
        Assert.Throws<ArgumentException>(() => set.Accept(old));
    }

    [Fact]
    public void NestedAndPartialAcceptanceKeepsInnerChanges()
    {
        // Another author deleted text inside Ann's insertion: accepting only Ann's insertion keeps Bob's deletion.
        RevisionSet set = Set("<w:p><w:ins w:id=\"1\" w:author=\"Ann\">" + R("keep ") + Del(2, "Bob", "cut") + "</w:ins></w:p>");
        Assert.Equal(2, set.Revisions.Count);
        Assert.True(set.Accept(set.Revisions[0]));
        Assert.Single(set.Revisions);
        Assert.Equal(RevisionKind.Deletion, set.Revisions[0].Kind);
        Assert.Equal("cut", set.Revisions[0].Text);
        set.Reject(set.Revisions[0]);
        Assert.Equal("keep cut\n", Text(set, RevisionView.Accepted));
    }

    [Fact]
    public void UnrelatedMarkupSurvivesAndUntouchedPartsAreNotModified()
    {
        const string extra = "<mc:AlternateContent xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\"><mc:Choice Requires=\"x\"><w:r><w:t>c</w:t></w:r></mc:Choice></mc:AlternateContent>";
        RevisionSet set = Set("<w:p>" + extra + Ins(1, "A", R("i")) + "</w:p>");
        set.AcceptAll();
        Assert.Contains(extra, Xml(set));
        RevisionSet none = Set("<w:p>" + R("clean") + "</w:p>");
        none.AcceptAll();
        none.RejectAll();
        Assert.False(none.Parts[0].Markup.IsModified);
    }

    [Fact]
    public void FootnotePartsAreResolvedLikeAnyOther()
    {
        RevisionPart body = Part("<w:p>" + R("body") + "</w:p>");
        RevisionPart notes = Part("<w:p>" + Ins(1, "A", R("note")) + "</w:p>", "/word/footnotes.xml");
        RevisionSet set = RevisionSet.Scan([body, notes]);
        Assert.Single(set.Revisions);
        Assert.Equal("/word/footnotes.xml", set.Revisions[0].Part.Uri);
        set.RejectAll();
        Assert.False(body.Markup.IsModified);
        Assert.True(notes.Markup.IsModified);
        Assert.Equal("\n", RevisionSet.GetText(notes, RevisionView.Accepted));
    }
}
