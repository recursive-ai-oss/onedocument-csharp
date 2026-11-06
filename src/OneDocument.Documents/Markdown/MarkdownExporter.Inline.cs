using System.Globalization;
using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Equations;
using RecursiveAI.OneDocument.TextExtraction;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Documents.Notes;
using RecursiveAI.OneDocument.Documents.Styles;

namespace RecursiveAI.OneDocument.Documents.Markdown;

internal sealed partial class MarkdownExporter
{
    private abstract class It;

    private sealed class TextIt(string text, bool bold, bool italic, bool strike, bool code) : It
    {
        public string Text { get; } = text;

        public bool Bold { get; } = bold;

        public bool Italic { get; } = italic;

        public bool Strike { get; } = strike;

        public bool Code { get; } = code;

        public bool SameFormat(TextIt o) => Bold == o.Bold && Italic == o.Italic && Strike == o.Strike && Code == o.Code;
    }

    private sealed class BreakIt : It;

    private sealed class RawIt(string markdown) : It
    {
        public string Markdown { get; } = markdown;
    }

    private sealed class LinkIt(string url, List<It> inner) : It
    {
        public string Url { get; } = url;

        public List<It> Inner { get; } = inner;
    }

    private sealed class FieldState(string instruction, List<It> saved)
    {
        public StringBuilder Instruction { get; } = new(instruction);

        public List<It> Saved { get; } = saved;

        public bool InInstruction { get; set; } = true;
    }

    private List<It> _cur = [];
    private readonly Stack<FieldState> _fields = new();

    // ---- gathering the inline content of a paragraph ----------------------------------------------------------------------------

    private List<It> GatherParagraph(Paragraph p)
    {
        _cur = [];
        _fields.Clear();
        Gather(p.Inlines(), 0);
        while (_fields.Count > 0)
        {
            EndField();
        }

        return _cur;
    }

    private string RenderParagraph(Paragraph p, bool inTable, bool heading)
    {
        List<It> items = GatherParagraph(p);
        var sb = new StringBuilder();
        bool lineStart = true;
        EmitItems(items, sb, ref lineStart, inTable, heading);
        return sb.ToString().Trim(' ');
    }

    private string RawText(Paragraph p)
    {
        var sb = new StringBuilder();
        void Walk(List<It> items)
        {
            foreach (It it in items)
            {
                switch (it)
                {
                    case TextIt t:
                        sb.Append(t.Text);
                        break;
                    case BreakIt:
                        sb.Append('\n');
                        break;
                    case LinkIt l:
                        Walk(l.Inner);
                        break;
                }
            }
        }

        Walk(GatherParagraph(p));
        return sb.ToString();
    }

    private void Gather(IEnumerable<InlineElement> inlines, int depth)
    {
        if (depth > 64)
        {
            return;
        }

        foreach (InlineElement e in inlines)
        {
            switch (e)
            {
                case Run r:
                    GatherRun(r);
                    break;
                case Hyperlink h:
                    GatherHyperlink(h, depth);
                    break;
                case SimpleField f:
                    {
                        string instr = f.Instruction ?? string.Empty;
                        if (TryHyperlinkTarget(instr, out string? target) && target is not null)
                        {
                            List<It> saved = _cur;
                            _cur = [];
                            Gather(f.Inlines(), depth + 1);
                            List<It> inner = _cur;
                            _cur = saved;
                            _cur.Add(new LinkIt(target, inner));
                        }
                        else
                        {
                            Gather(f.Inlines(), depth + 1);
                        }

                        break;
                    }

                case InlineGroup g:
                    if (g.ElementName is "del" or "moveFrom")
                    {
                        break;
                    }

                    Gather(g.Inlines(), depth + 1);
                    break;
                case InlineContentControl c:
                    Gather(c.Inlines(), depth + 1);
                    break;
                case OtherInline o:
                    GatherOther(o);
                    break;
            }
        }
    }

    private void GatherOther(OtherInline o)
    {
        string name = o.Markup.LocalNameText;
        if (name is "oMath" or "oMathPara")
        {
            _loss.Report("loss.markdown-equation", "equations", 1, "Equations were written as their linear text, not as math.");
            string linear = MathLinearizer.Linearize(OmmlReader.Read(o.Markup.Node, out _));
            _cur.Add(new TextIt(linear, false, false, false, false));
        }
        else if (name == "AlternateContent")
        {
            _loss.Report("loss.markdown-drawing", "drawings", 1, "Shapes, text boxes, charts and diagrams have no Markdown form and were left out.");
        }
    }

    private void GatherHyperlink(Hyperlink h, int depth)
    {
        string? url = null;
        string? anchor = h.Anchor;
        if (h.RelationshipId is { } rid)
        {
            foreach (Relationship rel in _doc.Package.GetRelationships(_part))
            {
                if (rel.Id == rid)
                {
                    url = rel.Target;
                    break;
                }
            }
        }

        if (anchor is not null && url is null)
        {
            url = _bookmarkSlugs.TryGetValue(anchor, out string? slug) ? "#" + slug
                : anchor.StartsWith("md_", StringComparison.Ordinal) ? "#" + anchor[3..].Replace('_', '-')
                : null;
            if (url is null)
            {
                _loss.Report("loss.markdown-link-anchor", "links to bookmarks", 1, "A link to a bookmark that is not on a heading was written as its text only.");
            }
        }
        else if (anchor is not null && url is not null)
        {
            url += "#" + anchor;
        }

        if (url is null)
        {
            Gather(h.Inlines(), depth + 1);
            return;
        }

        List<It> saved = _cur;
        _cur = [];
        Gather(h.Inlines(), depth + 1);
        List<It> inner = _cur;
        _cur = saved;
        _cur.Add(new LinkIt(url, inner));
    }

    // HYPERLINK "target" [\l "anchor"] (ECMA-376 Part 1 §17.16.5.25).
    private bool TryHyperlinkTarget(string instruction, out string? target)
    {
        target = null;
        string t = instruction.Trim();
        if (!t.StartsWith("HYPERLINK", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string rest = t[9..].Trim();
        string? url = null;
        string? local = null;
        int i = 0;
        while (i < rest.Length)
        {
            while (i < rest.Length && rest[i] == ' ')
            {
                i++;
            }

            if (i >= rest.Length)
            {
                break;
            }

            string token;
            if (rest[i] == '"')
            {
                int close = rest.IndexOf('"', i + 1);
                if (close < 0)
                {
                    close = rest.Length;
                }

                token = rest[(i + 1)..close];
                i = close + 1;
            }
            else
            {
                int end = rest.IndexOf(' ', i);
                if (end < 0)
                {
                    end = rest.Length;
                }

                token = rest[i..end];
                i = end;
            }

            if (token == "\\l")
            {
                while (i < rest.Length && rest[i] == ' ')
                {
                    i++;
                }

                if (i < rest.Length && rest[i] == '"')
                {
                    int close = rest.IndexOf('"', i + 1);
                    close = close < 0 ? rest.Length : close;
                    local = rest[(i + 1)..close];
                    i = close + 1;
                }
            }
            else if (token.StartsWith('\\'))
            {
                continue;
            }
            else
            {
                url ??= token;
            }
        }

        if (local is not null)
        {
            target = _bookmarkSlugs.TryGetValue(local, out string? slug) ? "#" + slug : null;
            return target is not null || url is not null;
        }

        target = url;
        return url is not null;
    }

    private void BeginField() => _fields.Push(new FieldState(string.Empty, _cur));

    private void EndField()
    {
        FieldState f = _fields.Pop();
        List<It> result = _cur;
        _cur = f.Saved;
        if (f.InInstruction)
        {
            // A field that never reached its result keeps nothing.
            return;
        }

        if (TryHyperlinkTarget(f.Instruction.ToString(), out string? target) && target is not null)
        {
            _cur.Add(new LinkIt(target, result));
        }
        else
        {
            _cur.AddRange(result);
        }
    }

    private void GatherRun(Run r)
    {
        ResolvedRunProperties rp = _doc.Styles.ResolveRun(r);
        bool Direct(string key) => rp.Get(key) is { IsOn: true } prop && prop.Origin.Source is PropertySource.DirectFormatting or PropertySource.CharacterStyle;
        bool bold = Direct("b") && !_headerRow;
        bool italic = Direct("i");
        bool strike = Direct("strike") || Direct("dstrike");
        string? font = rp.Fonts.Ascii;
        string? styleName = r.StyleId is null ? null : _doc.Styles.Find(r.StyleId)?.Name;
        bool code = (font is not null && IsMonospace(font)) || (styleName is not null && (styleName.Contains("code", StringComparison.OrdinalIgnoreCase) || styleName.Contains("verbatim", StringComparison.OrdinalIgnoreCase)));
        bool nonText = false;
        if (rp.Get("u") is { } u && u.Value is not (null or "none") && u.Origin.Source is PropertySource.DirectFormatting or PropertySource.CharacterStyle && r.StyleId != "Hyperlink")
        {
            nonText = true;
        }

        if (rp.Get("vertAlign") is { Value: "superscript" or "subscript" })
        {
            nonText = true;
        }

        if (nonText)
        {
            _loss.Report("loss.markdown-formatting", "character formatting", 1, "Underline, superscript, subscript, colours, highlighting and fonts have no Markdown form and were not written.");
        }

        var text = new StringBuilder();
        void FlushText()
        {
            if (text.Length > 0)
            {
                if (_fields.Count == 0 || !_fields.Peek().InInstruction)
                {
                    _cur.Add(new TextIt(text.ToString(), bold, italic, strike, code));
                }

                text.Clear();
            }
        }

        foreach (MarkupNode c in r.Markup.Node.ChildElements)
        {
            if (c.NamespaceUri is not (W or WStrict))
            {
                if (c.LocalNameString == "AlternateContent")
                {
                    FlushText();
                    Drawing(c);
                }

                continue;
            }

            switch (c.LocalNameString)
            {
                case "fldChar":
                    FlushText();
                    switch (A(c, "fldCharType"))
                    {
                        case "begin":
                            BeginField();
                            break;
                        case "separate":
                            if (_fields.Count > 0)
                            {
                                FieldState f = _fields.Peek();
                                f.InInstruction = false;
                                _cur = [];
                            }

                            break;
                        case "end":
                            if (_fields.Count > 0)
                            {
                                EndField();
                            }

                            break;
                    }

                    continue;
                case "instrText" or "delInstrText":
                    if (_fields.Count > 0 && _fields.Peek().InInstruction)
                    {
                        _fields.Peek().Instruction.Append(c.InnerText);
                    }

                    continue;
            }

            if (_fields.Count > 0 && _fields.Peek().InInstruction)
            {
                continue;
            }

            switch (c.LocalNameString)
            {
                case "t":
                    text.Append(c.InnerText);
                    break;
                case "tab" or "ptab":
                    text.Append('\t');
                    break;
                case "noBreakHyphen":
                    text.Append('-');
                    break;
                case "br":
                    if (A(c, "type") is "page" or "column")
                    {
                        // Markdown has no page or column break; a space keeps the words either side apart.
                        text.Append(' ');
                        break;
                    }

                    FlushText();
                    _cur.Add(new BreakIt());
                    break;
                case "cr":
                    FlushText();
                    _cur.Add(new BreakIt());
                    break;
                case "footnoteReference":
                    FlushText();
                    NoteReference(c, endnote: false);
                    break;
                case "endnoteReference":
                    FlushText();
                    NoteReference(c, endnote: true);
                    break;
                case "drawing" or "pict" or "object":
                    FlushText();
                    Drawing(c);
                    break;
                case "commentReference":
                    _loss.Report("loss.markdown-comment", "comments", 1, "Comments have no Markdown form and were left out.");
                    break;
            }
        }

        FlushText();
    }

    private static bool IsMonospace(string font) =>
        font.Contains("Consolas", StringComparison.OrdinalIgnoreCase) || font.Contains("Courier", StringComparison.OrdinalIgnoreCase) || font.Contains("Mono", StringComparison.OrdinalIgnoreCase)
        || font.Contains("Lucida Console", StringComparison.OrdinalIgnoreCase) || font.Contains("Menlo", StringComparison.OrdinalIgnoreCase);

    private void NoteReference(MarkupNode reference, bool endnote)
    {
        if (!long.TryParse(A(reference, "id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long id))
        {
            return;
        }

        Note? note = (endnote ? _doc.Endnotes : _doc.Footnotes).Find(id);
        if (note is null)
        {
            return;
        }

        string key = (endnote ? "e" : "f") + id.ToString(CultureInfo.InvariantCulture);
        if (!_noteLabels.TryGetValue(key, out string? label))
        {
            label = (_notes.Count + 1).ToString(CultureInfo.InvariantCulture);
            _noteLabels[key] = label;
            _notes.Add((label, note));
            if (endnote)
            {
                _loss.Report("loss.markdown-endnote", "endnotes", 1, "Endnotes were written as footnotes.");
            }
        }

        _cur.Add(new RawIt("[^" + label + "]"));
    }

    // ---- pictures ---------------------------------------------------------------------------------------------------------------

    private void Drawing(MarkupNode node)
    {
        if (node.LocalNameString == "AlternateContent")
        {
            MarkupNode pick = default;
            foreach (MarkupNode k in node.ChildElements)
            {
                if (k.LocalNameString == "Choice")
                {
                    pick = k;
                    break;
                }

                if (k.LocalNameString == "Fallback" && pick.IsNull)
                {
                    pick = k;
                }
            }

            if (!pick.IsNull)
            {
                foreach (MarkupNode k in pick.ChildElements)
                {
                    Drawing(k);
                }
            }

            return;
        }

        string? embed = null;
        string? descr = null;
        bool shape = false;
        foreach (MarkupNode d in Descendants(node))
        {
            switch (d.LocalNameString)
            {
                case "blip":
                    embed ??= RA(d, "embed");
                    break;
                case "imagedata":
                    embed ??= RA(d, "id");
                    break;
                case "docPr":
                    descr ??= d.GetAttribute("descr") ?? d.GetAttribute("title") ?? d.GetAttribute("name");
                    break;
                case "wsp" or "chart" or "txbxContent" or "dgm" or "relIds" or "oleObj":
                    shape = true;
                    break;
            }
        }

        if (embed is null || FindRelationshipTarget(embed) is not { } part)
        {
            _loss.Report("loss.markdown-drawing", "drawings", 1, "Shapes, text boxes, charts and diagrams have no Markdown form and were left out.");
            return;
        }

        _ = shape;
        string ext = Path.GetExtension(part.Name).TrimStart('.').ToLowerInvariant();
        if (ext.Length == 0)
        {
            ext = "bin";
        }

        string name = "image" + (++_imageCount).ToString(CultureInfo.InvariantCulture) + "." + ext;
        string? dest;
        if (_options.ImageSink is { } sink)
        {
            dest = sink(name, part.ContentType, part.GetBytes());
            if (string.IsNullOrEmpty(dest))
            {
                _loss.Report("loss.markdown-image", "pictures", 1, "A picture the image sink declined was left out.");
                return;
            }
        }
        else
        {
            dest = "media/" + name;
            _loss.Report("loss.markdown-image-bytes", "picture files", 1, "Pictures are written as links to media/imageN.ext; their bytes are not written unless an image sink is given.");
        }

        string alt = MarkdownWriting.EscapeText((descr ?? string.Empty).Replace('\n', ' '), false, false);
        _cur.Add(new RawIt("![" + alt + "](" + FormatDestination(dest) + ")"));
    }

    private IPackagePart? FindRelationshipTarget(string relId)
    {
        foreach (Relationship r in _doc.Package.GetRelationships(_part))
        {
            if (r.Id == relId && r.TargetMode == RelationshipTargetMode.Internal && PartUri.TryResolve(new PartUri(_part), r.Target, out PartUri uri))
            {
                return _doc.Package.FindPart(uri.Value);
            }
        }

        return null;
    }

    // ---- emitting ---------------------------------------------------------------------------------------------------------------

    private static bool IsMarkdownSpace(char c) => c is ' ' or '\t' or '\u00A0';

    private static string Marker(char kind) => kind switch { '~' => "~~", 'b' => "**", _ => "*" };

    private static void EmitItems(List<It> items, StringBuilder sb, ref bool lineStart, bool inTable, bool heading)
    {
        // Adjacent text with the same formatting is one stretch, so that emphasis markers are written once.
        var merged = new List<It>();
        foreach (It it in items)
        {
            if (it is TextIt t && merged.Count > 0 && merged[^1] is TextIt prev && prev.SameFormat(t))
            {
                merged[^1] = new TextIt(prev.Text + t.Text, t.Bold, t.Italic, t.Strike, t.Code);
            }
            else
            {
                merged.Add(it);
            }
        }

        var active = new List<char>();
        string pendingSpace = string.Empty;

        void CloseTo(int keep)
        {
            for (int i = active.Count - 1; i >= keep; i--)
            {
                sb.Append(Marker(active[i]));
            }

            active.RemoveRange(keep, active.Count - keep);
        }

        foreach (It it in merged)
        {
            switch (it)
            {
                case TextIt t:
                    {
                        string text = t.Text;
                        var wanted = new List<char>(3);
                        if (t.Strike)
                        {
                            wanted.Add('~');
                        }

                        if (t.Bold)
                        {
                            wanted.Add('b');
                        }

                        if (t.Italic)
                        {
                            wanted.Add('i');
                        }

                        int lead = 0;
                        while (!t.Code && lead < text.Length && IsMarkdownSpace(text[lead]))
                        {
                            lead++;
                        }

                        if (lead == text.Length)
                        {
                            pendingSpace += text.Replace('\t', ' ');
                            break;
                        }

                        int end = text.Length;
                        while (!t.Code && IsMarkdownSpace(text[end - 1]))
                        {
                            end--;
                        }

                        string core = text[lead..end];
                        string trail = end < text.Length ? text[end..].Replace('\t', ' ') : string.Empty;
                        string leadSpace = lead > 0 ? text[..lead].Replace('\t', ' ') : string.Empty;

                        int common = 0;
                        while (common < active.Count && common < wanted.Count && active[common] == wanted[common])
                        {
                            common++;
                        }

                        CloseTo(common);
                        sb.Append(pendingSpace).Append(leadSpace);
                        for (int i = common; i < wanted.Count; i++)
                        {
                            sb.Append(Marker(wanted[i]));
                            active.Add(wanted[i]);
                        }

                        if (t.Code)
                        {
                            sb.Append(CodeSpan(text));
                            lineStart = false;
                        }
                        else
                        {
                            sb.Append(MarkdownWriting.EscapeText(core, inTable, lineStart && AtLineStart(sb)));
                            lineStart = false;
                        }

                        pendingSpace = trail;
                        break;
                    }

                case BreakIt:
                    CloseTo(0);
                    pendingSpace = string.Empty;
                    sb.Append(inTable ? "<br>" : heading ? " " : "\\\n");
                    lineStart = true;
                    break;
                case RawIt raw:
                    CloseTo(0);
                    sb.Append(pendingSpace).Append(raw.Markdown);
                    pendingSpace = string.Empty;
                    lineStart = false;
                    break;
                case LinkIt link:
                    {
                        CloseTo(0);
                        var inner = new StringBuilder();
                        bool innerStart = false;
                        EmitItems(link.Inner, inner, ref innerStart, inTable, heading);
                        string label = inner.ToString().Trim(' ');
                        if (label.Length == 0)
                        {
                            break;
                        }

                        sb.Append(pendingSpace);
                        if (sb.Length > 0 && sb[^1] == '!' && !(sb.Length > 1 && sb[^2] == '\\'))
                        {
                            // "!" before a link would make it a picture.
                            sb.Insert(sb.Length - 1, '\\');
                        }

                        if (link.Inner.Count == 1 && link.Inner[0] is TextIt only && only.Text == link.Url && !only.Bold && !only.Italic && !only.Strike && !only.Code && IsAbsoluteUri(link.Url))
                        {
                            // The text is the address: an autolink says that in one piece.
                            sb.Append('<').Append(link.Url).Append('>');
                        }
                        else
                        {
                            sb.Append('[').Append(label).Append("](").Append(FormatDestination(link.Url)).Append(')');
                        }
                        pendingSpace = string.Empty;
                        lineStart = false;
                        break;
                    }
            }
        }

        CloseTo(0);
        sb.Append(pendingSpace);
    }

    // A scheme of two or more characters, a colon, and nothing a CommonMark autolink cannot hold (CommonMark 0.31.2 §6.5).
    private static bool IsAbsoluteUri(string url)
    {
        int colon = url.IndexOf(':', StringComparison.Ordinal);
        if (colon < 2 || colon > 32 || !char.IsAsciiLetter(url[0]))
        {
            return false;
        }

        for (int i = 0; i < colon; i++)
        {
            if (!(char.IsAsciiLetterOrDigit(url[i]) || url[i] is '+' or '.' or '-'))
            {
                return false;
            }
        }

        return url.AsSpan(colon + 1).IndexOfAnyInRange('\0', ' ') < 0 && url.AsSpan().IndexOfAny('<', '>') < 0;
    }

    private static bool AtLineStart(StringBuilder sb) => sb.Length == 0 || sb[^1] == '\n';

    private static string CodeSpan(string text)
    {
        text = text.Replace('\n', ' ');
        int longest = 0;
        int run = 0;
        foreach (char ch in text)
        {
            run = ch == '`' ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }

        string fence = new('`', longest + 1);
        bool pad = text.StartsWith('`') || text.EndsWith('`') || (text.StartsWith(' ') && text.EndsWith(' ') && text.Trim().Length > 0);
        return fence + (pad ? " " : string.Empty) + text + (pad ? " " : string.Empty) + fence;
    }

    // A destination with a space is written in angle brackets; any other one plainly, with parentheses and backslashes escaped (CommonMark 0.31.2 §6.3).
    private static string FormatDestination(string url)
    {
        url = url.Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", string.Empty, StringComparison.Ordinal).Replace("\t", "%09", StringComparison.Ordinal);
        var sb = new StringBuilder(url.Length + 2);
        if (url.Contains(' ', StringComparison.Ordinal))
        {
            sb.Append('<');
            foreach (char ch in url)
            {
                if (ch is '<' or '>' or '\\')
                {
                    sb.Append('\\');
                }

                sb.Append(ch);
            }

            return sb.Append('>').ToString();
        }

        foreach (char ch in url)
        {
            if (ch is '(' or ')' or '\\' or '<')
            {
                sb.Append('\\');
            }

            sb.Append(ch);
        }

        return sb.ToString();
    }
}
