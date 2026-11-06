using System.Buffers;
using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Markup.Xml;
using RecursiveAI.OneDocument.NumberFormats;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Spreadsheets.Xml2003;

// Converts a SpreadsheetML 2003 file into an XLSX package. This file holds the workbook level, the styles and the shared helpers; the
// worksheets are written in the other part of the class.
internal sealed partial class SpreadsheetMl2003Converter
{
    private const string MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string RelBase = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private const string CtBase = "application/vnd.openxmlformats-officedocument.spreadsheetml.";
    private const string SpreadsheetNs = "urn:schemas-microsoft-com:office:spreadsheet";

    private readonly MarkupDocument _doc;
    private readonly OpenContext _ctx;
    private readonly ILossSink _loss;
    private readonly CancellationToken _ct;
    private readonly Dictionary<string, Dictionary<string, string>> _styleDefs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _xfIndex = new(StringComparer.Ordinal);
    private readonly List<Dictionary<string, string>> _xfs = [];
    private readonly Dictionary<string, int> _fontIndex = new(StringComparer.Ordinal);
    private readonly List<string> _fonts = [];
    private readonly Dictionary<string, int> _fillIndex = new(StringComparer.Ordinal);
    private readonly List<string> _fills = [];
    private readonly Dictionary<string, int> _borderIndex = new(StringComparer.Ordinal);
    private readonly List<string> _borders = [];
    private readonly Dictionary<string, int> _numFmtIndex = new(StringComparer.Ordinal);
    private readonly List<string> _numFmts = [];
    private readonly Dictionary<string, int> _dxfIndex = new(StringComparer.Ordinal);
    private readonly List<string> _dxfs = [];
    private readonly List<(string Name, string Formula, int? Sheet, bool Hidden)> _names = [];
    private readonly HashSet<string> _nameKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _sheetNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _sheetIndexByName = new(StringComparer.OrdinalIgnoreCase);
    private DateSystem _dateSystem = DateSystem.Date1900;

    public SpreadsheetMl2003Converter(MarkupDocument doc, OpenContext ctx, ILossSink loss, CancellationToken ct)
    {
        _doc = doc;
        _ctx = ctx;
        _loss = loss;
        _ct = ct;
    }

    // ---- shared helpers ------------------------------------------------------------------------------------------------------------

    private static string Name(MarkupNode n) => n.LocalNameString;

    private static MarkupNode Child(MarkupNode parent, string local)
    {
        foreach (MarkupNode c in parent.ChildElements)
        {
            if (c.LocalNameString == local)
            {
                return c;
            }
        }

        return default;
    }

    // Attributes are read by local name whatever their prefix: files in the wild write ss:ID, ID, x:Range and Range alike.
    private static string? Attr(MarkupNode n, string local)
    {
        if (n.IsNull)
        {
            return null;
        }

        byte[] name = Encoding.UTF8.GetBytes(local);
        foreach (MarkupAttributeRef a in n.Attributes)
        {
            if (!a.IsNamespaceDeclaration && a.LocalName.SequenceEqual(name))
            {
                return a.Value;
            }
        }

        return null;
    }

    private static bool IsTrue(string? value) => value is "1" or "true" or "True" or "TRUE";

    private static int IntAttr(MarkupNode n, string local) =>
        int.TryParse(Attr(n, local), NumberStyles.None, CultureInfo.InvariantCulture, out int v) ? v : 0;

    private static string Inv(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private void Defect(string code, DefectSeverity severity, string message, MarkupNode at, DefectLocation? where = null) =>
        _ctx.Defects.Report(new Defect(DefectKindOf(code), code, severity, message, where ?? DefectLocation.InPart("/", Math.Max(0, at.IsNull ? 0 : at.SourceStart))));

    private static DefectKind DefectKindOf(string code) => code switch
    {
        "sml2003.bad-formula" => DefectKind.Formula,
        "sml2003.bad-number" or "sml2003.bad-date" or "sml2003.bad-value" => DefectKind.Value,
        "sml2003.bad-style-ref" => DefectKind.Reference,
        _ => DefectKind.Structure,
    };

    private void Lost(string code, string feature, string message, long count = 1) => _loss.Report(code, feature, count, message);

    // ---- workbook -----------------------------------------------------------------------------------------------------------------

    public OpcPackage Convert()
    {
        var package = OpcPackage.Create(_ctx.Limits);
        try
        {
            MarkupNode root = _doc.Root;
            if (root.IsNull || root.LocalNameString != "Workbook" || root.NamespaceUri != SpreadsheetNs)
            {
                throw new OfficeFormatException(DefectKind.Structure, "sml2003.not-spreadsheet", "The root element is not a SpreadsheetML 2003 Workbook.", DefectLocation.InPart("/", Math.Max(0, root.SourceStart)));
            }

            MarkupNode excel = Child(root, "ExcelWorkbook");
            if (!excel.IsNull)
            {
                MarkupNode d1904 = Child(excel, "Date1904");
                if (!d1904.IsNull && (d1904.InnerText.Trim() is "" or "True" or "true" or "1"))
                {
                    _dateSystem = DateSystem.Date1904;
                }
            }

            ReadStyles(root);
            _ = StyleIndex("Default"); // the default style is always cell format 0

            var sheets = new List<MarkupNode>();
            foreach (MarkupNode c in root.ChildElements)
            {
                if (Name(c) == "Worksheet")
                {
                    sheets.Add(c);
                }
            }

            if (sheets.Count == 0)
            {
                Defect("sml2003.no-sheets", DefectSeverity.Repaired, "The workbook has no worksheet; an empty one was added.", root);
            }

            var sheetNames = new List<string>();
            for (int i = 0; i < sheets.Count; i++)
            {
                string unique = UniqueSheetName(Attr(sheets[i], "Name"), i + 1, sheets[i]);
                sheetNames.Add(unique);
                _sheetIndexByName[unique] = i;
            }

            // Workbook-level names first, so a cell's NamedCell does not shadow a name that is defined explicitly.
            MarkupNode names = Child(root, "Names");
            if (!names.IsNull)
            {
                ReadNames(names, null);
            }

            var parts = new List<(string Name, string ContentType, ReadOnlyMemory<byte> Content)>();
            var workbookRels = new List<Relationship>();
            for (int i = 0; i < sheets.Count; i++)
            {
                _ct.ThrowIfCancellationRequested();
                WriteWorksheet(package, sheets[i], sheetNames[i], i + 1, parts, workbookRels);
            }

            if (sheets.Count == 0)
            {
                string empty = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<worksheet xmlns=\"" + MainNs + "\"><sheetData/></worksheet>";
                parts.Add(("/xl/worksheets/sheet1.xml", CtBase + "worksheet+xml", Encoding.UTF8.GetBytes(empty)));
                workbookRels.Add(new Relationship("rId1", RelBase + "worksheet", "worksheets/sheet1.xml"));
                sheetNames.Add("Sheet1");
            }

            workbookRels.Add(new Relationship("rIdStyles", RelBase + "styles", "styles.xml"));
            foreach ((string name, string contentType, ReadOnlyMemory<byte> content) in parts)
            {
                package.AddPart(name, contentType, content);
            }

            package.AddPart("/xl/styles.xml", CtBase + "styles+xml", BuildStyles());
            package.AddPart("/xl/workbook.xml", CtBase + "sheet.main+xml", BuildWorkbook(root, excel, sheetNames));
            package.SetRelationships("/xl/workbook.xml", workbookRels);

            var rootRels = new List<Relationship> { new("rId1", RelBase + "officeDocument", "xl/workbook.xml") };
            AddProperties(package, root, rootRels);
            package.SetRelationships(null, rootRels);
            return package;
        }
        catch
        {
            package.Dispose();
            throw;
        }
    }

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
            candidate = "Sheet" + number.ToString(CultureInfo.InvariantCulture);
        }

        string unique = candidate;
        for (int n = 2; !_sheetNames.Add(unique); n++)
        {
            string suffix = "_" + n.ToString(CultureInfo.InvariantCulture);
            unique = (candidate.Length + suffix.Length > 31 ? candidate[..(31 - suffix.Length)] : candidate) + suffix;
        }

        if (unique != name)
        {
            Defect("sml2003.sheet-name", DefectSeverity.Repaired, "The sheet name '" + name + "' is not valid or not unique; '" + unique + "' was used.", at);
        }

        return unique;
    }

    private static readonly Dictionary<string, string> BuiltInNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Print_Area"] = "_xlnm.Print_Area",
        ["Print_Titles"] = "_xlnm.Print_Titles",
        ["Criteria"] = "_xlnm.Criteria",
        ["Extract"] = "_xlnm.Extract",
        ["_FilterDatabase"] = "_xlnm._FilterDatabase",
    };

    private void ReadNames(MarkupNode names, int? sheet)
    {
        foreach (MarkupNode n in names.ChildElements)
        {
            if (Name(n) != "NamedRange")
            {
                continue;
            }

            string? name = Attr(n, "Name");
            string? refersTo = Attr(n, "RefersTo");
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(refersTo))
            {
                continue;
            }

            string? formula = ToA1(refersTo, 1, 1, n);
            if (formula is null)
            {
                continue;
            }

            if (BuiltInNames.TryGetValue(name, out string? builtIn))
            {
                name = builtIn;
            }

            if (_nameKeys.Add((sheet?.ToString(CultureInfo.InvariantCulture) ?? "-") + "!" + name))
            {
                _names.Add((name, formula, sheet, IsTrue(Attr(n, "Hidden"))));
            }
        }
    }

    private ReadOnlyMemory<byte> BuildWorkbook(MarkupNode root, MarkupNode excel, List<string> sheetNames)
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

        if (!excel.IsNull)
        {
            MarkupNode protect = Child(excel, "ProtectStructure");
            if (!protect.IsNull && IsTrue(protect.InnerText.Trim()))
            {
                w.WriteStartElement("workbookProtection");
                w.WriteAttribute("lockStructure", "1");
                w.WriteEndElement();
            }

            if (int.TryParse(Child(excel, "ActiveSheet").InnerText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int active) && active > 0 && active < sheetNames.Count)
            {
                w.WriteStartElement("bookViews");
                w.WriteStartElement("workbookView");
                w.WriteAttribute("activeTab", active.ToString(CultureInfo.InvariantCulture));
                w.WriteEndElement();
                w.WriteEndElement();
            }
        }

        w.WriteStartElement("sheets");
        for (int i = 0; i < sheetNames.Count; i++)
        {
            w.WriteStartElement("sheet");
            w.WriteAttribute("name", sheetNames[i]);
            w.WriteAttribute("sheetId", (i + 1).ToString(CultureInfo.InvariantCulture));
            w.WriteAttribute("r:id", i < _sheetRelIds.Count ? _sheetRelIds[i] : "rId1");
            w.WriteEndElement();
        }

        w.WriteEndElement();
        if (_names.Count > 0)
        {
            w.WriteStartElement("definedNames");
            foreach ((string name, string formula, int? sheet, bool hidden) in _names)
            {
                w.WriteStartElement("definedName");
                w.WriteAttribute("name", name);
                if (sheet is { } s)
                {
                    w.WriteAttribute("localSheetId", s.ToString(CultureInfo.InvariantCulture));
                }

                if (hidden)
                {
                    w.WriteAttribute("hidden", "1");
                }

                w.WriteText(formula);
                w.WriteEndElement();
            }

            w.WriteEndElement();
        }

        w.WriteEndElement();
        w.Complete();
        _ = root;
        return buffer.WrittenMemory;
    }

    private readonly List<string> _sheetRelIds = [];

    // ---- formulas -------------------------------------------------------------------------------------------------------------------

    // SpreadsheetML 2003 stores formulas in R1C1 notation relative to the cell they sit in; the hub and XLSX use A1.
    private string? ToA1(string text, int row, int col, MarkupNode at, DefectLocation? where = null)
    {
        var options = new FormulaParserOptions
        {
            Syntax = FormulaSyntax.R1C1,
            OriginRow = row,
            OriginColumn = col,
            FileForm = false,
            LeadingEquals = true,
            Limits = _ctx.Limits,
        };
        if (!FormulaParser.TryParse(text, out FormulaNode? node, out FormulaSyntaxError error, options) || node is null)
        {
            Defect("sml2003.bad-formula", DefectSeverity.Dropped, "The formula '" + text + "' cannot be read: " + error.Message, at, where);
            return null;
        }

        return FormulaPrinter.Print(node, new FormulaPrintOptions { Form = FormulaForm.File, Syntax = FormulaSyntax.A1 });
    }

    // Absolute R1C1 references to cells and areas ("R2C3", "R1C1:R5C3", "R1:R3", "C2:C4"), as an A1 reference; null when it is not one.
    private static string? RangeToA1(string text)
    {
        int colon = text.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0)
        {
            return TryCorner(text, out int r, out int c, out bool hasR, out bool hasC) && hasR && hasC ? Corner(r, c, true, true) : null;
        }

        if (!TryCorner(text[..colon], out int r1, out int c1, out bool hr1, out bool hc1) || !TryCorner(text[(colon + 1)..], out int r2, out int c2, out bool hr2, out bool hc2))
        {
            return null;
        }

        if (hr1 && hc1 && hr2 && hc2)
        {
            return Corner(r1, c1, true, true) + ":" + Corner(r2, c2, true, true);
        }

        if (hr1 && !hc1 && hr2 && !hc2)
        {
            return r1.ToString(CultureInfo.InvariantCulture) + ":" + r2.ToString(CultureInfo.InvariantCulture);
        }

        if (!hr1 && hc1 && !hr2 && hc2)
        {
            return CellAddress.ColumnName(c1) + ":" + CellAddress.ColumnName(c2);
        }

        return null;
    }

    private static string Corner(int row, int col, bool _, bool __) => CellAddress.ColumnName(col) + row.ToString(CultureInfo.InvariantCulture);

    private static bool TryCorner(string s, out int row, out int col, out bool hasRow, out bool hasCol)
    {
        row = 0;
        col = 0;
        hasRow = false;
        hasCol = false;
        int i = 0;
        s = s.Trim();
        while (i < s.Length)
        {
            char ch = s[i];
            if (ch is not ('R' or 'C' or 'r' or 'c'))
            {
                return false;
            }

            int start = ++i;
            while (i < s.Length && char.IsAsciiDigit(s[i]))
            {
                i++;
            }

            if (i == start || !int.TryParse(s.AsSpan(start, i - start), NumberStyles.None, CultureInfo.InvariantCulture, out int n) || n < 1)
            {
                return false;
            }

            if (ch is 'R' or 'r')
            {
                if (hasRow || n > CellAddress.MaxRows)
                {
                    return false;
                }

                row = n;
                hasRow = true;
            }
            else
            {
                if (hasCol || n > CellAddress.MaxColumns)
                {
                    return false;
                }

                col = n;
                hasCol = true;
            }
        }

        return hasRow || hasCol;
    }

    // A comma-separated list of absolute R1C1 areas as a space-separated A1 sqref; null when any part is not one.
    private static string? RangeListToA1(string text)
    {
        var parts = new List<string>();
        foreach (string piece in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string? a1 = RangeToA1(piece);
            if (a1 is null)
            {
                return null;
            }

            parts.Add(a1);
        }

        return parts.Count == 0 ? null : string.Join(' ', parts);
    }

    // ---- document properties --------------------------------------------------------------------------------------------------------

    private void AddProperties(OpcPackage package, MarkupNode root, List<Relationship> rootRels)
    {
        MarkupNode props = Child(root, "DocumentProperties");
        if (props.IsNull)
        {
            return;
        }

        string? Text(string local)
        {
            string t = Child(props, local).InnerText.Trim();
            return t.Length == 0 ? null : t;
        }

        var core = new List<(string Element, string Value, bool Date)>();
        void Add(string element, string? value, bool date = false)
        {
            if (value is null)
            {
                return;
            }

            if (date && !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _))
            {
                Defect("sml2003.bad-value", DefectSeverity.Dropped, "The date '" + value + "' in the document properties is not a date.", props);
                return;
            }

            core.Add((element, value, date));
        }

        Add("dc:title", Text("Title"));
        Add("dc:subject", Text("Subject"));
        Add("dc:creator", Text("Author"));
        Add("cp:keywords", Text("Keywords"));
        Add("dc:description", Text("Description"));
        Add("cp:lastModifiedBy", Text("LastAuthor"));
        Add("cp:revision", Text("Revision"));
        Add("dcterms:created", Text("Created"), date: true);
        Add("dcterms:modified", Text("LastSaved"), date: true);
        Add("cp:category", Text("Category"));
        if (core.Count > 0)
        {
            package.AddPart("/docProps/core.xml", "application/vnd.openxmlformats-package.core-properties+xml", Plain(w =>
            {
                w.WriteStartElement("cp:coreProperties");
                w.WriteNamespaceDeclaration("cp", "http://schemas.openxmlformats.org/package/2006/metadata/core-properties");
                w.WriteNamespaceDeclaration("dc", "http://purl.org/dc/elements/1.1/");
                w.WriteNamespaceDeclaration("dcterms", "http://purl.org/dc/terms/");
                w.WriteNamespaceDeclaration("xsi", "http://www.w3.org/2001/XMLSchema-instance");
                foreach ((string element, string value, bool date) in core)
                {
                    w.WriteStartElement(element);
                    if (date)
                    {
                        w.WriteAttribute("xsi:type", "dcterms:W3CDTF");
                    }

                    w.WriteText(value);
                    w.WriteEndElement();
                }

                w.WriteEndElement();
            }));
            rootRels.Add(new Relationship("rId2", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties", "docProps/core.xml"));
        }

        string? company = Text("Company");
        string? manager = Text("Manager");
        if (company is not null || manager is not null)
        {
            package.AddPart("/docProps/app.xml", "application/vnd.openxmlformats-officedocument.extended-properties+xml", Plain(w =>
            {
                w.WriteStartElement("Properties");
                w.WriteNamespaceDeclaration(null, "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties");
                if (manager is not null)
                {
                    w.WriteStartElement("Manager");
                    w.WriteText(manager);
                    w.WriteEndElement();
                }

                if (company is not null)
                {
                    w.WriteStartElement("Company");
                    w.WriteText(company);
                    w.WriteEndElement();
                }

                w.WriteEndElement();
            }));
            rootRels.Add(new Relationship("rId3", RelBase + "extended-properties", "docProps/app.xml"));
        }
    }

    private static ReadOnlyMemory<byte> Plain(Action<XmlWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new XmlWriter(buffer);
        w.WriteXmlDeclaration();
        write(w);
        w.Complete();
        return buffer.WrittenMemory;
    }
}
