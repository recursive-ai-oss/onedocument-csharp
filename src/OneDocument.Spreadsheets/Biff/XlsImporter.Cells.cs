using System.Globalization;
using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Spreadsheets.Biff;

internal sealed partial class XlsImporter
{
    private sealed class Pending
    {
        public int Row;
        public int Col;
        public int Xf;
        public int Offset;
        public byte[] Rgce = [];
        public byte[] Rgcb = [];
        public CellValue Value;
        public bool WantsString;
        public string? Text;
    }

    private sealed class SharedBody
    {
        public int FirstRow;
        public int LastRow;
        public int FirstCol;
        public int LastCol;
        public byte[] Rgce = [];
        public byte[] Rgcb = [];
    }

    private sealed class DataTableInfo
    {
        public int FirstRow;
        public int LastRow;
        public int FirstCol;
        public int LastCol;
        public int Flags;
        public int RwInpRw;
        public int ColInpRw;
        public int RwInpCol;
        public int ColInpCol;
    }

    private sealed class SheetCtx
    {
        public Worksheet Sheet = null!;
        public CellStore Store = null!;
        public int Index;
        public string Name = string.Empty;
        public List<Pending> Formulas = [];
        public Dictionary<(int, int), SharedBody> Shared = [];
        public Dictionary<(int, int), SharedBody> Arrays = [];
        public Dictionary<(int, int), DataTableInfo> Tables = [];
        public Pending? LastFormula;
        public (int Row, int Col)? LastFormulaAnchor;
        public List<ColumnProperties> Columns = [];
        public long Cells;
        public readonly Dictionary<(int, int, int), int> SyntheticXf = [];
        public int PendingIxfe = -1;
    }

    private CellValue[]? _sstValues;

    // ---- sheet list ----

    private void ImportSheets(List<(int Start, int End)> subs)
    {
        var used = new HashSet<int> { 0 };
        int hubIndex = 0;
        var states = new List<(Worksheet Sheet, int State)>();
        for (int s = 0; s < _boundSheets.Count; s++)
        {
            _token.ThrowIfCancellationRequested();
            BoundSheet bs = _boundSheets[s];
            if (bs.Type == 6)
            {
                _hasVba = true;
                continue;
            }

            int sub = FindSubstream(subs, bs.Position, s + 1, used);
            Worksheet ws = hubIndex == 0 ? _wb.Sheets[0] : _wb.Sheets.Add("_tmp" + I(hubIndex));
            SetSheetName(ws, bs.Name, hubIndex);
            hubIndex++;
            states.Add((ws, bs.State));
            if (bs.Type == 2)
            {
                _lossCounts["loss.charts"] = _lossCounts.GetValueOrDefault("loss.charts") + 1;
            }

            if (sub < 0)
            {
                Defect("biff.sheet-missing", DefectSeverity.Dropped, "The sheet '" + bs.Name + "' has no substream at offset " + bs.Position.ToString(CultureInfo.InvariantCulture) + "; it is left empty.", 0);
                continue;
            }

            used.Add(sub);
            if (bs.Type == 1)
            {
                _lossCounts["loss.macro-sheets"] = _lossCounts.GetValueOrDefault("loss.macro-sheets") + 1;
            }

            if (bs.Type is 0 or 1)
            {
                ProcessSheet(subs[sub].Start, subs[sub].End, ws, s);
            }
            else
            {
                CountChartRecords(subs[sub].Start, subs[sub].End);
            }
        }

        // A workbook needs a visible sheet; Excel itself refuses to hide the last one.
        bool anyVisible = states.Any(x => x.State == 0);
        foreach ((Worksheet ws, int st) in states)
        {
            if (st != 0)
            {
                if (!anyVisible)
                {
                    anyVisible = true;
                    Defect("biff.no-visible-sheet", DefectSeverity.Repaired, "Every sheet is hidden; the first one is shown.", 0);
                    continue;
                }

                ws.State = st == 2 ? SheetState.VeryHidden : SheetState.Hidden;
            }
        }
    }

    private int FindSubstream(List<(int Start, int End)> subs, long position, int ordinal, HashSet<int> used)
    {
        IReadOnlyList<BiffRecord> recs = _rs.Records;
        for (int k = 1; k < subs.Count; k++)
        {
            if (recs[subs[k].Start].Offset == position && !used.Contains(k))
            {
                return k;
            }
        }

        // The recorded position matches no substream: take the next one that has not been used.
        for (int k = 1; k < subs.Count; k++)
        {
            if (!used.Contains(k))
            {
                return k;
            }
        }

        return -1;
    }

    private void SetSheetName(Worksheet ws, string name, int index)
    {
        string wanted = name.Length == 0 ? "Sheet" + I(index + 1) : name;
        try
        {
            ws.Rename(wanted);
        }
        catch (ArgumentException)
        {
            string fallback = "Sheet" + I(index + 1);
            Defect("biff.sheet-name", DefectSeverity.Repaired, "The sheet name '" + name + "' is not valid or is used twice; the sheet is named '" + fallback + "'.", 0);
            ws.Rename(fallback);
        }
    }

    private void ImportLegacy(List<(int Start, int End)> subs)
    {
        int n = 0;
        foreach ((int start, int end) in subs)
        {
            _token.ThrowIfCancellationRequested();
            ReadOnlySpan<byte> bof = _rs.Data(_rs.Records[start]).Span;
            int dt = bof.Length >= 4 ? bof[2] | (bof[3] << 8) : 0x10;
            if (dt == 0x0100)
            {
                // BIFF4 workbook: the globals substream lists the sheets that follow.
                ReadGlobalsLegacy(start, end);
                continue;
            }

            if (dt is not (0x10 or 0x40 or 0x20))
            {
                continue;
            }

            Worksheet ws = n == 0 ? _wb.Sheets[0] : _wb.Sheets.Add("_tmp" + I(n));
            string name = n < _boundSheets.Count ? _boundSheets[n].Name : "Sheet" + I(n + 1);
            SetSheetName(ws, name, n);
            n++;
            if (dt == 0x20)
            {
                _lossCounts["loss.charts"] = _lossCounts.GetValueOrDefault("loss.charts") + 1;
                continue;
            }

            if (dt == 0x40)
            {
                _lossCounts["loss.macro-sheets"] = _lossCounts.GetValueOrDefault("loss.macro-sheets") + 1;
            }

            _fctx.SheetNames.Add(name);
            ProcessSheet(start, end, ws, n - 1);
        }

        if (n == 0)
        {
            throw new OfficeFormatException(DefectKind.Record, "biff.no-sheet", "The stream holds no worksheet.", DefectLocation.InRecord(_path, 0));
        }

        ResolveNames();
    }

    private void ReadGlobalsLegacy(int start, int end)
    {
        IReadOnlyList<BiffRecord> recs = _rs.Records;
        for (int i = start; i <= end; i++)
        {
            BiffRecord r = recs[i];
            _currentOffset = r.Offset;
            if (r.Type == 0x0085)
            {
                // BIFF4 workbook BoundSheet: a 4-byte offset, then the name (a length byte and characters).
                RecordReader rd = Reader(r);
                rd.U32();
                _boundSheets.Add(new BoundSheet { Name = rd.UnicodeStringShort(_enc8) });
                continue;
            }

            HandleGlobal(r, ref i);
        }
    }

    private static void CountChartRecords(int start, int end)
    {
        _ = start;
        _ = end;
    }

    // ---- one sheet ----

    private void ProcessSheet(int start, int end, Worksheet ws, int sheetNumber)
    {
        var sc = new SheetCtx { Sheet = ws, Store = ws.Store, Index = sheetNumber, Name = ws.Name };
        _sheet = sc;
        _sheetFeatures = new SheetFeatureData();
        IReadOnlyList<BiffRecord> recs = _rs.Records;
        int depth = 0;
        long cellLimit = _context.Limits.MaxRecordCount;
        for (int i = start; i <= end && i < recs.Count; i++)
        {
            _token.ThrowIfCancellationRequested();
            BiffRecord r = recs[i];
            _currentOffset = r.Offset;
            if (IsBof(r.Type))
            {
                depth++;
                if (depth > 1)
                {
                    _lossCounts["loss.charts"] = _lossCounts.GetValueOrDefault("loss.charts") + 1;
                }

                continue;
            }

            if (r.Type == Eof)
            {
                depth--;
                continue;
            }

            if (depth > 1)
            {
                // Inside an embedded chart substream: its records are kept in the record layer only.
                continue;
            }

            OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxRecordCount), cellLimit, ++sc.Cells, DefectLocation.InRecord(_path, r.Offset));
            HandleSheetRecord(sc, r, ref i);
        }

        FinishSheet(sc);
        _sheet = null;
    }

    private SheetCtx? _sheet;

    private void HandleSheetRecord(SheetCtx sc, BiffRecord r, ref int i)
    {
        switch (r.Type)
        {
            case 0x0203:
            case 0x0003:
                {
                    int row = U16(r, 0);
                    int col = U16(r, 2);
                    int xf = CellXf(sc, r, out int hdr);
                    SetNumber(sc, row, col, xf, F64(r, hdr));
                    break;
                }

            case 0x0002:
                {
                    int xf = CellXf(sc, r, out int hdr);
                    SetNumber(sc, U16(r, 0), U16(r, 2), xf, U16(r, hdr));
                    break;
                }

            case 0x027E:
                SetNumber(sc, U16(r, 0), U16(r, 2), U16(r, 4), DecodeRk(U32(r, 6)));
                break;
            case 0x00BD:
                MulRk(sc, r);
                break;
            case 0x0201:
            case 0x0001:
                {
                    int xf = CellXf(sc, r, out _);
                    SetBlank(sc, U16(r, 0), U16(r, 2), xf);
                    break;
                }

            case 0x00BE:
                MulBlank(sc, r);
                break;
            case 0x00FD:
                LabelSst(sc, r);
                break;
            case 0x0204:
            case 0x0004:
            case 0x00D6:
                Label(sc, r, ref i);
                break;
            case 0x0205:
            case 0x0005:
                BoolErr(sc, r);
                break;
            case 0x0006:
            case 0x0206:
            case 0x0406:
                Formula(sc, r);
                break;
            case 0x0207:
            case 0x0007:
                StringResult(sc, r, ref i);
                break;
            case 0x04BC:
                SharedFormulaRecord(sc, r);
                break;
            case 0x0021:
            case 0x0221:
                ArrayRecord(sc, r);
                break;
            case 0x0236:
            case 0x0036:
                TableRecord(sc, r);
                break;
            case 0x0208:
            case 0x0008:
                RowRecord(sc, r);
                break;
            case 0x007D:
                ColInfo(sc, r);
                break;
            case 0x0024:
                sc.Columns.Add(new ColumnProperties(U8(r, 0) + 1, U8(r, 1) + 1, U16(r, 2) / 256.0, 0, ColumnOptions.CustomWidth));
                break;
            case 0x00E5:
                MergedCells(sc, r);
                break;
            case 0x0044: // IXFE (BIFF2): the XF index for the next cell when its own field holds 63
                sc.PendingIxfe = U16(r, 0);
                break;
            default:
                HandleSheetFeature(sc, r, ref i);
                break;
        }
    }

    // BIFF2 cells carry three attribute bytes where later versions carry a 16-bit XF index: the XF number, the format and font, and
    // alignment, borders and shading. They are folded into the XF table here.
    private int CellXf(SheetCtx sc, BiffRecord r, out int headerLength)
    {
        if (_version >= 3)
        {
            headerLength = 6;
            return U16(r, 4);
        }

        headerLength = 7;
        int a0 = U8(r, 4);
        int a1 = U8(r, 5);
        int a2 = U8(r, 6);
        int ixfe = a0 & 0x3F;
        if (ixfe == 63 && sc.PendingIxfe >= 0)
        {
            ixfe = sc.PendingIxfe;
        }

        sc.PendingIxfe = -1;
        EnsureDefaultXf();
        int key2 = a2;
        int key1 = a1;
        int key0 = a0 & 0xC0;
        if (!sc.SyntheticXf.TryGetValue((key0, key1, key2), out int idx))
        {
            var x = new XfInfo
            {
                Format = a1 & 0x3F,
                Font = (a1 >> 6) & 3,
                Locked = (a0 & 0x80) != 0,
                Hidden = (a0 & 0x40) != 0,
                HAlign = a2 & 7,
            };
            for (int k = 0; k < 4; k++)
            {
                if ((a2 & (8 << k)) != 0)
                {
                    x.BorderStyle[k] = 1;
                    x.BorderColor[k] = 64;
                }
            }

            if ((a2 & 0x80) != 0)
            {
                x.Pattern = 2;
                x.Fore = 0;
                x.Back = 1;
            }

            idx = _xfs.Count;
            _xfs.Add(x);
            sc.SyntheticXf[(key0, key1, key2)] = idx;
        }

        _ = ixfe;
        return idx;
    }

    private static double DecodeRk(uint rk)
    {
        // [MS-XLS] §2.5.218 RkNumber: bit 0 divides by 100, bit 1 selects a 30-bit integer over the high 30 bits of an IEEE double.
        double v;
        if ((rk & 2) != 0)
        {
            v = (int)rk >> 2;
        }
        else
        {
            v = BitConverter.Int64BitsToDouble((long)(rk & 0xFFFFFFFCu) << 32);
        }

        return (rk & 1) != 0 ? v / 100.0 : v;
    }

    private bool InGrid(int row, int col)
    {
        if (row < 0 || col < 0 || row >= CellAddress.MaxRows || col >= CellAddress.MaxColumns)
        {
            Defect("biff.cell-out-of-range", DefectSeverity.Dropped, "A cell at row " + I(row + 1) + ", column " + I(col + 1) + " is outside the sheet.", _currentOffset, DefectKind.Reference);
            return false;
        }

        return true;
    }

    private void SetNumber(SheetCtx sc, int row, int col, int xf, double v)
    {
        if (!InGrid(row, col))
        {
            return;
        }

        sc.Store.SetValue(row + 1, col + 1, CellValue.FromNumber(v));
        if (xf != 0)
        {
            sc.Store.SetStyle(row + 1, col + 1, xf);
        }
    }

    private void SetBlank(SheetCtx sc, int row, int col, int xf)
    {
        if (xf != 0 && InGrid(row, col))
        {
            sc.Store.SetStyle(row + 1, col + 1, xf);
        }
    }

    private void SetText(SheetCtx sc, int row, int col, int xf, string text)
    {
        if (!InGrid(row, col))
        {
            return;
        }

        sc.Store.SetValue(row + 1, col + 1, _wb.MakeStringValue(text));
        if (xf != 0)
        {
            sc.Store.SetStyle(row + 1, col + 1, xf);
        }
    }

    private void MulRk(SheetCtx sc, BiffRecord r)
    {
        int row = U16(r, 0);
        int first = U16(r, 2);
        int last = U16(r, r.Length - 2);
        int n = Math.Min((r.Length - 6) / 6, last - first + 1);
        for (int k = 0; k < n; k++)
        {
            int at = 4 + k * 6;
            SetNumber(sc, row, first + k, U16(r, at), DecodeRk(U32(r, at + 2)));
        }
    }

    private void MulBlank(SheetCtx sc, BiffRecord r)
    {
        int row = U16(r, 0);
        int first = U16(r, 2);
        int last = U16(r, r.Length - 2);
        int n = Math.Min((r.Length - 6) / 2, last - first + 1);
        for (int k = 0; k < n; k++)
        {
            SetBlank(sc, row, first + k, U16(r, 4 + k * 2));
        }
    }

    private void LabelSst(SheetCtx sc, BiffRecord r)
    {
        long isst = U32(r, 6);
        if (isst >= _sst.Count)
        {
            Defect("biff.sst-index", DefectSeverity.Dropped, "A text cell refers to shared string " + isst.ToString(CultureInfo.InvariantCulture) + ", which the table does not hold.", r.Offset, DefectKind.Reference);
            SetBlank(sc, U16(r, 0), U16(r, 2), U16(r, 4));
            return;
        }

        int row = U16(r, 0);
        int col = U16(r, 2);
        if (!InGrid(row, col))
        {
            return;
        }

        _sstValues ??= new CellValue[_sst.Count];
        CellValue v = _sstValues[isst];
        if (v.IsEmpty)
        {
            v = _wb.MakeStringValue(_sst[(int)isst]);
            _sstValues[isst] = v;
        }
        else
        {
            _wb.SharedStrings.NoteReference(1);
        }

        sc.Store.SetValue(row + 1, col + 1, v);
        int xf = U16(r, 4);
        if (xf != 0)
        {
            sc.Store.SetStyle(row + 1, col + 1, xf);
        }
    }

    private void Label(SheetCtx sc, BiffRecord r, ref int i)
    {
        RecordReader rd = RecordReader.Open(_rs, i, withContinues: r.Type == 0x00D6 || _version >= 8, out int last);
        i = last;
        int row = rd.U16();
        int col = rd.U16();
        int xf;
        if (_version >= 3)
        {
            xf = rd.U16();
        }
        else
        {
            xf = CellXf(sc, r, out _);
            rd.Skip(3);
        }

        string text;
        if (_version >= 8)
        {
            text = rd.UnicodeString(2, true, _enc8);
        }
        else if (_version >= 3)
        {
            text = rd.Chars(rd.U16(), false, _enc8);
        }
        else
        {
            text = rd.UnicodeStringShort(_enc8);
        }

        SetText(sc, row, col, xf, text);
    }

    private static readonly CellError[] ErrorMap = BuildErrorMap();

    private static CellError[] BuildErrorMap()
    {
        var m = new CellError[0x30];
        for (int k = 0; k < m.Length; k++)
        {
            m[k] = CellError.Value;
        }

        m[0x00] = CellError.Null;
        m[0x07] = CellError.DivideByZero;
        m[0x0F] = CellError.Value;
        m[0x17] = CellError.Reference;
        m[0x1D] = CellError.Name;
        m[0x24] = CellError.Number;
        m[0x2A] = CellError.NotAvailable;
        m[0x2B] = CellError.GettingData;
        return m;
    }

    private static CellError ErrorOf(int code) => code is >= 0 and < 0x30 ? ErrorMap[code] : CellError.Value;

    private void BoolErr(SheetCtx sc, BiffRecord r)
    {
        int row = U16(r, 0);
        int col = U16(r, 2);
        int xf = CellXf(sc, r, out int hdr);
        int val = U8(r, hdr);
        bool isError = U8(r, hdr + 1) != 0;
        if (!InGrid(row, col))
        {
            return;
        }

        sc.Store.SetValue(row + 1, col + 1, isError ? CellValue.FromError(ErrorOf(val)) : CellValue.FromBoolean(val != 0));
        if (xf != 0)
        {
            sc.Store.SetStyle(row + 1, col + 1, xf);
        }
    }

    // ---- formulas ----

    // [MS-XLS] §2.4.127 Formula: cell, 8-byte cached value, flags, 4 unused bytes (BIFF5 and later), the token length and the tokens
    // followed by the extra data. A value whose last two bytes are 0xFFFF is not a number: 0 string (in the STRING record that
    // follows), 1 boolean, 2 error, 3 empty text.
    private void Formula(SheetCtx sc, BiffRecord r)
    {
        int row = U16(r, 0);
        int col = U16(r, 2);
        int xf;
        int vAt;
        int cceAt;
        int rgceAt;
        if (_version >= 3)
        {
            xf = U16(r, 4);
            vAt = 6;
            cceAt = _version >= 5 ? 20 : 16;
            rgceAt = cceAt + 2;
        }
        else
        {
            xf = CellXf(sc, r, out _);
            vAt = 7;
            cceAt = 16;
            rgceAt = 17;
        }

        int cce = _version >= 3 ? U16(r, cceAt) : U8(r, cceAt);
        if (rgceAt + cce > r.Length)
        {
            Defect("biff.formula-length", DefectSeverity.Dropped, "A formula claims " + I(cce) + " token bytes but the record is shorter.", r.Offset);
            cce = Math.Max(0, r.Length - rgceAt);
        }

        var p = new Pending { Row = row, Col = col, Xf = xf, Offset = r.Offset };
        ReadOnlySpan<byte> data = _rs.Data(r).Span;
        p.Rgce = data.Slice(rgceAt, cce).ToArray();
        if (rgceAt + cce < r.Length)
        {
            p.Rgcb = data[(rgceAt + cce)..].ToArray();
        }

        if (U16(r, vAt + 6) == 0xFFFF)
        {
            switch (U8(r, vAt))
            {
                case 0:
                    p.WantsString = true;
                    break;
                case 1:
                    p.Value = CellValue.FromBoolean(U8(r, vAt + 2) != 0);
                    break;
                case 2:
                    p.Value = CellValue.FromError(ErrorOf(U8(r, vAt + 2)));
                    break;
                default:
                    p.Text = string.Empty;
                    break;
            }
        }
        else
        {
            p.Value = CellValue.FromNumber(F64(r, vAt));
        }

        sc.Formulas.Add(p);
        sc.LastFormula = p;
        sc.LastFormulaAnchor = (row, col);
    }

    private void StringResult(SheetCtx sc, BiffRecord r, ref int i)
    {
        RecordReader rd = RecordReader.Open(_rs, i, withContinues: true, out int last);
        i = last;
        string text = _version >= 8 ? rd.UnicodeString(2, true, _enc8) : _version >= 3 ? rd.Chars(rd.U16(), false, _enc8) : rd.UnicodeStringShort(_enc8);
        if (sc.LastFormula is { WantsString: true } p)
        {
            p.Text = text;
            p.WantsString = false;
        }
        else
        {
            Defect("biff.string-orphan", DefectSeverity.Dropped, "A STRING record does not follow a formula that needs one.", r.Offset);
        }
    }

    private SharedBody ReadSharedBody(BiffRecord r, int headerLength, int cceAt)
    {
        var b = new SharedBody { FirstRow = U16(r, 0), LastRow = U16(r, 2), FirstCol = U8(r, 4), LastCol = U8(r, 5) };
        int cce = U16(r, cceAt);
        int at = cceAt + 2;
        _ = headerLength;
        ReadOnlySpan<byte> data = _rs.Data(r).Span;
        cce = Math.Min(cce, Math.Max(0, data.Length - at));
        b.Rgce = data.Slice(at, cce).ToArray();
        if (at + cce < data.Length)
        {
            b.Rgcb = data[(at + cce)..].ToArray();
        }

        return b;
    }

    // [MS-XLS] §2.4.260 ShrFmla: the range, a use count, then the shared tokens (relative references as PtgRefN).
    private void SharedFormulaRecord(SheetCtx sc, BiffRecord r)
    {
        if (sc.LastFormulaAnchor is { } a)
        {
            sc.Shared[a] = ReadSharedBody(r, 10, 8);
        }
    }

    // [MS-XLS] §2.4.4 Array: the range, flags, 4 unused bytes, the token length and the tokens.
    private void ArrayRecord(SheetCtx sc, BiffRecord r)
    {
        if (sc.LastFormulaAnchor is { } a)
        {
            sc.Arrays[a] = ReadSharedBody(r, 14, 12);
        }
    }

    private void TableRecord(SheetCtx sc, BiffRecord r)
    {
        if (sc.LastFormulaAnchor is { } a)
        {
            sc.Tables[a] = new DataTableInfo
            {
                FirstRow = U16(r, 0),
                LastRow = U16(r, 2),
                FirstCol = U8(r, 4),
                LastCol = U8(r, 5),
                Flags = U16(r, 6),
                RwInpRw = U16(r, 8),
                ColInpRw = U16(r, 10),
                RwInpCol = U16(r, 12),
                ColInpCol = U16(r, 14),
            };
        }
    }

    internal static string ColumnName(int column)
    {
        var sb = new StringBuilder(3);
        while (column > 0)
        {
            column--;
            sb.Insert(0, (char)('A' + (column % 26)));
            column /= 26;
        }

        return sb.ToString();
    }

    private static string A1(int row, int col) => ColumnName(col + 1) + I(row + 1);

    private static string RangeText(int r1, int c1, int r2, int c2) => r1 == r2 && c1 == c2 ? A1(r1, c1) : A1(r1, c1) + ":" + A1(r2, c2);

    // XML 1.0 cannot hold most control characters, lone surrogates or U+FFFE/U+FFFF.
    internal static bool IsXmlSafe(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if ((c < 0x20 && c != 9 && c != 10 && c != 13) || c == 0xFFFE || c == 0xFFFF)
            {
                return false;
            }

            if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                i++;
            }
            else if (char.IsSurrogate(c))
            {
                return false;
            }
        }

        return true;
    }

    private string? DecodeToText(ReadOnlyMemory<byte> rgce, ReadOnlyMemory<byte> rgcb, int row, int col, int offset)
    {
        var dec = new PtgDecoder(_fctx, _enc8);
        int saved = _currentOffset;
        _currentOffset = offset;
        try
        {
            if (dec.TryDecode(rgce, rgcb, row, col, out FormulaNode node))
            {
                string text = FormulaPrinter.Print(node);
                if (!IsXmlSafe(text))
                {
                    Defect("biff.formula-text-invalid", DefectSeverity.Dropped, "A formula holds a character that XML cannot carry (a control character in a text constant).", offset, DefectKind.Formula);
                    return null;
                }

                if (text.Length > _context.Limits.MaxFormulaLength)
                {
                    Defect("biff.formula-too-long", DefectSeverity.Dropped, "A formula is longer than the formula limit.", offset, DefectKind.Formula);
                    return null;
                }

                return text;
            }

            return null;
        }
        finally
        {
            _currentOffset = saved;
        }
    }

    private void ResolveFormulas(SheetCtx sc)
    {
        long lost = 0;
        foreach (Pending p in sc.Formulas)
        {
            _token.ThrowIfCancellationRequested();
            if (!InGrid(p.Row, p.Col))
            {
                continue;
            }

            int row1 = p.Row + 1;
            int col1 = p.Col + 1;
            CellValue value = p.Value;
            if (p.Text is not null)
            {
                value = new CellValue(CellValueKind.Text, 0, sc.Store.AddPoolString(p.Text));
            }
            else if (p.WantsString)
            {
                value = new CellValue(CellValueKind.Text, 0, sc.Store.AddPoolString(string.Empty));
            }

            sc.Store.SetValue(row1, col1, value);
            if (p.Xf != 0)
            {
                sc.Store.SetStyle(row1, col1, p.Xf);
            }

            CellFormula? formula = null;
            ReadOnlySpan<byte> g = p.Rgce;
            if (g.Length >= 5 && g[0] == 0x01 && g.Length == 5)
            {
                // PtgExp: the cell shares a formula with, or is inside the array of, the cell it names.
                int ar = g[1] | (g[2] << 8);
                int ac = g[3] | (g[4] << 8);

                if (sc.Shared.TryGetValue((ar, ac), out SharedBody? sb))
                {
                    string? text = DecodeToText(sb.Rgce, sb.Rgcb, p.Row, p.Col, p.Offset);
                    if (text is null)
                    {
                        lost++;
                    }
                    else
                    {
                        formula = new CellFormula(text);
                    }
                }
                else if (sc.Arrays.TryGetValue((ar, ac), out SharedBody? ab))
                {
                    if (ar == p.Row && ac == p.Col)
                    {
                        string? text = DecodeToText(ab.Rgce, ab.Rgcb, p.Row, p.Col, p.Offset);
                        if (text is null)
                        {
                            lost++;
                        }
                        else
                        {
                            formula = new CellFormula(text, CellFormulaKind.Array, RangeText(ab.FirstRow, ab.FirstCol, ab.LastRow, ab.LastCol));
                        }
                    }
                }
                else if (sc.Tables.TryGetValue((ar, ac), out DataTableInfo? dt))
                {
                    if (ar == p.Row && ac == p.Col)
                    {
                        formula = DataTableFormula(dt);
                    }
                }
                else
                {
                    Defect("biff.formula-exp-missing", DefectSeverity.Dropped, "A formula refers to a shared or array formula at " + A1(ar, ac) + " that the sheet does not define.", p.Offset, DefectKind.Formula);
                    lost++;
                }
            }
            else if (g.Length == 5 && g[0] == 0x02)
            {
                int ar = g[1] | (g[2] << 8);
                int ac = g[3] | (g[4] << 8);
                if (ar == p.Row && ac == p.Col && sc.Tables.TryGetValue((ar, ac), out DataTableInfo? dt))
                {
                    formula = DataTableFormula(dt);
                }
            }
            else
            {
                string? text = DecodeToText(p.Rgce, p.Rgcb, p.Row, p.Col, p.Offset);
                if (text is null)
                {
                    lost++;
                }
                else
                {
                    formula = new CellFormula(text);
                }
            }

            if (formula is not null)
            {
                sc.Store.SetFormula(row1, col1, formula);
            }
        }

        if (lost > 0)
        {
            Lose("loss.formulas", "formulas", lost, "Formulas that could not be converted keep their cached values as constants.");
        }
    }

    // [MS-XLS] §2.4.319 Table: a data table; flags say whether the input is a row or column and whether there are two inputs.
    private static CellFormula DataTableFormula(DataTableInfo t)
    {
        bool twoD = (t.Flags & 8) != 0;
        bool rowInput = (t.Flags & 4) != 0;
        var sb = new StringBuilder();
        if (twoD)
        {
            sb.Append(" dt2D=\"1\"");
        }

        if (rowInput && !twoD)
        {
            sb.Append(" dtr=\"1\"");
        }

        sb.Append(" r1=\"").Append(twoD ? A1(t.RwInpRw, t.ColInpRw) : A1(t.RwInpRw, t.ColInpRw)).Append('"');
        if (twoD)
        {
            sb.Append(" r2=\"").Append(A1(t.RwInpCol, t.ColInpCol)).Append('"');
        }

        return new CellFormula(string.Empty, CellFormulaKind.DataTable, RangeText(t.FirstRow, t.FirstCol, t.LastRow, t.LastCol), -1, sb.ToString());
    }

    // ---- rows, columns, merges ----

    private void RowRecord(SheetCtx sc, BiffRecord r)
    {
        int row = U16(r, 0);
        if (!InGrid(row, 0))
        {
            return;
        }

        RowProperties rp = sc.Sheet.GetRow(row + 1);
        if (_version < 3)
        {
            int h = U16(r, 6);
            if ((h & 0x8000) == 0 && h > 0)
            {
                rp.Height = h / 20.0;
            }

            return;
        }

        int height = U16(r, 6) & 0x7FFF;
        int flags = U8(r, 12);
        bool hidden = (flags & 0x20) != 0;
        bool custom = (flags & 0x40) != 0;
        int level = flags & 7;
        if (custom && height > 0)
        {
            rp.Height = height / 20.0;
        }

        if (hidden)
        {
            rp.Hidden = true;
        }

        if (level > 0)
        {
            rp.OutlineLevel = level;
        }

        if ((flags & 0x10) != 0)
        {
            rp.Collapsed = true;
        }

        if ((U8(r, 12) & 0x80) != 0)
        {
            int ixfe = U16(r, 14) & 0xFFF;
            if (ixfe != 0)
            {
                rp.StyleIndex = ixfe;
            }
        }
    }

    private void ColInfo(SheetCtx sc, BiffRecord r)
    {
        int first = U16(r, 0);
        int last = U16(r, 2);
        int width = U16(r, 4);
        int xf = U16(r, 6);
        int f = U16(r, 8);
        ColumnOptions o = ColumnOptions.None;
        if ((f & 1) != 0)
        {
            o |= ColumnOptions.Hidden;
        }

        if ((f & 2) != 0)
        {
            o |= ColumnOptions.CustomWidth;
        }

        if ((f & 4) != 0)
        {
            o |= ColumnOptions.BestFit;
        }

        if ((f & 8) != 0)
        {
            o |= ColumnOptions.Phonetic;
        }

        if ((f & 0x1000) != 0)
        {
            o |= ColumnOptions.Collapsed;
        }

        if (last < first || first > 255)
        {
            Defect("biff.colinfo-range", DefectSeverity.Dropped, "A column range runs from " + I(first) + " to " + I(last) + ".", r.Offset);
            return;
        }

        sc.Columns.Add(new ColumnProperties(first + 1, Math.Min(last, 255) + 1, width / 256.0, xf, o, (f >> 8) & 7));
    }

    private void MergedCells(SheetCtx sc, BiffRecord r)
    {
        int n = Math.Min(U16(r, 0), (r.Length - 2) / 8);
        for (int k = 0; k < n; k++)
        {
            int at = 2 + k * 8;
            int r1 = U16(r, at);
            int r2 = U16(r, at + 2);
            int c1 = U16(r, at + 4);
            int c2 = U16(r, at + 6);
            if (r2 < r1 || c2 < c1 || c2 > 255)
            {
                Defect("biff.merge-range", DefectSeverity.Dropped, "A merged range is inverted or outside the sheet.", r.Offset);
                continue;
            }

            sc.Sheet.Merge(new CellRange(new CellAddress(r1 + 1, c1 + 1), new CellAddress(r2 + 1, c2 + 1)));
        }
    }
}
