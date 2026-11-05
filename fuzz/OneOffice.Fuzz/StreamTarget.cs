using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RecursiveAI.OneOffice.Packaging;
using RecursiveAI.OneOffice.Spreadsheets;
using RecursiveAI.OneOffice.Spreadsheets.Streaming;

namespace RecursiveAI.OneOffice.Fuzz;

/// <summary>
/// The streaming workbook cursors. <c>stream</c> feeds the input (after one mode byte) as a worksheet, shared string table or style
/// sheet and walks every row and cell through every accessor; only <see cref="OfficeException"/> may escape, and a second walk must give the same cells.
/// <c>streamwrite</c> reads the input as a script of writer calls (numbers, any text, booleans, errors, formulas, gaps, merges), finishes the
/// workbook, and checks that the cursor and the workbook model read back exactly what was written.
/// </summary>
internal static class StreamTarget
{
    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private const string WbXml = "<workbook xmlns=\"" + Main + "\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"A\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>";
    private const string SheetXml = "<worksheet xmlns=\"" + Main + "\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c><c r=\"B1\" s=\"1\"><v>43000</v></c></row></sheetData></worksheet>";
    private const string SstXml = "<sst xmlns=\"" + Main + "\" count=\"1\" uniqueCount=\"1\"><si><t>x</t></si></sst>";
    private const string StylesXml = "<styleSheet xmlns=\"" + Main + "\"><cellXfs count=\"2\"><xf numFmtId=\"0\"/><xf numFmtId=\"14\"/></cellXfs></styleSheet>";

    // Small limits keep each execution fast; hostile input still reaches the same code paths.
    private static readonly OfficeLimits Limits = new() { MaxPartSize = 1 << 20, MaxRecordCount = 100_000, MaxTotalUncompressedSize = 8L << 20 };

    private static byte[] Workbook(byte[]? sheet, byte[]? sst, byte[]? styles)
    {
        OpcPackage p = OpcPackage.Create();
        p.SetRelationships(null, [new Relationship("rId1", R + "officeDocument", "xl/workbook.xml")]);
        p.AddPart("/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml", Encoding.UTF8.GetBytes(WbXml));
        p.SetRelationships("/xl/workbook.xml",
        [
            new Relationship("rId1", R + "worksheet", "worksheets/sheet1.xml"),
            new Relationship("rId2", R + "sharedStrings", "sharedStrings.xml"),
            new Relationship("rId3", R + "styles", "styles.xml"),
        ]);
        p.AddPart("/xl/worksheets/sheet1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml", sheet ?? Encoding.UTF8.GetBytes(SheetXml));
        p.AddPart("/xl/sharedStrings.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml", sst ?? Encoding.UTF8.GetBytes(SstXml));
        p.AddPart("/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml", styles ?? Encoding.UTF8.GetBytes(StylesXml));
        using var ms = new MemoryStream();
        p.WriteTo(ms);
        return ms.ToArray();
    }

    public static void RunRead(ReadOnlySpan<byte> data)
    {
        try
        {
            RunReadCore(data);
        }
        catch (OfficeException)
        {
            // A typed rejection of the input is the correct outcome.
        }
    }

    private static void RunReadCore(ReadOnlySpan<byte> data)
    {
        int mode = data.Length > 0 ? data[0] % 3 : 0;
        byte[] body = data.Length > 0 ? data[1..].ToArray() : [];
        byte[] file = Workbook(mode == 0 ? body : null, mode == 1 ? body : null, mode == 2 ? body : null);
        using var r = XlsxStreamReader.Open(new MemoryStream(file), new OpenContext(Limits));
        long first = Walk(r);
        long second = Walk(r);
        if (first != second)
        {
            throw new InvalidOperationException("two walks of the same sheet differ");
        }
    }

    private static long Walk(XlsxStreamReader r)
    {
        long sum = 0;
        Span<char> buf = stackalloc char[128];
        using SheetCursor c = r.OpenSheet(0);
        _ = c.Columns.Count + (c.DeclaredDimension?.ToString().Length ?? 0);
        while (c.ReadRow())
        {
            sum += c.RowNumber + (long)c.RowHeight + c.RowOutlineLevel + c.RowStyle + (c.IsRowHidden ? 1 : 0);
            int n = 0;
            while (c.ReadCell())
            {
                sum += c.Column + c.Style + (long)c.Kind + (long)(c.Number * 4) + c.SharedStringIndex;
                sum += c.TextUtf8.Length + c.FormulaUtf8.Length + c.FormulaRangeUtf8.Length + c.SharedFormulaIndex;
                if (c.Kind is CellValueKind.Text or CellValueKind.Error && c.TryCopyText(buf, out int w))
                {
                    sum += w;
                }

                if (c.IsDate && c.TryGetDateTime(out DateTime d))
                {
                    sum += d.Year;
                }

                _ = c.Value;
                if (++n > 20_000)
                {
                    break;
                }
            }
        }

        return sum;
    }

    private sealed record Expected(int Row, int Col, CellValueKind Kind, double Number, string? Text, bool IsFormula, string? Formula, CellError Error);

    public static void RunWrite(ReadOnlySpan<byte> script)
    {
        byte[] data = script.ToArray();
        var expected = new List<Expected>();
        var ms = new MemoryStream();
        int i = 0;
        int Take() => i < data.Length ? data[i++] : 0;
        using (XlsxStreamWriter w = XlsxStreamWriter.Create(ms, new XlsxWriteOptions { StringStorage = (Take() & 1) == 0 ? StreamStringStorage.Shared : StreamStringStorage.Inline, MaxSharedStrings = 1 + (Take() & 7) }))
        {
            SheetStreamWriter s = w.AddSheet("S");
            int row = 0;
            int col = 1;
            int ops = 0;
            while (i < data.Length && ops++ < 400)
            {
                int op = Take() % 8;
                switch (op)
                {
                    case 0:
                        row += 1 + (Take() & 3);
                        s.BeginRow(row);
                        col = 1;
                        break;
                    case 1:
                    {
                        if (row == 0)
                        {
                            row = 1;
                        }

                        double v = BitConverter.Int64BitsToDouble(BitConverter.ToInt64(Bytes(8)));
                        s.WriteNumber(v);
                        bool finite = double.IsFinite(v);
                        expected.Add(new Expected(row, col++, finite ? CellValueKind.Number : CellValueKind.Error, finite ? v : 0, null, false, null, finite ? default : CellError.Number));
                        break;
                    }

                    case 2:
                    {
                        if (row == 0)
                        {
                            row = 1;
                        }

                        string t = Text();
                        s.WriteText(t);
                        expected.Add(new Expected(row, col++, CellValueKind.Text, 0, t, false, null, default));
                        break;
                    }

                    case 3:
                        if (row == 0)
                        {
                            row = 1;
                        }

                        bool b = (Take() & 1) != 0;
                        s.WriteBoolean(b);
                        expected.Add(new Expected(row, col++, CellValueKind.Boolean, b ? 1 : 0, null, false, null, default));
                        break;
                    case 4:
                        if (row == 0)
                        {
                            row = 1;
                        }

                        var e = (CellError)(Take() % 17);
                        s.WriteError(e);
                        expected.Add(new Expected(row, col++, CellValueKind.Error, 0, null, false, null, e));
                        break;
                    case 5:
                        if (col + 3 < 16000)
                        {
                            int gap = Take() & 3;
                            s.SkipColumns(gap);
                            col += gap;
                        }

                        break;
                    case 6:
                    {
                        if (row == 0)
                        {
                            row = 1;
                        }

                        string f = Printable();
                        s.WriteFormula(f.AsSpan());
                        string stored = f.StartsWith('=') ? f[1..] : f;
                        expected.Add(new Expected(row, col++, CellValueKind.Empty, 0, null, true, stored, default));
                        break;
                    }

                    default:
                        s.Merge(CellRange.Parse("A1:B2"));
                        break;
                }

                if (col > 16000)
                {
                    break;
                }
            }

            w.AddSheet("Second").WriteNumber(1);
        }

        byte[] file = ms.ToArray();
        using var r = XlsxStreamReader.Open(new MemoryStream(file));
        using SheetCursor c = r.OpenSheet(0);
        int k = 0;
        while (c.ReadRow())
        {
            while (c.ReadCell())
            {
                if (k >= expected.Count)
                {
                    throw new InvalidOperationException("the cursor read more cells than were written");
                }

                Expected x = expected[k++];
                bool same = c.RowNumber == x.Row && c.Column == x.Col && c.HasFormula == x.IsFormula
                    && (x.IsFormula ? c.GetFormula() == x.Formula : c.Kind == x.Kind
                        && (x.Kind is not (CellValueKind.Number or CellValueKind.Boolean) || BitConverter.DoubleToInt64Bits(c.Number) == BitConverter.DoubleToInt64Bits(x.Number) || (x.Number == 0 && c.Number == 0))
                        && (x.Kind != CellValueKind.Text || c.GetText() == (x.Text!.Length > 32767 ? x.Text[..32767] : x.Text))
                        && (x.Kind != CellValueKind.Error || c.Error == x.Error));
                if (!same)
                {
                    throw new InvalidOperationException("cell " + x.Row + "," + x.Col + " did not read back as written");
                }
            }
        }

        if (k != expected.Count)
        {
            throw new InvalidOperationException("the cursor read fewer cells than were written: " + k + " of " + expected.Count);
        }

        using Workbook wb = Spreadsheets.Workbook.Open(new MemoryStream(file));
        long cells = wb.Sheets[0].Store.CellCount;
        if (cells != expected.Count)
        {
            throw new InvalidOperationException("the model read " + cells + " cells, " + expected.Count + " were written");
        }

        return;

        ReadOnlySpan<byte> Bytes(int n)
        {
            byte[] b = new byte[n];
            for (int j = 0; j < n; j++)
            {
                b[j] = (byte)Take();
            }

            return b;
        }

        string Printable()
        {
            int len = Take() % 30;
            var chars = new char[len];
            for (int j = 0; j < len; j++)
            {
                chars[j] = (char)(32 + (Take() % 95));
            }

            return new string(chars);
        }

        string Text()
        {
            int len = Take() % 40;
            var chars = new char[len];
            for (int j = 0; j < len; j++)
            {
                int lo = Take();
                int hi = Take() % 4 == 0 ? Take() : 0; // mostly Latin-1, sometimes any char, including controls and lone surrogates
                chars[j] = (char)((hi << 8) | lo);
            }

            return new string(chars);
        }
    }

    public static int WriteSeeds(string dir)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "stream-sheet.bin"), [.. new byte[] { 0 }, .. Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?>" + SheetXml)]);
        File.WriteAllBytes(Path.Combine(dir, "stream-sheet-rich.bin"), [.. new byte[] { 0 }, .. Encoding.UTF8.GetBytes("<worksheet xmlns=\"" + Main + "\"><dimension ref=\"A1:C3\"/><cols><col min=\"1\" max=\"2\" width=\"9\"/></cols><sheetData><row r=\"1\" ht=\"20\" hidden=\"1\"><c r=\"A1\" t=\"str\"><f>B1&amp;1</f><v>a</v></c><c r=\"B1\" t=\"inlineStr\"><is><r><t>x</t></r><rPh><t>p</t></rPh></is></c><c r=\"C1\" t=\"e\"><v>#N/A</v></c><c r=\"D1\" s=\"1\"><v>1</v></c><c r=\"E1\" t=\"d\"><v>2020-01-01</v></c></row><row r=\"3\"><c r=\"A3\"><f t=\"shared\" ref=\"A3:A4\" si=\"0\">1+1</f><v>2</v></c></row></sheetData></worksheet>")]);
        File.WriteAllBytes(Path.Combine(dir, "stream-sst.bin"), [.. new byte[] { 1 }, .. Encoding.UTF8.GetBytes("<sst xmlns=\"" + Main + "\" uniqueCount=\"2\"><si><r><t>a</t></r><r><rPr><b/></rPr><t xml:space=\"preserve\"> b</t></r><rPh sb=\"0\" eb=\"1\"><t>p</t></rPh></si><si/></sst>")]);
        File.WriteAllBytes(Path.Combine(dir, "stream-styles.bin"), [.. new byte[] { 2 }, .. Encoding.UTF8.GetBytes("<styleSheet xmlns=\"" + Main + "\"><numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"yyyy\"/></numFmts><cellXfs count=\"2\"><xf numFmtId=\"164\"><alignment/></xf><xf numFmtId=\"22\"/></cellXfs></styleSheet>")]);
        File.WriteAllBytes(Path.Combine(dir, "write-script.bin"), [0, 0, 0, 1, 0, 0, 0, 0, 0, 0xF0, 0x3F, 2, 5, 65, 0, 66, 0, 0xD8, 0, 3, 1, 4, 6, 6, 3, 61, 49, 43, 49, 7]);
        return 0;
    }
}
