using System;
using System.IO;
using OfficeOpenXml;

// EncryptionOracle: writes one small workbook twice, plain and password-protected, with EPPlus 4.5.3.3 (run as a black box).
//   EncryptionOracle <outdir> <password>
// Files: plain-epplus.xlsx, secret-epplus.xlsx and velvet-epplus.xlsx (the editing-only password). Its encryption is the agile scheme (Office 2010 and later).
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
    if (kind != "plain")
    {
        // "velvet" uses the fixed password Excel gives a workbook that is protected against editing only.
        package.SaveAs(new FileInfo(path), kind == "velvet" ? "VelvetSweatshop" : password);
    }
    else
    {
        package.SaveAs(new FileInfo(path));
    }
}
