using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Cfb;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Spreadsheets;
using RecursiveAI.OneDocument.Spreadsheets.Biff;

namespace RecursiveAI.OneDocument.Fuzz;

/// <summary>
/// The legacy workbook reader. <c>xls</c> feeds whole files (a compound file or a bare BIFF stream) to the converter, walks the
/// result and writes it as a workbook; <c>ptg</c> feeds formula tokens to the decoder with every BIFF layout, and encodes what it
/// decoded. Only <see cref="OfficeException"/> may escape.
/// </summary>
internal static class XlsTarget
{
    // Small limits keep each execution fast.
    private static readonly OfficeLimits Limits = new() { MaxAllocationBytes = 1 << 24, MaxRecordCount = 200_000, MaxPartSize = 8 << 20, MaxDefects = 100 };

    public static void Run(ReadOnlySpan<byte> data)
    {
        try
        {
            using Workbook wb = XlsConverter.ToWorkbook(ByteSource.FromMemory(data.ToArray()), new OpenContext(Limits));
            long sum = 0;
            foreach (Worksheet ws in wb.Sheets)
            {
                foreach (CellRowView row in ws.Store.EnumerateRows())
                {
                    for (int i = 0; i < row.Count; i++)
                    {
                        sum += (long)row.ValueAt(i).Kind + (row.FormulaAt(i)?.Text.Length ?? 0);
                    }
                }
            }

            using var ms = new MemoryStream();
            wb.SaveAs(ms, OfficeFormat.Xlsx);

            // What was read is written as BIFF8 and read again: the writer meets whatever the hub holds, and what it wrote must open.
            using var xls = new MemoryStream();
            wb.SaveAs(xls, OfficeFormat.Xls);
            using Workbook again = XlsConverter.ToWorkbook(ByteSource.FromMemory(xls.ToArray()), new OpenContext(Limits));
            if (again.Sheets.Count > wb.Sheets.Count)
            {
                throw new InvalidOperationException("The written workbook has more sheets than the one written.");
            }

            if (sum < 0)
            {
                throw new InvalidOperationException();
            }
        }
        catch (OfficeException)
        {
        }
    }

    public static void RunPtg(ReadOnlySpan<byte> data)
    {
        if (data.Length < 3)
        {
            return;
        }

        int version = data[0] % 5 switch { 0 => 2, 1 => 3, 2 => 4, 3 => 5, _ => 8 };
        int split = data[1] * (data.Length - 2) / 255;
        byte[] rgce = data.Slice(2, split).ToArray();
        byte[] rgcb = data[(2 + split)..].ToArray();
        var ctx = new FormulaContext
        {
            Version = version,
            SheetNames = ["A", "B", "C d"],
            Xti = [(0, 0, 0), (0, 0, 2), (1, 0, 0)],
            Books = [new SupBook { IsInternal = true }, new SupBook { Url = "x.xls", Sheets = ["S"], ExternNames = ["f"] }],
            Biff5Externs = ["B", null],
            Names = [new DefinedNameInfo { Name = "N1" }, new DefinedNameInfo { Name = "Loc", SheetScope = 1 }, new DefinedNameInfo { IsBuiltIn = true, BuiltIn = 6 }],
        };
        var dec = new PtgDecoder(ctx, Encoding.Latin1);
        if (!dec.TryDecode(rgce, rgcb, data[1], data[0], out FormulaNode node))
        {
            return;
        }

        string text = FormulaPrinter.Print(node);
        if (version != 8)
        {
            return;
        }

        var enc = new PtgEncoder(ctx);
        if (enc.TryEncode(node, out byte[] again, out byte[] extra))
        {
            if (!dec.TryDecode(again, extra, data[1], data[0], out FormulaNode back) || FormulaPrinter.Print(back) != text)
            {
                throw new InvalidOperationException("encode then decode changed the formula: " + text);
            }
        }
    }

    public static int WriteSeeds(string dir)
    {
        Directory.CreateDirectory(dir);
        string testdata = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(ThisFile())))!, "testdata", "xls");
        int n = 0;
        if (Directory.Exists(testdata))
        {
            foreach (string f in Directory.GetFiles(testdata, "*.xls"))
            {
                byte[] file = File.ReadAllBytes(f);
                File.WriteAllBytes(Path.Combine(dir, Path.GetFileName(f)), file);
                using var pkg = CfbPackage.Open(ByteSource.FromMemory(file));
                if (pkg.FindPart("/Workbook") is { } part)
                {
                    File.WriteAllBytes(Path.Combine(dir, Path.GetFileNameWithoutExtension(f) + ".biff"), part.GetBytes().ToArray());
                }

                n += 2;
            }
        }

        // Formula token seeds: version byte, split byte, then PtgRef + PtgInt + PtgAdd in the BIFF8 layout.
        File.WriteAllBytes(Path.Combine(dir, "ptg-add.bin"), [4, 255, 0x24, 0, 0, 0, 0xC0, 0x1E, 1, 0, 0x03]);
        File.WriteAllBytes(Path.Combine(dir, "ptg-sum.bin"), [4, 255, 0x25, 0, 0, 9, 0, 0, 0, 3, 0, 0x22, 1, 4, 0]);
        return n + 2 > 0 ? 0 : 1;
    }

    private static string ThisFile([CallerFilePath] string path = "") => path;
}
