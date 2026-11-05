using System.Data;
using System.Text;
using System.Text.Json;
using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Templating;

namespace RecursiveAI.OneDocument.Tests.Templating;

public class TemplateEngineTests
{
    private static string Render(string template, object? data, TemplateOptions? options = null) => TemplateEngine.RenderText(template, data, options);

    private static Dictionary<string, object?> Invoice() => new()
    {
        ["number"] = "INV-7",
        ["customer"] = new Dictionary<string, object?> { ["name"] = "Ada", ["vip"] = true },
        ["overdue"] = false,
        ["items"] = new List<object?>
        {
            new Dictionary<string, object?> { ["description"] = "Pen", ["qty"] = 2, ["price"] = 1.5 },
            new Dictionary<string, object?> { ["description"] = "Ink", ["qty"] = 1, ["price"] = 10 },
        },
    };

    // ---- expressions -------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Values_and_dotted_paths_are_case_insensitive()
    {
        Assert.Equal("Invoice INV-7 for Ada", Render("Invoice {{ number }} for {{ CUSTOMER.Name }}", Invoice()));
    }

    [Fact]
    public void Expressions_are_excel_formulas_with_text_formatting()
    {
        Assert.Equal("Total: $13.00", Render("Total: {{ TEXT(SUM(items.price * items.qty), \"$#,##0.00\") }}", Invoice()));
        Assert.Equal("Ada!", Render("{{ customer.name & \"!\" }}", Invoice()));
        Assert.Equal("yes", Render("{{ IF(customer.vip, \"yes\", \"no\") }}", Invoice()));
        Assert.Equal("2", Render("{{ COUNT(items.qty) }}", Invoice()));
    }

    [Fact]
    public void Array_lifting_works_over_collections()
    {
        Assert.Equal("13", Render("{{ SUM(items.price * items.qty) }}", Invoice()));
        Assert.Equal("1.5, 10", Render("{{ items.price }}", Invoice()));
        Assert.Equal("Pen, Ink", Render("{{ items.description }}", Invoice()));
        Assert.Equal("3", Render("{{ items.length + 1 }}", Invoice()));
    }

    [Fact]
    public void Variables_that_look_like_cell_addresses_still_work()
    {
        var data = new Dictionary<string, object?> { ["rev7"] = 5 };
        Assert.Equal("5", Render("{{ rev7 }}", data));
        Assert.Equal("10", Render("{{ rev7 * 2 }}", data));
    }

    [Fact]
    public void Curly_quotes_from_word_autocorrect_delimit_strings()
    {
        Assert.Equal("ab", Render("{{ \u201Ca\u201D & \u201Cb\u201D }}", null));
        Assert.Equal("x", Render("{% if name = \u201CA\u201D %}x{% endif %}", new Dictionary<string, object?> { ["name"] = "A" }));
    }

    [Fact]
    public void Closing_delimiters_inside_strings_do_not_end_the_tag()
    {
        Assert.Equal("}}", Render("{{ \"}}\" }}", null));
    }

    [Fact]
    public void Dates_print_as_iso_text_and_format_through_text()
    {
        var data = new Dictionary<string, object?> { ["d"] = new DateTime(2024, 3, 9) };
        Assert.Equal("2024-03-09", Render("{{ d }}", data));
        Assert.Equal("09/03/2024", Render("{{ TEXT(d, \"dd/mm/yyyy\") }}", data));
    }

    [Fact]
    public void Output_is_deterministic_with_a_seed_and_a_clock()
    {
        var clock = new FixedClock(new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero));
        var options = new TemplateOptions { Clock = clock, RandomSeed = 7 };
        string a = Render("{{ TEXT(NOW(), \"yyyy-mm-dd\") }} {{ RAND() }}", null, options);
        string b = Render("{{ TEXT(NOW(), \"yyyy-mm-dd\") }} {{ RAND() }}", null, options);
        Assert.Equal(a, b);
        Assert.StartsWith("2025-01-02 ", a, StringComparison.Ordinal);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    [Fact]
    public void Registered_functions_are_the_only_extension_point()
    {
        var registry = FunctionRegistry.CreateDefault();
        registry.AddCustom("SHOUT", 1, 1, a => a[0].Text.ToUpperInvariant());
        Assert.Equal("HEY", Render("{{ SHOUT(\"hey\") }}", null, new TemplateOptions { Functions = registry }));
    }

    // ---- blocks ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void For_loops_expose_loop_variables()
    {
        string t = "{% for i in items %}{{ loop.index }}/{{ loop.length }}:{{ i.description }}{% if loop.last %}.{% else %}, {% endif %}{% endfor %}";
        Assert.Equal("1/2:Pen, 2/2:Ink.", Render(t, Invoice()));
        Assert.Equal("0 1 ", Render("{% for i in items %}{{ loop.index0 }} {% endfor %}", Invoice()));
        Assert.Equal("TF", Render("{% for i in items %}{{ IF(loop.first, \"T\", \"F\") }}{% endfor %}", Invoice()));
    }

    [Fact]
    public void Block_tags_on_their_own_lines_leave_no_blank_lines()
    {
        string t = "Items:\n{% for i in items %}\n- {{ i.description }}\n{% endfor %}\ndone";
        Assert.Equal("Items:\n- Pen\n- Ink\ndone", Render(t, Invoice()));
    }

    [Fact]
    public void Nested_loops_see_the_outer_variable_and_parent_loop()
    {
        var data = new Dictionary<string, object?>
        {
            ["rows"] = new List<object?>
            {
                new Dictionary<string, object?> { ["name"] = "a", ["cells"] = new List<object?> { 1, 2 } },
                new Dictionary<string, object?> { ["name"] = "b", ["cells"] = new List<object?> { 3 } },
            },
        };
        string t = "{% for r in rows %}{{ r.name }}:{% for c in r.cells %}{{ c }}{{ loop.parent.index }}{% endfor %};{% endfor %}";
        Assert.Equal("a:1121;b:32;", Render(t, data));
    }

    [Fact]
    public void If_elif_else_pick_one_branch_with_truthiness_rules()
    {
        string t = "{% if n > 5 %}big{% elif n > 1 %}mid{% else %}small{% endif %}";
        Assert.Equal("big", Render(t, new Dictionary<string, object?> { ["n"] = 9 }));
        Assert.Equal("mid", Render(t, new Dictionary<string, object?> { ["n"] = 3 }));
        Assert.Equal("small", Render(t, new Dictionary<string, object?> { ["n"] = 0 }));
        Assert.Equal("no", Render("{% if items %}yes{% else %}no{% endif %}", new Dictionary<string, object?> { ["items"] = new List<object?>() }));
        Assert.Equal("yes", Render("{% if name %}yes{% endif %}", new Dictionary<string, object?> { ["name"] = "x" }));
        Assert.Equal("", Render("{% if name %}yes{% endif %}", new Dictionary<string, object?> { ["name"] = "" }));
        Assert.Equal("yes", Render("{% if customer %}yes{% endif %}", Invoice()));
    }

    [Fact]
    public void For_else_runs_for_empty_collections()
    {
        string t = "{% for i in items %}{{ i }}{% else %}none{% endfor %}";
        Assert.Equal("none", Render(t, new Dictionary<string, object?> { ["items"] = new List<object?>() }));
        Assert.Equal("12", Render(t, new Dictionary<string, object?> { ["items"] = new List<object?> { 1, 2 } }));
    }

    [Fact]
    public void Loops_run_over_formula_arrays()
    {
        Assert.Equal("123", Render("{% for i in SEQUENCE(3) %}{{ i }}{% endfor %}", null));
    }

    [Fact]
    public void Raw_and_comments()
    {
        Assert.Equal("{{ not }} {% evaluated %}", Render("{% raw %}{{ not }} {% evaluated %}{% endraw %}", null));
        Assert.Equal("ab", Render("a{# hidden {{ x }} #}b", null));
        Assert.Equal("{{ x }}\nmore {{ y }}\nz", Render("{% raw %}{{ x }}\nmore {{ y }}\n{% endraw %}z", null));
    }

    [Fact]
    public void Dash_markers_trim_white_space_in_the_unit()
    {
        Assert.Equal("a1b", Render("a  {{- 1 -}}   b", null));
        Assert.Equal("a b", Render("a {# c #} b", null).Replace("  ", " ", StringComparison.Ordinal));
    }

    [Fact]
    public void Custom_delimiters()
    {
        var options = new TemplateOptions { Delimiters = new TemplateDelimiters("<<", ">>", "<%", "%>", "<#", "#>") };
        Assert.Equal("Hi Ada: 1 2 ", Render("Hi <<name>>: <% for i in xs %><<i>> <% endfor %>", new Dictionary<string, object?> { ["name"] = "Ada", ["xs"] = new List<int> { 1, 2 } }, options));
        Assert.Throws<ArgumentException>(() => TemplateEngine.Parse(TemplateSource.FromText("x"), new TemplateOptions { Delimiters = new TemplateDelimiters("{", "}", "{{", "}}") }));
    }

    [Fact]
    public void Include_reads_templates_only_through_the_supplied_resolver()
    {
        var includes = new Includes(new Dictionary<string, string> { ["greet"] = "Hello {{ name }}!\n{% for i in xs %}{{ i }}{% endfor %}" });
        var options = new TemplateOptions { Includes = includes };
        var data = new Dictionary<string, object?> { ["name"] = "Ada", ["xs"] = new List<int> { 1, 2 } };
        Assert.Equal("[Hello Ada!\n12]", Render("[{% include 'greet' %}]", data, options));

        // Denied by default: a warning, and nothing is read.
        ParsedTemplate denied = TemplateEngine.Parse(TemplateSource.FromText("[{% include 'greet' %}]"));
        TemplateResult result = denied.Render(data);
        Assert.Equal("[]", result.ToText());
        Assert.Equal("template.include-denied", Assert.Single(result.Defects).Code);
        Assert.Equal("template.include-not-found", Assert.Single(TemplateEngine.Parse(TemplateSource.FromText("{% include 'zzz' %}"), options).Render(data).Defects).Code);
        Assert.Throws<TemplateException>(() => Render("{% include 'greet' %}", data, new TemplateOptions { Strict = true }));
    }

    private sealed class Includes(Dictionary<string, string> map) : ITemplateIncludes
    {
        public TemplateSource? Find(string name) => map.TryGetValue(name, out string? text) ? TemplateSource.FromText(text) : null;
    }

    [Fact]
    public void Self_including_templates_hit_the_depth_limit()
    {
        var includes = new Includes(new Dictionary<string, string> { ["a"] = "x{% include 'a' %}" });
        var ex = Assert.Throws<OfficeLimitException>(() => Render("{% include 'a' %}", null, new TemplateOptions { Includes = includes }));
        Assert.Equal(nameof(TemplateOptions.MaxBlockDepth), ex.LimitName);
    }

    [Fact]
    public void Image_html_and_markdown_tags_are_parsed_and_handed_to_the_binder()
    {
        var data = new Dictionary<string, object?> { ["logo"] = new byte[] { 1, 2, 3 }, ["body"] = "<b>x</b>" };
        ParsedTemplate t = TemplateEngine.Parse(TemplateSource.FromText("a{% image logo width=120 alt=\"Our logo\" %}b{% html body %}{% markdown \"**hi**\" %}"));
        Assert.True(t.IsValid);
        IReadOnlyList<TemplatePiece> pieces = Assert.Single(t.Render(data).Units).Pieces;
        Assert.Equal([TemplatePieceKind.Text, TemplatePieceKind.Image, TemplatePieceKind.Text, TemplatePieceKind.Html, TemplatePieceKind.Markdown], pieces.Select(p => p.Kind).ToArray());
        TemplateInsertion image = pieces[1].Insertion!;
        Assert.Equal(new byte[] { 1, 2, 3 }, (byte[])image.Value.Raw!);
        Assert.Equal("120", image.Options["width"]);
        Assert.Equal("Our logo", image.Options["alt"]);
        Assert.Equal("<b>x</b>", pieces[3].Insertion!.Value.Raw);
        Assert.Equal("**hi**", pieces[4].Insertion!.Value.Value.Text);
    }

    [Fact]
    public void Scope_overrides_are_parsed_and_attached_to_blocks()
    {
        ParsedTemplate t = TemplateEngine.Parse(TemplateSource.FromText("{% for r in rows scope=table-row %}{{ r }}{% endfor %}{% if x scope=\"slide\" %}y{% endif %}"));
        Assert.True(t.IsValid);
        Assert.Equal(["table-row", "slide"], t.Blocks.Select(b => b.Scope).ToArray());
        Assert.Equal("for", t.Blocks[0].Tag);
        Assert.Equal("rows", t.Blocks[0].Expression);
        Assert.Contains(TemplateEngine.Parse(TemplateSource.FromText("{% for r in rows scope=bogus %}{% endfor %}")).Defects, d => d.Code == "template.unknown-scope");
    }

    // ---- scope callback: binder widens a block to whole units -------------------------------------------------------------------

    private sealed class RowScope(TemplateUnitRange range) : ITemplateScopeResolver
    {
        public TemplateUnitRange? Resolve(in TemplateBlockInfo block) => block.Tag == "for" ? range : null;
    }

    [Fact]
    public void A_binder_can_widen_a_loop_to_whole_units()
    {
        // One table row is three units (its cells); the loop tags sit in the first and last cell.
        var source = new TemplateSource([new TemplateUnit("{% for i in xs %}A {{ i }}"), new TemplateUnit("B {{ i }}"), new TemplateUnit("C{% endfor %}"), new TemplateUnit("after")]);
        var data = new Dictionary<string, object?> { ["xs"] = new List<int> { 1, 2 } };
        TemplateResult r = TemplateEngine.Render(source, TemplateData.From(data), new TemplateOptions { Scopes = new RowScope(new TemplateUnitRange(0, 2)) });
        Assert.Equal("A 1\nB 1\nC\nA 2\nB 2\nC\nafter", r.ToText());
        Assert.Equal([0, 1, 2, 0, 1, 2, 3], r.Units.Select(u => u.SourceUnit).ToArray());
    }

    [Fact]
    public void A_widened_range_that_misses_a_tag_is_a_located_error()
    {
        var source = new TemplateSource([new TemplateUnit("x"), new TemplateUnit("{% for i in xs %}{{ i }}{% endfor %}")]);
        ParsedTemplate t = TemplateEngine.Parse(source, new TemplateOptions { Scopes = new RowScope(new TemplateUnitRange(0, 0)) });
        Assert.Contains(t.Defects, d => d.Code == "template.scope-invalid");
        Assert.False(t.IsValid);
    }

    [Fact]
    public void A_loop_whose_tags_sit_in_one_unit_repeats_inline()
    {
        Assert.Equal("a1b2bc", Render("a{% for i in xs %}{{ i }}b{% endfor %}c", new Dictionary<string, object?> { ["xs"] = new List<int> { 1, 2 } }));
    }

    // ---- validation --------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("a {{ x ", "template.unclosed-tag", 2)]
    [InlineData("{% for i in xs %}x", "template.unclosed-block", 0)]
    [InlineData("x {% endfor %}", "template.unexpected-tag", 2)]
    [InlineData("{% if a %}x{% else %}y{% else %}z{% endif %}", "template.unexpected-tag", 22)]
    [InlineData("{% elif a %}", "template.unexpected-tag", 0)]
    [InlineData("{% frobnicate %}", "template.unknown-tag", 0)]
    [InlineData("{{ }}", "template.empty-expression", 2)]
    [InlineData("ab {{ 1 + }}", "template.expression-syntax", 0)]
    [InlineData("{% for in xs %}{% endfor %}", "template.bad-for", 0)]
    [InlineData("{% raw %}never closed", "template.unclosed-block", 0)]
    [InlineData("{% if a %}x{% endfor %}", "template.unexpected-tag", 10)]
    [InlineData("{% image x width=1 foo %}", "template.bad-option", 0)]
    public void Malformed_templates_report_typed_located_defects(string template, string code, int minOffset)
    {
        ParsedTemplate t = TemplateEngine.Parse(TemplateSource.FromText(template));
        Assert.False(t.IsValid);
        Defect d = t.Defects.First(x => x.Code == code);
        Assert.Equal(DefectSeverity.Fatal, d.Severity);
        Assert.Equal(DefectLocationKind.Text, d.Location.Kind);
        Assert.True(d.Location.Offset >= minOffset, d.ToString());
        var ex = Assert.Throws<TemplateException>(() => t.Render(null));
        Assert.Equal(code, t.Defects.First(x => x.Severity == DefectSeverity.Fatal).Code);
        Assert.StartsWith("template.", ex.Defect.Code, StringComparison.Ordinal);
    }

    [Fact]
    public void Defects_name_the_binder_location_and_the_offset_in_the_unit()
    {
        var source = new TemplateSource([new TemplateUnit("fine"), new TemplateUnit("x {% bogus %}", "/word/document.xml#p2")]);
        Defect d = Assert.Single(TemplateEngine.Parse(source).Defects);
        Assert.Equal("template.unknown-tag", d.Code);
        Assert.Equal(DefectLocationKind.Part, d.Location.Kind);
        Assert.Equal("/word/document.xml#p2", d.Location.Container);
        Assert.Equal(2, d.Location.Offset);
    }

    [Fact]
    public void Expression_errors_point_inside_the_expression()
    {
        ParsedTemplate t = TemplateEngine.Parse(TemplateSource.FromText("0123{{ 1 + * 2 }}"));
        Defect d = Assert.Single(t.Defects);
        Assert.Equal("template.expression-syntax", d.Code);
        Assert.True(d.Location.Offset >= 8, d.ToString());
    }

    [Fact]
    public void All_errors_are_reported_not_just_the_first()
    {
        ParsedTemplate t = TemplateEngine.Parse(TemplateSource.FromText("{% bogus %}\n{{ 1 + }}\n{% endif %}"));
        Assert.Equal(["template.unknown-tag", "template.expression-syntax", "template.unexpected-tag"], t.Defects.Select(d => d.Code).ToArray());
    }

    [Fact]
    public void Unknown_functions_are_warnings_not_errors()
    {
        ParsedTemplate t = TemplateEngine.Parse(TemplateSource.FromText("{{ NOSUCHFUNCTION(1) }}"));
        Assert.True(t.IsValid);
        Defect d = Assert.Single(t.Defects);
        Assert.Equal("template.unknown-function", d.Code);
        Assert.Equal(DefectSeverity.Dropped, d.Severity);
    }

    [Fact]
    public void Defects_reach_the_sink_given_in_the_options()
    {
        var sink = new DefectCollection();
        TemplateEngine.Parse(TemplateSource.FromText("{% bogus %}"), new TemplateOptions { Defects = sink });
        Assert.Single(sink);
    }

    // ---- missing data ------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Missing_data_renders_empty_with_a_located_warning()
    {
        ParsedTemplate t = TemplateEngine.Parse(TemplateSource.FromText("a{{ nothing }}b{{ customer.zip }}c{{ nothing * 2 }}d{% for i in nothing %}x{% endfor %}"));
        TemplateResult r = t.Render(TemplateData.From(Invoice()));
        Assert.Equal("abcd", r.ToText());
        Assert.Equal(4, r.Defects.Count);
        Assert.All(r.Defects, d =>
        {
            Assert.Equal("template.missing-data", d.Code);
            Assert.Equal(DefectSeverity.Repaired, d.Severity);
        });
        Assert.Equal(4, r.Defects[0].Location.Offset);
    }

    [Fact]
    public void Handled_missing_data_is_not_reported()
    {
        TemplateResult r = TemplateEngine.Parse(TemplateSource.FromText("{{ IFERROR(nothing, \"n/a\") }}")).Render(null);
        Assert.Equal("n/a", r.ToText());
        Assert.Empty(r.Defects);
    }

    [Fact]
    public void Strict_mode_throws_at_missing_data_and_error_values()
    {
        var strict = new TemplateOptions { Strict = true };
        var ex = Assert.Throws<TemplateException>(() => Render("a{{ nothing }}", null, strict));
        Assert.Equal("template.missing-data", ex.Defect.Code);
        Assert.Equal(DefectSeverity.Fatal, ex.Defect.Severity);
        Assert.Equal("template.expression-error", Assert.Throws<TemplateException>(() => Render("{{ 1/0 }}", null, strict)).Defect.Code);
        Assert.Equal("#DIV/0!", Render("{{ 1/0 }}", null));
        Assert.Equal("template.not-a-collection", Assert.Throws<TemplateException>(() => Render("{% for i in x %}{% endfor %}", new Dictionary<string, object?> { ["x"] = 5 }, strict)).Defect.Code);
    }

    // ---- data sources ------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Json_documents_and_elements()
    {
        using JsonDocument doc = JsonDocument.Parse("{\"Name\":\"Ada\",\"tags\":[\"a\",\"b\"],\"n\":{\"v\":2.5},\"none\":null,\"ok\":true,\"list\":[{\"p\":1},{\"p\":2}]}");
        string t = "{{ name }} {% for g in tags %}{{ g }}{% endfor %} {{ n.v * 2 }} [{{ none }}] {{ IF(ok, \"y\", \"n\") }} {{ SUM(list.p) }}";
        Assert.Equal("Ada ab 5 [] y 3", Render(t, doc));
        Assert.Equal("Ada ab 5 [] y 3", Render(t, doc.RootElement));
        Assert.Equal("Ada ab 5 [] y 3", TemplateEngine.RenderText(t, TemplateData.FromJson(doc)));
    }

    [Fact]
    public void Dictionaries_lists_and_arrays()
    {
        var data = new Dictionary<string, string> { ["a"] = "1", ["B"] = "2" };
        Assert.Equal("1-2", Render("{{ a }}-{{ b }}", data));
        var nonGeneric = new System.Collections.Hashtable { ["k"] = new[] { "x", "y" } };
        Assert.Equal("xy", Render("{% for v in k %}{{ v }}{% endfor %}", nonGeneric));
    }

    [Fact]
    public void Data_tables_data_rows_and_readers()
    {
        var table = new DataTable();
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("Qty", typeof(int));
        table.Rows.Add("Pen", 2);
        table.Rows.Add("Ink", DBNull.Value);
        var data = new Dictionary<string, object?> { ["rows"] = table };
        string t = "{% for r in rows %}{{ r.name }}={{ r.qty }};{% endfor %}";
        Assert.Equal("Pen=2;Ink=;", Render(t, data));
        Assert.Equal("Pen=2;Ink=;", Render(t, new Dictionary<string, object?> { ["rows"] = table.CreateDataReader() }));
        Assert.Equal("Pen", Render("{{ name }}", table.Rows[0]));
    }

    [Fact]
    public void Xml_through_the_library_tokenizer()
    {
        byte[] xml = Encoding.UTF8.GetBytes("<order id=\"7\"><customer>Ada</customer><line sku=\"a\"><qty>2</qty></line><line sku=\"b\"><qty>3</qty></line></order>");
        ITemplateData data = TemplateData.FromXml(xml);
        Assert.Equal("7 Ada a:2 b:3 5", TemplateEngine.RenderText("{{ id }} {{ customer }} {% for l in line %}{{ l.sku }}:{{ l.qty }} {% endfor %}{{ SUM(line.qty) }}", data).Replace("  ", " ", StringComparison.Ordinal));
        Assert.Throws<OfficeFormatException>(() => TemplateData.FromXml(Encoding.UTF8.GetBytes("<!DOCTYPE a [<!ENTITY x 'y'>]><a/>")));
    }

    private sealed class Person : ITemplateObject
    {
        public string Name { get; init; } = string.Empty;

        public List<Person> Friends { get; init; } = [];

        public bool TryGetTemplateMember(string name, out object? value)
        {
            if (name.Equals(nameof(Name), StringComparison.OrdinalIgnoreCase))
            {
                value = Name;
                return true;
            }

            if (name.Equals(nameof(Friends), StringComparison.OrdinalIgnoreCase))
            {
                value = Friends;
                return true;
            }

            value = null;
            return false;
        }
    }

    [Fact]
    public void Template_objects_bind_without_reflection()
    {
        var p = new Person { Name = "Ada", Friends = [new Person { Name = "Bo" }, new Person { Name = "Cy" }] };
        Assert.Equal("Ada: Bo,Cy,", Render("{{ name }}: {% for f in friends %}{{ f.name }},{% endfor %}", p));
        Assert.Equal("Bo, Cy", Render("{{ friends.name }}", p));
    }

    [Fact]
    public void Plain_classes_without_a_model_are_refused_with_a_typed_exception()
    {
        Assert.Throws<OfficeUnsupportedException>(() => TemplateData.From(new object()));
    }

    [Fact]
    public void Row_limits_apply_when_adapting_data()
    {
        Assert.Throws<OfficeLimitException>(() => TemplateData.From(Enumerable.Range(0, 10).ToList(), 5));
    }

    // ---- sandbox limits ----------------------------------------------------------------------------------------------------------

    [Fact]
    public void Iteration_output_and_depth_limits()
    {
        var data = new Dictionary<string, object?> { ["xs"] = Enumerable.Range(0, 100).ToList() };
        var ex = Assert.Throws<OfficeLimitException>(() => Render("{% for a in xs %}{% for b in xs %}.{% endfor %}{% endfor %}", data, new TemplateOptions { MaxIterations = 1000 }));
        Assert.Equal(nameof(TemplateOptions.MaxIterations), ex.LimitName);
        ex = Assert.Throws<OfficeLimitException>(() => Render("{% for a in xs %}0123456789{% endfor %}", data, new TemplateOptions { MaxOutputCharacters = 500 }));
        Assert.Equal(nameof(TemplateOptions.MaxOutputCharacters), ex.LimitName);
        string deep = string.Concat(Enumerable.Repeat("{% if 1 %}", 80)) + "x" + string.Concat(Enumerable.Repeat("{% endif %}", 80));
        Assert.Throws<OfficeLimitException>(() => Render(deep, null));
        Assert.Throws<OfficeLimitException>(() => Render("{{ SUM(SEQUENCE(100000, 1000)) }}", null, new TemplateOptions { Limits = new OfficeLimits { MaxAllocationBytes = 1 << 20 } }));
    }

    [Fact]
    public void Rendering_can_be_cancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        ParsedTemplate t = TemplateEngine.Parse(TemplateSource.FromText("{% for i in xs %}x{% endfor %}"));
        Assert.Throws<OperationCanceledException>(() => t.Render(TemplateData.From(new Dictionary<string, object?> { ["xs"] = new List<int> { 1 } }), cts.Token));
    }

    // ---- the extensions round-trip through the AST --------------------------------------------------------------------------------

    [Theory]
    [InlineData("items.price*items.qty")]
    [InlineData("SUM(items.price * items.qty)")]
    [InlineData("TEXT( order.total , \"0.00\" )")]
    [InlineData("a.b.c+1")]
    public void Member_paths_round_trip_through_the_formula_ast(string text)
    {
        FormulaNode tree = FormulaParser.Parse(text, new FormulaParserOptions { TemplateExtensions = true, FileForm = false });
        Assert.Equal(text, FormulaPrinter.Print(tree, new FormulaPrintOptions { Form = FormulaForm.Display }));
        Assert.Contains(Descendants(tree), n => n is MemberPathNode);
    }

    private static IEnumerable<FormulaNode> Descendants(FormulaNode n)
    {
        yield return n;
        foreach (FormulaNode c in n.Children)
        {
            foreach (FormulaNode d in Descendants(c))
            {
                yield return d;
            }
        }
    }

    // ---- tags split across segments (Word runs) ----------------------------------------------------------------------------------

    private static readonly string[] SplitTemplates =
    [
        "Hi {{ customer.name }}, total {{ TEXT(SUM(items.price * items.qty), \"0.00\") }}.",
        "{% if customer.vip %}VIP {{ number }}{% else %}regular{% endif %}",
        "{% for i in items %}[{{ i.description }}:{{ loop.index }}]{% endfor %}",
        "x {# note #} {% raw %}{{ kept }}{% endraw %} {{- number -}} y",
        "{% image number width=5 %}|{{ \"a}}b\" }}",
    ];

    [Fact]
    public void Tags_split_across_segments_at_every_position_render_as_if_whole()
    {
        foreach (string template in SplitTemplates)
        {
            string whole = Render(template, Invoice(), null);
            for (int cut = 0; cut <= template.Length; cut++)
            {
                var unit = new TemplateUnit([new TemplateSegment(template[..cut]), new TemplateSegment(template[cut..])]);
                TemplateResult r = TemplateEngine.Render(new TemplateSource([unit]), TemplateData.From(Invoice()));
                Assert.True(whole == r.ToText(), $"cut {cut} of '{template}': '{r.ToText()}' != '{whole}'");
            }

            // Every character its own segment.
            var single = new TemplateUnit([.. template.Select(c => new TemplateSegment(c.ToString()))]);
            Assert.Equal(whole, TemplateEngine.Render(new TemplateSource([single]), TemplateData.From(Invoice())).ToText());

            // Three segments, cut at two positions (every pair on the shortest template, a stride on the rest).
            int stride = template.Length < 45 ? 1 : 5;
            for (int a = 0; a <= template.Length; a += stride)
            {
                for (int b = a; b <= template.Length; b += stride)
                {
                    var unit = new TemplateUnit([new TemplateSegment(template[..a]), new TemplateSegment(template[a..b]), new TemplateSegment(template[b..])]);
                    string got = TemplateEngine.Render(new TemplateSource([unit]), TemplateData.From(Invoice())).ToText();
                    Assert.True(whole == got, $"cuts {a},{b} of '{template}'");
                }
            }
        }
    }

    [Fact]
    public void A_split_tag_prints_into_the_segment_where_it_began_and_the_rest_keeps_its_own()
    {
        // Runs: "Hello {{ na" (bold) | "me }}, bye" (plain). The value takes the first run's formatting; ", bye" stays in the second.
        var unit = new TemplateUnit([new TemplateSegment("Hello {{ na"), new TemplateSegment("me }}, bye")]);
        TemplateResult r = TemplateEngine.Render(new TemplateSource([unit]), TemplateData.From(new Dictionary<string, object?> { ["name"] = "Ada" }));
        TemplateRenderedUnit u = Assert.Single(r.Units);
        Assert.Equal(["Hello Ada", ", bye"], u.Pieces.Select(p => p.Text).ToArray());
        Assert.Equal([0, 1], u.Pieces.Select(p => p.Segment).ToArray());
    }

    [Fact]
    public void Text_between_tags_keeps_the_segment_it_came_from_inside_loops()
    {
        var unit = new TemplateUnit([new TemplateSegment("A{% for i in xs %}"), new TemplateSegment("<{{ i }}>"), new TemplateSegment("{% endfor %}Z")]);
        TemplateResult r = TemplateEngine.Render(new TemplateSource([unit]), TemplateData.From(new Dictionary<string, object?> { ["xs"] = new List<int> { 1, 2 } }));
        TemplateRenderedUnit u = Assert.Single(r.Units);
        Assert.Equal("A<1><2>Z", u.Text);
        Assert.Equal(["A", "<1><2>", "Z"], u.Pieces.Select(p => p.Text).ToArray());
        Assert.Equal([0, 1, 2], u.Pieces.Select(p => p.Segment).ToArray());
    }

    // ---- numbers print the way a cell shows them --------------------------------------------------------------------------------

    [Fact]
    public void Numbers_use_general_formatting()
    {
        var d = new Dictionary<string, object?> { ["a"] = 0.1 + 0.2, ["b"] = 1234567.0, ["c"] = 12.50m, ["d"] = true };
        Assert.Equal("0.3 1234567 12.5 TRUE", Render("{{ a }} {{ b }} {{ c }} {{ d }}", d));
        Assert.Equal("1,234,567.00", Render("{{ TEXT(b, \"#,##0.00\") }}", d));
    }
}
