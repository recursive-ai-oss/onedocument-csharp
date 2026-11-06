using System.Buffers.Binary;
using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Doc.Binary;

/// <summary>One run of text in the <c>WordDocument</c> stream: a <c>Pcd</c> of the piece table ([MS-DOC] 2.9.156).</summary>
/// <param name="CpStart">The first character position of the piece.</param>
/// <param name="CpEnd">The character position just past the piece.</param>
/// <param name="Offset">The byte offset of the piece's first character in the <c>WordDocument</c> stream (already halved for 8-bit text).</param>
/// <param name="IsEightBit">True if the text is stored as 8-bit characters (<c>FcCompressed.fCompressed</c>), false for UTF-16.</param>
/// <param name="Prm">The <c>Prm</c> that adds properties to every character of the piece.</param>
public readonly record struct Piece(int CpStart, int CpEnd, long Offset, bool IsEightBit, ushort Prm)
{
    /// <summary>Gets the byte offset in the stream of the character at <paramref name="cp"/>, which must lie inside the piece.</summary>
    /// <param name="cp">The character position.</param>
    /// <returns>The offset.</returns>
    public long OffsetOf(int cp) => Offset + ((long)(cp - CpStart) * (IsEightBit ? 1 : 2));
}

/// <summary>
/// The piece table: how character positions map to text in the <c>WordDocument</c> stream. A document saved in full has one piece (or
/// a few); a fast-saved one has many, in any file order ([MS-DOC] 2.9.38 <c>Clx</c>, 2.8.35 <c>PlcPcd</c>).
/// </summary>
public sealed class PieceTable
{
    private PieceTable(Piece[] pieces, List<ReadOnlyMemory<byte>> prcs)
    {
        Pieces = pieces;
        Prcs = prcs;
    }

    /// <summary>Gets the pieces in character-position order.</summary>
    public IReadOnlyList<Piece> Pieces { get; }

    /// <summary>Gets the property lists (<c>Prc</c>) that complex <c>Prm</c> values index.</summary>
    public IReadOnlyList<ReadOnlyMemory<byte>> Prcs { get; }

    /// <summary>Gets the number of characters the pieces cover.</summary>
    public int Length => Pieces.Count == 0 ? 0 : Pieces[^1].CpEnd;

    internal static PieceTable Single(long offset, int length, bool eightBit) =>
        new([new Piece(0, length, offset, eightBit, 0)], []);

    /// <summary>Reads the <c>Clx</c>.</summary>
    /// <param name="table">The stream the <c>Clx</c> is in.</param>
    /// <param name="fc">The <c>Clx</c>'s offset.</param>
    /// <param name="lcb">The <c>Clx</c>'s size.</param>
    /// <param name="wordLength">The length of the <c>WordDocument</c> stream, to range-check pieces.</param>
    /// <param name="sink">Where repairs are reported.</param>
    /// <param name="tablePath">The table stream's path, for locations.</param>
    /// <param name="word6">True for a Word 6/95 file, whose pieces are plain 8-bit offsets.</param>
    /// <returns>The piece table.</returns>
    /// <exception cref="OfficeFormatException">The <c>Clx</c> is missing or has no piece table.</exception>
    internal static PieceTable Read(ReadOnlySpan<byte> table, uint fc, uint lcb, long wordLength, IDefectSink sink, string tablePath, bool word6 = false)
    {
        if (lcb == 0 || fc >= table.Length)
        {
            throw new OfficeFormatException(DefectKind.Record, "doc.no-piece-table", "The file has no piece table (Clx).", DefectLocation.InRecord(tablePath, fc));
        }

        long end = Math.Min((long)fc + lcb, table.Length);
        if (end < (long)fc + lcb)
        {
            sink.Report(new Defect(DefectKind.Record, "doc.clx-truncated", DefectSeverity.Repaired, "The Clx runs past the end of the table stream; it was cut at the end.", DefectLocation.InRecord(tablePath, fc)));
        }

        var prcs = new List<ReadOnlyMemory<byte>>();
        int pos = (int)fc;
        while (pos < end)
        {
            byte clxt = table[pos];
            if (clxt == 1)
            {
                // [MS-DOC] Prc: clxt 0x01, a 16-bit size and that many bytes of Prl.
                if (pos + 3 > end)
                {
                    break;
                }

                int cb = BinaryPrimitives.ReadUInt16LittleEndian(table[(pos + 1)..]);
                if (pos + 3 + cb > end)
                {
                    sink.Report(new Defect(DefectKind.Record, "doc.clx-truncated", DefectSeverity.Repaired, "A Prc in the Clx runs past its end.", DefectLocation.InRecord(tablePath, pos)));
                    break;
                }

                prcs.Add(table.Slice(pos + 3, cb).ToArray());
                pos += 3 + cb;
                continue;
            }

            if (clxt != 2)
            {
                throw new OfficeFormatException(DefectKind.Record, "doc.clx-bad", $"The Clx holds an entry of type 0x{clxt:X2}, neither a Prc (1) nor the piece table (2).", DefectLocation.InRecord(tablePath, pos));
            }

            if (pos + 5 > end)
            {
                break;
            }

            long plcLen = BinaryPrimitives.ReadUInt32LittleEndian(table[(pos + 1)..]);
            int plc = pos + 5;
            if (plc + plcLen > end)
            {
                sink.Report(new Defect(DefectKind.Record, "doc.clx-truncated", DefectSeverity.Repaired, "The piece table runs past the end of the Clx; the whole pieces that fit were kept.", DefectLocation.InRecord(tablePath, pos)));
                plcLen = end - plc;
            }

            return ReadPlcPcd(table.Slice(plc, (int)plcLen), plc, prcs, wordLength, sink, tablePath, word6);
        }

        throw new OfficeFormatException(DefectKind.Record, "doc.no-piece-table", "The Clx holds no piece table.", DefectLocation.InRecord(tablePath, fc));
    }

    private static PieceTable ReadPlcPcd(ReadOnlySpan<byte> plc, int plcOffset, List<ReadOnlyMemory<byte>> prcs, long wordLength, IDefectSink sink, string tablePath, bool word6)
    {
        // [MS-DOC] 2.8.35 PlcPcd: n+1 character positions (4 bytes) then n Pcd (8 bytes).
        int n = (plc.Length - 4) / 12;
        if (n <= 0)
        {
            throw new OfficeFormatException(DefectKind.Record, "doc.no-piece-table", "The piece table is empty.", DefectLocation.InRecord(tablePath, plcOffset));
        }

        var pieces = new List<Piece>(n);
        int last = BinaryPrimitives.ReadInt32LittleEndian(plc);
        if (last != 0)
        {
            sink.Report(new Defect(DefectKind.Record, "doc.piece-cp-order", DefectSeverity.Repaired, "The first piece does not start at character position 0.", DefectLocation.InRecord(tablePath, plcOffset)));
        }

        for (int i = 0; i < n; i++)
        {
            int cp0 = BinaryPrimitives.ReadInt32LittleEndian(plc[(i * 4)..]);
            int cp1 = BinaryPrimitives.ReadInt32LittleEndian(plc[((i + 1) * 4)..]);
            if (cp0 < 0 || cp1 < cp0)
            {
                sink.Report(new Defect(DefectKind.Record, "doc.piece-cp-order", DefectSeverity.Dropped, $"Piece {i} has character positions {cp0} to {cp1}; it was dropped.", DefectLocation.InRecord(tablePath, plcOffset + (i * 4))));
                continue;
            }

            int pcd = ((n + 1) * 4) + (i * 8);
            uint fcRaw = BinaryPrimitives.ReadUInt32LittleEndian(plc[(pcd + 2)..]);
            ushort prm = BinaryPrimitives.ReadUInt16LittleEndian(plc[(pcd + 6)..]);

            // [MS-DOC] 2.9.73 FcCompressed: bit 30 is fCompressed; with it set the text is 8-bit at fc/2.
            // Word 6/95 text is always 8-bit and the offset is the plain byte offset (settled on real files, docs/doc.md).
            bool eight = word6 || (fcRaw & 0x40000000) != 0;
            long offset = word6 ? fcRaw : fcRaw & 0x3FFFFFFF;
            if (eight && !word6)
            {
                offset /= 2;
            }

            long bytes = (long)(cp1 - cp0) * (eight ? 1 : 2);
            if (offset + bytes > wordLength)
            {
                long fit = Math.Max(0, (wordLength - offset) / (eight ? 1 : 2));
                sink.Report(new Defect(DefectKind.Record, "doc.piece-out-of-range", DefectSeverity.Repaired,
                    $"Piece {i} reads past the end of the WordDocument stream; {cp1 - cp0 - fit} characters are missing.", DefectLocation.InRecord(tablePath, plcOffset + (n + 1) * 4 + (i * 8))));
                // Keep the character positions (other structures index them); the missing characters read as spaces.
            }

            pieces.Add(new Piece(cp0, cp1, offset, eight, prm));
        }

        return new PieceTable([.. pieces], prcs);
    }

    /// <summary>Finds the piece that holds a character position.</summary>
    /// <param name="cp">The position.</param>
    /// <returns>The index of the piece, or -1 if the position is outside every piece.</returns>
    public int Find(int cp)
    {
        int lo = 0;
        int hi = Pieces.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            Piece p = Pieces[mid];
            if (cp < p.CpStart)
            {
                hi = mid - 1;
            }
            else if (cp >= p.CpEnd)
            {
                lo = mid + 1;
            }
            else
            {
                return mid;
            }
        }

        return -1;
    }
}
