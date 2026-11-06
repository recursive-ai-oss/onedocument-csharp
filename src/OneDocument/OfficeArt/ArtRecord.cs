using System.Buffers.Binary;
using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.OfficeArt;

/// <summary>
/// The eight-byte header in front of every OfficeArt record, and of every record in the PowerPoint binary format, which reuses the same layout.
/// </summary>
/// <param name="Version">The four-bit <c>recVer</c>; 0xF marks a container whose payload is a sequence of records.</param>
/// <param name="Instance">The twelve-bit <c>recInstance</c>; its meaning depends on the record type (a count, a shape type, a BLIP type…).</param>
/// <param name="Type">The sixteen-bit <c>recType</c>.</param>
/// <param name="Length">The <c>recLen</c> field: the number of payload bytes that follow the header, as stored (it may run past the end of the data).</param>
public readonly record struct ArtRecordHeader(int Version, int Instance, int Type, long Length)
{
    /// <summary>The size of a record header in bytes.</summary>
    public const int Size = 8;

    /// <summary>True when the payload is a sequence of child records.</summary>
    public bool IsContainer => Version == 0xF;

    /// <summary>Reads a header from the first eight bytes of <paramref name="data"/>.</summary>
    /// <param name="data">The bytes to read.</param>
    /// <param name="header">The header, when there are at least eight bytes.</param>
    /// <returns>False when fewer than eight bytes remain.</returns>
    public static bool TryRead(ReadOnlySpan<byte> data, out ArtRecordHeader header)
    {
        if (data.Length < Size)
        {
            header = default;
            return false;
        }

        // [MS-ODRAW] §2.2.1: recVer is the low four bits of the first word, recInstance the high twelve; recType and recLen follow.
        ushort verInstance = BinaryPrimitives.ReadUInt16LittleEndian(data);
        header = new ArtRecordHeader(verInstance & 0xF, verInstance >> 4, BinaryPrimitives.ReadUInt16LittleEndian(data[2..]), BinaryPrimitives.ReadUInt32LittleEndian(data[4..]));
        return true;
    }
}

/// <summary>Record types of the OfficeArt layer ([MS-ODRAW] §2.2) that readers dispatch on.</summary>
public static class ArtRecordType
{
    /// <summary>OfficeArtDggContainer: the drawing group (shared by all drawings of a file).</summary>
    public const int DggContainer = 0xF000;

    /// <summary>OfficeArtBStoreContainer: the BLIP store.</summary>
    public const int BStoreContainer = 0xF001;

    /// <summary>OfficeArtDgContainer: one drawing.</summary>
    public const int DgContainer = 0xF002;

    /// <summary>OfficeArtSpgrContainer: a group of shapes.</summary>
    public const int SpgrContainer = 0xF003;

    /// <summary>OfficeArtSpContainer: one shape.</summary>
    public const int SpContainer = 0xF004;

    /// <summary>OfficeArtSolverContainer: connector rules.</summary>
    public const int SolverContainer = 0xF005;

    /// <summary>OfficeArtFDGG: the drawing group header.</summary>
    public const int Fdgg = 0xF006;

    /// <summary>OfficeArtFBSE: a BLIP store entry.</summary>
    public const int Bse = 0xF007;

    /// <summary>OfficeArtFDG: the drawing header.</summary>
    public const int Fdg = 0xF008;

    /// <summary>OfficeArtFSPGR: the coordinate space of a group.</summary>
    public const int Fspgr = 0xF009;

    /// <summary>OfficeArtFSP: the shape header.</summary>
    public const int Fsp = 0xF00A;

    /// <summary>OfficeArtFOPT: the primary property table.</summary>
    public const int Fopt = 0xF00B;

    /// <summary>OfficeArtClientTextbox: host-defined text.</summary>
    public const int ClientTextbox = 0xF00D;

    /// <summary>OfficeArtChildAnchor: the position of a shape inside a group.</summary>
    public const int ChildAnchor = 0xF00F;

    /// <summary>OfficeArtClientAnchor: the host-defined position of a top-level shape.</summary>
    public const int ClientAnchor = 0xF010;

    /// <summary>OfficeArtClientData: host-defined shape data.</summary>
    public const int ClientData = 0xF011;

    /// <summary>OfficeArtFConnectorRule.</summary>
    public const int ConnectorRule = 0xF012;

    /// <summary>OfficeArtSecondaryFOPT.</summary>
    public const int SecondaryFopt = 0xF121;

    /// <summary>OfficeArtTertiaryFOPT.</summary>
    public const int TertiaryFopt = 0xF122;

    /// <summary>OfficeArtSplitMenuColorContainer.</summary>
    public const int SplitMenuColors = 0xF11E;

    /// <summary>The first BLIP record type (EMF).</summary>
    public const int BlipFirst = 0xF018;

    /// <summary>OfficeArtBlipEMF.</summary>
    public const int BlipEmf = 0xF01A;

    /// <summary>OfficeArtBlipWMF.</summary>
    public const int BlipWmf = 0xF01B;

    /// <summary>OfficeArtBlipPICT.</summary>
    public const int BlipPict = 0xF01C;

    /// <summary>OfficeArtBlipJPEG.</summary>
    public const int BlipJpeg = 0xF01D;

    /// <summary>OfficeArtBlipPNG.</summary>
    public const int BlipPng = 0xF01E;

    /// <summary>OfficeArtBlipDIB.</summary>
    public const int BlipDib = 0xF01F;

    /// <summary>OfficeArtBlipTIFF.</summary>
    public const int BlipTiff = 0xF029;

    /// <summary>The last BLIP record type.</summary>
    public const int BlipLast = 0xF117;
}

/// <summary>What a reader of record streams needs: the limits, where defects go and what the stream is called in their locations.</summary>
/// <param name="Limits">The limits in force.</param>
/// <param name="Defects">Where repairs are reported.</param>
/// <param name="StreamPath">The stream name used in <see cref="DefectLocation.InRecord"/> locations (for example <c>PowerPoint Document</c>).</param>
public sealed record ArtReadContext(OfficeLimits Limits, IDefectSink Defects, string StreamPath)
{
    /// <summary>A context with the default limits that discards defects; for tests and tools.</summary>
    public static ArtReadContext Default { get; } = new(OfficeLimits.Default, DiscardDefectSink.Instance, "OfficeArt");

    internal void Repair(string code, string message, long offset) =>
        Defects.Report(new Defect(DefectKind.Record, code, DefectSeverity.Repaired, message, DefectLocation.InRecord(StreamPath, offset)));

    internal void Drop(string code, string message, long offset) =>
        Defects.Report(new Defect(DefectKind.Record, code, DefectSeverity.Dropped, message, DefectLocation.InRecord(StreamPath, offset)));
}

/// <summary>
/// One record with the bytes it came from. Containers keep their children as records; nothing is copied, every <see cref="ReadOnlyMemory{T}"/>
/// points into the buffer that was parsed, so a reader that keeps the buffer keeps unknown records for free.
/// </summary>
public sealed class ArtRecord
{
    internal ArtRecord(ArtRecordHeader header, long offset, ReadOnlyMemory<byte> raw, IReadOnlyList<ArtRecord> children)
    {
        Header = header;
        Offset = offset;
        Raw = raw;
        Children = children;
    }

    /// <summary>The header as stored. <see cref="ArtRecordHeader.Length"/> may exceed <see cref="Payload"/> when the record was cut short.</summary>
    public ArtRecordHeader Header { get; }

    /// <summary>The stream offset of the header (the base offset passed to the parser plus the position in the buffer).</summary>
    public long Offset { get; }

    /// <summary>The header and payload bytes, clamped to the end of the data.</summary>
    public ReadOnlyMemory<byte> Raw { get; }

    /// <summary>The bytes after the header.</summary>
    public ReadOnlyMemory<byte> Payload => Raw[ArtRecordHeader.Size..];

    /// <summary>The child records of a container; empty for an atom.</summary>
    public IReadOnlyList<ArtRecord> Children { get; }

    /// <summary>The record type.</summary>
    public int Type => Header.Type;

    /// <summary>The first direct child of the given type, or null.</summary>
    /// <param name="type">The <c>recType</c> to look for.</param>
    public ArtRecord? Find(int type)
    {
        foreach (ArtRecord child in Children)
        {
            if (child.Type == type)
            {
                return child;
            }
        }

        return null;
    }

    /// <summary>The direct children of the given type, in order.</summary>
    /// <param name="type">The <c>recType</c> to look for.</param>
    public IEnumerable<ArtRecord> FindAll(int type)
    {
        foreach (ArtRecord child in Children)
        {
            if (child.Type == type)
            {
                yield return child;
            }
        }
    }

    /// <summary>Reads a little-endian 32-bit value from the payload, or <paramref name="fallback"/> when the payload is shorter.</summary>
    /// <param name="position">The position within the payload.</param>
    /// <param name="fallback">The value when the bytes are missing.</param>
    public int Int32At(int position, int fallback = 0) =>
        position >= 0 && Payload.Length - position >= 4 ? BinaryPrimitives.ReadInt32LittleEndian(Payload.Span[position..]) : fallback;

    /// <summary>Reads a little-endian 16-bit value from the payload, or <paramref name="fallback"/> when the payload is shorter.</summary>
    /// <param name="position">The position within the payload.</param>
    /// <param name="fallback">The value when the bytes are missing.</param>
    public int UInt16At(int position, int fallback = 0) =>
        position >= 0 && Payload.Length - position >= 2 ? BinaryPrimitives.ReadUInt16LittleEndian(Payload.Span[position..]) : fallback;
}

/// <summary>Parses a sequence of records, containers recursively. Used for OfficeArt data and for the PowerPoint binary stream, which share the record header.</summary>
public static class ArtRecords
{
    /// <summary>
    /// Parses <paramref name="data"/> as consecutive records. A record that claims more bytes than remain is clamped to the end and reported
    /// (<c>art.record-overrun</c>); trailing bytes too short for a header are reported (<c>art.record-truncated</c>) and ignored.
    /// </summary>
    /// <param name="data">The bytes holding the records.</param>
    /// <param name="streamOffset">The stream offset of <c>data[0]</c>, used in defect locations and <see cref="ArtRecord.Offset"/>.</param>
    /// <param name="context">Limits and defect sink.</param>
    /// <returns>The top-level records in order.</returns>
    /// <exception cref="OfficeLimitException">The record count, record size or nesting depth limit was exceeded.</exception>
    public static IReadOnlyList<ArtRecord> Parse(ReadOnlyMemory<byte> data, long streamOffset, ArtReadContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        long count = 0;
        return ParseLevel(data, streamOffset, context, 0, ref count);
    }

    private static List<ArtRecord> ParseLevel(ReadOnlyMemory<byte> data, long baseOffset, ArtReadContext context, int depth, ref long count)
    {
        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxRecursionDepth), context.Limits.MaxRecursionDepth, depth, DefectLocation.InRecord(context.StreamPath, baseOffset));
        var list = new List<ArtRecord>();
        int position = 0;
        while (position < data.Length)
        {
            if (!ArtRecordHeader.TryRead(data.Span[position..], out ArtRecordHeader header))
            {
                context.Repair("art.record-truncated", "Fewer than eight bytes remain where a record header should be; the bytes are ignored.", baseOffset + position);
                break;
            }

            long offset = baseOffset + position;
            count++;
            OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxRecordCount), context.Limits.MaxRecordCount, count, DefectLocation.InRecord(context.StreamPath, offset));
            long available = data.Length - position - ArtRecordHeader.Size;
            long length = header.Length;
            if (length > available)
            {
                context.Repair("art.record-overrun", "A record claims " + length + " bytes but only " + available + " remain; it is clamped to the end of the data.", offset);
                length = available;
            }

            OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxRecordSize), context.Limits.MaxRecordSize, length, DefectLocation.InRecord(context.StreamPath, offset));
            ReadOnlyMemory<byte> raw = data.Slice(position, (int)(ArtRecordHeader.Size + length));
            IReadOnlyList<ArtRecord> children = [];
            if (header.IsContainer && length > 0)
            {
                children = ParseLevel(raw[ArtRecordHeader.Size..], offset + ArtRecordHeader.Size, context, depth + 1, ref count);
            }

            list.Add(new ArtRecord(header, offset, raw, children));
            position += ArtRecordHeader.Size + (int)length;
        }

        return list;
    }
}
