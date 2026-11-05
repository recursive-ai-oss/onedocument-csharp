using RecursiveAI.OneDocument.Spreadsheets.Pivots;

namespace RecursiveAI.OneDocument.Spreadsheets.Tests;

/// <summary>
/// The sample workbooks for the pivot behaviours that only Excel can settle (testdata/pivots). Each is generated, refreshed by this library and
/// saved; set ONEDOCUMENT_PIVOT_SAMPLES_OUT to a folder to write them.
/// </summary>
public sealed class PivotSampleTests
{
    private static readonly string[] Functions = ["count", "average", "max", "min", "product", "countNums", "stdDev", "stdDevp", "var", "varp"];

    private static IEnumerable<(string Name, PivotBook Book)> Samples()
    {
        string sales = PivotBook.DataField("Sum of Sales", 3);
        yield return ("compact-rows", new PivotBook { RowFields = [0], DataFields = [sales] });
        yield return ("rows-and-columns", new PivotBook { RowFields = [0], ColFields = [2], DataFields = [sales], Location = "A3:D7" });
        yield return ("no-grand-totals", new PivotBook { RowFields = [0], ColFields = [2], DataFields = [sales], TableAttrs = " rowGrandTotals=\"0\" colGrandTotals=\"0\"" });
        var outline = new PivotBook { RowFields = [0, 1], DataFields = [sales], TableAttrs = " compact=\"0\" compactData=\"0\"" };
        outline.FieldAttrs[0] = " compact=\"0\"";
        outline.FieldAttrs[1] = " compact=\"0\"";
        yield return ("outline-two-fields", outline);
        var tabular = new PivotBook { RowFields = [0, 1], DataFields = [sales], TableAttrs = " compact=\"0\" outline=\"0\"" };
        tabular.FieldAttrs[0] = " compact=\"0\" outline=\"0\"";
        tabular.FieldAttrs[1] = " compact=\"0\" outline=\"0\"";
        yield return ("tabular-two-fields", tabular);
        yield return ("compact-two-fields", new PivotBook { RowFields = [0, 1], DataFields = [sales] });
        var bottom = new PivotBook { RowFields = [0, 1], DataFields = [sales] };
        bottom.FieldAttrs[0] = " subtotalTop=\"0\"";
        yield return ("subtotals-at-bottom", bottom);
        yield return ("two-data-fields", new PivotBook { RowFields = [0], DataFields = [sales, PivotBook.DataField("Sum of Units", 4)] });
        yield return ("two-data-fields-and-columns", new PivotBook { RowFields = [0], ColFields = [2], DataFields = [sales, PivotBook.DataField("Sum of Units", 4)], Location = "A3:H8" });
        yield return ("summary-functions", new PivotBook
        {
            RowFields = [0],
            DataFields = [.. Functions.Select(f => PivotBook.DataField(f, 3, f))],
        });
        yield return ("show-values-as", new PivotBook
        {
            RowFields = [0],
            ColFields = [2],
            DataFields =
            [
                PivotBook.DataField("pct total", 3, showAs: "percentOfTotal"), PivotBook.DataField("pct row", 3, showAs: "percentOfRow"), PivotBook.DataField("pct col", 3, showAs: "percentOfCol"),
                PivotBook.DataField("diff", 4, showAs: "difference", baseField: "0", baseItem: "1048828"), PivotBook.DataField("run", 4, showAs: "runTotal", baseField: "0"),
            ],
        });
        var page = new PivotBook { RowFields = [0], DataFields = [sales], Pages = [(2, 1)], Location = "A5:B8" };
        page.FieldItems[2] = "<items count=\"3\"><item x=\"0\"/><item x=\"1\"/><item t=\"default\"/></items>";
        page.CacheFieldsOverride = PivotBook.CacheWith(PivotBook.Headers, new() { [2] = ["Q1", "Q2"] });
        yield return ("report-filter", page);
        yield return ("date-groups", PivotGroupTests.Dates());
        var range = new PivotBook { RowFields = [4], DataFields = [sales] };
        range.CacheFieldsOverride = "<cacheFields count=\"5\">" + string.Concat(PivotBook.Headers.Take(4).Select(h => "<cacheField name=\"" + h + "\" numFmtId=\"0\"><sharedItems/></cacheField>"))
            + "<cacheField name=\"Units\" numFmtId=\"0\"><sharedItems/><fieldGroup base=\"4\"><rangePr autoStart=\"1\" autoEnd=\"1\" groupInterval=\"3\"/><groupItems count=\"0\"/></fieldGroup></cacheField></cacheFields>";
        yield return ("number-ranges", range);
        var calc = new PivotBook { RowFields = [0], DataFields = [PivotBook.DataField("Sum of Bonus", 5)] };
        calc.CacheFieldsOverride = "<cacheFields count=\"6\">" + string.Concat(PivotBook.Headers.Select(h => "<cacheField name=\"" + h + "\" numFmtId=\"0\"><sharedItems/></cacheField>"))
            + "<cacheField name=\"Bonus\" numFmtId=\"0\" formula=\"Sales*0.1+Units\" databaseField=\"0\"/></cacheFields>";
        calc.FieldAttrs[5] = string.Empty;
        yield return ("calculated-field", calc);
        yield return ("mixed-types", new PivotBook
        {
            DataHeaders = ["Mixed", "Words", "Nums"],
            Data = [["a", "x", 1.0], [2.0, "y", 2.5], [true, null!, 4.0], [null!, "x", 1.0]],
            SourceXml = "<worksheetSource ref=\"A1:C5\" sheet=\"Data\"/>",
            RowFields = [0],
            DataFields = [PivotBook.DataField("Sum", 2)],
        });
        var hidden = new PivotBook { RowFields = [0], DataFields = [sales], Location = "A3:B7" };
        hidden.FieldItems[0] = "<items count=\"3\"><item x=\"0\"/><item h=\"1\" x=\"1\"/><item t=\"default\"/></items>";
        hidden.CacheFieldsOverride = PivotBook.CacheWith(PivotBook.Headers, new() { [0] = ["East", "West"] });
        yield return ("hidden-item", hidden);
    }

    [Fact]
    public void EverySampleRefreshesAndSavesAndOpensAgain()
    {
        string? outDir = Environment.GetEnvironmentVariable("ONEDOCUMENT_PIVOT_SAMPLES_OUT");
        if (outDir is not null)
        {
            Directory.CreateDirectory(outDir);
        }

        foreach ((string name, PivotBook book) in Samples())
        {
            using Workbook wb = book.Refreshed(out PivotRefreshResult res);
            Assert.True(res.AllRefreshed, name);
            byte[] file = TestBook.Save(wb);
            using Workbook again = TestBook.Open(file);
            Assert.Equal(res.Outcomes[0].Range, again.Sheets["Pivot"].PivotTables.First().Location);
            Assert.False(again.HasUncommittedChanges, name);
            if (outDir is not null)
            {
                File.WriteAllBytes(Path.Combine(outDir, name + ".xlsx"), file);
            }
        }
    }
}
