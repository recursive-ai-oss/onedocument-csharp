using System.Buffers;
using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.Markup;
using RecursiveAI.OneOffice.Markup.Xml;

namespace RecursiveAI.OneOffice.Spreadsheets.Streaming;

/// <summary>
/// The pull reader the sheet cursor runs on. A worksheet part is machine-written, regular UTF-8, and it is the one part where
/// tokenizer cost dominates, so this reader is a scanner for that shape: it finds tags with vectorised searches, does not resolve
/// namespaces and does not validate names. It reports the same tokens as <see cref="MarkupReader"/> for any well-formed part.
/// For a part it cannot scan in place (UTF-16) or on request, it hands over to <see cref="MarkupReader"/>, so the two stay
/// interchangeable and are compared against each other by the fuzz target.
/// </summary>
/// <remarks>
/// What the scanner tolerates that the full tokenizer rejects: end tags are matched by depth, not by name; attribute lists are
/// sliced, not validated (a malformed list ends early); namespace declarations are not checked. It still refuses DTDs, truncated
/// parts, end tags with nothing open and the depth, name and allocation limits.
/// </remarks>
internal sealed class SheetXml : IDisposable
{
    // Characters that end the search for a tag's closing '>' : the '>' itself and the quotes that may hide one inside a value.
    private static readonly SearchValues<byte> TagStops = SearchValues.Create(">\"'"u8);
    private static readonly SearchValues<byte> NameStops = SearchValues.Create(" \t\r\n/>"u8);

    private readonly MarkupReader? _slow;
    private readonly Stream? _stream;
    private readonly OfficeLimits _limits;
    private readonly string _partName;
    private byte[] _buf = [];
    private int _pos;
    private int _end;
    private long _consumed;
    private bool _eof;
    private int _next;
    private int _depth;

    private XmlTokenKind _kind;
    private int _tokenDepth;
    private int _tokStart;
    private int _nameStart;
    private int _nameEnd;
    private int _colon = -1;
    private int _attrStart;
    private int _attrEnd;
    private int _contentStart;
    private int _contentEnd;

    public SheetXml(Stream stream, OpenContext context, string partName, bool forceFullTokenizer = false)
    {
        _limits = context.Limits;
        _partName = partName;
        if (forceFullTokenizer)
        {
            _slow = new MarkupReader(stream, context, partName, leaveOpen: false);
            return;
        }

        _stream = stream;
        _buf = new byte[64 * 1024];
        while (_end < 4 && !_eof)
        {
            Fill();
        }

        if (!XmlEncoding.TryDetect(_buf.AsSpan(0, _end), out XmlEncodingKind kind, out int bom) || kind != XmlEncodingKind.Utf8)
        {
            // UTF-16 cannot be scanned in place; the full tokenizer buffers and transcodes it.
            var chained = new ChainStream(_buf, _end, stream);
            _stream = null;
            _slow = new MarkupReader(chained, context, partName, leaveOpen: false);
            return;
        }

        _pos = _next = bom;
    }

    /// <summary>Gets a value indicating whether the full tokenizer is doing the work.</summary>
    public bool IsFullTokenizer => _slow is not null;

    public XmlTokenKind Kind => _slow is not null ? _slow.Kind : _kind;

    public int Depth => _slow is not null ? _slow.Depth : _tokenDepth;

    public long TokenOffset => _slow is not null ? _slow.TokenOffset : _consumed + _tokStart;

    public ReadOnlySpan<byte> Prefix => _slow is not null ? _slow.Prefix : _colon >= 0 ? _buf.AsSpan(_nameStart, _colon - _nameStart) : default;

    public ReadOnlySpan<byte> LocalName => _slow is not null ? _slow.LocalName : _buf.AsSpan(_colon >= 0 ? _colon + 1 : _nameStart, _nameEnd - (_colon >= 0 ? _colon + 1 : _nameStart));

    public ReadOnlySpan<byte> Content => _slow is not null ? _slow.Content : _buf.AsSpan(_contentStart, _contentEnd - _contentStart);

    public SheetAttributes Attributes => _slow is not null
        ? new SheetAttributes(_slow.Attributes)
        : new SheetAttributes(_buf.AsSpan(_attrStart, _attrEnd - _attrStart));

    public bool Read()
    {
        if (_slow is not null)
        {
            return _slow.Read();
        }

        _pos = _next;
        _kind = XmlTokenKind.None;
        while (true)
        {
            int avail = _end - _pos;
            if (avail == 0)
            {
                if (Fill())
                {
                    continue;
                }

                if (_depth > 0)
                {
                    throw Error(XmlErrorCode.UnexpectedEnd, "The part ends inside an element.");
                }

                _next = _pos;
                return false;
            }

            ReadOnlySpan<byte> s = _buf.AsSpan(_pos, avail);
            if (s[0] != (byte)'<')
            {
                int i = s.IndexOf((byte)'<');
                if (i < 0)
                {
                    if (Fill())
                    {
                        continue;
                    }

                    i = avail;
                }

                if (_depth == 0)
                {
                    // Whitespace around the root element.
                    _pos += i;
                    continue;
                }

                SetToken(XmlTokenKind.Text, _pos, _depth);
                _contentStart = _pos;
                _contentEnd = _pos + i;
                _next = _pos + i;
                return true;
            }

            if (avail < 2)
            {
                if (Fill())
                {
                    continue;
                }

                throw Error(XmlErrorCode.UnexpectedEnd, "The part ends inside a tag.");
            }

            byte c = s[1];
            if (c == (byte)'/')
            {
                int gt = s.IndexOf((byte)'>');
                if (gt < 0)
                {
                    if (Fill())
                    {
                        continue;
                    }

                    throw Error(XmlErrorCode.UnexpectedEnd, "The part ends inside an end tag.");
                }

                if (_depth == 0)
                {
                    throw Error(XmlErrorCode.MismatchedEndTag, "An end tag has no start tag.");
                }

                _depth--;
                int ns = _pos + 2;
                int ne = _pos + gt;
                while (ne > ns && IsSpace(_buf[ne - 1]))
                {
                    ne--;
                }

                SetName(ns, ne);
                _attrStart = _attrEnd = 0;
                SetToken(XmlTokenKind.EndElement, _pos, _depth);
                _next = _pos + gt + 1;
                return true;
            }

            if (c == (byte)'?')
            {
                int e = s.IndexOf("?>"u8);
                if (e < 0)
                {
                    if (Fill())
                    {
                        continue;
                    }

                    throw Error(XmlErrorCode.UnexpectedEnd, "The part ends inside a processing instruction.");
                }

                _pos += e + 2;
                continue;
            }

            if (c == (byte)'!')
            {
                if (s.StartsWith("<!--"u8))
                {
                    int e = s[4..].IndexOf("-->"u8);
                    if (e < 0)
                    {
                        if (Fill())
                        {
                            continue;
                        }

                        throw Error(XmlErrorCode.UnexpectedEnd, "The part ends inside a comment.");
                    }

                    _pos += e + 7;
                    continue;
                }

                if (s.StartsWith("<![CDATA["u8))
                {
                    int e = s[9..].IndexOf("]]>"u8);
                    if (e < 0)
                    {
                        if (Fill())
                        {
                            continue;
                        }

                        throw Error(XmlErrorCode.UnexpectedEnd, "The part ends inside a CDATA section.");
                    }

                    SetToken(XmlTokenKind.CData, _pos, _depth);
                    _contentStart = _pos + 9;
                    _contentEnd = _pos + 9 + e;
                    _next = _pos + e + 12;
                    return true;
                }

                if (avail < 9 && !_eof)
                {
                    if (Fill())
                    {
                        continue;
                    }
                }

                throw Error(XmlErrorCode.DtdRefused, "A document type declaration is not allowed.");
            }

            // A start or empty-element tag: find the '>' that is not inside an attribute value.
            int close = FindTagEnd(s);
            if (close < 0)
            {
                if (Fill())
                {
                    continue;
                }

                throw Error(XmlErrorCode.UnexpectedEnd, "The part ends inside a tag.");
            }

            int nameStart = _pos + 1;
            int nameLen = s[1..close].IndexOfAny(NameStops);
            if (nameLen < 0)
            {
                nameLen = close - 1;
            }

            if (nameLen == 0)
            {
                throw Error(XmlErrorCode.InvalidName, "A tag has no name.");
            }

            if (nameLen > _limits.MaxXmlNameLength)
            {
                throw new OfficeLimitException(nameof(OfficeLimits.MaxXmlNameLength), _limits.MaxXmlNameLength, nameLen, Where());
            }

            SetName(nameStart, nameStart + nameLen);
            bool selfClose = _buf[_pos + close - 1] == (byte)'/' && close - 1 >= 1 + nameLen;
            _attrStart = nameStart + nameLen;
            _attrEnd = _pos + close - (selfClose ? 1 : 0);
            if (selfClose)
            {
                SetToken(XmlTokenKind.EmptyElement, _pos, _depth);
            }
            else
            {
                SetToken(XmlTokenKind.StartElement, _pos, _depth);
                _depth++;
                if (_depth > _limits.MaxXmlDepth)
                {
                    throw new OfficeLimitException(nameof(OfficeLimits.MaxXmlDepth), _limits.MaxXmlDepth, _depth, Where());
                }
            }

            _next = _pos + close + 1;
            return true;
        }
    }

    /// <summary>
    /// At a start tag whose content is one plain piece of text (no references or carriage returns, no markup) that is already in the buffer, takes the text and
    /// the end tag in one step. Nothing is consumed when it does not apply, and the caller reads on token by token. The text stays valid
    /// until the next <see cref="Read"/>.
    /// </summary>
    public bool TryReadPlainText(out ReadOnlySpan<byte> text)
    {
        text = default;
        if (_slow is not null || _kind != XmlTokenKind.StartElement)
        {
            return false;
        }

        ReadOnlySpan<byte> s = _buf.AsSpan(_next, _end - _next);
        int lt = s.IndexOf((byte)'<');
        if (lt < 0 || lt + 2 > s.Length || s[lt + 1] != (byte)'/' || XmlText.NeedsDecoding(s[..lt]))
        {
            return false;
        }

        int gt = s[(lt + 2)..].IndexOf((byte)'>');
        if (gt < 0)
        {
            return false;
        }

        int ns = _next + lt + 2;
        int ne = ns + gt;
        while (ne > ns && IsSpace(_buf[ne - 1]))
        {
            ne--;
        }

        text = _buf.AsSpan(_next, lt);
        _depth--;
        _tokStart = _next + lt;
        SetName(ns, ne);
        _attrStart = _attrEnd = 0;
        _kind = XmlTokenKind.EndElement;
        _tokenDepth = _depth;
        _next += lt + 2 + gt + 1;
        return true;
    }

    /// <summary>Skips to after the end tag of the current start tag. Does nothing at other tokens.</summary>
    public bool Skip()
    {
        if (_slow is not null)
        {
            return _slow.Skip();
        }

        if (_kind != XmlTokenKind.StartElement)
        {
            return true;
        }

        int target = _tokenDepth;
        while (Read())
        {
            if (_kind == XmlTokenKind.EndElement && _tokenDepth == target)
            {
                return true;
            }
        }

        return false;
    }

    public void Dispose()
    {
        _slow?.Dispose();
        _stream?.Dispose();
    }

    private static bool IsSpace(byte b) => b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';

    private void SetToken(XmlTokenKind kind, int start, int depth)
    {
        _kind = kind;
        _tokStart = start;
        _tokenDepth = depth;
    }

    private void SetName(int start, int end)
    {
        _nameStart = start;
        _nameEnd = end;
        int colon = _buf.AsSpan(start, end - start).IndexOf((byte)':');
        _colon = colon >= 0 ? start + colon : -1;
    }

    // Returns the offset in s of the '>' that closes the tag s starts with, or -1 when s ends first.
    private static int FindTagEnd(ReadOnlySpan<byte> s)
    {
        int i = 1;
        while (true)
        {
            int j = s[i..].IndexOfAny(TagStops);
            if (j < 0)
            {
                return -1;
            }

            i += j;
            byte b = s[i];
            if (b == (byte)'>')
            {
                return i;
            }

            int k = s[(i + 1)..].IndexOf(b);
            if (k < 0)
            {
                return -1;
            }

            i += k + 2;
        }
    }

    private OfficeFormatException Error(XmlErrorCode code, string message) =>
        new(DefectKind.Markup, XmlSyntaxException.CodeNameOf(code), message, Where());

    private DefectLocation Where() => DefectLocation.InPart(_partName, _consumed + _pos);

    // Reads more of the part behind the bytes held. Keeps the bytes from _pos on, grows the buffer when one token fills it.
    private bool Fill()
    {
        if (_eof || _stream is null)
        {
            return false;
        }

        if (_pos > 0)
        {
            Buffer.BlockCopy(_buf, _pos, _buf, 0, _end - _pos);
            _consumed += _pos;
            _end -= _pos;
            _pos = 0;
        }

        if (_end == _buf.Length)
        {
            long size = _buf.Length * 2L;
            OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxAllocationBytes), _limits.MaxAllocationBytes, size, Where());
            Array.Resize(ref _buf, (int)Math.Min(size, Array.MaxLength));
        }

        int n = _stream.Read(_buf, _end, _buf.Length - _end);
        if (n <= 0)
        {
            _eof = true;
            return false;
        }

        _end += n;
        return true;
    }
}

/// <summary>One attribute of a sheet tag: the name's prefix and local part, and the raw value (references not decoded).</summary>
internal readonly ref struct SheetAttribute
{
    public SheetAttribute(ReadOnlySpan<byte> prefix, ReadOnlySpan<byte> localName, ReadOnlySpan<byte> rawValue)
    {
        Prefix = prefix;
        LocalName = localName;
        RawValue = rawValue;
    }

    public ReadOnlySpan<byte> Prefix { get; }

    public ReadOnlySpan<byte> LocalName { get; }

    public ReadOnlySpan<byte> RawValue { get; }
}

/// <summary>Enumerates the attributes of the sheet reader's current tag. A malformed attribute list ends the enumeration.</summary>
internal ref struct SheetAttributes
{
    private XmlAttributeEnumerator _full;
    private readonly bool _isFull;
    private readonly ReadOnlySpan<byte> _region;
    private int _pos;
    private SheetAttribute _current;

    public SheetAttributes(XmlAttributeEnumerator full)
    {
        _full = full;
        _isFull = true;
        _region = default;
        _pos = 0;
        _current = default;
    }

    public SheetAttributes(ReadOnlySpan<byte> region)
    {
        _full = default;
        _isFull = false;
        _region = region;
        _pos = 0;
        _current = default;
    }

    public readonly SheetAttribute Current => _current;

    public readonly SheetAttributes GetEnumerator() => this;

    public bool MoveNext()
    {
        if (_isFull)
        {
            if (!_full.MoveNext())
            {
                return false;
            }

            XmlAttributeToken t = _full.Current;
            _current = new SheetAttribute(t.Prefix, t.LocalName, t.RawValue);
            return true;
        }

        ReadOnlySpan<byte> r = _region;
        int p = _pos;
        while (p < r.Length && IsSpace(r[p]))
        {
            p++;
        }

        if (p >= r.Length)
        {
            _pos = r.Length;
            return false;
        }

        int ns = p;
        while (p < r.Length && r[p] != (byte)'=' && !IsSpace(r[p]))
        {
            p++;
        }

        int ne = p;
        while (p < r.Length && IsSpace(r[p]))
        {
            p++;
        }

        if (p >= r.Length || r[p] != (byte)'=' || ne == ns)
        {
            _pos = r.Length;
            return false;
        }

        p++;
        while (p < r.Length && IsSpace(r[p]))
        {
            p++;
        }

        if (p >= r.Length || (r[p] != (byte)'"' && r[p] != (byte)'\''))
        {
            _pos = r.Length;
            return false;
        }

        byte quote = r[p];
        int vs = p + 1;
        int len = r[vs..].IndexOf(quote);
        if (len < 0)
        {
            _pos = r.Length;
            return false;
        }

        _pos = vs + len + 1;
        ReadOnlySpan<byte> name = r[ns..ne];
        int colon = name.IndexOf((byte)':');
        _current = colon >= 0
            ? new SheetAttribute(name[..colon], name[(colon + 1)..], r.Slice(vs, len))
            : new SheetAttribute(default, name, r.Slice(vs, len));
        return true;
    }

    private static bool IsSpace(byte b) => b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';
}

/// <summary>A stream that reads some bytes already taken from another stream, then the rest of it.</summary>
internal sealed class ChainStream : Stream
{
    private readonly byte[] _head;
    private readonly int _headLength;
    private readonly Stream _rest;
    private int _headPos;

    public ChainStream(byte[] head, int headLength, Stream rest)
    {
        _head = head;
        _headLength = headLength;
        _rest = rest;
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (_headPos < _headLength)
        {
            int n = Math.Min(buffer.Length, _headLength - _headPos);
            _head.AsSpan(_headPos, n).CopyTo(buffer);
            _headPos += n;
            return n;
        }

        return _rest.Read(buffer);
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _rest.Dispose();
        }

        base.Dispose(disposing);
    }
}
