using RecursiveAI.OneDocument.Spreadsheets.Pivots;

namespace RecursiveAI.OneDocument.Spreadsheets.Templating;

// A pivot cache reads its data from a range of a sheet. When the template moved that range, the cache follows it, and the cache is marked
// to be refreshed when the workbook is opened (ECMA-376 Part 1 §18.10.1.64, refreshOnLoad), since its stored records describe the template.
internal static class PivotSources
{
    public static void Rewrite(Workbook wb, Dictionary<string, SheetPlan> plans)
    {
        if (plans.Count == 0)
        {
            return;
        }

        foreach (PivotCache cache in wb.PivotCaches)
        {
            if (cache.WorksheetSource is not { IsNull: false } source)
            {
                continue;
            }

            bool touched = false;
            string? sheet = source.GetAttribute("sheet");
            string? reference = source.GetAttribute("ref");
            if (sheet is not null && reference is not null && plans.TryGetValue(sheet, out SheetPlan? plan) && plan.Output.Count > 0
                && CellRange.TryParse(reference, out CellRange range))
            {
                OutSheet first = plan.Output[0];
                int a = plan.RowMode ? range.TopLeft.Row : range.TopLeft.Column;
                int b = plan.RowMode ? range.BottomRight.Row : range.BottomRight.Column;
                int s = first.Map.Start(a);
                int e = first.Map.End(b);
                if (s > 0 && e >= s)
                {
                    CellRange mapped = plan.RowMode
                        ? new CellRange(new CellAddress(s, range.TopLeft.Column), new CellAddress(e, range.BottomRight.Column))
                        : new CellRange(new CellAddress(range.TopLeft.Row, s), new CellAddress(range.BottomRight.Row, e));
                    if (mapped.ToString() != reference)
                    {
                        source.SetAttribute("ref", mapped.ToString());
                        touched = true;
                    }
                }

                if (first.FinalName != sheet)
                {
                    source.SetAttribute("sheet", first.FinalName);
                    touched = true;
                }
            }
            else if (sheet is not null && plans.ContainsKey(sheet) || (source.GetAttribute("name") is not null && plans.Count > 0))
            {
                // A table or defined name as the source was resized with its definition.
                touched = true;
            }

            if (touched)
            {
                cache.Root.SetAttribute("refreshOnLoad", "1");
            }
        }
    }
}
