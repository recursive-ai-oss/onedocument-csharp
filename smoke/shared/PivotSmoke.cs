using System.Text;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Spreadsheets;
using RecursiveAI.OneDocument.Spreadsheets.Pivots;

namespace RecursiveAI.OneDocument.Smoke;

/// <summary>Refreshes a pivot table whose cache holds only field names, reads the rendered cells and a GETPIVOTDATA answer, under NativeAOT and WASM.</summary>
internal static class PivotSmoke
{
    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static List<string> Run()
    {
        var failures = new List<string>();
        OpcPackage p = OpcPackage.Create();
        p.SetRelationships(null, [new Relationship("rId1", R + "/officeDocument", "xl/workbook.xml")]);
        p.AddPart("/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml", Encoding.UTF8.GetBytes(
            "<workbook xmlns=\"" + Main + "\" xmlns:r=\"" + R + "\"><sheets><sheet name=\"Data\" sheetId=\"1\" r:id=\"rId1\"/><sheet name=\"Pivot\" sheetId=\"2\" r:id=\"rId2\"/></sheets><pivotCaches><pivotCache cacheId=\"1\" r:id=\"rId3\"/></pivotCaches></workbook>"));
        p.SetRelationships("/xl/workbook.xml",
        [
            new Relationship("rId1", R + "/worksheet", "worksheets/sheet1.xml"),
            new Relationship("rId2", R + "/worksheet", "worksheets/sheet2.xml"),
            new Relationship("rId3", R + "/pivotCacheDefinition", "pivotCache/def.xml"),
        ]);
        p.AddPart("/xl/worksheets/sheet1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml", Encoding.UTF8.GetBytes(
            "<worksheet xmlns=\"" + Main + "\"><sheetData>"
            + "<row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>k</t></is></c><c r=\"B1\" t=\"inlineStr\"><is><t>v</t></is></c></row>"
            + "<row r=\"2\"><c r=\"A2\" t=\"inlineStr\"><is><t>x</t></is></c><c r=\"B2\"><v>1</v></c></row>"
            + "<row r=\"3\"><c r=\"A3\" t=\"inlineStr\"><is><t>y</t></is></c><c r=\"B3\"><v>2</v></c></row>"
            + "<row r=\"4\"><c r=\"A4\" t=\"inlineStr\"><is><t>x</t></is></c><c r=\"B4\"><v>4</v></c></row></sheetData></worksheet>"));
        p.AddPart("/xl/worksheets/sheet2.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml", Encoding.UTF8.GetBytes("<worksheet xmlns=\"" + Main + "\"><sheetData/></worksheet>"));
        p.SetRelationships("/xl/worksheets/sheet2.xml", [new Relationship("rId1", R + "/pivotTable", "../pivotTables/pt.xml")]);
        p.AddPart("/xl/pivotTables/pt.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.pivotTable+xml", Encoding.UTF8.GetBytes(
            "<pivotTableDefinition xmlns=\"" + Main + "\" name=\"P\" cacheId=\"1\"><location ref=\"A3:B5\" firstHeaderRow=\"1\" firstDataRow=\"1\" firstDataCol=\"1\"/>"
            + "<pivotFields count=\"2\"><pivotField axis=\"axisRow\" showAll=\"0\"><items count=\"1\"><item t=\"default\"/></items></pivotField><pivotField dataField=\"1\" showAll=\"0\"/></pivotFields>"
            + "<rowFields count=\"1\"><field x=\"0\"/></rowFields><dataFields count=\"1\"><dataField name=\"Sum of v\" fld=\"1\"/></dataFields></pivotTableDefinition>"));
        p.AddPart("/xl/pivotCache/def.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.pivotCacheDefinition+xml", Encoding.UTF8.GetBytes(
            "<pivotCacheDefinition xmlns=\"" + Main + "\" xmlns:r=\"" + R + "\" recordCount=\"0\"><cacheSource type=\"worksheet\"><worksheetSource ref=\"A1:B4\" sheet=\"Data\"/></cacheSource>"
            + "<cacheFields count=\"2\"><cacheField name=\"k\" numFmtId=\"0\"><sharedItems/></cacheField><cacheField name=\"v\" numFmtId=\"0\"><sharedItems/></cacheField></cacheFields></pivotCacheDefinition>"));
        using var file = new MemoryStream();
        p.WriteTo(file);
        file.Position = 0;
        using Workbook wb = Workbook.Open(file);
        PivotRefreshResult result = wb.RefreshPivots();
        Worksheet s = wb.Sheets["Pivot"];
        if (!result.AllRefreshed) failures.Add("pivot refresh");
        if (s["A4"].Text != "x" || s["B4"].Number != 5 || s["A5"].Text != "y" || s["B6"].Number != 7 || s["A6"].Text != "Grand Total") failures.Add("pivot cells");
        s["D1"].Formula = "GETPIVOTDATA(\"v\",$A$3,\"k\",\"x\")";
        wb.Recalculate();
        if (s["D1"].Number != 5) failures.Add("pivot getpivotdata");
        return failures;
    }
}
