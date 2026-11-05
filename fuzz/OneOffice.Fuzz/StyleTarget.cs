using System;
using System.IO;
using System.Linq;
using System.Text;
using RecursiveAI.OneOffice.Packaging;
using RecursiveAI.OneOffice.Spreadsheets;

namespace RecursiveAI.OneOffice.Fuzz;

/// <summary>
/// The workbook feature models over arbitrary parts. <c>styles</c> feeds the input as the style sheet: every cell format is resolved and
/// every number format parsed, formats are added, and the part is written and read back. <c>sheetfeat</c> feeds a worksheet part and
/// reads, adds and removes tables, conditional formats and validations. <c>table</c> feeds a table part and resizes and renames it.
/// Only <see cref="OfficeException"/> may escape.
/// </summary>
internal static class StyleTarget
{
    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private const string WbXml = "<workbook xmlns=\"" + Main + "\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"A\" sheetId=\"1\" r:id=\"rId1\"/></sheets>"
        + "<definedNames><definedName name=\"N\">A!$A$1</definedName></definedNames></workbook>";
    private const string SheetXml = "<worksheet xmlns=\"" + Main + "\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheetData><row r=\"1\"><c r=\"A1\" s=\"1\"><v>1.5</v></c><c r=\"B1\" t=\"inlineStr\"><is><t>h</t></is></c></row></sheetData></worksheet>";
    private const string StylesXml = "<styleSheet xmlns=\"" + Main + "\"><fonts count=\"1\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts><fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>"
        + "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>"
        + "<cellXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/><xf numFmtId=\"14\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/></cellXfs></styleSheet>";
    private const string TableXml = "<table xmlns=\"" + Main + "\" id=\"1\" name=\"T\" displayName=\"T\" ref=\"A1:B3\"><autoFilter ref=\"A1:B3\"/><tableColumns count=\"2\"><tableColumn id=\"1\" name=\"a\"/><tableColumn id=\"2\" name=\"b\"/></tableColumns>"
        + "<tableStyleInfo name=\"TableStyleMedium2\" showRowStripes=\"1\"/></table>";

    private static readonly OfficeLimits Limits = new() { MaxPartSize = 1 << 20, MaxRecordCount = 100_000, MaxTotalUncompressedSize = 8L << 20 };

    private static OpcPackage Package(byte[]? styles, byte[]? sheet, byte[]? table)
    {
        OpcPackage p = OpcPackage.Create();
        p.SetRelationships(null, [new Relationship("rId1", R + "officeDocument", "xl/workbook.xml")]);
        p.AddPart("/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml", Encoding.UTF8.GetBytes(WbXml));
        p.SetRelationships("/xl/workbook.xml", [new Relationship("rId1", R + "worksheet", "worksheets/sheet1.xml"), new Relationship("rId2", R + "styles", "styles.xml")]);
        p.AddPart("/xl/worksheets/sheet1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml", sheet ?? Encoding.UTF8.GetBytes(SheetXml));
        p.AddPart("/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml", styles ?? Encoding.UTF8.GetBytes(StylesXml));
        if (table is not null)
        {
            p.AddPart("/xl/tables/table1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.table+xml", table);
            p.SetRelationships("/xl/worksheets/sheet1.xml", [new Relationship("rId1", R + "table", "../tables/table1.xml")]);
        }

        return p;
    }

    public static void RunStyles(ReadOnlySpan<byte> data)
    {
        try
        {
            RunStylesCore(data);
        }
        catch (OfficeException)
        {
            // A typed rejection of the input is the correct outcome.
        }
    }

    private static void RunStylesCore(ReadOnlySpan<byte> data)
    {
        using Workbook wb = Workbook.FromPackage(Package(data.ToArray(), null, null), new OpenContext(Limits), ownsPackage: true);
        StyleSheet st = wb.Styles;
        int n = Math.Min(st.CellFormatCount, 3000);
        for (int i = 0; i < n; i++)
        {
            ResolvedCellStyle r = st.Resolve(i);
            _ = st.GetNumberFormat(i);
            _ = st.ResolveColor(r.Font.Color) + st.ResolveColor(r.Fill.Foreground) + st.ResolveColor(r.Border.Left.Color);
        }

        for (int i = 0; i < Math.Min(st.DifferentialFormatCount, 1000); i++)
        {
            _ = st.GetDifferentialFormat(i);
        }

        for (int i = 0; i < Math.Min(st.FontCount, 1000); i++) _ = st.GetFont(i);
        for (int i = 0; i < Math.Min(st.FillCount, 1000); i++) _ = st.GetFill(i);
        for (int i = 0; i < Math.Min(st.BorderCount, 1000); i++) _ = st.GetBorder(i);
        _ = st.TableStyles.Count;
        int fmt = st.GetOrAdd(new CellStyleSpec { NumberFormatCode = "0.0%", Font = new CellFont("Arial", 9, Bold: true, Color: StyleColor.FromTheme(3, -0.25)), Fill = CellFill.Solid(StyleColor.FromRgb(0x123456)) });
        Cell c = wb.Sheets[0]["C3"];
        c.SetNumber(0.5);
        c.StyleIndex = fmt;
        _ = c.FormattedText;
        _ = wb.Sheets[0]["A1"].FormattedText;
        st.AddDifferentialFormat(new DifferentialFormat(Fill: CellFill.Solid(StyleColor.FromIndexed(5))));
        using var ms = new MemoryStream();
        wb.SaveAs(ms, OfficeFormat.Xlsx);
        ms.Position = 0;
        using Workbook back = Workbook.Open(ms);
        _ = back.Styles.Resolve(fmt);
    }

    public static void RunSheetFeatures(ReadOnlySpan<byte> data)
    {
        try
        {
            RunSheetFeaturesCore(data);
        }
        catch (OfficeException)
        {
            // A typed rejection of the input is the correct outcome.
        }
    }

    private static void RunSheetFeaturesCore(ReadOnlySpan<byte> data)
    {
        using Workbook wb = Workbook.FromPackage(Package(null, data.ToArray(), null), new OpenContext(Limits), ownsPackage: true);
        Worksheet s = wb.Sheets[0];
        foreach (Table t in s.Tables)
        {
            _ = t.Range.ToString() + t.Columns.Count + t.DataRange + t.AutoFilterRange;
        }

        foreach (ConditionalFormat f in s.ConditionalFormats)
        {
            _ = f.Ranges.Count;
            foreach (ConditionalFormatRule r in f.Rules)
            {
                _ = r.Formulas.Count + r.Priority + (r.ColorScale?.Points.Count ?? 0) + (r.DataBar?.Points.Count ?? 0) + (r.IconSet?.Points.Count ?? 0);
            }
        }

        foreach (DataValidation v in s.Validations)
        {
            _ = v.Formula1 + v.Formula2 + v.Ranges.Count + v.Type;
        }

        s.Validations.Add(CellRange.Parse("D1:D4"), DataValidationSpec.List("a", "b"));
        s.ConditionalFormats.Add(CellRange.Parse("E1:E4"), ConditionalRuleSpec.DataBars(StyleColor.FromRgb(0x336699)));
        if (data.Length > 0 && (data[0] & 1) == 0)
        {
            Table t = s.Tables.Add("Fuzzed", CellRange.Parse("H1:J4"));
            t.Resize(CellRange.Parse("H1:K6"));
        }

        wb.Names.Add("Fz", "A!$A$1", s);
        using var ms = new MemoryStream();
        wb.SaveAs(ms, OfficeFormat.Xlsx);
        ms.Position = 0;
        using Workbook back = Workbook.Open(ms);
        _ = back.Sheets[0].Validations.Count + back.Sheets[0].ConditionalFormats.Count + back.Sheets[0].Tables.Count;
    }

    public static void RunTable(ReadOnlySpan<byte> data)
    {
        try
        {
            RunTableCore(data);
        }
        catch (OfficeException)
        {
            // A typed rejection of the input is the correct outcome.
        }
        catch (ArgumentException)
        {
            // The edit is refused for this table (overlap, name taken); that is the documented outcome.
        }
    }

    private static void RunTableCore(ReadOnlySpan<byte> data)
    {
        string sheet = SheetXml.Replace("</worksheet>", "<tableParts count=\"1\"><tablePart r:id=\"rId1\"/></tableParts></worksheet>", StringComparison.Ordinal);
        using Workbook wb = Workbook.FromPackage(Package(null, Encoding.UTF8.GetBytes(sheet), data.ToArray()), new OpenContext(Limits), ownsPackage: true);
        Worksheet s = wb.Sheets[0];
        Table? t = s.Tables.FirstOrDefault();
        if (t is null)
        {
            return;
        }

        _ = t.Name + t.DisplayName + t.StyleName + t.Id + t.Columns.Count + t.HeaderRowCount + t.TotalsRowCount + t.DataRange;
        t.SetStyle("TableStyleLight9");
        t.Resize(CellRange.Parse("A1:D5"));
        t.Rename("Renamed");
        using var ms = new MemoryStream();
        wb.SaveAs(ms, OfficeFormat.Xlsx);
    }

    public static int WriteSeeds(string dir)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "styles.xml"), "<?xml version=\"1.0\"?>" + StylesXml);
        File.WriteAllText(Path.Combine(dir, "styles-rich.xml"), "<styleSheet xmlns=\"" + Main + "\"><numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"0.000\"/></numFmts><fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><i/><u val=\"double\"/><vertAlign val=\"superscript\"/><sz val=\"9\"/><color theme=\"4\" tint=\"-0.25\"/><name val=\"Arial\"/></font></fonts>"
            + "<fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill><fill><gradientFill type=\"path\" left=\"0.5\"><stop position=\"0\"><color indexed=\"3\"/></stop><stop position=\"1\"><color rgb=\"FF00FF00\"/></stop></gradientFill></fill></fills>"
            + "<borders count=\"2\"><border><left/><right/><top/><bottom/><diagonal/></border><border diagonalUp=\"1\"><left style=\"thin\"><color auto=\"1\"/></left><right style=\"hair\"/><top/><bottom/><diagonal style=\"dashed\"><color rgb=\"FFFF0000\"/></diagonal></border></borders>"
            + "<cellStyleXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/><xf numFmtId=\"9\" fontId=\"1\" fillId=\"2\" borderId=\"1\" applyFont=\"1\"><alignment horizontal=\"center\"/></xf></cellStyleXfs>"
            + "<cellXfs count=\"3\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/><xf numFmtId=\"164\" fontId=\"1\" fillId=\"2\" borderId=\"1\" xfId=\"1\" applyFont=\"0\" applyAlignment=\"0\"><alignment wrapText=\"1\" textRotation=\"90\"/><protection locked=\"0\"/></xf><xf numFmtId=\"999\" fontId=\"9\" fillId=\"9\" borderId=\"9\" xfId=\"9\"/></cellXfs>"
            + "<cellStyles count=\"2\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/><cellStyle name=\"Fancy\" xfId=\"1\"/></cellStyles><dxfs count=\"1\"><dxf><font><b/></font><numFmt numFmtId=\"164\" formatCode=\"0.000\"/><fill><patternFill><bgColor rgb=\"FFFFC7CE\"/></patternFill></fill></dxf></dxfs>"
            + "<tableStyles count=\"1\" defaultTableStyle=\"TableStyleMedium2\"><tableStyle name=\"M\" pivot=\"0\"><tableStyleElement type=\"headerRow\" dxfId=\"0\"/></tableStyle></tableStyles><colors><indexedColors><rgbColor rgb=\"FF010203\"/></indexedColors></colors></styleSheet>");
        File.WriteAllText(Path.Combine(dir, "sheetfeat.xml"), "<worksheet xmlns=\"" + Main + "\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" xmlns:x14=\"http://schemas.microsoft.com/office/spreadsheetml/2009/9/main\" xmlns:xm=\"http://schemas.microsoft.com/office/excel/2006/main\"><sheetData/>"
            + "<conditionalFormatting sqref=\"A1:A3 C1\"><cfRule type=\"colorScale\" priority=\"1\"><colorScale><cfvo type=\"min\"/><cfvo type=\"max\"/><color rgb=\"FFFF0000\"/><color theme=\"4\"/></colorScale></cfRule><cfRule type=\"cellIs\" dxfId=\"0\" priority=\"2\" operator=\"between\"><formula>1</formula><formula>5</formula></cfRule></conditionalFormatting>"
            + "<dataValidations count=\"1\"><dataValidation type=\"list\" allowBlank=\"1\" sqref=\"B1:B9\"><formula1>\"a,b\"</formula1></dataValidation></dataValidations><tableParts count=\"1\"><tablePart r:id=\"rId1\"/></tableParts>"
            + "<extLst><ext uri=\"{78C0D931-6437-4d4b-8E6A-AA1B2B8B1F36}\"><x14:conditionalFormattings><x14:conditionalFormatting><x14:cfRule type=\"iconSet\" priority=\"3\" id=\"{1}\"><x14:iconSet iconSet=\"3Arrows\"><x14:cfvo type=\"percent\"><xm:f>0</xm:f></x14:cfvo><x14:cfvo type=\"percent\"><xm:f>33</xm:f></x14:cfvo></x14:iconSet></x14:cfRule><xm:sqref>F1:F4</xm:sqref></x14:conditionalFormatting></x14:conditionalFormattings></ext></extLst></worksheet>");
        File.WriteAllText(Path.Combine(dir, "table.xml"), TableXml);
        File.WriteAllText(Path.Combine(dir, "table-totals.xml"), "<table xmlns=\"" + Main + "\" id=\"2\" name=\"T2\" displayName=\"T2\" ref=\"A1:B4\" totalsRowCount=\"1\"><autoFilter ref=\"A1:B3\"/><tableColumns count=\"2\"><tableColumn id=\"1\" name=\"a\" totalsRowLabel=\"Total\"/><tableColumn id=\"2\" name=\"b\" totalsRowFunction=\"sum\"><calculatedColumnFormula>[@a]*2</calculatedColumnFormula></tableColumn></tableColumns></table>");
        return 0;
    }
}
