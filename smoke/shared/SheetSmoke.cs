using RecursiveAI.OneOffice.Spreadsheets;

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
        return failures;
    }
}
