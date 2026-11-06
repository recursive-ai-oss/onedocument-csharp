using OfficeOpenXml;

namespace RecursiveAI.OneDocument.Bench;

/// <summary>
/// Writes the password-protected comparator fixtures under <c>testdata/encryption</c> with EPPlus 4.5.3.3 (LGPL-3.0, run as a black box).
/// <c>encfixtures &lt;outdir&gt; &lt;password&gt;</c> writes plain-epplus.xlsx, secret-epplus.xlsx and velvet-epplus.xlsx (the fixed
/// editing-only password); the encryption is the agile scheme.
/// </summary>
internal static class EncryptionFixtures
{
    public static int Run(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("usage: encfixtures <outdir> <password>");
            return 2;
        }

        string dir = args[0];
        string password = args[1];
        Directory.CreateDirectory(dir);
        foreach (string kind in new[] { "plain", "secret", "velvet" })
        {
            using var package = new ExcelPackage();
            ExcelWorksheet ws = package.Workbook.Worksheets.Add("Data");
            ws.Cells["A1"].Value = "Name";
            ws.Cells["B1"].Value = "Amount";
            for (int i = 0; i < 40; i++)
            {
                ws.Cells[i + 2, 1].Value = "Item " + (i + 1);
                ws.Cells[i + 2, 2].Value = (i + 1) * 1.5;
            }

            ws.Cells["B43"].Formula = "SUM(B2:B41)";
            string path = Path.Combine(dir, kind + "-epplus.xlsx");
            if (kind == "plain")
            {
                package.SaveAs(new FileInfo(path));
            }
            else
            {
                package.SaveAs(new FileInfo(path), kind == "velvet" ? "VelvetSweatshop" : password);
            }
        }

        return 0;
    }
}
