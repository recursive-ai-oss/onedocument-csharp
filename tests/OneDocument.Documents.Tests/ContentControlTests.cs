using System.Text;
using RecursiveAI.OneDocument.Documents.ContentControls;
using RecursiveAI.OneDocument.Packaging;
using Xunit;

namespace RecursiveAI.OneDocument.Documents.Tests;

public class ContentControlTests
{
    private const string W14 = "http://schemas.microsoft.com/office/word/2010/wordml";
    private const string W15 = "http://schemas.microsoft.com/office/word/2012/wordml";
    private const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private const string StoreId = "{11111111-2222-3333-4444-555555555555}";

    private const string Body =
        "<w:sdt><w:sdtPr><w:alias w:val=\"Title\"/><w:tag w:val=\"title\"/><w:id w:val=\"101\"/><w:lock w:val=\"sdtLocked\"/><w:showingPlcHdr/><w:text/></w:sdtPr>" +
        "<w:sdtContent><w:p><w:pPr><w:jc w:val=\"center\"/></w:pPr><w:r><w:rPr><w:rStyle w:val=\"PlaceholderText\"/></w:rPr><w:t>Click here</w:t></w:r></w:p></w:sdtContent></w:sdt>" +
        "<w:p><w:r><w:t xml:space=\"preserve\">Name: </w:t></w:r>" +
        "<w:sdt><w:sdtPr><w:rPr><w:b/></w:rPr><w:tag w:val=\"name\"/><w:id w:val=\"102\"/><w:dataBinding w:prefixMappings=\"xmlns:ns0='urn:cust' xmlns:ns1='urn:other'\" w:xpath=\"/ns0:root[1]/ns0:name[1]\" w:storeItemID=\"" + StoreId + "\"/><w:text/></w:sdtPr>" +
        "<w:sdtContent><w:r><w:rPr><w:b/></w:rPr><w:t>Ada</w:t></w:r></w:sdtContent></w:sdt></w:p>" +
        "<w:p><w:sdt><w:sdtPr><w:tag w:val=\"colour\"/><w:dropDownList w:lastValue=\"r\"><w:listItem w:displayText=\"Red\" w:value=\"r\"/><w:listItem w:displayText=\"Green\" w:value=\"g\"/></w:dropDownList></w:sdtPr>" +
        "<w:sdtContent><w:r><w:t>Red</w:t></w:r></w:sdtContent></w:sdt>" +
        "<w:sdt><w:sdtPr><w:tag w:val=\"combo\"/><w:comboBox><w:listItem w:value=\"a\"/></w:comboBox></w:sdtPr><w:sdtContent><w:r><w:t>a</w:t></w:r></w:sdtContent></w:sdt>" +
        "<w:sdt><w:sdtPr><w:tag w:val=\"when\"/><w:date w:fullDate=\"2024-05-01T00:00:00Z\"><w:dateFormat w:val=\"d MMMM yyyy\"/><w:lid w:val=\"en-GB\"/></w:date></w:sdtPr><w:sdtContent><w:r><w:t>1 May 2024</w:t></w:r></w:sdtContent></w:sdt>" +
        "<w:sdt><w:sdtPr><w:tag w:val=\"agree\"/><w14:checkbox><w14:checked w14:val=\"0\"/><w14:checkedState w14:val=\"2612\" w14:font=\"MS Gothic\"/><w14:uncheckedState w14:val=\"2610\" w14:font=\"MS Gothic\"/></w14:checkbox></w:sdtPr>" +
        "<w:sdtContent><w:r><w:rPr><w:rFonts w:ascii=\"MS Gothic\" w:hAnsi=\"MS Gothic\"/></w:rPr><w:t>\u2610</w:t></w:r></w:sdtContent></w:sdt>" +
        "<w:sdt><w:sdtPr><w:tag w:val=\"pic\"/><w:picture/></w:sdtPr><w:sdtContent><w:r><w:drawing/></w:r></w:sdtContent></w:sdt></w:p>" +
        "<w:sdt><w:sdtPr><w:tag w:val=\"rep\"/><w15:repeatingSection/></w:sdtPr><w:sdtContent>" +
        "<w:sdt><w:sdtPr><w15:repeatingSectionItem/></w:sdtPr><w:sdtContent><w:p><w:r><w:t>item1</w:t></w:r></w:p></w:sdtContent></w:sdt>" +
        "<w:sdt><w:sdtPr><w15:repeatingSectionItem/></w:sdtPr><w:sdtContent><w:p><w:r><w:t>item2</w:t></w:r></w:p></w:sdtContent></w:sdt></w:sdtContent></w:sdt>" +
        "<w:sdt><w:sdtPr><w:group/></w:sdtPr><w:sdtContent><w:p><w:r><w:t>g</w:t></w:r></w:p></w:sdtContent></w:sdt>" +
        "<w:sdt><w:sdtPr><w:docPartObj><w:docPartGallery w:val=\"Table of Contents\"/><w:docPartUnique/></w:docPartObj></w:sdtPr><w:sdtContent><w:p/></w:sdtContent></w:sdt>" +
        "<w:sdt><w:sdtPr><w:citation/></w:sdtPr><w:sdtContent><w:p><w:r><w:t>c</w:t></w:r></w:p></w:sdtContent></w:sdt>" +
        "<w:sdt><w:sdtPr><w:bibliography/></w:sdtPr><w:sdtContent><w:p/></w:sdtContent></w:sdt>" +
        "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"1\"/></w:tblGrid>" +
        "<w:sdt><w:sdtPr><w:tag w:val=\"rowsdt\"/></w:sdtPr><w:sdtContent><w:tr><w:sdt><w:sdtPr><w:tag w:val=\"cellsdt\"/></w:sdtPr><w:sdtContent><w:tc><w:p><w:r><w:t>cell</w:t></w:r></w:p></w:tc></w:sdtContent></w:sdt></w:tr></w:sdtContent></w:sdt></w:tbl>";

    private static InMemoryPackage Package(string body = Body)
    {
        var p = new InMemoryPackage();
        p.SetRelationships(null, [new Relationship("rId1", Rel + "officeDocument", "word/document.xml")]);
        p.AddPart("/word/document.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml", Encoding.UTF8.GetBytes(
            $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<w:document xmlns:w=\"{TestDocs.WNs}\" xmlns:w14=\"{W14}\" xmlns:w15=\"{W15}\" xmlns:x=\"urn:unknown\"><w:body>{body}</w:body></w:document>"));
        p.SetRelationships("/word/document.xml", [new Relationship("rId9", Rel + "customXml", "../customXml/item1.xml")]);
        p.AddPart("/customXml/item1.xml", "application/xml", Encoding.UTF8.GetBytes(
            "<ns0:root xmlns:ns0=\"urn:cust\" xmlns:ns1=\"urn:other\" id=\"7\"><ns0:name>Ada Lovelace</ns0:name><ns0:item>one</ns0:item><ns0:item k=\"b\">two</ns0:item><ns1:item>other</ns1:item><plain>p</plain></ns0:root>"));
        p.SetRelationships("/customXml/item1.xml", [new Relationship("rId1", Rel + "customXmlProps", "itemProps1.xml")]);
        p.AddPart("/customXml/itemProps1.xml", "application/vnd.openxmlformats-officedocument.customXmlProperties+xml", Encoding.UTF8.GetBytes(
            "<ds:datastoreItem xmlns:ds=\"http://schemas.openxmlformats.org/officeDocument/2006/customXml\" ds:itemID=\"" + StoreId + "\"><ds:schemaRefs><ds:schemaRef ds:uri=\"urn:cust\"/></ds:schemaRefs></ds:datastoreItem>"));
        return p;
    }

    private static ContentControl ByTag(Document d, string tag) => d.ContentControls().First(c => c.Tag == tag);

    private static string MainXml(InMemoryPackage p) => Encoding.UTF8.GetString(p.FindPart("/word/document.xml")!.GetBytes().Span);

    [Fact]
    public void Census_FindsEveryTypeAndLevel()
    {
        using var doc = Document.FromPackage(Package());
        ContentControl[] all = doc.ContentControls().ToArray();
        Assert.Equal(16, all.Length);
        var types = all.Select(c => c.Type).ToHashSet();
        foreach (ContentControlType t in Enum.GetValues<ContentControlType>().Except([ContentControlType.Equation, ContentControlType.BuildingBlockList]))
        {
            Assert.Contains(t, types);
        }

        Assert.Equal(ContentControlLevel.Block, all[0].Level);
        Assert.Equal(ContentControlLevel.Inline, ByTag(doc, "name").Level);
        Assert.Equal(ContentControlLevel.Row, ByTag(doc, "rowsdt").Level);
        Assert.Equal(ContentControlLevel.Cell, ByTag(doc, "cellsdt").Level);
        Assert.Equal(ContentControlLevel.Block, all.First(c => c.Type == ContentControlType.RepeatingSectionItem).Level);
    }

    [Fact]
    public void Properties_AreRead()
    {
        using var doc = Document.FromPackage(Package());
        ContentControl title = ByTag(doc, "title");
        Assert.Equal("Title", title.Alias);
        Assert.Equal(101, title.Id);
        Assert.Equal(ContentControlLock.SdtLocked, title.Lock);
        Assert.True(title.IsShowingPlaceholder);
        Assert.True(title.IsTypeExplicit);
        Assert.Equal(ContentControlType.PlainText, title.Type);
        Assert.False(title.IsMultiLine);
        Assert.Equal("Click here", title.Text);

        ContentControl colour = ByTag(doc, "colour");
        Assert.Equal(["Red", "Green"], colour.ListItems.Select(i => i.DisplayText).ToArray());
        Assert.Equal(0, colour.SelectedIndex);
        Assert.Equal("a", ByTag(doc, "combo").ListItems.Single().DisplayText);

        ContentControl when = ByTag(doc, "when");
        Assert.Equal("d MMMM yyyy", when.DateFormat);
        Assert.Equal(new DateTimeOffset(2024, 5, 1, 0, 0, 0, TimeSpan.Zero), when.Date);
        Assert.False(ByTag(doc, "agree").IsChecked);
        Assert.Equal("Table of Contents", doc.ContentControls().Single(c => c.Type == ContentControlType.BuildingBlock).BuildingBlockGallery);

        ContentControl rep = ByTag(doc, "rep");
        Assert.Equal(2, rep.Children().Count());
        Assert.Equal(rep, rep.Children().First().Parent);
        Assert.Equal("item1", rep.Children().First().Text);
        Assert.Equal("Ada", ByTag(doc, "name").Text);
    }

    [Fact]
    public void HubWrappers_ReachTheControl()
    {
        using var doc = Document.FromPackage(Package());
        var block = (BlockContentControl)doc.Body.Blocks().First();
        Assert.Equal("title", block.Control.Tag);
        var inline = doc.Body.Paragraphs().Skip(1).First().Inlines().OfType<InlineContentControl>().First();
        Assert.Equal("name", inline.Control.Tag);
    }

    [Fact]
    public void Bindings_ResolveAgainstCustomXml()
    {
        using var doc = Document.FromPackage(Package());
        CustomXmlPart part = Assert.Single(doc.CustomXmlParts);
        Assert.Equal(StoreId, part.ItemId);
        Assert.Equal(["urn:cust"], part.SchemaUris.ToArray());

        BindingValue v = ByTag(doc, "name").EvaluateBinding();
        Assert.Equal(BindingStatus.Resolved, v.Status);
        Assert.Equal("Ada Lovelace", v.Value);
        Assert.Equal(BindingStatus.Invalid, ByTag(doc, "title").EvaluateBinding().Status);
    }

    [Theory]
    [InlineData("/ns0:root/ns0:name", BindingStatus.Resolved, "Ada Lovelace")]
    [InlineData("/ns0:root[1]/ns0:item[2]", BindingStatus.Resolved, "two")]
    [InlineData("/ns0:root/ns0:item[@k='b']", BindingStatus.Resolved, "two")]
    [InlineData("/ns0:root/ns1:item", BindingStatus.Resolved, "other")]
    [InlineData("/ns0:root/plain", BindingStatus.Resolved, "p")]
    [InlineData("/ns0:root/@id", BindingStatus.Resolved, "7")]
    [InlineData("/ns0:root/ns0:name/text()", BindingStatus.Resolved, "Ada Lovelace")]
    [InlineData("/ns0:root/*[2]", BindingStatus.Resolved, "one")]
    [InlineData("/ns0:root/ns0:item[3]", BindingStatus.NoMatch, null)]
    [InlineData("/ns0:root/ns0:missing", BindingStatus.NoMatch, null)]
    [InlineData("/ns0:other", BindingStatus.NoMatch, null)]
    [InlineData("/ns0:root/plain/@x", BindingStatus.NoMatch, null)]
    [InlineData("/ns0:root//ns0:name", BindingStatus.UnsupportedPath, null)]
    [InlineData("ns0:root", BindingStatus.UnsupportedPath, null)]
    [InlineData("/ns0:root/ns0:item[last()]", BindingStatus.UnsupportedPath, null)]
    [InlineData("/ns0:root/count(ns0:item)", BindingStatus.UnsupportedPath, null)]
    [InlineData("/nsX:root", BindingStatus.Invalid, null)]
    [InlineData("", BindingStatus.Invalid, null)]
    public void XPathSubset(string xpath, BindingStatus status, string? value)
    {
        using var doc = Document.FromPackage(Package());
        var b = new ContentControlBinding(xpath, "xmlns:ns0='urn:cust' xmlns:ns1=\"urn:other\"", StoreId);
        BindingValue r = b.Evaluate(doc);
        Assert.Equal(status, r.Status);
        Assert.Equal(value, r.Value);
    }

    [Fact]
    public void Binding_ToCoreProperties_UsesTheBuiltInStore()
    {
        InMemoryPackage p = Package("<w:p><w:sdt><w:sdtPr><w:tag w:val=\"t\"/><w:dataBinding w:prefixMappings=\"xmlns:ns0='http://purl.org/dc/elements/1.1/' xmlns:ns1='http://schemas.openxmlformats.org/package/2006/metadata/core-properties' \" w:xpath=\"/ns1:coreProperties[1]/ns0:title[1]\" w:storeItemID=\"{6C3C8BC8-F283-45AE-878A-BAB7291924A1}\"/><w:text/></w:sdtPr><w:sdtContent><w:r><w:t>x</w:t></w:r></w:sdtContent></w:sdt></w:p>");
        p.SetRelationships(null, [new Relationship("rId1", Rel + "officeDocument", "word/document.xml"), new Relationship("rId2", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties", "docProps/core.xml")]);
        p.AddPart("/docProps/core.xml", "application/xml", Encoding.UTF8.GetBytes("<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><dc:title>My title</dc:title></cp:coreProperties>"));
        using var doc = Document.FromPackage(p);
        BindingValue v = ByTag(doc, "t").EvaluateBinding();
        Assert.Equal(BindingStatus.Resolved, v.Status);
        Assert.Equal("My title", v.Value);
    }

    [Fact]
    public void Binding_WithUnknownStore_IsReported()
    {
        using var doc = Document.FromPackage(Package());
        Assert.Equal(BindingStatus.StoreNotFound, new ContentControlBinding("/a", null, "{nope}").Evaluate(doc).Status);
    }

    [Fact]
    public void SetText_Inline_KeepsRunFormattingAndOtherParts()
    {
        InMemoryPackage p = Package();
        byte[] customBefore = p.FindPart("/customXml/item1.xml")!.GetBytes().ToArray();
        using var doc = Document.FromPackage(p);
        ByTag(doc, "name").SetText("Grace <H> & \"co\"\tx\ny");
        doc.CommitChanges();
        string xml = MainXml(p);
        Assert.Contains("<w:r><w:rPr><w:b/></w:rPr><w:t xml:space=\"preserve\">Grace &lt;H&gt; &amp; \"co\"</w:t><w:tab/><w:t xml:space=\"preserve\">x y</w:t></w:r>", xml);
        Assert.Contains("xmlns:x=\"urn:unknown\"", xml);
        Assert.Equal(customBefore, p.FindPart("/customXml/item1.xml")!.GetBytes().ToArray());

        using var again = Document.FromPackage(p);
        Assert.Equal("Grace <H> & \"co\"\tx y", ByTag(again, "name").Text);
    }

    [Fact]
    public void SetText_InlineRich_UsesBreaks()
    {
        using var doc = Document.FromPackage(Package("<w:p><w:sdt><w:sdtPr><w:tag w:val=\"r\"/></w:sdtPr><w:sdtContent><w:r><w:t>x</w:t></w:r></w:sdtContent></w:sdt></w:p>"));
        ContentControl c = ByTag(doc, "r");
        c.SetText("a\nb");
        Assert.Contains("<w:t xml:space=\"preserve\">a</w:t><w:br/><w:t xml:space=\"preserve\">b</w:t>", c.Markup.Node.OuterXml);
        Assert.Equal("a\nb", c.Text);
    }

    [Fact]
    public void SetText_Placeholder_BecomesValue()
    {
        InMemoryPackage p = Package();
        using var doc = Document.FromPackage(p);
        ContentControl title = ByTag(doc, "title");
        title.SetText("Annual report");
        Assert.False(title.IsShowingPlaceholder);
        Assert.Equal("Annual report", title.Text);
        doc.CommitChanges();
        string xml = MainXml(p);
        Assert.DoesNotContain("PlaceholderText", xml);
        Assert.DoesNotContain("showingPlcHdr", xml);
        Assert.Contains("<w:jc w:val=\"center\"/>", xml);
        Assert.Contains("<w:alias w:val=\"Title\"/>", xml);
    }

    [Fact]
    public void SetText_Block_MakesOneParagraphPerLine_AndPlainTextFlattens()
    {
        using var doc = Document.FromPackage(Package("<w:sdt><w:sdtPr><w:tag w:val=\"r\"/></w:sdtPr><w:sdtContent><w:p><w:pPr><w:jc w:val=\"right\"/></w:pPr><w:r><w:t>x</w:t></w:r></w:p></w:sdtContent></w:sdt>"));
        ContentControl c = ByTag(doc, "r");
        c.SetText("a\nb");
        Assert.Equal("a\nb", c.Text);
        Assert.Equal(2, doc.Body.Paragraphs().Count());
        Assert.All(doc.Body.Paragraphs(), q => Assert.Contains("w:jc", q.Markup.Node.OuterXml));
    }

    [Fact]
    public void SetText_Rules()
    {
        using var doc = Document.FromPackage(Package());
        Assert.Throws<InvalidOperationException>(() => ByTag(doc, "agree").SetText("x"));
        Assert.Throws<InvalidOperationException>(() => ByTag(doc, "cellsdt").SetText("x"));
        Assert.Throws<ArgumentException>(() => ByTag(doc, "colour").SetText("Blue"));
        Assert.Throws<ArgumentException>(() => ByTag(doc, "name").SetText("bad\u0001"));
        ByTag(doc, "colour").SetText("g");
        Assert.Equal("Green", ByTag(doc, "colour").Text);
        Assert.Equal("g", ByTag(doc, "colour").LastValue);
        Assert.Equal(1, ByTag(doc, "colour").SelectedIndex);
        ByTag(doc, "combo").SetText("free text");
        Assert.Equal("free text", ByTag(doc, "combo").Text);
        Assert.Null(ByTag(doc, "combo").SelectedIndex);
    }

    [Fact]
    public void SetChecked_UpdatesStateAndGlyph_AndRoundTrips()
    {
        InMemoryPackage p = Package();
        using var doc = Document.FromPackage(p);
        ContentControl cb = ByTag(doc, "agree");
        cb.SetChecked(true);
        Assert.True(cb.IsChecked);
        Assert.Equal("\u2612", cb.Text);
        cb.SetChecked(false);
        Assert.False(cb.IsChecked);
        Assert.Equal("\u2610", cb.Text);
        cb.SetChecked(true);
        doc.CommitChanges();
        string xml = MainXml(p);
        Assert.Contains("<w14:checked w14:val=\"1\"/>", xml);
        Assert.Contains("MS Gothic", xml);
        using var again = Document.FromPackage(p);
        Assert.True(ByTag(again, "agree").IsChecked);
        Assert.Throws<InvalidOperationException>(() => ByTag(again, "name").SetChecked(true));
    }

    [Fact]
    public void SetChecked_AddsMissingCheckedElement()
    {
        using var doc = Document.FromPackage(Package("<w:p><w:sdt><w:sdtPr><w:tag w:val=\"c\"/><w14:checkbox/></w:sdtPr><w:sdtContent><w:r><w:t>\u2610</w:t></w:r></w:sdtContent></w:sdt></w:p>"));
        ContentControl cb = ByTag(doc, "c");
        Assert.False(cb.IsChecked);
        cb.SetChecked(true);
        Assert.True(cb.IsChecked);
        Assert.Equal("\u2612", cb.Text);
    }

    [Fact]
    public void SelectItem_ByIndex()
    {
        using var doc = Document.FromPackage(Package());
        ContentControl c = ByTag(doc, "colour");
        c.SelectItem(1);
        Assert.Equal("Green", c.Text);
        Assert.Throws<ArgumentOutOfRangeException>(() => c.SelectItem(2));
        Assert.Throws<InvalidOperationException>(() => ByTag(doc, "name").SelectItem(0));
    }

    [Fact]
    public void Remove_KeepingContent_AtEveryLevel()
    {
        InMemoryPackage p = Package();
        using var doc = Document.FromPackage(p);
        ByTag(doc, "name").RemoveKeepingContent();
        ByTag(doc, "title").RemoveKeepingContent();
        ByTag(doc, "cellsdt").RemoveKeepingContent();
        ByTag(doc, "rowsdt").RemoveKeepingContent();
        doc.CommitChanges();
        string xml = MainXml(p);
        Assert.Contains("<w:p><w:r><w:t xml:space=\"preserve\">Name: </w:t></w:r><w:r><w:rPr><w:b/></w:rPr><w:t>Ada</w:t></w:r></w:p>", xml);
        Assert.Contains("<w:tr><w:tc><w:p><w:r><w:t>cell</w:t></w:r></w:p></w:tc></w:tr>", xml);
        using var again = Document.FromPackage(p);
        Assert.Null(again.ContentControls().FirstOrDefault(c => c.Tag is "name" or "title" or "cellsdt" or "rowsdt"));
        Assert.Contains("Click here", again.Body.Paragraphs().Select(q => q.Text));
    }

    [Fact]
    public void Remove_WithContent_AndNested()
    {
        using var doc = Document.FromPackage(Package());
        ByTag(doc, "rep").RemoveKeepingContent();
        Assert.Equal(0, doc.ContentControls().Count(c => c.Type == ContentControlType.RepeatingSection));
        Assert.Equal(2, doc.ContentControls().Count(c => c.Type == ContentControlType.RepeatingSectionItem));
        ByTag(doc, "pic").RemoveWithContent();
        Assert.DoesNotContain(doc.ContentControls(), c => c.Tag == "pic");
    }

    [Fact]
    public void UntouchedDocument_WritesNothing()
    {
        InMemoryPackage p = Package();
        byte[] before = p.FindPart("/word/document.xml")!.GetBytes().ToArray();
        using var doc = Document.FromPackage(p);
        _ = doc.ContentControls().Select(c => (c.Type, c.Text, c.Binding)).ToArray();
        _ = doc.CustomXmlParts;
        Assert.False(doc.HasUncommittedChanges);
        doc.CommitChanges();
        Assert.Equal(before, p.FindPart("/word/document.xml")!.GetBytes().ToArray());
    }

    [Fact]
    public void DeepNesting_DoesNotOverflow()
    {
        string open = string.Concat(Enumerable.Repeat("<w:sdt><w:sdtPr><w:tag w:val=\"d\"/></w:sdtPr><w:sdtContent>", 100));
        string close = string.Concat(Enumerable.Repeat("</w:sdtContent></w:sdt>", 100));
        using var doc = Document.FromPackage(Package(open + "<w:p><w:r><w:t>deep</w:t></w:r></w:p>" + close));
        Assert.Equal(100, doc.ContentControls().Count());
        Assert.Equal("deep", doc.ContentControls().First().Text);
        Assert.NotNull(doc.ContentControls().Last().Parent);
    }
}
