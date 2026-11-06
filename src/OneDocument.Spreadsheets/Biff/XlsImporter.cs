using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Spreadsheets.Biff;

/// <summary>A FONT record ([MS-XLS] §2.4.122).</summary>
internal sealed class FontInfo
{
    public int Height = 200;
    public int Flags;
    public int Color = 0x7FFF;
    public int Weight = 400;
    public int Script;
    public int Underline;
    public int Family;
    public int Charset;
    public string Name = "Arial";
}

/// <summary>An XF record ([MS-XLS] §2.4.353): the formatting of a cell or a cell style.</summary>
internal sealed class XfInfo
{
    public int Font;
    public int Format;
    public bool Locked = true;
    public bool Hidden;
    public bool IsStyle;
    public int Parent = 0xFFF;
    public int HAlign;
    public int VAlign = 2;
    public bool Wrap;
    public int Rotation;
    public int Indent;
    public bool Shrink;
    public int ReadingOrder;
    public int[] BorderStyle = new int[4];
    public int[] BorderColor = new int[4];
    public int DiagStyle;
    public int DiagColor;
    public bool DiagDown;
    public bool DiagUp;
    public int Pattern;
    public int Fore = 64;
    public int Back = 65;
}

/// <summary>A BOUNDSHEET record ([MS-XLS] §2.4.28).</summary>
internal sealed class BoundSheet
{
    public long Position;
    public int State;
    public int Type;
    public string Name = string.Empty;
}

/// <summary>A STYLE record ([MS-XLS] §2.4.269).</summary>
internal sealed class StyleInfo
{
    public int Xf;
    public bool BuiltIn;
    public int BuiltInId;
    public int Level;
    public string Name = string.Empty;
}

/// <summary>
/// Reads a BIFF workbook (BIFF2 to BIFF8) into the spreadsheet hub. The record layer is lossless and the conversion reports what the
/// hub cannot carry. Split in parts: this file finds the substreams and reads the workbook globals; the others read cells and the
/// per-sheet features and write the style sheet.
/// </summary>
internal sealed partial class XlsImporter
{
    // Record types ([MS-XLS] §2.3 and the record sections). BIFF3 and later put the version in the high byte of a type.
    private const int Bof = 0x0809;
    private const int Eof = 0x000A;

    private readonly OpenContext _context;
    private readonly ILossSink _loss;
    private readonly CancellationToken _token;
    private BiffRecordSet _rs = null!;
    private string _path = "/";
    private int _version = 8;
    private int _codePage = 1252;
    private Encoding _enc8 = Encoding.Latin1;
    private bool _date1904;
    private readonly List<FontInfo> _fonts = [];
    private readonly Dictionary<int, string> _formats = [];
    private int _formatCounter;
    private readonly List<XfInfo> _xfs = [];
    private readonly List<StyleInfo> _styleInfos = [];
    private uint[]? _palette;
    private readonly List<string> _sst = [];
    private readonly List<BoundSheet> _boundSheets = [];
    private FormulaContext _fctx = null!;
    private readonly List<DefinedNameInfo> _names = [];
    private Workbook _wb = null!;
    private readonly Dictionary<string, long> _lossCounts = [];
    private bool _hasVba;
    private bool _hasProperties;
    private long _richStrings;

    public XlsImporter(OpenContext context, ILossSink loss, CancellationToken token)
    {
        _context = context;
        _loss = loss;
        _token = token;
    }

    /// <summary>The path the records came from, for defect locations.</summary>
    private string StreamPath => _path;

    private void Defect(string code, DefectSeverity severity, string message, int offset, DefectKind kind = DefectKind.Record) =>
        _context.Defects.Report(new Defect(kind, code, severity, message, DefectLocation.InRecord(_path, offset)));

    private void Lose(string code, string feature, long count, string message)
    {
        if (count <= 0)
        {
            return;
        }

        _loss.Report(code, feature, count, message);
    }

    /// <summary>Converts the workbook stream to a hub workbook.</summary>
    public Workbook Import(ReadOnlyMemory<byte> workbookStream, string path, bool hasVba, bool hasProperties = false)
    {
        _hasProperties = hasProperties;
        _path = path;
        _hasVba = hasVba;
        _rs = BiffRecordSet.Parse(workbookStream, _context, path);
        IReadOnlyList<BiffRecord> recs = _rs.Records;
        if (recs.Count == 0 || !IsBof(recs[0].Type))
        {
            throw new OfficeFormatException(DefectKind.Record, "biff.no-bof", "The stream does not begin with a BOF record.", DefectLocation.InRecord(path, 0));
        }

        DetectVersion(recs[0]);
        SetCodePage(_version >= 8 ? 1200 : 1252);
        List<(int Start, int End)> subs = FindSubstreams();
        _wb = Workbook.Create(_context.Limits);

        // Records that describe the workbook (fonts, formats, XFs, names, sheet list) come first in BIFF5 and later; BIFF2-4 repeat them per sheet.
        BuildFormulaContext();
        if (_version >= 5)
        {
            ReadGlobals(subs[0].Start, subs[0].End);
            ResolveNames();
            ImportSheets(subs);
        }
        else
        {
            ImportLegacy(subs);
        }

        ApplyWorkbookParts();
        ReportFinalLosses();
        return _wb;
    }

    private static bool IsBof(int type) => type is 0x0809 or 0x0409 or 0x0209 or 0x0009;

    private void DetectVersion(BiffRecord bof)
    {
        ReadOnlySpan<byte> d = _rs.Data(bof).Span;
        int vers = d.Length >= 2 ? d[0] | (d[1] << 8) : 0;
        _version = bof.Type switch
        {
            0x0009 => 2,
            0x0209 => 3,
            0x0409 => 4,
            _ => vers >= 0x0600 ? 8 : 5,
        };
    }

    // Top-level BOF..EOF ranges; a BOF inside one (a chart in a worksheet) belongs to the enclosing range.
    private List<(int Start, int End)> FindSubstreams()
    {
        var list = new List<(int, int)>();
        int depth = 0;
        int start = -1;
        IReadOnlyList<BiffRecord> recs = _rs.Records;
        for (int i = 0; i < recs.Count; i++)
        {
            if (IsBof(recs[i].Type))
            {
                if (depth == 0)
                {
                    start = i;
                }

                depth++;
            }
            else if (recs[i].Type == Eof && depth > 0)
            {
                depth--;
                if (depth == 0)
                {
                    list.Add((start, i));
                }
            }
        }

        if (depth > 0)
        {
            Defect("biff.missing-eof", DefectSeverity.Repaired, "A substream has no EOF record; it is read to the end of the stream.", recs[start].Offset);
            list.Add((start, recs.Count - 1));
        }

        return list;
    }

    private void SetCodePage(int cp)
    {
        _codePage = cp;
        _enc8 = ResolveEncoding(cp, out bool ok);
        if (!ok)
        {
            _context.Defects.Report(new Defect(DefectKind.Encoding, "biff.codepage-unavailable", DefectSeverity.Repaired,
                "The code page " + cp.ToString(CultureInfo.InvariantCulture) + " is not available; 8-bit text is read as Windows-1252.", DefectLocation.InRecord(_path, 0)));
        }
    }

    // [MS-XLS] §2.4.52 CodePage: 0x8000 is the Macintosh (Roman) page and 0x8001 Windows-1252; others are Windows code page numbers.
    private static Encoding ResolveEncoding(int cp, out bool ok)
    {
        ok = true;
        if (cp == 1200 || cp == 1201)
        {
            return Encoding.Latin1;
        }

        int id = cp switch { 0x8000 => 10000, 0x8001 => 1252, _ => cp };
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(id);
        }
        catch (ArgumentException)
        {
            ok = false;
            return Encoding.GetEncoding(1252);
        }
        catch (NotSupportedException)
        {
            ok = false;
            return Encoding.Latin1;
        }
    }

    private void BuildFormulaContext()
    {
        _fctx = new FormulaContext
        {
            Version = _version,
            Report = (code, message) =>
            {
                if (code.StartsWith("loss.", StringComparison.Ordinal))
                {
                    _lossCounts[code] = _lossCounts.GetValueOrDefault(code) + 1;
                    return;
                }

                _context.Defects.Report(new Defect(DefectKind.Formula, code, DefectSeverity.Dropped, message, DefectLocation.InRecord(_path, _currentOffset)));
            },
        };
    }

    private int _currentOffset;

    // ---- globals ----

    private void ReadGlobals(int start, int end)
    {
        IReadOnlyList<BiffRecord> recs = _rs.Records;
        for (int i = start; i <= end && i < recs.Count; i++)
        {
            _token.ThrowIfCancellationRequested();
            BiffRecord r = recs[i];
            _currentOffset = r.Offset;
            if (!HandleGlobal(r, ref i))
            {
                continue;
            }
        }
    }

    // Returns false for records it does not know.
    private bool HandleGlobal(BiffRecord r, ref int i)
    {
        switch (r.Type)
        {
            case 0x002F: // FilePass
                throw new OfficeEncryptedException(EncryptionProblem.PasswordRequired, DefectLocation.InRecord(_path, r.Offset));
            case 0x0042: // CodePage
                SetCodePage(U16(r, 0));
                return true;
            case 0x0022: // Date1904
                _date1904 = U16(r, 0) == 1;
                return true;
            case 0x0031:
            case 0x0231: // Font
                ReadFont(r);
                return true;
            case 0x041E:
            case 0x001E: // Format
                ReadFormat(r);
                return true;
            case 0x0043:
            case 0x0243:
            case 0x0443:
            case 0x00E0: // XF
                ReadXf(r);
                return true;
            case 0x0293: // Style
                ReadStyle(r);
                return true;
            case 0x0092: // Palette
                ReadPalette(r);
                return true;
            case 0x00FC: // SST
                ReadSst(ref i);
                return true;
            case 0x0085: // BoundSheet8
                ReadBoundSheet(r);
                return true;
            case 0x01AE: // SupBook
                ReadSupBook(r);
                return true;
            case 0x0023: // ExternName
            case 0x0223:
                ReadExternName(r);
                return true;
            case 0x0017: // ExternSheet
                ReadExternSheet(r);
                return true;
            case 0x0018: // Lbl
            case 0x0218:
                ReadName(r);
                return true;
            case 0x00EB: // MsoDrawingGroup: the picture store, spread over CONTINUE records
                {
                    RecordReader rd = RecordReader.Open(_rs, i, withContinues: true, out int last);
                    i = last;
                    _groupData = rd.RestJoined();
                    return true;
                }

            case 0x00D3: // ObProj: the workbook has a VBA project
                _hasVba = true;
                return true;
            default:
                return false;
        }
    }

    private int U8(BiffRecord r, int at) => at < r.Length ? _rs.Bytes.Span[r.DataOffset + at] : 0;

    private int U16(BiffRecord r, int at) => at + 1 < r.Length ? _rs.Bytes.Span[r.DataOffset + at] | (_rs.Bytes.Span[r.DataOffset + at + 1] << 8) : 0;

    private uint U32(BiffRecord r, int at) => (uint)(U16(r, at) | (U16(r, at + 2) << 16));

    private double F64(BiffRecord r, int at)
    {
        long v = 0;
        for (int k = 0; k < 8; k++)
        {
            v |= (long)U8(r, at + k) << (8 * k);
        }

        return BitConverter.Int64BitsToDouble(v);
    }

    private RecordReader Reader(BiffRecord r) => new(_rs.Data(r));

    private void ReadFont(BiffRecord r)
    {
        RecordReader rd = Reader(r);
        var f = new FontInfo { Height = rd.U16(), Flags = rd.U16() };
        if (_version == 2)
        {
            f.Color = 0x7FFF;
            f.Name = rd.UnicodeStringShort(_enc8);
            f.Weight = (f.Flags & 1) != 0 ? 700 : 400;
            f.Underline = (f.Flags & 2) != 0 ? 1 : 0;
        }
        else
        {
            f.Color = rd.U16();
            if (_version >= 5)
            {
                f.Weight = rd.U16();
                f.Script = rd.U16();
                f.Underline = rd.U8();
                f.Family = rd.U8();
                f.Charset = rd.U8();
                rd.U8();
                f.Name = _version >= 8 ? rd.UnicodeString(1, true, _enc8) : rd.UnicodeStringShort(_enc8);
            }
            else
            {
                f.Weight = (f.Flags & 1) != 0 ? 700 : 400;
                f.Underline = (f.Flags & 2) != 0 ? 1 : 0;
                f.Name = rd.UnicodeStringShort(_enc8);
            }
        }

        // The font numbered 4 is never stored; the list the XFs index skips it ([MS-XLS] §2.4.122).
        if (_fonts.Count == 4 && _version >= 3)
        {
            _fonts.Add(new FontInfo());
        }

        _fonts.Add(f);
    }

    private void ReadFormat(BiffRecord r)
    {
        RecordReader rd = Reader(r);
        // BIFF2 and BIFF3 number their formats by position; BIFF4 and later store the identifier.
        int id = _version >= 4 ? rd.U16() : _formatCounter;
        _formatCounter++;
        string code = _version >= 8 ? rd.UnicodeString(2, true, _enc8) : rd.UnicodeStringShort(_enc8);
        _formats[id] = code;
    }

    private void ReadStyle(BiffRecord r)
    {
        int xf = U16(r, 0);
        var s = new StyleInfo { Xf = xf & 0xFFF, BuiltIn = (xf & 0x8000) != 0 };
        if (s.BuiltIn)
        {
            s.BuiltInId = U8(r, 2);
            s.Level = U8(r, 3);
        }
        else
        {
            RecordReader rd = Reader(r);
            rd.U16();
            s.Name = _version >= 8 ? rd.UnicodeString(2, true, _enc8) : rd.UnicodeStringShort(_enc8);
        }

        _styleInfos.Add(s);
    }

    private void ReadPalette(BiffRecord r)
    {
        int n = Math.Min(U16(r, 0), 56);
        var p = new uint[56];
        for (int i = 0; i < n; i++)
        {
            int at = 2 + i * 4;
            p[i] = (uint)(U8(r, at) << 16 | U8(r, at + 1) << 8 | U8(r, at + 2));
        }

        if (n == 56)
        {
            _palette = p;
        }
    }

    // [MS-XLS] §2.4.265 SST: strings may be split by CONTINUE records, restarting their characters with a new flag byte.
    private void ReadSst(ref int i)
    {
        RecordReader rd = RecordReader.Open(_rs, i, withContinues: true, out int last);
        i = last;
        rd.U32();
        long unique = rd.U32();
        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxRecordCount), _context.Limits.MaxRecordCount, unique, DefectLocation.InRecord(_path, _rs.Records[i].Offset));
        for (long k = 0; k < unique && !rd.AtEnd; k++)
        {
            int cch = rd.U16();
            byte flags = rd.U8();
            bool wide = (flags & 1) != 0;
            int runs = (flags & 8) != 0 ? rd.U16() : 0;
            int ext = (flags & 4) != 0 ? rd.I32() : 0;
            string s = rd.Chars(cch, wide, Encoding.Latin1);
            if (runs > 0)
            {
                _richStrings++;
            }

            rd.Skip(runs * 4);
            if (ext > 0)
            {
                rd.Skip(ext);
            }

            _sst.Add(s);
            if (rd.Overrun)
            {
                Defect("biff.sst-truncated", DefectSeverity.Repaired, "The shared string table ends inside a string.", _rs.Records[i].Offset);
                break;
            }
        }

        if (_sst.Count < unique)
        {
            Defect("biff.sst-short", DefectSeverity.Repaired, "The shared string table holds " + _sst.Count.ToString(CultureInfo.InvariantCulture) + " of " + unique.ToString(CultureInfo.InvariantCulture) + " strings.", _rs.Records[i].Offset);
        }
    }

    private void ReadBoundSheet(BiffRecord r)
    {
        RecordReader rd = Reader(r);
        var b = new BoundSheet { Position = rd.U32(), State = rd.U8(), Type = rd.U8() };
        b.Name = _version >= 8 ? rd.UnicodeString(1, true, _enc8) : rd.UnicodeStringShort(_enc8);
        _boundSheets.Add(b);
        _fctx.SheetNames.Add(b.Name);
    }

    // [MS-XLS] §2.4.271 SupBook: 0x0401 in the name-length field marks this workbook, 0x3A01 the add-in functions.
    private void ReadSupBook(BiffRecord r)
    {
        var sb = new SupBook();
        int ctab = U16(r, 0);
        int cch = U16(r, 2);
        if (cch == 0x0401)
        {
            sb.IsInternal = true;
        }
        else if (cch == 0x3A01)
        {
            sb.IsAddIn = true;
        }
        else
        {
            RecordReader rd = Reader(r);
            rd.U16();
            rd.U16();
            sb.Url = rd.UnicodeStringBody(cch, true, _enc8);
            for (int k = 0; k < ctab && !rd.AtEnd; k++)
            {
                sb.Sheets.Add(rd.UnicodeString(2, true, _enc8));
            }
        }

        _fctx.Books.Add(sb);
    }

    private void ReadExternName(BiffRecord r)
    {
        if (_version < 8)
        {
            return;
        }

        if (_fctx.Books.Count == 0)
        {
            return;
        }

        RecordReader rd = Reader(r);
        rd.U16();
        rd.U32();
        string name = rd.UnicodeString(1, true, _enc8);
        _fctx.Books[^1].ExternNames.Add(name);
    }

    private void ReadExternSheet(BiffRecord r)
    {
        if (_version >= 8)
        {
            int c = U16(r, 0);
            for (int k = 0; k < c && 2 + k * 6 + 6 <= r.Length; k++)
            {
                int at = 2 + k * 6;
                _fctx.Xti.Add((U16(r, at), (short)U16(r, at + 2), (short)U16(r, at + 4)));
            }

            return;
        }

        // BIFF5 EXTERNSHEET: a length byte, then 0x03 + a sheet name of this workbook, or an external book's encoded name.
        int len = U8(r, 0);
        string text = _enc8.GetString(_rs.Bytes.Span.Slice(r.DataOffset + 1, Math.Min(len, Math.Max(0, r.Length - 1))));
        _fctx.Biff5Externs.Add(text.Length > 0 && text[0] == '\u0003' ? text[1..] : null);
    }

    // [MS-XLS] §2.4.150 Lbl.
    private void ReadName(BiffRecord r)
    {
        RecordReader rd = Reader(r);
        var n = new DefinedNameInfo();
        int grbit;
        int cch;
        int cce;
        int itab = 0;
        int cchMenu = 0;
        int cchDesc = 0;
        int cchHelp = 0;
        int cchStatus = 0;
        if (_version == 2)
        {
            grbit = rd.U8();
            rd.U8();
            cch = rd.U8();
            cce = rd.U8();
        }
        else
        {
            grbit = rd.U16();
            rd.U8();
            cch = rd.U8();
            cce = rd.U16();
            if (_version >= 5)
            {
                rd.U16();
                itab = rd.U16();
                cchMenu = rd.U8();
                cchDesc = rd.U8();
                cchHelp = rd.U8();
                cchStatus = rd.U8();
            }
        }

        n.Hidden = (grbit & 1) != 0;
        n.IsFunction = (grbit & 2) != 0;
        n.IsMacro = (grbit & 8) != 0;
        n.IsBuiltIn = (grbit & 0x20) != 0;
        n.SheetScope = itab > 0 ? itab - 1 : -1;
        if (_version >= 8 && cch > 0)
        {
            byte flag = rd.U8();
            n.Name = rd.Chars(cch, (flag & 1) != 0, Encoding.Latin1);
        }
        else
        {
            n.Name = rd.Chars(cch, false, _enc8);
        }

        if (n.IsBuiltIn && n.Name.Length > 0)
        {
            n.BuiltIn = n.Name[0];
            n.Name = string.Empty;
        }

        n.Rgce = rd.Bytes(cce);
        if (cchMenu + cchDesc + cchHelp + cchStatus > 0)
        {
            // The strings after the formula are menu text, a description and help texts; only the description is kept.
            if (cchMenu > 0)
            {
                rd.UnicodeStringBody(cchMenu, true, _enc8);
            }

            if (cchDesc > 0)
            {
                n.Comment = _version >= 8 ? rd.UnicodeStringBody(cchDesc, true, _enc8) : rd.Chars(cchDesc, false, _enc8);
            }
        }

        _names.Add(n);
        _fctx.Names.Add(n);
    }

    private void ResolveNames()
    {
        // Names reference each other and sheets, so their formulas are decoded once the whole table is known.
        var dec = new PtgDecoder(_fctx, _enc8);
        foreach (DefinedNameInfo n in _names)
        {
            if (n.Rgce.Length == 0)
            {
                n.Formula = string.Empty;
                continue;
            }

            if (dec.TryDecode(n.Rgce, n.Rgcb, 0, 0, out FormulaNode node))
            {
                n.Formula = FormulaPrinter.Print(node);
            }
        }
    }

    // BIFF8 stores width/height and many things in global order; converting them needs the final style table, so it is done last.
    private void ApplyWorkbookParts()
    {
        _wb.Package.FindPart("/xl/styles.xml")!.SetBytes(BuildStyles());
        if (_date1904)
        {
            SetDate1904();
        }

        ApplyNames();
    }

    private void ReportFinalLosses()
    {
        foreach ((string code, long n) in _lossCounts)
        {
            string feature = code["loss.".Length..].Replace('-', ' ');
            Lose(code, feature, n, "Not carried into the workbook (" + feature + ").");
        }

        // The property streams (title, author, …) are a separate format that is not read; they stay in the source file only.
        if (_hasProperties)
        {
            Lose("loss.document-properties", "document properties", 1, "The summary-information property streams are not converted.");
        }

        if (_hasVba)
        {
            Lose("loss.vba", "VBA project", 1, "The macro project is not carried over; the workbook is converted without macros.");
        }

        Lose("loss.rich-text", "rich text runs", _richStrings, "Formatting runs inside cell text are dropped; the text is kept.");
    }

    // ---- hub helpers ----

    private IPackagePart StylePart => _wb.Package.FindPart("/xl/styles.xml")!;
}
