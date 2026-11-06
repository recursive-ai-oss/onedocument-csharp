using System.Buffers.Binary;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Doc.Binary;

/// <summary>One style of the stylesheet ([MS-DOC] 2.9.263 <c>STD</c>).</summary>
public sealed class DocStyle
{
    /// <summary>Gets the style's index in the stylesheet.</summary>
    public int Istd { get; internal set; }

    /// <summary>Gets the invariant identifier of an application-defined style, or 0x0FFE for a user-defined style.</summary>
    public int Sti { get; internal set; }

    /// <summary>Gets the style type: 1 paragraph, 2 character, 3 table, 4 numbering.</summary>
    public int Stk { get; internal set; }

    /// <summary>Gets the index of the style this one inherits from, or 0x0FFF for none.</summary>
    public int IstdBase { get; internal set; }

    /// <summary>Gets the index of the style that follows a paragraph of this style.</summary>
    public int IstdNext { get; internal set; }

    /// <summary>Gets the primary name followed by any aliases, separated by commas.</summary>
    public string Name { get; internal set; } = string.Empty;

    /// <summary>Gets the style's paragraph properties (empty for a character style).</summary>
    public List<Prl> ParagraphProps { get; internal set; } = [];

    /// <summary>Gets the style's character properties.</summary>
    public List<Prl> CharacterProps { get; internal set; } = [];
}

/// <summary>The stylesheet ([MS-DOC] 2.9.269 <c>STSH</c>): the styles by index and the default fonts.</summary>
public sealed class DocStyleSheet
{
    private readonly DocStyle?[] _styles;

    private DocStyleSheet(DocStyle?[] styles, int[] defaultFonts)
    {
        _styles = styles;
        DefaultFonts = defaultFonts;
    }

    /// <summary>Gets the styles by index; a null entry is an empty slot.</summary>
    public IReadOnlyList<DocStyle?> Styles => _styles;

    /// <summary>Gets the default font indexes: ASCII, East Asian, other and complex-script.</summary>
    public IReadOnlyList<int> DefaultFonts { get; }

    /// <summary>Gets a style by index.</summary>
    /// <param name="istd">The index.</param>
    /// <returns>The style, or null if the index is out of range or the slot is empty.</returns>
    public DocStyle? Get(int istd) => (uint)istd < (uint)_styles.Length ? _styles[istd] : null;

    /// <summary>Reads the stylesheet.</summary>
    /// <param name="file">The file.</param>
    /// <returns>The stylesheet; an empty one, with a defect, when the file's is damaged.</returns>
    public static DocStyleSheet Read(DocFile file)
    {
        (uint fc, uint lcb) = file.Fib.Pair(DocFib.Stshf);
        IDefectSink sink = file.Context.Defects;
        byte[] t = file.Table;
        if (file.Fib.IsLegacyWord6 || lcb < 18 || (long)fc + 20 > t.Length)
        {
            return new DocStyleSheet([], [0, 0, 0, 0]);
        }

        long end = Math.Min((long)fc + lcb, t.Length);
        ReadOnlySpan<byte> s = t.AsSpan((int)fc, (int)(end - fc));
        int cbStshi = BinaryPrimitives.ReadUInt16LittleEndian(s);
        if (2 + cbStshi > s.Length || cbStshi < 18)
        {
            sink.Report(new Defect(DefectKind.Record, "doc.stylesheet-bad", DefectSeverity.Dropped, "The stylesheet header is cut short; styles were dropped.", DefectLocation.InRecord(file.TablePath, fc)));
            return new DocStyleSheet([], [0, 0, 0, 0]);
        }

        // [MS-DOC] 2.9.270 Stshif: cstd, cbSTDBase, flags, stiMaxWhenSaved, istdMaxFixedWhenSaved, nVerBuiltInNamesWhenSaved, ftcAsci, ftcFE, ftcOther, ftcBi.
        int cstd = BinaryPrimitives.ReadUInt16LittleEndian(s[2..]);
        int cbStdBase = BinaryPrimitives.ReadUInt16LittleEndian(s[4..]);
        int[] fonts =
        [
            BinaryPrimitives.ReadUInt16LittleEndian(s[14..]),
            BinaryPrimitives.ReadUInt16LittleEndian(s[16..]),
            cbStshi >= 20 ? BinaryPrimitives.ReadUInt16LittleEndian(s[18..]) : 0,
            cbStshi >= 22 ? BinaryPrimitives.ReadUInt16LittleEndian(s[20..]) : 0,
        ];
        var styles = new DocStyle?[Math.Min(cstd, 4094)];
        int pos = 2 + cbStshi;
        for (int i = 0; i < styles.Length; i++)
        {
            if (pos + 2 > s.Length)
            {
                sink.Report(new Defect(DefectKind.Record, "doc.stylesheet-bad", DefectSeverity.Dropped, $"The stylesheet ends after {i} of {cstd} styles.", DefectLocation.InRecord(file.TablePath, fc + pos)));
                break;
            }

            int cbStd = BinaryPrimitives.ReadUInt16LittleEndian(s[pos..]);
            pos += 2;
            if (cbStd == 0)
            {
                continue;
            }

            if (pos + cbStd > s.Length)
            {
                sink.Report(new Defect(DefectKind.Record, "doc.stylesheet-bad", DefectSeverity.Dropped, $"Style {i} runs past the end of the stylesheet.", DefectLocation.InRecord(file.TablePath, fc + pos)));
                break;
            }

            styles[i] = ReadStd(t, (int)fc + pos, cbStd, cbStdBase, i, sink, file.TablePath);
            pos += cbStd;
        }

        return new DocStyleSheet(styles, fonts);
    }

    private static DocStyle? ReadStd(byte[] t, int at, int cb, int cbStdBase, int istd, IDefectSink sink, string path)
    {
        ReadOnlySpan<byte> s = t.AsSpan(at, cb);
        if (cbStdBase < 10 || cbStdBase > cb)
        {
            return null;
        }

        // [MS-DOC] 2.9.272 StdfBase.
        ushort w0 = BinaryPrimitives.ReadUInt16LittleEndian(s);
        ushort w1 = BinaryPrimitives.ReadUInt16LittleEndian(s[2..]);
        ushort w2 = BinaryPrimitives.ReadUInt16LittleEndian(s[4..]);
        var st = new DocStyle
        {
            Istd = istd,
            Sti = w0 & 0x0FFF,
            Stk = w1 & 0x0F,
            IstdBase = w1 >> 4,
            IstdNext = w2 >> 4,
        };
        int cupx = w2 & 0x0F;
        int pos = cbStdBase;

        // Xstz: a 16-bit character count, the characters, and a terminating null.
        if (pos + 2 > cb)
        {
            return st;
        }

        int cch = BinaryPrimitives.ReadUInt16LittleEndian(s[pos..]);
        pos += 2;
        if (pos + (cch * 2) > cb)
        {
            sink.Report(new Defect(DefectKind.Record, "doc.stylesheet-bad", DefectSeverity.Repaired, $"The name of style {istd} runs past the style.", DefectLocation.InRecord(path, at)));
            return st;
        }

        st.Name = Encoding.Unicode.GetString(s.Slice(pos, cch * 2));
        pos += (cch * 2) + 2;
        if ((pos & 1) != 0)
        {
            pos++;
        }

        // [MS-DOC] GrLPUpxSw: cupx length-prefixed property blocks, each padded to an even length.
        var blocks = new List<ReadOnlyMemory<byte>>();
        for (int u = 0; u < cupx && pos + 2 <= cb; u++)
        {
            int cbUpx = BinaryPrimitives.ReadUInt16LittleEndian(s[pos..]);
            pos += 2;
            if (pos + cbUpx > cb)
            {
                cbUpx = cb - pos;
            }

            blocks.Add(t.AsMemory(at + pos, cbUpx));
            pos += cbUpx + (cbUpx & 1);
        }

        // Paragraph style: PAPX (istd + grpprl), CHPX. Character style: CHPX. Table style: TAPX, PAPX, CHPX.
        // The paragraph block starts with the style's own istd, which is not a modifier.
        static List<Prl> Para(ReadOnlyMemory<byte> b) => b.Length >= 2 ? SprmReader.Read(b[2..], out _) : [];
        switch (st.Stk)
        {
            case 1:
                if (blocks.Count > 0)
                {
                    st.ParagraphProps = Para(blocks[0]);
                }

                if (blocks.Count > 1)
                {
                    st.CharacterProps = SprmReader.Read(blocks[1], out _);
                }

                break;
            case 2:
                if (blocks.Count > 0)
                {
                    st.CharacterProps = SprmReader.Read(blocks[0], out _);
                }

                break;
            case 3:
                if (blocks.Count > 1)
                {
                    st.ParagraphProps = Para(blocks[1]);
                }

                if (blocks.Count > 2)
                {
                    st.CharacterProps = SprmReader.Read(blocks[2], out _);
                }

                break;
        }

        return st;
    }

    /// <summary>
    /// Tells whether a toggle character property is on in a style, following the inheritance chain upwards. Direct formatting that says
    /// "same as the style" (0x80) or "opposite of the style" (0x81) needs this ([MS-DOC] ToggleOperand).
    /// </summary>
    /// <param name="istdPara">The paragraph style.</param>
    /// <param name="istdChar">The character style, or -1.</param>
    /// <param name="sprm">The toggle modifier.</param>
    /// <returns>True if the property is on.</returns>
    public bool ToggleInStyle(int istdPara, int istdChar, ushort sprm)
    {
        bool? v = istdChar >= 0 ? Walk(istdChar, sprm) : null;
        v ??= Walk(istdPara, sprm);
        return v ?? false;
    }

    private bool? Walk(int istd, ushort sprm)
    {
        bool value = false;
        bool any = false;
        var seen = new HashSet<int>();
        // Collect from the base up so that nearer styles override.
        var chain = new List<DocStyle>();
        for (DocStyle? s = Get(istd); s is not null && seen.Add(s.Istd); s = Get(s.IstdBase))
        {
            chain.Add(s);
        }

        for (int i = chain.Count - 1; i >= 0; i--)
        {
            foreach (Prl p in chain[i].CharacterProps)
            {
                if (p.Sprm != sprm)
                {
                    continue;
                }

                any = true;
                value = p.OperandByte switch
                {
                    0 => false,
                    1 => true,
                    0x80 => value,
                    0x81 => !value,
                    _ => value,
                };
            }
        }

        return any ? value : null;
    }
}

/// <summary>The font table ([MS-DOC] <c>SttbfFfn</c>): font names by index.</summary>
public sealed class DocFontTable
{
    private readonly List<string> _names;

    private DocFontTable(List<string> names) => _names = names;

    /// <summary>Gets the number of fonts.</summary>
    public int Count => _names.Count;

    /// <summary>Gets a font's name.</summary>
    /// <param name="ftc">The font index.</param>
    /// <returns>The name, or null if the index is out of range.</returns>
    public string? Name(int ftc) => (uint)ftc < (uint)_names.Count ? _names[ftc] : null;

    /// <summary>Reads the font table.</summary>
    /// <param name="file">The file.</param>
    /// <returns>The table; empty if the file has none.</returns>
    public static DocFontTable Read(DocFile file)
    {
        (uint fc, uint lcb) = file.Fib.Pair(DocFib.SttbfFfn);
        var names = new List<string>();
        byte[] t = file.Table;
        if (lcb < 4 || (long)fc + 4 > t.Length)
        {
            return new DocFontTable(names);
        }

        long end = Math.Min((long)fc + lcb, t.Length);
        int pos = (int)fc;
        int cData = BinaryPrimitives.ReadUInt16LittleEndian(t.AsSpan(pos));
        pos += 4;
        for (int i = 0; i < cData && pos < end; i++)
        {
            // [MS-DOC] FFN: cbFfnM1 (size of the rest), then ffid, weight, charset, alt-name index, panose (10), signature (24), name.
            int cb = t[pos] + 1;
            if (pos + cb > end || cb < 41)
            {
                break;
            }

            int nameAt = pos + 40;
            int nameEnd = nameAt;
            while (nameEnd + 1 < pos + cb && (t[nameEnd] != 0 || t[nameEnd + 1] != 0))
            {
                nameEnd += 2;
            }

            names.Add(Encoding.Unicode.GetString(t, nameAt, nameEnd - nameAt));
            pos += cb;
        }

        return new DocFontTable(names);
    }
}

/// <summary>One level of a list definition ([MS-DOC] <c>LVL</c>).</summary>
public sealed class DocListLevel
{
    /// <summary>Gets the number the level starts at.</summary>
    public int StartAt { get; internal set; }

    /// <summary>Gets the number format (MSONFC): 0 decimal, 1 upper Roman, 2 lower Roman, 3 upper letter, 4 lower letter, 23 bullet, 255 none.</summary>
    public int Nfc { get; internal set; }

    /// <summary>Gets the justification: 0 left, 1 center, 2 right.</summary>
    public int Justify { get; internal set; }

    /// <summary>Gets a value indicating whether numbers of earlier levels show as Arabic.</summary>
    public bool Legal { get; internal set; }

    /// <summary>Gets a value indicating whether the sequence does not restart after a higher level.</summary>
    public bool NoRestart { get; internal set; }

    /// <summary>Gets what follows the number: 0 tab, 1 space, 2 nothing.</summary>
    public int Follow { get; internal set; }

    /// <summary>Gets the one-based positions in <see cref="Text"/> where level numbers go.</summary>
    public byte[] Placeholders { get; internal set; } = [];

    /// <summary>Gets the paragraph properties of the level.</summary>
    public List<Prl> ParagraphProps { get; internal set; } = [];

    /// <summary>Gets the character properties of the number text.</summary>
    public List<Prl> CharacterProps { get; internal set; } = [];

    /// <summary>Gets the number text with level placeholders as characters 0 to 8.</summary>
    public string Text { get; internal set; } = string.Empty;
}

/// <summary>A list definition ([MS-DOC] <c>LSTF</c> with its levels).</summary>
public sealed class DocList
{
    /// <summary>Gets the list identifier.</summary>
    public int Lsid { get; internal set; }

    /// <summary>Gets a value indicating whether the list has one level.</summary>
    public bool IsSimple { get; internal set; }

    /// <summary>Gets the styles linked to each level, or 0x0FFF.</summary>
    public int[] LinkedStyles { get; internal set; } = [];

    /// <summary>Gets the levels.</summary>
    public List<DocListLevel> Levels { get; internal set; } = [];
}

/// <summary>A list override ([MS-DOC] <c>LFO</c>): which list a paragraph's list index means, and level overrides.</summary>
public sealed class DocListOverride
{
    /// <summary>Gets the identifier of the list definition it refers to.</summary>
    public int Lsid { get; internal set; }

    /// <summary>Gets the per-level overrides: the level, the start number (or null) and the replacement level (or null).</summary>
    public List<(int Level, int? StartAt, DocListLevel? Format)> Levels { get; internal set; } = [];
}

/// <summary>The list tables of a document: <c>PlfLst</c> and <c>PlfLfo</c>.</summary>
public sealed class DocLists
{
    /// <summary>Gets the list definitions.</summary>
    public List<DocList> Lists { get; } = [];

    /// <summary>Gets the list overrides; a paragraph's <c>sprmPIlfo</c> is a one-based index into this.</summary>
    public List<DocListOverride> Overrides { get; } = [];

    /// <summary>Reads the list tables.</summary>
    /// <param name="file">The file.</param>
    /// <returns>The tables; empty if the file has none.</returns>
    public static DocLists Read(DocFile file)
    {
        var result = new DocLists();
        if (file.Fib.IsLegacyWord6)
        {
            return result;
        }

        byte[] t = file.Table;
        IDefectSink sink = file.Context.Defects;
        (uint fc, uint lcb) = file.Fib.Pair(DocFib.PlfLst);
        if (lcb >= 2 && (long)fc + 2 <= t.Length)
        {
            long end = Math.Min((long)fc + lcb, t.Length);
            int pos = (int)fc;
            int cLst = BinaryPrimitives.ReadUInt16LittleEndian(t.AsSpan(pos));
            pos += 2;
            var heads = new List<DocList>();
            for (int i = 0; i < cLst && pos + 28 <= end; i++)
            {
                var l = new DocList
                {
                    Lsid = BinaryPrimitives.ReadInt32LittleEndian(t.AsSpan(pos)),
                    IsSimple = (t[pos + 26] & 1) != 0,
                    LinkedStyles = new int[9],
                };
                for (int k = 0; k < 9; k++)
                {
                    l.LinkedStyles[k] = BinaryPrimitives.ReadInt16LittleEndian(t.AsSpan(pos + 8 + (k * 2)));
                }

                heads.Add(l);
                pos += 28;
            }

            // The levels follow all the headers, in order. They are not counted in lcbPlfLst: they run on up to the list overrides
            // (settled on real files, docs/doc.md).
            (uint lfoFc, _) = file.Fib.Pair(DocFib.PlfLfo);
            long lvlEnd = lfoFc > fc && lfoFc <= t.Length ? lfoFc : t.Length;
            end = Math.Max(end, lvlEnd);
            foreach (DocList l in heads)
            {
                int n = l.IsSimple ? 1 : 9;
                for (int k = 0; k < n; k++)
                {
                    DocListLevel? lv = ReadLvl(t, ref pos, (int)end);
                    if (lv is null)
                    {
                        sink.Report(new Defect(DefectKind.Record, "doc.lists-bad", DefectSeverity.Dropped, "The list definitions end early; the remaining levels were dropped.", DefectLocation.InRecord(file.TablePath, pos)));
                        break;
                    }

                    l.Levels.Add(lv);
                }

                result.Lists.Add(l);
            }
        }

        (fc, lcb) = file.Fib.Pair(DocFib.PlfLfo);
        if (lcb >= 4 && (long)fc + 4 <= t.Length)
        {
            long end = Math.Min((long)fc + lcb, t.Length);
            int pos = (int)fc;
            int lfoMac = BinaryPrimitives.ReadInt32LittleEndian(t.AsSpan(pos));
            pos += 4;
            var counts = new List<(int Lsid, int Clfolvl)>();
            for (int i = 0; i < lfoMac && pos + 16 <= end; i++)
            {
                counts.Add((BinaryPrimitives.ReadInt32LittleEndian(t.AsSpan(pos)), t[pos + 12]));
                pos += 16;
            }

            foreach ((int lsid, int clfolvl) in counts)
            {
                var o = new DocListOverride { Lsid = lsid };
                pos += 4; // LFOData.cp
                for (int k = 0; k < clfolvl && pos + 8 <= end; k++)
                {
                    int start = BinaryPrimitives.ReadInt32LittleEndian(t.AsSpan(pos));
                    uint flags = BinaryPrimitives.ReadUInt32LittleEndian(t.AsSpan(pos + 4));
                    pos += 8;
                    int lvl = (int)(flags & 0x0F);
                    bool fStart = (flags & 0x10) != 0;
                    bool fFormat = (flags & 0x20) != 0;
                    DocListLevel? lvf = null;
                    if (fFormat)
                    {
                        lvf = ReadLvl(t, ref pos, (int)end);
                    }

                    o.Levels.Add((lvl, fStart ? start : null, lvf));
                }

                result.Overrides.Add(o);
            }
        }

        return result;
    }

    private static DocListLevel? ReadLvl(byte[] t, ref int pos, int end)
    {
        if (pos + 28 > end)
        {
            return null;
        }

        var lv = new DocListLevel
        {
            StartAt = BinaryPrimitives.ReadInt32LittleEndian(t.AsSpan(pos)),
            Nfc = t[pos + 4],
            Justify = t[pos + 5] & 3,
            Legal = (t[pos + 5] & 4) != 0,
            NoRestart = (t[pos + 5] & 8) != 0,
            Placeholders = t.AsSpan(pos + 6, 9).ToArray(),
            Follow = t[pos + 15],
        };
        int cbChpx = t[pos + 24];
        int cbPapx = t[pos + 25];
        int p = pos + 28;
        if (p + cbPapx + cbChpx + 2 > end)
        {
            return null;
        }

        lv.ParagraphProps = SprmReader.Read(t.AsMemory(p, cbPapx), out _);
        p += cbPapx;
        lv.CharacterProps = SprmReader.Read(t.AsMemory(p, cbChpx), out _);
        p += cbChpx;
        int cch = BinaryPrimitives.ReadUInt16LittleEndian(t.AsSpan(p));
        p += 2;
        if (p + (cch * 2) > end)
        {
            return null;
        }

        lv.Text = Encoding.Unicode.GetString(t, p, cch * 2);
        pos = p + (cch * 2);
        return lv;
    }
}
