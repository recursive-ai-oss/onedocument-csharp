using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Spreadsheets;
using RecursiveAI.OneDocument.Spreadsheets.Streaming;

namespace RecursiveAI.OneDocument.Smoke;

/// <summary>Writes a workbook as a binary workbook and reads it back through the workbook model and the streaming cursor, under NativeAOT and WASM.</summary>
internal static class XlsbSmoke
{
    public static List<string> Run()
    {
        var failures = new List<string>();
        using var xlsx = new MemoryStream();
        using (XlsxStreamWriter w = XlsxStreamWriter.Create(xlsx, new XlsxWriteOptions()))
        {
            SheetStreamWriter s = w.AddSheet("Data");
            for (int r = 1; r <= 20; r++)
            {
                s.BeginRow();
                s.WriteNumber(r * 0.5);
                s.WriteText("row " + r);
                s.WriteBoolean(r % 2 == 0);
                s.WriteFormula("A" + r + "*2");
            }

            s.Complete();
        }

        xlsx.Position = 0;
        using Workbook source = Workbook.Open(xlsx);
        using var bin = new MemoryStream();
        var loss = new LossReport();
        source.SaveAs(bin, OfficeFormat.Xlsb, loss);
        if (!loss.IsLossless)
        {
            failures.Add("xlsb loss");
        }

        bin.Position = 0;
        using Workbook back = Workbook.Open(bin);
        if (!back.IsBinary || back.Sheets["Data"]["B7"].Text != "row 7" || back.Sheets["Data"]["D3"].Formula != "A3*2")
        {
            failures.Add("xlsb workbook");
        }

        bin.Position = 0;
        using XlsbStreamReader reader = XlsbStreamReader.Open(bin);
        using XlsbSheetCursor c = reader.OpenSheet("Data");
        int rows = 0;
        double sum = 0;
        while (c.ReadRow())
        {
            rows++;
            while (c.ReadCell())
            {
                if (c.Column == 1) sum += c.Number;
                if (c.Column == 2 && c.GetText() != "row " + rows) failures.Add("xlsb text");
                if (c.Column == 4 && c.GetFormula() != "A" + rows + "*2") failures.Add("xlsb formula");
            }
        }

        if (rows != 20 || sum != 0.5 * 20 * 21 / 2) failures.Add("xlsb rows");
        return failures;
    }
}
