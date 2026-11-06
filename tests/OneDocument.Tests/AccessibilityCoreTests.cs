using RecursiveAI.OneDocument.Accessibility;

namespace RecursiveAI.OneDocument.Tests;

public class AccessibilityCoreTests
{
    [Theory]
    [InlineData(0x000000, 0xFFFFFF, 21.0)]
    [InlineData(0xFFFFFF, 0xFFFFFF, 1.0)]
    [InlineData(0x767676, 0xFFFFFF, 4.54)]
    [InlineData(0x777777, 0xFFFFFF, 4.48)]
    [InlineData(0xFF0000, 0x000000, 5.25)]
    public void ContrastRatio_MatchesWcagValues(int fg, int bg, double expected)
    {
        Assert.Equal(expected, Contrast.Ratio(fg, bg), 2);
        Assert.Equal(Contrast.Ratio(fg, bg), Contrast.Ratio(bg, fg), 10);
    }

    [Theory]
    [InlineData(4.5, 11, false, true)]
    [InlineData(4.49, 11, false, false)]
    [InlineData(3.0, 18, false, true)]
    [InlineData(3.0, 14, true, true)]
    [InlineData(3.0, 14, false, false)]
    [InlineData(2.99, 24, true, false)]
    public void Passes_UsesLargeTextThreshold(double ratio, double size, bool bold, bool expected) =>
        Assert.Equal(expected, Contrast.Passes(ratio, size, bold));

    [Theory]
    [InlineData("FF0000", true, 0xFF0000)]
    [InlineData("#00ff00", true, 0x00FF00)]
    [InlineData("80123456", true, 0x123456)]
    [InlineData("auto", false, 0)]
    [InlineData("12345", false, 0)]
    [InlineData(null, false, 0)]
    public void TryParseHex(string? text, bool ok, int rgb)
    {
        Assert.Equal(ok, Contrast.TryParseHex(text, out int v));
        if (ok)
        {
            Assert.Equal(rgb, v);
        }
    }

    [Theory]
    [InlineData("IMG_0042.jpg", true)]
    [InlineData("chart.PNG", true)]
    [InlineData("C:\\pics\\a.gif", true)]
    [InlineData("A photo of a harbour.", false)]
    [InlineData("Version 2.0 of the plan", false)]
    [InlineData("", false)]
    public void FileNameAltText(string text, bool expected) => Assert.Equal(expected, AccessibilityText.LooksLikeFileName(text));

    [Theory]
    [InlineData("click here", null, true)]
    [InlineData("Read more", null, true)]
    [InlineData("https://example.org/page", null, true)]
    [InlineData("www.example.org", null, true)]
    [InlineData("example", "example", true)]
    [InlineData("", null, true)]
    [InlineData("Quarterly report", "https://example.org/q", false)]
    public void MeaninglessLinks(string text, string? target, bool meaningless) =>
        Assert.Equal(meaningless, AccessibilityText.MeaninglessLinkReason(text, target) is not null);

    [Theory]
    [InlineData("Sheet1", true)]
    [InlineData("sheet12", true)]
    [InlineData("Sheets", false)]
    [InlineData("Sales", false)]
    public void DefaultSheetNames(string name, bool expected) => Assert.Equal(expected, AccessibilityText.IsDefaultSheetName(name));

    [Theory]
    [InlineData("Default Section", true)]
    [InlineData("untitled section", true)]
    [InlineData("Section 3", true)]
    [InlineData("Section overview", false)]
    public void DefaultSectionNames(string name, bool expected) => Assert.Equal(expected, AccessibilityText.IsDefaultSectionName(name));

    [Fact]
    public void RuleCatalogue_HasUniqueIdsAndOfficeCategories()
    {
        Assert.Equal(AccessibilityRules.All.Count, AccessibilityRules.All.Select(r => r.Id).Distinct().Count());
        Assert.All(AccessibilityRules.All, r => Assert.True(r.AppliesTo(OfficeKind.Document) || r.AppliesTo(OfficeKind.Workbook) || r.AppliesTo(OfficeKind.Presentation)));
        // Categories as Office's published list gives them.
        Assert.Equal(AccessibilityCategory.Error, AccessibilityRules.AltTextMissing.Category);
        Assert.Equal(AccessibilityCategory.Error, AccessibilityRules.TableHeaderRow.Category);
        Assert.Equal(AccessibilityCategory.Warning, AccessibilityRules.TableMergedCells.Category);
        Assert.Equal(AccessibilityCategory.Warning, AccessibilityRules.ContrastLow.Category);
        Assert.Equal(AccessibilityCategory.Tip, AccessibilityRules.SlideTitleDuplicate.Category);
        Assert.Equal(AccessibilityCategory.Tip, AccessibilityRules.HeadingsMissing.Category);
        Assert.Equal(AccessibilityCategory.IntelligentServices, AccessibilityRules.AltTextSuggested.Category);
        Assert.Same(AccessibilityRules.SheetBlank, AccessibilityRules.Find("sheet.blank"));
        Assert.Null(AccessibilityRules.Find("nope"));
    }

    [Fact]
    public void DrawingColor_AppliesLuminanceTransforms()
    {
        var doc = RecursiveAI.OneDocument.Markup.MarkupDocument.Parse("<a:schemeClr xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" val=\"accent1\"><a:lumMod val=\"50000\"/></a:schemeClr>"u8.ToArray());
        Assert.True(DrawingColor.TryResolve(doc.Root, n => n == "accent1" ? 0x4472C4 : null, out int rgb, out double alpha));
        Assert.Equal(1.0, alpha);
        Assert.True(Contrast.RelativeLuminance(rgb) < Contrast.RelativeLuminance(0x4472C4));
        Assert.False(DrawingColor.TryResolve(doc.Root, _ => null, out _, out _));
    }

    [Fact]
    public void Collector_CapsFindingsPerRuleAndSaysSo()
    {
        var c = new AccessibilityCollector(OfficeKind.Document, maxPerRule: 3);
        for (int i = 0; i < 5; i++)
        {
            c.Add(new AccessibilityIssue(AccessibilityRules.AltTextMissing, new AccessibilityLocation { Paragraph = i + 1 }, null, "m", "s"));
        }

        c.Add(new AccessibilityIssue(AccessibilityRules.TableHeaderRow, new AccessibilityLocation(), null, "m", "s"));
        AccessibilityReport r = c.ToReport();
        Assert.Equal(4, r.Issues.Count);
        AccessibilitySkip skip = Assert.Single(r.Skipped);
        Assert.Equal("alt-text.missing", skip.RuleId);
        Assert.Contains("2 further", skip.Reason);
        Assert.Equal("paragraph 3", r.Issues[2].Location.Description);
    }
}
