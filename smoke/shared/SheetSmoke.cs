using RecursiveAI.OneOffice.Spreadsheets;
using RecursiveAI.OneOffice.Spreadsheets.Calc;

namespace RecursiveAI.OneOffice.Smoke;

/// <summary>Creates a workbook, writes it to memory, reopens it, edits a cell, saves again and reads the result back, under NativeAOT and WASM.</summary>
internal static class SheetSmoke
{
    public static List<string> Run()
    {
        var failures = new List<string>();
        using var first = new MemoryStream();
        using (Workbook wb = Workbook.Create())
        {
            Worksheet s = wb.Sheets[0];
            s["A1"].SetString("hello");
            s["B2"].SetNumber(41);
            s["C3"].Formula = "B2+1";
            s.Merge(CellRange.Parse("E1:F2"));
            wb.SaveAs(first, OfficeFormat.Xlsx);
        }

        first.Position = 0;
        using var second = new MemoryStream();
        using (Workbook wb = Workbook.Open(first))
        {
            Worksheet s = wb.Sheets["Sheet1"];
            if (s["A1"].Text != "hello") failures.Add("sheet text");
            if (s["B2"].Number != 41) failures.Add("sheet number");
            if (s["C3"].Formula != "B2+1") failures.Add("sheet formula");
            s["B2"].SetNumber(42);
            s["A2"].SetString("second");
            wb.SaveAs(second, OfficeFormat.Xlsx);
        }

        second.Position = 0;
        using Workbook back = Workbook.Open(second);
        Worksheet t = back.Sheets[0];
        if (t["B2"].Number != 42 || t["A2"].Text != "second" || t["A1"].Text != "hello") failures.Add("sheet edit");
        if (t.MergedCells.Count != 1 || t.MergedCells[0].ToString() != "E1:F2") failures.Add("sheet merge");
        FeatureChecks(failures);
        RecalcChecks(failures);
        return failures;
    }

    /// <summary>Recalculation: a name, a 3-D reference, a spill, a cycle, a parallel level and an incremental pass.</summary>
    private static void RecalcChecks(List<string> failures)
    {
        using Workbook wb = Workbook.Create();
        Worksheet s = wb.Sheets[0];
        Worksheet b = wb.Sheets.Add("Sheet2");
        s["A1"].SetNumber(2);
        s["A2"].SetNumber(3);
        b["A1"].SetNumber(10);
        wb.Names.Add("Pair", "Sheet1!$A$1:$A$2");
        s["B1"].Formula = "SUM(Pair)*2";
        s["B2"].Formula = "SUM(Sheet1:Sheet2!A1)";
        s["C1"].SetDynamicArrayFormula("SEQUENCE(3)");
        s["D1"].Formula = "SUM(C1#)";
        s["E1"].Formula = "E2+1";
        s["E2"].Formula = "E1+1";
        for (int i = 1; i <= 300; i++)
        {
            s[10 + i, 1].Formula = "A1*" + i;
        }

        RecalcResult r = wb.Recalculate();
        if (s["B1"].Number != 10) failures.Add("recalc name");
        if (s["B2"].Number != 12) failures.Add("recalc 3-D");
        if (s["C3"].Number != 3 || s["D1"].Number != 6) failures.Add("recalc spill");
        if (r.CircularCells != 2 || s["E1"].Number != 0) failures.Add("recalc cycle");
        if (s["A310"].Number != 600) failures.Add("recalc parallel");
        s["A1"].SetNumber(5);
        if (wb.Recalculate().Evaluated != 302 || s["B1"].Number != 16) failures.Add("recalc incremental");
    }

    /// <summary>Styles, formatted text, a table, a conditional format, a validation and a name, written and read back.</summary>
    private static void FeatureChecks(List<string> failures)
    {
        using var ms = new MemoryStream();
        using (Workbook wb = Workbook.Create())
        {
            Worksheet s = wb.Sheets[0];
            s["A1"].SetString("Name");
            s["B1"].SetString("Amount");
            s["A2"].SetString("x");
            s["B2"].SetNumber(1234.5);
            s["B2"].SetStyle(new CellStyleSpec { NumberFormatCode = "#,##0.00", Font = new CellFont("Arial", 10, Bold: true), Fill = CellFill.Solid(StyleColor.FromTheme(4, 0.4)) });
            s["C2"].SetDate(new DateTime(2024, 2, 29));
            s.Tables.Add("Sales", CellRange.Parse("A1:B2"));
            int dxf = wb.Styles.AddDifferentialFormat(new DifferentialFormat(new CellFont(Bold: true)));
            s.ConditionalFormats.Add(CellRange.Parse("B2:B9"), ConditionalRuleSpec.CellIs("greaterThan", "10", dxf));
            s.Validations.Add(CellRange.Parse("D2:D9"), DataValidationSpec.List("a", "b"));
            wb.Names.Add("Total", "Sheet1!$B$2");
            wb.SaveAs(ms, OfficeFormat.Xlsx);
        }

        ms.Position = 0;
        using Workbook back = Workbook.Open(ms);
        Worksheet t = back.Sheets[0];
        if (t["B2"].FormattedText != "1,234.50") failures.Add("formatted text");
        if (!t["B2"].Style.Font.Bold || back.Styles.ResolveColor(t["B2"].Style.Fill.Foreground) != unchecked((int)0xFF8FAADC)) failures.Add("resolved style");
        if (t["C2"].FormattedText != "2/29/2024") failures.Add("date text");
        if (t.Tables.Count != 1 || t.Tables["sales"]?.Columns.Count != 2) failures.Add("table");
        if (t.ConditionalFormats.Count != 1 || t.Validations.Count != 1 || back.Names.Find("Total") is null) failures.Add("sheet features");
    }
}
