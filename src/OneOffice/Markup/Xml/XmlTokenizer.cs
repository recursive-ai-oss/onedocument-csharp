using System.Text;

namespace RecursiveAI.OneOffice.Markup.Xml;

/// <summary>
/// A forward-only, allocation-free tokenizer for UTF-8 XML (XML 1.0 fifth edition, Namespaces in XML 1.0 third edition).
/// </summary>
/// <remarks>
/// <para>
/// Tokens tile the input: every byte after an optional UTF-8 byte order mark belongs to exactly one token, so copying the raw
/// tokens reproduces the input byte for byte. Names keep the prefixes as written. Text and attribute values are returned raw;
/// <see cref="XmlText"/> decodes them on demand.
/// </para>
/// <para>
/// The input is checked as it is read: names, attribute syntax, quoting, references (only the five predefined entities exist),
/// characters, UTF-8, matching end tags, a single root element, no text outside it, duplicate attributes, bound prefixes and
/// the namespace declaration rules. A document type declaration is refused with <see cref="XmlErrorCode.DtdRefused"/>.
/// Anything wrong raises <see cref="XmlSyntaxException"/> with the byte offset. There is no recovery.
/// </para>
/// <para>
/// To read a stream, give each tokenizer a block that starts where the previous one stopped (<see cref="BytesConsumed"/>) and the
/// same <see cref="XmlTokenizerState"/>. When <see cref="Read"/> returns false and <see cref="EndOfDocument"/> is false, the
/// token continues past the end of the block: refill and read again. Spans handed out are valid until the next <see cref="Read"/>.
/// </para>
/// </remarks>
public ref struct XmlTokenizer
{
    private readonly ReadOnlySpan<byte> _data;
    private readonly bool _final;
    private readonly XmlTokenizerState _state;
    private readonly long _blockBase;
    private int _pos;
    private XmlTokenInfo _info;
    private bool _ended;

    /// <summary>Starts reading a complete document held in one buffer, with the default limits.</summary>
    /// <param name="data">The whole document as UTF-8. A byte order mark is skipped.</param>
    public XmlTokenizer(ReadOnlySpan<byte> data)
        : this(data, true, new XmlTokenizerState())
    {
    }

    /// <summary>Starts reading a complete document held in one buffer.</summary>
    /// <param name="data">The whole document as UTF-8. A byte order mark is skipped.</param>
    /// <param name="limits">The limits to enforce.</param>
    public XmlTokenizer(ReadOnlySpan<byte> data, XmlLimits limits)
        : this(data, true, new XmlTokenizerState(limits))
    {
    }

    /// <summary>Starts reading a block of a document.</summary>
    /// <param name="data">The block. It must begin at <see cref="XmlTokenizerState.Consumed"/> of <paramref name="state"/>.</param>
    /// <param name="isFinalBlock">True when no bytes follow this block.</param>
    /// <param name="state">The state shared by all blocks of the document.</param>
    public XmlTokenizer(ReadOnlySpan<byte> data, bool isFinalBlock, XmlTokenizerState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _data = data;
        _final = isFinalBlock;
        _state = state;
        _blockBase = state.Consumed;
        _pos = 0;
        _info = default;
        _ended = false;
    }

    /// <summary>Gets the kind of the current token, or <see cref="XmlTokenKind.None"/> before the first read.</summary>
    public readonly XmlTokenKind Kind => _info.Kind;

    /// <summary>Gets where the current token lies in the block, as offsets.</summary>
    public readonly XmlTokenInfo Info => _info;

    /// <summary>Gets the state, which holds the namespace scope.</summary>
    public readonly XmlTokenizerState State => _state;

    /// <summary>Gets the offset in the block after the last token read. A following block starts here.</summary>
    public readonly int BytesConsumed => _pos;

    /// <summary>Gets a value that says whether <see cref="Read"/> stopped because the document ended (true) or the block ended mid-token (false).</summary>
    public readonly bool EndOfDocument => _ended;

    /// <summary>Gets the offset of the current token from the start of the whole input.</summary>
    public readonly long TokenOffset => _blockBase + _info.Start;

    /// <summary>Gets the whole current token, markup included, exactly as written.</summary>
    public readonly ReadOnlySpan<byte> Raw => _info.Raw(_data);

    /// <summary>Gets the content of the current token, raw (see <see cref="XmlTokenInfo.InnerStart"/>).</summary>
    public readonly ReadOnlySpan<byte> Content => _info.Content(_data);

    /// <summary>Gets the element name or processing-instruction target as written, prefix included.</summary>
    public readonly ReadOnlySpan<byte> RawName => _info.RawName(_data);

    /// <summary>Gets the element name's prefix as written; empty when there is none.</summary>
    public readonly ReadOnlySpan<byte> Prefix => _info.Prefix(_data);

    /// <summary>Gets the local part of the element name.</summary>
    public readonly ReadOnlySpan<byte> LocalName => _info.LocalName(_data);

    /// <summary>Gets the number of open ancestors of the current token.</summary>
    public readonly int Depth => _info.Depth;

    /// <summary>Gets the number of attributes of the current start or empty-element tag.</summary>
    public readonly int AttributeCount => _info.AttributeCount;

    /// <summary>Gets the attributes of the current start or empty-element tag, resolved against the current namespace scope.</summary>
    public readonly XmlAttributeEnumerator Attributes => _info.Attributes(_data, _state);

    /// <summary>
    /// Gets the namespace of the current start, empty-element or end tag, entity references decoded. Empty when the element is in
    /// no namespace. Valid until the next read.
    /// </summary>
    public readonly ReadOnlySpan<byte> NamespaceUri
    {
        get
        {
            if (_info.Kind is not (XmlTokenKind.StartElement or XmlTokenKind.EmptyElement or XmlTokenKind.EndElement))
            {
                return default;
            }

            return _state.TryResolve(_info.Prefix(_data), out ReadOnlySpan<byte> uri) ? uri : default;
        }
    }

    /// <summary>Reads the next token.</summary>
    /// <returns>
    /// True when a token is current. False at the end of the document (<see cref="EndOfDocument"/> is true) or when a non-final
    /// block ends inside a token (<see cref="EndOfDocument"/> is false; supply more bytes from <see cref="BytesConsumed"/>).
    /// </returns>
    /// <exception cref="XmlSyntaxException">The input is not well-formed XML, uses a DTD, or exceeds a limit.</exception>
    public bool Read()
    {
        if (_ended)
        {
            return false;
        }

        _state.EndPending();
        ReadOnlySpan<byte> d = _data;
        if (!_state.Started && !Begin(d))
        {
            return false;
        }

        int p = _pos;
        if (p >= d.Length)
        {
            return AtEnd(d);
        }

        return d[p] == (byte)'<' ? ReadMarkup(d, p) : ReadText(d, p);
    }

    /// <summary>Skips the current element's content, so that the next token follows its end tag.</summary>
    /// <remarks>For anything but a start tag this does nothing. The skipped tokens are validated like any others.</remarks>
    /// <returns>False if the document ended or the block ended before the matching end tag; true otherwise.</returns>
    /// <exception cref="XmlSyntaxException">The skipped content is not well-formed.</exception>
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

    private bool Begin(ReadOnlySpan<byte> d)
    {
        // XML 1.0 Appendix F: byte order marks and the encoded "<" tell the encoding. UTF-8 is the only one read here.
        if (d.Length < 4 && !_final)
        {
            return false;
        }

        int p = 0;
        if (d.Length >= 3 && d[0] == 0xEF && d[1] == 0xBB && d[2] == 0xBF)
        {
            p = 3;
        }
        else if (d.Length >= 2 && ((d[0] == 0xFF && d[1] == 0xFE) || (d[0] == 0xFE && d[1] == 0xFF) || (d[0] == 0 && (d[1] == 0x3C || d[1] == 0)) || (d[0] == 0x3C && d[1] == 0)))
        {
            throw new XmlSyntaxException(XmlErrorCode.UnsupportedEncoding, _blockBase, "The input is UTF-16 or UTF-32; transcode it to UTF-8 first.");
        }

        _state.Started = true;
        _pos = p;
        _state.Consumed = _blockBase + p;
        return true;
    }

    private bool AtEnd(ReadOnlySpan<byte> d)
    {
        if (!_final)
        {
            return false;
        }

        long at = _blockBase + d.Length;
        if (_state.Depth != 0)
        {
            throw new XmlSyntaxException(XmlErrorCode.UnexpectedEnd, at, "The input ends with elements still open.");
        }

        if (!_state.IsFragment && !_state.RootSeen)
        {
            throw new XmlSyntaxException(XmlErrorCode.NoRoot, at, "The document has no root element.");
        }

        _ended = true;
        _info = default;
        return false;
    }

    // Either the block is not final (the caller refills) or the document really is truncated.
    private bool Incomplete(ReadOnlySpan<byte> d)
    {
        if (_final)
        {
            throw new XmlSyntaxException(XmlErrorCode.UnexpectedEnd, _blockBase + d.Length, "The input ends inside a token.");
        }

        return false;
    }

    private bool Emit(XmlTokenKind kind, int start, int end, int nameStart, int nameEnd, int colon, int innerStart, int innerEnd, int attributeCount, int depth)
    {
        _info = new XmlTokenInfo(kind, start, end, nameStart, nameEnd, colon, innerStart, innerEnd, attributeCount, depth);
        _pos = end;
        _state.Consumed = _blockBase + end;
        _state.TokenCount++;
        return true;
    }

    private bool ReadText(ReadOnlySpan<byte> d, int p)
    {
        int lt = d[p..].IndexOf((byte)'<');
        int end;
        if (lt < 0)
        {
            if (!_final)
            {
                return false;
            }

            end = d.Length;
        }
        else
        {
            end = p + lt;
        }

        ReadOnlySpan<byte> span = d[p..end];
        XmlTokenKind kind;
        if (XmlScan.IsWhitespaceOnly(span))
        {
            kind = XmlTokenKind.Whitespace;
        }
        else
        {
            // XML 1.0 §2.1: character data is content, so it can only be inside the root element.
            if (!_state.IsFragment && _state.Depth == 0)
            {
                throw new XmlSyntaxException(XmlErrorCode.MisplacedContent, _blockBase + p, "Text is not allowed outside the root element.");
            }

            XmlScan.ValidateData(span, _blockBase + p, XmlScan.DataMode.Text);
            kind = XmlTokenKind.Text;
        }

        return Emit(kind, p, end, 0, 0, -1, p, end, 0, _state.Depth);
    }

    private bool ReadMarkup(ReadOnlySpan<byte> d, int p)
    {
        if (p + 1 >= d.Length)
        {
            return Incomplete(d);
        }

        switch (d[p + 1])
        {
            case (byte)'/':
                return ReadEndTag(d, p);
            case (byte)'?':
                return ReadProcessingInstruction(d, p);
            case (byte)'!':
                return ReadBang(d, p);
            default:
                return ReadStartTag(d, p);
        }
    }

    private bool ReadBang(ReadOnlySpan<byte> d, int p)
    {
        ReadOnlySpan<byte> rest = d[p..];
        if (rest.StartsWith("<!--"u8))
        {
            return ReadComment(d, p);
        }

        if (rest.StartsWith("<![CDATA["u8))
        {
            return ReadCData(d, p);
        }

        if (!_final && ("<![CDATA["u8.StartsWith(rest) || "<!--"u8.StartsWith(rest)))
        {
            return false;
        }

        if (rest.Length >= 3 && ((rest[2] | 0x20) >= 'a' && (rest[2] | 0x20) <= 'z'))
        {
            throw new XmlSyntaxException(XmlErrorCode.DtdRefused, _blockBase + p, "Document type declarations and entity declarations are not accepted.");
        }

        if (rest.Length < 3 && !_final)
        {
            return false;
        }

        throw new XmlSyntaxException(XmlErrorCode.Syntax, _blockBase + p, "Unrecognised markup declaration.");
    }

    private bool ReadComment(ReadOnlySpan<byte> d, int p)
    {
        int cs = p + 4;
        int i = d[cs..].IndexOf("--"u8);
        if (i < 0 || cs + i + 2 >= d.Length)
        {
            return Incomplete(d);
        }

        int dash = cs + i;

        // XML 1.0 §2.5: "--" must not occur in a comment except as the start of "-->".
        if (d[dash + 2] != (byte)'>')
        {
            throw new XmlSyntaxException(XmlErrorCode.Syntax, _blockBase + dash, "\"--\" is not allowed inside a comment.");
        }

        XmlScan.ValidateData(d[cs..dash], _blockBase + cs, XmlScan.DataMode.Raw);
        return Emit(XmlTokenKind.Comment, p, dash + 3, 0, 0, -1, cs, dash, 0, _state.Depth);
    }

    private bool ReadCData(ReadOnlySpan<byte> d, int p)
    {
        int cs = p + 9;
        int i = d[cs..].IndexOf("]]>"u8);
        if (i < 0)
        {
            return Incomplete(d);
        }

        if (!_state.IsFragment && _state.Depth == 0)
        {
            throw new XmlSyntaxException(XmlErrorCode.MisplacedContent, _blockBase + p, "A CDATA section is not allowed outside the root element.");
        }

        int ce = cs + i;
        XmlScan.ValidateData(d[cs..ce], _blockBase + cs, XmlScan.DataMode.Raw);
        return Emit(XmlTokenKind.CData, p, ce + 3, 0, 0, -1, cs, ce, 0, _state.Depth);
    }

    private bool ReadProcessingInstruction(ReadOnlySpan<byte> d, int p)
    {
        int ns = p + 2;
        int ne = XmlScan.ScanName(d, ns);
        if (ne >= d.Length)
        {
            return Incomplete(d);
        }

        if (ne == ns)
        {
            throw new XmlSyntaxException(XmlErrorCode.InvalidName, _blockBase + ns, "A processing instruction needs a target name.");
        }

        CheckNameLength(ne - ns, ns);
        ReadOnlySpan<byte> target = d[ns..ne];
        XmlScan.ValidateQualifiedName(target, _blockBase + ns, allowColon: false);
        bool isDeclaration = target.SequenceEqual("xml"u8);
        if (!isDeclaration && target.Length == 3 && (target[0] | 0x20) == 'x' && (target[1] | 0x20) == 'm' && (target[2] | 0x20) == 'l')
        {
            // XML 1.0 §2.6: target names that match "xml" in any case are reserved.
            throw new XmlSyntaxException(XmlErrorCode.InvalidName, _blockBase + ns, "The processing-instruction target \"xml\" is reserved.");
        }

        int ds;
        if (d[ne] == (byte)'?')
        {
            ds = ne;
        }
        else if (XmlScan.IsSpace(d[ne]))
        {
            ds = XmlScan.SkipSpace(d, ne);
        }
        else
        {
            throw new XmlSyntaxException(XmlErrorCode.Syntax, _blockBase + ne, "Expected white space after the processing-instruction target.");
        }

        int i = d[ds..].IndexOf("?>"u8);
        if (i < 0)
        {
            return Incomplete(d);
        }

        int de = ds + i;
        XmlScan.ValidateData(d[ds..de], _blockBase + ds, XmlScan.DataMode.Raw);
        if (isDeclaration)
        {
            // XML 1.0 §2.8: the declaration, if present, is the first thing in the document.
            if (_state.TokenCount != 0 || _state.IsFragment)
            {
                throw new XmlSyntaxException(XmlErrorCode.InvalidDeclaration, _blockBase + p, "The XML declaration must be the first thing in the document.");
            }

            ValidateDeclaration(d[ne..de], _blockBase + ne);
            return Emit(XmlTokenKind.XmlDeclaration, p, de + 2, 0, 0, -1, ne, de, 0, 0);
        }

        return Emit(XmlTokenKind.ProcessingInstruction, p, de + 2, ns, ne, -1, ds, de, 0, _state.Depth);
    }

    // XML 1.0 §2.8: VersionInfo EncodingDecl? SDDecl? S?
    private static void ValidateDeclaration(ReadOnlySpan<byte> s, long offset)
    {
        int i = 0;
        TryPseudoAttribute(s, ref i, offset, "version"u8, true, out ReadOnlySpan<byte> version);
        if (version.Length < 3 || !version.StartsWith("1."u8) || version[2..].IndexOfAnyExceptInRange((byte)'0', (byte)'9') >= 0)
        {
            throw new XmlSyntaxException(XmlErrorCode.InvalidDeclaration, offset, "The XML version must be 1.x.");
        }

        if (TryPseudoAttribute(s, ref i, offset, "encoding"u8, false, out ReadOnlySpan<byte> enc) && (enc.Length == 0 || !XmlScan.IsNameStart(enc[0]) || enc[0] == ':' || enc.IndexOfAnyExceptInRange((byte)'!', (byte)'~') >= 0))
        {
            throw new XmlSyntaxException(XmlErrorCode.InvalidDeclaration, offset, "The encoding name is malformed.");
        }

        if (TryPseudoAttribute(s, ref i, offset, "standalone"u8, false, out ReadOnlySpan<byte> sa) && !sa.SequenceEqual("yes"u8) && !sa.SequenceEqual("no"u8))
        {
            throw new XmlSyntaxException(XmlErrorCode.InvalidDeclaration, offset, "standalone must be yes or no.");
        }

        if (XmlScan.SkipSpace(s, i) != s.Length)
        {
            throw new XmlSyntaxException(XmlErrorCode.InvalidDeclaration, offset + i, "The XML declaration is malformed.");
        }
    }

    private static bool TryPseudoAttribute(ReadOnlySpan<byte> s, ref int i, long offset, ReadOnlySpan<byte> name, bool required, out ReadOnlySpan<byte> value)
    {
        value = default;
        int j = XmlScan.SkipSpace(s, i);
        if (j == i || !s[j..].StartsWith(name))
        {
            if (required)
            {
                throw new XmlSyntaxException(XmlErrorCode.InvalidDeclaration, offset + i, "The XML declaration is malformed.");
            }

            return false;
        }

        j = XmlScan.SkipSpace(s, j + name.Length);
        if (j >= s.Length || s[j] != '=')
        {
            throw new XmlSyntaxException(XmlErrorCode.InvalidDeclaration, offset + j, "The XML declaration is malformed.");
        }

        j = XmlScan.SkipSpace(s, j + 1);
        if (j >= s.Length || (s[j] != '"' && s[j] != '\''))
        {
            throw new XmlSyntaxException(XmlErrorCode.InvalidDeclaration, offset + j, "The XML declaration is malformed.");
        }

        int close = s[(j + 1)..].IndexOf(s[j]);
        if (close < 0)
        {
            throw new XmlSyntaxException(XmlErrorCode.InvalidDeclaration, offset + j, "The XML declaration is malformed.");
        }

        value = s.Slice(j + 1, close);
        i = j + 1 + close + 1;
        return true;
    }

    private void CheckNameLength(int length, int at)
    {
        if (length > _state.Limits.MaxNameLength)
        {
            throw new XmlSyntaxException(nameof(XmlLimits.MaxNameLength), _state.Limits.MaxNameLength, length, _blockBase + at);
        }
    }

    private bool ReadEndTag(ReadOnlySpan<byte> d, int p)
    {
        int ns = p + 2;
        int ne = XmlScan.ScanName(d, ns);
        if (ne >= d.Length)
        {
            return Incomplete(d);
        }

        if (ne == ns)
        {
            throw new XmlSyntaxException(XmlErrorCode.InvalidName, _blockBase + ns, "An end tag needs a name.");
        }

        CheckNameLength(ne - ns, ns);
        int q = XmlScan.SkipSpace(d, ne);
        if (q >= d.Length)
        {
            return Incomplete(d);
        }

        if (d[q] != (byte)'>')
        {
            throw new XmlSyntaxException(XmlErrorCode.Syntax, _blockBase + q, "Expected \">\" at the end of an end tag.");
        }

        ReadOnlySpan<byte> name = d[ns..ne];
        if (_state.Depth == 0 || !name.SequenceEqual(_state.OpenName))
        {
            throw new XmlSyntaxException(XmlErrorCode.MismatchedEndTag, _blockBase + p, "The end tag does not match the open element.");
        }

        int colon = name.IndexOf((byte)':');
        _state.SetPendingPop();
        return Emit(XmlTokenKind.EndElement, p, q + 1, ns, ne, colon >= 0 ? ns + colon : -1, ne, q, 0, _state.Depth - 1);
    }

    private bool ReadStartTag(ReadOnlySpan<byte> d, int p)
    {
        int ns = p + 1;
        int ne = XmlScan.ScanName(d, ns);
        if (ne >= d.Length)
        {
            return Incomplete(d);
        }

        if (ne == ns)
        {
            throw new XmlSyntaxException(XmlErrorCode.InvalidName, _blockBase + ns, "An element needs a name.");
        }

        CheckNameLength(ne - ns, ns);
        int colon = XmlScan.ValidateQualifiedName(d[ns..ne], _blockBase + ns, allowColon: true);
        int q = ne;
        int attributes = 0;
        int tagEnd;
        bool empty;
        int maxAttributes = _state.Limits.MaxAttributeCount;
        bool declaresNamespaces = false;
        int firstAttributePrefix = -1;
        int firstAttributePrefixLength = 0;
        while (true)
        {
            int before = q;
            q = XmlScan.SkipSpace(d, q);
            if (q >= d.Length)
            {
                return Incomplete(d);
            }

            byte c = d[q];
            if (c == (byte)'>')
            {
                tagEnd = q;
                empty = false;
                q++;
                break;
            }

            if (c == (byte)'/')
            {
                if (q + 1 >= d.Length)
                {
                    return Incomplete(d);
                }

                if (d[q + 1] != (byte)'>')
                {
                    throw new XmlSyntaxException(XmlErrorCode.Syntax, _blockBase + q, "Expected \">\" after \"/\".");
                }

                tagEnd = q;
                empty = true;
                q += 2;
                break;
            }

            if (q == before)
            {
                throw new XmlSyntaxException(XmlErrorCode.Syntax, _blockBase + q, "White space is required before an attribute.");
            }

            int an = XmlScan.ScanName(d, q);
            if (an >= d.Length)
            {
                return Incomplete(d);
            }

            if (an == q)
            {
                throw new XmlSyntaxException(XmlErrorCode.InvalidName, _blockBase + q, "An attribute needs a name.");
            }

            CheckNameLength(an - q, q);
            int attributeColon = XmlScan.ValidateQualifiedName(d[q..an], _blockBase + q, allowColon: true);
            if (attributeColon >= 0 ? (attributeColon == 5 && d[q..an].StartsWith("xmlns:"u8)) : (an - q == 5 && d[q..an].SequenceEqual("xmlns"u8)))
            {
                declaresNamespaces = true;
            }

            if (attributes == 0)
            {
                firstAttributePrefix = attributeColon >= 0 ? q : -1;
                firstAttributePrefixLength = attributeColon;
            }

            int r = XmlScan.SkipSpace(d, an);
            if (r >= d.Length)
            {
                return Incomplete(d);
            }

            if (d[r] != (byte)'=')
            {
                throw new XmlSyntaxException(XmlErrorCode.Syntax, _blockBase + r, "Expected \"=\" after an attribute name.");
            }

            r = XmlScan.SkipSpace(d, r + 1);
            if (r >= d.Length)
            {
                return Incomplete(d);
            }

            byte quote = d[r];
            if (quote != (byte)'"' && quote != (byte)'\'')
            {
                throw new XmlSyntaxException(XmlErrorCode.Syntax, _blockBase + r, "An attribute value must be quoted.");
            }

            int vs = r + 1;
            int vl = d[vs..].IndexOfAny(quote, (byte)'<');
            if (vl < 0)
            {
                return Incomplete(d);
            }

            if (d[vs + vl] == (byte)'<')
            {
                throw new XmlSyntaxException(XmlErrorCode.InvalidCharacter, _blockBase + vs + vl, "\"<\" is not allowed in an attribute value.");
            }

            XmlScan.ValidateData(d[vs..(vs + vl)], _blockBase + vs, XmlScan.DataMode.Attribute);
            attributes++;
            if (attributes > maxAttributes)
            {
                throw new XmlSyntaxException(nameof(XmlLimits.MaxAttributeCount), maxAttributes, attributes, _blockBase + q);
            }

            q = vs + vl + 1;
        }

        if (!_state.IsFragment && _state.Depth == 0 && _state.RootSeen)
        {
            throw new XmlSyntaxException(XmlErrorCode.MisplacedContent, _blockBase + p, "A document has one root element.");
        }

        if (_state.Depth >= _state.Limits.MaxDepth)
        {
            throw new XmlSyntaxException(nameof(XmlLimits.MaxDepth), _state.Limits.MaxDepth, _state.Depth + 1, _blockBase + p);
        }

        // The tag is complete and well-formed in itself; now bind namespaces. Nothing above changed the state, so a block that
        // ends inside the tag can be retried.
        int depth = _state.Depth;
        _state.Push(d[ns..ne]);
        _state.RootSeen = true;
        if (declaresNamespaces || attributes > 1)
        {
            BindNamespaces(d, ne, tagEnd, declaresNamespaces);
        }
        else if (attributes == 1 && firstAttributePrefix >= 0 && !_state.IsFragment
            && !_state.TryResolve(d.Slice(firstAttributePrefix, firstAttributePrefixLength), out _))
        {
            // One attribute and no declaration in this tag: only its prefix needs to resolve, and no pair can repeat.
            throw new XmlSyntaxException(XmlErrorCode.UnboundPrefix, _blockBase + firstAttributePrefix, "The attribute's prefix is not bound to a namespace.");
        }

        if (colon >= 0 && !_state.IsFragment)
        {
            ReadOnlySpan<byte> prefix = d[ns..(ns + colon)];
            if (!_state.TryResolve(prefix, out _))
            {
                throw new XmlSyntaxException(XmlErrorCode.UnboundPrefix, _blockBase + ns, "The element's prefix is not bound to a namespace.");
            }
        }

        if (empty)
        {
            _state.SetPendingPop();
        }

        return Emit(empty ? XmlTokenKind.EmptyElement : XmlTokenKind.StartElement, p, q, ns, ne, colon >= 0 ? ns + colon : -1, ne, tagEnd, attributes, depth);
    }

    // Namespaces in XML 1.0 §3, §6: declarations bind prefixes for the element and its content; the prefixes xml and xmlns are reserved.
    private readonly void BindNamespaces(ReadOnlySpan<byte> d, int from, int to, bool declaresNamespaces)
    {
        var declarations = new XmlAttributeEnumerator(d, from, to, null);
        while (declaresNamespaces && declarations.MoveNext())
        {
            XmlAttributeToken a = declarations.Current;
            if (!a.IsNamespaceDeclaration)
            {
                continue;
            }

            ReadOnlySpan<byte> prefix = a.DeclaredPrefix;
            ReadOnlySpan<byte> raw = a.RawValue;
            bool bad;
            if (prefix.Length == 0)
            {
                bad = IsReservedNamespace(raw);
            }
            else if (prefix.SequenceEqual("xmlns"u8))
            {
                bad = true;
            }
            else if (prefix.SequenceEqual("xml"u8))
            {
                bad = !SameUri(raw, XmlNamespaces.XmlUtf8);
            }
            else
            {
                bad = raw.Length == 0 || IsReservedNamespace(raw);
            }

            if (bad)
            {
                throw new XmlSyntaxException(XmlErrorCode.InvalidNamespaceDeclaration, _blockBase + a.Start, "The namespace declaration binds a reserved prefix or namespace, or is empty.");
            }

            if (!prefix.SequenceEqual("xml"u8))
            {
                _state.Declare(prefix, raw);
            }
        }

        // Attribute checks need every declaration of this tag, so they come second (an attribute may precede its declaration).
        var attrs = new XmlAttributeEnumerator(d, from, to, _state);
        while (attrs.MoveNext())
        {
            XmlAttributeToken a = attrs.Current;
            ReadOnlySpan<byte> prefix = a.Prefix;
            if (!a.IsNamespaceDeclaration && prefix.Length > 0 && !_state.IsFragment && !_state.TryResolve(prefix, out _))
            {
                throw new XmlSyntaxException(XmlErrorCode.UnboundPrefix, _blockBase + a.Start, "The attribute's prefix is not bound to a namespace.");
            }

            var later = new XmlAttributeEnumerator(d, a.End, to, _state);
            while (later.MoveNext())
            {
                XmlAttributeToken b = later.Current;
                bool duplicate = a.RawName.SequenceEqual(b.RawName);
                if (!duplicate && prefix.Length > 0 && b.Prefix.Length > 0 && !a.IsNamespaceDeclaration && !b.IsNamespaceDeclaration && a.LocalName.SequenceEqual(b.LocalName))
                {
                    // Namespaces in XML 1.0 §6.3: two attributes may not have the same expanded name.
                    ReadOnlySpan<byte> ua = a.NamespaceUri;
                    duplicate = ua.Length > 0 && ua.SequenceEqual(b.NamespaceUri);
                }

                if (duplicate)
                {
                    throw new XmlSyntaxException(XmlErrorCode.DuplicateAttribute, _blockBase + b.Start, "The element has the same attribute twice.");
                }
            }
        }
    }

    private static bool SameUri(ReadOnlySpan<byte> raw, ReadOnlySpan<byte> expected) =>
        raw.SequenceEqual(expected) || (XmlText.NeedsDecoding(raw, true) && XmlText.GetString(raw, true) == Encoding.UTF8.GetString(expected));

    private static bool IsReservedNamespace(ReadOnlySpan<byte> raw) => SameUri(raw, XmlNamespaces.XmlUtf8) || SameUri(raw, XmlNamespaces.XmlnsUtf8);
}
