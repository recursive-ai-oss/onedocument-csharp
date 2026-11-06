using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Ole;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Properties;

namespace RecursiveAI.OneDocument.Tests;

public class OleDocumentPropertiesTests
{
    private static DocumentProperties Fresh(out InMemoryPackage package)
    {
        package = new InMemoryPackage();
        return new DocumentProperties(package);
    }

    [Fact]
    public void HubPropertiesSurviveAWriteAndARead()
    {
        DocumentProperties src = Fresh(out InMemoryPackage p1);
        using (p1)
        {
            src.Core.Title = "Quarterly report";
            src.Core.Subject = "Sales";
            src.Core.Creator = "Ada";
            src.Core.Keywords = "q3, sales";
            src.Core.Description = "A comment";
            src.Core.LastModifiedBy = "Grace";
            src.Core.Revision = "7";
            src.Core.Category = "Reports";
            src.Core.Created = new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.Zero);
            src.Core.Modified = new DateTimeOffset(2021, 6, 7, 8, 9, 10, TimeSpan.Zero);
            src.Core.LastPrinted = new DateTimeOffset(2021, 6, 8, 0, 0, 0, TimeSpan.Zero);
            src.Extended.Application = "Spreadsheet";
            src.Extended.ApplicationVersion = "12.0000";
            src.Extended.Company = "Example Co";
            src.Extended.Manager = "Linus";
            src.Extended.Template = "Normal.dotm";
            src.Extended.Pages = 3;
            src.Extended.Words = 120;
            src.Extended.Characters = 700;
            src.Extended.CharactersWithSpaces = 820;
            src.Extended.Lines = 12;
            src.Extended.Paragraphs = 4;
            src.Extended.TotalTimeMinutes = 95;
            src.Extended.DocumentSecurity = 1;
            src.Extended.HyperlinkBase = "https://example.com/base/";
            src.Custom.SetString("Project", "Atlas \u00e9\u00e8");
            src.Custom.SetInt32("Count", 42);
            src.Custom.SetInt64("Big", 5_000_000_000);
            src.Custom.SetDouble("Ratio", 0.25);
            src.Custom.SetBoolean("Reviewed", true);
            src.Custom.SetDateTime("Due", new DateTimeOffset(2022, 2, 3, 4, 5, 6, TimeSpan.Zero));

            (byte[]? summary, byte[]? docSummary) = OleDocumentProperties.Create(src);
            Assert.NotNull(summary);
            Assert.NotNull(docSummary);

            DocumentProperties back = Fresh(out InMemoryPackage p2);
            using (p2)
            {
                var defects = new DefectCollection();
                IReadOnlyList<string> lost = OleDocumentProperties.Read(summary, docSummary, back, new OpenContext(defects: defects));
                Assert.Empty(lost);
                Assert.Empty(defects);
                Assert.Equal(src.Core.Title, back.Core.Title);
                Assert.Equal(src.Core.Subject, back.Core.Subject);
                Assert.Equal(src.Core.Creator, back.Core.Creator);
                Assert.Equal(src.Core.Keywords, back.Core.Keywords);
                Assert.Equal(src.Core.Description, back.Core.Description);
                Assert.Equal(src.Core.LastModifiedBy, back.Core.LastModifiedBy);
                Assert.Equal("7", back.Core.Revision);
                Assert.Equal("Reports", back.Core.Category);
                Assert.Equal(src.Core.Created, back.Core.Created);
                Assert.Equal(src.Core.Modified, back.Core.Modified);
                Assert.Equal(src.Core.LastPrinted, back.Core.LastPrinted);
                Assert.Equal("Spreadsheet", back.Extended.Application);
                Assert.Equal("12.0000", back.Extended.ApplicationVersion);
                Assert.Equal("Example Co", back.Extended.Company);
                Assert.Equal("Linus", back.Extended.Manager);
                Assert.Equal("Normal.dotm", back.Extended.Template);
                Assert.Equal(3, back.Extended.Pages);
                Assert.Equal(120, back.Extended.Words);
                Assert.Equal(700, back.Extended.Characters);
                Assert.Equal(820, back.Extended.CharactersWithSpaces);
                Assert.Equal(12, back.Extended.Lines);
                Assert.Equal(4, back.Extended.Paragraphs);
                Assert.Equal(95, back.Extended.TotalTimeMinutes);
                Assert.Equal(1, back.Extended.DocumentSecurity);
                Assert.Equal("https://example.com/base/", back.Extended.HyperlinkBase);
                Assert.Equal("Atlas \u00e9\u00e8", back.Custom.Find("Project")!.Value.AsString);
                Assert.Equal(42, back.Custom.Find("Count")!.Value.AsInt32);
                Assert.Equal(5_000_000_000, back.Custom.Find("Big")!.Value.AsInt64);
                Assert.Equal(0.25, back.Custom.Find("Ratio")!.Value.AsDouble);
                Assert.True(back.Custom.Find("Reviewed")!.Value.AsBoolean);
                Assert.Equal(new DateTimeOffset(2022, 2, 3, 4, 5, 6, TimeSpan.Zero), back.Custom.Find("Due")!.Value.AsDateTime);
                Assert.Equal(6, back.Custom.List().Count);
            }
        }
    }

    [Fact]
    public void HeadingPairsAndPartsRoundTrip()
    {
        var doc = new OlePropertySet(OlePropertySets.DocumentSummaryInformationFormatId) { CodePage = 1252 };
        doc.Set(12, OlePropertyValue.FromVariantVector([OlePropertyValue.FromString("Worksheets"), OlePropertyValue.FromInt32(2)]));
        doc.Set(13, OlePropertyValue.FromStringVector(["Sheet1", "Sheet2"]));
        var d = new OlePropertySets();
        d.Add(doc);
        DocumentProperties target = Fresh(out InMemoryPackage pkg);
        using (pkg)
        {
            Assert.Empty(OleDocumentProperties.Read(default, d.ToArray(), target));
            (_, byte[]? back) = OleDocumentProperties.Create(target);
            OlePropertySets read = OlePropertySets.Read(back!);
            OlePropertySet set = read.FindSet(OlePropertySets.DocumentSummaryInformationFormatId)!;
            var pairs = Assert.IsAssignableFrom<IReadOnlyList<object?>>(set.Find(12)!.Value);
            Assert.Equal("Worksheets", ((OlePropertyValue)pairs[0]!).AsString());
            Assert.Equal(2, ((OlePropertyValue)pairs[1]!).AsInt32());
            Assert.Equal(["Sheet1", "Sheet2"], Assert.IsAssignableFrom<IReadOnlyList<object?>>(set.Find(13)!.Value).Cast<string>());
        }
    }

    [Fact]
    public void UnmappedPropertiesAreNamed()
    {
        var summary = new OlePropertySet(OlePropertySets.SummaryInformationFormatId);
        summary.Set(2, OlePropertyValue.FromString("T"));
        summary.Set(17, OlePropertyValue.Create(OleVarType.VtCf, new OleClipboardData(0xFFFFFFFF, [1, 2, 3])));
        summary.Set(99, OlePropertyValue.FromInt32(1));
        var s = new OlePropertySets();
        s.Add(summary);
        var user = new OlePropertySet(OlePropertySets.UserDefinedPropertiesFormatId);
        user.SetNamed("blobby", OlePropertyValue.FromBlob([1]));
        user.SetNamed("fine", OlePropertyValue.FromInt32(3));
        var d = new OlePropertySets();
        d.Add(new OlePropertySet(OlePropertySets.DocumentSummaryInformationFormatId));
        d.Add(user);

        DocumentProperties target = Fresh(out InMemoryPackage pkg);
        using (pkg)
        {
            IReadOnlyList<string> lost = OleDocumentProperties.Read(s.ToArray(), d.ToArray(), target);
            Assert.Equal("T", target.Core.Title);
            Assert.Equal(3, lost.Count);
            Assert.Contains(lost, x => x.Contains("property 17") && x.Contains("thumbnail"));
            Assert.Contains(lost, x => x.Contains("property 99"));
            Assert.Contains(lost, x => x.Contains("blobby"));
            Assert.Equal(3, target.Custom.Find("fine")!.Value.AsInt32);
            var loss = new LossReport();
            OleDocumentProperties.ReportLoss(lost, loss, "loss.test-properties");
            Assert.Equal(3, loss.CountOf("loss.test-properties"));
        }
    }

    [Fact]
    public void AnEmptyHubHasNothingToWrite()
    {
        DocumentProperties p = Fresh(out InMemoryPackage pkg);
        using (pkg)
        {
            (byte[]? a, byte[]? b) = OleDocumentProperties.Create(p);
            Assert.Null(a);
            Assert.Null(b);
        }
    }

    [Fact]
    public void AnUnreadableStreamIsReportedAndSkipped()
    {
        DocumentProperties target = Fresh(out InMemoryPackage pkg);
        using (pkg)
        {
            var defects = new DefectCollection();
            IReadOnlyList<string> lost = OleDocumentProperties.Read(new byte[5], default, target, new OpenContext(defects: defects));
            Assert.Equal("SummaryInformation: the stream could not be read", Assert.Single(lost));
            Defect d = Assert.Single(defects);
            Assert.Equal("ole.propset-header", d.Code);
            Assert.Equal("/\u0005SummaryInformation", d.Location.Container);
        }
    }
}
