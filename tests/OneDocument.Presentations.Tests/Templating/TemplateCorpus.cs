using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Presentations.Tests;

/// <summary>
/// The template corpus: each case is described here and written to <c>testdata/templates/pptx/NAME/</c> (the template, its data and the expected
/// slide parts) when the golden files are regenerated. Templates carry the noise Office writes: split runs, proofing marks, empty end-of-paragraph
/// properties and row ids.
/// </summary>
internal static class TemplateCorpus
{
    private static readonly string s_png8 = Convert.ToBase64String(TestImages.Png(8, 4, 30, 120, 200));
    private static readonly string s_png2 = Convert.ToBase64String(TestImages.Png(2, 2, 240, 200, 20));

    public sealed record Case(string Name, Func<Presentation> Build, string DataJson);

    public static IReadOnlyList<Case> All { get; } =
    [
        new("fields-split-runs", () => Tpl.Deck(
            Tpl.Sp(2, "Title", Tpl.P(Tpl.B("Hello {{ cus"), Tpl.R("tomer.na"), Tpl.R("me }}"), Tpl.R(" — welcome"))),
            Tpl.Sp(3, "Body", Tpl.P(Tpl.R("Order "), Tpl.B("{{ order"), Tpl.R(".number }}"), Tpl.R(" total {{ TEXT(order.total, \"$#,##0.00\") }}")))),
            """{"customer":{"name":"Ada Lovelace"},"order":{"number":"A-17","total":1234.5}}"""),

        new("placeholders", () => Tpl.DeckOn("Title and Content",
            [Tpl.Ph(2, "Title 1", "title", 0, Tpl.P(Tpl.R("{{ deck.t"), Tpl.R("itle }}"))), Tpl.Ph(3, "Content 2", "body", 1, Tpl.Text("By {{ deck.author }}"), Tpl.Text("Date {{ deck.date }}"))]),
            """{"deck":{"title":"Quarterly review","author":"Finance","date":"2024-03-09"}}"""),

        new("paragraph-loop", () => Tpl.Deck(
            Tpl.Sp(2, "List", Tpl.Text("Items"), Tpl.Text("{% for i in items %}"), Tpl.P(Tpl.R("• "), Tpl.B("{{ i.name }}"), Tpl.R(" × {{ i.qty }}")), Tpl.Text("{% endfor %}"), Tpl.Text("end"))),
            """{"items":[{"name":"Pen","qty":2},{"name":"Ink","qty":1},{"name":"Pad","qty":5}]}"""),

        new("slide-per-item", () => Slides(Tpl.Sp(2, "Name", Tpl.Text("{% for c in customers scope=slide %}{{ c.name }}")), Tpl.Sp(3, "City", Tpl.Text("{{ c.city }}{% endfor %}"))),
            """{"customers":[{"name":"Ann","city":"Oslo"},{"name":"Bob","city":"Lima"},{"name":"Cy","city":"Rome"}]}"""),

        new("slide-per-item-with-notes", () =>
        {
            Presentation deck = Tpl.Deck(Tpl.Sp(2, "Name", Tpl.Text("{% for c in customers scope=slide %}Customer {{ loop.index }}: {{ c.name }}{% endfor %}")));
            Decks.AddNotesMaster(deck);
            deck.Slides[0].SetNotesText("Call {{ c.name }}\nabout {{ c.topic }}");
            return deck;
        }, """{"customers":[{"name":"Ann","topic":"renewal"},{"name":"Bob","topic":"upgrade"}]}"""),

        new("nested-loops", () => Slides(Tpl.Sp(2, "Head", Tpl.Text("{% for o in orders scope=slide %}Order {{ o.id }}")), Tpl.Sp(3, "Lines", Tpl.Text("{% for l in o.lines %}"), Tpl.Text("{{ l.sku }} ({{ l.qty }})"), Tpl.Text("{% endfor %}{% endfor %}"))),
            """{"orders":[{"id":1,"lines":[{"sku":"A","qty":1},{"sku":"B","qty":2}]},{"id":2,"lines":[{"sku":"C","qty":3}]},{"id":3,"lines":[]}]}"""),

        new("table-rows", () => Tpl.Deck(Tpl.Table(2, "Invoice",
            [Tpl.Text("Item"), Tpl.Text("Qty"), Tpl.Text("Price")],
            [Tpl.P(Tpl.R("{% for l in lines %}{{ l.n"), Tpl.R("ame }}")), Tpl.Text("{{ l.qty }}"), Tpl.Text("{{ TEXT(l.price, \"0.00\") }}{% endfor %}")],
            [Tpl.Text("Total"), Tpl.Text(""), Tpl.Text("{{ TEXT(SUM(lines.price * lines.qty), \"0.00\") }}")])),
            """{"lines":[{"name":"Pen","qty":2,"price":1.5},{"name":"Ink","qty":1,"price":10},{"name":"Pad","qty":5,"price":2.25}]}"""),

        new("table-cell-loop", () => Tpl.Deck(Tpl.Table(2, "Tags",
            [Tpl.Text("Name"), Tpl.Text("Tags")],
            [Tpl.Text("{{ person }}"), Tpl.P(Tpl.R("x")) + Tpl.Text("{% for t in tags %}{{ t }}; {% endfor %}")])),
            """{"person":"Ada","tags":["math","code","poetry"]}"""),

        new("table-empty-drops-table", () => Tpl.Deck(Tpl.Sp(2, "Title", Tpl.Text("Report")), Tpl.Table(3, "Rows",
            [Tpl.Text("{% if rows scope=table-row %}{{ rows.length }}{% endif %}"), Tpl.Text("x")])),
            """{"rows":[]}"""),

        new("notes-text", () =>
        {
            Presentation deck = Tpl.Deck(Tpl.Sp(2, "Title", Tpl.Text("Agenda for {{ who }}")));
            Decks.AddNotesMaster(deck);
            deck.Slides[0].SetNotesText("{% for p in points %}\n- {{ p }}\n{% endfor %}");
            return deck;
        }, """{"who":"the board","points":["budget","hiring","roadmap"]}"""),

        new("condition-paragraphs", () => Tpl.Deck(
            Tpl.Sp(2, "Status", Tpl.Text("{% if balance > 0 %}"), Tpl.Text("You owe {{ balance }}."), Tpl.Text("{% elif balance < 0 %}"), Tpl.Text("Credit of {{ -balance }}."), Tpl.Text("{% else %}"), Tpl.Text("Settled."), Tpl.Text("{% endif %}"))),
            """{"balance":-12}"""),

        new("condition-shape-removed", () => Tpl.Deck(
            Tpl.Sp(2, "Always", Tpl.Text("Account {{ id }}")),
            Tpl.Sp(3, "Warning", Tpl.Text("{% if overdue %}"), Tpl.Text("PAYMENT OVERDUE"), Tpl.Text("{% endif %}")),
            Tpl.Sp(4, "Inline", Tpl.Text("{% if overdue %}Please call us{% endif %}")),
            Tpl.Sp(5, "Thanks", Tpl.Text("Thank you{% if vip %}, valued customer{% endif %}"))),
            """{"id":"X1","overdue":false,"vip":true}"""),

        new("shape-repeat-cards", () => Tpl.Deck(
            Tpl.Sp(2, "Start", Tpl.Text("{% for p in people %}")),
            Tpl.Sp(3, "Card", Tpl.P(Tpl.B("{{ p.name }}")), Tpl.Text("{{ p.role }}")),
            Tpl.Sp(4, "End", Tpl.Text("{% endfor %}"))),
            """{"people":[{"name":"Ann","role":"CEO"},{"name":"Bob","role":"CTO"},{"name":"Cy","role":"CFO"}]}"""),

        new("slide-block-repeat", () => Slides(
            [Tpl.Sp(2, "Intro", Tpl.Text("{% for r in regions %}Region {{ r.name }}"))],
            [Tpl.Sp(2, "Detail", Tpl.Text("Sales {{ r.sales }}{% endfor %}"))],
            [Tpl.Sp(2, "Closing", Tpl.Text("Thank you"))]),
            """{"regions":[{"name":"North","sales":10},{"name":"South","sales":20}]}"""),

        new("slide-conditional", () => Slides(
            [Tpl.Sp(2, "A", Tpl.Text("Summary"))],
            [Tpl.Sp(2, "B", Tpl.Text("{% if risks scope=slide %}Risks: {{ risks }}{% endif %}"))],
            [Tpl.Sp(2, "C", Tpl.Text("Next steps"))]),
            """{"risks":""}"""),

        new("picture-replaces-shape", () => Tpl.Deck(
            Tpl.Sp(2, "Logo", Tpl.Text("{% image logo %}")),
            Tpl.Sp(3, "Cover", Tpl.Text("{% image logo fit=cover alt=\"Company logo\" %}")),
            Tpl.Sp(4, "Wide", Tpl.Text("{% image logo fit=stretch %}"))),
            "{\"logo\":\"" + s_png8 + "\"}"),

        new("picture-per-item", () => Tpl.Deck(
            Tpl.Sp(2, "Start", Tpl.Text("{% for p in people %}")),
            Tpl.Sp(3, "Name", Tpl.Text("{{ p.name }}")),
            Tpl.Sp(4, "Photo", Tpl.Text("{% image p.photo %}")),
            Tpl.Sp(5, "End", Tpl.Text("{% endfor %}"))),
            "{\"people\":[{\"name\":\"Ann\",\"photo\":\"" + s_png8 + "\"},{\"name\":\"Bob\",\"photo\":\"" + s_png2 + "\"},{\"name\":\"Cy\",\"photo\":\"" + s_png8 + "\"}]}"),

        new("hyperlink-target", () =>
        {
            Presentation deck = Tpl.Deck(Tpl.Sp(2, "Links", Tpl.Text("{% for c in contacts %}"),
                "<a:p><a:r><a:rPr lang=\"en-US\"><a:hlinkClick r:id=\"rId77\"/></a:rPr><a:t>{{ c.name }}</a:t></a:r></a:p>", Tpl.Text("{% endfor %}")));
            string part = deck.Slides[0].PartName;
            var rels = new List<Relationship>(deck.Package.GetRelationships(part))
            {
                new("rId77", Decks.Rel + "hyperlink", "mailto:{{ c.email }}", RelationshipTargetMode.External),
            };
            deck.Package.SetRelationships(part, rels);
            return deck;
        }, """{"contacts":[{"name":"Ann","email":"ann@example.com"},{"name":"Bob","email":"bob@example.com"}]}"""),

        new("group-text", () => Tpl.Deck(
            "<p:grpSp><p:nvGrpSpPr><p:cNvPr id=\"2\" name=\"Group\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"4000000\" cy=\"2000000\"/><a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"4000000\" cy=\"2000000\"/></a:xfrm></p:grpSpPr>"
            + Tpl.Sp(3, "Inner A", Tpl.P(Tpl.R("{{ a }}"))) + Tpl.Sp(4, "Inner B", Tpl.Text("{% if b %}B is {{ b }}{% endif %}")) + "</p:grpSp>"),
            """{"a":"first","b":"second"}"""),

        new("formulas-and-loop-vars", () => Tpl.Deck(
            Tpl.Sp(2, "Summary", Tpl.Text("{% for i in items %}"), Tpl.Text("{{ loop.index }}/{{ loop.length }} {{ UPPER(i.name) }} {{ IF(loop.last, \"(last)\", \"\") }}"), Tpl.Text("{% endfor %}"), Tpl.Text("Sum {{ SUM(items.price * items.qty) }} of {{ COUNTA(items.name) }} items"))),
            """{"items":[{"name":"pen","qty":2,"price":1.5},{"name":"ink","qty":1,"price":10}]}"""),

        new("missing-data-lenient", () => Tpl.Deck(Tpl.Sp(2, "Body", Tpl.Text("Hello {{ nobody.name }}!"), Tpl.Text("Total {{ IFERROR(1/0, \"n/a\") }}"))),
            """{"x":1}"""),

        new("line-breaks-fields-and-spacing", () => Tpl.Deck(
            Tpl.Sp(2, "Mixed",
                "<a:p><a:pPr algn=\"ctr\"/><a:r><a:rPr lang=\"en-US\" sz=\"2400\"/><a:t>Dear {{ name }}</a:t></a:r><a:br><a:rPr lang=\"en-US\" sz=\"2400\"/></a:br><a:r><a:rPr lang=\"en-US\" i=\"1\"/><a:t>{{ message }}</a:t></a:r></a:p>",
                "<a:p><a:r><a:rPr lang=\"en-US\"/><a:t>Page </a:t></a:r><a:fld id=\"{B6F15528-21DE-4FAA-801E-634DDDAF4B2B}\" type=\"slidenum\"><a:rPr lang=\"en-US\"/><a:t>‹#›</a:t></a:fld><a:r><a:rPr lang=\"en-US\"/><a:t> of {{ pages }}</a:t></a:r></a:p>")),
            "{\"name\":\"Ada\",\"message\":\"line one\\nline two\",\"pages\":3}"),

        new("raw-and-literal-braces", () => Tpl.Deck(Tpl.Sp(2, "Doc", Tpl.Text("Write {% raw %}{{ name }}{% endraw %} to print a name, for {{ name }}."))),
            """{"name":"Ada"}"""),
    ];

    private static Presentation Slides(params string[][] slides) => Tpl.Deck(slides);

    private static Presentation Slides(params string[] oneSlide) => Tpl.Deck([oneSlide]);
}
