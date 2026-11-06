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

    private static byte[] Base()
    {
        if (s_base is not null)
        {
            return s_base;
        }

        var ms = new MemoryStream();
        using (XlsxStreamWriter w = XlsxStreamWriter.Create(ms))
        {
            SheetStreamWriter s = w.AddSheet("S");
            for (int i = 0; i < 6; i++)
            {
                s.BeginRow();
                s.WriteNumber(i * 1.5);
                s.WriteText("t" + i, 0);
                s.WriteBoolean(i % 2 == 0);
                s.WriteFormulaNumber("A1+IF(B1>0,1,2)+SUM(A1:A3)", i);
                s.WriteDate(new DateTime(2020, 1, 1).AddDays(i), 0);
            }
        }

        using Workbook wb = Workbook.Open(new MemoryStream(ms.ToArray()));
        var bin = new MemoryStream();
        wb.SaveAs(bin, OfficeFormat.Xlsb, new LossReport());
        s_base = bin.ToArray();
        return s_base;
    }

    private static string? PartFor(int mode, OpcPackage p)
    {
        string? type = mode switch
        {
            0 => "application/vnd.ms-excel.worksheet",
            1 => "application/vnd.ms-excel.styles",
            2 => "application/vnd.ms-excel.sharedStrings",
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
        int mode = data.Length > 0 ? data[0] % 6 : 0;
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
        for (int mode = 0; mode <= 3; mode++)
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
