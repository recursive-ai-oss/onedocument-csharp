using System.Buffers;
using System.Globalization;
using System.Text;
using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.Markup;
using RecursiveAI.OneOffice.Markup.Xml;

namespace RecursiveAI.OneOffice.Spreadsheets;

/// <summary>
/// What a worksheet part holds besides the cells: the bytes before and after <c>sheetData</c>, kept as they were. The elements around
/// the cells (views, columns, merges, validations, extensions, anything unknown) stay in order and are only edited through the tree
/// built from them.
/// </summary>
internal sealed class SheetFrame
{
    public byte[] Head = [];
    public byte[] Tail = [];

    /// <summary>The start tag of <c>sheetData</c> as written, or empty when the part had none.</summary>
    public byte[] SheetDataTag = [];

    public bool SheetDataSelfClosed;
    public bool HadSheetData;
    public byte[] Prefix = [];
}

/// <summary>Reads a worksheet part (ECMA-376 Part 1 CT_Worksheet) into a <see cref="CellStore"/>, tolerantly, reporting every repair.</summary>
internal static class SheetReader
{
    private const int MaxCellsPerRow = 1 << 20;

    private sealed class Scratch
    {
        public ushort[] Cols = new ushort[64];
        public byte[] Kinds = new byte[64];
        public long[] Data = new long[64];
        public int[] Styles = new int[64];
        public int[] Formulas = new int[64];
        public int Count;
        public bool AnyFormula;
        public bool Sorted = true;
        public byte[] ValueText = new byte[256];
        public int ValueLength;
        public byte[] FormulaText = new byte[256];

        public void Reset()
        {
            Count = 0;
            AnyFormula = false;
            Sorted = true;
        }

        public int Add(int col0)
        {
            if (Count == Cols.Length)
            {
                int cap = Count * 2;
                Array.Resize(ref Cols, cap);
                Array.Resize(ref Kinds, cap);
                Array.Resize(ref Data, cap);
                Array.Resize(ref Styles, cap);
                Array.Resize(ref Formulas, cap);
            }

            if (Count > 0 && Cols[Count - 1] >= col0)
            {
                Sorted = false;
            }

            Cols[Count] = (ushort)col0;
            Kinds[Count] = 0;
            Data[Count] = 0;
            Styles[Count] = 0;
            Formulas[Count] = 0;
            return Count++;
        }
    }

    /// <summary>Reads the part.</summary>
    /// <exception cref="OfficeFormatException">The part is not well-formed XML or its root is not a worksheet.</exception>
    /// <exception cref="OfficeLimitException">A limit was exceeded.</exception>
    public static SheetFrame Read(Stream input, CellStore store, OpenContext context, string partName, string sheetName)
    {
        var frame = new SheetFrame();
        var head = new ArrayBufferWriter<byte>(512);
        var tail = new ArrayBufferWriter<byte>(512);
        var scratch = new Scratch();
        using var reader = new MarkupReader(input, context, partName, leaveOpen: true);
        byte[] rootPrefix = [];
        int state = 0; // 0 head, 1 inside sheetData, 2 tail
        int lastRow = 0;
        var cache = new ExtraCache();
        while (reader.Read())
        {
            if (state == 0)
            {
                if (reader.Depth == 0 && reader.Kind is XmlTokenKind.StartElement or XmlTokenKind.EmptyElement)
                {
                    if (!reader.LocalName.SequenceEqual("worksheet"u8))
                    {
                        throw new OfficeFormatException(DefectKind.Structure, "xlsx.wrong-root", "The sheet part does not start with a worksheet element.", DefectLocation.InPart(partName, reader.TokenOffset));
                    }

                    rootPrefix = reader.Prefix.ToArray();
                }

                if (reader.Depth == 1 && reader.Kind is XmlTokenKind.StartElement or XmlTokenKind.EmptyElement && reader.LocalName.SequenceEqual("sheetData"u8) && reader.Prefix.SequenceEqual(rootPrefix))
                {
                    frame.HadSheetData = true;
                    frame.SheetDataTag = reader.Raw.ToArray();
                    frame.Prefix = reader.Prefix.ToArray();
                    if (reader.Kind == XmlTokenKind.EmptyElement)
                    {
                        frame.SheetDataSelfClosed = true;
                        state = 2;
                    }
                    else
                    {
                        state = 1;
                    }

                    continue;
                }

                if (reader.Kind == XmlTokenKind.XmlDeclaration && reader.Raw.IndexOf("16"u8) >= 0 && IsUtf16Declaration(reader.Raw))
                {
                    XString.Raw(head, "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"u8);
                    continue;
                }

                XString.Raw(head, reader.Raw);
                continue;
            }

            if (state == 2)
            {
                XString.Raw(tail, reader.Raw);
                continue;
            }

            // Inside sheetData.
            if (reader.Kind == XmlTokenKind.EndElement && reader.Depth == 1)
            {
                state = 2;
                continue;
            }

            if (reader.Depth != 2 || reader.Kind is not (XmlTokenKind.StartElement or XmlTokenKind.EmptyElement))
            {
                continue;
            }

            if (!reader.LocalName.SequenceEqual("row"u8) || !reader.Prefix.SequenceEqual(rootPrefix))
            {
                // Not a row: not valid here; drop it with a report, nothing can hold it.
                context.Defects.Report(new Defect(DefectKind.Structure, "xlsx.sheetdata-unexpected-element", DefectSeverity.Dropped,
                    "An element other than a row inside sheetData was dropped.", DefectLocation.InPart(partName, reader.TokenOffset)));
                reader.Skip();
                continue;
            }

            ReadRow(reader, store, context, scratch, partName, sheetName, rootPrefix, ref lastRow, cache);
        }

        frame.Head = head.WrittenSpan.ToArray();
        frame.Tail = tail.WrittenSpan.ToArray();
        return frame;
    }

    private static bool IsUtf16Declaration(ReadOnlySpan<byte> decl)
    {
        string s = Encoding.ASCII.GetString(decl);
        return s.Contains("utf-16", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Remembers the last raw attribute string so rows that share one (for example a row-height hint) share the string.</summary>
    private sealed class ExtraCache
    {
        public byte[]? LastBytes;
        public string? LastString;

        public string Get(ReadOnlySpan<byte> bytes)
        {
            if (LastBytes is not null && bytes.SequenceEqual(LastBytes))
            {
                return LastString!;
            }

            LastBytes = bytes.ToArray();
            LastString = Encoding.UTF8.GetString(bytes);
            return LastString;
        }
    }

    private static bool ParseBool(ReadOnlySpan<byte> v) => v.SequenceEqual("1"u8) || v.SequenceEqual("true"u8) || v.SequenceEqual("True"u8) || v.SequenceEqual("TRUE"u8);

    private static bool TryInt(ReadOnlySpan<byte> v, out int value) => int.TryParse(v, NumberStyles.AllowLeadingSign | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, CultureInfo.InvariantCulture, out value);

    private static void ReadRow(MarkupReader reader, CellStore store, OpenContext context, Scratch scratch, string partName, string sheetName, byte[] rootPrefix, ref int lastRow, ExtraCache cache)
    {
        bool empty = reader.Kind == XmlTokenKind.EmptyElement;
        long rowOffset = reader.TokenOffset;
        int row = 0;
        double height = -1;
        RowFlags flags = RowFlags.None;
        int outline = 0;
        int style = 0;
        string? extra = null;
        ArrayBufferWriter<byte>? extraAttrs = null;
        foreach (XmlAttributeToken a in reader.Attributes)
        {
            if (a.Prefix.Length == 0)
            {
                ReadOnlySpan<byte> n = a.LocalName;
                ReadOnlySpan<byte> v = a.RawValue;
                if (n.SequenceEqual("r"u8))
                {
                    if (!TryInt(v, out row) || row is < 1 or > CellAddress.MaxRows)
                    {
                        context.Defects.Report(new Defect(DefectKind.Reference, "xlsx.row-index-invalid", DefectSeverity.Repaired,
                            "A row has an invalid r attribute; it takes the number after the previous row.", DefectLocation.InPart(partName, rowOffset)));
                        row = 0;
                    }

                    continue;
                }

                if (n.SequenceEqual("spans"u8))
                {
                    continue; // an optimisation hint derived from the cells; rebuilt on write
                }

                if (n.SequenceEqual("ht"u8))
                {
                    if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double h) && h >= 0)
                    {
                        height = h;
                    }

                    continue;
                }

                if (n.SequenceEqual("s"u8))
                {
                    _ = TryInt(v, out style);
                    continue;
                }

                if (n.SequenceEqual("outlineLevel"u8))
                {
                    _ = TryInt(v, out outline);
                    outline = Math.Clamp(outline, 0, 7);
                    continue;
                }

                RowFlags f = n.SequenceEqual("hidden"u8) ? RowFlags.Hidden
                    : n.SequenceEqual("customHeight"u8) ? RowFlags.CustomHeight
                    : n.SequenceEqual("customFormat"u8) ? RowFlags.CustomFormat
                    : n.SequenceEqual("collapsed"u8) ? RowFlags.Collapsed
                    : n.SequenceEqual("thickTop"u8) ? RowFlags.ThickTop
                    : n.SequenceEqual("thickBot"u8) ? RowFlags.ThickBottom
                    : RowFlags.None;
                if (f != RowFlags.None)
                {
                    if (ParseBool(v))
                    {
                        flags |= f;
                    }

                    continue;
                }
            }

            extraAttrs ??= new ArrayBufferWriter<byte>(64);
            AppendRawAttribute(extraAttrs, a);
        }

        if (extraAttrs is not null)
        {
            extra = cache.Get(extraAttrs.WrittenSpan);
        }

        if (row == 0)
        {
            row = lastRow + 1;
        }

        if (row > CellAddress.MaxRows)
        {
            throw new OfficeFormatException(DefectKind.Reference, "xlsx.row-index-invalid", "A row lies beyond the last row of a sheet.", DefectLocation.InPart(partName, rowOffset));
        }

        scratch.Reset();
        ArrayBufferWriter<byte>? rowChildren = null;
        int lastCol = 0;
        if (!empty)
        {
            while (reader.Read())
            {
                if (reader.Kind == XmlTokenKind.EndElement && reader.Depth == 2)
                {
                    break;
                }

                if (reader.Depth != 3 || reader.Kind is not (XmlTokenKind.StartElement or XmlTokenKind.EmptyElement))
                {
                    continue;
                }

                if (reader.LocalName.SequenceEqual("c"u8) && reader.Prefix.SequenceEqual(rootPrefix))
                {
                    ReadCell(reader, store, context, scratch, partName, sheetName, rootPrefix, row, ref lastCol, cache);
                }
                else
                {
                    rowChildren ??= new ArrayBufferWriter<byte>(128);
                    Capture(reader, rowChildren);
                }
            }
        }

        lastRow = row;
        RowData data = FinishRow(store, context, scratch, row, partName, sheetName, rowOffset);
        data.Height = height;
        data.Flags = flags;
        data.OutlineLevel = (byte)outline;
        data.Style = style;
        data.ExtraAttributes = extra;
        if (rowChildren is not null)
        {
            data.ExtraChildren = rowChildren.WrittenSpan.ToArray();
        }

        // Rows normally ascend. A row that does not is merged into the existing one.
        if (store.Rows.Count == 0 || store.Rows[^1].Row < row)
        {
            store.AppendRow(data);
        }
        else
        {
            context.Defects.Report(new Defect(DefectKind.Structure, "xlsx.row-out-of-order", DefectSeverity.Repaired,
                "Rows are not in ascending order; the row was merged into the row with the same number.", DefectLocation.InPart(partName, rowOffset)));
            MergeRow(store, data);
        }

        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxRecordCount), context.Limits.MaxRecordCount, store.CellCount, DefectLocation.InPart(partName, rowOffset));
    }

    private static void MergeRow(CellStore store, RowData data)
    {
        int idx = store.FindRowIndex(data.Row);
        if (idx < 0)
        {
            store.InsertLoadedRow(data);
            return;
        }

        RowData existing = store.Rows[idx];
        // Cells of the later row win over equal columns of the earlier one.
        var merged = new RowData(data.Row)
        {
            Height = data.Height >= 0 ? data.Height : existing.Height,
            Flags = existing.Flags | data.Flags,
            OutlineLevel = data.OutlineLevel != 0 ? data.OutlineLevel : existing.OutlineLevel,
            Style = data.Style != 0 ? data.Style : existing.Style,
            ExtraAttributes = data.ExtraAttributes ?? existing.ExtraAttributes,
            ExtraChildren = data.ExtraChildren ?? existing.ExtraChildren,
        };
        int n = existing.Count + data.Count;
        merged.Cols = new ushort[n];
        merged.Kinds = new byte[n];
        merged.Data = new long[n];
        merged.Styles = new int[n];
        int[] f = new int[n];
        bool anyF = false;
        int i = 0, j = 0, k = 0;
        while (i < existing.Count || j < data.Count)
        {
            bool takeNew = j < data.Count && (i >= existing.Count || data.Cols[j] <= existing.Cols[i]);
            if (takeNew)
            {
                if (i < existing.Count && data.Cols[j] == existing.Cols[i])
                {
                    i++;
                }

                merged.Cols[k] = data.Cols[j];
                merged.Kinds[k] = data.Kinds[j];
                merged.Data[k] = data.Data[j];
                merged.Styles[k] = data.Styles[j];
                f[k] = data.Formulas is null ? 0 : data.Formulas[j];
                j++;
            }
            else
            {
                merged.Cols[k] = existing.Cols[i];
                merged.Kinds[k] = existing.Kinds[i];
                merged.Data[k] = existing.Data[i];
                merged.Styles[k] = existing.Styles[i];
                f[k] = existing.Formulas is null ? 0 : existing.Formulas[i];
                i++;
            }

            anyF |= f[k] != 0;
            k++;
        }

        merged.Count = k;
        if (anyF)
        {
            merged.Formulas = f;
        }

        store.ReplaceRow(existing, merged);
    }

    private static RowData FinishRow(CellStore store, OpenContext context, Scratch s, int row, string partName, string sheetName, long rowOffset)
    {
        var data = new RowData(row);
        if (!s.Sorted)
        {
            SortAndDedupe(s, context, partName, sheetName, row);
        }

        int n = s.Count;
        data.Count = n;
        data.Cols = s.Cols.AsSpan(0, n).ToArray();
        data.Kinds = s.Kinds.AsSpan(0, n).ToArray();
        data.Data = s.Data.AsSpan(0, n).ToArray();
        data.Styles = s.Styles.AsSpan(0, n).ToArray();
        if (s.AnyFormula)
        {
            data.Formulas = s.Formulas.AsSpan(0, n).ToArray();
        }

        _ = store;
        _ = rowOffset;
        return data;
    }

    private static void SortAndDedupe(Scratch s, OpenContext context, string partName, string sheetName, int row)
    {
        int n = s.Count;
        int[] order = new int[n];
        for (int i = 0; i < n; i++)
        {
            order[i] = i;
        }

        ushort[] cols = s.Cols;
        Array.Sort(order, (a, b) => cols[a] != cols[b] ? cols[a].CompareTo(cols[b]) : a.CompareTo(b));
        context.Defects.Report(new Defect(DefectKind.Structure, "xlsx.cell-out-of-order", DefectSeverity.Repaired,
            "Cells of a row are not in ascending column order; they were sorted.", DefectLocation.InCell(sheetName, new CellAddress(row, cols[order[0]] + 1).ToString())));
        var c2 = new ushort[n];
        var k2 = new byte[n];
        var d2 = new long[n];
        var s2 = new int[n];
        var f2 = new int[n];
        int m = 0;
        for (int i = 0; i < n; i++)
        {
            int src = order[i];
            if (m > 0 && c2[m - 1] == cols[src])
            {
                m--; // a later duplicate replaces the earlier cell
                context.Defects.Report(new Defect(DefectKind.Structure, "xlsx.cell-duplicate", DefectSeverity.Dropped,
                    "Two cells have the same address; the later one is kept.", DefectLocation.InCell(sheetName, new CellAddress(row, cols[src] + 1).ToString())));
            }

            c2[m] = cols[src];
            k2[m] = s.Kinds[src];
            d2[m] = s.Data[src];
            s2[m] = s.Styles[src];
            f2[m] = s.Formulas[src];
            m++;
        }

        Array.Copy(c2, s.Cols, m);
        Array.Copy(k2, s.Kinds, m);
        Array.Copy(d2, s.Data, m);
        Array.Copy(s2, s.Styles, m);
        Array.Copy(f2, s.Formulas, m);
        s.Count = m;
        s.Sorted = true;
        _ = partName;
    }

    private static void AppendRawAttribute(ArrayBufferWriter<byte> w, XmlAttributeToken a)
    {
        XString.Raw(w, " "u8);
        XString.Raw(w, a.RawName);
        XString.Raw(w, "="u8);
        byte q = a.Quote;
        XString.Raw(w, new ReadOnlySpan<byte>(in q));
        XString.Raw(w, a.RawValue);
        XString.Raw(w, new ReadOnlySpan<byte>(in q));
    }

    /// <summary>Copies the current element, with everything inside it, as written.</summary>
    private static void Capture(MarkupReader reader, ArrayBufferWriter<byte> w)
    {
        XString.Raw(w, reader.Raw);
        if (reader.Kind != XmlTokenKind.StartElement)
        {
            return;
        }

        int depth = reader.Depth;
        while (reader.Read())
        {
            XString.Raw(w, reader.Raw);
            if (reader.Kind == XmlTokenKind.EndElement && reader.Depth == depth)
            {
                return;
            }
        }
    }

    private static void ReadCell(MarkupReader reader, CellStore store, OpenContext context, Scratch s, string partName, string sheetName, byte[] rootPrefix, int row, ref int lastCol, ExtraCache cache)
    {
        bool empty = reader.Kind == XmlTokenKind.EmptyElement;
        long cellOffset = reader.TokenOffset;
        int col = 0;
        int style = 0;
        int typeCode = 0; // 0 n, 1 s, 2 b, 3 e, 4 str, 5 inlineStr, 6 d, 7 unknown
        string? unknownType = null;
        ArrayBufferWriter<byte>? extraAttrs = null;
        bool badAddress = false;
        foreach (XmlAttributeToken a in reader.Attributes)
        {
            if (a.Prefix.Length == 0)
            {
                ReadOnlySpan<byte> n = a.LocalName;
                if (n.Length == 1 && n[0] == (byte)'r')
                {
                    if (CellAddress.TryParse(a.RawValue, out CellAddress addr))
                    {
                        col = addr.Column;
                        if (addr.Row != row)
                        {
                            context.Defects.Report(new Defect(DefectKind.Reference, "xlsx.cell-row-mismatch", DefectSeverity.Repaired,
                                "A cell's address names a different row than its row element; it stays in the row element's row.", DefectLocation.InCell(sheetName, addr.ToString())));
                        }
                    }
                    else
                    {
                        badAddress = true;
                    }

                    continue;
                }

                if (n.Length == 1 && n[0] == (byte)'s')
                {
                    _ = TryInt(a.RawValue, out style);
                    continue;
                }

                if (n.Length == 1 && n[0] == (byte)'t')
                {
                    typeCode = TypeCode(a.RawValue);
                    if (typeCode == 7)
                    {
                        unknownType = Encoding.UTF8.GetString(a.RawValue);
                    }

                    continue;
                }
            }

            extraAttrs ??= new ArrayBufferWriter<byte>(32);
            AppendRawAttribute(extraAttrs, a);
        }

        if (badAddress)
        {
            context.Defects.Report(new Defect(DefectKind.Reference, "xlsx.cell-address-invalid", DefectSeverity.Dropped,
                "A cell has an address outside the sheet or not in A1 form; the cell was dropped.", DefectLocation.InPart(partName, cellOffset)));
            if (!empty)
            {
                reader.Skip();
            }

            return;
        }

        if (col == 0)
        {
            col = lastCol + 1;
        }

        if (col > CellAddress.MaxColumns)
        {
            context.Defects.Report(new Defect(DefectKind.Reference, "xlsx.cell-address-invalid", DefectSeverity.Dropped,
                "A cell lies beyond the last column; the cell was dropped.", DefectLocation.InPart(partName, cellOffset)));
            if (!empty)
            {
                reader.Skip();
            }

            return;
        }

        lastCol = col;
        OfficeLimitException.ThrowIfExceeded("MaxRecordCount", MaxCellsPerRow, s.Count + 1L, DefectLocation.InPart(partName, cellOffset));
        int at = s.Add(col - 1);
        s.Styles[at] = style;

        CellFormula? formula = null;
        bool haveV = false;
        double number = 0;
        string? vText = null;
        ArrayBufferWriter<byte>? children = null;
        byte[]? inlineRaw = null;
        string? inlineText = null;
        s.ValueLength = 0;

        if (!empty)
        {
            while (reader.Read())
            {
                if (reader.Kind == XmlTokenKind.EndElement && reader.Depth == 3)
                {
                    break;
                }

                if (reader.Depth != 4 || reader.Kind is not (XmlTokenKind.StartElement or XmlTokenKind.EmptyElement))
                {
                    continue;
                }

                bool main = reader.Prefix.SequenceEqual(rootPrefix);
                ReadOnlySpan<byte> ln = reader.LocalName;
                if (main && ln.SequenceEqual("v"u8))
                {
                    haveV = true;
                    if (reader.Kind == XmlTokenKind.StartElement)
                    {
                        s.ValueLength = ReadTextInto(reader, ref s.ValueText);
                    }

                    continue;
                }

                if (main && ln.SequenceEqual("f"u8))
                {
                    formula = ReadFormula(reader, context, s, sheetName, new CellAddress(row, col));
                    continue;
                }

                if (main && ln.SequenceEqual("is"u8))
                {
                    var cap = new ArrayBufferWriter<byte>(64);
                    Capture(reader, cap);
                    ReadOnlySpan<byte> whole = cap.WrittenSpan;
                    int open = whole.IndexOf((byte)'>');
                    int close = whole.LastIndexOf("</"u8);
                    ReadOnlySpan<byte> inner = open >= 0 && close > open ? whole[(open + 1)..close] : default;
                    inlineText = XString.Decode(XString.ExtractText(inner));
                    inlineRaw = XString.HasRuns(inner) ? inner.ToArray() : null;
                    continue;
                }

                children ??= new ArrayBufferWriter<byte>(64);
                Capture(reader, children);
            }
        }

        if (extraAttrs is not null || children is not null)
        {
            store.SetExtras(row, col, new CellExtras { Attributes = extraAttrs is null ? null : cache.Get(extraAttrs.WrittenSpan), Children = children?.WrittenSpan.ToArray() });
        }

        // Decode the value by type: ECMA-376 Part 1 ST_CellType.
        byte kind = 0;
        long data = 0;
        bool known = true;
        ReadOnlySpan<byte> vSpan = s.ValueText.AsSpan(0, s.ValueLength);
        if (typeCode == 0)
        {
            if (haveV && !vSpan.IsEmpty)
            {
                if (double.TryParse(vSpan, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                {
                    kind = (byte)CellValueKind.Number;
                    data = BitConverter.DoubleToInt64Bits(number);
                }
                else
                {
                    known = false;
                }
            }
        }
        else if (typeCode == 1)
        {
            if (haveV && TryInt(vSpan, out int idx) && idx >= 0)
            {
                kind = (byte)CellValueKind.Text;
                data = idx;
            }
            else if (haveV)
            {
                known = false;
            }
        }
        else if (typeCode == 2)
        {
            if (haveV)
            {
                kind = (byte)CellValueKind.Boolean;
                data = ParseBool(vSpan) ? 1 : 0;
            }
        }
        else if (typeCode == 3)
        {
            if (haveV)
            {
                if (CellErrors.TryParse(vSpan, out CellError err))
                {
                    kind = (byte)CellValueKind.Error;
                    data = (int)err;
                }
                else
                {
                    kind = (byte)CellValueKind.Error;
                    data = store.AddPoolString(Encoding.UTF8.GetString(vSpan));
                }
            }
        }
        else if (typeCode == 4)
        {
            if (haveV)
            {
                vText = XString.Decode(Encoding.UTF8.GetString(vSpan));
                kind = (byte)CellValueKind.Text;
                data = store.AddPoolString(vText);
            }
        }
        else if (typeCode == 5)
        {
            if (inlineText is not null)
            {
                kind = (byte)CellValueKind.Text;
                data = store.AddPoolString(inlineText, inlineRaw);
            }
            else if (haveV)
            {
                kind = (byte)CellValueKind.Text;
                data = store.AddPoolString(XString.Decode(Encoding.UTF8.GetString(vSpan)));
            }
        }
        else if (typeCode == 6)
        {
            if (haveV)
            {
                kind = (byte)((byte)CellValueKind.Text | RowData.IsoDateBit);
                data = store.AddPoolString(Encoding.UTF8.GetString(vSpan).Trim());
            }
        }
        else
        {
            context.Defects.Report(new Defect(DefectKind.Value, "xlsx.cell-type-unknown", DefectSeverity.Repaired,
                "A cell has an unknown t attribute '" + unknownType + "'; its value is read as a number if it parses, otherwise text.", DefectLocation.InCell(sheetName, new CellAddress(row, col).ToString())));
            if (haveV && double.TryParse(vSpan, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            {
                kind = (byte)CellValueKind.Number;
                data = BitConverter.DoubleToInt64Bits(number);
            }
            else if (haveV)
            {
                kind = (byte)CellValueKind.Text;
                data = store.AddPoolString(Encoding.UTF8.GetString(vSpan));
            }
        }

        if (!known)
        {
            context.Defects.Report(new Defect(DefectKind.Value, "xlsx.cell-value-invalid", DefectSeverity.Repaired,
                "A cell's value does not match its type; the cell is read as empty.", DefectLocation.InCell(sheetName, new CellAddress(row, col).ToString())));
        }

        if (formula is not null)
        {
            kind |= RowData.HasFormulaBit;
            s.Formulas[at] = store.InternFormula(formula);
            s.AnyFormula = true;
        }

        s.Kinds[at] = kind;
        s.Data[at] = data;
    }

    private static int TypeCode(ReadOnlySpan<byte> t) =>
        t.SequenceEqual("n"u8) ? 0 : t.SequenceEqual("s"u8) ? 1 : t.SequenceEqual("b"u8) ? 2 : t.SequenceEqual("e"u8) ? 3
        : t.SequenceEqual("str"u8) ? 4 : t.SequenceEqual("inlineStr"u8) ? 5 : t.SequenceEqual("d"u8) ? 6 : 7;

    /// <summary>Reads the text inside the current element up to its end tag into a reusable buffer, references decoded.</summary>
    private static int ReadTextInto(MarkupReader reader, ref byte[] buffer)
    {
        int depth = reader.Depth;
        int len = 0;
        while (reader.Read())
        {
            if (reader.Kind == XmlTokenKind.EndElement && reader.Depth == depth)
            {
                break;
            }

            if (reader.Kind is XmlTokenKind.Text or XmlTokenKind.Whitespace or XmlTokenKind.CData)
            {
                ReadOnlySpan<byte> piece = reader.Content;
                if (len + piece.Length > buffer.Length)
                {
                    Array.Resize(ref buffer, Math.Max(buffer.Length * 2, len + piece.Length));
                }

                if (reader.Kind == XmlTokenKind.CData || !XmlText.NeedsDecoding(piece))
                {
                    piece.CopyTo(buffer.AsSpan(len));
                    len += piece.Length;
                }
                else
                {
                    len += XmlText.Decode(piece, buffer.AsSpan(len));
                }
            }
        }

        return len;
    }

    private static CellFormula ReadFormula(MarkupReader reader, OpenContext context, Scratch scratch, string sheetName, CellAddress cell)
    {
        var kind = CellFormulaKind.Normal;
        string? reference = null;
        int si = -1;
        ArrayBufferWriter<byte>? raw = null;
        foreach (XmlAttributeToken a in reader.Attributes)
        {
            if (a.Prefix.Length == 0)
            {
                ReadOnlySpan<byte> n = a.LocalName;
                ReadOnlySpan<byte> v = a.RawValue;
                if (n.SequenceEqual("t"u8))
                {
                    if (v.SequenceEqual("shared"u8))
                    {
                        kind = CellFormulaKind.Shared;
                    }
                    else if (v.SequenceEqual("array"u8))
                    {
                        kind = CellFormulaKind.Array;
                    }
                    else if (v.SequenceEqual("dataTable"u8))
                    {
                        kind = CellFormulaKind.DataTable;
                    }
                    else if (!v.SequenceEqual("normal"u8))
                    {
                        context.Defects.Report(new Defect(DefectKind.Formula, "xlsx.formula-type-unknown", DefectSeverity.Repaired,
                            "A formula has an unknown t attribute; it is read as an ordinary formula.", DefectLocation.InCell(sheetName, cell.ToString())));
                    }

                    continue;
                }

                if (n.SequenceEqual("ref"u8))
                {
                    reference = XmlText.GetString(v, true);
                    continue;
                }

                if (n.SequenceEqual("si"u8))
                {
                    _ = TryInt(v, out si);
                    continue;
                }
            }

            raw ??= new ArrayBufferWriter<byte>(24);
            AppendRawAttribute(raw, a);
        }

        string text = string.Empty;
        if (reader.Kind == XmlTokenKind.StartElement)
        {
            int n = ReadTextInto(reader, ref scratch.FormulaText);
            text = n == 0 ? string.Empty : Encoding.UTF8.GetString(scratch.FormulaText, 0, n);
            OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxFormulaLength), context.Limits.MaxFormulaLength, text.Length, DefectLocation.InCell(sheetName, cell.ToString()));
        }

        return new CellFormula(text, kind, reference, kind == CellFormulaKind.Shared ? si : -1, raw is null ? null : Encoding.UTF8.GetString(raw.WrittenSpan).TrimStart());
    }
}
