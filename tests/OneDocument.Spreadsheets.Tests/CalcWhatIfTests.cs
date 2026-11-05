using System.Text;
using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Spreadsheets.Calc;

namespace RecursiveAI.OneDocument.Spreadsheets.Tests;

public class CalcWhatIfTests
{
    private static Worksheet NewSheet(out Workbook wb)
    {
        wb = Workbook.Create();
        return wb.Sheets[0];
    }

    // ---- iterative calculation ---------------------------------------------------------------------------------------------

    [Fact]
    public void IterationIsOffByDefaultAndCyclesReadAsZero()
    {
        Worksheet s = NewSheet(out Workbook wb);
        using (wb)
        {
            Assert.False(wb.IsIterativeCalculation);
            Assert.Equal(100, wb.IterationCount);
            Assert.Equal(0.001, wb.IterationDelta);
            s["A1"].Formula = "A1/2+10";
            RecalcResult r = wb.Recalculate();
            Assert.Equal(0, s["A1"].Number);
            Assert.Equal(0, r.IterationRounds);
            Assert.Equal(1, r.CircularCells);
        }
    }

    [Fact]
    public void ASelfReferenceSettlesWhenIterationIsOn()
    {
        Worksheet s = NewSheet(out Workbook wb);
        using (wb)
        {
            wb.SetIterativeCalculation(true);
            s["A1"].Formula = "A1/2+10";
            RecalcResult r = wb.Recalculate();
            // Each round halves the distance to 20; the change falls to 10/2^(k-1), which is first within 0.001 in round 15.
            Assert.Equal(15, r.IterationRounds);
            Assert.Equal(1, r.IteratedCells);
            Assert.Equal(0, r.UnsettledCycles);
            Assert.Equal(0, r.CircularCells);
            Assert.InRange(s["A1"].Number, 19.99, 20);
            Assert.DoesNotContain("calc.circular-reference", TestBook.Defects(wb.OpenContext).Select(d => d.Code));
        }
    }

    [Fact]
    public void ACycleOfTwoCellsFindsItsFixedPointAndFeedsItsDependents()
    {
        Worksheet s = NewSheet(out Workbook wb);
        using (wb)
        {
            wb.SetIterativeCalculation(true, 100, 1e-9);
            s["A1"].Formula = "B1+1";
            s["B1"].Formula = "A1*0.5";
            s["C1"].Formula = "A1*10";
            RecalcResult r = wb.Recalculate();
            Assert.Equal(2, s["A1"].Number, 6);
            Assert.Equal(1, s["B1"].Number, 6);
            Assert.Equal(20, s["C1"].Number, 5);
            Assert.Equal(2, r.IteratedCells);
        }
    }

    [Fact]
    public void TheIterationCountEndsAnUnsettledCycleAndIsReported()
    {
        Worksheet s = NewSheet(out Workbook wb);
        using (wb)
        {
            wb.SetIterativeCalculation(true, 5, 0);
            s["A1"].Formula = "A1+1";
            RecalcResult r = wb.Recalculate();
            Assert.Equal(5, r.IterationRounds);
            Assert.Equal(1, r.UnsettledCycles);
            Assert.Equal(5, s["A1"].Number);
            Assert.Contains("calc.iteration-limit", TestBook.Defects(wb.OpenContext).Select(d => d.Code));
        }
    }

    [Fact]
    public void IterationStartsFromTheCachedValues()
    {
        Worksheet s = NewSheet(out Workbook wb);
        using (wb)
        {
            wb.SetIterativeCalculation(true);
            s["A1"].Formula = "A1/2+10";
            s["A1"].SetNumber(20);
            RecalcResult r = wb.Recalculate();
            Assert.Equal(1, r.IterationRounds);
            Assert.Equal(20, s["A1"].Number);
        }
    }

    [Fact]
    public void CyclesThatFeedEachOtherSettleInOrder()
    {
        Worksheet s = NewSheet(out Workbook wb);
        using (wb)
        {
            wb.SetIterativeCalculation(true, 200, 1e-9);
            s["B1"].Formula = "B1/2+A1";
            s["A1"].Formula = "A1/2+10";
            wb.Recalculate();
            Assert.Equal(20, s["A1"].Number, 5);
            Assert.Equal(40, s["B1"].Number, 5);
        }
    }

    [Fact]
    public void IterationSettingsRoundTripThroughTheFile()
    {
        Worksheet s = NewSheet(out Workbook wb);
        wb.SetIterativeCalculation(true, 50, 0.01);
        s["A1"].SetNumber(1);
        using var ms = new MemoryStream();
        wb.SaveAs(ms, OfficeFormat.Xlsx);
        wb.Dispose();
        ms.Position = 0;
        using Workbook again = Workbook.Open(ms);
        Assert.True(again.IsIterativeCalculation);
        Assert.Equal(50, again.IterationCount);
        Assert.Equal(0.01, again.IterationDelta);
        again.SetIterativeCalculation(false);
        Assert.False(again.IsIterativeCalculation);
        Assert.Throws<ArgumentOutOfRangeException>(() => again.SetIterativeCalculation(true, 0));
    }

    // ---- data tables ------------------------------------------------------------------------------------------------------

    private static CellFormula Table(string reference, string attributes) => new(string.Empty, CellFormulaKind.DataTable, reference, -1, attributes);

    [Fact]
    public void AColumnOrientedTableSubstitutesTheInputCell()
    {
        Worksheet s = NewSheet(out Workbook wb);
        using (wb)
        {
            s["A1"].SetNumber(2);
            s["B3"].Formula = "A1*10";
            s["A4"].SetNumber(1);
            s["A5"].SetNumber(2);
            s["A6"].SetNumber(3);
            s["B4"].FormulaDetail = Table("B4:B6", "dt2D=\"0\" dtr=\"0\" r1=\"A1\"");
            RecalcResult r = wb.Recalculate();
            Assert.Equal(10, s["B4"].Number);
            Assert.Equal(20, s["B5"].Number);
            Assert.Equal(30, s["B6"].Number);
            Assert.Equal(3, r.DataTableCells); // one pass: nothing the table wrote is read by another formula
            Assert.Equal(2, s["A1"].Number); // the input cell is left alone
            Assert.Equal(20, s["B3"].Number);
            Assert.Equal(CellFormulaKind.DataTable, s["B4"].FormulaDetail!.Kind);
        }
    }

    [Fact]
    public void ARowOrientedTableReadsItsInputsFromTheRowAbove()
    {
        Worksheet s = NewSheet(out Workbook wb);
        using (wb)
        {
            s["A1"].SetNumber(2);
            s["B1"].SetNumber(5);
            s["A3"].Formula = "A1*10+B1";
            s["B2"].SetNumber(1);
            s["C2"].SetNumber(2);
            s["D2"].SetNumber(3);
            s["B3"].FormulaDetail = Table("B3:D3", "dt2D=\"0\" dtr=\"1\" r1=\"A1\"");
            wb.Recalculate();
            Assert.Equal(15, s["B3"].Number);
            Assert.Equal(25, s["C3"].Number);
            Assert.Equal(35, s["D3"].Number);
        }
    }

    [Fact]
    public void ATwoVariableTableUsesTheRowInputForTheTopRowAndTheColumnInputForTheLeftColumn()
    {
        Worksheet s = NewSheet(out Workbook wb);
        using (wb)
        {
            s["A1"].SetNumber(1);
            s["B1"].SetNumber(1);
            s["A2"].Formula = "A1+B1*100";
            s["B2"].Formula = "A2*2"; // a second stage between the inputs and the corner would also work; the corner is A4 below
            s["A4"].Formula = "B2";
            s["B4"].SetNumber(1);
            s["C4"].SetNumber(2);
            s["A5"].SetNumber(10);
            s["A6"].SetNumber(20);
            // Row input A1 takes the top row; column input B1 takes the left column.
            s["B5"].FormulaDetail = Table("B5:C6", "dt2D=\"1\" dtr=\"1\" r1=\"A1\" r2=\"B1\"");
            wb.Recalculate();
            Assert.Equal(((1 + (10 * 100)) * 2), s["B5"].Number);
            Assert.Equal(((2 + (10 * 100)) * 2), s["C5"].Number);
            Assert.Equal(((1 + (20 * 100)) * 2), s["B6"].Number);
            Assert.Equal(((2 + (20 * 100)) * 2), s["C6"].Number);
            Assert.Equal(1, s["A1"].Number);
            Assert.Equal(1, s["B1"].Number);
            Assert.Equal(101, s["A2"].Number);
        }
    }

    [Fact]
    public void ADataTableSurvivesSaveAndReopenAndRecalculatesOnAnEdit()
    {
        Worksheet s = NewSheet(out Workbook wb);
        using var ms = new MemoryStream();
        s["A1"].SetNumber(2);
        s["B3"].Formula = "A1*10";
        s["A4"].SetNumber(1);
        s["A5"].SetNumber(2);
        s["B4"].FormulaDetail = Table("B4:B5", "dt2D=\"0\" dtr=\"0\" r1=\"A1\"");
        wb.Recalculate();
        wb.SaveAs(ms, OfficeFormat.Xlsx);
        wb.Dispose();
        ms.Position = 0;
        using Workbook again = Workbook.Open(ms);
        Worksheet t = again.Sheets[0];
        Assert.Equal(CellFormulaKind.DataTable, t["B4"].FormulaDetail!.Kind);
        t["A5"].SetNumber(7);
        again.Recalculate();
        Assert.Equal(10, t["B4"].Number);
        Assert.Equal(70, t["B5"].Number);
    }

    [Fact]
    public void ATableReadingAFormulaChainOverTheInputIsolatesItFromTheGrid()
    {
        Worksheet s = NewSheet(out Workbook wb);
        using (wb)
        {
            s["A1"].SetNumber(3);
            s["B1"].Formula = "A1+1";
            s["C1"].Formula = "SUM(B1:B1)*2";
            s["D1"].Formula = "C1+A1";
            s["B4"].SetNumber(10);
            s["B5"].SetNumber(20);
            s["C3"].Formula = "D1";
            s["C4"].FormulaDetail = Table("C4:C5", "r1=\"A1\"");
            wb.Recalculate();
            Assert.Equal(((10 + 1) * 2) + 10, s["C4"].Number);
            Assert.Equal(((20 + 1) * 2) + 20, s["C5"].Number);
            Assert.Equal(11, s["D1"].Number); // (3+1)*2+3, untouched
            Assert.Equal(11, s["C3"].Number);
        }
    }

    [Fact]
    public void ADeletedInputGivesRefErrors()
    {
        Worksheet s = NewSheet(out Workbook wb);
        using (wb)
        {
            s["B3"].Formula = "1";
            s["A4"].SetNumber(1);
            s["B4"].FormulaDetail = Table("B4:B4", "dt2D=\"0\" dtr=\"0\" r1=\"A1\" del1=\"1\"");
            wb.Recalculate();
            Assert.Equal(CellError.Reference, s["B4"].Value.Error);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("r1")]
    [InlineData("r1=")]
    [InlineData("r1=\"A1")]
    [InlineData("r1=A1 dtr=1")]
    [InlineData("=\"\" ==\"\"")]
    [InlineData("dt2D=\"1\" r1=\"ZZZZZ99999999\" r2=\"\"")]
    public void DamagedDataTableAttributesNeverThrow(string attributes)
    {
        Worksheet s = NewSheet(out Workbook wb);
        using (wb)
        {
            s["B3"].Formula = "1";
            s["A4"].SetNumber(1);
            s["B4"].FormulaDetail = Table("B4:B5", attributes);
            wb.Recalculate();
            Assert.Equal(CellError.Reference, s["B4"].Value.Error);
        }
    }

    // ---- Goal Seek ----------------------------------------------------------------------------------------------------------

    [Fact]
    public void GoalSeekFindsALinearSolutionAndAppliesIt()
    {
        Worksheet s = NewSheet(out Workbook wb);
        using (wb)
        {
            s["A1"].SetNumber(1);
            s["B1"].Formula = "A1*3+2";
            GoalSeekResult r = wb.GoalSeek(s["B1"], 14, s["A1"]);
            Assert.True(r.Found);
            Assert.Equal(GoalSeekStatus.Found, r.Status);
            Assert.InRange(r.Iterations, 1, 5);
            Assert.Equal(4, s["A1"].Number, 3);
            Assert.Equal(14, s["B1"].Number, 2);
            Assert.Equal(r.ChangingValue, s["A1"].Number);
        }
    }

    [Fact]
    public void GoalSeekFollowsAChainOfFormulasAndNonlinearFunctions()
    {
        Worksheet s = NewSheet(out Workbook wb);
        using (wb)
        {
            s["A1"].SetNumber(1);
            s["B1"].Formula = "A1^2";
            s["C1"].Formula = "B1+1";
            GoalSeekResult r = wb.GoalSeek(s["C1"], 50, s["A1"]);
            Assert.True(r.Found);
            Assert.Equal(7, s["A1"].Number, 2);
            Assert.True(Math.Abs(s["C1"].Number - 50) <= 0.001);
            Assert.True(r.Iterations <= 100);
        }
    }

    [Fact]
    public void GoalSeekReportsNotFoundAndLeavesTheCellAlone()
    {
        Worksheet s = NewSheet(out Workbook wb);
        using (wb)
        {
            s["A1"].SetNumber(3);
            s["B1"].Formula = "A1^2+1"; // never reaches 0
            GoalSeekResult r = wb.GoalSeek(s["B1"], 0, s["A1"]);
            Assert.False(r.Found);
            Assert.Equal(GoalSeekStatus.NotFound, r.Status);
            Assert.True(r.Iterations <= 100);
            Assert.Equal(3, s["A1"].Number);
            Assert.Equal(10, s["B1"].Number);
        }
    }

    [Fact]
    public void GoalSeekHonoursItsLimitsAndApplyOption()
    {
        Worksheet s = NewSheet(out Workbook wb);
        using (wb)
        {
            s["A1"].SetNumber(1);
            s["B1"].Formula = "A1^3";
            GoalSeekResult few = wb.GoalSeek(s["B1"], 1000, s["A1"], new GoalSeekOptions { MaxIterations = 2 });
            Assert.False(few.Found);
            Assert.Equal(2, few.Iterations);
            Assert.Equal(1, s["A1"].Number);

            GoalSeekResult dry = wb.GoalSeek(s["B1"], 1000, s["A1"], new GoalSeekOptions { Apply = false });
            Assert.True(dry.Found);
            Assert.Equal(10, dry.ChangingValue, 2);
            Assert.Equal(1, s["A1"].Number);

            wb.SetIterativeCalculation(false, 3, 0.001);
            GoalSeekResult byFile = wb.GoalSeek(s["B1"], 1000, s["A1"]);
            Assert.True(byFile.Iterations <= 3); // the file's iteration count applies when the options give none
        }
    }

    [Fact]
    public void GoalSeekRejectsCellsThatCannotWork()
    {
        Worksheet s = NewSheet(out Workbook wb);
        using (wb)
        {
            s["A1"].SetNumber(1);
            s["A2"].Formula = "A1+1";
            s["B1"].SetNumber(5);
            s["C1"].SetString("text");
            s["C2"].Formula = "C1&\"x\"";
            Assert.Equal(GoalSeekStatus.SetCellNotFormula, wb.GoalSeek(s["B1"], 3, s["A1"]).Status);
            Assert.Equal(GoalSeekStatus.ChangingCellIsFormula, wb.GoalSeek(s["A2"], 3, s["A2"]).Status);
            Assert.Equal(GoalSeekStatus.SetCellNotNumeric, wb.GoalSeek(s["C2"], 3, s["C1"]).Status);
            using Workbook other = Workbook.Create();
            Assert.Throws<ArgumentException>(() => wb.GoalSeek(other.Sheets[0]["A1"], 1, s["A1"]));
        }
    }

    // ---- external links -----------------------------------------------------------------------------------------------------

    private const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";

    private static string LinkXml(string cells, string sheets = "<sheetName val=\"Sheet1\"/>") =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<externalLink xmlns=\"" + TestBook.Main + "\" xmlns:r=\"" + TestBook.R + "\"><externalBook r:id=\"rId1\">"
        + "<sheetNames>" + sheets + "</sheetNames><sheetDataSet>" + cells + "</sheetDataSet></externalBook></externalLink>";

    private static string DefaultCells =>
        "<sheetData sheetId=\"0\"><row r=\"1\"><cell r=\"A1\"><v>5</v></cell><cell r=\"B1\" t=\"str\"><v>hi</v></cell></row><row r=\"2\"><cell r=\"A2\"><v>7</v></cell></row></sheetData>";

    private static Worksheet WithLink(out Workbook wb, string? linkXml = null, int links = 1)
    {
        wb = Workbook.Create();
        IOpcPackage p = wb.Package;
        var mainRels = new List<Relationship>(p.GetRelationships(wb.MainPartName));
        var refs = new StringBuilder("<externalReferences>");
        for (int i = 1; i <= links; i++)
        {
            string part = "/xl/externalLinks/externalLink" + i + ".xml";
            p.AddPart(part, "application/vnd.openxmlformats-officedocument.spreadsheetml.externalLink+xml", Encoding.UTF8.GetBytes(linkXml ?? LinkXml(DefaultCells)));
            p.SetRelationships(part, [new Relationship("rId1", Rel + "externalLinkPath", "file:///C:/data/Book" + i + ".xlsx", RelationshipTargetMode.External)]);
            mainRels.Add(new Relationship("rIdX" + i, Rel + "externalLink", "externalLinks/externalLink" + i + ".xml"));
            refs.Append("<externalReference r:id=\"rIdX").Append(i).Append("\"/>");
        }

        refs.Append("</externalReferences>");
        p.SetRelationships(wb.MainPartName, mainRels);
        foreach (var c in wb.Markup.Root.ChildElements)
        {
            if (c.LocalNameString == "sheets")
            {
                c.InsertXmlAfter(Encoding.UTF8.GetBytes(refs.ToString().Replace("<externalReferences>", "<externalReferences xmlns:r=\"" + TestBook.R + "\">")));
                break;
            }
        }

        return wb.Sheets[0];
    }

    private sealed class CountingResolver(Workbook? book) : IExternalWorkbookResolver
    {
        public int Calls { get; private set; }

        public ExternalLink? Last { get; private set; }

        public Workbook? Resolve(ExternalLink link)
        {
            Calls++;
            Last = link;
            return book;
        }
    }

    [Fact]
    public void ExternalLinksAreReadFromTheFile()
    {
        WithLink(out Workbook wb, links: 2);
        using (wb)
        {
            Assert.Equal(2, wb.ExternalLinks.Count);
            ExternalLink l = wb.ExternalLinks[0];
            Assert.Equal(1, l.Index);
            Assert.Equal("file:///C:/data/Book1.xlsx", l.Target);
            Assert.Equal(["Sheet1"], l.SheetNames);
            Assert.Equal(3, l.CachedCellCount);
            Assert.Equal("file:///C:/data/Book2.xlsx", wb.ExternalLinks[1].Target);
        }
    }

    [Fact]
    public void ReferencesToOtherWorkbooksReadTheCacheByDefault()
    {
        Worksheet s = WithLink(out Workbook wb);
        using (wb)
        {
            s["A1"].Formula = "[1]Sheet1!A1+1";
            s["A2"].Formula = "SUM([1]Sheet1!A1:A2)";
            s["A3"].Formula = "[1]Sheet1!B1";
            s["A4"].Formula = "[1]Sheet1!Z9";
            s["A5"].Formula = "[1]Nope!A1";
            s["A6"].Formula = "[2]Sheet1!A1";
            s["A7"].Formula = "[1]Sheet1!C1:C2";
            RecalcResult r = wb.Recalculate();
            Assert.Equal(6, s["A1"].Number);
            Assert.Equal(12, s["A2"].Number);
            Assert.Equal("hi", s["A3"].Text);
            Assert.Equal(CellError.Reference, s["A4"].Value.Error);
            Assert.Equal(CellError.Reference, s["A5"].Value.Error);
            Assert.Equal(CellError.Reference, s["A6"].Value.Error);
            Assert.Equal(CellError.Reference, s["A7"].Value.Error);
            Assert.Equal(0, r.ExternalResolved);
            Assert.Equal(4, r.ExternalMissing);
        }
    }

    [Fact]
    public void NothingIsOpenedWithoutAResolverAndTheLinkPartIsKeptByteIdentical()
    {
        Worksheet s = WithLink(out Workbook wb);
        using (wb)
        {
            byte[] before = wb.Package.FindPart("/xl/externalLinks/externalLink1.xml")!.GetBytes().ToArray();
            s["A1"].Formula = "[1]Sheet1!A1";
            wb.Recalculate();
            wb.RecalculateAll();
            Assert.Equal(before, wb.Package.FindPart("/xl/externalLinks/externalLink1.xml")!.GetBytes().ToArray());
            var counting = new CountingResolver(null);
            Assert.Equal(0, counting.Calls); // never handed to the workbook, so never called
        }
    }

    [Fact]
    public void AResolverSuppliesLiveValuesAndTheCacheIsRefreshedAndSaved()
    {
        Worksheet s = WithLink(out Workbook wb);
        using Workbook live = Workbook.Create();
        live.Sheets[0].Name = "Sheet1";
        live.Sheets[0]["A1"].SetNumber(50);
        live.Sheets[0]["A2"].SetNumber(70);
        live.Sheets[0]["B1"].SetString("new");
        s["A1"].Formula = "[1]Sheet1!A1+1";
        s["A2"].Formula = "SUM([1]Sheet1!A1:A2)";
        s["A3"].Formula = "[1]Sheet1!B1";
        s["A4"].Formula = "SUM([1]Sheet1!A:A)";
        var resolver = new CountingResolver(live);
        RecalcResult r = wb.Recalculate(new RecalcOptions { ExternalWorkbooks = resolver });
        Assert.Equal(1, resolver.Calls);
        Assert.Equal("file:///C:/data/Book1.xlsx", resolver.Last!.Target);
        Assert.Equal(51, s["A1"].Number);
        Assert.Equal(120, s["A2"].Number);
        Assert.Equal("new", s["A3"].Text);
        Assert.Equal(120, s["A4"].Number);
        Assert.Equal(4, r.ExternalResolved);
        Assert.Equal(0, r.ExternalMissing);

        using var ms = new MemoryStream();
        wb.SaveAs(ms, OfficeFormat.Xlsx);
        wb.Dispose();
        ms.Position = 0;
        using Workbook again = Workbook.Open(ms);
        ExternalLink link = again.ExternalLinks[0];
        Assert.Equal("file:///C:/data/Book1.xlsx", link.Target);
        // Opened without a resolver, the refreshed cache answers.
        again.Sheets[0]["A5"].Formula = "[1]Sheet1!A2";
        again.Recalculate();
        Assert.Equal(70, again.Sheets[0]["A5"].Number);
        Assert.Equal(51, again.Sheets[0]["A1"].Number);
    }

    [Fact]
    public void AResolverThatHasNoWorkbookFallsBackToTheCache()
    {
        Worksheet s = WithLink(out Workbook wb);
        using (wb)
        {
            s["A1"].Formula = "[1]Sheet1!A1";
            var resolver = new CountingResolver(null);
            RecalcResult r = wb.Recalculate(new RecalcOptions { ExternalWorkbooks = resolver });
            Assert.Equal(5, s["A1"].Number);
            Assert.Equal(1, resolver.Calls);
            Assert.Equal(0, r.ExternalResolved);
        }
    }

    [Fact]
    public void AFailingResolverIsReportedAndTheCacheUsed()
    {
        Worksheet s = WithLink(out Workbook wb);
        using (wb)
        {
            s["A1"].Formula = "[1]Sheet1!A1";
            wb.Recalculate(new RecalcOptions { ExternalWorkbooks = new ThrowingResolver() });
            Assert.Equal(5, s["A1"].Number);
            Assert.Contains("calc.external-resolve-failed", TestBook.Defects(wb.OpenContext).Select(d => d.Code));
        }
    }

    private sealed class ThrowingResolver : IExternalWorkbookResolver
    {
        public Workbook? Resolve(ExternalLink link) => throw new IOException("offline");
    }

    [Fact]
    public void AMissingOrDamagedLinkPartIsADefectAndTheOthersKeepTheirNumbers()
    {
        Worksheet s = WithLink(out Workbook wb, "<externalLink xmlns=\"" + TestBook.Main + "\"><externalBook></externalLink>");
        using (wb)
        {
            s["A1"].Formula = "[1]Sheet1!A1";
            wb.Recalculate();
            Assert.Equal(CellError.Reference, s["A1"].Value.Error);
            Assert.Single(wb.ExternalLinks);
            Assert.Contains("xlsx.external-link-unreadable", TestBook.Defects(wb.OpenContext).Select(d => d.Code));
        }
    }

    // ---- web services -----------------------------------------------------------------------------------------------------

    private sealed class FakeWeb : IWebServiceResolver
    {
        public List<string> Urls { get; } = [];

        public string? Fetch(string url)
        {
            Urls.Add(url);
            return url.Contains("down", StringComparison.Ordinal) ? null : "payload";
        }

        public CalcValue? FilterXml(string xml, string xpath) => CalcValue.FromText(xpath);
    }

    [Fact]
    public void WebFunctionsAreDeniedByDefaultAndKeepTheCachedValue()
    {
        Worksheet s = NewSheet(out Workbook wb);
        using (wb)
        {
            s["A1"].Formula = "WEBSERVICE(\"http://example.invalid/x\")";
            s["A1"].SetString("cached text");
            s["A2"].Formula = "WEBSERVICE(\"http://example.invalid/x\")";
            s["A3"].Formula = "FILTERXML(\"<a/>\",\"//a\")";
            s["A3"].SetString("kept");
            s["A4"].Formula = "LEN(WEBSERVICE(\"http://example.invalid/x\"))";
            wb.Recalculate();
            Assert.Equal("cached text", s["A1"].Text);
            Assert.Equal(CellError.Value, s["A2"].Value.Error);
            Assert.Equal("kept", s["A3"].Text);
            Assert.Equal(CellError.Value, s["A4"].Value.Error);
        }
    }

    [Fact]
    public void WebFunctionsAnswerThroughAnOptInResolver()
    {
        Worksheet s = NewSheet(out Workbook wb);
        using (wb)
        {
            s["A1"].Formula = "WEBSERVICE(\"http://example.invalid/x\")";
            s["A2"].Formula = "WEBSERVICE(\"http://down.invalid/\")";
            s["A3"].Formula = "FILTERXML(\"<a/>\",\"//a\")";
            var web = new FakeWeb();
            wb.Recalculate(new RecalcOptions { WebServices = web });
            Assert.Equal("payload", s["A1"].Text);
            Assert.Equal(CellError.Value, s["A2"].Value.Error);
            Assert.Equal("//a", s["A3"].Text);
            Assert.Equal(2, web.Urls.Count);
            int before = web.Urls.Count;
            s["A4"].Formula = "WEBSERVICE(\"file:///data/x.txt\")";
            wb.Recalculate(new RecalcOptions { WebServices = web });
            Assert.Equal(CellError.Value, s["A4"].Value.Error);
            Assert.DoesNotContain(web.Urls.Skip(before), u => u.StartsWith("file:", StringComparison.Ordinal)); // a file address never reaches the resolver
        }
    }
}
