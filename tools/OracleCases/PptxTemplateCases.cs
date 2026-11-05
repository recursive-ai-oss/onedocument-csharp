using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using RecursiveAI.OneDocument.Presentations.Templating;

namespace RecursiveAI.OneDocument.Tools.OracleCases;

/// <summary>
/// Renders the presentation template corpus (<c>testdata/templates/pptx/NAME/template.pptx</c> with <c>data.json</c>) and writes each result as
/// <c>pt-NAME.pptx</c>, for a desktop application to open and judge ("does it ask to repair the file?").
/// <c>OracleCases --pptx-templates CORPUS_DIR OUT_DIR</c>. Every input is a file the repository generated.
/// </summary>
internal static class PptxTemplateCases
{
    public static int Write(string corpus, string output)
    {
        if (!Directory.Exists(corpus))
        {
            Console.Error.WriteLine("no such directory: " + corpus);
            return 2;
        }

        Directory.CreateDirectory(output);
        int count = 0;
        foreach (string dir in Directory.EnumerateDirectories(corpus).Order(StringComparer.Ordinal))
        {
            string template = Path.Combine(dir, "template.pptx");
            if (!File.Exists(template))
            {
                continue;
            }

            using JsonDocument data = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "data.json")));
            using FileStream input = File.OpenRead(template);
            using FileStream result = File.Create(Path.Combine(output, "pt-" + Path.GetFileName(dir) + ".pptx"));
            PresentationTemplate.Render(input, data, result);
            count++;
        }

        Console.WriteLine(count + " rendered templates written to " + output);
        return 0;
    }
}
