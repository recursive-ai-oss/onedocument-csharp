using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using RecursiveAI.OneDocument.Inspection;

namespace RecursiveAI.OneDocument.Cli.Tests;

/// <summary>
/// The sanitiser-coverage review as tests: each adversarial document (<see cref="ReviewScenarios"/>) must be reported by inspection in
/// the categories it was written for, and sanitising it (every removable category, and the expected categories alone) must leave no
/// planted string, no dangling relationship id and a file that still opens. A case with a recorded gap is skipped while it still
/// fails and fails once it passes, so the record cannot go stale.
/// </summary>
public partial class SanitizeReviewTests
{
    static SanitizeReviewTests()
    {
        Documents.DocumentFormats.Register(Formats.FormatRegistry.Default);
        Spreadsheets.SpreadsheetFormats.Register(Formats.FormatRegistry.Default);
        Presentations.PresentationFormats.Register(Formats.FormatRegistry.Default);
    }

    [GeneratedRegex("r:(?:id|embed|link|pict|href|dm|lo|qs|cs)=\"([^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex RelationshipAttribute();

    [GeneratedRegex("Id=\"([^\"]+)\"[^>]*?Target=\"([^\"]+)\"(?:[^>]*TargetMode=\"([^\"]+)\")?", RegexOptions.CultureInvariant)]
    private static partial Regex RelationshipEntry();

    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (ReviewScenario s in ReviewScenarios.All)
        {
            data.Add(s.Name);
        }

        return data;
    }

    private sealed class Outcome
    {
        internal List<string> Problems { get; } = [];

        internal bool Ok => Problems.Count == 0;
    }

    private static Dictionary<string, byte[]> Entries(byte[] zip)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        using var za = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        foreach (ZipArchiveEntry e in za.Entries)
        {
            using Stream s = e.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            result[e.FullName] = ms.ToArray();
        }

        return result;
    }

    private static Outcome Run(ReviewScenario s, SanitizeOptions options, string label)
    {
        var o = new Outcome();
        byte[] sanitized;
        SanitizeReport report;
        try
        {
            using var output = new MemoryStream();
            report = Sanitizer.Sanitize(new MemoryStream(s.Bytes), output, options);
            sanitized = output.ToArray();
        }
        catch (Exception ex) when (ex is OfficeException or InvalidOperationException)
        {
            o.Problems.Add(label + ": sanitising failed: " + ex.GetType().Name + " " + ex.Message);
            return o;
        }

        string marker = "PLANTED-" + s.Name;
        Dictionary<string, byte[]> entries = Entries(sanitized);
        bool keptSomething = s.Kept.Length > 0;
        foreach ((string name, byte[] bytes) in entries)
        {
            if (!keptSomething && Contains(bytes, marker))
            {
                o.Problems.Add(label + ": '" + marker + "' is still in " + name);
            }
        }

        // Every relationship id used in a part must exist in the part's relationships; every internal target must exist.
        foreach ((string name, byte[] bytes) in entries)
        {
            if (!name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".vml", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string relsName = name[..(name.LastIndexOf('/') + 1)] + "_rels/" + name[(name.LastIndexOf('/') + 1)..] + ".rels";
            string rels = entries.TryGetValue(relsName, out byte[]? rb) ? Encoding.UTF8.GetString(rb) : string.Empty;
            foreach (Match m in RelationshipAttribute().Matches(Encoding.UTF8.GetString(bytes)))
            {
                if (!rels.Contains("Id=\"" + m.Groups[1].Value + "\"", StringComparison.Ordinal))
                {
                    o.Problems.Add(label + ": " + name + " uses relationship id " + m.Groups[1].Value + " that no longer exists");
                }
            }
        }

        foreach ((string name, byte[] bytes) in entries)
        {
            if (!name.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string dir = name.Contains("_rels/", StringComparison.Ordinal) ? name[..name.IndexOf("_rels/", StringComparison.Ordinal)] : string.Empty;
            foreach (Match m in RelationshipEntry().Matches(Encoding.UTF8.GetString(bytes)))
            {
                if (m.Groups[3].Value == "External")
                {
                    continue;
                }

                string target = m.Groups[2].Value;
                string full = target.StartsWith('/') ? target[1..] : System.IO.Path.GetFullPath("/" + dir + target).TrimStart('/');
                if (!entries.ContainsKey(full))
                {
                    o.Problems.Add(label + ": " + name + " points at missing part " + full);
                }
            }
        }

        foreach (string gone in s.Gone)
        {
            if (entries.ContainsKey(gone))
            {
                o.Problems.Add(label + ": " + gone + " is still in the file");
            }
        }

        foreach (InspectionCategory c in s.Kept)
        {
            if (options.Includes(c) && report.StatusOf(c) is not (SanitizeStatus.PartlyRemoved or SanitizeStatus.ReportOnly))
            {
                o.Problems.Add(label + ": " + InspectionCategories.Name(c) + " is kept in the file but its status is " + report.StatusOf(c));
            }
        }

        // The result opens, and a second inspection agrees with the report.
        try
        {
            InspectionReport again = Inspector.Inspect(new MemoryStream(sanitized));
            foreach (InspectionCategory c in InspectionCategories.All)
            {
                if (options.Includes(c) && !InspectionCategories.IsReportOnly(c) && !s.Kept.Contains(c) && again.CountOf(c) > 0 && report.StatusOf(c) == SanitizeStatus.Removed)
                {
                    o.Problems.Add(label + ": category " + InspectionCategories.Name(c) + " reported Removed but inspection still finds it");
                }
            }

            string dir = System.IO.Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? System.IO.Path.GetTempPath(), "review-" + s.Name + "-" + label);
            Directory.CreateDirectory(dir);
            string ext = s.Kind switch { OfficeKind.Document => ".docx", OfficeKind.Workbook => ".xlsx", _ => ".pptx" };
            string path = System.IO.Path.Combine(dir, "out" + ext);
            File.WriteAllBytes(path, sanitized);
            File.WriteAllBytes(System.IO.Path.Combine(dir, "in" + ext), s.Bytes);
            _ = Office.ExtractText(path);
            string? keep = Environment.GetEnvironmentVariable("ONEDOCUMENT_REVIEW_OUT");
            if (keep is not null)
            {
                Directory.CreateDirectory(keep);
                File.Copy(path, System.IO.Path.Combine(keep, s.Name + "-" + label + ext), overwrite: true);
                File.Copy(System.IO.Path.Combine(dir, "in" + ext), System.IO.Path.Combine(keep, s.Name + "-in" + ext), overwrite: true);
            }
        }
        catch (Exception ex) when (ex is OfficeException or InvalidOperationException or IOException)
        {
            o.Problems.Add(label + ": the result does not open: " + ex.GetType().Name + " " + ex.Message);
        }

        return o;
    }

    private static bool Contains(byte[] bytes, string text) => bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(text)) >= 0;

    private static Outcome Evaluate(ReviewScenario s)
    {
        var o = new Outcome();
        InspectionReport before = Inspector.Inspect(new MemoryStream(s.Bytes));
        foreach (InspectionCategory c in s.Expect)
        {
            if (before.CountOf(c) == 0)
            {
                o.Problems.Add("inspection does not report " + InspectionCategories.Name(c));
            }
        }

        if (s.ExpectNote is not null && !before.Notes.Any(n => n.Contains(s.ExpectNote, StringComparison.OrdinalIgnoreCase)))
        {
            o.Problems.Add("inspection does not note: " + s.ExpectNote);
        }

        Outcome all = Run(s, SanitizeOptions.Everything, "everything");
        o.Problems.AddRange(all.Problems);
        if (s.Only is not null)
        {
            Outcome only = Run(s, SanitizeOptions.Only(s.Only), "only");
            o.Problems.AddRange(only.Problems);
        }

        return o;
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void An_adversarial_document_is_found_and_cleaned(string name)
    {
        ReviewScenario s = ReviewScenarios.All.Single(x => x.Name == name);
        Outcome o = Evaluate(s);
        string? dump = Environment.GetEnvironmentVariable("ONEDOCUMENT_REVIEW_LOG");
        if (dump is not null)
        {
            File.AppendAllText(dump, name + "\t" + (o.Ok ? "ok" : string.Join(" | ", o.Problems)) + "\n");
        }

        if (s.Gap is not null)
        {
            if (o.Ok)
            {
                Assert.Fail("The recorded gap for " + name + " no longer fails; remove it: " + s.Gap);
            }

            Assert.Skip(s.Gap);
        }

        Assert.True(o.Ok, string.Join(Environment.NewLine, o.Problems));
    }

    [Fact]
    public void A_kept_item_does_not_excuse_other_findings_in_its_category()
    {
        // The provider claims to keep one comment but leaves another behind: the category has a kept item, yet verification must still fail.
        string dir = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath(), "review-verify-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        string input = Path.Combine(dir, "t.docx");
        File.WriteAllBytes(input, ReviewScenarios.All.Single(x => x.Name == "com-orphan").Bytes);
        Formats.FormatRegistry.Default.RegisterInspectionProvider(LeakyProvider.Instance, OfficeKind.Document);
        LeakyProvider.On.Value = true;
        try
        {
            SanitizeVerificationException ex = Assert.Throws<SanitizeVerificationException>(() => Sanitizer.Sanitize(input, Path.Combine(dir, "out.docx")));
            Assert.Equal(InspectionCategory.Comments, ex.Category);
            Assert.Contains(ex.Remaining, f => f.Location == "/word/leaked.xml");
        }
        finally
        {
            LeakyProvider.On.Value = false;
        }

        Assert.False(File.Exists(Path.Combine(dir, "out.docx")));
    }

    private sealed class LeakyProvider : IInspectionProvider
    {
        internal static readonly LeakyProvider Instance = new();
        internal static readonly AsyncLocal<bool> On = new();

        public void Inspect(InspectionSession session)
        {
            if (On.Value)
            {
                session.Add(InspectionCategory.Comments, "/word/leaked.xml", "a comment that stays without a word");
            }
        }

        public void Sanitize(SanitizeSession session)
        {
            if (On.Value)
            {
                session.NotRemoved(InspectionCategory.Comments, "sanitize.referenced", "/word/other.xml", "kept on purpose");
            }
        }
    }

    [Fact]
    public void Unreachable_parts_are_noted_by_inspection_and_dropped_by_sanitising()
    {
        ReviewScenario s = ReviewScenarios.All.Single(x => x.Name == "hdr-sections-and-orphan");
        InspectionReport r = Inspector.Inspect(new MemoryStream(s.Bytes));
        Assert.Contains(r.Notes, n => n.Contains("/word/header9.xml", StringComparison.Ordinal));
        using var output = new MemoryStream();
        SanitizeReport report = Sanitizer.Sanitize(new MemoryStream(s.Bytes), output);
        _ = report;
        Assert.DoesNotContain("header9", string.Join(",", Entries(output.ToArray()).Keys), StringComparison.Ordinal);
    }

    [Fact]
    public void Legacy_files_sanitise_to_ooxml_with_every_default_category_empty()
    {
        string root = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(RepoPath()))!, "..", "..", "testdata");
        int n = 0;
        foreach (string sub in new[] { "doc", "xls", "ppt" })
        {
            foreach (string file in Directory.EnumerateFiles(Path.Combine(root, sub), "*." + sub))
            {
                using var output = new MemoryStream();
                SanitizeReport report;
                try
                {
                    report = Sanitizer.Sanitize(File.OpenRead(file), output);
                }
                catch (OfficeException ex) when (ex is not SanitizeVerificationException)
                {
                    continue; // a file the readers refuse is not this test's business
                }

                InspectionReport after = Inspector.Inspect(new MemoryStream(output.ToArray()));
                foreach (InspectionCategory c in InspectionCategories.Defaults)
                {
                    Assert.True(after.CountOf(c) == 0 || report.StatusOf(c) == SanitizeStatus.PartlyRemoved, Path.GetFileName(file) + ": " + InspectionCategories.Name(c));
                }

                n++;
            }
        }

        Assert.True(n > 20, "legacy fixtures sanitised: " + n);
    }

    private static string RepoPath([System.Runtime.CompilerServices.CallerFilePath] string here = "") => here;

    [Fact]
    public void Text_hidden_by_a_style_goes_and_a_run_that_overrides_it_stays()
    {
        ReviewScenario s = ReviewScenarios.All.Single(x => x.Name == "hid-style");
        InspectionReport r = Inspector.Inspect(new MemoryStream(s.Bytes));
        Assert.Equal(3, r.CountOf(InspectionCategory.HiddenText));
        string dir = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath(), "review-style-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "out.docx");
        using (var output = new MemoryStream())
        {
            Sanitizer.Sanitize(new MemoryStream(s.Bytes), output);
            File.WriteAllBytes(path, output.ToArray());
        }

        string text = Office.ExtractText(path);
        Assert.DoesNotContain("PLANTED", text, StringComparison.Ordinal);
        Assert.Contains("shown-by-override", text, StringComparison.Ordinal);
    }
}
