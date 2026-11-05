using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Templating;

namespace RecursiveAI.OneDocument.Documents.Tests;

/// <summary>
/// Renders every template of the corpus under <c>testdata/templates/docx/</c> and compares the result part by part: parts listed in the case's
/// <c>expected</c> folder must match byte for byte, and every other part must be byte-identical to the template's. Set
/// <c>ONEDOCUMENT_UPDATE_GOLDEN=1</c> to rewrite the corpus from <see cref="TemplateCase"/> and the current output.
/// </summary>
public class DocxTemplateGoldenTests
{
    private static string Dir([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "testdata", "templates", "docx"));

    public static IEnumerable<TheoryDataRow<string>> Names() => TemplateCase.All.Select(c => new TheoryDataRow<string>(c.Name));

    private static string Scratch() => Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath(), "docx-golden");

    private static (Dictionary<string, byte[]> Parts, List<string> Warnings) RenderCase(string template, string json, string output, bool strict)
    {
        DocumentFormats.Register(FormatRegistry.Default);
        using JsonDocument data = JsonDocument.Parse(json);
        var warnings = Template.Render(template, data, output, new TemplateOptions { Strict = strict });
        return (ReadParts(File.ReadAllBytes(output)), [.. warnings.Select(w => w.Code).Order(StringComparer.Ordinal)]);
    }

    internal static Dictionary<string, byte[]> ReadParts(byte[] docx)
    {
        using OpcPackage package = OpcPackage.Open(new MemoryStream(docx));
        var parts = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (IPackagePart p in package.Parts)
        {
            parts[p.Name] = p.GetBytes().ToArray();
        }

        foreach (string source in new[] { string.Empty }.Concat(package.Parts.Select(p => p.Name)))
        {
            IReadOnlyList<Relationship> rels = package.GetRelationships(source.Length == 0 ? null : source);
            parts["rels:" + source] = Encoding.UTF8.GetBytes(string.Join("\n", rels.Select(r => r.Id + " " + r.Type + " " + r.Target + " " + r.TargetMode)));
        }

        return parts;
    }

    private static string FileName(string part) => part.TrimStart('/').Replace('/', '~').Replace(':', '~');

    [Theory]
    [MemberData(nameof(Names))]
    public void Rendering_a_template_gives_the_committed_output(string name)
    {
        TemplateCase c = TemplateCase.All.First(x => x.Name == name);
        string dir = Path.Combine(Dir(), name);
        string scratch = Scratch();
        Directory.CreateDirectory(scratch);
        string output = Path.Combine(scratch, name + ".out.docx");

        if (Environment.GetEnvironmentVariable("ONEDOCUMENT_UPDATE_GOLDEN") == "1")
        {
            Directory.CreateDirectory(Path.Combine(dir, "expected"));
            foreach (string old in Directory.GetFiles(Path.Combine(dir, "expected")))
            {
                File.Delete(old);
            }

            File.WriteAllBytes(Path.Combine(dir, "template.docx"), c.Build());
            File.WriteAllText(Path.Combine(dir, "data.json"), c.Json);
            (Dictionary<string, byte[]> outParts, List<string> warn) = RenderCase(Path.Combine(dir, "template.docx"), c.Json, output, c.Strict);
            Dictionary<string, byte[]> inParts = ReadParts(File.ReadAllBytes(Path.Combine(dir, "template.docx")));
            foreach (KeyValuePair<string, byte[]> kv in outParts)
            {
                if (!inParts.TryGetValue(kv.Key, out byte[]? before) || !before.AsSpan().SequenceEqual(kv.Value))
                {
                    File.WriteAllBytes(Path.Combine(dir, "expected", FileName(kv.Key)), kv.Value);
                }
            }

            File.WriteAllLines(Path.Combine(dir, "expected", "warnings.txt"), warn);
            return;
        }

        string templatePath = Path.Combine(dir, "template.docx");
        Assert.True(File.Exists(templatePath), "Missing committed template for " + name + "; run with ONEDOCUMENT_UPDATE_GOLDEN=1.");
        (Dictionary<string, byte[]> actual, List<string> warnings) = RenderCase(templatePath, File.ReadAllText(Path.Combine(dir, "data.json")), output, c.Strict);
        Dictionary<string, byte[]> original = ReadParts(File.ReadAllBytes(templatePath));
        string expectedDir = Path.Combine(dir, "expected");

        foreach (KeyValuePair<string, byte[]> kv in actual)
        {
            string expectedFile = Path.Combine(expectedDir, FileName(kv.Key));
            if (File.Exists(expectedFile))
            {
                Assert.True(File.ReadAllBytes(expectedFile).AsSpan().SequenceEqual(kv.Value), name + ": part " + kv.Key + " differs from expected. Actual:\n" + Encoding.UTF8.GetString(kv.Value));
            }
            else
            {
                Assert.True(original.TryGetValue(kv.Key, out byte[]? before) && before.AsSpan().SequenceEqual(kv.Value), name + ": part " + kv.Key + " changed but has no expected output.");
            }
        }

        foreach (string kv in original.Keys)
        {
            Assert.True(actual.ContainsKey(kv), name + ": part " + kv + " disappeared.");
        }

        Assert.Equal(File.ReadAllLines(Path.Combine(expectedDir, "warnings.txt")), warnings);
    }

    [Fact]
    public void The_corpus_has_at_least_twenty_five_templates_and_unique_names()
    {
        Assert.True(TemplateCase.All.Count >= 25);
        Assert.Equal(TemplateCase.All.Count, TemplateCase.All.Select(c => c.Name).Distinct().Count());
    }

    [Fact]
    public void Untouched_stories_stay_byte_identical_through_the_file_api()
    {
        TemplateCase c = TemplateCase.All.First(x => x.Name == "header-footer");
        byte[] docx = c.Build();
        // The footer part has tags but the header does not exist in this variant: use a template whose second story has no tags.
        TemplateCase only = c with { Parts = new() { ["header1"] = TemplateCase.P("no tags here"), ["footer1"] = TemplateCase.P("{{ company }}") } };
        byte[] before = only.Build();
        var output = new MemoryStream();
        DocumentFormats.Register(FormatRegistry.Default);
        using JsonDocument data = JsonDocument.Parse(c.Json);
        Template.Render(new MemoryStream(before), data, output);
        Dictionary<string, byte[]> a = ReadParts(before);
        Dictionary<string, byte[]> b = ReadParts(output.ToArray());
        Assert.True(a["/word/header1.xml"].AsSpan().SequenceEqual(b["/word/header1.xml"]));
        Assert.False(a["/word/footer1.xml"].AsSpan().SequenceEqual(b["/word/footer1.xml"]));
        _ = docx;
    }
}
