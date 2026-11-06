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

// Names, validation, conditional formats, hyperlinks, notes, filters and page layout.
internal sealed partial class OdsExporter
{
    private readonly StringBuilder _globalNames = new();
    private readonly Dictionary<Worksheet, StringBuilder> _localNames = [];
    private readonly Dictionary<Worksheet, string> _printAreas = [];
    private readonly Dictionary<Worksheet, (int R1, int R2, int C1, int C2)> _titles = [];
    private readonly StringBuilder _validationDefs = new();
    private readonly StringBuilder _databaseRanges = new();
    private int _validationCount;
    private int _filterCount;
    private readonly Dictionary<int, string> _conditionalStyles = [];

    // ---- text of references --------------------------------------------------------------------------------------------------------------------

    private static string OdfSheet(string name) => OpenFormulaPrinterSheet(name);

    private static string OpenFormulaPrinterSheet(string name)
    {
        bool plain = name.Length > 0 && !char.IsAsciiDigit(name[0]);
        foreach (char ch in name)
        {
            if (!(char.IsLetterOrDigit(ch) || ch == '_' || ch > 127))
            {
                plain = false;
                break;
            }
        }

        return plain ? name : "'" + name.Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    private static string CornerText(RefCoord row, RefCoord col, bool absolute) =>
        (absolute || col.Absolute ? "$" : string.Empty) + CellAddress.ColumnName(col.Index) + (absolute || row.Absolute ? "$" : string.Empty) + row.Index.ToString(CultureInfo.InvariantCulture);

    // A reference node as a cell range address of the document: Sheet.A1:Sheet.B2 (print ranges) or $Sheet.$A$1:.$B$2 (names).
    private static bool TryRangeAddress(FormulaNode node, bool absolute, out string text, out string? sheet)
    {
        text = string.Empty;
        sheet = null;
        switch (node)
        {
            case CellReferenceNode c when c.Prefix?.Sheet is { } s && c.Prefix.Workbook is null && !c.Prefix.Is3D:
                sheet = s;
                text = (absolute ? "$" : string.Empty) + OdfSheet(s) + "." + CornerText(c.Row, c.Column, absolute);
                return true;
            case AreaReferenceNode a when a.Prefix?.Sheet is { } s && a.Prefix.Workbook is null && !a.Prefix.Is3D:
                sheet = s;
                string first;
                string second;
                switch (a.AreaKind)
                {
                    case AreaKind.Columns:
                        first = (absolute || a.Column1.Absolute ? "$" : string.Empty) + CellAddress.ColumnName(a.Column1.Index);
                        second = (absolute || a.Column2.Absolute ? "$" : string.Empty) + CellAddress.ColumnName(a.Column2.Index);
                        break;
                    case AreaKind.Rows:
                        first = (absolute || a.Row1.Absolute ? "$" : string.Empty) + a.Row1.Index.ToString(CultureInfo.InvariantCulture);
                        second = (absolute || a.Row2.Absolute ? "$" : string.Empty) + a.Row2.Index.ToString(CultureInfo.InvariantCulture);
                        break;
                    default:
                        first = CornerText(a.Row1, a.Column1, absolute);
                        second = CornerText(a.Row2, a.Column2, absolute);
                        break;
                }

                text = absolute
                    ? "$" + OdfSheet(s) + "." + first + ":." + second
                    : OdfSheet(s) + "." + first + ":" + OdfSheet(s) + "." + second;
                return true;
            default:
                return false;
        }
    }

    private static void Flatten(FormulaNode node, List<FormulaNode> into)
    {
        switch (node)
        {
            case BinaryNode { Operator: BinaryOperator.Union } b:
                Flatten(b.Left, into);
                Flatten(b.Right, into);
                break;
            case ParenNode p:
                Flatten(p.Inner, into);
                break;
            default:
                into.Add(node);
                break;
        }
    }

    private FormulaNode? ParseOrNull(string text)
    {
        return FormulaParser.TryParse(text, out FormulaNode? node, out _, new FormulaParserOptions { Limits = _ctx.Limits }) ? node : null;
    }

    // An A1 formula as OpenFormula text; null when OpenFormula cannot say it.
    private string? ToOpenFormula(string a1Text)
    {
        FormulaNode? node = ParseOrNull(a1Text.StartsWith('=') ? a1Text[1..] : a1Text);
        if (node is null)
        {
            return null;
        }

        var problems = new List<OpenFormulaProblem>();
        string text = OpenFormulaPrinter.Print(node, problems);
        return problems.Exists(static p => !p.Portable) ? null : text;
    }

    // ---- names (OpenDocument 1.3 Part 1 §9.? table:named-range, table:named-expression) -----------------------------------------------------------

    private void PrepareNames()
    {
        ReportWorkbookLosses();
        foreach (DefinedName n in _wb.Names)
        {
            if (n.IsFunction || n.IsMacro || n.IsVbProcedure)
            {
                Lost("loss.macro-names", "macro names", "Names of macro functions are not written.");
                continue;
            }

            Worksheet? scope = n.Scope;
            if (scope is not null && scope.Kind != SheetKind.Worksheet)
            {
                continue;
            }

            if (n.IsBuiltIn)
            {
                PrepareBuiltIn(n, scope);
                continue;
            }

            FormulaNode? tree = ParseOrNull(n.Formula);
            if (tree is null)
            {
                Lost("loss.defined-names", "defined names", "A name whose formula cannot be read was not written.");
                continue;
            }

            StringBuilder target = scope is null ? _globalNames : (_localNames.TryGetValue(scope, out StringBuilder? existing) ? existing : _localNames[scope] = new StringBuilder());
            string baseCell = "$" + OdfSheet((scope ?? _sheets[0]).Name) + ".$A$1";
            var refs = new List<FormulaNode>();
            Flatten(tree, refs);
            var addresses = new List<string>();
            foreach (FormulaNode r in refs)
            {
                if (TryRangeAddress(r, absolute: true, out string address, out _))
                {
                    addresses.Add(address);
                }
                else
                {
                    addresses.Clear();
                    break;
                }
            }

            if (addresses.Count > 0)
            {
                target.Append("<table:named-range table:name=\"").Append(Esc(n.Name)).Append("\" table:base-cell-address=\"").Append(Esc(baseCell)).Append("\" table:cell-range-address=\"").Append(Esc(string.Join(' ', addresses))).Append("\"/>");
                continue;
            }

            string? expression = ToOpenFormula(n.Formula);
            if (expression is null)
            {
                Lost("loss.defined-names", "defined names", "A name whose formula OpenDocument cannot express was not written.");
                continue;
            }

            target.Append("<table:named-expression table:name=\"").Append(Esc(n.Name)).Append("\" table:base-cell-address=\"").Append(Esc(baseCell)).Append("\" table:expression=\"of:=").Append(Esc(expression)).Append("\"/>");
        }
    }

    private void PrepareBuiltIn(DefinedName n, Worksheet? scope)
    {
        if (scope is null)
        {
            return;
        }

        FormulaNode? tree = ParseOrNull(n.Formula);
        if (tree is null)
        {
            return;
        }

        var refs = new List<FormulaNode>();
        Flatten(tree, refs);
        switch (n.BuiltIn)
        {
            case BuiltInName.PrintArea:
                var areas = new List<string>();
                foreach (FormulaNode r in refs)
                {
                    if (TryRangeAddress(r, absolute: false, out string text, out _))
                    {
                        areas.Add(text);
                    }
                }

                if (areas.Count > 0)
                {
                    _printAreas[scope] = string.Join(' ', areas);
                }

                break;
            case BuiltInName.PrintTitles:
                int r1 = 0;
                int r2 = 0;
                int c1 = 0;
                int c2 = 0;
                foreach (FormulaNode r in refs)
                {
                    if (r is AreaReferenceNode { AreaKind: AreaKind.Rows } rows)
                    {
                        r1 = rows.Row1.Index;
                        r2 = rows.Row2.Index;
                    }
                    else if (r is AreaReferenceNode { AreaKind: AreaKind.Columns } cols)
                    {
                        c1 = cols.Column1.Index;
                        c2 = cols.Column2.Index;
                    }
                }

                _titles[scope] = (r1, r2, c1, c2);
                break;
            case BuiltInName.FilterDatabase:
                break;
            default:
                Lost("loss.defined-names", "defined names", "Built-in names other than print areas, print titles and filter ranges are not written.");
                break;
        }
    }

    private void CollectTitles(Worksheet ws, SheetContext x, int index)
    {
        _ = index;
        if (_titles.TryGetValue(ws, out (int R1, int R2, int C1, int C2) t))
        {
            x.TitleRows1 = t.R1;
            x.TitleRows2 = t.R2;
            x.TitleCols1 = t.C1;
            x.TitleCols2 = t.C2;
        }
    }

    private void ReportWorkbookLosses()
    {
        foreach (IPackagePart p in _wb.Package.Parts)
        {
            string n = p.Name;
            if (n.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase))
            {
                Lost("loss.vba", "macro project", "The macro project is not written.");
            }
            else if (n.StartsWith("/xl/externalLinks/", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                Lost("loss.external-links", "external workbook links", "Links to other workbooks are not written; formulas that use them keep their cached values.");
            }
            else if (n.StartsWith("/xl/pivotTables/", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                Lost("loss.pivot-tables", "pivot tables", "Pivot tables are not written; their cells keep their values.");
            }
            else if (p.ContentType.Contains("drawingml.chart+xml", StringComparison.Ordinal))
            {
                Lost("loss.charts", "charts", "Charts are not written.");
            }
            else if (n.StartsWith("/xl/media/", StringComparison.OrdinalIgnoreCase))
            {
                Lost("loss.pictures", "pictures", "Pictures are not written.");
            }
        }
    }

    // ---- data validation ------------------------------------------------------------------------------------------------------------------------

    private static readonly Dictionary<string, string> OperatorSymbols = new(StringComparer.Ordinal)
    {
        ["equal"] = "=", ["notEqual"] = "!=", ["greaterThan"] = ">", ["lessThan"] = "<", ["greaterThanOrEqual"] = ">=", ["lessThanOrEqual"] = "<=",
    };

    private void CollectValidations(Worksheet ws, SheetContext x)
    {
        foreach (DataValidation v in ws.Validations)
        {
            IReadOnlyList<CellRange> ranges = v.Ranges;
            if (ranges.Count == 0)
            {
                continue;
            }

            string? condition = ValidationCondition(v, ws);
            if (condition is null)
            {
                Lost("loss.validation", "data validation", "A validation rule that OpenDocument cannot express was not written.");
                continue;
            }

            string name = "val" + I(++_validationCount);
            CellAddress first = ranges[0].TopLeft;
            _validationDefs.Append("<table:content-validation table:name=\"").Append(name).Append("\" table:condition=\"of:").Append(Esc(condition)).Append("\" table:allow-empty-cell=\"").Append(v.AllowBlank ? "true" : "false").Append('"');
            if (v.Type == DataValidationType.List)
            {
                _validationDefs.Append(" table:display-list=\"").Append(v.InCellDropDown ? "unsorted" : "none").Append('"');
            }

            _validationDefs.Append(" table:base-cell-address=\"").Append(Esc(OdfSheet(ws.Name) + "." + CellAddress.ColumnName(first.Column) + I(first.Row))).Append("\">");
            if (v.Prompt is { Length: > 0 } or null && v.ShowInputMessage || v.PromptTitle is { Length: > 0 })
            {
                _validationDefs.Append("<table:help-message table:title=\"").Append(Esc(v.PromptTitle ?? string.Empty)).Append("\" table:display=\"").Append(v.ShowInputMessage ? "true" : "false").Append("\">").Append(Paragraphs(v.Prompt ?? string.Empty)).Append("</table:help-message>");
            }

            if (v.Error is { Length: > 0 } || v.ErrorTitle is { Length: > 0 } || v.ShowErrorMessage)
            {
                string kind = v.ErrorStyle switch { DataValidationErrorStyle.Warning => "warning", DataValidationErrorStyle.Information => "information", _ => "stop" };
                _validationDefs.Append("<table:error-message table:title=\"").Append(Esc(v.ErrorTitle ?? string.Empty)).Append("\" table:message-type=\"").Append(kind).Append("\" table:display=\"").Append(v.ShowErrorMessage ? "true" : "false").Append("\">").Append(Paragraphs(v.Error ?? string.Empty)).Append("</table:error-message>");
            }

            _validationDefs.Append("</table:content-validation>");
            foreach (CellRange r in ranges)
            {
                x.Validations.Add(new ValidationRange(name, r.TopLeft.Row, r.TopLeft.Column, r.BottomRight.Row, r.BottomRight.Column));
            }
        }
    }

    private string? ValidationCondition(DataValidation v, Worksheet ws)
    {
        _ = ws;
        string? f1 = v.Formula1 is { Length: > 0 } a ? a : null;
        string? f2 = v.Formula2 is { Length: > 0 } b ? b : null;
        string? o1 = f1 is null ? null : (f1.StartsWith('"') && v.Type != DataValidationType.List ? f1 : ToOpenFormula(f1));
        string? o2 = f2 is null ? null : ToOpenFormula(f2);
        string op = v.Operator;

        string? Rule(string prefix)
        {
            if (o1 is null)
            {
                return null;
            }

            if (op is "between" or "notBetween")
            {
                return o2 is null ? null : prefix + (op == "between" ? "is-between(" : "is-not-between(") + o1 + "," + o2 + ")";
            }

            return OperatorSymbols.TryGetValue(op, out string? sym) ? (prefix == "cell-content-text-length-" ? "cell-content-text-length()" : "cell-content()") + sym + o1 : null;
        }

        switch (v.Type)
        {
            case DataValidationType.WholeNumber:
                return Combine("cell-content-is-whole-number()", Rule("cell-content-"));
            case DataValidationType.DecimalNumber:
                return Combine("cell-content-is-decimal-number()", Rule("cell-content-"));
            case DataValidationType.Date:
                return Combine("cell-content-is-date()", Rule("cell-content-"));
            case DataValidationType.Time:
                return Combine("cell-content-is-time()", Rule("cell-content-"));
            case DataValidationType.TextLength:
                string? length = Rule("cell-content-text-length-");
                return length;
            case DataValidationType.List:
                if (f1 is null)
                {
                    return null;
                }

                if (f1.StartsWith('"') && f1.EndsWith('"') && f1.Length >= 2)
                {
                    string[] items = f1[1..^1].Split(',');
                    return "cell-content-is-in-list(" + string.Join(';', items.Select(static i => "\"" + i.Trim().Replace("\"", "\"\"", StringComparison.Ordinal) + "\"")) + ")";
                }

                return o1 is null ? null : "cell-content-is-in-list(" + o1 + ")";
            case DataValidationType.Custom:
                return o1 is null ? null : "is-true-formula(" + o1 + ")";
            default:
                return null;
        }

        static string? Combine(string type, string? rule) => rule is null ? null : type + " and " + rule;
    }

    // ---- hyperlinks and notes ---------------------------------------------------------------------------------------------------------------------

    private void CollectLinks(Worksheet ws, SheetContext x)
    {
        MarkupNode links = FrameChild(ws, "hyperlinks");
        if (links.IsNull)
        {
            return;
        }

        IReadOnlyList<Relationship> rels = ws.PartName is null ? [] : _wb.Package.GetRelationships(ws.PartName);
        foreach (MarkupNode h in links.ChildElements)
        {
            if (h.LocalNameString != "hyperlink" || h.GetAttribute("ref") is not { } reference || !CellRange.TryParse(reference, out CellRange range))
            {
                continue;
            }

            string? href = null;
            if (h.GetAttribute(_wb.RelationshipNamespace, "id") is { } rid)
            {
                href = rels.FirstOrDefault(rel => rel.Id == rid)?.Target;
            }
            else if (h.GetAttribute("location") is { Length: > 0 } location)
            {
                href = "#" + InternalTarget(location);
            }

            if (href is null)
            {
                continue;
            }

            string? tip = h.GetAttribute("tooltip");
            int cells = 0;
            for (int row = range.TopLeft.Row; row <= range.BottomRight.Row && cells < 64; row++)
            {
                for (int col = range.TopLeft.Column; col <= range.BottomRight.Column && cells < 64; col++, cells++)
                {
                    x.Links[Key(row, col)] = (href, tip);
                }
            }
        }
    }

    // "Sheet2!A1" or "'My Sheet'!A1:B2" as the "Sheet2.A1" a link in the document uses.
    private string InternalTarget(string location)
    {
        FormulaNode? node = ParseOrNull(location);
        if (node is not null && TryRangeAddress(node, absolute: false, out string text, out _))
        {
            return text.Replace(":" + (node is AreaReferenceNode a && a.Prefix?.Sheet is { } s ? OdfSheet(s) + "." : string.Empty), ":", StringComparison.Ordinal);
        }

        return location.Replace('!', '.');
    }

    private void CollectComments(Worksheet ws, SheetContext x)
    {
        if (ws.PartName is null)
        {
            return;
        }

        foreach (Relationship rel in _wb.Package.GetRelationships(ws.PartName))
        {
            if (!rel.Type.EndsWith("/comments", StringComparison.Ordinal) || !OpcPackage.TryResolveTarget(ws.PartName, rel, out PartUri target) || _wb.Package.FindPart(target.ToString()) is not { } part)
            {
                if (rel.Type.EndsWith("/threadedComment", StringComparison.Ordinal) || rel.Type.Contains("threadedcomments", StringComparison.OrdinalIgnoreCase))
                {
                    Lost("loss.threaded-comments", "threaded comments", "Replies of threaded comments are not written; the note text is.");
                }

                continue;
            }

            try
            {
                MarkupDocument doc = MarkupDocument.Parse(part.GetBytes(), _ctx, part.Name);
                var authors = new List<string>();
                foreach (MarkupNode n in doc.Root.ChildElements)
                {
                    if (n.LocalNameString == "authors")
                    {
                        foreach (MarkupNode a in n.ChildElements)
                        {
                            authors.Add(a.InnerText);
                        }
                    }
                    else if (n.LocalNameString == "commentList")
                    {
                        foreach (MarkupNode c in n.ChildElements)
                        {
                            if (c.GetAttribute("ref") is not { } r || !CellAddress.TryParse(r, out CellAddress at))
                            {
                                continue;
                            }

                            int.TryParse(c.GetAttribute("authorId"), NumberStyles.None, CultureInfo.InvariantCulture, out int ai);
                            MarkupNode textNode = default;
                            foreach (MarkupNode t in c.ChildElements)
                            {
                                if (t.LocalNameString == "text")
                                {
                                    textNode = t;
                                }
                            }

                            x.Comments[Key(at.Row, at.Column)] = (ai >= 0 && ai < authors.Count ? authors[ai] : string.Empty, CommentText(textNode));
                        }
                    }
                }
            }
            catch (OfficeException)
            {
                Lost("loss.comments", "comments", "A comments part that could not be read was not written.");
            }
        }
    }

    private static string CommentText(MarkupNode text)
    {
        if (text.IsNull)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        foreach (MarkupNode c in text.ChildElements)
        {
            if (c.LocalNameString == "t")
            {
                sb.Append(c.InnerText);
            }
            else if (c.LocalNameString == "r")
            {
                foreach (MarkupNode t in c.ChildElements)
                {
                    if (t.LocalNameString == "t")
                    {
                        sb.Append(t.InnerText);
                    }
                }
            }
        }

        return sb.ToString();
    }

    // ---- automatic filter -----------------------------------------------------------------------------------------------------------------------

    private void WriteAutoFilter(Worksheet ws)
    {
        MarkupNode filter = FrameChild(ws, "autoFilter");
        if (filter.IsNull || filter.GetAttribute("ref") is not { } reference || !CellRange.TryParse(reference, out CellRange range))
        {
            return;
        }

        string sheet = OdfSheet(ws.Name);
        string target = sheet + "." + CellAddress.ColumnName(range.TopLeft.Column) + I(range.TopLeft.Row) + ":" + sheet + "." + CellAddress.ColumnName(range.BottomRight.Column) + I(range.BottomRight.Row);
        _databaseRanges.Append("<table:database-range table:name=\"__Anonymous_Sheet_DB__").Append(_filterCount++).Append("\" table:target-range-address=\"").Append(Esc(target)).Append("\" table:display-filter-buttons=\"true\"/>");
        if (filter.ChildElements.GetEnumerator().MoveNext())
        {
            Lost("loss.filter-conditions", "filter conditions", "The conditions of an automatic filter are not written; the filter buttons are.");
        }
    }

    // ---- conditional formats (calcext:conditional-formats) ------------------------------------------------------------------------------------------

    private string ConditionalStyleOf(int dxf)
    {
        if (_conditionalStyles.TryGetValue(dxf, out string? existing))
        {
            return existing;
        }

        DifferentialFormat d = _wb.Styles.GetDifferentialFormat(dxf);
        string name = "ConditionalStyle_" + I(_conditionalStyles.Count + 1);
        var cell = new StringBuilder();
        var text = new StringBuilder();
        if (d.Fill is { } fill)
        {
            // A solid fill in a differential format shows its background colour.
            string rgb = RgbOf(fill.Background.Kind != ColorKind.None ? fill.Background : fill.Foreground);
            if (rgb.Length > 0)
            {
                cell.Append(" fo:background-color=\"").Append(rgb).Append('"');
            }
        }

        if (d.Border is { } b)
        {
            foreach ((string side, BorderSide s) in new[] { ("left", b.Left), ("right", b.Right), ("top", b.Top), ("bottom", b.Bottom) })
            {
                if (s.Style != BorderLineStyle.None)
                {
                    cell.Append(" fo:border-").Append(side).Append("=\"").Append(BorderValue(s, RgbOf(s.Color), out _)).Append('"');
                }
            }
        }

        if (d.Font is { } f)
        {
            if (f.Bold)
            {
                text.Append(" fo:font-weight=\"bold\"");
            }

            if (f.Italic)
            {
                text.Append(" fo:font-style=\"italic\"");
            }

            if (f.Underline != FontUnderline.None)
            {
                text.Append(" style:text-underline-style=\"solid\" style:text-underline-width=\"auto\" style:text-underline-color=\"font-color\"");
            }

            if (f.Strike)
            {
                text.Append(" style:text-line-through-style=\"solid\"");
            }

            if (RgbOf(f.Color) is { Length: > 0 } color)
            {
                text.Append(" fo:color=\"").Append(color).Append('"');
            }
        }

        string? data = d.NumberFormatCode is { Length: > 0 } code ? DataStyleOf(code) : null;
        _auto.Append("<style:style style:name=\"").Append(name).Append("\" style:family=\"table-cell\" style:parent-style-name=\"Default\"");
        if (data is not null)
        {
            _auto.Append(" style:data-style-name=\"").Append(data).Append('"');
        }

        _auto.Append('>');
        if (cell.Length > 0)
        {
            _auto.Append("<style:table-cell-properties").Append(cell).Append("/>");
        }

        if (text.Length > 0)
        {
            _auto.Append("<style:text-properties").Append(text).Append("/>");
        }

        _auto.Append("</style:style>");
        _conditionalStyles[dxf] = name;
        return name;
    }

    private string? PointText(ConditionalValuePoint p)
    {
        string type = p.Type switch
        {
            "min" => "minimum",
            "max" => "maximum",
            "num" => "number",
            "percent" => "percent",
            "percentile" => "percentile",
            "formula" => "formula",
            _ => string.Empty,
        };
        if (type.Length == 0)
        {
            return null;
        }

        string value = "0";
        if (type == "formula")
        {
            string? of = p.Value is null ? null : ToOpenFormula(p.Value);
            if (of is null)
            {
                return null;
            }

            value = "of:=" + of;
        }
        else if (p.Value is not null)
        {
            value = p.Value;
        }

        return "calcext:value=\"" + Esc(value) + "\" calcext:type=\"" + type + "\"";
    }

    private void WriteConditionalFormats(StringBuilder sb, Worksheet ws)
    {
        var body = new StringBuilder();
        foreach (ConditionalFormat format in ws.ConditionalFormats)
        {
            IReadOnlyList<CellRange> ranges = format.Ranges;
            if (ranges.Count == 0)
            {
                continue;
            }

            string sheet = OdfSheet(ws.Name);
            string targets = string.Join(' ', ranges.Select(r => sheet + "." + CellAddress.ColumnName(r.TopLeft.Column) + I(r.TopLeft.Row) + (r.RowCount == 1 && r.ColumnCount == 1 ? string.Empty : ":" + sheet + "." + CellAddress.ColumnName(r.BottomRight.Column) + I(r.BottomRight.Row))));
            CellAddress first = ranges[0].TopLeft;
            string baseCell = sheet + "." + CellAddress.ColumnName(first.Column) + I(first.Row);
            var rules = new StringBuilder();
            foreach (ConditionalFormatRule rule in format.Rules.OrderBy(static r => r.Priority))
            {
                if (!RuleXml(rule, baseCell, first, rules))
                {
                    Lost("loss.conditional-formats", "conditional formats", "A conditional format rule that OpenDocument cannot express was not written.");
                }
            }

            if (rules.Length > 0)
            {
                body.Append("<calcext:conditional-format calcext:target-range-address=\"").Append(Esc(targets)).Append("\">").Append(rules).Append("</calcext:conditional-format>");
            }
        }

        if (body.Length > 0)
        {
            sb.Append("<calcext:conditional-formats>").Append(body).Append("</calcext:conditional-formats>");
        }
    }

    private bool RuleXml(ConditionalFormatRule rule, string baseCell, CellAddress first, StringBuilder rules)
    {
        IReadOnlyList<string> formulas = rule.Formulas;
        string? Operand(int i) => i < formulas.Count ? ToOpenFormula(formulas[i]) : null;
        string cell = CellAddress.ColumnName(first.Column) + I(first.Row);

        string? value = null;
        switch (rule.Type)
        {
            case ConditionalRuleType.CellIs:
                string? a = Operand(0);
                if (a is null)
                {
                    return false;
                }

                string op = rule.Operator ?? "equal";
                if (op is "between" or "notBetween")
                {
                    string? b = Operand(1);
                    if (b is null)
                    {
                        return false;
                    }

                    value = (op == "between" ? "between(" : "not-between(") + a + "," + b + ")";
                }
                else if (OperatorSymbols.TryGetValue(op, out string? sym))
                {
                    value = sym + a;
                }

                break;
            case ConditionalRuleType.Expression or ConditionalRuleType.ContainsBlanks or ConditionalRuleType.NotContainsBlanks or ConditionalRuleType.TimePeriod:
                string? expression = Operand(0);
                value = expression is null ? null : "formula-is(" + expression + ")";
                break;
            case ConditionalRuleType.ContainsText:
                value = rule.Text is null ? null : "contains-text(\"" + rule.Text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\")";
                break;
            case ConditionalRuleType.NotContainsText:
                value = rule.Text is null ? null : "not-contains-text(\"" + rule.Text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\")";
                break;
            case ConditionalRuleType.BeginsWith:
                value = rule.Text is null ? null : "begins-with(\"" + rule.Text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\")";
                break;
            case ConditionalRuleType.EndsWith:
                value = rule.Text is null ? null : "ends-with(\"" + rule.Text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\")";
                break;
            case ConditionalRuleType.ContainsErrors:
                value = "is-error";
                break;
            case ConditionalRuleType.NotContainsErrors:
                value = "is-no-error";
                break;
            case ConditionalRuleType.DuplicateValues:
                value = "duplicate";
                break;
            case ConditionalRuleType.UniqueValues:
                value = "unique";
                break;
            case ConditionalRuleType.AboveAverage:
                string avg = rule.AboveAverage ? "above" : "below";
                value = avg + (rule.Markup.GetAttribute("equalAverage") is "1" or "true" ? "-equal-average" : "-average");
                break;
            case ConditionalRuleType.Top10:
                value = (rule.Bottom ? "bottom" : "top") + (rule.Percent ? "-percent(" : "-elements(") + rule.Rank.ToString(CultureInfo.InvariantCulture) + ")";
                break;
            case ConditionalRuleType.ColorScale when rule.ColorScale is { } scale:
                var entries = new StringBuilder();
                for (int i = 0; i < scale.Points.Count && i < scale.Colors.Count; i++)
                {
                    string? p = PointText(scale.Points[i]);
                    string color = RgbOf(scale.Colors[i]);
                    if (p is null || color.Length == 0)
                    {
                        return false;
                    }

                    entries.Append("<calcext:color-scale-entry ").Append(p).Append(" calcext:color=\"").Append(color).Append("\"/>");
                }

                rules.Append("<calcext:color-scale>").Append(entries).Append("</calcext:color-scale>");
                return true;
            case ConditionalRuleType.DataBar when rule.DataBar is { } bar:
                string? lo = bar.Points.Count > 0 ? PointText(bar.Points[0]) : null;
                string? hi = bar.Points.Count > 1 ? PointText(bar.Points[1]) : null;
                if (lo is null || hi is null)
                {
                    return false;
                }

                rules.Append("<calcext:data-bar calcext:positive-color=\"").Append(RgbOf(bar.Color)).Append("\" calcext:negative-color=\"#ff0000\" calcext:axis-position=\"automatic\" calcext:min-length=\"").Append(bar.MinLength).Append("\" calcext:max-length=\"").Append(bar.MaxLength)
                    .Append("\"><calcext:formatting-entry ").Append(lo).Append("/><calcext:formatting-entry ").Append(hi).Append("/></calcext:data-bar>");
                return true;
            case ConditionalRuleType.IconSet when rule.IconSet is { } icons:
                var points = new StringBuilder();
                foreach (ConditionalValuePoint p in icons.Points)
                {
                    string? pt = PointText(p);
                    if (pt is null)
                    {
                        return false;
                    }

                    points.Append("<calcext:formatting-entry ").Append(pt).Append("/>");
                }

                rules.Append("<calcext:icon-set calcext:icon-set-type=\"").Append(Esc(icons.Name)).Append("\">").Append(points).Append("</calcext:icon-set>");
                return true;
            default:
                return false;
        }

        _ = cell;
        if (value is null)
        {
            return false;
        }

        rules.Append("<calcext:condition calcext:value=\"").Append(Esc(value)).Append('"');
        if (rule.DxfId is { } dxf && dxf >= 0 && dxf < _wb.Styles.DifferentialFormatCount)
        {
            rules.Append(" calcext:apply-style-name=\"").Append(ConditionalStyleOf(dxf)).Append('"');
        }

        rules.Append(" calcext:base-cell-address=\"").Append(Esc(baseCell)).Append("\"/>");
        return true;
    }

    // ---- page layout ----------------------------------------------------------------------------------------------------------------------------------

    private static readonly Dictionary<int, (double W, double H)> PaperMillimetres = new()
    {
        [1] = (215.9, 279.4), [3] = (279.4, 431.8), [5] = (215.9, 355.6), [7] = (184.15, 266.7), [8] = (297, 420), [9] = (210, 297), [11] = (148, 210), [12] = (250, 353), [13] = (182, 257), [34] = (176, 250),
    };

    // Returns the master page name the sheet uses, or null for sheets with nothing set.
    private string? WritePageLayout(Worksheet ws, int index)
    {
        MarkupNode margins = FrameChild(ws, "pageMargins");
        MarkupNode setup = FrameChild(ws, "pageSetup");
        MarkupNode options = FrameChild(ws, "printOptions");
        MarkupNode hf = FrameChild(ws, "headerFooter");
        MarkupNode sheetPr = FrameChild(ws, "sheetPr");
        if (margins.IsNull && setup.IsNull && options.IsNull && hf.IsNull)
        {
            return null;
        }

        double M(string name, double fallback) => !margins.IsNull && double.TryParse(margins.GetAttribute(name), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;
        double left = M("left", 0.7);
        double right = M("right", 0.7);
        double top = M("top", 0.75);
        double bottom = M("bottom", 0.75);
        double header = M("header", 0.3);
        double footer = M("footer", 0.3);

        int paper = 1;
        if (!setup.IsNull && int.TryParse(setup.GetAttribute("paperSize"), NumberStyles.None, CultureInfo.InvariantCulture, out int p))
        {
            paper = p;
        }

        (double w, double h) = PaperMillimetres.TryGetValue(paper, out (double W, double H) size) ? size : PaperMillimetres[1];
        bool landscape = !setup.IsNull && setup.GetAttribute("orientation") == "landscape";
        if (landscape)
        {
            (w, h) = (h, w);
        }

        string headerText = hf.IsNull ? string.Empty : ChildText(hf, "oddHeader");
        string footerText = hf.IsNull ? string.Empty : ChildText(hf, "oddFooter");
        if (!hf.IsNull && (hf.GetAttribute("differentOddEven") is "1" or "true" || hf.GetAttribute("differentFirst") is "1" or "true"))
        {
            Lost("loss.header-footer-variants", "different first page and odd/even headers", "Only the headers and footers of odd pages are written.");
        }

        string layout = "Mpm" + I(++_pageLayoutCount);
        var pl = new StringBuilder();
        pl.Append("<style:page-layout style:name=\"").Append(layout).Append("\"><style:page-layout-properties fo:page-width=\"").Append(OdfLengths.Cm(w / 25.4 * 72)).Append("\" fo:page-height=\"").Append(OdfLengths.Cm(h / 25.4 * 72))
            .Append("\" style:print-orientation=\"").Append(landscape ? "landscape" : "portrait").Append("\" fo:margin-left=\"").Append(OdfLengths.Cm(left * 72)).Append("\" fo:margin-right=\"").Append(OdfLengths.Cm(right * 72));
        bool hasHeader = headerText.Length > 0;
        bool hasFooter = footerText.Length > 0;
        pl.Append("\" fo:margin-top=\"").Append(OdfLengths.Cm((hasHeader ? header : top) * 72)).Append("\" fo:margin-bottom=\"").Append(OdfLengths.Cm((hasFooter ? footer : bottom) * 72)).Append('"');
        if (!setup.IsNull)
        {
            // ODF 1.3 Part 1 (style:page-layout-properties): scale-to, scale-to-pages and the pair scale-to-X / scale-to-Y are alternatives, so a
            // sheet that fits to pages (which overrides its percentage in the source format) writes only the fit.
            bool fitToPage = !sheetPr.IsNull && sheetPr.ChildElements.Any(static c => c.LocalNameString == "pageSetUpPr" && c.GetAttribute("fitToPage") is "1" or "true");
            if (!fitToPage && int.TryParse(setup.GetAttribute("scale"), NumberStyles.None, CultureInfo.InvariantCulture, out int scale) && scale is >= 10 and <= 400 && scale != 100)
            {
                pl.Append(" style:scale-to=\"").Append(scale).Append("%\"");
            }

            if (fitToPage)
            {
                int.TryParse(setup.GetAttribute("fitToWidth"), NumberStyles.None, CultureInfo.InvariantCulture, out int fw);
                int.TryParse(setup.GetAttribute("fitToHeight"), NumberStyles.None, CultureInfo.InvariantCulture, out int fh);
                int fitX = setup.GetAttribute("fitToWidth") is null ? 1 : fw;
                int fitY = setup.GetAttribute("fitToHeight") is null ? 1 : fh;
                // 0 means "as many pages as needed", which is the absence of the attribute.
                if (fitX > 0)
                {
                    pl.Append(" style:scale-to-X=\"").Append(fitX).Append('"');
                }

                if (fitY > 0)
                {
                    pl.Append(" style:scale-to-Y=\"").Append(fitY).Append('"');
                }
            }

            if (setup.GetAttribute("useFirstPageNumber") is "1" or "true" && int.TryParse(setup.GetAttribute("firstPageNumber"), NumberStyles.None, CultureInfo.InvariantCulture, out int first))
            {
                pl.Append(" style:first-page-number=\"").Append(first).Append('"');
            }
        }

        var print = new List<string>();
        if (!options.IsNull && options.GetAttribute("gridLines") is "1" or "true")
        {
            print.Add("grid");
        }

        if (!options.IsNull && options.GetAttribute("headings") is "1" or "true")
        {
            print.Add("headers");
        }

        print.AddRange(["charts", "drawings", "objects", "zero-values"]);
        pl.Append(" style:print=\"").Append(string.Join(' ', print)).Append('"');
        if (!options.IsNull)
        {
            bool ch = options.GetAttribute("horizontalCentered") is "1" or "true";
            bool cv = options.GetAttribute("verticalCentered") is "1" or "true";
            if (ch || cv)
            {
                pl.Append(" style:table-centering=\"").Append(ch && cv ? "both" : ch ? "horizontal" : "vertical").Append('"');
            }
        }

        pl.Append("/>");
        pl.Append("<style:header-style><style:header-footer-properties fo:min-height=\"").Append(OdfLengths.Cm(Math.Max(0, top - header) * 72)).Append("\" fo:margin-left=\"0cm\" fo:margin-right=\"0cm\" fo:margin-bottom=\"0cm\"/></style:header-style>");
        pl.Append("<style:footer-style><style:header-footer-properties fo:min-height=\"").Append(OdfLengths.Cm(Math.Max(0, bottom - footer) * 72)).Append("\" fo:margin-left=\"0cm\" fo:margin-right=\"0cm\" fo:margin-top=\"0cm\"/></style:footer-style>");
        pl.Append("</style:page-layout>");
        _pageLayouts.Append(pl);

        string master = "PageStyle_" + I(index + 1);
        _masterPages.Append("<style:master-page style:name=\"").Append(master).Append("\" style:page-layout-name=\"").Append(layout).Append("\">");
        _masterPages.Append(hasHeader ? "<style:header>" + HeaderFooterXml(headerText) + "</style:header>" : "<style:header style:display=\"false\"/>");
        _masterPages.Append("<style:header-left style:display=\"false\"/>");
        _masterPages.Append(hasFooter ? "<style:footer>" + HeaderFooterXml(footerText) + "</style:footer>" : "<style:footer style:display=\"false\"/>");
        _masterPages.Append("<style:footer-left style:display=\"false\"/></style:master-page>");
        return master;
    }

    private static string ChildText(MarkupNode parent, string local)
    {
        foreach (MarkupNode c in parent.ChildElements)
        {
            if (c.LocalNameString == local)
            {
                return c.InnerText;
            }
        }

        return string.Empty;
    }

    // ECMA-376 Part 1 §18.3.1.39: &L, &C and &R start the three regions; &P, &N, &A, &D, &T and &F are fields; && is an ampersand; font codes are skipped.
    private static string HeaderFooterXml(string code)
    {
        var regions = new Dictionary<char, StringBuilder> { ['L'] = new(), ['C'] = new(), ['R'] = new() };
        char current = 'C';
        var text = new StringBuilder();
        void Flush()
        {
            if (text.Length > 0)
            {
                regions[current].Append(Inline(text.ToString()));
                text.Clear();
            }
        }

        for (int i = 0; i < code.Length; i++)
        {
            char c = code[i];
            if (c != '&' || i + 1 >= code.Length)
            {
                text.Append(c);
                continue;
            }

            char n = code[++i];
            switch (n)
            {
                case '&':
                    text.Append('&');
                    break;
                case 'L' or 'C' or 'R':
                    Flush();
                    current = n;
                    break;
                case 'P':
                    Flush();
                    regions[current].Append("<text:page-number>1</text:page-number>");
                    break;
                case 'N':
                    Flush();
                    regions[current].Append("<text:page-count>1</text:page-count>");
                    break;
                case 'A':
                    Flush();
                    regions[current].Append("<text:sheet-name>Sheet</text:sheet-name>");
                    break;
                case 'D':
                    Flush();
                    regions[current].Append("<text:date>01/01/2000</text:date>");
                    break;
                case 'T':
                    Flush();
                    regions[current].Append("<text:time>00:00:00</text:time>");
                    break;
                case 'F':
                    Flush();
                    regions[current].Append("<text:file-name text:display=\"name-and-extension\">file</text:file-name>");
                    break;
                case '"':
                    int end = code.IndexOf('"', i + 1);
                    i = end < 0 ? code.Length : end;
                    break;
                default:
                    // &nn is a font size, &B &I &U &S &X &Y &K... are font settings: skipped.
                    while (i + 1 < code.Length && char.IsAsciiDigit(code[i + 1]))
                    {
                        i++;
                    }

                    break;
            }
        }

        Flush();
        int used = regions.Values.Count(static r => r.Length > 0);
        if (used == 0)
        {
            return "<text:p/>";
        }

        if (used == 1 && regions['C'].Length > 0)
        {
            return "<text:p>" + regions['C'] + "</text:p>";
        }

        var sb = new StringBuilder();
        foreach ((char key, string element) in new[] { ('L', "region-left"), ('C', "region-center"), ('R', "region-right") })
        {
            if (regions[key].Length > 0)
            {
                sb.Append("<style:").Append(element).Append("><text:p>").Append(regions[key]).Append("</text:p></style:").Append(element).Append('>');
            }
        }

        return sb.ToString();
    }
}
