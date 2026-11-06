using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.TextExtraction;

namespace RecursiveAI.OneDocument.Redaction;

// What an element means for reading text across runs: a paragraph-like container, a text leaf, a field-code leaf or a break.
internal enum TextRole : byte
{
    None = 0,
    Container,
    Leaf,
    CodeLeaf,
    Break,
}

// Escaped: the text was decoded from SpreadsheetML _xHHHH_ escapes and goes back through EncodedText.EncodeX when it is written.
internal readonly record struct TextSegment(MarkupNode Leaf, string Text, bool Code, bool Escaped = false);

internal static class OoxmlText
{
    public static TextRole Classify(MarkupNode el, out string breakText)
    {
        breakText = string.Empty;
        NsKind ns = Ns.Classify(el.NamespaceUriBytes);
        ReadOnlySpan<byte> n = el.LocalName;
        switch (ns)
        {
            case NsKind.Wml:
                if (n.SequenceEqual("p"u8))
                {
                    return TextRole.Container;
                }

                if (n.SequenceEqual("t"u8) || n.SequenceEqual("delText"u8))
                {
                    return TextRole.Leaf;
                }

                if (n.SequenceEqual("instrText"u8) || n.SequenceEqual("delInstrText"u8))
                {
                    return TextRole.CodeLeaf;
                }

                if (n.SequenceEqual("tab"u8))
                {
                    breakText = "\t";
                    return TextRole.Break;
                }

                if (n.SequenceEqual("br"u8) || n.SequenceEqual("cr"u8))
                {
                    breakText = "\n";
                    return TextRole.Break;
                }

                if (n.SequenceEqual("noBreakHyphen"u8))
                {
                    breakText = "-";
                    return TextRole.Break;
                }

                return TextRole.None;
            case NsKind.Dml:
                if (n.SequenceEqual("p"u8))
                {
                    return TextRole.Container;
                }

                if (n.SequenceEqual("t"u8))
                {
                    return TextRole.Leaf;
                }

                if (n.SequenceEqual("br"u8))
                {
                    breakText = "\n";
                    return TextRole.Break;
                }

                return TextRole.None;
            case NsKind.Math:
                return n.SequenceEqual("t"u8) ? TextRole.Leaf : TextRole.None;
            case NsKind.Sml:
                if (n.SequenceEqual("si"u8) || n.SequenceEqual("is"u8) || n.SequenceEqual("text"u8) || n.SequenceEqual("rPh"u8))
                {
                    return TextRole.Container;
                }

                return n.SequenceEqual("t"u8) ? TextRole.Leaf : TextRole.None;
            default:
                return TextRole.None;
        }
    }

    // Collects the text of one container in reading order. A nested container (a text box's paragraph, phonetic text) is its own unit.
    public static void Collect(MarkupNode container, List<TextSegment> segments)
    {
        for (MarkupNode c = container.FirstChild; !c.IsNull; c = c.NextSibling)
        {
            if (c.Kind != MarkupNodeKind.Element)
            {
                continue;
            }

            switch (Classify(c, out string breakText))
            {
                case TextRole.Container:
                    break;
                case TextRole.Leaf:
                    string raw = c.InnerText;
                    string text = IsSmlNode(c) ? EncodedText.DecodeX(raw) : raw;
                    segments.Add(new TextSegment(c, text, false, !string.Equals(text, raw, StringComparison.Ordinal)));
                    break;
                case TextRole.CodeLeaf:
                    segments.Add(new TextSegment(c, c.InnerText, true));
                    break;
                case TextRole.Break:
                    segments.Add(new TextSegment(default, breakText, false));
                    break;
                default:
                    Collect(c, segments);
                    break;
            }
        }
    }

    public static bool IsSmlNode(MarkupNode el) => Ns.Classify(el.NamespaceUriBytes) == NsKind.Sml;

    public static bool IsSml(MarkupNode el, string local) =>
        Ns.Classify(el.NamespaceUriBytes) == NsKind.Sml && el.LocalNameString == local;
}
