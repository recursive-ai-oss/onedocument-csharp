using System.Text;
using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.Markup;
using RecursiveAI.OneOffice.Markup.Xml;

namespace RecursiveAI.OneOffice.Spreadsheets.Streaming;

/// <summary>
/// The shared string table as the streaming reader holds it: one pass over the part keeps only the plain text of each entry (runs
/// joined, phonetic runs left out, references decoded) as one UTF-8 block with an offset per entry. The markup is not kept, so the
/// table costs the text plus four bytes per entry. Entries are read as spans, without allocating.
/// </summary>
/// <remarks>ECMA-376 Part 1 CT_Sst, CT_Rst: an entry is a <c>t</c> element or runs <c>r</c> that each hold a <c>t</c>; <c>rPh</c> is phonetic data.</remarks>
internal sealed class StreamStrings
{
    private readonly byte[] _text;
    private readonly int[] _offsets; // Count + 1 entries

    private StreamStrings(byte[] text, int[] offsets)
    {
        _text = text;
        _offsets = offsets;
    }

    public static StreamStrings Empty { get; } = new([], [0]);

    public int Count => _offsets.Length - 1;

    /// <summary>Gets the UTF-8 text of an entry, with <c>_xHHHH_</c> escapes still in place.</summary>
    public ReadOnlySpan<byte> GetUtf8(int index) =>
        (uint)index < (uint)Count ? _text.AsSpan(_offsets[index], _offsets[index + 1] - _offsets[index]) : default;

    public static StreamStrings Read(Stream part, OpenContext context, string partName)
    {
        using var reader = new MarkupReader(part, context, partName, leaveOpen: true);
        byte[] text = new byte[4096];
        int length = 0;
        int[] offsets = new int[1024];
        int count = 0;
        offsets[0] = 0;
        bool inSi = false;
        while (reader.Read())
        {
            if (reader.Depth == 1 && reader.LocalName.SequenceEqual("si"u8))
            {
                if (reader.Kind == XmlTokenKind.StartElement)
                {
                    inSi = true;
                }
                else if (reader.Kind == XmlTokenKind.EmptyElement)
                {
                    Close();
                }
                else if (reader.Kind == XmlTokenKind.EndElement && inSi)
                {
                    inSi = false;
                    Close();
                }

                continue;
            }

            if (!inSi || reader.Kind != XmlTokenKind.StartElement)
            {
                continue;
            }

            ReadOnlySpan<byte> name = reader.LocalName;
            if (name.SequenceEqual("rPh"u8))
            {
                reader.Skip();
            }
            else if (name.SequenceEqual("t"u8) && reader.Depth is 2 or 3)
            {
                StreamXml.AppendText(reader, ref text, ref length);
                OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxAllocationBytes), context.Limits.MaxAllocationBytes, length, DefectLocation.InPart(partName, reader.TokenOffset));
            }
        }

        return new StreamStrings(text, offsets.AsSpan(0, count + 1).ToArray());

        void Close()
        {
            OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxRecordCount), context.Limits.MaxRecordCount, count + 1L, DefectLocation.InPart(partName, reader.TokenOffset));
            count++;
            if (count + 1 > offsets.Length)
            {
                Array.Resize(ref offsets, offsets.Length * 2);
            }

            offsets[count] = length;
        }
    }

    // Asking for the same entry again and again (a column of repeated names) would build the same string every time, so the strings
    // already built are kept, up to a budget; past it, strings are built per call as before. The budget bounds the extra memory.
    private const int MaxCachedEntries = 4_000_000;
    private const long CacheCharBudget = 8_000_000;
    private string?[]? _cache;
    private long _cachedChars;

    public string GetString(int index)
    {
        if ((uint)index >= (uint)Count || Count > MaxCachedEntries)
        {
            return StreamXml.ToString(GetUtf8(index));
        }

        string?[] cache = _cache ??= new string?[Count];
        string? s = cache[index];
        if (s is not null)
        {
            return s;
        }

        s = StreamXml.ToString(GetUtf8(index));
        if (_cachedChars + s.Length <= CacheCharBudget)
        {
            cache[index] = s;
            _cachedChars += s.Length;
        }

        return s;
    }

    public static int Utf16Length(ReadOnlySpan<byte> utf8) => Encoding.UTF8.GetCharCount(utf8);
}
