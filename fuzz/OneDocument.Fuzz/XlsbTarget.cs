using System;
using System.Collections.Generic;
using System.IO;
using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Spreadsheets;
using RecursiveAI.OneDocument.Spreadsheets.Streaming;
using RecursiveAI.OneDocument.Xlsb;

namespace RecursiveAI.OneDocument.Fuzz;

/// <summary>
/// The binary workbook layer. The first input byte picks what the rest of the input replaces in a valid generated workbook: the worksheet,
/// the style sheet, the shared string table or the workbook part (each opened through the hub, every cell read, the whole book saved as
/// SpreadsheetML), a bare record stream (the record reader), or a formula token string (decode, then encode the tree again).
/// Only <see cref="OfficeException"/> may escape.
/// </summary>
internal static class XlsbTarget
{
    private static readonly OfficeLimits Limits = new() { MaxPartSize = 1 << 20, MaxRecordCount = 100_000, MaxTotalUncompressedSize = 8L << 20 };
    private static byte[]? s_base;

    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static byte[] Base()
    {
        if (s_base is not null)
        {
            return s_base;
        }

        // One sheet that uses every translated feature, written as SpreadsheetML and converted by the library's own writer.
        const string sheet = "<worksheet xmlns=\"" + Main + "\" xmlns:r=\"" + R + "\"><sheetData>"
            + "<row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>Item</t></is></c><c r=\"B1\" t=\"inlineStr\"><is><t>Qty</t></is></c></row>"
            + "<row r=\"2\"><c r=\"A2\" t=\"inlineStr\"><is><t>a</t></is></c><c r=\"B2\"><f>SUM(Table1[Qty])+IF(A2=\"x\",1,2)</f><v>3</v></c></row></sheetData>"
            + "<sheetProtection sheet=\"1\" sort=\"0\"/><autoFilter ref=\"A1:B2\"><filterColumn colId=\"0\"><filters blank=\"1\"><filter val=\"a\"/><dateGroupItem year=\"2020\" month=\"2\" dateTimeGrouping=\"month\"/></filters></filterColumn>"
            + "<filterColumn colId=\"1\"><customFilters and=\"1\"><customFilter operator=\"greaterThan\" val=\"1\"/><customFilter operator=\"lessThan\" val=\"9\"/></customFilters></filterColumn></autoFilter>"
            + "<conditionalFormatting sqref=\"B2:B9\"><cfRule type=\"cellIs\" dxfId=\"0\" priority=\"1\" operator=\"between\"><formula>1</formula><formula>3</formula></cfRule><cfRule type=\"colorScale\" priority=\"2\"><colorScale><cfvo type=\"min\"/><cfvo type=\"max\"/><color rgb=\"FFF8696B\"/><color rgb=\"FF63BE7B\"/></colorScale></cfRule>"
            + "<cfRule type=\"dataBar\" priority=\"3\"><dataBar><cfvo type=\"min\"/><cfvo type=\"max\"/><color rgb=\"FF638EC6\"/></dataBar></cfRule><cfRule type=\"iconSet\" priority=\"4\"><iconSet iconSet=\"3Arrows\"><cfvo type=\"percent\" val=\"0\"/><cfvo type=\"percent\" val=\"33\"/><cfvo type=\"formula\" val=\"$D$1\"/></iconSet></cfRule></conditionalFormatting>"
            + "<dataValidations count=\"1\"><dataValidation type=\"list\" sqref=\"A2\" errorTitle=\"t\" prompt=\"p\"><formula1>\"a,b\"</formula1></dataValidation></dataValidations>"
            + "<rowBreaks count=\"1\" manualBreakCount=\"1\"><brk id=\"1\" max=\"16383\" man=\"1\"/></rowBreaks><ignoredErrors><ignoredError sqref=\"B2\" formula=\"1\"/></ignoredErrors><tableParts count=\"1\"><tablePart r:id=\"rId1\"/></tableParts></worksheet>";
        const string table = "<table xmlns=\"" + Main + "\" id=\"1\" name=\"Table1\" displayName=\"Table1\" ref=\"A1:B2\"><autoFilter ref=\"A1:B2\"/><tableColumns count=\"2\"><tableColumn id=\"1\" name=\"Item\"/><tableColumn id=\"2\" name=\"Qty\"><calculatedColumnFormula>Table1[[#This Row],[Item]]</calculatedColumnFormula></tableColumn></tableColumns><tableStyleInfo name=\"TableStyleMedium9\" showRowStripes=\"1\"/></table>";
        const string comments = "<comments xmlns=\"" + Main + "\"><authors><author>A</author></authors><commentList><comment ref=\"B2\" authorId=\"0\"><text><r><rPr><b/><sz val=\"9\"/><rFont val=\"Tahoma\"/></rPr><t>A:</t></r><r><t> hi</t></r></text></comment></commentList></comments>";
        const string styles = "<styleSheet xmlns=\"" + Main + "\"><fonts count=\"1\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts><fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills><borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs><cellXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/></cellXfs><dxfs count=\"1\"><dxf><font><b/></font></dxf></dxfs></styleSheet>";
        string rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
        OpcPackage p = OpcPackage.Create();
        p.SetRelationships(null, [new Relationship("rId1", rel + "officeDocument", "xl/workbook.xml")]);
        p.AddPart("/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml", System.Text.Encoding.UTF8.GetBytes("<workbook xmlns=\"" + Main + "\" xmlns:r=\"" + R + "\"><sheets><sheet name=\"S\" sheetId=\"1\" r:id=\"rId1\"/></sheets><definedNames><definedName name=\"N\">S!$A$1</definedName></definedNames></workbook>"));
        p.SetRelationships("/xl/workbook.xml", [new Relationship("rId1", rel + "worksheet", "worksheets/sheet1.xml"), new Relationship("rId2", rel + "styles", "styles.xml")]);
        p.AddPart("/xl/worksheets/sheet1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml", System.Text.Encoding.UTF8.GetBytes(sheet));
        p.SetRelationships("/xl/worksheets/sheet1.xml", [new Relationship("rId1", rel + "table", "../tables/table1.xml"), new Relationship("rId2", rel + "comments", "../comments1.xml")]);
        p.AddPart("/xl/tables/table1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.table+xml", System.Text.Encoding.UTF8.GetBytes(table));
        p.AddPart("/xl/comments1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.comments+xml", System.Text.Encoding.UTF8.GetBytes(comments));
        p.AddPart("/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml", System.Text.Encoding.UTF8.GetBytes(styles));
        using var ms = new MemoryStream();
        p.WriteTo(ms);
        using Workbook wb = Workbook.Open(new MemoryStream(ms.ToArray()));
        var bin = new MemoryStream();
        wb.SaveAs(bin, OfficeFormat.Xlsb, new LossReport());
        s_base = bin.ToArray();
        return s_base;
    }

    /// <summary>The forward cursor over a binary workbook, from a whole file: every worksheet, every row and cell, every accessor.</summary>
    public static void RunCursor(ReadOnlySpan<byte> data)
    {
        try
        {
            using XlsbStreamReader book = XlsbStreamReader.Open(new MemoryStream(data.ToArray()), new OpenContext(Limits));
            long sum = book.SharedStringCount;
            Span<char> buffer = stackalloc char[32];
            for (int i = 0; i < book.Sheets.Count; i++)
            {
                if (book.Sheets[i].Kind != SheetKind.Worksheet)
                {
                    continue;
                }

                using XlsbSheetCursor c = book.OpenSheet(i);
                sum += c.Columns.Count;
                while (c.ReadRow())
                {
                    sum += c.RowNumber + (long)c.RowHeight + c.RowOutlineLevel + c.RowStyle;
                    while (c.ReadCell())
                    {
                        sum += c.Column + c.Style + (long)c.Kind + (long)c.Number + c.Address.Row;
                        if (c.Kind == CellValueKind.Text)
                        {
                            sum += c.GetText().Length + c.TextUtf8.Length + (c.TryCopyText(buffer, out int n) ? n : -n);
                        }

                        if (c.IsDate && c.TryGetDateTime(out DateTime when))
                        {
                            sum += when.Ticks & 1;
                        }

                        sum += c.HasFormula ? c.GetFormula().Length : 0;
                    }
                }
            }

            if (sum == long.MinValue)
            {
                throw new InvalidOperationException();
            }
        }
        catch (OfficeException)
        {
        }
    }

    private static string? PartFor(int mode, OpcPackage p)
    {
        string? type = mode switch
        {
            0 => "application/vnd.ms-excel.worksheet",
            1 => "application/vnd.ms-excel.styles",
            2 => "application/vnd.ms-excel.sharedStrings",
            6 => "application/vnd.ms-excel.comments",
            7 => "application/vnd.ms-excel.table",
            _ => "application/vnd.ms-excel.sheet.binary.macroEnabled.main",
        };
        foreach (IPackagePart part in p.Parts)
        {
            if (mode == 3 ? part.Name.EndsWith("workbook.bin", StringComparison.OrdinalIgnoreCase) : string.Equals(part.ContentType, type, StringComparison.OrdinalIgnoreCase))
            {
                return part.Name;
            }
        }

        return null;
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
    }

    private static void RunCore(ReadOnlySpan<byte> data)
    {
        int mode = data.Length > 0 ? data[0] % 8 : 0;
        byte[] body = data.Length > 0 ? data[1..].ToArray() : [];
        var ctx = new OpenContext(Limits);
        if (mode == 4)
        {
            var reader = new XlsbRecordReader(body, ctx, "/fuzz.bin");
            while (reader.Read())
            {
                _ = reader.Type;
                _ = reader.Data.Length;
            }

            return;
        }

        if (mode == 5)
        {
            var cx = new XlsbFormulaContext();
            cx.SheetNames.AddRange(["A", "B"]);
            cx.SupBooks.Add(new XlsbSupBook(XlsbSupBookKind.Self));
            int split = body.Length == 0 ? 0 : body[0] % (body.Length + 1);
            byte[] rgce = body[..split];
            byte[] rgcb = body[split..];
            if (XlsbFormula.TryDecode(rgce, rgcb, cx, XlsbFormulaKind.Cell, 3, 3, out FormulaNode tree, out _))
            {
                _ = XlsbFormula.TryEncode(tree, cx, XlsbFormulaKind.Cell, 3, 3, out _, out _, out _);
            }

            return;
        }

        byte[] file = Replace(mode, body);
        using Workbook wb = Workbook.Open(new MemoryStream(file), new OpenContext(Limits));
        foreach (Worksheet sheet in wb.Sheets)
        {
            long sum = 0;
            foreach (CellRowView row in sheet.Store.EnumerateRows())
            {
                for (int i = 0; i < row.Count; i++)
                {
                    sum += row.ColumnAt(i) + row.StyleAt(i) + (long)row.ValueAt(i).Kind;
                }
            }

            _ = sum;
        }

        using var xlsx = new MemoryStream();
        wb.SaveAs(xlsx, OfficeFormat.Xlsx, new LossReport());

        char[] buf = new char[32];
        using XlsbStreamReader r = XlsbStreamReader.Open(new MemoryStream(file), new OpenContext(Limits));
        for (int s = 0; s < r.Sheets.Count; s++)
        {
            if (r.Sheets[s].Kind != SheetKind.Worksheet)
            {
                continue;
            }

            using XlsbSheetCursor c = r.OpenSheet(s);
            while (c.ReadRow())
            {
                while (c.ReadCell())
                {
                    _ = c.Value;
                    _ = c.TryCopyText(buf, out _);
                    if (c.HasFormula)
                    {
                        _ = c.GetFormula();
                    }
                }
            }
        }
    }

    private static byte[] Replace(int mode, byte[] body)
    {
        using OpcPackage src = OpcPackage.Open(new MemoryStream(Base()));
        string? target = PartFor(mode, src);
        OpcPackage dst = OpcPackage.Create();
        dst.SetRelationships(null, src.GetRelationships());
        foreach (IPackagePart part in src.Parts)
        {
            dst.AddPart(part.Name, part.ContentType, string.Equals(part.Name, target, StringComparison.OrdinalIgnoreCase) ? body : part.GetBytes());
            IReadOnlyList<Relationship> rels = src.GetRelationships(part.Name);
            if (rels.Count > 0)
            {
                dst.SetRelationships(part.Name, rels);
            }
        }

        using var ms = new MemoryStream();
        dst.WriteTo(ms);
        return ms.ToArray();
    }

    public static int WriteSeeds(string dir)
    {
        Directory.CreateDirectory(dir);
        using OpcPackage src = OpcPackage.Open(new MemoryStream(Base()));
        foreach (int mode in new[] { 0, 1, 2, 3, 6, 7 })
        {
            if (PartFor(mode, src) is { } name)
            {
                byte[] bytes = src.FindPart(name)!.GetBytes().ToArray();
                File.WriteAllBytes(Path.Combine(dir, "xlsb-part" + mode + ".bin"), [(byte)mode, .. bytes]);
                if (mode == 0)
                {
                    File.WriteAllBytes(Path.Combine(dir, "xlsb-records.bin"), [4, .. bytes]);
                }
            }
        }

        File.WriteAllBytes(Path.Combine(dir, "xlsb-formula.bin"), [5, 9, 0x24, 0, 0, 0, 0, 0, 0, 0, 0, 0x1E, 1, 0, 0x03, 0x41, 0x04, 0, 0x01, 0, 0x22, 2, 0x04, 0]);
        return 0;
    }
}
