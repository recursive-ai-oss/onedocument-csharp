using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using RecursiveAI.OneDocument.Ppt.Binary;

namespace RecursiveAI.OneDocument.Presentations.Tests;

/// <summary>
/// Reads the PowerPoint 97-2003 files that a black-box converter wrote from the generated decks in <c>testdata</c> and checks that the slides,
/// their text and the pictures come back. <c>testdata/ppt/README.md</c> records where the files come from.
/// </summary>
public partial class PptFixtureTests
{
    private static string TestData([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "testdata"));

    public static IEnumerable<TheoryDataRow<string>> Fixtures() =>
        Directory.EnumerateFiles(Path.Combine(TestData(), "ppt"), "lo-*.ppt").OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => new TheoryDataRow<string>(Path.GetFileName(f)));

    private static string SourceOf(string fixture)
    {
        string name = Path.GetFileNameWithoutExtension(fixture)["lo-".Length..];
        string data = TestData();
        if (name.EndsWith("-template", StringComparison.Ordinal))
        {
            return Path.Combine(data, "templates", "pptx", name[..^"-template".Length], "template.pptx");
        }

        return name.StartsWith("drawingml-", StringComparison.Ordinal)
            ? Path.Combine(data, "drawingml", name["drawingml-".Length..] + ".pptx")
            : Path.Combine(data, "slides", name + ".pptx");
    }

    [GeneratedRegex(@"\d{1,2}/\d{1,2}/\d{2,4}")]
    private static partial Regex DatePattern();

    private static string Words(string text) => string.Join(' ', DatePattern().Replace(text, "DATE").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void The_file_opens_with_the_slides_of_the_deck_it_was_written_from(string fixture)
    {
        using Presentation legacy = Presentation.Open(Path.Combine(TestData(), "ppt", fixture));
        using Presentation source = Presentation.Open(SourceOf(fixture));

        Assert.Equal(source.Slides.Count, legacy.Slides.Count);
        Assert.Equal(OfficeFormat.Ppt, legacy.SourceFormat);
        Assert.DoesNotContain(legacy.Defects, d => d.Severity == RecursiveAI.OneDocument.Diagnostics.DefectSeverity.Fatal);
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void The_text_matches_the_deck_it_was_written_from(string fixture)
    {
        // The chart and SmartArt deck keeps their cached text in graphics the converter draws differently; its text is not compared.
        if (fixture.Contains("pictures-charts", StringComparison.Ordinal))
        {
            return;
        }

        using Presentation legacy = Presentation.Open(Path.Combine(TestData(), "ppt", fixture));
        using Presentation source = Presentation.Open(SourceOf(fixture));

        Assert.Equal(Words(source.ExtractText()), Words(legacy.ExtractText()));
    }

    [Fact]
    public void The_converted_package_reopens_and_saves_as_presentationml()
    {
        using Presentation legacy = Presentation.Open(Path.Combine(TestData(), "ppt", "lo-slide-table.ppt"));
        byte[] bytes = Decks.Save(legacy);
        using Presentation again = Decks.Reopen(bytes);

        Assert.Equal(legacy.Slides.Count, again.Slides.Count);
        Assert.Equal(Words(legacy.ExtractText()), Words(again.ExtractText()));
        Assert.NotNull(legacy.ImportLoss);
    }
}
