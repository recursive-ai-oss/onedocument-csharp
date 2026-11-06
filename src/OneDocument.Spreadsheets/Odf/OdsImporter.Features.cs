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

// Names, database ranges, data validation, conditional formats and page layout of an OpenDocument spreadsheet.
internal sealed partial class OdsImporter
{
    private const string LoExt = OdfNamespaces.LibreOfficeExtension;

    private readonly Dictionary<string, string> _filters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _printRanges = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MarkupNode> _validationDefs = new(StringComparer.Ordinal);

    // ---- names ------------------------------------------------------------------------------------------------------------------------------

    private void ReadNames(MarkupNode container, int? sheet)
    {
        if (container.IsNull)
        {
            return;
        }

        foreach (MarkupNode n in container.ChildElements)
        {
            if (n.NamespaceUri != Table || n.LocalNameString is not ("named-range" or "named-expression"))
            {
                continue;
            }

            string? name = A(n, Table, "name");
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            string formula;
            int? scope = sheet;
            if (n.LocalNameString == "named-range")
            {
                List<Range> ranges = ReadRanges(A(n, Table, "cell-range-address"));
                if (ranges.Count == 0)
                {
                    Defect("ods.bad-name", DefectKind.Reference, DefectSeverity.Dropped, "The named range '" + name + "' has no readable range.", At(n));
                    continue;
                }

                if (scope is null && name.StartsWith("Excel_BuiltIn_", StringComparison.Ordinal) && ranges[0].Sheet is { } rs && _sheetIndex.TryGetValue(SheetOf(rs), out int si))
                {
                    scope = si;
                }

                formula = string.Join(',', ranges.Select(r => AbsoluteText(r.Sheet is null && scope is { } sc ? r with { Sheet = SheetNameAt(sc) } : r)));
            }
            else
            {
                string? expression = A(n, Table, "expression");
                if (expression is null)
                {
                    continue;
                }

                string? text = JoinedReferences(expression) ?? FormulaText(expression, n, "names", name);
                if (text is null)
                {
                    continue;
                }

                formula = text;
            }

            string mapped = name switch
            {
                "Excel_BuiltIn_Print_Area" => "_xlnm.Print_Area",
                "Excel_BuiltIn_Print_Titles" => "_xlnm.Print_Titles",
                _ => name,
            };
            if (name is "Excel_BuiltIn__FilterDatabase" or "Excel_BuiltIn_Database" or "Excel_BuiltIn_Criteria" or "Excel_BuiltIn_Extract" or "Excel_BuiltIn_Sheet_Title")
            {
                continue;
            }

            if (mapped.StartsWith("_xlnm.", StringComparison.Ordinal) && scope is null)
            {
                continue;
            }

            string safe = mapped.StartsWith("_xlnm.", StringComparison.Ordinal) ? mapped : SafeName(mapped, n);
            if (_nameKeys.Add((scope is { } s ? I(s) : "-") + "!" + safe))
            {
                _names.Add(new DefinedNameInfo(safe, formula, scope, false));
            }
        }
    }

    // Print titles and solver ranges are written as several references separated by ';' at the top level, which is not one OpenFormula expression;
    // a workbook stores them as a comma-separated list. Null when the expression is a single expression.
    private string? JoinedReferences(string expression)
    {
        int start = expression.StartsWith("of:=", StringComparison.Ordinal) ? 4 : 0;
        var parts = new List<string>();
        int depth = 0;
        int from = start;
        bool quoted = false;
        for (int i = start; i < expression.Length; i++)
        {
            char c = expression[i];
            if (c is '\'' or '"')
            {
                quoted = !quoted;
            }
            else if (!quoted && c is '[' or '(' or '{')
            {
                depth++;
            }
            else if (!quoted && c is ']' or ')' or '}')
            {
                depth--;
            }
            else if (!quoted && depth == 0 && c == ';')
            {
                parts.Add(expression[from..i]);
                from = i + 1;
            }
        }

        if (parts.Count == 0)
        {
            return null;
        }

        parts.Add(expression[from..]);
        var texts = new List<string>(parts.Count);
        foreach (string part in parts)
        {
            if (!OpenFormulaParser.TryParse(part, out FormulaNode? node, out _, new OpenFormulaParseOptions { Limits = _ctx.Limits }) || node is not { Kind: NodeKind.Cell or NodeKind.Area })
            {
                return null;
            }

            texts.Add(FormulaText("of:=" + part, default!, "names", string.Empty) ?? string.Empty);
        }

        return texts.Contains(string.Empty) ? null : string.Join(',', texts);
    }

    private string SheetNameAt(int index)
    {
        foreach (KeyValuePair<string, int> kv in _sheetIndex)
        {
            if (kv.Value == index)
            {
                return kv.Key;
            }
        }

        return string.Empty;
    }

    // ECMA-376 Part 1 §18.2.5: a name starts with a letter, underscore or backslash and holds letters, digits, underscores, periods and question marks.
    private string SafeName(string name, MarkupNode at)
    {
        var sb = new StringBuilder();
        foreach (char c in name)
        {
            sb.Append(char.IsLetterOrDigit(c) || c is '_' or '.' or '\\' or '?' || c > 127 ? c : '_');
        }

        if (sb.Length == 0 || char.IsAsciiDigit(sb[0]) || RefSyntaxLooksLikeCell(sb.ToString()))
        {
            sb.Insert(0, '_');
        }

        string safe = sb.ToString();
        if (safe != name)
        {
            Defect("ods.name", DefectKind.Structure, DefectSeverity.Repaired, "The name '" + name + "' is not valid in a workbook; '" + safe + "' was used.", At(at));
        }

        return safe;
    }

    private static bool RefSyntaxLooksLikeCell(string name) => CellAddress.TryParse(name, out _);

    // ---- database ranges (automatic filters) ---------------------------------------------------------------------------------------------------

    private void ReadDatabaseRanges(MarkupNode container)
    {
        if (container.IsNull)
        {
            return;
        }

        foreach (MarkupNode d in container.ChildElements)
        {
            if (!d.IsElement(Table, "database-range"))
            {
                continue;
            }

            List<Range> ranges = ReadRanges(A(d, Table, "target-range-address"));
            bool filter = A(d, Table, "display-filter-buttons") == "true";
            if (ranges.Count == 0 || ranges[0].Sheet is null)
            {
                continue;
            }

            string sheet = SheetOf(ranges[0].Sheet!);
            if (filter && !_filters.ContainsKey(sheet))
            {
                _filters[sheet] = RangeText(ranges[0]);
                foreach (MarkupNode f in d.ChildElements)
                {
                    if (f.IsElement(Table, "filter"))
                    {
                        Lost("loss.filter-conditions", "filter conditions", "The conditions of an automatic filter were not carried over; the filter buttons were.");
                        break;
                    }
                }
            }
            else if (!filter)
            {
                string? name = A(d, Table, "name");
                if (name is not null && !name.StartsWith("__Anonymous", StringComparison.Ordinal))
                {
                    Lost("loss.database-ranges", "database ranges", "A named database range keeps only its cells.");
                }
            }
        }
    }

    // ---- data validation (OpenDocument 1.3 Part 1 §9.? table:content-validation) ----------------------------------------------------------------------

    private void ReadValidationDefinitions(MarkupNode spreadsheet)
    {
        MarkupNode defs = Child(spreadsheet, Table, "content-validations");
        if (defs.IsNull)
        {
            return;
        }

        foreach (MarkupNode d in defs.ChildElements)
        {
            if (d.IsElement(Table, "content-validation") && A(d, Table, "name") is { } name)
            {
                _validationDefs.TryAdd(name, d);
            }
        }
    }

    private static List<string> TopLevelSplit(string s, params char[] separators)
    {
        var parts = new List<string>();
        int depth = 0;
        bool quote = false;
        int start = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '"')
            {
                quote = !quote;
            }
            else if (!quote)
            {
                if (c is '(' or '[' or '{')
                {
                    depth++;
                }
                else if (c is ')' or ']' or '}')
                {
                    depth--;
                }
                else if (depth == 0 && separators.Contains(c))
                {
                    parts.Add(s[start..i].Trim());
                    start = i + 1;
                }
            }
        }

        parts.Add(s[start..].Trim());
        return parts;
    }

    private static List<string> AndClauses(string condition)
    {
        var clauses = new List<string>();
        int depth = 0;
        bool quote = false;
        int start = 0;
        for (int i = 0; i < condition.Length; i++)
        {
            char c = condition[i];
            if (c == '"')
            {
                quote = !quote;
            }
            else if (!quote)
            {
                if (c is '(' or '[')
                {
                    depth++;
                }
                else if (c is ')' or ']')
                {
                    depth--;
                }
                else if (depth == 0 && c == ' ' && string.CompareOrdinal(condition, i, " and ", 0, 5) == 0)
                {
                    clauses.Add(condition[start..i].Trim());
                    start = i + 5;
                    i += 4;
                }
            }
        }

        clauses.Add(condition[start..].Trim());
        return clauses;
    }

    private static string Inside(string clause)
    {
        int open = clause.IndexOf('(', StringComparison.Ordinal);
        int close = clause.LastIndexOf(')');
        return open >= 0 && close > open ? clause[(open + 1)..close] : string.Empty;
    }

    private static readonly (string Symbol, string Name)[] Comparisons =
    [
        (">=", "greaterThanOrEqual"), ("<=", "lessThanOrEqual"), ("!=", "notEqual"), ("<>", "notEqual"), (">", "greaterThan"), ("<", "lessThan"), ("=", "equal"),
    ];

    private static bool TryComparison(string clause, string prefix, out string op, out string operand)
    {
        op = string.Empty;
        operand = string.Empty;
        if (!clause.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        string rest = clause[prefix.Length..].TrimStart();
        foreach ((string symbol, string name) in Comparisons)
        {
            if (rest.StartsWith(symbol, StringComparison.Ordinal))
            {
                op = name;
                operand = rest[symbol.Length..].Trim();
                return true;
            }
        }

        return false;
    }

    // The A1 text of an operand written in OpenFormula, moved from the cell the document anchors it to onto the first cell of the range it governs.
    private string? Operand(string text, MarkupNode at, string sheet, string address, int dRow, int dCol) =>
        FormulaText("of:=" + text, at, sheet, address, dRow, dCol);

    private static (int DRow, int DCol) BaseDelta(string? baseAddress, int firstRow, int firstCol)
    {
        if (baseAddress is not null)
        {
            int p = 0;
            if (TryReadRange(baseAddress, ref p, out Range r) && !r.WholeRows && !r.WholeColumns)
            {
                return (firstRow - r.Row1, firstCol - r.Col1);
            }
        }

        return (0, 0);
    }

    private void WriteDataValidations(XmlWriter w, SheetState state, string sheetName)
    {
        var entries = new List<(string Sqref, MarkupNode Def, (int Row, int Col) First)>();
        foreach (KeyValuePair<string, CellSet> kv in state.Validations)
        {
            if (kv.Value.IsEmpty)
            {
                continue;
            }

            if (_validationDefs.TryGetValue(kv.Key, out MarkupNode def))
            {
                entries.Add((kv.Value.ToSqref(), def, kv.Value.First));
            }
            else
            {
                Defect("ods.validation-missing", DefectKind.Reference, DefectSeverity.Dropped, "The validation '" + kv.Key + "' is not defined.", DefectLocation.InPart(sheetName));
            }
        }

        if (entries.Count == 0)
        {
            return;
        }

        var started = false;
        foreach ((string sqref, MarkupNode def, (int row, int col)) in entries)
        {
            string condition = A(def, Table, "condition") ?? string.Empty;
            if (condition.StartsWith("of:", StringComparison.Ordinal))
            {
                condition = condition[3..];
            }

            string address = Col(col) + I(row);
            (int dRow, int dCol) = BaseDelta(A(def, Table, "base-cell-address"), row, col);
            if (!TryValidation(condition, def, sheetName, address, dRow, dCol, out string type, out string? op, out string? f1, out string? f2))
            {
                Lost("loss.validation", "data validation", "A validation rule with no counterpart was not carried over.");
                continue;
            }

            if (!started)
            {
                w.WriteStartElement("dataValidations");
                started = true;
            }

            w.WriteStartElement("dataValidation");
            w.WriteAttribute("type", type);
            MarkupNode error = Child(def, Table, "error-message");
            string? kind = A(error, Table, "message-type");
            if (kind is "warning" or "information")
            {
                w.WriteAttribute("errorStyle", kind);
            }

            if (op is not null && op != "between")
            {
                w.WriteAttribute("operator", op);
            }

            w.WriteAttribute("allowBlank", A(def, Table, "allow-empty-cell") == "false" ? "0" : "1");
            if (A(def, Table, "display-list") == "none")
            {
                w.WriteAttribute("showDropDown", "1");
            }

            MarkupNode help = Child(def, Table, "help-message");
            if (!help.IsNull && A(help, Table, "display") == "true")
            {
                w.WriteAttribute("showInputMessage", "1");
            }

            if (!error.IsNull && A(error, Table, "display") == "true")
            {
                w.WriteAttribute("showErrorMessage", "1");
            }

            if (A(error, Table, "title") is { Length: > 0 } et)
            {
                w.WriteAttribute("errorTitle", et);
            }

            string errorText = ParagraphsOf(error);
            if (errorText.Length > 0)
            {
                w.WriteAttribute("error", errorText);
            }

            if (A(help, Table, "title") is { Length: > 0 } pt)
            {
                w.WriteAttribute("promptTitle", pt);
            }

            string helpText = ParagraphsOf(help);
            if (helpText.Length > 0)
            {
                w.WriteAttribute("prompt", helpText);
            }

            w.WriteAttribute("sqref", sqref);
            if (f1 is not null)
            {
                w.WriteStartElement("formula1");
                w.WriteText(f1);
                w.WriteEndElement();
            }

            if (f2 is not null)
            {
                w.WriteStartElement("formula2");
                w.WriteText(f2);
                w.WriteEndElement();
            }

            w.WriteEndElement();
        }

        if (started)
        {
            w.WriteEndElement();
        }
    }

    private static string ParagraphsOf(MarkupNode n)
    {
        if (n.IsNull)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        foreach (MarkupNode p in n.ChildElements)
        {
            if (p.IsElement(Text, "p"))
            {
                if (sb.Length > 0)
                {
                    sb.Append('\n');
                }

                string? href = null;
                bool rich = false;
                AppendInline(p, sb, ref href, ref rich, 0);
            }
        }

        return sb.ToString();
    }

    private bool TryValidation(string condition, MarkupNode def, string sheet, string address, int dRow, int dCol, out string type, out string? op, out string? f1, out string? f2)
    {
        type = "decimal";
        op = null;
        f1 = null;
        f2 = null;
        List<string> clauses = AndClauses(condition.Trim());
        string? typeClause = null;
        string? ruleClause = null;
        foreach (string clause in clauses)
        {
            if (clause is "cell-content-is-whole-number()" or "cell-content-is-decimal-number()" or "cell-content-is-date()" or "cell-content-is-time()")
            {
                typeClause = clause;
            }
            else
            {
                ruleClause = clause;
            }
        }

        if (ruleClause is null)
        {
            return false;
        }

        type = typeClause switch
        {
            "cell-content-is-whole-number()" => "whole",
            "cell-content-is-date()" => "date",
            "cell-content-is-time()" => "time",
            _ => "decimal",
        };

        string? Arg(string text) => Operand(text, def, sheet, address, dRow, dCol);

        if (ruleClause.StartsWith("cell-content-is-in-list(", StringComparison.Ordinal))
        {
            type = "list";
            List<string> items = TopLevelSplit(Inside(ruleClause), ',', ';');
            if (items.Count > 0 && items.All(static i => i.Length >= 2 && i[0] == '"' && i[^1] == '"'))
            {
                f1 = "\"" + string.Join(',', items.Select(static i => i[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal))) + "\"";
                return true;
            }

            if (items.Count == 1)
            {
                f1 = Arg(items[0]);
                return f1 is not null;
            }

            return false;
        }

        if (ruleClause.StartsWith("is-true-formula(", StringComparison.Ordinal))
        {
            type = "custom";
            f1 = Arg(Inside(ruleClause));
            return f1 is not null;
        }

        bool length = ruleClause.StartsWith("cell-content-text-length", StringComparison.Ordinal);
        if (length)
        {
            type = "textLength";
        }

        string between = length ? "cell-content-text-length-is-between(" : "cell-content-is-between(";
        string notBetween = length ? "cell-content-text-length-is-not-between(" : "cell-content-is-not-between(";
        if (ruleClause.StartsWith(between, StringComparison.Ordinal) || ruleClause.StartsWith(notBetween, StringComparison.Ordinal))
        {
            op = ruleClause.StartsWith(between, StringComparison.Ordinal) ? "between" : "notBetween";
            List<string> args = TopLevelSplit(Inside(ruleClause), ',', ';');
            if (args.Count != 2)
            {
                return false;
            }

            f1 = Arg(args[0]);
            f2 = Arg(args[1]);
            return f1 is not null && f2 is not null;
        }

        if (TryComparison(ruleClause, length ? "cell-content-text-length()" : "cell-content()", out string comparison, out string operand))
        {
            op = comparison;
            f1 = Arg(operand);
            return f1 is not null;
        }

        return false;
    }

    // ---- conditional formats (calcext:conditional-formats) ------------------------------------------------------------------------------------------

    private static readonly string[] IconSets =
    [
        "3Arrows", "3ArrowsGray", "3Flags", "3TrafficLights1", "3TrafficLights2", "3Signs", "3Symbols", "3Symbols2", "4Arrows", "4ArrowsGray", "4Rating",
        "4RedToBlack", "4TrafficLights", "5Arrows", "5ArrowsGray", "5Rating", "5Quarters",
    ];

    private void WriteConditionalFormats(XmlWriter w, MarkupNode container, string sheetName, ref int priority)
    {
        foreach (MarkupNode format in container.ChildElements)
        {
            if (!format.IsElement(CalcExt, "conditional-format"))
            {
                continue;
            }

            List<Range> ranges = ReadRanges(A(format, CalcExt, "target-range-address"));
            if (ranges.Count == 0)
            {
                Lost("loss.conditional-formats", "conditional formats", "A conditional format whose range cannot be read was dropped.");
                continue;
            }

            string sqref = string.Join(' ', ranges.Select(static r => RangeText(r)));
            int firstRow = Math.Max(1, ranges[0].Row1);
            int firstCol = Math.Max(1, ranges[0].Col1);
            var rules = new StringBuilder();
            foreach (MarkupNode rule in format.ChildElements)
            {
                switch (rule.LocalNameString)
                {
                    case "condition" when rule.NamespaceUri == CalcExt:
                        if (!TryConditionRule(rule, sheetName, firstRow, firstCol, priority, rules))
                        {
                            Lost("loss.conditional-formats", "conditional formats", "A conditional format rule with no counterpart was not carried over.");
                        }
                        else
                        {
                            priority++;
                        }

                        break;
                    case "color-scale":
                        if (TryColorScale(rule, sheetName, firstRow, firstCol, priority, rules))
                        {
                            priority++;
                        }

                        break;
                    case "data-bar":
                        if (TryDataBar(rule, sheetName, firstRow, firstCol, priority, rules))
                        {
                            priority++;
                        }

                        break;
                    case "icon-set":
                        if (TryIconSet(rule, sheetName, firstRow, firstCol, priority, rules))
                        {
                            priority++;
                        }

                        break;
                    default:
                        Lost("loss.conditional-formats", "conditional formats", "A conditional format rule with no counterpart was not carried over.");
                        break;
                }
            }

            if (rules.Length == 0)
            {
                continue;
            }

            w.WriteStartElement("conditionalFormatting");
            w.WriteAttribute("sqref", sqref);
            w.WriteRaw(Encoding.UTF8.GetBytes(rules.ToString()));
            w.WriteEndElement();
        }
    }

    private static string Esc(string s) => OdfLengths.Escape(s);

    private bool TryConditionRule(MarkupNode rule, string sheet, int row, int col, int priority, StringBuilder rules)
    {
        string value = A(rule, CalcExt, "value") ?? string.Empty;
        string address = Col(col) + I(row);
        (int dRow, int dCol) = BaseDelta(A(rule, CalcExt, "base-cell-address"), row, col);
        int dxf = A(rule, CalcExt, "apply-style-name") is { } style ? DxfIndex(style, rule) : -1;
        string dxfAttr = dxf >= 0 ? " dxfId=\"" + I(dxf) + "\"" : string.Empty;
        string? Arg(string t) => Operand(t, rule, sheet, address, dRow, dCol);
        string cell = address;

        string head = "<cfRule";
        string tail = " priority=\"" + I(priority) + "\"";

        if (value.StartsWith("formula-is(", StringComparison.Ordinal))
        {
            string? f = Arg(Inside(value));
            if (f is null)
            {
                return false;
            }

            rules.Append(head).Append(" type=\"expression\"").Append(dxfAttr).Append(tail).Append("><formula>").Append(Esc(f)).Append("</formula></cfRule>");
            return true;
        }

        if (value.StartsWith("between(", StringComparison.Ordinal) || value.StartsWith("not-between(", StringComparison.Ordinal))
        {
            List<string> args = TopLevelSplit(Inside(value), ',', ';');
            string? a = args.Count == 2 ? Arg(args[0]) : null;
            string? b = args.Count == 2 ? Arg(args[1]) : null;
            if (a is null || b is null)
            {
                return false;
            }

            rules.Append(head).Append(" type=\"cellIs\"").Append(dxfAttr).Append(tail).Append(" operator=\"").Append(value.StartsWith("not-", StringComparison.Ordinal) ? "notBetween" : "between")
                .Append("\"><formula>").Append(Esc(a)).Append("</formula><formula>").Append(Esc(b)).Append("</formula></cfRule>");
            return true;
        }

        foreach ((string symbol, string name) in Comparisons)
        {
            if (value.StartsWith(symbol, StringComparison.Ordinal))
            {
                string? f = Arg(value[symbol.Length..]);
                if (f is null)
                {
                    return false;
                }

                rules.Append(head).Append(" type=\"cellIs\"").Append(dxfAttr).Append(tail).Append(" operator=\"").Append(name).Append("\"><formula>").Append(Esc(f)).Append("</formula></cfRule>");
                return true;
            }
        }

        // Text rules (the formulas are the ones a spreadsheet application writes for them).
        foreach ((string prefix, string op, string template) in new[]
        {
            ("contains-text(", "containsText", "NOT(ISERROR(SEARCH({0},{1})))"),
            ("not-contains-text(", "notContains", "ISERROR(SEARCH({0},{1}))"),
            ("begins-with(", "beginsWith", "LEFT({1},LEN({0}))={0}"),
            ("ends-with(", "endsWith", "RIGHT({1},LEN({0}))={0}"),
        })
        {
            if (value.StartsWith(prefix, StringComparison.Ordinal))
            {
                string? text = Arg(Inside(value));
                if (text is null || text.Length < 2 || text[0] != '"')
                {
                    return false;
                }

                string f = string.Format(CultureInfo.InvariantCulture, template, text, cell);
                rules.Append(head).Append(" type=\"").Append(op == "notContains" ? "notContainsText" : op == "containsText" ? "containsText" : op == "beginsWith" ? "beginsWith" : "endsWith")
                    .Append('"').Append(dxfAttr).Append(tail).Append(" operator=\"").Append(op).Append("\" text=\"").Append(Esc(text[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal))).Append("\"><formula>").Append(Esc(f)).Append("</formula></cfRule>");
                return true;
            }
        }

        switch (value)
        {
            case "is-error":
                rules.Append(head).Append(" type=\"containsErrors\"").Append(dxfAttr).Append(tail).Append("><formula>ISERROR(").Append(cell).Append(")</formula></cfRule>");
                return true;
            case "is-no-error":
                rules.Append(head).Append(" type=\"notContainsErrors\"").Append(dxfAttr).Append(tail).Append("><formula>NOT(ISERROR(").Append(cell).Append("))</formula></cfRule>");
                return true;
            case "duplicate":
                rules.Append(head).Append(" type=\"duplicateValues\"").Append(dxfAttr).Append(tail).Append("/>");
                return true;
            case "unique":
                rules.Append(head).Append(" type=\"uniqueValues\"").Append(dxfAttr).Append(tail).Append("/>");
                return true;
            case "above-average" or "below-average" or "above-equal-average" or "below-equal-average":
                rules.Append(head).Append(" type=\"aboveAverage\"").Append(dxfAttr).Append(tail);
                if (value.StartsWith("below", StringComparison.Ordinal))
                {
                    rules.Append(" aboveAverage=\"0\"");
                }

                if (value.EndsWith("equal-average", StringComparison.Ordinal))
                {
                    rules.Append(" equalAverage=\"1\"");
                }

                rules.Append("/>");
                return true;
            default:
                break;
        }

        foreach ((string prefix, bool bottom, bool percent) in new[] { ("top-elements(", false, false), ("bottom-elements(", true, false), ("top-percent(", false, true), ("bottom-percent(", true, true) })
        {
            if (value.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(Inside(value).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int rank) && rank > 0)
            {
                rules.Append(head).Append(" type=\"top10\"").Append(dxfAttr).Append(tail);
                if (percent)
                {
                    rules.Append(" percent=\"1\"");
                }

                if (bottom)
                {
                    rules.Append(" bottom=\"1\"");
                }

                rules.Append(" rank=\"").Append(rank).Append("\"/>");
                return true;
            }
        }

        return false;
    }

    private string? CfvoXml(MarkupNode entry, string sheet, int row, int col)
    {
        string type = A(entry, CalcExt, "type") ?? "minimum";
        string val = A(entry, CalcExt, "value") ?? "0";
        switch (type)
        {
            case "minimum" or "auto-minimum":
                return "<cfvo type=\"min\"/>";
            case "maximum" or "auto-maximum":
                return "<cfvo type=\"max\"/>";
            case "percentile":
                return "<cfvo type=\"percentile\" val=\"" + Esc(val) + "\"/>";
            case "percent":
                return "<cfvo type=\"percent\" val=\"" + Esc(val) + "\"/>";
            case "number":
                return "<cfvo type=\"num\" val=\"" + Esc(val) + "\"/>";
            case "formula":
                string? f = Operand(val, entry, sheet, Col(col) + I(row), 0, 0);
                return f is null ? null : "<cfvo type=\"formula\" val=\"" + Esc(f) + "\"/>";
            default:
                return null;
        }
    }

    private bool TryColorScale(MarkupNode scale, string sheet, int row, int col, int priority, StringBuilder rules)
    {
        var cfvo = new StringBuilder();
        var colors = new StringBuilder();
        int count = 0;
        foreach (MarkupNode e in scale.ChildElements)
        {
            string? c = Rgb(A(e, CalcExt, "color"));
            string? v = CfvoXml(e, sheet, row, col);
            if (c is null || v is null)
            {
                Lost("loss.conditional-formats", "conditional formats", "A colour scale with an entry that cannot be read was not carried over.");
                return false;
            }

            cfvo.Append(v);
            colors.Append("<color rgb=\"").Append(c).Append("\"/>");
            count++;
        }

        if (count is not (2 or 3))
        {
            return false;
        }

        rules.Append("<cfRule type=\"colorScale\" priority=\"").Append(priority).Append("\"><colorScale>").Append(cfvo).Append(colors).Append("</colorScale></cfRule>");
        return true;
    }

    private bool TryDataBar(MarkupNode bar, string sheet, int row, int col, int priority, StringBuilder rules)
    {
        var entries = new List<string>();
        foreach (MarkupNode e in bar.ChildElements)
        {
            if (e.IsElement(CalcExt, "formatting-entry") && CfvoXml(e, sheet, row, col) is { } v)
            {
                entries.Add(v);
            }
        }

        if (entries.Count != 2)
        {
            return false;
        }

        string color = Rgb(A(bar, CalcExt, "positive-color")) ?? "FF638EC6";
        rules.Append("<cfRule type=\"dataBar\" priority=\"").Append(priority).Append("\"><dataBar>").Append(entries[0]).Append(entries[1])
            .Append("<color rgb=\"").Append(color).Append("\"/></dataBar></cfRule>");

        return true;
    }

    private bool TryIconSet(MarkupNode set, string sheet, int row, int col, int priority, StringBuilder rules)
    {
        string? name = A(set, CalcExt, "icon-set-type");
        if (name is null || !IconSets.Contains(name, StringComparer.Ordinal))
        {
            Lost("loss.conditional-formats", "conditional formats", "An icon set with no counterpart was not carried over.");
            return false;
        }

        var sb = new StringBuilder();
        foreach (MarkupNode e in set.ChildElements)
        {
            if (e.IsElement(CalcExt, "formatting-entry") && CfvoXml(e, sheet, row, col) is { } v)
            {
                sb.Append(v);
            }
        }

        rules.Append("<cfRule type=\"iconSet\" priority=\"").Append(priority).Append("\"><iconSet iconSet=\"").Append(name).Append("\">").Append(sb).Append("</iconSet></cfRule>");
        return true;
    }

    // ---- page layout -----------------------------------------------------------------------------------------------------------------------------

    private sealed class PageLayoutInfo
    {
        public double Width;
        public double Height;
        public bool Landscape;
        public double Left = 0.7;
        public double Right = 0.7;
        public double Top = 0.75;
        public double Bottom = 0.75;
        public double Header = 0.3;
        public double Footer = 0.3;
        public int Scale;
        public int FitWidth = -1;
        public int FitHeight = -1;
        public bool FitToPage;
        public bool Grid;
        public bool Headings;
        public bool CenterH;
        public bool CenterV;
        public string? FirstPage;
        public string HeaderText = string.Empty;
        public string FooterText = string.Empty;
    }

    private PageLayoutInfo? ReadPageLayout(OdfStyle master)
    {
        string? layoutName = A(master.Element, Style, "page-layout-name");
        OdfStyle? layout = layoutName is null ? null : OdsStyleLookup.Find(_styles, "page-layout", layoutName);
        if (layout is null)
        {
            return null;
        }

        var info = new PageLayoutInfo();
        MarkupNode p = layout.Properties("page-layout-properties");
        double Points(string ns, string name, double fallback) => OdfLengths.TryPoints(A(p, ns, name), out double v) ? v : fallback;
        info.Width = Points(Fo, "page-width", 0);
        info.Height = Points(Fo, "page-height", 0);
        info.Landscape = A(p, Style, "print-orientation") == "landscape";
        info.Left = Points(Fo, "margin-left", 0.7 * 72) / 72;
        info.Right = Points(Fo, "margin-right", 0.7 * 72) / 72;
        double top = Points(Fo, "margin-top", 0.75 * 72);
        double bottom = Points(Fo, "margin-bottom", 0.75 * 72);
        info.Header = top / 72;
        info.Footer = bottom / 72;

        MarkupNode header = Child(master.Element, Style, "header");
        MarkupNode footer = Child(master.Element, Style, "footer");
        bool headerOn = !header.IsNull && A(header, Style, "display") != "false";
        bool footerOn = !footer.IsNull && A(footer, Style, "display") != "false";
        double headerBody = 0;
        double footerBody = 0;
        MarkupNode hs = Child(Child(layout.Element, Style, "header-style"), Style, "header-footer-properties");
        MarkupNode fs = Child(Child(layout.Element, Style, "footer-style"), Style, "header-footer-properties");
        if (headerOn)
        {
            headerBody = (OdfLengths.TryPoints(A(hs, Fo, "min-height"), out double hh) ? hh : 0) + (OdfLengths.TryPoints(A(hs, Fo, "margin-bottom"), out double hm) ? hm : 0);
        }

        if (footerOn)
        {
            footerBody = (OdfLengths.TryPoints(A(fs, Fo, "min-height"), out double fh) ? fh : 0) + (OdfLengths.TryPoints(A(fs, Fo, "margin-top"), out double fm) ? fm : 0);
        }

        info.Top = (top + headerBody) / 72;
        info.Bottom = (bottom + footerBody) / 72;

        string? scaleTo = A(p, Style, "scale-to");
        if (scaleTo is not null && scaleTo.EndsWith('%') && int.TryParse(scaleTo.AsSpan(0, scaleTo.Length - 1), NumberStyles.None, CultureInfo.InvariantCulture, out int pct) && pct is >= 10 and <= 400)
        {
            info.Scale = pct;
        }

        if (int.TryParse(A(p, Style, "scale-to-X"), NumberStyles.None, CultureInfo.InvariantCulture, out int x) || int.TryParse(A(p, Style, "scale-to-Y"), NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            info.FitToPage = true;
            info.FitWidth = int.TryParse(A(p, Style, "scale-to-X"), NumberStyles.None, CultureInfo.InvariantCulture, out x) ? x : 0;
            info.FitHeight = int.TryParse(A(p, Style, "scale-to-Y"), NumberStyles.None, CultureInfo.InvariantCulture, out int y) ? y : 0;
        }
        else if (int.TryParse(A(p, Style, "scale-to-pages"), NumberStyles.None, CultureInfo.InvariantCulture, out int pages) && pages == 1)
        {
            info.FitToPage = true;
            info.FitWidth = 1;
            info.FitHeight = 1;
        }

        string print = A(p, Style, "print") ?? string.Empty;
        info.Grid = print.Contains("grid", StringComparison.Ordinal);
        info.Headings = print.Contains("headers", StringComparison.Ordinal);
        string? centering = A(p, Style, "table-centering");
        info.CenterH = centering is "horizontal" or "both";
        info.CenterV = centering is "vertical" or "both";
        string? first = A(p, Style, "first-page-number");
        if (first is not null && int.TryParse(first, NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            info.FirstPage = first;
        }

        if (headerOn)
        {
            info.HeaderText = HeaderFooterText(header);
        }

        if (footerOn)
        {
            info.FooterText = HeaderFooterText(footer);
        }

        return info;
    }

    // ECMA-376 Part 1 §18.3.1.39 headerFooter: &L, &C and &R start the three regions; &P, &N, &A, &D, &T and &F are fields; && is an ampersand.
    private static string HeaderFooterText(MarkupNode node)
    {
        var sb = new StringBuilder();
        bool regions = false;
        foreach ((string local, string code) in new[] { ("region-left", "&L"), ("region-center", "&C"), ("region-right", "&R") })
        {
            MarkupNode region = Child(node, Style, local);
            if (region.IsNull)
            {
                continue;
            }

            regions = true;
            sb.Append(code);
            AppendHeaderParagraphs(region, sb);
        }

        if (!regions)
        {
            var body = new StringBuilder();
            AppendHeaderParagraphs(node, body);
            if (body.Length > 0)
            {
                sb.Append("&C").Append(body);
            }
        }

        return sb.ToString();
    }

    private static void AppendHeaderParagraphs(MarkupNode parent, StringBuilder sb)
    {
        bool first = true;
        foreach (MarkupNode p in parent.ChildElements)
        {
            if (!p.IsElement(Text, "p"))
            {
                continue;
            }

            if (!first)
            {
                sb.Append('\n');
            }

            first = false;
            AppendHeaderInline(p, sb, 0);
        }
    }

    private static void AppendHeaderInline(MarkupNode parent, StringBuilder sb, int depth)
    {
        if (depth > 8)
        {
            return;
        }

        foreach (MarkupNode n in parent.Children)
        {
            if (n.Kind == MarkupNodeKind.Text)
            {
                sb.Append(n.InnerText.Replace("&", "&&", StringComparison.Ordinal));
            }
            else if (n.Kind == MarkupNodeKind.Element && n.NamespaceUri == Text)
            {
                switch (n.LocalNameString)
                {
                    case "page-number":
                        sb.Append("&P");
                        break;
                    case "page-count":
                        sb.Append("&N");
                        break;
                    case "sheet-name":
                        sb.Append("&A");
                        break;
                    case "date":
                        sb.Append("&D");
                        break;
                    case "time":
                        sb.Append("&T");
                        break;
                    case "file-name":
                        sb.Append("&F");
                        break;
                    case "s":
                        sb.Append(' ');
                        break;
                    case "title" or "subject" or "initial-creator":
                        break;
                    default:
                        AppendHeaderInline(n, sb, depth + 1);
                        break;
                }
            }
        }
    }

    // ECMA-376 Part 1 §18.3.1.51 pageSetup paperSize: the sizes that matter, matched within 3 mm.
    private static int PaperSize(double widthPt, double heightPt)
    {
        double w = Math.Min(widthPt, heightPt) / 72 * 25.4;
        double h = Math.Max(widthPt, heightPt) / 72 * 25.4;
        (double Width, double Height, int Id)[] sizes =
        [
            (210, 297, 9), (297, 420, 8), (148, 210, 11), (215.9, 279.4, 1), (215.9, 355.6, 5), (279.4, 431.8, 3), (184.15, 266.7, 7), (182, 257, 13), (250, 353, 12), (176, 250, 34),
        ];
        foreach ((double sw, double sh, int id) in sizes)
        {
            if (Math.Abs(w - sw) < 3 && Math.Abs(h - sh) < 3)
            {
                return id;
            }
        }

        return 0;
    }

    private static void WritePageSetup(XmlWriter w, PageLayoutInfo? l)
    {
        if (l is null)
        {
            return;
        }

        if (l.Grid || l.Headings || l.CenterH || l.CenterV)
        {
            w.WriteStartElement("printOptions");
            if (l.CenterH)
            {
                w.WriteAttribute("horizontalCentered", "1");
            }

            if (l.CenterV)
            {
                w.WriteAttribute("verticalCentered", "1");
            }

            if (l.Headings)
            {
                w.WriteAttribute("headings", "1");
            }

            if (l.Grid)
            {
                w.WriteAttribute("gridLines", "1");
            }

            w.WriteEndElement();
        }

        w.WriteStartElement("pageMargins");
        w.WriteAttribute("left", Inv(Math.Round(l.Left, 4)));
        w.WriteAttribute("right", Inv(Math.Round(l.Right, 4)));
        w.WriteAttribute("top", Inv(Math.Round(l.Top, 4)));
        w.WriteAttribute("bottom", Inv(Math.Round(l.Bottom, 4)));
        w.WriteAttribute("header", Inv(Math.Round(l.Header, 4)));
        w.WriteAttribute("footer", Inv(Math.Round(l.Footer, 4)));
        w.WriteEndElement();

        w.WriteStartElement("pageSetup");
        int paper = PaperSize(l.Width, l.Height);
        if (paper != 0)
        {
            w.WriteAttribute("paperSize", I(paper));
        }

        if (l.Scale > 0 && !l.FitToPage)
        {
            w.WriteAttribute("scale", I(l.Scale));
        }

        if (l.FitToPage)
        {
            w.WriteAttribute("fitToWidth", I(l.FitWidth));
            w.WriteAttribute("fitToHeight", I(l.FitHeight));
        }

        if (l.FirstPage is not null)
        {
            w.WriteAttribute("firstPageNumber", l.FirstPage);
            w.WriteAttribute("useFirstPageNumber", "1");
        }

        w.WriteAttribute("orientation", l.Landscape ? "landscape" : "portrait");
        w.WriteEndElement();

        if (l.HeaderText.Length > 0 || l.FooterText.Length > 0)
        {
            w.WriteStartElement("headerFooter");
            if (l.HeaderText.Length > 0)
            {
                w.WriteStartElement("oddHeader");
                w.WriteText(l.HeaderText);
                w.WriteEndElement();
            }

            if (l.FooterText.Length > 0)
            {
                w.WriteStartElement("oddFooter");
                w.WriteText(l.FooterText);
                w.WriteEndElement();
            }

            w.WriteEndElement();
        }
    }
}
