using System.Globalization;
using RecursiveAI.OneDocument.Calc;

namespace RecursiveAI.OneDocument.Tests.Calc;

/// <summary>A grid in a dictionary, for evaluator tests.</summary>
internal sealed class FakeResolver : ICalcResolver
{
    private readonly Dictionary<(string, int, int), CalcValue> _cells = [];

    public Dictionary<string, CalcValue> Names { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, CalcValue> Members { get; } = new(StringComparer.OrdinalIgnoreCase);

    public FakeResolver Set(string a1, CalcValue v, string sheet = "Sheet1")
    {
        var n = (FormulaNode)FormulaParser.Parse(a1);
        var c = (CellReferenceNode)n;
        _cells[(sheet, c.Row.Index, c.Column.Index)] = v;
        return this;
    }

    public CalcValue GetCell(string? sheet, int row, int column) =>
        _cells.TryGetValue((sheet ?? "Sheet1", row, column), out CalcValue v) ? v : CalcValue.Blank;

    public CalcValue ResolveName(string? scopeSheet, string name, in CalcCell origin) =>
        Names.TryGetValue(name, out CalcValue v) ? v : CalcValue.FromError(CalcError.Name);

    public CalcValue ResolveMember(IReadOnlyList<string> segments) =>
        Members.TryGetValue(string.Join('.', segments), out CalcValue v) ? v : CalcValue.FromError(CalcError.Name);
}

internal static class CalcTestHelper
{
    public static CalcValue Eval(string formula, FakeResolver? res = null, EvaluatorOptions? options = null, int row = 1, int col = 1)
    {
        var ev = new Evaluator(res ?? new FakeResolver(), options: options);
        FormulaNode n = FormulaParser.Parse(formula, new FormulaParserOptions { FileForm = false });
        return ev.Evaluate(n, new CalcCell("Sheet1", row, col));
    }

    public static string Show(CalcValue v)
    {
        if (v.Kind == ValueKind.Array)
        {
            CalcArray a = v.Array!;
            var rows = new List<string>();
            for (int r = 0; r < a.Rows; r++)
            {
                var cols = new List<string>();
                for (int c = 0; c < a.Columns; c++)
                {
                    cols.Add(Show(a[r, c]));
                }

                rows.Add(string.Join(",", cols));
            }

            return "{" + string.Join(";", rows) + "}";
        }

        return v.Kind switch
        {
            ValueKind.Number => v.Number.ToString("R", CultureInfo.InvariantCulture),
            ValueKind.Text => "\"" + v.Text + "\"",
            ValueKind.Boolean => v.Boolean ? "TRUE" : "FALSE",
            ValueKind.Error => CalcErrors.ToText(v.Error),
            ValueKind.Blank => "blank",
            _ => v.ToString(),
        };
    }
}
