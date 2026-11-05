using RecursiveAI.OneDocument.Templating;

namespace RecursiveAI.OneDocument.Tests.Templating;

[TemplateModel]
public sealed partial class InvoiceModel
{
    public string Number { get; init; } = string.Empty;

    public CustomerModel Customer { get; init; } = new();

    public List<LineModel> Lines { get; init; } = [];

    public DateTime Issued { get; init; }

    public decimal? Discount { get; init; }

    public string Hidden { private get; init; } = "";

    public static string Shared => "never visible";

    public int Fixed { get; } = 7;
}

[TemplateModel]
public sealed partial class CustomerModel
{
    public string Name { get; init; } = string.Empty;
}

[TemplateModel]
public readonly partial record struct LineModel(string Description, int Qty, double Price);

public class TemplateModelTests
{
    private static InvoiceModel Sample() => new()
    {
        Number = "INV-9",
        Customer = new CustomerModel { Name = "Ada" },
        Lines = [new LineModel("Pen", 2, 1.5), new LineModel("Ink", 1, 10)],
        Issued = new DateTime(2024, 5, 6),
    };

    [Fact]
    public void Generated_models_bind_properties_fields_nested_models_and_collections()
    {
        string t = "{{ number }} {{ customer.name }} {% for l in lines %}{{ l.description }}x{{ l.qty }} {% endfor %}= {{ SUM(lines.price * lines.qty) }} {{ issued }} [{{ discount }}] {{ fixed }}";
        Assert.Equal("INV-9 Ada Penx2 Inkx1 = 13 2024-05-06 [] 7", TemplateEngine.RenderText(t, Sample()));
    }

    [Fact]
    public void Members_that_are_not_public_readable_instance_members_are_not_visible()
    {
        TemplateResult r = TemplateEngine.Parse(TemplateSource.FromText("{{ hidden }}{{ shared }}")).Render(Sample());
        Assert.Equal(string.Empty, r.ToText());
        Assert.Equal(2, r.Defects.Count);
    }

    [Fact]
    public void Generated_lookup_matches_exact_names_first_then_ignoring_case()
    {
        var model = (ITemplateObject)Sample();
        Assert.True(model.TryGetTemplateMember("Number", out object? exact));
        Assert.Equal("INV-9", exact);
        Assert.True(model.TryGetTemplateMember("nUmBeR", out object? loose));
        Assert.Equal("INV-9", loose);
        Assert.False(model.TryGetTemplateMember("nope", out _));
    }
}
