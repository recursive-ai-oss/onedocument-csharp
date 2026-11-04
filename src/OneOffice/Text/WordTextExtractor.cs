using RecursiveAI.OneOffice.Markup;
using RecursiveAI.OneOffice.Markup.Xml;

namespace RecursiveAI.OneOffice.TextExtraction;

/// <summary>
/// Streams the text of one WordprocessingML part (the document, a header, a footer, the notes or the comments) to a <see cref="TextSink"/>.
/// It reads tokens and builds no tree; memory is the open-paragraph buffers, whatever the size of the part.
/// </summary>
/// <remarks>
/// Rules (ECMA-376 Part 1 §17): text is <c>w:t</c> (§17.3.3.31), and <c>w:delText</c> only when deleted text is wanted (§17.3.3.7); a
/// field's code is <c>w:instrText</c> (§17.16.23) and never output, so only the field's result remains; <c>w:tab</c>, <c>w:ptab</c> give a
/// tab and <c>w:br</c>, <c>w:cr</c> a line feed; a text box's paragraphs follow the paragraph that holds it.
/// </remarks>
internal sealed class WordTextExtractor
{
    private sealed class Frame(long maxBytes)
    {
        public readonly CharBuffer Text = new(maxBytes);
        public long Offset;
        public bool MarkDeleted;
        public bool Merged;
        public int NumId = -1;
        public int Level;
        public string? Style;
        public List<Deferred>? Deferred;
    }

    private readonly record struct Deferred(string Text, long Offset, int TableDepth);

    private readonly OpenContext _context;
    private readonly TextExtractionOptions _options;
    private readonly TextSink _sink;
    private readonly CancellationToken _cancellation;
    private readonly List<Frame> _frames = [];
    private readonly AlternateContentGate _alternate = new();
    private int _depth;
    private int _tableDepth;
    private int _boxDepth;
    private int _deletedDepth;
    private bool _inParagraphProperties;
    private bool _inParagraphMarkProperties;
    private bool _collect;
    private bool _pendingMerge;

    public WordTextExtractor(OpenContext context, TextExtractionOptions options, TextSink sink, CancellationToken cancellation)
    {
        _context = context;
        _options = options;
        _sink = sink;
        _cancellation = cancellation;
    }

    /// <summary>Reads the whole part.</summary>
    public void Run(MarkupReader reader)
    {
        while (reader.Read())
        {
            switch (reader.Kind)
            {
                case XmlTokenKind.StartElement:
                case XmlTokenKind.EmptyElement:
                    OnStart(reader);
                    break;
                case XmlTokenKind.EndElement:
                    OnEnd(reader);
                    break;
                case XmlTokenKind.Text:
                case XmlTokenKind.Whitespace:
                    if (_collect && _depth > 0)
                    {
                        Top().Text.AppendXml(reader.Content);
                    }

                    break;
                case XmlTokenKind.CData:
                    if (_collect && _depth > 0)
                    {
                        Top().Text.AppendXml(reader.Content, literal: true);
                    }

                    break;
            }
        }

        FlushMerge();
    }

    private Frame Top() => _frames[_depth - 1];

    private void OnStart(MarkupReader r)
    {
        bool start = r.Kind == XmlTokenKind.StartElement;
        NsKind ns = Ns.Classify(r.NamespaceUri);
        ReadOnlySpan<byte> name = r.LocalName;
        switch (ns)
        {
            case NsKind.Mc:
                _alternate.OnStart(r, name);
                return;
            case NsKind.Math:
                if (start && name.SequenceEqual("t"u8))
                {
                    _collect = _deletedDepth == 0 || _options.IncludeDeletedText;
                }

                return;
            case NsKind.Wml:
                break;
            default:
                return;
        }

        if (name.SequenceEqual("p"u8))
        {
            BeginParagraph(r.TokenOffset);
            if (!start)
            {
                EndParagraph();
            }

            return;
        }

        if (name.SequenceEqual("t"u8))
        {
            if (start)
            {
                _collect = _deletedDepth == 0 || _options.IncludeDeletedText;
            }

            return;
        }

        if (name.SequenceEqual("tab"u8))
        {
            // Inside w:pPr, w:tab is a tab stop, not a tab character.
            if (!_inParagraphProperties)
            {
                AppendChar('\t');
            }

            return;
        }

        if (name.SequenceEqual("br"u8) || name.SequenceEqual("cr"u8))
        {
            AppendChar('\n');
            return;
        }

        if (name.SequenceEqual("ptab"u8))
        {
            AppendChar('\t');
            return;
        }

        if (name.SequenceEqual("pPr"u8))
        {
            if (start && _depth > 0)
            {
                _inParagraphProperties = true;
            }

            return;
        }

        if (name.SequenceEqual("rPr"u8))
        {
            if (start && _inParagraphProperties)
            {
                _inParagraphMarkProperties = true;
            }

            return;
        }

        if (name.SequenceEqual("del"u8))
        {
            OnDeletion(start);
            return;
        }

        if (name.SequenceEqual("tc"u8))
        {
            if (start)
            {
                FlushMerge();
                if (_boxDepth == 0)
                {
                    _sink.CellStart(_tableDepth);
                }
            }

            return;
        }

        if (name.SequenceEqual("tbl"u8))
        {
            if (start)
            {
                FlushMerge();
                if (_boxDepth == 0)
                {
                    _tableDepth++;
                }
            }

            return;
        }

        if (name.SequenceEqual("numId"u8))
        {
            if (_inParagraphProperties)
            {
                SetNumbering(r, level: false);
            }

            return;
        }

        if (name.SequenceEqual("ilvl"u8))
        {
            if (_inParagraphProperties)
            {
                SetNumbering(r, level: true);
            }

            return;
        }

        if (name.SequenceEqual("pStyle"u8))
        {
            if (_inParagraphProperties && _options.ListLabels is not null && _depth > 0 && !Top().Merged
                && Ns.TryGetAttribute(r, "val"u8, out ReadOnlySpan<byte> style))
            {
                Top().Style = XmlText.GetString(style, true);
            }

            return;
        }

        if (name.SequenceEqual("delText"u8))
        {
            if (start)
            {
                _collect = _options.IncludeDeletedText;
            }

            return;
        }

        if (name.SequenceEqual("sdtPr"u8))
        {
            if (start)
            {
                r.Skip();
            }

            return;
        }

        if (name.SequenceEqual("txbxContent"u8))
        {
            if (start)
            {
                _boxDepth++;
            }
        }
        else if (name.SequenceEqual("moveFrom"u8))
        {
            OnDeletion(start);
        }
        else if (name.SequenceEqual("noBreakHyphen"u8))
        {
            AppendChar('-');
        }
        else if (name.SequenceEqual("sdtEndPr"u8) || name.EndsWith("PrChange"u8) || name.SequenceEqual("tblGridChange"u8) || name.SequenceEqual("numberingChange"u8))
        {
            // Property-change records hold the old properties (and for a paragraph, an old mark); they have no text.
            if (start)
            {
                r.Skip();
            }
        }
        else if ((name.SequenceEqual("footnote"u8) || name.SequenceEqual("endnote"u8)) && start && Ns.TryGetAttribute(r, "type"u8, out ReadOnlySpan<byte> type)
            && (type.SequenceEqual("separator"u8) || type.SequenceEqual("continuationSeparator"u8) || type.SequenceEqual("continuationNotice"u8)))
        {
            // Separator notes carry only the separator line.
            r.Skip();
        }
    }

    private void OnEnd(MarkupReader r)
    {
        NsKind ns = Ns.Classify(r.NamespaceUri);
        ReadOnlySpan<byte> name = r.LocalName;
        switch (ns)
        {
            case NsKind.Mc:
                _alternate.OnEnd(name);
                return;
            case NsKind.Math:
                if (name.SequenceEqual("t"u8))
                {
                    _collect = false;
                }

                return;
            case NsKind.Wml:
                break;
            default:
                return;
        }

        if (name.SequenceEqual("p"u8))
        {
            EndParagraph();
        }
        else if (name.SequenceEqual("t"u8) || name.SequenceEqual("delText"u8))
        {
            _collect = false;
        }
        else if (name.SequenceEqual("pPr"u8))
        {
            _inParagraphProperties = false;
            _inParagraphMarkProperties = false;
        }
        else if (name.SequenceEqual("rPr"u8))
        {
            _inParagraphMarkProperties = false;
        }
        else if (name.SequenceEqual("del"u8) || name.SequenceEqual("moveFrom"u8))
        {
            if (!_inParagraphMarkProperties)
            {
                _deletedDepth--;
            }
        }
        else if (name.SequenceEqual("txbxContent"u8))
        {
            _boxDepth--;
        }
        else if (name.SequenceEqual("tc"u8))
        {
            FlushMerge();
        }
        else if (name.SequenceEqual("tr"u8))
        {
            FlushMerge();
            if (_boxDepth == 0)
            {
                _sink.RowEnd(_tableDepth);
            }
        }
        else if (name.SequenceEqual("tbl"u8))
        {
            FlushMerge();
            if (_boxDepth == 0)
            {
                _tableDepth--;
            }
        }
    }

    // ECMA-376 Part 1 §17.13.5.14 and §17.13.5.22: inside a paragraph's mark properties, w:del / w:moveFrom say the paragraph mark was
    // deleted (the paragraph merges with the next one); elsewhere they wrap deleted runs.
    private void OnDeletion(bool start)
    {
        if (_inParagraphMarkProperties)
        {
            if (_depth > 0)
            {
                Top().MarkDeleted = true;
            }
        }
        else if (start)
        {
            _deletedDepth++;
        }
    }

    private void SetNumbering(MarkupReader r, bool level)
    {
        if (_options.ListLabels is null || _depth == 0 || Top().Merged || !Ns.TryGetAttribute(r, "val"u8, out ReadOnlySpan<byte> raw)
            || !int.TryParse(raw, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int value))
        {
            return;
        }

        if (level)
        {
            Top().Level = value;
        }
        else
        {
            Top().NumId = value;
        }
    }

    private void AppendChar(char c)
    {
        if (_depth > 0 && !_inParagraphProperties && (_deletedDepth == 0 || _options.IncludeDeletedText))
        {
            Top().Text.Append(c);
        }
    }

    private Frame FrameAt(int index)
    {
        while (_frames.Count <= index)
        {
            _frames.Add(new Frame(_context.Limits.MaxAllocationBytes));
        }

        return _frames[index];
    }

    private void BeginParagraph(long offset)
    {
        _cancellation.ThrowIfCancellationRequested();
        if (_depth == 0 && _pendingMerge)
        {
            // The previous paragraph's mark was deleted, so this one continues it.
            _pendingMerge = false;
            _depth = 1;
            Frame merged = FrameAt(0);
            merged.MarkDeleted = false;
            merged.Merged = true;
            return;
        }

        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxXmlDepth), _context.Limits.MaxXmlDepth, _depth + 1);
        Frame f = FrameAt(_depth);
        _depth++;
        f.Text.Clear();
        f.Offset = offset;
        f.MarkDeleted = false;
        f.Merged = false;
        f.NumId = -1;
        f.Level = 0;
        f.Style = null;
        f.Deferred?.Clear();
    }

    private void EndParagraph()
    {
        if (_depth == 0)
        {
            return;
        }

        Frame f = Top();
        if (_depth == 1)
        {
            _depth = 0;
            _inParagraphProperties = false;
            _inParagraphMarkProperties = false;
            if (f.MarkDeleted && !_options.IncludeDeletedText)
            {
                _pendingMerge = true;
                return;
            }

            Emit(f);
            return;
        }

        // A paragraph inside a paragraph is a text box's: it comes out after the paragraph that holds the box.
        Frame parent = _frames[_depth - 2];
        parent.Deferred ??= [];
        if (f.Text.Length > 0)
        {
            parent.Deferred.Add(new Deferred(f.Text.ToString(), f.Offset, _tableDepth));
        }

        if (f.Deferred is { Count: > 0 })
        {
            parent.Deferred.AddRange(f.Deferred);
            f.Deferred.Clear();
        }

        _depth--;
    }

    private void FlushMerge()
    {
        if (_pendingMerge)
        {
            _pendingMerge = false;
            Emit(FrameAt(0));
        }
    }

    private void Emit(Frame f)
    {
        if (_options.ListLabels is { } labels && (f.NumId > 0 || (f.NumId < 0 && f.Style is not null)))
        {
            string? label = labels.NextLabel(f.NumId, f.Level, f.Style);
            if (!string.IsNullOrEmpty(label))
            {
                f.Text.Insert(label + "\t");
            }
        }

        _sink.Paragraph(f.Text.AsSpan(), new ParagraphInfo(f.Offset, _tableDepth, false));
        if (f.Deferred is { Count: > 0 })
        {
            foreach (Deferred d in f.Deferred)
            {
                _sink.Paragraph(d.Text, new ParagraphInfo(d.Offset, d.TableDepth, true));
            }

            f.Deferred.Clear();
        }
    }
}
