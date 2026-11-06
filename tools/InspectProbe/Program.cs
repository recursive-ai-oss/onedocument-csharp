using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using RecursiveAI.OneDocument;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.Inspection;

// InspectProbe: information-only runs of inspection and sanitising over files.
//   InspectProbe scan [--sanitize] <file|dir>...   one JSON line per file: format, counts per category, defect codes or the typed failure;
//                                                  with --sanitize the file is also sanitised (all categories) into memory and the outcome
//                                                  (status per category, verification result) is added. Nothing is written.
//   The last line sums the counts and the failures by code.
RecursiveAI.OneDocument.Documents.DocumentFormats.Register(FormatRegistry.Default);
RecursiveAI.OneDocument.Spreadsheets.SpreadsheetFormats.Register(FormatRegistry.Default);
RecursiveAI.OneDocument.Presentations.PresentationFormats.Register(FormatRegistry.Default);

if (args.Length < 2 || args[0] != "scan")
{
    Console.Error.WriteLine("usage: InspectProbe scan [--sanitize] <file|dir>...");
    return 2;
}

bool sanitize = args.Contains("--sanitize");
var totals = new SortedDictionary<string, long>(StringComparer.Ordinal);
var failures = new SortedDictionary<string, int>(StringComparer.Ordinal);
int files = 0;
foreach (string path in args.Skip(1).Where(a => a != "--sanitize").SelectMany(Expand))
{
    files++;
    var line = new Dictionary<string, object?> { ["file"] = path };
    var defects = new DefectCollection();
    try
    {
        var context = new OpenContext(null, defects);
        InspectionReport r = Inspector.Inspect(path, context);
        line["format"] = r.SourceFormat.ToString();
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (InspectionCategory c in r.CategoriesFound)
        {
            counts[InspectionCategories.Name(c)] = r.CountOf(c);
            totals[InspectionCategories.Name(c)] = totals.GetValueOrDefault(InspectionCategories.Name(c)) + r.CountOf(c);
        }

        line["counts"] = counts;
        if (sanitize)
        {
            using var ms = new MemoryStream();
            using FileStream input = File.OpenRead(path);
            SanitizeReport s = Sanitizer.Sanitize(input, ms, SanitizeOptions.Everything, new OpenContext(null, new DefectCollection()));
            line["sanitized"] = new { s.OutputFormat, removed = s.Removed.Sum(x => x.Count), kept = s.NotRemoved.Count, bytes = ms.Length };
        }
    }
    catch (OfficeException ex)
    {
        line["error"] = ex.Defect.Code;
        failures[ex.Defect.Code] = failures.GetValueOrDefault(ex.Defect.Code) + 1;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        line["error"] = "io";
        failures["io"] = failures.GetValueOrDefault("io") + 1;
    }

    line["defects"] = defects.Select(d => d.Code).Distinct().Order().ToArray();
    Console.WriteLine(JsonSerializer.Serialize(line));
}

Console.WriteLine(JsonSerializer.Serialize(new { files, totals, failures }));
return 0;

static IEnumerable<string> Expand(string p) =>
    Directory.Exists(p) ? Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal) : [p];
