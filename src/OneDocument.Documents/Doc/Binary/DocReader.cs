using System.Buffers.Binary;
using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Doc.Binary;

/// <summary>A <c>Plc</c>: an array of character positions with a fixed-size record between each pair ([MS-DOC] 2.2.2).</summary>
/// <param name="Cps">The n+1 character positions (or n for a Plc without the closing position).</param>
/// <param name="Data">The data records, back to back.</param>
/// <param name="RecordSize">The size of one record.</param>
public sealed record Plc(int[] Cps, ReadOnlyMemory<byte> Data, int RecordSize)
{
    /// <summary>Gets the number of records.</summary>
    public int Count => RecordSize == 0 ? Math.Max(0, Cps.Length - 1) : Data.Length / RecordSize;

    /// <summary>Gets the bytes of record <paramref name="i"/>.</summary>
    /// <param name="i">The zero-based index.</param>
    /// <returns>The record.</returns>
    public ReadOnlySpan<byte> Record(int i) => Data.Span.Slice(i * RecordSize, RecordSize);

    /// <summary>An empty Plc.</summary>
    public static Plc None { get; } = new([], default, 0);
}

/// <summary>A paragraph of a story, found by walking the text for paragraph, cell and row marks ([MS-DOC] 2.4.2 to 2.4.5).</summary>
public sealed class DocParagraph
{
    /// <summary>Gets the first character position.</summary>
    public int Start { get; internal set; }

    /// <summary>Gets the position of the mark that ends the paragraph (0x0D, 0x07, or 0x0C for a section end).</summary>
    public int Mark { get; internal set; }

    /// <summary>Gets the table depth: 0 outside tables, 1 in a table, 2 in a table nested in a cell, and so on.</summary>
    public int Itap { get; internal set; }

    /// <summary>Gets a value indicating whether the mark ends a table cell.</summary>
    public bool IsCellEnd { get; internal set; }

    /// <summary>Gets a value indicating whether this is the row-end mark (a table terminating paragraph; it holds no text).</summary>
    public bool IsRowEnd { get; internal set; }

    /// <summary>Gets the style index stored with the paragraph's properties.</summary>
    public int Istd { get; internal set; }

    /// <summary>Gets the paragraph's direct properties, from its property run and its piece's <c>Prm</c>.</summary>
    public List<Prl> Props { get; internal set; } = [];

    /// <summary>Gets a value indicating whether the mark is a section end (a 0x0C that closes a section).</summary>
    public bool EndsSection { get; internal set; }
}

/// <summary>
/// Reads the structures of a Word binary file that sit on top of <see cref="DocFile"/>: the property runs, the paragraph walk, the
/// <c>Plc</c> arrays and the section list. One reader serves the text extractor and the conversion.
/// </summary>
public sealed class DocReader
{
    private readonly DocFile _file;
    private PropertyRuns? _chpx;
    private PropertyRuns? _papx;
    private int[]? _sectionEnds;

    /// <summary>Creates a reader.</summary>
    /// <param name="file">The opened file.</param>
    public DocReader(DocFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        _file = file;
    }

    /// <summary>Gets the file.</summary>
    public DocFile File => _file;

    /// <summary>Gets the character property runs, read on first use.</summary>
    public PropertyRuns CharacterRuns => _chpx ??= _file.Fib.IsLegacyWord6 ? PropertyRuns.Empty : PropertyRuns.Read(_file, DocFib.PlcfBteChpx, paragraph: false);

    /// <summary>Gets the paragraph property runs, read on first use.</summary>
    public PropertyRuns ParagraphRuns => _papx ??= _file.Fib.IsLegacyWord6 ? PropertyRuns.Empty : PropertyRuns.Read(_file, DocFib.PlcfBtePapx, paragraph: true);

    /// <summary>Reads a <c>Plc</c> from the table stream.</summary>
    /// <param name="pairIndex">The index of its offset pair in the FIB.</param>
    /// <param name="recordSize">The size of one data record (0 for a Plc with only positions).</param>
    /// <returns>The Plc, or <see cref="Plc.None"/> if the file has none or it is damaged.</returns>
    public Plc ReadPlc(int pairIndex, int recordSize)
    {
        (uint fc, uint lcb) = _file.Fib.Pair(pairIndex);
        if (lcb < 4 || fc >= _file.Table.Length)
        {
            return Plc.None;
        }

        long end = Math.Min((long)fc + lcb, _file.Table.Length);
        int len = (int)(end - fc);
        if (len < lcb)
        {
            _file.Context.Defects.Report(new Defect(DefectKind.Record, "doc.plc-truncated", DefectSeverity.Repaired, $"The structure at pair {pairIndex} runs past the end of the table stream.", DefectLocation.InRecord(_file.TablePath, fc)));
        }

        int n = recordSize == 0 ? (len / 4) - 1 : (len - 4) / (4 + recordSize);
        if (len < 4 || n < 0)
        {
            return Plc.None;
        }

        int cpCount = recordSize == 0 ? n + 1 : n + 1;
        var cps = new int[cpCount];
        ReadOnlySpan<byte> t = _file.Table.AsSpan((int)fc, len);
        for (int i = 0; i < cpCount; i++)
        {
            cps[i] = BinaryPrimitives.ReadInt32LittleEndian(t[(i * 4)..]);
        }

        ReadOnlyMemory<byte> data = recordSize == 0 ? default : _file.Table.AsMemory((int)fc + (cpCount * 4), n * recordSize);
        return new Plc(cps, data, recordSize);
    }

    /// <summary>Gets a value indicating whether even and odd pages have different headers and footers (<c>Dop.fFacingPages</c>, [MS-DOC] DopBase).</summary>
    public bool IsFacingPages
    {
        get
        {
            (uint fc, uint lcb) = _file.Fib.Pair(DocFib.Dop);
            return lcb >= 2 && (long)fc + 2 <= _file.Table.Length && (_file.Table[(int)fc] & 1) != 0;
        }
    }

    /// <summary>Gets the property modifiers of a section (<c>SEPX</c> found through <c>PlcfSed</c>, [MS-DOC] 2.8.26); empty for a Word 6/95 file or a section with defaults.</summary>
    /// <param name="section">The zero-based section index.</param>
    /// <returns>The modifiers.</returns>
    public List<Prl> SectionProperties(int section)
    {
        if (_file.Fib.IsLegacyWord6)
        {
            return [];
        }

        Plc sed = ReadPlc(DocFib.PlcfSed, 12);
        if (section < 0 || section >= sed.Count)
        {
            return [];
        }

        ReadOnlySpan<byte> rec = sed.Record(section);
        uint fcSepx = BinaryPrimitives.ReadUInt32LittleEndian(rec[2..]);
        if (fcSepx == 0xFFFFFFFF || (long)fcSepx + 2 > _file.Word.Length)
        {
            return [];
        }

        int cb = BinaryPrimitives.ReadUInt16LittleEndian(_file.Word.AsSpan((int)fcSepx));
        if ((long)fcSepx + 2 + cb > _file.Word.Length)
        {
            _file.Context.Defects.Report(new Defect(DefectKind.Record, "doc.sepx-bad", DefectSeverity.Dropped, $"The properties of section {section} run past the end of the WordDocument stream.", DefectLocation.InRecord(DocFile.WordPath, fcSepx)));
            return [];
        }

        List<Prl> list = SprmReader.Read(_file.Word.AsMemory((int)fcSepx + 2, cb), out _);
        // A text flow of 0 is the default (horizontal); writers store it anyway.
        list.RemoveAll(p => p.Sprm == 0x5033 && p.OperandU16 == 0);
        return list;
    }

    /// <summary>Gets the end positions of the sections of the main story ([MS-DOC] 2.8.26 <c>PlcfSed</c>).</summary>
    public IReadOnlyList<int> SectionEnds
    {
        get
        {
            if (_sectionEnds is not null)
            {
                return _sectionEnds;
            }

            Plc sed = ReadPlc(DocFib.PlcfSed, 12);
            var ends = new List<int>();
            for (int i = 1; i < sed.Cps.Length; i++)
            {
                ends.Add(sed.Cps[i]);
            }

            if (ends.Count == 0)
            {
                ends.Add(_file.Fib.CcpText);
            }

            return _sectionEnds = [.. ends];
        }
    }

    /// <summary>Converts a byte offset in the <c>WordDocument</c> stream to a character position inside a piece.</summary>
    /// <param name="piece">The piece.</param>
    /// <param name="fc">The byte offset, which may lie past the piece.</param>
    /// <returns>The character position, not beyond the piece's end.</returns>
    public static int CpOfOffset(Piece piece, long fc)
    {
        long d = (fc - piece.Offset) / (piece.IsEightBit ? 1 : 2);
        long cp = piece.CpStart + d;
        return (int)Math.Min(cp, piece.CpEnd);
    }

    /// <summary>Gets the direct character properties at a position, and where they stop applying.</summary>
    /// <param name="cp">The character position.</param>
    /// <param name="limit">Do not report a run end beyond this position.</param>
    /// <param name="runEnd">The first position after <paramref name="cp"/> where the properties may differ.</param>
    /// <returns>The modifiers: the character property run's, then those of the piece's <c>Prm</c>.</returns>
    public List<Prl> CharacterProperties(int cp, int limit, out int runEnd)
    {
        var props = new List<Prl>();
        int pi = _file.Pieces.Find(cp);
        runEnd = limit;
        if (pi < 0)
        {
            runEnd = Math.Min(limit, cp + 1);
            return props;
        }

        Piece piece = _file.Pieces.Pieces[pi];
        runEnd = Math.Min(limit, piece.CpEnd);
        long fc = piece.OffsetOf(cp);
        if (CharacterRuns.Find(fc, out long fcEnd, out ReadOnlyMemory<byte> grpprl, out _))
        {
            runEnd = Math.Min(runEnd, Math.Max(cp + 1, CpOfOffset(piece, fcEnd)));
            props.AddRange(SprmReader.Read(grpprl, out _));
        }
        else
        {
            runEnd = Math.Min(runEnd, Math.Max(cp + 1, CpOfOffset(piece, fcEnd)));
        }

        AppendPrm(props, piece, 2);
        return props;
    }

    /// <summary>Adds the modifiers a piece's <c>Prm</c> stands for, of one group (sgc), to a list.</summary>
    private void AppendPrm(List<Prl> props, Piece piece, int sgc)
    {
        ushort prm = piece.Prm;
        if (prm == 0)
        {
            return;
        }

        if ((prm & 1) == 0)
        {
            // [MS-DOC] Prm0: isprm (7 bits) selects the modifier, val (8 bits) is its one-byte operand.
            int isprm = (prm >> 1) & 0x7F;
            foreach ((byte i, ushort op) in SprmTable.Prm0)
            {
                if (i == isprm)
                {
                    if (((op >> 10) & 7) == sgc)
                    {
                        props.Add(new Prl(op, new byte[] { (byte)(prm >> 8) }));
                    }

                    break;
                }
            }

            return;
        }

        int ix = prm >> 1;
        if (ix < _file.Pieces.Prcs.Count)
        {
            foreach (Prl p in SprmReader.Read(_file.Pieces.Prcs[ix], out _))
            {
                if (p.Sgc == sgc || (sgc == 1 && p.Sgc == 5))
                {
                    props.Add(p);
                }
            }
        }
    }

    /// <summary>
    /// Walks a story and splits it into paragraphs by their marks, with each paragraph's table depth and direct properties
    /// ([MS-DOC] 2.4.2 to 2.4.6). Every character of the story belongs to exactly one paragraph; text after the last mark makes a
    /// final paragraph with no mark.
    /// </summary>
    /// <param name="start">The first character position.</param>
    /// <param name="length">The number of characters.</param>
    /// <param name="sectionEnds">Positions just after a section-ending 0x0C (main story only), else empty.</param>
    /// <returns>The paragraphs in order.</returns>
    public List<DocParagraph> Paragraphs(int start, int length, IReadOnlyCollection<int>? sectionEnds = null)
    {
        var result = new List<DocParagraph>();
        string text = _file.Text;
        int end = Math.Min(text.Length, start + length);
        int ps = start;
        HashSet<int>? sec = sectionEnds is { Count: > 0 } ? [.. sectionEnds] : null;
        for (int cp = start; cp < end; cp++)
        {
            char c = text[cp];
            bool mark = c == '\r' || c == '\a' || (c == '\f' && sec is not null && sec.Contains(cp + 1));
            if (!mark)
            {
                continue;
            }

            result.Add(Describe(ps, cp, c == '\f'));
            ps = cp + 1;
        }

        if (ps < end)
        {
            // No mark character: Mark is one past the text.
            result.Add(new DocParagraph { Start = ps, Mark = end });
        }

        if (_file.Fib.IsLegacyWord6)
        {
            // Word 6/95 files carry no row-end property: a cell mark that follows another cell mark with nothing between is the row end.
            for (int i = 1; i < result.Count; i++)
            {
                DocParagraph q = result[i];
                if (q.IsCellEnd && q.Mark == q.Start && result[i - 1].IsCellEnd)
                {
                    q.IsCellEnd = false;
                    q.IsRowEnd = true;
                }
            }
        }

        return result;
    }

    private DocParagraph Describe(int start, int mark, bool sectionEnd)
    {
        var p = new DocParagraph { Start = start, Mark = mark, EndsSection = sectionEnd };
        int pi = _file.Pieces.Find(mark);
        List<Prl> props = [];
        if (pi >= 0)
        {
            Piece piece = _file.Pieces.Pieces[pi];
            if (ParagraphRuns.Find(piece.OffsetOf(mark), out _, out ReadOnlyMemory<byte> grpprl, out int istd))
            {
                p.Istd = istd;
                props.AddRange(ExpandHuge(SprmReader.Read(grpprl, out _)));
            }

            AppendPrm(props, piece, 1);
        }

        p.Props = props;
        int itap = 0;
        bool inTable = false;
        bool ttp = false;
        bool innerCell = false;
        bool innerTtp = false;
        foreach (Prl pr in props)
        {
            switch (pr.Sprm)
            {
                case 0x2416:
                    inTable = pr.OperandByte != 0;
                    break;
                case 0x2417:
                    ttp = pr.OperandByte != 0;
                    break;
                case 0x6649:
                    itap = pr.OperandI32;
                    break;
                case 0x244B:
                    innerCell = pr.OperandByte != 0;
                    break;
                case 0x244C:
                    innerTtp = pr.OperandByte != 0;
                    break;
            }
        }

        if (itap == 0 && inTable)
        {
            itap = 1;
        }

        if (itap < 0)
        {
            itap = 0;
        }

        p.Itap = itap;
        char c = mark < _file.Text.Length ? _file.Text[mark] : '\0';
        if (itap > 0)
        {
            p.IsRowEnd = ttp || innerTtp;
            p.IsCellEnd = !p.IsRowEnd && (itap == 1 ? c == '\a' || innerCell : innerCell);
        }
        else if (c == '\a')
        {
            // A cell mark with no table properties (damaged or Word 6/95): treat it as depth 1.
            p.Itap = 1;
            p.IsCellEnd = true;
        }

        return p;
    }

    // [MS-DOC] sprmPHugePapx: the real modifier list is in the Data stream at the 4-byte offset in the operand.
    private List<Prl> ExpandHuge(List<Prl> props)
    {
        for (int i = 0; i < props.Count; i++)
        {
            if (props[i].Sprm != 0x6646)
            {
                continue;
            }

            int off = props[i].OperandI32;
            byte[] data = _file.Data;
            if (off >= 0 && off + 2 <= data.Length)
            {
                int cb = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(off));
                if (off + 2 + cb <= data.Length)
                {
                    props.RemoveAt(i);
                    props.InsertRange(i, SprmReader.Read(data.AsMemory(off + 2, cb), out _));
                    continue;
                }
            }

            _file.Context.Defects.Report(new Defect(DefectKind.Record, "doc.huge-papx-bad", DefectSeverity.Dropped, "sprmPHugePapx points outside the Data stream.", DefectLocation.InRecord("/Data", off)));
        }

        return props;
    }
}
