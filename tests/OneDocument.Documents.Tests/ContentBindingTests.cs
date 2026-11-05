using RecursiveAI.OneDocument.Documents.ContentControls;
using static RecursiveAI.OneDocument.Documents.Tests.ContentControlCases;

namespace RecursiveAI.OneDocument.Documents.Tests;

public sealed class ContentBindingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cc-" + Guid.NewGuid().ToString("N"));

    public ContentBindingTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    private Document Open(string body, string store = Order)
    {
        string path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".docx");
        Write(new Case("t", body, store, "refresh"), path);
        return Document.Open(path);
    }

    private static string Sdt(string type, string xpath, string content, string store = StoreId) =>
        "<w:p><w:sdt><w:sdtPr><w:tag w:val=\"t\"/><w:id w:val=\"5\"/><w:dataBinding w:prefixMappings=\"xmlns:ns0='urn:acme'\" w:xpath=\"" + xpath + "\" w:storeItemID=\"" + store + "\"/>" + type
        + "</w:sdtPr><w:sdtContent><w:r><w:t>" + content + "</w:t></w:r></w:sdtContent></w:sdt></w:p>";

    [Fact]
    public void UnknownStore_IsReported_AndTheControlKept()
    {
        using Document d = Open(Sdt("<w:text/>", "/ns0:order[1]/ns0:customer[1]", "kept", "{00000000-0000-0000-0000-000000000000}"));
        ContentBindingReport r = d.RefreshContentControls();
        Assert.Equal("binding.store-not-found", Assert.Single(r.Warnings).Code);
        Assert.Equal("kept", d.ContentControls().Single().Text);
    }

    [Fact]
    public void ValueNotInTheList_IsReported_AndTheDropDownKept()
    {
        const string List = "<w:dropDownList><w:listItem w:displayText=\"Red\" w:value=\"r\"/></w:dropDownList>";
        using Document d = Open(Sdt(List, "/ns0:order[1]/ns0:colour[1]", "Red"));
        ContentBindingReport r = d.RefreshContentControls();
        Assert.Equal("binding.value-not-in-list", Assert.Single(r.Warnings).Code);
        Assert.Equal("Red", d.ContentControls().Single().Text);
    }

    [Fact]
    public void PictureControl_IsReportedAsUnsupported()
    {
        using Document d = Open(Sdt("<w:picture/>", "/ns0:order[1]/ns0:customer[1]", "p"));
        Assert.Equal("binding.unsupported-control", Assert.Single(d.RefreshContentControls().Warnings).Code);
    }

    [Fact]
    public void RepeatingSectionWithoutAnItem_IsReported()
    {
        string section = "<w:sdt><w:sdtPr><w:id w:val=\"9\"/><w:dataBinding w:prefixMappings=\"xmlns:ns0='urn:acme'\" w:xpath=\"/ns0:order[1]/ns0:lines[1]/ns0:line\" w:storeItemID=\"" + StoreId + "\"/><w15:repeatingSection/></w:sdtPr><w:sdtContent><w:p/></w:sdtContent></w:sdt>";
        using Document d = Open(section);
        Assert.Equal("binding.no-template-item", Assert.Single(d.RefreshContentControls().Warnings).Code);
    }

    [Fact]
    public void Refresh_DoesNotChangeTheStore_AndBindDoes()
    {
        using Document d = Open(Sdt("<w:text/>", "/ns0:order[1]/ns0:customer[1]", "x"));
        string before = d.CustomXmlParts[0].Data.Root.OuterXml;
        d.RefreshContentControls();
        Assert.Equal(before, d.CustomXmlParts[0].Data.Root.OuterXml);
        ContentBindingReport r = d.BindContentControls(new Dictionary<string, object?> { ["customer"] = "New" });
        Assert.Empty(r.Warnings);
        Assert.Contains("<ns0:customer>New</ns0:customer>", d.CustomXmlParts[0].Data.Root.OuterXml, StringComparison.Ordinal);
        Assert.Equal("New", d.ContentControls().Single().Text);
    }

    [Fact]
    public void BindThenPush_RoundTripsTheValues()
    {
        using Document d = Open(Sdt("<w:text/>", "/ns0:order[1]/ns0:customer[1]", "x"));
        d.BindContentControls(new Dictionary<string, object?> { ["customer"] = "Round" });
        d.ContentControls().Single().SetText("Edited");
        d.PushContentControls();
        Assert.Equal(BindingStatus.Resolved, d.ContentControls().Single().EvaluateBinding().Status);
        Assert.Equal("Edited", d.ContentControls().Single().EvaluateBinding().Value);
    }

    [Fact]
    public void NestedRepeatingSections_ExpandPerOuterItem()
    {
        const string Store = "<ns0:order xmlns:ns0=\"urn:acme\"><ns0:groups><ns0:group><ns0:tags><ns0:tag>a</ns0:tag></ns0:tags></ns0:group></ns0:groups></ns0:order>";
        string inner = "<w:sdt><w:sdtPr><w:id w:val=\"20\"/><w:dataBinding w:prefixMappings=\"xmlns:ns0='urn:acme'\" w:xpath=\"/ns0:order[1]/ns0:groups[1]/ns0:group[1]/ns0:tags[1]/ns0:tag\" w:storeItemID=\"" + StoreId + "\"/><w15:repeatingSection/></w:sdtPr><w:sdtContent>"
            + "<w:sdt><w:sdtPr><w:id w:val=\"21\"/><w15:repeatingSectionItem/></w:sdtPr><w:sdtContent>"
            + Sdt("<w:text/>", "/ns0:order[1]/ns0:groups[1]/ns0:group[1]/ns0:tags[1]/ns0:tag[1]", "t") + "</w:sdtContent></w:sdt></w:sdtContent></w:sdt>";
        string outer = "<w:sdt><w:sdtPr><w:id w:val=\"10\"/><w:dataBinding w:prefixMappings=\"xmlns:ns0='urn:acme'\" w:xpath=\"/ns0:order[1]/ns0:groups[1]/ns0:group\" w:storeItemID=\"" + StoreId + "\"/><w15:repeatingSection/></w:sdtPr><w:sdtContent>"
            + "<w:sdt><w:sdtPr><w:id w:val=\"11\"/><w15:repeatingSectionItem/></w:sdtPr><w:sdtContent>" + inner + "</w:sdtContent></w:sdt></w:sdtContent></w:sdt>";
        using Document d = Open(outer, Store);
        ContentBindingReport r = d.BindContentControls("<order><groups><group><tags><tag>x</tag><tag>y</tag></tags></group><group><tags><tag>z</tag></tags></group></groups></order>");
        Assert.Empty(r.Warnings);
        Assert.Equal(["x", "y", "z"], d.ContentControls().Where(c => c.Type == ContentControlType.PlainText).Select(c => c.Text).ToArray());
        Assert.Equal(5, r.SectionItems);
        Assert.Equal(d.ContentControls().Select(c => c.Id).Distinct().Count(), d.ContentControls().Count());
    }
}
