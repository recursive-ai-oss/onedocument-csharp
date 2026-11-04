using RecursiveAI.OneOffice.Formats;
using RecursiveAI.OneOffice.IO;

namespace RecursiveAI.OneOffice.Tests;

// The registry is process-wide, so the tests that register handlers run one at a time and use formats nothing else detects.
[Collection("registry")]
public class RegistryAndHubTests
{
    [Fact]
    public void Every_format_has_one_descriptor_and_extensions_map_back()
    {
        foreach (OfficeFormat f in Enum.GetValues<OfficeFormat>().Where(f => f != OfficeFormat.Unknown))
        {
            FormatDescriptor d = OfficeFormats.Get(f);
            Assert.Equal(f, d.Format);
            Assert.StartsWith(".", d.DefaultExtension, StringComparison.Ordinal);
            Assert.NotEmpty(d.MediaType);
        }

        Assert.Equal(Enum.GetValues<OfficeFormat>().Length - 1, OfficeFormats.All.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => OfficeFormats.Get(OfficeFormat.Unknown));
    }

    [Theory]
    [InlineData("a.docx", OfficeFormat.Docx)]
    [InlineData("A.XLSX", OfficeFormat.Xlsx)]
    [InlineData("deck.pptm", OfficeFormat.Pptm)]
    [InlineData("old.xls", OfficeFormat.Xls)]
    [InlineData("x.csv", OfficeFormat.Csv)]
    [InlineData("x.md", OfficeFormat.Markdown)]
    [InlineData("x.pdf", OfficeFormat.Pdf)]
    [InlineData(".odt", OfficeFormat.Odt)]
    [InlineData("ods", OfficeFormat.Ods)]
    [InlineData("flat.xml", OfficeFormat.Unknown)]
    [InlineData("noext", OfficeFormat.Unknown)]
    public void Extensions_choose_output_formats(string name, OfficeFormat expected) =>
        Assert.Equal(expected, OfficeFormats.FromExtension(name));

    [Fact]
    public void Descriptor_traits()
    {
        Assert.True(OfficeFormats.Get(OfficeFormat.Dotm).IsTemplate);
        Assert.True(OfficeFormats.Get(OfficeFormat.Dotm).IsMacroEnabled);
        Assert.True(OfficeFormats.Get(OfficeFormat.Fodt).IsFlat);
        Assert.Equal(OfficeKind.Workbook, OfficeFormats.Get(OfficeFormat.Xlsb).Kind);
        Assert.Equal(OfficeKind.Output, OfficeFormats.Get(OfficeFormat.Pdf).Kind);
    }

    [Fact]
    public void Missing_handlers_raise_format_not_available()
    {
        var registry = new FormatRegistry();
        Assert.False(registry.TryGetReader(OfficeFormat.Doc, out _));
        var ex = Assert.Throws<FormatNotAvailableException>(() => registry.GetReader(OfficeFormat.Doc));
        Assert.Equal(OfficeFormat.Doc, ex.Format);
        Assert.Throws<FormatNotAvailableException>(() => registry.GetWriter(OfficeFormat.Pdf));
        Assert.Throws<ArgumentException>(() => registry.RegisterReader((_, _, _, _, _) => throw new InvalidOperationException(), OfficeFormat.Unknown));
    }

    [Fact]
    public void Open_dispatches_by_content_and_hands_the_reader_the_path_and_context()
    {
        string path = Path.Combine(Path.GetTempPath(), "oneoffice-open-" + Environment.ProcessId + ".dat");
        File.WriteAllText(path, "{\\rtf1 hello}");
        FormatRegistry.Default.RegisterReader((source, format, context, filePath, _) => new FakeFile(format, context, filePath, source), OfficeFormat.Rtf);
        FormatRegistry.Default.RegisterWriter((file, format, stream, _) => stream.Write(System.Text.Encoding.UTF8.GetBytes("written as " + format + " from " + file.Kind)), OfficeFormat.Rtf, OfficeFormat.Pdf);
        try
        {
            var context = new OpenContext(new OfficeLimits { MaxXmlDepth = 17 });
            using OfficeFile file = Office.Open(path, context);
            Assert.Equal(OfficeFormat.Rtf, file.Format);
            Assert.Equal(path, file.FilePath);
            Assert.Equal(17, file.Limits.MaxXmlDepth);
            Assert.Equal(OfficeKind.Document, file.Kind);

            // SaveAs picks the format by extension; Save() writes the opened format back to the same path.
            string pdf = Path.ChangeExtension(path, ".pdf");
            file.SaveAs(pdf);
            Assert.Equal("written as Pdf from Document", File.ReadAllText(pdf));
            File.Delete(pdf);
            file.Save();
            Assert.Equal("written as Rtf from Document", File.ReadAllText(path));
            Assert.Throws<ArgumentException>(() => file.SaveAs(Path.ChangeExtension(path, ".xml")));
            Assert.Throws<FormatNotAvailableException>(() => file.SaveAs(Path.ChangeExtension(path, ".docx")));

            var ms = new MemoryStream();
            file.SaveAs(ms, OfficeFormat.Pdf);
            Assert.Equal("written as Pdf from Document", System.Text.Encoding.UTF8.GetString(ms.ToArray()));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Open_of_unrecognised_content_is_a_typed_format_error()
    {
        using IByteSource junk = ByteSource.FromMemory(new byte[] { 1, 2, 3, 4 });
        var ex = Assert.Throws<OfficeFormatException>(() => Office.Open(junk));
        Assert.Equal("format.unrecognised", ex.Defect.Code);
    }

    [Fact]
    public void Open_of_a_format_with_no_reader_raises_format_not_available()
    {
        using IByteSource pdf = ByteSource.FromMemory("%PDF-1.7\n"u8.ToArray());
        var ex = Assert.Throws<FormatNotAvailableException>(() => Office.Open(pdf));
        Assert.Equal(OfficeFormat.Pdf, ex.Format);
    }

    [Fact]
    public void Extract_text_prefers_a_registered_extractor_then_falls_back_to_the_model()
    {
        FormatRegistry.Default.RegisterReader((source, format, context, filePath, _) => new FakeFile(format, context, filePath, source), OfficeFormat.Rtf);
        string path = Path.Combine(Path.GetTempPath(), "oneoffice-text-" + Environment.ProcessId + ".dat");
        File.WriteAllText(path, "{\\rtf1 hello}");
        try
        {
            using IByteSource rtf = ByteSource.FromFile(path);
            var all = new StringWriter();
            Office.ExtractText(rtf, all);
            Assert.Equal("text of Rtf", all.ToString());
            Assert.Equal(13, rtf.Length); // the caller's source is still usable

            FormatRegistry.Default.RegisterTextExtractor((_, format, _, output, _) => output.Write("fast " + format), OfficeFormat.Rtf);
            Assert.Equal("fast Rtf", Office.ExtractText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Disposed_files_refuse_operations()
    {
        var file = new FakeFile(OfficeFormat.Rtf, new OpenContext(), null, null);
        file.Dispose();
        file.Dispose();
        Assert.Throws<ObjectDisposedException>(() => file.ExtractText());
        Assert.True(file.Disposed);
    }

    [Fact]
    public void Defects_reported_to_the_context_are_visible_on_the_file()
    {
        var file = new FakeFile(OfficeFormat.Rtf, new OpenContext(), null, null);
        Assert.Empty(file.Defects);
        file.Report();
        Assert.Equal("test.defect", file.Defects[0].Code);

        var own = new Diagnostics.DefectCollection();
        var withOwnSink = new FakeFile(OfficeFormat.Rtf, new OpenContext(defects: new CustomSink()), null, null);
        Assert.Empty(withOwnSink.Defects);
        Assert.Empty(own);
    }

    private sealed class CustomSink : Diagnostics.IDefectSink
    {
        public void Report(Diagnostics.Defect defect)
        {
        }
    }

    private sealed class FakeFile(OfficeFormat format, OpenContext context, string? path, IByteSource? source) : OfficeFile(format, context, path)
    {
        public bool Disposed { get; private set; }

        public override OfficeKind Kind => OfficeKind.Document;

        public void Report() => Context.Defects.Report(new Diagnostics.Defect(Diagnostics.DefectKind.Value, "test.defect", Diagnostics.DefectSeverity.Repaired, "m", Diagnostics.DefectLocation.None));

        protected override void ExtractTextCore(TextWriter output, CancellationToken cancellationToken) => output.Write("text of " + Format);

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            source?.Dispose();
            base.Dispose(disposing);
        }
    }
}
