using System.Text;
using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.Markup;
using RecursiveAI.OneOffice.Markup.Xml;

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
/// A small forward XML reader for the packaging vocabularies: <c>[Content_Types].xml</c>, relationship parts and Flat OPC. It is a
/// thin adapter over <see cref="MarkupReader"/>, which does the tokenizing, the well-formedness checks (a DTD is always refused, so
/// no entity can expand) and the depth, attribute-count and name-length limits. It keeps the packaging layer's view of the stream:
/// start, end and text tokens with byte offsets, an empty-element tag followed by a synthesised end, and attributes by local name.
/// </summary>
internal sealed class OpcXmlReader : IDisposable
{
    private readonly ReadOnlyMemory<byte> _data;
    private readonly MarkupReader _reader;
    private readonly List<KeyValuePair<string, string>> _attributes = [];
    private bool _pendingEnd;
    private bool _sawRoot;

    /// <summary>Creates a reader over UTF-8 (or UTF-16 with a byte-order mark) XML.</summary>
    /// <param name="data">The part's bytes.</param>
    /// <param name="limits">The limits to enforce.</param>
    /// <param name="partName">The part's name, for the location of a limit that is hit.</param>
    public OpcXmlReader(ReadOnlyMemory<byte> data, OfficeLimits limits, string partName)
    {
        _data = Normalize(data);
        _reader = new MarkupReader(_data, new OpenContext(limits), partName);
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
            return true;
        }

        try
        {
            while (_reader.Read())
            {
                switch (_reader.Kind)
                {
                    case XmlTokenKind.StartElement:
                    case XmlTokenKind.EmptyElement:
                        _sawRoot = true;
                        Token = OpcXmlToken.StartElement;
                        IsEmptyElement = _reader.Kind == XmlTokenKind.EmptyElement;
                        _pendingEnd = IsEmptyElement;
                        Depth = _reader.Depth + 1;
                        LocalName = Encoding.UTF8.GetString(_reader.LocalName);
                        SetSpan();
                        _attributes.Clear();
                        foreach (XmlAttributeToken a in _reader.Attributes)
                        {
                            if (!a.IsNamespaceDeclaration)
                            {
                                _attributes.Add(new(Encoding.UTF8.GetString(a.LocalName), XmlText.GetString(a.RawValue)));
                            }
                        }

                        return true;
                    case XmlTokenKind.EndElement:
                        Token = OpcXmlToken.EndElement;
                        Depth = _reader.Depth + 1;
                        LocalName = Encoding.UTF8.GetString(_reader.LocalName);
                        SetSpan();
                        return true;
                    case XmlTokenKind.Text:
                    case XmlTokenKind.Whitespace:
                    case XmlTokenKind.CData:
                        if (_reader.Depth == 0 && _reader.Kind == XmlTokenKind.Whitespace && !_sawRoot)
                        {
                            continue;
                        }

                        Token = OpcXmlToken.Text;
                        Text = _reader.GetText() ?? string.Empty;
                        SetSpan();
                        return true;
                    default:
                        // The XML declaration, comments and processing instructions carry nothing the package vocabularies use.
                        continue;
                }
            }
        }
        catch (OfficeFormatException ex)
        {
            throw new OpcXmlException(ex.Message, (int)Math.Max(0, ex.Location.Offset), ex.Defect.Code == "xml.dtd-refused");
        }

        if (!_sawRoot)
        {
            throw new OpcXmlException("There is no root element.", _data.Length);
        }

        Token = OpcXmlToken.Eof;
        return false;
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

        throw new OpcXmlException("The document ends inside an element.", _data.Length);
    }

    /// <summary>Releases the underlying reader.</summary>
    public void Dispose() => _reader.Dispose();

    private void SetSpan()
    {
        TokenStart = (int)_reader.TokenOffset;
        TokenEnd = TokenStart + _reader.Raw.Length;
    }

    // UTF-16 input is transcoded here so that offsets index the bytes handed to the reader; UTF-8 is used as it is.
    private static ReadOnlyMemory<byte> Normalize(ReadOnlyMemory<byte> data)
    {
        ReadOnlySpan<byte> s = data.Span;
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
}
