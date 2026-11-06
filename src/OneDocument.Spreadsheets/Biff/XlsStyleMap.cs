using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Spreadsheets.Biff;

/// <summary>
/// Turns the hub's style sheet into the workbook-global style records of BIFF8: the colour palette, FONT, FORMAT, XF and STYLE
/// ([MS-XLS] §2.4.122, §2.4.126, §2.4.353, §2.4.269, §2.4.192), and encodes the differential formats of conditional-format rules
/// (DXFN, §2.5.95). Colours are collected first (<see cref="Register(StyleColor)"/>), then <see cref="Freeze"/> assigns palette slots.
/// </summary>
internal sealed class XlsStyleMap
{
    // [MS-XLS] §2.4.122: font numbers run 0-3 then 5 upward; number 4 is never used.
    private const int MaxFonts = 510;

    // [MS-XLS] §2.1.7.20.3 FORMATTING: at most 4,050 XF records, and 218 FORMAT records (identifiers 164 to 381).
    private const int MaxXf = 4050;
    private const int MaxFormats = 218;

    private static readonly HashSet<int> BuiltInFormatIds = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 37, 38, 39, 40, 45, 46, 47, 48, 49];

    private readonly StyleSheet _styles;
    private readonly ILossSink _loss;
    private readonly List<int> _seenRgb = [];
    private readonly HashSet<int> _seenSet = [];
    private readonly Dictionary<int, int> _rgbToIndex = [];
    private uint[] _palette = [];
    private bool _customPalette;
    private bool _frozen;
    private readonly List<(int Id, string Code)> _customFormats = [];
    private readonly Dictionary<string, int> _formatIds = new(StringComparer.Ordinal);
    private readonly List<byte[]> _fonts = [];
    private readonly List<byte[]> _xfs = [];
    private readonly List<byte[]> _styleRecords = [];
    private int _styleXfBase;
    private int _cellXfBase;
    private int _cellXfCount;
    private int _colorsApproximated;

    /// <summary>Creates the map over a workbook's style sheet.</summary>
    public XlsStyleMap(StyleSheet styles, ILossSink loss)
    {
        _styles = styles;
        _loss = loss;
        for (int i = 0; i < styles.FontCount; i++)
        {
            Register(styles.GetFont(i).Color);
        }

        for (int i = 0; i < styles.FillCount; i++)
        {
            CellFill f = styles.GetFill(i);
            Register(f.Foreground);
            Register(f.Background);
            if (f.Gradient is { } g)
            {
                foreach (GradientStop s in g.Stops)
                {
                    Register(s.Color);
                }
            }
        }

        for (int i = 0; i < styles.BorderCount; i++)
        {
            CellBorder b = styles.GetBorder(i);
            Register(b.Left.Color);
            Register(b.Right.Color);
            Register(b.Top.Color);
            Register(b.Bottom.Color);
            Register(b.Diagonal.Color);
        }
    }

    /// <summary>The number of cell formats written (what cells map onto).</summary>
    public int CellFormatCount => _cellXfCount;

    /// <summary>Registers the colours a differential format uses, before <see cref="Freeze"/>.</summary>
    public void Register(DifferentialFormat? d)
    {
        if (d is null)
        {
            return;
        }

        if (d.Font is { } f)
        {
            Register(f.Color);
        }

        if (d.Fill is { } fl)
        {
            Register(fl.Foreground);
            Register(fl.Background);
        }

        if (d.Border is { } b)
        {
            Register(b.Left.Color);
            Register(b.Right.Color);
            Register(b.Top.Color);
            Register(b.Bottom.Color);
            Register(b.Diagonal.Color);
        }
    }

    /// <summary>Registers one colour.</summary>
    public void Register(StyleColor c)
    {
        if (c.Kind is ColorKind.None or ColorKind.Auto)
        {
            return;
        }

        int rgb = _styles.ResolveColor(c) & 0xFFFFFF;
        if (_seenSet.Add(rgb))
        {
            _seenRgb.Add(rgb);
        }
    }

    /// <summary>
    /// Assigns the colours to palette slots (indexes 8 to 63, [MS-XLS] §2.4.188) and builds the font, format, XF and STYLE records.
    /// A colour that is already one of the default palette's keeps its index; others take slots no used colour needs. Beyond 56
    /// colours the nearest one is used and the loss report says so.
    /// </summary>
    public void Freeze()
    {
        if (_frozen)
        {
            return;
        }

        _frozen = true;
        uint[] palette = new uint[56];
        for (int i = 0; i < 56; i++)
        {
            palette[i] = ThemeColors.DefaultPalette[8 + i];
        }

        var taken = new bool[56];
        var pending = new List<int>();
        foreach (int rgb in _seenRgb)
        {
            int slot = Array.IndexOf(palette, (uint)rgb);
            if (slot >= 0 && !taken[slot])
            {
                taken[slot] = true;
                _rgbToIndex[rgb] = 8 + slot;
            }
            else if (slot >= 0)
            {
                _rgbToIndex[rgb] = 8 + slot;
            }
            else
            {
                pending.Add(rgb);
            }
        }

        int next = 0;
        foreach (int rgb in pending)
        {
            while (next < 56 && taken[next])
            {
                next++;
            }

            if (next >= 56)
            {
                _rgbToIndex[rgb] = Nearest(palette, rgb);
                _colorsApproximated++;
                continue;
            }

            taken[next] = true;
            palette[next] = (uint)rgb;
            _rgbToIndex[rgb] = 8 + next;
            _customPalette = true;
        }

        _palette = palette;
        if (_colorsApproximated > 0)
        {
            _loss.Report("loss.colors", "colours", _colorsApproximated, "A workbook palette holds 56 colours; colours beyond them were replaced by the nearest one.");
        }

        BuildFonts();
        BuildFormatsAndXfs();
    }

    private static int Nearest(uint[] palette, int rgb)
    {
        int best = 0;
        long bestD = long.MaxValue;
        for (int i = 0; i < palette.Length; i++)
        {
            int dr = ((int)(palette[i] >> 16) & 0xFF) - ((rgb >> 16) & 0xFF);
            int dg = ((int)(palette[i] >> 8) & 0xFF) - ((rgb >> 8) & 0xFF);
            int db = ((int)palette[i] & 0xFF) - (rgb & 0xFF);
            long d = (long)dr * dr + (long)dg * dg + (long)db * db;
            if (d < bestD)
            {
                bestD = d;
                best = 8 + i;
            }
        }

        return best;
    }

    /// <summary>The palette index of a colour; <paramref name="auto"/> stands for automatic or absent colours.</summary>
    public int Icv(StyleColor c, int auto)
    {
        if (c.Kind is ColorKind.None or ColorKind.Auto)
        {
            return auto;
        }

        int rgb = _styles.ResolveColor(c) & 0xFFFFFF;
        return _rgbToIndex.TryGetValue(rgb, out int i) ? i : auto;
    }

    // ---- fonts ----

    private void BuildFonts()
    {
        // The records are numbered 0 to 3 and then 5 upward: number 4 is never stored, so there is no record for it.
        int count = _styles.FontCount;
        for (int i = 0; i < Math.Max(count, 1); i++)
        {
            if (_fonts.Count >= MaxFonts)
            {
                _loss.Report("loss.fonts", "fonts", 1, "A workbook holds at most 510 fonts; cells using the others take the first font.");
                continue;
            }

            _fonts.Add(EncodeFont(i < count ? _styles.GetFont(i) : new CellFont("Arial", 10)));
        }
    }

    // [MS-XLS] §2.4.122 Font.
    private byte[] EncodeFont(CellFont f)
    {
        var b = new BiffBuffer();
        double size = f.Size > 0 ? f.Size : 10;
        b.U16((int)Math.Round(size * 20));
        int flags = (f.Italic ? 0x02 : 0) | (f.Strike ? 0x08 : 0);
        b.U16(flags);
        b.U16(Icv(f.Color, 0x7FFF));
        b.U16(f.Bold ? 700 : 400);
        b.U16(f.VerticalAlignment switch { FontVerticalAlignment.Superscript => 1, FontVerticalAlignment.Subscript => 2, _ => 0 });
        b.U8(f.Underline switch { FontUnderline.Straight => 1, FontUnderline.Doubled => 2, FontUnderline.SingleAccounting => 0x21, FontUnderline.DoubleAccounting => 0x22, _ => 0 });
        b.U8(f.Family);
        b.U8(f.Charset < 0 ? 0 : f.Charset & 0xFF);
        b.U8(0);
        string name = f.Name ?? "Arial";
        if (name.Length > 255)
        {
            name = name[..255];
        }

        b.String8(name);
        return b.Span.ToArray();
    }

    private int FontIndex(int hubFont)
    {
        // The hub font k is font number k below 4 and k+1 from 4 on (number 4 is skipped).
        int idx = hubFont < 4 ? hubFont : hubFont + 1;
        return hubFont >= 0 && hubFont < _fonts.Count ? idx : 0;
    }

    // ---- number formats ----

    private int FormatId(int hubId)
    {
        string code = _styles.GetNumberFormatCode(hubId);
        return FormatIdOfCode(code, hubId);
    }

    private int FormatIdOfCode(string code, int hubId)
    {
        bool overridden = _styles.NumberFormats.ContainsKey(hubId);
        if (code == "General")
        {
            return 0;
        }

        if (BuiltInFormatIds.Contains(hubId) && !overridden)
        {
            return hubId;
        }

        if (_formatIds.TryGetValue(code, out int known))
        {
            return known;
        }

        if (_customFormats.Count >= MaxFormats)
        {
            _loss.Report("loss.number-formats", "number formats", 1, "A workbook holds at most 218 custom number formats; cells using the others show General.");
            return 0;
        }

        int id = 164 + _customFormats.Count;
        _customFormats.Add((id, code));
        _formatIds[code] = id;
        return id;
    }

    // ---- XF ----

    private void BuildFormatsAndXfs()
    {
        int styleCount = _styles.StyleFormatCount;
        _styleXfBase = 0;
        int styleSlots = Math.Max(styleCount, 15);
        _cellXfBase = styleSlots;
        int cellCount = Math.Max(_styles.CellFormatCount, 1);
        if (styleSlots + cellCount > MaxXf)
        {
            _loss.Report("loss.cell-formats", "cell formats", styleSlots + cellCount - MaxXf, "A workbook holds at most 4,050 cell formats; cells using the others take the default format.");
            cellCount = Math.Max(MaxXf - styleSlots, 1);
        }

        _cellXfCount = cellCount;
        for (int j = 0; j < styleSlots; j++)
        {
            int src = j < styleCount ? j : 0;
            CellFormat sf = styleCount > 0 ? _styles.GetStyleFormat(src) : new CellFormat(0, 0, 0, 0, 0, null, null, null, null, null, null, false, null, null);
            _xfs.Add(EncodeXf(sf, isStyle: true, parent: 0xFFF, null));
        }

        for (int i = 0; i < cellCount; i++)
        {
            if (_styles.CellFormatCount == 0)
            {
                _xfs.Add(EncodeXf(new CellFormat(0, 0, 0, 0, 0, null, null, null, null, null, null, false, null, null), false, 0, null));
                continue;
            }

            ResolvedCellStyle r = _styles.Resolve(i);
            CellFormat cf = _styles.GetCellFormat(i);
            int parent = cf.StyleFormatId >= 0 && cf.StyleFormatId < styleCount ? cf.StyleFormatId : 0;
            _xfs.Add(EncodeXf(cf, false, parent, r));
        }

        for (int k = 0; k < _styles.CellStyles.Count; k++)
        {
            NamedCellStyle s = _styles.CellStyles[k];
            int xf = s.FormatId >= 0 && s.FormatId < styleCount ? s.FormatId : 0;
            var b = new BiffBuffer();
            if (s.BuiltInId >= 0 && s.BuiltInId <= 53)
            {
                b.U16(xf | 0x8000);
                b.U8(s.BuiltInId);
                b.U8(0xFF);
            }
            else
            {
                b.U16(xf);
                b.String16(s.Name.Length > 255 ? s.Name[..255] : s.Name);
            }

            _styleRecords.Add(b.Span.ToArray());
        }

        if (_styleRecords.Count == 0)
        {
            var b = new BiffBuffer();
            b.U16(0 | 0x8000);
            b.U8(0);
            b.U8(0xFF);
            _styleRecords.Add(b.Span.ToArray());
        }
    }

    /// <summary>The BIFF XF number a hub cell format maps to.</summary>
    public int Xf(int hubIndex) => hubIndex >= 0 && hubIndex < _cellXfCount ? _cellXfBase + hubIndex : _cellXfBase;

    // [MS-XLS] §2.4.353 XF. Cell formats are written flattened (each carries the properties it resolves to) and name their style as parent.
    private byte[] EncodeXf(CellFormat f, bool isStyle, int parent, ResolvedCellStyle? resolved)
    {
        int numFmtId = resolved?.NumberFormatId ?? f.NumberFormatId;
        CellFont? font = resolved?.Font;
        // A style sheet may list no fills or no borders at all (Excel opens such files); a format then has the empty fill and the empty border.
        CellFill fill = resolved?.Fill ?? (_styles.FillCount == 0 ? new CellFill() : _styles.GetFill(Math.Min(f.FillId, _styles.FillCount - 1)));
        CellBorder border = resolved?.Border ?? (_styles.BorderCount == 0 ? new CellBorder() : _styles.GetBorder(Math.Min(f.BorderId, _styles.BorderCount - 1)));
        CellAlignment al = resolved?.Alignment ?? f.Alignment ?? new CellAlignment();
        CellProtection prot = resolved?.Protection ?? f.Protection ?? new CellProtection();
        bool quote = resolved?.QuotePrefix ?? f.QuotePrefix;
        int fontId = f.FontId;
        _ = font;
        var b = new BiffBuffer();
        b.U16(FontIndex(fontId));
        b.U16(FormatId(numFmtId));
        int flags = (prot.Locked ? 1 : 0) | (prot.Hidden ? 2 : 0) | (isStyle ? 4 : 0) | (quote ? 8 : 0) | ((isStyle ? 0xFFF : _styleXfBase + parent) << 4);
        b.U16(flags);
        int alc = (int)al.Horizontal;
        int alcV = al.Vertical switch { CellVerticalAlignment.Top => 0, CellVerticalAlignment.Center => 1, CellVerticalAlignment.Bottom => 2, CellVerticalAlignment.Justify => 3, _ => 4 };
        b.U8((alc & 7) | (al.WrapText ? 8 : 0) | (alcV << 4) | (al.JustifyLastLine ? 0x80 : 0));
        b.U8(al.TextRotation is >= 0 and <= 180 or 255 ? al.TextRotation : 0);
        b.U8((Math.Min(al.Indent, 15) & 0xF) | (al.ShrinkToFit ? 0x10 : 0) | ((al.ReadingOrder & 3) << 6));
        b.U8(isStyle ? 0xFC : AttributeBits(f, numFmtId, fill, border, al, prot));
        uint border1 = (uint)(Style(border.Left.Style) | (Style(border.Right.Style) << 4) | (Style(border.Top.Style) << 8) | (Style(border.Bottom.Style) << 12))
            | ((uint)Icv(border.Left.Color, 64) << 16) | ((uint)Icv(border.Right.Color, 64) << 23) | (border.DiagonalDown ? 1u << 30 : 0) | (border.DiagonalUp ? 1u << 31 : 0);
        b.U32(border1);
        int pattern = (int)fill.Pattern;
        if (fill.Gradient is { } g)
        {
            pattern = 1;
            fill = new CellFill(FillPattern.Solid, g.Stops.Count > 0 ? g.Stops[0].Color : default, default);
            _loss.Report("loss.gradient-fills", "gradient fills", 1, "A gradient fill has no BIFF8 form and became a solid fill of its first colour.");
        }

        uint border2 = (uint)Icv(border.Top.Color, 64) | ((uint)Icv(border.Bottom.Color, 64) << 7) | ((uint)Icv(border.Diagonal.Color, 64) << 14)
            | ((uint)Style(border.Diagonal.Style) << 21) | ((uint)pattern << 26);
        b.U32(border2);
        int fore = Icv(fill.Foreground, 64);
        int back = Icv(fill.Background, 65);
        b.U16(fore | (back << 7));
        return b.Span.ToArray();
    }

    private static int Style(BorderLineStyle s) => (int)s;

    private static byte AttributeBits(CellFormat f, int numFmtId, CellFill fill, CellBorder border, CellAlignment al, CellProtection prot)
    {
        int bits = 0;
        if (numFmtId != 0)
        {
            bits |= 0x04;
        }

        if (f.FontId != 0)
        {
            bits |= 0x08;
        }

        if (al != new CellAlignment())
        {
            bits |= 0x10;
        }

        if (border != new CellBorder())
        {
            bits |= 0x20;
        }

        if (fill.Pattern != FillPattern.None || fill.Gradient is not null)
        {
            bits |= 0x40;
        }

        if (!prot.Locked || prot.Hidden)
        {
            bits |= 0x80;
        }

        return (byte)bits;
    }

    // ---- output ----

    /// <summary>Writes FONT, FORMAT, XF, STYLE and PALETTE ([MS-XLS] §2.1.7.20.3 FORMATTING).</summary>
    public void Emit(BiffSink s)
    {
        foreach (byte[] f in _fonts)
        {
            s.Exact(0x0031, f);
        }

        foreach ((int id, string code) in _customFormats)
        {
            var b = new BiffBuffer();
            b.U16(id);
            b.String16(code);
            s.Record(0x041E, b);
        }

        foreach (byte[] x in _xfs)
        {
            s.Exact(0x00E0, x);
        }

        foreach (byte[] st in _styleRecords)
        {
            s.Exact(0x0293, st);
        }

        if (_customPalette)
        {
            var b = new BiffBuffer();
            b.U16(56);
            foreach (uint rgb in _palette)
            {
                b.U8((int)(rgb >> 16) & 0xFF);
                b.U8((int)(rgb >> 8) & 0xFF);
                b.U8((int)rgb & 0xFF);
                b.U8(0);
            }

            s.Record(0x0092, b);
        }
    }

    // ---- differential formats ----

    /// <summary>Encodes a differential format as a DXFN structure ([MS-XLS] §2.5.95). The number format is a user string.</summary>
    public byte[] EncodeDxf(DifferentialFormat d)
    {
        bool hasNum = !string.IsNullOrEmpty(d.NumberFormatCode) && d.NumberFormatCode != "General";
        bool hasFont = d.Font is not null;
        bool hasAlc = d.Alignment is not null;
        bool hasBdr = d.Border is not null;
        bool hasPat = d.Fill is { } df0 && (df0.Pattern != FillPattern.None || df0.Gradient is not null || df0.Foreground.Kind is not (ColorKind.None or ColorKind.Auto) || df0.Background.Kind is not (ColorKind.None or ColorKind.Auto));
        bool hasProt = d.Protection is not null;

        // Ninch bits (A..U) say "ignore this field"; everything a differential format does not set is ignored.
        uint flags = 0;
        flags |= 0xFFu; // A-H: alignment pieces ignored unless set below
        if (hasAlc)
        {
            flags &= ~0xFFu;
        }

        flags |= (1u << 8) | (1u << 9); // I, J: protection
        if (hasProt)
        {
            flags &= ~((1u << 8) | (1u << 9));
        }

        CellBorder bd = d.Border ?? new CellBorder();
        if (!hasBdr || bd.Left.Style == BorderLineStyle.None)
        {
            flags |= 1u << 10;
        }

        if (!hasBdr || bd.Right.Style == BorderLineStyle.None)
        {
            flags |= 1u << 11;
        }

        if (!hasBdr || bd.Top.Style == BorderLineStyle.None)
        {
            flags |= 1u << 12;
        }

        if (!hasBdr || bd.Bottom.Style == BorderLineStyle.None)
        {
            flags |= 1u << 13;
        }

        flags |= (1u << 14) | (1u << 15); // diagonals
        CellFill fl = d.Fill ?? new CellFill();
        if (!hasPat)
        {
            flags |= (1u << 16) | (1u << 17) | (1u << 18);
        }

        if (!hasNum)
        {
            flags |= 1u << 19; // T: ifmtNinch
        }

        flags |= 1u << 20; // U: the font index is not used
        if (hasNum)
        {
            flags |= 1u << 25;
        }

        if (hasFont)
        {
            flags |= 1u << 26;
        }

        if (hasAlc)
        {
            flags |= 1u << 27;
        }

        if (hasBdr)
        {
            flags |= 1u << 28;
        }

        if (hasPat)
        {
            flags |= 1u << 29;
        }

        if (hasProt)
        {
            flags |= 1u << 30;
        }

        var b = new BiffBuffer();
        b.U32(flags);
        b.U16((hasNum ? 1 : 0) | (1 << 15)); // fIfmtUser, fZeroInited
        if (hasNum)
        {
            var str = new BiffBuffer();
            str.String16(d.NumberFormatCode!);
            b.U16(str.Length + 2);
            b.Bytes(str.Span);
        }

        if (hasFont)
        {
            EncodeDxfFont(b, d.Font!);
        }

        if (hasAlc)
        {
            CellAlignment al = d.Alignment!;
            int alcV = al.Vertical switch { CellVerticalAlignment.Top => 0, CellVerticalAlignment.Center => 1, CellVerticalAlignment.Bottom => 2, CellVerticalAlignment.Justify => 3, _ => 4 };
            b.U8(((int)al.Horizontal & 7) | (al.WrapText ? 8 : 0) | (alcV << 4) | (al.JustifyLastLine ? 0x80 : 0));
            b.U8(al.TextRotation is >= 0 and <= 180 or 255 ? al.TextRotation : 0);
            b.U8(Math.Min(al.Indent, 15) & 0xF);
            b.U8((al.ShrinkToFit ? 1 : 0) | ((al.ReadingOrder & 3) << 6));
            b.U32(0);
        }

        if (hasBdr)
        {
            uint b1 = (uint)(Style(bd.Left.Style) | (Style(bd.Right.Style) << 4) | (Style(bd.Top.Style) << 8) | (Style(bd.Bottom.Style) << 12))
                | ((uint)Icv(bd.Left.Color, 64) << 16) | ((uint)Icv(bd.Right.Color, 64) << 23);
            uint b2 = (uint)Icv(bd.Top.Color, 64) | ((uint)Icv(bd.Bottom.Color, 64) << 7);
            b.U32(b1);
            b.U32(b2);
        }

        if (hasPat)
        {
            // A differential format's fill with no pattern named is solid, and a solid fill keeps its colour in the background slot.
            bool solid = fl.Pattern is FillPattern.Solid or FillPattern.None;
            int fls = solid ? 1 : (int)fl.Pattern;
            StyleColor main = solid
                ? (fl.Background.Kind is not (ColorKind.None or ColorKind.Auto) ? fl.Background : fl.Foreground)
                : fl.Foreground;
            int fore = solid ? Icv(main, 64) : Icv(fl.Foreground, 64);
            int back = solid ? Icv(main, 65) : Icv(fl.Background, 65);
            b.U32((uint)((fls << 10) | (fore << 16) | (back << 23)));
        }

        if (hasProt)
        {
            CellProtection p = d.Protection!;
            b.U16((p.Locked ? 1 : 0) | (p.Hidden ? 2 : 0));
        }

        return b.Span.ToArray();
    }

    // [MS-XLS] §2.5.93 DXFFntD: 118 bytes, with the name optional and the Ninch fields saying which attributes the format leaves alone.
    private void EncodeDxfFont(BiffBuffer b, CellFont f)
    {
        var rec = new byte[118];
        int p = 0;
        if (!string.IsNullOrEmpty(f.Name) && f.Name.Length <= 31)
        {
            bool wide = !BiffBuffer.IsCompressible(f.Name);
            rec[0] = (byte)f.Name.Length;
            rec[1] = (byte)(wide ? 1 : 0);
            p = 2;
            foreach (char c in f.Name)
            {
                rec[p++] = (byte)c;
                if (wide)
                {
                    rec[p++] = (byte)(c >> 8);
                }
            }
        }

        if (f.Size > 0)
        {
            BitConverter.TryWriteBytes(rec.AsSpan(64, 4), (int)Math.Round(f.Size * 20));
        }

        int ts = (f.Italic ? 0x02 : 0) | (f.Strike ? 0x80 : 0);
        BitConverter.TryWriteBytes(rec.AsSpan(68, 4), ts);
        BitConverter.TryWriteBytes(rec.AsSpan(72, 2), (short)(f.Bold ? 700 : 400));
        rec[76] = (byte)(f.Underline switch { FontUnderline.Straight => 1, FontUnderline.Doubled => 2, FontUnderline.SingleAccounting => 0x21, FontUnderline.DoubleAccounting => 0x22, _ => 0 });
        int icv = f.Color.Kind is ColorKind.None or ColorKind.Auto ? -1 : Icv(f.Color, 0x7FFF);
        BitConverter.TryWriteBytes(rec.AsSpan(80, 4), icv);
        // tsNinch: italic and strikeout are always stated; the other style bits are ignored.
        BitConverter.TryWriteBytes(rec.AsSpan(88, 4), 0x7Du);
        BitConverter.TryWriteBytes(rec.AsSpan(92, 4), 1); // fSssNinch
        BitConverter.TryWriteBytes(rec.AsSpan(96, 4), 0); // fUlsNinch: the underline is stated
        BitConverter.TryWriteBytes(rec.AsSpan(100, 4), 0); // fBlsNinch: the weight is stated
        BitConverter.TryWriteBytes(rec.AsSpan(108, 4), -1);
        BitConverter.TryWriteBytes(rec.AsSpan(112, 4), -1);
        b.Bytes(rec);
    }
}
