using System.Globalization;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Spreadsheets.Pivots;

namespace RecursiveAI.OneDocument.Spreadsheets.Tests;

public sealed class PivotRefreshTests
{
    internal static string[] Grid(Worksheet ws, string range)
    {
        CellRange r = CellRange.Parse(range);
        var lines = new List<string>();
        for (int row = r.TopLeft.Row; row <= r.BottomRight.Row; row++)
        {
            var cells = new List<string>();
            for (int col = r.TopLeft.Column; col <= r.BottomRight.Column; col++)
            {
                CellValue v = ws[row, col].Value;
                cells.Add(v.Kind switch
                {
                    CellValueKind.Number => Math.Round(v.Number, 6).ToString("R", CultureInfo.InvariantCulture),
                    CellValueKind.Empty => string.Empty,
                    CellValueKind.Boolean => v.Boolean ? "TRUE" : "FALSE",
                    _ => ws.GetText(v),
                });
            }

            lines.Add(string.Join("|", cells));
        }

        return [.. lines];
    }

    internal static PivotTable Table(Workbook wb) => wb.Sheets["Pivot"].PivotTables.First();

    internal static string Attr(PivotTable t, string element, string name) => PivotCache.Child(t.Root, element).GetAttribute(name) ?? string.Empty;

    private static PivotBook One(params string[] data) => new() { RowFields = [0], DataFields = [.. data.Length == 0 ? [PivotBook.DataField("Sum of Sales", 3)] : data] };

    [Fact]
    public void CompactOneRowFieldWithGrandTotal()
    {
        using Workbook wb = One().Refreshed(out PivotRefreshResult res);
        Assert.True(res.AllRefreshed);
        Worksheet ws = wb.Sheets["Pivot"];
        Assert.Equal(["Row Labels|Sum of Sales", "East|530", "West|540", "Grand Total|1070"], Grid(ws, "A3:B6"));
        PivotTable t = Table(wb);
        Assert.Equal("A3:B6", Attr(t, "location", "ref"));
        Assert.Equal("1", Attr(t, "location", "firstHeaderRow"));
        Assert.Equal("1", Attr(t, "location", "firstDataRow"));
        Assert.Equal("1", Attr(t, "location", "firstDataCol"));
        Assert.Equal("3", Attr(t, "rowItems", "count"));
        Assert.Equal(8, wb.PivotCaches.FindById(1)!.RecordCount);
        Assert.Equal(res.Outcomes[0].Range, CellRange.Parse("A3:B6"));
    }

    [Fact]
    public void RowAndColumnFieldsWithTotals()
    {
        var b = One();
        b.ColFields = [2];
        b.Location = "A3:D7";
        using Workbook wb = b.Refreshed(out _);
        Worksheet ws = wb.Sheets["Pivot"];
        Assert.Equal(
        [
            "Sum of Sales|Column Labels||",
            "Row Labels|Q1|Q2|Grand Total",
            "East|300|230|530",
            "West|300|240|540",
            "Grand Total|600|470|1070",
        ], Grid(ws, "A3:D7"));
        PivotTable t = Table(wb);
        Assert.Equal("A3:D7", Attr(t, "location", "ref"));
        Assert.Equal("2", Attr(t, "location", "firstDataRow"));
        Assert.Equal("3", Attr(t, "colItems", "count"));
    }

    [Fact]
    public void GrandTotalsCanBeSwitchedOff()
    {
        var b = One();
        b.ColFields = [2];
        b.TableAttrs = " rowGrandTotals=\"0\" colGrandTotals=\"0\"";
        using Workbook wb = b.Refreshed(out _);
        Assert.Equal(["Row Labels|Q1|Q2", "East|300|230", "West|300|240"], Grid(wb.Sheets["Pivot"], "A4:C6"));
        Assert.Equal("", Grid(wb.Sheets["Pivot"], "A7:A7")[0]);
    }

    [Theory]
    [InlineData("sum", 540)]
    [InlineData("count", 4)]
    [InlineData("average", 135)]
    [InlineData("max", 300)]
    [InlineData("min", 50)]
    [InlineData("product", 300.0 * 50 * 70 * 120)]
    [InlineData("countNums", 4)]
    [InlineData("var", 12966.666667)]
    [InlineData("varp", 9725)]
    [InlineData("stdDev", 113.871272)]
    [InlineData("stdDevp", 98.615415)]
    public void SummaryFunctions(string subtotal, double west)
    {
        using Workbook wb = One(PivotBook.DataField("X", 3, subtotal)).Refreshed(out _);
        CellValue v = wb.Sheets["Pivot"]["B5"].Value;
        Assert.Equal(west, v.Number, 2);
    }

    [Fact]
    public void CountCountsTextButCountNumsDoesNot()
    {
        using Workbook wb = One(PivotBook.DataField("C", 1, "count"), PivotBook.DataField("N", 1, "countNums")).Refreshed(out _);
        Assert.Equal(["Row Labels|C|N", "East|4|0", "West|4|0"], Grid(wb.Sheets["Pivot"], "A3:C5"));
    }

    [Fact]
    public void SingleItemVarianceIsDivideByZero()
    {
        var b = new PivotBook { RowFields = [1], DataFields = [PivotBook.DataField("V", 3, "var")], Data = [["East", "A", "Q1", 1.0, 1.0], ["East", "B", "Q1", 2.0, 1.0], ["East", "B", "Q1", 4.0, 1.0]] };
        using Workbook wb = b.Refreshed(out _);
        Assert.Equal(CellValueKind.Error, wb.Sheets["Pivot"]["B4"].Value.Kind);
        Assert.Equal(CellError.DivideByZero, wb.Sheets["Pivot"]["B4"].Value.Error);
        Assert.Equal(2.0, wb.Sheets["Pivot"]["B5"].Value.Number, 6);
    }

    [Fact]
    public void TwoDataFieldsOnColumns()
    {
        var b = One(PivotBook.DataField("Sum of Sales", 3), PivotBook.DataField("Sum of Units", 4));
        using Workbook wb = b.Refreshed(out _);
        Assert.Equal(["Row Labels|Sum of Sales|Sum of Units", "East|530|13", "West|540|23", "Grand Total|1070|36"], Grid(wb.Sheets["Pivot"], "A3:C6"));
        PivotTable t = Table(wb);
        Assert.Equal("0", Attr(t, "location", "firstHeaderRow"));
        Assert.Equal(0, t.RowFields[0]);
        Assert.Equal([-2], t.ColumnFields);
    }

    [Fact]
    public void TwoDataFieldsAcrossAColumnField()
    {
        var b = One(PivotBook.DataField("Sum of Sales", 3), PivotBook.DataField("Sum of Units", 4));
        b.ColFields = [2];
        b.Location = "A3:H8";
        using Workbook wb = b.Refreshed(out _);
        Worksheet ws = wb.Sheets["Pivot"];
        Assert.Equal(
        [
            "|Column Labels|||||",
            "|Q1||Q2||Total Sum of Sales|Total Sum of Units",
            "Row Labels|Sum of Sales|Sum of Units|Sum of Sales|Sum of Units||",
            "East|300|3|230|10|530|13",
            "West|300|4|240|19|540|23",
            "Grand Total|600|7|470|29|1070|36",
        ], Grid(ws, "A3:G8"));
    }

    [Fact]
    public void OutlineFormWithSubtotalsAtTop()
    {
        var b = new PivotBook { RowFields = [0, 1], DataFields = [PivotBook.DataField("Sum of Sales", 3)], Location = "A3:B9" };
        b.FieldAttrs[0] = " compact=\"0\"";
        b.FieldAttrs[1] = " compact=\"0\"";
        b.TableAttrs = " compact=\"0\" compactData=\"0\"";
        using Workbook wb = b.Refreshed(out _);
        Assert.Equal(
        [
            "Region|Product|Sum of Sales",
            "East||530",
            "|A|250",
            "|B|280",
            "West||540",
            "|A|420",
            "|B|120",
            "Grand Total||1070",
        ], Grid(wb.Sheets["Pivot"], "A3:C10"));
    }

    [Fact]
    public void TabularFormHasSubtotalRowsBelow()
    {
        var b = new PivotBook { RowFields = [0, 1], DataFields = [PivotBook.DataField("Sum of Sales", 3)], Location = "A3:B9" };
        b.FieldAttrs[0] = " compact=\"0\" outline=\"0\"";
        b.FieldAttrs[1] = " compact=\"0\" outline=\"0\"";
        b.TableAttrs = " compact=\"0\" outline=\"0\"";
        using Workbook wb = b.Refreshed(out _);
        Assert.Equal(
        [
            "Region|Product|Sum of Sales",
            "East|A|250",
            "|B|280",
            "East Total||530",
            "West|A|420",
            "|B|120",
            "West Total||540",
            "Grand Total||1070",
        ], Grid(wb.Sheets["Pivot"], "A3:C10"));
        PivotTable t = Table(wb);
        Assert.Equal("7", Attr(t, "rowItems", "count"));
    }

    [Fact]
    public void CompactTwoLevelsIndentChildren()
    {
        var b = new PivotBook { RowFields = [0, 1], DataFields = [PivotBook.DataField("Sum of Sales", 3)], Location = "A3:B9" };
        using Workbook wb = b.Refreshed(out _);
        Worksheet ws = wb.Sheets["Pivot"];
        Assert.Equal(["Row Labels|Sum of Sales", "East|530", "A|250", "B|280", "West|540", "A|420", "B|120", "Grand Total|1070"], Grid(ws, "A3:B10"));
        Assert.Equal(0, ws["A4"].Style.Alignment.Indent);
        Assert.Equal(1, ws["A5"].Style.Alignment.Indent);
    }

    [Fact]
    public void SubtotalsAtBottomInCompactForm()
    {
        var b = new PivotBook { RowFields = [0, 1], DataFields = [PivotBook.DataField("Sum of Sales", 3)] };
        b.FieldAttrs[0] = " subtotalTop=\"0\"";
        using Workbook wb = b.Refreshed(out _);
        Assert.Equal(["Row Labels|Sum of Sales", "East|", "A|250", "B|280", "East Total|530", "West|", "A|420", "B|120", "West Total|540", "Grand Total|1070"], Grid(wb.Sheets["Pivot"], "A3:B12"));
    }

    [Fact]
    public void NoSubtotalForAFieldThatSwitchesThemOff()
    {
        var b = new PivotBook { RowFields = [0, 1], DataFields = [PivotBook.DataField("Sum of Sales", 3)] };
        b.FieldAttrs[0] = " defaultSubtotal=\"0\"";
        using Workbook wb = b.Refreshed(out _);
        Assert.Equal(["Row Labels|Sum of Sales", "East|", "A|250", "B|280", "West|", "A|420", "B|120", "Grand Total|1070"], Grid(wb.Sheets["Pivot"], "A3:B10"));
    }

    [Fact]
    public void ShowDataAsPercentOfTotalRowAndColumn()
    {
        var b = One(PivotBook.DataField("T", 3, showAs: "percentOfTotal"), PivotBook.DataField("R", 3, showAs: "percentOfRow"), PivotBook.DataField("C", 3, showAs: "percentOfCol"));
        using Workbook wb = b.Refreshed(out _);
        Worksheet ws = wb.Sheets["Pivot"];
        Assert.Equal(530.0 / 1070, ws["B4"].Value.Number, 9);
        Assert.Equal(1.0, ws["C4"].Value.Number, 9);
        Assert.Equal(530.0 / 1070, ws["D4"].Value.Number, 9);
        Assert.Equal(1.0, ws["B6"].Value.Number, 9);
        Assert.Equal("0.00%", ws["B4"].Style.NumberFormatCode);
    }

    [Fact]
    public void ShowDataAsDifferenceAndRunningTotalAlongTheBaseField()
    {
        var b = One(PivotBook.DataField("D", 3, showAs: "difference", baseField: "0", baseItem: "1048828"), PivotBook.DataField("RT", 3, showAs: "runTotal", baseField: "0"));
        using Workbook wb = b.Refreshed(out _);
        Worksheet ws = wb.Sheets["Pivot"];
        Assert.Equal(CellValueKind.Empty, ws["B4"].Value.Kind);
        Assert.Equal(10, ws["B5"].Value.Number);
        Assert.Equal(530, ws["C4"].Value.Number);
        Assert.Equal(1070, ws["C5"].Value.Number);
    }

    [Fact]
    public void PercentDifferenceFromASpecificItem()
    {
        var b = One(PivotBook.DataField("PD", 3, showAs: "percentDiff", baseField: "0", baseItem: "0"));
        using Workbook wb = b.Refreshed(out _);
        Worksheet ws = wb.Sheets["Pivot"];
        Assert.Equal(CellValueKind.Empty, ws["B4"].Value.Kind);
        Assert.Equal((540.0 - 530) / 530, ws["B5"].Value.Number, 9);
    }

    [Fact]
    public void PageFieldSelectionFiltersTheRecords()
    {
        var b = One();
        b.Pages = [(2, 1)];
        b.Location = "A5:B8";
        b.FieldItems[2] = "<items count=\"3\"><item x=\"0\"/><item x=\"1\"/><item t=\"default\"/></items>";
        b.CacheFieldsOverride = PivotBook.CacheWith(PivotBook.Headers, new() { [2] = ["Q1", "Q2"] });
        using Workbook wb = b.Refreshed(out _);
        Worksheet ws = wb.Sheets["Pivot"];
        // Q2 is the second item of the field, so the page filter shows it.
        Assert.Equal(["Qtr|Q2"], Grid(ws, "A3:B3"));
        Assert.Equal(["Row Labels|Sum of Sales", "East|230", "West|240", "Grand Total|470"], Grid(ws, "A5:B8"));
        PivotTable t = Table(wb);
        Assert.Equal("1", Attr(t, "location", "rowPageCount"));
        Assert.Equal("1", PivotCache.Child(PivotCache.Child(t.Root, "pageFields"), "pageField").GetAttribute("item"));
    }

    [Fact]
    public void PageFieldWithoutSelectionShowsAll()
    {
        var b = One();
        b.Pages = [(2, null)];
        b.Location = "A5:B8";
        using Workbook wb = b.Refreshed(out _);
        Worksheet ws = wb.Sheets["Pivot"];
        Assert.Equal(["Qtr|(All)"], Grid(ws, "A3:B3"));
        Assert.Equal(["East|530", "West|540", "Grand Total|1070"], Grid(ws, "A6:B8"));
    }

    [Fact]
    public void TablePlacedTooHighIsMovedBelowItsPageFields()
    {
        var b = One();
        b.Pages = [(2, null)];
        b.Location = "A1:B4";
        using Workbook wb = b.Refreshed(out _);
        Assert.Equal("A3:B6", Attr(Table(wb), "location", "ref"));
        Assert.Equal("Qtr", wb.Sheets["Pivot"].GetText(wb.Sheets["Pivot"]["A1"].Value));
    }

    [Fact]
    public void HiddenItemsAreLeftOutOfTheTotals()
    {
        var b = One();
        b.FieldItems[0] = "<items count=\"3\"><item x=\"0\"/><item h=\"1\" x=\"1\"/><item t=\"default\"/></items>";
        b.CacheFieldsOverride = PivotBook.CacheWith(PivotBook.Headers, new() { [0] = ["East", "West"] });
        using Workbook wb = b.Refreshed(out _);
        Assert.Equal(["Row Labels|Sum of Sales", "East|530", "Grand Total|530"], Grid(wb.Sheets["Pivot"], "A3:B5"));
        Assert.Contains("h=\"1\"", System.Text.Encoding.UTF8.GetString(Table(wb).Markup.ToArray()));
    }

    [Fact]
    public void ItemsSortAscendingAndDescending()
    {
        var b = One();
        b.FieldAttrs[0] = " sortType=\"descending\"";
        using Workbook wb = b.Refreshed(out _);
        Assert.Equal(["West|540", "East|530"], Grid(wb.Sheets["Pivot"], "A4:B5"));
    }

    [Fact]
    public void ManualOrderIsKeptAndNewItemsFollow()
    {
        var b = One();
        b.Data = [.. PivotBook.Rows, ["North", "A", "Q1", 5.0, 1.0]];
        b.SourceXml = "<worksheetSource ref=\"A1:E10\" sheet=\"Data\"/>";
        b.FieldItems[0] = "<items count=\"3\"><item x=\"1\"/><item x=\"0\"/><item t=\"default\"/></items>";
        b.CacheFieldsOverride = "<cacheFields count=\"5\"><cacheField name=\"Region\" numFmtId=\"0\"><sharedItems count=\"2\"><s v=\"East\"/><s v=\"West\"/></sharedItems></cacheField>"
            + "<cacheField name=\"Product\" numFmtId=\"0\"><sharedItems/></cacheField><cacheField name=\"Qtr\" numFmtId=\"0\"><sharedItems/></cacheField><cacheField name=\"Sales\" numFmtId=\"0\"><sharedItems/></cacheField><cacheField name=\"Units\" numFmtId=\"0\"><sharedItems/></cacheField></cacheFields>";
        using Workbook wb = b.Refreshed(out _);
        // The stored order was West, East; North is new and goes after them.
        Assert.Equal(["Row Labels|Sum of Sales", "West|540", "East|530", "North|5", "Grand Total|1075"], Grid(wb.Sheets["Pivot"], "A3:B7"));
    }

    [Fact]
    public void CalculatedFieldUsesTheSumsOfTheOtherFields()
    {
        var b = new PivotBook { RowFields = [0] };
        b.DataFields = [PivotBook.DataField("Sum of Bonus", 5)];
        b.CacheFieldsOverride = "<cacheFields count=\"6\">" + string.Concat(PivotBook.Headers.Select(h => "<cacheField name=\"" + h + "\" numFmtId=\"0\"><sharedItems/></cacheField>"))
            + "<cacheField name=\"Bonus\" numFmtId=\"0\" formula=\"Sales*0.1+Units\" databaseField=\"0\"/></cacheFields>";
        b.FieldAttrs[5] = string.Empty;
        using Workbook wb = b.Refreshed(out _);
        Worksheet ws = wb.Sheets["Pivot"];
        Assert.Equal(53 + 13, ws["B4"].Value.Number, 6);
        Assert.Equal(54 + 23, ws["B5"].Value.Number, 6);
        Assert.Equal(107 + 36, ws["B6"].Value.Number, 6);
    }

    [Fact]
    public void CalculatedFieldNamesWithSpacesInQuotes()
    {
        var b = new PivotBook { RowFields = [0], DataHeaders = ["Region", "Unit Price", "Qty"], Data = [["East", 2.0, 3.0], ["East", 4.0, 5.0], ["West", 1.0, 1.0]], SourceXml = "<worksheetSource ref=\"A1:C4\" sheet=\"Data\"/>" };
        b.DataFields = [PivotBook.DataField("Total", 3)];
        b.CacheFieldsOverride = "<cacheFields count=\"4\"><cacheField name=\"Region\" numFmtId=\"0\"><sharedItems/></cacheField><cacheField name=\"Unit Price\" numFmtId=\"0\"><sharedItems/></cacheField><cacheField name=\"Qty\" numFmtId=\"0\"><sharedItems/></cacheField>"
            + "<cacheField name=\"Total\" numFmtId=\"0\" formula=\"'Unit Price' * Qty\" databaseField=\"0\"/></cacheFields>";
        b.FieldAttrs[3] = string.Empty;
        using Workbook wb = b.Refreshed(out _);
        // The formula runs on the sums: (2+4) * (3+5) = 48, not the sum of the products.
        Assert.Equal(48, wb.Sheets["Pivot"]["B4"].Value.Number);
        Assert.Equal(1, wb.Sheets["Pivot"]["B5"].Value.Number);
    }

    [Fact]
    public void ExternalSourcesAreNotSupportedAndLeftAlone()
    {
        var b = One();
        b.SourceType = "external";
        b.SourceXml = "<consolidation/>";
        byte[] file = b.Build();
        using Workbook wb = TestBook.Open(file);
        PivotRefreshResult res = wb.RefreshPivots();
        Assert.Equal(PivotRefreshStatus.NotSupported, res.Outcomes[0].Status);
        Assert.False(wb.HasUncommittedChanges);
        Assert.Equal(PivotSourceKind.External, wb.PivotCaches.FindById(1)!.SourceKind);
    }

    [Fact]
    public void MissingSourceSheetIsReportedAndNothingChanges()
    {
        var b = One();
        b.SourceXml = "<worksheetSource ref=\"A1:E9\" sheet=\"Nope\"/>";
        using Workbook wb = TestBook.Open(b.Build());
        PivotRefreshResult res = wb.RefreshPivots();
        Assert.Equal(PivotRefreshStatus.SourceInvalid, res.Outcomes[0].Status);
        Assert.Contains("Nope", res.Outcomes[0].Message);
        Assert.False(wb.HasUncommittedChanges);
    }

    [Fact]
    public void EmptyHeaderCellIsASourceError()
    {
        var b = One();
        b.DataHeaders = ["Region", "", "Qtr", "Sales", "Units"];
        using Workbook wb = TestBook.Open(b.Build());
        PivotRefreshResult res = wb.RefreshPivots();
        Assert.Equal(PivotRefreshStatus.SourceInvalid, res.Outcomes[0].Status);
    }

    [Fact]
    public void CancelledRefreshChangesNothing()
    {
        using Workbook wb = TestBook.Open(One().Build());
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => wb.RefreshPivots(null, cts.Token));
        Assert.False(wb.HasUncommittedChanges);
    }

    [Fact]
    public void TableSourceUsesTheTablesRange()
    {
        // A defined name stands for the source range.
        var b = One();
        b.SourceXml = "<worksheetSource name=\"Sales\"/>";
        using Workbook wb = TestBook.Open(b.Build());
        wb.Names.Add("Sales", "Data!$A$1:$E$9");
        wb.RefreshPivots();
        Assert.Equal(["East|530", "West|540", "Grand Total|1070"], Grid(wb.Sheets["Pivot"], "A4:B6"));
    }

    [Fact]
    public void SecondRefreshChangesNothing()
    {
        var b = One();
        b.ColFields = [2];
        using Workbook wb = b.Refreshed(out _);
        byte[] first = TestBook.Save(wb);
        using Workbook again = TestBook.Open(first);
        again.RefreshPivots();
        byte[] second = TestBook.Save(again);
        Dictionary<string, string> h1 = TestBook.PartHashes(first);
        Dictionary<string, string> h2 = TestBook.PartHashes(second);
        foreach (string part in h1.Keys.Where(k => k.Contains("pivot", StringComparison.OrdinalIgnoreCase)))
        {
            Assert.Equal(h1[part], h2[part]);
        }

        Assert.Equal(Grid(wb.Sheets["Pivot"], "A3:D7"), Grid(again.Sheets["Pivot"], "A3:D7"));
    }

    [Fact]
    public void UntouchedPivotPartsRoundTripByteIdentical()
    {
        var b = One();
        b.ColFields = [2];
        using Workbook wb = b.Refreshed(out _);
        byte[] first = TestBook.Save(wb);
        using Workbook again = TestBook.Open(first);
        _ = again.PivotCaches.Count;
        _ = again.Sheets["Pivot"].PivotTables.Count;
        Assert.False(again.HasUncommittedChanges);
        byte[] second = TestBook.Save(again);
        Assert.Equal(TestBook.PartHashes(first), TestBook.PartHashes(second));
    }

    [Fact]
    public void RefreshingAfterASourceEditPicksItUp()
    {
        var b = One();
        using Workbook wb = b.Refreshed(out _);
        Worksheet data = wb.Sheets["Data"];
        data["D2"].SetNumber(1000);
        wb.RefreshPivots();
        Assert.Equal(["East|1430", "West|540", "Grand Total|1970"], Grid(wb.Sheets["Pivot"], "A4:B6"));
        // Values come back from the saved file too.
        using Workbook re = TestBook.Open(TestBook.Save(wb));
        Assert.Equal(["East|1430", "West|540", "Grand Total|1970"], Grid(re.Sheets["Pivot"], "A4:B6"));
    }

    [Fact]
    public void CacheSharedItemsAndRecordsAreWritten()
    {
        using Workbook wb = One().Refreshed(out _);
        byte[] file = TestBook.Save(wb);
        using OpcPackage p = OpcPackage.Open(new MemoryStream(file));
        string def = System.Text.Encoding.UTF8.GetString(p.FindPart("/xl/pivotCache/pivotCacheDefinition1.xml")!.GetBytes().Span);
        string rec = System.Text.Encoding.UTF8.GetString(p.FindPart("/xl/pivotCache/pivotCacheRecords1.xml")!.GetBytes().Span);
        Assert.Contains("recordCount=\"8\"", def);
        Assert.Contains("<sharedItems count=\"2\"><s v=\"East\"/><s v=\"West\"/></sharedItems>", def);
        Assert.Contains("containsSemiMixedTypes=\"0\" containsString=\"0\" containsNumber=\"1\" containsInteger=\"1\" minValue=\"50\" maxValue=\"300\"", def);
        Assert.Contains("r:id=\"rId1\"", def);
        Assert.StartsWith("<r><x v=\"0\"/><x v=\"0\"/><x v=\"0\"/><n v=\"100\"/><n v=\"1\"/></r>", rec[(rec.IndexOf("<r>", StringComparison.Ordinal))..]);
        Assert.Contains("count=\"8\"", rec);
        Assert.Contains(p.GetRelationships("/xl/pivotCache/pivotCacheDefinition1.xml"), r => r.Type.EndsWith("/pivotCacheRecords", StringComparison.Ordinal) && r.Target == "pivotCacheRecords1.xml");
    }

    [Fact]
    public void UnknownMarkupInTheDefinitionSurvives()
    {
        var b = One();
        b.TableXmlExtra = "<extLst><ext uri=\"{abc}\" xmlns:x14=\"http://schemas.microsoft.com/office/spreadsheetml/2009/9/main\"><x14:pivotTableDefinition hideValuesRow=\"1\"/></ext></extLst>";
        b.CacheFieldsOverride = "<cacheFields count=\"5\"><cacheField name=\"Region\" numFmtId=\"0\" caption=\"Area\" myAttr=\"keep\"><sharedItems/></cacheField>"
            + "<cacheField name=\"Product\" numFmtId=\"0\"><sharedItems/></cacheField><cacheField name=\"Qtr\" numFmtId=\"0\"><sharedItems/></cacheField><cacheField name=\"Sales\" numFmtId=\"0\"><sharedItems/></cacheField><cacheField name=\"Units\" numFmtId=\"0\"><sharedItems/></cacheField></cacheFields>";
        using Workbook wb = b.Refreshed(out _);
        string table = System.Text.Encoding.UTF8.GetString(Table(wb).Markup.ToArray());
        Assert.Contains("hideValuesRow=\"1\"", table);
        Assert.Contains("PivotStyleLight16", table);
        string cache = System.Text.Encoding.UTF8.GetString(wb.PivotCaches.FindById(1)!.Markup.ToArray());
        Assert.Contains("myAttr=\"keep\"", cache);
        Assert.Contains("caption=\"Area\"", cache);
    }

    [Fact]
    public void ShrinkingTableClearsTheOldCells()
    {
        var b = One();
        b.Location = "A3:B20";
        using Workbook wb = TestBook.Open(b.Build());
        Worksheet ws = wb.Sheets["Pivot"];
        ws["A15"].SetString("old");
        ws["B20"].SetNumber(1);
        wb.RefreshPivots();
        Assert.Equal(CellValueKind.Empty, ws["A15"].Value.Kind);
        Assert.Equal(CellValueKind.Empty, ws["B20"].Value.Kind);
    }

    [Fact]
    public void RemovedSourceColumnDropsItsFieldAndReportsIt()
    {
        var b = new PivotBook { RowFields = [0], DataFields = [PivotBook.DataField("Sum of Sales", 3)] };
        b.CacheFieldsOverride = null;
        byte[] file = b.Build();
        var ctx = new OpenContext();
        using Workbook wb = Workbook.Open(new MemoryStream(file), ctx);
        // The Product column is deleted from the source: the sheet shifts left.
        Worksheet data = wb.Sheets["Data"];
        for (int r = 1; r <= 9; r++)
        {
            for (int c = 2; c <= 4; c++)
            {
                CellValue v = data[r, c + 1].Value;
                if (v.Kind == CellValueKind.Text)
                {
                    data[r, c].SetString(data.GetText(v));
                }
                else
                {
                    data[r, c].SetNumber(v.Number);
                }
            }

            data[r, 5].Clear();
        }

        PivotCache cache = wb.PivotCaches.FindById(1)!;
        PivotCache.Child(PivotCache.Child(cache.Root, "cacheSource"), "worksheetSource").SetAttribute("ref", "A1:D9");
        PivotRefreshResult res = wb.RefreshPivots();
        Assert.True(res.AllRefreshed);
        Assert.Equal(["Region", "Qtr", "Sales", "Units"], cache.FieldNames);
        Assert.Equal(["East|530", "West|540"], Grid(wb.Sheets["Pivot"], "A4:B5"));
        Assert.Equal("2", PivotCache.Child(PivotCache.Child(Table(wb).Root, "dataFields"), "dataField").GetAttribute("fld"));
    }
}
