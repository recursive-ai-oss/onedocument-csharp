using System.Text.Json;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.Templating;

namespace RecursiveAI.OneDocument.Presentations.Tests;

public class TemplateDispatchTests
{
    [Fact]
    public void Template_Render_and_Validate_reach_the_presentation_binder()
    {
        PresentationFormats.Register(FormatRegistry.Default);
        TemplateCorpus.Case c = TemplateCorpus.All[0];
        string dir = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath(), "pptx-dispatch");
        Directory.CreateDirectory(dir);
        string template = Path.Combine(dir, "t.pptx");
        using (Presentation p = c.Build())
        {
            p.SaveAs(template);
        }

        Assert.DoesNotContain(Template.Validate(template), d => d.Severity == RecursiveAI.OneDocument.Diagnostics.DefectSeverity.Fatal);
        string output = Path.Combine(dir, "o.pptx");
        using JsonDocument data = JsonDocument.Parse(c.DataJson);
        Template.Render(template, data, output);
        string text = Office.ExtractText(output);
        Assert.DoesNotContain("{{", text, StringComparison.Ordinal);
        Assert.Contains("Ada Lovelace", text, StringComparison.Ordinal);
    }
}
