using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RecursiveAI.OneOffice;
using RecursiveAI.OneOffice.Spreadsheets;

namespace RecursiveAI.OneOffice.Tools.XlsxProbe;

/// <summary>
/// <c>XlsxProbe gen OUT</c>: writes small generated workbooks that use every feature the style, table, conditional formatting, validation and name
/// models can author, for opening in desktop Excel (does it open without a repair prompt?). Two of them are built by opening a saved base file and
/// editing it, which is the path real edits take.
/// </summary>
internal static class GenerateFeatures
{
    public static int Run(string outDir)
    {
        Directory.CreateDirectory(outDir);
        Styles(Path.Combine(outDir, "g-styles.xlsx"));
        Tables(Path.Combine(outDir, "g-tables.xlsx"));
        Rules(Path.Combine(outDir, "g-cf.xlsx"));
        Validation(Path.Combine(outDir, "g-dv.xlsx"));
        Names(Path.Combine(outDir, "g-names.xlsx"));
        string baseFile = Path.Combine(outDir, "g-base.xlsx");
        using (Workbook b = Workbook.Create())
        {
            Fill(b.Sheets[0], 12);
            b.Sheets.Add("Second");
            b.SaveAs(baseFile);
        }

        using (Workbook wb = Workbook.Open(baseFile))
        {
            Worksheet s = wb.Sheets[0];
            s.Tables.Add("Orders", CellRange.Parse("A1:C8"), styleName: "TableStyleMedium9");
            s.Tables.First().Resize(CellRange.Parse("A1:D10"));
            s.Validations.Add(CellRange.Parse("E2:E10"), DataValidationSpec.List("Open", "Closed"));
            wb.SaveAs(Path.Combine(outDir, "g-edit-tables.xlsx"));
        }

        using (Workbook wb = Workbook.Open(baseFile))
        {
            Worksheet s = wb.Sheets[0];
            s["A2"].UpdateStyle(x => x with { Font = new CellFont("Arial", 14, Bold: true), Fill = CellFill.Solid(StyleColor.FromTheme(5, 0.6)), NumberFormatCode = "#,##0.00;[Red]-#,##0.00" });
            int dxf = wb.Styles.AddDifferentialFormat(new DifferentialFormat(new CellFont(Italic: true, Color: StyleColor.FromRgb(0xC00000))));
            s.ConditionalFormats.Add(CellRange.Parse("B2:B12"), ConditionalRuleSpec.CellIs("lessThan", "5", dxf));
            wb.Names.Add("Data", "Sheet1!$A$1:$C$12");
            wb.Names.AddBuiltIn(BuiltInName.PrintArea, s, "Sheet1!$A$1:$C$12");
            wb.Sheets[1]["B2"].SetDate(new DateTime(2024, 2, 29, 13, 30, 0));
            wb.SaveAs(Path.Combine(outDir, "g-edit-mixed.xlsx"));
        }

        ApplyFlags(Path.Combine(outDir, "apply"));
        Console.WriteLine("{\"kind\":\"summary\",\"files\":9}");
        return 0;
    }

    private static void Fill(Worksheet s, int rows)
    {
        s["A1"].SetString("Item");
        s["B1"].SetString("Qty");
        s["C1"].SetString("Price");
        for (int r = 2; r <= rows; r++)
        {
            s[r, 1].SetString("Item " + r);
            s[r, 2].SetNumber(r * 3 % 11);
            s[r, 3].SetNumber(r * 1.25);
        }
    }

    private static void Styles(string path)
    {
        using Workbook wb = Workbook.Create();
        Worksheet s = wb.Sheets[0];
        wb.Styles.AddNamedStyle("Highlight", new CellStyleSpec { Font = new CellFont("Calibri", 11, Bold: true), Fill = CellFill.Solid(StyleColor.FromRgb(0xFFFF99)) });
        var specs = new CellStyleSpec[]
        {
            new() { Font = new CellFont("Times New Roman", 12, Bold: true, Italic: true, Underline: FontUnderline.Doubled, Color: StyleColor.FromRgb(0x1F4E79)) },
            new() { Fill = new CellFill(FillPattern.DarkTrellis, StyleColor.FromRgb(0x808080), StyleColor.FromTheme(0)) },
            new() { Fill = new CellFill(FillPattern.None, Gradient: new GradientFill(false, 90, 0, 0, 0, 0, [new GradientStop(0, StyleColor.FromRgb(0xFFFFFF)), new GradientStop(1, StyleColor.FromRgb(0x4472C4))])) },
            new() { Border = new CellBorder(new BorderSide(BorderLineStyle.Thin, StyleColor.FromRgb(0)), new BorderSide(BorderLineStyle.Medium), new BorderSide(BorderLineStyle.Doubled, StyleColor.FromIndexed(10)), new BorderSide(BorderLineStyle.Dashed), new BorderSide(BorderLineStyle.Hair), DiagonalUp: true) },
            new() { Alignment = new CellAlignment(CellHorizontalAlignment.Center, CellVerticalAlignment.Center, WrapText: true, TextRotation: 45) },
            new() { Alignment = new CellAlignment(CellHorizontalAlignment.Left, Indent: 2), Protection = new CellProtection(false, true) },
            new() { NumberFormatCode = "[$-409]dddd, mmmm d, yyyy" },
            new() { NumberFormatCode = "0.00E+00", QuotePrefix = false },
            new() { StyleName = "Highlight", NumberFormatCode = "0%" },
        };
        for (int i = 0; i < specs.Length; i++)
        {
            Cell c = s[i + 1, 1];
            c.SetNumber(1234.5678 + i);
            c.SetStyle(specs[i]);
            s[i + 1, 2].SetString(c.FormattedText);
        }

        wb.SaveAs(path);
    }

    private static void Tables(string path)
    {
        using Workbook wb = Workbook.Create();
        Worksheet s = wb.Sheets[0];
        Fill(s, 10);
        s.Tables.Add("Sales", CellRange.Parse("A1:C10"));
        s.Tables.Add("Plain", CellRange.Parse("F2:G5"), hasHeaderRow: false, styleName: null);
        s["I1"].SetString("Name");
        s["J1"].SetString("Name");
        s["I2"].SetNumber(1);
        s.Tables.Add("Dup", CellRange.Parse("I1:J4"), styleName: "TableStyleLight15");
        Worksheet s2 = wb.Sheets.Add("Other");
        Fill(s2, 5);
        s2.Tables.Add("OtherData", CellRange.Parse("A1:C5"), styleName: "TableStyleDark3");
        s2.Tables.First().Resize(CellRange.Parse("A1:E8"));
        wb.SaveAs(path);
    }

    private static void Rules(string path)
    {
        using Workbook wb = Workbook.Create();
        Worksheet s = wb.Sheets[0];
        Fill(s, 20);
        int d1 = wb.Styles.AddDifferentialFormat(new DifferentialFormat(new CellFont(Bold: true, Color: StyleColor.FromRgb(0x9C0006)), Fill: new CellFill(FillPattern.Solid, Background: StyleColor.FromRgb(0xFFC7CE))));
        int d2 = wb.Styles.AddDifferentialFormat(new DifferentialFormat(NumberFormatCode: "0.0", Border: CellBorder.Box(BorderLineStyle.Thin, StyleColor.FromRgb(0x888888))));
        ConditionalFormatCollection cf = s.ConditionalFormats;
        cf.Add(CellRange.Parse("B2:B20"), ConditionalRuleSpec.CellIs("between", "2", d1, "6"));
        cf.Add(CellRange.Parse("C2:C20"), ConditionalRuleSpec.ThreeColorScale(StyleColor.FromRgb(0xF8696B), StyleColor.FromRgb(0xFFEB84), StyleColor.FromRgb(0x63BE7B)));
        cf.Add(CellRange.Parse("B2:B20"), ConditionalRuleSpec.DataBars(StyleColor.FromRgb(0x638EC6)));
        cf.Add(CellRange.Parse("C2:C20"), ConditionalRuleSpec.Icons("3Arrows"));
        cf.Add(CellRange.Parse("B2:C20"), ConditionalRuleSpec.Top(3, d2));
        cf.Add(CellRange.Parse("A2:A20"), ConditionalRuleSpec.Expression("MOD(ROW(),2)=0", d2));
        cf.Add(CellRange.Parse("B2:B20"), ConditionalRuleSpec.Duplicates(d1));
        cf.Add([CellRange.Parse("A2:A5"), CellRange.Parse("A10:A12")], ConditionalRuleSpec.TwoColorScale(StyleColor.FromRgb(0xFFFFFF), StyleColor.FromRgb(0x5A8AC6)));
        wb.SaveAs(path);
    }

    private static void Validation(string path)
    {
        using Workbook wb = Workbook.Create();
        Worksheet s = wb.Sheets[0];
        Worksheet lists = wb.Sheets.Add("Lists");
        lists["A1"].SetString("Red");
        lists["A2"].SetString("Green");
        s.Validations.Add(CellRange.Parse("A1:A10"), DataValidationSpec.List("Yes", "No") with { Prompt = "Pick", PromptTitle = "Choice", Error = "Not allowed", ErrorTitle = "Stop" });
        s.Validations.Add(CellRange.Parse("B1:B10"), DataValidationSpec.WholeBetween(1, 100) with { ErrorStyle = DataValidationErrorStyle.Warning });
        s.Validations.Add(CellRange.Parse("C1:C10"), DataValidationSpec.DecimalBetween(0.5, 9.5) with { Operator = "notBetween" });
        s.Validations.Add(CellRange.Parse("D1:D10"), DataValidationSpec.Custom("LEN(D1)<5") with { ErrorStyle = DataValidationErrorStyle.Information });
        s.Validations.Add([CellRange.Parse("E1:E3"), CellRange.Parse("G1:G3")], DataValidationSpec.ListFromRange("Lists!$A$1:$A$2") with { InCellDropDown = false });
        wb.SaveAs(path);
    }

    private static void Names(string path)
    {
        using Workbook wb = Workbook.Create();
        Worksheet s = wb.Sheets[0];
        Fill(s, 6);
        Worksheet o = wb.Sheets.Add("Other Sheet");
        wb.Names.Add("Total", "SUM(Sheet1!$B$2:$B$6)");
        wb.Names.Add("TaxRate", "0.2", hidden: true);
        wb.Names.Add("Local", "'Other Sheet'!$A$1", o);
        wb.Names.AddBuiltIn(BuiltInName.PrintArea, s, "Sheet1!$A$1:$C$6");
        wb.Names.AddBuiltIn(BuiltInName.PrintTitles, s, "Sheet1!$1:$1");
        DefinedName n = wb.Names.Add("Temp", "1");
        wb.Names.Rename(n, "Renamed_1");
        wb.SaveAs(path);
    }

    /// <summary>
    /// A grid-mode workbook that settles how an explicit false <c>applyNumberFormat</c> is read: each cell format names a cell style whose number
    /// format is 0% and carries its own format and flag; Excel's displayed text says which one wins. Writes <c>STEM.xlsx</c> and <c>STEM.ours.tsv</c>.
    /// </summary>
    private static void ApplyFlags(string stem)
    {
        (string Attrs, string Label)[] cases =
        [
            ("numFmtId=\"0\" applyNumberFormat=\"0\"", "own General, flag 0"),
            ("numFmtId=\"0\"", "own General, no flag"),
            ("numFmtId=\"0\" applyNumberFormat=\"1\"", "own General, flag 1"),
            ("numFmtId=\"10\" applyNumberFormat=\"0\"", "own 0.00%, flag 0"),
            ("numFmtId=\"10\"", "own 0.00%, no flag"),
        ];
        using (Workbook wb = Workbook.Create())
        {
            Worksheet s = wb.Sheets[0];
            s.Rename("cells");
            _ = wb.Styles.GetOrAdd(new CellStyleSpec { NumberFormatCode = "0.0" }); // makes the style sheet a part
            s["A1"].SetString("id");
            for (int i = 0; i < cases.Length; i++)
            {
                s[i + 2, 1].SetString("ap-" + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
                s[i + 2, 2].SetNumber(0.5);
                s[i + 2, 2].StyleIndex = 2 + i;
            }

            wb.SaveAs(stem + ".tmp.xlsx");
        }

        // The cell formats are written by hand because the model's builder always states what it applies.
        using (Packaging.OpcPackage p = Packaging.OpcPackage.Open(new MemoryStream(File.ReadAllBytes(stem + ".tmp.xlsx"))))
        {
            Packaging.IPackagePart styles = p.FindPart("/xl/styles.xml")!;
            string xml = System.Text.Encoding.UTF8.GetString(styles.GetBytes().Span);
            xml = xml.Replace("</cellStyleXfs>", "<xf numFmtId=\"9\" fontId=\"0\" fillId=\"0\" borderId=\"0\" applyNumberFormat=\"1\"/></cellStyleXfs>", StringComparison.Ordinal).Replace("<cellStyleXfs count=\"1\">", "<cellStyleXfs count=\"2\">", StringComparison.Ordinal);
            string xfs = string.Concat(cases.Select(c => "<xf " + c.Attrs + " fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"1\"/>"));
            xml = xml.Replace("</cellXfs>", xfs + "</cellXfs>", StringComparison.Ordinal);
            int at = xml.IndexOf("<cellXfs count=\"", StringComparison.Ordinal);
            xml = xml.Remove(at, xml.IndexOf('>', at) - at + 1).Insert(at, "<cellXfs count=\"" + (cases.Length + 2).ToString(System.Globalization.CultureInfo.InvariantCulture) + "\">");
            styles.SetBytes(System.Text.Encoding.UTF8.GetBytes(xml));
            using var fs = File.Create(stem + ".xlsx");
            p.WriteTo(fs);
        }

        File.Delete(stem + ".tmp.xlsx");
        using Workbook back = Workbook.Open(stem + ".xlsx");
        var ours = new System.Text.StringBuilder();
        for (int i = 0; i < cases.Length; i++)
        {
            Cell c = back.Sheets[0][i + 2, 2];
            ours.Append("ap-").Append(i + 1).Append('\t').Append(c.GetFormattedText(99)).Append('\t').Append(cases[i].Label).Append('\t').Append("0.5\n");
        }

        File.WriteAllText(stem + ".ours.tsv", ours.ToString());
    }
}
