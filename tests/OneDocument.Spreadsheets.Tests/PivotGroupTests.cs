using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Spreadsheets.Pivots;

namespace RecursiveAI.OneDocument.Spreadsheets.Tests;

public sealed class PivotGroupTests
{
    internal static PivotBook Dates()
    {
        var b = new PivotBook
        {
            DataHeaders = ["Date", "Sales"],
            Data =
            [
                [new DateTime(2020, 1, 15), 10.0],
                [new DateTime(2020, 2, 10), 20.0],
                [new DateTime(2020, 2, 20), 30.0],
                [new DateTime(2021, 1, 5), 40.0],
                [new DateTime(2021, 3, 1), 50.0],
            ],
            SourceXml = "<worksheetSource ref=\"A1:B6\" sheet=\"Data\"/>",
            RowFields = [1, 0],
            DataFields = [PivotBook.DataField("Sum of Sales", 2)],
        };
        b.CacheFieldsOverride = "<cacheFields count=\"3\">"
            + "<cacheField name=\"Date\" numFmtId=\"14\"><sharedItems/><fieldGroup base=\"0\"><rangePr groupBy=\"months\"/><groupItems count=\"0\"/></fieldGroup></cacheField>"
            + "<cacheField name=\"Years\" numFmtId=\"0\" databaseField=\"0\"><fieldGroup base=\"0\"><rangePr groupBy=\"years\"/><groupItems count=\"0\"/></fieldGroup></cacheField>"
            + "<cacheField name=\"Sales\" numFmtId=\"0\"><sharedItems/></cacheField></cacheFields>";
        b.FieldAttrs[2] = string.Empty;
        return b;
    }

    [Fact]
    public void DatesGroupByYearsAndMonths()
    {
        PivotBook b = Dates();
        // pivot fields: 0 Date (months), 1 Years, 2 Sales; the years field is the outer row field.
        using Workbook wb = b.Refreshed(out PivotRefreshResult res);
        Assert.True(res.AllRefreshed);
        Assert.Equal(
        [
            "Row Labels|Sum of Sales",
            "2020|60",
            "Jan|10",
            "Feb|50",
            "2021|90",
            "Jan|40",
            "Mar|50",
            "Grand Total|150",
        ], PivotRefreshTests.Grid(wb.Sheets["Pivot"], "A3:B10"));
        string cache = System.Text.Encoding.UTF8.GetString(wb.PivotCaches.FindById(1)!.Markup.ToArray());
        Assert.Contains("<groupItems count=\"14\"><s v=\"&lt;1/15/2020\"/><s v=\"Jan\"/>", cache);
        Assert.Contains("<s v=\"&gt;3/1/2021\"/>", cache);
        Assert.Contains("startDate=\"2020-01-15T00:00:00\" endDate=\"2021-03-01T00:00:00\"", cache);
        Assert.Contains("<groupItems count=\"4\"><s v=\"&lt;1/15/2020\"/><s v=\"2020\"/><s v=\"2021\"/><s v=\"&gt;3/1/2021\"/>", cache);
    }

    [Fact]
    public void QuartersAndDaysGroup()
    {
        PivotBook b = Dates();
        b.RowFields = [0];
        b.CacheFieldsOverride = b.CacheFieldsOverride!.Replace("groupBy=\"months\"", "groupBy=\"quarters\"", StringComparison.Ordinal);
        using Workbook wb = b.Refreshed(out _);
        Assert.Equal(["Qtr1|150"], PivotRefreshTests.Grid(wb.Sheets["Pivot"], "A4:B4"));
    }

    [Fact]
    public void NumbersGroupIntoRanges()
    {
        var b = new PivotBook { RowFields = [4], DataFields = [PivotBook.DataField("Sum of Sales", 3)] };
        b.CacheFieldsOverride = "<cacheFields count=\"5\">" + string.Concat(PivotBook.Headers.Take(4).Select(h => "<cacheField name=\"" + h + "\" numFmtId=\"0\"><sharedItems/></cacheField>"))
            + "<cacheField name=\"Units\" numFmtId=\"0\"><sharedItems/><fieldGroup base=\"4\"><rangePr autoStart=\"1\" autoEnd=\"1\" groupInterval=\"3\"/><groupItems count=\"0\"/></fieldGroup></cacheField></cacheFields>";
        using Workbook wb = b.Refreshed(out _);
        Assert.Equal(["Row Labels|Sum of Sales", "1-3|450", "4-6|420", "7-9|200", "Grand Total|1070"], PivotRefreshTests.Grid(wb.Sheets["Pivot"], "A3:B7"));
        string cache = System.Text.Encoding.UTF8.GetString(wb.PivotCaches.FindById(1)!.Markup.ToArray());
        Assert.Contains("startNum=\"1\" endNum=\"8\"", cache);
        Assert.Contains("<groupItems count=\"5\"><s v=\"&lt;1\"/><s v=\"1-3\"/>", cache);
    }

    [Fact]
    public void ManualGroupsFollowTheirItemsAndNewItemsGetTheirOwnGroup()
    {
        var b = new PivotBook { RowFields = [0], DataFields = [PivotBook.DataField("Sum of Sales", 3)] };
        b.Data = [.. PivotBook.Rows, ["North", "A", "Q1", 5.0, 1.0]];
        b.SourceXml = "<worksheetSource ref=\"A1:E10\" sheet=\"Data\"/>";
        b.CacheFieldsOverride = "<cacheFields count=\"5\"><cacheField name=\"Region\" numFmtId=\"0\"><sharedItems count=\"2\"><s v=\"East\"/><s v=\"West\"/></sharedItems>"
            + "<fieldGroup base=\"0\"><discretePr count=\"2\"><x v=\"0\"/><x v=\"0\"/></discretePr><groupItems count=\"1\"><s v=\"Everything\"/></groupItems></fieldGroup></cacheField>"
            + string.Concat(PivotBook.Headers.Skip(1).Select(h => "<cacheField name=\"" + h + "\" numFmtId=\"0\"><sharedItems/></cacheField>")) + "</cacheFields>";
        b.FieldItems[0] = "<items count=\"2\"><item x=\"0\"/><item t=\"default\"/></items>";
        using Workbook wb = b.Refreshed(out _);
        Assert.Equal(["Row Labels|Sum of Sales", "Everything|1070", "North|5", "Grand Total|1075"], PivotRefreshTests.Grid(wb.Sheets["Pivot"], "A3:B6"));
    }

    [Fact]
    public void DateColumnFlagsInTheCache()
    {
        PivotBook b = Dates();
        b.RowFields = [];
        b.DataFields = [PivotBook.DataField("Sum of Sales", 2)];
        b.ColFields = [];
        b.CacheFieldsOverride = null;
        b.DataHeaders = ["Date", "Sales"];
        using Workbook wb = b.Refreshed(out _);
        string cache = System.Text.Encoding.UTF8.GetString(wb.PivotCaches.FindById(1)!.Markup.ToArray());
        Assert.Contains("<sharedItems containsSemiMixedTypes=\"0\" containsNonDate=\"0\" containsDate=\"1\" containsString=\"0\" minDate=\"2020-01-15T00:00:00\" maxDate=\"2021-03-01T00:00:00\" count=\"5\"><d v=\"2020-01-15T00:00:00\"/>", cache);
    }

    [Fact]
    public void MixedAndBlankColumnsFlagTheirKinds()
    {
        var b = new PivotBook
        {
            DataHeaders = ["Mixed", "Words", "Nums"],
            Data = [["a", "x", 1.0], [2.0, "y", 2.5], [true, null!, 4.0], [null!, "x", 1.0]],
            SourceXml = "<worksheetSource ref=\"A1:C5\" sheet=\"Data\"/>",
            RowFields = [0],
            DataFields = [PivotBook.DataField("Sum", 2)],
        };
        using Workbook wb = b.Refreshed(out _);
        string cache = System.Text.Encoding.UTF8.GetString(wb.PivotCaches.FindById(1)!.Markup.ToArray());
        Assert.Contains("<cacheField name=\"Mixed\" numFmtId=\"0\"><sharedItems containsBlank=\"1\" containsMixedTypes=\"1\" containsNumber=\"1\" containsInteger=\"1\" minValue=\"2\" maxValue=\"2\" count=\"4\">", cache);
        Assert.Contains("<cacheField name=\"Words\" numFmtId=\"0\"><sharedItems containsBlank=\"1\" count=\"3\"><s v=\"x\"/><s v=\"y\"/><m/></sharedItems>", cache);
        Assert.Contains("<cacheField name=\"Nums\" numFmtId=\"0\"><sharedItems containsSemiMixedTypes=\"0\" containsString=\"0\" containsNumber=\"1\" minValue=\"1\" maxValue=\"4\"/>", cache);
        Assert.Equal(["Row Labels|Sum", "2|2.5", "a|1", "TRUE|4", "(blank)|1", "Grand Total|8.5"], PivotRefreshTests.Grid(wb.Sheets["Pivot"], "A3:B8"));
    }
}

public sealed class PivotLookupTests
{
    private static Workbook Book(out Worksheet ws)
    {
        var b = new PivotBook { RowFields = [0], ColFields = [2], DataFields = [PivotBook.DataField("Sum of Sales", 3)], Location = "A3:D7" };
        Workbook wb = b.Refreshed(out _);
        ws = wb.Sheets["Pivot"];
        return wb;
    }

    private static string Show(Worksheet ws, string a) => ws[a].Value.Kind switch
    {
        CellValueKind.Number => ws[a].Value.Number.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        CellValueKind.Error => CellErrors.ToText(ws[a].Value.Error),
        _ => ws.GetText(ws[a].Value),
    };

    [Theory]
    [InlineData("GETPIVOTDATA(\"Sales\",$A$3,\"Region\",\"East\",\"Qtr\",\"Q2\")", "230")]
    [InlineData("GETPIVOTDATA(\"Sum of Sales\",$A$3,\"Region\",\"West\",\"Qtr\",\"Q1\")", "300")]
    [InlineData("GETPIVOTDATA(\"Sales\",$A$3,\"Qtr\",\"Q2\",\"Region\",\"West\")", "240")]
    [InlineData("GETPIVOTDATA(\"Sales\",$A$3,\"Region\",\"West\")", "540")]
    [InlineData("GETPIVOTDATA(\"Sales\",$A$3,\"Qtr\",\"Q1\")", "600")]
    [InlineData("GETPIVOTDATA(\"Sales\",$A$3)", "1070")]
    [InlineData("GETPIVOTDATA(\"Sales\",B5,\"region\",\"east\")", "530")]
    [InlineData("GETPIVOTDATA(\"Sales\",$A$3,\"Region\",\"North\")", "#REF!")]
    [InlineData("GETPIVOTDATA(\"Units\",$A$3,\"Region\",\"East\")", "#REF!")]
    [InlineData("GETPIVOTDATA(\"Sales\",$A$3,\"Product\",\"A\")", "#REF!")]
    [InlineData("GETPIVOTDATA(\"Sales\",$A$20)", "#REF!")]
    [InlineData("GETPIVOTDATA(\"Sales\",$A$3,\"Region\")", "#REF!")]
    public void ReadsWhatTheTableShows(string formula, string expected)
    {
        using Workbook wb = Book(out Worksheet ws);
        ws["F1"].Formula = formula;
        wb.Recalculate();
        Assert.Equal(expected, Show(ws, "F1"));
    }

    [Fact]
    public void ItemCanComeFromACell()
    {
        using Workbook wb = Book(out Worksheet ws);
        ws["H1"].SetString("West");
        ws["F1"].Formula = "GETPIVOTDATA(\"Sales\",$A$3,\"Region\",H1)";
        wb.Recalculate();
        Assert.Equal("540", Show(ws, "F1"));
    }

    [Fact]
    public void AnswersFromTheLastRefreshUntilTheTableIsRefreshedAgain()
    {
        using Workbook wb = Book(out Worksheet ws);
        wb.Sheets["Data"]["D2"].SetNumber(1000);
        ws["F1"].Formula = "GETPIVOTDATA(\"Sales\",$A$3,\"Region\",\"East\")";
        wb.Recalculate();
        Assert.Equal("530", Show(ws, "F1"));
        wb.RefreshPivots();
        wb.RecalculateAll();
        Assert.Equal("1430", Show(ws, "F1"));
    }

    [Fact]
    public void WorksAcrossSheetsAndWithTwoDataFields()
    {
        var b = new PivotBook { RowFields = [0], DataFields = [PivotBook.DataField("Sum of Sales", 3), PivotBook.DataField("Sum of Units", 4)] };
        using Workbook wb = b.Refreshed(out _);
        Worksheet data = wb.Sheets["Data"];
        data["H1"].Formula = "GETPIVOTDATA(\"Sum of Units\",Pivot!$A$3,\"Region\",\"West\")";
        data["H2"].Formula = "GETPIVOTDATA(\"Sales\",Pivot!$A$3)";
        wb.Recalculate();
        Assert.Equal("23", Show(data, "H1"));
        Assert.Equal("1070", Show(data, "H2"));
    }

    [Fact]
    public void ReportFilterAnswersOnlyForTheShownItem()
    {
        var b = new PivotBook { RowFields = [0], DataFields = [PivotBook.DataField("Sum of Sales", 3)], Pages = [(2, 1)], Location = "A5:B8" };
        b.FieldItems[2] = "<items count=\"3\"><item x=\"0\"/><item x=\"1\"/><item t=\"default\"/></items>";
        b.CacheFieldsOverride = PivotBook.CacheWith(PivotBook.Headers, new() { [2] = ["Q1", "Q2"] });
        using Workbook wb = b.Refreshed(out _);
        Worksheet ws = wb.Sheets["Pivot"];
        ws["F1"].Formula = "GETPIVOTDATA(\"Sales\",$A$5,\"Region\",\"East\",\"Qtr\",\"Q2\")";
        ws["F2"].Formula = "GETPIVOTDATA(\"Sales\",$A$5,\"Region\",\"East\",\"Qtr\",\"Q1\")";
        wb.Recalculate();
        Assert.Equal("230", Show(ws, "F1"));
        Assert.Equal("#REF!", Show(ws, "F2"));
    }
}

public sealed class PivotDefectTests
{
    private static string[] Codes(OpenContext c) => [.. TestBook.Defects(c).Select(d => d.Code)];

    [Fact]
    public void PivotPartsThatDoNotResolveAreReported()
    {
        byte[] file = new PivotBook { RowFields = [0], DataFields = [PivotBook.DataField("S", 3)] }.Build();
        byte[] broken;
        using (OpcPackage p = OpcPackage.Open(new MemoryStream(file)))
        {
            p.SetRelationships("/xl/worksheets/sheet2.xml",
            [
                new Relationship("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotTable", "../pivotTables/missing.xml"),
                new Relationship("rId2", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotTable", "../pivotTables/bad.xml"),
            ]);
            p.AddPart("/xl/pivotTables/bad.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.pivotTable+xml", System.Text.Encoding.UTF8.GetBytes("<notapivot xmlns=\"" + TestBook.Main + "\"/>"));
            p.AddPart("/xl/pivotCache/bad.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.pivotCacheDefinition+xml", System.Text.Encoding.UTF8.GetBytes("<other xmlns=\"" + TestBook.Main + "\"/>"));
            p.SetRelationships("/xl/workbook.xml", [.. p.GetRelationships("/xl/workbook.xml").Where(r => r.Id != "rId3"), new Relationship("rId3", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotCacheDefinition", "pivotCache/bad.xml")]);
            using var ms = new MemoryStream();
            p.WriteTo(ms);
            broken = ms.ToArray();
        }

        var ctx = new OpenContext();
        using Workbook wb = TestBook.Open(broken, ctx);
        Assert.Empty(wb.Sheets["Pivot"].PivotTables);
        Assert.Empty(wb.PivotCaches);
        string[] codes = Codes(ctx);
        Assert.Contains("xlsx.pivot-table-part-missing", codes);
        Assert.Contains("xlsx.pivot-table-unreadable", codes);
        Assert.Contains("xlsx.pivot-cache-unreadable", codes);
        Assert.Empty(wb.RefreshPivots().Outcomes);
    }

    [Fact]
    public void FeaturesRefreshDoesNotReproduceAreReportedAndStillRender()
    {
        var b = new PivotBook { RowFields = [0], DataFields = [PivotBook.DataField("S", 3, showAs: "rankAscending")], TableXmlExtra = "<filters count=\"1\"><filter fld=\"0\" type=\"captionEqual\" id=\"1\"/></filters>" };
        b.FieldAttrs[0] = " sumSubtotal=\"1\" defaultSubtotal=\"0\"";
        var ctx = new OpenContext();
        using Workbook wb = Workbook.Open(new MemoryStream(b.Build()), ctx);
        Assert.True(wb.RefreshPivots().AllRefreshed);
        string[] codes = Codes(ctx);
        Assert.Contains("pivot.show-data-as-unsupported", codes);
        Assert.Contains("pivot.filters-not-applied", codes);
        Assert.Contains("pivot.custom-subtotals-unsupported", codes);
        Assert.Equal(530, wb.Sheets["Pivot"]["B4"].Value.Number);
    }

    [Fact]
    public void UnknownGroupingIsDroppedAndReported()
    {
        var b = new PivotBook { RowFields = [0], DataFields = [PivotBook.DataField("S", 3)] };
        b.CacheFieldsOverride = "<cacheFields count=\"5\"><cacheField name=\"Region\" numFmtId=\"0\"><sharedItems/></cacheField><cacheField name=\"Product\" numFmtId=\"0\"><sharedItems/></cacheField><cacheField name=\"Qtr\" numFmtId=\"0\"><sharedItems/></cacheField>"
            + "<cacheField name=\"Sales\" numFmtId=\"0\"><sharedItems/><fieldGroup base=\"3\"><rangePr groupBy=\"weeks\"/><groupItems count=\"0\"/></fieldGroup></cacheField><cacheField name=\"Units\" numFmtId=\"0\"><sharedItems/></cacheField></cacheFields>";
        var ctx = new OpenContext();
        using Workbook wb = Workbook.Open(new MemoryStream(b.Build()), ctx);
        Assert.True(wb.RefreshPivots().AllRefreshed);
        Assert.Contains("pivot.group-unsupported", Codes(ctx));
    }

    [Fact]
    public void UnreadableCalculatedFormulaShowsAnErrorAndIsReported()
    {
        var b = new PivotBook { RowFields = [0], DataFields = [PivotBook.DataField("Bad", 5)] };
        b.CacheFieldsOverride = "<cacheFields count=\"6\">" + string.Concat(PivotBook.Headers.Select(h => "<cacheField name=\"" + h + "\" numFmtId=\"0\"><sharedItems/></cacheField>"))
            + "<cacheField name=\"Bad\" numFmtId=\"0\" formula=\"Sales*(\" databaseField=\"0\"/></cacheFields>";
        b.FieldAttrs[5] = string.Empty;
        var ctx = new OpenContext();
        using Workbook wb = Workbook.Open(new MemoryStream(b.Build()), ctx);
        wb.RefreshPivots();
        Assert.Contains("pivot.calculated-field-unparsable", Codes(ctx));
        Assert.Equal(CellError.Value, wb.Sheets["Pivot"]["B4"].Value.Error);
    }

    [Fact]
    public void DeletedSourceColumnIsReportedAndItsReferencesDropped()
    {
        var b = new PivotBook { RowFields = [0, 1], DataFields = [PivotBook.DataField("G", 1), PivotBook.DataField("S", 3)] };
        b.DataHeaders = PivotBook.Headers;
        b.CacheFieldsOverride = PivotBook.CacheWith(["Region", "Gone", "Qtr", "Sales", "Units"], []);
        b.FieldAttrs[1] = string.Empty;
        var ctx = new OpenContext();
        using Workbook wb = Workbook.Open(new MemoryStream(b.Build()), ctx);
        wb.RefreshPivots();
        string[] codes = Codes(ctx);
        Assert.Contains("pivot.source-field-missing", codes);
        Assert.Contains("pivot.axis-field-dropped", codes);
        Assert.Contains("pivot.data-field-dropped", codes);
    }
}

public sealed class PivotSharedCacheTests
{
    [Fact]
    public void TablesOnOneCacheAreRefreshedTogether()
    {
        var b = new PivotBook { RowFields = [0], DataFields = [PivotBook.DataField("Sum of Sales", 3)] };
        byte[] one = b.Build();
        var second = new PivotBook { RowFields = [1], DataFields = [PivotBook.DataField("Sum of Sales", 3)], Location = "G3:H6", TableName = "PT2" };
        byte[] file;
        using (OpcPackage p = OpcPackage.Open(new MemoryStream(one)))
        {
            p.AddPart("/xl/pivotTables/pivotTable2.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.pivotTable+xml", System.Text.Encoding.UTF8.GetBytes(second.TableDef()));
            p.SetRelationships("/xl/pivotTables/pivotTable2.xml", [new Relationship("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotCacheDefinition", "../pivotCache/pivotCacheDefinition1.xml")]);
            p.SetRelationships("/xl/worksheets/sheet2.xml",
            [
                .. p.GetRelationships("/xl/worksheets/sheet2.xml"),
                new Relationship("rId2", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotTable", "../pivotTables/pivotTable2.xml"),
            ]);
            using var ms = new MemoryStream();
            p.WriteTo(ms);
            file = ms.ToArray();
        }

        using Workbook wb = TestBook.Open(file);
        PivotRefreshResult res = wb.RefreshPivots();
        Assert.Equal(2, res.Outcomes.Count);
        Assert.True(res.AllRefreshed);
        Worksheet ws = wb.Sheets["Pivot"];
        Assert.Equal(["East|530", "West|540", "Grand Total|1070"], PivotRefreshTests.Grid(ws, "A4:B6"));
        Assert.Equal(["A|670", "B|400", "Grand Total|1070"], PivotRefreshTests.Grid(ws, "G4:H6"));
        Assert.Equal(2, wb.PivotCaches.FindById(1)!.Tables.Count);
    }

    [Fact]
    public void ReportFilterSelectionFollowsItsItemWhenTheItemListChanges()
    {
        var b = new PivotBook { RowFields = [0], DataFields = [PivotBook.DataField("Sum of Sales", 3)], Pages = [(2, 1)], Location = "A5:B8" };
        b.Data = [.. PivotBook.Rows, ["East", "A", "Q0", 7.0, 1.0]];
        b.SourceXml = "<worksheetSource ref=\"A1:E10\" sheet=\"Data\"/>";
        b.FieldAttrs[2] = " sortType=\"ascending\"";
        b.FieldItems[2] = "<items count=\"3\"><item x=\"0\"/><item x=\"1\"/><item t=\"default\"/></items>";
        b.CacheFieldsOverride = PivotBook.CacheWith(PivotBook.Headers, new() { [2] = ["Q1", "Q2"] });
        using Workbook wb = b.Refreshed(out _);
        // Q0 sorts first, so Q2 moves from position 1 to 2 and the filter still shows Q2.
        Assert.Equal("2", PivotCache.Child(PivotCache.Child(PivotRefreshTests.Table(wb).Root, "pageFields"), "pageField").GetAttribute("item"));
        Assert.Equal(["Qtr|Q2"], PivotRefreshTests.Grid(wb.Sheets["Pivot"], "A3:B3"));
        Assert.Equal(["East|230", "West|240"], PivotRefreshTests.Grid(wb.Sheets["Pivot"], "A6:B7"));
    }
}
