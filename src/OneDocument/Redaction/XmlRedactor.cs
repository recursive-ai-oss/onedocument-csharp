using System.Linq;
using System.Security;
using System.Text;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.TextExtraction;

namespace RecursiveAI.OneDocument.Redaction;

// Removes matches from one XML part: paragraph-like containers across runs, then every remaining text node, attribute value, comment and
// processing instruction. Only the edited nodes change; everything else in the part keeps its bytes.
internal sealed class XmlRedactor
{
    private readonly RedactionContext _ctx;
    private readonly string _location;
    private readonly string _category;
    private readonly HashSet<MarkupNode> _handled = [];
    private readonly List<TextSegment> _segments = [];
    private readonly List<(int Start, int Length)> _spans = [];
    private int _count;

    public XmlRedactor(RedactionContext ctx, string location, string category)
    {
        _ctx = ctx;
        _location = location;
        _category = category;
    }

    public int Count => _count;

    public void Run(MarkupDocument doc)
    {
        MarkupNode root = doc.Root;
        if (root.IsNull)
        {
            return;
        }

        Containers(root);
        Generic(doc.DocumentNode);
        _ctx.Report.Add(_category, _location, _count);
    }

    private void Containers(MarkupNode el)
    {
        for (MarkupNode c = el.FirstChild; !c.IsNull; c = c.NextSibling)
        {
            if (c.Kind != MarkupNodeKind.Element)
            {
                continue;
            }

            if (OoxmlText.Classify(c, out _) == TextRole.Container)
            {
                Container(c);
            }

            Containers(c);
        }
    }

    private void Container(MarkupNode container)
    {
        _segments.Clear();
        OoxmlText.Collect(container, _segments);
        if (_segments.Count == 0)
        {
            return;
        }

        var visible = new StringBuilder();
        var code = new StringBuilder();
        var starts = new int[_segments.Count];
        for (int i = 0; i < _segments.Count; i++)
        {
            StringBuilder sb = _segments[i].Code ? code : visible;
            starts[i] = sb.Length;
            sb.Append(_segments[i].Text);
        }

        var replacement = new string?[_segments.Count];
        TextSegment[] segs = [.. _segments];
        foreach (MarkupNode leaf in segs.Select(static s => s.Leaf))
        {
            if (!leaf.IsNull)
            {
                _handled.Add(leaf);
            }
        }

        Stream(segs, starts, visible.ToString(), false, replacement);
        Stream(segs, starts, code.ToString(), true, replacement);
        for (int i = 0; i < segs.Length; i++)
        {
            if (replacement[i] is { } text)
            {
                SetLeafText(segs[i].Leaf, segs[i].Escaped ? EncodedText.EncodeX(text) : text);
            }
        }
    }

    private void Stream(TextSegment[] segs, int[] starts, string text, bool code, string?[] replacement)
    {
        if (text.Length == 0 || !_ctx.FindSpans(text, _location, _spans))
        {
            return;
        }

        var covered = new int[text.Length]; // 0 not covered, 1 covered, 2 first char of a span
        var spans = _spans.ToArray();
        foreach ((int s, int l) in spans)
        {
            for (int k = s; k < s + l; k++)
            {
                covered[k] = 1;
            }
        }

        int spanIndex = 0;
        bool placed = false;
        for (int i = 0; i < segs.Length; i++)
        {
            if (segs[i].Code != code)
            {
                continue;
            }

            string t = segs[i].Text;
            var sb = new StringBuilder(t.Length);
            bool changed = false;
            for (int j = 0; j < t.Length; j++)
            {
                int g = starts[i] + j;
                while (spanIndex < spans.Length - 1 && g >= spans[spanIndex].Start + spans[spanIndex].Length)
                {
                    spanIndex++;
                    placed = false;
                }

                if (covered[g] == 0)
                {
                    sb.Append(t[j]);
                    continue;
                }

                if (segs[i].Leaf.IsNull)
                {
                    sb.Append(t[j]); // a tab or break inside a match stays where it is
                    continue;
                }

                changed = true;
                if (!placed)
                {
                    (int s, int l) = spans[spanIndex];
                    sb.Append(_ctx.Mark(text.Substring(s, l), code));
                    placed = true;
                    _count++;
                }
            }

            if (changed)
            {
                replacement[i] = sb.ToString();
            }
        }

    }

    private static void SetLeafText(MarkupNode leaf, string text)
    {
        leaf.SetText(text);
        bool padded = text.Length > 0 && (text[0] == ' ' || text[^1] == ' ');
        if (padded && Ns.Classify(leaf.NamespaceUriBytes) != NsKind.Dml && RedactionSupport.Attr(leaf, "space") is null)
        {
            leaf.SetAttribute("xml:space", "preserve");
        }
    }

    private void Generic(MarkupNode parent)
    {
        for (MarkupNode c = parent.FirstChild; !c.IsNull;)
        {
            MarkupNode next = c.NextSibling;
            switch (c.Kind)
            {
                case MarkupNodeKind.Element:
                    Element(c);
                    break;
                case MarkupNodeKind.Text:
                case MarkupNodeKind.CData:
                    if (!_handled.Contains(parent))
                    {
                        TextNode(c, parent);
                    }

                    break;
                case MarkupNodeKind.Comment:
                case MarkupNodeKind.ProcessingInstruction:
                    if (_ctx.FindSpans(Encoding.UTF8.GetString(c.RawContent), _location, _spans))
                    {
                        c.Remove();
                        _count++;
                    }

                    break;
            }

            c = next;
        }
    }

    private void Element(MarkupNode el)
    {
        Attributes(el);
        if (OoxmlText.IsSml(el, "c") || OoxmlText.IsSml(el, "cell"))
        {
            Cell(el);
        }
        else if (OoxmlText.IsSml(el, "n") && el.GetAttribute("v") is { } v && _ctx.Apply(v, false, _location, out string replaced) > 0)
        {
            // A number in a pivot cache becomes a string item; a mark in a numeric attribute would be invalid.
            string p = PrefixOf(el);
            el.ReplaceWithXml(Encoding.UTF8.GetBytes($"<{p}s v=\"{SecurityElement.Escape(replaced)}\"/>"));
            _count++;
            return;
        }

        Generic(el);
    }

    private static string PrefixOf(MarkupNode el) => el.Prefix.Length == 0 ? string.Empty : Encoding.UTF8.GetString(el.Prefix) + ":";

    private void Attributes(MarkupNode el)
    {
        List<(string Name, string Value)>? edits = null;
        foreach (MarkupAttributeRef a in el.Attributes)
        {
            if (a.IsNamespaceDeclaration)
            {
                continue;
            }

            NsKind kind = Ns.Classify(a.NamespaceUriBytes);
            if (kind is NsKind.Rel or NsKind.Mc)
            {
                continue;
            }

            string name = Encoding.UTF8.GetString(a.RawName);
            if (name == "xml:space")
            {
                continue;
            }

            string value = a.Value;
            if (Base64(value, out string reencoded))
            {
                (edits ??= []).Add((name, reencoded));
                _count++;
                continue;
            }

            // SpreadsheetML names and captions use the same _xHHHH_ escapes as its strings.
            bool escaped = OoxmlText.IsSmlNode(el);
            string plain = escaped ? EncodedText.DecodeX(value) : value;
            if (_ctx.Apply(plain, IsIdentifierAttribute(el, a), _location, out string replaced) > 0)
            {
                (edits ??= []).Add((name, escaped ? EncodedText.EncodeX(replaced) : replaced));
                _count++;
            }
        }

        if (edits is not null)
        {
            foreach ((string name, string value) in edits)
            {
                el.SetAttribute(name, value);
            }
        }
    }

    // A value that is wholly base64 is binary data held as text (a custom XML blob, an embedded file). Its bytes are searched for literal matches,
    // and for patterns when they read as text; a match is overwritten in the bytes, which keeps the length, and the value is encoded again.
    private bool Base64(string value, out string reencoded)
    {
        reencoded = value;
        if (!EncodedText.TryDecodeBase64(value, out byte[] bytes))
        {
            return false;
        }

        int n = BinaryText.Redact(bytes, _ctx, false, _location, patterns: EncodedText.LooksLikeText(bytes));
        if (n == 0)
        {
            return false;
        }

        reencoded = EncodedText.EncodeBase64Like(value, bytes);
        return true;
    }

    private static bool IsIdentifierAttribute(MarkupNode el, MarkupAttributeRef a)
    {
        ReadOnlySpan<byte> attr = a.LocalName;
        ReadOnlySpan<byte> element = el.LocalName;
        if (attr.SequenceEqual("name"u8))
        {
            return element.SequenceEqual("bookmarkStart"u8) || element.SequenceEqual("definedName"u8) || element.SequenceEqual("sheet"u8) || element.SequenceEqual("table"u8);
        }

        return attr.SequenceEqual("displayName"u8) && element.SequenceEqual("table"u8);
    }

    private static bool IsIdentifierText(MarkupNode parent)
    {
        ReadOnlySpan<byte> n = parent.LocalName;
        return n.SequenceEqual("f"u8) || n.SequenceEqual("formula"u8) || n.SequenceEqual("formula1"u8) || n.SequenceEqual("formula2"u8)
            || n.SequenceEqual("definedName"u8) || n.SequenceEqual("instrText"u8) || n.SequenceEqual("delInstrText"u8);
    }

    private void TextNode(MarkupNode node, MarkupNode parent)
    {
        string text = node.Kind == MarkupNodeKind.CData ? Encoding.UTF8.GetString(node.RawContent) : node.InnerText;
        string result;
        if (Base64(text, out string reencoded))
        {
            result = reencoded;
        }
        else
        {
            bool escaped = OoxmlText.IsSmlNode(parent);
            string plain = escaped ? EncodedText.DecodeX(text) : text;
            if (_ctx.Apply(plain, IsIdentifierText(parent), _location, out string replaced) == 0)
            {
                return;
            }

            result = escaped ? EncodedText.EncodeX(replaced) : replaced;
        }

        _count++;
        if (node.Kind == MarkupNodeKind.Text)
        {
            node.SetText(result);
        }
        else
        {
            parent.SetText(result);
        }
    }

    // SpreadsheetML cells: a matched number or formula result becomes a string; shared-string cells are handled with the string table.
    private void Cell(MarkupNode cell)
    {
        MarkupNode v = default;
        bool hasFormula = false;
        for (MarkupNode c = cell.FirstChild; !c.IsNull; c = c.NextSibling)
        {
            if (OoxmlText.IsSml(c, "v"))
            {
                v = c;
            }
            else if (OoxmlText.IsSml(c, "f"))
            {
                hasFormula = true;
            }
        }

        if (v.IsNull)
        {
            return;
        }

        _handled.Add(v);
        string? t = cell.GetAttribute("t");
        if (t is "s" or "e")
        {
            return;
        }

        // A string result is stored with the same _xHHHH_ escapes as a string item.
        bool textResult = t is "str" or "inlineStr";
        string value = textResult ? EncodedText.DecodeX(v.InnerText) : v.InnerText;
        if (_ctx.Apply(value, false, _location, out string replaced) == 0)
        {
            return;
        }

        if (textResult)
        {
            replaced = EncodedText.EncodeX(replaced);
        }

        _count++;
        bool external = OoxmlText.IsSml(cell, "cell");
        if (hasFormula || external)
        {
            cell.SetAttribute("t", "str");
            v.SetText(replaced);
            return;
        }

        v.Remove();
        cell.SetAttribute("t", "inlineStr");
        string p = PrefixOf(cell);
        string space = replaced.StartsWith(' ') || replaced.EndsWith(' ') ? " xml:space=\"preserve\"" : string.Empty;
        cell.AppendXml(Encoding.UTF8.GetBytes($"<{p}is><{p}t{space}>{SecurityElement.Escape(replaced)}</{p}t></{p}is>"));
    }
}
