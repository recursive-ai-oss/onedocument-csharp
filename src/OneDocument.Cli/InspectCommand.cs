using System.Linq;
using System.Text.Json;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.Inspection;

namespace RecursiveAI.OneDocument.Cli;

/// <summary>
/// <c>onedocument inspect &lt;file&gt; [--json] [--password &lt;p&gt;]</c> reports the hidden data and personal information in a file;
/// <c>onedocument sanitize &lt;in&gt; &lt;out&gt; [--keep &lt;categories&gt;] [--only &lt;categories&gt;] [--json] [--password &lt;p&gt;]</c> removes it into a new file.
/// Categories are comma-separated names such as <c>comments,hidden-sheets</c>; <c>onedocument inspect</c> prints them. Both exit 0 on success,
/// 1 when the file could not be processed (or the sanitised result failed verification, in which case nothing is written) and 2 on a usage error.
/// </summary>
internal static class InspectCommand
{
    public const string Usage =
        "       onedocument inspect <file> [--json] [--password <p>]\n" +
        "       onedocument sanitize <in> <out> [--all] [--keep <categories>] [--only <categories>] [--json] [--password <p>]\n";

    public static int Inspect(string[] args, TextWriter error)
    {
        if (!Parse(args, 1, out List<string> files, out _, out _, out bool json, out string? password, out int bad, out _) || files.Count != 1 || bad != 0)
        {
            error.Write(Usage);
            return 2;
        }

        var defects = new DefectCollection();
        try
        {
            InspectionReport report = Office.Inspect(files[0], new OpenContext(null, defects, password));
            if (json)
            {
                WriteJson(w =>
                {
                    w.WriteString("file", files[0]);
                    WriteReport(w, report);
                });
            }
            else
            {
                Console.Out.Write(report.ToString());
            }

            return 0;
        }
        catch (Exception ex) when (ex is OfficeException or IOException or UnauthorizedAccessException)
        {
            Fail(error, ex, json, files[0]);
            return 1;
        }
    }

    public static int Sanitize(string[] args, TextWriter error)
    {
        if (!Parse(args, 2, out List<string> files, out List<InspectionCategory> keep, out List<InspectionCategory> only, out bool json, out string? password, out int bad, out bool all) || files.Count != 2 || bad != 0 || ((keep.Count > 0 || all) && only.Count > 0))
        {
            error.Write(Usage);
            return 2;
        }

        SanitizeOptions options = only.Count > 0 ? SanitizeOptions.Only([.. only]) : (all ? SanitizeOptions.Everything : SanitizeOptions.Default).Without([.. keep]);
        var defects = new DefectCollection();
        try
        {
            SanitizeReport report = Sanitizer.Sanitize(files[0], files[1], options, new OpenContext(null, defects, password));
            OfficeFormat named = OfficeFormats.FromExtension(files[1]);
            string? warning = named != OfficeFormat.Unknown && named != report.OutputFormat
                ? $"the file was written as {report.OutputFormat} but is named like {named}; rename it to match"
                : null;
            if (json)
            {
                WriteJson(w =>
                {
                    w.WriteString("file", files[0]);
                    w.WriteString("output", files[1]);
                    w.WriteString("sourceFormat", report.SourceFormat.ToString());
                    w.WriteString("outputFormat", report.OutputFormat.ToString());
                    w.WriteStartArray("categories");
                    foreach (InspectionCategory c in InspectionCategories.All)
                    {
                        if (report.StatusOf(c) == SanitizeStatus.NotSelected)
                        {
                            continue;
                        }

                        w.WriteStartObject();
                        w.WriteString("name", InspectionCategories.Name(c));
                        w.WriteString("status", report.StatusOf(c).ToString());
                        w.WriteNumber("before", report.Before.CountOf(c));
                        w.WriteNumber("removed", report.RemovedCount(c));
                        w.WriteNumber("after", report.After.CountOf(c));
                        w.WriteEndObject();
                    }

                    w.WriteEndArray();
                    w.WriteStartArray("removed");
                    foreach (RemovedItem r in report.Removed)
                    {
                        w.WriteStartObject();
                        w.WriteString("category", InspectionCategories.Name(r.Category));
                        w.WriteString("location", r.Location);
                        w.WriteString("description", r.Description);
                        w.WriteNumber("count", r.Count);
                        w.WriteEndObject();
                    }

                    w.WriteEndArray();
                    w.WriteStartArray("notRemoved");
                    foreach (NotRemovedItem n in report.NotRemoved)
                    {
                        w.WriteStartObject();
                        w.WriteString("category", InspectionCategories.Name(n.Category));
                        w.WriteString("code", n.Code);
                        w.WriteString("location", n.Location);
                        w.WriteString("reason", n.Reason);
                        w.WriteEndObject();
                    }

                    w.WriteEndArray();
                    WriteStrings(w, "notes", warning is null ? report.Notes : [.. report.Notes, warning]);
                });
            }
            else
            {
                Console.Out.Write(report.ToString());
                if (warning is not null)
                {
                    error.WriteLine("warning: " + warning);
                }
            }

            return 0;
        }
        catch (Exception ex) when (ex is OfficeException or IOException or UnauthorizedAccessException)
        {
            Fail(error, ex, json, files[0]);
            return 1;
        }
    }

    private static bool Parse(string[] args, int positionalCount, out List<string> files, out List<InspectionCategory> keep, out List<InspectionCategory> only, out bool json, out string? password, out int bad, out bool all)
    {
        all = false;
        files = [];
        keep = [];
        only = [];
        json = false;
        password = null;
        bad = 0;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--json":
                    json = true;
                    break;
                case "--all" when positionalCount == 2:
                    all = true;
                    break;
                case "--password" when i + 1 < args.Length:
                    password = args[++i];
                    break;
                case "--keep" or "--only" when positionalCount == 2 && i + 1 < args.Length:
                    List<InspectionCategory> target = args[i] == "--keep" ? keep : only;
                    foreach (string name in args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        if (InspectionCategories.TryParse(name, out InspectionCategory c))
                        {
                            target.Add(c);
                        }
                        else
                        {
                            Console.Error.WriteLine("unknown category: " + name + " (known: " + string.Join(", ", InspectionCategories.All.Select(InspectionCategories.Name)) + ")");
                            bad++;
                        }
                    }

                    break;
                default:
                    if (args[i].StartsWith('-'))
                    {
                        return false;
                    }

                    files.Add(args[i]);
                    break;
            }
        }

        return true;
    }

    private static void WriteReport(Utf8JsonWriter w, InspectionReport report)
    {
        w.WriteString("kind", report.Kind.ToString());
        w.WriteString("sourceFormat", report.SourceFormat.ToString());
        w.WriteString("format", report.Format.ToString());
        w.WriteBoolean("converted", report.WasConverted);
        w.WriteStartObject("counts");
        foreach (InspectionCategory c in InspectionCategories.All)
        {
            int n = report.CountOf(c);
            if (n > 0)
            {
                w.WriteNumber(InspectionCategories.Name(c), n);
            }
        }

        w.WriteEndObject();
        w.WriteStartArray("findings");
        foreach (InspectionFinding f in report.Findings)
        {
            w.WriteStartObject();
            w.WriteString("category", InspectionCategories.Name(f.Category));
            w.WriteString("location", f.Location);
            w.WriteString("description", f.Description);
            w.WriteNumber("count", f.Count);
            if (f.Detail is not null)
            {
                w.WriteString("detail", f.Detail);
            }

            w.WriteEndObject();
        }

        w.WriteEndArray();
        WriteStrings(w, "notes", report.Notes);
        WriteStrings(w, "tips", report.Tips);
    }

    private static void WriteStrings(Utf8JsonWriter w, string name, IReadOnlyList<string> values)
    {
        w.WriteStartArray(name);
        foreach (string v in values)
        {
            w.WriteStringValue(v);
        }

        w.WriteEndArray();
    }

    private static void Fail(TextWriter error, Exception ex, bool json, string file)
    {
        if (json)
        {
            WriteJson(w =>
            {
                w.WriteString("file", file);
                w.WriteStartObject("error");
                if (ex is OfficeException oe)
                {
                    w.WriteString("code", oe.Defect.Code);
                    w.WriteString("location", oe.Location.ToString());
                }

                w.WriteString("message", ex.Message);
                w.WriteEndObject();
            });
        }
        else if (ex is OfficeException oe)
        {
            error.WriteLine($"error {oe.Defect.Code} at {oe.Location}: {ex.Message}");
        }
        else
        {
            error.WriteLine("error: " + ex.Message);
        }
    }

    private static void WriteJson(Action<Utf8JsonWriter> fill)
    {
        using Stream stdout = Console.OpenStandardOutput();
        using (var w = new Utf8JsonWriter(stdout, new JsonWriterOptions { Indented = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            fill(w);
            w.WriteEndObject();
        }

        stdout.WriteByte((byte)'\n');
    }
}
