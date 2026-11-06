using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Spreadsheets.Biff;

namespace RecursiveAI.OneDocument.Spreadsheets.Tests.Biff;

public class XlsOracleTests
{
    internal static string Dir
    {
        get
        {
            string d = AppContext.BaseDirectory;
            while (d.Length > 0 && !Directory.Exists(Path.Combine(d, "testdata")))
            {
                d = Path.GetDirectoryName(d) ?? string.Empty;
            }

            if (d.Length == 0)
            {
                // Output is outside the checkout; the source tree is found through the compile-time path of this file.
                d = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(SourcePath())))!;
                d = Path.GetDirectoryName(d)!;
            }

            return Path.Combine(d, "testdata", "xls");
        }
    }

    private static string SourcePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    private static string Norm(string? formula)
    {
        if (formula is null)
        {
            return string.Empty;
        }

        return FormulaPrinter.Print(FormulaParser.Parse(formula));
    }

    // Differences that come from LibreOffice's own export, not from the reader: the cell and what the file holds.
    private static readonly HashSet<string> KnownLibreOfficeDifferences =
    [
        "formulas/F!D33",
    ];

    [Theory]
    [InlineData("basic")]
    [InlineData("formulas")]
    [InlineData("styles")]
    [InlineData("features")]
    public void ConvertedWorkbookMatchesTheSourceItWasExportedFrom(string name)
    {
        using Workbook original = Workbook.Open(Path.Combine(Dir, name + ".xlsx"));
        var ctx = new OpenContext();
        using Workbook converted = XlsConverter.ToWorkbook(ByteSourceFromFile(Path.Combine(Dir, name + ".xls")), ctx);
        var problems = new List<string>();
        Assert.Equal(original.Sheets.Count, converted.Sheets.Count);
        foreach (Worksheet o in original.Sheets)
        {
            Worksheet c = converted.Sheets[o.Name];
            Assert.Equal(o.State, c.State);
            foreach (CellRowView row in o.Store.EnumerateRows())
            {
                for (int i = 0; i < row.Count; i++)
                {
                    var addr = new CellAddress(row.Row, row.ColumnAt(i));
                    string key = name + "/" + o.Name + "!" + addr;
                    if (KnownLibreOfficeDifferences.Contains(key))
                    {
                        continue;
                    }

                    CellValue ov = row.ValueAt(i);
                    Cell cc = c[addr];
                    CellValue cv = cc.Value;
                    string? of = row.FormulaAt(i)?.Text;
                    if (ov.Kind == CellValueKind.Empty && of is null)
                    {
                        continue;
                    }

                    if (of is not null)
                    {
                        // Volatile and clock functions differ between runs.
                        if (of.Contains("NOW()", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        string cf = cc.Formula ?? string.Empty;
                        if (row.FormulaAt(i)!.Kind == CellFormulaKind.Normal && Norm(of) != Norm(cf))
                        {
                            problems.Add($"{key}: formula '{of}' became '{cf}'");
                        }
                    }

                    string ot = o.GetText(ov);
                    string ct = c.GetText(cv);
                    if (ov.Kind == CellValueKind.Number && cv.Kind == CellValueKind.Number)
                    {
                        if (Math.Abs(ov.Number - cv.Number) > 1e-9 * Math.Max(1, Math.Abs(ov.Number)))
                        {
                            problems.Add($"{key}: number {ov.Number.ToString("R", CultureInfo.InvariantCulture)} became {cv.Number.ToString("R", CultureInfo.InvariantCulture)}");
                        }
                    }
                    else if (ov.Kind != cv.Kind && !(ov.Kind == CellValueKind.Empty))
                    {
                        // openpyxl leaves formula results empty, so only constants are compared by kind.
                        if (of is null)
                        {
                            problems.Add($"{key}: {ov.Kind} '{ot}' became {cv.Kind} '{ct}'");
                        }
                    }
                    else if (of is null && ot != ct)
                    {
                        problems.Add($"{key}: '{ot}' became '{ct}'");
                    }
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.DoesNotContain(((DefectCollection)ctx.Defects).ToArray(), d => d.Severity == DefectSeverity.Fatal);
    }

    private static RecursiveAI.OneDocument.IO.IByteSource ByteSourceFromFile(string path) => RecursiveAI.OneDocument.IO.ByteSource.FromMemory(File.ReadAllBytes(path));

    [Fact]
    public void PicturesAreCarriedWithTheirAnchors()
    {
        using Workbook wb = XlsConverter.ToWorkbook(ByteSourceFromFile(Path.Combine(Dir, "pictures.xls")), new OpenContext());
        Assert.True(wb.ImportLoss!.CountOf("loss.document-properties") >= 1);
        byte[] png = wb.Package.FindPart("/xl/media/image1.png")!.GetBytes().ToArray();
        byte[] jpg = wb.Package.FindPart("/xl/media/image2.jpeg")!.GetBytes().ToArray();
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png[..4]);
        Assert.Equal(new byte[] { 0xFF, 0xD8 }, jpg[..2]);
        string drawing = System.Text.Encoding.UTF8.GetString(wb.Package.FindPart("/xl/drawings/drawing1.xml")!.GetBytes().ToArray());
        Assert.Contains("<xdr:from><xdr:col>1</xdr:col>", drawing, StringComparison.Ordinal);
        Assert.Contains("<xdr:row>8</xdr:row>", drawing, StringComparison.Ordinal);
        Assert.Contains(wb.Package.GetRelationships(wb.Sheets[0].PartName!), r => r.Type.EndsWith("/drawing", StringComparison.Ordinal));
        using var saved = new MemoryStream();
        wb.SaveAs(saved, OfficeFormat.Xlsx);
        Assert.True(saved.Length > 2000);
    }

    [Fact]
    public void EveryFormulaOfTheOracleFilesSurvivesAnEncodeAndDecode()
    {
        // Parse the formula text, encode it as BIFF8 tokens, decode the tokens: the text must come back the same.
        int count = 0;
        var problems = new List<string>();
        foreach (string name in new[] { "basic", "formulas", "features" })
        {
            using Workbook wb = XlsConverter.ToWorkbook(ByteSourceFromFile(Path.Combine(Dir, name + ".xls")), new OpenContext());
            var ctx = new FormulaContext { Version = 8, SheetNames = [.. wb.Sheets.Select(s => s.Name)] };
            foreach (DefinedName n in wb.Names)
            {
                ctx.Names.Add(new DefinedNameInfo { Name = n.Name, SheetScope = n.LocalSheetId ?? -1 });
            }

            foreach (Worksheet ws in wb.Sheets)
            {
                foreach (CellRowView row in ws.Store.EnumerateRows())
                {
                    for (int i = 0; i < row.Count; i++)
                    {
                        string? text = row.FormulaAt(i)?.Text;
                        if (string.IsNullOrEmpty(text) || row.FormulaAt(i)!.Kind != CellFormulaKind.Normal)
                        {
                            continue;
                        }

                        count++;
                        FormulaNode tree = FormulaParser.Parse(text);
                        var enc = new PtgEncoder(ctx);
                        if (!enc.TryEncode(tree, out byte[] rgce, out byte[] rgcb))
                        {
                            problems.Add($"{name}/{ws.Name}!{new CellAddress(row.Row, row.ColumnAt(i))}: cannot encode '{text}': {enc.Reason}");
                            continue;
                        }

                        var dec = new PtgDecoder(ctx, Encoding.Latin1);
                        if (!dec.TryDecode(rgce, rgcb, 0, 0, out FormulaNode back))
                        {
                            problems.Add($"{name}/{ws.Name}!{new CellAddress(row.Row, row.ColumnAt(i))}: cannot decode '{text}'");
                            continue;
                        }

                        if (FormulaPrinter.Print(back) != FormulaPrinter.Print(tree))
                        {
                            problems.Add($"'{text}' came back as '{FormulaPrinter.Print(back)}'");
                        }
                    }
                }
            }
        }

        Assert.True(count > 60, "formulas checked: " + count);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }
}
