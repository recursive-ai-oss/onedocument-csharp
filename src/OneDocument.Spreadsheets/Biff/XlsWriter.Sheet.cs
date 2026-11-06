using System.Globalization;
using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Spreadsheets.Biff;

internal sealed partial class XlsWriter
{
    private sealed record ArrayInfo(int Row1, int Col1, int Row2, int Col2);

    private sealed record TableInfo(int Row1, int Col1, int Row2, int Col2, int Flags, int RowIn, int ColIn, int RowIn2, int ColIn2);

    // The helpers that read the sheet frame (everything in the part but the cells).
    private static MarkupNode Child(MarkupNode parent, string local)
    {
        if (parent.IsNull)
        {
            return default;
        }

        foreach (MarkupNode c in parent.ChildElements)
        {
            if (c.LocalNameString == local)
            {
                return c;
            }
        }

        return default;
    }

    private static bool Attr(MarkupNode n, string name, bool dflt)
    {
        string? v = n.IsNull ? null : n.GetAttribute(name);
        return v is null ? dflt : v is "1" or "true";
    }

    private static int AttrInt(MarkupNode n, string name, int dflt) =>
        !n.IsNull && n.GetAttribute(name) is { } v && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) ? i : dflt;

    private static double AttrDouble(MarkupNode n, string name, double dflt) =>
        !n.IsNull && n.GetAttribute(name) is { } v && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : dflt;

    private static bool TryRange(string text, out int r1, out int c1, out int r2, out int c2)
    {
        r1 = c1 = r2 = c2 = 0;
        if (!CellRange.TryParse(text.Replace("$", string.Empty, StringComparison.Ordinal), out CellRange r))
        {
            return false;
        }

        r1 = r.TopLeft.Row - 1;
        c1 = r.TopLeft.Column - 1;
        r2 = r.BottomRight.Row - 1;
        c2 = r.BottomRight.Column - 1;
        return true;
    }

    private CellRange? AutoFilterRange(Worksheet ws)
    {
        MarkupNode af = Child(ws.LayoutMarkup, "autoFilter");
        if (!af.IsNull && af.GetAttribute("ref") is { } r && CellRange.TryParse(r, out CellRange range) && range.BottomRight.Row <= MaxRows && range.BottomRight.Column <= MaxColumns)
        {
            return range;
        }

        return null;
    }

    private static int BiffErrorCode(CellError e) => e switch
    {
        CellError.Null => 0x00,
        CellError.DivideByZero => 0x07,
        CellError.Value => 0x0F,
        CellError.Reference => 0x17,
        CellError.Name => 0x1D,
        CellError.Number => 0x24,
        CellError.NotAvailable => 0x2A,
        CellError.GettingData => 0x2B,
        _ => 0x0F,
    };

    // [MS-XLS] §2.5.217 RkNumber: a 30-bit integer, or the same divided by 100.
    private static bool TryRk(double v, out uint rk)
    {
        rk = 0;
        if (double.IsNaN(v) || double.IsInfinity(v) || (v == 0 && double.IsNegative(v)))
        {
            return false;
        }

        if (v == Math.Floor(v) && Math.Abs(v) < (1 << 29))
        {
            rk = (uint)((int)v << 2) | 2;
            return true;
        }

        double h = v * 100;
        if (Math.Abs(h) < (1 << 29) && Math.Round(h) / 100 == v && h == Math.Floor(h))
        {
            rk = (uint)((int)h << 2) | 3;
            return true;
        }

        return false;
    }

    private void WriteSheet(int si)
    {
        Worksheet ws = _sheets[si];
        _s.PatchU32(_sheetOut[si].BoundSheetPatchAt, (uint)_s.Position);
        _sheetOut[si].StreamPosition = _s.Position;
        MarkupNode root = ws.LayoutMarkup;
        Bof(0x0010);

        // Gather the rows once: the INDEX record needs the row range and the block count before the rows are written.
        var rows = new List<(int Row, CellRowView View)>();
        int minRow = int.MaxValue;
        int maxRow = -1;
        int minCol = int.MaxValue;
        int maxCol = -1;
        long outside = 0;
        foreach (CellRowView r in ws.Store.EnumerateRows())
        {
            if (r.Row > MaxRows)
            {
                outside += r.Count;
                continue;
            }

            rows.Add((r.Row - 1, r));
            minRow = Math.Min(minRow, r.Row - 1);
            maxRow = Math.Max(maxRow, r.Row - 1);
            for (int i = 0; i < r.Count; i++)
            {
                int c = r.ColumnAt(i) - 1;
                if (c >= MaxColumns)
                {
                    outside++;
                    continue;
                }

                minCol = Math.Min(minCol, c);
                maxCol = Math.Max(maxCol, c);
            }
        }

        if (outside > 0)
        {
            _loss.Report("loss.cells-outside-grid", "cells outside the BIFF8 grid", outside, "Cells below row 65,536 or right of column IV were not written.");
        }

        int blocks = (rows.Count + 31) / 32;
        int indexPosition = _s.Position;
        var idx = new BiffBuffer();
        idx.U32(0);
        idx.U32((uint)(maxRow < 0 ? 0 : minRow));
        idx.U32((uint)(maxRow < 0 ? 0 : maxRow + 1));
        idx.U32(0);
        for (int i = 0; i < blocks; i++)
        {
            idx.U32(0);
        }

        _s.Record(0x020B, idx);

        // GLOBALS ([MS-XLS] §2.1.7.20.6)
        U16Record(0x000D, 1); // CalcMode
        U16Record(0x000C, 100); // CalcCount
        U16Record(0x000F, 1); // CalcRefMode
        U16Record(0x0011, 0); // CalcIter
        var delta = new BiffBuffer();
        delta.F64(0.001);
        _s.Record(0x0010, delta);
        U16Record(0x005F, 1); // CalcSaveRecalc
        MarkupNode printOptions = Child(root, "printOptions");
        U16Record(0x002A, Attr(printOptions, "headings", false) ? 1 : 0);
        U16Record(0x002B, Attr(printOptions, "gridLines", false) ? 1 : 0);
        U16Record(0x0082, 1); // GridSet
        int maxRowLevel = 0;
        foreach ((int _, CellRowView v) in rows)
        {
            maxRowLevel = Math.Max(maxRowLevel, ws.GetRow(v.Row).OutlineLevel);
        }

        int maxColLevel = ws.Columns.Count == 0 ? 0 : ws.Columns.Max(c => c.OutlineLevel);
        var guts = new BiffBuffer();
        guts.U16(maxRowLevel > 0 ? 5 + 12 * maxRowLevel : 0);
        guts.U16(maxColLevel > 0 ? 5 + 12 * maxColLevel : 0);
        guts.U16(maxRowLevel);
        guts.U16(maxColLevel);
        _s.Record(0x0080, guts);
        MarkupNode fmt = Child(root, "sheetFormatPr");
        var drh = new BiffBuffer();
        double dh = AttrDouble(fmt, "defaultRowHeight", 15);
        drh.U16((Attr(fmt, "customHeight", false) ? 1 : 0) | (Attr(fmt, "zeroHeight", false) ? 2 : 0));
        drh.U16((int)Math.Round(dh * 20));
        _s.Record(0x0225, drh);
        MarkupNode sheetPr = Child(root, "sheetPr");
        MarkupNode outlinePr = Child(sheetPr, "outlinePr");
        MarkupNode pageSetUpPr = Child(sheetPr, "pageSetUpPr");
        int wsBool = 0x04C1;
        if (!Attr(outlinePr, "summaryBelow", true))
        {
            wsBool &= ~0x40;
        }

        if (!Attr(outlinePr, "summaryRight", true))
        {
            wsBool &= ~0x80;
        }

        if (Attr(pageSetUpPr, "fitToPage", false))
        {
            wsBool |= 0x100;
        }

        U16Record(0x0081, wsBool);
        WritePageBreaks(root);
        WritePageSetup(root);
        WriteSheetProtection(root);

        // COLUMNS
        int defColWidth = AttrInt(fmt, "baseColWidth", 8);
        int defColWidthPosition = _s.Position;
        U16Record(0x0055, defColWidth);
        WriteColumns(ws);
        WriteAutoFilterInfo(ws, root);

        var dim = new BiffBuffer();
        dim.U32((uint)(maxRow < 0 ? 0 : minRow));
        dim.U32((uint)(maxRow < 0 ? 0 : maxRow + 1));
        dim.U16(maxRow < 0 ? 0 : minCol);
        dim.U16(maxRow < 0 ? 0 : maxCol + 1);
        dim.U16(0);
        _s.Record(0x0200, dim);

        // CELLTABLE
        var dbCells = WriteCellTable(si, ws, rows);
        for (int i = 0; i < dbCells.Count; i++)
        {
            _s.PatchU32(indexPosition + 4 + 16 + 4 * i, (uint)dbCells[i]);
        }

        _s.PatchU32(indexPosition + 4 + 12, (uint)defColWidthPosition);

        WriteObjects(si, ws);
        WriteWindows(si, ws, root);
        WriteMerges(ws);
        WriteConditionalFormats(ws);
        WriteHyperlinks(ws, root);
        WriteDataValidations(ws);
        ReportSheetLosses(ws, root);
        Eof();
    }

    private void WriteColumns(Worksheet ws)
    {
        long outside = 0;
        foreach (ColumnProperties c in ws.Columns.OrderBy(c => c.Min))
        {
            if (c.Min > MaxColumns)
            {
                outside++;
                continue;
            }

            int last = Math.Min(c.Max, MaxColumns);
            var b = new BiffBuffer();
            b.U16(c.Min - 1);
            b.U16(last - 1);
            b.U16((int)Math.Min(Math.Round(c.Width * 256), 65535));
            b.U16(_styles.Xf(c.Style));
            int f = ((c.Flags & ColumnOptions.Hidden) != 0 ? 1 : 0) | ((c.Flags & ColumnOptions.CustomWidth) != 0 ? 2 : 0) | ((c.Flags & ColumnOptions.BestFit) != 0 ? 4 : 0)
                | ((c.Flags & ColumnOptions.Phonetic) != 0 ? 8 : 0) | ((c.OutlineLevel & 7) << 8) | ((c.Flags & ColumnOptions.Collapsed) != 0 ? 0x1000 : 0);
            b.U16(f);
            b.U16(0);
            _s.Record(0x007D, b);
            if (c.Max > MaxColumns)
            {
                outside++;
            }
        }

        if (outside > 0)
        {
            _loss.Report("loss.cells-outside-grid", "cells outside the BIFF8 grid", outside, "Column settings right of column IV were not written.");
        }
    }

    private void WriteAutoFilterInfo(Worksheet ws, MarkupNode root)
    {
        if (AutoFilterRange(ws) is not { } r)
        {
            return;
        }

        var b = new BiffBuffer();
        b.U16(r.ColumnCount);
        _s.Record(0x009D, b);
        MarkupNode af = Child(root, "autoFilter");
        long criteria = 0;
        foreach (MarkupNode c in af.ChildElements)
        {
            if (c.LocalNameString == "filterColumn")
            {
                criteria++;
            }
        }

        if (criteria > 0)
        {
            _loss.Report("loss.autofilter-criteria", "autofilter criteria", criteria, "Filter criteria are not written; the filter range is.");
        }
    }

    // ---- the cell table ----

    private List<int> WriteCellTable(int si, Worksheet ws, List<(int Row, CellRowView View)> rows)
    {
        var dbCells = new List<int>();
        Dictionary<long, Encoded> formulas = _formulas[si];
        var arrays = new List<(ArrayInfo Area, int AnchorRow, int AnchorCol)>();
        var tables = new List<TableInfo>();
        foreach ((int _, CellRowView v) in rows)
        {
            for (int i = 0; i < v.Count; i++)
            {
                CellFormula? f = v.FormulaAt(i);
                if (f is null || f.Reference is null)
                {
                    continue;
                }

                if (f.Kind == CellFormulaKind.Array && TryRange(f.Reference, out int r1, out int c1, out int r2, out int c2) && c2 < MaxColumns && r2 < MaxRows)
                {
                    arrays.Add((new ArrayInfo(r1, c1, r2, c2), v.Row - 1, v.ColumnAt(i) - 1));
                }
                else if (f.Kind == CellFormulaKind.DataTable && TryRange(f.Reference, out r1, out c1, out r2, out c2) && c2 < MaxColumns && r2 < MaxRows)
                {
                    tables.Add(ReadTable(f, r1, c1, r2, c2));
                }
            }
        }

        var cells = new BiffSink();
        for (int start = 0; start < rows.Count; start += 32)
        {
            _token.ThrowIfCancellationRequested();
            int end = Math.Min(start + 32, rows.Count);
            var rowStarts = new List<int>();
            var rowHasCells = new List<bool>();
            var rowRecords = new List<byte[]>();
            cells.Reset();
            for (int k = start; k < end; k++)
            {
                (int row0, CellRowView view) = rows[k];
                rowStarts.Add(cells.Position);
                int first = int.MaxValue;
                int last = -1;
                bool any = false;
                for (int i = 0; i < view.Count; i++)
                {
                    int col0 = view.ColumnAt(i) - 1;
                    if (col0 >= MaxColumns)
                    {
                        continue;
                    }

                    if (WriteCell(cells, si, ws, view, i, row0, col0, formulas, arrays, tables))
                    {
                        any = true;
                        first = Math.Min(first, col0);
                        last = Math.Max(last, col0);
                    }
                }

                rowHasCells.Add(any);
                rowRecords.Add(RowRecord(ws, view.Row, any ? first : 0, any ? last + 1 : 0));
            }

            int firstRowPosition = _s.Position;
            int endOfFirstRow = firstRowPosition + 4 + rowRecords[0].Length;
            foreach (byte[] rr in rowRecords)
            {
                _s.Exact(0x0208, rr);
            }

            int cellBase = _s.Position;
            _s.AppendRaw(cells.ToArray());
            int dbPosition = _s.Position;
            var db = new BiffBuffer();
            db.U32((uint)(dbPosition - firstRowPosition));
            int previous = -1;
            for (int k = 0; k < rowStarts.Count; k++)
            {
                if (!rowHasCells[k])
                {
                    continue;
                }

                int at = cellBase + rowStarts[k];
                db.U16(previous < 0 ? at - endOfFirstRow : at - previous);
                previous = at;
            }

            _s.Record(0x00D7, db);
            dbCells.Add(dbPosition);
        }

        return dbCells;
    }

    private static TableInfo ReadTable(CellFormula f, int r1, int c1, int r2, int c2)
    {
        string a = f.RawAttributes ?? string.Empty;
        string? Get(string name)
        {
            int i = a.IndexOf(" " + name + "=\"", StringComparison.Ordinal);
            if (i < 0)
            {
                return null;
            }

            int s = i + name.Length + 3;
            int e = a.IndexOf('"', s);
            return e < 0 ? null : a[s..e];
        }

        bool twoD = Get("dt2D") is "1" or "true";
        bool rowInput = Get("dtr") is "1" or "true";
        int ri = 0;
        int ci = 0;
        int ri2 = 0;
        int ci2 = 0;
        if (Get("r1") is { } x && CellAddress.TryParse(x, out CellAddress a1))
        {
            ri = a1.Row - 1;
            ci = a1.Column - 1;
        }

        if (Get("r2") is { } y && CellAddress.TryParse(y, out CellAddress a2))
        {
            ri2 = a2.Row - 1;
            ci2 = a2.Column - 1;
        }

        // [MS-XLS] §2.4.319 Table flags: bit 2 fRw (row input), bit 3 fTbl2 (two inputs).
        return new TableInfo(r1, c1, r2, c2, (rowInput ? 4 : 0) | (twoD ? 8 : 0), ri, ci, ri2, ci2);
    }

    private byte[] RowRecord(Worksheet ws, int row1, int colMic, int colMac)
    {
        RowProperties p = ws.GetRow(row1);
        var b = new BiffBuffer();
        b.U16(row1 - 1);
        b.U16(colMic);
        b.U16(colMac);
        double h = p.Height;
        bool custom = h >= 0;
        b.U16(custom ? (int)Math.Round(h * 20) & 0x7FFF : 0x00FF);
        b.U32(0);
        int xf = p.StyleIndex;
        int flags = (p.OutlineLevel & 7) | (p.Collapsed ? 0x10 : 0) | (p.Hidden ? 0x20 : 0) | (custom ? 0x40 : 0) | (xf != 0 ? 0x80 : 0);
        b.U8(flags);
        b.U8(0x01);
        b.U16(xf != 0 ? _styles.Xf(xf) : 0);
        return b.Span.ToArray();
    }

    private bool WriteCell(BiffSink s, int si, Worksheet ws, CellRowView view, int i, int row0, int col0, Dictionary<long, Encoded> formulas,
        List<(ArrayInfo Area, int AnchorRow, int AnchorCol)> arrays, List<TableInfo> tables)
    {
        CellValue v = view.ValueAt(i);
        int xf = _styles.Xf(view.StyleAt(i));
        CellFormula? f = view.FormulaAt(i);
        var b = new BiffBuffer();
        long key = Key(row0, col0);

        // Cells that belong to an array or a data table are FORMULA records that point at the anchor ([MS-XLS] §2.5.198.58 PtgExp).
        (ArrayInfo Area, int AnchorRow, int AnchorCol)? member = null;
        foreach (var a in arrays)
        {
            if (row0 >= a.Area.Row1 && row0 <= a.Area.Row2 && col0 >= a.Area.Col1 && col0 <= a.Area.Col2 && !(row0 == a.AnchorRow && col0 == a.AnchorCol))
            {
                member = a;
                break;
            }
        }

        TableInfo? table = null;
        foreach (TableInfo t in tables)
        {
            if (row0 >= t.Row1 && row0 <= t.Row2 && col0 >= t.Col1 && col0 <= t.Col2)
            {
                table = t;
                break;
            }
        }

        if (table is { } tb)
        {
            byte[] rgce = [0x02, (byte)tb.Row1, (byte)(tb.Row1 >> 8), (byte)tb.Col1, (byte)(tb.Col1 >> 8)];
            WriteFormulaRecord(s, ws, row0, col0, xf, v, rgce, []);
            if (row0 == tb.Row1 && col0 == tb.Col1)
            {
                var t = new BiffBuffer();
                t.U16(tb.Row1);
                t.U16(tb.Row2);
                t.U8(tb.Col1);
                t.U8(tb.Col2);
                t.U16(tb.Flags);
                t.U16(tb.RowIn);
                t.U16(tb.ColIn);
                t.U16(tb.RowIn2);
                t.U16(tb.ColIn2);
                s.Record(0x0236, t);
            }

            return true;
        }

        if (member is { } m)
        {
            byte[] rgce = [0x01, (byte)m.AnchorRow, (byte)(m.AnchorRow >> 8), (byte)m.AnchorCol, (byte)(m.AnchorCol >> 8)];
            WriteFormulaRecord(s, ws, row0, col0, xf, v, rgce, []);
            return true;
        }

        if (f is not null && formulas.TryGetValue(key, out Encoded? e))
        {
            if (f.Kind == CellFormulaKind.Array && f.Reference is not null && TryRange(f.Reference, out int r1, out int c1, out int r2, out int c2) && c2 < MaxColumns && r2 < MaxRows)
            {
                byte[] exp = [0x01, (byte)row0, (byte)(row0 >> 8), (byte)col0, (byte)(col0 >> 8)];
                WriteFormulaRecord(s, ws, row0, col0, xf, v, exp, []);
                var a = new BiffBuffer();
                a.U16(r1);
                a.U16(r2);
                a.U8(c1);
                a.U8(c2);
                a.U16(0);
                a.U32(0);
                a.U16(e.Rgce.Length);
                a.Bytes(e.Rgce);
                a.Bytes(e.Rgcb);
                s.Record(0x0221, a);
                return true;
            }

            WriteFormulaRecord(s, ws, row0, col0, xf, v, e.Rgce, e.Rgcb);
            return true;
        }

        // A plain value (or a formula BIFF8 cannot hold, which keeps its cached value).
        switch (v.Kind)
        {
            case CellValueKind.Empty:
                if (xf == _styles.Xf(0) && f is null)
                {
                    return false;
                }

                b.U16(row0);
                b.U16(col0);
                b.U16(xf);
                s.Record(0x0201, b);
                return true;
            case CellValueKind.Number:
                if (double.IsNaN(v.Number) || double.IsInfinity(v.Number))
                {
                    WriteBoolErr(s, b, row0, col0, xf, 0x24, true);
                    return true;
                }

                if (TryRk(v.Number, out uint rk))
                {
                    b.U16(row0);
                    b.U16(col0);
                    b.U16(xf);
                    b.U32(rk);
                    s.Record(0x027E, b);
                }
                else
                {
                    b.U16(row0);
                    b.U16(col0);
                    b.U16(xf);
                    b.F64(v.Number);
                    s.Record(0x0203, b);
                }

                return true;
            case CellValueKind.Boolean:
                WriteBoolErr(s, b, row0, col0, xf, v.Boolean ? 1 : 0, false);
                return true;
            case CellValueKind.Error:
                WriteBoolErr(s, b, row0, col0, xf, BiffErrorCode(v.Error), true);
                return true;
            default:
                string t = ws.GetText(v);
                if (t.Length > 32767)
                {
                    t = t[..32767];
                }

                b.U16(row0);
                b.U16(col0);
                b.U16(xf);
                b.U32((uint)(_sst.TryGetValue(t, out int idx) ? idx : 0));
                s.Record(0x00FD, b);
                return true;
        }
    }

    private static void WriteBoolErr(BiffSink s, BiffBuffer b, int row, int col, int xf, int value, bool isError)
    {
        b.U16(row);
        b.U16(col);
        b.U16(xf);
        b.U8(value);
        b.U8(isError ? 1 : 0);
        s.Record(0x0205, b);
    }

    // [MS-XLS] §2.4.127 Formula: the cached result is a double, or a tagged value with 0xFFFF in its last two bytes.
    private void WriteFormulaRecord(BiffSink s, Worksheet ws, int row0, int col0, int xf, CellValue v, byte[] rgce, byte[] rgcb)
    {
        var b = new BiffBuffer();
        b.U16(row0);
        b.U16(col0);
        b.U16(xf);
        string? text = null;
        switch (v.Kind)
        {
            case CellValueKind.Number when !double.IsNaN(v.Number) && !double.IsInfinity(v.Number):
                b.F64(v.Number);
                break;
            case CellValueKind.Number:
                b.Bytes([2, 0, 0x24, 0, 0, 0, 0xFF, 0xFF]);
                break;
            case CellValueKind.Boolean:
                b.Bytes([1, 0, (byte)(v.Boolean ? 1 : 0), 0, 0, 0, 0xFF, 0xFF]);
                break;
            case CellValueKind.Error:
                b.Bytes([2, 0, (byte)BiffErrorCode(v.Error), 0, 0, 0, 0xFF, 0xFF]);
                break;
            case CellValueKind.Text:
                text = ws.GetText(v);
                if (text.Length > 32767)
                {
                    text = text[..32767];
                }

                b.Bytes([0, 0, 0, 0, 0, 0, 0xFF, 0xFF]);
                break;
            default:
                b.Bytes([3, 0, 0, 0, 0, 0, 0xFF, 0xFF]);
                break;
        }

        b.U16(0);
        b.U32(0);
        b.U16(rgce.Length);
        b.Bytes(rgce);
        b.Bytes(rgcb);
        s.Record(0x0006, b);
        if (text is not null)
        {
            var st = new BiffBuffer();
            st.String16(text);
            s.Record(0x0207, st);
        }
    }
}
