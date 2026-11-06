using System.Net.Http;
using System.Text;
using System.Text.Json;
using RecursiveAI.OneDocument.Documents.Markdown;

namespace RecursiveAI.OneDocument.Documents.Tests;

/// <summary>
/// Runs the examples of the published CommonMark 0.31.2 and GitHub Flavored Markdown specifications against the parser. The specification
/// files are fetched when the test runs (or read from <c>ONEDOCUMENT_SPEC_DIR</c> when it is set) and are never kept in the repository.
/// The tree is rendered to HTML in the shape the specifications print (see <see cref="MdHtml"/>) and compared after white space between
/// tags is ignored. The failures, by section, go to the test output and to <c>ONEDOCUMENT_ARTIFACTS</c> when it is set.
/// </summary>
public sealed class MarkdownSpecTests
{
    private const string CommonMarkUrl = "https://spec.commonmark.org/0.31.2/spec.json";
    private const string GfmUrl = "https://github.github.com/gfm/";

    private static string? Fetch(string url, string name)
    {
        string? dir = Environment.GetEnvironmentVariable("ONEDOCUMENT_SPEC_DIR") ?? Environment.GetEnvironmentVariable("TMPDIR");
        string? cached = dir is null ? null : Path.Combine(dir, name);
        if (cached is not null && File.Exists(cached))
        {
            return File.ReadAllText(cached);
        }

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            string text = http.GetStringAsync(url).GetAwaiter().GetResult();
            if (cached is not null)
            {
                Directory.CreateDirectory(dir!);
                File.WriteAllText(cached, text);
            }

            return text;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return null;
        }
    }

    private sealed record Example(int Number, string Section, string Markdown, string Html);

    private static List<Example> Load(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        var list = new List<Example>();
        foreach (JsonElement e in doc.RootElement.EnumerateArray())
        {
            list.Add(new Example(e.GetProperty("example").GetInt32(), e.GetProperty("section").GetString()!, e.GetProperty("markdown").GetString()!, e.GetProperty("html").GetString()!));
        }

        return list;
    }

    // The GFM specification is published as a page, not as JSON: each example is a block with a Markdown column and an HTML column,
    // and the section is the last heading before it.
    private static List<Example> LoadGfmHtml(string page)
    {
        var list = new List<Example>();
        var tokens = System.Text.RegularExpressions.Regex.Matches(page,
            "<h[1-6][^>]*>(?<h>.*?)</h[1-6]>|<div class=\"example\" id=\"example-(?<n>\\d+)\">(?<body>.*?)</code></pre>\\s*</div>\\s*</div>",
            System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(20));
        string section = string.Empty;
        foreach (System.Text.RegularExpressions.Match m in tokens)
        {
            if (m.Groups["h"].Success)
            {
                string h = System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(m.Groups["h"].Value, "<[^>]*>", string.Empty));
                section = System.Text.RegularExpressions.Regex.Replace(h.Trim(), @"^[\d.]+\s*", string.Empty);
                continue;
            }

            string body = m.Groups["body"].Value;
            int md = body.IndexOf("<code class=\"language-markdown\">", StringComparison.Ordinal);
            int html = body.IndexOf("<code class=\"language-html\">", StringComparison.Ordinal);
            if (md < 0 || html < 0)
            {
                continue;
            }

            string Text(string s, int from, int to)
            {
                string raw = s[from..to];
                raw = raw.Replace("<span class=\"space\"> </span>", " ", StringComparison.Ordinal);
                raw = System.Text.RegularExpressions.Regex.Replace(raw, "<span class=\"tab\">[^<]*</span>", "\t");
                raw = System.Text.RegularExpressions.Regex.Replace(raw, "<[^>]*>", string.Empty);
                return System.Net.WebUtility.HtmlDecode(raw);
            }

            int mdStart = md + "<code class=\"language-markdown\">".Length;
            int mdEnd = body.IndexOf("</code>", mdStart, StringComparison.Ordinal);
            int htmlStart = html + "<code class=\"language-html\">".Length;
            list.Add(new Example(int.Parse(m.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture), section, Text(body, mdStart, mdEnd), Text(body, htmlStart, body.Length)));
        }

        return list;
    }

    private static (int Passed, int Total, string Report) Run(IEnumerable<Example> examples, MarkdownParserOptions options, bool gfm)
    {
        int passed = 0;
        int total = 0;
        var report = new StringBuilder();
        var bySection = new SortedDictionary<string, List<int>>(StringComparer.Ordinal);
        foreach (Example ex in examples)
        {
            total++;
            string actual;
            try
            {
                MarkdownParseResult result = MarkdownParser.Parse(ex.Markdown, options);
                actual = MdHtml.Render(result.Root, gfm);
            }
            catch (Exception e)
            {
                actual = "EXCEPTION " + e.GetType().Name + ": " + e.Message;
            }

            if (MdHtml.Normalize(actual) == MdHtml.Normalize(ex.Html))
            {
                passed++;
            }
            else
            {
                if (!bySection.TryGetValue(ex.Section, out List<int>? l))
                {
                    bySection[ex.Section] = l = [];
                }

                l.Add(ex.Number);
                report.Append("--- example ").Append(ex.Number).Append(" (").Append(ex.Section).Append(")\n").Append(ex.Markdown.Replace("\t", "→")).Append("=== expected\n").Append(ex.Html).Append("=== actual\n").Append(actual).Append('\n');
            }
        }

        var summary = new StringBuilder();
        summary.Append(passed).Append('/').Append(total).Append(" passed\n");
        foreach ((string section, List<int> numbers) in bySection)
        {
            summary.Append("  ").Append(section).Append(": ").Append(string.Join(",", numbers)).Append('\n');
        }

        return (passed, total, summary.ToString() + "\n" + report);
    }

    private static void Save(string name, string text)
    {
        string? dir = Environment.GetEnvironmentVariable("ONEDOCUMENT_ARTIFACTS");
        if (dir is not null)
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, name), text);
        }
    }

    [Fact]
    public void CommonMarkExamples()
    {
        string? json = Fetch(CommonMarkUrl, "commonmark-0.31.2-spec.json");
        if (json is null)
        {
            Assert.Skip("The CommonMark specification could not be fetched.");
        }

        (int passed, int total, string report) = Run(Load(json!), MarkdownParserOptions.CommonMark, gfm: false);
        Save("commonmark-spec.txt", report);
        Assert.True(total >= 650, "examples read: " + total);
        Assert.True(total - passed <= CommonMarkAllowedFailures, report[..Math.Min(report.Length, 6000)]);
    }

    // Examples that need the full HTML5 entity table or HTML serialisation details this parser does not reproduce are listed in docs/markdown.md.
    private const int CommonMarkAllowedFailures = 0;
    private const int GfmAllowedFailures = 0;

    [Fact]
    public void GfmExtensionExamples()
    {
        string? json = Fetch(GfmUrl, "gfm-spec.html");
        if (json is null)
        {
            Assert.Skip("The GFM specification could not be fetched.");
        }

        string[] extensions = ["Tables (extension)", "Task list items (extension)", "Strikethrough (extension)", "Autolinks (extension)", "Disallowed Raw HTML (extension)"];
        (int passed, int total, string report) = Run(LoadGfmHtml(json!).Where(e => extensions.Contains(e.Section, StringComparer.Ordinal)), MarkdownParserOptions.Gfm, gfm: true);
        Save("gfm-spec.txt", report);
        Assert.True(total >= 25, "examples read: " + total);
        Assert.True(total - passed <= GfmAllowedFailures, report[..Math.Min(report.Length, 6000)]);
    }

    static MarkdownSpecTests() => DocumentFormats.Register(RecursiveAI.OneDocument.Formats.FormatRegistry.Default);

    // Every example of the CommonMark specification goes into a document, out as Markdown and into a document again; the two documents must read the same.
    // Examples whose input is raw HTML, or whose meaning depends on things Word has no way to say (a heading inside a list item beside other blocks…), differ and are listed.
    [Fact]
    public void SpecExamplesSurviveDocumentRoundTrip()
    {
        string? json = Fetch(CommonMarkUrl, "commonmark-0.31.2-spec.json");
        if (json is null)
        {
            Assert.Skip("The CommonMark specification could not be fetched.");
        }

        var failures = new StringBuilder();
        int checkedCount = 0;
        int differ = 0;
        int crashed = 0;
        foreach (Example ex in Load(json!))
        {
            checkedCount++;
            try
            {
                using Document first = Document.ParseMarkdown(ex.Markdown);
                string md = first.ToMarkdown();
                using Document second = Document.ParseMarkdown(md);
                string a = MarkdownConversionTests.Signature(first);
                string b = MarkdownConversionTests.Signature(second);
                if (a != b)
                {
                    differ++;
                    failures.Append("--- example ").Append(ex.Number).Append(" (").Append(ex.Section).Append(")\n").Append(ex.Markdown.Replace("\t", "→")).Append("=== exported\n").Append(md).Append("=== first\n").Append(a).Append("=== second\n").Append(b).Append('\n');
                }
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                crashed++;
                failures.Append("--- example ").Append(ex.Number).Append(" CRASH ").Append(e.GetType().Name).Append(": ").Append(e.Message).Append('\n');
            }
        }

        Save("roundtrip-spec.txt", checkedCount + " checked, " + differ + " differ, " + crashed + " crashed\n" + failures);
        Assert.True(crashed == 0, failures.ToString()[..Math.Min(failures.Length, 4000)]);
        Assert.True(differ <= RoundTripAllowedDifferences, checkedCount + " checked, " + differ + " differ\n" + failures.ToString()[..Math.Min(failures.Length, 4000)]);
    }

    private const int RoundTripAllowedDifferences = 10;
}
