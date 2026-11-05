using System.Text;
using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.Packaging;

namespace RecursiveAI.OneOffice.Spreadsheets.Tests;

public sealed class FeatureDefectTests
{
    private const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";

    private static string[] Codes(OpenContext c) => [.. TestBook.Defects(c).Select(d => d.Code)];

    [Fact]
    public void StylesWithWrongRootAreFatalOnFirstUse()
    {
        var ctx = new OpenContext();
        using Workbook wb = TestBook.Open(StyleTests.Book("<?xml version=\"1.0\"?><other/>"), ctx);
        OfficeFormatException ex = Assert.Throws<OfficeFormatException>(() => wb.Styles);
        Assert.Equal("xlsx.styles-wrong-root", ex.Defect.Code);
        Assert.Equal("/xl/styles.xml", ex.Location.Container);
    }

    [Fact]
    public void InvalidNumberFormatIsDroppedWithLocation()
    {
        var ctx = new OpenContext();
        using Workbook wb = TestBook.Open(StyleTests.Book("<?xml version=\"1.0\"?><styleSheet xmlns=\"" + TestBook.Main + "\"><numFmts count=\"1\"><numFmt numFmtId=\"x\"/></numFmts></styleSheet>"), ctx);
        Assert.Empty(wb.Styles.NumberFormats);
        Assert.Contains("xlsx.numfmt-invalid", Codes(ctx));
    }

    [Fact]
    public void UnreadableThemeFallsBackToTheDefaultColours()
    {
        var ctx = new OpenContext();
        byte[] file;
        using (OpcPackage p = OpcPackage.Open(new MemoryStream(StyleTests.Book(theme: false))))
        {
            p.AddPart("/xl/theme/theme1.xml", "application/vnd.openxmlformats-officedocument.theme+xml", Encoding.UTF8.GetBytes("<a:theme"));
            p.SetRelationships("/xl/workbook.xml", [.. p.GetRelationships("/xl/workbook.xml"), new Relationship("rId9", Rel + "theme", "theme/theme1.xml")]);
            using var ms = new MemoryStream();
            p.WriteTo(ms);
            file = ms.ToArray();
        }

        using Workbook wb = TestBook.Open(file, ctx);
        Assert.Equal(unchecked((int)0xFF4472C4), wb.Styles.ResolveColor(StyleColor.FromTheme(4)));
        Assert.Contains("xlsx.theme-unreadable", Codes(ctx));
    }

    [Fact]
    public void TablePartsThatDoNotResolveAreReported()
    {
        string sheet = TestBook.Sheet1Xml.Replace("<pageMargins", "<pageMargins", StringComparison.Ordinal).Replace("<extLst>", "<tableParts count=\"2\"><tablePart r:id=\"rId7\"/><tablePart r:id=\"rId8\"/></tableParts><extLst>", StringComparison.Ordinal);
        byte[] file;
        using (OpcPackage p = OpcPackage.Open(new MemoryStream(TestBook.Bytes(sheet))))
        {
            p.AddPart("/xl/tables/table9.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.table+xml", Encoding.UTF8.GetBytes("<notatable xmlns=\"" + TestBook.Main + "\"/>"));
            p.SetRelationships("/xl/worksheets/sheet1.xml", [new Relationship("rId8", Rel + "table", "../tables/table9.xml")]);
            using var ms = new MemoryStream();
            p.WriteTo(ms);
            file = ms.ToArray();
        }

        var ctx = new OpenContext();
        using Workbook wb = TestBook.Open(file, ctx);
        Assert.Empty(wb.Sheets[0].Tables);
        string[] codes = Codes(ctx);
        Assert.Contains("xlsx.table-part-missing", codes);
        Assert.Contains("xlsx.table-unreadable", codes);
    }
}
