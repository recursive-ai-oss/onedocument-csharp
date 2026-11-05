using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RecursiveAI.OneDocument.Spreadsheets;

namespace RecursiveAI.OneDocument.Tools.CalcSweep;

/// <summary>
/// The workbooks that test what the corpus rarely shows: spills and what blocks them, <c>A1#</c>, structured references, names holding
/// lambdas, 3-D references, circular references, implicit intersection and legacy array formulas. Each case fills the first sheet; the
/// formulas carry no cached values, so Excel calculates them when it opens the file. <c>CalcSweep gen DIR</c> writes the files,
/// <c>CalcSweep compare DIR ANSWERS</c> recalculates each with OneDocument and compares the first sheet with the cells Excel reported
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
        // ---- what-if: iteration, data tables, other workbooks, web calls ---------------------------------------------------------
        // Open behaviour these settle: how many rounds Excel makes and where it stops (iterate-*), the order cells of one cycle are
        // calculated in, which input cell takes the top row of a two-variable table, what Excel shows for a link cell its cache lacks,
        // and what WEBSERVICE gives with no network.
        ("iterate-self", wb =>
        {
            wb.SetIterativeCalculation(true, 100, 0.001);
            Worksheet s = wb.Sheets[0];
            s["A1"].Formula = "A1/2+10";
            s["B1"].Formula = "A1*2";
        }),
        ("iterate-count", wb =>
        {
            wb.SetIterativeCalculation(true, 5, 0);
            Worksheet s = wb.Sheets[0];
            s["A1"].Formula = "A1+1";
            s["A2"].Formula = "A2*2+1";
        }),
        ("iterate-pair-order", wb =>
        {
            wb.SetIterativeCalculation(true, 3, 0);
            Worksheet s = wb.Sheets[0];
            s["A1"].Formula = "B1+1";
            s["B1"].Formula = "A1+1";
            s["A3"].Formula = "B3+1";
            s["B3"].Formula = "A3+1";
            s["C3"].Formula = "A3*10+B3";
        }),
        ("datatable-column", wb =>
        {
            Worksheet s = wb.Sheets[0];
            s["A1"].SetNumber(2);
            s["B3"].Formula = "A1*10";
            s["C3"].Formula = "A1^2";
            for (int i = 0; i < 3; i++)
            {
                s[4 + i, 1].SetNumber(i + 1);
                s[4 + i, 2].SetNumber(0);
                s[4 + i, 3].SetNumber(0);
            }

            s["B4"].FormulaDetail = new CellFormula(string.Empty, CellFormulaKind.DataTable, "B4:C6", -1, "dt2D=\"0\" dtr=\"0\" r1=\"A1\"");
        }),
        ("datatable-row", wb =>
        {
            Worksheet s = wb.Sheets[0];
            s["A1"].SetNumber(2);
            s["A3"].Formula = "A1*10";
            s["A4"].Formula = "A1^2";
            for (int i = 0; i < 3; i++)
            {
                s[2, 2 + i].SetNumber(i + 1);
                s[3, 2 + i].SetNumber(0);
                s[4, 2 + i].SetNumber(0);
            }

            s["B3"].FormulaDetail = new CellFormula(string.Empty, CellFormulaKind.DataTable, "B3:D4", -1, "dt2D=\"0\" dtr=\"1\" r1=\"A1\"");
        }),
        ("datatable-2d", wb =>
        {
            Worksheet s = wb.Sheets[0];
            s["A1"].SetNumber(1);
            s["B1"].SetNumber(1);
            s["A3"].Formula = "A1+B1*100";
            for (int i = 0; i < 2; i++)
            {
                s[3, 2 + i].SetNumber(i + 1);
                s[4 + i, 1].SetNumber((i + 1) * 10);
                s[4 + i, 2].SetNumber(0);
                s[4 + i, 3].SetNumber(0);
            }

            // r1 is the row input cell (fed from the top row), r2 the column input cell (fed from the left column).
            s["B4"].FormulaDetail = new CellFormula(string.Empty, CellFormulaKind.DataTable, "B4:C5", -1, "dt2D=\"1\" dtr=\"1\" r1=\"A1\" r2=\"B1\"");
        }),
        ("external-cached", wb =>
        {
            Worksheet s = ExternalLinkCase.Add(wb);
            s["A1"].Formula = "[1]Sheet1!A1+1";
            s["A2"].Formula = "SUM([1]Sheet1!A1:A2)";
            s["A3"].Formula = "[1]Sheet1!B1";
            s["A4"].Formula = "[1]Sheet1!Z9";
            s["A5"].Formula = "[1]Sheet1!C1:C2";
            s["A6"].Formula = "SUM([1]Sheet1!A:A)";
        }),
        ("web-offline", wb =>
        {
            Worksheet s = wb.Sheets[0];
            s["A1"].Formula = "WEBSERVICE(\"http://example.invalid/x\")";
            s["A2"].Formula = "LEN(WEBSERVICE(\"http://example.invalid/x\"))";
            s["A3"].Formula = "FILTERXML(\"<a><b>1</b></a>\",\"//b\")";
            s["A4"].Formula = "ISERROR(WEBSERVICE(\"http://example.invalid/x\"))";
        }),
    ];
}

/// <summary>Adds an external-link part to a workbook: link 1 to a workbook that is never opened, with five cached cells on <c>Sheet1</c>.</summary>
internal static class ExternalLinkCase
{
    private const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static Worksheet Add(Workbook wb)
    {
        var p = wb.Package;
        const string part = "/xl/externalLinks/externalLink1.xml";
        string xml = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<externalLink xmlns=\"" + Main + "\" xmlns:r=\"" + R + "\"><externalBook r:id=\"rId1\">"
            + "<sheetNames><sheetName val=\"Sheet1\"/></sheetNames><sheetDataSet><sheetData sheetId=\"0\">"
            + "<row r=\"1\"><cell r=\"A1\"><v>5</v></cell><cell r=\"B1\" t=\"str\"><v>hi</v></cell></row><row r=\"2\"><cell r=\"A2\"><v>7</v></cell></row>"
            + "</sheetData></sheetDataSet></externalBook></externalLink>";
        p.AddPart(part, "application/vnd.openxmlformats-officedocument.spreadsheetml.externalLink+xml", System.Text.Encoding.UTF8.GetBytes(xml));
        p.SetRelationships(part, [new RecursiveAI.OneDocument.Packaging.Relationship("rId1", Rel + "externalLinkPath", "Source.xlsx", RecursiveAI.OneDocument.Packaging.RelationshipTargetMode.External)]);
        var rels = new List<RecursiveAI.OneDocument.Packaging.Relationship>(p.GetRelationships(wb.MainPartName))
        {
            new("rIdExt1", Rel + "externalLink", "externalLinks/externalLink1.xml"),
        };
        p.SetRelationships(wb.MainPartName, rels);
        foreach (var c in wb.Markup.Root.ChildElements)
        {
            if (c.LocalNameString == "sheets")
            {
                c.InsertXmlAfter(System.Text.Encoding.UTF8.GetBytes("<externalReferences xmlns:r=\"" + R + "\"><externalReference r:id=\"rIdExt1\"/></externalReferences>"));
                break;
            }
        }

        return wb.Sheets[0];
    }
}
