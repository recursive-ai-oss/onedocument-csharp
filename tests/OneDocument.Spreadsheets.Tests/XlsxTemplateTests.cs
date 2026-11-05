using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Spreadsheets.Templating;
using RecursiveAI.OneDocument.Templating;

namespace RecursiveAI.OneDocument.Spreadsheets.Tests;

public partial class XlsxTemplateTests
{
    private static readonly string GoldenDir = Path.Combine(Path.GetDirectoryName(Source())!, "..", "..", "testdata", "templates", "xlsx");

    private static string Source([CallerFilePath] string path = "") => path;

    private sealed record Case(string Name, Action<Workbook> Build, string Json, Func<Workbook, string>? Extra = null, Func<Workbook>? Create = null);

    private static void Put(Worksheet s, string address, object? value, string? formula = null)
    {
        Cell c = s[address];
        switch (value)
        {
            case null:
                break;
            case string t:
                c.SetString(t);
                break;
            case int i:
                c.SetNumber(i);
                break;
            case double d:
                c.SetNumber(d);
                break;
            case bool b:
                c.SetBoolean(b);
                break;
        }

        if (formula is not null)
        {
            c.Formula = formula;
        }
    }

    private static Worksheet First(Workbook wb) => wb.Sheets[0];

    private static IEnumerable<Case> Cases()
    {
        const string items = """{"title":"Autumn","items":[{"name":"Bolt","qty":3,"price":1.5},{"name":"Nut","qty":10,"price":0.25},{"name":"Gear","qty":2,"price":12}]}""";
        yield return new("values", wb => { Put(First(wb), "A1", "Report {{ title }}"); Put(First(wb), "A2", "{{ items.length }}"); Put(First(wb), "B2", "{{ TRUE }}"); }, items);
        yield return new("typed-values", wb => { Put(First(wb), "A1", "{{ MAX(items.qty) }}"); Put(First(wb), "B1", "{{ SUM(items.qty) }}"); Put(First(wb), "C1", "{{ SUM(items.qty) }} pcs"); Put(First(wb), "D1", "{{ 1/0 }}"); }, items);
        yield return new("row-block", wb =>
        {
            Worksheet s = First(wb);
            Put(s, "A1", "Name"); Put(s, "B1", "Qty");
            Put(s, "A2", "{% for it in items %}{{ it.name }}"); Put(s, "B2", "{{ it.qty }}{% endfor %}");
            Put(s, "A3", "end");
        }, items);
        yield return new("row-block-sum-grows", wb =>
        {
            Worksheet s = First(wb);
            Put(s, "A1", "Name"); Put(s, "B1", "Qty");
            Put(s, "A2", "{% for it in items %}{{ it.name }}"); Put(s, "B2", "{{ it.qty }}{% endfor %}");
            Put(s, "A3", "Total", null); Put(s, "B3", null, "SUM(B2:B2)");
            Put(s, "D1", null, "B3*2");
        }, items);
        yield return new("row-relative-formula", wb =>
        {
            Worksheet s = First(wb);
            Put(s, "A2", "{% for it in items %}{{ it.name }}"); Put(s, "B2", "{{ it.qty }}"); Put(s, "C2", "{{ it.price }}"); Put(s, "D2", null, "B2*C2");
            Put(s, "E2", "{% endfor %}"); Put(s, "D3", null, "SUM(D2:D2)");
        }, items);
        yield return new("band-two-rows", wb =>
        {
            Worksheet s = First(wb);
            Put(s, "A1", "{% for it in items %}{{ it.name }}"); Put(s, "B1", "{{ it.qty }}");
            Put(s, "A2", "price"); Put(s, "B2", "{{ it.price }}{% endfor %}"); Put(s, "C2", null, "B2*B1");
            Put(s, "A3", "footer");
        }, items);
        yield return new("column-block", wb =>
        {
            Worksheet s = First(wb);
            Put(s, "A1", "Item"); Put(s, "A2", "Qty");
            Put(s, "B1", "{% for it in items scope=column %}{{ it.name }}"); Put(s, "B2", "{{ it.qty }}{% endfor %}");
            Put(s, "C3", null, "SUM(B2:B2)");
        }, items);
        yield return new("sheet-scope", wb =>
        {
            Worksheet s = First(wb);
            s.Rename("Region {{ r.name }}");
            Put(s, "A1", "{% for r in regions scope=sheet %}{{ r.name }}"); Put(s, "A2", "Total"); Put(s, "B2", "{{ r.total }}{% endfor %}"); Put(s, "C2", null, "B2*2");
        }, """{"regions":[{"name":"North","total":10},{"name":"South","total":20},{"name":"North","total":5}]}""");
        yield return new("nested-blocks", wb =>
        {
            Worksheet s = First(wb);
            Put(s, "A1", "{% for g in groups %}{{ g.name }}");
            Put(s, "A2", "{% for m in g.members %}{{ m }}"); Put(s, "B2", "{% endfor %}");
            Put(s, "A3", "{% endfor %}");
        }, """{"groups":[{"name":"A","members":["x","y"]},{"name":"B","members":["z"]}]}""");
        yield return new("merged-in-row", wb =>
        {
            Worksheet s = First(wb);
            Put(s, "A1", "{% for it in items %}{{ it.name }}"); Put(s, "C1", "{% endfor %}"); s.Merge(CellRange.Parse("A1:B1")); s.Merge(CellRange.Parse("A2:C2")); Put(s, "A2", "footer");
        }, items);
        yield return new("table-grows", wb =>
        {
            Worksheet s = First(wb);
            Put(s, "A1", "Name"); Put(s, "B1", "Qty");
            Put(s, "A2", "{% for it in items %}{{ it.name }}"); Put(s, "B2", "{{ it.qty }}{% endfor %}");
            s.Tables.Add("Parts", CellRange.Parse("A1:B2"));
        }, items, wb => string.Join(";", First(wb).Tables.Select(t => t.Name + "=" + t.Range)));
        yield return new("defined-name-grows", wb =>
        {
            Worksheet s = First(wb);
            Put(s, "A1", "{% for it in items %}x"); Put(s, "B1", "{{ it.qty }}{% endfor %}"); Put(s, "A2", "t"); Put(s, "B2", null, "SUM(Qty)");
            wb.Names.Add("Qty", "Sheet1!$B$1:$B$1");
            wb.Names.Add("Top", "Sheet1!$A$2");
        }, items, wb => string.Join(";", wb.Names.Select(n => n.Name + "=" + n.Formula)));
        yield return new("cf-and-validation", wb =>
        {
            Worksheet s = First(wb);
            Put(s, "A1", "{% for it in items %}{{ it.qty }}"); Put(s, "B1", "{% endfor %}"); Put(s, "A2", "foot");
            s.ConditionalFormats.Add(CellRange.Parse("A1:A1"), ConditionalRuleSpec.CellIs("greaterThan", "5", 0));
            s.Validations.Add(CellRange.Parse("A1:B2"), DataValidationSpec.List("a", "b"));
        }, items, wb => string.Join(";", First(wb).ConditionalFormats.SelectMany(c => c.Ranges).Select(r => r.ToString())) + "|" + string.Join(";", First(wb).Validations.SelectMany(c => c.Ranges).Select(r => r.ToString())));
        yield return new("missing-data", wb => { Put(First(wb), "A1", "{{ nothing }}"); Put(First(wb), "A2", "{{ title }}"); }, items);
        yield return new("if-row", wb =>
        {
            Worksheet s = First(wb);
            Put(s, "A1", "top"); Put(s, "A2", "{% if show %}secret"); Put(s, "B2", "{% endif %}"); Put(s, "A3", "bottom"); Put(s, "B3", null, "A1&A3");
        }, """{"show":false}""");
        yield return new("empty-loop-ref-error", wb =>
        {
            Worksheet s = First(wb);
            Put(s, "A1", "{% for it in items %}{{ it.name }}"); Put(s, "B1", "{% endfor %}"); Put(s, "A2", "after"); Put(s, "C2", null, "A1+1"); Put(s, "D2", null, "SUM(A1:A2)");
        }, """{"items":[]}""");
        yield return new("cross-sheet", wb =>
        {
            Worksheet s = First(wb);
            Put(s, "A1", "{% for it in items %}{{ it.qty }}"); Put(s, "B1", "{% endfor %}");
            Worksheet o = wb.Sheets.Add("Summary");
            Put(o, "A1", null, "SUM(Sheet1!A1:A1)"); Put(o, "A2", null, "Sheet1!A1");
        }, items, wb => wb.Sheets["Summary"]["A1"].Formula + "|" + wb.Sheets["Summary"]["A2"].Formula);
        yield return new("empty-rows-keep-gaps", wb =>
        {
            Worksheet s = First(wb);
            Put(s, "A1", "head"); Put(s, "A4", "{% for it in items %}{{ it.name }}"); Put(s, "B4", "{% endfor %}"); Put(s, "A8", "tail"); Put(s, "B8", null, "A8&A1");
        }, items);
        yield return new("shared-formula", wb =>
        {
            Worksheet s = First(wb);
            Put(s, "A1", "{% for it in items %}{{ it.qty }}"); Put(s, "B1", "{% endfor %}");
            Put(s, "A5", 1); Put(s, "A6", 2);
            s["B5"].FormulaDetail = new CellFormula("A5*2", CellFormulaKind.Shared, "B5:B6", 0);
            s["B6"].FormulaDetail = new CellFormula(string.Empty, CellFormulaKind.Shared, null, 0);
        }, items);
        yield return new("loop-index-and-else", wb =>
        {
            Worksheet s = First(wb);
            Put(s, "A1", "{% for it in items %}{{ loop.index }}"); Put(s, "B1", "{{ it.name }}"); Put(s, "C1", "{% else %}none{% endfor %}");
        }, """{"items":[{"name":"a"},{"name":"b"}]}""");
        yield return new("sheet-name-clash", wb =>
        {
            Worksheet s = First(wb);
            wb.Sheets.Add("North");
            s.Rename("{{ title }}");
            Put(s, "A1", "x {{ title }}");
        }, """{"title":"North"}""");
        foreach (Case feature in FeatureCases())
        {
            yield return feature;
        }

        yield return new("date-and-bool-data", wb =>
        {
            Worksheet s = First(wb);
            Put(s, "A1", "{{ when }}"); Put(s, "B1", "{{ flag }}"); Put(s, "C1", "{{ TEXT(when, \"dd/mm/yyyy\") }}");
        }, """{"when":"2024-03-09T00:00:00","flag":true}""");
    }

    public static IEnumerable<object[]> CaseNames() => Cases().Select(static c => new object[] { c.Name });

    private static Workbook Build(Case c)
    {
        Workbook wb = c.Create?.Invoke() ?? Workbook.Create();
        c.Build(wb);
        return wb;
    }

    private static string Dump(Workbook wb, Func<Workbook, string>? extra)
    {
        var sb = new StringBuilder();
        foreach (Worksheet s in wb.Sheets)
        {
            sb.Append('[').Append(s.Name).Append("] merges=").AppendLine(string.Join(",", s.MergedCells.Select(m => m.ToString())));
            foreach (CellRowView row in s.Store.EnumerateRows())
            {
                for (int i = 0; i < row.Count; i++)
                {
                    int col = row.ColumnAt(i);
                    CellValue v = row.ValueAt(i);
                    sb.Append(new CellAddress(row.Row, col)).Append(" s=").Append(row.StyleAt(i));
                    sb.Append(' ').Append(v.Kind switch
                    {
                        CellValueKind.Number => "n " + v.Number.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                        CellValueKind.Boolean => "b " + v.Boolean,
                        CellValueKind.Text => "t " + s.GetText(v),
                        CellValueKind.Error => "e " + s.GetText(v),
                        _ => "-",
                    });
                    if (row.FormulaAt(i) is { } f)
                    {
                        sb.Append(" f=").Append(f.Text);
                    }

                    sb.AppendLine();
                }
            }
        }

        if (extra is not null)
        {
            sb.Append("extra: ").AppendLine(extra(wb));
        }

        return sb.ToString().Replace("\r\n", "\n");
    }

    private static (string Dump, IReadOnlyList<Defect> Warnings) Render(Case c)
    {
        using Workbook wb = Build(c);
        using JsonDocument json = JsonDocument.Parse(c.Json);
        XlsxTemplateResult r = XlsxTemplateBinder.Render(wb, json, new TemplateOptions { Clock = new FixedClock() });
        Assert.True(wb.NeedsRecalculation);
        return (Dump(wb, c.Extra), r.Warnings);
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2024, 3, 9, 0, 0, 0, TimeSpan.Zero);
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void RendersTheGoldenOutput(string name)
    {
        Case c = Cases().Single(x => x.Name == name);
        (string dump, _) = Render(c);
        string file = Path.Combine(GoldenDir, name + ".expected.txt");
        if (Environment.GetEnvironmentVariable("ONEDOCUMENT_UPDATE_GOLDEN") == "1")
        {
            Directory.CreateDirectory(GoldenDir);
            File.WriteAllText(file, dump);
            File.WriteAllText(Path.Combine(GoldenDir, name + ".data.json"), c.Json);
            using Workbook t = Build(c);
            t.SaveAs(Path.Combine(GoldenDir, name + ".template.xlsx"));
        }

        Assert.Equal(File.ReadAllText(file).Replace("\r\n", "\n"), dump);
        Assert.Equal(dump, Render(c).Dump);
    }

    [Fact]
    public void ARenderedWorkbookSavesToTheSameBytesEveryTime()
    {
        Case c = Cases().Single(x => x.Name == "row-block-sum-grows");
        byte[] Save()
        {
            using Workbook wb = Build(c);
            using JsonDocument json = JsonDocument.Parse(c.Json);
            XlsxTemplateBinder.Render(wb, json);
            using var ms = new MemoryStream();
            wb.SaveAs(ms, OfficeFormat.Xlsx);
            return ms.ToArray();
        }

        Assert.Equal(Save(), Save());
    }

    [Fact]
    public void ASheetWithoutTagsIsLeftUntouched()
    {
        using Workbook wb = Workbook.Create();
        Put(wb.Sheets[0], "A1", "plain");
        Worksheet t = wb.Sheets.Add("T");
        Put(t, "A1", "{{ title }}");
        using JsonDocument json = JsonDocument.Parse("""{"title":"x"}""");
        wb.Sheets[0].Load();
        XlsxTemplateBinder.Render(wb, json);
        Assert.Equal("plain", wb.Sheets[0]["A1"].Text);
        Assert.Equal("x", wb.Sheets["T"]["A1"].Text);
    }

    [Fact]
    public void ATypedTagKeepsItsType()
    {
        Case c = Cases().Single(x => x.Name == "typed-values");
        (string dump, _) = Render(c);
        Assert.Contains("A1 s=0 n 10", dump);
        Assert.Contains("B1 s=0 n 15", dump);
        Assert.Contains("C1 s=0 t 15 pcs", dump);
        Assert.Contains("D1 s=0 e #DIV/0!", dump);
    }

    [Fact]
    public void MissingDataIsAWarningWithAPlace()
    {
        Case c = Cases().Single(x => x.Name == "missing-data");
        (_, IReadOnlyList<Defect> warnings) = Render(c);
        Defect d = Assert.Single(warnings, w => w.Code == "template.missing-data");
        Assert.Contains("A1", d.Location.ToString());
    }

    [Fact]
    public void ABrokenTemplateThrowsALocatedError()
    {
        using Workbook wb = Workbook.Create();
        Put(wb.Sheets[0], "B3", "{% for x in %}");
        TemplateException e = Assert.Throws<TemplateException>(() => XlsxTemplateBinder.Render(wb, null));
        Assert.Contains("B3", e.Defect.Location.ToString());
    }

    [Fact]
    public void ASheetNameThatIsTakenIsRepairedWithAWarning()
    {
        Case c = Cases().Single(x => x.Name == "sheet-name-clash");
        (_, IReadOnlyList<Defect> warnings) = Render(c);
        Assert.Contains(warnings, w => w.Code == "template.sheet-name-repaired");
    }

    [Fact]
    public void AnImageTagWithBadDataIsReportedWithItsCell()
    {
        using Workbook wb = Workbook.Create();
        Put(wb.Sheets[0], "A1", "{% image logo %}");
        using JsonDocument json = JsonDocument.Parse("""{"logo":"x"}""");
        XlsxTemplateResult r = XlsxTemplateBinder.Render(wb, json);
        Defect d = Assert.Single(r.Warnings, w => w.Code == "template.image-invalid");
        Assert.Contains("A1", d.Location.ToString());
    }

    [Fact]
    public void ABlockPastTheLastColumnIsAFatalOverflow()
    {
        using Workbook wb = Workbook.Create();
        Put(wb.Sheets[0], "A1", "{% for i in n scope=column %}{{ i }}{% endfor %}");
        var data = new Dictionary<string, object?> { ["n"] = Enumerable.Range(0, 16385).ToArray() };
        TemplateException e = Assert.Throws<TemplateException>(() => XlsxTemplateBinder.Render(wb, data));
        Assert.Equal("template.sheet-overflow", e.Defect.Code);
    }
}
