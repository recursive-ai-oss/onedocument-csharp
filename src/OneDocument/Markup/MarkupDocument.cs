using System.Buffers;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup.Xml;

namespace RecursiveAI.OneDocument.Markup;

/// <summary>
/// A lazy, lossless tree over the bytes of one XML part.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lazy.</b> Parsing makes one validating pass and records, for each node, where it lies in the part's buffer (a few integers).
/// Names, attributes, text and namespaces are read from the buffer when asked for, never copied into objects. The buffer is
/// held, not copied: do not change it while the document lives. (A UTF-16 part is transcoded once to UTF-8 and the copy is held.)
/// </para>
/// <para>
/// <b>Lossless.</b> Every byte of the part after the byte order mark belongs to exactly one node: tags, text, whitespace
/// between elements, comments, processing instructions, CDATA sections. <see cref="WriteTo(IBufferWriter{byte})"/> of an
/// unedited document writes the original bytes exactly, UTF-16 and byte order mark included. After edits, only the edited
/// regions are new bytes: an edited start tag keeps every other attribute, the quote style, the spacing and the order; everything
/// outside the edited elements is copied from the original untouched. Prefixes, namespace declarations,
/// <c>mc:AlternateContent</c>, <c>mc:Ignorable</c>, <c>extLst</c> and unknown elements need no special care: nothing is interpreted or dropped.
/// </para>
/// <para>
/// <b>Edits</b> are made through <see cref="MarkupNode"/> (set or remove an attribute, set text, insert, replace or remove nodes).
/// New bytes are kept in a side buffer and nodes point into it; the original buffer is never touched. Spans obtained before an edit
/// should not be used after it. A document is not safe for concurrent mutation; after loading, concurrent reads are safe.
/// </para>
/// <para>
/// <b>Not interpreted:</b> there are no repairs. The tokenizer's rules are strict (a part Office would call corrupt is refused
/// with a located <see cref="OfficeFormatException"/>), and a UTF-8 byte order mark, CR LF line ends and entity references are kept as written.
/// </para>
/// </remarks>
public sealed partial class MarkupDocument
{
    private const byte StartOverlay = 1;
    private const byte EndOverlay = 2;
    private const byte Dirty = 4;
    private const int NodeSize = 44;

    private readonly ReadOnlyMemory<byte> _original;
    private readonly ReadOnlyMemory<byte> _src;
    private readonly XmlEncodingKind _encoding;
    private readonly int _originalBomLength;
    private readonly int _srcBomLength;
    private readonly OfficeLimits _limits;
    private readonly string? _partUri;
    private Node[] _nodes = new Node[64];
    private int _count = 1;
    private byte[] _overlay = [];
    private int _overlayLength;
    private bool _modified;

    private struct Node
    {
        public MarkupNodeKind Kind;
        public byte Flags;
        public int Parent;
        public int FirstChild;
        public int LastChild;
        public int Next;
        public int Prev;

        // Non-elements: the node's bytes are [A, B). Elements: the start tag is [A, B) and the end tag [C, D), which is empty
        // (C == D) for an element written as <a/>. Which buffer a range is in is in Flags (original when the bit is clear).
        public int A;
        public int B;
        public int C;
        public int D;
    }

    private MarkupDocument(ReadOnlyMemory<byte> original, ReadOnlyMemory<byte> src, XmlEncodingKind encoding, int originalBom, int srcBom, OfficeLimits limits, string? partUri)
    {
        _original = original;
        _src = src;
        _encoding = encoding;
        _originalBomLength = originalBom;
        _srcBomLength = srcBom;
        _limits = limits;
        _partUri = partUri;
        _nodes[0].Kind = MarkupNodeKind.Document;
    }

    /// <summary>Parses a part.</summary>
    /// <param name="data">The part's bytes: UTF-8 (with or without byte order mark) or UTF-16. The document keeps a reference; do not modify the memory afterwards.</param>
    /// <param name="context">Limits and defect sink; null for the defaults.</param>
    /// <param name="partUri">The part's URI, for error locations; null when it has none.</param>
    /// <returns>The document.</returns>
    /// <exception cref="OfficeFormatException">The part is not well-formed XML, has a DTD, or is in an unsupported encoding (codes <c>xml.*</c>, see the defect catalogue), located by part URI and byte offset.</exception>
    /// <exception cref="OfficeLimitException">The part is larger than <see cref="OfficeLimits.MaxPartSize"/>, nests deeper than <see cref="OfficeLimits.MaxXmlDepth"/>, has more attributes on one element than <see cref="OfficeLimits.MaxXmlAttributeCount"/>, a name longer than <see cref="OfficeLimits.MaxXmlNameLength"/>, or needs more than <see cref="OfficeLimits.MaxAllocationBytes"/> for its node table.</exception>
    public static MarkupDocument Parse(ReadOnlyMemory<byte> data, OpenContext? context = null, string? partUri = null)
    {
        OfficeLimits limits = context?.Limits ?? OfficeLimits.Default;
        DefectLocation start = partUri is null ? DefectLocation.AtText(0) : DefectLocation.InPart(partUri, 0);
        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxPartSize), limits.MaxPartSize, data.Length, start);
        if (!XmlEncoding.TryDetect(data.Span, out XmlEncodingKind kind, out int bom))
        {
            throw MarkupErrors.Translate(new XmlSyntaxException(XmlErrorCode.UnsupportedEncoding, 0, "The part is UTF-32 or an encoding that XML parts do not use."), partUri);
        }

        ReadOnlyMemory<byte> src = data;
        int srcBom = bom;
        if (kind != XmlEncodingKind.Utf8)
        {
            try
            {
                src = XmlEncoding.Utf16ToUtf8(data.Span[bom..], kind == XmlEncodingKind.Utf16BigEndian);
            }
            catch (XmlSyntaxException ex)
            {
                throw MarkupErrors.Translate(ex, partUri, o => bom + (2 * o));
            }

            srcBom = 0;
        }

        var doc = new MarkupDocument(data, src, kind, bom, srcBom, limits, partUri);
        try
        {
            doc.BuildDocument();
        }
        catch (XmlSyntaxException ex)
        {
            Func<long, long>? map = kind == XmlEncodingKind.Utf8 ? null : o => MarkupErrors.Utf16Offset(src.Span, o, bom);
            throw MarkupErrors.Translate(ex, partUri, map);
        }

        return doc;
    }

    /// <summary>Parses a part, reporting failure with a return value.</summary>
    /// <param name="data">The part's bytes.</param>
    /// <param name="document">The document, or null when parsing failed.</param>
    /// <param name="context">Limits and defect sink; null for the defaults.</param>
    /// <param name="partUri">The part's URI, for error locations.</param>
    /// <returns>True when the part parsed. False for anything <see cref="Parse"/> would raise as an <see cref="OfficeException"/>.</returns>
    public static bool TryParse(ReadOnlyMemory<byte> data, out MarkupDocument? document, OpenContext? context = null, string? partUri = null)
    {
        try
        {
            document = Parse(data, context, partUri);
            return true;
        }
        catch (OfficeException)
        {
            document = null;
            return false;
        }
    }

    /// <summary>Gets the document node, the parent of the root element and of the comments and whitespace around it.</summary>
    public MarkupNode DocumentNode => new(this, 0);

    /// <summary>Gets the root element.</summary>
    public MarkupNode Root
    {
        get
        {
            for (int n = _nodes[0].FirstChild; n != 0; n = _nodes[n].Next)
            {
                if (_nodes[n].Kind == MarkupNodeKind.Element)
                {
                    return new MarkupNode(this, n);
                }
            }

            return default;
        }
    }

    /// <summary>Gets a value that says whether any edit has been made. An unmodified document writes its original bytes.</summary>
    public bool IsModified => _modified;

    /// <summary>Gets the bytes the document was parsed from.</summary>
    public ReadOnlyMemory<byte> OriginalBytes => _original;

    /// <summary>Gets the encoding of the original bytes. Written bytes use the same encoding.</summary>
    public XmlEncodingKind SourceEncoding => _encoding;

    /// <summary>Gets a value that says whether the original bytes start with a byte order mark, which is written back.</summary>
    public bool HasByteOrderMark => _originalBomLength > 0;

    /// <summary>Writes the document in its original encoding.</summary>
    /// <param name="destination">Where the bytes go.</param>
    /// <remarks>
    /// An unmodified document writes <see cref="OriginalBytes"/>. A modified one writes the original bytes outside the edited regions and the new bytes inside them.
    /// A modified UTF-16 document is written as UTF-16 with the byte order mark it had.
    /// </remarks>
    public void WriteTo(IBufferWriter<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!_modified)
        {
            WriteBytes(destination, _original.Span);
            return;
        }

        if (_encoding == XmlEncodingKind.Utf8)
        {
            Emit(0, destination);
            return;
        }

        var utf8 = new ArrayBufferWriter<byte>();
        Emit(0, utf8);
        try
        {
            XmlEncoding.Utf8ToUtf16(utf8.WrittenSpan, _encoding == XmlEncodingKind.Utf16BigEndian, _originalBomLength > 0, destination);
        }
        catch (XmlSyntaxException ex)
        {
            // Edited text is checked on the way in, so this means a bug rather than bad input.
            throw MarkupErrors.Translate(ex, _partUri);
        }
    }

    /// <summary>Writes the document to a stream.</summary>
    /// <param name="stream">The stream.</param>
    public void WriteTo(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var buffer = new ArrayBufferWriter<byte>();
        WriteTo(buffer);
        stream.Write(buffer.WrittenSpan);
    }

    /// <summary>Returns the document's bytes (see <see cref="WriteTo(IBufferWriter{byte})"/>).</summary>
    /// <returns>A new array.</returns>
    public byte[] ToArray()
    {
        var buffer = new ArrayBufferWriter<byte>(_original.Length + 64);
        WriteTo(buffer);
        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteBytes(IBufferWriter<byte> w, ReadOnlySpan<byte> bytes)
    {
        bytes.CopyTo(w.GetSpan(bytes.Length));
        w.Advance(bytes.Length);
    }

    // Reading ---------------------------------------------------------------------------------------------------------------

    internal ReadOnlySpan<byte> Bytes(byte flags, byte overlayBit, int a, int b) =>
        (flags & overlayBit) != 0 ? _overlay.AsSpan(a, b - a) : _src.Span[a..b];

    internal MarkupNodeKind KindOf(int n) => _nodes[n].Kind;

    internal int ParentOf(int n) => _nodes[n].Parent;

    internal int FirstChildOf(int n) => _nodes[n].FirstChild;

    internal int LastChildOf(int n) => _nodes[n].LastChild;

    internal int NextOf(int n) => _nodes[n].Next;

    internal int PrevOf(int n) => _nodes[n].Prev;

    internal ReadOnlySpan<byte> StartTag(int n) => Bytes(_nodes[n].Flags, StartOverlay, _nodes[n].A, _nodes[n].B);

    internal ReadOnlySpan<byte> NodeBytes(int n) => Bytes(_nodes[n].Flags, StartOverlay, _nodes[n].A, _nodes[n].B);

    internal bool IsSelfClosing(int n) => _nodes[n].C == _nodes[n].D;

    /// <summary>Offset of the node's first byte in the original buffer, or -1 when its bytes are new.</summary>
    internal int SourceStart(int n) => (_nodes[n].Flags & StartOverlay) != 0 ? -1 : _nodes[n].A;

    internal int SourceEnd(int n)
    {
        Node x = _nodes[n];
        if (x.Kind != MarkupNodeKind.Element)
        {
            return (x.Flags & StartOverlay) != 0 ? -1 : x.B;
        }

        return (x.Flags & EndOverlay) != 0 ? -1 : x.D;
    }

    /// <summary>The raw content of a text-like node: the text, the inside of a CDATA section or comment, the data of a processing instruction.</summary>
    internal ReadOnlySpan<byte> Content(int n)
    {
        ReadOnlySpan<byte> raw = NodeBytes(n);
        switch (_nodes[n].Kind)
        {
            case MarkupNodeKind.CData:
                return raw[9..^3];
            case MarkupNodeKind.Comment:
                return raw[4..^3];
            case MarkupNodeKind.ProcessingInstruction:
            case MarkupNodeKind.XmlDeclaration:
                {
                    int ne = XmlScanName(raw, 2);
                    int ds = XmlScanSpace(raw, ne);
                    return raw[ds..^2];
                }

            default:
                return raw;
        }
    }

    private static int XmlScanName(ReadOnlySpan<byte> d, int p)
    {
        while (p < d.Length && !IsSpaceOrMarkup(d[p]))
        {
            p++;
        }

        return p;
    }

    private static int XmlScanSpace(ReadOnlySpan<byte> d, int p)
    {
        while (p < d.Length && (d[p] is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r'))
        {
            p++;
        }

        return p;
    }

    private static bool IsSpaceOrMarkup(byte b) => b is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r' or (byte)'?' or (byte)'>' or (byte)'/';

    /// <summary>Splits an element's name. The tag is [A, B) and starts with "&lt;".</summary>
    internal void ElementName(int n, out ReadOnlySpan<byte> raw, out ReadOnlySpan<byte> prefix, out ReadOnlySpan<byte> local)
    {
        ReadOnlySpan<byte> tag = StartTag(n);
        int end = 1;
        while (end < tag.Length && !(tag[end] is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r' or (byte)'/' or (byte)'>'))
        {
            end++;
        }

        raw = tag[1..end];
        int colon = raw.IndexOf((byte)':');
        prefix = colon >= 0 ? raw[..colon] : default;
        local = colon >= 0 ? raw[(colon + 1)..] : raw;
    }

    internal XmlAttributeEnumerator AttributesOf(int n, out ReadOnlySpan<byte> tag)
    {
        tag = StartTag(n);
        int nameEnd = 1;
        while (nameEnd < tag.Length && !(tag[nameEnd] is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r' or (byte)'/' or (byte)'>'))
        {
            nameEnd++;
        }

        int inner = tag.Length >= 2 && tag[^2] == (byte)'/' ? tag.Length - 2 : tag.Length - 1;
        return new XmlAttributeEnumerator(tag, nameEnd, inner, null);
    }

    /// <summary>Finds the namespace a prefix is bound to at an element, looking at the element and its ancestors. Entity references in the value are not decoded.</summary>
    internal bool TryResolvePrefix(int element, ReadOnlySpan<byte> prefix, out ReadOnlySpan<byte> rawUri)
    {
        for (int n = element; n > 0; n = _nodes[n].Parent)
        {
            if (_nodes[n].Kind != MarkupNodeKind.Element)
            {
                continue;
            }

            XmlAttributeEnumerator en = AttributesOf(n, out _);
            while (en.MoveNext())
            {
                XmlAttributeToken a = en.Current;
                if (a.IsNamespaceDeclaration && a.DeclaredPrefix.SequenceEqual(prefix))
                {
                    rawUri = a.RawValue;
                    return true;
                }
            }
        }

        if (prefix.SequenceEqual("xml"u8))
        {
            rawUri = XmlNamespacesBytes;
            return true;
        }

        rawUri = default;
        return false;
    }

    private static ReadOnlySpan<byte> XmlNamespacesBytes => "http://www.w3.org/XML/1998/namespace"u8;

    internal static bool UriEquals(ReadOnlySpan<byte> rawUri, ReadOnlySpan<byte> uri) =>
        rawUri.SequenceEqual(uri) || (XmlText.NeedsDecoding(rawUri, true) && XmlText.GetString(rawUri, true) == Encoding.UTF8.GetString(uri));

    // Building --------------------------------------------------------------------------------------------------------------

    private int AddNode(MarkupNodeKind kind, int parent)
    {
        if (_count == _nodes.Length)
        {
            int newLength = _nodes.Length * 2;
            OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxAllocationBytes), _limits.MaxAllocationBytes, (long)newLength * NodeSize, PartLocation());
            Array.Resize(ref _nodes, newLength);
        }

        int n = _count++;
        _nodes[n] = new Node { Kind = kind, Parent = parent };
        if (parent >= 0)
        {
            int last = _nodes[parent].LastChild;
            _nodes[n].Prev = last;
            if (last != 0)
            {
                _nodes[last].Next = n;
            }
            else
            {
                _nodes[parent].FirstChild = n;
            }

            _nodes[parent].LastChild = n;
        }

        return n;
    }

    private DefectLocation PartLocation() => _partUri is null ? DefectLocation.None : DefectLocation.InPart(_partUri);

    private void BuildDocument()
    {
        var state = new XmlTokenizerState(MarkupErrors.ToXmlLimits(_limits));
        BuildNodes(_src.Span, 0, 0, 0, state);
    }

    /// <summary>Tokenizes <paramref name="data"/> and appends its nodes as children of <paramref name="holder"/>. Offsets are shifted by <paramref name="baseOffset"/>; <paramref name="flags"/> says which buffer the data is in.</summary>
    /// <returns>The deepest nesting seen below the holder (an element directly under the holder is depth 1).</returns>
    private int BuildNodes(ReadOnlySpan<byte> data, int baseOffset, byte flags, int holder, XmlTokenizerState state)
    {
        byte elementFlags = (byte)(flags != 0 ? StartOverlay | EndOverlay : 0);
        int cur = holder;
        int depth = 0;
        int maxDepth = 0;
        var tk = new XmlTokenizer(data, true, state);
        while (tk.Read())
        {
            XmlTokenInfo t = tk.Info;
            int n;
            switch (t.Kind)
            {
                case XmlTokenKind.StartElement:
                    n = AddNode(MarkupNodeKind.Element, cur);
                    _nodes[n].A = baseOffset + t.Start;
                    _nodes[n].B = baseOffset + t.End;
                    _nodes[n].Flags = elementFlags;
                    cur = n;
                    depth++;
                    maxDepth = Math.Max(maxDepth, depth);
                    break;
                case XmlTokenKind.EmptyElement:
                    n = AddNode(MarkupNodeKind.Element, cur);
                    _nodes[n].A = baseOffset + t.Start;
                    _nodes[n].B = _nodes[n].C = _nodes[n].D = baseOffset + t.End;
                    _nodes[n].Flags = elementFlags;
                    maxDepth = Math.Max(maxDepth, depth + 1);
                    break;
                case XmlTokenKind.EndElement:
                    _nodes[cur].C = baseOffset + t.Start;
                    _nodes[cur].D = baseOffset + t.End;
                    cur = _nodes[cur].Parent;
                    depth--;
                    break;
                default:
                    n = AddNode(NodeKindOf(t.Kind), cur);
                    _nodes[n].A = baseOffset + t.Start;
                    _nodes[n].B = baseOffset + t.End;
                    _nodes[n].Flags = (byte)(flags & StartOverlay);
                    break;
            }
        }

        return maxDepth;
    }

    private static MarkupNodeKind NodeKindOf(XmlTokenKind kind) => kind switch
    {
        XmlTokenKind.XmlDeclaration => MarkupNodeKind.XmlDeclaration,
        XmlTokenKind.Text => MarkupNodeKind.Text,
        XmlTokenKind.Whitespace => MarkupNodeKind.Whitespace,
        XmlTokenKind.CData => MarkupNodeKind.CData,
        XmlTokenKind.Comment => MarkupNodeKind.Comment,
        _ => MarkupNodeKind.ProcessingInstruction,
    };

    // Writing ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Writes <paramref name="top"/> (or, for the document node, everything) in UTF-8. Consecutive pieces that are adjacent in
    /// the same buffer are copied with one call, so an unedited stretch is one block copy however many nodes it holds.
    /// The walk is iterative, so depth is limited by the limits object and not by the stack.
    /// </summary>
    internal void Emit(int top, IBufferWriter<byte> w)
    {
        var run = new Run(this, w);
        int cur;
        if (top == 0)
        {
            run.Add(0, 0, _srcBomLength);
            cur = _nodes[0].FirstChild;
        }
        else
        {
            cur = top;
        }

        while (cur != 0)
        {
            Node n = _nodes[cur];
            if (n.Kind == MarkupNodeKind.Element)
            {
                bool clean = (n.Flags & Dirty) == 0 && ((n.Flags & StartOverlay) != 0) == ((n.Flags & EndOverlay) != 0);
                if (clean)
                {
                    run.Add((byte)(n.Flags & StartOverlay), n.A, n.D);
                }
                else
                {
                    run.Add((byte)(n.Flags & StartOverlay), n.A, n.B);
                    if (n.FirstChild != 0)
                    {
                        cur = n.FirstChild;
                        continue;
                    }

                    run.Add((byte)((n.Flags & EndOverlay) != 0 ? StartOverlay : 0), n.C, n.D);
                }
            }
            else
            {
                run.Add((byte)(n.Flags & StartOverlay), n.A, n.B);
            }

            while (true)
            {
                if (cur == top)
                {
                    cur = 0;
                    break;
                }

                if (_nodes[cur].Next != 0)
                {
                    cur = _nodes[cur].Next;
                    break;
                }

                cur = _nodes[cur].Parent;
                if (cur == 0)
                {
                    break;
                }

                Node p = _nodes[cur];
                run.Add((byte)((p.Flags & EndOverlay) != 0 ? StartOverlay : 0), p.C, p.D);
            }
        }

        run.Flush();
    }

    private struct Run
    {
        private readonly MarkupDocument _doc;
        private readonly IBufferWriter<byte> _w;
        private byte _overlayBit;
        private int _start;
        private int _end;

        public Run(MarkupDocument doc, IBufferWriter<byte> w)
        {
            _doc = doc;
            _w = w;
            _overlayBit = 0;
            _start = _end = 0;
        }

        public void Add(byte overlayBit, int a, int b)
        {
            if (b <= a)
            {
                return;
            }

            if (_end > _start && _overlayBit == overlayBit && _end == a)
            {
                _end = b;
                return;
            }

            Flush();
            _overlayBit = overlayBit;
            _start = a;
            _end = b;
        }

        public void Flush()
        {
            if (_end > _start)
            {
                WriteBytes(_w, _doc.Bytes(_overlayBit, StartOverlay, _start, _end));
            }

            _start = _end = 0;
        }
    }

    // Text ------------------------------------------------------------------------------------------------------------------

    internal string InnerText(int n)
    {
        MarkupNodeKind k = _nodes[n].Kind;
        if (k is MarkupNodeKind.Text or MarkupNodeKind.Whitespace)
        {
            return XmlText.GetString(NodeBytes(n));
        }

        if (k == MarkupNodeKind.CData)
        {
            return Encoding.UTF8.GetString(Content(n));
        }

        if (k is MarkupNodeKind.Comment or MarkupNodeKind.ProcessingInstruction or MarkupNodeKind.XmlDeclaration)
        {
            return Encoding.UTF8.GetString(Content(n));
        }

        var sb = new StringBuilder();
        int cur = _nodes[n].FirstChild;
        while (cur != 0)
        {
            Node x = _nodes[cur];
            switch (x.Kind)
            {
                case MarkupNodeKind.Text:
                case MarkupNodeKind.Whitespace:
                    sb.Append(XmlText.GetString(NodeBytes(cur)));
                    break;
                case MarkupNodeKind.CData:
                    sb.Append(Encoding.UTF8.GetString(Content(cur)));
                    break;
            }

            if (x.Kind == MarkupNodeKind.Element && x.FirstChild != 0)
            {
                cur = x.FirstChild;
                continue;
            }

            while (cur != n && cur != 0 && _nodes[cur].Next == 0)
            {
                cur = _nodes[cur].Parent;
            }

            cur = cur == n || cur == 0 ? 0 : _nodes[cur].Next;
        }

        return sb.ToString();
    }
}
