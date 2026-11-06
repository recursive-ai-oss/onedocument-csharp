using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Spreadsheets.Biff;

/// <summary>
/// One record of a BIFF stream: its type, where its header starts and how long its data is. The bytes stay in the stream's buffer;
/// a record never copies them ([MS-XLS] §2.1.4 Record: a 2-byte type, a 2-byte size, then at most 8224 bytes of data).
/// </summary>
/// <param name="Type">The record type (<c>0x0809</c> for a BIFF8 BOF).</param>
/// <param name="Offset">Offset of the record header in the stream.</param>
/// <param name="Length">Length of the data in bytes, not counting the 4-byte header.</param>
public readonly record struct BiffRecord(int Type, int Offset, int Length)
{
    /// <summary>Offset of the first data byte.</summary>
    public int DataOffset => Offset + 4;

    /// <summary>Offset just past the data, which is where the next record starts.</summary>
    public int End => Offset + 4 + Length;
}

/// <summary>
/// A BIFF stream cut into records: the lossless record layer under XLS. The records are kept in file order together with the stream's
/// bytes, so any record, known or not, can be read back exactly. A stream that ends inside a record keeps the complete records before it
/// and reports <c>biff.truncated-record</c>.
/// </summary>
public sealed class BiffRecordSet
{
    private readonly List<BiffRecord> _records;

    private BiffRecordSet(ReadOnlyMemory<byte> bytes, List<BiffRecord> records, string path)
    {
        Bytes = bytes;
        _records = records;
        Path = path;
    }

    /// <summary>The stream's bytes, exactly as read.</summary>
    public ReadOnlyMemory<byte> Bytes { get; }

    /// <summary>The path used to locate defects: <c>/Workbook</c>, <c>/Book</c>, or <c>/</c> for a bare stream.</summary>
    public string Path { get; }

    /// <summary>The records in file order.</summary>
    public IReadOnlyList<BiffRecord> Records => _records;

    /// <summary>Gets the data of a record.</summary>
    /// <param name="record">A record of this stream.</param>
    /// <returns>The data, without the 4-byte header.</returns>
    public ReadOnlyMemory<byte> Data(BiffRecord record) => Bytes.Slice(record.DataOffset, record.Length);

    /// <summary>Cuts a stream into records.</summary>
    /// <param name="bytes">The stream.</param>
    /// <param name="context">Limits and defect sink. <see cref="OfficeLimits.MaxRecordCount"/> bounds the records.</param>
    /// <param name="path">The stream path for defect locations.</param>
    /// <returns>The stream.</returns>
    /// <exception cref="OfficeLimitException">The stream has more records than <see cref="OfficeLimits.MaxRecordCount"/>.</exception>
    public static BiffRecordSet Parse(ReadOnlyMemory<byte> bytes, OpenContext context, string path = "/")
    {
        ArgumentNullException.ThrowIfNull(context);
        ReadOnlySpan<byte> s = bytes.Span;
        var records = new List<BiffRecord>(Math.Min(s.Length / 8 + 4, 1 << 20));
        int pos = 0;
        while (pos + 4 <= s.Length)
        {
            int type = s[pos] | (s[pos + 1] << 8);
            int len = s[pos + 2] | (s[pos + 3] << 8);
            if (pos + 4 + len > s.Length)
            {
                context.Defects.Report(new Defect(DefectKind.Record, "biff.truncated-record", DefectSeverity.Dropped,
                    "A record of type 0x" + type.ToString("X4", System.Globalization.CultureInfo.InvariantCulture) + " claims " + len.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + " bytes but the stream ends first; it and anything after it are ignored.", DefectLocation.InRecord(path, pos)));
                return new BiffRecordSet(bytes, records, path);
            }

            OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxRecordCount), context.Limits.MaxRecordCount, records.Count + 1L, DefectLocation.InRecord(path, pos));
            records.Add(new BiffRecord(type, pos, len));
            pos += 4 + len;
        }

        if (pos != s.Length && s.Length - pos > 0)
        {
            // A few stray bytes after the last EOF are common padding; only report when a partial header is left.
            context.Defects.Report(new Defect(DefectKind.Record, "biff.trailing-bytes", DefectSeverity.Dropped,
                (s.Length - pos).ToString(System.Globalization.CultureInfo.InvariantCulture) + " bytes after the last record are ignored.", DefectLocation.InRecord(path, pos)));
        }

        return new BiffRecordSet(bytes, records, path);
    }
}
