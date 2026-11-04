using RecursiveAI.OneOffice.Documents.Revisions;
using RecursiveAI.OneOffice.Markup;
using static RecursiveAI.OneOffice.Documents.Tests.Fixture;

namespace RecursiveAI.OneOffice.Documents.Tests;

public class RevisionAuthorTests
{
    private static readonly DateTimeOffset When = new(2025, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private const string Para = "<w:p><w:r><w:rPr><w:b/></w:rPr><w:t xml:space=\"preserve\">Hello </w:t></w:r><w:r><w:t xml:space=\"preserve\">brave new </w:t></w:r><w:r><w:t>world</w:t></w:r></w:p>";

    private static (RevisionSet Set, RevisionAuthor Author) Open(string body = Para, string who = "Zed")
    {
        RevisionSet set = Set(body);
        return (set, set.CreateAuthor(who, When));
    }

    private static void AssertRoundTrip(RevisionSet set, string original, string expected)
    {
        Assert.Equal(expected, Text(set, RevisionView.Accepted));
        Assert.Equal(original, Text(set, RevisionView.Rejected));
        string xml = Xml(set);
        RevisionSet reparsed = RevisionSet.Scan([new RevisionPart("/word/document.xml", MarkupDocument.Parse(System.Text.Encoding.UTF8.GetBytes(xml)))]);
        Assert.Equal(set.Revisions.Count, reparsed.Revisions.Count);
        RevisionSet a = Reparse(xml);
        a.AcceptAll();
        Assert.Equal(expected, Text(a, RevisionView.Accepted));
        Assert.Empty(a.Revisions);
        RevisionSet r = Reparse(xml);
        r.RejectAll();
        Assert.Equal(original, Text(r, RevisionView.Accepted));
        Assert.Empty(r.Revisions);
    }

    private static RevisionSet Reparse(string xml) =>
        RevisionSet.Scan([new RevisionPart("/word/document.xml", MarkupDocument.Parse(System.Text.Encoding.UTF8.GetBytes(xml)))]);

    [Fact]
    public void InsertTextInTheMiddleOfARunSplitsItAndKeepsFormatting()
    {
        (RevisionSet set, RevisionAuthor au) = Open();
        au.InsertText(Paragraph(set, 0), 3, "XY");
        Assert.Single(set.Revisions);
        Revision r = set.Revisions[0];
        Assert.Equal(RevisionKind.Insertion, r.Kind);
        Assert.Equal("Zed", r.Author);
        Assert.Equal(When, r.Date);
        Assert.Equal(1, r.Id);
        Assert.Equal("XY", r.Text);
        Assert.Contains("<w:rPr><w:b/></w:rPr><w:t xml:space=\"preserve\">XY</w:t>", Xml(set));
        AssertRoundTrip(set, "Hello brave new world\n", "HelXYlo brave new world\n");
    }

    [Fact]
    public void InsertTextAtStartEndAndBetweenRuns()
    {
        (RevisionSet set, RevisionAuthor au) = Open();
        au.InsertText(Paragraph(set, 0), 0, "<");
        au.InsertText(Paragraph(set, 0), 22, ">");
        au.InsertText(Paragraph(set, 0), 7, "|");
        AssertRoundTrip(set, "Hello brave new world\n", "<Hello |brave new world>\n");
        Assert.Equal([1, 2, 3], set.Revisions.Select(x => x.Id).Order().ToArray());
    }

    [Fact]
    public void InsertTextConvertsTabsAndBreaksAndEscapes()
    {
        (RevisionSet set, RevisionAuthor au) = Open("<w:p/>");
        au.InsertText(Paragraph(set, 0), 0, "a<b&c\td\ne");
        Assert.Contains("<w:t xml:space=\"preserve\">a&lt;b&amp;c</w:t><w:tab/><w:t xml:space=\"preserve\">d</w:t><w:br/><w:t xml:space=\"preserve\">e</w:t>", Xml(set));
        AssertRoundTrip(set, "\n", "a<b&c\td\ne\n");
    }

    [Fact]
    public void DeleteTextAcrossRunsSplitsThemAndWrapsOnce()
    {
        (RevisionSet set, RevisionAuthor au) = Open();
        au.DeleteText(Paragraph(set, 0), 3, 12);
        Assert.Single(set.Revisions);
        Assert.Equal("lo brave new", set.Revisions[0].Text);
        AssertRoundTrip(set, "Hello brave new world\n", "Hel world\n");
    }

    [Fact]
    public void DeletingYourOwnInsertionRemovesItAndAnotherAuthorsBecomesADeletionInside()
    {
        (RevisionSet set, RevisionAuthor au) = Open("<w:p>" + R("base ") + Ins(7, "Zed", R("mine")) + Ins(8, "Other", R("theirs")) + "</w:p>");
        au.DeleteText(Paragraph(set, 0), 5, 4);
        au.DeleteText(Paragraph(set, 0), 5, 6);
        Assert.Equal("base \n", Text(set, RevisionView.Accepted));
        Assert.DoesNotContain("mine", Xml(set));
        Assert.Contains("<w:ins w:id=\"8\" w:author=\"Other\"", Xml(set));
        Assert.Contains("<w:delText xml:space=\"preserve\">theirs</w:delText>", Xml(set));
        Assert.Equal("base \n", Text(set, RevisionView.Rejected));
        Assert.Equal("base \n", Reparse(Xml(set)).Tap(s => s.AcceptAll()).Text());
    }

    [Fact]
    public void InsertInsideAnotherAuthorsInsertionSplitsTheirWrapper()
    {
        (RevisionSet set, RevisionAuthor au) = Open("<w:p>" + Ins(8, "Other", R("abcdef")) + "</w:p>");
        au.InsertText(Paragraph(set, 0), 3, "-");
        string x = Xml(set);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Count(x, "w:author=\"Other\""));
        Assert.Equal("abc-def\n", Text(set, RevisionView.Accepted));
        Assert.Equal("\n", Text(set, RevisionView.Rejected));
    }

    [Fact]
    public void ReplaceTextWritesDeletionThenInsertion()
    {
        (RevisionSet set, RevisionAuthor au) = Open();
        au.ReplaceText(Paragraph(set, 0), 6, 5, "fierce");
        Assert.Equal([RevisionKind.Deletion, RevisionKind.Insertion], set.Revisions.Select(x => x.Kind).ToArray());
        AssertRoundTrip(set, "Hello brave new world\n", "Hello fierce new world\n");
    }

    // Observed in Word 365 (16.0.20527): moved-away text is w:t inside w:moveFrom; w:delText there triggers the repair prompt.
    [Fact]
    public void MovedAwayText_StaysPlainText()
    {
        (RevisionSet set, RevisionAuthor au) = Open("<w:p>" + R("one two three") + "</w:p><w:p>" + R("four") + "</w:p>");
        au.MoveText(Paragraph(set, 0), 4, 4, Paragraph(set, 1), 0);
        Assert.DoesNotContain("delText", Xml(set));
        au.MoveParagraph(Paragraph(set, 0), Paragraph(set, 1));
        Assert.DoesNotContain("delText", Xml(set));
    }

    [Fact]
    public void MoveTextWithinAndAcrossParagraphs()
    {
        (RevisionSet set, RevisionAuthor au) = Open("<w:p>" + R("one two three") + "</w:p><w:p>" + R("four") + "</w:p>");
        au.MoveText(Paragraph(set, 0), 4, 4, Paragraph(set, 1), 4);
        Assert.Contains(set.Revisions, x => x.Kind == RevisionKind.MoveFrom && x.MoveName is not null);
        Assert.Contains(set.Revisions, x => x.Kind == RevisionKind.MoveTo && x.MoveName is not null);
        Assert.Equal(set.Revisions.First(x => x.Kind == RevisionKind.MoveFrom).MoveName, set.Revisions.First(x => x.Kind == RevisionKind.MoveTo).MoveName);
        AssertRoundTrip(set, "one two three\nfour\n", "one three\nfourtwo \n");

        (RevisionSet s2, RevisionAuthor a2) = Open("<w:p>" + R("alpha beta gamma") + "</w:p>");
        a2.MoveText(Paragraph(s2, 0), 11, 5, Paragraph(s2, 0), 0);
        AssertRoundTrip(s2, "alpha beta gamma\n", "gammaalpha beta \n");
    }

    [Fact]
    public void MoveTextIntoItselfIsRefused()
    {
        (RevisionSet set, RevisionAuthor au) = Open();
        Assert.Throws<ArgumentException>(() => au.MoveText(Paragraph(set, 0), 2, 5, Paragraph(set, 0), 4));
        Assert.False(set.Parts[0].Markup.IsModified);
    }

    [Fact]
    public void InsertDeleteAndMoveParagraphs()
    {
        string body = "<w:p><w:pPr><w:pStyle w:val=\"H\"/></w:pPr>" + R("first") + "</w:p><w:p>" + R("second") + "</w:p><w:p>" + R("third") + "</w:p>";
        (RevisionSet set, RevisionAuthor au) = Open(body);
        au.InsertParagraph(Paragraph(set, 0), "inserted");
        AssertRoundTrip(set, "first\nsecond\nthird\n", "first\ninserted\nsecond\nthird\n");
        Assert.Contains("<w:pStyle w:val=\"H\"/>", Paragraph(set, 1).OuterXml);

        (RevisionSet d, RevisionAuthor ad) = Open(body);
        ad.DeleteParagraph(Paragraph(d, 1));
        AssertRoundTrip(d, "first\nsecond\nthird\n", "first\nthird\n");

        (RevisionSet m, RevisionAuthor am) = Open(body);
        am.MoveParagraph(Paragraph(m, 0), Paragraph(m, 2));
        AssertRoundTrip(m, "first\nsecond\nthird\n", "second\nthird\nfirst\n");
    }

    [Fact]
    public void InsertParagraphAtTheEndOfACellRejectsCleanly()
    {
        (RevisionSet set, RevisionAuthor au) = Open("<w:tbl><w:tblPr/><w:tblGrid/><w:tr><w:tc><w:p>" + R("only") + "</w:p></w:tc></w:tr></w:tbl>");
        au.InsertParagraph(Paragraph(set, 0), "more");
        AssertRoundTrip(set, "only\n", "only\nmore\n");
    }

    [Fact]
    public void FormatTextSplitsRunsAndRecordsOldProperties()
    {
        (RevisionSet set, RevisionAuthor au) = Open();
        au.FormatText(Paragraph(set, 0), 2, 6, "<w:i/><w:b w:val=\"0\"/>");
        Assert.Equal(2, set.Revisions.Count(x => x.Kind == RevisionKind.RunPropertiesChange));
        Assert.Equal("Hello brave new world\n", Text(set, RevisionView.Accepted));
        RevisionSet r = Reparse(Xml(set));
        r.RejectAll();
        Assert.Equal("Hello brave new world\n", Text(r, RevisionView.Accepted));
        Assert.Contains("<w:rPr><w:b/></w:rPr>", Xml(r));
        Assert.DoesNotContain("<w:i/>", Xml(r));
        RevisionSet a = Reparse(Xml(set));
        a.AcceptAll();
        Assert.Contains("<w:i/>", Xml(a));
        Assert.DoesNotContain("Change", Xml(a));
    }

    [Fact]
    public void PropertyChangesKeepTheFirstRecordedOriginal()
    {
        (RevisionSet set, RevisionAuthor au) = Open("<w:p><w:pPr><w:jc w:val=\"left\"/><w:rPr><w:i/></w:rPr></w:pPr>" + R("x") + "</w:p>");
        au.ChangeParagraphProperties(Paragraph(set, 0), "<w:jc w:val=\"center\"/>");
        au.ChangeParagraphProperties(Paragraph(set, 0), "<w:jc w:val=\"right\"/>");
        Assert.Single(set.Revisions);
        set.RejectAll();
        Assert.Equal("<w:p><w:pPr><w:jc w:val=\"left\"/><w:rPr><w:i/></w:rPr></w:pPr><w:r><w:t xml:space=\"preserve\">x</w:t></w:r></w:p>", Paragraph(set, 0).OuterXml);
    }

    [Fact]
    public void TableAuthoring()
    {
        string body = "<w:tbl><w:tblPr><w:jc w:val=\"left\"/></w:tblPr><w:tblGrid><w:gridCol w:w=\"1\"/></w:tblGrid><w:tr><w:tc><w:tcPr><w:tcW w:w=\"5\"/></w:tcPr><w:p>" + R("a") + "</w:p></w:tc></w:tr><w:tr><w:tc><w:p>" + R("b") + "</w:p></w:tc></w:tr></w:tbl>";
        (RevisionSet set, RevisionAuthor au) = Open(body);
        MarkupNode table = Find(set, "tbl");
        au.ChangeTableProperties(table, "<w:jc w:val=\"center\"/>");
        au.ChangeTableGrid(table, "<w:gridCol w:w=\"9\"/>");
        MarkupNode row1 = Find(set, "tr");
        au.ChangeTableRowProperties(row1, "<w:cantSplit/>");
        au.ChangeTableCellProperties(Find(set, "tc"), "<w:tcW w:w=\"7\"/>");
        au.MarkRowDeleted(NextRow(row1));
        Assert.Equal(7, set.Revisions.Count);
        AssertRoundTrip(set, "a\nb\n", "a\n");
        RevisionSet r = Reparse(Xml(set));
        r.RejectAll();
        string x = Xml(r);
        Assert.Contains("<w:tblPr><w:jc w:val=\"left\"/></w:tblPr>", x);
        Assert.Contains("<w:gridCol w:w=\"1\"/>", x);
        Assert.Contains("<w:tcPr><w:tcW w:w=\"5\"/></w:tcPr>", x);
        Assert.DoesNotContain("cantSplit", x);
    }

    private static MarkupNode NextRow(MarkupNode row) => Wml(row.NextSibling);

    private static MarkupNode Wml(MarkupNode n)
    {
        while (!n.IsNull && n.Kind != MarkupNodeKind.Element)
        {
            n = n.NextSibling;
        }

        return n;
    }

    private static MarkupNode Find(RevisionSet set, string local)
    {
        MarkupNode root = set.Parts[0].Markup.Root;
        for (MarkupNode n = root; !n.IsNull;)
        {
            if (n.Kind == MarkupNodeKind.Element && n.LocalNameString == local)
            {
                return n;
            }

            if (!n.FirstChild.IsNull)
            {
                n = n.FirstChild;
                continue;
            }

            MarkupNode c = n;
            n = default;
            for (; !c.IsNull && c != root; c = c.Parent)
            {
                if (!c.NextSibling.IsNull)
                {
                    n = c.NextSibling;
                    break;
                }
            }
        }

        throw new InvalidOperationException(local);
    }

    [Fact]
    public void CellMergeAndCellMarks()
    {
        (RevisionSet set, RevisionAuthor au) = Open("<w:tbl><w:tblPr/><w:tblGrid/><w:tr><w:tc><w:p>" + R("a") + "</w:p></w:tc><w:tc><w:p>" + R("b") + "</w:p></w:tc></w:tr></w:tbl>");
        MarkupNode first = Find(set, "tc");
        au.MergeCells(first, "restart", null);
        Assert.Equal(RevisionKind.CellMerged, set.Revisions[0].Kind);
        Assert.Contains("<w:vMerge w:val=\"restart\"/>", Xml(set));
        RevisionSet r = Reparse(Xml(set));
        r.RejectAll();
        Assert.DoesNotContain("vMerge", Xml(r));
        Assert.DoesNotContain("tcPr", Xml(r));
        (RevisionSet s2, RevisionAuthor a2) = Open("<w:tbl><w:tblPr/><w:tblGrid/><w:tr><w:tc><w:p>" + R("a") + "</w:p></w:tc><w:tc><w:p>" + R("b") + "</w:p></w:tc></w:tr></w:tbl>");
        a2.MarkCellDeleted(Find(s2, "tc"));
        AssertRoundTrip(s2, "a\nb\n", "b\n");
        (RevisionSet s3, RevisionAuthor a3) = Open("<w:tbl><w:tblPr/><w:tblGrid/><w:tr><w:tc><w:p>" + R("a") + "</w:p></w:tc><w:tc><w:p>" + R("b") + "</w:p></w:tc></w:tr></w:tbl>");
        a3.MarkCellInserted(Find(s3, "tc"));
        AssertRoundTrip(s3, "b\n", "a\nb\n");
        (RevisionSet s4, RevisionAuthor a4) = Open("<w:tbl><w:tblPr/><w:tblGrid/><w:tr><w:tc><w:p>" + R("a") + "</w:p></w:tc></w:tr></w:tbl><w:p>" + R("z") + "</w:p>");
        a4.MarkRowInserted(Find(s4, "tr"));
        AssertRoundTrip(s4, "z\n", "a\nz\n");
    }

    [Fact]
    public void AuthoringIsDeterministic()
    {
        string Run()
        {
            (RevisionSet set, RevisionAuthor au) = Open();
            au.InsertText(Paragraph(set, 0), 5, "!");
            au.DeleteText(Paragraph(set, 0), 8, 3);
            au.FormatText(Paragraph(set, 0), 0, 2, "<w:u w:val=\"single\"/>");
            return Xml(set);
        }

        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void NodesFromOtherDocumentsAndWrongElementsAreRefused()
    {
        (RevisionSet set, RevisionAuthor au) = Open();
        RevisionSet other = Set("<w:p>" + R("x") + "</w:p>");
        Assert.Throws<ArgumentException>(() => au.InsertText(Paragraph(other, 0), 0, "a"));
        Assert.Throws<ArgumentException>(() => au.InsertText(Find(set, "r"), 0, "a"));
        Assert.Throws<ArgumentOutOfRangeException>(() => au.InsertText(Paragraph(set, 0), 99, "a"));
        Assert.Throws<ArgumentOutOfRangeException>(() => au.DeleteText(Paragraph(set, 0), 20, 5));
        Assert.Throws<ArgumentException>(() => au.InsertText(Paragraph(set, 0), 0, "bad\u0001"));
        Assert.Throws<ArgumentException>(() => au.ChangeParagraphProperties(Paragraph(set, 0), "<w:jc"));
        Assert.False(set.Parts[0].Markup.IsModified);
    }

    [Fact]
    public void RandomEditsRoundTripAgainstAStringModel()
    {
        for (int seed = 1; seed <= 40; seed++)
        {
            var rng = new Random(seed);
            const string original = "The quick brown fox jumps over the lazy dog";
            (RevisionSet set, RevisionAuthor first) = Open("<w:p>" + R("The quick ") + "<w:r><w:rPr><w:i/></w:rPr><w:t>brown fox</w:t></w:r>" + R(" jumps over the lazy dog") + "</w:p>");
            RevisionAuthor second = set.CreateAuthor("Other", When.AddDays(1));
            string model = original;
            for (int step = 0; step < 12; step++)
            {
                RevisionAuthor au = rng.Next(2) == 0 ? first : second;
                MarkupNode p = Paragraph(set, 0);
                int pos = rng.Next(model.Length + 1);
                switch (rng.Next(3))
                {
                    case 0:
                        {
                            string t = "<" + step + ">";
                            au.InsertText(p, pos, t);
                            model = model.Insert(pos, t);
                            break;
                        }

                    case 1:
                        {
                            int len = Math.Min(rng.Next(1, 9), model.Length - pos);
                            au.DeleteText(p, pos, len);
                            model = model.Remove(pos, len);
                            break;
                        }

                    default:
                        {
                            int len = Math.Min(rng.Next(1, 6), model.Length - pos);
                            string t = "[" + step + "]";
                            au.ReplaceText(p, pos, len, t);
                            model = model.Remove(pos, len).Insert(pos, t);
                            break;
                        }
                }

                Assert.True(model + "\n" == Text(set, RevisionView.Accepted), $"seed {seed} step {step}");
            }

            string xml = Xml(set);
            Assert.Equal(original + "\n", Text(set, RevisionView.Rejected));
            RevisionSet a = Reparse(xml);
            a.AcceptAll();
            Assert.Equal(model + "\n", Text(a, RevisionView.Accepted));
            Assert.Empty(a.Revisions);
            RevisionSet r = Reparse(xml);
            r.RejectAll();
            Assert.Equal(original + "\n", Text(r, RevisionView.Accepted));
            Assert.Empty(r.Revisions);
        }
    }
}

internal static class RevisionTestExtensions
{
    internal static RevisionSet Tap(this RevisionSet set, Action<RevisionSet> act)
    {
        act(set);
        return set;
    }

    internal static string Text(this RevisionSet set) => RevisionSet.GetText(set.Parts[0], RevisionView.Accepted);
}
