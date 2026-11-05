using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.Templating;

namespace RecursiveAI.OneDocument.Tools.OracleCases;

/// <summary>
/// Renders the document template corpus (<c>testdata/templates/docx/NAME/template.docx</c> with <c>data.json</c>) and writes each result as
/// <c>dt-NAME.docx</c>, for a desktop application to open and judge ("does it ask to repair the file?").
/// <c>OracleCases --docx-templates CORPUS_DIR OUT_DIR</c>. Every input is a file the repository generated.
/// </summary>
internal static class DocxTemplateCases
{
    public static int Write(string corpus, string output)
    {
        if (!Directory.Exists(corpus))
        {
            Console.Error.WriteLine("no such directory: " + corpus);
            return 2;
        }

        DocumentFormats.Register(FormatRegistry.Default);
        Directory.CreateDirectory(output);
        int count = 0;
        foreach (string dir in Directory.EnumerateDirectories(corpus).Order(StringComparer.Ordinal))
        {
            string template = Path.Combine(dir, "template.docx");
            if (!File.Exists(template))
            {
                continue;
            }

            using JsonDocument data = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "data.json")));
            using FileStream input = File.OpenRead(template);
            using FileStream result = File.Create(Path.Combine(output, "dt-" + Path.GetFileName(dir) + ".docx"));
            Template.Render(input, data, result, OfficeFormat.Docx);
            count++;
        }

        Console.WriteLine(count + " rendered templates written to " + output);
        return 0;
    }
}
