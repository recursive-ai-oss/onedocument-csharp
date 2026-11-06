using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Spreadsheets.Tests;

/// <summary>
/// Round trips of the workbooks in <c>testdata</c> through OpenDocument. The two methods are driven by the environment so that the
/// reference reader (a spreadsheet application, run as a binary) can sit between them: set <c>ONEDOCUMENT_ODS_WORK</c> to a directory, run
/// <c>Export</c>, let the reference reader convert <c>ours/*.ods</c> to <c>lo/*.xlsx</c> (<c>tools/ods/check_with_reference.py</c>), then run <c>Compare</c>.
/// </summary>
public class OdsOracleTests
{
    static OdsOracleTests() => SpreadsheetFormats.Register(FormatRegistry.Default);

    private static string TestData([CallerFilePath] string here = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "../../testdata"));

    private static string? Work => Environment.GetEnvironmentVariable("ONEDOCUMENT_ODS_WORK");

    private static IEnumerable<string> Inputs()
    {
        string? extra = Environment.GetEnvironmentVariable("ONEDOCUMENT_ODS_INPUTS");
        var dirs = new List<string> { TestData() };
        if (extra is not null)
        {
            dirs.AddRange(extra.Split(':', StringSplitOptions.RemoveEmptyEntries));
        }

        foreach (string d in dirs)
        {
            foreach (string f in Directory.EnumerateFiles(d, "*.xlsx", SearchOption.AllDirectories).OrderBy(static x => x, StringComparer.Ordinal))
            {
                // Encrypted fixtures and the large generated ones are not part of the round trip.
                if (f.Contains("/encryption/", StringComparison.Ordinal) || new FileInfo(f).Length > 4_000_000)
                {
                    continue;
                }

                yield return f;
            }
        }
    }

    private static string Name(string path) =>
        Path.GetFileNameWithoutExtension(path) + "-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(path.Replace(TestData(), string.Empty, StringComparison.Ordinal))))[..6].ToLowerInvariant();

    [Fact]
    public void Export()
    {
        if (Work is not { } work)
        {
            return;
        }

        Directory.CreateDirectory(Path.Combine(work, "ours"));
        var report = new StringBuilder();
        int ok = 0;
        foreach (string input in Inputs())
        {
            string name = Name(input);
            try
            {
                var ctx = new OpenContext();
                using Workbook wb = Workbook.Open(input, ctx);
                wb.SaveAs(Path.Combine(work, "ours", name + ".ods"));
                string lossText = string.Join(";", (wb.ExportLoss?.Items ?? []).Select(static l => l.Code + "x" + l.Count.ToString(CultureInfo.InvariantCulture)));
                wb.SaveAs(Path.Combine(work, "ours", name + ".fods"));
                File.Copy(input, Path.Combine(work, "ours", name + ".src.xlsx"), overwrite: true);
                report.Append("ok\t").Append(name).Append('\t').Append(input.Replace(TestData(), string.Empty, StringComparison.Ordinal)).Append('\t').AppendLine(lossText);
                ok++;
            }
            catch (Exception ex) when (ex is OfficeException or InvalidOperationException)
            {
                report.Append("fail\t").Append(name).Append('\t').Append(input.Replace(TestData(), string.Empty, StringComparison.Ordinal)).Append('\t').AppendLine(ex.GetType().Name + ": " + ex.Message);
            }
        }

        File.WriteAllText(Path.Combine(work, "export.tsv"), report.ToString());
        Assert.True(ok > 0);
    }

    [Fact]
    public void Compare()
    {
        if (Work is not { } work || !Directory.Exists(Path.Combine(work, "ours")))
        {
            return;
        }

        var report = new StringBuilder();
        int files = 0;
        int sourceDiffs = 0;
        int referenceDiffs = 0;
        int referenceFiles = 0;
        long uncached = 0;
        foreach (string ods in Directory.EnumerateFiles(Path.Combine(work, "ours"), "*.ods").OrderBy(static x => x, StringComparer.Ordinal))
        {
            string name = Path.GetFileNameWithoutExtension(ods);
            string source = Path.Combine(work, "ours", name + ".src.xlsx");
            files++;
            try
            {
                using Workbook original = Workbook.Open(source);
                using Workbook back = Workbook.Open(ods);
                List<string> a = Fingerprint(original);
                List<string> b = Fingerprint(back);
                List<string> only = Diff(a, b, "src>", "ods>");
                sourceDiffs += only.Count;
                foreach (string line in only.Take(40))
                {
                    report.Append("SRC\t").Append(name).Append('\t').AppendLine(line);
                }

                string reference = Path.Combine(work, "lo", name + ".xlsx");
                if (File.Exists(reference))
                {
                    referenceFiles++;
                    using Workbook lo = Workbook.Open(reference);
                    List<string> c = Fingerprint(lo, valuesOnly: true);
                    List<string> d = Fingerprint(back, valuesOnly: true, skipUncached: true);
                    uncached += CountUncached(back);
                    List<string> diff = Diff(d, c, "ours>", "ref>");
                    referenceDiffs += diff.Count;
                    foreach (string line in diff.Take(40))
                    {
                        report.Append("REF\t").Append(name).Append('\t').AppendLine(line);
                    }
                }
            }
            catch (Exception ex) when (ex is OfficeException or InvalidOperationException or ArgumentException)
            {
                report.Append("ERR\t").Append(name).Append('\t').AppendLine(ex.GetType().Name + ": " + ex.Message);
                sourceDiffs++;
            }
        }

        report.Append("files\t").Append(files).Append("\treference\t").Append(referenceFiles).Append("\tsource-diffs\t").Append(sourceDiffs).Append("\treference-diffs\t").Append(referenceDiffs).Append("\tformulas-without-cached-value\t").AppendLine(uncached.ToString(CultureInfo.InvariantCulture));
        File.WriteAllText(Path.Combine(work, "compare.tsv"), report.ToString());
    }

    private static List<string> Diff(List<string> a, List<string> b, string aLabel, string bLabel)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string s in a)
        {
            counts[s] = counts.GetValueOrDefault(s) + 1;
        }

        var result = new List<string>();
        foreach (string s in b)
        {
            if (counts.TryGetValue(s, out int n) && n > 0)
            {
                counts[s] = n - 1;
            }
            else
            {
                result.Add(bLabel + s);
            }
        }

        foreach (KeyValuePair<string, int> kv in counts)
        {
            for (int i = 0; i < kv.Value; i++)
            {
                result.Add(aLabel + kv.Key);
            }
        }

        result.Sort(StringComparer.Ordinal);
        return result;
    }

    private static string Num(double v) => Math.Round(v, 9, MidpointRounding.AwayFromZero).ToString("G9", CultureInfo.InvariantCulture);

    // One line per observable fact of a workbook, so two workbooks can be compared as bags of lines.
    private static long CountUncached(Workbook wb)
    {
        long n = 0;
        foreach (Worksheet ws in wb.Sheets)
        {
            foreach (CellRowView row in ws.Store.EnumerateRows())
            {
                for (int i = 0; i < row.Count; i++)
                {
                    if (row.HasFormulaAt(i) && row.ValueAt(i).Kind == CellValueKind.Empty)
                    {
                        n++;
                    }
                }
            }
        }

        return n;
    }

    private static List<string> Fingerprint(Workbook wb, bool valuesOnly = false, bool skipUncached = false)
    {
        var lines = new List<string>();
        foreach (Worksheet ws in wb.Sheets)
        {
            if (ws.Kind != SheetKind.Worksheet)
            {
                continue;
            }

            string s = ws.Name;
            lines.Add("sheet " + s + " " + ws.State);
            var shared = new Dictionary<int, (string Text, int Row, int Col)>();
            foreach (CellRowView row in ws.Store.EnumerateRows())
            {
                for (int i = 0; i < row.Count; i++)
                {
                    if (row.FormulaAt(i) is { Kind: CellFormulaKind.Shared, Text.Length: > 0, SharedIndex: >= 0 } f)
                    {
                        shared[f.SharedIndex] = (f.Text, row.Row, row.ColumnAt(i));
                    }
                }
            }

            foreach (CellRowView row in ws.Store.EnumerateRows())
            {
                for (int i = 0; i < row.Count; i++)
                {
                    CellValue v = row.ValueAt(i);
                    int col = row.ColumnAt(i);
                    CellFormula? f = row.FormulaAt(i);
                    string formula = string.Empty;
                    if (f is not null)
                    {
                        string text = f.Text;
                        int dr = 0;
                        int dc = 0;
                        if (f.Kind == CellFormulaKind.Shared && text.Length == 0 && shared.TryGetValue(f.SharedIndex, out (string Text, int Row, int Col) a))
                        {
                            text = a.Text;
                            dr = row.Row - a.Row;
                            dc = col - a.Col;
                        }

                        formula = Normal(text, dr, dc);
                    }

                    string value = v.Kind switch
                    {
                        CellValueKind.Number => "N" + Num(v.Number),
                        CellValueKind.Text => "S" + ws.GetText(v),
                        CellValueKind.Boolean => "B" + (v.Boolean ? "1" : "0"),
                        CellValueKind.Error => "E" + ws.GetText(v),
                        _ => "-",
                    };
                    if (value == "-" && formula.Length == 0 && (valuesOnly || row.StyleAt(i) == 0))
                    {
                        continue;
                    }

                    if (skipUncached && value == "-")
                    {
                        continue;
                    }

                    string address = CellAddress.ColumnName(col) + row.Row.ToString(CultureInfo.InvariantCulture);
                    var sb = new StringBuilder("cell ").Append(s).Append('!').Append(address).Append(' ').Append(value);
                    if (formula.Length > 0)
                    {
                        sb.Append(" f=").Append(formula);
                    }

                    if (!valuesOnly)
                    {
                        sb.Append(" fmt=").Append(NormalFormat(wb.Styles.Resolve(row.StyleAt(i)).NumberFormatCode));
                    }

                    lines.Add(sb.ToString());
                }
            }

            if (valuesOnly)
            {
                continue;
            }

            foreach (CellRange m in ws.MergedCells)
            {
                lines.Add("merge " + s + " " + m.TopLeft + ":" + m.BottomRight);
            }

            foreach (DataValidation v in ws.Validations)
            {
                lines.Add("dv " + s + " " + v.Type + " " + v.Operator + " " + v.Formula1?.TrimStart('=') + "|" + v.Formula2?.TrimStart('=') + " " + string.Join(",", v.Ranges.Select(static r => r.TopLeft + ":" + r.BottomRight)) + " blank=" + v.AllowBlank);
            }

            foreach (ConditionalFormat c in ws.ConditionalFormats)
            {
                foreach (ConditionalFormatRule r in c.Rules)
                {
                    lines.Add("cf " + s + " " + r.Type + " " + r.Operator + " " + string.Join("|", r.Formulas) + " " + string.Join(",", c.Ranges.Select(static x => x.TopLeft + ":" + x.BottomRight)));
                }
            }

            foreach (ColumnProperties col in ws.Columns)
            {
                if ((col.Flags & ColumnOptions.Hidden) != 0)
                {
                    lines.Add("hiddencol " + s + " " + col.Min + "-" + col.Max);
                }
            }

            foreach (CellRowView row in ws.Store.EnumerateRows())
            {
                if (ws.GetRow(row.Row).Hidden)
                {
                    lines.Add("hiddenrow " + s + " " + row.Row);
                }
            }

            foreach (string c in Comments(wb, ws))
            {
                lines.Add("comment " + s + " " + c);
            }
        }

        foreach (DefinedName n in wb.Names)
        {
            if (!n.IsBuiltIn || n.BuiltIn is BuiltInName.PrintArea or BuiltInName.PrintTitles)
            {
                lines.Add("name " + n.Name + " scope=" + n.LocalSheetId + " " + Normal(n.Formula, 0, 0));
            }
        }

        return lines;
    }

    private static string Normal(string text, int dr, int dc)
    {
        try
        {
            FormulaNode node = FormulaParser.Parse(text);
            if (dr != 0 || dc != 0)
            {
                node = FormulaTranslator.Translate(node, dr, dc);
            }

            string printed = FormulaPrinter.Print(node, new FormulaPrintOptions { Form = FormulaForm.Display });
            // Quotes that a name does not need, and a one-cell range written as a range, are spellings of the same reference.
            printed = System.Text.RegularExpressions.Regex.Replace(printed, "'([A-Za-z_][A-Za-z0-9_]*)'!", "$1!", System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            return System.Text.RegularExpressions.Regex.Replace(printed, @"(\$?[A-Z]+\$?\d+):\1\b", "$1", System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        }
        catch (OfficeException)
        {
            return text;
        }
    }

    // Number-format codes compared as the parsed section sources, so that spelling variants of one code do not count.
    private static string NormalFormat(string code)
    {
        if (code == "General")
        {
            return code;
        }

        code = code.Replace("_)", " ", StringComparison.Ordinal);
        return NumberFormats.NumberFormat.TryParse(code, out NumberFormats.NumberFormat? f, out _) ? string.Join(";", f.Sections.Select(static s => s.Source.Replace("\"", string.Empty, StringComparison.Ordinal).Replace("\\", string.Empty, StringComparison.Ordinal))) : code;
    }

    private static IEnumerable<string> Comments(Workbook wb, Worksheet ws)
    {
        if (ws.PartName is null)
        {
            yield break;
        }

        foreach (Relationship rel in wb.Package.GetRelationships(ws.PartName))
        {
            if (!rel.Type.EndsWith("/comments", StringComparison.Ordinal) || !OpcPackage.TryResolveTarget(ws.PartName, rel, out PartUri target) || wb.Package.FindPart(target.ToString()) is not { } part)
            {
                continue;
            }

            MarkupDocument doc = MarkupDocument.Parse(part.GetBytes(), wb.OpenContext, part.Name);
            foreach (MarkupNode n in doc.Root.ChildElements)
            {
                if (n.LocalNameString == "commentList")
                {
                    foreach (MarkupNode c in n.ChildElements)
                    {
                        yield return c.GetAttribute("ref") + " " + c.InnerText.Trim();
                    }
                }
            }
        }
    }
}
