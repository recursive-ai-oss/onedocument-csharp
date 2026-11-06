using System.Buffers.Binary;
using System.IO.Compression;
using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.OfficeArt;

/// <summary>The kind of image a BLIP holds ([MS-ODRAW] §2.4.1, MSOBLIPTYPE).</summary>
public enum ArtImageFormat
{
    /// <summary>A type this reader does not know.</summary>
    Unknown = 0,

    /// <summary>Enhanced metafile.</summary>
    Emf = 2,

    /// <summary>Windows metafile.</summary>
    Wmf = 3,

    /// <summary>Macintosh PICT.</summary>
    Pict = 4,

    /// <summary>JPEG (RGB or CMYK).</summary>
    Jpeg = 5,

    /// <summary>PNG.</summary>
    Png = 6,

    /// <summary>Device-independent bitmap; <see cref="ArtImage.Data"/> is completed to a BMP file.</summary>
    Dib = 7,

    /// <summary>TIFF.</summary>
    Tiff = 17,
}

/// <summary>A picture decoded from a BLIP record: its format and file bytes.</summary>
/// <param name="Format">The image format.</param>
/// <param name="Data">The bytes of an image file in <paramref name="Format"/> (metafiles decompressed; a DIB with a BMP file header added).</param>
/// <param name="Uid">The 16-byte identifier of the picture data (an MD4 digest; used to tell repeated pictures apart).</param>
public sealed record ArtImage(ArtImageFormat Format, ReadOnlyMemory<byte> Data, ReadOnlyMemory<byte> Uid)
{
    /// <summary>A media type for the image: <c>image/png</c>, <c>image/jpeg</c>, <c>image/x-emf</c>, <c>image/x-wmf</c>, <c>image/bmp</c>, <c>image/tiff</c> or <c>image/x-pict</c>.</summary>
    public string MediaType => Format switch
    {
        ArtImageFormat.Emf => "image/x-emf",
        ArtImageFormat.Wmf => "image/x-wmf",
        ArtImageFormat.Pict => "image/x-pict",
        ArtImageFormat.Jpeg => "image/jpeg",
        ArtImageFormat.Png => "image/png",
        ArtImageFormat.Dib => "image/bmp",
        ArtImageFormat.Tiff => "image/tiff",
        _ => "application/octet-stream",
    };

    /// <summary>The file extension without a dot: <c>emf</c>, <c>wmf</c>, <c>pict</c>, <c>jpeg</c>, <c>png</c>, <c>bmp</c>, <c>tiff</c> or <c>bin</c>.</summary>
    public string Extension => Format switch
    {
        ArtImageFormat.Emf => "emf",
        ArtImageFormat.Wmf => "wmf",
        ArtImageFormat.Pict => "pict",
        ArtImageFormat.Jpeg => "jpeg",
        ArtImageFormat.Png => "png",
        ArtImageFormat.Dib => "bmp",
        ArtImageFormat.Tiff => "tiff",
        _ => "bin",
    };
}

/// <summary>One entry of the BLIP store (<c>OfficeArtFBSE</c>, [MS-ODRAW] §2.2.32).</summary>
public sealed class ArtBlipEntry
{
    internal ArtBlipEntry(ArtRecord record)
    {
        Record = record;
    }

    /// <summary>The BSE record, or the BLIP record itself for stores that hold BLIPs directly.</summary>
    public ArtRecord Record { get; }

    /// <summary>The Windows BLIP type (<c>btWin32</c>).</summary>
    public int BlipType { get; internal set; }

    /// <summary>The picture identifier (<c>rgbUid</c>).</summary>
    public ReadOnlyMemory<byte> Uid { get; internal set; }

    /// <summary>The size of the BLIP in bytes (<c>size</c>).</summary>
    public long Size { get; internal set; }

    /// <summary>The number of references (<c>cRef</c>); 0 marks an empty slot.</summary>
    public long ReferenceCount { get; internal set; }

    /// <summary>The offset of the BLIP in the delay stream (<c>foDelay</c>), or -1 when it is not there.</summary>
    public long DelayOffset { get; internal set; } = -1;

    /// <summary>The BLIP name, if any.</summary>
    public string Name { get; internal set; } = "";

    /// <summary>The BLIP record embedded in the entry, or null when the picture lives in the delay stream.</summary>
    public ArtRecord? EmbeddedBlip { get; internal set; }
}

/// <summary>The BLIP store ([MS-ODRAW] §2.2.20): pictures shared by the shapes of a file, addressed by one-based index.</summary>
public sealed class ArtBlipStore
{
    private ArtBlipStore(List<ArtBlipEntry> entries)
    {
        Entries = entries;
    }

    /// <summary>The entries in order. A shape's <c>pib</c> property is the one-based index into this list.</summary>
    public IReadOnlyList<ArtBlipEntry> Entries { get; }

    /// <summary>Parses a BLIP store container.</summary>
    /// <param name="store">An <c>OfficeArtBStoreContainer</c> record.</param>
    /// <param name="context">Limits and defect sink.</param>
    /// <returns>The store; an entry that is not a BSE or BLIP record is kept as an empty slot.</returns>
    public static ArtBlipStore Parse(ArtRecord store, ArtReadContext context)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(context);
        var entries = new List<ArtBlipEntry>(store.Children.Count);
        foreach (ArtRecord child in store.Children)
        {
            var entry = new ArtBlipEntry(child);
            if (child.Type == ArtRecordType.Bse)
            {
                ReadOnlySpan<byte> p = child.Payload.Span;
                if (p.Length >= 36)
                {
                    entry.BlipType = p[0];
                    entry.Uid = child.Payload.Slice(2, 16);
                    entry.Size = BinaryPrimitives.ReadUInt32LittleEndian(p[20..]);
                    entry.ReferenceCount = BinaryPrimitives.ReadUInt32LittleEndian(p[24..]);
                    uint delay = BinaryPrimitives.ReadUInt32LittleEndian(p[28..]);
                    entry.DelayOffset = delay == 0xFFFFFFFF ? -1 : delay;
                    int nameBytes = p[33];
                    int afterName = 36 + nameBytes;
                    if (nameBytes > 0 && afterName <= p.Length)
                    {
                        var chars = new char[nameBytes / 2];
                        for (int i = 0; i < chars.Length; i++)
                        {
                            chars[i] = (char)BinaryPrimitives.ReadUInt16LittleEndian(p[(36 + (i * 2))..]);
                        }

                        entry.Name = new string(chars).TrimEnd('\0');
                    }

                    if (afterName < p.Length)
                    {
                        // §2.2.32: the embedded BLIP, when present, follows the name.
                        IReadOnlyList<ArtRecord> inner = ArtRecords.Parse(child.Payload[afterName..], child.Offset + ArtRecordHeader.Size + afterName, context);
                        if (inner.Count > 0)
                        {
                            entry.EmbeddedBlip = inner[0];
                        }
                    }
                }
                else
                {
                    context.Repair("art.bse-truncated", "A BLIP store entry is shorter than its 36-byte fixed part; the slot is empty.", child.Offset);
                }
            }
            else if (child.Type >= ArtRecordType.BlipFirst && child.Type <= ArtRecordType.BlipLast)
            {
                entry.EmbeddedBlip = child;
                entry.ReferenceCount = 1;
            }

            entries.Add(entry);
        }

        return new ArtBlipStore(entries);
    }

    /// <summary>
    /// Finds the picture of a one-based index. A BLIP that is not embedded in its entry is read from <paramref name="delayStream"/> at the
    /// entry's offset (in PowerPoint files the <c>Pictures</c> stream). An index out of range, an empty slot or a damaged BLIP yields null and
    /// a defect, never an exception.
    /// </summary>
    /// <param name="index">The one-based index (a shape's <c>pib</c> or <c>fillBlip</c> value).</param>
    /// <param name="delayStream">The delay stream bytes, or empty when the file has none.</param>
    /// <param name="context">Limits and defect sink; the stream path names the stream the store was read from.</param>
    public ArtImage? GetImage(int index, ReadOnlyMemory<byte> delayStream, ArtReadContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (index < 1 || index > Entries.Count)
        {
            context.Repair("art.blip-index", "A picture refers to BLIP " + index + " but the store has " + Entries.Count + " entries.", -1);
            return null;
        }

        ArtBlipEntry entry = Entries[index - 1];
        ArtRecord? blip = entry.EmbeddedBlip;
        if (blip is null)
        {
            if (entry.DelayOffset < 0 || entry.DelayOffset >= delayStream.Length)
            {
                if (entry.ReferenceCount != 0 || entry.DelayOffset >= 0)
                {
                    context.Repair("art.blip-missing", "BLIP " + index + " is not in the file's picture stream.", entry.Record.Offset);
                }

                return null;
            }

            IReadOnlyList<ArtRecord> found = ParseOne(delayStream, entry.DelayOffset, context);
            if (found.Count == 0)
            {
                return null;
            }

            blip = found[0];
        }

        return ArtBlip.Decode(blip, context);
    }

    // Only the record at the offset is wanted, not the rest of a stream of many pictures.
    private static IReadOnlyList<ArtRecord> ParseOne(ReadOnlyMemory<byte> stream, long offset, ArtReadContext context)
    {
        ReadOnlyMemory<byte> rest = stream[(int)offset..];
        if (!ArtRecordHeader.TryRead(rest.Span, out ArtRecordHeader header))
        {
            return [];
        }

        long total = Math.Min(rest.Length, ArtRecordHeader.Size + header.Length);
        return ArtRecords.Parse(rest[..(int)total], offset, context) is { Count: > 0 } list ? [list[0]] : [];
    }
}

/// <summary>Decodes BLIP records ([MS-ODRAW] §2.2.23 to §2.2.31).</summary>
public static class ArtBlip
{
    /// <summary>
    /// Reads the picture of a BLIP record. Metafile data is inflated (bounded by <see cref="OfficeLimits.MaxAllocationBytes"/>); a DIB gets a BMP file header.
    /// </summary>
    /// <param name="blip">A BLIP record (EMF, WMF, PICT, JPEG, PNG, DIB or TIFF).</param>
    /// <param name="context">Limits and defect sink.</param>
    /// <returns>The image, or null (with a defect <c>art.blip-invalid</c>) when the record is damaged or of an unknown type.</returns>
    public static ArtImage? Decode(ArtRecord blip, ArtReadContext context)
    {
        ArgumentNullException.ThrowIfNull(blip);
        ArgumentNullException.ThrowIfNull(context);
        int instance = blip.Header.Instance;
        ReadOnlyMemory<byte> payload = blip.Payload;
        // Instances of the two-UID variants: the second UID (of the uncompressed or alternate data) is present only for these.
        (ArtImageFormat format, bool twoUids, bool metafile) = blip.Type switch
        {
            ArtRecordType.BlipEmf => (ArtImageFormat.Emf, instance == 0x3D5, true),
            ArtRecordType.BlipWmf => (ArtImageFormat.Wmf, instance == 0x217, true),
            ArtRecordType.BlipPict => (ArtImageFormat.Pict, instance == 0x543, true),
            ArtRecordType.BlipJpeg => (ArtImageFormat.Jpeg, instance is 0x46B or 0x6E3, false),
            ArtRecordType.BlipPng => (ArtImageFormat.Png, instance == 0x6E1, false),
            ArtRecordType.BlipDib => (ArtImageFormat.Dib, instance == 0x7A9, false),
            ArtRecordType.BlipTiff => (ArtImageFormat.Tiff, instance == 0x6E5, false),
            _ => (ArtImageFormat.Unknown, false, false),
        };
        if (format == ArtImageFormat.Unknown)
        {
            context.Drop("art.blip-invalid", "BLIP record type 0x" + blip.Type.ToString("X", System.Globalization.CultureInfo.InvariantCulture) + " is not a picture type this reader knows.", blip.Offset);
            return null;
        }

        int position = twoUids ? 32 : 16;
        if (payload.Length < position)
        {
            context.Drop("art.blip-invalid", "A BLIP is shorter than its identifier.", blip.Offset);
            return null;
        }

        ReadOnlyMemory<byte> uid = payload.Slice(0, 16);
        if (!metafile)
        {
            // §2.2.27 to §2.2.30: one tag byte, then the file bytes.
            position += 1;
            if (position > payload.Length)
            {
                context.Drop("art.blip-invalid", "A BLIP has no picture data.", blip.Offset);
                return null;
            }

            ReadOnlyMemory<byte> data = payload[position..];
            if (format == ArtImageFormat.Dib)
            {
                byte[]? bmp = DibToBmp(data.Span);
                if (bmp is null)
                {
                    context.Drop("art.blip-invalid", "A DIB BLIP has a damaged bitmap header.", blip.Offset);
                    return null;
                }

                return new ArtImage(format, bmp, uid);
            }

            return new ArtImage(format, data, uid);
        }

        // §2.2.31 OfficeArtMetafileHeader: cbSize (uncompressed size), rcBounds, ptSize, cbSave (stored size), compression, filter.
        if (payload.Length < position + 34)
        {
            context.Drop("art.blip-invalid", "A metafile BLIP is shorter than its metafile header.", blip.Offset);
            return null;
        }

        ReadOnlySpan<byte> h = payload.Span[position..];
        long cbSize = BinaryPrimitives.ReadUInt32LittleEndian(h);
        long cbSave = BinaryPrimitives.ReadUInt32LittleEndian(h[28..]);
        byte compression = h[32];
        ReadOnlyMemory<byte> stored = payload[(position + 34)..];
        if (cbSave < stored.Length)
        {
            stored = stored[..(int)cbSave];
        }

        if (compression == 0xFE)
        {
            return new ArtImage(format, stored, uid);
        }

        if (compression != 0)
        {
            context.Drop("art.blip-invalid", "A metafile BLIP uses compression method " + compression + ", which is not defined.", blip.Offset);
            return null;
        }

        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxAllocationBytes), context.Limits.MaxAllocationBytes, cbSize, DefectLocation.InRecord(context.StreamPath, blip.Offset));
        try
        {
            // Compression 0 is DEFLATE with a zlib header.
            using var input = new MemoryStream(stored.ToArray(), writable: false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buffer = new byte[16 * 1024];
            long limit = Math.Min(cbSize == 0 ? context.Limits.MaxAllocationBytes : cbSize, context.Limits.MaxAllocationBytes);
            int read;
            while ((read = zlib.Read(buffer, 0, buffer.Length)) > 0)
            {
                output.Write(buffer, 0, read);
                if (output.Length > limit)
                {
                    context.Drop("art.blip-invalid", "A metafile BLIP inflates past its stated size.", blip.Offset);
                    return null;
                }
            }

            return new ArtImage(format, output.ToArray(), uid);
        }
        catch (InvalidDataException)
        {
            context.Drop("art.blip-invalid", "The compressed metafile data of a BLIP is damaged.", blip.Offset);
            return null;
        }
    }

    // A DIB BLIP stores a BITMAPINFOHEADER-led bitmap with no BITMAPFILEHEADER. Prefix one so the bytes are a .bmp file.
    private static byte[]? DibToBmp(ReadOnlySpan<byte> dib)
    {
        if (dib.Length < 40)
        {
            return null;
        }

        uint headerSize = BinaryPrimitives.ReadUInt32LittleEndian(dib);
        if (headerSize < 12 || headerSize > dib.Length)
        {
            return null;
        }

        int bitCount;
        uint colorsUsed = 0;
        uint compression = 0;
        if (headerSize == 12)
        {
            bitCount = BinaryPrimitives.ReadUInt16LittleEndian(dib[10..]);
        }
        else
        {
            bitCount = BinaryPrimitives.ReadUInt16LittleEndian(dib[14..]);
            compression = BinaryPrimitives.ReadUInt32LittleEndian(dib[16..]);
            colorsUsed = BinaryPrimitives.ReadUInt32LittleEndian(dib[32..]);
        }

        long palette = 0;
        if (bitCount is > 0 and <= 8)
        {
            long entries = colorsUsed == 0 ? 1L << bitCount : Math.Min(colorsUsed, 1L << bitCount);
            palette = entries * (headerSize == 12 ? 3 : 4);
        }
        else if (compression == 3 && headerSize == 40)
        {
            // BI_BITFIELDS: three colour masks follow the 40-byte header.
            palette = 12;
        }

        long pixelOffset = 14 + headerSize + palette;
        var bmp = new byte[14 + dib.Length];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(2), (uint)bmp.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(10), (uint)Math.Min(pixelOffset, bmp.Length));
        dib.CopyTo(bmp.AsSpan(14));
        return bmp;
    }
}
