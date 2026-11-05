using System.Buffers;
using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Markup.Xml;

namespace RecursiveAI.OneDocument.Spreadsheets;

/// <summary>An <see cref="IBufferWriter{T}"/> that hands full buffers to a stream, so a large part is written without being held.</summary>
internal sealed class StreamBufferWriter : IBufferWriter<byte>, IDisposable
{
    private readonly Stream _stream;
    private byte[] _buffer;
    private int _filled;

    public StreamBufferWriter(Stream stream, int size = 1 << 16)
    {
        _stream = stream;
        _buffer = new byte[size];
    }

    public void Advance(int count) => _filled += count;

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        Ensure(sizeHint);
        return _buffer.AsMemory(_filled);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        Ensure(sizeHint);
        return _buffer.AsSpan(_filled);
    }

    private void Ensure(int sizeHint)
    {
        int need = Math.Max(sizeHint, 1);
        if (_buffer.Length - _filled >= need)
        {
            return;
        }

        Flush();
        if (_buffer.Length < need)
        {
            _buffer = new byte[need];
        }
    }

    public void Flush()
    {
        if (_filled > 0)
        {
            _stream.Write(_buffer, 0, _filled);
            _filled = 0;
        }
    }

    public void Dispose() => Flush();
}

/// <summary>Writes the <c>sheetData</c> of a worksheet from the cell store (ECMA-376 Part 1 CT_SheetData, CT_Row, CT_Cell).</summary>
internal static class SheetWriter
{
    public static void WriteRows(IBufferWriter<byte> w, CellStore store, SharedStringTable? strings, ReadOnlySpan<byte> prefixColon)
    {
        Span<byte> addr = stackalloc byte[10]; // longest A1 reference is XFD1048576
        foreach (RowData r in store.Rows)
        {
            WriteRowStart(w, r, prefixColon);
            if (r.Count == 0 && r.ExtraChildren is null)
            {
                XString.Raw(w, "/>"u8);
                continue;
            }

            XString.Raw(w, ">"u8);
            for (int i = 0; i < r.Count; i++)
            {
                WriteCell(w, store, strings, prefixColon, r, i, addr);
            }

            if (r.ExtraChildren is not null)
            {
                XString.Raw(w, r.ExtraChildren);
            }

            XString.Raw(w, "</"u8);
            XString.Raw(w, prefixColon);
            XString.Raw(w, "row>"u8);
        }
    }

    private static void WriteRowStart(IBufferWriter<byte> w, RowData r, ReadOnlySpan<byte> pc)
    {
        XString.Raw(w, "<"u8);
        XString.Raw(w, pc);
        XString.Raw(w, "row r=\""u8);
        WriteInt(w, r.Row);
        XString.Raw(w, "\""u8);
        if (r.Style != 0 || (r.Flags & RowFlags.CustomFormat) != 0)
        {
            XString.Raw(w, " s=\""u8);
            WriteInt(w, r.Style);
            XString.Raw(w, "\""u8);
        }

        if ((r.Flags & RowFlags.CustomFormat) != 0)
        {
            XString.Raw(w, " customFormat=\"1\""u8);
        }

        if (r.Height >= 0)
        {
            XString.Raw(w, " ht=\""u8);
            WriteDouble(w, r.Height);
            XString.Raw(w, "\""u8);
        }

        if ((r.Flags & RowFlags.Hidden) != 0)
        {
            XString.Raw(w, " hidden=\"1\""u8);
        }

        if ((r.Flags & RowFlags.CustomHeight) != 0)
        {
            XString.Raw(w, " customHeight=\"1\""u8);
        }

        if (r.OutlineLevel != 0)
        {
            XString.Raw(w, " outlineLevel=\""u8);
            WriteInt(w, r.OutlineLevel);
            XString.Raw(w, "\""u8);
        }

        if ((r.Flags & RowFlags.Collapsed) != 0)
        {
            XString.Raw(w, " collapsed=\"1\""u8);
        }

        if ((r.Flags & RowFlags.ThickTop) != 0)
        {
            XString.Raw(w, " thickTop=\"1\""u8);
        }

        if ((r.Flags & RowFlags.ThickBottom) != 0)
        {
            XString.Raw(w, " thickBot=\"1\""u8);
        }

        if (r.ExtraAttributes is not null)
        {
            WriteString(w, r.ExtraAttributes);
        }
    }

    private static void WriteCell(IBufferWriter<byte> w, CellStore store, SharedStringTable? strings, ReadOnlySpan<byte> pc, RowData r, int i, Span<byte> addr)
    {
        byte kindByte = r.Kinds[i];
        var kind = (CellValueKind)(kindByte & RowData.KindMask);
        bool hasFormula = (kindByte & RowData.HasFormulaBit) != 0;
        long data = r.Data[i];
        int col = r.Cols[i] + 1;
        CellExtras? extras = store.HasExtras ? store.GetExtras(r.Row, col) : null;

        XString.Raw(w, "<"u8);
        XString.Raw(w, pc);
        XString.Raw(w, "c r=\""u8);
        int n = new CellAddress(r.Row, col).WriteUtf8(addr);
        XString.Raw(w, addr[..n]);
        XString.Raw(w, "\""u8);
        if (r.Styles[i] != 0)
        {
            XString.Raw(w, " s=\""u8);
            WriteInt(w, r.Styles[i]);
            XString.Raw(w, "\""u8);
        }

        string? stringText = null;
        byte[]? inlineRaw = null;
        switch (kind)
        {
            case CellValueKind.Boolean:
                XString.Raw(w, " t=\"b\""u8);
                break;
            case CellValueKind.Error:
                XString.Raw(w, " t=\"e\""u8);
                break;
            case CellValueKind.Text:
                if ((kindByte & RowData.IsoDateBit) != 0)
                {
                    XString.Raw(w, " t=\"d\""u8);
                    stringText = store.PoolAt((int)data).Text;
                }
                else if ((int)data >= 0 && !hasFormula)
                {
                    XString.Raw(w, " t=\"s\""u8);
                }
                else if ((int)data >= 0)
                {
                    XString.Raw(w, " t=\"str\""u8);
                    stringText = strings is not null && (int)data < strings.Count ? strings.GetText((int)data) : string.Empty;
                }
                else if (hasFormula)
                {
                    XString.Raw(w, " t=\"str\""u8);
                    stringText = store.PoolAt((int)data).Text;
                }
                else
                {
                    XString.Raw(w, " t=\"inlineStr\""u8);
                    PoolString p = store.PoolAt((int)data);
                    stringText = p.Text;
                    inlineRaw = p.RawInline;
                }

                break;
        }

        if (extras?.Attributes is { } attrs)
        {
            WriteString(w, attrs);
        }

        CellFormula? formula = hasFormula && r.Formulas is { } fa && fa[i] != 0 ? store.FormulaAt(fa[i]) : null;
        bool hasValue = kind != CellValueKind.Empty;
        if (formula is null && !hasValue && extras?.Children is null)
        {
            XString.Raw(w, "/>"u8);
            return;
        }

        XString.Raw(w, ">"u8);
        if (formula is not null)
        {
            WriteFormula(w, formula, pc);
        }

        switch (kind)
        {
            case CellValueKind.Number:
                XString.Raw(w, "<"u8);
                XString.Raw(w, pc);
                XString.Raw(w, "v>"u8);
                WriteDouble(w, BitConverter.Int64BitsToDouble(data));
                CloseV(w, pc);
                break;
            case CellValueKind.Boolean:
                XString.Raw(w, "<"u8);
                XString.Raw(w, pc);
                XString.Raw(w, data != 0 ? "v>1"u8 : "v>0"u8);
                CloseV(w, pc);
                break;
            case CellValueKind.Error:
                XString.Raw(w, "<"u8);
                XString.Raw(w, pc);
                XString.Raw(w, "v>"u8);
                WriteString(w, (int)data >= 0 ? CellErrors.ToText((CellError)(int)data) : store.PoolAt((int)data).Text, escape: true);
                CloseV(w, pc);
                break;
            case CellValueKind.Text:
                if (inlineRaw is not null)
                {
                    XString.Raw(w, "<"u8);
                    XString.Raw(w, pc);
                    XString.Raw(w, "is>"u8);
                    XString.Raw(w, inlineRaw);
                    XString.Raw(w, "</"u8);
                    XString.Raw(w, pc);
                    XString.Raw(w, "is>"u8);
                }
                else if (stringText is not null && (kindByte & RowData.IsoDateBit) == 0 && !hasFormula && (int)data < 0)
                {
                    XString.Raw(w, "<"u8);
                    XString.Raw(w, pc);
                    XString.Raw(w, "is>"u8);
                    XString.WriteT(w, XString.Encode(stringText), pc);
                    XString.Raw(w, "</"u8);
                    XString.Raw(w, pc);
                    XString.Raw(w, "is>"u8);
                }
                else if (stringText is not null)
                {
                    XString.Raw(w, "<"u8);
                    XString.Raw(w, pc);
                    XString.Raw(w, "v>"u8);
                    WriteString(w, (kindByte & RowData.IsoDateBit) != 0 ? stringText : XString.Encode(stringText), escape: true);
                    CloseV(w, pc);
                }
                else
                {
                    XString.Raw(w, "<"u8);
                    XString.Raw(w, pc);
                    XString.Raw(w, "v>"u8);
                    WriteInt(w, (int)data);
                    CloseV(w, pc);
                }

                break;
        }

        if (extras?.Children is { } children)
        {
            XString.Raw(w, children);
        }

        XString.Raw(w, "</"u8);
        XString.Raw(w, pc);
        XString.Raw(w, "c>"u8);
    }

    private static void CloseV(IBufferWriter<byte> w, ReadOnlySpan<byte> pc)
    {
        XString.Raw(w, "</"u8);
        XString.Raw(w, pc);
        XString.Raw(w, "v>"u8);
    }

    private static void WriteFormula(IBufferWriter<byte> w, CellFormula f, ReadOnlySpan<byte> pc)
    {
        XString.Raw(w, "<"u8);
        XString.Raw(w, pc);
        XString.Raw(w, "f"u8);
        switch (f.Kind)
        {
            case CellFormulaKind.Shared:
                XString.Raw(w, " t=\"shared\""u8);
                break;
            case CellFormulaKind.Array:
                XString.Raw(w, " t=\"array\""u8);
                break;
            case CellFormulaKind.DataTable:
                XString.Raw(w, " t=\"dataTable\""u8);
                break;
        }

        if (f.Reference is not null)
        {
            XString.Raw(w, " ref=\""u8);
            WriteString(w, f.Reference, escape: true, attribute: true);
            XString.Raw(w, "\""u8);
        }

        if (f.SharedIndex >= 0)
        {
            XString.Raw(w, " si=\""u8);
            WriteInt(w, f.SharedIndex);
            XString.Raw(w, "\""u8);
        }

        if (f.RawAttributes is not null)
        {
            XString.Raw(w, " "u8);
            WriteString(w, f.RawAttributes);
        }

        if (f.Text.Length == 0)
        {
            XString.Raw(w, "/>"u8);
            return;
        }

        XString.Raw(w, ">"u8);
        WriteString(w, f.Text, escape: true);
        XString.Raw(w, "</"u8);
        XString.Raw(w, pc);
        XString.Raw(w, "f>"u8);
    }

    public static void WriteInt(IBufferWriter<byte> w, int value)
    {
        Span<byte> s = w.GetSpan(11);
        value.TryFormat(s, out int n, default, CultureInfo.InvariantCulture);
        w.Advance(n);
    }

    public static void WriteDouble(IBufferWriter<byte> w, double value)
    {
        Span<byte> s = w.GetSpan(32);
        value.TryFormat(s, out int n, default, CultureInfo.InvariantCulture);
        // The shortest form can be one unit in the last place off for some powers of two (2^-25 formats as 2.980232238769531E-08, which
        // reads back as the neighbour below). Long forms are read back and replaced by 17 significant digits when they differ.
        if (n >= 15 && (!double.TryParse(s[..n], NumberStyles.Float, CultureInfo.InvariantCulture, out double back) || back != value))
        {
            value.TryFormat(s, out n, "G17", CultureInfo.InvariantCulture);
        }

        w.Advance(n);
    }

    /// <summary>Writes text as UTF-8, escaped for element content (or an attribute value) when asked, else verbatim.</summary>
    public static void WriteString(IBufferWriter<byte> w, string text, bool escape = false, bool attribute = false)
    {
        if (escape)
        {
            XmlText.Escape(w, text, attribute);
            return;
        }

        int max = Encoding.UTF8.GetMaxByteCount(text.Length);
        Span<byte> s = w.GetSpan(max);
        int n = Encoding.UTF8.GetBytes(text, s);
        w.Advance(n);
    }
}
