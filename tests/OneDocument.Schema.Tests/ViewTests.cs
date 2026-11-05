using RecursiveAI.OneDocument.Schema.Wordprocessing;

namespace RecursiveAI.OneDocument.Schema.Tests;

public sealed class ViewTests
{
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string WStrict = "http://purl.oclc.org/ooxml/wordprocessingml/main";

    private static (TextTree Tree, Paragraph P) Para(string xml)
    {
        var tree = TextTree.Parse(xml);
        var body = tree.RootRef.FindChild(SchemaNamespace.Wordprocessing, "body"u8)!.Value;
        var p = body.FindChild(SchemaNamespace.Wordprocessing, "p"u8)!.Value;
        return (tree, Paragraph.Create(p));
    }

    private static string Doc(string body, string ns = W, string extra = "")
        => $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<w:document xmlns:w=\"{ns}\"{extra}>\n  <w:body>{body}</w:body>\n</w:document>";

    [Fact]
    public void ReadingDoesNotChangeTheSerialization()
    {
        var xml = Doc("<w:p w:rsidR=\"00A1\"><w:pPr><w:jc w:val=\"left\"/></w:pPr><w:r><w:t xml:space=\"preserve\"> hi </w:t></w:r></w:p>");
        var (tree, p) = Para(xml);
        Assert.Equal("00A1", p.RsidR);
        Assert.Equal(JustificationValue.Left, p.ParagraphProperties!.Value.Justification!.Value.Val);
        Assert.Equal(xml, tree.Serialize());
    }

    [Fact]
    public void SettingJcValueChangesOnlyThatAttribute()
    {
        var xml = Doc("<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/><w:jc w:val=\"left\"/></w:pPr><w:r><w:t>x</w:t></w:r></w:p>");
        var (tree, p) = Para(xml);
        var jc = p.ParagraphProperties!.Value.Justification!.Value;
        jc.Val = JustificationValue.Center;
        Assert.Equal(xml.Replace("w:val=\"left\"", "w:val=\"center\"", StringComparison.Ordinal), tree.Serialize());
    }

    [Fact]
    public void AddedChildrenGoWhereTheSchemaSequencePutsThem()
    {
        var (tree, p) = Para(Doc("<w:p><w:pPr><w:pStyle w:val=\"A\"/><w:spacing w:after=\"10\"/></w:pPr></w:p>"));
        var ppr = p.ParagraphProperties!.Value;
        ppr.GetOrAddKeepNext().Val = true;      // after pStyle, before spacing
        ppr.GetOrAddJustification().Val = JustificationValue.Both; // after spacing
        var xml = tree.Serialize();
        Assert.Contains("<w:pStyle w:val=\"A\"/><w:keepNext w:val=\"true\"/><w:spacing w:after=\"10\"/><w:jc w:val=\"both\"/></w:pPr>", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void ParagraphChildrenEnumerateByNameAndKeepUnknownContent()
    {
        var xml = Doc(
            "<w:p w14:paraId=\"1A2B3C4D\"><w:r><w:t>a</w:t></w:r><mc:AlternateContent><mc:Choice Requires=\"w14\"/></mc:AlternateContent><w:r><w:t>b</w:t></w:r><w:bookmarkStart w:id=\"0\" w:name=\"n\"/></w:p>",
            extra: " xmlns:w14=\"http://schemas.microsoft.com/office/word/2010/wordml\" xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\"");
        var (tree, p) = Para(xml);
        Assert.Equal(2, p.Runs.Count);
        Assert.Equal("b", p.Runs.ToArray()[1].Texts.ToArray()[0].Text);
        var unknown = new List<string>();
        foreach (var c in p.UnknownChildren) unknown.Add(c.LocalNameText);
        Assert.Equal(["AlternateContent"], unknown);
        var attrs = UnknownAttributes.Of(p);
        Assert.Single(attrs);
        Assert.Equal("paraId", attrs[0].LocalName);
        Assert.Equal("1A2B3C4D", attrs[0].Value);
        Assert.Equal(xml, tree.Serialize());
    }

    [Fact]
    public void UnknownEnumerationValuesAreKept()
    {
        var (_, p) = Para(Doc("<w:p><w:pPr><w:jc w:val=\"mystery\"/></w:pPr></w:p>"));
        var jc = p.ParagraphProperties!.Value.Justification!.Value;
        Assert.Equal(JustificationValue.Unrecognized, jc.Val);
        Assert.Equal("mystery", jc.ValText);
        Assert.Throws<ArgumentOutOfRangeException>(() => jc.Val = JustificationValue.Unrecognized);
    }

    [Fact]
    public void StrictNamespacesReadAndKeepTheirSpelling()
    {
        var (tree, p) = Para(Doc("<w:p><w:pPr><w:jc w:val=\"start\"/></w:pPr></w:p>", WStrict));
        var ppr = p.ParagraphProperties!.Value;
        Assert.Equal(JustificationValue.Start, ppr.Justification!.Value.Val);
        ppr.GetOrAddKeepNext().Val = true;
        Assert.Contains("<w:keepNext w:val=\"true\"/>", tree.Serialize(), StringComparison.Ordinal);
        Assert.DoesNotContain("xmlns:w=\"" + W + "\"", tree.Serialize(), StringComparison.Ordinal);
    }

    [Fact]
    public void BooleansAndNumbersParseTolerantly()
    {
        var xml = Doc("<w:p><w:r><w:rPr><w:b w:val=\"off\"/><w:i/><w:sz w:val=\"24\"/><w:szCs w:val=\"x\"/></w:rPr></w:r></w:p>");
        var (_, p) = Para(xml);
        var rpr = p.Runs.ToArray()[0].RunProperties!.Value;
        Assert.False(rpr.Bold!.Value.Val);
        Assert.Null(rpr.Italic!.Value.Val);
        Assert.Equal(24, rpr.Size!.Value.Val);
        Assert.Null(rpr.SizeComplexScript!.Value.Val);
        Assert.Equal("x", rpr.SizeComplexScript!.Value.ValText);
    }

    [Fact]
    public void SetTextAndEntitiesRoundTrip()
    {
        var (tree, p) = Para(Doc("<w:p><w:r><w:t>old</w:t></w:r></w:p>"));
        var t = p.Runs.ToArray()[0].Texts.ToArray()[0];
        t.Text = "a & b";
        Assert.Equal("a & b", t.Text);
        Assert.Contains("<w:t>a &amp; b</w:t>", tree.Serialize(), StringComparison.Ordinal);
    }

    [Fact]
    public void FactoryResolvesRootsAndChildren()
    {
        var kind = ViewFactory.RootKind(SchemaNamespace.Wordprocessing, "document"u8);
        Assert.Equal(ViewKind.WordprocessingDocument, kind);
        var body = ViewFactory.ChildKind(kind, SchemaNamespace.Wordprocessing, "body"u8, out var rank);
        Assert.Equal(ViewKind.WordprocessingBody, body);
        Assert.True(rank >= 0);
        Assert.Equal(ViewKind.None, ViewFactory.ChildKind(kind, SchemaNamespace.Wordprocessing, "nonsense"u8, out _));
        Assert.Equal(SchemaNamespace.Wordprocessing, SchemaNamespaces.Resolve(SchemaNamespaces.Uri(SchemaNamespace.Wordprocessing, true)));
    }
}
