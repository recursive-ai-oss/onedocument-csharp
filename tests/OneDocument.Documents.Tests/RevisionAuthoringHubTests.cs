using System.Text;
using RecursiveAI.OneDocument.Documents.Revisions;
using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Documents.Tests;

/// <summary>Authoring through the document model: ids from the caller, typed property changes, tables, moves, other parts.</summary>
public class RevisionAuthoringHubTests
{
    private static readonly DateTimeOffset When = new(2025, 3, 4, 5, 6, 7, TimeSpan.Zero);

    private const string Two =
        "<w:p><w:pPr><w:pStyle w:val=\"Body\"/><w:jc w:val=\"right\"/></w:pPr><w:r><w:rPr><w:b/><w:sz w:val=\"28\"/></w:rPr><w:t xml:space=\"preserve\">Hello </w:t></w:r><w:r><w:t>world</w:t></w:r></w:p>"
        + "<w:p><w:r><w:t>Second paragraph</w:t></w:r></w:p>";

    private static string Xml(Document d) => d.MainMarkup.Root.OuterXml;

    private static RevisionSet Reparse(Document d) =>
        RevisionSet.Scan([new RevisionPart("/word/document.xml", MarkupDocument.Parse(Encoding.UTF8.GetBytes(Xml(d))))]);

    private static string PartText(RevisionSet s, RevisionView v) => RevisionSet.GetText(s.Parts[0], v);

    private static string Pad(string body) => body;

    /// <summary>Accepted and rejected text from the live views, then again after really accepting or rejecting a re-read copy.</summary>
    private static void Check(Document d, string accepted, string rejected)
    {
        RevisionSet live = Reparse(d);
        Assert.Equal(accepted, PartText(live, RevisionView.Accepted));
        Assert.Equal(rejected, PartText(live, RevisionView.Rejected));
        RevisionSet a = Reparse(d);
        a.AcceptAll();
        Assert.Empty(a.Revisions);
        Assert.Equal(accepted, PartText(a, RevisionView.Accepted));
        RevisionSet r = Reparse(d);
        r.RejectAll();
        Assert.Empty(r.Revisions);
        Assert.Equal(rejected, PartText(r, RevisionView.Accepted));
    }

    private static string RejectedXml(Document d)
    {
        RevisionSet r = Reparse(d);
        r.RejectAll();
        return r.Parts[0].Markup.Root.OuterXml;
    }

    private static string AcceptedXml(Document d)
    {
        RevisionSet r = Reparse(d);
        r.AcceptAll();
        return r.Parts[0].Markup.Root.OuterXml;
    }

    private static (Document Doc, RevisionAuthor Au) Open(string body = Two, RevisionIds? ids = null)
    {
        Document d = TestDocs.Open(body);
        return (d, ids is null ? d.Revisions.CreateAuthor("Redliner", When) : d.Revisions.CreateAuthor("Redliner", When, ids));
    }

    private static List<Paragraph> Paras(Document d) => [.. d.Body.Paragraphs()];

    // ------------------------------------------------------------------ ids

    [Fact]
    public void StartingAtWritesTheSequenceFromTheCallersNumber()
    {
        (Document d, RevisionAuthor au) = Open(ids: RevisionIds.StartingAt(100));
        au.InsertText(Paras(d)[0], 0, "A");
        au.DeleteText(Paras(d)[1], 0, 3);
        d.Revisions.Refresh();
        Assert.Equal([100, 101], d.Revisions.Revisions.Select(r => r.Id).ToArray());
    }

    [Fact]
    public void CallbackSuppliesEveryId()
    {
        int n = 7000;
        (Document d, RevisionAuthor au) = Open(ids: RevisionIds.FromCallback(() => n += 10));
        au.InsertText(Paras(d)[0], 3, "XY");
        au.ReplaceText(Paras(d)[1], 0, 6, "Third");
        d.Revisions.Refresh();
        Assert.Equal([7010, 7020, 7030], d.Revisions.Revisions.Select(r => r.Id).OrderBy(i => i).ToArray());
    }

    [Fact]
    public void DefaultContinuesAfterTheLargestIdInUseIncludingBookmarks()
    {
        (Document d, RevisionAuthor au) = Open("<w:p><w:bookmarkStart w:id=\"41\" w:name=\"b\"/><w:r><w:t>text</w:t></w:r><w:bookmarkEnd w:id=\"41\"/></w:p>");
        au.InsertText(Paras(d)[0], 0, "x");
        d.Revisions.Refresh();
        Assert.Equal(42, d.Revisions.Revisions[0].Id);
    }

    [Fact]
    public void AnIdAlreadyInUseIsATypedError()
    {
        string body = "<w:p><w:ins w:id=\"5\" w:author=\"Ann\"><w:r><w:t>old</w:t></w:r></w:ins><w:r><w:t>text</w:t></w:r></w:p>";
        (Document d, RevisionAuthor au) = Open(body, RevisionIds.StartingAt(5));
        RevisionIdConflictException e = Assert.Throws<RevisionIdConflictException>(() => au.InsertText(Paras(d)[0], 0, "x"));
        Assert.Equal(5, e.Id);
        Assert.Equal("docx.revision-id-conflict", e.Defect.Code);

        (Document d2, RevisionAuthor au2) = Open(body, RevisionIds.FromCallback(() => -3));
        Assert.Throws<RevisionIdConflictException>(() => au2.InsertText(Paras(d2)[0], 0, "x"));

        // A bookmark's id counts too, and so does an id the author handed out earlier.
        (Document d3, RevisionAuthor au3) = Open("<w:p><w:bookmarkStart w:id=\"1\" w:name=\"b\"/><w:r><w:t>text</w:t></w:r></w:p>", RevisionIds.FromCallback(() => 1));
        Assert.Throws<RevisionIdConflictException>(() => au3.InsertText(Paras(d3)[0], 0, "x"));
        (Document d4, RevisionAuthor au4) = Open(ids: RevisionIds.FromCallback(() => 9));
        au4.InsertText(Paras(d4)[0], 0, "x");
        Assert.Throws<RevisionIdConflictException>(() => au4.InsertText(Paras(d4)[1], 0, "x"));
    }

    [Fact]
    public void TheSameEditsWriteTheSameBytes()
    {
        static string Run()
        {
            (Document d, RevisionAuthor au) = Open(ids: RevisionIds.StartingAt(300));
            List<Paragraph> p = Paras(d);
            au.ReplaceText(p[0], 2, 5, "ipsum");
            au.FormatText(p[0], 0, 3, new RunPropertiesDelta { Italic = true, Color = "FF0000" });
            au.ChangeParagraphProperties(p[1], new ParagraphPropertiesDelta { Alignment = ParagraphAlignment.Center, LeftIndent = 720 });
            au.MoveText(p[0], 0, 2, p[1], 4, "moved-a");
            au.InsertParagraph(p[1], "tail");
            return Xml(d);
        }

        Assert.Equal(Run(), Run());
    }

    // ----------------------------------------------------------------- text

    [Fact]
    public void ReplaceAcrossRunsKeepsFormattingOfTheOldText()
    {
        (Document d, RevisionAuthor au) = Open();
        au.ReplaceText(Paras(d)[0], 3, 6, "-NEW-");
        Check(d, "Hel-NEW-ld\nSecond paragraph\n", "Hello world\nSecond paragraph\n");
        Assert.Contains("<w:rPr><w:b/><w:sz w:val=\"28\"/></w:rPr><w:t xml:space=\"preserve\">-NEW-</w:t>", Xml(d));
        Assert.Equal(1, d.Revisions.Revisions.Count(r => r.Kind == RevisionKind.Insertion));
        Assert.Equal("lo wor", d.Revisions.Revisions.Single(r => r.Kind == RevisionKind.Deletion).Text);
    }

    [Fact]
    public void InsertAndDeleteWholeParagraphs()
    {
        (Document d, RevisionAuthor au) = Open();
        Paragraph added = au.InsertParagraph(Paras(d)[1], "Third");
        au.DeleteParagraph(Paras(d)[0]);
        Assert.Equal("Third", added.GetText(RevisionView.Accepted));
        Check(d, "Second paragraph\nThird\n", "Hello world\nSecond paragraph\n");
        // Rejecting restores the original properties of the paragraph the new one was typed after.
        Assert.Contains("<w:pStyle w:val=\"Body\"/>", RejectedXml(d));
    }

    [Fact]
    public void MovedTextCarriesTheNameOnBothRangeMarkers()
    {
        (Document d, RevisionAuthor au) = Open();
        au.MoveText(Paras(d)[1], 0, 6, Paras(d)[0], 5, "m-1");
        string xml = Xml(d);
        Assert.Contains("<w:moveFromRangeStart", xml);
        Assert.Contains("<w:moveToRangeStart", xml);
        Assert.Equal(2, xml.Split("w:name=\"m-1\"").Length - 1);
        Assert.Contains("<w:moveFromRangeEnd", xml);
        Assert.Contains("<w:moveToRangeEnd", xml);
        Check(d, "HelloSecond world\n paragraph\n", "Hello world\nSecond paragraph\n");
        RevisionSet set = Reparse(d);
        Assert.All(set.Revisions.Where(r => r.Kind is RevisionKind.MoveFrom or RevisionKind.MoveTo), r => Assert.Equal("m-1", r.MoveName));
    }

    [Fact]
    public void AMoveNameAlreadyInUseIsRefused()
    {
        (Document d, RevisionAuthor au) = Open();
        au.MoveText(Paras(d)[1], 0, 6, Paras(d)[0], 5, "same");
        d.Revisions.Refresh();
        Assert.Throws<ArgumentException>(() => au.MoveParagraph(Paras(d)[0], Paras(d)[^1], "same"));
    }

    [Fact]
    public void MovedParagraphIsRecordedAtBothEnds()
    {
        (Document d, RevisionAuthor au) = Open();
        au.MoveParagraph(Paras(d)[0], Paras(d)[1], "para-move");
        Check(d, "Second paragraph\nHello world\n", "Hello world\nSecond paragraph\n");
        Assert.Equal(2, Xml(d).Split("w:name=\"para-move\"").Length - 1);
    }

    // ----------------------------------------------------- typed properties

    [Fact]
    public void RunDeltaIsMergedInSchemaOrderAndRejectRestoresTheOldProperties()
    {
        (Document d, RevisionAuthor au) = Open();
        string original = Xml(d);
        au.FormatText(Paras(d)[0], 0, 5, new RunPropertiesDelta { Italic = true, Color = "336699", SizePoints = 10, Bold = false, Font = "Arial", Underline = RunUnderline.SingleLine });
        string xml = Xml(d);
        Assert.Contains("<w:rPr><w:rFonts w:ascii=\"Arial\" w:hAnsi=\"Arial\" w:eastAsia=\"Arial\" w:cs=\"Arial\"/><w:b w:val=\"0\"/><w:bCs w:val=\"0\"/><w:i/><w:iCs/><w:color w:val=\"336699\"/><w:sz w:val=\"20\"/><w:szCs w:val=\"20\"/><w:u w:val=\"single\"/><w:rPrChange", xml);
        Assert.Equal(1, Reparse(d).Revisions.Count(r => r.Kind == RevisionKind.RunPropertiesChange));
        Check(d, "Hello world\nSecond paragraph\n", "Hello world\nSecond paragraph\n");
        Assert.Equal(original.Replace("<w:t xml:space=\"preserve\">Hello </w:t></w:r>", "<w:t xml:space=\"preserve\">Hello</w:t></w:r><w:r><w:rPr><w:b/><w:sz w:val=\"28\"/></w:rPr><w:t xml:space=\"preserve\"> </w:t></w:r>", StringComparison.Ordinal), RejectedXml(d));
    }

    [Fact]
    public void RunDeltaOnARunKeepsTheFirstRecordedOriginal()
    {
        (Document d, RevisionAuthor au) = Open();
        Run run = Paras(d)[0].Runs().First();
        au.ChangeRunProperties(run, new RunPropertiesDelta { Italic = true });
        au.ChangeRunProperties(Paras(d)[0].Runs().First(), new RunPropertiesDelta { Strike = true });
        string rejected = RejectedXml(d);
        Assert.Contains("<w:r><w:rPr><w:b/><w:sz w:val=\"28\"/></w:rPr><w:t xml:space=\"preserve\">Hello </w:t></w:r>", rejected);
        string accepted = AcceptedXml(d);
        Assert.Contains("<w:r><w:rPr><w:b/><w:i/><w:iCs/><w:strike/><w:sz w:val=\"28\"/></w:rPr>", accepted);
    }

    [Fact]
    public void ParagraphDeltaKeepsUntouchedAttributesAndOrder()
    {
        (Document d, RevisionAuthor au) = Open("<w:p><w:pPr><w:ind w:right=\"100\" w:hanging=\"360\"/><w:jc w:val=\"left\"/></w:pPr><w:r><w:t>x</w:t></w:r></w:p>");
        au.ChangeParagraphProperties(Paras(d)[0], new ParagraphPropertiesDelta
        {
            LeftIndent = 720,
            FirstLineIndent = 240,
            SpacingBefore = 120,
            LineSpacingMultiple = 1.5,
            Alignment = ParagraphAlignment.Center,
            StyleId = "Heading1",
            NumberingId = 3,
            NumberingLevel = 1,
            KeepNext = true,
        });
        string xml = Xml(d);
        Assert.Contains("<w:pPr><w:pStyle w:val=\"Heading1\"/><w:keepNext/><w:numPr><w:ilvl w:val=\"1\"/><w:numId w:val=\"3\"/></w:numPr><w:spacing w:before=\"120\" w:line=\"360\" w:lineRule=\"auto\"/><w:ind w:right=\"100\" w:left=\"720\" w:firstLine=\"240\"/><w:jc w:val=\"center\"/><w:pPrChange", xml);
        Assert.Contains("<w:pPrChange w:id=\"1\" w:author=\"Redliner\" w:date=\"2025-03-04T05:06:07Z\"><w:pPr><w:ind w:right=\"100\" w:hanging=\"360\"/><w:jc w:val=\"left\"/></w:pPr></w:pPrChange>", xml);
        Check(d, "x\n", "x\n");
        Assert.Contains("<w:pPr><w:ind w:right=\"100\" w:hanging=\"360\"/><w:jc w:val=\"left\"/></w:pPr>", RejectedXml(d));
        Assert.Contains("<w:numId w:val=\"3\"/>", AcceptedXml(d));
    }

    [Fact]
    public void NumberingCanBeRemovedAsATrackedChange()
    {
        (Document d, RevisionAuthor au) = Open("<w:p><w:pPr><w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"2\"/></w:numPr></w:pPr><w:r><w:t>item</w:t></w:r></w:p>");
        au.ChangeParagraphProperties(Paras(d)[0], new ParagraphPropertiesDelta { NumberingId = 0 });
        Assert.Contains("<w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"0\"/></w:numPr>", AcceptedXml(d));
        Assert.Contains("<w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"2\"/></w:numPr>", RejectedXml(d));
    }

    [Fact]
    public void InvalidDeltasThrowBeforeTheDocumentChanges()
    {
        (Document d, RevisionAuthor au) = Open();
        string before = Xml(d);
        Assert.ThrowsAny<ArgumentException>(() => au.FormatText(Paras(d)[0], 0, 3, new RunPropertiesDelta { Color = "red" }));
        Assert.ThrowsAny<ArgumentException>(() => au.FormatText(Paras(d)[0], 0, 3, new RunPropertiesDelta { SizePoints = 10.3 }));
        Assert.ThrowsAny<ArgumentException>(() => au.ChangeParagraphProperties(Paras(d)[0], new ParagraphPropertiesDelta { NumberingLevel = 12 }));
        Assert.Equal(before, Xml(d));
    }

    // --------------------------------------------------------------- tables

    private const string Grid =
        "<w:tbl><w:tblPr><w:tblStyle w:val=\"TableGrid\"/><w:tblW w:w=\"0\" w:type=\"auto\"/></w:tblPr><w:tblGrid><w:gridCol w:w=\"3000\"/><w:gridCol w:w=\"3000\"/></w:tblGrid>"
        + "<w:tr><w:tc><w:tcPr><w:tcW w:w=\"3000\" w:type=\"dxa\"/></w:tcPr><w:p><w:r><w:t>a1</w:t></w:r></w:p></w:tc><w:tc><w:tcPr><w:tcW w:w=\"3000\" w:type=\"dxa\"/></w:tcPr><w:p><w:r><w:t>b1</w:t></w:r></w:p></w:tc></w:tr>"
        + "<w:tr><w:tc><w:tcPr><w:tcW w:w=\"3000\" w:type=\"dxa\"/></w:tcPr><w:p><w:r><w:t>a2</w:t></w:r></w:p></w:tc><w:tc><w:tcPr><w:tcW w:w=\"3000\" w:type=\"dxa\"/></w:tcPr><w:p><w:r><w:t>b2</w:t></w:r></w:p></w:tc></w:tr></w:tbl>"
        + "<w:p><w:r><w:t>after</w:t></w:r></w:p>";

    private static Table FirstTable(Document d) => d.Body.Blocks().OfType<Table>().First();

    [Fact]
    public void RowInsertWithTextAndRowDelete()
    {
        (Document d, RevisionAuthor au) = Open(Grid);
        string[] cells = ["a3", "b3"];
        au.InsertRow(FirstTable(d), 2, cells);
        au.MarkRowDeleted(FirstTable(d).Rows().First());
        Check(d, "a2\nb2\na3\nb3\nafter\n", "a1\nb1\na2\nb2\nafter\n");
        RevisionSet set = Reparse(d);
        Assert.Equal(1, set.Revisions.Count(r => r.Kind == RevisionKind.TableRowInserted));
        Assert.Equal(1, set.Revisions.Count(r => r.Kind == RevisionKind.TableRowDeleted));
    }

    [Fact]
    public void RowInsertWithTooManyTextsLeavesTheTableAlone()
    {
        (Document d, RevisionAuthor au) = Open(Grid);
        string before = Xml(d);
        string[] cells = ["1", "2", "3"];
        Assert.Throws<ArgumentException>(() => au.InsertRow(FirstTable(d), 1, cells));
        Assert.Equal(before, Xml(d));
    }

    [Fact]
    public void WholeTableInsertAndDelete()
    {
        (Document d, RevisionAuthor au) = Open(Two);
        string[][] grid = [["h1", "h2", "h3"], ["x", "y"]];
        Table t = au.InsertTable(Paras(d)[1], grid.Select(r => (IReadOnlyList<string>)r).ToList());
        Assert.Equal(2, t.RowCount);
        Check(d, "Hello world\nSecond paragraph\nh1\nh2\nh3\nx\ny\n\n", "Hello world\nSecond paragraph\n");
        Assert.DoesNotContain("<w:tbl>", RejectedXml(d));
        Assert.Equal(2, Reparse(d).Revisions.Count(r => r.Kind == RevisionKind.TableRowInserted));

        (Document d2, RevisionAuthor au2) = Open(Grid);
        au2.DeleteTable(FirstTable(d2));
        Check(d2, "after\n", "a1\nb1\na2\nb2\nafter\n");
        Assert.DoesNotContain("<w:tbl>", AcceptedXml(d2));
    }

    [Fact]
    public void TablePropertyChangesAreTypedAndMerged()
    {
        (Document d, RevisionAuthor au) = Open(Grid);
        Table t = FirstTable(d);
        au.ChangeTableProperties(t, new TablePropertiesDelta { WidthTwips = 6000, Alignment = ParagraphAlignment.Center, FixedLayout = true });
        au.ChangeTableRowProperties(t.Rows().First(), new RowPropertiesDelta { HeightTwips = 500, RepeatAsHeader = true });
        au.ChangeTableCellProperties(t.Rows().First().Cells().First(), new CellPropertiesDelta { ShadingFill = "DDDDDD", VerticalAlignment = "center" });
        string xml = Xml(d);
        Assert.Contains("<w:tblPr><w:tblStyle w:val=\"TableGrid\"/><w:tblW w:w=\"6000\" w:type=\"dxa\"/><w:jc w:val=\"center\"/><w:tblLayout w:type=\"fixed\"/><w:tblPrChange", xml);
        Assert.Contains("<w:trPr><w:trHeight w:val=\"500\" w:hRule=\"atLeast\"/><w:tblHeader/><w:trPrChange", xml);
        Assert.Contains("<w:tcW w:w=\"3000\" w:type=\"dxa\"/><w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"DDDDDD\"/><w:vAlign w:val=\"center\"/><w:tcPrChange", xml);
        RevisionSet set = Reparse(d);
        Assert.Equal(3, set.Revisions.Count);
        Check(d, "a1\nb1\na2\nb2\nafter\n", "a1\nb1\na2\nb2\nafter\n");
        string rejected = RejectedXml(d);
        Assert.Contains("<w:tblPr><w:tblStyle w:val=\"TableGrid\"/><w:tblW w:w=\"0\" w:type=\"auto\"/></w:tblPr>", rejected);
        Assert.DoesNotContain("trHeight", rejected);
        Assert.DoesNotContain("DDDDDD", rejected);
    }

    // ---------------------------------------------------- other part kinds

    [Fact]
    public void EditsInAFootnoteUseTheSameApi()
    {
        InMemoryPackageBuilder.Footnote(out Document d);
        RevisionAuthor au = d.Revisions.CreateAuthor("Redliner", When, RevisionIds.StartingAt(900));
        Paragraph p = d.Footnotes.Normal()[0].Paragraphs().First();
        au.ReplaceText(p, 0, 4, "New");
        d.Revisions.Refresh();
        Assert.Equal(2, d.Revisions.Revisions.Count);
        Assert.All(d.Revisions.Revisions, r => Assert.Equal("/word/footnotes.xml", r.Part.Uri));
        Assert.Equal("New note", p.GetText(RevisionView.Accepted));
        Assert.Equal("Some note", p.GetText(RevisionView.Rejected));
        Assert.Equal([900, 901], d.Revisions.Revisions.Select(r => r.Id).OrderBy(i => i).ToArray());
    }

    [Fact]
    public void AuthoringOnAFreshlyOpenedDocumentReachesEveryPart()
    {
        var stream = new MemoryStream();
        using (Document source = Document.Create())
        {
            source.Body.Paragraphs().First().SetText("Body");
            source.Footnotes.Add("Some note").AppendReference(source.Body.Paragraphs().First());
            source.SaveAs(stream, OfficeFormat.Docx);
        }

        stream.Position = 0;
        using Document d = Document.Open(stream);
        RevisionAuthor au = d.Revisions.CreateAuthor("Redliner", When, RevisionIds.StartingAt(10));
        au.InsertText(Paras(d)[0], 0, "x");
        au.ReplaceText(d.Footnotes.Normal()[0].Paragraphs().First(), 1, 4, "New");
        Assert.Equal(3, d.Revisions.Revisions.Count);
        d.CommitChanges();
        Assert.Equal("xBody", Paras(d)[0].GetText(RevisionView.Accepted));
        Assert.Equal(" New note", d.Footnotes.Normal()[0].Paragraphs().First().GetText(RevisionView.Accepted));
    }

    [Fact]
    public void ANodeFromAnotherDocumentIsRefused()
    {
        (_, RevisionAuthor au) = Open();
        Document other = TestDocs.Open(Two);
        Assert.Throws<ArgumentException>(() => au.InsertText(Paras(other)[0], 0, "x"));
    }

    [Fact]
    public void ClockIsNeverRead()
    {
        Document d = TestDocs.Open(Two);
        RevisionAuthor au = d.Revisions.CreateAuthor("NoDate", null, RevisionIds.StartingAt(1));
        au.InsertText(Paras(d)[0], 0, "x");
        Assert.DoesNotContain("w:date", Xml(d));
    }
}

internal static class InMemoryPackageBuilder
{
    private const string RelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";

    internal static void Footnote(out Document d)
    {
        RecursiveAI.OneDocument.Packaging.InMemoryPackage p = TestDocs.Package("<w:p><w:r><w:t>Body</w:t></w:r></w:p>");
        p.AddPart("/word/footnotes.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.footnotes+xml",
            Encoding.UTF8.GetBytes($"<w:footnotes xmlns:w=\"{TestDocs.WNs}\"><w:footnote w:type=\"separator\" w:id=\"-1\"><w:p><w:r><w:separator/></w:r></w:p></w:footnote><w:footnote w:id=\"1\"><w:p><w:r><w:t>Some note</w:t></w:r></w:p></w:footnote></w:footnotes>"));
        p.SetRelationships("/word/document.xml", [new RecursiveAI.OneDocument.Packaging.Relationship("rId5", RelType + "footnotes", "footnotes.xml")]);
        d = Document.FromPackage(p);
    }
}
