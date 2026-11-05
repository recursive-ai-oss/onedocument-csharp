using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Documents.Templating;

/// <summary>An inline wrapper (hyperlink, tracked change, smart tag, content control) that a run sits in: the text that opens it and the text that closes it.</summary>
internal sealed class RunWrapper(string open, string close)
{
    public string Open { get; } = open;

    public string Close { get; } = close;
}

/// <summary>
/// One piece of a paragraph's inline content, in document order: either a stretch of text with the formatting of one run (a <i>segment</i>, which
/// the template engine reads) or content the engine never sees (a bookmark, a picture, a field character, a proofing mark), kept as XML.
/// </summary>
internal sealed class ParagraphItem
{
    /// <summary>The wrappers around the item, outermost first.</summary>
    public RunWrapper[] Chain { get; init; } = [];

    /// <summary>The XML of an opaque item; null for a text segment.</summary>
    public string? Xml { get; init; }

    /// <summary>The index of the text segment, or -1 for an opaque item.</summary>
    public int Segment { get; init; } = -1;

    /// <summary>The start tag of the run a segment came from, with its attributes (revision-save ids included).</summary>
    public string RunStart { get; init; } = string.Empty;

    /// <summary>The run properties element of the run a segment came from, or empty.</summary>
    public string RunProperties { get; init; } = string.Empty;

    /// <summary>The text of a segment: <c>w:t</c> text, with a tab as U+0009 and a plain line break as U+000A.</summary>
    public string Text { get; init; } = string.Empty;
}

/// <summary>
/// A paragraph cut into the pieces templating needs. Each run that holds text becomes one segment (a run that mixes text with other content is
/// split into several items, which is how Word itself treats them), and everything else stays as opaque XML in its place. Word splits a tag across runs
/// whenever it was typed in several edits, was spell-checked or sits in a tracked change, so the engine reads the segments of a paragraph as one text.
/// </summary>
internal sealed class ParagraphModel
{
    private static readonly string[] WrapperNames = ["hyperlink", "ins", "del", "moveFrom", "moveTo", "smartTag", "customXml", "sdt", "dir", "bdo"];

    /// <summary>The prefix of the main namespace in the paragraph's scope.</summary>
    public required string Prefix { get; init; }

    /// <summary>The paragraph's start tag, or the whole element when it has no content.</summary>
    public required string StartTag { get; init; }

    /// <summary>The <c>w:pPr</c> element, or empty.</summary>
    public string ParagraphProperties { get; private set; } = string.Empty;

    /// <summary>True when the paragraph has no content, written <c>&lt;w:p/&gt;</c>.</summary>
    public bool IsEmpty { get; init; }

    /// <summary>The paragraph exactly as it was in the part.</summary>
    public required string Raw { get; init; }

    /// <summary>The items in order.</summary>
    public List<ParagraphItem> Items { get; } = [];

    /// <summary>The text items in order; segment <c>i</c> of the engine's unit is <c>Segments[i]</c>.</summary>
    public List<ParagraphItem> Segments { get; } = [];

    /// <summary>The item index of each segment.</summary>
    public List<int> SegmentItem { get; } = [];

    /// <summary>The text of the whole paragraph.</summary>
    public string Text => string.Concat(Segments.Select(static s => s.Text));

    /// <summary>The end tag, <c>&lt;/w:p&gt;</c>.</summary>
    public string EndTag => IsEmpty ? string.Empty : "</" + Prefix + ":p>";

    /// <summary>Reads a paragraph element.</summary>
    /// <param name="p">The <c>w:p</c> element.</param>
    /// <param name="ns">The main namespace.</param>
    /// <returns>The model.</returns>
    public static ParagraphModel Read(MarkupNode p, string ns)
    {
        string raw = p.OuterXml;
        string prefix = p.Prefix.Length == 0 ? string.Empty : Encoding.UTF8.GetString(p.Prefix);
        string start = XmlSnip.StartTag(raw, out bool selfClosing);
        var model = new ParagraphModel { Prefix = prefix, StartTag = start, Raw = raw, IsEmpty = selfClosing };
        if (!selfClosing)
        {
            model.Collect(p, ns, [], top: true);
        }

        return model;
    }

    private void Collect(MarkupNode parent, string ns, RunWrapper[] chain, bool top)
    {
        foreach (MarkupNode c in parent.ChildElements)
        {
            if (top && c.IsElement(ns, "pPr"))
            {
                ParagraphProperties = c.OuterXml;
                continue;
            }

            if (c.IsElement(ns, "r"))
            {
                ReadRun(c, ns, chain);
                continue;
            }

            if (IsWrapper(c, ns) && !c.IsSelfClosing && TryWrap(c, ns, out RunWrapper? wrapper, out MarkupNode content))
            {
                Collect(content, ns, [.. chain, wrapper], top: false);
                continue;
            }

            Items.Add(new ParagraphItem { Chain = chain, Xml = c.OuterXml });
        }
    }

    private static bool IsWrapper(MarkupNode c, string ns)
    {
        foreach (string name in WrapperNames)
        {
            if (c.IsElement(ns, name))
            {
                return true;
            }
        }

        return false;
    }

    // The wrapper's start tag plus the property children that must come first (content-control properties, smart-tag properties).
    private static bool TryWrap(MarkupNode e, string ns, out RunWrapper wrapper, out MarkupNode content)
    {
        string raw = e.OuterXml;
        string open = XmlSnip.StartTag(raw, out _);
        string close = "</" + e.Name + ">";
        content = e;
        var head = new StringBuilder();
        if (e.IsElement(ns, "sdt"))
        {
            MarkupNode sdtContent = default;
            foreach (MarkupNode c in e.ChildElements)
            {
                if (c.IsElement(ns, "sdtContent"))
                {
                    sdtContent = c;
                }
                else
                {
                    head.Append(c.OuterXml);
                }
            }

            if (sdtContent.IsNull || sdtContent.IsSelfClosing)
            {
                wrapper = null!;
                return false;
            }

            open += head + XmlSnip.StartTag(sdtContent.OuterXml, out _);
            close = "</" + sdtContent.Name + ">" + close;
            content = sdtContent;
        }
        else
        {
            foreach (MarkupNode c in e.ChildElements)
            {
                if (c.IsElement(ns, "smartTagPr") || c.IsElement(ns, "customXmlPr"))
                {
                    head.Append(c.OuterXml);
                }
            }

            open += head;
        }

        wrapper = new RunWrapper(open, close);
        return true;
    }

    private void ReadRun(MarkupNode run, string ns, RunWrapper[] chain)
    {
        string runStart = XmlSnip.StartTag(run.OuterXml, out bool selfClosing);
        string rPr = string.Empty;
        bool hasText = false;
        foreach (MarkupNode c in run.ChildElements)
        {
            if (c.IsElement(ns, "rPr"))
            {
                rPr = c.OuterXml;
            }
            else if (c.IsElement(ns, "t"))
            {
                hasText = true;
            }
        }

        if (selfClosing || !hasText)
        {
            Items.Add(new ParagraphItem { Chain = chain, Xml = run.OuterXml });
            return;
        }

        var text = new StringBuilder();
        void FlushText()
        {
            if (text.Length > 0)
            {
                AddSegment(chain, runStart, rPr, text.ToString());
                text.Clear();
            }
        }

        foreach (MarkupNode c in run.ChildElements)
        {
            if (c.IsElement(ns, "rPr"))
            {
                continue;
            }

            if (c.IsElement(ns, "t"))
            {
                text.Append(c.InnerText);
            }
            else if (c.IsElement(ns, "tab") && c.IsSelfClosing)
            {
                text.Append('\t');
            }
            else if (c.IsElement(ns, "br") && IsPlainBreak(c))
            {
                text.Append('\n');
            }
            else
            {
                FlushText();
                Items.Add(new ParagraphItem { Chain = chain, Xml = runStart + rPr + c.OuterXml + "</" + run.Name + ">" });
            }
        }

        FlushText();
    }

    // ECMA-376 Part 1 §17.3.3.1: a break without a type, or of type textWrapping, with no clear attribute, is an ordinary line break.
    private static bool IsPlainBreak(MarkupNode br)
    {
        string? type = br.GetAttribute("http://schemas.openxmlformats.org/wordprocessingml/2006/main", "type")
            ?? br.GetAttribute("http://purl.oclc.org/ooxml/wordprocessingml/main", "type");
        string? clear = br.GetAttribute("http://schemas.openxmlformats.org/wordprocessingml/2006/main", "clear")
            ?? br.GetAttribute("http://purl.oclc.org/ooxml/wordprocessingml/main", "clear");
        return (type is null or "textWrapping") && clear is null;
    }

    private void AddSegment(RunWrapper[] chain, string runStart, string rPr, string text)
    {
        var item = new ParagraphItem { Chain = chain, Segment = Segments.Count, RunStart = runStart, RunProperties = rPr, Text = text };
        SegmentItem.Add(Items.Count);
        Segments.Add(item);
        Items.Add(item);
    }
}

/// <summary>Small helpers over serialized XML text.</summary>
internal static class XmlSnip
{
    /// <summary>The start tag of an element given as text, up to its closing angle bracket, quotes respected.</summary>
    public static string StartTag(string outerXml, out bool selfClosing)
    {
        char quote = '\0';
        for (int i = 0; i < outerXml.Length; i++)
        {
            char ch = outerXml[i];
            if (quote != '\0')
            {
                if (ch == quote)
                {
                    quote = '\0';
                }
            }
            else if (ch is '"' or '\'')
            {
                quote = ch;
            }
            else if (ch == '>')
            {
                selfClosing = i > 0 && outerXml[i - 1] == '/';
                return outerXml[..(i + 1)];
            }
        }

        selfClosing = false;
        return outerXml;
    }

    /// <summary>Escapes character data; characters XML cannot carry are dropped.</summary>
    public static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length + 8);
        foreach (char c in text)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                default:
                    if ((c < 0x20 && c != '\t' && c != '\n' && c != '\r') || c == '\uFFFE' || c == '\uFFFF')
                    {
                        break;
                    }

                    sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>The run content for a text: <c>w:t</c> with a tab as <c>w:tab</c> and a line feed as <c>w:br</c> (ECMA-376 Part 1 §17.3.3).</summary>
    public static string RunContent(string prefix, string text)
    {
        var sb = new StringBuilder();
        string p = prefix.Length == 0 ? string.Empty : prefix + ":";
        int start = 0;
        for (int i = 0; i <= text.Length; i++)
        {
            bool end = i == text.Length;
            char c = end ? '\0' : text[i];
            if (!end && c != '\t' && c != '\n')
            {
                continue;
            }

            if (i > start)
            {
                string piece = text.Substring(start, i - start).Replace("\r", string.Empty, StringComparison.Ordinal);
                if (piece.Length > 0)
                {
                    sb.Append('<').Append(p).Append("t xml:space=\"preserve\">").Append(Escape(piece)).Append("</").Append(p).Append("t>");
                }
            }

            if (!end)
            {
                sb.Append('<').Append(p).Append(c == '\t' ? "tab" : "br").Append("/>");
            }

            start = i + 1;
        }

        return sb.ToString();
    }
}
