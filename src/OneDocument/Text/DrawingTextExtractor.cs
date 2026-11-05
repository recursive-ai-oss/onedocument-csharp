using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Markup.Xml;

namespace RecursiveAI.OneDocument.TextExtraction;

/// <summary>
/// Streams the text of one PresentationML part (a slide or a notes page) to a <see cref="TextSink"/>, in shape order: DrawingML
/// paragraphs (<c>a:p</c>, ECMA-376 Part 1 §21.1.2.2.6) with their runs (<c>a:t</c>, §21.1.2.3.12), line breaks (<c>a:br</c>, §21.1.2.2.1)
/// and field results, and table cells (<c>a:tc</c>, §21.1.3.16).
/// </summary>
internal sealed class DrawingTextExtractor
{
    private readonly TextSink _sink;
    private readonly CancellationToken _cancellation;
    private readonly CharBuffer _text;
    private readonly AlternateContentGate _alternate = new();
    private bool _inParagraph;
    private bool _collect;
    private long _offset;
    private int _tableDepth;
    private bool _dropShape;

    public DrawingTextExtractor(OpenContext context, TextSink sink, CancellationToken cancellation)
    {
        _sink = sink;
        _cancellation = cancellation;
        _text = new CharBuffer(context.Limits.MaxAllocationBytes);
    }

    /// <summary>Reads the whole part.</summary>
    public void Run(MarkupReader r)
    {
        while (r.Read())
        {
            switch (r.Kind)
            {
                case XmlTokenKind.StartElement:
                case XmlTokenKind.EmptyElement:
                    OnStart(r);
                    break;
                case XmlTokenKind.EndElement:
                    OnEnd(r);
                    break;
                case XmlTokenKind.Text:
                case XmlTokenKind.Whitespace:
                    if (_collect && _inParagraph)
                    {
                        _text.AppendXml(r.Content);
                    }

                    break;
                case XmlTokenKind.CData:
                    if (_collect && _inParagraph)
                    {
                        _text.AppendXml(r.Content, literal: true);
                    }

                    break;
            }
        }
    }

    private void OnStart(MarkupReader r)
    {
        bool start = r.Kind == XmlTokenKind.StartElement;
        NsKind ns = Ns.Classify(r.NamespaceUri);
        ReadOnlySpan<byte> name = r.LocalName;
        switch (ns)
        {
            case NsKind.Mc:
                _alternate.OnStart(r, name);
                break;
            case NsKind.Math:
                if (start && name.SequenceEqual("t"u8))
                {
                    _collect = true;
                }

                break;
            case NsKind.Dml:
                OnDrawing(r, name, start);
                break;
            case NsKind.Pml:
                if (name.SequenceEqual("sp"u8))
                {
                    _dropShape = false;
                }
                else if (name.SequenceEqual("ph"u8) && Ns.TryGetAttribute(r, "type"u8, out ReadOnlySpan<byte> type)
                    && (type.SequenceEqual("sldNum"u8) || type.SequenceEqual("sldImg"u8)))
                {
                    // A slide-number placeholder holds a field whose result is the number (or a "‹#›" stand-in), and the slide-image
                    // placeholder on a notes page holds no text; neither is the author's text.
                    _dropShape = true;
                }
                else if (start && name.SequenceEqual("extLst"u8))
                {
                    r.Skip();
                }

                break;
        }
    }

    private void OnDrawing(MarkupReader r, ReadOnlySpan<byte> name, bool start)
    {
        if (name.SequenceEqual("p"u8))
        {
            _cancellation.ThrowIfCancellationRequested();
            _text.Clear();
            _offset = r.TokenOffset;
            _inParagraph = true;
            if (!start)
            {
                EndParagraph();
            }
        }
        else if (name.SequenceEqual("t"u8))
        {
            if (start)
            {
                _collect = true;
            }
        }
        else if (name.SequenceEqual("br"u8))
        {
            if (_inParagraph)
            {
                _text.Append('\n');
            }
        }
        else if (name.SequenceEqual("tbl"u8))
        {
            if (start)
            {
                _tableDepth++;
            }
        }
        else if (name.SequenceEqual("tc"u8))
        {
            if (start)
            {
                _sink.CellStart(_tableDepth);
            }
        }
        else if (start && name.SequenceEqual("extLst"u8))
        {
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
                break;
            case NsKind.Math:
                if (name.SequenceEqual("t"u8))
                {
                    _collect = false;
                }

                break;
            case NsKind.Dml:
                if (name.SequenceEqual("p"u8))
                {
                    EndParagraph();
                }
                else if (name.SequenceEqual("t"u8))
                {
                    _collect = false;
                }
                else if (name.SequenceEqual("tr"u8))
                {
                    _sink.RowEnd(_tableDepth);
                }
                else if (name.SequenceEqual("tbl"u8))
                {
                    _tableDepth--;
                }

                break;
            case NsKind.Pml:
                if (name.SequenceEqual("sp"u8))
                {
                    _dropShape = false;
                }

                break;
        }
    }

    private void EndParagraph()
    {
        if (!_inParagraph)
        {
            return;
        }

        _inParagraph = false;
        _collect = false;

        // Slides are full of empty paragraphs (spacers, empty placeholders); they carry no text and are not output.
        if (_text.Length > 0 && !_dropShape)
        {
            _sink.Paragraph(_text.AsSpan(), new ParagraphInfo(_offset, _tableDepth, false));
        }
    }
}
