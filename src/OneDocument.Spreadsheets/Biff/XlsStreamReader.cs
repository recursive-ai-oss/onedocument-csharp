using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Encryption;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.NumberFormats;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Spreadsheets.Biff;

namespace RecursiveAI.OneDocument.Spreadsheets.Streaming;

/// <summary>
/// Reads a legacy binary workbook (Excel 97-2003 <c>.xls</c>, and the older BIFF2 to BIFF5 files) as a forward-only stream of rows and
/// cells, beside the workbook hub: a worksheet's records are read from the file through a buffer, one at a time, and never held. It
/// mirrors <see cref="XlsxStreamReader"/>; the cursor is an <see cref="XlsSheetCursor"/>.
/// </summary>
/// <remarks>
/// <para>
/// Opening reads the workbook-global records only (the sheet list, the number formats and cell formats, the shared string table);
/// each cursor then reads its own worksheet from the file. The shared string table is held as plain text (the characters of every entry
/// plus four bytes per entry), so a workbook with many distinct long strings costs that much memory; nothing else grows with the file.
/// A password-protected workbook is decrypted whole into memory first, as the hub does. Nothing is recalculated: a formula cell
/// reports the result Excel stored.
/// </para>
/// <para>
/// BIFF2 to BIFF4 files carry their formats inside the sheet records, so opening one reads the whole file once to find the sheets.
/// A BIFF file has at most 65,536 rows and 256 columns.
/// </para>
/// </remarks>
public sealed class XlsStreamReader : IDisposable
{
    private const int FilePass = 0x002F;
    private static readonly byte[] CfbSignature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    private readonly List<StreamSheetInfo> _sheets = [];
    private readonly List<long> _sheetOffsets = [];
    private readonly CancellationToken _cancellation;
    private readonly Dictionary<int, string> _formats = [];
    private readonly List<int> _xfFormats = [];
    private readonly Dictionary<(int, int, int), int> _syntheticXf = [];
    private IByteSource? _source;
    private RecursiveAI.OneDocument.Cfb.CfbPackage? _cfb;
    private IPackagePart? _part;
    private byte[]? _plain;
    private string _path = "/";
    private int _formatCounter;
    private sbyte[] _dateXf = [];
    private XlsStrings _strings = XlsStrings.Empty;
    private bool _disposed;

    private XlsStreamReader(IByteSource source, OpenContext context, CancellationToken cancellation)
    {
        _source = source;
        Context = context;
        _cancellation = cancellation;
    }

    internal OpenContext Context { get; }

    internal int Version { get; private set; } = 8;

    internal Encoding Enc8 { get; private set; } = Encoding.Latin1;

    internal string StreamPath => _path;

    /// <summary>Gets the sheets of the workbook in tab order.</summary>
    public IReadOnlyList<StreamSheetInfo> Sheets => _sheets;

    /// <summary>Gets the date system (1900 or 1904) the workbook uses for its dates.</summary>
    public DateSystem DateSystem { get; private set; } = DateSystem.Date1900;

    /// <summary>Opens a legacy binary workbook file. The format is found from the content, never the extension.</summary>
    /// <param name="path">The file.</param>
    /// <param name="context">Limits, defect sink and password; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the open, and the row reads of every cursor made from this reader.</param>
    /// <returns>The reader, which the caller disposes.</returns>
    /// <exception cref="OfficeFormatException">The content is not a BIFF workbook, or is damaged beyond repair.</exception>
    /// <exception cref="OfficeEncryptedException">The workbook is password protected and the password is missing or wrong.</exception>
    /// <exception cref="OfficeLimitException">A limit was exceeded.</exception>
    /// <exception cref="IOException">The file cannot be read.</exception>
    public static XlsStreamReader Open(string path, OpenContext? context = null, CancellationToken cancellationToken = default) =>
        Create(ByteSource.FromFile(path), context ?? new OpenContext(), cancellationToken);

    /// <summary>Opens a legacy binary workbook from a seekable stream.</summary>
    /// <param name="stream">The stream; it must stay readable until the reader is disposed.</param>
    /// <param name="context">Limits, defect sink and password; null for the defaults.</param>
    /// <param name="leaveOpen">True (the default) to leave the stream open when the reader is disposed.</param>
    /// <param name="cancellationToken">Cancels the open, and the row reads of every cursor made from this reader.</param>
    /// <returns>The reader, which the caller disposes.</returns>
    /// <exception cref="OfficeFormatException">The content is not a BIFF workbook, or is damaged beyond repair.</exception>
    /// <exception cref="OfficeEncryptedException">The workbook is password protected and the password is missing or wrong.</exception>
    public static XlsStreamReader Open(Stream stream, OpenContext? context = null, bool leaveOpen = true, CancellationToken cancellationToken = default) =>
        Create(ByteSource.FromStream(stream, leaveOpen), context ?? new OpenContext(), cancellationToken);

    /// <summary>Opens a legacy binary workbook from a byte source, which the reader then owns.</summary>
    /// <param name="source">The bytes; it must support synchronous reads.</param>
    /// <param name="context">Limits, defect sink and password; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the open, and the row reads of every cursor made from this reader.</param>
    /// <returns>The reader, which the caller disposes.</returns>
    /// <exception cref="OfficeFormatException">The content is not a BIFF workbook, or is damaged beyond repair.</exception>
    public static XlsStreamReader Open(IByteSource source, OpenContext? context = null, CancellationToken cancellationToken = default) =>
        Create(source, context ?? new OpenContext(), cancellationToken);

    private static XlsStreamReader Create(IByteSource source, OpenContext context, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(source);
        var reader = new XlsStreamReader(source, context, cancellation);
        try
        {
            reader.Load();
            return reader;
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException or IOException))
        {
            reader.Dispose();
            throw Guard(ex, "/");
        }
    }

    // Anything that is not already a typed exception is damage, not a bug in the caller: it surfaces as a typed one.
    internal static Exception Guard(Exception ex, string path) =>
        ex is OfficeException ? ex : new OfficeFormatException(DefectKind.Record, "biff.malformed", "The workbook is damaged: " + ex.GetType().Name + ".", DefectLocation.InRecord(path), ex);

    internal Stream OpenData() =>
        _plain is not null ? new MemoryStream(_plain, writable: false)
        : _part is not null ? _part.OpenRead()
        : ByteSource.AsStream(_source!);

    private void Load()
    {
        Span<byte> head = stackalloc byte[8];
        int n = _source!.Read(0, head);
        if (n >= 8 && head.SequenceEqual(CfbSignature))
        {
            _cfb = RecursiveAI.OneDocument.Cfb.CfbPackage.Open(_source, Context);
            _part = _cfb.FindPart("/Workbook") ?? _cfb.FindPart("/Book")
                ?? throw new OfficeFormatException(DefectKind.Structure, "biff.no-workbook-stream", "The compound file has no Workbook or Book stream.");
            _path = _part.Name;
        }

        ReadGlobals(false);
    }

    private static bool IsBof(int type) => type is 0x0809 or 0x0409 or 0x0209 or 0x0009;

    private void Defect(string code, DefectSeverity severity, string message, long offset, DefectKind kind = DefectKind.Record) =>
        Context.Defects.Report(new Defect(kind, code, severity, message, DefectLocation.InRecord(_path, offset)));

    // The workbook-global records ([MS-XLS] §2.1.7.20.1): everything a cursor needs before it reads a cell.
    private void ReadGlobals(bool decrypted)
    {
        using Stream data = OpenData();
        var rs = new BiffRecordStream(data);
        if (!rs.Read() || !IsBof(rs.Type))
        {
            throw new OfficeFormatException(DefectKind.Record, "biff.no-bof", "The stream does not begin with a BOF record.", DefectLocation.InRecord(_path, 0));
        }

        // [MS-XLS] §2.4.21 BOF: BIFF8 and BIFF5 share a record type; the version field tells them apart.
        ReadOnlySpan<byte> bof = rs.Data;
        int vers = bof.Length >= 2 ? bof[0] | (bof[1] << 8) : 0;
        Version = rs.Type switch { 0x0009 => 2, 0x0209 => 3, 0x0409 => 4, _ => vers >= 0x0600 ? 8 : 5 };
        SetCodePage(Version >= 8 ? 1200 : 1252);
        var bound = new List<(long Position, int State, int Type, string Name)>();
        int depth = 1;
        long substreamStart = 0;
        int substreamType = bof.Length >= 4 ? bof[2] | (bof[3] << 8) : 0;
        bool legacy = Version < 5;
        while (rs.Read())
        {
            _cancellation.ThrowIfCancellationRequested();
            int type = rs.Type;
            if (IsBof(type))
            {
                if (depth == 0)
                {
                    substreamStart = rs.Offset;
                    ReadOnlySpan<byte> d = rs.Data;
                    substreamType = d.Length >= 4 ? d[2] | (d[3] << 8) : 0x10;
                }

                depth++;
                continue;
            }

            if (type == 0x000A)
            {
                if (--depth == 0)
                {
                    if (!legacy)
                    {
                        break;
                    }

                    if (substreamType is 0x10 or 0x20 or 0x40)
                    {
                        _sheetOffsets.Add(substreamStart);
                        _legacyKinds.Add(substreamType);
                    }
                }

                continue;
            }

            if (depth > 1)
            {
                continue;
            }

            if (type == FilePass && !decrypted)
            {
                Decrypt(rs.Offset);
                ReadGlobals(true);
                return;
            }

            HandleGlobal(rs, bound);
        }

        if (rs.Truncated)
        {
            Defect("biff.truncated-record", DefectSeverity.Dropped, "The stream ends inside a record; the record and anything after it are ignored.", rs.Position);
        }

        if (legacy)
        {
            BuildLegacySheets(bound);
        }
        else
        {
            BuildSheets(bound);
        }
    }

    private readonly List<int> _legacyKinds = [];

    // Decrypts the whole workbook stream into memory, as the hub's reader does; offsets stay where they were.
    private void Decrypt(long filePassOffset)
    {
        long limit = Context.Limits.MaxPartSize;
        using Stream s = OpenData();
        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxPartSize), limit, s.CanSeek ? s.Length : 0, DefectLocation.InRecord(_path));
        using var copy = new MemoryStream();
        s.CopyTo(copy);
        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxPartSize), limit, copy.Length, DefectLocation.InRecord(_path));
        _plain = XlsEncryption.Decrypt(copy.GetBuffer().AsSpan(0, (int)copy.Length), (int)filePassOffset, Context, _path);
    }

    private void SetCodePage(int cp)
    {
        Enc8 = XlsImporter.ResolveEncoding(cp, out bool ok);
        if (!ok)
        {
            Context.Defects.Report(new Defect(DefectKind.Encoding, "biff.codepage-unavailable", DefectSeverity.Repaired,
                "The code page " + cp.ToString(CultureInfo.InvariantCulture) + " is not available; 8-bit text is read as Windows-1252.", DefectLocation.InRecord(_path, 0)));
        }
    }

    private void HandleGlobal(BiffRecordStream rs, List<(long Position, int State, int Type, string Name)> bound)
    {
        ReadOnlySpan<byte> d = rs.Data;
        switch (rs.Type)
        {
            case 0x0042: // CodePage
                SetCodePage(d.Length >= 2 ? d[0] | (d[1] << 8) : 1252);
                break;
            case 0x0022: // Date1904
                DateSystem = d.Length >= 2 && (d[0] | (d[1] << 8)) == 1 ? DateSystem.Date1904 : DateSystem.Date1900;
                break;
            case 0x041E:
            case 0x001E: // Format
                ReadFormat(d);
                break;
            case 0x0043:
            case 0x0243:
            case 0x0443:
            case 0x00E0: // XF: the number format is a 16-bit field in BIFF5 and BIFF8, a byte before ([MS-XLS] §2.4.353)
                _xfFormats.Add(Version >= 5 ? (d.Length >= 4 ? d[2] | (d[3] << 8) : 0) : (d.Length >= 2 ? d[1] : 0));
                break;
            case 0x00FC: // SST
                _strings = XlsStrings.Read(rs, Context, _path);
                break;
            case 0x0085: // BoundSheet8
                ReadBoundSheet(d, bound);
                break;
            default:
                break;
        }
    }

    private void ReadFormat(ReadOnlySpan<byte> d)
    {
        var rd = new RecordReader(d.ToArray());
        // BIFF2 and BIFF3 number their formats by position; BIFF4 and later store the identifier ([MS-XLS] §2.4.126).
        int id = Version >= 4 ? rd.U16() : _formatCounter;
        _formatCounter++;
        _formats[id] = Version >= 8 ? rd.UnicodeString(2, true, Enc8) : rd.UnicodeStringShort(Enc8);
    }

    private void ReadBoundSheet(ReadOnlySpan<byte> d, List<(long Position, int State, int Type, string Name)> bound)
    {
        var rd = new RecordReader(d.ToArray());
        long position = rd.U32();
        if (Version >= 5)
        {
            int state = rd.U8();
            int type = rd.U8();
            bound.Add((position, state, type, Version >= 8 ? rd.UnicodeString(1, true, Enc8) : rd.UnicodeStringShort(Enc8)));
        }
        else
        {
            // BIFF4 workbook: a 4-byte offset, then the name.
            bound.Add((position, 0, 0, rd.UnicodeStringShort(Enc8)));
        }
    }

    private static readonly System.Buffers.SearchValues<char> InvalidSheetNameChars = System.Buffers.SearchValues.Create("\\/?*[]:");

    private string NameOf(string name, int index, HashSet<string> used)
    {
        string wanted = name.Length == 0 ? "Sheet" + (index + 1).ToString(CultureInfo.InvariantCulture) : name;
        bool valid = wanted.Length <= 31 && wanted.AsSpan().IndexOfAny(InvalidSheetNameChars) < 0 && wanted[0] != '\'' && wanted[^1] != '\'' && !used.Contains(wanted);
        if (valid)
        {
            used.Add(wanted);
            return wanted;
        }

        string fallback = "Sheet" + (index + 1).ToString(CultureInfo.InvariantCulture);
        Defect("biff.sheet-name", DefectSeverity.Repaired, "The sheet name '" + name + "' is not valid or is used twice; the sheet is named '" + fallback + "'.", 0);
        used.Add(fallback);
        return fallback;
    }

    private void BuildSheets(List<(long Position, int State, int Type, string Name)> bound)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int index = 0;
        bool anyVisible = false;
        foreach (var b in bound)
        {
            // Type 6 is a Visual Basic module sheet: not a sheet of the workbook.
            if (b.Type == 6)
            {
                continue;
            }

            anyVisible |= b.State == 0;
        }

        foreach (var b in bound)
        {
            if (b.Type == 6)
            {
                continue;
            }

            string name = NameOf(b.Name, index, used);
            SheetKind kind = b.Type == 0 ? SheetKind.Worksheet : b.Type == 1 ? SheetKind.MacroSheet : SheetKind.Chartsheet;
            SheetState state = b.State == 2 ? SheetState.VeryHidden : b.State == 1 ? SheetState.Hidden : SheetState.Visible;
            if (state != SheetState.Visible && !anyVisible)
            {
                // A workbook needs a visible sheet; the hub shows the first one when every sheet is hidden.
                anyVisible = true;
                state = SheetState.Visible;
                Defect("biff.no-visible-sheet", DefectSeverity.Repaired, "Every sheet is hidden; the first one is shown.", 0);
            }

            _sheets.Add(new StreamSheetInfo(index, name, index + 1, kind, state, _path));
            _sheetOffsets.Add(b.Position);
            index++;
        }
    }

    private void BuildLegacySheets(List<(long Position, int State, int Type, string Name)> bound)
    {
        if (_sheetOffsets.Count == 0)
        {
            throw new OfficeFormatException(DefectKind.Record, "biff.no-sheet", "The stream holds no worksheet.", DefectLocation.InRecord(_path, 0));
        }

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < _sheetOffsets.Count; i++)
        {
            int dt = _legacyKinds[i];
            string name = NameOf(i < bound.Count ? bound[i].Name : string.Empty, i, used);
            SheetKind kind = dt == 0x10 ? SheetKind.Worksheet : dt == 0x40 ? SheetKind.MacroSheet : SheetKind.Chartsheet;
            _sheets.Add(new StreamSheetInfo(i, name, i + 1, kind, SheetState.Visible, _path));
        }
    }

    /// <summary>Opens a sheet's rows.</summary>
    /// <param name="index">The 0-based index into <see cref="Sheets"/>.</param>
    /// <returns>The cursor, positioned before the first row. The caller disposes it. Several cursors may be open; each reads its own part of the file.</returns>
    /// <exception cref="ArgumentOutOfRangeException">No such sheet.</exception>
    /// <exception cref="ArgumentException">The sheet is a chart or macro sheet.</exception>
    public XlsSheetCursor OpenSheet(int index)
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

        try
        {
            return new XlsSheetCursor(this, info.Name, _sheetOffsets[index], _cancellation);
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException or IOException))
        {
            throw Guard(ex, _path);
        }
    }

    /// <summary>Opens a sheet's rows by name (compared ignoring case; the first match when names repeat).</summary>
    /// <param name="name">The sheet's name.</param>
    /// <returns>The cursor.</returns>
    /// <exception cref="KeyNotFoundException">No sheet has that name.</exception>
    /// <exception cref="ArgumentException">The sheet is a chart or macro sheet.</exception>
    public XlsSheetCursor OpenSheet(string name)
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

    /// <summary>Gets the number of entries in the shared string table.</summary>
    public int SharedStringCount => _strings.Count;

    /// <summary>Gets one shared string as text (each string is built once and kept, within a budget of about 8 million characters, then built per call).</summary>
    /// <param name="index">The 0-based index.</param>
    /// <returns>The text, empty for an index outside the table.</returns>
    public string GetSharedString(int index) => _strings.GetString(index);

    /// <summary>Tells whether a cell format shows dates or times, by its number format.</summary>
    /// <param name="styleIndex">The cell's XF index.</param>
    /// <returns>True for a date or time format; false for any other, and for an index the workbook does not have.</returns>
    public bool IsDateStyle(int styleIndex)
    {
        if ((uint)styleIndex >= (uint)_xfFormats.Count)
        {
            return false;
        }

        if (_dateXf.Length < _xfFormats.Count)
        {
            Array.Resize(ref _dateXf, _xfFormats.Count);
        }

        sbyte known = _dateXf[styleIndex];
        if (known == 0)
        {
            known = (sbyte)(ComputeIsDate(_xfFormats[styleIndex]) ? 1 : -1);
            _dateXf[styleIndex] = known;
        }

        return known > 0;
    }

    /// <summary>Gets the number format id of a cell format (0 for General and for an unknown index).</summary>
    /// <param name="styleIndex">The cell's XF index.</param>
    /// <returns>The format id as the file stores it.</returns>
    public int GetNumberFormatId(int styleIndex) => (uint)styleIndex < (uint)_xfFormats.Count ? _xfFormats[styleIndex] : 0;

    private bool ComputeIsDate(int formatId)
    {
        // A format the file defines wins over the built-in one of the same number; "General" stands for the built-in.
        NumberFormat? format;
        if (_formats.TryGetValue(formatId, out string? code) && code.Length > 0 && !string.Equals(code, "General", StringComparison.OrdinalIgnoreCase))
        {
            format = NumberFormat.TryParse(code, out NumberFormat? parsed, out _) ? parsed : null;
        }
        else
        {
            format = NumberFormat.GetBuiltIn(formatId);
        }

        return format?.IsDateTime ?? false;
    }

    // BIFF2 cells carry their format in three attribute bytes instead of an XF index; each distinct combination gets an index here, after
    // the default one, as the hub's reader numbers them.
    internal int SyntheticXf(int a0, int a1, int a2)
    {
        if (_xfFormats.Count == 0)
        {
            _xfFormats.Add(0);
        }

        (int, int, int) key = (a0 & 0xC0, a1, a2);
        if (!_syntheticXf.TryGetValue(key, out int index))
        {
            index = _xfFormats.Count;
            _xfFormats.Add(a1 & 0x3F);
            _syntheticXf[key] = index;
        }

        return index;
    }

    internal XlsStrings Strings => _strings;

    /// <summary>Closes the workbook.</summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _cfb?.Dispose();
            _source?.Dispose();
            _plain = null;
        }
    }
}
