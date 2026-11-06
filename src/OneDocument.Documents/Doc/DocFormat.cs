using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Doc.Binary;

namespace RecursiveAI.OneDocument.Doc;

/// <summary>Shared helpers that write WordprocessingML for the properties of a Word binary file.</summary>
internal static class WmlWrite
{
    /// <summary>Appends text escaped for XML content or an attribute; characters XML 1.0 cannot carry are dropped.</summary>
    internal static StringBuilder Esc(this StringBuilder sb, string s)
    {
        foreach (char c in s)
        {
            switch (c)
            {
                case '&':
                    sb.Append("&amp;");
                    break;
                case '<':
                    sb.Append("&lt;");
                    break;
                case '>':
                    sb.Append("&gt;");
                    break;
                case '"':
                    sb.Append("&quot;");
                    break;
                default:
                    // XML 1.0 Char: tab, line feed, carriage return and everything from U+0020 except the two non-characters.
                    if (c >= ' ' ? c < '\uFFFE' : c is '\t' or '\n' or '\r')
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        return sb;
    }

    internal static string I(int v) => v.ToString(CultureInfo.InvariantCulture);

    internal static string Hex6(int rgb) => rgb.ToString("X6", CultureInfo.InvariantCulture);

    // [MS-DOC] Ico: the 16 colours of the character colour index.
    private static readonly string[] IcoHex =
    [
        "auto", "000000", "0000FF", "00FFFF", "00FF00", "FF00FF", "FF0000", "FFFF00", "FFFFFF",
        "000080", "008080", "008000", "800080", "800000", "808000", "808080", "C0C0C0",
    ];

    private static readonly string[] IcoName =
    [
        "none", "black", "blue", "cyan", "green", "magenta", "red", "yellow", "white",
        "darkBlue", "darkCyan", "darkGreen", "darkMagenta", "darkRed", "darkYellow", "darkGray", "lightGray",
    ];

    internal static string IcoToHex(int ico) => (uint)ico < IcoHex.Length ? IcoHex[ico] : "auto";

    internal static string IcoToHighlight(int ico) => (uint)ico < IcoName.Length ? IcoName[ico] : "none";

    // COLORREF: red, green, blue, then a byte that is 0xFF for "automatic".
    internal static string ColorRef(ReadOnlySpan<byte> c) => c.Length >= 4 && c[3] != 0xFF ? Hex6((c[0] << 16) | (c[1] << 8) | c[2]) : "auto";

    // [MS-DOC] Brc: border styles by brcType, and the ST_Border value each stands for.
    internal static string BorderStyle(int type) => type switch
    {
        0 => "nil",
        1 => "single",
        2 => "thick",
        3 => "double",
        5 => "single",
        6 => "dotted",
        7 => "dashed",
        8 => "dotDash",
        9 => "dotDotDash",
        10 => "triple",
        11 => "thinThickSmallGap",
        12 => "thickThinSmallGap",
        13 => "thinThickThinSmallGap",
        14 => "thinThickMediumGap",
        15 => "thickThinMediumGap",
        16 => "thinThickThinMediumGap",
        17 => "thinThickLargeGap",
        18 => "thickThinLargeGap",
        19 => "thinThickThinLargeGap",
        20 => "wave",
        21 => "doubleWave",
        22 => "dashSmallGap",
        23 => "dashDotStroked",
        24 => "threeDEmboss",
        25 => "threeDEngrave",
        26 => "outset",
        27 => "inset",
        255 => "nil",
        _ => "single",
    };

    /// <summary>Writes a border element from an 8-byte <c>Brc</c>.</summary>
    internal static void Border(StringBuilder sb, string element, ReadOnlySpan<byte> brc)
    {
        if (brc.Length < 8 || (brc[0] == 0xFF && brc[1] == 0xFF && brc[2] == 0xFF && brc[3] == 0xFF && brc[4] == 0xFF))
        {
            return;
        }

        string color = ColorRef(brc);
        int width = brc[4];
        int type = brc[5];
        int space = brc[6] & 0x1F;
        sb.Append('<').Append(element).Append(" w:val=\"").Append(BorderStyle(type)).Append("\" w:sz=\"").Append(I(Math.Clamp(width, 2, 96)))
            .Append("\" w:space=\"").Append(I(space)).Append("\" w:color=\"").Append(color).Append('"');
        if ((brc[6] & 0x20) != 0)
        {
            sb.Append(" w:shadow=\"1\"");
        }

        sb.Append("/>");
    }

    /// <summary>Writes a border element from a 4-byte <c>Brc80</c> (width, type, colour index, space and flags).</summary>
    internal static void Border80(StringBuilder sb, string element, ReadOnlySpan<byte> brc)
    {
        if (brc.Length < 4 || (brc[0] == 0xFF && brc[1] == 0xFF))
        {
            return;
        }

        byte[] wide = new byte[8];
        // Widen to the 8-byte form: colour from the index, then width, type, and space/flags.
        string hex = IcoToHex(brc[2] & 0x1F);
        if (hex == "auto")
        {
            wide[3] = 0xFF;
        }
        else
        {
            int rgb = int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            wide[0] = (byte)(rgb >> 16);
            wide[1] = (byte)(rgb >> 8);
            wide[2] = (byte)rgb;
        }

        wide[4] = brc[0];
        wide[5] = brc[1];
        wide[6] = brc[3];
        Border(sb, element, wide);
    }

    private static readonly string[] ShdPattern =
    [
        "clear", "solid", "pct5", "pct10", "pct20", "pct25", "pct30", "pct40", "pct50", "pct60", "pct70", "pct75", "pct80", "pct90",
        "horzStripe", "vertStripe", "reverseDiagStripe", "diagStripe", "horzCross", "diagCross",
        "thinHorzStripe", "thinVertStripe", "thinReverseDiagStripe", "thinDiagStripe", "thinHorzCross", "thinDiagCross",
    ];

    /// <summary>Writes <c>w:shd</c> from a 10-byte <c>Shd</c> (two colours and a pattern).</summary>
    internal static void Shading(StringBuilder sb, ReadOnlySpan<byte> shd)
    {
        if (shd.Length < 10)
        {
            return;
        }

        int ipat = BinaryPrimitives.ReadUInt16LittleEndian(shd[8..]);
        if (ipat == 0xFFFF)
        {
            return;
        }

        string fore = ColorRef(shd);
        string back = ColorRef(shd[4..]);
        string pat = ipat < ShdPattern.Length ? ShdPattern[ipat] : "clear";
        if (ipat == 0 && fore == "auto" && back == "auto")
        {
            return;
        }

        sb.Append("<w:shd w:val=\"").Append(pat).Append("\" w:color=\"").Append(fore).Append("\" w:fill=\"").Append(back).Append("\"/>");
    }

    /// <summary>Writes <c>w:shd</c> from a 2-byte <c>Shd80</c>.</summary>
    internal static void Shading80(StringBuilder sb, ReadOnlySpan<byte> shd)
    {
        if (shd.Length < 2)
        {
            return;
        }

        int v = BinaryPrimitives.ReadUInt16LittleEndian(shd);
        int fore = v & 0x1F;
        int back = (v >> 5) & 0x1F;
        int ipat = v >> 10;
        if (ipat == 0 && fore == 0 && back == 0)
        {
            return;
        }

        string pat = ipat < ShdPattern.Length ? ShdPattern[ipat] : "clear";
        sb.Append("<w:shd w:val=\"").Append(pat).Append("\" w:color=\"").Append(IcoToHex(fore)).Append("\" w:fill=\"").Append(IcoToHex(back)).Append("\"/>");
    }
}

/// <summary>Resolves what character formatting needs from the document: font names, styles and style identifiers.</summary>
internal sealed class FormatContext
{
    internal FormatContext(DocFontTable fonts, DocStyleSheet styles, Func<int, string?> styleId)
    {
        Fonts = fonts;
        Styles = styles;
        StyleId = styleId;
    }

    internal DocFontTable Fonts { get; }

    internal DocStyleSheet Styles { get; }

    internal Func<int, string?> StyleId { get; }

    /// <summary>The paragraph style of the paragraph being written, for toggle resolution.</summary>
    internal int ParagraphStyle { get; set; }
}

/// <summary>Character properties collected from a list of modifiers, written as <c>w:rPr</c>.</summary>
internal sealed class RunFormat
{
    private readonly Dictionary<ushort, byte> _toggles = [];
    private int _istd = -1;
    private int _ftcAscii = -1;
    private int _ftcFe = -1;
    private int _ftcOther = -1;
    private int _ftcBi = -1;
    private string? _color;
    private int _spacing = int.MinValue;
    private int _scale;
    private int _kern = -1;
    private int _position = int.MinValue;
    private int _size = -1;
    private int _sizeBi = -1;
    private int _highlight = -1;
    private int _underline = -1;
    private int _effect;
    private byte[]? _border;
    private byte[]? _border80;
    private byte[]? _shade;
    private byte[]? _shade80;
    private int _vert = -1;
    private int _emphasis;
    private int _lidAscii;
    private int _lidFe;
    private int _lidBi;
    private bool _dstrike;
    private bool _rtl;
    private bool _cs;

    /// <summary>Gets the revision mark kind: 0 none, 1 inserted, 2 deleted.</summary>
    internal int Revision { get; private set; }

    /// <summary>Gets the revision author index (into <c>SttbfRMark</c>) of an insertion.</summary>
    internal int InsAuthor { get; private set; }

    /// <summary>Gets the revision date of an insertion (a <c>DTTM</c>).</summary>
    internal uint InsDate { get; private set; }

    /// <summary>Gets the author index of a deletion.</summary>
    internal int DelAuthor { get; private set; }

    /// <summary>Gets the date of a deletion.</summary>
    internal uint DelDate { get; private set; }

    /// <summary>Gets a value indicating whether the character is a special one (picture, note reference, symbol).</summary>
    internal bool IsSpecial { get; private set; }

    /// <summary>Gets the picture location (<c>sprmCPicLocation</c>), or -1.</summary>
    internal int PicLocation { get; private set; } = -1;

    /// <summary>Gets a value indicating whether the special character stands for binary data (a form field's data).</summary>
    internal bool IsData { get; private set; }

    /// <summary>Gets a value indicating whether the special character stands for an OLE object.</summary>
    internal bool IsOle { get; private set; }

    /// <summary>Gets the symbol font index and character of <c>sprmCSymbol</c>, or (-1, 0).</summary>
    internal (int Font, int Char) Symbol { get; private set; } = (-1, 0);

    /// <summary>Gets a value indicating whether the text is hidden by a field's <c>fFldVanish</c>.</summary>
    internal bool FieldVanish { get; private set; }

    /// <summary>Gets the character style index set directly, or -1.</summary>
    internal int CharStyle => _istd;

    internal bool HasRevisionOnly => Revision != 0;

    internal static RunFormat From(IEnumerable<Prl> props)
    {
        var f = new RunFormat();
        foreach (Prl p in props)
        {
            f.Apply(p);
        }

        return f;
    }

    internal void Apply(Prl p)
    {
        switch (p.Sprm)
        {
            case 0x0800:
                Revision = p.OperandByte == 1 ? 2 : Revision;
                break;
            case 0x0801:
                Revision = p.OperandByte == 1 ? 1 : Revision;
                break;
            case 0x0802:
                FieldVanish = p.OperandByte == 1;
                break;
            case 0x6A03:
                PicLocation = p.OperandI32;
                break;
            case 0x4804:
                InsAuthor = p.OperandU16;
                break;
            case 0x6805:
                InsDate = (uint)p.OperandI32;
                break;
            case 0x4863:
                DelAuthor = p.OperandU16;
                break;
            case 0x6864:
                DelDate = (uint)p.OperandI32;
                break;
            case 0x0806:
                IsData = p.OperandByte == 1;
                break;
            case 0x080A:
                IsOle = p.OperandByte == 1;
                break;
            case 0x0855:
                IsSpecial = p.OperandByte == 1;
                break;
            case 0x6A09:
                if (p.Operand.Length >= 4)
                {
                    Symbol = (p.OperandU16, BinaryPrimitives.ReadUInt16LittleEndian(p.Operand.Span[2..]));
                }

                break;
            case 0x4A30:
                _istd = p.OperandU16;
                break;
            case 0x0835:
            case 0x0836:
            case 0x0837:
            case 0x0838:
            case 0x0839:
            case 0x083A:
            case 0x083B:
            case 0x083C:
            case 0x0858:
            case 0x0854:
            case 0x085C:
            case 0x085D:
            case 0x0875:
            case 0x0811:
            case 0x0818:
                _toggles[p.Sprm] = p.OperandByte;
                break;
            case 0x2A53:
                _dstrike = p.OperandByte == 1;
                break;
            case 0x4A4F:
                _ftcAscii = p.OperandU16;
                break;
            case 0x4A50:
                _ftcFe = p.OperandU16;
                break;
            case 0x4A51:
                _ftcOther = p.OperandU16;
                break;
            case 0x4A5E:
                _ftcBi = p.OperandU16;
                break;
            case 0x2A42:
                _color = WmlWrite.IcoToHex(p.OperandByte);
                break;
            case 0x4A60:
                _color = WmlWrite.IcoToHex(p.OperandU16);
                break;
            case 0x6870:
                _color = WmlWrite.ColorRef(p.Operand.Span);
                break;
            case 0x8840:
                _spacing = p.OperandI16;
                break;
            case 0x4852:
                _scale = p.OperandU16;
                break;
            case 0x484B:
                _kern = p.OperandU16;
                break;
            case 0x4845:
                _position = p.OperandI16;
                break;
            case 0x4A43:
                _size = p.OperandU16;
                break;
            case 0x4A61:
                _sizeBi = p.OperandU16;
                break;
            case 0x2A0C:
                _highlight = p.OperandByte;
                break;
            case 0x2A3E:
                _underline = p.OperandByte;
                break;
            case 0x2859:
                _effect = p.OperandByte;
                break;
            case 0xCA72:
                _border = p.Operand.Length >= 9 ? p.Operand.Span[1..9].ToArray() : null;
                break;
            case 0x6865:
                _border80 = p.Operand.Length >= 4 ? p.Operand.Span[..4].ToArray() : null;
                break;
            case 0xCA71:
                _shade = p.Operand.Length >= 11 ? p.Operand.Span[1..11].ToArray() : null;
                break;
            case 0x4866:
                _shade80 = p.Operand.Length >= 2 ? p.Operand.Span[..2].ToArray() : null;
                break;
            case 0x2A48:
                _vert = p.OperandByte;
                break;
            case 0x2A34:
                _emphasis = p.OperandByte;
                break;
            case 0x4873:
                _lidAscii = p.OperandU16;
                break;
            case 0x4874:
                _lidFe = p.OperandU16;
                break;
            case 0x485F:
                _lidBi = p.OperandU16;
                break;
            case 0x085A:
                _rtl = p.OperandByte == 1;
                break;
            case 0x0882:
                _cs = p.OperandByte == 1;
                break;
            case 0x2A33:
                // sprmCPlain: back to the style's formatting.
                _toggles.Clear();
                _color = null;
                _size = -1;
                _underline = -1;
                _highlight = -1;
                break;
        }
    }

    // A toggle operand: 0 off, 1 on, 0x80 same as the style, 0x81 opposite of the style ([MS-DOC] ToggleOperand).
    private string? Toggle(ushort sprm, FormatContext ctx, string element, string? valueIfOn = null)
    {
        if (!_toggles.TryGetValue(sprm, out byte v))
        {
            return null;
        }

        bool on;
        switch (v)
        {
            case 0:
                on = false;
                break;
            case 1:
                on = true;
                break;
            case 0x80:
                return null;
            case 0x81:
                on = !ctx.Styles.ToggleInStyle(ctx.ParagraphStyle, _istd, sprm);
                break;
            default:
                return null;
        }

        _ = valueIfOn;
        return on ? "<w:" + element + "/>" : "<w:" + element + " w:val=\"0\"/>";
    }

    /// <summary>Appends the property elements (no wrapper) in schema order.</summary>
    internal void WriteInner(StringBuilder sb, FormatContext ctx, bool withStyle = true)
    {
        if (withStyle && _istd >= 0 && ctx.StyleId(_istd) is { } sid)
        {
            sb.Append("<w:rStyle w:val=\"").Esc(sid).Append("\"/>");
        }

        if (_ftcAscii >= 0 || _ftcFe >= 0 || _ftcOther >= 0 || _ftcBi >= 0)
        {
            string? a = ctx.Fonts.Name(_ftcAscii);
            string? fe = ctx.Fonts.Name(_ftcFe);
            string? ot = ctx.Fonts.Name(_ftcOther);
            string? bi = ctx.Fonts.Name(_ftcBi);
            if (a is not null || fe is not null || ot is not null || bi is not null)
            {
                sb.Append("<w:rFonts");
                if (a is not null)
                {
                    sb.Append(" w:ascii=\"").Esc(a).Append('"');
                }

                if (ot is not null || a is not null)
                {
                    sb.Append(" w:hAnsi=\"").Esc(ot ?? a!).Append('"');
                }

                if (fe is not null)
                {
                    sb.Append(" w:eastAsia=\"").Esc(fe).Append('"');
                }

                if (bi is not null)
                {
                    sb.Append(" w:cs=\"").Esc(bi).Append('"');
                }

                sb.Append("/>");
            }
        }

        sb.Append(Toggle(0x0835, ctx, "b")).Append(Toggle(0x085C, ctx, "bCs")).Append(Toggle(0x0836, ctx, "i")).Append(Toggle(0x085D, ctx, "iCs"))
            .Append(Toggle(0x083B, ctx, "caps")).Append(Toggle(0x083A, ctx, "smallCaps")).Append(Toggle(0x0837, ctx, "strike"));
        if (_dstrike)
        {
            sb.Append("<w:dstrike/>");
        }

        sb.Append(Toggle(0x0838, ctx, "outline")).Append(Toggle(0x0839, ctx, "shadow")).Append(Toggle(0x0858, ctx, "emboss")).Append(Toggle(0x0854, ctx, "imprint"))
            .Append(Toggle(0x0875, ctx, "noProof")).Append(Toggle(0x083C, ctx, "vanish")).Append(Toggle(0x0811, ctx, "webHidden"));
        if (_color is not null)
        {
            sb.Append("<w:color w:val=\"").Append(_color).Append("\"/>");
        }

        if (_spacing != int.MinValue)
        {
            sb.Append("<w:spacing w:val=\"").Append(WmlWrite.I(_spacing)).Append("\"/>");
        }

        if (_scale is > 0 and <= 600 && _scale != 100)
        {
            sb.Append("<w:w w:val=\"").Append(WmlWrite.I(_scale)).Append("\"/>");
        }

        if (_kern > 0)
        {
            sb.Append("<w:kern w:val=\"").Append(WmlWrite.I(_kern)).Append("\"/>");
        }

        if (_position != int.MinValue)
        {
            sb.Append("<w:position w:val=\"").Append(WmlWrite.I(_position)).Append("\"/>");
        }

        if (_size > 0)
        {
            sb.Append("<w:sz w:val=\"").Append(WmlWrite.I(_size)).Append("\"/>");
        }

        if (_sizeBi > 0)
        {
            sb.Append("<w:szCs w:val=\"").Append(WmlWrite.I(_sizeBi)).Append("\"/>");
        }

        if (_highlight > 0)
        {
            sb.Append("<w:highlight w:val=\"").Append(WmlWrite.IcoToHighlight(_highlight)).Append("\"/>");
        }

        if (_underline >= 0)
        {
            sb.Append("<w:u w:val=\"").Append(UnderlineName(_underline)).Append("\"/>");
        }

        if (_effect > 0)
        {
            string? e = _effect switch { 1 => "lights", 2 => "blinkBackground", 3 => "sparkle", 4 => "antsBlack", 5 => "antsRed", 6 => "shimmer", _ => null };
            if (e is not null)
            {
                sb.Append("<w:effect w:val=\"").Append(e).Append("\"/>");
            }
        }

        if (_border is not null)
        {
            WmlWrite.Border(sb, "w:bdr", _border);
        }
        else if (_border80 is not null)
        {
            WmlWrite.Border80(sb, "w:bdr", _border80);
        }

        if (_shade is not null)
        {
            WmlWrite.Shading(sb, _shade);
        }
        else if (_shade80 is not null)
        {
            WmlWrite.Shading80(sb, _shade80);
        }

        if (_vert >= 0)
        {
            sb.Append("<w:vertAlign w:val=\"").Append(_vert switch { 1 => "superscript", 2 => "subscript", _ => "baseline" }).Append("\"/>");
        }

        if (_rtl)
        {
            sb.Append("<w:rtl/>");
        }

        if (_cs)
        {
            sb.Append("<w:cs/>");
        }

        if (_emphasis > 0)
        {
            string? em = _emphasis switch { 1 => "dot", 2 => "comma", 3 => "circle", 4 => "underDot", _ => null };
            if (em is not null)
            {
                sb.Append("<w:em w:val=\"").Append(em).Append("\"/>");
            }
        }

        if (_lidAscii != 0 || _lidFe != 0 || _lidBi != 0)
        {
            sb.Append("<w:lang");
            if (_lidAscii != 0)
            {
                sb.Append(" w:val=\"").Append(_lidAscii.ToString("X4", CultureInfo.InvariantCulture)).Append('"');
            }

            if (_lidFe != 0)
            {
                sb.Append(" w:eastAsia=\"").Append(_lidFe.ToString("X4", CultureInfo.InvariantCulture)).Append('"');
            }

            if (_lidBi != 0)
            {
                sb.Append(" w:bidi=\"").Append(_lidBi.ToString("X4", CultureInfo.InvariantCulture)).Append('"');
            }

            sb.Append("/>");
        }

        sb.Append(Toggle(0x0818, ctx, "specVanish"));
    }

    // [MS-DOC] Kul: underline kinds.
    private static string UnderlineName(int kul) => kul switch
    {
        0 => "none",
        1 => "single",
        2 => "words",
        3 => "double",
        4 => "dotted",
        6 => "thick",
        7 => "dash",
        9 => "dotDash",
        10 => "dotDotDash",
        11 => "wave",
        20 => "dottedHeavy",
        23 => "dashedHeavy",
        25 => "dashDotHeavy",
        26 => "dashDotDotHeavy",
        27 => "wavyHeavy",
        39 => "dashLong",
        43 => "wavyDouble",
        55 => "dashLongHeavy",
        _ => "single",
    };

    /// <summary>Gets a key that is equal for two formats that write the same elements, so runs can be joined.</summary>
    internal string Key(FormatContext ctx)
    {
        var sb = new StringBuilder();
        WriteInner(sb, ctx);
        sb.Append('|').Append(Revision).Append('|').Append(Revision == 1 ? InsAuthor : DelAuthor).Append('|').Append(Revision == 1 ? InsDate : DelDate);
        return sb.ToString();
    }
}

/// <summary>Paragraph properties collected from a list of modifiers, written as the content of <c>w:pPr</c>.</summary>
internal sealed class ParaFormat
{
    private int _istd = -1;
    private byte? _keepLines;
    private byte? _keepNext;
    private byte? _pageBreakBefore;
    private byte? _widow;
    private int _ilvl = -1;
    private int _ilfo = int.MinValue;
    private byte? _noLineNumbers;
    private readonly byte[]?[] _brc = new byte[6][];
    private readonly byte[]?[] _brc80 = new byte[6][];
    private byte[]? _shd;
    private byte[]? _shd80;
    private readonly SortedDictionary<int, byte> _tabAdd = [];
    private readonly SortedSet<int> _tabClear = [];
    private byte? _noAutoHyphen;
    private byte? _kinsoku;
    private byte? _wordWrap;
    private byte? _overflow;
    private byte? _topLine;
    private byte? _autoSpaceDE;
    private byte? _autoSpaceDN;
    private byte? _bidi;
    private byte? _adjustRight;
    private int _before = int.MinValue;
    private int _after = int.MinValue;
    private byte? _beforeAuto;
    private byte? _afterAuto;
    private int _beforeLines = int.MinValue;
    private int _afterLines = int.MinValue;
    private int _line = int.MinValue;
    private int _lineMult;
    private int _left = int.MinValue;
    private int _right = int.MinValue;
    private int _first = int.MinValue;
    private byte? _contextual;
    private byte? _mirror;
    private int _jc = -1;
    private int _outline = -1;
    private int _frameX = int.MinValue;
    private int _frameY = int.MinValue;
    private int _frameW = int.MinValue;
    private int _frameH = int.MinValue;
    private int _framePc = -1;
    private int _frameWrap = -1;
    private int _frameDropCap = -1;
    private int _frameFromTextH = int.MinValue;
    private int _frameFromTextV = int.MinValue;
    private int _align = -1;

    /// <summary>Gets the paragraph style set by <c>sprmPIstd</c> among the modifiers, or -1.</summary>
    internal int Istd => _istd;

    /// <summary>Gets the list index (<c>sprmPIlfo</c>) set directly, or <c>int.MinValue</c>.</summary>
    internal int Ilfo => _ilfo;

    /// <summary>Gets the list level set directly, or -1.</summary>
    internal int Ilvl => _ilvl;

    /// <summary>Gets the outline level, or -1.</summary>
    internal int OutlineLevel => _outline;

    /// <summary>Gets the left indent, or <c>int.MinValue</c>.</summary>
    internal int Left => _left;

    internal static ParaFormat From(IEnumerable<Prl> props)
    {
        var f = new ParaFormat();
        foreach (Prl p in props)
        {
            f.Apply(p);
        }

        return f;
    }

    internal void Apply(Prl p)
    {
        switch (p.Sprm)
        {
            case 0x4600:
                _istd = p.OperandU16;
                break;
            case 0x2405:
                _keepLines = p.OperandByte;
                break;
            case 0x2406:
                _keepNext = p.OperandByte;
                break;
            case 0x2407:
                _pageBreakBefore = p.OperandByte;
                break;
            case 0x2431:
                _widow = p.OperandByte;
                break;
            case 0x260A:
                _ilvl = p.OperandByte;
                break;
            case 0x460B:
                _ilfo = p.OperandI16;
                break;
            case 0x240C:
                _noLineNumbers = p.OperandByte;
                break;
            case 0x6424:
                _brc80[0] = Copy(p, 0, 4);
                break;
            case 0x6425:
                _brc80[1] = Copy(p, 0, 4);
                break;
            case 0x6426:
                _brc80[2] = Copy(p, 0, 4);
                break;
            case 0x6427:
                _brc80[3] = Copy(p, 0, 4);
                break;
            case 0x6428:
                _brc80[4] = Copy(p, 0, 4);
                break;
            case 0x6629:
                _brc80[5] = Copy(p, 0, 4);
                break;
            case 0xC64E:
                _brc[0] = Copy(p, 1, 8);
                break;
            case 0xC64F:
                _brc[1] = Copy(p, 1, 8);
                break;
            case 0xC650:
                _brc[2] = Copy(p, 1, 8);
                break;
            case 0xC651:
                _brc[3] = Copy(p, 1, 8);
                break;
            case 0xC652:
                _brc[4] = Copy(p, 1, 8);
                break;
            case 0xC653:
                _brc[5] = Copy(p, 1, 8);
                break;
            case 0xC64D:
                _shd = Copy(p, 1, 10);
                break;
            case 0x442D:
                _shd80 = Copy(p, 0, 2);
                break;
            case 0xC60D:
                ApplyTabs(p.Operand.Span, hasClose: false);
                break;
            case 0xC615:
                ApplyTabs(p.Operand.Span, hasClose: true);
                break;
            case 0x242A:
                _noAutoHyphen = p.OperandByte;
                break;
            case 0x2433:
                _kinsoku = p.OperandByte;
                break;
            case 0x2434:
                _wordWrap = p.OperandByte;
                break;
            case 0x2435:
                _overflow = p.OperandByte;
                break;
            case 0x2436:
                _topLine = p.OperandByte;
                break;
            case 0x2437:
                _autoSpaceDE = p.OperandByte;
                break;
            case 0x2438:
                _autoSpaceDN = p.OperandByte;
                break;
            case 0x2441:
                _bidi = p.OperandByte;
                break;
            case 0x2448:
                _adjustRight = p.OperandByte;
                break;
            case 0xA413:
                _before = p.OperandU16;
                break;
            case 0xA414:
                _after = p.OperandU16;
                break;
            case 0x245B:
                _beforeAuto = p.OperandByte;
                break;
            case 0x245C:
                _afterAuto = p.OperandByte;
                break;
            case 0x4458:
                _beforeLines = p.OperandU16;
                break;
            case 0x4459:
                _afterLines = p.OperandU16;
                break;
            case 0x6412:
                if (p.Operand.Length >= 4)
                {
                    _line = p.OperandI16;
                    _lineMult = BinaryPrimitives.ReadUInt16LittleEndian(p.Operand.Span[2..]);
                }

                break;
            case 0x845E:
            case 0x840F:
                _left = p.OperandI16;
                break;
            case 0x845D:
            case 0x840E:
                _right = p.OperandI16;
                break;
            case 0x8460:
            case 0x8411:
                _first = p.OperandI16;
                break;
            case 0x246D:
                _contextual = p.OperandByte;
                break;
            case 0x2470:
                _mirror = p.OperandByte;
                break;
            case 0x2461:
            case 0x2403:
                _jc = p.OperandByte;
                break;
            case 0x2640:
                _outline = p.OperandByte;
                break;
            case 0x8418:
                _frameX = p.OperandI16;
                break;
            case 0x8419:
                _frameY = p.OperandI16;
                break;
            case 0x841A:
                _frameW = p.OperandI16;
                break;
            case 0x442B:
                _frameH = p.OperandU16;
                break;
            case 0x261B:
                _framePc = p.OperandByte;
                break;
            case 0x2423:
                _frameWrap = p.OperandByte;
                break;
            case 0x442C:
                _frameDropCap = p.OperandU16;
                break;
            case 0x842F:
                _frameFromTextH = p.OperandI16;
                break;
            case 0x842E:
                _frameFromTextV = p.OperandI16;
                break;
            case 0x4439:
                _align = p.OperandU16;
                break;
        }
    }

    private static byte[]? Copy(Prl p, int skip, int n) => p.Operand.Length >= skip + n ? p.Operand.Span.Slice(skip, n).ToArray() : null;

    // [MS-DOC] PChgTabsPapxOperand / PChgTabsOperand: deleted tab stops, (for PChgTabs, their close-by stops,) then added stops with descriptors.
    private void ApplyTabs(ReadOnlySpan<byte> o, bool hasClose)
    {
        if (o.Length < 2 || o[0] == 255)
        {
            return;
        }

        int p = 1;
        int del = o[p++];
        if (p + (del * 2) > o.Length)
        {
            return;
        }

        for (int i = 0; i < del; i++)
        {
            int pos = BinaryPrimitives.ReadInt16LittleEndian(o[(p + (i * 2))..]);
            _tabClear.Add(pos);
            _tabAdd.Remove(pos);
        }

        p += del * 2;
        if (hasClose)
        {
            p += del * 2;
        }

        if (p >= o.Length)
        {
            return;
        }

        int add = o[p++];
        if (p + (add * 3) > o.Length)
        {
            return;
        }

        for (int i = 0; i < add; i++)
        {
            int pos = BinaryPrimitives.ReadInt16LittleEndian(o[(p + (i * 2))..]);
            byte tbd = o[p + (add * 2) + i];
            _tabAdd[pos] = tbd;
            _tabClear.Remove(pos);
        }
    }

    /// <summary>Gets a value indicating whether the paragraph is positioned as a frame.</summary>
    internal bool HasFrame => _frameX != int.MinValue || _frameY != int.MinValue || _frameW != int.MinValue || _frameH != int.MinValue || _framePc >= 0 || _frameDropCap > 0;

    private static string? On(byte? v, string e) => v is null ? null : v.Value == 0 ? "<w:" + e + " w:val=\"0\"/>" : v.Value == 1 ? "<w:" + e + "/>" : null;

    /// <summary>Appends the property elements in schema order, up to but not including <c>w:rPr</c>.</summary>
    internal void WriteInner(StringBuilder sb, FormatContext ctx, Func<int, string?> numId)
    {
        sb.Append(On(_keepNext, "keepNext")).Append(On(_keepLines, "keepLines")).Append(On(_pageBreakBefore, "pageBreakBefore"));
        if (HasFrame)
        {
            sb.Append("<w:framePr");
            if (_frameDropCap > 0)
            {
                sb.Append(" w:dropCap=\"").Append((_frameDropCap & 3) == 2 ? "margin" : "drop").Append("\" w:lines=\"").Append(WmlWrite.I(_frameDropCap >> 3)).Append('"');
            }

            if (_frameW > 0)
            {
                sb.Append(" w:w=\"").Append(WmlWrite.I(_frameW)).Append('"');
            }

            if (_frameH > 0)
            {
                sb.Append(" w:h=\"").Append(WmlWrite.I(_frameH & 0x7FFF)).Append("\" w:hRule=\"").Append((_frameH & 0x8000) != 0 ? "atLeast" : "exact").Append('"');
            }

            if (_frameFromTextV != int.MinValue)
            {
                sb.Append(" w:vSpace=\"").Append(WmlWrite.I(_frameFromTextV)).Append('"');
            }

            if (_frameFromTextH != int.MinValue)
            {
                sb.Append(" w:hSpace=\"").Append(WmlWrite.I(_frameFromTextH)).Append('"');
            }

            if (_frameWrap >= 0)
            {
                string? wrap = _frameWrap switch { 1 => "none", 2 => "around", 3 => "notBeside", 4 => "through", 5 => "tight", _ => null };
                if (wrap is not null)
                {
                    sb.Append(" w:wrap=\"").Append(wrap).Append('"');
                }
            }

            if (_framePc >= 0)
            {
                // [MS-DOC] sprmPPc: bits 4-5 are the vertical anchor, bits 6-7 the horizontal one (0 margin, 1 page, 2 column/text).
                sb.Append(" w:vAnchor=\"").Append(((_framePc >> 4) & 3) switch { 0 => "margin", 1 => "page", _ => "text" })
                    .Append("\" w:hAnchor=\"").Append(((_framePc >> 6) & 3) switch { 0 => "margin", 1 => "page", _ => "text" }).Append('"');
            }

            if (_frameX != int.MinValue)
            {
                sb.Append(Align("x", _frameX, "xAlign", "left", "center", "right", "inside", "outside"));
            }

            if (_frameY != int.MinValue)
            {
                sb.Append(Align("y", _frameY, "yAlign", "top", "center", "bottom", "inside", "outside"));
            }

            sb.Append("/>");
        }

        sb.Append(On(_widow, "widowControl"));
        if (_ilfo != int.MinValue)
        {
            if (numId(_ilfo) is { } n)
            {
                sb.Append("<w:numPr>");
                if (_ilvl >= 0)
                {
                    sb.Append("<w:ilvl w:val=\"").Append(WmlWrite.I(Math.Min(8, _ilvl))).Append("\"/>");
                }

                sb.Append("<w:numId w:val=\"").Append(n).Append("\"/></w:numPr>");
            }
            else if (_ilfo == 0)
            {
                sb.Append("<w:numPr><w:numId w:val=\"0\"/></w:numPr>");
            }
        }
        else if (_ilvl >= 0)
        {
            sb.Append("<w:numPr><w:ilvl w:val=\"").Append(WmlWrite.I(Math.Min(8, _ilvl))).Append("\"/></w:numPr>");
        }

        sb.Append(On(_noLineNumbers, "suppressLineNumbers"));
        bool anyBorder = false;
        foreach (byte[]? b in _brc)
        {
            anyBorder |= b is not null;
        }

        foreach (byte[]? b in _brc80)
        {
            anyBorder |= b is not null;
        }

        if (anyBorder)
        {
            var inner = new StringBuilder();
            string[] names = ["w:top", "w:left", "w:bottom", "w:right", "w:between", "w:bar"];
            for (int i = 0; i < 6; i++)
            {
                if (_brc[i] is { } b)
                {
                    WmlWrite.Border(inner, names[i], b);
                }
                else if (_brc80[i] is { } b80)
                {
                    WmlWrite.Border80(inner, names[i], b80);
                }
            }

            if (inner.Length > 0)
            {
                sb.Append("<w:pBdr>").Append(inner).Append("</w:pBdr>");
            }
        }

        if (_shd is not null)
        {
            WmlWrite.Shading(sb, _shd);
        }
        else if (_shd80 is not null)
        {
            WmlWrite.Shading80(sb, _shd80);
        }

        if (_tabAdd.Count > 0 || _tabClear.Count > 0)
        {
            sb.Append("<w:tabs>");
            foreach (int pos in _tabClear)
            {
                sb.Append("<w:tab w:val=\"clear\" w:pos=\"").Append(WmlWrite.I(pos)).Append("\"/>");
            }

            foreach (KeyValuePair<int, byte> t in _tabAdd)
            {
                string jc = (t.Value & 7) switch { 1 => "center", 2 => "right", 3 => "decimal", 4 => "bar", _ => "left" };
                string? lead = ((t.Value >> 3) & 7) switch { 1 => "dot", 2 => "hyphen", 3 => "underscore", 4 => "heavy", 5 => "middleDot", _ => null };
                sb.Append("<w:tab w:val=\"").Append(jc).Append('"');
                if (lead is not null)
                {
                    sb.Append(" w:leader=\"").Append(lead).Append('"');
                }

                sb.Append(" w:pos=\"").Append(WmlWrite.I(t.Key)).Append("\"/>");
            }

            sb.Append("</w:tabs>");
        }

        sb.Append(On(_noAutoHyphen, "suppressAutoHyphens")).Append(On(_kinsoku, "kinsoku")).Append(On(_wordWrap, "wordWrap")).Append(On(_overflow, "overflowPunct"))
            .Append(On(_topLine, "topLinePunct")).Append(On(_autoSpaceDE, "autoSpaceDE")).Append(On(_autoSpaceDN, "autoSpaceDN")).Append(On(_bidi, "bidi")).Append(On(_adjustRight, "adjustRightInd"));
        if (_before != int.MinValue || _after != int.MinValue || _line != int.MinValue || _beforeAuto is not null || _afterAuto is not null || _beforeLines != int.MinValue || _afterLines != int.MinValue)
        {
            sb.Append("<w:spacing");
            if (_before != int.MinValue)
            {
                sb.Append(" w:before=\"").Append(WmlWrite.I(_before)).Append('"');
            }

            if (_beforeLines != int.MinValue)
            {
                sb.Append(" w:beforeLines=\"").Append(WmlWrite.I(_beforeLines)).Append('"');
            }

            if (_beforeAuto is not null)
            {
                sb.Append(" w:beforeAutospacing=\"").Append(_beforeAuto.Value != 0 ? '1' : '0').Append('"');
            }

            if (_after != int.MinValue)
            {
                sb.Append(" w:after=\"").Append(WmlWrite.I(_after)).Append('"');
            }

            if (_afterLines != int.MinValue)
            {
                sb.Append(" w:afterLines=\"").Append(WmlWrite.I(_afterLines)).Append('"');
            }

            if (_afterAuto is not null)
            {
                sb.Append(" w:afterAutospacing=\"").Append(_afterAuto.Value != 0 ? '1' : '0').Append('"');
            }

            if (_line != int.MinValue)
            {
                // [MS-DOC] LSPD: with fMultLinespace the value is in 240ths of a line; otherwise positive is "at least" and negative "exactly".
                if (_lineMult != 0)
                {
                    sb.Append(" w:line=\"").Append(WmlWrite.I(Math.Max(0, _line))).Append("\" w:lineRule=\"auto\"");
                }
                else if (_line > 0)
                {
                    sb.Append(" w:line=\"").Append(WmlWrite.I(_line)).Append("\" w:lineRule=\"atLeast\"");
                }
                else if (_line < 0)
                {
                    sb.Append(" w:line=\"").Append(WmlWrite.I(-_line)).Append("\" w:lineRule=\"exact\"");
                }
            }

            sb.Append("/>");
        }

        if (_left != int.MinValue || _right != int.MinValue || _first != int.MinValue)
        {
            sb.Append("<w:ind");
            if (_left != int.MinValue)
            {
                sb.Append(" w:left=\"").Append(WmlWrite.I(_left)).Append('"');
            }

            if (_right != int.MinValue)
            {
                sb.Append(" w:right=\"").Append(WmlWrite.I(_right)).Append('"');
            }

            if (_first != int.MinValue)
            {
                sb.Append(_first < 0 ? " w:hanging=\"" : " w:firstLine=\"").Append(WmlWrite.I(Math.Abs(_first))).Append('"');
            }

            sb.Append("/>");
        }

        sb.Append(On(_contextual, "contextualSpacing")).Append(On(_mirror, "mirrorIndents"));
        if (_jc >= 0)
        {
            sb.Append("<w:jc w:val=\"").Append(_jc switch { 1 => "center", 2 => "right", 3 => "both", 4 => "distribute", 5 => "mediumKashida", 7 => "thaiDistribute", 8 => "highKashida", 9 => "lowKashida", _ => "left" }).Append("\"/>");
        }

        if (_align >= 0)
        {
            sb.Append("<w:textAlignment w:val=\"").Append(_align switch { 0 => "top", 1 => "center", 2 => "baseline", 3 => "bottom", _ => "auto" }).Append("\"/>");
        }

        if (_outline is >= 0 and <= 8)
        {
            sb.Append("<w:outlineLvl w:val=\"").Append(WmlWrite.I(_outline)).Append("\"/>");
        }
    }

    // [MS-DOC] sprmPDxaAbs / sprmPDyaAbs: small negative values are alignments.
    private static string Align(string pos, int v, string alignAttr, string a0, string a1, string a2, string a3, string a4)
    {
        string? name = v switch { -4 => a0, -8 => a1, -12 => a2, -16 => a3, -20 => a4, _ => null };
        return name is not null ? " w:" + alignAttr + "=\"" + name + "\"" : " w:" + pos + "=\"" + WmlWrite.I(v) + "\"";
    }
}
