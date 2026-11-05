using System.Buffers;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup.Xml;

namespace RecursiveAI.OneDocument.Markup;

/// <summary>
/// A forward-only reader over one XML part, for streaming readers that must not hold the part or build a tree. It is the
/// tokenizer with the library's limits, errors and a refilled buffer.
/// </summary>
/// <remarks>
/// <para>
/// Over a <see cref="Stream"/>, memory is bounded by the largest single token (plus the open-element stack), not by the part;
/// <see cref="OfficeLimits.MaxPartSize"/> does not apply, <see cref="OfficeLimits.MaxAllocationBytes"/> bounds one token.
/// Over memory, nothing is copied. A UTF-16 part is buffered whole and transcoded to UTF-8 (it is bounded by
/// <see cref="OfficeLimits.MaxPartSize"/>).
/// </para>
/// <para>
/// After <see cref="Read"/> returns true the token's spans stay valid until the next <see cref="Read"/>. Reading allocates nothing per token.
/// Failures raise <see cref="OfficeFormatException"/> or <see cref="OfficeLimitException"/> located by part URI and byte offset
/// (offsets count UTF-8 bytes, from the start of the part).
/// </para>
/// </remarks>
public sealed class MarkupReader : IDisposable
{
    private readonly OfficeLimits _limits;
    private readonly string? _partUri;
    private readonly Stream? _stream;
    private readonly bool _leaveOpen;
    private readonly XmlTokenizerState _state;
    private ReadOnlyMemory<byte> _memory;
    private byte[] _buffer = [];
    private int _head;
    private int _tail;
    private bool _eof;
    private bool _encodingChecked;
    private int _pendingAdvance;
    private XmlTokenInfo _info;
    private bool _ended;
    private bool _memoryMode;
    private long _tokenOffset;

    /// <summary>Reads a part held in memory.</summary>
    /// <param name="data">The part's bytes: UTF-8 or UTF-16. Not copied unless UTF-16.</param>
    /// <param name="context">Limits and defect sink; null for the defaults.</param>
    /// <param name="partUri">The part's URI, for error locations.</param>
    /// <exception cref="OfficeFormatException">The part is in an encoding XML parts do not use, or is invalid UTF-16.</exception>
    /// <exception cref="OfficeLimitException">The part exceeds <see cref="OfficeLimits.MaxPartSize"/>.</exception>
    public MarkupReader(ReadOnlyMemory<byte> data, OpenContext? context = null, string? partUri = null)
    {
        _limits = context?.Limits ?? OfficeLimits.Default;
        _partUri = partUri;
        _state = new XmlTokenizerState(MarkupErrors.ToXmlLimits(_limits));
        _eof = true;
        _memoryMode = true;
        _encodingChecked = true;
        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxPartSize), _limits.MaxPartSize, data.Length, Where(0));
        _memory = Utf8Of(data);
    }

    /// <summary>Reads a part from a stream, refilling a buffer as it goes.</summary>
    /// <param name="stream">A readable stream positioned at the start of the part. It need not be seekable.</param>
    /// <param name="context">Limits and defect sink; null for the defaults.</param>
    /// <param name="partUri">The part's URI, for error locations.</param>
    /// <param name="leaveOpen">True to leave <paramref name="stream"/> open when the reader is disposed.</param>
    /// <param name="bufferSize">The first buffer size in bytes; it doubles when a token needs more. At least 4,096.</param>
    public MarkupReader(Stream stream, OpenContext? context = null, string? partUri = null, bool leaveOpen = false, int bufferSize = 16384)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _limits = context?.Limits ?? OfficeLimits.Default;
        _partUri = partUri;
        _state = new XmlTokenizerState(MarkupErrors.ToXmlLimits(_limits));
        _stream = stream;
        _leaveOpen = leaveOpen;
        _buffer = new byte[Math.Max(4096, bufferSize)];
    }

    /// <summary>Gets the kind of the current token; <see cref="XmlTokenKind.None"/> before the first read and after the end.</summary>
    public XmlTokenKind Kind => _info.Kind;

    /// <summary>Gets the number of open ancestors of the current token (0 for the root element's tags).</summary>
    public int Depth => _info.Depth;

    /// <summary>Gets the offset of the current token from the start of the part, in UTF-8 bytes.</summary>
    public long TokenOffset => _tokenOffset;

    /// <summary>Gets the whole current token exactly as written.</summary>
    public ReadOnlySpan<byte> Raw => _info.Raw(Block);

    /// <summary>Gets the token's content, raw (see <see cref="XmlTokenInfo.InnerStart"/>).</summary>
    public ReadOnlySpan<byte> Content => _info.Content(Block);

    /// <summary>Gets the element name or processing-instruction target as written.</summary>
    public ReadOnlySpan<byte> RawName => _info.RawName(Block);

    /// <summary>Gets the element name's prefix as written.</summary>
    public ReadOnlySpan<byte> Prefix => _info.Prefix(Block);

    /// <summary>Gets the element's local name.</summary>
    public ReadOnlySpan<byte> LocalName => _info.LocalName(Block);

    /// <summary>Gets the number of attributes of the current tag, namespace declarations included.</summary>
    public int AttributeCount => _info.AttributeCount;

    /// <summary>Gets the attributes of the current start or empty-element tag, with namespaces resolved.</summary>
    public XmlAttributeEnumerator Attributes => _info.Attributes(Block, _state);

    /// <summary>Gets the namespace of the current start, empty-element or end tag; empty when the element is in no namespace.</summary>
    public ReadOnlySpan<byte> NamespaceUri =>
        _info.Kind is XmlTokenKind.StartElement or XmlTokenKind.EmptyElement or XmlTokenKind.EndElement && _state.TryResolve(_info.Prefix(Block), out ReadOnlySpan<byte> uri) ? uri : default;

    /// <summary>Looks up the namespace a prefix is bound to in the scope of the current start, empty-element or end tag.</summary>
    /// <param name="prefix">The prefix as written; empty for the default namespace.</param>
    /// <param name="uri">The namespace URI as written. Valid until the next <see cref="Read"/>.</param>
    /// <returns>True when a declaration binds the prefix.</returns>
    public bool TryResolvePrefix(ReadOnlySpan<byte> prefix, out ReadOnlySpan<byte> uri) => _state.TryResolve(prefix, out uri);

    /// <summary>Gets the decoded text of a text, whitespace or CDATA token (allocates); null for other tokens.</summary>
    /// <returns>The text with references decoded and line ends normalised (CDATA is taken literally).</returns>
    public string? GetText() => _info.Kind switch
    {
        XmlTokenKind.Text or XmlTokenKind.Whitespace => XmlText.GetString(Content),
        XmlTokenKind.CData => System.Text.Encoding.UTF8.GetString(Content),
        _ => null,
    };

    private ReadOnlySpan<byte> Block => _memoryMode ? _memory.Span : _buffer.AsSpan(_head, _tail - _head);

    /// <summary>Reads the next token.</summary>
    /// <returns>False at the end of the part.</returns>
    /// <exception cref="OfficeFormatException">The part is not well-formed.</exception>
    /// <exception cref="OfficeLimitException">A limit was exceeded.</exception>
    /// <exception cref="IOException">The stream failed.</exception>
    public bool Read()
    {
        while (true)
        {
            switch (Advance())
            {
                case Step.Token:
                    return true;
                case Step.End:
                    return false;
                default:
                    Fill();
                    break;
            }
        }
    }

    /// <summary>Reads the next token, reading the stream asynchronously.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>False at the end of the part.</returns>
    /// <exception cref="OfficeFormatException">The part is not well-formed.</exception>
    /// <exception cref="OfficeLimitException">A limit was exceeded.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public async ValueTask<bool> ReadAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            switch (Advance())
            {
                case Step.Token:
                    return true;
                case Step.End:
                    return false;
                default:
                    await FillAsync(cancellationToken).ConfigureAwait(false);
                    break;
            }
        }
    }

    /// <summary>Skips to after the end tag of the current start tag. Does nothing at other tokens.</summary>
    /// <returns>False if the part ended first.</returns>
    public bool Skip()
    {
        if (_info.Kind != XmlTokenKind.StartElement)
        {
            return true;
        }

        int target = _info.Depth;
        while (Read())
        {
            if (_info.Kind == XmlTokenKind.EndElement && _info.Depth == target)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Releases the stream unless it was left open.</summary>
    public void Dispose()
    {
        if (_stream is not null && !_leaveOpen)
        {
            _stream.Dispose();
        }
    }

    private enum Step
    {
        Token,
        End,
        NeedMore,
    }

    private DefectLocation Where(long offset) => _partUri is null ? DefectLocation.AtText(offset) : DefectLocation.InPart(_partUri, offset);

    private ReadOnlyMemory<byte> Utf8Of(ReadOnlyMemory<byte> data)
    {
        if (!XmlEncoding.TryDetect(data.Span, out XmlEncodingKind kind, out int bom))
        {
            throw MarkupErrors.Translate(new XmlSyntaxException(XmlErrorCode.UnsupportedEncoding, 0, "The part is UTF-32 or an encoding that XML parts do not use."), _partUri);
        }

        if (kind == XmlEncodingKind.Utf8)
        {
            return data;
        }

        try
        {
            return XmlEncoding.Utf16ToUtf8(data.Span[bom..], kind == XmlEncodingKind.Utf16BigEndian);
        }
        catch (XmlSyntaxException ex)
        {
            throw MarkupErrors.Translate(ex, _partUri, o => bom + (2 * o));
        }
    }

    private Step Advance()
    {
        if (_ended)
        {
            return Step.End;
        }

        // The previous token's bytes may be dropped now that the caller is done with them.
        if (_pendingAdvance > 0)
        {
            if (_memoryMode)
            {
                _memory = _memory[_pendingAdvance..];
            }
            else
            {
                _head += _pendingAdvance;
            }

            _pendingAdvance = 0;
        }

        long blockBase = _state.Consumed;
        var tk = new XmlTokenizer(Block, _eof, _state);
        try
        {
            if (tk.Read())
            {
                _info = tk.Info;
                _tokenOffset = blockBase + _info.Start;
                _pendingAdvance = tk.BytesConsumed;
                return Step.Token;
            }
        }
        catch (XmlSyntaxException ex)
        {
            _ended = true;
            _info = default;
            throw MarkupErrors.Translate(ex, _partUri);
        }

        int consumed = tk.BytesConsumed;
        if (tk.EndOfDocument)
        {
            _ended = true;
            _info = default;
            return Step.End;
        }

        // The block ended inside a token: keep the bytes the tokenizer did consume (a byte order mark) out of the retry.
        _info = default;
        if (_memoryMode)
        {
            _memory = _memory[consumed..];
        }
        else
        {
            _head += consumed;
        }

        return Step.NeedMore;
    }

    private void MakeRoom()
    {
        if (_head > 0)
        {
            Buffer.BlockCopy(_buffer, _head, _buffer, 0, _tail - _head);
            _tail -= _head;
            _head = 0;
        }

        if (_tail == _buffer.Length)
        {
            long newSize = _buffer.Length * 2L;
            OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxAllocationBytes), _limits.MaxAllocationBytes, newSize, Where(_state.Consumed));
            Array.Resize(ref _buffer, (int)Math.Min(newSize, Array.MaxLength));
        }
    }

    private void Fill()
    {
        MakeRoom();
        int n = _stream!.Read(_buffer, _tail, _buffer.Length - _tail);
        AfterRead(n);
    }

    private async ValueTask FillAsync(CancellationToken cancellationToken)
    {
        MakeRoom();
        int n = await _stream!.ReadAsync(_buffer.AsMemory(_tail), cancellationToken).ConfigureAwait(false);
        AfterRead(n);
    }

    private void AfterRead(int n)
    {
        if (n == 0)
        {
            _eof = true;
            return;
        }

        _tail += n;
        if (_encodingChecked || (_tail - _head < 4 && !_eof))
        {
            return;
        }

        _encodingChecked = true;
        if (!XmlEncoding.TryDetect(_buffer.AsSpan(_head, _tail - _head), out XmlEncodingKind kind, out _) || kind != XmlEncodingKind.Utf8)
        {
            // UTF-16 cannot be tokenized in place: buffer the rest of the part and switch to memory mode.
            SwitchToMemory();
        }
    }

    private void SwitchToMemory()
    {
        var all = new MemoryStream();
        all.Write(_buffer, _head, _tail - _head);
        long cap = _limits.MaxPartSize;
        byte[] chunk = ArrayPool<byte>.Shared.Rent(16384);
        try
        {
            int n;
            while ((n = _stream!.Read(chunk, 0, chunk.Length)) > 0)
            {
                all.Write(chunk, 0, n);
                OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxPartSize), cap, all.Length, Where(all.Length));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk);
        }

        _memory = Utf8Of(all.ToArray());
        _eof = true;
        _memoryMode = true;
        _head = _tail = 0;
    }
}
