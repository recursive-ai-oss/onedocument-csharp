using System;
using System.IO;
using System.Text;
using RecursiveAI.OneDocument.Layout;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Rendering.Spreadsheets;
using RecursiveAI.OneDocument.Spreadsheets;

namespace RecursiveAI.OneDocument.Fuzz;

/// <summary>
/// Print rendering over an arbitrary worksheet part. The input is the worksheet; the workbook around it is fixed (one sheet, a style sheet with a few
/// formats and differential formats). Its pages are laid out and the first three painted, which exercises the page setup, break lists, headers and
/// footers, merges, conditional formats and cell text. Only <see cref="OfficeException"/> may escape.
/// </summary>
internal static class SheetRenderTarget
{
    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private const string Styles = "<styleSheet xmlns=\"" + Main + "\"><fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"16\"/><name val=\"Arial\"/></font></fonts>" +
        "<fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFFFFF00\"/></patternFill></fill></fills>" +
        "<borders count=\"2\"><border/><border><left style=\"thin\"/><right style=\"double\"/><top style=\"dashed\"/><bottom style=\"thick\"/></border></borders>" +
        "<cellXfs count=\"5\"><xf/><xf fontId=\"1\" applyFont=\"1\"/><xf fillId=\"2\" borderId=\"1\"/><xf numFmtId=\"4\"><alignment horizontal=\"center\" wrapText=\"1\" textRotation=\"45\"/></xf><xf numFmtId=\"14\"><alignment shrinkToFit=\"1\" indent=\"2\"/></xf></cellXfs>" +
        "<dxfs count=\"1\"><dxf><font><b/></font><fill><patternFill><bgColor rgb=\"FFFFC7CE\"/></patternFill></fill></dxf></dxfs></styleSheet>";

    private static readonly OfficeLimits Limits = new() { MaxPartSize = 1 << 20, MaxRecordCount = 100_000, MaxTotalUncompressedSize = 8L << 20 };

    // The default fonts are loaded once: building the library is the slow part of a run.
    private static readonly Lazy<FontLibrary> Fonts = new(FontLibrary.CreateDefault);

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 3, 9, 14, 5, 0, TimeSpan.Zero);

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    public static void Run(ReadOnlySpan<byte> data)
    {
        try
        {
            OpcPackage p = OpcPackage.Create();
            p.SetRelationships(null, [new Relationship("rId1", R + "officeDocument", "xl/workbook.xml")]);
            p.AddPart("/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml",
                Encoding.UTF8.GetBytes("<workbook xmlns=\"" + Main + "\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"A\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>"));
            p.SetRelationships("/xl/workbook.xml", [new Relationship("rId1", R + "worksheet", "worksheets/sheet1.xml"), new Relationship("rId2", R + "styles", "styles.xml")]);
            p.AddPart("/xl/worksheets/sheet1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml", data.ToArray());
            p.AddPart("/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml", Encoding.UTF8.GetBytes(Styles));
            using Workbook wb = Workbook.FromPackage(p, new OpenContext(Limits), ownsPackage: true);
            SheetLayoutDocument layout = WorkbookRenderer.Layout(wb, new SheetRenderOptions { Fonts = Fonts.Value, MaxPages = 12, Clock = new FixedClock() });
            for (int i = 0; i < Math.Min(3, layout.Pages.Count); i++)
            {
                _ = WorkbookRenderer.Paint(layout, layout.Pages[i]);
            }
        }
        catch (OfficeException)
        {
            // A typed rejection of the input is the correct outcome.
        }
    }

    // The whole workbook varies: the input is split at zero bytes into the worksheet, the style sheet and the workbook part.
    public static void RunWorkbook(ReadOnlySpan<byte> data)
    {
        System.Collections.Generic.List<byte[]> pieces = DocPartsTarget.Split(data, 3);
        try
        {
            OpcPackage p = OpcPackage.Create();
            p.SetRelationships(null, [new Relationship("rId1", R + "officeDocument", "xl/workbook.xml")]);
            p.AddPart("/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml", pieces[2]);
            p.SetRelationships("/xl/workbook.xml", [new Relationship("rId1", R + "worksheet", "worksheets/sheet1.xml"), new Relationship("rId2", R + "styles", "styles.xml")]);
            p.AddPart("/xl/worksheets/sheet1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml", pieces[0]);
            p.AddPart("/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml", pieces[1]);
            using Workbook wb = Workbook.FromPackage(p, new OpenContext(Limits), ownsPackage: true);
            SheetLayoutDocument layout = WorkbookRenderer.Layout(wb, new SheetRenderOptions { Fonts = Fonts.Value, MaxPages = 12, Clock = new FixedClock() });
            for (int i = 0; i < Math.Min(3, layout.Pages.Count); i++)
            {
                _ = WorkbookRenderer.Paint(layout, layout.Pages[i]);
            }
        }
        catch (OfficeException)
        {
        }
    }

    public static int WriteWorkbookSeeds(string dir)
    {
        Directory.CreateDirectory(dir);
        WriteSeeds(dir);
        string book = "<workbook xmlns=\"" + Main + "\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><definedNames><definedName name=\"_xlnm.Print_Area\" localSheetId=\"0\">A!$A$1:$B$4</definedName><definedName name=\"_xlnm.Print_Titles\" localSheetId=\"0\">A!$1:$1</definedName></definedNames><sheets><sheet name=\"A\" sheetId=\"1\" r:id=\"rId1\"/></sheets><calcPr/></workbook>";
        foreach (string f in Directory.GetFiles(dir, "print-*.xml"))
        {
            File.WriteAllBytes(f + ".wb", [.. File.ReadAllBytes(f), 0, .. Encoding.UTF8.GetBytes(Styles), 0, .. Encoding.UTF8.GetBytes(book)]);
            File.Delete(f);
        }

        return 0;
    }

    public static int WriteSeeds(string dir)
    {
        Directory.CreateDirectory(dir);
        string head = "<worksheet xmlns=\"" + Main + "\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">";
        File.WriteAllText(Path.Combine(dir, "print-basic.xml"), head + "<sheetPr><pageSetUpPr fitToPage=\"1\"/></sheetPr><cols><col min=\"1\" max=\"2\" width=\"20\" customWidth=\"1\"/></cols><sheetData><row r=\"1\"><c r=\"A1\" t=\"inlineStr\" s=\"1\"><is><t>Title</t></is></c><c r=\"B1\" s=\"3\"><v>1234.5678</v></c></row><row r=\"2\" ht=\"30\" customHeight=\"1\"><c r=\"A2\" s=\"2\"><v>-1</v></c><c r=\"B2\" s=\"4\"><v>45000</v></c></row></sheetData><mergeCells count=\"1\"><mergeCell ref=\"A3:B4\"/></mergeCells><printOptions headings=\"1\" gridLines=\"1\"/><pageMargins left=\"0.5\" right=\"0.5\" top=\"1\" bottom=\"1\" header=\"0.3\" footer=\"0.3\"/><pageSetup paperSize=\"9\" scale=\"80\" orientation=\"landscape\" fitToHeight=\"0\"/><headerFooter><oddHeader>&amp;L&amp;\"Arial,Bold\"&amp;12&amp;A&amp;CPage &amp;P of &amp;N&amp;R&amp;D</oddHeader><oddFooter>&amp;F</oddFooter></headerFooter><rowBreaks count=\"1\" manualBreakCount=\"1\"><brk id=\"1\" max=\"16383\" man=\"1\"/></rowBreaks></worksheet>");
        File.WriteAllText(Path.Combine(dir, "print-cf.xml"), head + "<sheetData><row r=\"1\"><c r=\"A1\"><v>1</v></c><c r=\"B1\"><v>5</v></c></row><row r=\"2\"><c r=\"A2\"><v>9</v></c><c r=\"B2\"><v>2</v></c></row></sheetData><conditionalFormatting sqref=\"A1:B2\"><cfRule type=\"cellIs\" dxfId=\"0\" priority=\"1\" operator=\"greaterThan\"><formula>4</formula></cfRule><cfRule type=\"dataBar\" priority=\"2\"><dataBar><cfvo type=\"min\"/><cfvo type=\"max\"/><color rgb=\"FF638EC6\"/></dataBar></cfRule><cfRule type=\"iconSet\" priority=\"3\"><iconSet iconSet=\"3Arrows\"><cfvo type=\"percent\" val=\"0\"/><cfvo type=\"percent\" val=\"33\"/><cfvo type=\"percent\" val=\"67\"/></iconSet></cfRule></conditionalFormatting><pageSetup scale=\"10\"/></worksheet>");
        return 0;
    }
}
