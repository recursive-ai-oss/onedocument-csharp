using System.Text;
using System.Text.Json;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Documents.MailMerge;
using RecursiveAI.OneDocument.Templating;

namespace RecursiveAI.OneDocument.Documents.Tests;

public sealed class MailMergeGoldenTests
{
    private static readonly string[] Parts = ["/word/document.xml", "/word/settings.xml", "/word/header1.xml"];

    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (MailMergeCases.Case c in MailMergeCases.All())
        {
            data.Add(c.Name);
        }

        return data;
    }

    private static string Root([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "testdata", "templates", "mailmerge"));

    private static bool Update => Environment.GetEnvironmentVariable("ONEDOCUMENT_UPDATE_GOLDEN") == "1";

    [Theory]
    [MemberData(nameof(Names))]
    public void RendersTheGoldenOutput(string name)
    {
        MailMergeCases.Case c = MailMergeCases.All().Single(x => x.Name == name);
        string dir = Path.Combine(Root(), name);
        string templatePath = Path.Combine(dir, "template.docx");
        if (Update)
        {
            Directory.CreateDirectory(dir);
            MailMergeCases.Write(c, templatePath);
            File.WriteAllText(Path.Combine(dir, "data.json"), c.Data.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n");
            File.WriteAllText(Path.Combine(dir, "case.txt"), (c.Single ? "mode=single\n" : "mode=perrecord\n") + string.Concat((c.Answers ?? []).Select(a => "answer:" + a + "\n")));
        }

        string caseText = File.ReadAllText(Path.Combine(dir, "case.txt"));
        var answers = new Dictionary<string, string>();
        foreach (string line in caseText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("answer:", StringComparison.Ordinal))
            {
                int eq = line.IndexOf('=', StringComparison.Ordinal);
                answers[line[7..eq]] = line[(eq + 1)..];
            }
        }

        var options = new MailMergeOptions
        {
            Mode = caseText.Contains("mode=single", StringComparison.Ordinal) ? MailMergeMode.SingleDocument : MailMergeMode.PerRecord,
            Answers = answers.Count > 0 ? answers : null,
        };
        using JsonDocument json = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "data.json")));
        using Document template = Document.Open(templatePath);
        using MailMergeResult result = template.MailMerge(TemplateData.FromJson(json), options);

        string expectedDir = Path.Combine(dir, "expected");
        var actual = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        for (int i = 0; i < result.Documents.Count; i++)
        {
            Document d = result.Documents[i];
            d.CommitChanges();
            foreach (string part in Parts)
            {
                if (d.Package.FindPart(part) is { } p)
                {
                    actual[i.ToString(System.Globalization.CultureInfo.InvariantCulture) + part] = p.GetBytes().ToArray();
                }
            }

            // Every output opens again.
            using var ms = new MemoryStream();
            d.SaveAs(ms, OfficeFormat.Docx);
            ms.Position = 0;
            using Document reopened = Document.Open(ms);
            _ = reopened.Body.Paragraphs().Count();
        }

        actual["warnings.txt"] = Encoding.UTF8.GetBytes(string.Concat(result.Warnings.Select(w => w.ToString() + "\n")));
        if (Update)
        {
            if (Directory.Exists(expectedDir))
            {
                Directory.Delete(expectedDir, true);
            }

            foreach ((string key, byte[] bytes) in actual)
            {
                string target = Path.Combine(expectedDir, key.TrimStart('/'));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target, bytes);
            }

            return;
        }

        var expectedFiles = Directory.GetFiles(expectedDir, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(expectedDir, f).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.Equal(expectedFiles, actual.Keys.Select(k => k.TrimStart('/')).Order(StringComparer.Ordinal).ToList());
        foreach ((string key, byte[] bytes) in actual)
        {
            byte[] want = File.ReadAllBytes(Path.Combine(expectedDir, key.TrimStart('/')));
            Assert.Equal(Encoding.UTF8.GetString(want), Encoding.UTF8.GetString(bytes));
        }
    }
}
