using System.Linq;
using System.Text.Json;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Templating;

namespace RecursiveAI.OneDocument.Cli;

/// <summary>
/// <c>onedocument template &lt;template&gt; &lt;data.json&gt; -o &lt;out&gt; [--strict]</c> renders a template with JSON data; <c>onedocument template --validate &lt;template&gt;</c>
/// checks a template without data. The output format is the extension of <c>-o</c>. Warnings and errors go to standard error with their locations.
/// </summary>
internal static class TemplateCommand
{
    public const string Usage =
        "       onedocument template <template> <data.json> -o <out> [--strict]\n" +
        "       onedocument template --validate <template>\n";

    /// <summary>Runs the command.</summary>
    /// <param name="args">The arguments after <c>template</c>.</param>
    /// <param name="error">Where warnings, errors and usage go.</param>
    /// <returns>0 on success, 1 when the template or data could not be processed (or is invalid), 2 on a usage error.</returns>
    public static int Run(string[] args, TextWriter error)
    {
        var positional = new List<string>();
        string? output = null;
        bool strict = false;
        bool validate = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--strict":
                    strict = true;
                    break;
                case "--validate":
                    validate = true;
                    break;
                case "-o" or "--output" when i + 1 < args.Length:
                    output = args[++i];
                    break;
                default:
                    if (args[i].StartsWith('-'))
                    {
                        error.Write(Usage);
                        return 2;
                    }

                    positional.Add(args[i]);
                    break;
            }
        }

        if (validate ? positional.Count != 1 : positional.Count != 2 || output is null)
        {
            error.Write(Usage);
            return 2;
        }

        try
        {
            var options = new TemplateOptions { Strict = strict };
            if (validate)
            {
                IReadOnlyList<Defect> problems = Template.Validate(positional[0], options);
                Print(problems, error);
                return problems.Any(static d => d.Severity == DefectSeverity.Fatal) ? 1 : 0;
            }

            using JsonDocument data = JsonDocument.Parse(File.ReadAllBytes(positional[1]));
            IReadOnlyList<Defect> warnings = Template.Render(positional[0], data, output!, options);
            Print(warnings, error);
            return 0;
        }
        catch (Exception ex) when (ex is OfficeException or IOException or UnauthorizedAccessException or JsonException)
        {
            if (ex is OfficeException oe)
            {
                error.WriteLine($"error {oe.Defect.Code}{CliText.At(oe.Location)}: {ex.Message}");
            }
            else
            {
                error.WriteLine("error: " + ex.Message);
            }

            return 1;
        }
    }

    private static void Print(IReadOnlyList<Defect> defects, TextWriter error)
    {
        foreach (Defect d in defects)
        {
            error.WriteLine($"{d.Severity.ToString().ToLowerInvariant()} {d.Code}{CliText.At(d.Location)}: {d.Message}");
        }
    }
}
