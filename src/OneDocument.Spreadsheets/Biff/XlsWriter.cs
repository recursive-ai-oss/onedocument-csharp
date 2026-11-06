using System.Linq;
using System.Globalization;
using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Spreadsheets.Biff;

/// <summary>
/// Writes a workbook as one clean BIFF8 Workbook stream ([MS-XLS] §2.1.7.20). The hub is read through its public model (cells, styles, names,
/// validation, conditional formats) and its sheet frames; whatever BIFF8 cannot hold is reported to the loss sink, never dropped silently.
/// </summary>
internal sealed partial class XlsWriter
{
    // [MS-XLS] §2.1.7.20.5: a worksheet holds 65,536 rows and 256 columns.
    internal const int MaxRows = 65536;
    internal const int MaxColumns = 256;

    private readonly Workbook _wb;
    private readonly ILossSink _loss;
    private readonly CancellationToken _token;
    private readonly BiffSink _s = new();
    private readonly List<Worksheet> _sheets = [];
    private readonly Dictionary<Worksheet, int> _sheetIndex = [];
    private XlsStyleMap _styles = null!;
    private FormulaContext _fctx = null!;
    private readonly List<DefinedNameInfo> _names = [];
    private readonly Dictionary<string, int> _sst = new(StringComparer.Ordinal);
    private readonly List<string> _sstList = [];
    private long _sstReferences;
    private readonly Dictionary<long, Encoded>[] _formulas;
    private readonly List<SheetOut> _sheetOut = [];

    private sealed record Encoded(byte[] Rgce, byte[] Rgcb);

    private XlsWriter(Workbook wb, ILossSink loss, CancellationToken token)
    {
        _wb = wb;
        _loss = loss;
        _token = token;
        long chart = 0;
        foreach (Worksheet ws in wb.Sheets)
        {
            if (ws.Kind == SheetKind.Worksheet)
            {
                _sheetIndex[ws] = _sheets.Count;
                _sheets.Add(ws);
            }
            else
            {
                chart++;
            }
        }

        if (chart > 0)
        {
            loss.Report("loss.charts", "chart and dialog sheets", chart, "Chart sheets and dialog sheets are not written.");
        }

        _formulas = new Dictionary<long, Encoded>[_sheets.Count];
        for (int i = 0; i < _formulas.Length; i++)
        {
            _formulas[i] = [];
        }
    }

    /// <summary>Writes the workbook to a stream of BIFF8 records.</summary>
    /// <param name="wb">The workbook.</param>
    /// <param name="loss">Receives what BIFF8 cannot carry.</param>
    /// <param name="token">Cancels the write.</param>
    /// <returns>The Workbook stream.</returns>
    public static byte[] Write(Workbook wb, ILossSink loss, CancellationToken token)
    {
        if (wb.Sheets.Count == 0 || !wb.Sheets.Any(s => s.Kind == SheetKind.Worksheet))
        {
            throw new InvalidOperationException("A BIFF8 workbook needs at least one worksheet.");
        }

        var w = new XlsWriter(wb, loss, token);
        return w.Build();
    }

    private byte[] Build()
    {
        ReportWorkbookLosses();
        _styles = new XlsStyleMap(_wb.Styles, _loss);
        foreach (Worksheet ws in _sheets)
        {
            foreach (ConditionalFormat cf in ws.ConditionalFormats)
            {
                foreach (ConditionalFormatRule r in cf.Rules)
                {
                    if (r.DxfId is { } d && d >= 0 && d < _wb.Styles.DifferentialFormatCount)
                    {
                        _styles.Register(_wb.Styles.GetDifferentialFormat(d));
                    }

                    if (r.ColorScale is { } cs)
                    {
                        foreach (StyleColor c in cs.Colors)
                        {
                            _styles.Register(c);
                        }
                    }

                    if (r.DataBar is { } db)
                    {
                        _styles.Register(db.Color);
                    }
                }
            }
        }

        _styles.Freeze();
        _fctx = new FormulaContext { Version = 8, SheetNames = [.. _sheets.Select(s => s.Name)] };
        PrepareNames();
        for (int i = 0; i < _sheets.Count; i++)
        {
            EncodeSheetFormulas(i);
        }

        EncodeNames();
        CollectStrings();

        WriteGlobals();
        _token.ThrowIfCancellationRequested();
        for (int i = 0; i < _sheets.Count; i++)
        {
            _token.ThrowIfCancellationRequested();
            WriteSheet(i);
        }

        return _s.ToArray();
    }

    // ---- workbook-level losses ----

    private void ReportWorkbookLosses()
    {
        foreach (IPackagePart p in _wb.Package.Parts)
        {
            string n = p.Name;
            if (n.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase))
            {
                _loss.Report("loss.vba", "macro project", 1, "The macro project is not written.");
            }
            else if (n.StartsWith("/xl/externalLinks/", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                _loss.Report("loss.external-links", "external workbook links", 1, "Links to other workbooks are not written; formulas that use them keep their cached values.");
            }
            else if (n.StartsWith("/xl/pivotTables/", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                _loss.Report("loss.pivot-tables", "pivot tables", 1, "Pivot tables are not written; their cells keep their values.");
            }
            else if (n.StartsWith("/xl/charts/", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && !n.Contains("/_rels/", StringComparison.Ordinal)
                && p.ContentType.Contains("drawingml.chart+xml", StringComparison.Ordinal))
            {
                _loss.Report("loss.charts", "charts", 1, "Charts are not written.");
            }
            else if (n.StartsWith("/xl/media/", StringComparison.OrdinalIgnoreCase))
            {
                _loss.Report("loss.pictures", "pictures", 1, "Pictures are not written.");
            }
        }

    }

    // ---- sheet names ----

    private static string BiffSheetName(string name) => name.Length > 31 ? name[..31] : name;

    // ---- defined names ----

    private static readonly (string Text, int Code)[] BuiltInCodes =
    [
        ("Consolidate_Area", 0), ("Auto_Open", 1), ("Auto_Close", 2), ("Extract", 3), ("Database", 4), ("Criteria", 5), ("Print_Area", 6), ("Print_Titles", 7),
        ("Sheet_Title", 0x0C), ("_FilterDatabase", 0x0D),
    ];

    private readonly List<(DefinedNameInfo Info, string Text)> _nameTexts = [];

    private void PrepareNames()
    {
        foreach (DefinedName n in _wb.Names)
        {
            if (n.IsFunction || n.IsMacro || n.IsVbProcedure)
            {
                _loss.Report("loss.macro-names", "macro names", 1, "Names of macro functions are not written.");
                continue;
            }

            int scope = -1;
            if (n.LocalSheetId is { } lid)
            {
                Worksheet? sc = lid >= 0 && lid < _wb.Sheets.Count ? _wb.Sheets[lid] : null;
                if (sc is null || !_sheetIndex.TryGetValue(sc, out scope))
                {
                    _loss.Report("loss.defined-names", "defined names", 1, "A name scoped to a sheet that is not written was dropped.");
                    continue;
                }
            }

            var info = new DefinedNameInfo { SheetScope = scope, Hidden = n.IsHidden, Comment = n.Comment ?? string.Empty };
            if (n.IsBuiltIn)
            {
                string tail = n.Name["_xlnm.".Length..];
                int idx = Array.FindIndex(BuiltInCodes, b => string.Equals(b.Text, tail, StringComparison.OrdinalIgnoreCase));
                if (idx < 0 || scope < 0)
                {
                    _loss.Report("loss.defined-names", "defined names", 1, "A built-in name that BIFF8 does not know was dropped.");
                    continue;
                }

                info.IsBuiltIn = true;
                info.BuiltIn = BuiltInCodes[idx].Code;
                info.Name = tail;
            }
            else
            {
                info.Name = n.Name.Length > 255 ? n.Name[..255] : n.Name;
            }

            _names.Add(info);
            _nameTexts.Add((info, n.Formula));
        }

        // An autofilter needs its hidden _FilterDatabase name even when the hub does not list it.
        for (int i = 0; i < _sheets.Count; i++)
        {
            if (AutoFilterRange(_sheets[i]) is { } afr && !_names.Any(x => x.IsBuiltIn && x.BuiltIn == 0x0D && x.SheetScope == i))
            {
                var info = new DefinedNameInfo { SheetScope = i, Hidden = true, IsBuiltIn = true, BuiltIn = 0x0D, Name = "_FilterDatabase" };
                _names.Add(info);
                _nameTexts.Add((info, QuoteSheet(_sheets[i].Name) + "!" + AbsoluteRange(afr)));
            }
        }

        _fctx.Names.AddRange(_names);
    }

    private void EncodeNames()
    {
        for (int i = 0; i < _nameTexts.Count; i++)
        {
            (DefinedNameInfo info, string text) = _nameTexts[i];
            if (!TryEncodeText(text, 0, 0, out Encoded? e, out string why))
            {
                _loss.Report("loss.defined-names", "defined names", 1, "The name '" + info.Name + "' was dropped: " + why);
                info.Rgce = [];
                info.Formula = null;
                continue;
            }

            info.Rgce = e!.Rgce;
            info.Rgcb = e.Rgcb;
            info.Formula = text;
        }
    }

    internal static string QuoteSheet(string name)
    {
        bool plain = name.Length > 0 && (char.IsLetter(name[0]) || name[0] == '_') && name.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '.');
        return plain ? name : "'" + name.Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    private static string AbsoluteRange(CellRange r) =>
        "$" + XlsImporter.ColumnName(r.TopLeft.Column) + "$" + r.TopLeft.Row.ToString(CultureInfo.InvariantCulture) + ":$" + XlsImporter.ColumnName(r.BottomRight.Column) + "$" + r.BottomRight.Row.ToString(CultureInfo.InvariantCulture);

    // ---- formulas ----

    private bool TryEncodeText(string text, int row, int col, out Encoded? encoded, out string why)
    {
        encoded = null;
        why = string.Empty;
        FormulaNode tree;
        try
        {
            tree = FormulaParser.Parse(text.StartsWith('=') ? text[1..] : text);
        }
        catch (OfficeException ex)
        {
            why = ex.Message;
            return false;
        }
        catch (FormatException ex)
        {
            why = ex.Message;
            return false;
        }
        catch (ArgumentException ex)
        {
            why = ex.Message;
            return false;
        }

        return TryEncodeTree(tree, out encoded, out why);
    }

    private bool TryEncodeTree(FormulaNode tree, out Encoded? encoded, out string why)
    {
        var enc = new PtgEncoder(_fctx);
        if (!enc.TryEncode(tree, out byte[] rgce, out byte[] rgcb))
        {
            encoded = null;
            why = enc.Reason ?? "the formula has no BIFF8 form";
            return false;
        }

        // [MS-XLS] §2.5.198.2: a formula holds at most 8,192 token bytes.
        if (rgce.Length > 8192 - 1)
        {
            encoded = null;
            why = "the formula is longer than BIFF8 allows";
            return false;
        }

        encoded = new Encoded(rgce, rgcb);
        why = string.Empty;
        return true;
    }

    private static long Key(int row, int col) => ((long)row << 16) | (uint)col;

    // Parses each formula once, resolving shared formulas against their anchors, and encodes it. A formula that cannot be written keeps
    // its cached value and is counted.
    private void EncodeSheetFormulas(int si)
    {
        Worksheet ws = _sheets[si];
        Dictionary<long, Encoded> into = _formulas[si];
        var anchors = new Dictionary<int, (string Text, int Row, int Col)>();
        foreach (CellRowView row in ws.Store.EnumerateRows())
        {
            for (int i = 0; i < row.Count; i++)
            {
                CellFormula? f = row.FormulaAt(i);
                if (f is { Kind: CellFormulaKind.Shared, Text.Length: > 0, SharedIndex: >= 0 })
                {
                    anchors[f.SharedIndex] = (f.Text, row.Row, row.ColumnAt(i));
                }
            }
        }

        long dropped = 0;
        long dynamic = 0;
        foreach (CellRowView row in ws.Store.EnumerateRows())
        {
            if (row.Row > MaxRows)
            {
                continue;
            }

            for (int i = 0; i < row.Count; i++)
            {
                CellFormula? f = row.FormulaAt(i);
                int col = row.ColumnAt(i);
                if (f is null || col > MaxColumns)
                {
                    continue;
                }

                _token.ThrowIfCancellationRequested();
                if (f.Kind == CellFormulaKind.DataTable)
                {
                    continue;
                }

                string? text = f.Text;
                int dr = 0;
                int dc = 0;
                if (f.Kind == CellFormulaKind.Shared && text.Length == 0)
                {
                    if (!anchors.TryGetValue(f.SharedIndex, out var a))
                    {
                        dropped++;
                        continue;
                    }

                    text = a.Text;
                    dr = row.Row - a.Row;
                    dc = col - a.Col;
                }

                if (text.Length == 0)
                {
                    dropped++;
                    continue;
                }

                if (f.Kind == CellFormulaKind.Array && ws.Store.GetExtras(row.Row, col)?.Attributes?.Contains("cm=", StringComparison.Ordinal) == true)
                {
                    dynamic++;
                }

                FormulaNode tree;
                try
                {
                    tree = FormulaParser.Parse(text);
                    if (dr != 0 || dc != 0)
                    {
                        tree = FormulaTranslator.Translate(tree, dr, dc);
                    }
                }
                catch (Exception ex) when (ex is OfficeException or FormatException or ArgumentException or InvalidOperationException)
                {
                    dropped++;
                    continue;
                }

                if (TryEncodeTree(tree, out Encoded? e, out _))
                {
                    into[Key(row.Row - 1, col - 1)] = e!;
                }
                else
                {
                    dropped++;
                }
            }
        }

        if (dropped > 0)
        {
            _loss.Report("loss.formulas", "formulas", dropped, "Formulas BIFF8 cannot express (newer functions, structured references, lambdas, external links) keep their cached values as constants.");
        }

        if (dynamic > 0)
        {
            _loss.Report("loss.dynamic-arrays", "dynamic array formulas", dynamic, "Dynamic array formulas are written as legacy array formulas over their current range.");
        }
    }

    // ---- shared strings ----

    private void CollectStrings()
    {
        long rich = 0;
        foreach (Worksheet ws in _sheets)
        {
            foreach (CellRowView row in ws.Store.EnumerateRows())
            {
                if (row.Row > MaxRows)
                {
                    continue;
                }

                for (int i = 0; i < row.Count; i++)
                {
                    if (row.ColumnAt(i) > MaxColumns)
                    {
                        continue;
                    }

                    CellValue v = row.ValueAt(i);
                    if (v.Kind != CellValueKind.Text || row.HasFormulaAt(i))
                    {
                        continue;
                    }

                    if (v.StringRef >= 0 && v.StringRef < _wb.SharedStrings.Count && _wb.SharedStrings.IsRich(v.StringRef))
                    {
                        rich++;
                    }

                    string t = ws.GetText(v);
                    if (t.Length > 32767)
                    {
                        t = t[..32767];
                    }

                    _sstReferences++;
                    if (!_sst.ContainsKey(t))
                    {
                        _sst[t] = _sstList.Count;
                        _sstList.Add(t);
                    }
                }
            }
        }

        if (rich > 0)
        {
            _loss.Report("loss.rich-text", "rich text", rich, "Formatting runs inside cell text are not written; the text is kept.");
        }
    }
}
