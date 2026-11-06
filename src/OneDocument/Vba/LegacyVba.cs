using System.Linq;
using System.Buffers.Binary;
using System.Globalization;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.IO;
using RecursiveAI.OneDocument.Cfb;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Vba;

/// <summary>Removing the VBA project from Word 97-2003 and Excel 97-2003 files, at the level of the compound file and the workbook stream's records.</summary>
internal static class LegacyVba
{
    // [MS-XLS] §2.4.185 ObProj, §2.4.184 ObNoMacros: the records that announce a project; §2.4.28 BoundSheet8, §2.4.144 Index, §2.4.106 ExtSst.
    private const ushort RecBof = 0x0809, RecEof = 0x000A, RecFilePass = 0x002F, RecBoundSheet = 0x0085, RecExtSst = 0x00FF, RecIndex = 0x020B;
    private const ushort RecObProj = 0x00D3, RecObNoMacros = 0x01BD;

    internal static VbaRemovalReport Copy(IByteSource source, OfficeFormat format, Stream destination, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[] buffer = new byte[81920];
        long offset = 0;
        int n;
        while ((n = source.Read(offset, buffer)) > 0)
        {
            destination.Write(buffer, 0, n);
            offset += n;
        }

        return new VbaRemovalReport(format);
    }

    private static long StreamBytesUnder(CfbPackage cfb, string prefix)
    {
        long total = 0;
        foreach (CfbStreamPart p in cfb.Streams)
        {
            if (p.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                total += p.Length;
            }
        }

        return total;
    }

    internal static VbaRemovalReport RemoveFromDoc(IByteSource source, Stream destination, OpenContext context, CancellationToken cancellationToken)
    {
        using CfbPackage cfb = CfbPackage.Open(source, context);
        var report = new VbaRemovalReport(OfficeFormat.Doc);
        // [MS-DOC] §2.5.2 FibBase: bit 8 of the flags word at 0x0A is fEncrypted.
        if (cfb.FindPart("/WordDocument") is { } wd && wd.Length >= 12 && (BinaryPrimitives.ReadUInt16LittleEndian(wd.GetBytes().Span[0x0A..]) & 0x0100) != 0)
        {
            throw new OfficeUnsupportedException("removing macros from an encrypted Word file (decrypt it first)", DefectLocation.InRecord("/WordDocument", 0x0A));
        }

        long bytes = StreamBytesUnder(cfb, "/Macros/");
        if (bytes == 0 && cfb.FindPart("/Macros") is null && !cfb.Storages.Any(s => string.Equals(s.Path, "/Macros", StringComparison.OrdinalIgnoreCase)))
        {
            cfb.WriteTo(destination, cancellationToken);
            return report;
        }

        report.HadProject = true;
        try
        {
            VbaProject project = VbaProject.Parse(cfb, "/Macros", "/Macros", context);
            report.ModulesRemoved = project.Modules.Count;
            report.SignaturesRemoved = project.Signatures;
        }
        catch (OfficeException ex)
        {
            report.Note("vba.project-unreadable", "The project could not be read before removal, so its modules are not counted: " + ex.Message, ex.Location);
        }

        int streams = cfb.RemoveStorage("/Macros");
        report.Add(VbaRemovedKind.Project, "/Macros", bytes, "Removed the Macros storage (" + streams.ToString(CultureInfo.InvariantCulture) + " streams).");
        // The command table (customizations, key bindings that name macros) and a signature kept in the table stream's user variables are
        // not interpreted; a binding to a macro that is gone does nothing.
        report.Note("vba.doc-customizations-kept", "Toolbar, menu and key customizations, and a signature kept in the document's user variables, are left as they are; a binding to a removed macro does nothing.", DefectLocation.InRecord("/1Table"));
        cfb.WriteTo(destination, cancellationToken);
        return report;
    }

    private readonly record struct Record(int Offset, ushort Type, int Length);

    internal static VbaRemovalReport RemoveFromXls(IByteSource source, Stream destination, OpenContext context, CancellationToken cancellationToken)
    {
        using CfbPackage cfb = CfbPackage.Open(source, context);
        var report = new VbaRemovalReport(OfficeFormat.Xls);
        const string storage = "/_VBA_PROJECT_CUR";
        bool hasStorage = cfb.Streams.Any(p => p.Name.StartsWith(storage + "/", StringComparison.OrdinalIgnoreCase));
        CfbStreamPart? book = cfb.FindPart("/Workbook") as CfbStreamPart ?? cfb.FindPart("/Book") as CfbStreamPart;
        if (book is null)
        {
            throw new OfficeFormatException(DefectKind.Structure, "vba.workbook-stream-missing", "The compound file has no Workbook stream.", DefectLocation.InRecord("/Workbook"));
        }

        byte[] old = book.GetBytes().ToArray();
        List<Record> records = Scan(old, context.Limits);
        var doomed = new List<Record>();
        int depth = 0;
        foreach (Record r in records)
        {
            if (r.Type == RecBof)
            {
                depth++;
            }
            else if (r.Type == RecEof)
            {
                depth--;
            }
            else if (depth == 1 && r.Type == RecFilePass)
            {
                throw new OfficeUnsupportedException("removing macros from an encrypted Excel file (decrypt it first)", DefectLocation.InRecord(book.Name, r.Offset));
            }
            else if (depth == 1 && r.Type is RecObProj or RecObNoMacros)
            {
                doomed.Add(r);
            }

            if (depth <= 0 && r.Type == RecEof)
            {
                break; // the workbook globals end at the first EOF; sheets follow
            }
        }

        if (!hasStorage && doomed.Count == 0)
        {
            cfb.WriteTo(destination, cancellationToken);
            return report;
        }

        report.HadProject = true;
        if (hasStorage)
        {
            try
            {
                VbaProject project = VbaProject.Parse(cfb, storage, storage, context);
                report.ModulesRemoved = project.Modules.Count;
                report.SignaturesRemoved = project.Signatures;
            }
            catch (OfficeException ex)
            {
                report.Note("vba.project-unreadable", "The project could not be read before removal, so its modules are not counted: " + ex.Message, ex.Location);
            }

            long bytes = StreamBytesUnder(cfb, storage + "/");
            int streams = cfb.RemoveStorage(storage);
            report.Add(VbaRemovedKind.Project, storage, bytes, "Removed the project storage (" + streams.ToString(CultureInfo.InvariantCulture) + " streams).");
        }

        if (doomed.Count > 0)
        {
            byte[] edited = Rewrite(old, records, doomed, book.Name, report);
            book.SetBytes(edited);
        }

        cfb.WriteTo(destination, cancellationToken);
        return report;
    }

    // Splits a BIFF stream into records. A record whose length runs past the end ends the scan; the bytes after it are kept as they are.
    private static List<Record> Scan(byte[] data, OfficeLimits limits)
    {
        var list = new List<Record>();
        int pos = 0;
        while (pos + 4 <= data.Length)
        {
            ushort type = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(pos));
            int len = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(pos + 2));
            if (pos + 4 + len > data.Length)
            {
                break;
            }

            OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxRecordCount), limits.MaxRecordCount, list.Count + 1L, DefectLocation.InRecord("Workbook", pos));
            list.Add(new Record(pos, type, len));
            pos += 4 + len;
        }

        return list;
    }

    // Drops the doomed records and corrects every stream position that pointed past them: the sheet positions in BoundSheet8, the DefColWidth
    // and DBCell positions in Index, and the string positions in ExtSst ([MS-XLS] §2.5.167 ISSTInf).
    private static byte[] Rewrite(byte[] old, List<Record> records, List<Record> doomed, string stream, VbaRemovalReport report)
    {
        int Map(long position)
        {
            long shift = 0;
            foreach (Record d in doomed)
            {
                if (d.Offset < position)
                {
                    shift += 4 + d.Length;
                }
            }

            return (int)Math.Max(0, position - shift);
        }

        long removedBytes = 0;
        foreach (Record d in doomed)
        {
            removedBytes += 4 + d.Length;
            report.Add(VbaRemovedKind.Marker, stream + "@" + d.Offset.ToString(CultureInfo.InvariantCulture), 4 + d.Length, d.Type == RecObProj ? "Removed the ObProj record." : "Removed the ObNoMacros record.");
        }

        byte[] result = new byte[old.Length - removedBytes];
        int w = 0;
        int lastEnd = 0;
        foreach (Record r in records)
        {
            lastEnd = r.Offset + 4 + r.Length;
            if (doomed.Contains(r))
            {
                continue;
            }

            Buffer.BlockCopy(old, r.Offset, result, w, 4 + r.Length);
            int data = w + 4;
            switch (r.Type)
            {
                case RecBoundSheet when r.Length >= 4:
                    Patch(result, data, Map(BinaryPrimitives.ReadUInt32LittleEndian(old.AsSpan(r.Offset + 4))));
                    break;
                case RecIndex when r.Length >= 16:
                    // reserved (4), rwMic (4), rwMac (4), ibXF (4), then one DBCell position per row block.
                    for (int at = 12; at + 4 <= r.Length; at += 4)
                    {
                        Patch(result, data + at, Map(BinaryPrimitives.ReadUInt32LittleEndian(old.AsSpan(r.Offset + 4 + at))));
                    }

                    break;
                case RecExtSst when r.Length >= 2:
                    // dsst (2), then ISSTInf entries: ib (4), cbOffset (2), reserved (2).
                    for (int at = 2; at + 8 <= r.Length; at += 8)
                    {
                        Patch(result, data + at, Map(BinaryPrimitives.ReadUInt32LittleEndian(old.AsSpan(r.Offset + 4 + at))));
                    }

                    break;
            }

            w += 4 + r.Length;
        }

        if (lastEnd < old.Length)
        {
            Buffer.BlockCopy(old, lastEnd, result, w, old.Length - lastEnd);
        }

        return result;
    }

    private static void Patch(byte[] buffer, int at, int value) => BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(at), (uint)value);
}
