using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Documents.Fields;
using RecursiveAI.OneDocument.Documents.MailMerge;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Schema;

namespace RecursiveAI.OneDocument.Layout;

// ECMA-376 Part 1 §17.16.3 (formulas and expressions): a formula over numbers, bookmark names and, in a table cell, the position words ABOVE, BELOW, LEFT
// and RIGHT. The expression goes through the same calc engine as spreadsheet formulas; the table is a one-sheet grid whose cells read as
// the numbers they hold (currency signs, thousands separators and percent signs are dropped as Word does).
internal static partial class FormulaField
{
    [GeneratedRegex(@"\b(ABOVE|BELOW|LEFT|RIGHT)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Position();

    // The functions Word's formulas have. They are the spreadsheet functions except where §17.16.3.4 differs: MOD keeps the sign of its first
    // argument (MOD(21,-5) is 1, not -4) and INT drops the fraction (INT(-2.5) is -2, not -3).
    private static readonly Lazy<FunctionRegistry> Functions = new(static () =>
    {
        FunctionRegistry registry = FunctionRegistry.CreateDefault();
        registry.AddCustom("MOD", 2, 2, static a =>
            a[0].Kind == ValueKind.Number && a[1].Kind == ValueKind.Number && a[1].Number != 0 ? CalcValue.FromNumber(a[0].Number % a[1].Number) : CalcValue.FromError(CalcError.Value));
        registry.AddCustom("INT", 1, 1, static a => a[0].Kind == ValueKind.Number ? CalcValue.FromNumber(Math.Truncate(a[0].Number)) : CalcValue.FromError(CalcError.Value));
        return registry;
    });

    public static string? Evaluate(FieldSite site, FieldInstruction f)
    {
        string expression = string.Join(' ', f.Arguments).Trim();
        if (expression.Length == 0)
        {
            return null;
        }

        var grid = TableGrid.Find(site.Paragraph.Markup.Node);
        string text = Position().Replace(expression, m => grid is null ? "#REF!" : grid.Range(m.Value.ToUpperInvariant()));
        if (!FormulaParser.TryParse(text, out FormulaNode? node, out _) || node is null)
        {
            return "!Syntax Error, " + expression;
        }

        try
        {
            var evaluator = new Evaluator(new Resolver(site.Env, grid), Functions.Value, options: new EvaluatorOptions { Clock = site.Env.Options.Clock });
            CalcValue v = evaluator.Evaluate(node, new CalcCell(null, 1, 1));
            v = evaluator.ToScalar(v);
            switch (v.Kind)
            {
                case ValueKind.Number:
                    // §17.16.3: 1/3 is 0.33, so without a picture the result keeps at most two decimals.
                    string number = f.Switch("#")?.Value is { } picture ? WordPicture.FormatNumber(v.Number, picture) : Math.Round(v.Number, 2, MidpointRounding.AwayFromZero).ToString("0.##", CultureInfo.InvariantCulture);
                    return FieldEvaluator.Finish(number, f);
                case ValueKind.Text:
                    return FieldEvaluator.Finish(v.Text, f);
                case ValueKind.Boolean:
                    return v.Boolean ? "1" : "0";
                case ValueKind.Blank:
                    return "0";
                default:
                    return "!Syntax Error, " + expression;
            }
        }
        catch (RecursiveAI.OneDocument.OfficeException)
        {
            return "!Syntax Error, " + expression;
        }
    }

    private sealed class Resolver(FieldEnv env, TableGrid? grid) : ICalcResolver
    {
        public CalcValue GetCell(string? sheet, int row, int column) => grid is not null && grid.TryNumber(row, column, out double n) ? CalcValue.FromNumber(n) : CalcValue.Blank;

        // A whole row or column (SUM(B:B)) reads only the cells the table has.
        public CellArea ClipArea(in CellArea area) => grid is null ? area : area with { Row2 = Math.Min(area.Row2, grid.RowCount), Column2 = Math.Min(area.Column2, grid.ColumnCount) };

        public CalcValue ResolveName(string? scopeSheet, string name, in CalcCell origin)
        {
            if (env.Model.Bookmark(name) is { } bm && TableGrid.TryParseNumber(bm.Text.ToString(), out double n))
            {
                return CalcValue.FromNumber(n);
            }

            return CalcValue.FromError(CalcError.Name);
        }
    }

    // The cells of the table the field sits in, read as a grid of rows and columns (merged cells count once).
    private sealed class TableGrid
    {
        private readonly List<List<MarkupNode>> _rows = [];
        private int _row;
        private int _column;

        public static TableGrid? Find(MarkupNode paragraph)
        {
            MarkupNode cell = default;
            MarkupNode tbl = default;
            for (MarkupNode n = paragraph.Parent; !n.IsNull; n = n.Parent)
            {
                if (cell.IsNull && n.Kind == MarkupNodeKind.Element && Is(n, "tc"))
                {
                    cell = n;
                }
                else if (!cell.IsNull && n.Kind == MarkupNodeKind.Element && Is(n, "tbl"))
                {
                    tbl = n;
                    break;
                }
            }

            if (tbl.IsNull)
            {
                return null;
            }

            var grid = new TableGrid();
            for (MarkupNode tr = tbl.FirstChild; !tr.IsNull; tr = tr.NextSibling)
            {
                if (tr.Kind != MarkupNodeKind.Element || !Is(tr, "tr"))
                {
                    continue;
                }

                var cells = new List<MarkupNode>();
                for (MarkupNode tc = tr.FirstChild; !tc.IsNull; tc = tc.NextSibling)
                {
                    if (tc.Kind == MarkupNodeKind.Element && Is(tc, "tc"))
                    {
                        if (tc == cell)
                        {
                            grid._row = grid._rows.Count + 1;
                            grid._column = cells.Count + 1;
                        }

                        cells.Add(tc);
                    }
                }

                grid._rows.Add(cells);
            }

            return grid._row == 0 ? null : grid;
        }

        private static bool Is(MarkupNode n, string local) => new MarkupRef(n).Namespace == SchemaNamespace.Wordprocessing && Xml.Eq(n.LocalName, local);

        // Word's cell references are A1 style; the position words stand for the contiguous run of numeric cells in that direction.
        public string Range(string word)
        {
            int dr = word switch { "ABOVE" => -1, "BELOW" => 1, _ => 0 };
            int dc = word switch { "LEFT" => -1, "RIGHT" => 1, _ => 0 };
            int r = _row + dr;
            int c = _column + dc;
            int lastR = _row;
            int lastC = _column;
            // The range stops at a blank or non-numeric cell, except that a blank first cell counts as 0 (§17.16.3.5).
            while (r >= 1 && c >= 1 && r <= _rows.Count && c <= _rows[r - 1].Count && (TryNumber(r, c, out _) || (lastR == _row && lastC == _column && IsBlank(r, c))))
            {
                lastR = r;
                lastC = c;
                r += dr;
                c += dc;
            }

            if (lastR == _row && lastC == _column)
            {
                // Nothing numeric that way: a cell far outside the table reads as empty.
                return "ZZ9999";
            }

            return Cell(Math.Min(lastR, _row + dr), Math.Min(lastC, _column + dc)) + ":" + Cell(Math.Max(lastR, _row + dr), Math.Max(lastC, _column + dc));
        }

        private static string Cell(int row, int column)
        {
            var sb = new StringBuilder();
            for (int c = column; c > 0; c = (c - 1) / 26)
            {
                sb.Insert(0, (char)('A' + ((c - 1) % 26)));
            }

            return sb.Append(row.ToString(CultureInfo.InvariantCulture)).ToString();
        }

        public int RowCount => _rows.Count;

        public int ColumnCount => _rows.Count == 0 ? 0 : _rows.Max(static r => r.Count);

        private bool IsBlank(int row, int column) => TextOf(_rows[row - 1][column - 1]).Trim().Length == 0;

        public bool TryNumber(int row, int column, out double number)
        {
            number = 0;
            if (row < 1 || row > _rows.Count || column < 1 || column > _rows[row - 1].Count)
            {
                return false;
            }

            if (row == _row && column == _column)
            {
                return false; // the field's own cell
            }

            return TryParseNumber(TextOf(_rows[row - 1][column - 1]), out number);
        }

        private static string TextOf(MarkupNode cell)
        {
            var sb = new StringBuilder();
            Collect(cell, sb);
            return sb.ToString();
        }

        private static void Collect(MarkupNode n, StringBuilder sb)
        {
            for (MarkupNode c = n.FirstChild; !c.IsNull; c = c.NextSibling)
            {
                if (c.Kind != MarkupNodeKind.Element)
                {
                    continue;
                }

                if (Is(c, "t"))
                {
                    sb.Append(c.InnerText);
                }
                else
                {
                    Collect(c, sb);
                }
            }
        }

        // Currency signs, thousands separators and a percent sign do not stop a cell from being a number (to check against Word).
        public static bool TryParseNumber(string text, out double number)
        {
            string t = text.Trim().Replace(",", string.Empty, StringComparison.Ordinal).Replace("$", string.Empty, StringComparison.Ordinal).TrimEnd('%').Trim();
            return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
        }
    }
}
