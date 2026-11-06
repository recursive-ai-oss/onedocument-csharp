using System.Text;
using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Spreadsheets.Biff;

/// <summary>
/// The shared string table of a BIFF8 workbook ([MS-XLS] §2.4.265 SST) as one pool of UTF-16 text with an index of starts, read from the
/// SST record and the CONTINUE records after it. Rich-text runs and phonetic data are left out.
/// </summary>
internal sealed class XlsStrings
{
    // Strings built by GetString are kept up to this many characters in all; past it they are built per call.
    private const long CacheBudget = 8_000_000;

    private readonly char[] _pool;
    private readonly int[] _starts;
    private string?[]? _cache;
    private long _cached;

    private XlsStrings(char[] pool, int[] starts)
    {
        _pool = pool;
        _starts = starts;
    }

    public static XlsStrings Empty { get; } = new([], [0]);

    public int Count => _starts.Length - 1;

    public ReadOnlySpan<char> Get(int index) => (uint)index < (uint)Count ? _pool.AsSpan(_starts[index], _starts[index + 1] - _starts[index]) : default;

    public string GetString(int index)
    {
        if ((uint)index >= (uint)Count)
        {
            return string.Empty;
        }

        string?[] cache = _cache ??= new string?[Count];
        string? s = cache[index];
        if (s is not null)
        {
            return s;
        }

        ReadOnlySpan<char> text = Get(index);
        s = text.Length == 0 ? string.Empty : new string(text);
        if (_cached + s.Length <= CacheBudget)
        {
            cache[index] = s;
            _cached += s.Length;
        }

        return s;
    }

    /// <summary>Reads the table from the SST record the stream is at, with the CONTINUE records after it.</summary>
    public static XlsStrings Read(BiffRecordStream rs, OpenContext context, string path)
    {
        long offset = rs.Offset;
        var parts = new List<ReadOnlyMemory<byte>> { rs.Data.ToArray() };
        long total = rs.Length;
        while (rs.NextIsContinue())
        {
            rs.Read();
            total += rs.Length;
            OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxAllocationBytes), context.Limits.MaxAllocationBytes, total, DefectLocation.InRecord(path, offset));
            parts.Add(rs.Data.ToArray());
        }

        var rd = RecordReaderOf(parts);
        rd.U32();
        long unique = rd.U32();
        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxRecordCount), context.Limits.MaxRecordCount, unique, DefectLocation.InRecord(path, offset));
        char[] pool = new char[Math.Min(Math.Max(total, 16), 1 << 20)];
        var starts = new List<int>((int)Math.Min(unique, 1 << 20) + 1);
        int used = 0;
        var sb = new StringBuilder();
        for (long k = 0; k < unique && !rd.AtEnd; k++)
        {
            int cch = rd.U16();
            byte flags = rd.U8();
            bool wide = (flags & 1) != 0;
            int runs = (flags & 8) != 0 ? rd.U16() : 0;
            int ext = (flags & 4) != 0 ? rd.I32() : 0;
            sb.Clear();
            rd.AppendChars(sb, cch, wide, Encoding.Latin1);
            rd.Skip(runs * 4);
            if (ext > 0)
            {
                rd.Skip(ext);
            }

            starts.Add(used);
            if (used + sb.Length > pool.Length)
            {
                OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxAllocationBytes), context.Limits.MaxAllocationBytes, ((long)used + sb.Length) * 2, DefectLocation.InRecord(path, offset));
                Array.Resize(ref pool, (int)Math.Min(Math.Max((long)pool.Length * 2, (long)used + sb.Length), int.MaxValue / 2));
            }

            sb.CopyTo(0, pool, used, sb.Length);
            used += sb.Length;
            if (rd.Overrun)
            {
                context.Defects.Report(new Defect(DefectKind.Record, "biff.sst-truncated", DefectSeverity.Repaired, "The shared string table ends inside a string.", DefectLocation.InRecord(path, offset)));
                break;
            }
        }

        if (starts.Count < unique)
        {
            context.Defects.Report(new Defect(DefectKind.Record, "biff.sst-short", DefectSeverity.Repaired,
                "The shared string table holds " + starts.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " of " + unique.ToString(System.Globalization.CultureInfo.InvariantCulture) + " strings.",
                DefectLocation.InRecord(path, offset)));
        }

        starts.Add(used);
        return new XlsStrings(pool, [.. starts]);
    }

    private static RecordReader RecordReaderOf(List<ReadOnlyMemory<byte>> parts) => RecordReader.FromParts([.. parts]);
}
