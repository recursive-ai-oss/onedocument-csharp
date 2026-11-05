using System.Linq;
using RecursiveAI.OneDocument.Calc;

namespace RecursiveAI.OneDocument.Spreadsheets.Templating;

// Moves the references inside formulas to where the cells went. References are rewritten on the formula syntax tree, never as text.
internal sealed class Rewriter(Dictionary<string, SheetPlan> plans, Workbook workbook)
{
    public string RewriteText(string text, string ownerSheetOld, OwnerContext? ctx, string? ownerSheetFinal)
    {
        _ = ownerSheetFinal;
        if (text.Length == 0 || !FormulaParser.TryParse(text, out FormulaNode? node, out _, new FormulaParserOptions { Limits = workbook.Limits }) || node is null)
        {
            return text;
        }

        bool changed = false;
        FormulaNode result = FormulaRewriter.Rewrite(node, n =>
        {
            FormulaNode? r = Map(n, ownerSheetOld, ctx);
            changed |= r is not null;
            return r;
        });
        return changed ? FormulaPrinter.Print(result) : text;
    }

    public void RewriteUntouched(List<SheetPlan> done)
    {
        var skip = new HashSet<Worksheet>(done.Select(static p => p.Sheet));
        foreach (Worksheet ws in workbook.Sheets.ToList())
        {
            if (ws.Kind != SheetKind.Worksheet || skip.Contains(ws) || done.Any(p => p.Output.Count > 1 && p.Output.Skip(1).Any(o => o.FinalName == ws.Name)))
            {
                continue;
            }

            CellStore store = ws.Store;
            var edits = new List<(int Row, int Col, CellFormula F)>();
            foreach (RowData r in store.Rows)
            {
                if (r.Formulas is null)
                {
                    continue;
                }

                for (int i = 0; i < r.Count; i++)
                {
                    if (r.Formulas[i] != 0 && store.FormulaAt(r.Formulas[i]) is { Text.Length: > 0 } f)
                    {
                        string t = RewriteText(f.Text, ws.Name, null, null);
                        if (t != f.Text)
                        {
                            edits.Add((r.Row, r.Cols[i] + 1, f with { Text = t }));
                        }
                    }
                }
            }

            foreach ((int row, int col, CellFormula f) in edits)
            {
                ws[row, col].FormulaDetail = f;
            }
        }
    }

    public void RewriteNames(Workbook wb)
    {
        foreach (DefinedName name in wb.Names.ToList())
        {
            string text = name.Formula;
            string t = RewriteText(text, string.Empty, null, null);
            if (t != text)
            {
                name.Formula = t;
            }
        }
    }

    private FormulaNode? Map(FormulaNode n, string ownerSheetOld, OwnerContext? ctx)
    {
        switch (n)
        {
            case CellReferenceNode c:
                {
                    if (!Target(c.Prefix, ownerSheetOld, ctx, out SheetPlan? plan, out OutSheet? ts, out SheetPrefix? prefix, out OwnerContext? same, out bool missing))
                    {
                        return missing ? new ErrorNode(CalcError.Ref, c.Prefix) : null;
                    }

                    int row = c.Row.Index;
                    int col = c.Column.Index;
                    if (plan!.RowMode)
                    {
                        row = Single(plan, ts!, row, same);
                    }
                    else
                    {
                        col = Single(plan, ts!, col, same);
                    }

                    if (row == 0 || col == 0)
                    {
                        return new ErrorNode(CalcError.Ref, prefix);
                    }

                    if (row == c.Row.Index && col == c.Column.Index && ReferenceEquals(prefix, c.Prefix))
                    {
                        return null;
                    }

                    return new CellReferenceNode(prefix, new RefCoord(row, c.Row.Absolute), new RefCoord(col, c.Column.Absolute));
                }

            case AreaReferenceNode a:
                {
                    if (!Target(a.Prefix, ownerSheetOld, ctx, out SheetPlan? plan, out OutSheet? ts, out SheetPrefix? prefix, out OwnerContext? same, out bool missing))
                    {
                        return missing ? new ErrorNode(CalcError.Ref, a.Prefix) : null;
                    }

                    int r1 = a.Row1.Index;
                    int r2 = a.Row2.Index;
                    int c1 = a.Column1.Index;
                    int c2 = a.Column2.Index;
                    bool onMajor = plan!.RowMode ? a.AreaKind != AreaKind.Columns : a.AreaKind != AreaKind.Rows;
                    if (onMajor)
                    {
                        int lo = plan.RowMode ? r1 : c1;
                        int hi = plan.RowMode ? r2 : c2;
                        int s;
                        int e;
                        if (same is not null && plan.SameBand(same.SourceMajor, lo) && plan.SameBand(same.SourceMajor, hi))
                        {
                            s = ts!.Map.Near(lo, same.SourceMajor, same.OutMajor);
                            e = ts.Map.Near(hi, same.SourceMajor, same.OutMajor);
                        }
                        else
                        {
                            s = ts!.Map.Start(lo);
                            e = ts.Map.End(hi);
                        }

                        if (s == 0 || e == 0 || e < s)
                        {
                            return new ErrorNode(CalcError.Ref, prefix);
                        }

                        if (plan.RowMode)
                        {
                            r1 = s;
                            r2 = e;
                        }
                        else
                        {
                            c1 = s;
                            c2 = e;
                        }
                    }

                    if (r1 == a.Row1.Index && r2 == a.Row2.Index && c1 == a.Column1.Index && c2 == a.Column2.Index && ReferenceEquals(prefix, a.Prefix))
                    {
                        return null;
                    }

                    return new AreaReferenceNode(prefix, a.AreaKind, new RefCoord(r1, a.Row1.Absolute), new RefCoord(c1, a.Column1.Absolute), new RefCoord(r2, a.Row2.Absolute), new RefCoord(c2, a.Column2.Absolute));
                }

            default:
                return null;
        }
    }

    private static int Single(SheetPlan plan, OutSheet ts, int index, OwnerContext? same) =>
        same is not null && plan.SameBand(same.SourceMajor, index) ? ts.Map.Near(index, same.SourceMajor, same.OutMajor) : ts.Map.Single(index);

    // Finds the sheet a reference points at after rendering. Another sheet's reference goes to the first output sheet; a reference inside
    // a rendered sheet goes to that same output sheet, so every copy of a sheet block refers to its own cells.
    private bool Target(SheetPrefix? prefix, string ownerSheetOld, OwnerContext? ctx, out SheetPlan? plan, out OutSheet? ts, out SheetPrefix? newPrefix, out OwnerContext? same, out bool missing)
    {
        plan = null;
        ts = null;
        newPrefix = prefix;
        same = null;
        missing = false;
        string? name = prefix is null ? ownerSheetOld : prefix.Workbook is null && !prefix.Is3D ? prefix.Sheet : null;
        if (name is null || !plans.TryGetValue(name, out plan))
        {
            return false;
        }

        ts = ctx is not null && ReferenceEquals(ctx.Plan, plan) ? ctx.Sheet : plan.Output.FirstOrDefault();
        if (ts is null)
        {
            missing = true;
            return false;
        }

        same = ctx is not null && ReferenceEquals(ctx.Sheet, ts) ? ctx : null;
        if (prefix is not null && !string.Equals(ts.FinalName, name, StringComparison.Ordinal))
        {
            newPrefix = new SheetPrefix(ts.FinalName);
        }

        return true;
    }
}
