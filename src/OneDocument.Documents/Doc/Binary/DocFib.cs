using System.Buffers.Binary;
using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Doc.Binary;

/// <summary>
/// The File Information Block at offset 0 of the <c>WordDocument</c> stream ([MS-DOC] section 2.5): the version, flags, the character
/// counts of each text story and the table of (offset, size) pairs that locate every other structure. Word 97 to 2013 files are read
/// by [MS-DOC]; Word 6.0 and Word 95 files (wIdent 0xA5DC) are read by the same accessors from their older layout, whose offsets are
/// not in [MS-DOC] (see <c>docs/doc.md</c>).
/// </summary>
public sealed class DocFib
{
    /// <summary>The number of fc/lcb pairs read at most.</summary>
    private const int MaxPairs = 160;

    private readonly uint[] _fc;
    private readonly uint[] _lcb;

    private DocFib(uint[] fc, uint[] lcb)
    {
        _fc = fc;
        _lcb = lcb;
    }

    /// <summary>Gets the effective <c>nFib</c>: FibRgCswNew.nFibNew when present, else FibBase.nFib ([MS-DOC] 2.5.14).</summary>
    public int NFib { get; private set; }

    /// <summary>Gets a value indicating whether this is a Word 6.0 or Word 95 file (before the Word 97 FIB).</summary>
    public bool IsLegacyWord6 { get; private set; }

    /// <summary>Gets the FibBase.wIdent.</summary>
    public int WIdent { get; private set; }

    /// <summary>Gets a value indicating whether this is a document template.</summary>
    public bool IsTemplate { get; private set; }

    /// <summary>Gets a value indicating whether the last save was an incremental one: the text may be in several pieces ("fast saved").</summary>
    public bool IsComplex { get; private set; }

    /// <summary>Gets a value indicating whether the file is encrypted or obfuscated.</summary>
    public bool IsEncrypted { get; private set; }

    /// <summary>Gets a value indicating whether the file uses XOR obfuscation rather than RC4.</summary>
    public bool IsObfuscated { get; private set; }

    /// <summary>Gets a value indicating whether the table stream is <c>1Table</c> (otherwise <c>0Table</c>). Word 6 and 95 have none.</summary>
    public bool UsesOneTable { get; private set; }

    /// <summary>Gets a value indicating whether the file contains only AutoText items.</summary>
    public bool IsGlossary { get; private set; }

    /// <summary>Gets FibBase.lKey: the XOR verifier, or the size of the encryption header at the start of the table stream.</summary>
    public int Key { get; private set; }

    /// <summary>Gets the install language (FibBase.lid).</summary>
    public int Lid { get; private set; }

    /// <summary>Gets the character set of 8-bit text in a Word 6/95 file (0 is ANSI); always 0 for Word 97 and later.</summary>
    public int Charset { get; private set; }

    /// <summary>Gets the count of characters in the main story.</summary>
    public int CcpText { get; private set; }

    /// <summary>Gets the count of characters in the footnote story.</summary>
    public int CcpFtn { get; private set; }

    /// <summary>Gets the count of characters in the header story.</summary>
    public int CcpHdd { get; private set; }

    /// <summary>Gets the count of characters in the macro story (Word 6/95 only; 0 otherwise).</summary>
    public int CcpMcr { get; private set; }

    /// <summary>Gets the count of characters in the comment story.</summary>
    public int CcpAtn { get; private set; }

    /// <summary>Gets the count of characters in the endnote story.</summary>
    public int CcpEdn { get; private set; }

    /// <summary>Gets the count of characters in the text box story.</summary>
    public int CcpTxbx { get; private set; }

    /// <summary>Gets the count of characters in the header text box story.</summary>
    public int CcpHdrTxbx { get; private set; }

    /// <summary>Gets the offset of the first text byte in a Word 6/95 file that has no piece table.</summary>
    public uint FcMin { get; private set; }

    /// <summary>Gets the offset just past the last text byte in a Word 6/95 file that has no piece table.</summary>
    public uint FcMac { get; private set; }

    /// <summary>Gets the number of fc/lcb pairs the file carries.</summary>
    public int PairCount => _fc.Length;

    /// <summary>Gets the sum of the story lengths, which is the number of characters of the document-wide character position space.</summary>
    public long CcpTotal => (long)CcpText + CcpFtn + CcpHdd + CcpMcr + CcpAtn + CcpEdn + CcpTxbx + CcpHdrTxbx;

    /// <summary>Gets an (offset, size) pair by its index in FibRgFcLcb97 ([MS-DOC] 2.5.6); (0, 0) when the file has fewer pairs.</summary>
    /// <param name="index">The zero-based index of the pair, for example 33 for <c>fcClx</c>/<c>lcbClx</c>.</param>
    /// <returns>The offset and size.</returns>
    public (uint Fc, uint Lcb) Pair(int index) => (uint)index < (uint)_fc.Length ? (_fc[index], _lcb[index]) : (0u, 0u);

    // The indexes into FibRgFcLcb97, in file order ([MS-DOC] 2.5.6).
    internal const int Stshf = 1;
    internal const int PlcffndRef = 2;
    internal const int PlcffndTxt = 3;
    internal const int PlcfandRef = 4;
    internal const int PlcfandTxt = 5;
    internal const int PlcfSed = 6;
    internal const int PlcfHdd = 11;
    internal const int PlcfBteChpx = 12;
    internal const int PlcfBtePapx = 13;
    internal const int SttbfFfn = 15;
    internal const int PlcfFldMom = 16;
    internal const int SttbfBkmk = 21;
    internal const int PlcfBkf = 22;
    internal const int PlcfBkl = 23;
    internal const int Dop = 31;
    internal const int Clx = 33;
    internal const int GrpXstAtnOwners = 36;
    internal const int PlcSpaMom = 40;
    internal const int PlcfendRef = 46;
    internal const int PlcfendTxt = 47;
    internal const int DggInfo = 50;
    internal const int SttbfRMark = 51;
    internal const int PlcftxbxTxt = 56;
    internal const int PlcfHdrtxbxTxt = 58;
    internal const int PlfLst = 73;
    internal const int PlfLfo = 74;
    internal const int SttbListNames = 90;

    /// <summary>Reads the FIB.</summary>
    /// <param name="word">The <c>WordDocument</c> stream.</param>
    /// <param name="sink">Where repairs are reported.</param>
    /// <param name="streamPath">The stream's path, for locations.</param>
    /// <returns>The FIB.</returns>
    /// <exception cref="OfficeFormatException">The stream is not a Word binary file or the FIB is cut short.</exception>
    internal static DocFib Read(ReadOnlySpan<byte> word, IDefectSink sink, string streamPath)
    {
        if (word.Length < 32)
        {
            throw Fatal("doc.fib-truncated", "The WordDocument stream is too short to hold a file information block.", streamPath, 0);
        }

        int wIdent = BinaryPrimitives.ReadUInt16LittleEndian(word);
        int nFib = BinaryPrimitives.ReadUInt16LittleEndian(word[2..]);
        bool old = wIdent == 0xA5DC || (wIdent == 0xA5EC && nFib < 0xC0);
        if (wIdent != 0xA5EC && wIdent != 0xA5DC)
        {
            throw Fatal("doc.bad-ident", $"The WordDocument stream starts with 0x{wIdent:X4}, not a Word binary file identifier (0xA5EC or 0xA5DC).", streamPath, 0);
        }

        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(word[10..]);
        var fib = new DocFib([], [])
        {
            WIdent = wIdent,
            NFib = nFib,
            IsLegacyWord6 = old,
        };

        return old ? ReadWord6(word, flags, fib, streamPath) : ReadWord97(word, flags, nFib, sink, streamPath);
    }

    private static DocFib ReadWord97(ReadOnlySpan<byte> w, ushort flags, int nFib, IDefectSink sink, string path)
    {
        // [MS-DOC] 2.5.15: read the base, then each counted section, never past the end of the stream.
        if (w.Length < 34)
        {
            throw Fatal("doc.fib-truncated", "The file information block is cut short after FibBase.", path, w.Length);
        }

        int pos = 32;
        int csw = BinaryPrimitives.ReadUInt16LittleEndian(w[pos..]);
        pos += 2 + (csw * 2);
        if (pos + 2 > w.Length)
        {
            throw Fatal("doc.fib-truncated", "The file information block is cut short in FibRgW97.", path, w.Length);
        }

        int cslw = BinaryPrimitives.ReadUInt16LittleEndian(w[pos..]);
        int lwStart = pos + 2;
        pos = lwStart + (cslw * 4);
        if (pos + 2 > w.Length || cslw < 11)
        {
            throw Fatal("doc.fib-truncated", "The file information block is cut short in FibRgLw97.", path, w.Length);
        }

        
        int pairs = BinaryPrimitives.ReadUInt16LittleEndian(w[pos..]);
        int pairStart = pos + 2;
        int available = Math.Max(0, (w.Length - pairStart) / 8);
        if (pairs > available)
        {
            sink.Report(new Defect(DefectKind.Record, "doc.fib-pairs-truncated", DefectSeverity.Repaired,
                $"The file information block declares {pairs} offset pairs but only {available} fit in the stream.", DefectLocation.InRecord(path, pos)));
            pairs = available;
        }

        pairs = Math.Min(pairs, MaxPairs);
        var fc = new uint[pairs];
        var lcb = new uint[pairs];
        for (int i = 0; i < pairs; i++)
        {
            fc[i] = BinaryPrimitives.ReadUInt32LittleEndian(w[(pairStart + (i * 8))..]);
            lcb[i] = BinaryPrimitives.ReadUInt32LittleEndian(w[(pairStart + (i * 8) + 4)..]);
        }

        int effective = nFib;
        int afterPairs = pairStart + (BinaryPrimitives.ReadUInt16LittleEndian(w[(lwStart + (cslw * 4))..]) * 8);
        if (afterPairs + 2 <= w.Length)
        {
            int cswNew = BinaryPrimitives.ReadUInt16LittleEndian(w[afterPairs..]);
            if (cswNew > 0 && afterPairs + 4 <= w.Length)
            {
                effective = BinaryPrimitives.ReadUInt16LittleEndian(w[(afterPairs + 2)..]);
            }
        }

        return new DocFib(fc, lcb)
        {
            WIdent = 0xA5EC,
            NFib = effective,
            IsLegacyWord6 = false,
            Lid = BinaryPrimitives.ReadUInt16LittleEndian(w[6..]),
            IsTemplate = (flags & 0x0001) != 0,
            IsGlossary = (flags & 0x0002) != 0,
            IsComplex = (flags & 0x0004) != 0,
            IsEncrypted = (flags & 0x0100) != 0,
            UsesOneTable = (flags & 0x0200) != 0,
            IsObfuscated = (flags & 0x8000) != 0,
            Key = BinaryPrimitives.ReadInt32LittleEndian(w[14..]),
            CcpText = Lw(w, lwStart, 3),
            CcpFtn = Lw(w, lwStart, 4),
            CcpHdd = Lw(w, lwStart, 5),
            CcpAtn = Lw(w, lwStart, 7),
            CcpEdn = Lw(w, lwStart, 8),
            CcpTxbx = Lw(w, lwStart, 9),
            CcpHdrTxbx = Lw(w, lwStart, 10),
        };
    }

    private static DocFib ReadWord6(ReadOnlySpan<byte> w, ushort flags, DocFib seed, string path)
    {
        // Word 6.0 and Word 95 (nFib 101 and 104): a single fixed-layout header. [MS-DOC] does not describe it; the offsets
        // below were settled on real Word 6/95 files (docs/doc.md): fcMin/fcMac bracket the text, the story lengths follow at
        // 0x34 in the order of the later format with a macro story after the headers, and the offset pairs start at 0x58 in the
        // order of FibRgFcLcb97.
        const int PairStart = 0x58;
        if (w.Length < PairStart + 8)
        {
            throw Fatal("doc.fib-truncated", "The Word 6/95 file information block is cut short.", path, w.Length);
        }

        
        int pairs = Math.Min(75, (w.Length - PairStart) / 8);
        var fc = new uint[pairs];
        var lcb = new uint[pairs];
        for (int i = 0; i < pairs; i++)
        {
            fc[i] = BinaryPrimitives.ReadUInt32LittleEndian(w[(PairStart + (i * 8))..]);
            lcb[i] = BinaryPrimitives.ReadUInt32LittleEndian(w[(PairStart + (i * 8) + 4)..]);
        }

        return new DocFib(fc, lcb)
        {
            WIdent = seed.WIdent,
            NFib = seed.NFib,
            IsLegacyWord6 = true,
            Lid = BinaryPrimitives.ReadUInt16LittleEndian(w[6..]),
            IsTemplate = (flags & 0x0001) != 0,
            IsGlossary = (flags & 0x0002) != 0,
            IsComplex = (flags & 0x0004) != 0,
            IsEncrypted = (flags & 0x0100) != 0,
            IsObfuscated = false,
            UsesOneTable = false,
            Key = Int(w, 14),
            Charset = BinaryPrimitives.ReadUInt16LittleEndian(w[0x14..]),
            FcMin = BinaryPrimitives.ReadUInt32LittleEndian(w[0x18..]),
            FcMac = BinaryPrimitives.ReadUInt32LittleEndian(w[0x1C..]),
            CcpText = Int(w, 0x34),
            CcpFtn = Int(w, 0x38),
            CcpHdd = Int(w, 0x3C),
            CcpMcr = Int(w, 0x40),
            CcpAtn = Int(w, 0x44),
            CcpEdn = Int(w, 0x48),
            CcpTxbx = Int(w, 0x4C),
            CcpHdrTxbx = Int(w, 0x50),
        };
    }

    private static int Lw(ReadOnlySpan<byte> w, int start, int i) => BinaryPrimitives.ReadInt32LittleEndian(w[(start + (i * 4))..]);

    private static int Int(ReadOnlySpan<byte> w, int off) => BinaryPrimitives.ReadInt32LittleEndian(w[off..]);

    private static OfficeFormatException Fatal(string code, string message, string path, long offset) =>
        new(DefectKind.Record, code, message, DefectLocation.InRecord(path, offset));
}
