using System.Text;
using RecursiveAI.OneOffice.Diagnostics;

namespace RecursiveAI.OneOffice.Packaging;

/// <summary>What <see cref="OpcXmlReader"/> is positioned on.</summary>
internal enum OpcXmlToken
{
    /// <summary>The end of the document.</summary>
    Eof = 0,

    /// <summary>A start tag (also an empty-element tag, see <see cref="OpcXmlReader.IsEmptyElement"/>).</summary>
    StartElement = 1,

    /// <summary>An end tag. An empty-element tag produces none.</summary>
    EndElement = 2,

    /// <summary>Character data or a CDATA section.</summary>
    Text = 3,
}

/// <summary>The XML is not well formed; carries the byte offset of the problem. Never leaves the packaging layer.</summary>
internal sealed class OpcXmlException(string message, int offset, bool isDtd = false) : Exception(message)
{
    /// <summary>Gets the byte offset in the (UTF-8) input.</summary>
    public int Offset { get; } = offset;

    /// <summary>Gets a value indicating whether the input declared a DTD, which is always refused.</summary>
    public bool IsDtd { get; } = isDtd;
}

/// <summary>
/// A small forward XML reader for the packaging vocabularies: <c>[Content_Types].xml</c>, relationship parts and Flat OPC. It is
/// internal and deliberately minimal: elements, attributes, text, the five predefined entities and character references. Prefixes
/// are not resolved (names are matched by local name), comments and processing instructions are skipped, and a DTD is refused,
/// so no entity can expand. It enforces the depth, attribute-count and name-length limits.
/// </summary>
/// <remarks>
/// This is a stand-in kept behind its own seam so the general-purpose tokenizer can replace it; nothing outside the packaging
/// layer sees it.
/// </remarks>
internal sealed class OpcXmlReader
{
    private readonly ReadOnlyMemory<byte> _data;
    private readonly int _maxDepth;
    private readonly int _maxAttributes;
    private readonly int _maxNameLength;
    private readonly string _partName;
    private readonly List<string> _open = [];
    private readonly List<KeyValuePair<string, string>> _attributes = [];
    private int _pos;
    private bool _pendingEnd;
    private bool _sawRoot;
    private bool _finishedRoot;

    /// <summary>Creates a reader over UTF-8 (or UTF-16 with a byte-order mark) XML.</summary>
    /// <param name="data">The part's bytes.</param>
    /// <param name="limits">The limits to enforce.</param>
    /// <param name="partName">The part's name, for the location of a limit that is hit.</param>
    public OpcXmlReader(ReadOnlyMemory<byte> data, OfficeLimits limits, string partName)
    {
        _partName = partName;
        _data = Normalize(data, out int start);
        _pos = start;
        _maxDepth = limits.MaxXmlDepth;
        _maxAttributes = limits.MaxXmlAttributeCount;
        _maxNameLength = limits.MaxXmlNameLength;
    }

    /// <summary>Gets the kind of the current token.</summary>
    public OpcXmlToken Token { get; private set; }

    /// <summary>Gets the local name (without prefix) of the current element.</summary>
    public string LocalName { get; private set; } = string.Empty;

    /// <summary>Gets a value indicating whether the current start tag was written as an empty element.</summary>
    public bool IsEmptyElement { get; private set; }

    /// <summary>Gets the number of open elements including the current one (the root element is 1).</summary>
    public int Depth { get; private set; }

    /// <summary>Gets the offset of the first byte of the current token.</summary>
    public int TokenStart { get; private set; }

    /// <summary>Gets the offset just after the current token.</summary>
    public int TokenEnd { get; private set; }

    /// <summary>Gets the decoded character data of a <see cref="OpcXmlToken.Text"/> token.</summary>
    public string Text { get; private set; } = string.Empty;

    /// <summary>Gets the underlying (normalised UTF-8) bytes, which <see cref="TokenStart"/> and <see cref="TokenEnd"/> index.</summary>
    public ReadOnlyMemory<byte> Data => _data;

    /// <summary>Gets an attribute of the current start tag by local name.</summary>
    /// <param name="localName">The attribute's name without prefix.</param>
    /// <returns>The decoded value, or null if absent. For a repeated name the first wins.</returns>
    public string? GetAttribute(string localName)
    {
        foreach (KeyValuePair<string, string> a in _attributes)
        {
            if (a.Key == localName)
            {
                return a.Value;
            }
        }

        return null;
    }

    /// <summary>Reads the next token.</summary>
    /// <returns>False at the end of the document.</returns>
    /// <exception cref="OpcXmlException">The input is not well formed or declares a DTD.</exception>
    /// <exception cref="OfficeLimitException">A depth, attribute-count or name-length limit was exceeded.</exception>
    public bool Read()
    {
        if (_pendingEnd)
        {
            _pendingEnd = false;
            Token = OpcXmlToken.EndElement;
            TokenStart = TokenEnd;
            if (_open.Count == 0)
            {
                _finishedRoot = true;
            }

            return true;
        }

        ReadOnlySpan<byte> s = _data.Span;
        while (true)
        {
            if (_pos >= s.Length)
            {
                if (_open.Count > 0)
                {
                    throw new OpcXmlException("The document ends inside an element.", _pos);
                }

                if (!_sawRoot)
                {
                    throw new OpcXmlException("There is no root element.", _pos);
                }

                Token = OpcXmlToken.Eof;
                return false;
            }

            if (s[_pos] != (byte)'<')
            {
                int start = _pos;
                int lt = s[_pos..].IndexOf((byte)'<');
                int end = lt < 0 ? s.Length : _pos + lt;
                if (_open.Count == 0)
                {
                    // Outside the root only white space is allowed.
                    for (int i = start; i < end; i++)
                    {
                        if (!IsSpace(s[i]))
                        {
                            throw new OpcXmlException("Text outside the root element.", i);
                        }
                    }

                    _pos = end;
                    continue;
                }

                Text = DecodeText(s[start..end], start);
                _pos = end;
                Token = OpcXmlToken.Text;
                TokenStart = start;
                TokenEnd = end;
                return true;
            }

            int at = _pos;
            if (Starts(s, at, "<!--"u8))
            {
                int close = IndexOf(s, at + 4, "-->"u8);
                if (close < 0)
                {
                    throw new OpcXmlException("A comment is not closed.", at);
                }

                _pos = close + 3;
                continue;
            }

            if (Starts(s, at, "<?"u8))
            {
                int close = IndexOf(s, at + 2, "?>"u8);
                if (close < 0)
                {
                    throw new OpcXmlException("A processing instruction is not closed.", at);
                }

                _pos = close + 2;
                continue;
            }

            if (Starts(s, at, "<![CDATA["u8))
            {
                if (_open.Count == 0)
                {
                    throw new OpcXmlException("CDATA outside the root element.", at);
                }

                int close = IndexOf(s, at + 9, "]]>"u8);
                if (close < 0)
                {
                    throw new OpcXmlException("A CDATA section is not closed.", at);
                }

                Text = Encoding.UTF8.GetString(s[(at + 9)..close]);
                _pos = close + 3;
                Token = OpcXmlToken.Text;
                TokenStart = at;
                TokenEnd = _pos;
                return true;
            }

            if (Starts(s, at, "<!"u8))
            {
                // <!DOCTYPE and anything else declared: entities could expand, so the whole input is refused.
                throw new OpcXmlException("A document type declaration is not allowed.", at, isDtd: true);
            }

            if (at + 1 < s.Length && s[at + 1] == (byte)'/')
            {
                ReadEndTag(s, at);
                return true;
            }

            ReadStartTag(s, at);
            return true;
        }
    }

    /// <summary>
    /// With a start tag current, reads on to its matching end tag (nothing for an empty element) and leaves the reader on that end tag.
    /// </summary>
    /// <returns>The offset of the first byte of the end tag, which is where the element's content ends.</returns>
    public int SkipToEnd()
    {
        if (Token != OpcXmlToken.StartElement)
        {
            throw new InvalidOperationException("Not on a start tag.");
        }

        int target = Depth;
        if (IsEmptyElement)
        {
            Read();
            return TokenStart;
        }

        while (Read())
        {
            if (Token == OpcXmlToken.EndElement && Depth == target)
            {
                return TokenStart;
            }
        }

        throw new OpcXmlException("The document ends inside an element.", _pos);
    }

    private static ReadOnlyMemory<byte> Normalize(ReadOnlyMemory<byte> data, out int start)
    {
        ReadOnlySpan<byte> s = data.Span;
        start = 0;
        if (s.Length >= 3 && s[0] == 0xEF && s[1] == 0xBB && s[2] == 0xBF)
        {
            start = 3;
            return data;
        }

        if (s.Length >= 2 && s[0] == 0xFF && s[1] == 0xFE)
        {
            return Encoding.UTF8.GetBytes(Encoding.Unicode.GetString(s[2..]));
        }

        if (s.Length >= 2 && s[0] == 0xFE && s[1] == 0xFF)
        {
            return Encoding.UTF8.GetBytes(Encoding.BigEndianUnicode.GetString(s[2..]));
        }

        return data;
    }

    private void ReadStartTag(ReadOnlySpan<byte> s, int at)
    {
        if (_finishedRoot)
        {
            throw new OpcXmlException("More than one root element.", at);
        }

        int p = at + 1;
        string qname = ReadName(s, ref p);
        if (qname.Length == 0)
        {
            throw new OpcXmlException("An element name is missing.", p);
        }

        _attributes.Clear();
        bool empty = false;
        while (true)
        {
            p = SkipSpace(s, p);
            if (p >= s.Length)
            {
                throw new OpcXmlException("A start tag is not closed.", at);
            }

            if (s[p] == (byte)'>')
            {
                p++;
                break;
            }

            if (s[p] == (byte)'/')
            {
                if (p + 1 >= s.Length || s[p + 1] != (byte)'>')
                {
                    throw new OpcXmlException("A '/' in a start tag is not followed by '>'.", p);
                }

                empty = true;
                p += 2;
                break;
            }

            int nameAt = p;
            string aname = ReadName(s, ref p);
            if (aname.Length == 0)
            {
                throw new OpcXmlException("An attribute name is missing.", nameAt);
            }

            p = SkipSpace(s, p);
            if (p >= s.Length || s[p] != (byte)'=')
            {
                throw new OpcXmlException("An attribute has no value.", p);
            }

            p = SkipSpace(s, p + 1);
            if (p >= s.Length || (s[p] != (byte)'"' && s[p] != (byte)'\''))
            {
                throw new OpcXmlException("An attribute value is not quoted.", p);
            }

            byte quote = s[p];
            int vstart = p + 1;
            int vend = vstart + s[vstart..].IndexOf(quote);
            if (vend < vstart)
            {
                throw new OpcXmlException("An attribute value is not closed.", p);
            }

            if (s[vstart..vend].IndexOf((byte)'<') >= 0)
            {
                throw new OpcXmlException("'<' in an attribute value.", vstart);
            }

            if (_attributes.Count >= _maxAttributes)
            {
                OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxXmlAttributeCount), _maxAttributes, _attributes.Count + 1L, DefectLocation.InPart(_partName, nameAt));
            }

            string local = LocalOf(aname);
            if (!aname.StartsWith("xmlns", StringComparison.Ordinal))
            {
                _attributes.Add(new KeyValuePair<string, string>(local, DecodeText(s[vstart..vend], vstart)));
            }

            p = vend + 1;
            if (p < s.Length && !IsSpace(s[p]) && s[p] != (byte)'>' && s[p] != (byte)'/')
            {
                throw new OpcXmlException("An attribute is not followed by white space.", p);
            }
        }

        _sawRoot = true;
        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxXmlDepth), _maxDepth, _open.Count + 1L, DefectLocation.InPart(_partName, at));
        _open.Add(qname);
        Depth = _open.Count;
        LocalName = LocalOf(qname);
        IsEmptyElement = empty;
        Token = OpcXmlToken.StartElement;
        TokenStart = at;
        TokenEnd = p;
        _pos = p;
        if (empty)
        {
            _open.RemoveAt(_open.Count - 1);
            _pendingEnd = true;
            // The synthetic end token reports the same depth and name as its start tag.
        }
    }

    private void ReadEndTag(ReadOnlySpan<byte> s, int at)
    {
        int p = at + 2;
        string qname = ReadName(s, ref p);
        p = SkipSpace(s, p);
        if (p >= s.Length || s[p] != (byte)'>')
        {
            throw new OpcXmlException("An end tag is not closed.", at);
        }

        if (_open.Count == 0 || _open[^1] != qname)
        {
            throw new OpcXmlException("An end tag does not match its start tag.", at);
        }

        Depth = _open.Count;
        LocalName = LocalOf(qname);
        _open.RemoveAt(_open.Count - 1);
        if (_open.Count == 0)
        {
            _finishedRoot = true;
        }

        IsEmptyElement = false;
        Token = OpcXmlToken.EndElement;
        TokenStart = at;
        TokenEnd = p + 1;
        _pos = p + 1;
    }

    private string ReadName(ReadOnlySpan<byte> s, ref int p)
    {
        int start = p;
        while (p < s.Length && !IsSpace(s[p]) && s[p] is not ((byte)'=' or (byte)'>' or (byte)'/' or (byte)'<' or (byte)'"' or (byte)'\''))
        {
            p++;
        }

        if (p - start > _maxNameLength)
        {
            OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxXmlNameLength), _maxNameLength, p - start, DefectLocation.InPart(_partName, start));
        }

        return Encoding.UTF8.GetString(s[start..p]);
    }

    private static string LocalOf(string qname)
    {
        int colon = qname.LastIndexOf(':');
        return colon < 0 ? qname : qname[(colon + 1)..];
    }

    // ECMA-376 Part 2 does not use anything beyond the predefined entities and numeric references in these parts, so that is all
    // that is decoded; an undeclared entity is an error rather than a guess.
    private static string DecodeText(ReadOnlySpan<byte> raw, int baseOffset)
    {
        if (raw.IndexOf((byte)'&') < 0)
        {
            return Encoding.UTF8.GetString(raw);
        }

        string text = Encoding.UTF8.GetString(raw);
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c != '&')
            {
                sb.Append(c);
                continue;
            }

            int semi = text.IndexOf(';', i + 1);
            if (semi < 0 || semi - i > 12)
            {
                throw new OpcXmlException("An entity reference is not closed.", baseOffset);
            }

            string name = text.Substring(i + 1, semi - i - 1);
            switch (name)
            {
                case "lt": sb.Append('<'); break;
                case "gt": sb.Append('>'); break;
                case "amp": sb.Append('&'); break;
                case "quot": sb.Append('"'); break;
                case "apos": sb.Append('\''); break;
                default:
                    if (name.Length > 1 && name[0] == '#' && TryCodePoint(name, out int cp))
                    {
                        sb.Append(char.ConvertFromUtf32(cp));
                        break;
                    }

                    throw new OpcXmlException($"The entity '&{name};' is not declared.", baseOffset);
            }

            i = semi;
        }

        return sb.ToString();
    }

    private static bool TryCodePoint(string name, out int codePoint)
    {
        bool ok = name.Length > 2 && (name[1] == 'x' || name[1] == 'X')
            ? int.TryParse(name.AsSpan(2), System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out codePoint)
            : int.TryParse(name.AsSpan(1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out codePoint);
        return ok && codePoint is > 0 and <= 0x10FFFF && codePoint is not (>= 0xD800 and <= 0xDFFF);
    }

    private static bool Starts(ReadOnlySpan<byte> s, int at, ReadOnlySpan<byte> prefix) =>
        at + prefix.Length <= s.Length && s.Slice(at, prefix.Length).SequenceEqual(prefix);

    private static int IndexOf(ReadOnlySpan<byte> s, int from, ReadOnlySpan<byte> marker)
    {
        if (from > s.Length)
        {
            return -1;
        }

        int i = s[from..].IndexOf(marker);
        return i < 0 ? -1 : from + i;
    }

    private static bool IsSpace(byte b) => b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';

    private static int SkipSpace(ReadOnlySpan<byte> s, int p)
    {
        while (p < s.Length && IsSpace(s[p]))
        {
            p++;
        }

        return p;
    }
}
