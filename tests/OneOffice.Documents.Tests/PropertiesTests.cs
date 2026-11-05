using System.Text;
using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.Packaging;
using RecursiveAI.OneOffice.Properties;

namespace RecursiveAI.OneOffice.Documents.Tests;

public class PropertiesTests
{
    private const string CoreXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" " +
        "xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:dcmitype=\"http://purl.org/dc/dcmitype/\" " +
        "xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"><dc:title>Old &amp; title</dc:title><dc:creator>Ann</dc:creator><cp:keywords>a, b</cp:keywords>" +
        "<cp:lastModifiedBy>Bob</cp:lastModifiedBy><cp:revision>3</cp:revision><dcterms:created xsi:type=\"dcterms:W3CDTF\">2020-01-02T03:04:05Z</dcterms:created>" +
        "<dcterms:modified xsi:type=\"dcterms:W3CDTF\">2021-02-03T04:05:06+02:00</dcterms:modified><cp:contentStatus>Draft</cp:contentStatus></cp:coreProperties>";

    private const string CustomXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/custom-properties\" " +
        "xmlns:vt=\"http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes\">" +
        "<property fmtid=\"{D5CDD505-2E9C-101B-9397-08002B2CF9AE}\" pid=\"2\" name=\"Project\"><vt:lpwstr>Apollo</vt:lpwstr></property>" +
        "<property fmtid=\"{D5CDD505-2E9C-101B-9397-08002B2CF9AE}\" pid=\"4\" name=\"Count\"><vt:i4>7</vt:i4></property></Properties>";

    private static InMemoryPackage Package(bool core = false, bool custom = false, string? coreXml = null)
    {
        InMemoryPackage p = TestDocs.Package("<w:p/>");
        var rels = new List<Relationship>(p.GetRelationships());
        if (core)
        {
            p.AddPart("/docProps/core.xml", "application/vnd.openxmlformats-package.core-properties+xml", Encoding.UTF8.GetBytes(coreXml ?? CoreXml));
            rels.Add(new Relationship("rId2", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties", "docProps/core.xml"));
        }

        if (custom)
        {
            p.AddPart("/docProps/custom.xml", "application/vnd.openxmlformats-officedocument.custom-properties+xml", Encoding.UTF8.GetBytes(CustomXml));
            rels.Add(new Relationship("rId3", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/custom-properties", "docProps/custom.xml"));
        }

        p.SetRelationships(null, rels);
        return p;
    }

    private static string Text(InMemoryPackage p, string name) => Encoding.UTF8.GetString(p.FindPart(name)!.GetBytes().Span);

    [Fact]
    public void ReadsCoreProperties()
    {
        using Document d = Document.FromPackage(Package(core: true));
        CoreProperties c = d.Properties.Core;
        Assert.Equal("Old & title", c.Title);
        Assert.Equal("Ann", c.Creator);
        Assert.Equal("a, b", c.Keywords);
        Assert.Equal("Bob", c.LastModifiedBy);
        Assert.Equal("3", c.Revision);
        Assert.Equal("Draft", c.ContentStatus);
        Assert.Equal(new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.Zero), c.Created);
        Assert.Equal(new DateTimeOffset(2021, 2, 3, 4, 5, 6, TimeSpan.FromHours(2)), c.Modified);
        Assert.Null(c.Subject);
        Assert.Null(c.LastPrinted);
    }

    [Fact]
    public void AbsentPartsReadEmptyAndStayAbsent()
    {
        InMemoryPackage p = Package();
        using Document d = Document.FromPackage(p);
        Assert.Null(d.Properties.Core.Title);
        Assert.Null(d.Properties.Extended.Company);
        Assert.Empty(d.Properties.Custom.List());
        d.CommitChanges();
        Assert.Null(p.FindPart("/docProps/core.xml"));
        Assert.Null(p.FindPart("/docProps/custom.xml"));
        Assert.Null(p.FindPart("/docProps/app.xml"));
        Assert.Empty(d.Defects);
    }

    [Fact]
    public void UntouchedPartsAreNotRewritten()
    {
        InMemoryPackage p = Package(core: true, custom: true);
        using Document d = Document.FromPackage(p);
        _ = d.Properties.Core.Title;
        _ = d.Properties.Custom.List();
        Assert.False(d.HasUncommittedChanges);
        d.CommitChanges();
        Assert.Equal(CoreXml, Text(p, "/docProps/core.xml"));
        Assert.Equal(CustomXml, Text(p, "/docProps/custom.xml"));
    }

    [Fact]
    public void EditingOnePropertyKeepsTheOthersInOrder()
    {
        InMemoryPackage p = Package(core: true);
        using Document d = Document.FromPackage(p);
        d.Properties.Core.Title = "New <title>";
        d.Properties.Core.Subject = "Added";
        d.Properties.Core.Keywords = null;
        d.Properties.Core.Modified = new DateTimeOffset(2025, 6, 7, 8, 9, 10, TimeSpan.FromHours(-5));
        d.Properties.Core.LastPrinted = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        d.CommitChanges();
        string xml = Text(p, "/docProps/core.xml");
        Assert.Contains("<dc:title>New &lt;title&gt;</dc:title><dc:creator>Ann</dc:creator><cp:lastModifiedBy>Bob</cp:lastModifiedBy>", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("keywords", xml, StringComparison.Ordinal);
        Assert.Contains("<dc:subject>Added</dc:subject>", xml, StringComparison.Ordinal);
        Assert.Contains("<dcterms:modified xsi:type=\"dcterms:W3CDTF\">2025-06-07T13:09:10Z</dcterms:modified>", xml, StringComparison.Ordinal);
        Assert.Contains("<cp:lastPrinted>2025-01-01T00:00:00Z</cp:lastPrinted>", xml, StringComparison.Ordinal);
        Assert.Contains("<dcterms:created xsi:type=\"dcterms:W3CDTF\">2020-01-02T03:04:05Z</dcterms:created>", xml, StringComparison.Ordinal);

        // Written bytes read back the same through a fresh document.
        using Document back = Document.FromPackage(p);
        Assert.Equal("New <title>", back.Properties.Core.Title);
        Assert.Equal(new DateTimeOffset(2025, 6, 7, 13, 9, 10, TimeSpan.Zero), back.Properties.Core.Modified);
    }

    [Fact]
    public void CreatesTheCorePartWithRelationshipAndContentTypeAndIsDeterministic()
    {
        byte[] Make()
        {
            InMemoryPackage p = Package();
            using Document d = Document.FromPackage(p);
            d.Properties.Core.Title = "T";
            d.Properties.Core.Creator = "C";
            d.Properties.Core.Created = new DateTimeOffset(2024, 5, 6, 7, 8, 9, TimeSpan.Zero);
            d.CommitChanges();
            Assert.Equal("application/vnd.openxmlformats-package.core-properties+xml", p.FindPart("/docProps/core.xml")!.ContentType);
            Assert.Contains(p.GetRelationships(), r => r.Type.EndsWith("/metadata/core-properties", StringComparison.Ordinal) && r.Target == "docProps/core.xml");
            Assert.Contains(p.GetRelationships(), r => r.Type.EndsWith("/officeDocument", StringComparison.Ordinal));
            return p.FindPart("/docProps/core.xml")!.GetBytes().ToArray();
        }

        byte[] a = Make();
        Assert.Equal(a, Make());
        string xml = Encoding.UTF8.GetString(a);
        Assert.Contains("<dcterms:created xsi:type=\"dcterms:W3CDTF\">2024-05-06T07:08:09Z</dcterms:created>", xml, StringComparison.Ordinal);
        Assert.Contains("<dc:title>T</dc:title>", xml, StringComparison.Ordinal);
        using Document back = Document.FromPackage(Package());
        Assert.Null(back.Properties.Core.Title);
    }

    [Fact]
    public void ExtendedPropertiesAreCreatedAndEdited()
    {
        InMemoryPackage p = Package();
        using Document d = Document.FromPackage(p);
        d.Properties.Extended.Application = "OneOffice";
        d.Properties.Extended.Company = "A & B";
        d.Properties.Extended.Pages = 12;
        d.Properties.Extended.SetValue("Manager", "Cy");
        d.CommitChanges();
        string xml = Text(p, "/docProps/app.xml");
        Assert.Contains("<Application>OneOffice</Application>", xml, StringComparison.Ordinal);
        Assert.Contains("<Company>A &amp; B</Company>", xml, StringComparison.Ordinal);
        using Document back = Document.FromPackage(p);
        Assert.Equal("A & B", back.Properties.Extended.Company);
        Assert.Equal(12, back.Properties.Extended.Pages);
        Assert.Equal("Cy", back.Properties.Extended.GetValue("Manager"));
        Assert.Throws<ArgumentException>(() => back.Properties.Extended.SetValue("a b", "x"));
    }

    [Fact]
    public void CustomPropertiesReadAddEditAndRemove()
    {
        InMemoryPackage p = Package(custom: true);
        using Document d = Document.FromPackage(p);
        CustomProperties c = d.Properties.Custom;
        Assert.Equal(["Project", "Count"], c.List().Select(x => x.Name).ToArray());
        Assert.Equal("Apollo", c.Find("project")?.AsString);
        Assert.Equal(7, c.Find("Count")?.AsInt32);

        c.SetInt32("Count", 8);
        c.SetString("Owner", "Eve & Co");
        c.SetBoolean("Reviewed", true);
        c.SetDouble("Ratio", 0.1);
        c.SetInt64("Big", 5_000_000_000);
        c.SetDateTime("Due", new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));
        Assert.True(c.Remove("Project"));
        Assert.False(c.Remove("Nope"));
        d.CommitChanges();

        string xml = Text(p, "/docProps/custom.xml");
        Assert.Contains("pid=\"4\" name=\"Count\"><vt:i4>8</vt:i4>", xml, StringComparison.Ordinal);
        Assert.Contains("pid=\"5\" name=\"Owner\"><vt:lpwstr>Eve &amp; Co</vt:lpwstr>", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("Apollo", xml, StringComparison.Ordinal);

        using Document back = Document.FromPackage(p);
        CustomProperties r = back.Properties.Custom;
        Assert.Equal(["Count", "Owner", "Reviewed", "Ratio", "Big", "Due"], r.List().Select(x => x.Name).ToArray());
        Assert.Equal(true, r.Find("Reviewed")?.AsBoolean);
        Assert.Equal(0.1, r.Find("Ratio")?.AsDouble);
        Assert.Equal(5_000_000_000, r.Find("Big")?.AsInt64);
        Assert.Null(r.Find("Big")?.AsInt32);
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), r.Find("Due")?.AsDateTime);
        Assert.Equal(CustomPropertyType.Boolean, r.Find("Reviewed")?.Type);
    }

    [Fact]
    public void CustomPartIsCreatedWhenAbsent()
    {
        InMemoryPackage p = Package();
        using Document d = Document.FromPackage(p);
        d.Properties.Custom.SetString("Key", "Value");
        d.CommitChanges();
        Assert.Equal("application/vnd.openxmlformats-officedocument.custom-properties+xml", p.FindPart("/docProps/custom.xml")!.ContentType);
        Assert.Contains("pid=\"2\" name=\"Key\"", Text(p, "/docProps/custom.xml"), StringComparison.Ordinal);
        Assert.Contains(p.GetRelationships(), r => r.Type.EndsWith("/custom-properties", StringComparison.Ordinal));
    }

    [Fact]
    public void UnreadablePartIsReportedAndNeverOverwritten()
    {
        InMemoryPackage p = Package(core: true, coreXml: "<cp:coreProperties");
        var defects = new DefectCollection();
        using Document d = Document.FromPackage(p, new OpenContext(defects: defects));
        Assert.Null(d.Properties.Core.Title);
        Assert.Contains(defects, x => x.Code == "props.part-unreadable");
        var ex = Assert.Throws<OfficeFormatException>(() => d.Properties.Core.Title = "x");
        Assert.Equal("props.part-unreadable", ex.Defect.Code);
        Assert.Equal("<cp:coreProperties", Text(p, "/docProps/core.xml"));
    }

    [Fact]
    public void PartWithTheWrongRootIsUnreadableAndMinimizedFuzzFindStaysFixed()
    {
        // Found by fuzzing: a custom-properties part that is text without a root element used to be written into.
        InMemoryPackage p = Package(core: true, coreXml: "<w:numbering xmlns:w=\"urn:x\"/>");
        var defects = new DefectCollection();
        using Document d = Document.FromPackage(p, new OpenContext(defects: defects));
        Assert.Null(d.Properties.Core.Title);
        Assert.Contains(defects, x => x.Code == "props.part-unreadable");
        Assert.Throws<OfficeFormatException>(() => d.Properties.Core.Creator = "x");
        InMemoryPackage q = Package(custom: true);
        q.FindPart("/docProps/custom.xml")!.SetBytes(Encoding.UTF8.GetBytes("\0\0t:i4></property>"));
        using Document e = Document.FromPackage(q);
        Assert.Empty(e.Properties.Custom.List());
        Assert.Throws<OfficeFormatException>(() => e.Properties.Custom.SetString("k", "v"));
    }

    [Fact]
    public void BadDatesReadAsNull()
    {
        using Document d = Document.FromPackage(Package(core: true, coreXml: CoreXml.Replace("2020-01-02T03:04:05Z", "last tuesday", StringComparison.Ordinal)));
        Assert.Null(d.Properties.Core.Created);
    }
}
