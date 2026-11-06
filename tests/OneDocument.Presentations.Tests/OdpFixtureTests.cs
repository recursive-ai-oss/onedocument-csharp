using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Presentations.Tests;

/// <summary>
/// Reads the OpenDocument presentations that a black-box converter wrote from the generated decks in <c>testdata</c> and checks that the slides,
/// their text, notes, tables and pictures come back. <c>testdata/odp/README.md</c> records where the files come from.
/// </summary>
public partial class OdpFixtureTests
{
    static OdpFixtureTests() => PresentationFormats.Register(RecursiveAI.OneDocument.Formats.FormatRegistry.Default);

    internal static string TestData([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "testdata"));

    public static IEnumerable<TheoryDataRow<string>> Fixtures() =>
        Directory.EnumerateFiles(Path.Combine(TestData(), "odp"), "lo-*.odp").OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => new TheoryDataRow<string>(Path.GetFileName(f)));

    internal static string SourceOf(string fixture)
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

    internal static string Words(string text) => string.Join(' ', DatePattern().Replace(text, "DATE").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void The_file_opens_with_the_slides_of_the_deck_it_was_written_from(string fixture)
    {
        using Presentation odp = Presentation.Open(Path.Combine(TestData(), "odp", fixture));
        using Presentation source = Presentation.Open(SourceOf(fixture));

        Assert.Equal(OfficeFormat.Odp, odp.Format);
        Assert.Equal(source.Slides.Count, odp.Slides.Count);
        Assert.DoesNotContain(odp.Defects, d => d.Severity == DefectSeverity.Fatal);
        Assert.NotNull(odp.ImportLoss);
        for (int i = 0; i < source.Slides.Count; i++)
        {
            Assert.Equal(source.Slides[i].IsHidden, odp.Slides[i].IsHidden);
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void The_text_of_every_slide_matches_the_deck_it_was_written_from(string fixture)
    {
        // The chart and SmartArt deck keeps their cached text in graphics the converter draws differently; its text is not compared.
        if (fixture.Contains("pictures-charts", StringComparison.Ordinal))
        {
            return;
        }

        using Presentation odp = Presentation.Open(Path.Combine(TestData(), "odp", fixture));
        using Presentation source = Presentation.Open(SourceOf(fixture));

        for (int i = 0; i < source.Slides.Count; i++)
        {
            Assert.Equal(Words(source.Slides[i].Text), Words(odp.Slides[i].Text));
            Assert.Equal(Words(source.Slides[i].Notes?.NotesText ?? ""), Words(odp.Slides[i].Notes?.NotesText ?? ""));
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void The_converted_package_reopens_and_saves_as_presentationml(string fixture)
    {
        using Presentation odp = Presentation.Open(Path.Combine(TestData(), "odp", fixture));
        byte[] bytes = Decks.Save(odp);
        using Presentation again = Decks.Reopen(bytes);

        Assert.Equal(OfficeFormat.Pptx, again.Format);
        Assert.Equal(odp.Slides.Count, again.Slides.Count);
        Assert.Equal(Words(odp.ExtractText()), Words(again.ExtractText()));
    }

    [Fact]
    public void Tables_pictures_and_notes_come_back()
    {
        using Presentation odp = Presentation.Open(Path.Combine(TestData(), "odp", "lo-slide-table.odp"));
        using Presentation source = Presentation.Open(SourceOf("lo-slide-table.odp"));
        int Tables(Presentation p) => p.Slides.Sum(s => s.AllShapes().Count(sh => sh.Table is not null));
        Assert.True(Tables(source) > 0);
        Assert.Equal(Tables(source), Tables(odp));

        using Presentation notes = Presentation.Open(Path.Combine(TestData(), "odp", "lo-slide-slides-hidden-notes.odp"));
        Assert.Contains(notes.Slides, s => s.IsHidden);
        Assert.Contains(notes.Slides, s => !string.IsNullOrEmpty(s.Notes?.NotesText));

        using Presentation pics = Presentation.Open(Path.Combine(TestData(), "odp", "lo-slide-pictures-charts-diagrams.odp"));
        using Presentation picSource = Presentation.Open(SourceOf("lo-slide-pictures-charts-diagrams.odp"));
        int Pictures(Presentation p) => p.Slides.Sum(s => s.AllShapes().Count(sh => sh.Kind == ShapeKind.Picture));
        Assert.True(Pictures(pics) >= 1);
        Assert.True(Pictures(pics) >= Pictures(picSource) - 1);
    }
}
