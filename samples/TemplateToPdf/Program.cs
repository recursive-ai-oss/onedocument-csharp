// Fill a Word template from a plain object and write the result as PDF: one line.
using RecursiveAI.OneDocument;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Samples;
using RecursiveAI.OneDocument.Templating;

string dir = SampleSupport.WorkDirectory(args);
string templatePath = Path.Combine(dir, "invoice-template.docx");

// A template is an ordinary document with tags in its text.
using (Document template = Document.Create())
{
    template.Body.AddParagraph("Invoice {{ number }} for {{ customer.name }}");
    template.Body.AddParagraph("{% for line in lines %}");
    template.Body.AddParagraph("{{ line.description }}: {{ TEXT(line.qty * line.price, \"0.00\") }}");
    template.Body.AddParagraph("{% endfor %}");
    template.Body.AddParagraph("Total: {{ TEXT(SUM(lines.qty * lines.price), \"0.00\") }}");
    template.SaveAs(templatePath);
}

var invoice = new Invoice
{
    Number = "1001",
    Customer = new Customer { Name = "Ada Lovelace" },
    Lines = [new InvoiceLine("Pens", 3, 1.5), new InvoiceLine("Ink", 1, 10)],
};

// The task.
Template.Render(templatePath, invoice, Path.Combine(dir, "invoice.pdf"));

SampleSupport.RequirePdf(Path.Combine(dir, "invoice.pdf"));

// The same call to a .docx shows the filled text.
Template.Render(templatePath, invoice, Path.Combine(dir, "invoice.docx"));
string text = Office.ExtractText(Path.Combine(dir, "invoice.docx"));
if (!text.Contains("Invoice 1001 for Ada Lovelace", StringComparison.Ordinal) || !text.Contains("Total: 14.50", StringComparison.Ordinal))
{
    throw new InvalidOperationException("The filled template is wrong:\n" + text);
}

Console.WriteLine(text);

/// <summary>Data classes are marked so the engine reads them through generated code, not reflection.</summary>
[TemplateModel]
internal sealed partial class Invoice
{
    public string Number { get; init; } = string.Empty;

    public Customer Customer { get; init; } = new();

    public List<InvoiceLine> Lines { get; init; } = [];
}

[TemplateModel]
internal sealed partial class Customer
{
    public string Name { get; init; } = string.Empty;
}

[TemplateModel]
internal sealed partial record InvoiceLine(string Description, int Qty, double Price);
