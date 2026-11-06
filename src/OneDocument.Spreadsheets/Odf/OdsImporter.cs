using System.Buffers;
using System.Globalization;
using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Calc.OpenFormula;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Markup.Xml;
using RecursiveAI.OneDocument.NumberFormats;
using RecursiveAI.OneDocument.Odf;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Spreadsheets.Odf;

// Converts an OpenDocument spreadsheet (ODS, OTS, flat FODS; OpenDocument 1.3 Part 1 §9 "Spreadsheet Document Content") into an in-memory
// XLSX package, which the workbook hub then wraps. This file holds the workbook level; styles, sheets and features are in the other parts.
internal sealed partial class OdsImporter
{
    private const string MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string RelBase = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private const string CtBase = "application/vnd.openxmlformats-officedocument.spreadsheetml.";
    private const string CalcExt = "urn:org:documentfoundation:names:experimental:calc:xmlns:calcext:1.0";
    private const string Table = OdfNamespaces.Table;
    private const string Office = OdfNamespaces.Office;
    private const string Style = OdfNamespaces.Style;
    private const string Text = OdfNamespaces.Text;
    private const string Fo = OdfNamespaces.Fo;

    private readonly OdfPackage _pkg;
    private readonly OpenContext _ctx;
    private readonly ILossSink _loss;
    private readonly CancellationToken _ct;
    private readonly OdfStyleSet _styles;
    private DateSystem _dateSystem = DateSystem.Date1900;
    private long _budget;

    public OdsImporter(OdfPackage pkg, OpenContext ctx, ILossSink loss, CancellationToken ct)
    {
        _pkg = pkg;
        _ctx = ctx;
        _loss = loss;
        _ct = ct;
        _styles = pkg.Styles;
        _budget = ctx.Limits.MaxRecordCount;
    }

    private void Defect(string code, DefectKind kind, DefectSeverity severity, string message, DefectLocation location) =>
        _ctx.Defects.Report(new Defect(kind, code, severity, message, location));

    private void Lost(string code, string feature, string message, long count = 1) => _loss.Report(code, feature, count, message);

    private static DefectLocation At(MarkupNode node) => DefectLocation.InPart(OdfPackage.ContentName, Math.Max(0, node.IsNull ? 0 : node.SourceStart));

    private static string? A(MarkupNode n, string ns, string local) => n.IsNull ? null : n.GetAttribute(ns, local);

    private static MarkupNode Child(MarkupNode n, string ns, string local) => OdfMarkup.Child(n, ns, local);

    private static string Inv(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string I(long value) => value.ToString(CultureInfo.InvariantCulture);

    public OpcPackage Convert()
    {
        MarkupNode spreadsheet = Child(_pkg.Body, Office, "spreadsheet");
        if (spreadsheet.IsNull)
        {
            throw new OfficeFormatException(DefectKind.Structure, "ods.not-spreadsheet", "The document is not an OpenDocument spreadsheet.", DefectLocation.InPart(OdfPackage.ContentName, 0));
        }

        var package = OpcPackage.Create(_ctx.Limits);
        try
        {
            ReadCalculationSettings(spreadsheet);
            ReadSettings();
            var tables = new List<MarkupNode>();
            foreach (MarkupNode c in spreadsheet.ChildElements)
            {
                if (c.IsElement(Table, "table"))
                {
                    tables.Add(c);
                }
                else if (c.NamespaceUri == Table && c.LocalNameString is "data-pilot-tables" or "consolidation" or "dde-links" or "label-ranges" or "tracked-changes")
                {
                    Lost("loss." + c.LocalNameString, c.LocalNameString, "Spreadsheet content without a counterpart here was not carried over: " + c.LocalNameString + ".");
                }
            }

            var sheetNames = new List<string>();
            foreach (MarkupNode t in tables)
            {
                sheetNames.Add(UniqueSheetName(A(t, Table, "name"), sheetNames.Count + 1, t));
            }

            if (tables.Count == 0)
            {
                Defect("ods.no-sheets", DefectKind.Structure, DefectSeverity.Repaired, "The spreadsheet has no table; an empty sheet was added.", At(spreadsheet));
                sheetNames.Add("Sheet1");
            }

            for (int i = 0; i < sheetNames.Count; i++)
            {
                _sheetIndex[sheetNames[i]] = i;
            }

            PrepareStyles();
            ReadValidationDefinitions(spreadsheet);
            ReadNames(Child(spreadsheet, Table, "named-expressions"), null);
            ReadDatabaseRanges(Child(spreadsheet, Table, "database-ranges"));

            var parts = new List<(string Name, string ContentType, ReadOnlyMemory<byte> Content)>();
            var workbookRels = new List<Relationship>();
            for (int i = 0; i < tables.Count; i++)
            {
                _ct.ThrowIfCancellationRequested();
                WriteWorksheet(package, tables[i], sheetNames[i], i, parts, workbookRels);
            }

            if (tables.Count == 0)
            {
                string empty = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<worksheet xmlns=\"" + MainNs + "\"><sheetData/></worksheet>";
                parts.Add(("/xl/worksheets/sheet1.xml", CtBase + "worksheet+xml", Encoding.UTF8.GetBytes(empty)));
                workbookRels.Add(new Relationship("rId1", RelBase + "worksheet", "worksheets/sheet1.xml"));
                _sheetRelIds.Add("rId1");
            }

            workbookRels.Add(new Relationship("rIdStyles", RelBase + "styles", "styles.xml"));
            foreach ((string name, string contentType, ReadOnlyMemory<byte> content) in parts)
            {
                package.AddPart(name, contentType, content);
            }

            package.AddPart("/xl/styles.xml", CtBase + "styles+xml", BuildStyles());
            package.AddPart("/xl/workbook.xml", CtBase + "sheet.main+xml", BuildWorkbook(spreadsheet, sheetNames));
            package.SetRelationships("/xl/workbook.xml", workbookRels);

            var rootRels = new List<Relationship> { new("rId1", RelBase + "officeDocument", "xl/workbook.xml") };
            AddProperties(package, rootRels);
            package.SetRelationships(null, rootRels);
            return package;
        }
        catch
        {
            package.Dispose();
            throw;
        }
    }

    private readonly Dictionary<string, int> _sheetIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _sheetNameSet = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _sheetRelIds = [];
    private readonly List<string> _sheetStates = [];

    // ECMA-376 Part 1 §18.2.19 (sheet names): at most 31 characters, none of []:*?/\, not empty, unique ignoring case.
    private string UniqueSheetName(string? raw, int number, MarkupNode at)
    {
        string name = raw ?? string.Empty;
        var sb = new StringBuilder();
        foreach (char ch in name)
        {
            if (ch is not ('[' or ']' or ':' or '*' or '?' or '/' or '\\'))
            {
                sb.Append(ch);
            }
        }

        string candidate = sb.ToString().Trim('\'');
        if (candidate.Length > 31)
        {
            candidate = candidate[..31];
        }

        if (candidate.Length == 0)
        {
            candidate = "Sheet" + I(number);
        }

        string unique = candidate;
        for (int n = 2; !_sheetNameSet.Add(unique); n++)
        {
            string suffix = "_" + I(n);
            unique = (candidate.Length + suffix.Length > 31 ? candidate[..(31 - suffix.Length)] : candidate) + suffix;
        }

        if (unique != name)
        {
            Defect("ods.sheet-name", DefectKind.Structure, DefectSeverity.Repaired, "The sheet name '" + name + "' is not valid in a workbook or not unique; '" + unique + "' was used.", At(at));
            _renamedSheets[name] = unique;
        }

        return unique;
    }

    private readonly Dictionary<string, string> _renamedSheets = new(StringComparer.OrdinalIgnoreCase);

    private string SheetOf(string name) => _renamedSheets.TryGetValue(name, out string? n) ? n : name;

    // OpenDocument 1.3 Part 1 §19.? table:calculation-settings: null-date (the day serial 0 stands for) and iteration.
    private bool _iterate;
    private double _iterateDelta = 0.001;
    private int _iterateCount = 100;

    private void ReadCalculationSettings(MarkupNode spreadsheet)
    {
        MarkupNode settings = Child(spreadsheet, Table, "calculation-settings");
        if (settings.IsNull)
        {
            return;
        }

        MarkupNode nullDate = Child(settings, Table, "null-date");
        if (!nullDate.IsNull)
        {
            string? date = A(nullDate, Table, "date-value");
            if (date is not null && date.StartsWith("1904-01-01", StringComparison.Ordinal))
            {
                _dateSystem = DateSystem.Date1904;
            }
            else if (date is not null && !date.StartsWith("1899-12-30", StringComparison.Ordinal) && !date.StartsWith("1899-12-31", StringComparison.Ordinal))
            {
                Lost("loss.null-date", "date origin", "The spreadsheet counts days from " + date + "; dates are converted as if the origin were 1899-12-30.");
            }
        }

        MarkupNode iteration = Child(settings, Table, "iteration");
        if (!iteration.IsNull && A(iteration, Table, "status") != "disable")
        {
            _iterate = A(iteration, Table, "status") == "enable";
            if (double.TryParse(A(iteration, Table, "maximum-difference"), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && d > 0)
            {
                _iterateDelta = d;
            }

            if (int.TryParse(A(iteration, Table, "steps"), NumberStyles.None, CultureInfo.InvariantCulture, out int steps) && steps > 0)
            {
                _iterateCount = steps;
            }
        }
    }

    private ReadOnlyMemory<byte> BuildWorkbook(MarkupNode spreadsheet, List<string> sheetNames)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new XmlWriter(buffer);
        w.WriteXmlDeclaration();
        w.WriteStartElement("workbook");
        w.WriteNamespaceDeclaration(null, MainNs);
        w.WriteNamespaceDeclaration("r", RNs);
        if (_dateSystem == DateSystem.Date1904)
        {
            w.WriteStartElement("workbookPr");
            w.WriteAttribute("date1904", "1");
            w.WriteEndElement();
        }

        if (A(spreadsheet, Table, "structure-protected") == "true")
        {
            w.WriteStartElement("workbookProtection");
            w.WriteAttribute("lockStructure", "1");
            w.WriteEndElement();
            if (A(spreadsheet, Table, "protection-key") is not null)
            {
                Lost("loss.protection-password", "protection password", "A protection password is stored as a hash that cannot be carried to the workbook; the structure is protected without a password.");
            }
        }

        if (_activeSheet > 0 && _activeSheet < sheetNames.Count)
        {
            w.WriteStartElement("bookViews");
            w.WriteStartElement("workbookView");
            w.WriteAttribute("activeTab", I(_activeSheet));
            w.WriteEndElement();
            w.WriteEndElement();
        }

        w.WriteStartElement("sheets");
        for (int i = 0; i < sheetNames.Count; i++)
        {
            w.WriteStartElement("sheet");
            w.WriteAttribute("name", sheetNames[i]);
            w.WriteAttribute("sheetId", I(i + 1));
            if (i < _sheetStates.Count && _sheetStates[i] is { Length: > 0 } state)
            {
                w.WriteAttribute("state", state);
            }

            w.WriteAttribute("r:id", i < _sheetRelIds.Count ? _sheetRelIds[i] : "rId1");
            w.WriteEndElement();
        }

        w.WriteEndElement();
        if (_names.Count > 0)
        {
            w.WriteStartElement("definedNames");
            foreach (DefinedNameInfo n in _names)
            {
                w.WriteStartElement("definedName");
                w.WriteAttribute("name", n.Name);
                if (n.Sheet is { } s)
                {
                    w.WriteAttribute("localSheetId", I(s));
                }

                if (n.Hidden)
                {
                    w.WriteAttribute("hidden", "1");
                }

                w.WriteText(n.Formula);
                w.WriteEndElement();
            }

            w.WriteEndElement();
        }

        if (_iterate)
        {
            w.WriteStartElement("calcPr");
            w.WriteAttribute("iterate", "1");
            w.WriteAttribute("iterateCount", I(_iterateCount));
            w.WriteAttribute("iterateDelta", Inv(_iterateDelta));
            w.WriteEndElement();
        }

        w.WriteEndElement();
        w.Complete();
        return buffer.WrittenMemory;
    }

    private int _activeSheet;

    // ---- document properties ---------------------------------------------------------------------------------------------------------

    private void AddProperties(OpcPackage package, List<Relationship> rootRels)
    {
        OdfMetadata meta = _pkg.Metadata;
        var core = new StringBuilder();
        void Add(string element, string? value, bool date = false)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            if (date)
            {
                if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset dto))
                {
                    Defect("ods.bad-date", DefectKind.Value, DefectSeverity.Dropped, "The date '" + value + "' in the document properties is not a date.", DefectLocation.InPart(OdfPackage.MetaName, 0));
                    return;
                }

                core.Append('<').Append(element).Append(" xsi:type=\"dcterms:W3CDTF\">").Append(dto.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)).Append("</").Append(element).Append('>');
                return;
            }

            core.Append('<').Append(element).Append('>').Append(OdfLengths.Escape(value)).Append("</").Append(element).Append('>');
        }

        // The schema fixes the order of the core properties.
        Add("dc:title", meta.Title);
        Add("dc:subject", meta.Subject);
        Add("dc:creator", meta.Creator);
        Add("cp:keywords", string.Join(", ", meta.Keywords));
        Add("dc:description", meta.Description);
        Add("cp:lastModifiedBy", meta.LastModifiedBy);
        Add("dcterms:created", meta.Created, date: true);
        Add("dcterms:modified", meta.Modified, date: true);
        Add("dc:language", meta.Language);
        if (core.Length > 0)
        {
            string xml = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">"
                + core + "</cp:coreProperties>";
            package.AddPart("/docProps/core.xml", "application/vnd.openxmlformats-package.core-properties+xml", Encoding.UTF8.GetBytes(xml));
            rootRels.Add(new Relationship("rId2", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties", "docProps/core.xml"));
        }

        IReadOnlyList<KeyValuePair<string, string>> custom = meta.UserDefined;
        if (custom.Count > 0)
        {
            var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/custom-properties\" xmlns:vt=\"http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes\">");
            int pid = 2;
            foreach (KeyValuePair<string, string> kv in custom)
            {
                sb.Append("<property fmtid=\"{D5CDD505-2E9C-101B-9397-08002B2CF9AE}\" pid=\"").Append(pid++).Append("\" name=\"").Append(OdfLengths.Escape(kv.Key)).Append("\"><vt:lpwstr>")
                    .Append(OdfLengths.Escape(kv.Value)).Append("</vt:lpwstr></property>");
            }

            sb.Append("</Properties>");
            package.AddPart("/docProps/custom.xml", "application/vnd.openxmlformats-officedocument.custom-properties+xml", Encoding.UTF8.GetBytes(sb.ToString()));
            rootRels.Add(new Relationship("rId4", RelBase + "custom-properties", "docProps/custom.xml"));
        }
    }

    // ---- addresses ----------------------------------------------------------------------------------------------------------------------

    // A cell range address of the document (OpenDocument 1.3 Part 1 §9.? "cell range address"): $Sheet.$A$1:.$B$2, 'My Sheet'.A1:'My Sheet'.B2, Sheet.1:Sheet.3.
    internal readonly record struct Range(string? Sheet, int Row1, int Col1, int Row2, int Col2, bool WholeRows, bool WholeColumns)
    {
        public bool IsSingle => Row1 == Row2 && Col1 == Col2;
    }

    // Reads one address at p and advances. Returns false when the text at p is not an address.
    internal static bool TryReadRange(string s, ref int p, out Range range)
    {
        range = default;
        int start = p;
        while (p < s.Length && s[p] == ' ')
        {
            p++;
        }

        if (!ReadCorner(s, ref p, out string? sheet1, out int r1, out int c1))
        {
            p = start;
            return false;
        }

        string? sheet = sheet1;
        int r2 = r1;
        int c2 = c1;
        if (p < s.Length && s[p] == ':')
        {
            p++;
            if (!ReadCorner(s, ref p, out _, out r2, out c2))
            {
                p = start;
                return false;
            }
        }

        bool wholeRows = c1 == 0 && c2 == 0;
        bool wholeColumns = r1 == 0 && r2 == 0;
        range = new Range(sheet, Math.Min(r1, r2), Math.Min(c1, c2), Math.Max(r1, r2), Math.Max(c1, c2), wholeRows, wholeColumns);
        return true;
    }

    private static bool ReadCorner(string s, ref int p, out string? sheet, out int row, out int column)
    {
        sheet = null;
        row = 0;
        column = 0;
        int q = p;
        if (q < s.Length && s[q] == '$')
        {
            q++;
        }

        if (q < s.Length && s[q] == '\'')
        {
            q++;
            var sb = new StringBuilder();
            while (q < s.Length)
            {
                if (s[q] == '\'')
                {
                    if (q + 1 < s.Length && s[q + 1] == '\'')
                    {
                        sb.Append('\'');
                        q += 2;
                        continue;
                    }

                    q++;
                    break;
                }

                sb.Append(s[q++]);
            }

            sheet = sb.ToString();
        }
        else
        {
            int ns = q;
            while (q < s.Length && s[q] is not ('.' or ':' or ' '))
            {
                q++;
            }

            if (q > ns)
            {
                sheet = s[ns..q];
            }
        }

        if (q >= s.Length || s[q] != '.')
        {
            return false;
        }

        q++;
        if (q < s.Length && s[q] == '$')
        {
            q++;
        }

        int ls = q;
        while (q < s.Length && char.IsAsciiLetter(s[q]))
        {
            q++;
        }

        if (q > ls && !CellAddress.TryParseColumn(s.AsSpan(ls, q - ls), out column))
        {
            return false;
        }

        if (q < s.Length && s[q] == '$')
        {
            q++;
        }

        int ds = q;
        while (q < s.Length && char.IsAsciiDigit(s[q]))
        {
            q++;
        }

        if (q > ds && !int.TryParse(s.AsSpan(ds, q - ds), NumberStyles.None, CultureInfo.InvariantCulture, out row))
        {
            return false;
        }

        if (row == 0 && column == 0)
        {
            return false;
        }

        p = q;
        return true;
    }

    internal static List<Range> ReadRanges(string? text)
    {
        var list = new List<Range>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return list;
        }

        int p = 0;
        while (p < text.Length)
        {
            if (!TryReadRange(text, ref p, out Range r))
            {
                break;
            }

            list.Add(r);
            while (p < text.Length && text[p] == ' ')
            {
                p++;
            }
        }

        return list;
    }

    private static string Col(int c) => CellAddress.ColumnName(c);

    // The range as sqref text (relative, "A1:B2").
    internal static string RangeText(Range r)
    {
        if (r.WholeRows)
        {
            return I(r.Row1) + ":" + I(r.Row2);
        }

        if (r.WholeColumns)
        {
            return Col(r.Col1) + ":" + Col(r.Col2);
        }

        string a = Col(r.Col1) + I(r.Row1);
        return r.IsSingle ? a : a + ":" + Col(r.Col2) + I(r.Row2);
    }

    // The range as a reference in a defined name: Sheet!$A$1:$B$2.
    internal string AbsoluteText(Range r)
    {
        string body;
        if (r.WholeRows)
        {
            body = "$" + I(r.Row1) + ":$" + I(r.Row2);
        }
        else if (r.WholeColumns)
        {
            body = "$" + Col(r.Col1) + ":$" + Col(r.Col2);
        }
        else
        {
            body = "$" + Col(r.Col1) + "$" + I(r.Row1);
            if (!r.IsSingle)
            {
                body += ":$" + Col(r.Col2) + "$" + I(r.Row2);
            }
        }

        return r.Sheet is null ? body : new SheetPrefix(SheetOf(r.Sheet)).ToString() + body;
    }

    // ---- formulas -------------------------------------------------------------------------------------------------------------------

    // Reads the formula attribute (of:=..., or the Excel dialect msoxl:=...) into the A1 text a worksheet stores, without the equals sign.
    // Null when it cannot be read; the caller keeps the cached value and the defect says why.
    private string? FormulaText(string formula, MarkupNode at, string sheet, string address, int rowDelta = 0, int columnDelta = 0)
    {
        int colon = formula.IndexOf(':', StringComparison.Ordinal);
        string dialect = colon > 0 && colon + 1 < formula.Length && formula[colon + 1] == '=' ? formula[..colon].ToLowerInvariant() : "of";
        FormulaNode? node;
        FormulaSyntaxError error;

        // A cell whose formula attribute holds only the prefix has nothing to read; its cached value is the cell.
        if (colon > 0 && string.IsNullOrWhiteSpace(formula[(colon + 2)..]))
        {
            return null;
        }

        if (dialect == "of")
        {
            if (!OpenFormulaParser.TryParse(formula, out node, out error, new OpenFormulaParseOptions { Limits = _ctx.Limits }))
            {
                Defect("ods.bad-formula", DefectKind.Formula, DefectSeverity.Dropped, "The formula '" + Shorten(formula) + "' cannot be read (" + error.Code + "): " + error.Message, DefectLocation.InCell(sheet, address));
                Lost("loss.formulas", "formulas", "A formula that cannot be read keeps only its cached value.");
                return null;
            }
        }
        else if (dialect == "msoxl")
        {
            if (!FormulaParser.TryParse(formula[(colon + 2)..], out node, out error, new FormulaParserOptions { Limits = _ctx.Limits }))
            {
                Defect("ods.bad-formula", DefectKind.Formula, DefectSeverity.Dropped, "The formula '" + Shorten(formula) + "' cannot be read (" + error.Code + "): " + error.Message, DefectLocation.InCell(sheet, address));
                Lost("loss.formulas", "formulas", "A formula that cannot be read keeps only its cached value.");
                return null;
            }
        }
        else
        {
            Defect("ods.formula-dialect", DefectKind.Unsupported, DefectSeverity.Dropped, "The formula syntax '" + dialect + "' is not read; the cached value is kept.", DefectLocation.InCell(sheet, address));
            Lost("loss.formulas", "formulas", "A formula in a syntax other than OpenFormula keeps only its cached value.");
            return null;
        }

        _ = at;
        if (rowDelta != 0 || columnDelta != 0)
        {
            node = FormulaTranslator.Translate(node!, rowDelta, columnDelta);
        }

        string text = FormulaPrinter.Print(node!, new FormulaPrintOptions { Form = FormulaForm.File, Syntax = FormulaSyntax.A1 });
        return text.StartsWith('=') ? text[1..] : text;
    }

    private static string Shorten(string s) => s.Length <= 80 ? s : s[..80] + "...";
}
