using System.Buffers;
using System.Text;

namespace RecursiveAI.OneDocument.Markup.Xml;

/// <summary>
/// Writes well-formed UTF-8 XML to an <see cref="IBufferWriter{T}"/>. Names, prefixes and attribute order are written exactly as
/// given, and raw bytes can be written verbatim, so output can reproduce a document that was read with <see cref="XmlTokenizer"/>.
/// </summary>
/// <remarks>
/// <para>
/// The writer does not indent, does not invent prefixes and never writes anything the caller did not ask for. It keeps a stack of
/// open element names, so it allocates when elements nest deeper than before; it allocates nothing per call otherwise.
/// A start tag stays open until content follows, so an element without content is written as <c>&lt;a/&gt;</c>.
/// </para>
/// <para>
/// Misuse (an end element with none open, a name that is not a valid XML name, a duplicate attribute, text that XML cannot carry)
/// raises <see cref="ArgumentException"/> or <see cref="InvalidOperationException"/>; the writer never produces ill-formed output
/// and never reads input, so it raises no <see cref="XmlSyntaxException"/>.
/// </para>
/// </remarks>
public sealed class XmlWriter
{
    private readonly IBufferWriter<byte> _out;
    private byte[] _names = new byte[128];
    private int _nameBytes;
    private int[] _nameEnds = new int[16];
    private int _depth;
    private bool _tagOpen;
    private bool _rootWritten;
    private byte[] _attrNames = new byte[128];
    private int _attrNameBytes;
    private int[] _attrNameEnds = new int[8];
    private int _attrCount;

    /// <summary>Creates a writer.</summary>
    /// <param name="output">Where the bytes go. Each call advances it; there is nothing to flush.</param>
    public XmlWriter(IBufferWriter<byte> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        _out = output;
    }

    /// <summary>Gets the number of elements open now.</summary>
    public int Depth => _depth;

    /// <summary>Writes <c>&lt;?xml version="1.0" encoding="UTF-8" standalone="yes"?&gt;</c> and a CR LF, as Office writes its parts.</summary>
    /// <param name="standalone">The standalone value to write, or null to leave it out.</param>
    /// <exception cref="InvalidOperationException">Something has been written already.</exception>
    public void WriteXmlDeclaration(bool? standalone = true)
    {
        if (_rootWritten || _depth > 0)
        {
            throw new InvalidOperationException("The XML declaration must come first.");
        }

        WriteRawCore("<?xml version=\"1.0\" encoding=\"UTF-8\""u8);
        if (standalone is bool s)
        {
            WriteRawCore(s ? " standalone=\"yes\""u8 : " standalone=\"no\""u8);
        }

        WriteRawCore("?>\r\n"u8);
    }

    /// <summary>Starts an element with a name as written, prefix included, such as <c>w:p</c>.</summary>
    /// <param name="name">The qualified name.</param>
    /// <exception cref="ArgumentException">The name is not a valid XML name.</exception>
    public void WriteStartElement(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        WriteStartElement(Encoding.UTF8.GetBytes(name));
    }

    /// <summary>Starts an element with a name as written, in UTF-8.</summary>
    /// <param name="name">The qualified name.</param>
    /// <exception cref="ArgumentException">The name is not a valid XML name.</exception>
    public void WriteStartElement(ReadOnlySpan<byte> name)
    {
        CheckName(name);
        CloseStartTag();
        if (_depth == 0 && _rootWritten)
        {
            throw new InvalidOperationException("A document has one root element.");
        }

        _rootWritten = true;
        Put((byte)'<');
        WriteRawCore(name);
        if (_depth == _nameEnds.Length)
        {
            Array.Resize(ref _nameEnds, _depth * 2);
        }

        if (_nameBytes + name.Length > _names.Length)
        {
            Array.Resize(ref _names, Math.Max(_names.Length * 2, _nameBytes + name.Length));
        }

        name.CopyTo(_names.AsSpan(_nameBytes));
        _nameBytes += name.Length;
        _nameEnds[_depth++] = _nameBytes;
        _tagOpen = true;
        _attrCount = 0;
        _attrNameBytes = 0;
    }

    /// <summary>Writes an attribute on the open start tag, in the order called, quoted with <c>"</c>.</summary>
    /// <param name="name">The qualified name as written.</param>
    /// <param name="value">The value; it is escaped.</param>
    /// <exception cref="ArgumentException">The name is invalid or already used on this element, or the value has a character XML cannot carry.</exception>
    /// <exception cref="InvalidOperationException">No start tag is open.</exception>
    public void WriteAttribute(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        byte[] n = Encoding.UTF8.GetBytes(name);
        BeginAttribute(n);
        Put((byte)'"');
        XmlText.Escape(_out, value, isAttributeValue: true);
        Put((byte)'"');
    }

    /// <summary>Writes an attribute whose value is already escaped, verbatim, with the quote the source used.</summary>
    /// <param name="name">The qualified name as written.</param>
    /// <param name="rawValue">The value bytes exactly as they stand between the quotes. The caller guarantees they are well-formed for <paramref name="quote"/>.</param>
    /// <param name="quote">The quote character: <c>"</c> or <c>'</c>.</param>
    /// <exception cref="ArgumentException">The name is invalid or already used on this element, or the quote is not a quote.</exception>
    /// <exception cref="InvalidOperationException">No start tag is open.</exception>
    public void WriteAttributeRaw(ReadOnlySpan<byte> name, ReadOnlySpan<byte> rawValue, byte quote = (byte)'"')
    {
        if (quote != (byte)'"' && quote != (byte)'\'')
        {
            throw new ArgumentException("The quote must be \" or '.", nameof(quote));
        }

        BeginAttribute(name);
        Put(quote);
        WriteRawCore(rawValue);
        Put(quote);
    }

    /// <summary>Writes a namespace declaration, <c>xmlns</c> or <c>xmlns:prefix</c>, on the open start tag.</summary>
    /// <param name="prefix">The prefix, or null or empty for the default namespace.</param>
    /// <param name="namespaceUri">The namespace URI.</param>
    /// <exception cref="ArgumentException">The prefix is invalid or already declared on this element.</exception>
    /// <exception cref="InvalidOperationException">No start tag is open.</exception>
    public void WriteNamespaceDeclaration(string? prefix, string namespaceUri) =>
        WriteAttribute(string.IsNullOrEmpty(prefix) ? "xmlns" : "xmlns:" + prefix, namespaceUri);

    /// <summary>Ends the current element: <c>/&gt;</c> if it has no content, otherwise an end tag with the name it was started with.</summary>
    /// <exception cref="InvalidOperationException">No element is open.</exception>
    public void WriteEndElement()
    {
        if (_depth == 0)
        {
            throw new InvalidOperationException("No element is open.");
        }

        int end = _nameEnds[_depth - 1];
        int start = _depth > 1 ? _nameEnds[_depth - 2] : 0;
        if (_tagOpen)
        {
            WriteRawCore("/>"u8);
            _tagOpen = false;
        }
        else
        {
            WriteRawCore("</"u8);
            WriteRawCore(_names.AsSpan(start, end - start));
            Put((byte)'>');
        }

        _depth--;
        _nameBytes = start;
    }

    /// <summary>Writes text content, escaped.</summary>
    /// <param name="text">The text.</param>
    /// <exception cref="ArgumentException">The text has a character XML cannot carry.</exception>
    /// <exception cref="InvalidOperationException">No element is open.</exception>
    public void WriteText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        WriteText(text.AsSpan());
    }

    /// <summary>Writes text content, escaped.</summary>
    /// <param name="text">The text.</param>
    /// <exception cref="ArgumentException">The text has a character XML cannot carry.</exception>
    /// <exception cref="InvalidOperationException">No element is open.</exception>
    public void WriteText(ReadOnlySpan<char> text)
    {
        RequireContentPosition();
        CloseStartTag();
        XmlText.Escape(_out, text, isAttributeValue: false);
    }

    /// <summary>Writes a CDATA section. A <c>]]&gt;</c> in the text is split across two sections.</summary>
    /// <param name="text">The text, not escaped.</param>
    /// <exception cref="ArgumentException">The text has a character XML cannot carry.</exception>
    /// <exception cref="InvalidOperationException">No element is open.</exception>
    public void WriteCData(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        RequireContentPosition();
        CloseStartTag();
        ReadOnlySpan<char> rest = text;
        WriteRawCore("<![CDATA["u8);
        while (true)
        {
            int i = rest.IndexOf("]]>", StringComparison.Ordinal);
            ReadOnlySpan<char> part = i < 0 ? rest : rest[..(i + 2)];
            CheckCharacters(part);
            WriteChars(part);
            if (i < 0)
            {
                break;
            }

            WriteRawCore("]]><![CDATA["u8);
            rest = rest[(i + 2)..];
        }

        WriteRawCore("]]>"u8);
    }

    /// <summary>Writes a comment.</summary>
    /// <param name="text">The comment text; it must not contain <c>--</c> or end with <c>-</c>.</param>
    /// <exception cref="ArgumentException">The text is not allowed in a comment.</exception>
    public void WriteComment(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Contains("--", StringComparison.Ordinal) || text.EndsWith('-'))
        {
            throw new ArgumentException("A comment cannot contain \"--\" or end with \"-\".", nameof(text));
        }

        CheckCharacters(text);
        CloseStartTag();
        WriteRawCore("<!--"u8);
        WriteChars(text);
        WriteRawCore("-->"u8);
    }

    /// <summary>Writes a processing instruction.</summary>
    /// <param name="target">The target name (not <c>xml</c>).</param>
    /// <param name="data">The data; it must not contain <c>?&gt;</c>. May be empty.</param>
    /// <exception cref="ArgumentException">The target or data is not allowed.</exception>
    public void WriteProcessingInstruction(string target, string data)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(data);
        byte[] t = Encoding.UTF8.GetBytes(target);
        XmlScan.ValidateQualifiedName(t, -1, allowColon: false);
        if (target.Equals("xml", StringComparison.OrdinalIgnoreCase) || data.Contains("?>", StringComparison.Ordinal))
        {
            throw new ArgumentException("The processing instruction is not allowed.", nameof(target));
        }

        CheckCharacters(data);
        CloseStartTag();
        WriteRawCore("<?"u8);
        WriteRawCore(t);
        if (data.Length > 0)
        {
            Put((byte)' ');
            WriteChars(data);
        }

        WriteRawCore("?>"u8);
    }

    /// <summary>
    /// Writes bytes exactly as given: a copied token, a whole subtree, the whitespace between tokens. The caller guarantees the
    /// bytes are well-formed in this position. A pending start tag is closed first.
    /// </summary>
    /// <param name="utf8">The bytes.</param>
    public void WriteRaw(ReadOnlySpan<byte> utf8)
    {
        CloseStartTag();
        WriteRawCore(utf8);
    }

    /// <summary>Checks that every element has been ended.</summary>
    /// <exception cref="InvalidOperationException">Elements are still open or there was no root element.</exception>
    public void Complete()
    {
        if (_depth != 0 || !_rootWritten)
        {
            throw new InvalidOperationException("The document is not complete.");
        }
    }

    private void RequireContentPosition()
    {
        if (_depth == 0)
        {
            throw new InvalidOperationException("Text is not allowed outside the root element.");
        }
    }

    private static void CheckName(ReadOnlySpan<byte> name)
    {
        try
        {
            XmlScan.ValidateQualifiedName(name, -1, allowColon: true);
        }
        catch (XmlSyntaxException ex)
        {
            throw new ArgumentException("The name is not a valid XML name.", nameof(name), ex);
        }

        if (XmlScan.ScanName(name, 0) != name.Length)
        {
            throw new ArgumentException("The name is not a valid XML name.", nameof(name));
        }
    }

    private static void CheckCharacters(ReadOnlySpan<char> s)
    {
        foreach (char c in s)
        {
            if ((c < 0x20 && c is not ('\t' or '\n' or '\r')) || c is '\uFFFE' or '\uFFFF')
            {
                throw new ArgumentException($"U+{(int)c:X4} cannot be written in XML.", nameof(s));
            }
        }
    }

    private void BeginAttribute(ReadOnlySpan<byte> name)
    {
        if (!_tagOpen)
        {
            throw new InvalidOperationException("Attributes can only be written right after a start tag.");
        }

        CheckName(name);
        int at = 0;
        for (int i = 0; i < _attrCount; i++)
        {
            int e = _attrNameEnds[i];
            if (_attrNames.AsSpan(at, e - at).SequenceEqual(name))
            {
                throw new ArgumentException("The element already has this attribute.", nameof(name));
            }

            at = e;
        }

        if (_attrCount == _attrNameEnds.Length)
        {
            Array.Resize(ref _attrNameEnds, _attrCount * 2);
        }

        if (_attrNameBytes + name.Length > _attrNames.Length)
        {
            Array.Resize(ref _attrNames, Math.Max(_attrNames.Length * 2, _attrNameBytes + name.Length));
        }

        name.CopyTo(_attrNames.AsSpan(_attrNameBytes));
        _attrNameBytes += name.Length;
        _attrNameEnds[_attrCount++] = _attrNameBytes;
        Put((byte)' ');
        WriteRawCore(name);
        Put((byte)'=');
    }

    private void CloseStartTag()
    {
        if (_tagOpen)
        {
            Put((byte)'>');
            _tagOpen = false;
        }
    }

    private void Put(byte b)
    {
        _out.GetSpan(1)[0] = b;
        _out.Advance(1);
    }

    private void WriteRawCore(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
        {
            return;
        }

        bytes.CopyTo(_out.GetSpan(bytes.Length));
        _out.Advance(bytes.Length);
    }

    private void WriteChars(ReadOnlySpan<char> chars)
    {
        if (chars.Length == 0)
        {
            return;
        }

        Span<byte> span = _out.GetSpan(Encoding.UTF8.GetMaxByteCount(chars.Length));
        var enc = new UTF8Encoding(false, true);
        try
        {
            int n = enc.GetBytes(chars, span);
            _out.Advance(n);
        }
        catch (EncoderFallbackException ex)
        {
            throw new ArgumentException("The text has an unpaired surrogate.", nameof(chars), ex);
        }
    }
}
