using System.Text;
using System.Text.Json;
using RecursiveAI.OneDocument.Documents.ContentControls;

namespace RecursiveAI.OneDocument.Documents.Tests;

public sealed class ContentControlGoldenTests
{
    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (ContentControlCases.Case c in ContentControlCases.All())
        {
            data.Add(c.Name);
        }

        return data;
    }

    private static string Root([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "testdata", "templates", "contentcontrols"));

    [Theory]
    [MemberData(nameof(Names))]
    public void BindsToTheGoldenOutput(string name)
    {
        bool update = Environment.GetEnvironmentVariable("ONEDOCUMENT_UPDATE_GOLDEN") == "1";
        ContentControlCases.Case c = ContentControlCases.All().Single(x => x.Name == name);
        string dir = Path.Combine(Root(), name);
        string templatePath = Path.Combine(dir, "template.docx");
        string dataPath = Path.Combine(dir, c.DataKind == "xml" ? "data.xml" : "data.json");
        if (update)
        {
            Directory.CreateDirectory(dir);
            ContentControlCases.Write(c, templatePath);
            File.WriteAllText(Path.Combine(dir, "case.txt"), "operation=" + c.Operation + "\n");
            if (c.Data is not null)
            {
                File.WriteAllText(dataPath, c.Data + "\n");
            }
        }

        string operation = File.ReadAllText(Path.Combine(dir, "case.txt")).Trim()[10..];
        using Document doc = Document.Open(templatePath);
        ContentBindingReport report;
        if (operation == "bind")
        {
            string text = File.ReadAllText(dataPath);
            if (c.DataKind == "xml")
            {
                report = doc.BindContentControls(text);
            }
            else
            {
                using JsonDocument json = JsonDocument.Parse(text);
                report = doc.BindContentControls(RecursiveAI.OneDocument.Templating.TemplateData.FromJson(json));
            }
        }
        else if (operation == "push")
        {
            report = doc.PushContentControls();
        }
        else
        {
            report = doc.RefreshContentControls();
        }

        doc.CommitChanges();
        var actual = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["word/document.xml"] = doc.Package.FindPart("/word/document.xml")!.GetBytes().ToArray(),
            ["customXml/item1.xml"] = doc.Package.FindPart("/customXml/item1.xml")!.GetBytes().ToArray(),
            ["warnings.txt"] = Encoding.UTF8.GetBytes(string.Concat(report.Warnings.Select(w => w + "\n"))),
        };

        using var ms = new MemoryStream();
        doc.SaveAs(ms, OfficeFormat.Docx);
        ms.Position = 0;
        using Document reopened = Document.Open(ms);
        _ = reopened.ContentControls().Count();

        string expectedDir = Path.Combine(dir, "expected");
        if (update)
        {
            if (Directory.Exists(expectedDir))
            {
                Directory.Delete(expectedDir, true);
            }

            foreach ((string key, byte[] bytes) in actual)
            {
                string target = Path.Combine(expectedDir, key);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target, bytes);
            }

            return;
        }

        foreach ((string key, byte[] bytes) in actual)
        {
            Assert.Equal(File.ReadAllText(Path.Combine(expectedDir, key)), Encoding.UTF8.GetString(bytes));
        }
    }
}
