using RecursiveAI.OneOffice.Calc;

namespace RecursiveAI.OneOffice.Spreadsheets;

public sealed partial class Cell
{
    /// <summary>
    /// Sets a dynamic-array formula: one whose result spills into the cells below and to the right when the workbook is calculated
    /// (<see cref="Workbook.Recalculate"/>), as a formula typed in current Excel does. The cell is stored as an array formula with the
    /// dynamic-array cell metadata ([MS-XLSX] CT_DynamicArrayProperties), and the workbook gets that metadata part if it has none.
    /// Like <see cref="FormulaDetail"/> it discards the cached value and asks the workbook to recalculate when it is next opened.
    /// </summary>
    /// <param name="formula">The formula as a user types it (function names without the file-format prefixes), with or without a leading <c>=</c>.</param>
    /// <exception cref="FormulaParseException">The formula text cannot be read.</exception>
    /// <exception cref="NotSupportedException">The workbook already has a metadata part that holds no dynamic-array entry.</exception>
    public void SetDynamicArrayFormula(string formula)
    {
        ArgumentNullException.ThrowIfNull(formula);
        FormulaNode tree = FormulaParser.Parse(formula, new FormulaParserOptions { FileForm = false, LeadingEquals = true, Limits = _sheet.Workbook.Limits });
        string text = FormulaPrinter.Print(tree);
        int cm = _sheet.Workbook.EnsureDynamicArrayMetadata();
        CellStore store = _sheet.Store;
        // Keep the old spill range so the cells the earlier formula filled are cleared by the next calculation.
        CellFormula? before = store.GetFormula(_row, _column);
        string reference = before is { Kind: CellFormulaKind.Array, Reference: { } old } ? old : Address.ToString();
        FormulaDetail = new CellFormula(text, CellFormulaKind.Array, reference);
        CellExtras extras = store.GetExtras(_row, _column) ?? new CellExtras();
        string attrs = extras.Attributes ?? string.Empty;
        if (!attrs.Contains(" cm=\"", StringComparison.Ordinal))
        {
            extras.Attributes = attrs + " cm=\"" + cm.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\"";
        }

        store.SetExtras(_row, _column, extras);
    }
}
