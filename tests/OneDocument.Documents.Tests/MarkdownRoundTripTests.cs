using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace RecursiveAI.OneDocument.Documents.Tests;

/// <summary>
/// Word documents written by other applications and by us, taken to Markdown and back: the second hub must agree with the first on
/// text, headings, lists, tables, code and links. Formatting, fonts, headers, images and notes are not compared; they are
/// documented as dropped or moved by the exporter.
/// </summary>
public sealed partial class MarkdownRoundTripTests
{
    static MarkdownRoundTripTests() => DocumentFormats.Register(RecursiveAI.OneDocument.Formats.FormatRegistry.Default);

    private static string TestData([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "testdata"));

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    private static string Norm(string s) => Spaces().Replace(s, " ").Trim();

    // The comparable view: one line per block. Headings by level, list items, code lines, paragraphs, tables by cell, link targets.
    // Everything is compared as whitespace-normalised text, because Markdown has no way to keep runs of spaces or tabs.
    internal static List<string> Features(Document doc)
    {
        var labelled = new HashSet<BlockElement>();
        foreach (var label in doc.Numbering.LabelBody())
        {
            labelled.Add(label.Paragraph);
        }

        var lines = new List<string>();
        var links = new List<string>();
        void Blocks(IEnumerable<BlockElement> blocks)
        {
            foreach (BlockElement b in blocks)
            {
                switch (b)
                {
                    case Paragraph p:
                        string text = Norm(p.Text);
                        foreach (Hyperlink h in p.Inlines().OfType<Hyperlink>())
                        {
                            links.Add(h.Anchor is { } a ? "#" + a : doc.Package.GetRelationships(doc.MainPartName).FirstOrDefault(r => r.Id == h.RelationshipId)?.Target ?? "?");
                        }

                        if (text.Length == 0)
                        {
                            break;
                        }

                        Match m = HeadingId().Match(p.StyleId ?? string.Empty);
                        if (m.Success)
                        {
                            lines.Add("H" + m.Groups[1].Value + " " + text);
                        }
                        else if (p.StyleId == "SourceCode")
                        {
                            lines.Add("C " + text);
                        }
                        else if (labelled.Contains(p))
                        {
                            lines.Add("L " + text);
                        }
                        else
                        {
                            lines.Add("P " + text);
                        }

                        break;
                    case Table t:
                        foreach (TableRow row in t.Rows())
                        {
                            // A merged cell comes back as the cell plus an empty one: Markdown tables have no spans. Empty cells are left out of the comparison.
                            lines.Add("T " + string.Join(" | ", row.Cells().Select(c => Norm(c.Text)).Where(c => c.Length > 0)));
                        }

                        break;
                }
            }
        }

        Blocks(doc.Body.Blocks());
        // Links with a script scheme are written to Markdown but read back as plain text, by design (the importer's scheme allow-list).
        lines.AddRange(links.Where(l => !l.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)).Select(l => "link " + l));
        return lines;
    }

    [GeneratedRegex(@"!\[[^\]]*\]")]
    private static partial Regex ImageAlt();

    [GeneratedRegex(@"^Heading([1-9])$")]
    private static partial Regex HeadingId();

    // The documents: the Word files we committed (HTML, template and legacy inputs) and one we write ourselves from Markdown.
    public static IEnumerable<object[]> Inputs()
    {
        string data = TestData();
        yield return [Path.Combine(data, "html", "report.docx")];
        foreach (string f in Directory.GetFiles(Path.Combine(data, "templates", "docx"), "*.docx", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            yield return [f];
        }

        foreach (string f in Directory.GetFiles(Path.Combine(data, "doc"), "*.doc").OrderBy(f => f, StringComparer.Ordinal))
        {
            yield return [f];
        }
    }

    // Two hubs agree when every line the first has appears in the second in the same order. The second may have fewer lines only for
    // content with no Markdown form (empty cells collapse, tables-in-tables flatten); those cases are listed in Explained.
    private static readonly Dictionary<string, (string Diff, string Why)> Explained = new(StringComparer.Ordinal)
    {
        ["hyperlink/template.docx"] = ("  1: <none>  !=  link mailto:hi@example.com\n",
            "the text contains mailto:hi@example.com, which GFM's extended autolink rule turns into a link on read"),
        ["section-repeat/template.docx"] = ("  1: H1 Chapter {{ c }}  !=  P Chapter {{ c }}\n",
            "the file uses Heading1 without a styles part, so the style is undefined and the exporter does not treat the paragraph as a heading"),
        ["doc/basic.doc"] = ("  8: <none>  !=  link https://example.com/\n",
            "a legacy-file hyperlink is a field in the hub, not a link inline; the exporter writes it as a link"),
    };

    [Theory]
    [MemberData(nameof(Inputs))]
    public void WordDocumentsKeepTextHeadingsListsTablesCodeAndLinksThroughMarkdown(string path)
    {
        using Document original = Document.Open(path);
        string md = original.ToMarkdown();
        // Alt text is not body text: blank it so the comparison sees what the original's picture paragraph held.
        using Document back = Document.ParseMarkdown(ImageAlt().Replace(md, "![]"));
        List<string> a = Features(original);
        List<string> b = Features(back);
        string name = Path.GetFileName(Path.GetDirectoryName(path)!) + "/" + Path.GetFileName(path);
        string diff = Diff(a, b);
        if (Explained.TryGetValue(name, out var expected))
        {
            Assert.True(expected.Diff == diff, name + " (" + expected.Why + ") now differs as:\n" + diff);
            return;
        }

        Assert.True(diff.Length == 0, name + ":\n" + diff + "\n--- markdown ---\n" + (md.Length > 2000 ? md[..2000] : md));
    }

    private static string Diff(List<string> a, List<string> b)
    {
        // Link lines are compared as sets in order of appearance; block lines in order.
        var sb = new StringBuilder();
        int n = Math.Max(a.Count, b.Count);
        for (int i = 0; i < n; i++)
        {
            string x = i < a.Count ? a[i] : "<none>";
            string y = i < b.Count ? b[i] : "<none>";
            if (x != y)
            {
                sb.Append("  ").Append(i).Append(": ").Append(x).Append("  !=  ").Append(y).Append('\n');
                if (sb.Length > 1500)
                {
                    break;
                }
            }
        }

        return sb.ToString();
    }

    [Fact]
    public void ADocumentWrittenFromMarkdownComesBackEqualThroughDocxAndMarkdown()
    {
        const string md = "# Title\n\nText with a [link](https://example.com/x) and `code`.\n\n## Lists\n\n- one\n- two\n  - nested\n\n1. first\n2. second\n\n```\nline 1\nline 2\n```\n\n| A | B |\n|---|---|\n| 1 | 2 |\n";
        using Document first = Document.ParseMarkdown(md);
        using var ms = new MemoryStream();
        first.SaveAs(ms, OfficeFormat.Docx);
        ms.Position = 0;
        using Document docx = Document.Open(ms);
        using Document second = Document.ParseMarkdown(docx.ToMarkdown());
        Assert.Equal(Features(first), Features(docx));
        Assert.Equal(Features(first), Features(second));
    }
}
