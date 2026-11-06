using System.Diagnostics;
using System.Text.Json;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.Presentations;
using RecursiveAI.OneDocument.Redaction;
using RecursiveAI.OneDocument.Spreadsheets;

namespace RecursiveAI.OneDocument.Tools.RedactProbe;

/// <summary>
/// Redacts every document under a directory and prints one JSON line per file (information only):
///   RedactProbe &lt;inputDir&gt; &lt;outputDir&gt; --literal TEXT | --regex PATTERN [--ignore-case] [--limit N]
/// Each line has the file, the replacements, gaps, whether the output verified clean, survivors, and the typed error if the file could not be processed.
/// The last line is a summary. Outputs are kept in outputDir so LibreOffice and the schema validator can be run over them.
/// </summary>
internal static class Program
{
    private static readonly string[] Extensions = [".docx", ".docm", ".xlsx", ".xlsm", ".pptx", ".pptm", ".doc", ".xls", ".ppt", ".rtf"];

    private static int Main(string[] args)
    {
        if (args.Length < 4)
        {
            Console.Error.WriteLine("usage: RedactProbe <inputDir> <outputDir> --literal TEXT | --regex PATTERN [--ignore-case] [--limit N]");
            return 2;
        }

        DocumentFormats.Register(FormatRegistry.Default);
        SpreadsheetFormats.Register(FormatRegistry.Default);
        PresentationFormats.Register(FormatRegistry.Default);
        bool ignoreCase = args.Contains("--ignore-case");
        int limit = int.MaxValue;
        for (int i = 2; i + 1 < args.Length; i++)
        {
            if (args[i] == "--limit")
            {
                limit = int.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        RedactionMatch match = args[2] == "--literal" ? RedactionMatch.Literal(args[3], ignoreCase) : RedactionMatch.Pattern(args[3], ignoreCase);
        Directory.CreateDirectory(args[1]);
        int done = 0, verified = 0, failed = 0, unverified = 0;
        foreach (string file in Directory.EnumerateFiles(args[0], "*", SearchOption.AllDirectories).Where(f => Extensions.Contains(Path.GetExtension(f).ToLowerInvariant())).Order(StringComparer.Ordinal).Take(limit))
        {
            var plan = new RedactionPlan();
            plan.Matches.Add(match);
            string output = Path.Combine(args[1], Path.GetFileNameWithoutExtension(file) + ".redacted" + OutputExtension(file));
            var watch = Stopwatch.StartNew();
            try
            {
                using OfficeFile source = Office.Open(file);
                RedactionReport report = source.Redact(plan, output);
                done++;
                if (report.Verified)
                {
                    verified++;
                }
                else
                {
                    unverified++;
                }

                Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["file"] = Path.GetFileName(file),
                    ["output"] = Path.GetFileName(output),
                    ["replacements"] = report.TotalReplacements,
                    ["converted"] = report.Converted,
                    ["verified"] = report.Verified,
                    ["survivors"] = report.Survivors.Count,
                    ["survivorLocations"] = report.Survivors.Take(3).Select(x => x.Location).ToArray(),
                    ["gaps"] = report.Gaps.GroupBy(g => g.Code).ToDictionary(g => g.Key, g => g.Count()),
                    ["ms"] = watch.ElapsedMilliseconds,
                }));
            }
            catch (OfficeException ex)
            {
                failed++;
                Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object?> { ["file"] = Path.GetFileName(file), ["error"] = ex.Defect.Code }));
            }
        }

        Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object?> { ["summary"] = true, ["redacted"] = done, ["verified"] = verified, ["unverified"] = unverified, ["typedErrors"] = failed }));
        return 0;
    }

    private static string OutputExtension(string file) => Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".docm" => ".docm",
        ".xlsm" => ".xlsm",
        ".pptm" => ".pptm",
        ".docx" or ".doc" or ".rtf" => ".docx",
        ".xlsx" or ".xls" => ".xlsx",
        _ => ".pptx",
    };
}
