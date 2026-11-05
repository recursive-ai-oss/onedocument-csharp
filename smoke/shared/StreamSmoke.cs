using RecursiveAI.OneOffice.Spreadsheets;
using RecursiveAI.OneOffice.Spreadsheets.Streaming;

namespace RecursiveAI.OneOffice.Smoke;

/// <summary>Writes a small workbook with the streaming writer and reads it back with the streaming reader, under NativeAOT and WASM.</summary>
internal static class StreamSmoke
{
    public static List<string> Run()
    {
        var failures = new List<string>();
        using var file = new MemoryStream();
        using (XlsxStreamWriter w = XlsxStreamWriter.Create(file, new XlsxWriteOptions()))
        {
            int date = w.AddFormat(StreamCellFormat.Date);
            SheetStreamWriter s = w.AddSheet("Data");
            for (int r = 1; r <= 50; r++)
            {
                s.BeginRow();
                s.WriteNumber(r * 0.5);
                s.WriteText("row " + r);
                s.WriteBoolean(r % 2 == 0);
                s.WriteDate(new DateTime(2024, 1, 1).AddDays(r), date);
                s.WriteFormula("A" + r + "*2");
            }

            s.Complete();
        }

        file.Position = 0;
        using XlsxStreamReader reader = XlsxStreamReader.Open(file);
        using SheetCursor c = reader.OpenSheet("Data");
        int rows = 0;
        double sum = 0;
        while (c.ReadRow())
        {
            rows++;
            while (c.ReadCell())
            {
                if (c.Column == 1) sum += c.Number;
                if (c.Column == 2 && c.GetText() != "row " + rows) failures.Add("stream text");
                if (c.Column == 3 && c.Boolean != (rows % 2 == 0)) failures.Add("stream bool");
                if (c.Column == 4 && (!c.TryGetDateTime(out DateTime d) || d != new DateTime(2024, 1, 1).AddDays(rows))) failures.Add("stream date");
                if (c.Column == 5 && c.GetFormula() != "A" + rows + "*2") failures.Add("stream formula");
            }
        }

        if (rows != 50 || sum != 0.5 * 50 * 51 / 2) failures.Add("stream rows");
        return failures;
    }
}
