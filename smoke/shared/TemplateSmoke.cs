using System.Data;
using System.Text;
using System.Text.Json;
using RecursiveAI.OneDocument.Spreadsheets;
using RecursiveAI.OneDocument.Spreadsheets.Templating;
using RecursiveAI.OneDocument.Templating;

namespace RecursiveAI.OneDocument.Smoke;

/// <summary>A template model bound by the source generator: the engine reads it without reflection.</summary>
[TemplateModel]
internal sealed partial class SmokeInvoice
{
    public string Number { get; init; } = string.Empty;

    public List<SmokeLine> Lines { get; init; } = [];
}

[TemplateModel]
internal sealed partial record SmokeLine(string Description, int Qty, double Price);

/// <summary>Renders a template against a generated model, JSON, a data table and XML, and splits a tag across runs, under NativeAOT and WASM.</summary>
internal static class TemplateSmoke
{
    public static List<string> Run()
    {
        var failures = new List<string>();
        void Check(string name, string actual, string expected)
        {
            if (actual != expected)
            {
                failures.Add($"template {name}: '{actual}' != '{expected}'");
            }
        }

        const string Text = "Invoice {{ number }}\n{% for l in lines %}\n- {{ l.description }} {{ TEXT(l.price * l.qty, \"0.00\") }}\n{% endfor %}\nTotal {{ SUM(lines.price * lines.qty) }}";
        var invoice = new SmokeInvoice { Number = "7", Lines = [new SmokeLine("Pen", 2, 1.5), new SmokeLine("Ink", 1, 10)] };
        string expected = "Invoice 7\n- Pen 3.00\n- Ink 10.00\nTotal 13";
        Check("poco", TemplateEngine.RenderText(Text, invoice), expected);

        using JsonDocument json = JsonDocument.Parse("{\"number\":\"7\",\"lines\":[{\"description\":\"Pen\",\"qty\":2,\"price\":1.5},{\"description\":\"Ink\",\"qty\":1,\"price\":10}]}");
        Check("json", TemplateEngine.RenderText(Text, json), expected);

        var table = new DataTable();
        table.Columns.Add("description", typeof(string));
        table.Columns.Add("qty", typeof(int));
        table.Columns.Add("price", typeof(double));
        table.Rows.Add("Pen", 2, 1.5);
        table.Rows.Add("Ink", 1, 10.0);
        Check("table", TemplateEngine.RenderText(Text, new Dictionary<string, object?> { ["number"] = "7", ["lines"] = table }), expected);

        byte[] xml = Encoding.UTF8.GetBytes("<i><number>7</number><lines><description>Pen</description><qty>2</qty><price>1.5</price></lines><lines><description>Ink</description><qty>1</qty><price>10</price></lines></i>");
        Check("xml", TemplateEngine.RenderText(Text, TemplateData.FromXml(xml)), expected);

        var unit = new TemplateUnit([new TemplateSegment("Hi {{ na"), new TemplateSegment("me }}!")]);
        TemplateResult split = TemplateEngine.Render(new TemplateSource([unit]), TemplateData.From(new Dictionary<string, object?> { ["name"] = "Ada" }));
        Check("split", split.ToText(), "Hi Ada!");
        if (TemplateEngine.Parse(TemplateSource.FromText("{% for x in y %}")).IsValid)
        {
            failures.Add("template unclosed block was accepted");
        }

        using Workbook wb = Workbook.Create();
        Worksheet sheet = wb.Sheets[0];
        sheet["A1"].SetString("{% for l in lines %}{{ l.description }}");
        sheet["B1"].SetString("{{ l.qty }}{% endfor %}");
        sheet["B2"].Formula = "SUM(B1:B1)";
        XlsxTemplateBinder.Render(wb, invoice);
        string rendered = sheet["A1"].Text + "|" + sheet["A2"].Text + "|" + sheet["B2"].Number + "|" + sheet["B3"].Formula;
        Check("xlsx", rendered, "Pen|Ink|1|SUM(B1:B2)");

        return failures;
    }
}
