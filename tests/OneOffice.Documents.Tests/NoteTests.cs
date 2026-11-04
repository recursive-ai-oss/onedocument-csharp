using System.Text;
using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.Documents.Notes;
using RecursiveAI.OneOffice.Packaging;
using RecursiveAI.OneOffice.Schema;

namespace RecursiveAI.OneOffice.Documents.Tests;

internal static class PartHelper
{
    public const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";

    public static void Add(InMemoryPackage p, string name, string contentType, string relationshipType, string xml)
    {
        p.AddPart(name, contentType, Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n" + xml));
        var rels = new List<Relationship>(p.GetRelationships("/word/document.xml"));
        rels.Add(new Relationship("rIdX" + rels.Count, relationshipType, name["/word/".Length..]));
        p.SetRelationships("/word/document.xml", rels);
    }

    public static string Text(InMemoryPackage p, string name) => Encoding.UTF8.GetString(p.FindPart(name)!.GetBytes().Span);
}

public class NoteTests
{
    private const string W = TestDocs.WNs;

    private static string Footnotes(string extra = "") =>
        $"<w:footnotes xmlns:w=\"{W}\"><w:footnote w:type=\"separator\" w:id=\"-1\"><w:p><w:r><w:separator/></w:r></w:p></w:footnote>" +
        "<w:footnote w:type=\"continuationSeparator\" w:id=\"0\"><w:p><w:r><w:continuationSeparator/></w:r></w:p></w:footnote>" +
        "<w:footnote w:id=\"1\"><w:p><w:r><w:footnoteRef/></w:r><w:r><w:t xml:space=\"preserve\"> First note.</w:t></w:r></w:p></w:footnote>" + extra + "</w:footnotes>";

    private static InMemoryPackage WithFootnotes(string body, string? footnotes = null)
    {
        InMemoryPackage p = TestDocs.Package(body);
        if (footnotes is not null)
        {
            PartHelper.Add(p, "/word/footnotes.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.footnotes+xml", PartHelper.Rel + "footnotes", footnotes);
        }

        return p;
    }

    [Fact]
    public void Reads_NotesTypesAndReferences()
    {
        InMemoryPackage p = WithFootnotes("<w:p><w:r><w:t>Text</w:t></w:r><w:r><w:rPr><w:vertAlign w:val=\"superscript\"/></w:rPr><w:footnoteReference w:id=\"1\"/></w:r></w:p>", Footnotes());
        using var d = Document.FromPackage(p);
        Assert.Equal(3, d.Footnotes.Count);
        Assert.Equal([NoteType.Separator, NoteType.ContinuationSeparator, NoteType.Normal], d.Footnotes.Select(n => n.Type).ToArray());
        Note note = Assert.Single(d.Footnotes.Normal());
        Assert.Equal(1, note.Id);
        Assert.Equal(" First note.", note.Text);
        NoteReference r = Assert.Single(d.NoteReferences(NoteKind.Footnote));
        Assert.Same(note, r.Note is null ? null : d.Footnotes.Find(1));
        Assert.Equal(1, r.Id);
        Assert.NotNull(r.Run);
        Assert.Single(note.References());
        Assert.Empty(d.Endnotes);
    }

    [Fact]
    public void MissingAndDuplicateNotes_AreReported()
    {
        var defects = new DefectCollection();
        InMemoryPackage p = WithFootnotes("<w:p><w:r><w:footnoteReference w:id=\"9\"/></w:r></w:p>", Footnotes("<w:footnote w:id=\"1\"><w:p/></w:footnote>"));
        using var d = Document.FromPackage(p, new OpenContext(defects: defects));
        _ = d.NoteReferences(NoteKind.Footnote);
        Assert.Contains(defects, x => x.Code == "docx.note-missing" && x.Location.Offset > 0);
        Assert.Contains(defects, x => x.Code == "docx.note-duplicate-id");
        Assert.Equal(" First note.", d.Footnotes.Find(1)!.Text);
    }

    [Fact]
    public void Add_CreatesThePartWithSeparators_AndReferencesTheNote()
    {
        InMemoryPackage p = TestDocs.Package("<w:p><w:r><w:t>Body</w:t></w:r></w:p>", "<w:style w:type=\"character\" w:styleId=\"FootnoteReference\"/>");
        PartHelper.Add(p, "/word/settings.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml", PartHelper.Rel + "settings", $"<w:settings xmlns:w=\"{W}\"><w:zoom w:percent=\"100\"/><w:compat/></w:settings>");
        byte[] styles = p.FindPart("/word/styles.xml")!.GetBytes().ToArray();
        using var d = Document.FromPackage(p);
        Note note = d.Footnotes.Add("Hello & goodbye");
        Assert.Equal(1, note.Id);
        NoteReference r = note.InsertReference(d.Body.Paragraphs().Single().Runs().Single());
        Assert.Equal(1, r.Id);
        d.CommitChanges();

        Assert.Equal(styles, p.FindPart("/word/styles.xml")!.GetBytes().ToArray());
        string doc = PartHelper.Text(p, "/word/document.xml");
        Assert.Contains("<w:r><w:rPr><w:rStyle w:val=\"FootnoteReference\"/></w:rPr><w:footnoteReference w:id=\"1\"/></w:r>", doc);
        string notes = PartHelper.Text(p, "/word/footnotes.xml");
        Assert.Contains("w:type=\"separator\" w:id=\"-1\"", notes);
        Assert.Contains("w:type=\"continuationSeparator\" w:id=\"0\"", notes);
        Assert.Contains("Hello &amp; goodbye", notes);
        Assert.Contains("<w:rStyle w:val=\"FootnoteReference\"/>", notes);
        string settings = PartHelper.Text(p, "/word/settings.xml");
        Assert.Contains("<w:zoom w:percent=\"100\"/><w:footnotePr><w:footnote w:id=\"-1\"/><w:footnote w:id=\"0\"/></w:footnotePr><w:compat/>", settings);
        Assert.Contains(p.GetRelationships("/word/document.xml"), x => x.Type.EndsWith("/footnotes", StringComparison.Ordinal));

        using var again = Document.FromPackage(p);
        Assert.Equal("Hello & goodbye".Insert(0, " "), again.Footnotes.Find(1)!.Text);
        Assert.Equal(2, again.Footnotes.Add("second").Id);
    }

    [Fact]
    public void Remove_DropsTheReferenceAndTheNote_AndKeepsSeparators()
    {
        InMemoryPackage p = WithFootnotes("<w:p><w:r><w:t>a</w:t></w:r><w:r><w:footnoteReference w:id=\"1\"/></w:r></w:p>", Footnotes());
        using var d = Document.FromPackage(p);
        Note note = d.Footnotes.Find(1)!;
        Assert.True(d.Footnotes.Remove(note));
        Assert.Equal(2, d.Footnotes.Count);
        Assert.Empty(d.NoteReferences(NoteKind.Footnote));
        Assert.Equal("a", d.Body.Paragraphs().Single().Text);
        Assert.Throws<InvalidOperationException>(() => d.Footnotes.Remove(d.Footnotes[0]));
        Assert.Throws<InvalidOperationException>(() => d.Footnotes[0].InsertReference(d.Body.Paragraphs().Single().Runs().First()));
    }

    [Fact]
    public void SetText_KeepsTheNumberMark()
    {
        InMemoryPackage p = WithFootnotes("<w:p/>", Footnotes());
        using var d = Document.FromPackage(p);
        Note note = d.Footnotes.Find(1)!;
        note.SetText(" Changed\nlines");
        Assert.Equal(" Changed\nlines", note.Text);
        Assert.Contains("<w:footnoteRef/>", note.Container.Node.OuterXml);
    }

    [Fact]
    public void Numbering_SectionAndSettings_InSchemaOrder()
    {
        InMemoryPackage p = TestDocs.Package("<w:p/><w:sectPr><w:pgSz w:w=\"100\"/></w:sectPr>");
        PartHelper.Add(p, "/word/settings.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml", PartHelper.Rel + "settings", $"<w:settings xmlns:w=\"{W}\"><w:compat/></w:settings>");
        using var d = Document.FromPackage(p);
        MarkupRef sect = d.Body.SectionProperties!.Value;
        NoteNumbering section = d.GetNoteNumbering(sect, NoteKind.Footnote);
        Assert.Null(section.Start);
        section.Restart = NoteRestart.EachPage;
        section.NumberFormat = "lowerRoman";
        section.Start = 3;
        section.Position = NotePosition.BeneathText;
        Assert.Contains("<w:footnotePr><w:pos w:val=\"beneathText\"/><w:numFmt w:val=\"lowerRoman\"/><w:numStart w:val=\"3\"/><w:numRestart w:val=\"eachPage\"/></w:footnotePr><w:pgSz", sect.Node.OuterXml);
        d.GetNoteNumbering(sect, NoteKind.Endnote).Position = NotePosition.SectionEnd;
        Assert.Contains("</w:footnotePr><w:endnotePr><w:pos w:val=\"sectEnd\"/></w:endnotePr><w:pgSz", sect.Node.OuterXml);

        NoteNumbering doc = d.GetNoteNumbering(NoteKind.Footnote);
        doc.Start = 5;
        d.CommitChanges();
        Assert.Contains("<w:footnotePr><w:numStart w:val=\"5\"/></w:footnotePr><w:compat/>", PartHelper.Text(p, "/word/settings.xml"));
        EffectiveNoteNumbering e = NoteNumbering.Combine(section, doc);
        Assert.Equal(new EffectiveNoteNumbering(NotePosition.BeneathText, "lowerRoman", 3, NoteRestart.EachPage), e);
        Assert.Equal(new EffectiveNoteNumbering(NotePosition.PageBottom, "decimal", 5, NoteRestart.Continuous), NoteNumbering.Combine(null, doc));
        section.Start = null;
        Assert.Null(section.Start);
    }

}
