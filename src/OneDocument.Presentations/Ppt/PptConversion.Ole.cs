using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.OfficeArt;
using RecursiveAI.OneDocument.Ole;

namespace RecursiveAI.OneDocument.Ppt.Binary;

/// <summary>One embedded or linked OLE object of the presentation ([MS-PPT] 2.10.12): what the external object list says about it and its storage.</summary>
/// <param name="ExObjId">The identifier the shapes refer to (<c>ExObjRefAtom.exObjIdRef</c>).</param>
/// <param name="Kind">0 embedded, 1 linked, 2 control (<c>ExOleObjTypeEnum</c>).</param>
/// <param name="ProgId">The programmatic identifier of the container's <c>ProgIDAtom</c>, or null.</param>
/// <param name="PersistId">The persist identifier of the storage record.</param>
/// <param name="Offset">The offset of the container in the document stream.</param>
internal sealed record PptOleObject(uint ExObjId, int Kind, string? ProgId, uint PersistId, long Offset);

internal sealed partial class PptConversion
{
    private Dictionary<uint, PptOleObject>? _oleObjects;
    private readonly Dictionary<uint, OleStorageImage?> _oleStorages = [];
    private int _oleCounter;

    /// <summary>The number of OLE objects written as object parts or links.</summary>
    public int OleCarried { get; set; }

    /// <summary>Finds an OLE object of the external object list by the identifier a shape's <c>ExObjRefAtom</c> names.</summary>
    public PptOleObject? OleObject(uint exObjId)
    {
        if (_oleObjects is null)
        {
            _oleObjects = [];
            if (P.ExternalObjects is { } list)
            {
                foreach (ArtRecord c in list.Children)
                {
                    if (c.Type is not (PptRecordType.ExternalOleEmbed or PptRecordType.ExternalOleLink))
                    {
                        continue;
                    }

                    // ExOleObjAtom: drawAspect, type, exObjId, subType, persistIdRef, unused (24 bytes after the header).
                    if (c.Find(PptRecordType.ExternalOleObjectAtom) is not { } atom || atom.Payload.Length < 20)
                    {
                        continue;
                    }

                    uint id = unchecked((uint)atom.Int32At(8));
                    string? progId = null;
                    foreach (ArtRecord s in c.FindAll(PptRecordType.CString))
                    {
                        // ProgIDAtom is the string of instance 2 ([MS-PPT] 2.10.14).
                        if (s.Header.Instance == 2)
                        {
                            progId = Encoding.Unicode.GetString(s.Payload.Span).TrimEnd('\0');
                        }
                    }

                    _oleObjects.TryAdd(id, new PptOleObject(id, atom.Int32At(4), progId, unchecked((uint)atom.Int32At(16)), c.Offset));
                }
            }
        }

        return _oleObjects.GetValueOrDefault(exObjId);
    }

    /// <summary>
    /// Reads the storage of an OLE object: the <c>ExOleObjStg</c> persist object, stored as the storage's bytes or as a zlib stream of them
    /// ([MS-PPT] 2.10.34 to 2.10.36). Null, with a defect, when it is missing or is not a compound file.
    /// </summary>
    public OleStorageImage? OleStorage(PptOleObject obj)
    {
        if (_oleStorages.TryGetValue(obj.PersistId, out OleStorageImage? known))
        {
            return known;
        }

        OleStorageImage? image = null;
        OpenContext ctx = P.File.Context;
        ArtRecord? record = P.File.GetPersistObject(obj.PersistId);
        if (record is not null && record.Type == PptRecordType.ExternalOleObjectStg)
        {
            byte[]? bytes = null;
            ReadOnlyMemory<byte> payload = record.Payload;
            if (record.Header.Instance == 0)
            {
                bytes = payload.ToArray();
            }
            else if (payload.Length > 4)
            {
                uint size = BinaryPrimitives.ReadUInt32LittleEndian(payload.Span);
                long limit = Math.Min(ctx.Limits.MaxPartSize, ctx.Limits.MaxAllocationBytes);
                if (size > limit)
                {
                    ctx.Defects.Report(new Defect(DefectKind.Limit, "ppt.ole-storage-too-large", DefectSeverity.Dropped, "The object's storage is " + size.ToString(CultureInfo.InvariantCulture) + " bytes decompressed, past the limit; it is not carried.", DefectLocation.InRecord(PptFile.DocumentStreamName, record.Offset)));
                }
                else
                {
                    bytes = Inflate(payload[4..], (int)size, record.Offset);
                }
            }

            if (bytes is not null)
            {
                image = OleStorageImage.FromCompoundFile(bytes, "ExOleObjStg" + obj.ExObjId.ToString(CultureInfo.InvariantCulture), ctx);
                if (image is null)
                {
                    ctx.Defects.Report(new Defect(DefectKind.Structure, "ppt.ole-storage-unreadable", DefectSeverity.Dropped, "The object's storage is not a compound file; it is not carried.", DefectLocation.InRecord(PptFile.DocumentStreamName, record.Offset)));
                }
            }
        }
        else if (record is not null)
        {
            ctx.Defects.Report(new Defect(DefectKind.Structure, "ppt.ole-storage-unreadable", DefectSeverity.Dropped, "Persist object " + obj.PersistId.ToString(CultureInfo.InvariantCulture) + " is not an object storage record.", DefectLocation.InRecord(PptFile.DocumentStreamName, record.Offset)));
        }

        _oleStorages[obj.PersistId] = image;
        return image;
    }

    private byte[]? Inflate(ReadOnlyMemory<byte> compressed, int expected, long offset)
    {
        try
        {
            using var input = new MemoryStream(compressed.ToArray());
            using var z = new ZLibStream(input, CompressionMode.Decompress);
            var output = new byte[expected];
            int total = 0;
            while (total < expected)
            {
                int n = z.Read(output, total, expected - total);
                if (n <= 0)
                {
                    break;
                }

                total += n;
            }

            if (total != expected)
            {
                P.File.Context.Defects.Report(new Defect(DefectKind.Record, "ppt.ole-storage-unreadable", DefectSeverity.Repaired, "The object's storage decompresses to " + total.ToString(CultureInfo.InvariantCulture) + " bytes, not the " + expected.ToString(CultureInfo.InvariantCulture) + " the record says.", DefectLocation.InRecord(PptFile.DocumentStreamName, offset)));
                return total == 0 ? null : output.AsSpan(0, total).ToArray();
            }

            return output;
        }
        catch (InvalidDataException)
        {
            P.File.Context.Defects.Report(new Defect(DefectKind.Record, "ppt.ole-storage-unreadable", DefectSeverity.Dropped, "The object's storage is not a valid zlib stream; it is not carried.", DefectLocation.InRecord(PptFile.DocumentStreamName, offset)));
            return null;
        }
    }

    /// <summary>Adds an embedded object to the package and returns the target of the relationship to it, relative to a slide part.</summary>
    public string AddEmbeddedObject(byte[] compoundFile)
    {
        int n = ++_oleCounter;
        string name = "oleObject" + n.ToString(CultureInfo.InvariantCulture) + ".bin";
        Package.AddPart("/ppt/embeddings/" + name, OleEmbedding.ObjectContentType, compoundFile);
        return "../embeddings/" + name;
    }
}
