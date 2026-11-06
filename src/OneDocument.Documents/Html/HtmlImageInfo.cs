using System.Buffers.Binary;

namespace RecursiveAI.OneDocument.Documents.Html;

/// <summary>The type and pixel size of a PNG, JPEG, GIF or BMP picture, read from its header. Nothing is decoded.</summary>
internal readonly record struct HtmlImageInfo(string Extension, string ContentType, int Width, int Height)
{
    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static HtmlImageInfo? Detect(ReadOnlySpan<byte> d)
    {
        if (d.Length >= 24 && d[..8].SequenceEqual(PngSignature))
        {
            // PNG specification §11.2.2: IHDR follows the signature, with width and height as 32-bit big-endian integers.
            return Make("png", "image/png", BinaryPrimitives.ReadInt32BigEndian(d[16..]), BinaryPrimitives.ReadInt32BigEndian(d[20..]));
        }

        if (d.Length >= 10 && (d[..6].SequenceEqual("GIF87a"u8) || d[..6].SequenceEqual("GIF89a"u8)))
        {
            return Make("gif", "image/gif", BinaryPrimitives.ReadUInt16LittleEndian(d[6..]), BinaryPrimitives.ReadUInt16LittleEndian(d[8..]));
        }

        if (d.Length >= 26 && d[0] == (byte)'B' && d[1] == (byte)'M')
        {
            int headerSize = BinaryPrimitives.ReadInt32LittleEndian(d[14..]);
            if (headerSize == 12 && d.Length >= 22)
            {
                return Make("bmp", "image/bmp", BinaryPrimitives.ReadUInt16LittleEndian(d[18..]), BinaryPrimitives.ReadUInt16LittleEndian(d[20..]));
            }

            return Make("bmp", "image/bmp", BinaryPrimitives.ReadInt32LittleEndian(d[18..]), Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(d[22..])));
        }

        if (d.Length >= 4 && d[0] == 0xFF && d[1] == 0xD8)
        {
            // ISO/IEC 10918-1 Annex B: marker segments until a start-of-frame segment, which holds the height and width.
            int i = 2;
            while (i + 9 < d.Length)
            {
                if (d[i] != 0xFF)
                {
                    i++;
                    continue;
                }

                byte m = d[i + 1];
                if (m == 0xFF)
                {
                    i++;
                    continue;
                }

                if (m is 0xD8 or 0x01 or >= 0xD0 and <= 0xD7)
                {
                    i += 2;
                    continue;
                }

                int len = BinaryPrimitives.ReadUInt16BigEndian(d[(i + 2)..]);
                if (m is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC))
                {
                    return Make("jpeg", "image/jpeg", BinaryPrimitives.ReadUInt16BigEndian(d[(i + 7)..]), BinaryPrimitives.ReadUInt16BigEndian(d[(i + 5)..]));
                }

                if (len < 2)
                {
                    break;
                }

                i += 2 + len;
            }

            return Make("jpeg", "image/jpeg", 96, 96);
        }

        return null;
    }

    private static HtmlImageInfo Make(string ext, string ct, int w, int h) =>
        w is > 0 and < 100000 && h is > 0 and < 100000 ? new HtmlImageInfo(ext, ct, w, h) : new HtmlImageInfo(ext, ct, 96, 96);
}
