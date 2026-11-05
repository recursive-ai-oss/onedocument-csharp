using RecursiveAI.OneOffice.NumberFormats;

namespace RecursiveAI.OneOffice.Spreadsheets;

public sealed partial class Cell
{
    /// <summary>Gets the cell's effective style: font, fill, border, alignment, protection and number format after the named cell style is applied.</summary>
    public ResolvedCellStyle Style => _sheet.Workbook.Styles.Resolve(StyleIndex);

    /// <summary>Gives the cell the cell format described by <paramref name="spec"/>, adding one to the style sheet when no equal one exists.</summary>
    /// <param name="spec">The description.</param>
    public void SetStyle(CellStyleSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        StyleIndex = _sheet.Workbook.Styles.GetOrAdd(spec);
    }

    /// <summary>Changes part of the cell's style: <paramref name="change"/> receives a spec equal to the current style and returns the new one.</summary>
    /// <param name="change">The edit, for example <c>s =&gt; s with { Font = s.Font! with { Bold = true } }</c>.</param>
    public void UpdateStyle(Func<CellStyleSpec, CellStyleSpec> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        StyleSheet styles = _sheet.Workbook.Styles;
        StyleIndex = styles.GetOrAdd(change(CellStyleSpec.From(styles.Resolve(StyleIndex))));
    }

    /// <summary>
    /// Gets the text Excel shows for the cell: a number through the cell's number format (with the workbook's 1900 or 1904 date system),
    /// text through the format's text section, TRUE/FALSE for booleans, the error text for errors, the empty string for an empty cell.
    /// General shows at most 11 characters, as in a default-width column; a number a format cannot show gives a row of <c>#</c>.
    /// </summary>
    public string FormattedText => GetFormattedText(11);

    /// <summary>Gets the text Excel shows for the cell in a column that fits <paramref name="generalWidth"/> characters of a General number (see <see cref="FormattedText"/>, which uses 11, the width of a default column).</summary>
    /// <param name="generalWidth">The characters a General number may use before it turns to scientific form or rounds; at most 15 significant digits are shown whatever the width.</param>
    /// <returns>The displayed text.</returns>
    public string GetFormattedText(int generalWidth)
    {
        CellValue v = Value;
        switch (v.Kind)
        {
            case CellValueKind.Number:
                return Format().Format(v.Number, new FormatOptions(_sheet.Workbook.DateSystem, GeneralWidth: generalWidth));
            case CellValueKind.Text:
                return Format().FormatText(_sheet.GetText(v));
            case CellValueKind.Boolean:
                return v.Number != 0 ? "TRUE" : "FALSE";
            default:
                return _sheet.GetText(v);
        }
    }

    /// <summary>Writes <see cref="FormattedText"/> of a number cell into a buffer without allocating.</summary>
    /// <param name="destination">Where to write.</param>
    /// <param name="written">The characters written.</param>
    /// <returns>True when the cell is a number and the text fitted; false otherwise (use <see cref="FormattedText"/> for other kinds).</returns>
    public bool TryFormatNumber(Span<char> destination, out int written)
    {
        written = 0;
        CellValue v = Value;
        if (v.Kind != CellValueKind.Number)
        {
            return false;
        }

        FormatResult r = Format().TryFormat(v.Number, destination, new FormatOptions(_sheet.Workbook.DateSystem));
        written = r.Length;
        return r.Status is FormatStatus.Ok or FormatStatus.DoesNotFit;
    }

    private NumberFormat Format() => _sheet.Workbook.Styles.GetNumberFormat(StyleIndex);

    /// <summary>Gets a value indicating whether the cell holds a number that its number format shows as a date or time.</summary>
    public bool IsDate => Value.Kind == CellValueKind.Number && Format().IsDateTime;

    /// <summary>Gets the date and time of a numeric cell in the workbook's date system, or null when the cell is not a number or the number is not a valid serial (negative, past 9999-12-31, or the day Excel's 1900 system invents).</summary>
    public DateTime? DateValue
    {
        get
        {
            CellValue v = Value;
            return v.Kind == CellValueKind.Number && ExcelDate.TryToDateTime(v.Number, _sheet.Workbook.DateSystem, out DateTime d) ? d : null;
        }
    }

    /// <summary>
    /// Sets a date as its serial number in the workbook's date system. A cell whose number format does not show dates gets the built-in
    /// date format (id 14) added to its style, so the value is displayed as a date.
    /// </summary>
    /// <param name="value">The date; the time of day is kept.</param>
    /// <exception cref="ArgumentOutOfRangeException">The date is before the system's first day or after 9999-12-31.</exception>
    public void SetDate(DateTime value)
    {
        if (!ExcelDate.TryToSerial(value, _sheet.Workbook.DateSystem, out double serial))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "The date is outside the workbook's date system.");
        }

        SetNumber(serial);
        if (!Format().IsDateTime)
        {
            string code = value.TimeOfDay == TimeSpan.Zero ? NumberFormat.GetBuiltInCode(14)! : "m/d/yyyy h:mm";
            UpdateStyle(s => s with { NumberFormatCode = code });
        }
    }
}
