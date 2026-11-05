using System.Text;
using RecursiveAI.OneOffice.Calc;
using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.Spreadsheets.Calc;

namespace RecursiveAI.OneOffice.Spreadsheets.Tests;

public class CalcTests
{
    private static Worksheet NewSheet(out Workbook wb)
    {
        wb = Workbook.Create();
        return wb.Sheets[0];
    }

    private static MemoryStream SaveToMemory(Workbook wb)
    {
        var ms = new MemoryStream();
        wb.SaveAs(ms, OfficeFormat.Xlsx);
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public void FirstRecalculateComputesEverythingAndStoresCachedValues()
    {
        Worksheet s = NewSheet(out Workbook wb);
        using (wb)
        {
            s["A1"].SetNumber(2);
            s["A2"].SetNumber(3);
            s["A3"].Formula = "SUM(A1:A2)";
            s["B3"].Formula = "A3*10";
            RecalcResult r = wb.Recalculate();
            Assert.True(r.GraphBuilt);
            Assert.Equal(2, r.Evaluated);
            Assert.Equal(5, s["A3"].Number);
            Assert.Equal(50, s["B3"].Number);
            Assert.Equal(2, r.Levels);
        }
    }

    [Fact]
    public void OpeningDoesNotRecalculate()
    {
        Worksheet s = NewSheet(out Workbook wb);
        s["A1"].SetNumber(2);
        s["B1"].Formula = "A1*2";
        wb.Recalculate();
        s["A1"].SetNumber(100);
        using MemoryStream ms = SaveToMemory(wb);
        wb.Dispose();
        using Workbook again = Workbook.Open(ms);
        Assert.Equal(4, again.Sheets[0]["B1"].Number); // the stale cached value stays until asked
        again.Recalculate();
        Assert.Equal(200, again.Sheets[0]["B1"].Number);
    }

    [Fact]
    public void AnEditRecomputesOnlyItsDependents()
    {
        Worksheet s = NewSheet(out Workbook wb);
        using (wb)
        {
            // 100 independent chains of 5 formulas: B(i) = A(i)+1, C = B+1 ...
            for (int i = 1; i <= 100; i++)
            {
                s[i, 1].SetNumber(i);
                s[i, 2].Formula = "A" + i + "+1";
                for (int c = 3; c <= 6; c++)
                {
                    s[i, c].Formula = CellAddress.ColumnName(c - 1) + i + "+1";
                }
            }

            wb.Recalculate();
            Assert.Equal(1 + 5, s["F1"].Number);
            Assert.Equal(100 + 5, s["F100"].Number);

            s["A7"].SetNumber(1000);
            RecalcResult r = wb.Recalculate();
            Assert.False(r.GraphBuilt);
            Assert.Equal(5, r.Evaluated);
            Assert.Equal(1005, s["F7"].Number);
            Assert.Equal(6 + 5 - 1 + 1, s["F6"].Number); // untouched

            // Nothing changed: nothing evaluated.
            Assert.Equal(0, wb.Recalculate().Evaluated);
        }
    }

    [Fact]
    public void RangeDependentsAreFoundThroughTheRangeIndex()
    {
        Worksheet s = NewSheet(out Workbook wb);
        using (wb)
        {
            for (int i = 1; i <= 50; i++)
            {
                s[i, 1].SetNumber(1);
            }

            s["C1"].Formula = "SUM(A1:A50)";
            s["C2"].Formula = "SUM(A1:A10)";
            s["C3"].Formula = "SUM(A20:A30)";
            s["C4"].Formula = "C1+C2";
            wb.Recalculate();
            Assert.Equal(60, s["C4"].Number);
            s["A5"].SetNumber(11);
            RecalcResult r = wb.Recalculate();
            Assert.Equal(3, r.Evaluated); // C1, C2, C4
            Assert.Equal(80, s["C4"].Number);
            Assert.Equal(11, s["C3"].Number);
        }
    }

    [Fact]
    public void EditingAFormulaRebuildsTheGraphAndRecomputesJustThatCellAndItsDependents()
    {
        Worksheet s = NewSheet(out Workbook wb);
        using (wb)
        {
            s["A1"].SetNumber(1);
            s["B1"].Formula = "A1+1";
            s["C1"].Formula = "B1+1";
            s["D1"].Formula = "A1+100";
            wb.Recalculate();
            s["B1"].Formula = "A1+10";
            RecalcResult r = wb.Recalculate();
            Assert.True(r.GraphBuilt);
            Assert.Equal(2, r.Evaluated);
            Assert.Equal(12, s["C1"].Number);
        }
    }

    [Fact]
    public void ResultsDoNotDependOnTheThreadCount()
    {
        double[] Run(int threads)
        {
            Worksheet s = NewSheet(out Workbook wb);
            using (wb)
            {
                for (int i = 1; i <= 400; i++)
                {
                    s[i, 1].SetNumber(i);
                }

                for (int i = 1; i <= 400; i++)
                {
                    s[i, 2].Formula = "A" + i + "*2";
                    s[i, 3].Formula = i > 1 ? "B" + i + "+C" + (i - 1) : "B1";
                    s[i, 4].Formula = "SUM(B1:B" + i + ")-C" + i;
                }

                wb.Recalculate(new RecalcOptions { MaxDegreeOfParallelism = threads, ParallelThreshold = 8 });
                var values = new double[1200];
                for (int i = 1; i <= 400; i++)
                {
                    values[i - 1] = s[i, 2].Number;
                    values[400 + i - 1] = s[i, 3].Number;
                    values[800 + i - 1] = s[i, 4].Number;
                }

                return values;
            }
        }

        double[] one = Run(1);
        Assert.Equal(one, Run(2));
        Assert.Equal(one, Run(8));
        Assert.Equal(400 * 401, one[400 + 399]);
        Assert.All(one[800..], v => Assert.Equal(0, v));
    }

    [Fact]
    public void CircularReferencesReadAsZeroAndAreReported()
    {
        Worksheet s = NewSheet(out Workbook wb);
        using (wb)
        {
            s["A1"].Formula = "B1+1";
            s["B1"].Formula = "A1+1";
            s["C1"].Formula = "A1+5";
            s["D1"].Formula = "D1+1";
            RecalcResult r = wb.Recalculate();
            Assert.Equal(3, r.CircularCells);
            Assert.Equal(0, s["A1"].Number);
            Assert.Equal(0, s["B1"].Number);
            Assert.Equal(0, s["D1"].Number);
            Assert.Equal(5, s["C1"].Number);
            Assert.Contains("calc.circular-reference", TestBook.Defects(wb.OpenContext).Select(d => d.Code));
        }

    }

    [Fact]
    public void DynamicArrayFormulaSpillsAndAReferenceToASpilledCellFollows()
    {
        Worksheet s = NewSheet(out Workbook wb);
        using (wb)
        {
            s["A1"].SetDynamicArrayFormula("=SEQUENCE(3)");
            s["B1"].Formula = "A2*10";
            s["C1"].Formula = "SUM(A1#)";
            wb.Recalculate();
            Assert.Equal(1, s["A1"].Number);
            Assert.Equal(2, s["A2"].Number);
            Assert.Equal(3, s["A3"].Number);
            Assert.Equal("A1:A3", s["A1"].FormulaDetail!.Reference);
            Assert.Equal(20, s["B1"].Number);
            Assert.Equal(6, s["C1"].Number);
            Assert.Null(s["A2"].Formula);
        }
    }

    [Fact]
    public void ASpillResizesAndClearsCellsItNoLongerCovers()
    {
        Worksheet s = NewSheet(out Workbook wb);
        using (wb)
        {
            s["A1"].SetNumber(3);
            s["C1"].SetDynamicArrayFormula("SEQUENCE(A1)");
            s["D1"].Formula = "SUM(C1#)";
            wb.Recalculate();
            Assert.Equal(6, s["D1"].Number);
            Assert.Equal(3, s["C3"].Number);

            s["A1"].SetNumber(2);
            wb.Recalculate();
            Assert.False(s["C3"].Exists);
            Assert.Equal(3, s["D1"].Number);
            Assert.Equal("C1:C2", s["C1"].FormulaDetail!.Reference);

            s["A1"].SetNumber(5);
            wb.Recalculate();
            Assert.Equal(5, s["C5"].Number);
            Assert.Equal(15, s["D1"].Number);
        }
    }

    [Fact]
    public void ASpillBlockedByAValueShowsSpillErrorUntilTheBlockerGoes()
    {
        Worksheet s = NewSheet(out Workbook wb);
        using (wb)
        {
            s["A2"].SetNumber(99);
            s["A1"].SetDynamicArrayFormula("SEQUENCE(3)");
            RecalcResult r = wb.Recalculate();
            Assert.Equal(1, r.SpillErrors);
            Assert.Equal(CellError.Spill, s["A1"].Value.Error);
            Assert.Equal(99, s["A2"].Number);

            s["A2"].Clear();
            wb.Recalculate();
            Assert.Equal(1, s["A1"].Number);
            Assert.Equal(2, s["A2"].Number);
            Assert.Equal(3, s["A3"].Number);
        }
    }

    [Fact]
    public void MergedCellsTablesAndTheSheetEdgeBlockASpill()
    {
        Worksheet s = NewSheet(out Workbook wb);
        using (wb)
        {
            s.Merge(CellRange.Parse("B2:B3"));
            s["B1"].SetDynamicArrayFormula("SEQUENCE(3)"); // runs into the merged range
            s["F1"].SetString("h1");
            s["F2"].SetString("h2");
            s.Tables.Add("T", CellRange.Parse("F1:F2"));
            s["D1"].SetDynamicArrayFormula("SEQUENCE(1,3)"); // D1:F1 runs into the table
            s["F5"].SetDynamicArrayFormula("SEQUENCE(1,2)"); // clear of everything
            s["XFD1"].SetDynamicArrayFormula("SEQUENCE(1,2)"); // past the last column
            wb.Recalculate();
            Assert.Equal(CellError.Spill, s["B1"].Value.Error);
            Assert.Equal(CellError.Spill, s["D1"].Value.Error);
            Assert.Equal(CellError.Spill, s["XFD1"].Value.Error);
            Assert.Equal(1, s["F5"].Number);
            Assert.Equal(2, s["G5"].Number);
        }
    }

    [Fact]
    public void ASpillIsWrittenWithItsRangeAndMetadataAndReadsBack()
    {
        Worksheet s = NewSheet(out Workbook wb);
        MemoryStream ms;
        using (wb)
        {
            s["A1"].SetDynamicArrayFormula("SEQUENCE(3)");
            s["B1"].Formula = "A3+1";
            wb.Recalculate();
            ms = SaveToMemory(wb);
        }

        using (ms)
        using (var reader = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Read, leaveOpen: true))
        {
            string sheet = new StreamReader(reader.GetEntry("xl/worksheets/sheet1.xml")!.Open(), Encoding.UTF8).ReadToEnd();
            Assert.Contains("<f t=\"array\" ref=\"A1:A3\">_xlfn.SEQUENCE(3)</f>", sheet);
            Assert.Contains(" cm=\"1\"", sheet);
            Assert.NotNull(reader.GetEntry("xl/metadata.xml"));
            Assert.Contains("XLDAPR", new StreamReader(reader.GetEntry("xl/metadata.xml")!.Open()).ReadToEnd());
            ms.Position = 0;
            using Workbook again = Workbook.Open(ms);
            Worksheet a = again.Sheets[0];
            Assert.Equal(3, a["A3"].Number);
            Assert.Equal(4, a["B1"].Number);
            // The reloaded workbook treats the anchor as a dynamic array: shrinking the source shrinks the spill.
            a["A1"].SetDynamicArrayFormula("SEQUENCE(2)");
            again.Recalculate();
            Assert.False(a["A3"].Exists);
            Assert.Equal(1, a["B1"].Number); // A3 is empty now, and B1 followed the cleared cell
        }
    }

    [Fact]
    public void LegacyArrayFormulaFillsItsRangeAndDoesNotSpill()
    {
        Worksheet s = NewSheet(out Workbook wb);
        using (wb)
        {
            s["A1"].SetNumber(1);
            s["A2"].SetNumber(2);
            s["A3"].SetNumber(3);
            s["B1"].FormulaDetail = new CellFormula("A1:A3*2", CellFormulaKind.Array, "B1:B3");
            wb.Recalculate();
            Assert.Equal(2, s["B1"].Number);
            Assert.Equal(4, s["B2"].Number);
            Assert.Equal(6, s["B3"].Number);
        }
    }

    [Fact]
    public void ImplicitIntersectionAppliesToPlainFormulas()
    {
        Worksheet s = NewSheet(out Workbook wb);
        using (wb)
        {
            s["A1"].SetNumber(10);
            s["A2"].SetNumber(20);
            s["B2"].Formula = "A1:A3";
            wb.Recalculate();
            Assert.Equal(20, s["B2"].Number);
        }
    }

    [Fact]
    public void DefinedNamesBindIncludingSheetScopeAndLambdas()
    {
        Worksheet s = NewSheet(out Workbook wb);
        using (wb)
        {
            Worksheet other = wb.Sheets.Add("Other");
            s["A1"].SetNumber(4);
            s["A2"].SetNumber(6);
            wb.Names.Add("Rate", "Sheet1!$A$1");
            wb.Names.Add("Rows", "Sheet1!$A$1:$A$2");
            wb.Names.Add("Rate", "100", other); // the Other sheet's own Rate hides the workbook's
            wb.Names.Add("Twice", "LAMBDA(_xlpm.x,_xlpm.x*2)");
            wb.Names.Add("Konst", "42");
            s["B1"].Formula = "Rate*2";
            s["B2"].Formula = "SUM(Rows)";
            s["B3"].Formula = "Twice(A2)";
            s["B4"].Formula = "Konst+1";
            other["A1"].Formula = "Rate+1";
            wb.Recalculate();
            Assert.Equal(8, s["B1"].Number);
            Assert.Equal(10, s["B2"].Number);
            Assert.Equal(12, s["B3"].Number);
            Assert.Equal(43, s["B4"].Number);
            Assert.Equal(101, other["A1"].Number);

            s["A2"].SetNumber(7);
            RecalcResult r = wb.Recalculate();
            Assert.Equal(2, r.Evaluated); // SUM(Rows) and Twice(A2): the name's range is a dependency of the formula using it
            Assert.Equal(11, s["B2"].Number);
            Assert.Equal(14, s["B3"].Number);
        }
    }

    [Fact]
    public void StructuredReferencesResolveColumnsRowsHeadersAndTotals()
    {
        Worksheet s = NewSheet(out Workbook wb);
        using (wb)
        {
            s["A1"].SetString("Item");
            s["B1"].SetString("Qty");
            s["C1"].SetString("Double");
            s["A2"].SetString("x");
            s["B2"].SetNumber(3);
            s["A3"].SetString("y");
            s["B3"].SetNumber(5);
            s["A4"].SetString("z");
            s["B4"].SetNumber(7);
            s.Tables.Add("Sales", CellRange.Parse("A1:C4"));
            s["C2"].Formula = "Sales[[#This Row],[Qty]]*2";
            s["C3"].Formula = "Sales[[#This Row],[Qty]]*2";
            s["C4"].Formula = "[@Qty]*2";
            s["E1"].Formula = "SUM(Sales[Qty])";
            s["E2"].Formula = "SUM(Sales[[#Data],[Qty]:[Double]])";
            s["E3"].Formula = "INDEX(Sales[#Headers],1,2)";
            s["E4"].Formula = "ROWS(Sales[#All])";
            s["E5"].Formula = "Sales[[#This Row],[Qty]]"; // outside the table: #VALUE!
            wb.Recalculate();
            Assert.Equal(6, s["C2"].Number);
            Assert.Equal(14, s["C4"].Number);
            Assert.Equal(15, s["E1"].Number);
            Assert.Equal(15 + 30, s["E2"].Number);
            Assert.Equal("Qty", s["E3"].Text);
            Assert.Equal(4, s["E4"].Number);
            Assert.Equal(CellError.Value, s["E5"].Value.Error);

            s["B3"].SetNumber(50);
            RecalcResult r = wb.Recalculate();
            // C3, E1, E2 and E4 read B3 (E4 reads the whole table, even though ROWS only counts); E3 reads the headers only.
            Assert.Equal(4, r.Evaluated);
            Assert.Equal(60, s["E1"].Number);
        }
    }

    [Fact]
    public void ThreeDimensionalReferencesCrossSheetsInWorkbookOrder()
    {
        Worksheet s = NewSheet(out Workbook wb);
        using (wb)
        {
            Worksheet b = wb.Sheets.Add("Sheet2");
            Worksheet c = wb.Sheets.Add("Sheet3");
            wb.Sheets.Add("Sheet4")["A1"].SetNumber(1000);
            s["A1"].SetNumber(1);
            b["A1"].SetNumber(2);
            c["A1"].SetNumber(4);
            s["B1"].Formula = "SUM(Sheet1:Sheet3!A1)";
            s["B2"].Formula = "SUM(Sheet3:Sheet1!A1:A1)";
            wb.Recalculate();
            Assert.Equal(7, s["B1"].Number);
            Assert.Equal(7, s["B2"].Number);
            b["A1"].SetNumber(20);
            RecalcResult r = wb.Recalculate();
            Assert.Equal(2, r.Evaluated);
            Assert.Equal(25, s["B1"].Number);
        }
    }

    [Fact]
    public void WholeColumnReferencesAreClippedToTheUsedRange()
    {
        Worksheet s = NewSheet(out Workbook wb);
        using (wb)
        {
            for (int i = 1; i <= 10; i++)
            {
                s[i, 1].SetNumber(i);
            }

            s["B1"].Formula = "SUM(A:A)";
            s["B2"].Formula = "SUM(1:1)";
            s["B3"].Formula = "ROWS(A:A)";
            wb.Recalculate();
            Assert.Equal(55, s["B1"].Number);
            Assert.Equal(1 + 55, s["B2"].Number); // row 1 holds A1 and B1
            Assert.Equal(1048576, s["B3"].Number);
            s["A200"].SetNumber(1);
            wb.Recalculate();
            Assert.Equal(56, s["B1"].Number);
        }
    }

    [Fact]
    public void SharedFormulasFromAFileTranslateToEachCell()
    {
        const string sheet = "<?xml version=\"1.0\"?><worksheet xmlns=\"" + TestBook.Main + "\"><sheetData>"
            + "<row r=\"1\"><c r=\"A1\"><v>1</v></c><c r=\"B1\"><v>10</v></c></row>"
            + "<row r=\"2\"><c r=\"A2\"><v>2</v></c><c r=\"B2\"><f t=\"shared\" ref=\"B2:B4\" si=\"0\">A2*2+B1</f><v>0</v></c></row>"
            + "<row r=\"3\"><c r=\"A3\"><v>3</v></c><c r=\"B3\"><f t=\"shared\" si=\"0\"/><v>0</v></c></row>"
            + "<row r=\"4\"><c r=\"A4\"><v>4</v></c><c r=\"B4\"><f t=\"shared\" si=\"0\"/><v>0</v></c></row>"
            + "</sheetData></worksheet>";
        using Workbook wb = TestBook.Open(TestBook.Bytes(sheet));
        wb.Recalculate();
        Worksheet s = wb.Sheets["Data"];
        Assert.Equal(14, s["B2"].Number);
        Assert.Equal(20, s["B3"].Number);
        Assert.Equal(28, s["B4"].Number);
        s["A2"].SetNumber(100);
        RecalcResult r = wb.Recalculate();
        Assert.Equal(3, r.Evaluated); // B2 and, through B1 -> B2 -> B3 -> B4, the rest
    }

    private static FunctionRegistry RegistryWith(params FunctionDefinition[] extra)
    {
        FunctionRegistry reg = FunctionRegistry.CreateDefault();
        foreach (FunctionDefinition d in extra)
        {
            reg.Add(d);
        }

        return reg;
    }

    [Fact]
    public void AFormulaThatBuildsReferencesAtRunTimeSeesTheCellsItReadsCalculated()
    {
        // CELLAT("A9") reads a cell chosen at run time; the cell it reads is a formula further down the order.
        FunctionDefinition cellAt = FunctionDefinition.Create("CELLAT", 1, 1, [ArgKind.Value], (c, a) =>
            c.Resolver.GetCell(null, int.Parse(CalcConvert.ToText(a[0])), 1), FunctionTraits.DynamicReference);
        FunctionRegistry reg = RegistryWith(cellAt);
        Worksheet s = NewSheet(out Workbook wb);
        using (wb)
        {
            s["A1"].Formula = "CELLAT(9)+1";
            s["A9"].Formula = "A10*2";
            s["A10"].Formula = "A11+0";
            s["A11"].SetNumber(21);
            RecalcResult r = wb.Recalculate(new RecalcOptions { Functions = reg });
            Assert.Equal(43, s["A1"].Number);
            Assert.Equal(42, s["A9"].Number);
            Assert.Equal(3, r.Evaluated);

            // A dynamic formula is dirty on every recalculation.
            Assert.Equal(1, wb.Recalculate(new RecalcOptions { Functions = reg }).Evaluated);
        }
    }

    [Fact]
    public void VolatileFormulasRecalculateEveryTimeAndTheirDependentsWithThem()
    {
        int calls = 0;
        FunctionRegistry reg = RegistryWith(FunctionDefinition.Create("TICK", 0, 0, [], (_, _) => CalcValue.FromNumber(++calls), FunctionTraits.Volatile));
        Worksheet s = NewSheet(out Workbook wb);
        using (wb)
        {
            s["A1"].Formula = "TICK()";
            s["A2"].Formula = "A1*10";
            s["B1"].SetNumber(1);
            s["B2"].Formula = "B1+1";
            var o = new RecalcOptions { Functions = reg, MaxDegreeOfParallelism = 1 };
            wb.Recalculate(o);
            Assert.Equal(10, s["A2"].Number);
            RecalcResult r = wb.Recalculate(o);
            Assert.Equal(2, r.Evaluated);
            Assert.Equal(20, s["A2"].Number);
        }
    }

    [Fact]
    public void ManualCalculationStaysManualAndAnExplicitCallStillCalculates()
    {
        using Workbook wb = TestBook.Open();
        foreach (RecursiveAI.OneOffice.Markup.MarkupNode c in wb.Markup.Root.ChildElements)
        {
            if (c.LocalNameString == "calcPr")
            {
                c.SetAttribute("calcMode", "manual");
            }
        }

        Assert.True(wb.IsManualCalculation);
        Worksheet s = wb.Sheets["Data"];
        s["B1"].SetNumber(8);
        wb.Recalculate();
        Assert.Equal(16, s["A2"].Number);
        Assert.True(wb.IsManualCalculation);
        Assert.False(wb.NeedsRecalculation);
    }

    [Fact]
    public void AFormulaWithAnUnreadableTextKeepsItsStoredValueAndIsReported()
    {
        Worksheet s = NewSheet(out Workbook wb);
        using (wb)
        {
            s["A1"].Formula = "1+";
            s["A2"].Formula = "2+2";
            wb.Recalculate();
            Assert.Equal(4, s["A2"].Number);
            Assert.Contains("calc.formula-unparsable", TestBook.Defects(wb.OpenContext).Select(d => d.Code));
        }
    }

    [Fact]
    public void AFormulaThatPassesALimitShowsValueErrorAndIsReported()
    {
        using Workbook wb = Workbook.Create(new OfficeLimits { MaxAllocationBytes = 1000 });
        Worksheet s = wb.Sheets[0];
        s["A1"].Formula = "SUM(SEQUENCE(1000))";
        s["A2"].Formula = "1+1";
        wb.Recalculate();
        Assert.Equal(CellError.Value, s["A1"].Value.Error);
        Assert.Equal(2, s["A2"].Number);
        Assert.Contains("calc.limit-exceeded", TestBook.Defects(wb.OpenContext).Select(d => d.Code));
    }
}
