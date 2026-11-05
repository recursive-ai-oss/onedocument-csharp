using System.Globalization;
using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.NumberFormats;
using RecursiveAI.OneOffice.Spreadsheets.Csv;
using RecursiveAI.OneOffice.Spreadsheets.Streaming;

namespace RecursiveAI.OneOffice.Spreadsheets.Binding;

/// <summary>Finds which sheet column feeds each schema column: by header text, or by fixed index, or by position without a header.</summary>
internal sealed class ColumnMap
{
    private readonly RowSchema _schema;
    private readonly bool[] _mapped;
    private int[] _slotOf = new int[32];

    public ColumnMap(RowSchema schema)
    {
        _schema = schema;
        _mapped = new bool[schema.Count];
        Array.Fill(_slotOf, -1);
        int previous = 0;
        for (int i = 0; i < schema.Count; i++)
        {
            ColumnSpec c = schema.Columns[i];
            int position = Math.Max(previous + 1, c.Index);
            previous = position;
            if (!c.CanRead)
            {
                continue;
            }

            // Without a header, columns sit where the writer would put them; with one, only fixed indexes are known up front.
            if (!schema.HasHeader || c.Index > 0)
            {
                Set(schema.HasHeader ? c.Index : position, i);
                _mapped[i] = true;
            }
        }
    }

    private void Set(int sourceColumn, int slot)
    {
        if (sourceColumn >= _slotOf.Length)
        {
            int old = _slotOf.Length;
            Array.Resize(ref _slotOf, Math.Max(sourceColumn + 1, old * 2));
            Array.Fill(_slotOf, -1, old, _slotOf.Length - old);
        }

        _slotOf[sourceColumn] = slot;
    }

    /// <summary>Tells the reader which sheet column feeds each schema column, so a problem with an empty cell has an address.</summary>
    public void Bind(RowReader reader)
    {
        for (int source = 0; source < _slotOf.Length; source++)
        {
            if (_slotOf[source] >= 0)
            {
                reader.SetSourceColumn(_slotOf[source], source);
            }
        }
    }

    /// <summary>Gets the schema column a 1-based sheet column feeds, or -1.</summary>
    public int SlotOf(int sourceColumn) => (uint)sourceColumn < (uint)_slotOf.Length ? _slotOf[sourceColumn] : -1;

    /// <summary>Offers a header cell; the first schema column with that name that is not yet fed takes it.</summary>
    public void AddHeader(int sourceColumn, ReadOnlySpan<char> text, OpenContext context, string sheet, int row)
    {
        ReadOnlySpan<char> name = text.Trim();
        bool seen = false;
        for (int i = 0; i < _schema.Count; i++)
        {
            ColumnSpec c = _schema.Columns[i];
            if (!c.CanRead || c.Index > 0 || !name.Equals(c.Name.AsSpan().Trim(), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (_mapped[i])
            {
                seen = true;
                continue;
            }

            _mapped[i] = true;
            Set(sourceColumn, i);
            return;
        }

        if (seen)
        {
            context.Defects.Report(new Defect(DefectKind.Structure, "binding.header-duplicate", DefectSeverity.Dropped,
                "The header '" + name.ToString() + "' repeats; the first column with that name is used.",
                DefectLocation.InCell(sheet, new CellAddress(Math.Max(row, 1), sourceColumn).ToString())));
        }
    }

    /// <summary>Checks the header against the schema once it is read.</summary>
    /// <exception cref="OfficeFormatException">The type is strict and a column that cannot be empty is missing.</exception>
    public void Validate(OpenContext context, string sheet, int headerRow)
    {
        for (int i = 0; i < _schema.Count; i++)
        {
            ColumnSpec c = _schema.Columns[i];
            if (_mapped[i] || !c.CanRead)
            {
                continue;
            }

            DefectLocation where = DefectLocation.InCell(sheet, new CellAddress(Math.Max(headerRow, 1), 1).ToString());
            string message = "The header row has no column '" + c.Name + "' (needed by " + _schema.TypeName + ").";
            if (_schema.Strict)
            {
                if (!c.Nullable)
                {
                    throw new OfficeFormatException(DefectKind.Structure, "binding.column-missing", message, where);
                }
            }
            else
            {
                context.Defects.Report(new Defect(DefectKind.Structure, "binding.column-missing", DefectSeverity.Repaired, message + " The member keeps its default.", where));
            }
        }
    }
}

/// <summary>A forward-only source of rows that fills a <see cref="RowReader"/>.</summary>
internal abstract class RowSource : IDisposable
{
    public abstract string SheetName { get; }

    public abstract DateSystem DateSystem { get; }

    /// <summary>Reads the next row that has a value in a mapped column into the reader; false at the end.</summary>
    public abstract bool Next(RowReader reader);

    /// <summary>Tells a new reader where its columns come from.</summary>
    public abstract void Bind(RowReader reader);

    public abstract void Dispose();
}

/// <summary>Reads rows from an XLSX worksheet through the streaming cursor.</summary>
internal sealed class XlsxRowSource : RowSource
{
    private readonly XlsxStreamReader _book;
    private readonly SheetCursor _cursor;
    private readonly ColumnMap _map;
    private readonly string _sheetName;

    public XlsxRowSource(Stream stream, RowSchema schema, OpenContext context, CancellationToken cancellationToken)
    {
        _book = XlsxStreamReader.Open(stream, context, leaveOpen: true, cancellationToken);
        try
        {
            int index = -1;
            for (int i = 0; i < _book.Sheets.Count; i++)
            {
                StreamSheetInfo s = _book.Sheets[i];
                if (schema.Sheet is { } wanted
                    ? string.Equals(s.Name, wanted, StringComparison.OrdinalIgnoreCase)
                    : s.Kind == SheetKind.Worksheet && s.State == SheetState.Visible)
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                throw new OfficeFormatException(DefectKind.Reference, "binding.sheet-not-found",
                    schema.Sheet is null ? "The workbook has no visible worksheet." : "The workbook has no sheet named '" + schema.Sheet + "'.");
            }

            _sheetName = _book.Sheets[index].Name;
            _cursor = _book.OpenSheet(index);
            _map = new ColumnMap(schema);
            if (schema.HasHeader)
            {
                ReadHeader(schema, context);
            }
        }
        catch
        {
            _cursor?.Dispose();
            _book.Dispose();
            throw;
        }
    }

    public override string SheetName => _sheetName;

    public override DateSystem DateSystem => _book.DateSystem;

    private void ReadHeader(RowSchema schema, OpenContext context)
    {
        // The first row with any cell is the header; rows above it that are empty are skipped.
        while (_cursor.ReadRow())
        {
            bool any = false;
            while (_cursor.ReadCell())
            {
                string? text = _cursor.Kind switch
                {
                    CellValueKind.Text => _cursor.GetText(),
                    CellValueKind.Number => _cursor.Number.ToString(CultureInfo.InvariantCulture),
                    CellValueKind.Boolean => _cursor.Boolean ? "TRUE" : "FALSE",
                    _ => null,
                };
                if (!string.IsNullOrWhiteSpace(text))
                {
                    any = true;
                    _map.AddHeader(_cursor.Column, text, context, _sheetName, _cursor.RowNumber);
                }
            }

            if (any)
            {
                _map.Validate(context, _sheetName, _cursor.RowNumber);
                return;
            }
        }
    }

    public override void Bind(RowReader reader) => _map.Bind(reader);

    public override bool Next(RowReader reader)
    {
        while (_cursor.ReadRow())
        {
            reader.Clear();
            bool any = false;
            while (_cursor.ReadCell())
            {
                int slot = _map.SlotOf(_cursor.Column);
                if (slot < 0)
                {
                    continue;
                }

                int col = _cursor.Column;
                switch (_cursor.Kind)
                {
                    case CellValueKind.Number:
                        ColumnType type = reader.TypeOf(slot);
                        bool dated = (type is ColumnType.Text or ColumnType.Date or ColumnType.DateTime or ColumnType.Time) && _cursor.IsDate;
                        reader.SetNumber(slot, _cursor.Number, dated, col);
                        any = true;
                        break;
                    case CellValueKind.Boolean:
                        reader.SetBoolean(slot, _cursor.Boolean, col);
                        any = true;
                        break;
                    case CellValueKind.Error:
                        reader.SetError(slot, _cursor.Error, col);
                        any = true;
                        break;
                    case CellValueKind.Text:
                        if (reader.TypeOf(slot) == ColumnType.Text)
                        {
                            string text = _cursor.GetText();
                            reader.SetText(slot, text, col);
                            any |= text.Length > 0;
                        }
                        else
                        {
                            int need = _cursor.TextUtf8.Length;
                            if (need == 0)
                            {
                                break;
                            }

                            char[] buffer = reader.TextBuffer(slot, need);
                            if (_cursor.TryCopyText(buffer, out int written))
                            {
                                reader.CommitText(slot, written, col);
                                any |= !buffer.AsSpan(0, written).IsWhiteSpace();
                            }
                            else
                            {
                                reader.SetText(slot, _cursor.GetText(), col);
                                any = true;
                            }
                        }

                        break;
                }
            }

            if (any)
            {
                reader.RowNumber = _cursor.RowNumber;
                return true;
            }
        }

        return false;
    }

    public override void Dispose()
    {
        _cursor.Dispose();
        _book.Dispose();
    }
}

/// <summary>Reads rows from delimited text.</summary>
internal sealed class CsvRowSource : RowSource
{
    private readonly CsvReader _csv;
    private readonly ColumnMap _map;
    private readonly CancellationToken _cancellation;

    public CsvRowSource(Stream stream, bool tab, RowSchema schema, OpenContext context, CancellationToken cancellationToken)
    {
        SheetName = schema.Sheet ?? "Sheet1";
        _cancellation = cancellationToken;
        _csv = new CsvReader(stream, new CsvReadOptions { Delimiter = tab ? '\t' : null }, context, leaveOpen: true);
        _map = new ColumnMap(schema);
        try
        {
            if (schema.HasHeader)
            {
                while (_csv.ReadRecord(cancellationToken))
                {
                    if (_csv.IsBlank)
                    {
                        continue;
                    }

                    for (int f = 0; f < _csv.FieldCount; f++)
                    {
                        _map.AddHeader(f + 1, _csv.GetField(f), context, SheetName, (int)_csv.RecordNumber);
                    }

                    _map.Validate(context, SheetName, (int)_csv.RecordNumber);
                    break;
                }
            }
        }
        catch
        {
            _csv.Dispose();
            throw;
        }
    }

    public override string SheetName { get; }

    public override DateSystem DateSystem => DateSystem.Date1900;

    public override void Bind(RowReader reader) => _map.Bind(reader);

    public override bool Next(RowReader reader)
    {
        while (_csv.ReadRecord(_cancellation))
        {
            if (_csv.IsBlank)
            {
                continue;
            }

            reader.Clear();
            bool any = false;
            for (int f = 0; f < _csv.FieldCount; f++)
            {
                int slot = _map.SlotOf(f + 1);
                if (slot < 0)
                {
                    continue;
                }

                ReadOnlySpan<char> field = _csv.GetField(f);
                bool isText = reader.TypeOf(slot) == ColumnType.Text;
                if (field.IsEmpty)
                {
                    if (isText && _csv.IsQuoted(f))
                    {
                        reader.SetText(slot, string.Empty, f + 1);
                        any = true;
                    }

                    continue;
                }

                if (isText)
                {
                    reader.SetText(slot, new string(field), f + 1);
                    any = true;
                }
                else
                {
                    char[] buffer = reader.TextBuffer(slot, field.Length);
                    field.CopyTo(buffer);
                    reader.CommitText(slot, field.Length, f + 1);
                    any |= !field.IsWhiteSpace();
                }
            }

            if (any)
            {
                reader.RowNumber = (int)Math.Min(_csv.RecordNumber, int.MaxValue);
                return true;
            }
        }

        return false;
    }

    public override void Dispose() => _csv.Dispose();
}

/// <summary>Writes rows into an XLSX worksheet through the streaming writer.</summary>
internal sealed class XlsxRowSink : RowSink
{
    private readonly XlsxStreamWriter _book;
    private readonly Dictionary<string, int> _formats = new(StringComparer.Ordinal);

    public XlsxRowSink(XlsxStreamWriter book, RowSchema schema)
    {
        _book = book;
        HeaderStyle = schema.HasHeader ? book.AddFormat(new StreamCellFormat { Bold = true }) : 0;
        Sheet = book.AddSheet(schema.Sheet ?? "Sheet1", new SheetWriteOptions { FreezeRows = schema.HasHeader ? 1 : 0, Columns = Widths(schema) });
    }

    public SheetStreamWriter Sheet { get; }

    // Wide enough for the header and for what the column holds, so dates do not show as ####.
    private static List<ColumnProperties> Widths(RowSchema schema)
    {
        int[] targets = RowWriter.Targets(schema);
        var widths = new List<ColumnProperties>(schema.Count);
        for (int i = 0; i < schema.Count; i++)
        {
            ColumnSpec c = schema.Columns[i];
            double width = c.Type switch
            {
                ColumnType.Date => 12,
                ColumnType.DateTime => 20,
                ColumnType.Time => 10,
                ColumnType.Identifier => 38,
                _ => 10,
            };
            widths.Add(new ColumnProperties(targets[i], targets[i], Math.Max(width, c.Name.Length + 3), 0, ColumnOptions.CustomWidth));
        }

        return widths;
    }

    public override DateSystem DateSystem => DateSystem.Date1900;

    public override int HeaderStyle { get; }

    public override int StyleFor(in ColumnSpec column)
    {
        string? format = column.Format ?? column.Type switch
        {
            ColumnType.Date => "yyyy-mm-dd",
            ColumnType.DateTime => "yyyy-mm-dd hh:mm:ss",
            ColumnType.Time => "hh:mm:ss",
            _ => null,
        };
        if (format is null)
        {
            return 0;
        }

        if (!_formats.TryGetValue(format, out int style))
        {
            style = _book.AddFormat(new StreamCellFormat { NumberFormat = format });
            _formats[format] = style;
        }

        return style;
    }

    public override void BeginRow() => Sheet.BeginRow();

    public override void EndRow(int width)
    {
    }

    public override void MoveTo(int column) => Sheet.MoveToColumn(column);

    public override void Text(ReadOnlySpan<char> text, int style, bool looksNumeric) => Sheet.WriteText(text, style);

    public override void Number(double value, int style) => Sheet.WriteNumber(value, style);

    public override void Boolean(bool value, int style) => Sheet.WriteBoolean(value, style);

    public override void DateTime(DateTime value, int style) => Sheet.WriteDate(value, style);

    public override void Time(TimeOnly value, int style) => Sheet.WriteNumber(value.Ticks / (double)TimeSpan.TicksPerDay, style);
}

/// <summary>Writes rows as delimited text.</summary>
internal sealed class CsvRowSink : RowSink
{
    private readonly CsvWriter _csv;
    private readonly char[] _buffer = new char[40];
    private int _count;

    public CsvRowSink(CsvWriter csv)
    {
        _csv = csv;
    }

    public override DateSystem DateSystem => DateSystem.Date1900;

    public override int HeaderStyle => 0;

    public override int StyleFor(in ColumnSpec column) => 0;

    public override void BeginRow() => _count = 0;

    public override void EndRow(int width)
    {
        Pad(width);
        _csv.EndRecord();
    }

    private void Pad(int count)
    {
        while (_count < count)
        {
            _csv.WriteField(default, isText: false);
            _count++;
        }
    }

    public override void MoveTo(int column) => Pad(column - 1);

    public override void Text(ReadOnlySpan<char> text, int style, bool looksNumeric)
    {
        _csv.WriteField(text, isText: !looksNumeric);
        _count++;
    }

    public override void Number(double value, int style)
    {
        _csv.WriteNumber(value);
        _count++;
    }

    public override void Decimal(decimal value, int style)
    {
        value.TryFormat(_buffer, out int n, default, CultureInfo.InvariantCulture);
        _csv.WriteField(_buffer.AsSpan(0, n), isText: false);
        _count++;
    }

    public override void Boolean(bool value, int style)
    {
        _csv.WriteField(value ? "TRUE" : "FALSE", isText: false);
        _count++;
    }

    public override void DateTime(DateTime value, int style)
    {
        string pattern = value.TimeOfDay == TimeSpan.Zero ? "yyyy-MM-dd" : value.Millisecond == 0 ? "yyyy-MM-dd HH:mm:ss" : "yyyy-MM-dd HH:mm:ss.fff";
        value.TryFormat(_buffer, out int n, pattern, CultureInfo.InvariantCulture);
        _csv.WriteField(_buffer.AsSpan(0, n), isText: false);
        _count++;
    }

    public override void Time(TimeOnly value, int style)
    {
        value.TryFormat(_buffer, out int n, value.Millisecond == 0 ? "HH:mm:ss" : "HH:mm:ss.fff", CultureInfo.InvariantCulture);
        _csv.WriteField(_buffer.AsSpan(0, n), isText: false);
        _count++;
    }
}
