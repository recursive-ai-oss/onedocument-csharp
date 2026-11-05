using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Spreadsheets.Tests;

/// <summary>Builds small workbook packages and files in memory.</summary>
internal static class TestBook
{
    public const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    public const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private const string Decl = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n";

    public const string Sheet1Xml = Decl
        + "<worksheet xmlns=\"" + Main + "\" xmlns:r=\"" + R + "\" xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" xmlns:x14ac=\"http://schemas.microsoft.com/office/spreadsheetml/2009/9/ac\" mc:Ignorable=\"x14ac\">"
        + "<dimension ref=\"A1:D4\"/><sheetViews><sheetView workbookViewId=\"0\"><pane xSplit=\"1\" ySplit=\"2\" topLeftCell=\"B3\" state=\"frozen\"/></sheetView></sheetViews>"
        + "<sheetFormatPr defaultRowHeight=\"15\" x14ac:dyDescent=\"0.25\"/>"
        + "<cols><col min=\"1\" max=\"2\" width=\"12.5\" customWidth=\"1\"/><col min=\"4\" max=\"4\" width=\"9\" hidden=\"1\" style=\"2\"/></cols>"
        + "<sheetData>"
        + "<row r=\"1\" spans=\"1:4\" ht=\"20\" customHeight=\"1\" x14ac:dyDescent=\"0.25\"><c r=\"A1\" t=\"s\"><v>0</v></c><c r=\"B1\" s=\"1\"><v>1.5</v></c><c r=\"C1\" t=\"b\"><v>1</v></c><c r=\"D1\" t=\"e\"><v>#DIV/0!</v></c></row>"
        + "<row r=\"2\" hidden=\"1\" outlineLevel=\"1\"><c r=\"A2\"><f>B1*2</f><v>3</v></c><c r=\"B2\" t=\"str\"><f>A1&amp;\"x\"</f><v>helloxx &amp; more</v></c><c r=\"C2\" t=\"inlineStr\"><is><t xml:space=\"preserve\"> inline </t></is></c>"
        + "<c r=\"D2\" cm=\"1\"><f t=\"array\" ref=\"D2:D3\">SUM(B1:B1)</f><v>1.5</v></c></row>"
        + "<row r=\"4\"><c r=\"A4\"><f t=\"shared\" ref=\"A4:B4\" si=\"0\">A2+1</f><v>4</v></c><c r=\"B4\"><f t=\"shared\" si=\"0\"/><v>5</v><extLst><ext uri=\"{x}\"/></extLst></c></row>"
        + "</sheetData>"
        + "<mergeCells count=\"1\"><mergeCell ref=\"A5:B6\"/></mergeCells>"
        + "<pageMargins left=\"0.7\" right=\"0.7\" top=\"0.75\" bottom=\"0.75\" header=\"0.3\" footer=\"0.3\"/>"
        + "<extLst><ext uri=\"{abc}\" xmlns:x14=\"http://schemas.microsoft.com/office/spreadsheetml/2009/9/main\"><x14:x/></ext></extLst>"
        + "</worksheet>";

    public const string SharedXml = Decl + "<sst xmlns=\"" + Main + "\" count=\"2\" uniqueCount=\"2\"><si><t>First</t></si><si><r><t>Rich</t></r><r><rPr><b/></rPr><t> text</t></r><rPh sb=\"0\" eb=\"1\"><t>ph</t></rPh></si></sst>";

    /// <summary>A workbook file with two sheets, shared strings, styles and a calculation chain.</summary>
    public static byte[] Bytes(string? sheet1 = null, string? shared = null, byte[]? sheet1Bytes = null)
    {
        OpcPackage p = OpcPackage.Create();
        p.SetRelationships(null, [new Relationship("rId1", Rel + "officeDocument", "xl/workbook.xml")]);
        p.AddPart("/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml", Encoding.UTF8.GetBytes(Decl
            + "<workbook xmlns=\"" + Main + "\" xmlns:r=\"" + R + "\"><bookViews><workbookView activeTab=\"1\"/></bookViews><sheets><sheet name=\"Data\" sheetId=\"1\" r:id=\"rId1\"/><sheet name=\"Other\" sheetId=\"5\" state=\"hidden\" r:id=\"rId2\"/></sheets>"
            + "<definedNames><definedName name=\"Local\" localSheetId=\"1\">Other!$A$1</definedName></definedNames><calcPr calcId=\"191029\"/></workbook>"));
        p.SetRelationships("/xl/workbook.xml",
        [
            new Relationship("rId1", Rel + "worksheet", "worksheets/sheet1.xml"),
            new Relationship("rId2", Rel + "worksheet", "worksheets/sheet2.xml"),
            new Relationship("rId3", Rel + "sharedStrings", "sharedStrings.xml"),
            new Relationship("rId4", Rel + "styles", "styles.xml"),
            new Relationship("rId5", Rel + "calcChain", "calcChain.xml"),
        ]);
        p.AddPart("/xl/worksheets/sheet1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml", sheet1Bytes ?? Encoding.UTF8.GetBytes(sheet1 ?? Sheet1Xml));
        p.AddPart("/xl/worksheets/sheet2.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml", Encoding.UTF8.GetBytes(Decl
            + "<worksheet xmlns=\"" + Main + "\"><sheetData><row r=\"1\"><c r=\"A1\"><v>7</v></c></row></sheetData></worksheet>"));
        p.AddPart("/xl/sharedStrings.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml", Encoding.UTF8.GetBytes(shared ?? SharedXml));
        p.AddPart("/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml", Encoding.UTF8.GetBytes(Decl + "<styleSheet xmlns=\"" + Main + "\"/>"));
        p.AddPart("/xl/calcChain.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.calcChain+xml", Encoding.UTF8.GetBytes(Decl + "<calcChain xmlns=\"" + Main + "\"><c r=\"A2\" i=\"1\"/></calcChain>"));
        using var ms = new MemoryStream();
        p.WriteTo(ms);
        return ms.ToArray();
    }

    public static Workbook Open(byte[]? bytes = null, OpenContext? context = null) => Workbook.Open(new MemoryStream(bytes ?? Bytes()), context);

    public static byte[] Save(Workbook wb)
    {
        using var ms = new MemoryStream();
        wb.SaveAs(ms, wb.Format);
        return ms.ToArray();
    }

    /// <summary>The parts of a file by name, as (content hash).</summary>
    public static Dictionary<string, string> PartHashes(byte[] file)
    {
        using OpcPackage p = OpcPackage.Open(new MemoryStream(file));
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (IPackagePart part in p.Parts)
        {
            d[part.Name] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(part.GetBytes().Span));
        }

        return d;
    }

    public static DefectCollection Defects(OpenContext c) => (DefectCollection)c.Defects;
}
