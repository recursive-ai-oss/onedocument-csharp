using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Spreadsheets;
using RecursiveAI.OneDocument.Spreadsheets.Csv;

namespace RecursiveAI.OneDocument.Smoke;

/// <summary>Reads a small CSV into a workbook (found by content), reads fields and values, and writes it back, under NativeAOT and WASM.</summary>
internal static class CsvSmoke
{
    public static List<string> Run()
    {
        var failures = new List<string>();
        byte[] file = [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes("name,qty,when\r\n\"a, b\",12.5%,2020-01-15\r\nc,\"x\ny\",TRUE\r\n")];
        using (Workbook wb = Workbook.Open(new MemoryStream(file)))
        {
            Worksheet s = wb.Sheets[0];
            if (s["A2"].Text != "a, b") failures.Add("csv text");
            if (System.Math.Abs(s["B2"].Number - 0.125) > 1e-12) failures.Add("csv percent");
            if (s["C2"].Number != 43845) failures.Add("csv date");
            if (s["B3"].Text != "x\ny") failures.Add("csv newline");
            if (!s["C3"].Value.Boolean) failures.Add("csv boolean");
            var loss = new LossReport();
            using var output = new MemoryStream();
            CsvDocument.Write(wb, output, new CsvWriteOptions { ByteOrderMark = false }, loss);
            string text = Encoding.UTF8.GetString(output.ToArray());
            if (text != "name,qty,when\r\n\"a, b\",12.50%,1/15/2020\r\nc,\"x\ny\",TRUE\r\n") failures.Add("csv write: " + text);
            if (!loss.IsLossless && loss.CountOf("loss.cell-styles") == 0) failures.Add("csv loss");
        }

        using var reader = new CsvReader(new MemoryStream(file));
        int records = 0;
        while (reader.ReadRecord())
        {
            records++;
        }

        if (records != 3) failures.Add("csv records");
        return failures;
    }
}
