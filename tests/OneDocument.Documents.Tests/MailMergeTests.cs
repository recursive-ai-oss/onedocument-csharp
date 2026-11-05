using System.Data;
using System.Text.Json;
using RecursiveAI.OneDocument.Documents.MailMerge;
using RecursiveAI.OneDocument.Templating;
using static RecursiveAI.OneDocument.Documents.Tests.MailMergeCases;

namespace RecursiveAI.OneDocument.Documents.Tests;

public sealed class MailMergeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mm-" + Guid.NewGuid().ToString("N"));

    public MailMergeTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    private Document Template(string body, string? header = null, bool settings = false)
    {
        string path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".docx");
        Write(new Case("t", body, "[]", Header: header, Settings: settings), path);
        return Document.Open(path);
    }

    private static string[] Texts(MailMergeResult r) => [.. r.Documents.Select(d => string.Join("|", d.Body.Paragraphs().Select(p => p.Text)))];

    private static readonly List<Dictionary<string, object?>> Three = [.. new[] { "a", "b", "c" }.Select(x => new Dictionary<string, object?> { ["First"] = x })];

    private static readonly string OneField = P(Mf("First") + Run(" ") + Mf("Last"));

    [Fact]
    public void DataTable_IsOneRecordPerRow()
    {
        var table = new DataTable();
        table.Columns.Add("First");
        table.Columns.Add("Last");
        table.Rows.Add("Ada", "Lovelace");
        table.Rows.Add("Grace", DBNull.Value);
        using Document t = Template(OneField);
        using MailMergeResult r = t.MailMerge(table);
        Assert.Equal(["Ada Lovelace", "Grace "], Texts(r));
    }

    [Fact]
    public void DataReader_IsReadToTheEnd()
    {
        var table = new DataTable();
        table.Columns.Add("First");
        table.Columns.Add("Last");
        table.Rows.Add("A", "B");
        table.Rows.Add("C", "D");
        using Document t = Template(OneField);
        using MailMergeResult r = t.MailMerge(table.CreateDataReader());
        Assert.Equal(["A B", "C D"], Texts(r));
    }

    [Fact]
    public void ListOfDictionaries_AndASingleRecord()
    {
        using Document t = Template(OneField);
        using MailMergeResult many = t.MailMerge(new List<Dictionary<string, object?>> { new() { ["first"] = "x", ["LAST"] = 7 }, new() { ["First"] = "y" } });
        Assert.Equal(["x 7", "y "], Texts(many));
        using MailMergeResult one = t.MailMerge(new Dictionary<string, object?> { ["First"] = "solo", ["Last"] = "act" });
        Assert.Equal(["solo act"], Texts(one));
    }

    [Fact]
    public void NoRecords_GiveNoDocuments_EvenMergedIntoOne()
    {
        using Document t = Template(OneField);
        Assert.Empty(t.MailMerge(Array.Empty<object>()).Documents);
        Assert.Empty(t.MailMerge(null, new MailMergeOptions { Mode = MailMergeMode.SingleDocument }).Documents);
    }

    [Fact]
    public void TooManyRecords_ThrowsTheLimit()
    {
        using Document t = Template(OneField);
        Assert.Throws<OfficeLimitException>(() => t.MailMerge(Three, new MailMergeOptions { MaxRecords = 2 }));
    }

    [Fact]
    public void TheTemplateIsNotChanged()
    {
        using Document t = Template(OneField);
        string before = t.Body.Container.Node.OuterXml;
        using MailMergeResult r = t.MailMerge(new[] { new Dictionary<string, object?> { ["First"] = "A", ["Last"] = "B" } });
        Assert.Equal(before, t.Body.Container.Node.OuterXml);
        Assert.False(t.HasUncommittedChanges);
    }

    [Fact]
    public void MailMergeSettings_AndTheirConnection_AreRemoved()
    {
        using Document t = Template(OneField, settings: true);
        using MailMergeResult r = t.MailMerge(new[] { new Dictionary<string, object?> { ["First"] = "A", ["Last"] = "B" } });
        Document d = Assert.Single(r.Documents);
        d.CommitChanges();
        string settings = System.Text.Encoding.UTF8.GetString(d.Package.FindPart("/word/settings.xml")!.GetBytes().ToArray());
        Assert.DoesNotContain("mailMerge", settings, StringComparison.Ordinal);
        Assert.Contains("defaultTabStop", settings, StringComparison.Ordinal);
        Assert.DoesNotContain(d.Package.GetRelationships("/word/settings.xml"), x => x.Type.EndsWith("mailMergeSource", StringComparison.Ordinal));
        // The template still has its connection: nothing was opened or removed there.
        Assert.Contains(t.Package.GetRelationships("/word/settings.xml"), x => x.Type.EndsWith("mailMergeSource", StringComparison.Ordinal));
    }

    [Fact]
    public void Cancellation_StopsBetweenPasses()
    {
        using Document t = Template(OneField);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => t.MailMerge(new[] { new Dictionary<string, object?> { ["First"] = "A" } }, null, cts.Token));
    }

    [Fact]
    public void MissingField_IsAWarning_NotAnError()
    {
        using Document t = Template(P(Mf("Nope") + Run("!")));
        using MailMergeResult r = t.MailMerge(new[] { new Dictionary<string, object?> { ["First"] = "A" } });
        Assert.Equal(["!"], Texts(r));
        Assert.Equal("mailmerge.field-missing", Assert.Single(r.Warnings).Code);
    }

    [Fact]
    public void MalformedIf_IsReported()
    {
        using Document t = Template(P(Cf("x", string.Empty, " IF 1 = ")));
        using MailMergeResult r = t.MailMerge(new[] { new Dictionary<string, object?> { ["First"] = "A" } });
        Assert.Equal("mailmerge.field-malformed", Assert.Single(r.Warnings).Code);
        Assert.Equal([""], Texts(r));
    }

    [Fact]
    public void UnclosedField_IsLeftAndReported()
    {
        using Document t = Template("<w:p><w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText> MERGEFIELD First </w:instrText></w:r><w:r><w:t>tail</w:t></w:r></w:p>");
        using MailMergeResult r = t.MailMerge(new[] { new Dictionary<string, object?> { ["First"] = "A" } });
        Assert.Contains(r.Warnings, w => w.Code == "mailmerge.field-unbalanced");
        Assert.Single(r.Documents);
    }

    [Fact]
    public void UnsupportedFieldTypes_StayAndAreReported()
    {
        using Document t = Template(P(Cf("«AddressBlock»", string.Empty, " ADDRESSBLOCK ")));
        using MailMergeResult r = t.MailMerge(new[] { new Dictionary<string, object?> { ["First"] = "A" } });
        Assert.Equal("mailmerge.field-unsupported", Assert.Single(r.Warnings).Code);
        Assert.Single(r.Documents[0].Fields());
    }

    [Fact]
    public void CurlyQuotes_AndSpacesInValues_Compare()
    {
        using Document t = Template(P(Cf("no", string.Empty, " IF ", Mf("City"), " = \"New York\" \"yes\" \"no\" ")));
        using MailMergeResult r = t.MailMerge(new[] { new Dictionary<string, object?> { ["City"] = "New York" }, new Dictionary<string, object?> { ["City"] = "Paris" } });
        Assert.Equal(["yes", "no"], Texts(r));
    }

    [Fact]
    public void EmptyValue_InAnIf_StillCompares()
    {
        using Document t = Template(P(Cf("x", string.Empty, " IF ", Mf("Note"), " = \"\" \"blank\" \"set\" ")));
        using MailMergeResult r = t.MailMerge(new[] { new Dictionary<string, object?> { ["Note"] = "" }, new Dictionary<string, object?> { ["Note"] = "z" } });
        Assert.Equal(["blank", "set"], Texts(r));
    }

    private string One(string body, object? v)
    {
        using Document t = Template(P(body));
        using MailMergeResult r = t.MailMerge(new[] { new Dictionary<string, object?> { ["v"] = v } });
        return Texts(r)[0];
    }

    [Theory]
    [InlineData("a", "=", "A", true)]
    [InlineData("a", "<>", "A", false)]
    [InlineData("10", ">", "9", true)]
    [InlineData("10", "<", "9", false)]
    [InlineData("abc", "<", "abd", true)]
    [InlineData("hello", "=", "h*o", true)]
    [InlineData("hello", "=", "h?llo", true)]
    [InlineData("hello", "=", "h?lo", false)]
    [InlineData("hello", "<>", "x*", true)]
    [InlineData("1,000", "=", "1000", true)]
    [InlineData("2", ">=", "2.0", true)]
    public void IfCompare_FollowsTheFieldRules(string left, string op, string right, bool expected) =>
        Assert.Equal(expected ? "T" : "F", One(Cf("x", string.Empty, " IF ", Mf("v"), " " + op.Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal) + " \"" + right + "\" \"T\" \"F\" "), left));

    [Theory]
    [InlineData("MMMM d, yyyy", "March 9, 2024")]
    [InlineData("dddd, dd MMM yy", "Saturday, 09 Mar 24")]
    [InlineData("h:mm am/pm", "3:07 PM")]
    [InlineData("HH:mm:ss", "15:07:05")]
    [InlineData("M/d/yyyy 'at' H:mm", "3/9/2024 at 15:07")]
    [InlineData("hh A/P", "03 P")]
    public void DatePictures_UseWordsLetters(string picture, string expected) =>
        Assert.Equal(expected, One(Mf("v", "\\@ \"" + picture + "\""), new DateTime(2024, 3, 9, 15, 7, 5)));

    [Theory]
    [InlineData(1234.5, "#,##0.00", "1,234.50")]
    [InlineData(-1234.5, "$#,##0.00;($#,##0.00)", "($1,234.50)")]
    [InlineData(7, "000", "007")]
    [InlineData(0.256, "0.0%", "0.3%")]
    [InlineData(15, "$###", "$ 15")]
    [InlineData(222492, "x##", "492")]
    [InlineData(0.125, "0.00x", "0.125")]
    [InlineData(0.75, ".x", ".8")]
    [InlineData(-80, "-##", "-80")]
    [InlineData(10, "+##", "+10")]
    [InlineData(-10, "+##", "-10")]
    [InlineData(33, "##%", "33%")]
    [InlineData(0, "$#,##0.00;($#,##0.00);'none'", "none")]
    [InlineData(-5, "0", "-5")]
    [InlineData(3, "'No.' 0", "No. 3")]
    public void NumericPictures_FormatLikeACell(double value, string picture, string expected) =>
        Assert.Equal(expected, One(Mf("v", "\\# \"" + picture + "\""), value));

    [Fact]
    public void SingleDocument_ShiftsIdsAndRenamesBookmarks()
    {
        using Document t = Template(P("<w:bookmarkStart w:id=\"0\" w:name=\"top\"/>" + Mf("First") + "<w:bookmarkEnd w:id=\"0\"/>"));
        using MailMergeResult r = t.MailMerge(new[] { new Dictionary<string, object?> { ["First"] = "A" }, new Dictionary<string, object?> { ["First"] = "B" }, new Dictionary<string, object?> { ["First"] = "C" } },
            new MailMergeOptions { Mode = MailMergeMode.SingleDocument });
        Document d = Assert.Single(r.Documents);
        string xml = d.Body.Container.Node.OuterXml;
        Assert.Contains("w:name=\"top\"", xml, StringComparison.Ordinal);
        Assert.Contains("w:name=\"top_1\"", xml, StringComparison.Ordinal);
        Assert.Contains("w:name=\"top_2\"", xml, StringComparison.Ordinal);
        Assert.Contains("w:id=\"20000\"", xml, StringComparison.Ordinal);
        Assert.Equal(3, d.Sections.Count);
    }

    [Fact]
    public void SingleDocument_IsTheOnlyDocument_AndPerRecordCountsPasses()
    {
        using Document t = Template(OneField);
        var data = JsonDocument.Parse("""[{"First":"1","Last":"a"},{"First":"2","Last":"b"}]""");
        using MailMergeResult one = t.MailMerge(TemplateData.FromJson(data), new MailMergeOptions { Mode = MailMergeMode.SingleDocument });
        Assert.Single(one.Documents);
        using MailMergeResult each = t.MailMerge(TemplateData.FromJson(data));
        Assert.Equal(2, each.Documents.Count);
    }
}
