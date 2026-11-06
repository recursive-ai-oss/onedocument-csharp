using System.Globalization;
using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
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

            string la = LinksKey(a, x);
            string lb = LinksKey(b, y);
            if (la != lb)
            {
                d.Add("hyperlinks " + la + " vs " + lb);
            }

            string ka = CommentsKey(a, x);
            string kb = CommentsKey(b, y);
            if (ka != kb)
            {
                d.Add("comments " + ka + " vs " + kb);
            }

            string cfa = CfKey(a, x);
            string cfb = CfKey(b, y);
            if (cfa != cfb)
            {
                d.Add("conditional formats " + cfa + " vs " + cfb);
            }
        }

        var na = a.Names.Select(n => n.Name + "@" + n.LocalSheetId + "=" + n.Formula).ToHashSet();
        var nb = b.Names.Select(n => n.Name + "@" + n.LocalSheetId + "=" + n.Formula).ToHashSet();
        if (!na.SetEquals(nb))
        {
            d.Add("names only before: " + string.Join(";", na.Except(nb).Order()) + " only after: " + string.Join(";", nb.Except(na).Order()));
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

    private static string LinksKey(Workbook wb, Worksheet ws)
    {
        var sb = new StringBuilder();
        var rels = ws.PartName is null ? [] : wb.Package.GetRelationships(ws.PartName);
        foreach (MarkupNode root in ws.LayoutMarkup.ChildElements)
        {
            if (root.LocalNameString != "hyperlinks")
            {
                continue;
            }

            foreach (MarkupNode h in root.ChildElements)
            {
                string? rid = h.GetAttribute(wb.RelationshipNamespace, "id");
                sb.Append(h.GetAttribute("ref")).Append('|').Append(h.GetAttribute("location")).Append('|').Append(rid is null ? null : rels.FirstOrDefault(r => r.Id == rid)?.Target).Append('|').Append(h.GetAttribute("display")).Append(';');
            }
        }

        return sb.ToString();
    }

    private static string CommentsKey(Workbook wb, Worksheet ws)
    {
        var sb = new StringBuilder();
        if (ws.PartName is null)
        {
            return string.Empty;
        }

        foreach (Relationship rel in wb.Package.GetRelationships(ws.PartName))
        {
            if (!rel.Type.EndsWith("/comments", StringComparison.Ordinal) || !OpcPackage.TryResolveTarget(ws.PartName, rel, out PartUri target) || wb.Package.FindPart(target.ToString()) is not { } part)
            {
                continue;
            }

            MarkupDocument doc = MarkupDocument.Parse(part.GetBytes(), new OpenContext(), part.Name);
            var authors = new List<string>();
            foreach (MarkupNode n in doc.Root.ChildElements)
            {
                if (n.LocalNameString == "authors")
                {
                    authors.AddRange(n.ChildElements.Select(c => c.InnerText));
                }
                else if (n.LocalNameString == "commentList")
                {
                    foreach (MarkupNode c in n.ChildElements)
                    {
                        sb.Append(c.GetAttribute("ref")).Append(':').Append(authors[int.Parse(c.GetAttribute("authorId") ?? "0", CultureInfo.InvariantCulture)]).Append(':').Append(c.InnerText).Append(';');
                    }
                }
            }
        }

        return sb.ToString();
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

    internal static Workbook Sample()
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

    [Fact]
    public void HyperlinksAndNotesSurviveAWriteAndRead()
    {
        using Workbook wb = Workbook.Create();
        Worksheet ws = wb.Sheets[0];
        ws["A1"].SetString("site");
        ws["A2"].SetString("file");
        ws["A3"].SetString("here");
        ws["B2"].SetString("noted");
        const string rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var rels = new List<Relationship>(wb.Package.GetRelationships(ws.PartName!))
        {
            new("rIdL1", rel + "/hyperlink", "https://example.com/a?b=1&c=2", RelationshipTargetMode.External),
            new("rIdL2", rel + "/hyperlink", "l/file.xlsx", RelationshipTargetMode.External),
            new("rIdC", rel + "/comments", "../comments1.xml"),
        };
        wb.Package.AddPart("/xl/comments1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.comments+xml", Encoding.UTF8.GetBytes(
            "<comments xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><authors><author>Ann</author><author>Bo é</author></authors><commentList>"
            + "<comment ref=\"B2\" authorId=\"1\"><text><t>first line&#10;second 中</t></text></comment><comment ref=\"A1\" authorId=\"0\"><text><r><t>rich </t></r><r><t>note</t></r></text></comment></commentList></comments>"));
        wb.Package.SetRelationships(ws.PartName!, rels);
        ws.InsertFrameElement("hyperlinks", "<hyperlinks xmlns:r=\"" + rel + "\"><hyperlink ref=\"A1\" r:id=\"rIdL1\" display=\"Example\"/><hyperlink ref=\"A2:B2\" r:id=\"rIdL2\"/><hyperlink ref=\"A3\" location=\"'Sheet1'!C3\" display=\"go\"/></hyperlinks>");
        byte[] xls = Save(wb, out LossReport loss);
        using Workbook back = Reopen(xls);
        List<string> diff = Differences(wb, back);
        Assert.True(diff.Count == 0, string.Join("\n", diff) + "\nloss: " + string.Join(", ", loss.Items.Select(i => i.Code + "=" + i.Count)));
        Assert.Contains("Bo é:first line", CommentsKey(back, back.Sheets[0]));
        Assert.Contains("https://example.com/a?b=1&c=2", LinksKey(back, back.Sheets[0]));
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

/// <summary>
/// Reads every workbook of a folder, writes it as BIFF8 and reads that back (information, not a gate). Set <c>ONEDOCUMENT_XLS_CORPUS</c> to the
/// folder and <c>ONEDOCUMENT_XLS_OUT</c> to where the written files and <c>summary.txt</c> go; without them the test does nothing.
/// </summary>
public class XlsWriterCorpusTests
{
    [Fact]
    public void ACorpusFolderIsWrittenAndReadBack()
    {
        string? corpus = Environment.GetEnvironmentVariable("ONEDOCUMENT_XLS_CORPUS");
        string? output = Environment.GetEnvironmentVariable("ONEDOCUMENT_XLS_OUT");
        if (string.IsNullOrEmpty(corpus) || string.IsNullOrEmpty(output))
        {
            return;
        }

        Directory.CreateDirectory(output);
        int limit = int.TryParse(Environment.GetEnvironmentVariable("ONEDOCUMENT_XLS_LIMIT"), out int l) ? l : int.MaxValue;
        int read = 0;
        int unreadable = 0;
        int written = 0;
        int writeFailed = 0;
        int equal = 0;
        int different = 0;
        var summary = new StringBuilder();
        var codes = new Dictionary<string, long>();
        var messages = new Dictionary<string, string>();
        var importLoss = new Dictionary<string, long>();
        var ruleTypes = new Dictionary<string, long>();
        string[] only = (Environment.GetEnvironmentVariable("ONEDOCUMENT_XLS_ONLY") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (string path in Directory.EnumerateFiles(corpus, "*.xls").Order().Where(p => only.Length == 0 || only.Contains(Path.GetFileNameWithoutExtension(p))).Take(limit))
        {
            Workbook? wb = null;
            try
            {
                wb = XlsConverter.ToWorkbook(ByteSource.FromMemory(File.ReadAllBytes(path)), new OpenContext());
                read++;
            }
            catch (OfficeException)
            {
                unreadable++;
                continue;
            }

            using (wb)
            {
                foreach (LossItem i in wb.ImportLoss!.Items)
                {
                    importLoss[i.Code] = importLoss.GetValueOrDefault(i.Code) + i.Count;
                }

                foreach (Worksheet ws in wb.Sheets)
                {
                    foreach (ConditionalFormat g in ws.ConditionalFormats)
                    {
                        foreach (ConditionalFormatRule r in g.Rules)
                        {
                            ruleTypes[r.Type.ToString()] = ruleTypes.GetValueOrDefault(r.Type.ToString()) + 1;
                        }
                    }
                }

                try
                {
                    byte[] xls = XlsWriterTests.Save(wb, out LossReport loss);
                    foreach (LossItem i in loss.Items)
                    {
                        codes[i.Code] = codes.GetValueOrDefault(i.Code) + i.Count;
                        messages.TryAdd(i.Code + " / " + Path.GetFileName(path), i.Message);
                    }

                    written++;
                    File.WriteAllBytes(Path.Combine(output, Path.GetFileName(path)), xls);
                    using Workbook back = XlsWriterTests.Reopen(xls);
                    List<string> diff = XlsWriterTests.Differences(wb, back);
                    if (diff.Count == 0)
                    {
                        equal++;
                    }
                    else
                    {
                        different++;
                        summary.Append(Path.GetFileName(path)).Append(": ").Append(diff.Count).Append(" differences, first: ").AppendLine(diff[0].Length > 900 ? diff[0][..900] : diff[0]);
                    }
                }
                catch (Exception ex)
                {
                    writeFailed++;
                    summary.Append(Path.GetFileName(path)).Append(": write failed ").Append(ex.GetType().Name).Append(' ').AppendLine(ex.Message.Length > 200 ? ex.Message[..200] : ex.Message);
                }
            }
        }

        summary.Insert(0, $"read {read}, unreadable {unreadable}, written {written}, write failed {writeFailed}, read back equal {equal}, different {different}\nloss: {string.Join(", ", codes.Select(c => c.Key + "=" + c.Value))}\n");
        summary.AppendLine("import loss: " + string.Join(", ", importLoss.Select(c => c.Key + "=" + c.Value)));
        summary.AppendLine("rule types read: " + string.Join(", ", ruleTypes.Select(c => c.Key + "=" + c.Value)));
        foreach (var group in messages.GroupBy(m => m.Key[..m.Key.IndexOf(" / ", StringComparison.Ordinal)]))
        {
            summary.Append("loss message ").Append(group.Key).Append(": ").AppendLine(group.First().Value);
        }

        File.WriteAllText(Path.Combine(output, "summary.txt"), summary.ToString());
    }

    /// <summary>Writes the files that go to Excel unanswered: set <c>ONEDOCUMENT_XLS_CASES</c> to the folder (see <c>testdata/xls/excel-cases.md</c>).</summary>
    [Fact]
    public void TheExcelCasesAreWrittenWhenAskedFor()
    {
        string? dir = Environment.GetEnvironmentVariable("ONEDOCUMENT_XLS_CASES");
        if (string.IsNullOrEmpty(dir))
        {
            return;
        }

        Directory.CreateDirectory(dir);
        using (Workbook wb = XlsWriterTests.Sample())
        {
            File.WriteAllBytes(Path.Combine(dir, "generated.xls"), XlsWriterTests.Save(wb, out _));
        }

        foreach (string name in new[] { "basic", "formulas", "styles", "features" })
        {
            using Workbook wb = XlsConverter.ToWorkbook(ByteSource.FromMemory(File.ReadAllBytes(Path.Combine(XlsOracleTests.Dir, name + ".xls"))), new OpenContext());
            File.WriteAllBytes(Path.Combine(dir, name + "-rewritten.xls"), XlsWriterTests.Save(wb, out _));
        }
    }
}
