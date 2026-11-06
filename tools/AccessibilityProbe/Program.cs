using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using RecursiveAI.OneDocument;
using RecursiveAI.OneDocument.Accessibility;

namespace RecursiveAI.OneDocument.Tools.AccessibilityProbe;

/// <summary>
/// Runs the accessibility check over a directory and counts findings per rule and kind. Information only: nothing is compared or gated.
///
///   AccessibilityProbe scan &lt;dir&gt; &lt;out.tsv&gt; [--shard i/n] [--limit N]
///
/// Writes one line per file (kind, findings per category, skipped checks, outcome) and prints the totals per rule. Files that do not open
/// are counted by the typed defect code of the exception; any other exception is a failure of the checker and is reported as such.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 3 || args[0] != "scan")
        {
            Console.Error.WriteLine("usage: AccessibilityProbe scan <dir> <out.tsv> [--shard i/n] [--limit N]");
            return 2;
        }

        RecursiveAI.OneDocument.Documents.DocumentFormats.Register(RecursiveAI.OneDocument.Formats.FormatRegistry.Default);
        RecursiveAI.OneDocument.Spreadsheets.SpreadsheetFormats.Register(RecursiveAI.OneDocument.Formats.FormatRegistry.Default);
        RecursiveAI.OneDocument.Presentations.PresentationFormats.Register(RecursiveAI.OneDocument.Formats.FormatRegistry.Default);

        int shard = 0, shards = 1, limit = int.MaxValue;
        for (int i = 3; i + 1 < args.Length; i += 2)
        {
            if (args[i] == "--shard")
            {
                string[] parts = args[i + 1].Split('/');
                shard = int.Parse(parts[0], CultureInfo.InvariantCulture);
                shards = int.Parse(parts[1], CultureInfo.InvariantCulture);
            }
            else if (args[i] == "--limit")
            {
                limit = int.Parse(args[i + 1], CultureInfo.InvariantCulture);
            }
        }

        string[] files = Directory.EnumerateFiles(args[1], "*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".pptx", StringComparison.OrdinalIgnoreCase)
                || f.EndsWith(".docm", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".xlsm", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".pptm", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.Ordinal).Where((_, i) => i % shards == shard).Take(limit).ToArray();

        var perRule = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var filesWithRule = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var typed = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var skipped = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int ok = 0, failures = 0;
        long ms = 0;
        using var output = new StreamWriter(args[2]);
        output.WriteLine("file\tkind\terrors\twarnings\ttips\tservices\tskipped\tresult");
        foreach (string file in files)
        {
            string rel = Path.GetRelativePath(args[1], file);
            try
            {
                var sw = Stopwatch.StartNew();
                using OfficeFile open = Office.Open(file);
                AccessibilityReport report = open.CheckAccessibility();
                ms += sw.ElapsedMilliseconds;
                ok++;
                foreach (IGrouping<string, AccessibilityIssue> g in report.Issues.GroupBy(i => i.RuleId))
                {
                    perRule[g.Key] = perRule.GetValueOrDefault(g.Key) + g.Count();
                    filesWithRule[g.Key] = filesWithRule.GetValueOrDefault(g.Key) + 1;
                }

                foreach (AccessibilitySkip s in report.Skipped)
                {
                    skipped[s.RuleId] = skipped.GetValueOrDefault(s.RuleId) + 1;
                }

                output.WriteLine(string.Join('\t', rel, report.Kind, report.Count(AccessibilityCategory.Error), report.Count(AccessibilityCategory.Warning), report.Count(AccessibilityCategory.Tip),
                    report.Count(AccessibilityCategory.IntelligentServices), report.Skipped.Count, "ok"));
            }
            catch (OfficeException ex)
            {
                string code = ex.Defect.Code;
                typed[code] = typed.GetValueOrDefault(code) + 1;
                output.WriteLine(rel + "\t\t\t\t\t\t\t" + code);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                failures++;
                output.WriteLine(rel + "\t\t\t\t\t\t\tFAILURE " + ex.GetType().Name + ": " + ex.Message.Replace('\n', ' '));
            }
        }

        Console.WriteLine("files {0}, checked {1}, typed open errors {2}, checker failures {3}, check time {4} ms", files.Length, ok, typed.Values.Sum(), failures, ms);
        Console.WriteLine("rule\tfindings\tfiles");
        foreach ((string rule, int n) in perRule)
        {
            Console.WriteLine("{0}\t{1}\t{2}", rule, n, filesWithRule[rule]);
        }

        foreach ((string code, int n) in typed)
        {
            Console.WriteLine("open error {0}\t{1}", code, n);
        }

        foreach ((string rule, int n) in skipped)
        {
            Console.WriteLine("skipped {0}\t{1}", rule, n);
        }

        return failures == 0 ? 0 : 1;
    }
}
