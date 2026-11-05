using System;
using System.IO;
using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Spreadsheets;
using RecursiveAI.OneDocument.Spreadsheets.Pivots;

namespace RecursiveAI.OneDocument.Fuzz;

/// <summary>
/// Pivot tables and caches over arbitrary definitions. The input is a pivot table definition, or a cache definition and a table definition
/// separated by a zero byte; they sit in a small workbook whose source sheet holds a few records. The workbook is refreshed and written.
/// Only <see cref="OfficeException"/> (and argument errors from the edits) may escape.
/// </summary>
internal static class PivotTarget
{
    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private const string CacheSeed = "<pivotCacheDefinition xmlns=\"" + Main + "\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" recordCount=\"0\"><cacheSource type=\"worksheet\"><worksheetSource ref=\"A1:C4\" sheet=\"A\"/></cacheSource>"
        + "<cacheFields count=\"3\"><cacheField name=\"k\" numFmtId=\"0\"><sharedItems/></cacheField><cacheField name=\"g\" numFmtId=\"0\"><sharedItems/></cacheField><cacheField name=\"v\" numFmtId=\"0\"><sharedItems/></cacheField></cacheFields></pivotCacheDefinition>";
    private const string TableSeed = "<pivotTableDefinition xmlns=\"" + Main + "\" name=\"P\" cacheId=\"1\"><location ref=\"A8:C12\" firstHeaderRow=\"1\" firstDataRow=\"2\" firstDataCol=\"1\"/>"
        + "<pivotFields count=\"3\"><pivotField axis=\"axisRow\" showAll=\"0\"><items count=\"1\"><item t=\"default\"/></items></pivotField><pivotField axis=\"axisCol\" showAll=\"0\"><items count=\"1\"><item t=\"default\"/></items></pivotField><pivotField dataField=\"1\" showAll=\"0\"/></pivotFields>"
        + "<rowFields count=\"1\"><field x=\"0\"/></rowFields><colFields count=\"1\"><field x=\"1\"/></colFields><dataFields count=\"1\"><dataField name=\"Sum of v\" fld=\"2\"/></dataFields></pivotTableDefinition>";

    private static readonly OfficeLimits Limits = new() { MaxPartSize = 1 << 20, MaxRecordCount = 100_000, MaxTotalUncompressedSize = 8L << 20 };

    private static byte[] Sheet()
    {
        var sb = new StringBuilder("<worksheet xmlns=\"" + Main + "\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>k</t></is></c><c r=\"B1\" t=\"inlineStr\"><is><t>g</t></is></c><c r=\"C1\" t=\"inlineStr\"><is><t>v</t></is></c></row>");
        string[] keys = ["x", "y", "x"];
        for (int i = 0; i < 3; i++)
        {
            int r = i + 2;
            sb.Append("<row r=\"").Append(r).Append("\"><c r=\"A").Append(r).Append("\" t=\"inlineStr\"><is><t>").Append(keys[i]).Append("</t></is></c><c r=\"B").Append(r).Append("\"><v>").Append(i).Append("</v></c><c r=\"C").Append(r).Append("\"><v>").Append(i * 2.5).Append("</v></c></row>");
        }

        return Encoding.UTF8.GetBytes(sb.Append("</sheetData></worksheet>").ToString());
    }

    private static OpcPackage Package(byte[] cache, byte[] table)
    {
        OpcPackage p = OpcPackage.Create();
        p.SetRelationships(null, [new Relationship("rId1", R + "officeDocument", "xl/workbook.xml")]);
        p.AddPart("/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml", Encoding.UTF8.GetBytes(
            "<workbook xmlns=\"" + Main + "\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"A\" sheetId=\"1\" r:id=\"rId1\"/><sheet name=\"B\" sheetId=\"2\" r:id=\"rId2\"/></sheets><pivotCaches><pivotCache cacheId=\"1\" r:id=\"rId3\"/></pivotCaches></workbook>"));
        p.SetRelationships("/xl/workbook.xml", [new Relationship("rId1", R + "worksheet", "worksheets/sheet1.xml"), new Relationship("rId2", R + "worksheet", "worksheets/sheet2.xml"), new Relationship("rId3", R + "pivotCacheDefinition", "pivotCache/def.xml")]);
        p.AddPart("/xl/worksheets/sheet1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml", Sheet());
        p.AddPart("/xl/worksheets/sheet2.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml", Encoding.UTF8.GetBytes("<worksheet xmlns=\"" + Main + "\"><sheetData/></worksheet>"));
        p.SetRelationships("/xl/worksheets/sheet2.xml", [new Relationship("rId1", R + "pivotTable", "../pivotTables/pt.xml")]);
        p.AddPart("/xl/pivotTables/pt.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.pivotTable+xml", table);
        p.AddPart("/xl/pivotCache/def.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.pivotCacheDefinition+xml", cache);
        return p;
    }

    public static void Run(ReadOnlySpan<byte> data)
    {
        try
        {
            RunCore(data);
        }
        catch (OfficeException)
        {
            // A typed rejection of the input is the correct outcome.
        }
        catch (ArgumentException)
        {
            // An edit the document refuses (an unbound prefix in generated XML, say) is the documented outcome.
        }
    }

    private static void RunCore(ReadOnlySpan<byte> data)
    {
        int split = data.IndexOf((byte)0);
        byte[] cache = split >= 0 ? data[..split].ToArray() : Encoding.UTF8.GetBytes(CacheSeed);
        byte[] table = split >= 0 ? data[(split + 1)..].ToArray() : data.ToArray();
        using Workbook wb = Workbook.FromPackage(Package(cache, table), new OpenContext(Limits), ownsPackage: true);
        _ = wb.PivotCaches.Count;
        foreach (PivotTable t in wb.Sheets["B"].PivotTables)
        {
            _ = t.Name + t.CacheId + t.Location + t.RowFields.Count + t.ColumnFields.Count + t.PageFields.Count + t.DataFields.Count;
        }

        wb.RefreshPivots();
        using var ms = new MemoryStream();
        wb.SaveAs(ms, OfficeFormat.Xlsx);
    }

    public static int WriteSeeds(string dir)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "pivot-table.xml"), TableSeed);
        File.WriteAllBytes(Path.Combine(dir, "pivot-both.bin"), [.. Encoding.UTF8.GetBytes(CacheSeed), 0, .. Encoding.UTF8.GetBytes(TableSeed)]);
        return 0;
    }
}
