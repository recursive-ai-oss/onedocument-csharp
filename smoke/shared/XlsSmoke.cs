using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Spreadsheets;
using RecursiveAI.OneDocument.Spreadsheets.Biff;
using RecursiveAI.OneDocument.Spreadsheets.Streaming;

namespace RecursiveAI.OneDocument.Smoke;

/// <summary>Converts a tiny BIFF8 workbook (a number, a formula, a text) to a workbook and writes it as XLSX, under NativeAOT and WASM.</summary>
internal static class XlsSmoke
{
    private static byte[] Rec(int type, params byte[] data) => [(byte)type, (byte)(type >> 8), (byte)data.Length, (byte)(data.Length >> 8), .. data];

    public static List<string> Run()
    {
        var failures = new List<string>();
        byte[] bof(int dt) => Rec(0x0809, 0x00, 0x06, (byte)dt, 0x00, 0xBB, 0x0D, 0xCC, 0x07, 0, 0, 0, 0, 6, 0, 0, 0);
        byte[] eof = Rec(0x000A);
        byte[] sheet =
        [
            .. bof(0x10),
            .. Rec(0x0203, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x04, 0x40),
            .. Rec(0x0006, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x14, 0x40, 0, 0, 0, 0, 0, 0, 9, 0, 0x24, 0, 0, 0, 0xC0, 0x1E, 2, 0, 0x05),
            .. eof,
        ];
        byte[] head = [.. bof(0x05), .. Rec(0x0085, 0, 0, 0, 0, 0, 0, 1, 0, (byte)'S'), .. eof];
        int position = head.Length;
        head[24] = (byte)position;
        head[25] = (byte)(position >> 8);
        byte[] file = [.. head, .. sheet];
        using Workbook wb = XlsConverter.ToWorkbook(ByteSource.FromMemory(file));
        Worksheet s = wb.Sheets[0];
        if (s.Name != "S") failures.Add("xls sheet name " + s.Name);
        if (s["A1"].Number != 2.5) failures.Add("xls number");
        if (s["B1"].Formula != "A1*2") failures.Add("xls formula " + s["B1"].Formula);
        if (wb.ImportLoss is null) failures.Add("xls loss report");
        using var output = new MemoryStream();
        wb.SaveAs(output, OfficeFormat.Xlsx);
        if (output.Length < 500) failures.Add("xls save");

        // The same bytes through the forward cursor.
        using XlsStreamReader reader = XlsStreamReader.Open(ByteSource.FromMemory(file));
        using XlsSheetCursor c = reader.OpenSheet("S");
        if (!c.ReadRow() || !c.ReadCell() || c.Kind != CellValueKind.Number || c.Number != 2.5) failures.Add("xls cursor number");
        if (!c.ReadCell() || !c.HasFormula || c.Number != 5) failures.Add("xls cursor formula result");
        if (c.ReadCell() || c.ReadRow()) failures.Add("xls cursor end");
        return failures;
    }
}
