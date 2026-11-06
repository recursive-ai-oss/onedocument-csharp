using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Spreadsheets.Biff;

internal sealed partial class XlsImporter
{
    private const string DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private const string MainDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

    private sealed class Blip
    {
        public byte[] Data = [];
        public string Extension = "bin";
        public string ContentType = "application/octet-stream";
    }

    private byte[]? _groupData;
    private List<Blip?>? _blips;
    private int _imageCounter;
    private int _drawingCounter;

    // [MS-ODRAW] §2.2.20 OfficeArtBStoreContainer: one BSE per picture, each carrying its blip.
    private List<Blip?> Blips()
    {
        if (_blips is not null)
        {
            return _blips;
        }

        _blips = [];
        if (_groupData is null)
        {
            return _blips;
        }

        ReadOnlySpan<byte> d = _groupData;
        foreach (EscherRecord top in EscherReader.Children(d, 0, d.Length))
        {
            if (top.Type != EscherReader.DggContainer)
            {
                continue;
            }

            foreach (EscherRecord child in EscherReader.Children(d, top.DataOffset, top.End))
            {
                if (child.Type != EscherReader.BStoreContainer)
                {
                    continue;
                }

                foreach (EscherRecord bse in EscherReader.Children(d, child.DataOffset, child.End))
                {
                    _blips.Add(bse.Type == EscherReader.Bse ? ReadBse(d, bse) : null);
                }
            }
        }

        return _blips;
    }

    private Blip? ReadBse(ReadOnlySpan<byte> d, EscherRecord bse)
    {
        int cbName = bse.Length >= 36 ? d[bse.DataOffset + 33] : 0;
        int p = bse.DataOffset + 36 + cbName;
        if (p + 8 > bse.End)
        {
            return null;
        }

        var inner = EscherReader.Children(d, p, bse.End);
        if (inner.Count == 0)
        {
            return null;
        }

        EscherRecord b = inner[0];
        int pos = b.DataOffset;
        pos += 16;
        if ((b.Instance & 1) != 0)
        {
            pos += 16;
        }

        switch (b.Type)
        {
            case 0xF01D:
            case 0xF02A:
                return pos + 1 > b.End ? null : new Blip { Data = d[(pos + 1)..b.End].ToArray(), Extension = "jpeg", ContentType = "image/jpeg" };
            case 0xF01E:
                return pos + 1 > b.End ? null : new Blip { Data = d[(pos + 1)..b.End].ToArray(), Extension = "png", ContentType = "image/png" };
            case 0xF029:
                return pos + 1 > b.End ? null : new Blip { Data = d[(pos + 1)..b.End].ToArray(), Extension = "tiff", ContentType = "image/tiff" };
            case 0xF01F:
                return pos + 1 > b.End ? null : DibToBmp(d[(pos + 1)..b.End]);
            case 0xF01A:
            case 0xF01B:
                {
                    // [MS-ODRAW] §2.2.31 OfficeArtMetafileHeader: sizes, bounds, then the compression method (0 = DEFLATE).
                    if (pos + 34 > b.End)
                    {
                        return null;
                    }

                    int compressedSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.Slice(pos + 28, 4));
                    int compression = d[pos + 32];
                    int start = pos + 34;
                    int len = Math.Min(compressedSize, b.End - start);
                    if (len <= 0)
                    {
                        return null;
                    }

                    byte[] data;
                    if (compression == 0)
                    {
                        try
                        {
                            using var input = new MemoryStream(d.Slice(start, len).ToArray());
                            using var z = new ZLibStream(input, CompressionMode.Decompress);
                            using var output = new MemoryStream();
                            byte[] buffer = new byte[16384];
                            int n;
                            long limit = Math.Min(_context.Limits.MaxAllocationBytes, 256L << 20);
                            while ((n = z.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                output.Write(buffer, 0, n);
                                if (output.Length > limit)
                                {
                                    return null;
                                }
                            }

                            data = output.ToArray();
                        }
                        catch (InvalidDataException)
                        {
                            return null;
                        }
                    }
                    else
                    {
                        data = d.Slice(start, len).ToArray();
                    }

                    return b.Type == 0xF01A
                        ? new Blip { Data = data, Extension = "emf", ContentType = "image/x-emf" }
                        : new Blip { Data = data, Extension = "wmf", ContentType = "image/x-wmf" };
                }

            default:
                return null;
        }
    }

    // A DIB blip has no file header; a BMP file is the DIB behind a 14-byte BITMAPFILEHEADER.
    private static Blip? DibToBmp(ReadOnlySpan<byte> dib)
    {
        if (dib.Length < 40)
        {
            return null;
        }

        uint headerSize = BinaryPrimitives.ReadUInt32LittleEndian(dib);
        int bitCount = BinaryPrimitives.ReadUInt16LittleEndian(dib[14..]);
        uint compression = BinaryPrimitives.ReadUInt32LittleEndian(dib[16..]);
        uint colorsUsed = BinaryPrimitives.ReadUInt32LittleEndian(dib[32..]);
        long palette = bitCount <= 8 ? (colorsUsed != 0 ? colorsUsed : 1u << bitCount) * 4 : colorsUsed * 4;
        if (compression == 3 && headerSize == 40)
        {
            palette += 12;
        }

        long offset = 14 + headerSize + palette;
        var bmp = new byte[14 + dib.Length];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(2), (uint)bmp.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(10), (uint)offset);
        dib.CopyTo(bmp.AsSpan(14));
        return new Blip { Data = bmp, Extension = "bmp", ContentType = "image/bmp" };
    }

    private void WriteDrawing(SheetCtx sc, List<Relationship> rels, Func<string> relId)
    {
        SheetFeatureData f = _sheetFeatures;
        _drawingConverted = false;
        if (f.Drawing.Count == 0)
        {
            return;
        }

        byte[] stream = f.Drawing.SelectMany(x => x).ToArray();
        var pictures = new List<(int Blip, int[] Anchor, int Obj)>();
        int clientShapes = 0;
        CollectPictures(stream, 0, stream.Length, pictures, 0, ref clientShapes);
        PrepareOle();
        pictures.RemoveAll(x => x.Obj >= 0 && f.OleTaken.Contains(x.Obj));
        if (pictures.Count == 0)
        {
            return;
        }

        List<Blip?> blips = Blips();
        var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<xdr:wsDr xmlns:xdr=\"" + DrawingNs + "\" xmlns:a=\"" + MainDrawingNs + "\" xmlns:r=\"" + RelNs + "\">");
        var drawingRels = new List<Relationship>();
        int id = 1;
        long skipped = 0;
        foreach ((int blipIndex, int[] a, int _) in pictures)
        {
            if (blipIndex < 1 || blipIndex > blips.Count || blips[blipIndex - 1] is not { } blip)
            {
                skipped++;
                continue;
            }

            string mediaName = "/xl/media/image" + I(++_imageCounter) + "." + blip.Extension;
            _wb.Package.AddPart(mediaName, blip.ContentType, blip.Data);
            string rid = "rId" + I(drawingRels.Count + 1);
            drawingRels.Add(new Relationship(rid, RelBase + "image", "../media/image" + I(_imageCounter) + "." + blip.Extension));
            id++;
            xml.Append("<xdr:twoCellAnchor editAs=\"oneCell\"><xdr:from><xdr:col>").Append(I(a[0])).Append("</xdr:col><xdr:colOff>").Append(I(a[1] * 9525 * 64 / 1024)).Append("</xdr:colOff><xdr:row>").Append(I(a[2]))
                .Append("</xdr:row><xdr:rowOff>").Append(I(a[3] * 9525 * 20 / 256)).Append("</xdr:rowOff></xdr:from><xdr:to><xdr:col>").Append(I(a[4])).Append("</xdr:col><xdr:colOff>").Append(I(a[5] * 9525 * 64 / 1024))
                .Append("</xdr:colOff><xdr:row>").Append(I(a[6])).Append("</xdr:row><xdr:rowOff>").Append(I(a[7] * 9525 * 20 / 256)).Append("</xdr:rowOff></xdr:to>")
                .Append("<xdr:pic><xdr:nvPicPr><xdr:cNvPr id=\"").Append(I(id)).Append("\" name=\"Picture ").Append(I(id - 1)).Append("\"/><xdr:cNvPicPr><a:picLocks noChangeAspect=\"1\"/></xdr:cNvPicPr></xdr:nvPicPr>")
                .Append("<xdr:blipFill><a:blip r:embed=\"").Append(rid).Append("\"/><a:stretch><a:fillRect/></a:stretch></xdr:blipFill><xdr:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"").Append(L(Extent(a, 0, 1, 4, 5, 64))).Append("\" cy=\"").Append(L(Extent(a, 2, 3, 6, 7, 20))).Append("\"/></a:xfrm>")
                .Append("<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></xdr:spPr></xdr:pic><xdr:clientData/></xdr:twoCellAnchor>");
        }

        xml.Append("</xdr:wsDr>");
        if (drawingRels.Count == 0)
        {
            _lossCounts["loss.pictures"] = _lossCounts.GetValueOrDefault("loss.pictures") + skipped;
            return;
        }

        int n = ++_drawingCounter;
        string drawingName = "/xl/drawings/drawing" + I(n) + ".xml";
        _wb.Package.AddPart(drawingName, "application/vnd.openxmlformats-officedocument.drawing+xml", Encoding.UTF8.GetBytes(xml.ToString()));
        _wb.Package.SetRelationships(drawingName, drawingRels);
        string did = relId();
        rels.Add(new Relationship(did, RelBase + "drawing", "../drawings/drawing" + I(n) + ".xml"));
        sc.Sheet.InsertFrameElement("drawing", "<drawing xmlns:r=\"" + RelNs + "\" r:id=\"" + did + "\"/>");
        _drawingConverted = true;
        if (skipped > 0)
        {
            _lossCounts["loss.pictures"] = _lossCounts.GetValueOrDefault("loss.pictures") + skipped;
        }
    }

    private static string L(long v) => v.ToString(CultureInfo.InvariantCulture);

    // An anchor gives cells and offsets inside them; the picture's size is estimated with the default cell size (64 by 20 pixels, 9525 EMU each).
    private static long Extent(int[] a, int cellFrom, int offFrom, int cellTo, int offTo, int cellPixels)
    {
        long emu = (long)(a[cellTo] - a[cellFrom]) * cellPixels * 9525;
        emu += (long)(a[offTo] - a[offFrom]) * cellPixels * 9525 / (cellPixels == 64 ? 1024 : 256);
        return Math.Max(emu, 9525);
    }

    // Finds the shapes that name a picture (the pib property, [MS-ODRAW] §2.3.23.1) and take their cell anchor.
    private static void CollectPictures(byte[] d, int start, int end, List<(int, int[], int)> into, int depth, ref int clientShapes)
    {
        if (depth > 16)
        {
            return;
        }

        foreach (EscherRecord r in EscherReader.Children(d, start, end))
        {
            if (r.Type == EscherReader.SpContainer)
            {
                int pib = 0;
                int[]? anchor = null;
                bool clientData = false;
                foreach (EscherRecord c in EscherReader.Children(d, r.DataOffset, r.End))
                {
                    if (c.Type == 0xF011)
                    {
                        clientData = true; // OfficeArtClientData: the shape has an Obj record
                    }
                    else if (c.Type == EscherReader.Opt)
                    {
                        for (int k = 0; k < c.Instance && c.DataOffset + (k + 1) * 6 <= c.End; k++)
                        {
                            int at = c.DataOffset + k * 6;
                            int opid = d[at] | (d[at + 1] << 8);
                            if ((opid & 0x3FFF) == 0x0104)
                            {
                                pib = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(at + 2, 4));
                            }
                        }
                    }
                    else if (c.Type == EscherReader.ClientAnchor && c.Length >= 18)
                    {
                        int a = c.DataOffset + 2;
                        int V(int i) => d[a + i * 2] | (d[a + i * 2 + 1] << 8);
                        anchor = [V(0), V(1), V(2), V(3), V(4), V(5), V(6), V(7)];
                    }
                }

                // The shapes with client data and the Obj records that follow the drawing are in the same order ([MS-XLS] 2.4.181).
                int obj = clientData ? clientShapes++ : -1;
                if (pib > 0 && anchor is not null)
                {
                    into.Add((pib, anchor, obj));
                }
            }
            else if (r.IsContainer)
            {
                CollectPictures(d, r.DataOffset, r.End, into, depth + 1, ref clientShapes);
            }
        }
    }
}
