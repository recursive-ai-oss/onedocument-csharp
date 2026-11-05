using System;
using System.IO;
using System.Text;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Spreadsheets;

namespace RecursiveAI.OneDocument.Fuzz;

/// <summary>
/// The workbook layer over arbitrary parts. <c>sheet</c> feeds the input as a worksheet part: it is read into the store, forced
/// through the write path, written, read back and compared by cell count. <c>sst</c> feeds a shared string table and reads every
/// string; <c>wbpart</c> feeds the workbook part and walks the sheet list. Only <see cref="OfficeException"/> may escape.
/// </summary>
internal static class SheetTarget
{
    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private const string WbXml = "<workbook xmlns=\"" + Main + "\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"A\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>";
    private const string SheetXml = "<worksheet xmlns=\"" + Main + "\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c></row></sheetData></worksheet>";
    private const string SstXml = "<sst xmlns=\"" + Main + "\" count=\"1\" uniqueCount=\"1\"><si><t>x</t></si></sst>";

    // Small limits keep each execution fast; hostile input still reaches the same code paths.
    private static readonly OfficeLimits Limits = new() { MaxPartSize = 1 << 20, MaxRecordCount = 100_000, MaxTotalUncompressedSize = 8L << 20 };

    private static OpcPackage Package(byte[]? workbook, byte[]? sheet, byte[]? sst)
    {
        OpcPackage p = OpcPackage.Create();
        p.SetRelationships(null, [new Relationship("rId1", R + "officeDocument", "xl/workbook.xml")]);
        p.AddPart("/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml", workbook ?? Encoding.UTF8.GetBytes(WbXml));
        p.SetRelationships("/xl/workbook.xml", [new Relationship("rId1", R + "worksheet", "worksheets/sheet1.xml"), new Relationship("rId2", R + "sharedStrings", "sharedStrings.xml")]);
        p.AddPart("/xl/worksheets/sheet1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml", sheet ?? Encoding.UTF8.GetBytes(SheetXml));
        p.AddPart("/xl/sharedStrings.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml", sst ?? Encoding.UTF8.GetBytes(SstXml));
        return p;
    }

    public static void RunSheet(ReadOnlySpan<byte> data)
    {
        try
        {
            RunSheetCore(data);
        }
        catch (OfficeException)
        {
            // A typed rejection of the input is the correct outcome.
        }
    }

    private static void RunSheetCore(ReadOnlySpan<byte> data)
    {
        using Workbook wb = Workbook.FromPackage(Package(null, data.ToArray(), null), new OpenContext(Limits), ownsPackage: true);
        Worksheet s = wb.Sheets[0];
        long count = s.Store.CellCount;
        foreach (CellRowView row in s.Store.EnumerateRows())
        {
            for (int i = 0; i < row.Count; i++)
            {
                CellValue v = row.ValueAt(i);
                if (v.Kind == CellValueKind.Text)
                {
                    _ = s.GetText(v);
                }
            }
        }

        _ = s.MergedCells.Count + s.Columns.Count + s.UsedRange().ToString().Length;
        if (data.Length > 0 && (data[0] & 1) == 0)
        {
            s["B2"].SetString("fuzz");
        }

        s.Rewrite();
        using var ms = new MemoryStream();
        wb.SaveAs(ms, OfficeFormat.Xlsx);
        ms.Position = 0;
        using Workbook back = Workbook.Open(ms);
        long after = back.Sheets[0].Store.CellCount;
        if (after < count)
        {
            throw new InvalidOperationException("cells were lost by the write path: " + count + " -> " + after);
        }
    }

    public static void RunSst(ReadOnlySpan<byte> data)
    {
        try
        {
            RunSstCore(data);
        }
        catch (OfficeException)
        {
            // A typed rejection of the input is the correct outcome.
        }
    }

    private static void RunSstCore(ReadOnlySpan<byte> data)
    {
        using Workbook wb = Workbook.FromPackage(Package(null, null, data.ToArray()), new OpenContext(Limits), ownsPackage: true);
        SharedStringTable t = wb.SharedStrings;
        for (int i = 0; i < t.Count && i < 2000; i++)
        {
            _ = t.GetText(i);
            _ = t.IsRich(i);
        }

        t.Add("fuzz");
        wb.Sheets[0]["C3"].SetString("fuzz2");
        using var ms = new MemoryStream();
        wb.SaveAs(ms, OfficeFormat.Xlsx);
    }

    public static void RunWorkbookPart(ReadOnlySpan<byte> data)
    {
        try
        {
            RunWorkbookPartCore(data);
        }
        catch (OfficeException)
        {
            // A typed rejection of the input is the correct outcome.
        }
    }

    private static void RunWorkbookPartCore(ReadOnlySpan<byte> data)
    {
        using Workbook wb = Workbook.FromPackage(Package(data.ToArray(), null, null), new OpenContext(Limits), ownsPackage: true);
        foreach (Worksheet s in wb.Sheets)
        {
            _ = s.Name + s.State + s.Index + s.PartName;
        }

        if (wb.Sheets.Count > 1)
        {
            wb.Sheets.Move(wb.Sheets[0], wb.Sheets.Count - 1);
        }

        _ = wb.NeedsRecalculation;
        wb.MarkNeedsRecalculation();
        using var ms = new MemoryStream();
        wb.SaveAs(ms, OfficeFormat.Xlsx);
    }

    public static int WriteSeeds(string dir)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "sheet.xml"), "<?xml version=\"1.0\"?>" + SheetXml);
        File.WriteAllText(Path.Combine(dir, "sheet-rich.xml"), "<worksheet xmlns=\"" + Main + "\"><dimension ref=\"A1:C3\"/><cols><col min=\"1\" max=\"2\" width=\"9\"/></cols><sheetData><row r=\"1\" ht=\"20\" hidden=\"1\"><c r=\"A1\" t=\"str\"><f>B1&amp;1</f><v>a</v></c><c r=\"B1\" t=\"inlineStr\"><is><r><t>x</t></r></is></c><c r=\"C1\" t=\"e\"><v>#N/A</v></c></row><row r=\"3\"><c r=\"A3\"><f t=\"shared\" ref=\"A3:A4\" si=\"0\">1+1</f><v>2</v></c></row></sheetData><mergeCells count=\"1\"><mergeCell ref=\"A1:B2\"/></mergeCells><extLst><ext uri=\"x\"/></extLst></worksheet>");
        File.WriteAllText(Path.Combine(dir, "sst.xml"), SstXml);
        File.WriteAllText(Path.Combine(dir, "sst-rich.xml"), "<sst xmlns=\"" + Main + "\" uniqueCount=\"2\"><si><r><t>a</t></r><r><rPr><b/></rPr><t xml:space=\"preserve\"> b</t></r><rPh sb=\"0\" eb=\"1\"><t>p</t></rPh></si><si/></sst>");
        File.WriteAllText(Path.Combine(dir, "workbook.xml"), WbXml);
        File.WriteAllText(Path.Combine(dir, "workbook-two.xml"), "<workbook xmlns=\"" + Main + "\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><bookViews><workbookView activeTab=\"1\"/></bookViews><sheets><sheet name=\"A\" sheetId=\"1\" r:id=\"rId1\"/><sheet name=\"B\" sheetId=\"2\" state=\"hidden\" r:id=\"rId9\"/></sheets><definedNames><definedName name=\"n\" localSheetId=\"1\">A!A1</definedName></definedNames></workbook>");
        return 0;
    }
}
