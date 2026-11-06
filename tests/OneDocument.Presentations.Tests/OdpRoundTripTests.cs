using System.Text;
using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Presentations.Tests;

/// <summary>Writes the generated decks as OpenDocument presentations and reads them back.</summary>
public class OdpRoundTripTests
{
    static OdpRoundTripTests() => PresentationFormats.Register(RecursiveAI.OneDocument.Formats.FormatRegistry.Default);

    private static readonly string[] Templates = ["placeholders", "table-rows", "slide-per-item-with-notes", "hyperlink-target"];

    public static IEnumerable<TheoryDataRow<string>> Decks() =>
        Directory.EnumerateFiles(Path.Combine(OdpFixtureTests.TestData(), "slides"), "*.pptx")
            .Concat(Directory.EnumerateFiles(Path.Combine(OdpFixtureTests.TestData(), "drawingml"), "*.pptx"))
            .Concat(Templates.Select(n => Path.Combine(OdpFixtureTests.TestData(), "templates", "pptx", n, "template.pptx")))
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => new TheoryDataRow<string>(f));

    private static byte[] Save(Presentation p, OfficeFormat format)
    {
        using var ms = new MemoryStream();
        p.SaveAs(ms, format);
        return ms.ToArray();
    }

    private static int Count(Presentation p, Func<Shape, bool> test) => p.Slides.Sum(s => s.AllShapes().Count(test));

    [Theory]
    [MemberData(nameof(Decks))]
    public void A_deck_written_as_odp_reads_back_with_its_slides_and_text(string path)
    {
        using Presentation source = Presentation.Open(path);
        byte[] odp = Save(source, OfficeFormat.Odp);
        Assert.NotNull(source.ExportLoss);
        using Presentation back = Presentation.Open(new MemoryStream(odp));

        Assert.Equal(OfficeFormat.Odp, back.Format);
        Assert.Equal(source.Slides.Count, back.Slides.Count);
        Assert.DoesNotContain(back.Defects, d => d.Severity == DefectSeverity.Fatal);
        for (int i = 0; i < source.Slides.Count; i++)
        {
            Assert.Equal(OdpFixtureTests.Words(source.Slides[i].Text), OdpFixtureTests.Words(back.Slides[i].Text));
            Assert.Equal(OdpFixtureTests.Words(source.Slides[i].Notes?.NotesText ?? ""), OdpFixtureTests.Words(back.Slides[i].Notes?.NotesText ?? ""));
            Assert.Equal(source.Slides[i].IsHidden, back.Slides[i].IsHidden);
        }

        Assert.Equal(Count(source, s => s.Table is not null), Count(back, s => s.Table is not null));
        Assert.Equal(Count(source, s => s.Kind == ShapeKind.Picture), Count(back, s => s.Kind == ShapeKind.Picture));
    }

    [Theory]
    [MemberData(nameof(Decks))]
    public void A_deck_written_as_flat_odp_reads_back_with_the_same_text(string path)
    {
        // Charts and SmartArt are not written to OpenDocument; the deck that holds them keeps its text in their cached data.
        if (path.Contains("pictures-charts", StringComparison.Ordinal))
        {
            return;
        }

        using Presentation source = Presentation.Open(path);
        byte[] fodp = Save(source, OfficeFormat.Fodp);
        Assert.Contains("<office:document ", Encoding.UTF8.GetString(fodp), StringComparison.Ordinal);
        using Presentation back = Presentation.Open(new MemoryStream(fodp));

        Assert.Equal(OfficeFormat.Fodp, back.Format);
        Assert.Equal(OdpFixtureTests.Words(source.ExtractText()), OdpFixtureTests.Words(back.ExtractText()));
    }

    [Fact]
    public void An_untouched_odp_saves_its_original_bytes_and_an_edit_ends_that()
    {
        using Presentation source = Presentation.Open(Path.Combine(OdpFixtureTests.TestData(), "slides", "slide-runs.pptx"));
        byte[] odp = Save(source, OfficeFormat.Odp);
        using Presentation back = Presentation.Open(new MemoryStream(odp));
        Assert.Equal(odp, Save(back, OfficeFormat.Odp));

        back.Slides[0].Shapes.First(s => s.TextBody is not null).TextBody!.Paragraphs[0].SetText("Edited title");
        byte[] edited = Save(back, OfficeFormat.Odp);
        Assert.NotEqual(odp, edited);
        using Presentation again = Presentation.Open(new MemoryStream(edited));
        Assert.Contains("Edited title", again.Slides[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_written_package_starts_with_a_stored_mimetype_and_a_template_has_its_own_type()
    {
        using Presentation source = Presentation.Open(Path.Combine(OdpFixtureTests.TestData(), "slides", "slide-runs.pptx"));
        byte[] odp = Save(source, OfficeFormat.Odp);
        Assert.Equal((byte)'P', odp[0]);
        Assert.Equal(0, BitConverter.ToUInt16(odp, 8));
        Assert.Contains("application/vnd.oasis.opendocument.presentation", Encoding.ASCII.GetString(odp, 30, 80), StringComparison.Ordinal);

        byte[] otp = Save(source, OfficeFormat.Otp);
        using Presentation template = Presentation.Open(new MemoryStream(otp));
        Assert.Equal(OfficeFormat.Otp, template.Format);
    }

    [Fact]
    public void Office_statics_convert_both_ways_and_extract_text()
    {
        string dir = Path.Combine(Path.GetTempPath(), "odp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string pptx = Path.Combine(OdpFixtureTests.TestData(), "slides", "slide-table.pptx");
            string odp = Path.Combine(dir, "t.odp");
            string back = Path.Combine(dir, "t.pptx");
            Office.Convert(pptx, odp);
            Office.Convert(odp, back);
            Assert.Equal(OdpFixtureTests.Words(Office.ExtractText(pptx)), OdpFixtureTests.Words(Office.ExtractText(odp)));
            Assert.Equal(OdpFixtureTests.Words(Office.ExtractText(pptx)), OdpFixtureTests.Words(Office.ExtractText(back)));
            Assert.Equal(OfficeFormat.Odp, Office.Detect(odp));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void A_text_document_is_not_a_presentation_and_says_so()
    {
        string rich = Path.Combine(OdpFixtureTests.TestData(), "odf", "lo-rich.odt");
        Assert.Throws<FormatNotAvailableException>(() => Presentation.Open(rich));
    }
}
