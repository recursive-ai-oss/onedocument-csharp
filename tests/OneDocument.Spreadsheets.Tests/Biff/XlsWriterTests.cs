using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Spreadsheets.Biff;

namespace RecursiveAI.OneDocument.Spreadsheets.Tests.Biff;

public class XlsWriterTests
{

    internal static byte[] Save(Workbook wb, out LossReport loss)
    {
        using var ms = new MemoryStream();
        wb.SaveAs(ms, OfficeFormat.Xls);
        loss = wb.ExportLoss!;
        return ms.ToArray();
    }

    internal static Workbook Reopen(byte[] bytes) => XlsConverter.ToWorkbook(ByteSource.FromMemory(bytes), new OpenContext());

    private static string Color(Workbook wb, StyleColor c) => c.Kind is ColorKind.None or ColorKind.Auto ? "auto" : (wb.Styles.ResolveColor(c) & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture);

    private static string StyleKey(Workbook wb, int xf)
    {
        ResolvedCellStyle s = wb.Styles.Resolve(xf);
        CellFont f = s.Font;
        CellBorder b = s.Border;
        CellFill fl = s.Fill;
        var sb = new StringBuilder();
        sb.Append(f.Name).Append('|').Append(f.Size.ToString(CultureInfo.InvariantCulture)).Append('|').Append(f.Bold).Append(f.Italic).Append(f.Strike).Append(f.Underline).Append(f.VerticalAlignment).Append('|').Append(Color(wb, f.Color));
        sb.Append("|fill:").Append(fl.Pattern).Append(fl.Pattern == FillPattern.None ? string.Empty : Color(wb, fl.Foreground));
        sb.Append("|nf:").Append(s.NumberFormatCode);
        foreach (BorderSide side in new[] { b.Left, b.Right, b.Top, b.Bottom })
        {
            sb.Append('|').Append(side.Style);
            if (side.Style != BorderLineStyle.None)
            {
                sb.Append(Color(wb, side.Color));
            }
        }

        CellAlignment a = s.Alignment;
        sb.Append("|al:").Append(a.Horizontal).Append(a.Vertical).Append(a.WrapText).Append(a.Indent).Append(a.TextRotation).Append(a.ShrinkToFit);
        sb.Append("|pr:").Append(s.Protection.Locked).Append(s.Protection.Hidden);
        return sb.ToString();
    }

    /// <summary>Lists how two workbooks differ in the things BIFF8 can carry.</summary>
    internal static List<string> Differences(Workbook a, Workbook b)
    {
        var d = new List<string>();
        if (a.Sheets.Count != b.Sheets.Count)
        {
            d.Add("sheet count " + a.Sheets.Count + " vs " + b.Sheets.Count);
            return d;
        }

        for (int i = 0; i < a.Sheets.Count; i++)
        {
            Worksheet x = a.Sheets[i];
            Worksheet y = b.Sheets[i];
            if (x.Name != y.Name || x.State != y.State)
            {
                d.Add("sheet " + i + " " + x.Name + "/" + y.Name);
            }

            var cells = new Dictionary<(int, int), (CellValue V, CellFormula? F, int S)>();
            foreach (CellRowView row in x.Store.EnumerateRows())
            {
                for (int k = 0; k < row.Count; k++)
                {
                    cells[(row.Row, row.ColumnAt(k))] = (row.ValueAt(k), row.FormulaAt(k), row.StyleAt(k));
                }
            }

            foreach (CellRowView row in y.Store.EnumerateRows())
            {
                for (int k = 0; k < row.Count; k++)
                {
                    (int, int) key = (row.Row, row.ColumnAt(k));
                    CellValue vb = row.ValueAt(k);
                    CellFormula? fb = row.FormulaAt(k);
                    int sb = row.StyleAt(k);
                    string at = x.Name + "!" + new CellAddress(key.Item1, key.Item2);
                    if (!cells.Remove(key, out var ca))
                    {
                        if (!vb.IsEmpty || fb is not null)
                        {
                            d.Add(at + " appeared");
                        }

                        continue;
                    }

                    CellValue va = ca.V;
                    if (va.Kind != vb.Kind || (va.Kind == CellValueKind.Text ? x.GetText(va) != y.GetText(vb) : va.Kind == CellValueKind.Number ? va.Number != vb.Number : va.Kind == CellValueKind.Boolean ? va.Boolean != vb.Boolean : va.Kind == CellValueKind.Error && va.Error != vb.Error))
                    {
                        d.Add(at + " value " + va + " vs " + vb);
                    }

                    string? ta = ca.F is null ? null : FormulaText(ca.F);
                    string? tb = fb is null ? null : FormulaText(fb);
                    if (ta != tb)
                    {
                        d.Add(at + " formula " + ta + " vs " + tb);
                    }

                    if (StyleKey(a, ca.S) != StyleKey(b, sb))
                    {
                        d.Add(at + " style " + StyleKey(a, ca.S) + " vs " + StyleKey(b, sb));
                    }
                }
            }

            foreach (var kv in cells)
            {
                if (!kv.Value.V.IsEmpty || kv.Value.F is not null || kv.Value.S != 0)
                {
                    d.Add(x.Name + "!" + new CellAddress(kv.Key.Item1, kv.Key.Item2) + " vanished");
                }
            }

            string ma = string.Join(",", x.MergedCells.Select(r => r.ToString()).Order());
            string mb = string.Join(",", y.MergedCells.Select(r => r.ToString()).Order());
            if (ma != mb)
            {
                d.Add("merges " + ma + " vs " + mb);
            }

            string ca2 = string.Join(";", x.Columns.Select(c => c.Min + "-" + c.Max + ":" + Math.Round(c.Width, 2).ToString(CultureInfo.InvariantCulture) + ":" + (c.Flags & ColumnOptions.Hidden)));
            string cb = string.Join(";", y.Columns.Select(c => c.Min + "-" + c.Max + ":" + Math.Round(c.Width, 2).ToString(CultureInfo.InvariantCulture) + ":" + (c.Flags & ColumnOptions.Hidden)));
            if (ca2 != cb)
            {
                d.Add("columns " + ca2 + " vs " + cb);
            }

            foreach (CellRowView row in x.Store.EnumerateRows())
            {
                RowProperties ra = x.GetRow(row.Row);
                RowProperties rb = y.GetRow(row.Row);
                if (Math.Abs(ra.Height - rb.Height) > 0.05 || ra.Hidden != rb.Hidden || ra.OutlineLevel != rb.OutlineLevel)
                {
                    d.Add("row " + row.Row + " " + ra.Height + "/" + rb.Height);
                }
            }

            string va2 = string.Join(";", x.Validations.Select(v => v.Type + "," + v.Operator + "," + v.Formula1 + "," + v.Formula2 + "," + string.Join(" ", v.Ranges) + "," + v.AllowBlank + v.ShowErrorMessage + v.Error));
            string vb2 = string.Join(";", y.Validations.Select(v => v.Type + "," + v.Operator + "," + v.Formula1 + "," + v.Formula2 + "," + string.Join(" ", v.Ranges) + "," + v.AllowBlank + v.ShowErrorMessage + v.Error));
            if (va2 != vb2)
            {
                d.Add("validation " + va2 + " vs " + vb2);
            }

            string cfa = CfKey(a, x);
            string cfb = CfKey(b, y);
            if (cfa != cfb)
            {
                d.Add("conditional formats " + cfa + " vs " + cfb);
            }
        }

        string na = string.Join(";", a.Names.Select(n => n.Name + "@" + n.LocalSheetId + "=" + n.Formula).Order());
        string nb = string.Join(";", b.Names.Select(n => n.Name + "@" + n.LocalSheetId + "=" + n.Formula).Order());
        if (na != nb)
        {
            d.Add("names " + na + " vs " + nb);
        }

        return d;
    }

    // A differential format's fill with no pattern named is solid, with its colour in the background.
    private static string FillKey(Workbook wb, CellFill? f)
    {
        if (f is null)
        {
            return "-";
        }

        StyleColor c = f.Background.Kind is not (ColorKind.None or ColorKind.Auto) ? f.Background : f.Foreground;
        bool has = c.Kind is not (ColorKind.None or ColorKind.Auto);
        if (f.Pattern is FillPattern.None or FillPattern.Solid)
        {
            return has ? "solid" + Color(wb, c) : "-";
        }

        return f.Pattern + Color(wb, f.Foreground);
    }

    private static string FormulaText(CellFormula f) => f.Kind + ":" + f.Text + ":" + f.Reference;

    private static string CfKey(Workbook wb, Worksheet ws)
    {
        var sb = new StringBuilder();
        foreach (ConditionalFormat g in ws.ConditionalFormats)
        {
            sb.Append('[').Append(string.Join(" ", g.Ranges));
            foreach (ConditionalFormatRule r in g.Rules.OrderBy(r => r.Priority))
            {
                sb.Append('{').Append(r.Type).Append(',').Append(r.Operator).Append(',').Append(string.Join("|", r.Formulas)).Append(',').Append(r.Priority).Append(',').Append(r.StopIfTrue);
                if (r.DxfId is { } id)
                {
                    DifferentialFormat df = wb.Styles.GetDifferentialFormat(id);
                    sb.Append(",dxf:").Append(df.Font?.Bold).Append(df.Font?.Italic).Append(df.Font is null ? "-" : Color(wb, df.Font.Color)).Append(df.NumberFormatCode).Append(FillKey(wb, df.Fill));
                }

                if (r.ColorScale is { } cs)
                {
                    sb.Append(",cs:").Append(string.Join("/", cs.Points.Select(p => p.Type + p.Value))).Append(string.Join("/", cs.Colors.Select(c => Color(wb, c))));
                }

                if (r.DataBar is { } db)
                {
                    sb.Append(",db:").Append(string.Join("/", db.Points.Select(p => p.Type + p.Value))).Append(Color(wb, db.Color)).Append(db.MinLength).Append(db.MaxLength).Append(db.ShowValue);
                }

                if (r.IconSet is { } ic)
                {
                    sb.Append(",is:").Append(ic.Name).Append(string.Join("/", ic.Points.Select(p => p.Type + p.Value + p.GreaterOrEqual))).Append(ic.ShowValue).Append(ic.Reverse);
                }

                if (r.Type == ConditionalRuleType.Top10)
                {
                    sb.Append(",top:").Append(r.Rank).Append(r.Percent).Append(r.Bottom);
                }

                if (r.Type is ConditionalRuleType.ContainsText or ConditionalRuleType.NotContainsText or ConditionalRuleType.BeginsWith or ConditionalRuleType.EndsWith)
                {
                    sb.Append(",text:").Append(r.Text);
                }

                if (r.Type == ConditionalRuleType.TimePeriod)
                {
                    sb.Append(",period:").Append(r.TimePeriod);
                }

                sb.Append('}');
            }

            sb.Append(']');
        }

        return sb.ToString();
    }

    private static Workbook Sample()
    {
        Workbook wb = Workbook.Create();
        Worksheet ws = wb.Sheets[0];
        ws.Rename("Data");
        ws["A1"].SetString("Name");
        ws["B1"].SetString("Qty");
        ws["C1"].SetString("Price");
        ws["D1"].SetString("Total");
        string[] names = ["Apple", "Pear", "Fig \u00e9\u4e2d", "Plum"];
        for (int i = 0; i < names.Length; i++)
        {
            int r = i + 2;
            ws[r, 1].SetString(names[i]);
            ws[r, 2].SetNumber((i + 1) * 3);
            ws[r, 3].SetNumber(1.25 * (i + 1) + 0.005);
            ws[r, 4].Formula = "B" + r + "*C" + r;
            ws[r, 4].SetNumber((i + 1) * 3 * (1.25 * (i + 1) + 0.005));
        }

        ws["A7"].SetBoolean(true);
        ws["B7"].SetError(CellError.DivideByZero);
        ws["C7"].SetNumber(1e300);
        ws["D7"].SetNumber(-0.5);
        ws["A8"].SetString(new string('x', 300));
        ws["B8"].Formula = "SUM(B2:B5)";
        ws["B8"].SetNumber(30);
        ws["C8"].Formula = "IF(B8>10,\"big\",\"small\")";
        ws["C8"].SetString("big");
        ws["A1"].SetStyle(new CellStyleSpec { Font = new CellFont("Arial", 12, Bold: true, Color: StyleColor.FromRgb(0x336699)), Fill = CellFill.Solid(StyleColor.FromRgb(0xFFEECC)), Border = CellBorder.Box(BorderLineStyle.Thin, StyleColor.FromRgb(0x112233)) });
        ws["C2"].SetStyle(new CellStyleSpec { NumberFormatCode = "#,##0.00", Alignment = new CellAlignment(CellHorizontalAlignment.Right, WrapText: true) });
        ws["D2"].SetStyle(new CellStyleSpec { NumberFormatCode = "0.0%;[Red]-0.0%", Font = new CellFont("Courier New", 9, Italic: true) });
        ws.Merge(new CellRange(new CellAddress(10, 1), new CellAddress(10, 4)));
        ws["A10"].SetString("merged");
        ws.SetColumns([new ColumnProperties(1, 1, 18, 0, ColumnOptions.CustomWidth), new ColumnProperties(3, 4, 11.5, 0, ColumnOptions.CustomWidth | ColumnOptions.Hidden)]);
        ws.GetRow(3).Height = 30;
        ws.GetRow(4).Hidden = true;
        Worksheet s2 = wb.Sheets.Add("Second sheet");
        s2["A1"].Formula = "Data!B8*2";
        s2["A1"].SetNumber(60);
        s2["A2"].Formula = "SUM(Data!B2:B5)+Total_Qty";
        s2["A2"].SetNumber(60);
        wb.Names.Add("Total_Qty", "Data!$B$8");
        wb.Names.Add("Local", "'Second sheet'!$A$1", s2);
        ws.Validations.Add(new CellRange(new CellAddress(2, 2), new CellAddress(5, 2)), DataValidationSpec.WholeBetween(1, 100));
        ws.Validations.Add(new CellRange(new CellAddress(2, 1), new CellAddress(5, 1)), DataValidationSpec.List("a", "b", "c"));
        int dxf = wb.Styles.AddDifferentialFormat(new DifferentialFormat(Font: new CellFont(Bold: true, Color: StyleColor.FromRgb(0x9C0006)), Fill: CellFill.Solid(StyleColor.FromRgb(0xFFC7CE))));
        var range = new CellRange(new CellAddress(2, 2), new CellAddress(5, 2));
        ws.ConditionalFormats.Add(range, ConditionalRuleSpec.CellIs("greaterThan", "6", dxf));
        ws.ConditionalFormats.Add(range, ConditionalRuleSpec.Expression("MOD(B2,2)=0", dxf));
        ws.ConditionalFormats.Add(new CellRange(new CellAddress(2, 3), new CellAddress(5, 3)), ConditionalRuleSpec.ThreeColorScale(StyleColor.FromRgb(0xF8696B), StyleColor.FromRgb(0xFFEB84), StyleColor.FromRgb(0x63BE7B)));
        ws.ConditionalFormats.Add(new CellRange(new CellAddress(2, 4), new CellAddress(5, 4)), ConditionalRuleSpec.DataBars(StyleColor.FromRgb(0x638EC6)));
        ws.ConditionalFormats.Add(new CellRange(new CellAddress(2, 2), new CellAddress(5, 2)), ConditionalRuleSpec.Icons("3TrafficLights1"));
        ws.ConditionalFormats.Add(new CellRange(new CellAddress(2, 2), new CellAddress(5, 2)), ConditionalRuleSpec.Top(2, dxf));
        return wb;
    }

    [Fact]
    public void AGeneratedWorkbookSurvivesAWriteAndRead()
    {
        using Workbook wb = Sample();
        byte[] xls = Save(wb, out LossReport loss);
        using Workbook back = Reopen(xls);
        List<string> diff = Differences(wb, back);
        Assert.True(diff.Count == 0, string.Join("\n", diff) + "\nloss: " + string.Join(", ", loss.Items.Select(i => i.Code + "=" + i.Count)));
    }

    [Theory]
    [InlineData("basic")]
    [InlineData("formulas")]
    [InlineData("features")]
    [InlineData("styles")]
    public void WorkbooksReadFromLegacyFilesSurviveAWriteAndRead(string name)
    {
        using Workbook original = XlsConverter.ToWorkbook(ByteSource.FromMemory(File.ReadAllBytes(Path.Combine(XlsOracleTests.Dir, name + ".xls"))), new OpenContext());
        byte[] xls = Save(original, out LossReport loss);
        using Workbook back = Reopen(xls);
        List<string> diff = Differences(original, back);
        Assert.True(diff.Count == 0, string.Join("\n", diff.Take(30)) + "\nloss: " + string.Join(", ", loss.Items.Select(i => i.Code + "=" + i.Count)));
    }
}
