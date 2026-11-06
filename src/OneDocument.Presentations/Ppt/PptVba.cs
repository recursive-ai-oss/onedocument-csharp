using System.Buffers.Binary;
using System.IO.Compression;
using System.Linq;
using RecursiveAI.OneDocument.Cfb;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Ppt.Binary;
using RecursiveAI.OneDocument.Vba;

namespace RecursiveAI.OneDocument.Presentations;

/// <summary>
/// Finds and removes the VBA project of a PowerPoint 97-2003 file. The project is a persist object of the <c>PowerPoint Document</c> stream
/// ([MS-PPT] §2.10.40 VbaProjectStg): a zlib-compressed compound file in an RT_ExternalOleObjectStg record, announced by the VBAInfoAtom of the
/// document container (§2.4.11). Nothing is run.
/// </summary>
internal static class PptVba
{
    private const string DocumentStream = "/PowerPoint Document";
    private const int RtDocument = 0x03E8, RtVbaInfoAtom = 0x0400, RtExternalOleObjectStg = 0x1011;
    private const uint EncryptedHeaderToken = 0xF3D1C4DF;
    private const string LooseStorage = "/_VBA_PROJECT_CUR";

    private static byte[] ReadAll(IByteSource source, OfficeLimits limits)
    {
        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxPartSize), limits.MaxPartSize, source.Length);
        byte[] data = new byte[source.Length];
        int got = 0;
        while (got < data.Length)
        {
            int n = source.Read(got, data.AsSpan(got));
            if (n <= 0)
            {
                break;
            }

            got += n;
        }

        return data;
    }

    private static bool IsEncrypted(CfbPackage cfb)
    {
        // [MS-PPT] §2.3.2: the Current User stream is never encrypted; its header token says whether the rest is.
        ReadOnlySpan<byte> cu = cfb.FindPart("/Current User") is { } p ? p.GetBytes().Span : default;
        return cu.Length >= 16 && BinaryPrimitives.ReadUInt32LittleEndian(cu[12..]) == EncryptedHeaderToken;
    }

    private readonly record struct Found(long Offset, int Length, int Instance);

    internal static VbaProject? Read(IByteSource source, OpenContext context, CancellationToken cancellationToken)
    {
        byte[] bytes = ReadAll(source, context.Limits);
        using CfbPackage cfb = CfbPackage.Open(ByteSource.FromMemory(bytes), context);
        if (cfb.Streams.Any(s => s.Name.StartsWith(LooseStorage + "/", StringComparison.OrdinalIgnoreCase)))
        {
            return VbaProject.FromStorage(cfb, LooseStorage, context);
        }

        if (cfb.FindPart(DocumentStream) is not { } part)
        {
            return null;
        }

        ReadOnlyMemory<byte> document = part.GetBytes();
        if (IsEncrypted(cfb))
        {
            using IByteSource again = ByteSource.FromMemory(bytes);
            document = PptFile.Open(again, context, cancellationToken).DocumentStream;
        }

        byte[]? storage = null;
        long where = -1;
        foreach (Found f in ProjectRecords(document.Span, context))
        {
            byte[]? cf = Storage(document.Span, f, context.Limits);
            if (cf is not null)
            {
                storage = cf; // the newest copy is the last one in the stream
                where = f.Offset;
            }
        }

        return storage is null ? null : VbaProject.FromProjectStorage(storage, context, "PowerPoint Document@" + where);
    }

    internal static VbaRemovalReport Remove(IByteSource source, Stream destination, OpenContext context, CancellationToken cancellationToken)
    {
        byte[] bytes = ReadAll(source, context.Limits);
        using CfbPackage cfb = CfbPackage.Open(ByteSource.FromMemory(bytes), context);
        var report = new VbaRemovalReport(OfficeFormat.Ppt);
        if (IsEncrypted(cfb))
        {
            throw new OfficeUnsupportedException("removing macros from an encrypted PowerPoint file (decrypt it first)", DefectLocation.InRecord("/Current User", 12));
        }

        bool changed = false;
        if (cfb.Streams.Any(s => s.Name.StartsWith(LooseStorage + "/", StringComparison.OrdinalIgnoreCase)))
        {
            report.HadProject = true;
            try
            {
                VbaProject p = VbaProject.FromStorage(cfb, LooseStorage, context);
                report.ModulesRemoved = p.Modules.Count;
                report.SignaturesRemoved = p.Signatures;
            }
            catch (OfficeException ex)
            {
                report.Note("vba.project-unreadable", "The project could not be read before removal, so its modules are not counted: " + ex.Message, ex.Location);
            }

            long total = cfb.Streams.Where(s => s.Name.StartsWith(LooseStorage + "/", StringComparison.OrdinalIgnoreCase)).Sum(s => s.Length);
            cfb.RemoveStorage(LooseStorage);
            report.Add(VbaRemovedKind.Project, LooseStorage, total, "Removed the project storage.");
            changed = true;
        }

        if (cfb.FindPart(DocumentStream) is CfbStreamPart part)
        {
            byte[] doc = part.GetBytes().ToArray();
            bool edited = false;
            bool counted = report.HadProject;
            foreach (Found f in ProjectRecords(doc, context))
            {
                byte[]? cf = Storage(doc, f, context.Limits);
                if (cf is null)
                {
                    continue;
                }

                report.HadProject = true;
                if (!counted)
                {
                    counted = true;
                    try
                    {
                        VbaProject p = VbaProject.FromProjectStorage(cf, context, "PowerPoint Document@" + f.Offset);
                        report.ModulesRemoved = p.Modules.Count;
                        report.SignaturesRemoved = p.Signatures;
                    }
                    catch (OfficeException ex)
                    {
                        report.Note("vba.project-unreadable", "The project could not be read before removal, so its modules are not counted: " + ex.Message, ex.Location);
                    }
                }

                Wipe(doc, f);
                report.Add(VbaRemovedKind.Project, "PowerPoint Document@" + f.Offset.ToString(System.Globalization.CultureInfo.InvariantCulture), f.Length, "Emptied the project storage record (its length is kept so no stream position moves).");
                edited = true;
            }

            foreach (long atom in VbaInfoAtoms(doc))
            {
                // [MS-PPT] §2.4.11: fHasMacros 0 says the VBA storage is empty.
                if (BinaryPrimitives.ReadUInt32LittleEndian(doc.AsSpan((int)atom + 12)) != 0)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(doc.AsSpan((int)atom + 12), 0);
                    report.Add(VbaRemovedKind.Marker, "PowerPoint Document@" + atom.ToString(System.Globalization.CultureInfo.InvariantCulture), 0, "Cleared fHasMacros in the VBAInfoAtom.");
                    edited = true;
                }
            }

            if (edited)
            {
                part.SetBytes(doc);
                changed = true;
            }
        }

        if (!changed)
        {
            cfb.WriteTo(destination, cancellationToken);
            return report;
        }

        // The signature of a PowerPoint project is a property of the summary streams, which are not interpreted here.
        report.Note("vba.ppt-signature-kept", "A signature kept in the file's property sets is not looked for and stays as it is.", DefectLocation.InRecord("/\u0005DocumentSummaryInformation"));
        cfb.WriteTo(destination, cancellationToken);
        return report;
    }

    // Top-level records of the document stream that can hold a persisted storage; the record walk is bounded by the stream.
    private static List<Found> ProjectRecords(ReadOnlySpan<byte> doc, OpenContext context)
    {
        var list = new List<Found>();
        long pos = 0;
        long count = 0;
        while (pos + 8 <= doc.Length)
        {
            int verInst = BinaryPrimitives.ReadUInt16LittleEndian(doc[(int)pos..]);
            int type = BinaryPrimitives.ReadUInt16LittleEndian(doc[(int)(pos + 2)..]);
            long len = BinaryPrimitives.ReadUInt32LittleEndian(doc[(int)(pos + 4)..]);
            if (pos + 8 + len > doc.Length)
            {
                break;
            }

            OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxRecordCount), context.Limits.MaxRecordCount, ++count, DefectLocation.InRecord("PowerPoint Document", pos));
            if (type == RtExternalOleObjectStg && (verInst & 0x0F) == 0 && (verInst >> 4) <= 1 && len >= 12)
            {
                list.Add(new Found(pos, (int)len, verInst >> 4));
            }

            pos += 8 + len;
        }

        return list;
    }

    // The storage a record holds, when it holds a compound file that has a VBA storage: a VBA project, not an OLE object or a control.
    private static byte[]? Storage(ReadOnlySpan<byte> doc, Found f, OfficeLimits limits)
    {
        ReadOnlySpan<byte> payload = doc.Slice((int)f.Offset + 8, f.Length);
        byte[]? bytes;
        if (f.Instance == 0)
        {
            bytes = payload.ToArray();
        }
        else
        {
            // [MS-PPT] §2.10.36: decompressedSize (4), then the zlib stream.
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(payload);
            if (size < 8 || size > limits.MaxPartSize)
            {
                return null;
            }

            try
            {
                using var input = new MemoryStream(payload[4..].ToArray());
                using var z = new ZLibStream(input, CompressionMode.Decompress);
                bytes = new byte[size];
                int got = 0;
                while (got < bytes.Length)
                {
                    int n = z.Read(bytes, got, bytes.Length - got);
                    if (n <= 0)
                    {
                        break;
                    }

                    got += n;
                }

                if (got < 8)
                {
                    return null;
                }
            }
            catch (InvalidDataException)
            {
                return null;
            }
        }

        // The compound-file signature, then look for the VBA storage by opening it: other persisted storages are skipped.
        if (bytes.Length < 8 || bytes[0] != 0xD0 || bytes[1] != 0xCF || bytes[2] != 0x11 || bytes[3] != 0xE0)
        {
            return null;
        }

        try
        {
            using CfbPackage inner = CfbPackage.Open(ByteSource.FromMemory(bytes), new OpenContext(limits));
            return inner.FindPart("/VBA/dir") is null && inner.FindPart("/VBA/_VBA_PROJECT") is null ? null : bytes;
        }
        catch (OfficeException)
        {
            return null;
        }
    }

    // Empties a record's payload in place: for a compressed record, a zero decompressed size and an empty zlib stream.
    private static void Wipe(byte[] doc, Found f)
    {
        Span<byte> payload = doc.AsSpan((int)f.Offset + 8, f.Length);
        payload.Clear();
        if (f.Instance == 1 && payload.Length >= 12)
        {
            // An empty zlib stream (RFC 1950: header 78 9C, one empty final block, Adler-32 of nothing).
            ReadOnlySpan<byte> empty = [0x78, 0x9C, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01];
            empty.CopyTo(payload[4..]);
        }
    }

    // The offsets of every VBAInfoAtom under a document container.
    private static List<long> VbaInfoAtoms(byte[] doc)
    {
        var atoms = new List<long>();
        long pos = 0;
        while (pos + 8 <= doc.Length)
        {
            int type = BinaryPrimitives.ReadUInt16LittleEndian(doc.AsSpan((int)pos + 2));
            long len = BinaryPrimitives.ReadUInt32LittleEndian(doc.AsSpan((int)pos + 4));
            if (pos + 8 + len > doc.Length)
            {
                break;
            }

            if (type == RtDocument)
            {
                Descend(doc, pos + 8, pos + 8 + len, atoms, 0);
            }

            pos += 8 + len;
        }

        return atoms;
    }

    private static void Descend(byte[] doc, long start, long end, List<long> atoms, int depth)
    {
        long pos = start;
        while (pos + 8 <= end)
        {
            int verInst = BinaryPrimitives.ReadUInt16LittleEndian(doc.AsSpan((int)pos));
            int type = BinaryPrimitives.ReadUInt16LittleEndian(doc.AsSpan((int)pos + 2));
            long len = BinaryPrimitives.ReadUInt32LittleEndian(doc.AsSpan((int)pos + 4));
            if (pos + 8 + len > end)
            {
                return;
            }

            if (type == RtVbaInfoAtom && len >= 12)
            {
                atoms.Add(pos);
            }
            else if ((verInst & 0x0F) == 0x0F && depth < 6)
            {
                Descend(doc, pos + 8, pos + 8 + len, atoms, depth + 1);
            }

            pos += 8 + len;
        }
    }
}
