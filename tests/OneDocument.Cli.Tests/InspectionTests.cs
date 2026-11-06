using System.Security.Cryptography;
using System.Threading;
using System.Text;
using RecursiveAI.OneDocument.Inspection;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Cli.Tests;

public class InspectionTests
{
    static InspectionTests()
    {
        // The kinds register their formats and inspection providers when their assemblies load; make sure they have.
        Documents.DocumentFormats.Register(Formats.FormatRegistry.Default);
        Spreadsheets.SpreadsheetFormats.Register(Formats.FormatRegistry.Default);
        Presentations.PresentationFormats.Register(Formats.FormatRegistry.Default);
        Formats.FormatRegistry.Default.RegisterInspectionProvider(FaultyProvider.Instance, OfficeKind.Document);
    }

    // Behaves as if absent unless a test sets Mode in its own async flow.
    private sealed class FaultyProvider : IInspectionProvider
    {
        internal static readonly FaultyProvider Instance = new();
        internal static readonly AsyncLocal<string?> Mode = new();

        public void Inspect(InspectionSession session)
        {
            if (Mode.Value == "leak")
            {
                session.Add(InspectionCategory.Comments, "/word/leak.xml", "a comment the provider did not remove");
            }
        }

        public void Sanitize(SanitizeSession session)
        {
            if (Mode.Value == "throw")
            {
                throw new InvalidOperationException("markup the provider cannot edit");
            }
        }
    }

    [Fact]
    public void A_category_that_is_not_empty_afterwards_fails_verification_and_writes_nothing()
    {
        string dir = Scratch();
        string input = Write(dir, "t.docx", TortureFiles.Docx());
        string output = Path.Combine(dir, "out.docx");
        FaultyProvider.Mode.Value = "leak";
        SanitizeVerificationException ex;
        try
        {
            ex = Assert.Throws<SanitizeVerificationException>(() => Sanitizer.Sanitize(input, output));
        }
        finally
        {
            FaultyProvider.Mode.Value = null;
        }

        Assert.Equal("sanitize.verification-failed", ex.Defect.Code);
        Assert.Equal(InspectionCategory.Comments, ex.Category);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void Markup_that_cannot_be_edited_is_a_typed_failure_and_writes_nothing()
    {
        string dir = Scratch();
        string input = Write(dir, "t.docx", TortureFiles.Docx());
        string output = Path.Combine(dir, "out.docx");
        FaultyProvider.Mode.Value = "throw";
        OfficeFormatException ex;
        try
        {
            ex = Assert.Throws<OfficeFormatException>(() => Sanitizer.Sanitize(input, output));
        }
        finally
        {
            FaultyProvider.Mode.Value = null;
        }

        Assert.Equal("sanitize.failed", ex.Defect.Code);
        Assert.False(File.Exists(output));
    }

    private static string Scratch()
    {
        string dir = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath(), "inspect-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string Write(string dir, string name, byte[] bytes)
    {
        string path = Path.Combine(dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static string RepoFile(string[] parts, [System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.Combine([Path.GetDirectoryName(here)!, "..", "..", .. parts]);

    /// <summary>Writes the generated documents when ONEDOCUMENT_TORTURE_OUT names a directory (the committed fixtures come from here).</summary>
    [Fact]
    public void The_generated_documents_can_be_written_for_other_tools()
    {
        string? dir = Environment.GetEnvironmentVariable("ONEDOCUMENT_TORTURE_OUT");
        if (dir is null)
        {
            return;
        }

        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "torture.docx"), TortureFiles.Docx());
        File.WriteAllBytes(Path.Combine(dir, "torture.xlsx"), TortureFiles.Xlsx());
        File.WriteAllBytes(Path.Combine(dir, "torture.pptx"), TortureFiles.Pptx());
    }

    private static Dictionary<string, byte[]> Parts(string path)
    {
        using OpcPackage p = OpcPackage.Open(path);
        return p.Parts.ToDictionary(x => x.Name, x => x.GetBytes().ToArray(), StringComparer.OrdinalIgnoreCase);
    }

    private static List<string> Planted(string path)
    {
        var hits = new List<string>();
        foreach ((string name, byte[] bytes) in Parts(path))
        {
            if (Encoding.UTF8.GetString(bytes).Contains("PLANTED", StringComparison.Ordinal))
            {
                hits.Add(name);
            }
        }

        return hits;
    }

    private static InspectionReport RoundTripSanitize(string input, string output, out SanitizeReport sanitize, SanitizeOptions? options = null)
    {
        sanitize = Sanitizer.Sanitize(input, output, options);
        return Inspector.Inspect(output);
    }

    [Fact]
    public void A_word_document_reports_each_category_with_counts_and_locations()
    {
        string dir = Scratch();
        InspectionReport r = Inspector.Inspect(Write(dir, "t.docx", TortureFiles.Docx()));
        Assert.Equal(1, r.CountOf(InspectionCategory.Comments));
        Assert.Equal("/word/comments.xml", r.FindingsOf(InspectionCategory.Comments)[0].Location);
        Assert.Contains("PLANTED-Commenter", r.FindingsOf(InspectionCategory.Comments)[0].Detail, StringComparison.Ordinal);
        Assert.Equal(2, r.CountOf(InspectionCategory.Revisions));
        Assert.Equal(9, r.CountOf(InspectionCategory.Properties));
        Assert.Equal(5, r.CountOf(InspectionCategory.PersonalInformation));
        Assert.Equal(2, r.CountOf(InspectionCategory.CustomXml));
        Assert.Equal(2, r.CountOf(InspectionCategory.HeadersFooters));
        Assert.Equal(1, r.CountOf(InspectionCategory.HiddenText));
        Assert.Equal(1, r.CountOf(InspectionCategory.InvisibleContent));
        Assert.Equal(1, r.CountOf(InspectionCategory.CollapsedHeadings));
        Assert.Equal(1, r.CountOf(InspectionCategory.EmbeddedObjects));
        Assert.Equal(1, r.CountOf(InspectionCategory.RemoteTemplates));
        Assert.Equal(1, r.CountOf(InspectionCategory.LinkedContent));
        Assert.Equal(2, r.CountOf(InspectionCategory.WebExtensions));
        Assert.Equal(1, r.CountOf(InspectionCategory.Thumbnails));
        Assert.Contains(r.Tips, t => t.Contains("white text", StringComparison.Ordinal));
        Assert.Equal(0, r.CountOf(InspectionCategory.Macros));
    }

    [Fact]
    public void Sanitising_a_word_document_leaves_every_removed_category_empty_and_no_planted_string()
    {
        string dir = Scratch();
        string input = Write(dir, "t.docx", TortureFiles.Docx());
        string output = Path.Combine(dir, "clean.docx");
        InspectionReport after = RoundTripSanitize(input, output, out SanitizeReport s);
        foreach (InspectionCategory c in InspectionCategories.Defaults)
        {
            Assert.Equal(0, after.CountOf(c));
        }

        Assert.Empty(s.NotRemoved);
        Assert.Equal(SanitizeStatus.NotSelected, s.StatusOf(InspectionCategory.EmbeddedObjects));
        Assert.Equal(1, after.CountOf(InspectionCategory.EmbeddedObjects));
        Assert.Equal(1, after.CountOf(InspectionCategory.LinkedContent));
        Assert.Empty(Planted(output));
        Assert.Equal(OfficeFormat.Docx, s.OutputFormat);
        string text = Office.ExtractText(output);
        Assert.Contains("Visible text", text, StringComparison.Ordinal);
        Assert.Contains("inserted", text, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden", text, StringComparison.Ordinal);
        Assert.Contains("shown", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Untouched_parts_stay_byte_identical_and_the_input_is_not_changed()
    {
        string dir = Scratch();
        string input = Write(dir, "t.docx", TortureFiles.Docx());
        string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input)));
        string output = Path.Combine(dir, "clean.docx");
        Sanitizer.Sanitize(input, output);
        Assert.Equal(hash, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input))));
        Dictionary<string, byte[]> before = Parts(input);
        Dictionary<string, byte[]> after = Parts(output);
        Assert.True(after["/word/styles.xml"].AsSpan().SequenceEqual(before["/word/styles.xml"]));
        Assert.False(after.ContainsKey("/word/media/p.png")); // only the hidden picture used it
    }

    [Fact]
    public void Selected_categories_only_are_removed()
    {
        string dir = Scratch();
        string input = Write(dir, "t.docx", TortureFiles.Docx());
        string output = Path.Combine(dir, "clean.docx");
        InspectionReport after = RoundTripSanitize(input, output, out SanitizeReport s, SanitizeOptions.Except(InspectionCategory.Comments));
        Assert.Equal(1, after.CountOf(InspectionCategory.Comments));
        Assert.Equal(SanitizeStatus.NotSelected, s.StatusOf(InspectionCategory.Comments));
        Assert.Equal(0, after.CountOf(InspectionCategory.Revisions));
        Assert.Equal(["/word/comments.xml"], Planted(output).ToArray());
    }

    [Fact]
    public void A_workbook_reports_and_sanitises_every_category()
    {
        string dir = Scratch();
        string input = Write(dir, "t.xlsx", TortureFiles.Xlsx());
        InspectionReport r = Inspector.Inspect(input);
        Assert.Equal(2, r.CountOf(InspectionCategory.Comments));
        Assert.Equal(1, r.CountOf(InspectionCategory.Revisions));
        Assert.Equal(2, r.CountOf(InspectionCategory.HiddenSheets));
        Assert.Equal(1, r.CountOf(InspectionCategory.HiddenNames));
        Assert.Equal(2, r.CountOf(InspectionCategory.HiddenRowsColumns));
        Assert.Equal(1, r.CountOf(InspectionCategory.HeadersFooters));
        Assert.Equal(1, r.CountOf(InspectionCategory.ExternalLinks));
        Assert.Equal(1, r.CountOf(InspectionCategory.DataConnections));
        Assert.Equal(1, r.CountOf(InspectionCategory.InvisibleContent));
        Assert.Equal(4, r.CountOf(InspectionCategory.PersonalInformation)); // person part, printer settings, file-sharing user, absolute path
        Assert.Equal(8, r.CountOf(InspectionCategory.Properties));

        string output = Path.Combine(dir, "clean.xlsx");
        InspectionReport after = RoundTripSanitize(input, output, out SanitizeReport s, SanitizeOptions.Everything);
        foreach (InspectionCategory c in InspectionCategories.Everything)
        {
            Assert.True(after.CountOf(c) == 0, c + " still has " + after.CountOf(c));
        }

        Assert.Empty(s.NotRemoved);
        Assert.Empty(Planted(output));
        Dictionary<string, byte[]> parts = Parts(output);
        Assert.False(parts.ContainsKey("/xl/worksheets/sheet2.xml"));
        Assert.False(parts.ContainsKey("/xl/worksheets/sheet3.xml"));
        Assert.False(parts.ContainsKey("/xl/calcChain.xml"));
        string wb = Encoding.UTF8.GetString(parts["/xl/workbook.xml"]);
        Assert.DoesNotContain("Secret", wb, StringComparison.Ordinal);
        Assert.Contains("name=\"Last\"", wb, StringComparison.Ordinal);
        // The print area of "Last" was scoped to sheet position 3 and now to position 1.
        Assert.Contains("localSheetId=\"1\"", wb, StringComparison.Ordinal);
        Assert.DoesNotContain("externalReference", wb, StringComparison.Ordinal);
        string sheet1 = Encoding.UTF8.GetString(parts["/xl/worksheets/sheet1.xml"]);
        Assert.DoesNotContain("<f>[1]", sheet1, StringComparison.Ordinal);
        Assert.Contains("<v>42</v>", sheet1, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden=", sheet1, StringComparison.Ordinal);
        Assert.DoesNotContain("legacyDrawing", sheet1, StringComparison.Ordinal);
        Assert.Contains("<f>SUM(B1:B1)</f>", sheet1, StringComparison.Ordinal);
        Assert.Contains("pageSetup", sheet1, StringComparison.Ordinal);
        Assert.DoesNotContain("r:id=\"rIdPr\"", sheet1, StringComparison.Ordinal);
    }

    [Fact]
    public void A_hidden_sheet_that_a_formula_reads_is_kept_and_reported()
    {
        string dir = Scratch();
        string input = Write(dir, "t.xlsx", TortureFiles.Xlsx(referenceHiddenSheet: true));
        string output = Path.Combine(dir, "clean.xlsx");
        InspectionReport after = RoundTripSanitize(input, output, out SanitizeReport s, SanitizeOptions.Everything);
        Assert.Equal(SanitizeStatus.PartlyRemoved, s.StatusOf(InspectionCategory.HiddenSheets));
        NotRemovedItem kept = Assert.Single(s.NotRemoved, n => n.Category == InspectionCategory.HiddenSheets);
        Assert.Equal("sanitize.referenced", kept.Code);
        Assert.Equal("workbook!Secret", kept.Location);
        Assert.Equal(1, after.CountOf(InspectionCategory.HiddenSheets));
        Assert.Contains("name=\"Secret\"", Encoding.UTF8.GetString(Parts(output)["/xl/workbook.xml"]), StringComparison.Ordinal);
    }

    [Fact]
    public void A_presentation_reports_and_sanitises_every_category()
    {
        string dir = Scratch();
        string input = Write(dir, "t.pptx", TortureFiles.Pptx());
        InspectionReport r = Inspector.Inspect(input);
        Assert.Equal(4, r.CountOf(InspectionCategory.Comments)); // a classic and a modern comment, and one author part for each
        Assert.Equal(2, r.CountOf(InspectionCategory.SpeakerNotes));
        Assert.Equal(1, r.CountOf(InspectionCategory.HiddenSlides));
        Assert.Equal(1, r.CountOf(InspectionCategory.InvisibleContent));
        Assert.Equal(1, r.CountOf(InspectionCategory.OffSlideContent));
        Assert.Equal(1, r.CountOf(InspectionCategory.Ink));

        string output = Path.Combine(dir, "clean.pptx");
        InspectionReport after = RoundTripSanitize(input, output, out SanitizeReport s, SanitizeOptions.Everything);
        foreach (InspectionCategory c in InspectionCategories.Everything)
        {
            Assert.True(after.CountOf(c) == 0, c + " still has " + after.CountOf(c));
        }

        Assert.Empty(s.NotRemoved);
        Assert.Empty(Planted(output));
        Dictionary<string, byte[]> parts = Parts(output);
        Assert.False(parts.ContainsKey("/ppt/slides/slide2.xml"));
        Assert.False(parts.ContainsKey("/ppt/ink/ink1.xml"));
        string pres = Encoding.UTF8.GetString(parts["/ppt/presentation.xml"]);
        Assert.DoesNotContain("rIdS2", pres, StringComparison.Ordinal);
        Assert.Contains("rIdS1", pres, StringComparison.Ordinal);
        string slide1 = Encoding.UTF8.GetString(parts["/ppt/slides/slide1.xml"]);
        Assert.Contains("Visible slide", slide1, StringComparison.Ordinal);
        Assert.DoesNotContain("extLst", slide1, StringComparison.Ordinal);
        Assert.DoesNotContain("AlternateContent", slide1, StringComparison.Ordinal);
    }

    [Fact]
    public void A_macro_enabled_file_is_written_macro_free_and_says_so()
    {
        string dir = Scratch();
        string input = RepoFile(["testdata", "vba", "lo-roundtrip.docm"]);
        string output = Path.Combine(dir, "clean.docx");
        InspectionReport before = Inspector.Inspect(input);
        Assert.True(before.CountOf(InspectionCategory.Macros) > 0);
        SanitizeReport s = Sanitizer.Sanitize(input, output, SanitizeOptions.Only(InspectionCategory.Macros));
        Assert.Equal(OfficeFormat.Docx, s.OutputFormat);
        Assert.Equal(OfficeFormat.Docx, Office.Detect(output));
        Assert.Equal(0, Inspector.Inspect(output).CountOf(InspectionCategory.Macros));
        Assert.Equal(SanitizeStatus.Removed, s.StatusOf(InspectionCategory.Macros));
    }

    [Fact]
    public void A_legacy_file_is_inspected_through_the_hub_and_sanitised_to_ooxml()
    {
        string dir = Scratch();
        string input = RepoFile(["testdata", "doc", "revisions.doc"]);
        InspectionReport r = Inspector.Inspect(input);
        Assert.True(r.WasConverted);
        Assert.Equal(OfficeFormat.Doc, r.SourceFormat);
        Assert.Equal(OfficeFormat.Docx, r.Format);
        Assert.True(r.CountOf(InspectionCategory.Revisions) > 0);
        string output = Path.Combine(dir, "clean.docx");
        SanitizeReport s = Sanitizer.Sanitize(input, output);
        Assert.Equal(OfficeFormat.Docx, s.OutputFormat);
        Assert.Equal(OfficeFormat.Docx, Office.Detect(output));
        Assert.Equal(0, Inspector.Inspect(output).CountOf(InspectionCategory.Revisions));

        string ppt = RepoFile(["testdata", "ppt", "lo-slide-slides-hidden-notes.ppt"]);
        InspectionReport pr = Inspector.Inspect(ppt);
        Assert.True(pr.CountOf(InspectionCategory.HiddenSlides) > 0);
        Assert.True(pr.CountOf(InspectionCategory.SpeakerNotes) > 0);
        string pout = Path.Combine(dir, "clean.pptx");
        Sanitizer.Sanitize(ppt, pout, SanitizeOptions.Everything);
        InspectionReport pa = Inspector.Inspect(pout);
        Assert.Equal(0, pa.CountOf(InspectionCategory.HiddenSlides));
        Assert.Equal(0, pa.CountOf(InspectionCategory.SpeakerNotes));
    }

    [Fact]
    public void The_defaults_leave_what_office_does_not_remove()
    {
        string dir = Scratch();
        string output = Path.Combine(dir, "clean.xlsx");
        InspectionReport after = RoundTripSanitize(Write(dir, "t.xlsx", TortureFiles.Xlsx()), output, out SanitizeReport s);
        Assert.Equal(1, after.CountOf(InspectionCategory.HiddenNames));
        Assert.Equal(1, after.CountOf(InspectionCategory.ExternalLinks));
        Assert.Equal(SanitizeStatus.NotSelected, s.StatusOf(InspectionCategory.HiddenNames));
        // The hidden name reads the hidden sheet, so that sheet has to stay; the other one goes.
        Assert.Equal(SanitizeStatus.PartlyRemoved, s.StatusOf(InspectionCategory.HiddenSheets));
        Assert.Equal(1, after.CountOf(InspectionCategory.HiddenSheets));

        string pptx = Path.Combine(dir, "clean.pptx");
        InspectionReport pa = RoundTripSanitize(Write(dir, "t.pptx", TortureFiles.Pptx()), pptx, out _);
        Assert.Equal(1, pa.CountOf(InspectionCategory.HiddenSlides));
    }

    [Fact]
    public void The_command_line_prints_reports_and_writes_the_file()
    {
        string dir = Scratch();
        string input = Write(dir, "t.docx", TortureFiles.Docx());
        string output = Path.Combine(dir, "clean.docx");
        var err = new StringWriter();
        Assert.Equal(0, InspectCommand.Sanitize([input, output, "--keep", "comments,hidden-text"], err));
        Assert.Equal(1, Inspector.Inspect(output).CountOf(InspectionCategory.Comments));
        Assert.Equal(1, Inspector.Inspect(output).CountOf(InspectionCategory.HiddenText));
        Assert.Equal(2, InspectCommand.Sanitize([input, output, "--keep", "nonsense"], err));
        Assert.Equal(1, InspectCommand.Inspect([Path.Combine(dir, "missing.docx")], err));
    }

    [Fact]
    public void Garbage_is_a_typed_failure_and_writes_nothing()
    {
        string dir = Scratch();
        string input = Write(dir, "bad.docx", [1, 2, 3, 4, 5]);
        string output = Path.Combine(dir, "out.docx");
        Assert.Throws<OfficeFormatException>(() => Sanitizer.Sanitize(input, output));
        Assert.False(File.Exists(output));
    }
}
