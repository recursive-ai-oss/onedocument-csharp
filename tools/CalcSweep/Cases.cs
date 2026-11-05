using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RecursiveAI.OneOffice.Spreadsheets;

namespace RecursiveAI.OneOffice.Tools.CalcSweep;

/// <summary>
/// The workbooks that test what the corpus rarely shows: spills and what blocks them, <c>A1#</c>, structured references, names holding
/// lambdas, 3-D references, circular references, implicit intersection and legacy array formulas. Each case fills the first sheet; the
/// formulas carry no cached values, so Excel calculates them when it opens the file. <c>CalcSweep gen DIR</c> writes the files,
/// <c>CalcSweep compare DIR ANSWERS</c> recalculates each with OneOffice and compares the first sheet with the cells Excel reported
/// (<c>NAME.cells.tsv</c> from the oracle's cells mode).
/// </summary>
internal static class Cases
{
    public static IReadOnlyList<(string Name, Action<Workbook> Build)> All { get; } =
    [
        ("spill-basic", wb =>
        {
            Worksheet s = wb.Sheets[0];
            s["A1"].SetDynamicArrayFormula("SEQUENCE(3,2)");
            s["D1"].Formula = "SUM(A1#)";
            s["D2"].Formula = "A2*10";
            s["D3"].Formula = "ROWS(A1#)";
            s["D4"].Formula = "COLUMNS(A1#)";
        }),
        ("spill-blocked-value", wb =>
        {
            Worksheet s = wb.Sheets[0];
            s["A2"].SetNumber(5);
            s["A1"].SetDynamicArrayFormula("SEQUENCE(3)");
            s["C1"].Formula = "A2";
        }),
        ("spill-blocked-merge", wb =>
        {
            Worksheet s = wb.Sheets[0];
            s.Merge(CellRange.Parse("B2:B3"));
            s["B1"].SetDynamicArrayFormula("SEQUENCE(3)");
            s["D1"].SetDynamicArrayFormula("SEQUENCE(2)");
        }),
        ("spill-blocked-table", wb =>
        {
            Worksheet s = wb.Sheets[0];
            s["C1"].SetString("h");
            s["C2"].SetNumber(1);
            s.Tables.Add("Blocker", CellRange.Parse("C1:C2"));
            s["A1"].SetDynamicArrayFormula("SEQUENCE(1,4)");
            s["A5"].SetDynamicArrayFormula("SEQUENCE(1,4)");
        }),
        ("spill-edge", wb =>
        {
            Worksheet s = wb.Sheets[0];
            s["XFD1"].SetDynamicArrayFormula("SEQUENCE(1,2)");
            s["A1048576"].SetDynamicArrayFormula("SEQUENCE(2)");
            s["A1"].Formula = "1";
        }),
        ("spill-text-and-errors", wb =>
        {
            Worksheet s = wb.Sheets[0];
            s["A1"].SetDynamicArrayFormula("MAP(SEQUENCE(4),LAMBDA(x,IF(x=3,1/0,TEXT(x,\"0.0\"))))");
            s["C1"].Formula = "A1#";
        }),
        ("names-lambda", wb =>
        {
            Worksheet s = wb.Sheets[0];
            s["A1"].SetNumber(4);
            s["A2"].SetNumber(6);
            wb.Sheets.Add("Other")["A1"].SetNumber(100);
            wb.Names.Add("Rate", "Sheet1!$A$1");
            wb.Names.Add("Rows", "Sheet1!$A$1:$A$2");
            wb.Names.Add("Rate", "Other!$A$1", wb.Sheets[1]);
            wb.Names.Add("Twice", "LAMBDA(_xlpm.x,_xlpm.x*2)");
            wb.Names.Add("Konst", "42");
            s["B1"].Formula = "Rate*2";
            s["B2"].Formula = "SUM(Rows)";
            s["B3"].Formula = "Twice(A2)";
            s["B4"].Formula = "Konst+1";
            wb.Sheets[1]["B1"].Formula = "Rate+1";
            s["B5"].Formula = "Other!B1";
        }),
        ("structured", wb =>
        {
            Worksheet s = wb.Sheets[0];
            s["A1"].SetString("Item");
            s["B1"].SetString("Qty");
            s["C1"].SetString("Double");
            string[] items = ["x", "y", "z"];
            double[] qty = [3, 5, 7];
            for (int i = 0; i < 3; i++)
            {
                s[2 + i, 1].SetString(items[i]);
                s[2 + i, 2].SetNumber(qty[i]);
            }

            s.Tables.Add("Sales", CellRange.Parse("A1:C4"));
            s["C2"].Formula = "Sales[[#This Row],[Qty]]*2";
            s["C3"].Formula = "[@Qty]*2";
            s["C4"].Formula = "Sales[[#This Row],[Qty]]*2";
            s["E1"].Formula = "SUM(Sales[Qty])";
            s["E2"].Formula = "SUM(Sales[[#Data],[Qty]:[Double]])";
            s["E3"].Formula = "INDEX(Sales[#Headers],1,2)";
            s["E4"].Formula = "ROWS(Sales[#All])";
            s["E5"].Formula = "Sales[[#This Row],[Qty]]";
            s["E6"].Formula = "ROWS(Sales[Item])";
        }),
        ("three-d", wb =>
        {
            Worksheet s = wb.Sheets[0];
            wb.Sheets.Add("Sheet2")["A1"].SetNumber(2);
            wb.Sheets.Add("Sheet3")["A1"].SetNumber(4);
            wb.Sheets.Add("Sheet4")["A1"].SetNumber(1000);
            s["A1"].SetNumber(1);
            s["B1"].Formula = "SUM(Sheet1:Sheet3!A1)";
            s["B2"].Formula = "SUM(Sheet3:Sheet1!A1:A1)";
            s["B3"].Formula = "SUM(Sheet2:Sheet4!A1)";
        }),
        ("circular", wb =>
        {
            Worksheet s = wb.Sheets[0];
            s["A1"].Formula = "B1+1";
            s["B1"].Formula = "A1+1";
            s["C1"].Formula = "A1+5";
            s["D1"].Formula = "D1+1";
            s["E1"].Formula = "D1*2";
        }),
        ("implicit-intersection", wb =>
        {
            Worksheet s = wb.Sheets[0];
            s["A1"].SetNumber(10);
            s["A2"].SetNumber(20);
            s["A3"].SetNumber(30);
            s["B2"].Formula = "A1:A3";
            s["C5"].Formula = "A1:A3";
            s["D2"].Formula = "SUM(A1:A3)";
        }),
        ("legacy-array", wb =>
        {
            Worksheet s = wb.Sheets[0];
            s["A1"].SetNumber(1);
            s["A2"].SetNumber(2);
            s["A3"].SetNumber(3);
            s["B1"].FormulaDetail = new CellFormula("A1:A3*2", CellFormulaKind.Array, "B1:B3");
            s["D1"].FormulaDetail = new CellFormula("A1:A3*2", CellFormulaKind.Array, "D1:D5");
        }),
        ("whole-column", wb =>
        {
            Worksheet s = wb.Sheets[0];
            for (int i = 1; i <= 10; i++)
            {
                s[i, 1].SetNumber(i);
            }

            s["B1"].Formula = "SUM(A:A)";
            s["B2"].Formula = "COUNT(A:A)";
            s["B3"].Formula = "ROWS(A:A)";
            s["B4"].Formula = "SUM(1:1)";
        }),
    ];
}
