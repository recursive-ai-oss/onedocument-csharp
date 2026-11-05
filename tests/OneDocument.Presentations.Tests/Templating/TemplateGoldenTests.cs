using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using RecursiveAI.OneDocument.Presentations.Templating;

namespace RecursiveAI.OneDocument.Presentations.Tests;

/// <summary>
/// Renders every template of the corpus again and compares the parts that rendering writes (slides, notes, their relationships, media, the slide
/// list and the content types) with the committed expected parts, character for character. Set ONEDOCUMENT_UPDATE_GOLDEN=1 to write the corpus.
/// </summary>
public class TemplateGoldenTests
{
    private static string Root([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "..", "testdata", "templates", "pptx"));

    public static IEnumerable<TheoryDataRow<string>> Names() => TemplateCorpus.All.Select(c => new TheoryDataRow<string>(c.Name));

    // The parts that a render can change.
    private static bool Compared(string name) =>
        name.StartsWith("ppt/slides/", StringComparison.Ordinal) || name.StartsWith("ppt/notesSlides/", StringComparison.Ordinal) || name.StartsWith("ppt/media/", StringComparison.Ordinal)
        || name is "ppt/presentation.xml" or "ppt/_rels/presentation.xml.rels" or "[Content_Types].xml";

    private static SortedDictionary<string, byte[]> Parts(byte[] zip)
    {
        var map = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach ((string k, byte[] v) in Decks.Entries(zip))
        {
            if (Compared(k))
            {
                map[k] = v;
            }
        }

        return map;
    }

    private static byte[] RenderBytes(byte[] template, string json)
    {
        using JsonDocument data = JsonDocument.Parse(json);
        using var output = new MemoryStream();
        PresentationTemplate.Render(new MemoryStream(template), data, output);
        return output.ToArray();
    }

    [Fact]
    public void TheCorpusHasAtLeastFifteenTemplates() => Assert.True(TemplateCorpus.All.Count >= 15);

    [Fact]
    public void RegenerateGoldenFilesWhenAsked()
    {
        if (Environment.GetEnvironmentVariable("ONEDOCUMENT_UPDATE_GOLDEN") != "1")
        {
            return;
        }

        foreach (TemplateCorpus.Case c in TemplateCorpus.All)
        {
            string dir = Path.Combine(Root(), c.Name);
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }

            Directory.CreateDirectory(dir);
            byte[] template;
            using (Presentation deck = c.Build())
            {
                template = Decks.Save(deck);
            }

            File.WriteAllBytes(Path.Combine(dir, "template.pptx"), template);
            File.WriteAllText(Path.Combine(dir, "data.json"), c.DataJson + "\n", new UTF8Encoding(false));
            foreach ((string name, byte[] bytes) in Parts(RenderBytes(template, c.DataJson)))
            {
                string path = Path.Combine(dir, "expected", name);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, bytes);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void RenderingReproducesTheExpectedParts(string name)
    {
        string dir = Path.Combine(Root(), name);
        byte[] template = File.ReadAllBytes(Path.Combine(dir, "template.pptx"));
        string json = File.ReadAllText(Path.Combine(dir, "data.json"));
        SortedDictionary<string, byte[]> actual = Parts(RenderBytes(template, json));
        var expected = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        string expectedDir = Path.Combine(dir, "expected");
        foreach (string file in Directory.EnumerateFiles(expectedDir, "*", SearchOption.AllDirectories))
        {
            expected[Path.GetRelativePath(expectedDir, file).Replace('\\', '/')] = File.ReadAllBytes(file);
        }

        Assert.Equal(expected.Keys, actual.Keys);
        foreach ((string part, byte[] bytes) in expected)
        {
            Assert.True(bytes.AsSpan().SequenceEqual(actual[part]), name + ": " + part + "\n" + Encoding.UTF8.GetString(actual[part]));
        }
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void RenderingTwiceGivesTheSameBytes(string name)
    {
        string dir = Path.Combine(Root(), name);
        byte[] template = File.ReadAllBytes(Path.Combine(dir, "template.pptx"));
        string json = File.ReadAllText(Path.Combine(dir, "data.json"));
        Assert.True(RenderBytes(template, json).AsSpan().SequenceEqual(RenderBytes(template, json)));
    }
}
