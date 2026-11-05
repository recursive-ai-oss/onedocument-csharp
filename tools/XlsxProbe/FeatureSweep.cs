using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using RecursiveAI.OneOffice;
using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.Spreadsheets;

namespace RecursiveAI.OneOffice.Tools.XlsxProbe;

/// <summary>
/// <c>XlsxProbe features DIR OUT [--max N] [--shard I/N]</c>: for each workbook, counts what the style, table, conditional formatting,
/// validation and name models read (the census), checks that reading all of it leaves an untouched save byte-identical, then makes one
/// edit per feature class on a fresh copy and writes it to <c>OUT/ed-CLASS/ID.xlsx</c> for the schema and LibreOffice checks.
/// </summary>
internal static class FeatureSweep
{
    private static readonly string[] Classes = ["style", "table", "cf", "dv", "name"];

    public static int Run(string[] args)
    {
        string dir = args[0];
        string outDir = args[1];
        int max = int.MaxValue;
        int shard = 0, shards = 1;
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "--max") max = int.Parse(args[++i]);
            else if (args[i] == "--shard")
            {
                string[] p = args[++i].Split('/');
                shard = int.Parse(p[0]);
                shards = int.Parse(p[1]);
            }
        }

        Directory.CreateDirectory(outDir);
        string[] files = [.. Directory.EnumerateFiles(dir, "*.xlsx", SearchOption.AllDirectories).Order(StringComparer.Ordinal)];
        var lines = new List<string>();
        var statuses = new Dictionary<string, int>();
        int n = 0;
        for (int fi = shard; fi < files.Length && n < max; fi += shards)
        {
            n++;
            Dictionary<string, object?> r = CheckFile(files[fi], dir, outDir);
            lines.Add(JsonSerializer.Serialize(r));
            string key = "status:" + r["status"];
            statuses[key] = statuses.GetValueOrDefault(key) + 1;
        }

        File.WriteAllLines(Path.Combine(outDir, $"sweep-{shard}.jsonl"), lines);
        Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object?> { ["kind"] = "summary", ["shard"] = shard, ["files"] = n, ["statuses"] = statuses }));
        return 0;
    }

    private static Dictionary<string, object?> CheckFile(string path, string root, string outDir)
    {
        string rel = Path.GetRelativePath(root, path);
        string id = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rel)))[..16];
        var r = new Dictionary<string, object?> { ["kind"] = "file", ["file"] = rel, ["id"] = id };
        var ctx = new OpenContext();
        try
        {
            using Workbook wb = Workbook.Open(path, ctx);
            Census(wb, r);

            string untouched = Path.Combine(outDir, "untouched", id + ".xlsx");
            Directory.CreateDirectory(Path.GetDirectoryName(untouched)!);
            using (var fs = File.Create(untouched))
            {
                wb.SaveAs(fs, wb.Format);
            }

            (int same, int diff, string? first) = Program.ComparePackages(path, untouched);
            File.Delete(untouched);
            r["untouchedParts"] = same + diff;
            r["untouchedDiff"] = diff;
            if (first is not null) r["untouchedFirstDiff"] = first;

            var edits = new Dictionary<string, object?>();
            foreach (string cls in Classes)
            {
                edits[cls] = Edit(path, id, outDir, cls);
            }

            r["edits"] = edits;
            r["defects"] = ((DefectCollection)ctx.Defects).Count;
            var codes = new Dictionary<string, int>();
            foreach (Defect d in (DefectCollection)ctx.Defects)
            {
                codes[d.Code] = codes.GetValueOrDefault(d.Code) + 1;
            }

            r["defectCodes"] = codes;
            bool editFailed = edits.Values.Any(v => v is string s && s.StartsWith("FAIL", StringComparison.Ordinal));
            r["status"] = diff != 0 ? "untouched-diff" : editFailed ? "edit-failed" : "ok";
        }
        catch (OfficeException ex)
        {
            r["status"] = "typed-error";
            r["errorClass"] = ex.GetType().Name;
            r["errorCode"] = ex.Defect.Code;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            r["status"] = "UNTYPED-EXCEPTION";
            r["errorClass"] = ex.GetType().Name;
            r["error"] = ex.Message;
            r["stack"] = ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim();
        }

        return r;
    }

    private static void Census(Workbook wb, Dictionary<string, object?> r)
    {
        StyleSheet st = wb.Styles;
        r["xfs"] = st.CellFormatCount;
        r["fonts"] = st.FontCount;
        r["fills"] = st.FillCount;
        r["borders"] = st.BorderCount;
        r["dxfs"] = st.DifferentialFormatCount;
        r["numFmts"] = st.NumberFormats.Count;
        r["cellStyles"] = st.CellStyles.Count;
        r["tableStyles"] = st.TableStyles.Count;
        int resolveFail = 0;
        for (int i = 0; i < st.CellFormatCount; i++)
        {
            try
            {
                _ = st.Resolve(i);
                _ = st.GetNumberFormat(i);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                resolveFail++;
            }
        }

        r["resolveFailures"] = resolveFail;
        int tables = 0, tableCols = 0, cfGroups = 0, cfExt = 0, dv = 0, dvExt = 0, formatted = 0;
        var cfTypes = new Dictionary<string, int>();
        var dvTypes = new Dictionary<string, int>();
        foreach (Worksheet s in wb.Sheets)
        {
            if (s.Kind != SheetKind.Worksheet)
            {
                continue;
            }

            foreach (Table t in s.Tables)
            {
                tables++;
                tableCols += t.Columns.Count;
            }

            foreach (ConditionalFormat f in s.ConditionalFormats)
            {
                cfGroups++;
                if (f.IsExtended) cfExt++;
                foreach (ConditionalFormatRule rule in f.Rules)
                {
                    string k = rule.TypeName.Length == 0 ? "(none)" : rule.TypeName;
                    cfTypes[k] = cfTypes.GetValueOrDefault(k) + 1;
                    _ = rule.Formulas;
                    _ = rule.ColorScale;
                    _ = rule.DataBar;
                    _ = rule.IconSet;
                }

                _ = f.Ranges;
            }

            foreach (DataValidation v in s.Validations)
            {
                dv++;
                if (v.IsExtended) dvExt++;
                string k = v.Type.ToString();
                dvTypes[k] = dvTypes.GetValueOrDefault(k) + 1;
                _ = v.Formula1;
                _ = v.Ranges;
            }

            int budget = 400;
            foreach (CellRowView row in s.Store.EnumerateRows())
            {
                for (int i = 0; i < row.Count && budget > 0; i++)
                {
                    if (row.ValueAt(i).Kind == CellValueKind.Number)
                    {
                        budget--;
                        _ = s[row.Row, row.ColumnAt(i)].FormattedText;
                        formatted++;
                    }
                }

                if (budget <= 0) break;
            }
        }

        r["tables"] = tables;
        r["tableColumns"] = tableCols;
        r["cfGroups"] = cfGroups;
        r["cfExtended"] = cfExt;
        r["cfTypes"] = cfTypes;
        r["dv"] = dv;
        r["dvExtended"] = dvExt;
        r["dvTypes"] = dvTypes;
        r["formattedNumbers"] = formatted;
        int names = 0, builtin = 0, hidden = 0;
        foreach (DefinedName n in wb.Names)
        {
            names++;
            if (n.IsBuiltIn) builtin++;
            if (n.IsHidden) hidden++;
        }

        r["names"] = names;
        r["builtinNames"] = builtin;
        r["hiddenNames"] = hidden;
    }

    private static string Edit(string path, string id, string outDir, string cls)
    {
        try
        {
            using Workbook wb = Workbook.Open(path, new OpenContext());
            Worksheet? s = wb.Sheets.FirstOrDefault(x => x.Kind == SheetKind.Worksheet && x.PartName is not null);
            if (s is null)
            {
                return "skip: no worksheet";
            }

            string note = "ok";
            switch (cls)
            {
                case "style":
                    s["A1"].UpdateStyle(x => x with { NumberFormatCode = "0.0000", Font = (x.Font ?? new CellFont()) with { Bold = true, Italic = true, Color = StyleColor.FromRgb(0x336699) }, Fill = CellFill.Solid(StyleColor.FromTheme(4, 0.4)) });
                    if (!s["A1"].Style.Font.Bold) return "FAIL: style not applied";
                    break;
                case "table":
                    if (s.Tables.Count > 0)
                    {
                        Table t = s.Tables.First();
                        CellRange rg = t.Range;
                        if (rg.BottomRight.Row + 1 > CellAddress.MaxRows) return "skip: table at last row";
                        try
                        {
                            t.Resize(new CellRange(rg.TopLeft, new CellAddress(rg.BottomRight.Row + 1, rg.BottomRight.Column)));
                            note = "resized";
                        }
                        catch (ArgumentException ex)
                        {
                            return "skip: " + ex.Message;
                        }
                    }
                    else
                    {
                        string name = "OneOfficeTable";
                        for (int i = 2; wb.Names.Find(name) is not null || s.Tables[name] is not null; i++) name = "OneOfficeTable" + i;
                        s.Tables.Add(name, CellRange.Parse("XFA1:XFC4"));
                        note = "added";
                    }

                    break;
                case "cf":
                    int dxf = wb.Styles.AddDifferentialFormat(new DifferentialFormat(new CellFont(Bold: true, Color: StyleColor.FromRgb(0x9C0006)), Fill: new CellFill(FillPattern.Solid, Background: StyleColor.FromRgb(0xFFC7CE))));
                    s.ConditionalFormats.Add(CellRange.Parse("A1:A5"), ConditionalRuleSpec.Expression("A1>0", dxf));
                    s.ConditionalFormats.Add(CellRange.Parse("B1:B5"), ConditionalRuleSpec.TwoColorScale(StyleColor.FromRgb(0xFFFFFF), StyleColor.FromRgb(0x63BE7B)));
                    break;
                case "dv":
                    s.Validations.Add(CellRange.Parse("A1:A5"), DataValidationSpec.List("Yes", "No") with { Error = "Pick Yes or No", ErrorTitle = "Invalid" });
                    break;
                case "name":
                    wb.Names.Add("OneOfficeName", "'" + s.Name.Replace("'", "''", StringComparison.Ordinal) + "'!$A$1");
                    wb.Names.Add("OneOfficeLocal", "'" + s.Name.Replace("'", "''", StringComparison.Ordinal) + "'!$B$2:$C$3", s);
                    break;
            }

            string dir = Path.Combine(outDir, "ed-" + cls);
            Directory.CreateDirectory(dir);
            string target = Path.Combine(dir, id + ".xlsx");
            using (var fs = File.Create(target))
            {
                wb.SaveAs(fs, wb.Format);
            }

            // The edited file reopens and the edit is visible.
            using Workbook back = Workbook.Open(target, new OpenContext());
            Worksheet bs = back.Sheets.First(x => x.Kind == SheetKind.Worksheet && x.PartName is not null);
            bool seen = cls switch
            {
                "style" => bs["A1"].Style.Font.Bold,
                "table" => bs.Tables.Count > 0,
                "cf" => bs.ConditionalFormats.Count > 0,
                "dv" => bs.Validations.Count > 0,
                _ => back.Names.Find("OneOfficeName") is not null,
            };
            if (!seen)
            {
                return "FAIL: edit not visible after reopen";
            }

            string[] changed = Program.ChangedParts(path, target);
            return note + "; changed " + string.Join(',', changed.Select(c => c.Replace("/xl/", string.Empty, StringComparison.Ordinal)));
        }
        catch (OfficeException ex)
        {
            return "FAIL: typed " + ex.Defect.Code;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return "FAIL: " + ex.GetType().Name + ": " + ex.Message;
        }
    }
}
