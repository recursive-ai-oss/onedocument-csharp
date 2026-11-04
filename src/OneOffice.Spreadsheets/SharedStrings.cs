using System.Buffers;
using System.Text;
using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.Markup;
using RecursiveAI.OneOffice.Markup.Xml;

namespace RecursiveAI.OneOffice.Spreadsheets;

/// <summary>
/// ST_Xstring: text in which a character that XML cannot carry is written as <c>_xHHHH_</c> (ECMA-376 Part 1 §22.9.2.19 and §18.4.12 <c>t</c>).
/// </summary>
internal static class XString
{
    public static bool NeedsDecoding(string s) => s.Contains("_x", StringComparison.Ordinal);

    public static string Decode(string s)
    {
        if (!NeedsDecoding(s))
        {
            return s;
        }

        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '_' && i + 6 < s.Length + 0 && TryEscape(s, i, out char c))
            {
                sb.Append(c);
                i += 6;
            }
            else
            {
                sb.Append(s[i]);
            }
        }

        return sb.ToString();
    }

    private static bool TryEscape(string s, int i, out char c)
    {
        c = '\0';
        // _xHHHH_ is seven characters long.
        if (i + 7 > s.Length || s[i + 1] != 'x' || s[i + 6] != '_')
        {
            return false;
        }

        int v = 0;
        for (int k = 2; k < 6; k++)
        {
            int h = HexValue(s[i + k]);
            if (h < 0)
            {
                return false;
            }

            v = (v << 4) | h;
        }

        c = (char)v;
        return true;
    }

    private static int HexValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1,
    };

    /// <summary>Encodes text for a part: characters XML cannot carry (or would change) and text that looks like an escape.</summary>
    public static string Encode(string s)
    {
        bool need = false;
        foreach (char c in s)
        {
            if (c < 0x20 && c != '\t' && c != '\n' || c == '\r' || c is '\uFFFE' or '\uFFFF' || c == '_')
            {
                need = true;
                break;
            }
        }

        if (!need)
        {
            return s;
        }

        var sb = new StringBuilder(s.Length + 8);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c < 0x20 && c != '\t' && c != '\n' || c == '\r' || c is '\uFFFE' or '\uFFFF')
            {
                sb.Append("_x").Append(((int)c).ToString("X4", System.Globalization.CultureInfo.InvariantCulture)).Append('_');
            }
            else if (c == '_' && TryEscape(s, i, out _))
            {
                sb.Append("_x005F_");
            }
            else if (char.IsSurrogate(c) && !(char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) && !(char.IsLowSurrogate(c) && i > 0 && char.IsHighSurrogate(s[i - 1])))
            {
                sb.Append("_x").Append(((int)c).ToString("X4", System.Globalization.CultureInfo.InvariantCulture)).Append('_');
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    /// <summary>Writes a <c>t</c> element (with <c>xml:space</c> when the text has edge spaces) given an already encoded text.</summary>
    public static void WriteT(IBufferWriter<byte> w, string encoded, ReadOnlySpan<byte> prefixColon)
    {
        bool preserve = encoded.Length > 0 && (encoded[0] is ' ' or '\t' or '\n' || encoded[^1] is ' ' or '\t' or '\n');
        Raw(w, "<"u8);
        Raw(w, prefixColon);
        Raw(w, preserve ? "t xml:space=\"preserve\">"u8 : "t>"u8);
        XmlText.Escape(w, encoded, isAttributeValue: false);
        Raw(w, "</"u8);
        Raw(w, prefixColon);
        Raw(w, "t>"u8);
    }

    /// <summary>True when a string fragment holds anything beyond plain text elements: rich-text runs or phonetic data.</summary>
    public static bool HasRuns(ReadOnlySpan<byte> fragment) =>
        fragment.IndexOf("<r>"u8) >= 0 || fragment.IndexOf("<r "u8) >= 0 || fragment.IndexOf(":r>"u8) >= 0 || fragment.IndexOf(":r "u8) >= 0
        || fragment.IndexOf("rPh"u8) >= 0 || fragment.IndexOf("phoneticPr"u8) >= 0;

    public static void Raw(IBufferWriter<byte> w, ReadOnlySpan<byte> bytes)
    {
        bytes.CopyTo(w.GetSpan(bytes.Length));
        w.Advance(bytes.Length);
    }

    /// <summary>
    /// Collects the text of a <c>si</c> or <c>is</c> fragment held as bytes: every <c>t</c> directly inside it or inside an <c>r</c>, never
    /// the phonetic runs (<c>rPh</c>). The fragment was already checked to be well formed by the pass that found it.
    /// </summary>
    public static string ExtractText(ReadOnlySpan<byte> fragment)
    {
        StringBuilder? sb = null;
        string? single = null;
        int i = 0;
        while (i < fragment.Length)
        {
            int lt = fragment[i..].IndexOf((byte)'<');
            if (lt < 0)
            {
                break;
            }

            i += lt;
            if (i + 1 >= fragment.Length)
            {
                break;
            }

            byte next = fragment[i + 1];
            if (next == (byte)'!' || next == (byte)'?')
            {
                // Comment, CDATA outside t (not valid here) or processing instruction: skip to its end.
                ReadOnlySpan<byte> end = fragment[i..].StartsWith("<!--"u8) ? "-->"u8 : fragment[i..].StartsWith("<?"u8) ? "?>"u8 : ">"u8;
                int e = fragment[i..].IndexOf(end);
                i = e < 0 ? fragment.Length : i + e + end.Length;
                continue;
            }

            if (next == (byte)'/')
            {
                int e = fragment[i..].IndexOf((byte)'>');
                i = e < 0 ? fragment.Length : i + e + 1;
                continue;
            }

            int nameStart = i + 1;
            int nameEnd = nameStart;
            while (nameEnd < fragment.Length && fragment[nameEnd] is not ((byte)' ' or (byte)'>' or (byte)'/' or (byte)'\t' or (byte)'\n' or (byte)'\r'))
            {
                nameEnd++;
            }

            ReadOnlySpan<byte> name = fragment[nameStart..nameEnd];
            int colon = name.IndexOf((byte)':');
            ReadOnlySpan<byte> local = colon >= 0 ? name[(colon + 1)..] : name;
            int tagEnd = FindTagEnd(fragment, nameEnd);
            if (tagEnd < 0)
            {
                break;
            }

            bool selfClosing = fragment[tagEnd - 1] == (byte)'/';
            if (local.SequenceEqual("rPh"u8))
            {
                if (selfClosing)
                {
                    i = tagEnd + 1;
                    continue;
                }

                // rPh holds only t children, no nesting of rPh.
                int e = IndexOfEndTag(fragment, tagEnd + 1, local);
                i = e < 0 ? fragment.Length : e;
                continue;
            }

            if (local.SequenceEqual("t"u8) && !selfClosing)
            {
                int contentStart = tagEnd + 1;
                int e = IndexOfEndTag(fragment, contentStart, local);
                int contentEnd = e < 0 ? fragment.Length : e;
                string text = DecodeContent(fragment[contentStart..contentEnd]);
                if (sb is not null)
                {
                    sb.Append(text);
                }
                else if (single is null)
                {
                    single = text;
                }
                else
                {
                    sb = new StringBuilder(single).Append(text);
                }

                i = contentEnd;
                continue;
            }

            i = tagEnd + 1;
        }

        return sb is not null ? sb.ToString() : single ?? string.Empty;
    }

    private static string DecodeContent(ReadOnlySpan<byte> raw)
    {
        if (raw.StartsWith("<![CDATA["u8))
        {
            // A single CDATA section is the whole content in practice.
            int end = raw.LastIndexOf("]]>"u8);
            return Encoding.UTF8.GetString(raw[9..(end < 9 ? raw.Length : end)]);
        }

        return XmlText.GetString(raw);
    }

    private static int FindTagEnd(ReadOnlySpan<byte> d, int from)
    {
        byte quote = 0;
        for (int k = from; k < d.Length; k++)
        {
            byte b = d[k];
            if (quote != 0)
            {
                if (b == quote)
                {
                    quote = 0;
                }
            }
            else if (b is (byte)'"' or (byte)'\'')
            {
                quote = b;
            }
            else if (b == (byte)'>')
            {
                return k;
            }
        }

        return -1;
    }

    private static int IndexOfEndTag(ReadOnlySpan<byte> d, int from, ReadOnlySpan<byte> local)
    {
        int pos = from;
        while (pos < d.Length)
        {
            int lt = d[pos..].IndexOf("</"u8);
            if (lt < 0)
            {
                return -1;
            }

            int s = pos + lt + 2;
            int e = s;
            while (e < d.Length && d[e] != (byte)'>' && d[e] is not ((byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r'))
            {
                e++;
            }

            ReadOnlySpan<byte> n = d[s..e];
            int colon = n.IndexOf((byte)':');
            if ((colon >= 0 ? n[(colon + 1)..] : n).SequenceEqual(local))
            {
                return pos + lt;
            }

            pos = e;
        }

        return -1;
    }
}

/// <summary>
/// The shared string table of a workbook (ECMA-376 Part 1 §18.4): the strings cells refer to by index. It is read lazily: opening
/// scans the part once for where each <c>si</c> entry starts and ends, and a string is decoded only when asked for. New strings are
/// appended; existing entries (rich text, phonetic runs, unknown markup) are never rewritten, so they stay as stored.
/// </summary>
/// <remarks>Adding looks for an equal string only among the strings added in this session, so a string already in the stored table may be
/// stored a second time; readers accept that. Compaction is not done here.</remarks>
public sealed class SharedStringTable
{
    private readonly ReadOnlyMemory<byte> _bytes;
    private readonly int[] _starts;
    private readonly int[] _ends;
    private readonly List<string> _added = [];
    private readonly Dictionary<string, int> _addedIndex = new(StringComparer.Ordinal);
    private readonly int _rootStart;
    private readonly int _rootEnd;
    private readonly int _closeStart;
    private readonly bool _rootEmpty;
    private readonly long _storedCount;
    private readonly bool _hasCount;
    private readonly bool _hasUnique;
    private long _referencesAdded;

    private SharedStringTable()
    {
        _bytes = ReadOnlyMemory<byte>.Empty;
        _starts = [];
        _ends = [];
        _rootEmpty = false;
    }

    private SharedStringTable(ReadOnlyMemory<byte> bytes, int[] starts, int[] ends, int rootStart, int rootEnd, int closeStart, bool rootEmpty, long storedCount, bool hasCount, bool hasUnique)
    {
        _bytes = bytes;
        _starts = starts;
        _ends = ends;
        _rootStart = rootStart;
        _rootEnd = rootEnd;
        _closeStart = closeStart;
        _rootEmpty = rootEmpty;
        _storedCount = storedCount;
        _hasCount = hasCount;
        _hasUnique = hasUnique;
    }

    /// <summary>Creates a table with no entries, for a workbook that had none.</summary>
    /// <returns>The empty table.</returns>
    public static SharedStringTable CreateEmpty() => new();

    /// <summary>Gets the number of entries, stored and added.</summary>
    public int Count => _starts.Length + _added.Count;

    /// <summary>Gets a value indicating whether strings were added since the table was read.</summary>
    public bool IsModified => _added.Count > 0 || _referencesAdded != 0;

    /// <summary>Reads a table from the part's bytes.</summary>
    /// <param name="bytes">The <c>sst</c> part.</param>
    /// <param name="context">Limits and defect sink.</param>
    /// <param name="partName">The part name, for locations.</param>
    /// <returns>The table.</returns>
    /// <exception cref="OfficeFormatException">The part is not well-formed XML (<c>xml.*</c>).</exception>
    /// <exception cref="OfficeLimitException">A limit was exceeded.</exception>
    public static SharedStringTable Read(ReadOnlyMemory<byte> bytes, OpenContext context, string partName)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (bytes.Length >= 2 && ((bytes.Span[0] == 0xFF && bytes.Span[1] == 0xFE) || (bytes.Span[0] == 0xFE && bytes.Span[1] == 0xFF)))
        {
            // UTF-16 parts are transcoded by the reader, so offsets into the stored bytes would not line up.
            bytes = Encoding.UTF8.GetBytes(Encoding.Unicode.GetString(bytes.Span[2..]));
        }

        var starts = new List<int>();
        var ends = new List<int>();
        int rootStart = -1;
        int rootEnd = -1;
        int closeStart = -1;
        bool rootEmpty = false;
        long storedCount = 0;
        bool hasCount = false;
        bool hasUnique = false;
        int open = -1;
        using var reader = new MarkupReader(bytes, context, partName);
        while (reader.Read())
        {
            switch (reader.Kind)
            {
                case XmlTokenKind.StartElement or XmlTokenKind.EmptyElement when reader.Depth == 0:
                    rootStart = (int)reader.TokenOffset;
                    rootEnd = rootStart + reader.Raw.Length;
                    rootEmpty = reader.Kind == XmlTokenKind.EmptyElement;
                    foreach (XmlAttributeToken a in reader.Attributes)
                    {
                        if (a.Prefix.Length == 0 && a.LocalName.SequenceEqual("count"u8))
                        {
                            hasCount = true;
                            _ = long.TryParse(Encoding.ASCII.GetString(a.RawValue), out storedCount);
                        }
                        else if (a.Prefix.Length == 0 && a.LocalName.SequenceEqual("uniqueCount"u8))
                        {
                            hasUnique = true;
                        }
                    }

                    break;
                case XmlTokenKind.StartElement when reader.Depth == 1 && reader.LocalName.SequenceEqual("si"u8):
                    open = (int)reader.TokenOffset;
                    break;
                case XmlTokenKind.EmptyElement when reader.Depth == 1 && reader.LocalName.SequenceEqual("si"u8):
                    starts.Add((int)reader.TokenOffset);
                    ends.Add((int)reader.TokenOffset + reader.Raw.Length);
                    break;
                case XmlTokenKind.EndElement when reader.Depth == 1 && reader.LocalName.SequenceEqual("si"u8) && open >= 0:
                    starts.Add(open);
                    ends.Add((int)reader.TokenOffset + reader.Raw.Length);
                    open = -1;
                    break;
                case XmlTokenKind.EndElement when reader.Depth == 0:
                    closeStart = (int)reader.TokenOffset;
                    break;
            }
        }

        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxRecordCount), context.Limits.MaxRecordCount, starts.Count, DefectLocation.InPart(partName));
        return new SharedStringTable(bytes, [.. starts], [.. ends], rootStart, rootEnd, closeStart, rootEmpty, storedCount, hasCount, hasUnique);
    }

    /// <summary>Gets the plain text of an entry: the text runs joined, phonetic runs left out, escapes of the form <c>_x000D_</c> decoded.</summary>
    /// <param name="index">The 0-based index.</param>
    /// <returns>The text.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the table.</exception>
    public string GetText(int index)
    {
        if ((uint)index >= (uint)Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        if (index >= _starts.Length)
        {
            return _added[index - _starts.Length];
        }

        return XString.Decode(XString.ExtractText(_bytes.Span[_starts[index].._ends[index]]));
    }

    /// <summary>Tells whether an entry is more than one plain text element (rich-text runs or phonetic runs).</summary>
    /// <param name="index">The 0-based index.</param>
    /// <returns>True when the entry has runs or phonetic data.</returns>
    public bool IsRich(int index)
    {
        if ((uint)index >= (uint)Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        if (index >= _starts.Length)
        {
            return false;
        }

        return XString.HasRuns(_bytes.Span[_starts[index].._ends[index]]);
    }

    /// <summary>Adds a plain string and returns its index; a string equal to one added earlier in this session reuses it.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The index.</returns>
    public int Add(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (_addedIndex.TryGetValue(text, out int existing))
        {
            return existing;
        }

        int index = _starts.Length + _added.Count;
        _added.Add(text);
        _addedIndex[text] = index;
        return index;
    }

    internal void NoteReference(int delta) => _referencesAdded += delta;

    /// <summary>Writes the table: the stored entries as stored, the added ones appended, the counts in the root updated.</summary>
    internal byte[] ToBytes(string? emptyRootNamespace = null)
    {
        var w = new ArrayBufferWriter<byte>(_bytes.Length + (_added.Count * 40) + 256);
        long unique = Count;
        long count = Math.Max(_storedCount + _referencesAdded, unique);
        if (_bytes.IsEmpty)
        {
            XString.Raw(w, "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n"u8);
            XString.Raw(w, Encoding.UTF8.GetBytes("<sst xmlns=\"" + (emptyRootNamespace ?? "http://schemas.openxmlformats.org/spreadsheetml/2006/main") + "\" count=\"" + count + "\" uniqueCount=\"" + unique + "\">"));
            WriteAdded(w, default);
            XString.Raw(w, "</sst>"u8);
            return w.WrittenSpan.ToArray();
        }

        ReadOnlySpan<byte> src = _bytes.Span;
        XString.Raw(w, src[.._rootStart]);
        ReadOnlySpan<byte> tag = src[_rootStart.._rootEnd];
        // Rebuild the root tag with the new counts; every other attribute and namespace declaration is copied as written.
        int nameEnd = 1;
        while (nameEnd < tag.Length && tag[nameEnd] is not ((byte)' ' or (byte)'>' or (byte)'/' or (byte)'\t' or (byte)'\r' or (byte)'\n'))
        {
            nameEnd++;
        }

        ReadOnlySpan<byte> rootName = tag[1..nameEnd];
        XString.Raw(w, tag[..nameEnd]);
        // A lone start tag is not a document, so its attributes are scanned by hand.
        int pos = nameEnd;
        bool wroteCount = false;
        bool wroteUnique = false;
        while (pos < tag.Length)
        {
            while (pos < tag.Length && tag[pos] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
            {
                pos++;
            }

            if (pos >= tag.Length || tag[pos] is (byte)'>' or (byte)'/')
            {
                break;
            }

            int ns = pos;
            while (pos < tag.Length && tag[pos] != (byte)'=')
            {
                pos++;
            }

            ReadOnlySpan<byte> an = tag[ns..pos].TrimEnd((byte)' ');
            pos++;
            while (pos < tag.Length && tag[pos] is (byte)' ' or (byte)'\t')
            {
                pos++;
            }

            byte q = tag[pos];
            int vs = ++pos;
            while (tag[pos] != q)
            {
                pos++;
            }

            ReadOnlySpan<byte> raw = tag[vs..pos];
            pos++;
            XString.Raw(w, " "u8);
            XString.Raw(w, an);
            if (an.SequenceEqual("count"u8))
            {
                wroteCount = true;
                XString.Raw(w, Encoding.ASCII.GetBytes("=\"" + count + "\""));
            }
            else if (an.SequenceEqual("uniqueCount"u8))
            {
                wroteUnique = true;
                XString.Raw(w, Encoding.ASCII.GetBytes("=\"" + unique + "\""));
            }
            else
            {
                XString.Raw(w, "="u8);
                XString.Raw(w, new[] { q });
                XString.Raw(w, raw);
                XString.Raw(w, new[] { q });
            }
        }

        if (!wroteCount && (_hasCount || _added.Count > 0))
        {
            XString.Raw(w, Encoding.ASCII.GetBytes(" count=\"" + count + "\""));
        }

        if (!wroteUnique && (_hasUnique || _added.Count > 0))
        {
            XString.Raw(w, Encoding.ASCII.GetBytes(" uniqueCount=\"" + unique + "\""));
        }

        if (_rootEmpty)
        {
            XString.Raw(w, ">"u8);
            WriteAdded(w, default);
            XString.Raw(w, "</"u8);
            XString.Raw(w, rootName);
            XString.Raw(w, ">"u8);
            XString.Raw(w, src[_rootEnd..]);
            return w.WrittenSpan.ToArray();
        }

        XString.Raw(w, ">"u8);
        XString.Raw(w, src[_rootEnd.._closeStart]);
        int colon = rootName.IndexOf((byte)':');
        WriteAdded(w, colon >= 0 ? rootName[..(colon + 1)] : default);
        XString.Raw(w, src[_closeStart..]);
        return w.WrittenSpan.ToArray();
    }

    private void WriteAdded(IBufferWriter<byte> w, ReadOnlySpan<byte> prefixColon)
    {
        foreach (string s in _added)
        {
            XString.Raw(w, "<"u8);
            XString.Raw(w, prefixColon);
            XString.Raw(w, "si>"u8);
            XString.WriteT(w, XString.Encode(s), prefixColon);
            XString.Raw(w, "</"u8);
            XString.Raw(w, prefixColon);
            XString.Raw(w, "si>"u8);
        }
    }
}
