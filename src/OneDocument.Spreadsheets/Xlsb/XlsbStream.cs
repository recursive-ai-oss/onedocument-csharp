using System.Linq;
using System.Buffers.Binary;
using System.Text;
using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.NumberFormats;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Xlsb;

namespace RecursiveAI.OneDocument.Spreadsheets.Streaming;

/// <summary>
/// Reads a binary workbook (XLSB) as a forward-only stream of rows and cells, beside the workbook hub, at constant memory: a worksheet
/// part is read record by record and never translated or held. It mirrors <see cref="XlsxStreamReader"/>; the cursor is an
/// <see cref="XlsbSheetCursor"/>.
/// </summary>
/// <remarks>
/// Opening reads the package directory, the content types, the relationships and the workbook part's sheet list. The shared string
/// table and the style sheet are read the first time a cell asks for text or a date. Shared strings are held as UTF-16 text (the text of
/// every entry plus four bytes per entry). Nothing is recalculated.
/// </remarks>
public sealed class XlsbStreamReader : IDisposable
{
    private readonly OpcPackage _package;
    private readonly List<StreamSheetInfo> _sheets = [];
    private readonly string _workbookPart;
    private readonly CancellationToken _cancellation;
    private XlsbStrings? _strings;
    private bool[]? _dateStyles;
    private int[]? _formatIds;
    private XlsbFormulaContext? _formulas;
    private bool _disposed;

    private XlsbStreamReader(OpcPackage package, OpenContext context, CancellationToken cancellation)
    {
        _package = package;
        Context = context;
        _cancellation = cancellation;
        Relationship? main = package.GetRelationships().FirstOrDefault(r => r.Type.EndsWith("/officeDocument", StringComparison.Ordinal) && r.TargetMode == RelationshipTargetMode.Internal);
        if (main is null || !PartUri.TryResolve(null, main.Target, out PartUri uri) || package.FindPart(uri.Value) is not { } part)
        {
            throw new OfficeFormatException(DefectKind.Structure, "xlsb.no-main-part",
                "The package has no workbook part: no officeDocument relationship points at a part that exists.", DefectLocation.InPart("/_rels/.rels"));
        }

        _workbookPart = part.Name;
        if (!string.Equals(part.ContentType, XlsbBook.WorkbookBinType, StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatNotAvailableException(OfficeFormat.Unknown, "stream as a binary workbook:");
        }

        var rels = package.GetRelationships(part.Name);
        var entries = new List<XlsbSheetEntry>();
        var r = new XlsbRecordReader(part.GetBytes(), context, part.Name);
        while (r.Read())
        {
            if (r.Type == XlsbRecordType.BundleSh)
            {
                var f = new XlsbRecordFields(r.Data);
                int state = f.I32();
                int id = f.I32();
                string? rel = f.NullableWideString();
                entries.Add(new XlsbSheetEntry(f.WideString(), id, rel, state));
            }
            else if (r.Type == XlsbRecordType.WbProp)
            {
                DateSystem = (BinaryPrimitives.ReadUInt32LittleEndian(r.Data) & 1) != 0 ? DateSystem.Date1904 : DateSystem.Date1900;
            }
        }

        int i = 0;
        foreach (XlsbSheetEntry e in entries)
        {
            string? partName = null;
            SheetKind kind = SheetKind.Worksheet;
            Relationship? rel = rels.FirstOrDefault(x => x.Id == e.RelationshipId);
            if (rel is not null && OpcPackage.TryResolveTarget(part.Name, rel, out PartUri target) && package.FindPart(target.Value) is { } sp)
            {
                partName = sp.Name;
                kind = rel.Type.EndsWith("/chartsheet", StringComparison.Ordinal) ? SheetKind.Chartsheet
                    : rel.Type.EndsWith("/dialogsheet", StringComparison.Ordinal) ? SheetKind.DialogSheet
                    : rel.Type.Contains("Macrosheet", StringComparison.OrdinalIgnoreCase) ? SheetKind.MacroSheet : SheetKind.Worksheet;
            }

            _sheets.Add(new StreamSheetInfo(i++, e.Name, e.SheetId, kind, e.State == 2 ? SheetState.VeryHidden : e.State == 1 ? SheetState.Hidden : SheetState.Visible, partName));
        }
    }

    internal OpenContext Context { get; }

    /// <summary>Gets the sheets of the workbook in tab order.</summary>
    public IReadOnlyList<StreamSheetInfo> Sheets => _sheets;

    /// <summary>Gets the date system (1900 or 1904) the workbook uses for its dates.</summary>
    public DateSystem DateSystem { get; } = DateSystem.Date1900;

    /// <summary>Opens a binary workbook file.</summary>
    /// <param name="path">The file.</param>
    /// <param name="context">Limits and defect sink; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the open and every row read.</param>
    /// <returns>The reader, which the caller disposes.</returns>
    /// <exception cref="OfficeFormatException">The content is not a readable binary workbook.</exception>
    public static XlsbStreamReader Open(string path, OpenContext? context = null, CancellationToken cancellationToken = default)
    {
        context ??= new OpenContext();
        return Create(OpcPackage.Open(path, context), context, cancellationToken);
    }

    /// <summary>Opens a binary workbook from a seekable stream.</summary>
    /// <param name="stream">The stream; it must stay readable until the reader is disposed.</param>
    /// <param name="context">Limits and defect sink; null for the defaults.</param>
    /// <param name="leaveOpen">True (the default) to leave the stream open when the reader is disposed.</param>
    /// <param name="cancellationToken">Cancels the open and every row read.</param>
    /// <returns>The reader, which the caller disposes.</returns>
    /// <exception cref="OfficeFormatException">The content is not a readable binary workbook.</exception>
    public static XlsbStreamReader Open(Stream stream, OpenContext? context = null, bool leaveOpen = true, CancellationToken cancellationToken = default)
    {
        context ??= new OpenContext();
        return Create(OpcPackage.Open(stream, context, leaveOpen), context, cancellationToken);
    }

    private static string MainPartName(OpcPackage package)
    {
        Relationship? main = package.GetRelationships().FirstOrDefault(r => r.Type.EndsWith("/officeDocument", StringComparison.Ordinal));
        return main is not null && PartUri.TryResolve(null, main.Target, out PartUri uri) ? uri.Value : "/";
    }

    private string PartNameOf(string contentType) => _package.Parts.FirstOrDefault(p => string.Equals(p.ContentType, contentType, StringComparison.OrdinalIgnoreCase))?.Name ?? _workbookPart;

    private static XlsbStreamReader Create(OpcPackage package, OpenContext context, CancellationToken cancellation)
    {
        try
        {
            return new XlsbStreamReader(package, context, cancellation);
        }
        catch (Exception e) when (XlsbGuard.IsMalformed(e))
        {
            package.Dispose();
            throw XlsbGuard.Typed(e, MainPartName(package));
        }
        catch
        {
            package.Dispose();
            throw;
        }
    }

    /// <summary>Opens a worksheet for reading.</summary>
    /// <param name="index">The zero-based position in <see cref="Sheets"/>.</param>
    /// <returns>The cursor, which the caller disposes.</returns>
    /// <exception cref="ArgumentOutOfRangeException">No such sheet.</exception>
    /// <exception cref="ArgumentException">The sheet is not a worksheet.</exception>
    public XlsbSheetCursor OpenSheet(int index)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if ((uint)index >= (uint)_sheets.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        StreamSheetInfo info = _sheets[index];
        if (info.Kind != SheetKind.Worksheet)
        {
            throw new ArgumentException("Sheet '" + info.Name + "' is not a worksheet.", nameof(index));
        }

        if (info.PartName is null || _package.FindPart(info.PartName) is not { } part)
        {
            return new XlsbSheetCursor(this, null, info.Name, string.Empty, _cancellation);
        }

        return new XlsbSheetCursor(this, part.OpenRead(), info.Name, info.PartName, _cancellation);
    }

    /// <summary>Opens a worksheet by name (case-insensitive).</summary>
    /// <param name="name">The sheet name.</param>
    /// <returns>The cursor, which the caller disposes.</returns>
    /// <exception cref="KeyNotFoundException">No sheet has that name.</exception>
    public XlsbSheetCursor OpenSheet(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        foreach (StreamSheetInfo s in _sheets)
        {
            if (string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return OpenSheet(s.Index);
            }
        }

        throw new KeyNotFoundException("No sheet is named '" + name + "'.");
    }

    /// <summary>Gets the number of entries of the shared string table (reads it on first use).</summary>
    public int SharedStringCount => Strings().Count;

    /// <summary>Gets one shared string as text (an allocation).</summary>
    /// <param name="index">The zero-based entry.</param>
    /// <returns>The text, with rich-text runs joined and phonetic runs left out; empty for an index out of range.</returns>
    public string GetSharedString(int index) => new(Strings().Get(index));

    /// <summary>Gets whether a style's number format shows dates or times (reads the style sheet on first use).</summary>
    /// <param name="styleIndex">The cell format index.</param>
    /// <returns>True for a date or time format.</returns>
    public bool IsDateStyle(int styleIndex)
    {
        EnsureStyles();
        return (uint)styleIndex < (uint)_dateStyles!.Length && _dateStyles[styleIndex];
    }

    /// <summary>Gets the number format id of a cell format (reads the style sheet on first use).</summary>
    /// <param name="styleIndex">The cell format index.</param>
    /// <returns>The id, or 0 for an index out of range.</returns>
    public int GetNumberFormatId(int styleIndex)
    {
        EnsureStyles();
        return (uint)styleIndex < (uint)_formatIds!.Length ? _formatIds[styleIndex] : 0;
    }

    internal XlsbStrings Strings()
    {
        try
        {
            return StringsCore();
        }
        catch (Exception e) when (XlsbGuard.IsMalformed(e))
        {
            throw XlsbGuard.Typed(e, PartNameOf(XlsbBook.SharedStringsBinType));
        }
    }

    private XlsbStrings StringsCore()
    {
        if (_strings is not null)
        {
            return _strings;
        }

        IPackagePart? part = _package.Parts.FirstOrDefault(p => string.Equals(p.ContentType, XlsbBook.SharedStringsBinType, StringComparison.OrdinalIgnoreCase));
        _strings = part is null ? XlsbStrings.Empty : XlsbStrings.Read(part.OpenRead(), Context, part.Name);
        return _strings;
    }

    private void EnsureStyles()
    {
        try
        {
            EnsureStylesCore();
        }
        catch (Exception e) when (XlsbGuard.IsMalformed(e))
        {
            throw XlsbGuard.Typed(e, PartNameOf(XlsbBook.StylesBinType));
        }
    }

    private void EnsureStylesCore()
    {
        if (_dateStyles is not null)
        {
            return;
        }

        IPackagePart? part = _package.Parts.FirstOrDefault(p => string.Equals(p.ContentType, XlsbBook.StylesBinType, StringComparison.OrdinalIgnoreCase));
        var codes = new Dictionary<int, string>();
        var ids = new List<int>();
        if (part is not null)
        {
            using Stream s = part.OpenRead();
            var r = new XlsbRecordReader(s, Context, part.Name);
            bool inCellXfs = false;
            while (r.Read())
            {
                switch (r.Type)
                {
                    case XlsbRecordType.Fmt:
                    {
                        var f = new XlsbRecordFields(r.Data);
                        int id = f.U16();
                        codes[id] = f.WideString();
                        break;
                    }

                    case XlsbRecordType.BeginCellXFs: inCellXfs = true; break;
                    case XlsbRecordType.EndCellXFs: inCellXfs = false; break;
                    case XlsbRecordType.XF when inCellXfs:
                        ids.Add(r.Data.Length >= 4 ? BinaryPrimitives.ReadUInt16LittleEndian(r.Data[2..]) : 0);
                        break;
                }
            }
        }

        var date = new bool[ids.Count];
        var byId = new Dictionary<int, bool>();
        for (int i = 0; i < ids.Count; i++)
        {
            if (!byId.TryGetValue(ids[i], out bool d))
            {
                NumberFormat? f = codes.TryGetValue(ids[i], out string? c) ? (NumberFormat.TryParse(c, out NumberFormat? parsed, out _) ? parsed : null) : NumberFormat.GetBuiltIn(ids[i]);
                d = f?.IsDateTime ?? false;
                byId[ids[i]] = d;
            }

            date[i] = d;
        }

        _formatIds = [.. ids];
        _dateStyles = date;
    }

    internal XlsbFormulaContext Formulas()
    {
        try
        {
            return FormulasCore();
        }
        catch (Exception e) when (XlsbGuard.IsMalformed(e))
        {
            throw XlsbGuard.Typed(e, _workbookPart);
        }
    }

    private XlsbFormulaContext FormulasCore()
    {
        if (_formulas is not null)
        {
            return _formulas;
        }

        var cx = new XlsbFormulaContext();
        IPackagePart main = _package.FindPart(_workbookPart)!;
        XlsbWorkbookPart.ToXml(main.GetBytes(), Context, main.Name, cx, _ => null, out _);
        _formulas = cx;
        return cx;
    }

    /// <summary>Releases the package.</summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _package.Dispose();
        }
    }
}

/// <summary>The shared strings as one pool of UTF-16 text with an index of starts.</summary>
internal sealed class XlsbStrings
{
    private readonly char[] _pool;
    private readonly int[] _starts;

    private XlsbStrings(char[] pool, int[] starts)
    {
        _pool = pool;
        _starts = starts;
    }

    public static XlsbStrings Empty { get; } = new([], [0]);

    public int Count => _starts.Length - 1;

    public ReadOnlySpan<char> Get(int index) => (uint)index < (uint)Count ? _pool.AsSpan(_starts[index], _starts[index + 1] - _starts[index]) : default;

    public static XlsbStrings Read(Stream stream, OpenContext context, string partName)
    {
        using (stream)
        {
            var r = new XlsbRecordReader(stream, context, partName);
            var pool = new List<char[]>();
            var starts = new List<int>();
            char[] block = new char[1 << 16];
            int used = 0;
            long total = 0;
            while (r.Read())
            {
                if (r.Type != XlsbRecordType.SSTItem)
                {
                    continue;
                }

                var f = new XlsbRecordFields(r.Data);
                f.U8();
                uint n = f.U32();
                if (f.Overrun || n > (uint)(f.Remaining / 2))
                {
                    starts.Add(checked((int)total));
                    continue;
                }

                starts.Add(checked((int)total));
                ReadOnlySpan<byte> bytes = f.Bytes((int)n * 2);
                int chars = (int)n;
                OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxAllocationBytes), context.Limits.MaxAllocationBytes, (total + chars) * 2, DefectLocation.InRecord(partName, r.Offset));
                if (used + chars > block.Length)
                {
                    pool.Add(block[..used]);
                    block = new char[Math.Max(1 << 16, chars)];
                    used = 0;
                }

                Encoding.Unicode.GetChars(bytes, block.AsSpan(used));
                used += chars;
                total += chars;
            }

            pool.Add(block[..used]);
            starts.Add(checked((int)total));
            char[] all = new char[total];
            int at = 0;
            foreach (char[] b in pool)
            {
                b.CopyTo(all, at);
                at += b.Length;
            }

            return new XlsbStrings(all, [.. starts]);
        }
    }
}

/// <summary>
/// A forward cursor over the rows and cells of one worksheet of a binary workbook. Members are valid after the matching read returns
/// true, until the cursor moves. Reading allocates nothing per cell: numbers, booleans, errors, styles, shared-string indexes and
/// text spans come from the record buffer; only <see cref="GetText"/>, <see cref="GetFormula"/> and the other string-returning
/// members allocate.
/// </summary>
public sealed class XlsbSheetCursor : IDisposable
{
    private readonly XlsbStreamReader _owner;
    private readonly XlsbRecordReader? _reader;
    private readonly Stream? _stream;
    private readonly CancellationToken _cancellation;
    private readonly string _partName;
    private bool _inRow;
    private bool _rowDone;
    private bool _ended;
    private int _rowIndex;
    private int _col;
    private int _style;
    private CellValueKind _kind;
    private double _number;
    private CellError _error;
    private int _sharedIndex = -1;
    private bool _hasFormula;
    private CellFormulaKind _formulaKind;
    private byte[] _formula = [];
    private int _formulaLength;
    private byte[] _formulaExtra = [];
    private int _formulaExtraLength;
    private byte[] _text = new byte[256];
    private int _textLength;
    private char[] _chars = new char[256];
    private int _charsLength;
    private double _rowHeight = -1;
    private int _rowFlags;
    private int _rowOutline;
    private int _rowStyle;
    private bool _pendingMaster;
    private int _rowCellColumn;
    private int _rowStart;

    internal XlsbSheetCursor(XlsbStreamReader owner, Stream? part, string sheetName, string partName, CancellationToken cancellation)
    {
        _owner = owner;
        _stream = part;
        _partName = partName;
        _cancellation = cancellation;
        SheetName = sheetName;
        if (part is null)
        {
            _ended = true;
            return;
        }

        _reader = new XlsbRecordReader(part, owner.Context, partName);
        var columns = new List<ColumnProperties>();
        while (_reader.Read())
        {
            if (_reader.Type == XlsbRecordType.WsDim)
            {
                var f = new XlsbRecordFields(_reader.Data);
                int r1 = f.I32();
                int r2 = f.I32();
                int c1 = f.I32();
                int c2 = f.I32();
                if (!f.Overrun && r1 >= 0 && c1 >= 0 && r2 >= r1 && c2 >= c1 && r2 < CellAddress.MaxRows && c2 < CellAddress.MaxColumns)
                {
                    DeclaredDimension = new CellRange(new CellAddress(r1 + 1, c1 + 1), new CellAddress(r2 + 1, c2 + 1));
                }
            }
            else if (_reader.Type == XlsbRecordType.ColInfo)
            {
                var f = new XlsbRecordFields(_reader.Data);
                int c1 = f.I32();
                int c2 = f.I32();
                uint dx = f.U32();
                int style = f.I32();
                int flags = f.U16();
                columns.Add(new ColumnProperties(c1 + 1, c2 + 1, dx / 256.0, style,
                    ((flags & 1) != 0 ? ColumnOptions.Hidden : 0) | ((flags & 2) != 0 ? ColumnOptions.CustomWidth : 0) | ((flags & 4) != 0 ? ColumnOptions.BestFit : 0)
                    | ((flags & 0x1000) != 0 ? ColumnOptions.Collapsed : 0), (flags >> 8) & 7));
            }
            else if (_reader.Type == XlsbRecordType.BeginSheetData)
            {
                break;
            }
        }

        Columns = columns;
        if (_reader.WasTruncated || _reader.RecordCount == 0)
        {
            _ended = true;
        }
    }

    /// <summary>Gets the sheet name.</summary>
    public string SheetName { get; }

    /// <summary>Gets the range the part declares as its dimension, or null.</summary>
    public CellRange? DeclaredDimension { get; private set; }

    /// <summary>Gets the column property runs of the sheet.</summary>
    public IReadOnlyList<ColumnProperties> Columns { get; private set; } = [];

    /// <summary>Gets the one-based row number of the current row.</summary>
    public int RowNumber => _rowIndex + 1;

    /// <summary>Gets the row height in points, or -1 when the row has none.</summary>
    public double RowHeight => _rowHeight;

    /// <summary>Gets a value indicating whether the row is hidden.</summary>
    public bool IsRowHidden => (_rowFlags & 1) != 0;

    /// <summary>Gets a value indicating whether the row height was set by hand.</summary>
    public bool HasCustomRowHeight => (_rowFlags & 2) != 0;

    /// <summary>Gets a value indicating whether the row has a format of its own.</summary>
    public bool HasCustomRowFormat => (_rowFlags & 4) != 0;

    /// <summary>Gets a value indicating whether the row is collapsed.</summary>
    public bool IsRowCollapsed => (_rowFlags & 8) != 0;

    /// <summary>Gets the outline level of the row.</summary>
    public int RowOutlineLevel => _rowOutline;

    /// <summary>Gets the row's cell format index.</summary>
    public int RowStyle => _rowStyle;

    /// <summary>Moves to the next row; the cells of the current row that were not read are skipped.</summary>
    /// <returns>True when a row is available.</returns>
    /// <exception cref="OperationCanceledException">The cancellation token given to the reader was cancelled.</exception>
    public bool ReadRow()
    {
        try
        {
            return ReadRowCore();
        }
        catch (Exception e) when (XlsbGuard.IsMalformed(e))
        {
            throw XlsbGuard.Typed(e, _partName);
        }
    }

    private bool ReadRowCore()
    {
        _cancellation.ThrowIfCancellationRequested();
        _inRow = false;
        if (_ended || _reader is null)
        {
            return false;
        }

        _pendingMaster = false;
        if (_lookahead)
        {
            _lookahead = false;
            _inRow = true;
            return true;
        }

        while (_reader.Read())
        {
            switch (_reader.Type)
            {
                case XlsbRecordType.RowHdr:
                    ParseRow();
                    _inRow = true;
                    _rowDone = false;
                    _rowCellColumn = -1;
                    return true;
                case XlsbRecordType.EndSheetData:
                    _ended = true;
                    return false;
            }
        }

        _ended = true;
        return false;
    }

    private void ParseRow()
    {
        var f = new XlsbRecordFields(_reader!.Data);
        int row = f.I32();
        int style = f.I32();
        int height = f.U16();
        int b1 = f.U8();
        int b2 = f.U8();
        if (f.Overrun || row < 0 || row >= CellAddress.MaxRows)
        {
            _owner.Context.Defects.Report(new Defect(DefectKind.Record, "xlsb.row-invalid", DefectSeverity.Repaired,
                "A row header is damaged; the row index is taken as the one after the previous row.", DefectLocation.InRecord(_partName, _reader.Offset)));
            row = Math.Clamp(_rowIndex + 1, 0, CellAddress.MaxRows - 1);
        }

        _rowIndex = row;
        _rowStyle = (b2 & 0x40) != 0 ? style : 0;
        _rowHeight = (b2 & 0x20) != 0 || height != 0 ? height / 20.0 : -1;
        _rowOutline = b2 & 7;
        _rowFlags = ((b2 & 0x10) != 0 ? 1 : 0) | ((b2 & 0x20) != 0 ? 2 : 0) | ((b2 & 0x40) != 0 ? 4 : 0) | ((b2 & 8) != 0 ? 8 : 0);
        _ = b1;
        _rowStart = row;
    }

    /// <summary>Gets the one-based column of the current cell.</summary>
    public int Column => _col + 1;

    /// <summary>Gets the address of the current cell.</summary>
    public CellAddress Address => new(_rowIndex + 1, _col + 1);

    /// <summary>Gets the cell format index of the current cell.</summary>
    public int Style => _style;

    /// <summary>Gets the kind of the current cell's value; for a formula, the kind of its cached result.</summary>
    public CellValueKind Kind => _kind;

    /// <summary>Gets the current cell's value; a text value refers to the shared string table by index (or is -1 for text stored in the cell).</summary>
    public CellValue Value => _kind switch
    {
        CellValueKind.Number => CellValue.FromNumber(_number),
        CellValueKind.Boolean => CellValue.FromBoolean(_number != 0),
        CellValueKind.Error => CellValue.FromError(_error),
        CellValueKind.Text => new CellValue(CellValueKind.Text, 0, _sharedIndex),
        _ => default,
    };

    /// <summary>Gets the number of a number cell (or 0 or 1 for a boolean).</summary>
    public double Number => _kind is CellValueKind.Number or CellValueKind.Boolean ? _number : 0;

    /// <summary>Gets the value of a boolean cell.</summary>
    public bool Boolean => _kind == CellValueKind.Boolean && _number != 0;

    /// <summary>Gets the error of an error cell.</summary>
    public CellError Error => _kind == CellValueKind.Error ? _error : default;

    /// <summary>Gets the shared-string index of a text cell that refers to the table, or -1.</summary>
    public int SharedStringIndex => _kind == CellValueKind.Text ? _sharedIndex : -1;

    /// <summary>Gets the text of a text cell as UTF-8, valid until the cursor moves; no allocation.</summary>
    public ReadOnlySpan<byte> TextUtf8
    {
        get
        {
            if (_kind != CellValueKind.Text)
            {
                return default;
            }

            if (_sharedIndex >= 0)
            {
                ReadOnlySpan<char> s = _owner.Strings().Get(_sharedIndex);
                int n = Encoding.UTF8.GetMaxByteCount(s.Length);
                if (_text.Length < n)
                {
                    _text = new byte[Math.Max(n, _text.Length * 2)];
                }

                return _text.AsSpan(0, Encoding.UTF8.GetBytes(s, _text));
            }

            return _text.AsSpan(0, _textLength);
        }
    }

    /// <summary>Copies the text of a text cell into <paramref name="destination"/>.</summary>
    /// <param name="destination">Where to write.</param>
    /// <param name="written">The number of characters written.</param>
    /// <returns>True when it fitted.</returns>
    public bool TryCopyText(Span<char> destination, out int written)
    {
        written = 0;
        if (_kind != CellValueKind.Text)
        {
            return true;
        }

        ReadOnlySpan<char> s = _sharedIndex >= 0 ? _owner.Strings().Get(_sharedIndex) : _chars.AsSpan(0, _charsLength);
        if (s.Length > destination.Length)
        {
            return false;
        }

        s.CopyTo(destination);
        written = s.Length;
        return true;
    }

    /// <summary>Gets the text of a text cell as a string (an allocation).</summary>
    /// <returns>The text; empty for other kinds.</returns>
    public string GetText() => _kind != CellValueKind.Text ? string.Empty : _sharedIndex >= 0 ? new string(_owner.Strings().Get(_sharedIndex)) : new string(_chars, 0, _charsLength);

    /// <summary>Gets a value indicating whether the current cell holds a formula.</summary>
    public bool HasFormula => _hasFormula;

    /// <summary>Gets the kind of the formula: normal, shared (the cell uses another cell's formula) or array.</summary>
    public CellFormulaKind FormulaKind => _formulaKind;

    /// <summary>Gets a value indicating whether the current cell's number format shows a date or time.</summary>
    public bool IsDate => _kind == CellValueKind.Number && _owner.IsDateStyle(_style);

    /// <summary>Converts the current cell to a date when its format is a date format.</summary>
    /// <param name="value">The date.</param>
    /// <returns>True for a number cell with a date format whose value is a valid date.</returns>
    public bool TryGetDateTime(out DateTime value)
    {
        if (IsDate)
        {
            return ExcelDate.TryToDateTime(_number, _owner.DateSystem, out value);
        }

        value = default;
        return false;
    }

    /// <summary>Decodes the formula of the current cell as text (an allocation). A cell that uses a shared formula has no text of its own: empty.</summary>
    /// <returns>The formula without the leading equals sign, or an empty string.</returns>
    public string GetFormula()
    {
        try
        {
            return GetFormulaCore();
        }
        catch (Exception e) when (XlsbGuard.IsMalformed(e))
        {
            throw XlsbGuard.Typed(e, _partName);
        }
    }

    private string GetFormulaCore()
    {
        if (!_hasFormula || _formulaLength == 0)
        {
            return string.Empty;
        }

        XlsbFormulaKind kind = _formulaKind == CellFormulaKind.Array ? XlsbFormulaKind.Array : _formulaKind == CellFormulaKind.Shared ? XlsbFormulaKind.Shared : XlsbFormulaKind.Cell;
        return XlsbFormula.TryDecode(_formula.AsSpan(0, _formulaLength), _formulaExtra.AsSpan(0, _formulaExtraLength), _owner.Formulas(), kind, _rowIndex, _col, out FormulaNode node, out _)
            ? FormulaPrinter.Print(node, new FormulaPrintOptions { Form = FormulaForm.File })
            : string.Empty;
    }

    /// <summary>Moves to the next cell of the current row.</summary>
    /// <returns>True when a cell is available.</returns>
    public bool ReadCell()
    {
        try
        {
            return ReadCellCore();
        }
        catch (Exception e) when (XlsbGuard.IsMalformed(e))
        {
            throw XlsbGuard.Typed(e, _partName);
        }
    }

    private bool ReadCellCore()
    {
        if (!_inRow || _reader is null)
        {
            return false;
        }

        while (!_rowDone)
        {
            if (_pendingMaster)
            {
                // The record after a shared or array formula's first cell holds the formula itself.
                _pendingMaster = false;
                if (_reader.Read())
                {
                    if (_reader.Type is XlsbRecordType.ShrFmla or XlsbRecordType.ArrFmla)
                    {
                        LoadMasterFormula(_reader.Type == XlsbRecordType.ArrFmla);
                        continue;
                    }

                    if (!Dispatch())
                    {
                        _rowDone = true;
                        return false;
                    }

                    return true;
                }

                _rowDone = true;
                _ended = true;
                return false;
            }

            if (!_reader.Read())
            {
                _rowDone = true;
                _ended = true;
                return false;
            }

            if (_reader.Type == XlsbRecordType.RowHdr)
            {
                // Hand the next row to ReadRow without losing it.
                ParseRow();
                _rowDone = false;
                _rowCellColumn = -1;
                _inRow = false;
                _lookahead = true;
                return false;
            }

            if (_reader.Type == XlsbRecordType.EndSheetData)
            {
                _rowDone = true;
                _ended = true;
                return false;
            }

            if (Dispatch())
            {
                return true;
            }
        }

        return false;
    }

    private bool _lookahead;

    // A cell record: decode it into the current-cell fields. Returns false for a record that is not a cell.
    private bool Dispatch()
    {
        int type = _reader!.Type;
        if (type is XlsbRecordType.ShrFmla or XlsbRecordType.ArrFmla)
        {
            return false;
        }

        ReadOnlySpan<byte> d = _reader.Data;
        if (type is < XlsbRecordType.CellBlank or > XlsbRecordType.FmlaError && type != XlsbRecordType.CellRString)
        {
            return false;
        }

        if (d.Length < 8)
        {
            _owner.Context.Defects.Report(new Defect(DefectKind.Record, "xlsb.record-short", DefectSeverity.Dropped,
                "A cell record is shorter than a cell header; it is skipped.", DefectLocation.InRecord(_partName, _reader.Offset)));
            return false;
        }

        int col = BinaryPrimitives.ReadInt32LittleEndian(d);
        int word = BinaryPrimitives.ReadInt32LittleEndian(d[4..]);
        if ((uint)col >= CellAddress.MaxColumns)
        {
            _owner.Context.Defects.Report(new Defect(DefectKind.Reference, "xlsb.cell-column-invalid", DefectSeverity.Dropped,
                "A cell has a column outside the sheet; it is skipped.", DefectLocation.InRecord(_partName, _reader.Offset)));
            return false;
        }

        _col = col;
        _style = word & 0xFFFFFF;
        _hasFormula = false;
        _formulaKind = CellFormulaKind.Normal;
        _formulaLength = 0;
        _sharedIndex = -1;
        ReadOnlySpan<byte> v = d[8..];
        switch (type)
        {
            case XlsbRecordType.CellBlank:
                _kind = CellValueKind.Empty;
                break;
            case XlsbRecordType.CellRk:
                _kind = CellValueKind.Number;
                _number = v.Length >= 4 ? Rk(BinaryPrimitives.ReadUInt32LittleEndian(v)) : 0;
                break;
            case XlsbRecordType.CellReal:
                _kind = CellValueKind.Number;
                _number = v.Length >= 8 ? BinaryPrimitives.ReadDoubleLittleEndian(v) : 0;
                break;
            case XlsbRecordType.CellBool:
                _kind = CellValueKind.Boolean;
                _number = v.Length >= 1 && v[0] != 0 ? 1 : 0;
                break;
            case XlsbRecordType.CellError:
                _kind = CellValueKind.Error;
                _error = v.Length >= 1 ? ErrorOf(v[0]) : CellError.Value;
                break;
            case XlsbRecordType.CellIsst:
                _kind = CellValueKind.Text;
                _sharedIndex = v.Length >= 4 ? (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(v), int.MaxValue) : 0;
                break;
            case XlsbRecordType.CellSt:
            case XlsbRecordType.CellRString:
                _kind = CellValueKind.Text;
                InlineText(v, type == XlsbRecordType.CellRString);
                break;
            default:
                Formula(type, v);
                break;
        }

        _rowCellColumn = col;
        return true;
    }

    private void InlineText(ReadOnlySpan<byte> v, bool rich)
    {
        if (rich)
        {
            v = v.Length > 0 ? v[1..] : v;
        }

        if (v.Length < 4)
        {
            _textLength = 0;
            _charsLength = 0;
            return;
        }

        uint n = BinaryPrimitives.ReadUInt32LittleEndian(v);
        if (n > (uint)((v.Length - 4) / 2))
        {
            n = (uint)((v.Length - 4) / 2);
        }

        ReadOnlySpan<byte> bytes = v.Slice(4, (int)n * 2);
        if (_chars.Length < (int)n)
        {
            _chars = new char[Math.Max((int)n, _chars.Length * 2)];
        }

        _charsLength = Encoding.Unicode.GetChars(bytes, _chars);
        int max = Encoding.UTF8.GetMaxByteCount(_charsLength);
        if (_text.Length < max)
        {
            _text = new byte[Math.Max(max, _text.Length * 2)];
        }

        _textLength = Encoding.UTF8.GetBytes(_chars.AsSpan(0, _charsLength), _text);
    }

    private void Formula(int type, ReadOnlySpan<byte> v)
    {
        int at;
        switch (type)
        {
            case XlsbRecordType.FmlaNum:
                _kind = CellValueKind.Number;
                _number = v.Length >= 8 ? BinaryPrimitives.ReadDoubleLittleEndian(v) : 0;
                at = 8;
                break;
            case XlsbRecordType.FmlaBool:
                _kind = CellValueKind.Boolean;
                _number = v.Length >= 1 && v[0] != 0 ? 1 : 0;
                at = 1;
                break;
            case XlsbRecordType.FmlaError:
                _kind = CellValueKind.Error;
                _error = v.Length >= 1 ? ErrorOf(v[0]) : CellError.Value;
                at = 1;
                break;
            default:
                _kind = CellValueKind.Text;
                InlineText(v, false);
                at = v.Length >= 4 ? Math.Min(v.Length, 4 + (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(v), (uint)v.Length) * 2) : v.Length;
                break;
        }

        _hasFormula = true;
        at += 2;
        if (v.Length < at + 4)
        {
            return;
        }

        int cce = BinaryPrimitives.ReadInt32LittleEndian(v[at..]);
        at += 4;
        if (cce < 0 || cce > v.Length - at)
        {
            return;
        }

        ReadOnlySpan<byte> rgce = v.Slice(at, cce);
        at += cce;
        ReadOnlySpan<byte> rgcb = default;
        if (v.Length >= at + 4)
        {
            int cb = BinaryPrimitives.ReadInt32LittleEndian(v[at..]);
            at += 4;
            if (cb >= 0 && cb <= v.Length - at)
            {
                rgcb = v.Slice(at, cb);
            }
        }

        if (rgce.Length >= 5 && rgce[0] == 0x01)
        {
            int mr = BinaryPrimitives.ReadInt32LittleEndian(rgce[1..]);
            int mc = rgcb.Length >= 4 ? BinaryPrimitives.ReadInt32LittleEndian(rgcb) : _col;
            _formulaKind = CellFormulaKind.Shared;
            if (mr == _rowIndex && mc == _col)
            {
                _pendingMaster = true;
            }

            return;
        }

        Keep(rgce, rgcb);
    }

    private void Keep(ReadOnlySpan<byte> rgce, ReadOnlySpan<byte> rgcb)
    {
        if (_formula.Length < rgce.Length)
        {
            _formula = new byte[Math.Max(rgce.Length, _formula.Length * 2)];
        }

        rgce.CopyTo(_formula);
        _formulaLength = rgce.Length;
        if (_formulaExtra.Length < rgcb.Length)
        {
            _formulaExtra = new byte[Math.Max(rgcb.Length, _formulaExtra.Length * 2)];
        }

        rgcb.CopyTo(_formulaExtra);
        _formulaExtraLength = rgcb.Length;
    }

    private void LoadMasterFormula(bool array)
    {
        // [MS-XLSB] §2.4.798, §2.4.6: the range (16 bytes), for an array one flag byte, then the formula.
        ReadOnlySpan<byte> d = _reader!.Data;
        int at = 16 + (array ? 1 : 0);
        if (d.Length < at + 4)
        {
            return;
        }

        int cce = BinaryPrimitives.ReadInt32LittleEndian(d[at..]);
        at += 4;
        if (cce < 0 || cce > d.Length - at)
        {
            return;
        }

        ReadOnlySpan<byte> rgce = d.Slice(at, cce);
        at += cce;
        ReadOnlySpan<byte> rgcb = default;
        if (d.Length >= at + 4)
        {
            int cb = BinaryPrimitives.ReadInt32LittleEndian(d[at..]);
            at += 4;
            if (cb >= 0 && cb <= d.Length - at)
            {
                rgcb = d.Slice(at, cb);
            }
        }

        _formulaKind = array ? CellFormulaKind.Array : CellFormulaKind.Shared;
        Keep(rgce, rgcb);
    }

    private static double Rk(uint v)
    {
        double n = (v & 2) != 0 ? (int)v >> 2 : BitConverter.Int64BitsToDouble((long)(v & 0xFFFFFFFC) << 32);
        return (v & 1) != 0 ? n / 100.0 : n;
    }

    private static CellError ErrorOf(int code) => code switch
    {
        0x00 => CellError.Null,
        0x07 => CellError.DivideByZero,
        0x0F => CellError.Value,
        0x17 => CellError.Reference,
        0x1D => CellError.Name,
        0x24 => CellError.Number,
        0x2A => CellError.NotAvailable,
        0x2B => CellError.GettingData,
        0x2C => CellError.Spill,
        0x2D => CellError.Calc,
        _ => CellError.Unknown,
    };

    /// <summary>Releases the part stream.</summary>
    public void Dispose() => _stream?.Dispose();
}
