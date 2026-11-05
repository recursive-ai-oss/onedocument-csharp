using System.Globalization;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.NumberFormats;

namespace RecursiveAI.OneDocument.Spreadsheets.Binding;

/// <summary>Where the cells of a <see cref="RowWriter"/> go: an XLSX sheet or a delimited text record.</summary>
internal abstract class RowSink
{
    public abstract DateSystem DateSystem { get; }

    /// <summary>Returns the style a column's cells use (0 where the output has no styles).</summary>
    public abstract int StyleFor(in ColumnSpec column);

    public abstract int HeaderStyle { get; }

    public abstract void BeginRow();

    /// <summary>Ends the row; <paramref name="width"/> is the number of columns the schema spans, for output that must not be ragged.</summary>
    public abstract void EndRow(int width);

    /// <summary>Puts the next cell in a 1-based column (never before the next one).</summary>
    public abstract void MoveTo(int column);

    public abstract void Text(ReadOnlySpan<char> text, int style, bool looksNumeric);

    public abstract void Number(double value, int style);

    public abstract void Boolean(bool value, int style);

    public abstract void DateTime(DateTime value, int style);

    public abstract void Time(TimeOnly value, int style);

    public virtual void Decimal(decimal value, int style) => Number((double)value, style);
}

/// <summary>
/// Writes the cells of one row for generated <see cref="ISpreadsheetRow{TSelf}"/> code. Columns are addressed by schema column
/// number and must be written in ascending order; a column that is not written stays empty.
/// </summary>
/// <remarks>
/// Whole numbers beyond 2^53 cannot be held exactly by a spreadsheet number, so they are written as text of their digits. A date
/// outside the range of the workbook's date system raises an <see cref="OfficeFormatException"/> (<c>binding.value-out-of-range</c>)
/// located at the cell.
/// </remarks>
public sealed class RowWriter
{
    private const long MaxExactInteger = 1L << 53;

    private readonly RowSchema _schema;
    private readonly RowSink _sink;
    private readonly string _sheetName;
    private readonly int[] _styles;
    private readonly int[] _targets;
    private readonly char[] _buffer = new char[48];
    private int _lastColumn;

    internal RowWriter(RowSchema schema, RowSink sink, string sheetName)
    {
        _schema = schema;
        _sink = sink;
        _sheetName = sheetName;
        _styles = new int[schema.Count];
        _targets = Targets(schema);
        for (int i = 0; i < schema.Count; i++)
        {
            _styles[i] = sink.StyleFor(schema.Columns[i]);
        }

        Width = schema.Count == 0 ? 0 : _targets[^1];
    }

    /// <summary>The sheet column of each schema column: its fixed index, or the one after the previous column; columns never overlap.</summary>
    internal static int[] Targets(RowSchema schema)
    {
        int[] targets = new int[schema.Count];
        int previous = 0;
        for (int i = 0; i < schema.Count; i++)
        {
            previous = Math.Max(previous + 1, schema.Columns[i].Index);
            targets[i] = previous;
        }

        return targets;
    }

    /// <summary>Gets the 1-based number of the row being written.</summary>
    public int RowNumber { get; internal set; }

    internal int Width { get; }

    internal void WriteHeader()
    {
        _sink.BeginRow();
        _lastColumn = -1;
        for (int i = 0; i < _schema.Count; i++)
        {
            if (_schema.Columns[i].CanWrite)
            {
                _sink.MoveTo(_targets[i]);
                _sink.Text(_schema.Columns[i].Name, _sink.HeaderStyle, looksNumeric: false);
            }
        }

        _sink.EndRow(Width);
    }

    internal void BeginRow()
    {
        _sink.BeginRow();
        _lastColumn = -1;
    }

    internal void EndRow() => _sink.EndRow(Width);

    private int Position(int column)
    {
        if (column <= _lastColumn)
        {
            throw new InvalidOperationException("Columns must be written in ascending order, each once.");
        }

        _lastColumn = column;
        _sink.MoveTo(_targets[column]);
        return _styles[column];
    }

    /// <summary>Writes text. Null writes nothing.</summary>
    /// <param name="column">The schema column number.</param>
    /// <param name="value">The text.</param>
    public void Write(int column, string? value)
    {
        int style = Position(column);
        if (value is not null)
        {
            _sink.Text(value, style, looksNumeric: false);
        }
    }

    /// <summary>Writes a boolean.</summary>
    /// <param name="column">The schema column number.</param>
    /// <param name="value">The value.</param>
    public void Write(int column, bool value) => _sink.Boolean(value, Position(column));

    /// <summary>Writes a whole number. Beyond ±2^53 it is written as text so that no digit is lost.</summary>
    /// <param name="column">The schema column number.</param>
    /// <param name="value">The value.</param>
    public void Write(int column, long value)
    {
        int style = Position(column);
        if (value is > MaxExactInteger or < -MaxExactInteger)
        {
            value.TryFormat(_buffer, out int n, default, CultureInfo.InvariantCulture);
            _sink.Text(_buffer.AsSpan(0, n), style, looksNumeric: true);
        }
        else
        {
            _sink.Number(value, style);
        }
    }

    /// <summary>Writes an unsigned whole number. Beyond 2^53 it is written as text so that no digit is lost.</summary>
    /// <param name="column">The schema column number.</param>
    /// <param name="value">The value.</param>
    public void Write(int column, ulong value)
    {
        int style = Position(column);
        if (value > MaxExactInteger)
        {
            value.TryFormat(_buffer, out int n, default, CultureInfo.InvariantCulture);
            _sink.Text(_buffer.AsSpan(0, n), style, looksNumeric: true);
        }
        else
        {
            _sink.Number(value, style);
        }
    }

    /// <summary>Writes a number. NaN and infinity, which a spreadsheet cannot hold, leave the cell empty.</summary>
    /// <param name="column">The schema column number.</param>
    /// <param name="value">The value.</param>
    public void Write(int column, double value)
    {
        int style = Position(column);
        if (double.IsFinite(value))
        {
            _sink.Number(value, style);
        }
    }

    /// <summary>Writes a single-precision number as the double that has its shortest decimal form, so 0.1f shows as 0.1.</summary>
    /// <param name="column">The schema column number.</param>
    /// <param name="value">The value.</param>
    public void Write(int column, float value)
    {
        int style = Position(column);
        if (float.IsFinite(value))
        {
            value.TryFormat(_buffer, out int n, "R", CultureInfo.InvariantCulture);
            _sink.Number(double.Parse(_buffer.AsSpan(0, n), NumberStyles.Float, CultureInfo.InvariantCulture), style);
        }
    }

    /// <summary>Writes a decimal. A spreadsheet number keeps 15 significant digits, so a decimal with more loses the rest in XLSX; CSV keeps them all.</summary>
    /// <param name="column">The schema column number.</param>
    /// <param name="value">The value.</param>
    public void Write(int column, decimal value) => _sink.Decimal(value, Position(column));

    /// <summary>Writes a date and time as a serial of the workbook's date system. The time zone and kind are dropped. <see cref="DateTime.MinValue"/> (an unset date) leaves the cell empty.</summary>
    /// <param name="column">The schema column number.</param>
    /// <param name="value">The value.</param>
    /// <exception cref="OfficeFormatException">The date is outside the range of the date system.</exception>
    public void Write(int column, DateTime value)
    {
        int style = Position(column);
        if (value == System.DateTime.MinValue)
        {
            // An unset date (default(DateTime), also default(DateOnly)) has no spreadsheet value: the cell stays empty.
            return;
        }

        if (!ExcelDate.TryToSerial(value, _sink.DateSystem, out _))
        {
            throw new OfficeFormatException(DefectKind.Value, "binding.value-out-of-range", "The date " + value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " is outside the range of the workbook's date system.",
                DefectLocation.InCell(_sheetName, new CellAddress(Math.Max(RowNumber, 1), _targets[column]).ToString()));
        }

        _sink.DateTime(value, style);
    }

    /// <summary>Writes a date.</summary>
    /// <param name="column">The schema column number.</param>
    /// <param name="value">The value.</param>
    /// <exception cref="OfficeFormatException">The date is outside the range of the date system.</exception>
    public void Write(int column, DateOnly value) => Write(column, value.ToDateTime(TimeOnly.MinValue));

    /// <summary>Writes the date and time of day of the value; the offset is dropped.</summary>
    /// <param name="column">The schema column number.</param>
    /// <param name="value">The value.</param>
    /// <exception cref="OfficeFormatException">The date is outside the range of the date system.</exception>
    public void Write(int column, DateTimeOffset value) => Write(column, value.DateTime);

    /// <summary>Writes a time of day as a fraction of a day.</summary>
    /// <param name="column">The schema column number.</param>
    /// <param name="value">The value.</param>
    public void Write(int column, TimeOnly value) => _sink.Time(value, Position(column));

    /// <summary>Writes a GUID as text in the 36-character form.</summary>
    /// <param name="column">The schema column number.</param>
    /// <param name="value">The value.</param>
    public void Write(int column, Guid value)
    {
        int style = Position(column);
        value.TryFormat(_buffer, out int n);
        _sink.Text(_buffer.AsSpan(0, n), style, looksNumeric: false);
    }

    /// <summary>Writes an enum value as its name.</summary>
    /// <param name="column">The schema column number.</param>
    /// <param name="name">The name of the member.</param>
    public void WriteName(int column, string name) => Write(column, name);

    /// <summary>Writes nothing for a column (a null value). Writing it is optional.</summary>
    /// <param name="column">The schema column number.</param>
    public void WriteEmpty(int column) => Position(column);
}
