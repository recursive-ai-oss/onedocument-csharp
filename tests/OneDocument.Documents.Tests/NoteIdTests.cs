using System.Linq;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Documents.Tests;

/// <summary>New notes and comments never take an id that is already in use somewhere in the document.</summary>
public class NoteIdTests
{
    private const string W = TestDocs.WNs;
    private const string Mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    private static InMemoryPackage Package(string body, string footnotes, string? settings = null)
    {
        InMemoryPackage p = TestDocs.Package(body);
        PartHelper.Add(p, "/word/footnotes.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.footnotes+xml", PartHelper.Rel + "footnotes", footnotes);
        if (settings is not null)
        {
            PartHelper.Add(p, "/word/settings.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml", PartHelper.Rel + "settings", settings);
        }

        return p;
    }

    private static string Note(string id, string type = "") =>
        $"<w:footnote w:id=\"{id}\"{type}><w:p><w:r><w:t>n{id}</w:t></w:r></w:p></w:footnote>";

    [Fact]
    public void SeparatorsOnlyWithZeroAndOneStillGiveTwo()
    {
        using var d = Document.FromPackage(Package("<w:p/>",
            $"<w:footnotes xmlns:w=\"{W}\">{Note("0", " w:type=\"separator\"")}{Note("1", " w:type=\"continuationSeparator\"")}</w:footnotes>"));
        Assert.Equal(2, d.Footnotes.Add("a").Id);
        Assert.Equal(3, d.Footnotes.Add("b").Id);
    }

    [Fact]
    public void IdsInAlternateContentBranchesAndDanglingReferencesAndSettingsAreTaken()
    {
        string body = "<w:p><w:r><w:footnoteReference w:id=\"9\"/></w:r></w:p>";
        string footnotes = $"<w:footnotes xmlns:w=\"{W}\" xmlns:mc=\"{Mc}\">{Note("-1", " w:type=\"separator\"")}{Note("0", " w:type=\"continuationSeparator\"")}" +
            $"<mc:AlternateContent><mc:Choice Requires=\"w\">{Note("14")}</mc:Choice><mc:Fallback>{Note("14")}</mc:Fallback></mc:AlternateContent></w:footnotes>";
        string settings = $"<w:settings xmlns:w=\"{W}\"><w:footnotePr><w:footnote w:id=\"-1\"/><w:footnote w:id=\"20\"/></w:footnotePr></w:settings>";
        using var d = Document.FromPackage(Package(body, footnotes, settings));
        Assert.Equal(21, d.Footnotes.Add("x").Id);
        Assert.Equal(22, d.Footnotes.Add("y").Id);
        Assert.Equal(d.Footnotes.Select(n => n.Id).Distinct().Count(), d.Footnotes.Count(n => n.Id >= 21) + d.Footnotes.Count(n => n.Id < 21));
    }

    [Fact]
    public void AlternateContentBranchAloneRaisesTheNextId()
    {
        string footnotes = $"<w:footnotes xmlns:w=\"{W}\" xmlns:mc=\"{Mc}\">{Note("1")}<mc:AlternateContent><mc:Choice Requires=\"w\">{Note("7")}</mc:Choice></mc:AlternateContent></w:footnotes>";
        using var d = Document.FromPackage(Package("<w:p/>", footnotes));
        Assert.Equal(8, d.Footnotes.Add("x").Id);
    }

    [Fact]
    public void CommentsSkipIdsOfDanglingRangesAndReferences()
    {
        string body = "<w:p><w:commentRangeStart w:id=\"7\"/><w:r><w:t>t</w:t></w:r><w:commentRangeEnd w:id=\"7\"/></w:p><w:p><w:r><w:commentReference w:id=\"3\"/></w:r></w:p>";
        using var d = Document.FromPackage(TestDocs.Package(body));
        Run run = d.Body.Paragraphs().First().Runs().First();
        Assert.Equal(8, d.Comments.Add(run, run, "Ann", "one").Id);
        Assert.Equal(9, d.Comments.Add(run, run, "Ann", "two").Id);
    }
}
