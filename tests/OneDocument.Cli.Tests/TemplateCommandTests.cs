using System.Text;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Cli.Tests;

public class TemplateCommandTests
{
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static string Scratch()
    {
        string dir = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath(), "cli-template-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string WriteTemplate(string dir, string paragraphs)
    {
        string path = Path.Combine(dir, "t.docx");
        using OpcPackage p = OpcPackage.Create();
        p.SetRelationships(null, [new Relationship("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument", "word/document.xml")]);
        p.AddPart("/word/document.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml",
            Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<w:document xmlns:w=\"" + W + "\"><w:body>" + paragraphs + "</w:body></w:document>"));
        p.SaveAs(path);
        return path;
    }

    [Fact]
    public void A_template_renders_with_json_data_and_the_output_format_follows_the_extension()
    {
        string dir = Scratch();
        string template = WriteTemplate(dir, "<w:p><w:r><w:t>Hello {{ name }}</w:t></w:r></w:p>");
        string data = Path.Combine(dir, "d.json");
        File.WriteAllText(data, "{\"name\":\"World\"}");
        string output = Path.Combine(dir, "o.docx");
        var err = new StringWriter();
        Assert.Equal(0, TemplateCommand.Run([template, data, "-o", output], err));
        Assert.Equal("", err.ToString());
        Assert.Contains("Hello World", Office.ExtractText(output), StringComparison.Ordinal);
    }

    [Fact]
    public void Strict_mode_fails_on_missing_data_and_validate_reports_located_errors()
    {
        string dir = Scratch();
        string template = WriteTemplate(dir, "<w:p><w:r><w:t>{{ nope }}</w:t></w:r></w:p><w:p><w:r><w:t>{% if x %}</w:t></w:r></w:p>");
        string data = Path.Combine(dir, "d.json");
        File.WriteAllText(data, "{}");
        var err = new StringWriter();
        Assert.Equal(1, TemplateCommand.Run([template, data, "-o", Path.Combine(dir, "o.docx"), "--strict"], err));
        Assert.Contains("template.unclosed-block", err.ToString(), StringComparison.Ordinal);

        var err2 = new StringWriter();
        Assert.Equal(1, TemplateCommand.Run(["--validate", template], err2));
        Assert.Contains("/word/document.xml#body/p2", err2.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Bad_usage_exits_2()
    {
        Assert.Equal(2, TemplateCommand.Run(["only-one"], new StringWriter()));
    }
}
