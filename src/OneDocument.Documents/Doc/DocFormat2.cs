using System.Buffers.Binary;
using System.Text;
using RecursiveAI.OneDocument.Doc.Binary;

namespace RecursiveAI.OneDocument.Doc;

/// <summary>One cell of a table row as the row-end paragraph's modifiers define it ([MS-DOC] TC80 and the cell modifiers).</summary>
internal sealed class CellDef
{
    internal int Width;
    internal int HorzMerge;
    internal int VertMerge;
    internal int VertAlign;
    internal bool NoWrap;
    internal readonly byte[]?[] Brc80 = new byte[4][];
    internal readonly byte[]?[] Brc = new byte[6][];
    internal byte[]? Shd;
    internal byte[]? Shd80;
}

/// <summary>Row and cell properties collected from the modifiers of a table row's end mark, written as <c>w:tblPr</c>, <c>w:trPr</c> and <c>w:tcPr</c>.</summary>
internal sealed class TableRowFormat
{
    private int _left;
    private bool _leftSet;
    private readonly byte[]?[] _tblBrc80 = new byte[6][];
    private readonly byte[]?[] _tblBrc = new byte[6][];

    internal List<CellDef> Cells { get; } = [];

    internal int Jc { get; private set; } = -1;

    internal bool CantSplit { get; private set; }

    internal bool Header { get; private set; }

    internal int RowHeight { get; private set; }

    internal int GapHalf { get; private set; } = -1;

    internal bool Bidi { get; private set; }

    internal bool Autofit { get; private set; } = true;

    internal int PaddingLeft { get; private set; } = -1;

    internal int PaddingRight { get; private set; } = -1;

    internal int PaddingTop { get; private set; } = -1;

    internal int PaddingBottom { get; private set; } = -1;

    /// <summary>Gets the number of modifiers of the row the reader does not carry (merges, splits).</summary>
    internal int Unmapped { get; private set; }

    /// <summary>Gets the absolute positions of the cell edges: the left edge of the row, then each cell's right edge.</summary>
    internal int[] Edges()
    {
        var e = new int[Cells.Count + 1];
        e[0] = _left;
        for (int i = 0; i < Cells.Count; i++)
        {
            e[i + 1] = e[i] + Cells[i].Width;
        }

        return e;
    }

    internal static TableRowFormat From(IEnumerable<Prl> props)
    {
        var f = new TableRowFormat();
        foreach (Prl p in props)
        {
            f.Apply(p);
        }

        return f;
    }

    private void Apply(Prl p)
    {
        ReadOnlySpan<byte> o = p.Operand.Span;
        switch (p.Sprm)
        {
            case 0xD608:
                DefTable(o);
                break;
            case 0x9601:
                _left = p.OperandI16;
                _leftSet = true;
                break;
            case 0x9602:
                GapHalf = p.OperandI16;
                break;
            case 0x5400:
            case 0x548A:
                Jc = p.OperandU16;
                break;
            case 0x3403:
            case 0x3466:
                CantSplit = p.OperandByte != 0;
                break;
            case 0x3404:
                Header = p.OperandByte != 0;
                break;
            case 0x9407:
                RowHeight = p.OperandI16;
                break;
            case 0x560B:
            case 0x5664:
                Bidi = p.OperandU16 != 0;
                break;
            case 0x3615:
                Autofit = p.OperandByte != 0;
                break;
            case 0xD605:
                if (o.Length >= 25)
                {
                    for (int i = 0; i < 6; i++)
                    {
                        _tblBrc80[i] = o.Slice(1 + (i * 4), 4).ToArray();
                    }
                }

                break;
            case 0xD613:
                if (o.Length >= 49)
                {
                    for (int i = 0; i < 6; i++)
                    {
                        _tblBrc[i] = o.Slice(1 + (i * 8), 8).ToArray();
                    }
                }

                break;
            case 0xD634:
                // TCellPaddingDefault: cb, itcFirst, itcLim, grfbrc, ftsWidth, wWidth
                if (o.Length >= 7)
                {
                    int w = BinaryPrimitives.ReadUInt16LittleEndian(o[5..]);
                    int g = o[3];
                    if ((g & 1) != 0)
                    {
                        PaddingTop = w;
                    }

                    if ((g & 2) != 0)
                    {
                        PaddingLeft = w;
                    }

                    if ((g & 4) != 0)
                    {
                        PaddingBottom = w;
                    }

                    if ((g & 8) != 0)
                    {
                        PaddingRight = w;
                    }
                }

                break;
            case 0x7621:
                // TInsert: itcFirst, ctc, dxaCol
                if (o.Length >= 4)
                {
                    int at = Math.Min(o[0], Cells.Count);
                    int ctc = o[1];
                    int w = BinaryPrimitives.ReadUInt16LittleEndian(o[2..]);
                    for (int i = 0; i < ctc && Cells.Count < 63; i++)
                    {
                        Cells.Insert(at, new CellDef { Width = w });
                    }
                }

                break;
            case 0x5622:
                if (o.Length >= 2)
                {
                    int a = Math.Min((int)o[0], Cells.Count);
                    int b = Math.Min((int)o[1], Cells.Count);
                    if (b > a)
                    {
                        Cells.RemoveRange(a, b - a);
                    }
                }

                break;
            case 0x7623:
                // TDxaCol: itcFirst, itcLim, dxaCol
                if (o.Length >= 4)
                {
                    int w = BinaryPrimitives.ReadUInt16LittleEndian(o[2..]);
                    for (int i = o[0]; i < Math.Min((int)o[1], Cells.Count); i++)
                    {
                        Cells[i].Width = w;
                    }
                }

                break;
            case 0x5624:
            case 0x5625:
                Unmapped++;
                break;
            case 0xD62F:
                // TSetBrc: cb, itcFirst, itcLim, bordersToApply, brc(8)
                if (o.Length >= 12)
                {
                    for (int c = o[1]; c < Math.Min((int)o[2], Cells.Count); c++)
                    {
                        for (int s = 0; s < 6; s++)
                        {
                            if ((o[3] & (1 << s)) != 0)
                            {
                                Cells[c].Brc[s] = o.Slice(4, 8).ToArray();
                            }
                        }
                    }
                }

                break;
            case 0xD62D:
                // TSetShd: cb, itcFirst, itcLim, shd(10)
                if (o.Length >= 13)
                {
                    for (int c = o[1]; c < Math.Min((int)o[2], Cells.Count); c++)
                    {
                        Cells[c].Shd = o.Slice(3, 10).ToArray();
                    }
                }

                break;
            case 0xD612:
            case 0xD616:
            case 0xD60C:
                {
                    int baseCell = p.Sprm == 0xD612 ? 0 : p.Sprm == 0xD616 ? 22 : 44;
                    if (o.Length >= 1)
                    {
                        int n = Math.Min(o[0], o.Length - 1) / 10;
                        for (int i = 0; i < n && baseCell + i < Cells.Count; i++)
                        {
                            Cells[baseCell + i].Shd = o.Slice(1 + (i * 10), 10).ToArray();
                        }
                    }

                    break;
                }

            case 0xD609:
                if (o.Length >= 1)
                {
                    int n = Math.Min(o[0], o.Length - 1) / 2;
                    for (int i = 0; i < n && i < Cells.Count; i++)
                    {
                        Cells[i].Shd80 = o.Slice(1 + (i * 2), 2).ToArray();
                    }
                }

                break;
            case 0xD62C:
                // TVertAlign: cb, itcFirst, itcLim, vertAlign
                if (o.Length >= 4)
                {
                    for (int c = o[1]; c < Math.Min((int)o[2], Cells.Count); c++)
                    {
                        Cells[c].VertAlign = o[3];
                    }
                }

                break;
            case 0xD62B:
                // TVertMerge: cb, itc, fVertMerge/fVertRestart
                if (o.Length >= 4)
                {
                    int c = o[1];
                    if (c < Cells.Count)
                    {
                        Cells[c].VertMerge = (o[3] & 2) != 0 ? 3 : (o[3] & 1) != 0 ? 1 : 0;
                    }
                }

                break;
        }
    }

    // [MS-DOC] TDefTableOperand: a 16-bit size, the cell count, count+1 cell edges, then one TC80 per cell.
    private void DefTable(ReadOnlySpan<byte> o)
    {
        if (o.Length < 3)
        {
            return;
        }

        int n = Math.Min((int)o[2], 63);
        if (3 + ((n + 1) * 2) > o.Length)
        {
            n = Math.Max(0, (o.Length - 3) / 2 - 1);
        }

        Cells.Clear();
        int[] edges = new int[n + 1];
        for (int i = 0; i <= n; i++)
        {
            edges[i] = BinaryPrimitives.ReadInt16LittleEndian(o[(3 + (i * 2))..]);
        }

        if (!_leftSet)
        {
            _left = edges[0];
        }

        int tc = 3 + ((n + 1) * 2);
        for (int i = 0; i < n; i++)
        {
            var c = new CellDef { Width = Math.Max(0, edges[i + 1] - edges[i]) };
            if (tc + ((i + 1) * 20) <= o.Length)
            {
                ReadOnlySpan<byte> t = o.Slice(tc + (i * 20), 20);
                int grf = BinaryPrimitives.ReadUInt16LittleEndian(t);
                c.HorzMerge = grf & 3;
                c.VertMerge = (grf >> 5) & 3;
                c.VertAlign = (grf >> 7) & 3;
                c.NoWrap = (grf & 0x2000) != 0;
                for (int b = 0; b < 4; b++)
                {
                    c.Brc80[b] = t.Slice(4 + (b * 4), 4).ToArray();
                }
            }

            Cells.Add(c);
        }
    }

    internal void WriteTableProperties(StringBuilder sb)
    {
        if (Jc >= 0)
        {
            sb.Append("<w:jc w:val=\"").Append(Jc switch { 1 => "center", 2 => "right", _ => "left" }).Append("\"/>");
        }

        if (_leftSet || Cells.Count > 0)
        {
            int ind = _left + Math.Max(0, GapHalf);
            if (ind != 0)
            {
                sb.Append("<w:tblInd w:w=\"").Append(WmlWrite.I(ind)).Append("\" w:type=\"dxa\"/>");
            }
        }

        var inner = new StringBuilder();
        string[] names = ["w:top", "w:left", "w:bottom", "w:right", "w:insideH", "w:insideV"];
        for (int i = 0; i < 6; i++)
        {
            if (_tblBrc[i] is { } b)
            {
                WmlWrite.Border(inner, names[i], b);
            }
            else if (_tblBrc80[i] is { } b80)
            {
                WmlWrite.Border80(inner, names[i], b80);
            }
        }

        if (inner.Length > 0)
        {
            sb.Append("<w:tblBorders>").Append(inner).Append("</w:tblBorders>");
        }

        if (!Autofit)
        {
            sb.Append("<w:tblLayout w:type=\"fixed\"/>");
        }

        if (GapHalf >= 0 || PaddingLeft >= 0 || PaddingRight >= 0 || PaddingTop >= 0 || PaddingBottom >= 0)
        {
            sb.Append("<w:tblCellMar>");
            if (PaddingTop >= 0)
            {
                sb.Append("<w:top w:w=\"").Append(WmlWrite.I(PaddingTop)).Append("\" w:type=\"dxa\"/>");
            }

            int left = PaddingLeft >= 0 ? PaddingLeft : GapHalf;
            if (left >= 0)
            {
                sb.Append("<w:left w:w=\"").Append(WmlWrite.I(left)).Append("\" w:type=\"dxa\"/>");
            }

            if (PaddingBottom >= 0)
            {
                sb.Append("<w:bottom w:w=\"").Append(WmlWrite.I(PaddingBottom)).Append("\" w:type=\"dxa\"/>");
            }

            int right = PaddingRight >= 0 ? PaddingRight : GapHalf;
            if (right >= 0)
            {
                sb.Append("<w:right w:w=\"").Append(WmlWrite.I(right)).Append("\" w:type=\"dxa\"/>");
            }

            sb.Append("</w:tblCellMar>");
        }

        if (Bidi)
        {
            sb.Append("<w:bidiVisual/>");
        }
    }

    internal void WriteRowProperties(StringBuilder sb)
    {
        if (CantSplit)
        {
            sb.Append("<w:cantSplit/>");
        }

        if (RowHeight != 0)
        {
            sb.Append("<w:trHeight w:val=\"").Append(WmlWrite.I(Math.Abs(RowHeight))).Append("\" w:hRule=\"").Append(RowHeight < 0 ? "exact" : "atLeast").Append("\"/>");
        }

        if (Header)
        {
            sb.Append("<w:tblHeader/>");
        }
    }

    /// <summary>Writes the properties of a cell: its width, grid span, merges, borders, shading and vertical alignment.</summary>
    internal static void WriteCellProperties(StringBuilder sb, CellDef c, int width, int span, int vMerge)
    {
        sb.Append("<w:tcW w:w=\"").Append(WmlWrite.I(width)).Append("\" w:type=\"dxa\"/>");
        if (span > 1)
        {
            sb.Append("<w:gridSpan w:val=\"").Append(WmlWrite.I(span)).Append("\"/>");
        }

        if (vMerge == 3)
        {
            sb.Append("<w:vMerge w:val=\"restart\"/>");
        }
        else if (vMerge == 1)
        {
            sb.Append("<w:vMerge/>");
        }

        var inner = new StringBuilder();
        string[] names = ["w:top", "w:left", "w:bottom", "w:right", "w:insideH", "w:insideV"];
        for (int i = 0; i < 4; i++)
        {
            if (c.Brc[i] is { } b)
            {
                WmlWrite.Border(inner, names[i], b);
            }
            else if (c.Brc80[i] is { } b80)
            {
                WmlWrite.Border80(inner, names[i], b80);
            }
        }

        if (inner.Length > 0)
        {
            sb.Append("<w:tcBorders>").Append(inner).Append("</w:tcBorders>");
        }

        if (c.Shd is not null)
        {
            WmlWrite.Shading(sb, c.Shd);
        }
        else if (c.Shd80 is not null)
        {
            WmlWrite.Shading80(sb, c.Shd80);
        }

        if (c.NoWrap)
        {
            sb.Append("<w:noWrap/>");
        }

        if (c.VertAlign > 0)
        {
            sb.Append("<w:vAlign w:val=\"").Append(c.VertAlign == 1 ? "center" : "bottom").Append("\"/>");
        }
    }
}

/// <summary>Section properties collected from a section's modifiers, written as <c>w:sectPr</c>.</summary>
internal sealed class SectionFormat
{
    private int _bkc = 2;
    private bool _titlePage;
    private int _cols;
    private int _colSpace = 720;
    private bool _evenly = true;
    private bool _lineBetween;
    private readonly Dictionary<int, int> _colWidth = [];
    private readonly Dictionary<int, int> _colSpacing = [];
    private int _nfcPgn;
    private bool _pgnRestart;
    private int _pgnStart = -1;
    private int _orient = 1;
    private int _pgW = 12240;
    private int _pgH = 15840;
    private int _left = 1800;
    private int _right = 1800;
    private int _top = 1440;
    private int _bottom = 1440;
    private int _gutter;
    private int _hdrTop = 720;
    private int _hdrBottom = 720;
    private int _vjc;
    private int _lnc = -1;
    private int _lnMod;
    private int _lnDist;
    private int _lnStart;
    private bool _bidi;
    private bool _rtlGutter;
    private readonly byte[]?[] _brc80 = new byte[4][];
    private int _pgbProp = -1;
    private int _charSpace = int.MinValue;
    private int _linePitch = int.MinValue;
    private int _clm = -1;
    private int _ftnFmt = -1;
    private int _ftnStart = -1;
    private int _ftnRestart = -1;
    private int _ednFmt = -1;
    private int _ednStart = -1;
    private int _ednRestart = -1;

    internal bool TitlePage => _titlePage;

    internal static SectionFormat From(IEnumerable<Prl> props)
    {
        var f = new SectionFormat();
        foreach (Prl p in props)
        {
            f.Apply(p);
        }

        return f;
    }

    private void Apply(Prl p)
    {
        switch (p.Sprm)
        {
            case 0x3009:
                _bkc = p.OperandByte;
                break;
            case 0x300A:
                _titlePage = p.OperandByte != 0;
                break;
            case 0x500B:
                _cols = p.OperandU16;
                break;
            case 0x900C:
                _colSpace = p.OperandI16;
                break;
            case 0xF203:
                if (p.Operand.Length >= 3)
                {
                    _colWidth[p.Operand.Span[0]] = BinaryPrimitives.ReadUInt16LittleEndian(p.Operand.Span[1..]);
                }

                break;
            case 0xF204:
                if (p.Operand.Length >= 3)
                {
                    _colSpacing[p.Operand.Span[0]] = BinaryPrimitives.ReadUInt16LittleEndian(p.Operand.Span[1..]);
                }

                break;
            case 0x3005:
                _evenly = p.OperandByte != 0;
                break;
            case 0x3019:
                _lineBetween = p.OperandByte != 0;
                break;
            case 0x300E:
                _nfcPgn = p.OperandByte;
                break;
            case 0x3011:
                _pgnRestart = p.OperandByte != 0;
                break;
            case 0x501C:
                _pgnStart = p.OperandU16;
                break;
            case 0x301D:
                _orient = p.OperandByte;
                break;
            case 0xB01F:
                _pgW = p.OperandU16;
                break;
            case 0xB020:
                _pgH = p.OperandU16;
                break;
            case 0xB021:
                _left = p.OperandU16;
                break;
            case 0xB022:
                _right = p.OperandU16;
                break;
            case 0x9023:
                _top = p.OperandI16;
                break;
            case 0x9024:
                _bottom = p.OperandI16;
                break;
            case 0xB025:
                _gutter = p.OperandU16;
                break;
            case 0xB017:
                _hdrTop = p.OperandU16;
                break;
            case 0xB018:
                _hdrBottom = p.OperandU16;
                break;
            case 0x301A:
                _vjc = p.OperandByte;
                break;
            case 0x3013:
                _lnc = p.OperandByte;
                break;
            case 0x5015:
                _lnMod = p.OperandU16;
                break;
            case 0x9016:
                _lnDist = p.OperandI16;
                break;
            case 0x501B:
                _lnStart = p.OperandU16;
                break;
            case 0x3228:
                _bidi = p.OperandByte != 0;
                break;
            case 0x322A:
                _rtlGutter = p.OperandByte != 0;
                break;
            case 0x702B:
                _brc80[0] = p.Operand.Length >= 4 ? p.Operand.Span[..4].ToArray() : null;
                break;
            case 0x702C:
                _brc80[1] = p.Operand.Length >= 4 ? p.Operand.Span[..4].ToArray() : null;
                break;
            case 0x702D:
                _brc80[2] = p.Operand.Length >= 4 ? p.Operand.Span[..4].ToArray() : null;
                break;
            case 0x702E:
                _brc80[3] = p.Operand.Length >= 4 ? p.Operand.Span[..4].ToArray() : null;
                break;
            case 0x522F:
                _pgbProp = p.OperandU16;
                break;
            case 0x7030:
                _charSpace = p.OperandI32;
                break;
            case 0x9031:
                _linePitch = p.OperandI16;
                break;
            case 0x5032:
                _clm = p.OperandU16;
                break;
            case 0x5040:
                _ftnFmt = p.OperandByte;
                break;
            case 0x503F:
                _ftnStart = p.OperandU16;
                break;
            case 0x303C:
                _ftnRestart = p.OperandByte;
                break;
            case 0x5042:
                _ednFmt = p.OperandByte;
                break;
            case 0x5041:
                _ednStart = p.OperandU16;
                break;
            case 0x303E:
                _ednRestart = p.OperandByte;
                break;
        }
    }

    // [MS-DOC] sprmSNfcFtnRef, sprmSNFtn, sprmSRncFtn (and the endnote forms): number format, first number, and when numbering restarts.
    private static void NoteProps(StringBuilder sb, string element, int fmt, int start, int restart)
    {
        if (fmt < 0 && start < 0 && restart < 0)
        {
            return;
        }

        sb.Append('<').Append(element).Append('>');
        if (fmt >= 0)
        {
            sb.Append("<w:numFmt w:val=\"").Append(DocConverter.NumFormat(fmt)).Append("\"/>");
        }

        if (start >= 0)
        {
            sb.Append("<w:numStart w:val=\"").Append(WmlWrite.I(start)).Append("\"/>");
        }

        if (restart >= 0)
        {
            sb.Append("<w:numRestart w:val=\"").Append(restart switch { 1 => "eachSect", 2 => "eachPage", _ => "continuous" }).Append("\"/>");
        }

        sb.Append("</").Append(element).Append('>');
    }

    /// <summary>Writes the section properties; <paramref name="references"/> is the header and footer references already written as XML.</summary>
    internal void Write(StringBuilder sb, string references)
    {
        sb.Append("<w:sectPr>").Append(references);
        NoteProps(sb, "w:footnotePr", _ftnFmt, _ftnStart, _ftnRestart);
        NoteProps(sb, "w:endnotePr", _ednFmt, _ednStart, _ednRestart);

        string? type = _bkc switch { 0 => "continuous", 1 => "nextColumn", 3 => "evenPage", 4 => "oddPage", _ => null };
        if (type is not null)
        {
            sb.Append("<w:type w:val=\"").Append(type).Append("\"/>");
        }

        sb.Append("<w:pgSz w:w=\"").Append(WmlWrite.I(_pgW)).Append("\" w:h=\"").Append(WmlWrite.I(_pgH)).Append('"');
        if (_orient == 2)
        {
            sb.Append(" w:orient=\"landscape\"");
        }

        sb.Append("/><w:pgMar w:top=\"").Append(WmlWrite.I(_top)).Append("\" w:right=\"").Append(WmlWrite.I(_right)).Append("\" w:bottom=\"").Append(WmlWrite.I(_bottom))
            .Append("\" w:left=\"").Append(WmlWrite.I(_left)).Append("\" w:header=\"").Append(WmlWrite.I(_hdrTop)).Append("\" w:footer=\"").Append(WmlWrite.I(_hdrBottom))
            .Append("\" w:gutter=\"").Append(WmlWrite.I(_gutter)).Append("\"/>");
        bool anyBrc = false;
        foreach (byte[]? b in _brc80)
        {
            anyBrc |= b is not null;
        }

        if (anyBrc)
        {
            // [MS-DOC] sprmSPgbProp: bits 0-2 which pages, bits 3-4 depth (front/back), bits 5-7 offset from (text/page edge).
            int apply = _pgbProp >= 0 ? _pgbProp & 7 : 0;
            int offsetFrom = _pgbProp >= 0 ? (_pgbProp >> 5) & 7 : 0;
            sb.Append("<w:pgBorders w:offsetFrom=\"").Append(offsetFrom == 1 ? "page" : "text").Append('"');
            if (_pgbProp >= 0 && ((_pgbProp >> 3) & 3) == 1)
            {
                sb.Append(" w:zOrder=\"back\"");
            }

            if (apply == 1)
            {
                sb.Append(" w:display=\"firstPage\"");
            }
            else if (apply == 2)
            {
                sb.Append(" w:display=\"notFirstPage\"");
            }

            sb.Append('>');
            string[] names = ["w:top", "w:left", "w:bottom", "w:right"];
            for (int i = 0; i < 4; i++)
            {
                if (_brc80[i] is { } b)
                {
                    WmlWrite.Border80(sb, names[i], b);
                }
            }

            sb.Append("</w:pgBorders>");
        }

        if (_lnc >= 0 || _lnMod > 0)
        {
            sb.Append("<w:lnNumType w:countBy=\"").Append(WmlWrite.I(Math.Max(1, _lnMod))).Append('"');
            if (_lnStart > 0)
            {
                sb.Append(" w:start=\"").Append(WmlWrite.I(_lnStart + 1)).Append('"');
            }

            if (_lnDist > 0)
            {
                sb.Append(" w:distance=\"").Append(WmlWrite.I(_lnDist)).Append('"');
            }

            sb.Append(" w:restart=\"").Append(_lnc switch { 1 => "newSection", 2 => "continuous", _ => "newPage" }).Append("\"/>");
        }

        if (_pgnRestart || _pgnStart >= 0 || _nfcPgn != 0)
        {
            sb.Append("<w:pgNumType");
            string? fmt = _nfcPgn switch { 1 => "upperRoman", 2 => "lowerRoman", 3 => "upperLetter", 4 => "lowerLetter", _ => null };
            if (fmt is not null)
            {
                sb.Append(" w:fmt=\"").Append(fmt).Append('"');
            }

            if (_pgnRestart)
            {
                sb.Append(" w:start=\"").Append(WmlWrite.I(Math.Max(0, _pgnStart))).Append('"');
            }

            sb.Append("/>");
        }

        if (_cols > 0)
        {
            int n = _cols + 1;
            sb.Append("<w:cols w:num=\"").Append(WmlWrite.I(n)).Append('"');
            if (_lineBetween)
            {
                sb.Append(" w:sep=\"1\"");
            }

            if (_evenly || _colWidth.Count == 0)
            {
                sb.Append(" w:space=\"").Append(WmlWrite.I(_colSpace)).Append("\"/>");
            }
            else
            {
                sb.Append(" w:equalWidth=\"0\">");
                for (int i = 0; i < n; i++)
                {
                    int w = _colWidth.TryGetValue(i, out int cw) ? cw : 0;
                    sb.Append("<w:col w:w=\"").Append(WmlWrite.I(w)).Append('"');
                    if (i < n - 1)
                    {
                        sb.Append(" w:space=\"").Append(WmlWrite.I(_colSpacing.TryGetValue(i, out int cs) ? cs : _colSpace)).Append('"');
                    }

                    sb.Append("/>");
                }

                sb.Append("</w:cols>");
            }
        }

        if (_vjc != 0)
        {
            sb.Append("<w:vAlign w:val=\"").Append(_vjc switch { 1 => "center", 2 => "both", _ => "bottom" }).Append("\"/>");
        }

        if (_titlePage)
        {
            sb.Append("<w:titlePg/>");
        }

        if (_bidi)
        {
            sb.Append("<w:bidi/>");
        }

        if (_rtlGutter)
        {
            sb.Append("<w:rtlGutter/>");
        }

        if (_clm >= 0 || _linePitch != int.MinValue)
        {
            string? kind = _clm switch { 1 => "lines", 2 => "linesAndChars", 3 => "snapToChars", _ => null };
            sb.Append("<w:docGrid");
            if (kind is not null)
            {
                sb.Append(" w:type=\"").Append(kind).Append('"');
            }

            if (_linePitch != int.MinValue)
            {
                sb.Append(" w:linePitch=\"").Append(WmlWrite.I(_linePitch)).Append('"');
            }

            if (_charSpace != int.MinValue && _clm is 2 or 3)
            {
                sb.Append(" w:charSpace=\"").Append(WmlWrite.I(_charSpace)).Append('"');
            }

            sb.Append("/>");
        }

        sb.Append("</w:sectPr>");
    }
}
