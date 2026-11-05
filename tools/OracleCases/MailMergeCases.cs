using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using RecursiveAI.OneDocument;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Documents.ContentControls;
using RecursiveAI.OneDocument.Documents.MailMerge;
using RecursiveAI.OneDocument.Templating;

namespace RecursiveAI.OneDocument.Tools.OracleCases;

/// <summary>
/// Cases for desktop Word to judge the mail-merge and content-control binding outputs. <c>OracleCases --mailmerge TEMPLATES OUT</c>
/// reads the committed golden templates (<c>testdata/templates/mailmerge</c> and <c>testdata/templates/contentcontrols</c>) and writes, into
/// OUT, per case: the template with a CSV data file and an actions sidecar that makes the driver run Word's own merge into a new document
/// (<c>mailmerge:DATA.csv|single</c>) for comparison, and OneDocument's own outputs (one document per pass, and everything merged into one)
/// under <c>OUT/ours</c>. Content-control cases get the template (Word refreshes bound controls from the custom XML when it opens a file, so
/// opening and re-saving it gives Word's answer for the refresh cases) and OneDocument's output (does Word open it clean, and does it show the
/// same text). Every input is generated; no downloaded file is read. Cases with <c>ASK</c> or <c>FILLIN</c> are left out of Word's merge
/// because Word would stop to prompt.
/// </summary>
internal static class MailMergeCases
{
    public static int Write(string templates, string output)
    {
        string ours = Path.Combine(output, "ours");
        Directory.CreateDirectory(ours);
        int count = 0;
        foreach (string dir in Directory.GetDirectories(Path.Combine(templates, "mailmerge")).Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileName(dir);
            string caseText = File.ReadAllText(Path.Combine(dir, "case.txt"));
            string json = File.ReadAllText(Path.Combine(dir, "data.json"));
            var answers = caseText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(l => l.StartsWith("answer:", StringComparison.Ordinal))
                .Select(l => (Key: l[7..l.IndexOf('=', StringComparison.Ordinal)], Value: l[(l.IndexOf('=', StringComparison.Ordinal) + 1)..]))
                .ToDictionary(x => x.Key, x => x.Value);
            if (answers.Count == 0)
            {
                File.Copy(Path.Combine(dir, "template.docx"), Path.Combine(output, "mm-" + name + ".docx"), overwrite: true);
                File.WriteAllText(Path.Combine(output, "mm-" + name + ".data.csv"), Csv(json), new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(output, "mm-" + name + ".actions.txt"), "mailmerge:mm-" + name + ".data.csv|single\n");
            }

            using JsonDocument doc = JsonDocument.Parse(json);
            foreach (MailMergeMode mode in new[] { MailMergeMode.PerRecord, MailMergeMode.SingleDocument })
            {
                using Document template = Document.Open(Path.Combine(dir, "template.docx"));
                using MailMergeResult result = template.MailMerge(TemplateData.FromJson(doc), new MailMergeOptions { Mode = mode, Answers = answers.Count > 0 ? answers : null });
                for (int i = 0; i < result.Documents.Count; i++)
                {
                    string file = mode == MailMergeMode.SingleDocument ? "mm-" + name + ".single.docx" : "mm-" + name + ".rec" + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + ".docx";
                    result.Documents[i].SaveAs(Path.Combine(ours, file), OfficeFormat.Docx);
                    count++;
                }
            }
        }

        foreach (string dir in Directory.GetDirectories(Path.Combine(templates, "contentcontrols")).Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileName(dir);
            string operation = File.ReadAllText(Path.Combine(dir, "case.txt")).Trim()[10..];
            File.Copy(Path.Combine(dir, "template.docx"), Path.Combine(output, "cc-" + name + ".docx"), overwrite: true);
            using Document d = Document.Open(Path.Combine(dir, "template.docx"));
            string xmlData = Path.Combine(dir, "data.xml");
            string jsonData = Path.Combine(dir, "data.json");
            if (operation == "bind")
            {
                if (File.Exists(xmlData))
                {
                    d.BindContentControls(File.ReadAllText(xmlData));
                }
                else
                {
                    using JsonDocument data = JsonDocument.Parse(File.ReadAllText(jsonData));
                    d.BindContentControls(TemplateData.FromJson(data));
                }
            }
            else if (operation == "push")
            {
                d.PushContentControls();
            }
            else
            {
                d.RefreshContentControls();
            }

            d.SaveAs(Path.Combine(ours, "cc-" + name + ".docx"), OfficeFormat.Docx);
            count++;
        }

        Console.WriteLine(count + " documents written to " + ours);
        return 0;
    }

    // Word reads a comma-separated text file whose first line names the fields. Only scalar members of flat records become columns.
    private static string Csv(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        IEnumerable<JsonElement> rows = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray() : [doc.RootElement];
        var columns = new List<string>();
        var records = new List<Dictionary<string, string>>();
        foreach (JsonElement row in rows)
        {
            var record = new Dictionary<string, string>();
            if (row.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty p in row.EnumerateObject())
                {
                    if (p.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    {
                        continue;
                    }

                    if (!columns.Contains(p.Name))
                    {
                        columns.Add(p.Name);
                    }

                    record[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString()! : p.Value.GetRawText();
                }
            }

            records.Add(record);
        }

        var sb = new StringBuilder();
        sb.Append(string.Join(",", columns.Select(Quote))).Append("\r\n");
        foreach (Dictionary<string, string> r in records)
        {
            sb.Append(string.Join(",", columns.Select(c => Quote(r.GetValueOrDefault(c, string.Empty))))).Append("\r\n");
        }

        return sb.ToString();
    }

    private static string Quote(string s) => "\"" + s.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
