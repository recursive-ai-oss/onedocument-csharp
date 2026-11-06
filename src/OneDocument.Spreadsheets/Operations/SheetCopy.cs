using System.Globalization;
using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.NumberFormats;
using RecursiveAI.OneDocument.Operations;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Spreadsheets.Calc;

namespace RecursiveAI.OneDocument.Spreadsheets;

/// <summary>Choices for copying or moving a sheet into a workbook.</summary>
public sealed class SheetCopyOptions
{
    /// <summary>Gets or sets the name of the new sheet; null keeps the source name. A name the destination already uses gets <c>(2)</c>, <c>(3)</c> … appended, as Excel does.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets the position of the new sheet among the destination's sheets; null (the default) puts it last.</summary>
    public int? Index { get; set; }

    /// <summary>Gets or sets the file name that references to sheets left behind in the source workbook carry (<c>[1]Sheet2!A1</c> points at this file). Null uses the source's file name, or <c>Source.xlsx</c> when it has none.</summary>
    public string? SourceLinkTarget { get; set; }
}

/// <summary>The sheet a copy made and what the copy did.</summary>
public sealed class SheetCopyResult
{
    internal SheetCopyResult(Worksheet sheet, MergeReport report)
    {
        Sheet = sheet;
        Report = report;
    }

    /// <summary>Gets the new sheet.</summary>
    public Worksheet Sheet { get; }

    /// <summary>Gets the report: counts (<c>cells</c>, <c>formulas</c>, <c>styles</c>, <c>names</c>, <c>tables</c>), renames and conflicts, losses and what could not be copied.</summary>
    public MergeReport Report { get; }
}

public sealed partial class Worksheet
{
    /// <summary>
    /// Copies this sheet into <paramref name="destination"/> (which may be this sheet's own workbook): cells with their cached values,
    /// formulas, styles, row and column settings, merged cells, tables, conditional formats, data validations, sheet-level names,
    /// hyperlinks and the page and view settings.
    /// </summary>
    /// <param name="destination">The workbook that gets the copy.</param>
    /// <param name="options">Name and position of the copy; null for the defaults.</param>
    /// <returns>The new sheet and the report.</returns>
    /// <remarks>
    /// <para>Cell styles are brought over by value: each cell format is looked up in the destination's style sheet or added there.
    /// Formulas are re-pointed: references to this sheet follow its new name; references to other sheets of the source workbook, which stay
    /// behind, become references to an external link to the source (<c>[1]Sheet2!A1</c>) with the referenced cells' values cached, so the copy
    /// still calculates to the same values; names the formulas use are added to the destination (a name the destination already has with
    /// another definition is kept and reported). Cached values are kept and nothing is recalculated.</para>
    /// <para>Drawings, charts, pictures, comments, pivot tables and other parts hanging off the sheet are not copied; each kind is a located problem in the report.</para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is null.</exception>
    /// <exception cref="OfficeUnsupportedException">This sheet is a chart, dialog or macro sheet.</exception>
    public SheetCopyResult CopyTo(Workbook destination, SheetCopyOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        return new SheetCopier(this, destination, options ?? new SheetCopyOptions()).Run();
    }

    /// <summary>Copies this sheet into <paramref name="destination"/> and removes it from its own workbook (see <see cref="CopyTo"/>).</summary>
    /// <param name="destination">The workbook that gets the sheet.</param>
    /// <param name="options">Name and position of the sheet in the destination; null for the defaults.</param>
    /// <returns>The new sheet and the report. Formulas in the sheets left behind that referred to the moved sheet are not re-pointed; they are counted as a problem.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The destination is this sheet's own workbook (use <see cref="SheetCollection.Move"/>), or this is the last visible sheet.</exception>
    public SheetCopyResult MoveTo(Workbook destination, SheetCopyOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (ReferenceEquals(destination, _workbook))
        {
            throw new InvalidOperationException("The sheet is already in that workbook; use Sheets.Move to change its position.");
        }

        SheetCopyResult result = CopyTo(destination, options);
        int dangling = SheetCopier.CountReferencesTo(this);
        _workbook.Sheets.Remove(this);
        if (dangling > 0)
        {
            result.Report.Problem("merge.references-left", dangling.ToString(CultureInfo.InvariantCulture) + " formula(s) in the sheets left behind refer to the moved sheet; they were not re-pointed and will read #REF! when recalculated.",
                DefectLocation.None);
        }

        return result;
    }
}

public sealed partial class Workbook
{
    internal void ResetExternalLinksCache() => _externalLinks = null;
}

internal sealed class SheetCopier
{
    private static readonly string[] FrameElements = ["sheetPr", "sheetViews", "sheetFormatPr", "sheetProtection", "autoFilter", "conditionalFormatting", "dataValidations", "printOptions", "pageMargins", "pageSetup", "headerFooter", "rowBreaks", "colBreaks"];
    private static readonly string[] WorkbookOrder =
    [
        "fileVersion", "fileSharing", "workbookPr", "workbookProtection", "bookViews", "sheets", "functionGroups", "externalReferences", "definedNames", "calcPr",
        "oleSize", "customWorkbookViews", "pivotCaches", "smartTagPr", "smartTagTypes", "webPublishing", "fileRecoveryPr", "webPublishObjects", "extLst",
    ];

    private readonly Worksheet _src;
    private readonly Workbook _dstWb;
    private readonly SheetCopyOptions _o;
    private readonly bool _same;
    private readonly MergeReport _r = new();
    private readonly Dictionary<int, int> _styleMap = [];
    private readonly Dictionary<int, int> _dxfMap = [];
    private readonly Dictionary<string, string> _tableNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SortedSet<(int Row, int Col)>> _external = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _externalOrder = [];
    private readonly HashSet<string> _namesInProgress = new(StringComparer.OrdinalIgnoreCase);
    private Worksheet _dst = null!;
    private int _externalIndex;

    public SheetCopier(Worksheet src, Workbook dst, SheetCopyOptions options)
    {
        _src = src;
        _dstWb = dst;
        _o = options;
        _same = ReferenceEquals(src.Workbook, dst);
    }

    private Workbook SrcWb => _src.Workbook;

    public SheetCopyResult Run()
    {
        if (_src.Kind != SheetKind.Worksheet)
        {
            throw new OfficeUnsupportedException("Copying a " + _src.Kind + " sheet", DefectLocation.InPart(_src.PartName ?? SrcWb.MainPartName));
        }

        _src.Load();
        string name = UniqueName(_o.Name ?? _src.Name);
        if (!string.Equals(name, _o.Name ?? _src.Name, StringComparison.OrdinalIgnoreCase) || (_o.Name is null && !_same && false))
        {
            _r.Resolve(new MergeResolution("merge.sheet-renamed", _o.Name ?? _src.Name, "renamed to " + name, "The destination already has a sheet with that name."));
        }

        _dst = _dstWb.Sheets.Insert(Math.Clamp(_o.Index ?? _dstWb.Sheets.Count, 0, _dstWb.Sheets.Count), name);
        _externalIndex = _dstWb.ExternalLinks.Count + 1;

        CopyTables();
        CopyCells();
        CopyRowsColumnsMerges();
        CopyFrame();
        CopyNames();
        CopyHyperlinks();
        ReportUncopied();
        WriteExternalLink();
        _r.Count("sheets");
        return new SheetCopyResult(_dst, _r);
    }

    private string UniqueName(string wanted)
    {
        if (!_dstWb.Sheets.TryGet(wanted, out _))
        {
            return wanted;
        }

        for (int i = 2; ; i++)
        {
            string suffix = " (" + i.ToString(CultureInfo.InvariantCulture) + ")";
            string candidate = (wanted.Length + suffix.Length > 31 ? wanted[..(31 - suffix.Length)] : wanted) + suffix;
            if (!_dstWb.Sheets.TryGet(candidate, out _))
            {
                return candidate;
            }
        }
    }

    // ---- styles -------------------------------------------------------------------------------------------------------------------

    private int MapStyle(int index)
    {
        if (_same || index == 0 && SameDefault())
        {
            return index;
        }

        if (!_styleMap.TryGetValue(index, out int mapped))
        {
            // A cell format is carried by value: the destination interns the same font, fill, border and number format it already has.
            CellStyleSpec spec = CellStyleSpec.From(SrcWb.Styles.Resolve(index));
            spec = spec with { StyleName = EnsureNamedStyle(spec.StyleName) };
            mapped = _dstWb.Styles.GetOrAdd(spec);
            _styleMap[index] = mapped;
            _r.Count("styles");
        }

        return mapped;
    }

    // A cell format builds on a named cell style (Normal, Comma …): the destination gets the style, with the source's definition, when it has none by that name.
    private string? EnsureNamedStyle(string? name)
    {
        if (name is null || _dstWb.Styles.CellStyles.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return name;
        }

        NamedCellStyle? named = SrcWb.Styles.CellStyles.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        if (named is null || named.FormatId < 0)
        {
            return null;
        }

        try
        {
            StyleSheet s = SrcWb.Styles;
            CellFormat f = s.GetStyleFormat(named.FormatId);
            _dstWb.Styles.AddNamedStyle(named.Name, new CellStyleSpec
            {
                NumberFormatCode = s.GetNumberFormatCode(f.NumberFormatId),
                Font = s.GetFont(f.FontId),
                Fill = s.GetFill(f.FillId),
                Border = s.GetBorder(f.BorderId),
                Alignment = f.Alignment,
                Protection = f.Protection,
            });
            _r.Count("cell-styles");
            return named.Name;
        }
        catch (ArgumentException)
        {
            _r.Loss.Report("merge.cell-style-names", "Names of cell styles", 1, "A cell style the destination could not take over was dropped; cells keep its look as direct formatting.");
            return null;
        }
    }

    private bool SameDefault() => SrcWb.Styles.Resolve(0) == _dstWb.Styles.Resolve(0);

    private int MapDxf(int index)
    {
        if (_same)
        {
            return index;
        }

        if (!_dxfMap.TryGetValue(index, out int mapped))
        {
            mapped = index >= 0 && index < SrcWb.Styles.DifferentialFormatCount ? _dstWb.Styles.AddDifferentialFormat(SrcWb.Styles.GetDifferentialFormat(index)) : index;
            _dxfMap[index] = mapped;
        }

        return mapped;
    }

    // ---- cells --------------------------------------------------------------------------------------------------------------------

    private void CopyCells()
    {
        CellStore from = _src.Store;
        CellStore to = _dst.Store;
        bool shiftDates = !_same && SrcWb.DateSystem != _dstWb.DateSystem;
        double shift = SrcWb.DateSystem == DateSystem.Date1900 ? -1462 : 1462;
        long cells = 0;
        long formulas = 0;
        long dates = 0;
        foreach (RowData row in from.Rows)
        {
            if (row.HasProperties)
            {
                RowData d = to.EditRow(row.Row);
                d.Height = row.Height;
                d.Flags = row.Flags;
                d.OutlineLevel = row.OutlineLevel;
                d.Style = row.Style == 0 ? 0 : MapStyle(row.Style);
            }

            for (int i = 0; i < row.Count; i++)
            {
                int r = row.Row;
                int c = row.Cols[i] + 1;
                CellValue v = RowData.Decode(row.Kinds[i], row.Data[i]);
                int style = row.Styles[i];
                int mappedStyle = style == 0 ? MapStyle(0) : MapStyle(style);
                CellFormula? f = (row.Kinds[i] & RowData.HasFormulaBit) != 0 && row.Formulas is { } fa && fa[i] != 0 ? from.FormulaAt(fa[i]) : null;
                if (f is not null)
                {
                    to.SetFormula(r, c, f with { Text = f.Text.Length == 0 ? f.Text : RewriteFormula(f.Text, r, c), RawAttributes = null });
                    formulas++;
                }

                switch (v.Kind)
                {
                    case CellValueKind.Text:
                        to.SetValue(r, c, _dstWb.MakeStringValue(_src.GetText(v)));
                        break;
                    case CellValueKind.Number when shiftDates && SrcWb.Styles.GetNumberFormat(style).IsDateTime:
                        to.SetValue(r, c, CellValue.FromNumber(v.Number + shift));
                        dates++;
                        break;
                    case CellValueKind.Empty:
                        if (f is null)
                        {
                            to.SetValue(r, c, default);
                        }

                        break;
                    default:
                        to.SetValue(r, c, v);
                        break;
                }

                to.SetStyle(r, c, mappedStyle);
                cells++;
            }
        }

        _r.Count("cells", cells);
        _r.Count("formulas", formulas);
        if (dates > 0)
        {
            _r.Resolve(new MergeResolution("merge.date-system", _src.Name, "converted", dates.ToString(CultureInfo.InvariantCulture)
                + " date value(s) were shifted because the workbooks use different date systems (1900 and 1904)."));
        }
    }

    private void CopyRowsColumnsMerges()
    {
        _dst.SetColumns(_src.Columns.Select(c => c with { Style = c.Style == 0 ? 0 : MapStyle(c.Style), RawAttributes = null }));
        foreach (CellRange m in _src.MergedCells)
        {
            _dst.Merge(m);
        }
    }

    // ---- formulas -----------------------------------------------------------------------------------------------------------------

    private string RewriteFormula(string text, int row, int column)
    {
        if (_same && !NeedsRewriteWithinWorkbook(text))
        {
            return text;
        }

        FormulaNode tree;
        try
        {
            tree = FormulaParser.Parse(text);
        }
        catch (OfficeException)
        {
            _r.Problem("merge.formula-unreadable", "The formula could not be read, so it was copied as written.", DefectLocation.InCell(_src.Name, new CellAddress(row, column).ToString()));
            return text;
        }

        FormulaNode rewritten = FormulaRewriter.Rewrite(tree, n => RewriteNode(n));
        return ReferenceEquals(rewritten, tree) ? text : FormulaPrinter.Print(rewritten);
    }

    private bool NeedsRewriteWithinWorkbook(string text) =>
        text.Contains(_src.Name, StringComparison.OrdinalIgnoreCase) || _tableNames.Count > 0 || text.Contains('[');

    private FormulaNode? RewriteNode(FormulaNode n)
    {
        switch (n)
        {
            case CellReferenceNode c when c.Prefix is { } p:
                return NewPrefix(p, c.Row.Index, c.Column.Index, c.Row.Index, c.Column.Index) is { } cp ? new CellReferenceNode(cp, c.Row, c.Column) : null;
            case CellReferenceNode c when c.Prefix is null:
                return null;
            case AreaReferenceNode a when a.Prefix is { } p:
                return NewPrefix(p, a.Row1.Index, a.Column1.Index, a.Row2.Index, a.Column2.Index, a.AreaKind) is { } ap
                    ? new AreaReferenceNode(ap, a.AreaKind, a.Row1, a.Column1, a.Row2, a.Column2) : null;
            case NameNode nm:
                return RewriteName(nm);
            case StructuredReferenceNode s:
                {
                    SheetPrefix? sp = s.Prefix is { } p0 ? NewPrefix(p0, 1, 1, 1, 1) : null;
                    string? table = s.Table is { } t && _tableNames.TryGetValue(t, out string? nt) ? nt : s.Table;
                    return table != s.Table || sp is not null ? new StructuredReferenceNode(sp ?? s.Prefix, table, s.Specifier) : null;
                }
            default:
                return null;
        }
    }

    private SheetPrefix? NewPrefix(SheetPrefix p, int r1, int c1, int r2, int c2, AreaKind kind = AreaKind.Cells)
    {
        if (p.Workbook is not null)
        {
            // A reference through one of the source's own external links: the destination gets the same link.
            string mapped = MapExternalLink(p.Workbook);
            return new SheetPrefix(p.Sheet, p.LastSheet, mapped);
        }

        if (p.Sheet is null)
        {
            return null;
        }

        if (string.Equals(p.Sheet, _src.Name, StringComparison.OrdinalIgnoreCase) && p.LastSheet is null)
        {
            return string.Equals(_dst.Name, p.Sheet, StringComparison.Ordinal) ? null : new SheetPrefix(_dst.Name);
        }

        if (_same)
        {
            return null;
        }

        // A sheet that stays behind in the source workbook: the copy points at the source through an external link and caches the cells it reads.
        foreach (string s in new[] { p.Sheet, p.LastSheet }.Where(x => x is not null).Cast<string>())
        {
            if (!_external.TryGetValue(s, out SortedSet<(int, int)>? cells))
            {
                _external[s] = cells = [];
                _externalOrder.Add(s);
            }

            if (s == p.Sheet)
            {
                int rows = kind == AreaKind.Columns ? 0 : r2 - r1 + 1;
                int cols = kind == AreaKind.Rows ? 0 : c2 - c1 + 1;
                if (rows > 0 && cols > 0 && (long)rows * cols <= 4096)
                {
                    for (int rr = Math.Min(r1, r2); rr <= Math.Max(r1, r2); rr++)
                    {
                        for (int cc = Math.Min(c1, c2); cc <= Math.Max(c1, c2); cc++)
                        {
                            cells.Add((rr, cc));
                        }
                    }
                }
            }
        }

        return new SheetPrefix(p.Sheet, p.LastSheet, _externalIndex.ToString(CultureInfo.InvariantCulture));
    }

    // Names the copied formulas use: a name scoped to the source sheet follows it (see CopyNames); a workbook-level name is added to the destination.
    private NameNode? RewriteName(NameNode nm)
    {
        if (nm.Prefix is { } p)
        {
            return NewPrefix(p, 1, 1, 1, 1) is { } np ? new NameNode(np, nm.Name) : null;
        }

        if (_same || nm.Name.StartsWith("_xlpm.", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (SrcWb.Names.Find(nm.Name, _src) is not null)
        {
            return null;
        }

        if (SrcWb.Names.Find(nm.Name, null) is { } global && !global.IsBuiltIn)
        {
            EnsureGlobalName(global);
        }

        return null;
    }

    private void EnsureGlobalName(DefinedName global)
    {
        if (!_namesInProgress.Add(global.Name))
        {
            return;
        }

        string formula = RewriteFormula(global.Formula, 1, 1);
        if (_dstWb.Names.Find(global.Name, null) is { } existing)
        {
            if (existing.Formula != formula)
            {
                _r.Resolve(new MergeResolution("merge.name-conflict", global.Name, "kept destination",
                    "The destination defines '" + global.Name + "' differently; the copied formulas use the destination's definition."));
            }

            return;
        }

        _dstWb.Names.Add(global.Name, formula, null, global.IsHidden);
        _r.Count("names");
    }

    private void CopyNames()
    {
        foreach (DefinedName n in SrcWb.Names.ForSheet(_src).ToList())
        {
            string formula = RewriteFormula(n.Formula, 1, 1);
            if (n.IsBuiltIn)
            {
                _dstWb.Names.AddBuiltIn(n.BuiltIn, _dst, formula);
            }
            else
            {
                _dstWb.Names.Add(n.Name, formula, _dst, n.IsHidden);
            }

            _r.Count("names");
        }
    }

    // ---- tables -------------------------------------------------------------------------------------------------------------------

    private void CopyTables()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Worksheet s in _dstWb.Sheets.Where(s => s.Kind == SheetKind.Worksheet))
        {
            foreach (Table t in s.Tables)
            {
                used.Add(t.Name);
            }
        }

        foreach (DefinedName n in _dstWb.Names)
        {
            used.Add(n.Name);
        }

        foreach (Table t in _src.Tables.ToList())
        {
            string name = t.Name;
            for (int k = 2; used.Contains(name); k++)
            {
                name = t.Name + "_" + k.ToString(CultureInfo.InvariantCulture);
            }

            used.Add(name);
            _tableNames[t.Name] = name;
            if (name != t.Name)
            {
                _r.Resolve(new MergeResolution("merge.table-renamed", t.Name, "renamed to " + name, "Table names are unique in a workbook."));
            }
        }
    }

    private void AddTablesAfterCells()
    {
        foreach (Table t in _src.Tables.ToList())
        {
            bool hasHeader = t.HeaderRowCount > 0;
            Table added = _dst.Tables.Add(_tableNames[t.Name], t.Range, hasHeader, t.StyleName);
            _r.Count("tables");
            if (t.TotalsRowCount > 0 || t.Columns.Any(c => c.CalculatedColumnFormula is not null || c.TotalsRowFunction is not null))
            {
                _r.Loss.Report("merge.table-detail", "Table totals row and calculated columns", 1,
                    "Table '" + t.Name + "' was copied with its range, header and style; its totals row settings and calculated column formulas were not carried over.");
            }

            _ = added;
        }
    }

    // ---- the rest of the sheet ----------------------------------------------------------------------------------------------------

    private void CopyFrame()
    {
        AddTablesAfterCells();
        MarkupNode srcRoot = _src.FrameRoot;
        MarkupNode dstRoot = _dst.FrameRoot;
        foreach (MarkupNode e in srcRoot.ChildElements.ToList())
        {
            string local = e.LocalNameString;
            if (Array.IndexOf(FrameElements, local) < 0)
            {
                continue;
            }

            var options = new MarkupFragmentOptions
            {
                RewriteAttribute = (MarkupNode el, in MarkupAttributeRef a) =>
                {
                    string al = Encoding.UTF8.GetString(a.LocalName);
                    string el2 = el.LocalNameString;
                    if (al == "tabSelected" || (el2 == "pageSetup" && a.Prefix.Length > 0 && al == "id"))
                    {
                        return AttributeEdit.Remove;
                    }

                    if (el2 == "cfRule" && al == "dxfId" && int.TryParse(a.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int dxf))
                    {
                        return AttributeEdit.Set(MapDxf(dxf).ToString(CultureInfo.InvariantCulture));
                    }

                    return AttributeEdit.Keep;
                },
                RewriteText = t => t.Parent is { IsNull: false } parent && parent.LocalNameString is "formula" or "formula1" or "formula2"
                    ? RewriteFormula(t.InnerText, 1, 1) is var f && f != t.InnerText ? f : null
                    : null,
            };
            string xml = Encoding.UTF8.GetString(MarkupFragment.Serialize(e, dstRoot, options));
            if (local == "sheetViews")
            {
                xml = xml.Replace(" tabSelected=\"1\"", string.Empty, StringComparison.Ordinal);
            }

            _dst.InsertFrameElement(local, xml);
            if (local is "conditionalFormatting")
            {
                _r.Count("conditional-formats");
            }
            else if (local is "dataValidations")
            {
                _r.Count("data-validations");
            }
        }

        _dst.NoteFrameEdited();
        if (srcRoot.ChildElements.Any(e => e.LocalNameString == "extLst"))
        {
            _r.Loss.Report("merge.sheet-extensions", "Sheet extension list", 1,
                "The sheet's extension list (x14 conditional formats, sparklines, slicer links and the like) was not copied.");
        }
    }

    private void CopyHyperlinks()
    {
        MarkupNode hs = default;
        foreach (MarkupNode e in _src.FrameRoot.ChildElements)
        {
            if (e.LocalNameString == "hyperlinks")
            {
                hs = e;
            }
        }

        if (hs.IsNull || _dst.PartName is not { } dstPart)
        {
            return;
        }

        var xml = new StringBuilder();
        string pc = Xl.Prefix(_dst.FrameRoot);
        var rels = new List<Relationship>(_dstWb.Package.GetRelationships(dstPart));
        IReadOnlyList<Relationship> srcRels = _src.PartName is { } sp ? SrcWb.Package.GetRelationships(sp) : [];
        long count = 0;
        foreach (MarkupNode h in hs.ChildElements)
        {
            string rid = h.GetAttribute(RelNs, "id") ?? h.GetAttribute(RelNsStrict, "id") ?? string.Empty;
            string attrs = string.Empty;
            foreach (MarkupAttributeRef a in h.Attributes)
            {
                if (a.IsNamespaceDeclaration || a.Prefix.Length > 0)
                {
                    continue;
                }

                string value = a.Value;
                if (Encoding.UTF8.GetString(a.LocalName) == "location")
                {
                    value = RewriteLocation(value);
                }

                attrs += " " + Encoding.UTF8.GetString(a.LocalName) + "=\"" + Escape(value) + "\"";
            }

            if (rid.Length > 0)
            {
                Relationship? rel = srcRels.FirstOrDefault(r => r.Id == rid);
                if (rel is null)
                {
                    continue;
                }

                int n = 1;
                while (rels.Exists(r => r.Id == "rId" + n.ToString(CultureInfo.InvariantCulture)))
                {
                    n++;
                }

                string newId = "rId" + n.ToString(CultureInfo.InvariantCulture);
                rels.Add(rel with { Id = newId });
                attrs += " r:id=\"" + newId + "\"";
            }

            xml.Append('<').Append(pc).Append("hyperlink").Append(attrs).Append("/>");
            count++;
        }

        if (count > 0)
        {
            _dstWb.Package.SetRelationships(dstPart, rels);
            _dst.InsertFrameElement("hyperlinks", "<" + pc + "hyperlinks xmlns:r=\"" + RelNs + "\">" + xml + "</" + pc + "hyperlinks>");
            _dst.NoteFrameEdited();
            _r.Count("hyperlinks", count);
        }
    }

    private const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string RelNsStrict = "http://purl.oclc.org/ooxml/officeDocument/relationships";

    // A hyperlink location is a reference such as Sheet1!A1 or a name; the part about this sheet follows its new name.
    private string RewriteLocation(string location)
    {
        string quoted = "'" + _src.Name.Replace("'", "''", StringComparison.Ordinal) + "'!";
        if (_dst.Name == _src.Name)
        {
            return location;
        }

        string target = FormulaNeedsQuotes(_dst.Name) ? "'" + _dst.Name.Replace("'", "''", StringComparison.Ordinal) + "'!" : _dst.Name + "!";
        if (location.StartsWith(quoted, StringComparison.OrdinalIgnoreCase))
        {
            return target + location[quoted.Length..];
        }

        return location.StartsWith(_src.Name + "!", StringComparison.OrdinalIgnoreCase) ? target + location[(_src.Name.Length + 1)..] : location;
    }

    private static bool FormulaNeedsQuotes(string name) => name.Any(c => !(char.IsLetterOrDigit(c) || c == '_' || c == '.'));

    private static string Escape(string s) => s.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal);

    private void ReportUncopied()
    {
        if (_src.PartName is not { } part)
        {
            return;
        }

        foreach (IGrouping<string, Relationship> g in SrcWb.Package.GetRelationships(part)
            .Where(r => r.TargetMode == RelationshipTargetMode.Internal)
            .GroupBy(r => r.Type[(r.Type.LastIndexOf('/') + 1)..]))
        {
            if (g.Key is "table" or "printerSettings" or "hyperlink")
            {
                continue;
            }

            _r.Problem("merge.sheet-part-not-copied", "The sheet has " + g.Count().ToString(CultureInfo.InvariantCulture) + " " + g.Key + " part(s) (drawings, charts, comments, pivot tables and the like) that were not copied.",
                DefectLocation.InPart(part));
        }
    }

    // ---- external links -----------------------------------------------------------------------------------------------------------

    private readonly Dictionary<string, string> _linkMap = new(StringComparer.Ordinal);

    // The source's own link [n] becomes a link of the destination with the same target and cached values (the part is copied whole).
    private string MapExternalLink(string index)
    {
        if (_same || !int.TryParse(index, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n < 1 || n > SrcWb.ExternalLinks.Count)
        {
            return index;
        }

        if (_linkMap.TryGetValue(index, out string? done))
        {
            return done;
        }

        ExternalLink link = SrcWb.ExternalLinks[n - 1];
        IPackagePart? part = SrcWb.Package.FindPart(link.PartName);
        if (part is null)
        {
            return index;
        }

        string name = "/xl/externalLinks/externalLink" + (_dstWb.Package.Parts.Count + 1000).ToString(CultureInfo.InvariantCulture) + ".xml";
        for (int k = 1; _dstWb.Package.FindPart(name) is not null; k++)
        {
            name = "/xl/externalLinks/externalLink" + (_dstWb.Package.Parts.Count + 1000 + k).ToString(CultureInfo.InvariantCulture) + ".xml";
        }

        _dstWb.Package.AddPart(name, part.ContentType, part.GetBytes());
        _dstWb.Package.SetRelationships(name, SrcWb.Package.GetRelationships(link.PartName).ToList());
        int dstIndex = RegisterLinkPart(name);
        // The links the destination already had come first, so the new index is stable even when more are added later.
        _linkMap[index] = dstIndex.ToString(CultureInfo.InvariantCulture);
        _externalIndex = Math.Max(_externalIndex, dstIndex + 1);
        return _linkMap[index];
    }

    private int RegisterLinkPart(string partName)
    {
        string rid = NewWorkbookRelationshipId();
        var rels = new List<Relationship>(_dstWb.Package.GetRelationships(_dstWb.MainPartName))
        {
            new Relationship(rid, "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLink", Relative(_dstWb.MainPartName, partName)),
        };
        _dstWb.Package.SetRelationships(_dstWb.MainPartName, rels);
        MarkupNode root = _dstWb.Markup.Root;
        string pc = Xl.Prefix(root);
        MarkupNode existing = Xl.Child(root, "externalReferences");
        string refXml = "<" + pc + "externalReference xmlns:r=\"" + RelNs + "\" r:id=\"" + rid + "\"/>";
        if (existing.IsNull)
        {
            Xl.InsertInOrder(root, WorkbookOrder, "externalReferences", "<" + pc + "externalReferences>" + refXml + "</" + pc + "externalReferences>");
        }
        else
        {
            existing.AppendXml(Encoding.UTF8.GetBytes(refXml));
        }

        _dstWb.ResetExternalLinksCache();
        return Xl.Child(root, "externalReferences").ChildElements.Count();
    }

    private string NewWorkbookRelationshipId()
    {
        var rels = _dstWb.Package.GetRelationships(_dstWb.MainPartName);
        int n = 1;
        while (rels.Any(r => r.Id == "rId" + n.ToString(CultureInfo.InvariantCulture)))
        {
            n++;
        }

        return "rId" + n.ToString(CultureInfo.InvariantCulture);
    }

    private static string Relative(string from, string to)
    {
        string[] f = from.TrimStart('/').Split('/');
        string[] t = to.TrimStart('/').Split('/');
        int common = 0;
        while (common < f.Length - 1 && common < t.Length - 1 && string.Equals(f[common], t[common], StringComparison.OrdinalIgnoreCase))
        {
            common++;
        }

        return string.Concat(Enumerable.Repeat("../", f.Length - 1 - common)) + string.Join('/', t[common..]);
    }

    // The link to the sheets left behind: sheet names in the order formulas number them (sheetId is the position), and the cells the copy reads.
    private void WriteExternalLink()
    {
        if (_externalOrder.Count == 0)
        {
            return;
        }

        string name = "/xl/externalLinks/externalLink" + _externalIndex.ToString(CultureInfo.InvariantCulture) + ".xml";
        for (int k = 1; _dstWb.Package.FindPart(name) is not null; k++)
        {
            name = "/xl/externalLinks/externalLink" + (_externalIndex + k).ToString(CultureInfo.InvariantCulture) + ".xml";
        }

        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<externalLink xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"").Append(RelNs).Append("\">");
        sb.Append("<externalBook r:id=\"rId1\"><sheetNames>");
        foreach (string s in _externalOrder)
        {
            sb.Append("<sheetName val=\"").Append(Escape(s)).Append("\"/>");
        }

        sb.Append("</sheetNames><sheetDataSet>");
        for (int i = 0; i < _externalOrder.Count; i++)
        {
            sb.Append("<sheetData sheetId=\"").Append(i.ToString(CultureInfo.InvariantCulture)).Append("\">");
            SrcWb.Sheets.TryGet(_externalOrder[i], out Worksheet? other);
            foreach (IGrouping<int, (int Row, int Col)> row in _external[_externalOrder[i]].GroupBy(c => c.Row))
            {
                sb.Append("<row r=\"").Append(row.Key.ToString(CultureInfo.InvariantCulture)).Append("\">");
                foreach ((int r, int c) in row)
                {
                    CellValue v = other is { Kind: SheetKind.Worksheet } ? other[r, c].Value : default;
                    string addr = new CellAddress(r, c).ToString();
                    switch (v.Kind)
                    {
                        case CellValueKind.Number:
                            sb.Append("<cell r=\"").Append(addr).Append("\"><v>").Append(v.Number.ToString("R", CultureInfo.InvariantCulture)).Append("</v></cell>");
                            break;
                        case CellValueKind.Boolean:
                            sb.Append("<cell r=\"").Append(addr).Append("\" t=\"b\"><v>").Append(v.Number != 0 ? "1" : "0").Append("</v></cell>");
                            break;
                        case CellValueKind.Error:
                            sb.Append("<cell r=\"").Append(addr).Append("\" t=\"e\"><v>").Append(CellErrors.ToText(v.Error)).Append("</v></cell>");
                            break;
                        case CellValueKind.Text:
                            sb.Append("<cell r=\"").Append(addr).Append("\" t=\"str\"><v>").Append(Escape(other!.GetText(v))).Append("</v></cell>");
                            break;
                    }
                }

                sb.Append("</row>");
            }

            sb.Append("</sheetData>");
        }

        sb.Append("</sheetDataSet></externalBook></externalLink>");
        _dstWb.Package.AddPart(name, "application/vnd.openxmlformats-officedocument.spreadsheetml.externalLink+xml", Encoding.UTF8.GetBytes(sb.ToString()));
        string target = _o.SourceLinkTarget ?? (SrcWb.FilePath is { } fp ? Path.GetFileName(fp) : "Source.xlsx");
        _dstWb.Package.SetRelationships(name, [new Relationship("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLinkPath", target, RelationshipTargetMode.External)]);
        int index = RegisterLinkPart(name);
        _r.Count("external-links");
        _r.Loss.Report("merge.external-references", "References to sheets that stayed behind", _externalOrder.Count,
            "Formulas that referred to other sheets of the source workbook (" + string.Join(", ", _externalOrder) + ") now refer to '" + target + "' as an external link, with the values they read cached.");
        _ = index;
    }

    // ---- moving -------------------------------------------------------------------------------------------------------------------

    public static int CountReferencesTo(Worksheet moved)
    {
        int count = 0;
        foreach (Worksheet s in moved.Workbook.Sheets)
        {
            if (ReferenceEquals(s, moved) || s.Kind != SheetKind.Worksheet)
            {
                continue;
            }

            foreach (RowData row in s.Store.Rows)
            {
                for (int i = 0; i < row.Count; i++)
                {
                    if ((row.Kinds[i] & RowData.HasFormulaBit) != 0 && row.Formulas is { } fa && fa[i] != 0
                        && s.Store.FormulaAt(fa[i]) is { } f && f.Text.Contains(moved.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        count++;
                    }
                }
            }
        }

        return count;
    }
}
