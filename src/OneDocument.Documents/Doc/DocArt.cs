using System.Buffers.Binary;
using System.IO.Compression;

namespace RecursiveAI.OneDocument.Doc;

/// <summary>A picture found in the drawing layer: its bytes in a format an OOXML package can carry.</summary>
/// <param name="Bytes">The image data (a complete JPEG, PNG, TIFF, BMP, EMF or WMF file).</param>
/// <param name="Extension">The part extension without the dot.</param>
/// <param name="ContentType">The media type.</param>
internal readonly record struct ArtPicture(byte[] Bytes, string Extension, string ContentType);

/// <summary>
/// A minimal reader of the OfficeArt records ([MS-ODRAW] 2.2) that pictures come in: picture records inside a shape container
/// (inline pictures in the <c>Data</c> stream) and the shape/picture tables of the drawing layer (floating pictures). The bytes of
/// every record are left as they are; only the pictures are extracted. This is a seam: the shared OfficeArt reader replaces it.
/// </summary>
internal static class DocArt
{
    private const int RecBlipFirst = 0xF018;
    private const int RecBlipLast = 0xF117;

    /// <summary>Reads the picture of a <c>PICFAndOfficeArtData</c> in the Data stream ([MS-DOC] PICF).</summary>
    internal static ArtPicture? ReadInlinePicture(byte[] data, int location, out int widthTwips, out int heightTwips, out bool isShapeFile)
    {
        widthTwips = 0;
        heightTwips = 0;
        isShapeFile = false;
        if (location < 0 || location + 68 > data.Length)
        {
            return null;
        }

        ReadOnlySpan<byte> s = data.AsSpan(location);
        int lcb = BinaryPrimitives.ReadInt32LittleEndian(s);
        int cbHeader = BinaryPrimitives.ReadUInt16LittleEndian(s[4..]);
        int mm = BinaryPrimitives.ReadUInt16LittleEndian(s[6..]);
        int xExt = BinaryPrimitives.ReadInt16LittleEndian(s[8..]);
        int yExt = BinaryPrimitives.ReadInt16LittleEndian(s[10..]);
        int dxaGoal = BinaryPrimitives.ReadInt16LittleEndian(s[28..]);
        int dyaGoal = BinaryPrimitives.ReadInt16LittleEndian(s[30..]);
        int mx = BinaryPrimitives.ReadUInt16LittleEndian(s[32..]);
        int my = BinaryPrimitives.ReadUInt16LittleEndian(s[34..]);
        widthTwips = dxaGoal > 0 ? (int)((long)dxaGoal * (mx == 0 ? 1000 : mx) / 1000) : xExt;
        heightTwips = dyaGoal > 0 ? (int)((long)dyaGoal * (my == 0 ? 1000 : my) / 1000) : yExt;
        if (lcb < cbHeader || location + lcb > data.Length || cbHeader < 68)
        {
            return null;
        }

        int pos = cbHeader;
        isShapeFile = mm == 0x66;
        if (isShapeFile && pos < lcb)
        {
            pos += 1 + s[pos];
        }

        return FindPicture(s.Slice(0, lcb)[pos..], 0);
    }

    /// <summary>Finds the first picture in a run of OfficeArt records, looking inside containers and file blocks.</summary>
    internal static ArtPicture? FindPicture(ReadOnlySpan<byte> records, int depth)
    {
        int pos = 0;
        while (pos + 8 <= records.Length && depth < 8)
        {
            int verInst = BinaryPrimitives.ReadUInt16LittleEndian(records[pos..]);
            int type = BinaryPrimitives.ReadUInt16LittleEndian(records[(pos + 2)..]);
            uint len = BinaryPrimitives.ReadUInt32LittleEndian(records[(pos + 4)..]);
            int ver = verInst & 0xF;
            int inst = verInst >> 4;
            int body = pos + 8;
            if (len > records.Length - body)
            {
                len = (uint)(records.Length - body);
            }

            ReadOnlySpan<byte> payload = records.Slice(body, (int)len);
            if (type >= RecBlipFirst && type <= RecBlipLast)
            {
                if (DecodeBlip(type, inst, payload) is { } pic)
                {
                    return pic;
                }
            }
            else if (type == 0xF007 && payload.Length > 36)
            {
                // OfficeArtFBSE: a 36-byte header, the name, then the picture record when it is embedded.
                int cbName = payload[33];
                int embedded = 36 + cbName;
                if (embedded + 8 <= payload.Length && FindPicture(payload[embedded..], depth + 1) is { } p)
                {
                    return p;
                }
            }
            else if (ver == 0xF)
            {
                if (FindPicture(payload, depth + 1) is { } p)
                {
                    return p;
                }
            }

            pos = body + (int)len;
        }

        return null;
    }

    /// <summary>Decodes one picture record ([MS-ODRAW] 2.2.23 to 2.2.31).</summary>
    internal static ArtPicture? DecodeBlip(int type, int inst, ReadOnlySpan<byte> p)
    {
        bool twoUids = (inst & 1) == 1;
        int pos = twoUids ? 32 : 16;
        switch (type)
        {
            case 0xF01A:
            case 0xF01B:
            case 0xF01C:
                {
                    // A metafile record: uid(s), a 34-byte header (sizes, bounds, compression), then the data.
                    if (pos + 34 > p.Length)
                    {
                        return null;
                    }

                    int cbSave = BinaryPrimitives.ReadInt32LittleEndian(p[(pos + 28)..]);
                    int compression = p[pos + 32];
                    ReadOnlySpan<byte> raw = p[(pos + 34)..];
                    if (cbSave > 0 && cbSave < raw.Length)
                    {
                        raw = raw[..cbSave];
                    }

                    byte[] bytes;
                    if (compression == 0)
                    {
                        try
                        {
                            using var input = new MemoryStream(raw.ToArray());
                            using var z = new ZLibStream(input, CompressionMode.Decompress);
                            using var output = new MemoryStream();
                            z.CopyTo(output);
                            bytes = output.ToArray();
                        }
                        catch (InvalidDataException)
                        {
                            return null;
                        }
                    }
                    else
                    {
                        bytes = raw.ToArray();
                    }

                    return type switch
                    {
                        0xF01A => new ArtPicture(bytes, "emf", "image/x-emf"),
                        0xF01B => new ArtPicture(bytes, "wmf", "image/x-wmf"),
                        _ => null,
                    };
                }

            case 0xF01D:
            case 0xF02A:
                return pos + 1 <= p.Length ? new ArtPicture(p[(pos + 1)..].ToArray(), "jpeg", "image/jpeg") : null;
            case 0xF01E:
                return pos + 1 <= p.Length ? new ArtPicture(p[(pos + 1)..].ToArray(), "png", "image/png") : null;
            case 0xF029:
                return pos + 1 <= p.Length ? new ArtPicture(p[(pos + 1)..].ToArray(), "tiff", "image/tiff") : null;
            case 0xF01F:
                return pos + 1 <= p.Length ? DibToBmp(p[(pos + 1)..]) : null;
            default:
                return null;
        }
    }

    // A DIB lacks the 14-byte file header that makes it a .bmp.
    private static ArtPicture? DibToBmp(ReadOnlySpan<byte> dib)
    {
        if (dib.Length < 40)
        {
            return null;
        }

        int biSize = BinaryPrimitives.ReadInt32LittleEndian(dib);
        int bits = BinaryPrimitives.ReadUInt16LittleEndian(dib[14..]);
        int used = BinaryPrimitives.ReadInt32LittleEndian(dib[32..]);
        int compression = BinaryPrimitives.ReadInt32LittleEndian(dib[16..]);
        int palette = used > 0 ? used * 4 : bits <= 8 ? (1 << bits) * 4 : 0;
        if (compression == 3 && biSize == 40)
        {
            palette += 12;
        }

        var bmp = new byte[14 + dib.Length];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(2), bmp.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(10), 14 + biSize + palette);
        dib.CopyTo(bmp.AsSpan(14));
        return new ArtPicture(bmp, "bmp", "image/bmp");
    }

    /// <summary>The drawing layer's shapes: which picture (by index into the picture store) each shape identifier shows.</summary>
    internal sealed class Drawing
    {
        /// <summary>Gets the picture store entries: the offset of the picture record in the <c>WordDocument</c> stream (foDelay), or an embedded picture.</summary>
        internal List<(long FoDelay, ArtPicture? Embedded)> Store { get; } = [];

        /// <summary>Gets the picture index (one-based) of each shape identifier.</summary>
        internal Dictionary<int, int> ShapePicture { get; } = [];

        /// <summary>Gets the identifiers of shapes that hold text (text boxes).</summary>
        internal HashSet<int> TextShapes { get; } = [];
    }

    /// <summary>Reads the drawing layer from the <c>OfficeArtContent</c> in the table stream ([MS-DOC] OfficeArtContent).</summary>
    internal static Drawing ReadDrawing(byte[] table, uint fc, uint lcb)
    {
        var d = new Drawing();
        if (lcb < 8 || fc >= table.Length)
        {
            return d;
        }

        int len = (int)Math.Min(lcb, table.Length - fc);
        ReadOnlySpan<byte> s = table.AsSpan((int)fc, len);
        int pos = 0;
        int guard = 0;
        while (pos + 8 <= s.Length && guard++ < 4096)
        {
            // Between drawings a one-byte drawing label precedes each container; the group container starts the content.
            int type = BinaryPrimitives.ReadUInt16LittleEndian(s[(pos + 2)..]);
            if (type < 0xF000 || type > 0xF1FF)
            {
                pos++;
                continue;
            }

            uint rl = BinaryPrimitives.ReadUInt32LittleEndian(s[(pos + 4)..]);
            int recLen = (int)Math.Min(rl, (uint)(s.Length - pos - 8));
            ReadOnlySpan<byte> body = s.Slice(pos + 8, recLen);
            if (type == 0xF000)
            {
                Walk(body, d, 0);
            }
            else if (type == 0xF002)
            {
                Walk(body, d, 0);
            }

            pos += 8 + recLen;
        }

        return d;
    }

    private static void Walk(ReadOnlySpan<byte> s, Drawing d, int depth)
    {
        int pos = 0;
        while (pos + 8 <= s.Length && depth < 12)
        {
            int verInst = BinaryPrimitives.ReadUInt16LittleEndian(s[pos..]);
            int type = BinaryPrimitives.ReadUInt16LittleEndian(s[(pos + 2)..]);
            uint rl = BinaryPrimitives.ReadUInt32LittleEndian(s[(pos + 4)..]);
            int ver = verInst & 0xF;
            int recLen = (int)Math.Min(rl, (uint)(s.Length - pos - 8));
            ReadOnlySpan<byte> body = s.Slice(pos + 8, recLen);
            switch (type)
            {
                case 0xF007 when body.Length >= 36:
                    {
                        long fo = BinaryPrimitives.ReadUInt32LittleEndian(body[28..]);
                        ArtPicture? emb = null;
                        int embedded = 36 + body[33];
                        if (embedded + 8 <= body.Length)
                        {
                            emb = FindPicture(body[embedded..], 0);
                        }

                        d.Store.Add((fo, emb));
                        break;
                    }

                case 0xF004:
                    {
                        int spid = 0;
                        int pib = 0;
                        bool text = false;
                        ShapeInfo(body, ref spid, ref pib, ref text);
                        if (pib > 0)
                        {
                            d.ShapePicture[spid] = pib;
                        }

                        if (text)
                        {
                            d.TextShapes.Add(spid);
                        }

                        break;
                    }

                default:
                    if (ver == 0xF)
                    {
                        Walk(body, d, depth + 1);
                    }

                    break;
            }

            pos += 8 + recLen;
        }
    }

    // A shape container: the shape record (identifier) and the property table (picture index, text identifier).
    private static void ShapeInfo(ReadOnlySpan<byte> s, ref int spid, ref int pib, ref bool text)
    {
        int pos = 0;
        while (pos + 8 <= s.Length)
        {
            int verInst = BinaryPrimitives.ReadUInt16LittleEndian(s[pos..]);
            int type = BinaryPrimitives.ReadUInt16LittleEndian(s[(pos + 2)..]);
            uint rl = BinaryPrimitives.ReadUInt32LittleEndian(s[(pos + 4)..]);
            int recLen = (int)Math.Min(rl, (uint)(s.Length - pos - 8));
            ReadOnlySpan<byte> body = s.Slice(pos + 8, recLen);
            if (type == 0xF00A && body.Length >= 4)
            {
                spid = BinaryPrimitives.ReadInt32LittleEndian(body);
            }
            else if (type == 0xF00B)
            {
                int count = verInst >> 4;
                for (int i = 0; i < count && (i + 1) * 6 <= body.Length; i++)
                {
                    int opid = BinaryPrimitives.ReadUInt16LittleEndian(body[(i * 6)..]);
                    int op = BinaryPrimitives.ReadInt32LittleEndian(body[((i * 6) + 2)..]);
                    int id = opid & 0x3FFF;
                    if (id == 0x104)
                    {
                        pib = op;
                    }
                    else if (id == 0x80)
                    {
                        text = true;
                    }
                }
            }

            pos += 8 + recLen;
        }
    }

    /// <summary>Reads a picture record at an offset of the <c>WordDocument</c> stream (the picture store points there).</summary>
    internal static ArtPicture? ReadAt(byte[] word, long offset)
    {
        if (offset < 0 || offset + 8 > word.Length)
        {
            return null;
        }

        return FindPicture(word.AsSpan((int)offset), 0);
    }
}
