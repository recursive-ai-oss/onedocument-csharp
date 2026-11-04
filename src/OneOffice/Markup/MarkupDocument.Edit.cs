using System.Buffers;
using System.Text;
using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.Markup.Xml;

namespace RecursiveAI.OneOffice.Markup;

public sealed partial class MarkupDocument
{
    private int AppendOverlay(ReadOnlySpan<byte> bytes)
    {
        long need = (long)_overlayLength + bytes.Length;
        if (need > _overlay.Length)
        {
            long capacity = Math.Max(need, Math.Max(256, _overlay.Length * 2L));
            OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxAllocationBytes), _limits.MaxAllocationBytes, capacity, PartLocation());
            if (capacity > Array.MaxLength)
            {
                capacity = Array.MaxLength;
            }

            Array.Resize(ref _overlay, (int)capacity);
        }

        bytes.CopyTo(_overlay.AsSpan(_overlayLength));
        int offset = _overlayLength;
        _overlayLength += bytes.Length;
        return offset;
    }

    private void MarkDirty(int n)
    {
        _modified = true;
        while (n > 0 && (_nodes[n].Flags & Dirty) == 0)
        {
            _nodes[n].Flags |= Dirty;
            n = _nodes[n].Parent;
        }
    }

    private void RequireElement(int n)
    {
        if (_nodes[n].Kind != MarkupNodeKind.Element)
        {
            throw new InvalidOperationException("The operation needs an element.");
        }
    }

    private void TagLayout(int n, out ReadOnlySpan<byte> tag, out int nameEnd, out int inner)
    {
        tag = StartTag(n);
        nameEnd = 1;
        while (nameEnd < tag.Length && !(tag[nameEnd] is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r' or (byte)'/' or (byte)'>'))
        {
            nameEnd++;
        }

        inner = tag.Length >= 2 && tag[^2] == (byte)'/' ? tag.Length - 2 : tag.Length - 1;
    }

    private void CommitStartTag(int n, ReadOnlySpan<byte> tag)
    {
        int offset = AppendOverlay(tag);
        _nodes[n].A = offset;
        _nodes[n].B = offset + tag.Length;
        _nodes[n].Flags |= StartOverlay;
        MarkDirty(n);
    }

    private void ValidateAttributeName(int element, ReadOnlySpan<byte> name)
    {
        int colon;
        try
        {
            colon = XmlScan.ValidateQualifiedName(name, -1, allowColon: true);
        }
        catch (XmlSyntaxException ex)
        {
            throw new ArgumentException("The attribute name is not a valid XML name.", nameof(name), ex);
        }

        if (XmlScan.ScanName(name, 0) != name.Length)
        {
            throw new ArgumentException("The attribute name is not a valid XML name.", nameof(name));
        }

        if (colon > 0)
        {
            ReadOnlySpan<byte> prefix = name[..colon];
            if (!prefix.SequenceEqual("xmlns"u8) && !TryResolvePrefix(element, prefix, out _))
            {
                throw new ArgumentException("The attribute's prefix is not bound to a namespace at this element.", nameof(name));
            }
        }
    }

    internal void SetAttribute(int element, string rawName, string value)
    {
        RequireElement(element);
        byte[] name = Encoding.UTF8.GetBytes(rawName);
        ValidateAttributeName(element, name);
        TagLayout(element, out ReadOnlySpan<byte> tag, out int nameEnd, out int inner);
        var w = new ArrayBufferWriter<byte>(tag.Length + name.Length + value.Length + 8);
        int lastEnd = nameEnd;
        bool found = false;
        var en = new XmlAttributeEnumerator(tag, nameEnd, inner, null);
        while (en.MoveNext())
        {
            XmlAttributeToken a = en.Current;
            lastEnd = a.End;
            if (a.RawName.SequenceEqual(name))
            {
                WriteBytes(w, tag[..a.ValueStart]);
                XmlText.Escape(w, value, true, (char)a.Quote);
                WriteBytes(w, tag[a.ValueEnd..]);
                found = true;
                break;
            }
        }

        if (!found)
        {
            WriteBytes(w, tag[..lastEnd]);
            WriteBytes(w, " "u8);
            WriteBytes(w, name);
            WriteBytes(w, "=\""u8);
            XmlText.Escape(w, value, true);
            WriteBytes(w, "\""u8);
            WriteBytes(w, tag[lastEnd..]);
        }

        CommitStartTag(element, w.WrittenSpan);
    }

    internal void SetAttribute(int element, string namespaceUri, string localName, string value)
    {
        RequireElement(element);
        string prefix = FindOrDeclarePrefix(element, namespaceUri);
        SetAttribute(element, prefix + ":" + localName, value);
    }

    private string FindOrDeclarePrefix(int element, string namespaceUri)
    {
        if (namespaceUri == XmlNamespaces.Xml)
        {
            return "xml";
        }

        byte[] uri = Encoding.UTF8.GetBytes(namespaceUri);
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
                if (a.IsNamespaceDeclaration && a.DeclaredPrefix.Length > 0 && UriEquals(a.RawValue, uri))
                {
                    // The nearest declaration of this prefix at the element must still be this namespace (it may be rebound closer).
                    if (TryResolvePrefix(element, a.DeclaredPrefix, out ReadOnlySpan<byte> bound) && UriEquals(bound, uri))
                    {
                        return Encoding.UTF8.GetString(a.DeclaredPrefix);
                    }
                }
            }
        }

        for (int k = 1; ; k++)
        {
            string candidate = "ns" + k.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!TryResolvePrefix(element, Encoding.UTF8.GetBytes(candidate), out _))
            {
                SetAttribute(element, "xmlns:" + candidate, namespaceUri);
                return candidate;
            }
        }
    }

    internal bool RemoveAttribute(int element, string rawName)
    {
        RequireElement(element);
        byte[] name = Encoding.UTF8.GetBytes(rawName);
        TagLayout(element, out ReadOnlySpan<byte> tag, out int nameEnd, out int inner);
        int previousEnd = nameEnd;
        var en = new XmlAttributeEnumerator(tag, nameEnd, inner, null);
        while (en.MoveNext())
        {
            XmlAttributeToken a = en.Current;
            if (a.RawName.SequenceEqual(name))
            {
                int from = a.Start;
                while (from > previousEnd && XmlScan.IsSpace(tag[from - 1]))
                {
                    from--;
                }

                var w = new ArrayBufferWriter<byte>(tag.Length);
                WriteBytes(w, tag[..from]);
                WriteBytes(w, tag[a.End..]);
                CommitStartTag(element, w.WrittenSpan);
                return true;
            }

            previousEnd = a.End;
        }

        return false;
    }

    /// <summary>Turns <c>&lt;a/&gt;</c> into <c>&lt;a&gt;&lt;/a&gt;</c> so the element can hold content. Other bytes of the tag are kept.</summary>
    private void EnsureEndTag(int element)
    {
        if (!IsSelfClosing(element))
        {
            return;
        }

        TagLayout(element, out ReadOnlySpan<byte> tag, out int nameEnd, out _);
        int cut = tag.Length - 2;
        while (cut > nameEnd && XmlScan.IsSpace(tag[cut - 1]))
        {
            cut--;
        }

        var start = new ArrayBufferWriter<byte>(cut + 1);
        WriteBytes(start, tag[..cut]);
        WriteBytes(start, ">"u8);
        var end = new ArrayBufferWriter<byte>(nameEnd + 2);
        WriteBytes(end, "</"u8);
        WriteBytes(end, tag[1..nameEnd]);
        WriteBytes(end, ">"u8);
        int so = AppendOverlay(start.WrittenSpan);
        int eo = AppendOverlay(end.WrittenSpan);
        _nodes[element].A = so;
        _nodes[element].B = so + start.WrittenCount;
        _nodes[element].C = eo;
        _nodes[element].D = eo + end.WrittenCount;
        _nodes[element].Flags |= StartOverlay | EndOverlay;
        MarkDirty(element);
    }

    private int ElementDepth(int n)
    {
        int d = 0;
        for (; n > 0; n = _nodes[n].Parent)
        {
            if (_nodes[n].Kind == MarkupNodeKind.Element)
            {
                d++;
            }
        }

        return d;
    }

    /// <summary>Parses <paramref name="xml"/> as content and links its nodes under <paramref name="parent"/>, before <paramref name="before"/> (0 appends).</summary>
    internal void InsertXml(int parent, int before, ReadOnlySpan<byte> xml)
    {
        if (_nodes[parent].Kind is not (MarkupNodeKind.Element or MarkupNodeKind.Document))
        {
            throw new InvalidOperationException("Content can only be inserted into an element or the document node.");
        }

        int nodeMark = _count;
        int overlayMark = _overlayLength;
        bool ok = false;
        try
        {
            int baseOffset = AppendOverlay(xml);
            int holder = AddNode(MarkupNodeKind.Document, -1);
            _nodes[holder].Parent = parent;
            int depth;
            try
            {
                var state = new XmlTokenizerState(MarkupErrors.ToXmlLimits(_limits), fragment: true);
                depth = BuildNodes(_overlay.AsSpan(baseOffset, xml.Length), baseOffset, StartOverlay, holder, state);
            }
            catch (XmlSyntaxException ex)
            {
                throw MarkupErrors.Translate(ex, null);
            }

            OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxXmlDepth), _limits.MaxXmlDepth, ElementDepth(parent) + depth, PartLocation());
            CheckInsertedFragment(holder, parent);
            int first = _nodes[holder].FirstChild;
            if (first == 0)
            {
                ok = true;
                return;
            }

            if (_nodes[parent].Kind == MarkupNodeKind.Element)
            {
                EnsureEndTag(parent);
            }

            int last = _nodes[holder].LastChild;
            for (int c = first; c != 0; c = _nodes[c].Next)
            {
                _nodes[c].Parent = parent;
            }

            int prev = before != 0 ? _nodes[before].Prev : _nodes[parent].LastChild;
            _nodes[first].Prev = prev;
            if (prev != 0)
            {
                _nodes[prev].Next = first;
            }
            else
            {
                _nodes[parent].FirstChild = first;
            }

            _nodes[last].Next = before;
            if (before != 0)
            {
                _nodes[before].Prev = last;
            }
            else
            {
                _nodes[parent].LastChild = last;
            }

            MarkDirty(parent);
            ok = true;
        }
        finally
        {
            if (!ok)
            {
                _count = nodeMark;
                _overlayLength = overlayMark;
            }
        }
    }

    private void CheckInsertedFragment(int holder, int parent)
    {
        bool atDocument = _nodes[parent].Kind == MarkupNodeKind.Document;
        for (int c = _nodes[holder].FirstChild; c != 0 && atDocument; c = _nodes[c].Next)
        {
            if (_nodes[c].Kind is MarkupNodeKind.Element or MarkupNodeKind.Text or MarkupNodeKind.CData)
            {
                throw new ArgumentException("Only comments, processing instructions and whitespace can be inserted beside the root element.");
            }
        }

        // The tokenizer accepts unbound prefixes in a fragment; they must be bound where the fragment lands.
        for (int i = holder + 1; i < _count; i++)
        {
            if (_nodes[i].Kind != MarkupNodeKind.Element)
            {
                continue;
            }

            ElementName(i, out _, out ReadOnlySpan<byte> prefix, out _);
            if (prefix.Length > 0 && !TryResolvePrefix(i, prefix, out _))
            {
                throw new ArgumentException("The inserted XML uses a prefix that is not bound where it is inserted.");
            }

            XmlAttributeEnumerator en = AttributesOf(i, out _);
            while (en.MoveNext())
            {
                XmlAttributeToken a = en.Current;
                if (!a.IsNamespaceDeclaration && a.Prefix.Length > 0 && !TryResolvePrefix(i, a.Prefix, out _))
                {
                    throw new ArgumentException("The inserted XML uses a prefix that is not bound where it is inserted.");
                }
            }
        }
    }

    internal void RemoveNode(int n)
    {
        MarkupNodeKind kind = _nodes[n].Kind;
        if (kind == MarkupNodeKind.Document)
        {
            throw new InvalidOperationException("The document node cannot be removed.");
        }

        int parent = _nodes[n].Parent;
        if (parent < 0)
        {
            return;
        }

        if (kind == MarkupNodeKind.Element && parent == 0)
        {
            throw new InvalidOperationException("The root element cannot be removed or replaced.");
        }

        int prev = _nodes[n].Prev;
        int next = _nodes[n].Next;
        if (prev != 0)
        {
            _nodes[prev].Next = next;
        }
        else
        {
            _nodes[parent].FirstChild = next;
        }

        if (next != 0)
        {
            _nodes[next].Prev = prev;
        }
        else
        {
            _nodes[parent].LastChild = prev;
        }

        _nodes[n].Parent = -1;
        _nodes[n].Prev = 0;
        _nodes[n].Next = 0;
        MarkDirty(parent);
    }

    internal void ReplaceNode(int n, ReadOnlySpan<byte> xml)
    {
        if (_nodes[n].Parent < 0 || n == 0)
        {
            throw new InvalidOperationException("The node is not in the tree.");
        }

        if (_nodes[n].Kind == MarkupNodeKind.Element && _nodes[n].Parent == 0)
        {
            throw new InvalidOperationException("The root element cannot be removed or replaced.");
        }

        int parent = _nodes[n].Parent;
        InsertXml(parent, n, xml);
        RemoveNode(n);
    }

    internal void SetText(int n, string text)
    {
        var w = new ArrayBufferWriter<byte>(text.Length + 16);
        XmlText.Escape(w, text, false);
        ReadOnlySpan<byte> bytes = w.WrittenSpan;
        MarkupNodeKind kind = _nodes[n].Kind;
        switch (kind)
        {
            case MarkupNodeKind.Element:
                {
                    if (text.Length > 0)
                    {
                        EnsureEndTag(n);
                    }

                    _nodes[n].FirstChild = 0;
                    _nodes[n].LastChild = 0;
                    if (text.Length > 0)
                    {
                        int offset = AppendOverlay(bytes);
                        int t = AddNode(MarkupNodeKind.Text, n);
                        _nodes[t].A = offset;
                        _nodes[t].B = offset + bytes.Length;
                        _nodes[t].Flags = StartOverlay;
                    }

                    MarkDirty(n);
                    break;
                }

            case MarkupNodeKind.Text:
            case MarkupNodeKind.Whitespace:
            case MarkupNodeKind.CData:
                {
                    int offset = AppendOverlay(bytes);
                    _nodes[n].A = offset;
                    _nodes[n].B = offset + bytes.Length;
                    _nodes[n].Flags |= StartOverlay;
                    _nodes[n].Kind = XmlScan.IsWhitespaceOnly(bytes) ? MarkupNodeKind.Whitespace : MarkupNodeKind.Text;
                    MarkDirty(_nodes[n].Parent);
                    break;
                }

            default:
                throw new InvalidOperationException("Text can be set on elements and text nodes only.");
        }
    }
}
