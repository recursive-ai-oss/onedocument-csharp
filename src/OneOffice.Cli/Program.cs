using System.Text;
using System.Text.Json;
using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.IO;

namespace RecursiveAI.OneOffice.Cli;

/// <summary>
/// The <c>oneoffice</c> tool: a thin shell over the <see cref="Office"/> statics. Each command prints its result on standard output,
/// the defects it met on standard error (with their locations), and exits 0 on success, 1 when the input could not be processed and
/// 2 on a usage error. <c>--json</c> prints one JSON object instead.
/// </summary>
internal static class Program
{
    private const string Usage =
        "usage: oneoffice detect <file> [--json]\n" +
        "       oneoffice extract <file> [-o <out.txt>] [--json]\n";

    private static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "detect")
        {
            return Run(args, "detect", Detect);
        }

        if (args.Length >= 2 && args[0] == "extract")
        {
            return Run(args, "extract", Extract);
        }

        Console.Error.Write(Usage);
        return 2;
    }

    private sealed record Options(string File, string? Output, bool Json);

    private static int Run(string[] args, string command, Func<Options, DefectCollection, int> body)
    {
        string? file = null;
        string? output = null;
        bool json = false;
        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--json":
                    json = true;
                    break;
                case "-o" or "--output" when command == "extract" && i + 1 < args.Length:
                    output = args[++i];
                    break;
                default:
                    if (file is not null || args[i].StartsWith('-'))
                    {
                        Console.Error.Write(Usage);
                        return 2;
                    }

                    file = args[i];
                    break;
            }
        }

        if (file is null)
        {
            Console.Error.Write(Usage);
            return 2;
        }

        var options = new Options(file, output, json);
        var defects = new DefectCollection();
        try
        {
            int code = body(options, defects);
            PrintDefects(defects);
            return code;
        }
        catch (Exception ex) when (ex is OfficeException or IOException or UnauthorizedAccessException)
        {
            PrintDefects(defects);
            Failure(options, ex);
            return 1;
        }
    }

    private static int Detect(Options o, DefectCollection defects)
    {
        OfficeFormat format = Office.Detect(o.File);
        if (o.Json)
        {
            WriteJson(w =>
            {
                w.WriteString("file", o.File);
                w.WriteString("format", format.ToString());
            });
        }
        else
        {
            Console.WriteLine(format);
        }

        return 0;
    }

    private static int Extract(Options o, DefectCollection defects)
    {
        var context = new OpenContext(defects: defects);
        if (o.Json)
        {
            using var text = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
            Office.ExtractText(o.File, text, context);
            if (o.Output is not null)
            {
                WriteFile(o.Output, text.ToString());
            }

            WriteJson(w =>
            {
                w.WriteString("file", o.File);
                w.WriteString("format", Office.Detect(o.File).ToString());
                if (o.Output is null)
                {
                    w.WriteString("text", text.ToString());
                }
                else
                {
                    w.WriteString("output", o.Output);
                }

                WriteDefects(w, defects);
            });
        }
        else if (o.Output is not null)
        {
            AtomicFile.Write(o.Output, s =>
            {
                using var writer = new StreamWriter(s, new UTF8Encoding(false), 4096, leaveOpen: true);
                Office.ExtractText(o.File, writer, context);
            });
        }
        else
        {
            using var writer = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false), 4096);
            Office.ExtractText(o.File, writer, context);
        }

        return 0;
    }

    private static void WriteFile(string path, string text) =>
        AtomicFile.Write(path, s => s.Write(new UTF8Encoding(false).GetBytes(text)));

    private static void PrintDefects(DefectCollection defects)
    {
        foreach (Defect d in defects)
        {
            Console.Error.WriteLine($"{d.Severity.ToString().ToLowerInvariant()} {d.Code} at {d.Location}: {d.Message}");
        }

        if (defects.Overflow > 0)
        {
            Console.Error.WriteLine($"{defects.Overflow} more defects were not recorded.");
        }
    }

    private static void Failure(Options o, Exception ex)
    {
        if (o.Json)
        {
            WriteJson(w =>
            {
                w.WriteString("file", o.File);
                w.WriteStartObject("error");
                if (ex is OfficeException oe)
                {
                    w.WriteString("code", oe.Defect.Code);
                    w.WriteString("kind", oe.Defect.Kind.ToString());
                    w.WriteString("location", oe.Location.ToString());
                }

                w.WriteString("message", ex.Message);
                w.WriteEndObject();
            });
        }
        else if (ex is OfficeException oe)
        {
            Console.Error.WriteLine($"error {oe.Defect.Code} at {oe.Location}: {ex.Message}");
        }
        else
        {
            Console.Error.WriteLine("error: " + ex.Message);
        }
    }

    private static void WriteDefects(Utf8JsonWriter w, DefectCollection defects)
    {
        w.WriteStartArray("defects");
        foreach (Defect d in defects)
        {
            w.WriteStartObject();
            w.WriteString("code", d.Code);
            w.WriteString("kind", d.Kind.ToString());
            w.WriteString("severity", d.Severity.ToString());
            w.WriteString("location", d.Location.ToString());
            w.WriteString("message", d.Message);
            w.WriteEndObject();
        }

        w.WriteEndArray();
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
