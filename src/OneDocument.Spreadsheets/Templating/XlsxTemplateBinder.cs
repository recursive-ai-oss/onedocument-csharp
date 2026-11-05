using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Templating;

namespace RecursiveAI.OneDocument.Spreadsheets.Templating;

/// <summary>How a workbook template is rendered beyond what <see cref="TemplateOptions"/> says.</summary>
public sealed class XlsxTemplateOptions
{
    /// <summary>
    /// When true the workbook is calculated with the calc engine after rendering, so the saved cached values are the ones the formulas give.
    /// When false (the default) the formulas have no cached values and the workbook is marked to be calculated when it is opened.
    /// </summary>
    public bool RecalculateNow { get; init; }
}

/// <summary>The outcome of rendering a workbook template.</summary>
/// <param name="Warnings">Warnings found while rendering (missing data, sheet names that had to be repaired, tags that cannot be bound here).</param>
public sealed record XlsxTemplateResult(IReadOnlyList<Defect> Warnings);

// What a cell looked like before rendering. The store is emptied and refilled, so everything is copied out as plain values first.
internal sealed class CapturedCell
{
    public int Row;
    public int Column;
    public CellValueKind Kind;
    public double Number;
    public string? Text;
    public CellError Error;
    public int Style;
    public CellFormula? Formula;
    public CellExtras? Extras;
    public bool IsTemplate;
}

internal sealed class CapturedRow
{
    public int Row;
    public double Height;
    public RowFlags Flags;
    public byte Outline;
    public int Style;
    public string? ExtraAttributes;
    public byte[]? ExtraChildren;
    public bool HasProperties;
}

internal sealed class OutMajor
{
    public int Source;
    public int Out;
    public List<OutCell> Cells = [];
}

internal sealed class OutCell
{
    public CapturedCell Source = null!;
    public int Unit;
    public IReadOnlyList<TemplatePiece>? Pieces;
}

internal sealed class OutSheet
{
    public string Name = string.Empty;
    public string FinalName = string.Empty;
    public List<OutMajor> Majors = [];
    public AxisMap Map = null!;
}

// The plan for one template sheet: what it holds, how it was cut into units, and what came out.
internal sealed class SheetPlan : ITemplateScopeResolver
{
    public Worksheet Sheet = null!;
    public string OldName = string.Empty;
    public bool RowMode = true;
    public List<CapturedCell> Cells = [];
    public Dictionary<int, CapturedRow> RowProps = [];
    public List<ColumnProperties> ColumnProps = [];
    public int[] Majors = [];
    public List<TemplateUnit> Units = [];
    public List<int> UnitMajor = [];
    public List<CapturedCell?> UnitCell = [];
    public Dictionary<int, int> MarkerUnit = [];
    public Dictionary<int, int> LastUnit = [];
    public List<(int From, int To)> Spans = [];
    public List<OutSheet> Output = [];
    public List<CellRange> Merges = [];

    public TemplateUnitRange? Resolve(in TemplateBlockInfo block)
    {
        string? scope = block.Scope;
        int a = Math.Min(block.OpenUnit, block.CloseUnit);
        int b = Math.Max(block.OpenUnit, block.CloseUnit);
        if (scope == "sheet" || a == 0)
        {
            return new TemplateUnitRange(0, Units.Count - 1);
        }

        if (scope == "inline" || (a == b && scope is null))
        {
            return null;
        }

        int ma = UnitMajor[a];
        int mb = UnitMajor[b];
        Spans.Add((ma, mb));
        return new TemplateUnitRange(MarkerUnit[ma], LastUnit[mb]);
    }

    public bool SameBand(int a, int b)
    {
        foreach ((int from, int to) in Spans)
        {
            if (a >= from && a <= to && b >= from && b <= to)
            {
                return true;
            }
        }

        return false;
    }
}

internal sealed record OwnerContext(SheetPlan Plan, OutSheet Sheet, int SourceMajor, int OutMajor);

/// <summary>
/// Renders a workbook template: tags in cell text, sheet names and numbers of cells become data; row, column, band and sheet blocks repeat
/// the cells they cover; everything that refers to cells that moved (formulas, defined names, merged cells, tables, conditional formats,
/// validations, hyperlinks) follows them. See <c>docs/templating-xlsx.md</c>.
/// </summary>
public static class XlsxTemplateBinder
{
    /// <summary>Renders a workbook template in place: every sheet with a tag in a cell or in its name is replaced by what the tags produce.</summary>
    /// <param name="workbook">The template; it becomes the result. Sheets without tags are not touched.</param>
    /// <param name="data">The data, as <see cref="TemplateData.From"/> reads it, or an <see cref="ITemplateData"/>.</param>
    /// <param name="options">Template options (delimiters, strict mode, clock, functions), or null.</param>
    /// <param name="xlsx">Workbook options, or null.</param>
    /// <param name="cancellationToken">Cancels a long render.</param>
    /// <returns>The warnings (missing data, repaired sheet names, tags that cannot be bound in a workbook).</returns>
    /// <exception cref="TemplateException">A template is invalid (located at the cell), a rendered sheet would not fit, or strict mode met missing data.</exception>
    /// <exception cref="OfficeLimitException">A template limit was exceeded.</exception>
    public static XlsxTemplateResult Render(Workbook workbook, object? data, TemplateOptions? options = null, XlsxTemplateOptions? xlsx = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        options ??= new TemplateOptions();
        xlsx ??= new XlsxTemplateOptions();
        ITemplateData root = data as ITemplateData ?? TemplateData.From(data, options.MaxRows);
        var warnings = new List<Defect>();

        var plans = new List<SheetPlan>();
        foreach (Worksheet ws in workbook.Sheets.ToList())
        {
            if (ws.Kind == SheetKind.Worksheet && Capture(ws, options) is { } plan)
            {
                plans.Add(plan);
            }
        }

        foreach (SheetPlan plan in plans)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RenderSheet(plan, root, options, warnings, cancellationToken);
        }

        if (plans.Count > 0)
        {
            AssignNames(workbook, plans, warnings);
            var byName = new Dictionary<string, SheetPlan>(StringComparer.OrdinalIgnoreCase);
            foreach (SheetPlan p in plans)
            {
                byName[p.OldName] = p;
            }

            var rewriter = new Rewriter(byName, workbook);
            foreach (SheetPlan plan in plans)
            {
                Apply(workbook, plan, rewriter);
            }

            rewriter.RewriteUntouched(plans);
            rewriter.RewriteNames(workbook);
        }

        // Cached values of formulas were discarded where formulas were written, so the workbook has to be calculated before it is used.
        workbook.MarkNeedsRecalculation();
        if (xlsx.RecalculateNow || (plans.Count > 0 && workbook.IsManualCalculation))
        {
            workbook.RecalculateAll(null, cancellationToken);
        }

        return new XlsxTemplateResult(warnings);
    }

    // ---- reading a sheet into units ----------------------------------------------------------------------------------------------

    private static bool HasTag(string text, TemplateDelimiters d) =>
        text.Contains(d.ExpressionOpen, StringComparison.Ordinal) || text.Contains(d.BlockOpen, StringComparison.Ordinal) || text.Contains(d.CommentOpen, StringComparison.Ordinal);

    private static SheetPlan? Capture(Worksheet ws, TemplateOptions options)
    {
        TemplateDelimiters d = options.Delimiters;
        CellStore store = ws.Store;
        var plan = new SheetPlan { Sheet = ws, OldName = ws.Name };
        var anchors = new Dictionary<int, (CellFormula F, int Row, int Col)>();
        foreach (RowData r in store.Rows)
        {
            for (int i = 0; i < r.Count; i++)
            {
                if (r.Formulas is { } fi && fi[i] != 0 && store.FormulaAt(fi[i]) is { Kind: CellFormulaKind.Shared, Text.Length: > 0 } f)
                {
                    anchors[f.SharedIndex] = (f, r.Row, r.Cols[i] + 1);
                }
            }
        }

        bool any = HasTag(ws.Name, d);
        foreach (RowData r in store.Rows)
        {
            if (r.HasProperties)
            {
                plan.RowProps[r.Row] = new CapturedRow { Row = r.Row, Height = r.Height, Flags = r.Flags, Outline = r.OutlineLevel, Style = r.Style, ExtraAttributes = r.ExtraAttributes, ExtraChildren = r.ExtraChildren, HasProperties = true };
            }

            for (int i = 0; i < r.Count; i++)
            {
                var cc = new CapturedCell { Row = r.Row, Column = r.Cols[i] + 1, Style = r.Styles[i], Extras = store.GetExtras(r.Row, r.Cols[i] + 1) };
                CellValue v = RowData.Decode(r.Kinds[i], r.Data[i]);
                cc.Kind = v.Kind;
                switch (v.Kind)
                {
                    case CellValueKind.Number:
                        cc.Number = v.Number;
                        break;
                    case CellValueKind.Boolean:
                        cc.Number = v.Boolean ? 1 : 0;
                        break;
                    case CellValueKind.Text:
                        cc.Text = ws.GetText(v);
                        break;
                    case CellValueKind.Error:
                        cc.Text = ws.GetText(v);
                        if (CellErrors.TryParse(cc.Text, out CellError e))
                        {
                            cc.Error = e;
                        }
                        else
                        {
                            cc.Kind = CellValueKind.Text;
                        }

                        break;
                }

                if (r.Formulas is { } fi && fi[i] != 0)
                {
                    cc.Formula = Unshare(store.FormulaAt(fi[i]), r.Row, cc.Column, anchors);
                }
                else if (cc.Kind == CellValueKind.Text && cc.Text is { } t && HasTag(t, d))
                {
                    cc.IsTemplate = true;
                    any = true;
                }

                plan.Cells.Add(cc);
            }
        }

        if (!any)
        {
            return null;
        }

        foreach (ColumnProperties c in ws.Columns)
        {
            plan.ColumnProps.Add(c);
        }

        plan.Merges = [.. ws.MergedCells];
        plan.RowMode = !plan.Cells.Any(static c => c.IsTemplate && c.Text!.Contains("scope=column", StringComparison.OrdinalIgnoreCase));
        BuildUnits(plan);
        return plan;
    }

    // A cell of a shared formula holds only the group's index; give it its own text so the cell can move on its own.
    private static CellFormula Unshare(CellFormula f, int row, int column, Dictionary<int, (CellFormula F, int Row, int Col)> anchors)
    {
        if (f.Kind != CellFormulaKind.Shared)
        {
            return f;
        }

        string text = f.Text;
        if (text.Length == 0 && anchors.TryGetValue(f.SharedIndex, out var a))
        {
            if (FormulaParser.TryParse(a.F.Text, out FormulaNode? node, out _) && node is not null)
            {
                text = FormulaPrinter.Print(FormulaTranslator.Translate(node, row - a.Row, column - a.Col));
            }
            else
            {
                text = a.F.Text;
            }
        }

        return new CellFormula(text, CellFormulaKind.Normal, null, -1, f.RawAttributes);
    }

    private static string ColumnName(int column) => new CellAddress(1, column).ToString().TrimEnd('1');

    private static string Address(int row, int column) => ColumnName(column) + row.ToString(CultureInfo.InvariantCulture);

    private static void BuildUnits(SheetPlan plan)
    {
        Worksheet ws = plan.Sheet;
        string part = ws.PartName ?? "/xl/worksheets/sheet.xml";
        plan.Units.Add(new TemplateUnit(ws.Name, part + "#" + ws.Name));
        plan.UnitMajor.Add(-1);
        plan.UnitCell.Add(null);

        var byMajor = new SortedDictionary<int, List<CapturedCell>>();
        foreach (CapturedCell c in plan.Cells)
        {
            int m = plan.RowMode ? c.Row : c.Column;
            if (!byMajor.TryGetValue(m, out List<CapturedCell>? l))
            {
                byMajor[m] = l = [];
            }

            l.Add(c);
        }

        if (plan.RowMode)
        {
            foreach (int r in plan.RowProps.Keys)
            {
                byMajor.TryAdd(r, []);
            }
        }
        else
        {
            foreach (ColumnProperties c in plan.ColumnProps)
            {
                for (int k = c.Min; k <= c.Max && k - c.Min < 16384; k++)
                {
                    byMajor.TryAdd(k, []);
                }
            }
        }

        plan.Majors = [.. byMajor.Keys];
        foreach ((int m, List<CapturedCell> cells) in byMajor)
        {
            if (!plan.RowMode)
            {
                cells.Sort(static (x, y) => x.Row.CompareTo(y.Row));
            }

            plan.MarkerUnit[m] = plan.Units.Count;
            plan.Units.Add(new TemplateUnit(string.Empty, part + "#" + ws.Name + "!" + (plan.RowMode ? m.ToString(CultureInfo.InvariantCulture) : ColumnName(m))));
            plan.UnitMajor.Add(m);
            plan.UnitCell.Add(null);
            foreach (CapturedCell c in cells)
            {
                plan.Units.Add(new TemplateUnit(c.IsTemplate ? c.Text! : string.Empty, part + "#" + ws.Name + "!" + Address(c.Row, c.Column)));
                plan.UnitMajor.Add(m);
                plan.UnitCell.Add(c);
            }

            plan.LastUnit[m] = plan.Units.Count - 1;
        }
    }

    // ---- rendering ---------------------------------------------------------------------------------------------------------------

    private static TemplateOptions WithScopes(TemplateOptions o, ITemplateScopeResolver scopes) => new()
    {
        Delimiters = o.Delimiters,
        Strict = o.Strict,
        Limits = o.Limits,
        Defects = o.Defects,
        Functions = o.Functions,
        Clock = o.Clock,
        RandomSeed = o.RandomSeed,
        Scopes = scopes,
        Includes = o.Includes,
        MaxIterations = o.MaxIterations,
        MaxOutputCharacters = o.MaxOutputCharacters,
        MaxBlockDepth = o.MaxBlockDepth,
        MaxRows = o.MaxRows,
    };

    private static void RenderSheet(SheetPlan plan, ITemplateData root, TemplateOptions options, List<Defect> warnings, CancellationToken ct)
    {
        var source = new TemplateSource(plan.Units);
        ParsedTemplate parsed = TemplateEngine.Parse(source, WithScopes(options, plan));
        TemplateResult result = parsed.Render(root, ct);
        warnings.AddRange(parsed.Defects.Where(static d => d.Severity != DefectSeverity.Fatal));
        warnings.AddRange(result.Defects);

        OutSheet? cur = null;
        OutMajor? curMajor = null;
        int prevSrc = 0;
        int prevOut = 0;
        foreach (TemplateRenderedUnit ru in result.Units)
        {
            int u = ru.SourceUnit;
            if (u == 0)
            {
                cur = NewSheet(plan, ru.Text);
                curMajor = null;
                prevSrc = 0;
                prevOut = 0;
                continue;
            }

            cur ??= NewSheet(plan, plan.OldName);
            CapturedCell? cell = plan.UnitCell[u];
            if (cell is null)
            {
                int s = plan.UnitMajor[u];
                int o = cur.Map.Place(s, prevSrc, prevOut, curMajor is null);
                if (o > (plan.RowMode ? CellAddress.MaxRows : CellAddress.MaxColumns))
                {
                    throw new TemplateException(new Defect(DefectKind.Structure, "template.sheet-overflow", DefectSeverity.Fatal,
                        "The rendered sheet '" + cur.Name + "' would have more " + (plan.RowMode ? "rows" : "columns") + " than a sheet can hold.", DefectLocation.InPart(plan.Sheet.PartName ?? "/xl/worksheets/sheet.xml")));
                }

                curMajor = new OutMajor { Source = s, Out = o };
                cur.Majors.Add(curMajor);
                prevSrc = s;
                prevOut = o;
                cur.Map.TailSource = s;
                cur.Map.TailOut = o;
            }
            else if (curMajor is not null)
            {
                curMajor.Cells.Add(new OutCell { Source = cell, Unit = u, Pieces = ru.Pieces });
            }
        }

        foreach (OutSheet sheet in plan.Output)
        {
            foreach (OutMajor m in sheet.Majors)
            {
                Restore(plan, m, options.Delimiters);
            }
        }

        foreach (OutSheet sheet in plan.Output)
        {
            foreach (OutMajor m in sheet.Majors)
            {
                foreach (OutCell c in m.Cells)
                {
                    if (c.Pieces is not null && c.Pieces.Any(static p => p.Kind != TemplatePieceKind.Text))
                    {
                        warnings.Add(new Defect(DefectKind.Structure, "template.tag-not-bound", DefectSeverity.Dropped,
                            "An image, html or markdown tag in a cell is not bound in a workbook; it printed nothing.", DefectLocation.InCell(plan.OldName, Address(c.Source.Row, c.Source.Column))));
                    }
                }
            }
        }
    }

    private static OutSheet NewSheet(SheetPlan plan, string name)
    {
        var s = new OutSheet { Name = name, Map = new AxisMap(plan.Majors) };
        plan.Output.Add(s);
        return s;
    }

    // A cell that held only block tags prints nothing, but it keeps its place and style in the rows that repeat.
    private static void Restore(SheetPlan plan, OutMajor m, TemplateDelimiters d)
    {
        int first = plan.MarkerUnit[m.Source];
        int last = plan.LastUnit[m.Source];
        for (int u = first + 1; u <= last; u++)
        {
            CapturedCell? c = plan.UnitCell[u];
            if (c is null || !c.IsTemplate || m.Cells.Any(x => x.Unit == u))
            {
                continue;
            }

            string stripped = Regex.Replace(c.Text!, Regex.Escape(d.BlockOpen) + ".*?" + Regex.Escape(d.BlockClose) + "|" + Regex.Escape(d.CommentOpen) + ".*?" + Regex.Escape(d.CommentClose), string.Empty, RegexOptions.Singleline);
            if (string.IsNullOrWhiteSpace(stripped))
            {
                m.Cells.Add(new OutCell { Source = c, Unit = u, Pieces = [] });
            }
        }

        m.Cells.Sort((x, y) => (plan.RowMode ? x.Source.Column : x.Source.Row).CompareTo(plan.RowMode ? y.Source.Column : y.Source.Row));
    }

    // ---- sheet names -------------------------------------------------------------------------------------------------------------

    private static string CleanName(string name)
    {
        var chars = name.Trim().Trim('\'').ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (chars[i] is '[' or ']' or ':' or '*' or '?' or '/' or '\\')
            {
                chars[i] = '_';
            }
        }

        string s = new(chars);
        return s.Length > 31 ? s[..31] : s;
    }

    private static void AssignNames(Workbook wb, List<SheetPlan> plans, List<Defect> warnings)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var templateSheets = new HashSet<Worksheet>(plans.Select(static p => p.Sheet));
        foreach (Worksheet ws in wb.Sheets)
        {
            if (!templateSheets.Contains(ws))
            {
                used.Add(ws.Name);
            }
        }

        foreach (SheetPlan plan in plans)
        {
            int n = 0;
            foreach (OutSheet o in plan.Output)
            {
                string wanted = CleanName(o.Name);
                if (wanted.Length == 0)
                {
                    wanted = n == 0 ? plan.OldName : plan.OldName + " " + (n + 1).ToString(CultureInfo.InvariantCulture);
                }

                string name = wanted;
                for (int k = 2; !used.Add(name); k++)
                {
                    string suffix = " (" + k.ToString(CultureInfo.InvariantCulture) + ")";
                    name = (wanted.Length + suffix.Length > 31 ? wanted[..(31 - suffix.Length)] : wanted) + suffix;
                }

                if (name != o.Name)
                {
                    warnings.Add(new Defect(DefectKind.Structure, "template.sheet-name-repaired", DefectSeverity.Repaired,
                        "The sheet name '" + o.Name + "' is not usable as it stands; the sheet is called '" + name + "'.", DefectLocation.InPart(plan.Sheet.PartName ?? "/xl/worksheets/sheet.xml")));
                }

                o.FinalName = name;
                n++;
            }
        }
    }

    // ---- writing the workbook ----------------------------------------------------------------------------------------------------

    private static void Apply(Workbook wb, SheetPlan plan, Rewriter rewriter)
    {
        Worksheet original = plan.Sheet;
        if (plan.Output.Count == 0)
        {
            // Every iteration of a sheet block was empty: the sheet goes, unless it is the last visible one.
            ClearStore(original.Store);
            if (wb.Sheets.Count > 1)
            {
                wb.Sheets.Remove(original);
            }

            return;
        }

        int insertAt = original.Index;
        for (int i = 0; i < plan.Output.Count; i++)
        {
            OutSheet o = plan.Output[i];
            Worksheet target;
            if (i == 0)
            {
                target = original;
                ClearStore(target.Store);
                if (target.Name != o.FinalName)
                {
                    target.Rename(o.FinalName);
                }
            }
            else
            {
                target = wb.Sheets.Insert(insertAt + i, o.FinalName);
            }

            WriteSheet(plan, o, target, i == 0, rewriter);
        }
    }

    private static void ClearStore(CellStore store)
    {
        foreach (int r in store.Rows.Select(static r => r.Row).ToArray())
        {
            store.RemoveRow(r);
        }

        store.IsModified = true;
    }

    private static void WriteSheet(SheetPlan plan, OutSheet o, Worksheet target, bool isOriginal, Rewriter rewriter)
    {
        CellStore store = target.Store;
        var columns = new List<ColumnProperties>();
        foreach (OutMajor m in o.Majors)
        {
            if (plan.RowMode && plan.RowProps.TryGetValue(m.Source, out CapturedRow? rp))
            {
                RowData rd = store.EditRow(m.Out);
                rd.Height = rp.Height;
                rd.Flags = rp.Flags;
                rd.OutlineLevel = rp.Outline;
                rd.Style = rp.Style;
                rd.ExtraAttributes = rp.ExtraAttributes;
                rd.ExtraChildren = rp.ExtraChildren;
            }

            if (!plan.RowMode)
            {
                foreach (ColumnProperties cp in plan.ColumnProps)
                {
                    if (m.Source >= cp.Min && m.Source <= cp.Max)
                    {
                        columns.Add(cp with { Min = m.Out, Max = m.Out });
                    }
                }
            }

            foreach (OutCell oc in m.Cells)
            {
                CapturedCell c = oc.Source;
                int row = plan.RowMode ? m.Out : c.Row;
                int col = plan.RowMode ? c.Column : m.Out;
                var cell = target[row, col];
                if (c.Formula is { } f)
                {
                    var ctx = new OwnerContext(plan, o, plan.RowMode ? c.Row : c.Column, m.Out);
                    string text = rewriter.RewriteText(f.Text, plan.OldName, ctx, o.FinalName);
                    string? reference = f.Reference;
                    if (f.Kind == CellFormulaKind.Array && reference is not null && CellRange.TryParse(reference, out CellRange rr))
                    {
                        reference = new CellRange(new CellAddress(row, col), new CellAddress(row + rr.RowCount - 1, col + rr.ColumnCount - 1)).ToString();
                    }

                    cell.FormulaDetail = new CellFormula(text, f.Kind, reference, f.SharedIndex, f.RawAttributes);
                }
                else if (oc.Pieces is { } pieces && c.IsTemplate)
                {
                    SetTemplateValue(cell, c, pieces);
                }
                else
                {
                    switch (c.Kind)
                    {
                        case CellValueKind.Number:
                            cell.SetNumber(c.Number);
                            break;
                        case CellValueKind.Boolean:
                            cell.SetBoolean(c.Number != 0);
                            break;
                        case CellValueKind.Text:
                            cell.SetString(c.Text ?? string.Empty);
                            break;
                        case CellValueKind.Error:
                            cell.SetError(c.Error);
                            break;
                    }
                }

                if (c.Style != 0 || !cell.Exists)
                {
                    cell.StyleIndex = c.Style;
                }

                if (c.Extras is not null)
                {
                    store.SetExtras(row, col, c.Extras);
                }
            }
        }

        if (plan.RowMode && !isOriginal)
        {
            target.SetColumns(plan.ColumnProps);
        }

        if (!plan.RowMode)
        {
            columns.Sort(static (x, y) => x.Min.CompareTo(y.Min));
            target.SetColumns(columns);
        }

        FrameModel frame = target.Frame;
        if (isOriginal)
        {
            RewriteFrame(plan, o, target, frame);
        }
        else
        {
            frame.Merges = MapMerges(plan, o, plan.Merges);
            frame.MergesChanged = true;
        }
    }

    private static void SetTemplateValue(Cell cell, CapturedCell source, IReadOnlyList<TemplatePiece> pieces)
    {
        string text = string.Concat(pieces.Select(static p => p.Text));
        // A cell that is only one {{ }} tag keeps the type of what the tag produced: a number stays a number so it can be formatted and summed.
        if (pieces.Count == 1 && pieces[0].Typed is { } v)
        {
            switch (v.Kind)
            {
                case ValueKind.Number:
                    cell.SetNumber(v.Number);
                    return;
                case ValueKind.Boolean:
                    cell.SetBoolean(v.Boolean);
                    return;
                case ValueKind.Error:
                    if (CellErrors.TryParse(CalcErrors.ToText(v.Error), out CellError e))
                    {
                        cell.SetError(e);
                        return;
                    }

                    break;
                case ValueKind.Blank:
                    return;
            }
        }

        if (text.Length > 0)
        {
            cell.SetString(text);
        }
    }

    // The elements around the cells that name ranges: merged cells, tables, conditional formats, validations and hyperlinks.
    private static void RewriteFrame(SheetPlan plan, OutSheet o, Worksheet target, FrameModel frame)
    {
        frame.Merges = MapMerges(plan, o, plan.Merges);
        frame.MergesChanged = true;
        frame.Changed = true;

        foreach (ConditionalFormat cf in target.ConditionalFormats)
        {
            MapSqRef(plan, o, cf.Markup, "sqref");
        }

        foreach (DataValidation dv in target.Validations)
        {
            MapSqRef(plan, o, dv.Markup, "sqref");
        }

        foreach (MarkupNode child in frame.Root.ChildElements)
        {
            if (child.LocalNameString == "hyperlinks")
            {
                foreach (MarkupNode h in child.ChildElements)
                {
                    MapSqRef(plan, o, h, "ref");
                }
            }
        }

        if (plan.RowMode)
        {
            foreach (Table t in target.Tables.ToList())
            {
                CellRange r = t.Range;
                if (MapRange(plan, o, r) is { } nr && nr.IsValid)
                {
                    t.Resize(nr);
                }
            }
        }
    }

    private static void MapSqRef(SheetPlan plan, OutSheet o, MarkupNode node, string attribute)
    {
        string? text = node.GetAttribute(attribute);
        if (text is null)
        {
            return;
        }

        var mapped = new List<CellRange>();
        foreach (CellRange r in SqRef.Parse(text))
        {
            if (MapRange(plan, o, r) is { } nr && nr.IsValid)
            {
                mapped.Add(nr);
            }
        }

        if (mapped.Count > 0)
        {
            string value = SqRef.Format(mapped);
            if (value != text)
            {
                node.SetAttribute(attribute, value);
            }
        }
    }

    private static CellRange? MapRange(SheetPlan plan, OutSheet o, CellRange r)
    {
        int a = plan.RowMode ? r.TopLeft.Row : r.TopLeft.Column;
        int b = plan.RowMode ? r.BottomRight.Row : r.BottomRight.Column;
        int s = o.Map.Start(a);
        int e = o.Map.End(b);
        if (s == 0 || e == 0 || e < s)
        {
            return null;
        }

        return plan.RowMode
            ? new CellRange(new CellAddress(s, r.TopLeft.Column), new CellAddress(e, r.BottomRight.Column))
            : new CellRange(new CellAddress(r.TopLeft.Row, s), new CellAddress(r.BottomRight.Row, e));
    }

    // A merged range inside a repeated block is repeated with it; one that spans the block is stretched.
    private static List<CellRange> MapMerges(SheetPlan plan, OutSheet o, List<CellRange> merges)
    {
        var result = new List<CellRange>();
        foreach (CellRange r in merges)
        {
            int a = plan.RowMode ? r.TopLeft.Row : r.TopLeft.Column;
            int b = plan.RowMode ? r.BottomRight.Row : r.BottomRight.Column;
            if (plan.SameBand(a, b) && o.Map.Instances.TryGetValue(a, out List<int>? starts))
            {
                foreach (int s in starts)
                {
                    int e = a == b ? s : o.Map.Near(b, a, s);
                    result.Add(Build(plan, r, s, e));
                }
            }
            else if (MapRange(plan, o, r) is { } nr)
            {
                result.Add(nr);
            }
        }

        return result;
    }

    private static CellRange Build(SheetPlan plan, CellRange r, int s, int e) => plan.RowMode
        ? new CellRange(new CellAddress(s, r.TopLeft.Column), new CellAddress(e, r.BottomRight.Column))
        : new CellRange(new CellAddress(r.TopLeft.Row, s), new CellAddress(r.BottomRight.Row, e));
}
