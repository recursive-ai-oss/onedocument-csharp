using System.Text;

namespace RecursiveAI.OneDocument.Documents.Markdown;

/// <summary>A link reference definition (§4.7).</summary>
internal readonly record struct MdReference(string Destination, string Title);

/// <summary>
/// The inline parser: code spans, emphasis and strong emphasis by the delimiter-run algorithm of §6.2 (with the openers-bottom bounds that
/// keep it linear), links and images (§6.3, §6.4), autolinks, raw HTML, character references, line breaks, and the GitHub extensions
/// (strike-through, extended autolinks, footnote references). Link destinations and titles are scanned once, forward.
/// </summary>
internal sealed class MdInlineParser
{
    private sealed class Delimiter
    {
        public char Char;
        public int Count;
        public int OriginalCount;
        public MdNode Node = null!;
        public Delimiter? Previous;
        public Delimiter? Next;
        public bool CanOpen;
        public bool CanClose;
    }

    private sealed class Bracket
    {
        public MdNode Node = null!;
        public Bracket? Previous;
        public Delimiter? PreviousDelimiter;
        public int Index;
        public bool Image;
        public bool Active = true;
        public bool BracketAfter;
        public int Depth;
    }

    private readonly Dictionary<string, MdReference> _references;
    private readonly HashSet<string>? _footnotes;
    private readonly MarkdownParserOptions _options;
    private readonly MdScan.HtmlMemo _htmlMemo = new();
    private Dictionary<int, int>? _backtickFailedFrom;
    private string _subject = string.Empty;
    private int _pos;
    private Delimiter? _delimiters;
    private Bracket? _brackets;
    private int _deactivatedDepth;

    public MdInlineParser(Dictionary<string, MdReference> references, HashSet<string>? footnotes, MarkdownParserOptions options)
    {
        _references = references;
        _footnotes = footnotes;
        _options = options;
    }

    /// <summary>Parses the inline content of a block (a paragraph, heading or table cell) into children of <paramref name="block"/>.</summary>
    public void Parse(MdNode block, string content)
    {
        _subject = content;
        _pos = 0;
        _delimiters = null;
        _brackets = null;
        _deactivatedDepth = 0;
        _backtickFailedFrom = null;
        _htmlMemo.NoCommentClose = _htmlMemo.NoProcessingClose = _htmlMemo.NoCdataClose = _htmlMemo.NoDeclarationClose = int.MaxValue;
        while (_pos < _subject.Length && ParseInline(block))
        {
        }

        ProcessEmphasis(null);
        MdAutolink.MergeText(block);
        if (_options.Autolinks)
        {
            MdAutolink.Apply(block);
        }
    }

    private char Peek() => _pos < _subject.Length ? _subject[_pos] : '\0';

    private bool AtEnd => _pos >= _subject.Length;

    private bool ParseInline(MdNode block)
    {
        char c = _subject[_pos];
        switch (c)
        {
            case '\n':
                ParseNewline(block);
                return true;
            case '\\':
                ParseBackslash(block);
                return true;
            case '`':
                ParseBackticks(block);
                return true;
            case '*' or '_':
                HandleDelimiter(c, block);
                return true;
            case '~' when _options.Strikethrough:
                HandleDelimiter(c, block);
                return true;
            case '[':
                ParseOpenBracket(block);
                return true;
            case '!':
                ParseBang(block);
                return true;
            case ']':
                ParseCloseBracket(block);
                return true;
            case '<':
                if (!ParseAutolink(block) && !ParseHtmlTag(block))
                {
                    _pos++;
                    AppendText(block, "<");
                }

                return true;
            case '&':
                ParseEntity(block);
                return true;
            default:
                ParseString(block);
                return true;
        }
    }

    private static void AppendText(MdNode block, string text) => block.AppendChild(new MdNode(MdKind.Text) { Literal = text });

    private bool IsSpecial(char c) =>
        c is '\n' or '`' or '[' or ']' or '\\' or '!' or '<' or '&' or '*' or '_' || (c == '~' && _options.Strikethrough);

    private void ParseString(MdNode block)
    {
        int start = _pos;
        _pos++;
        while (_pos < _subject.Length && !IsSpecial(_subject[_pos]))
        {
            _pos++;
        }

        AppendText(block, _subject.Substring(start, _pos - start));
    }

    private void ParseNewline(MdNode block)
    {
        _pos++;
        MdNode? last = block.LastChild;
        if (last is { Kind: MdKind.Text } && last.Literal.Length > 0 && last.Literal[^1] == ' ')
        {
            bool hard = last.Literal.Length > 1 && last.Literal[^2] == ' ';
            last.Literal = last.Literal.TrimEnd(' ');
            block.AppendChild(new MdNode(hard ? MdKind.LineBreak : MdKind.SoftBreak));
        }
        else
        {
            block.AppendChild(new MdNode(MdKind.SoftBreak));
        }

        while (_pos < _subject.Length && _subject[_pos] == ' ')
        {
            _pos++;
        }
    }

    private void ParseBackslash(MdNode block)
    {
        _pos++;
        if (Peek() == '\n' && !AtEnd)
        {
            block.AppendChild(new MdNode(MdKind.LineBreak));
            _pos++;
            while (_pos < _subject.Length && _subject[_pos] == ' ')
            {
                _pos++;
            }
        }
        else if (!AtEnd && MdScan.IsAsciiPunctuation(_subject[_pos]))
        {
            AppendText(block, _subject[_pos].ToString());
            _pos++;
        }
        else
        {
            AppendText(block, "\\");
        }
    }

    private void ParseBackticks(MdNode block)
    {
        int start = _pos;
        int ticks = 0;
        while (_pos < _subject.Length && _subject[_pos] == '`')
        {
            ticks++;
            _pos++;
        }

        int afterOpen = _pos;
        // A search for a closer of this length that already failed from an earlier position fails from here too: do not scan again.
        if (_backtickFailedFrom is null || !_backtickFailedFrom.TryGetValue(ticks, out int failed) || afterOpen < failed)
        {
            int i = afterOpen;
            while (i < _subject.Length)
            {
                int j = _subject.IndexOf('`', i);
                if (j < 0)
                {
                    break;
                }

                int k = j;
                while (k < _subject.Length && _subject[k] == '`')
                {
                    k++;
                }

                if (k - j == ticks)
                {
                    string contents = _subject.Substring(afterOpen, j - afterOpen).Replace('\n', ' ');
                    // §6.1: one space is stripped from each side when both sides have one and the contents are not all spaces.
                    if (contents.Length > 2 && contents[0] == ' ' && contents[^1] == ' ' && contents.AsSpan().IndexOfAnyExcept(' ') >= 0)
                    {
                        contents = contents[1..^1];
                    }

                    block.AppendChild(new MdNode(MdKind.Code) { Literal = contents });
                    _pos = k;
                    return;
                }

                i = k;
            }

            (_backtickFailedFrom ??= [])[ticks] = afterOpen;
        }

        _pos = afterOpen;
        AppendText(block, _subject.Substring(start, ticks));
    }

    // ---- emphasis ------------------------------------------------------------------------------------------------------------

    private void HandleDelimiter(char c, MdNode block)
    {
        int start = _pos;
        int count = 0;
        while (_pos < _subject.Length && _subject[_pos] == c)
        {
            count++;
            _pos++;
        }

        int before = start == 0 ? -1 : MdScan.CodePointStartBefore(_subject, start);
        bool afterWhitespace = _pos >= _subject.Length || MdScan.IsUnicodeWhitespace(_subject[_pos]);
        bool afterPunct = !afterWhitespace && MdScan.IsPunctuation(_subject, _pos);
        bool beforeWhitespace = before < 0 || MdScan.IsUnicodeWhitespace(_subject[before]);
        bool beforePunct = !beforeWhitespace && MdScan.IsPunctuation(_subject, before);

        // §6.2: left- and right-flanking delimiter runs.
        bool leftFlanking = !afterWhitespace && (!afterPunct || beforeWhitespace || beforePunct);
        bool rightFlanking = !beforeWhitespace && (!beforePunct || afterWhitespace || afterPunct);
        bool canOpen;
        bool canClose;
        if (c == '_')
        {
            canOpen = leftFlanking && (!rightFlanking || beforePunct);
            canClose = rightFlanking && (!leftFlanking || afterPunct);
        }
        else
        {
            canOpen = leftFlanking;
            canClose = rightFlanking;
        }

        MdNode node = new(MdKind.Text) { Literal = _subject.Substring(start, count) };
        block.AppendChild(node);
        // GFM strike-through: one or two tildes; a longer run is plain text.
        if (c == '~' && count > 2)
        {
            return;
        }

        if (canOpen || canClose)
        {
            var d = new Delimiter { Char = c, Count = count, OriginalCount = count, Node = node, Previous = _delimiters, CanOpen = canOpen, CanClose = canClose };
            if (_delimiters is not null)
            {
                _delimiters.Next = d;
            }

            _delimiters = d;
        }
    }

    private void RemoveDelimiter(Delimiter d)
    {
        if (d.Previous is not null)
        {
            d.Previous.Next = d.Next;
        }

        if (d.Next is null)
        {
            _delimiters = d.Previous;
        }
        else
        {
            d.Next.Previous = d.Previous;
        }
    }

    private void ProcessEmphasis(Delimiter? stackBottom)
    {
        // The lowest delimiter worth searching for an opener, per closer kind: the length mod 3 and whether the closer can also open (§6.2 rule of 3).
        var openersBottom = new Delimiter?[3 * 2 * 3 + 2];
        for (int i = 0; i < openersBottom.Length; i++)
        {
            openersBottom[i] = stackBottom;
        }

        Delimiter? closer = _delimiters;
        while (closer is not null && closer.Previous != stackBottom)
        {
            closer = closer.Previous;
        }

        while (closer is not null)
        {
            if (!closer.CanClose)
            {
                closer = closer.Next;
                continue;
            }

            int index = closer.Char switch
            {
                '_' => (closer.CanOpen ? 3 : 0) + (closer.OriginalCount % 3),
                '*' => 6 + (closer.CanOpen ? 3 : 0) + (closer.OriginalCount % 3),
                _ => 12 + closer.OriginalCount,
            };

            Delimiter? opener = closer.Previous;
            bool found = false;
            while (opener is not null && opener != stackBottom && opener != openersBottom[index])
            {
                if (opener.Char == closer.Char && opener.CanOpen)
                {
                    if (closer.Char == '~')
                    {
                        found = opener.Count == closer.Count;
                    }
                    else
                    {
                        // §6.2 rule 9/10: if one delimiter can both open and close, the sum of the run lengths must not be a multiple of 3 unless both are.
                        bool oddMatch = (closer.CanOpen || opener.CanClose) && closer.OriginalCount % 3 != 0 && (opener.OriginalCount + closer.OriginalCount) % 3 == 0;
                        found = !oddMatch;
                    }

                    if (found)
                    {
                        break;
                    }
                }

                opener = opener.Previous;
            }

            Delimiter oldCloser = closer;
            if (!found)
            {
                closer = closer.Next;
            }
            else
            {
                int use = closer.Char == '~' ? closer.Count : (closer.Count >= 2 && opener!.Count >= 2 ? 2 : 1);
                MdNode openerNode = opener!.Node;
                MdNode closerNode = closer.Node;
                opener.Count -= use;
                closer.Count -= use;
                openerNode.Literal = openerNode.Literal[..^use];
                closerNode.Literal = closerNode.Literal[..^use];
                MdNode wrap = new(closer.Char == '~' ? MdKind.Strike : use == 1 ? MdKind.Emph : MdKind.Strong);
                MdNode? tmp = openerNode.Next;
                while (tmp is not null && tmp != closerNode)
                {
                    MdNode? next = tmp.Next;
                    wrap.AppendChild(tmp);
                    tmp = next;
                }

                openerNode.InsertAfter(wrap);
                // Delimiters between the two are dead.
                Delimiter? between = closer.Previous;
                while (between is not null && between != opener)
                {
                    Delimiter? p = between.Previous;
                    RemoveDelimiter(between);
                    between = p;
                }

                if (opener.Count == 0)
                {
                    openerNode.Unlink();
                    RemoveDelimiter(opener);
                }

                if (closer.Count == 0)
                {
                    closerNode.Unlink();
                    Delimiter? next = closer.Next;
                    RemoveDelimiter(closer);
                    closer = next;
                }
            }

            if (!found)
            {
                openersBottom[index] = oldCloser.Previous;
                if (!oldCloser.CanOpen)
                {
                    RemoveDelimiter(oldCloser);
                }
            }
        }

        while (_delimiters is not null && _delimiters != stackBottom)
        {
            RemoveDelimiter(_delimiters);
        }
    }

    // ---- links, images, footnote references -----------------------------------------------------------------------------------

    private void AddBracket(MdNode node, int index, bool image)
    {
        if (_brackets is not null)
        {
            _brackets.BracketAfter = true;
        }

        _brackets = new Bracket
        {
            Node = node,
            Previous = _brackets,
            PreviousDelimiter = _delimiters,
            Index = index,
            Image = image,
            Depth = (_brackets?.Depth ?? 0) + 1,
        };
    }

    private void RemoveBracket()
    {
        _brackets = _brackets!.Previous;
        int depth = _brackets?.Depth ?? 0;
        if (_deactivatedDepth > depth)
        {
            _deactivatedDepth = depth;
        }
    }

    private void ParseOpenBracket(MdNode block)
    {
        int start = _pos;
        _pos++;
        MdNode node = new(MdKind.Text) { Literal = "[" };
        block.AppendChild(node);
        AddBracket(node, start, false);
    }

    private void ParseBang(MdNode block)
    {
        int start = _pos;
        _pos++;
        if (Peek() == '[')
        {
            _pos++;
            MdNode node = new(MdKind.Text) { Literal = "![" };
            block.AppendChild(node);
            AddBracket(node, start + 1, true);
        }
        else
        {
            AppendText(block, "!");
        }
    }

    private void ParseCloseBracket(MdNode block)
    {
        _pos++;
        int startPos = _pos;
        Bracket? opener = _brackets;
        if (opener is null)
        {
            AppendText(block, "]");
            return;
        }

        if (!opener.Active)
        {
            AppendText(block, "]");
            RemoveBracket();
            return;
        }

        bool isImage = opener.Image;
        string? dest = null;
        string? title = null;
        bool matched = false;

        // GitHub footnote reference: [^label] where a definition with that label exists.
        if (_footnotes is not null && !isImage && opener.Index + 1 < _subject.Length && _subject[opener.Index + 1] == '^' && startPos - 1 > opener.Index + 2)
        {
            string label = _subject.Substring(opener.Index + 2, startPos - 1 - (opener.Index + 2));
            if (label.AsSpan().IndexOfAny(" \t\n") < 0 && _footnotes.Contains(MdText.NormalizeLabel(label)))
            {
                var reference = new MdNode(MdKind.FootnoteReference) { Label = label };
                MdNode? t = opener.Node.Next;
                while (t is not null)
                {
                    MdNode? n = t.Next;
                    t.Unlink();
                    t = n;
                }

                block.AppendChild(reference);
                ProcessEmphasis(opener.PreviousDelimiter);
                RemoveBracket();
                opener.Node.Unlink();
                return;
            }
        }

        // Inline link: ( destination title ).
        if (Peek() == '(')
        {
            int save = _pos;
            _pos++;
            SkipSpaceNewline();
            string? d = ParseLinkDestination();
            if (d is not null)
            {
                SkipSpaceNewline();
                string? tt = null;
                if (_pos > 0 && _subject[_pos - 1] is ' ' or '\t' or '\n' or '\r' or '\f' or '\v')
                {
                    tt = ParseLinkTitle();
                }

                SkipSpaceNewline();
                if (Peek() == ')')
                {
                    _pos++;
                    dest = d;
                    title = tt;
                    matched = true;
                }
            }

            if (!matched)
            {
                _pos = save;
            }
        }

        if (!matched)
        {
            // Reference link: [text][label], [text][] or [text].
            int beforeLabel = _pos;
            int n = ParseLinkLabel();
            string? refLabel = null;
            if (n > 2)
            {
                refLabel = _subject.Substring(beforeLabel, n);
            }
            else if (!opener.BracketAfter)
            {
                refLabel = _subject.Substring(opener.Index, startPos - opener.Index);
            }

            if (n == 0)
            {
                _pos = startPos;
            }

            if (refLabel is not null && refLabel.Length > 2 && _references.TryGetValue(MdText.NormalizeLabel(refLabel[1..^1]), out MdReference r))
            {
                dest = r.Destination;
                title = r.Title;
                matched = true;
            }
        }

        if (matched)
        {
            var node = new MdNode(isImage ? MdKind.Image : MdKind.Link) { Destination = dest ?? string.Empty, Title = title ?? string.Empty };
            MdNode? tmp = opener.Node.Next;
            while (tmp is not null)
            {
                MdNode? next = tmp.Next;
                node.AppendChild(tmp);
                tmp = next;
            }

            block.AppendChild(node);
            ProcessEmphasis(opener.PreviousDelimiter);
            RemoveBracket();
            opener.Node.Unlink();
            if (!isImage)
            {
                // §6.3: links may not contain other links, so every earlier link opener goes inactive. Only the part of the stack not already
                // deactivated is walked, so a text of many links does not walk the whole stack each time.
                Bracket? b = _brackets;
                while (b is not null && b.Depth > _deactivatedDepth)
                {
                    if (!b.Image)
                    {
                        b.Active = false;
                    }

                    b = b.Previous;
                }

                _deactivatedDepth = _brackets?.Depth ?? 0;
            }
        }
        else
        {
            RemoveBracket();
            _pos = startPos;
            AppendText(block, "]");
        }
    }

    private void SkipSpaceNewline()
    {
        while (_pos < _subject.Length && _subject[_pos] == ' ')
        {
            _pos++;
        }

        if (_pos < _subject.Length && _subject[_pos] == '\n')
        {
            _pos++;
            while (_pos < _subject.Length && _subject[_pos] == ' ')
            {
                _pos++;
            }
        }
    }

    // §6.3: a link label is up to 999 characters between brackets, with no unescaped brackets. Returns the length with brackets, or 0.
    private int ParseLinkLabel()
    {
        if (Peek() != '[')
        {
            return 0;
        }

        int i = _pos + 1;
        int limit = Math.Min(_subject.Length, _pos + 1001);
        while (i < limit)
        {
            char c = _subject[i];
            if (c == '\\' && i + 1 < _subject.Length)
            {
                i += 2;
                continue;
            }

            if (c == '[')
            {
                return 0;
            }

            if (c == ']')
            {
                if (i - _pos - 1 > 999)
                {
                    return 0;
                }

                int len = i + 1 - _pos;
                _pos = i + 1;
                return len;
            }

            i++;
        }

        return 0;
    }

    // §6.3: an inline link destination is <...> without line breaks or unescaped '<' '>', or a run with no spaces or control characters and balanced parentheses.
    internal string? ParseLinkDestination()
    {
        if (Peek() == '<')
        {
            int i = _pos + 1;
            while (i < _subject.Length)
            {
                char c = _subject[i];
                if (c == '\\' && i + 1 < _subject.Length && MdScan.IsAsciiPunctuation(_subject[i + 1]))
                {
                    i += 2;
                    continue;
                }

                if (c is '\n' or '<' or '\0')
                {
                    return null;
                }

                if (c == '>')
                {
                    string raw = _subject.Substring(_pos + 1, i - _pos - 1);
                    _pos = i + 1;
                    return MdText.Unescape(raw);
                }

                i++;
            }

            return null;
        }

        int start = _pos;
        int open = 0;
        while (_pos < _subject.Length)
        {
            char c = _subject[_pos];
            if (c == '\\' && _pos + 1 < _subject.Length && MdScan.IsAsciiPunctuation(_subject[_pos + 1]))
            {
                _pos += 2;
            }
            else if (c == '(')
            {
                // Implementations may limit nesting; 32 is the usual bound, and it keeps a failed scan short.
                if (++open > 32)
                {
                    _pos = start;
                    return null;
                }

                _pos++;
            }
            else if (c == ')')
            {
                if (open < 1)
                {
                    break;
                }

                open--;
                _pos++;
            }
            else if (c <= ' ' || c == '\u007F')
            {
                break;
            }
            else
            {
                _pos++;
            }
        }

        if (open != 0 || (_pos == start && Peek() != ')'))
        {
            _pos = start;
            return null;
        }

        return MdText.Unescape(_subject.Substring(start, _pos - start));
    }

    // §6.3: a title is "...", '...' or (...), with backslash escapes; it may span lines.
    internal string? ParseLinkTitle()
    {
        char open = Peek();
        if (open is not ('"' or '\'' or '('))
        {
            return null;
        }

        char close = open == '(' ? ')' : open;
        int i = _pos + 1;
        while (i < _subject.Length)
        {
            char c = _subject[i];
            if (c == '\\' && i + 1 < _subject.Length && MdScan.IsAsciiPunctuation(_subject[i + 1]))
            {
                i += 2;
                continue;
            }

            if (c == close)
            {
                string raw = _subject.Substring(_pos + 1, i - _pos - 1);
                _pos = i + 1;
                return MdText.Unescape(raw);
            }

            if (open == '(' && c == '(')
            {
                return null;
            }

            i++;
        }

        return null;
    }

    /// <summary>Reads a link reference definition at <paramref name="start"/> of a paragraph's text; returns the position after it, or -1.</summary>
    internal int ParseReference(string text, int start)
    {
        _subject = text;
        _pos = start;
        int labelStart = _pos;
        int n = ParseLinkLabel();
        if (n == 0)
        {
            return -1;
        }

        string rawLabel = text.Substring(labelStart, n);
        if (Peek() != ':')
        {
            return -1;
        }

        _pos++;
        SkipSpaceNewline();
        string? dest = ParseLinkDestination();
        if (dest is null)
        {
            return -1;
        }

        int beforeTitle = _pos;
        SkipSpaceNewline();
        string? title = null;
        if (_pos != beforeTitle)
        {
            title = ParseLinkTitle();
        }

        if (title is null)
        {
            title = string.Empty;
            _pos = beforeTitle;
        }

        bool atLineEnd = true;
        if (!SpacesToLineEnd())
        {
            if (title.Length == 0)
            {
                atLineEnd = false;
            }
            else
            {
                title = string.Empty;
                _pos = beforeTitle;
                atLineEnd = SpacesToLineEnd();
            }
        }

        if (!atLineEnd)
        {
            return -1;
        }

        string label = MdText.NormalizeLabel(rawLabel[1..^1]);
        if (label.Length == 0)
        {
            return -1;
        }

        _references.TryAdd(label, new MdReference(dest, title));
        return _pos;
    }

    private bool SpacesToLineEnd()
    {
        int i = _pos;
        while (i < _subject.Length && _subject[i] is ' ' or '\t')
        {
            i++;
        }

        if (i >= _subject.Length)
        {
            _pos = i;
            return true;
        }

        if (_subject[i] == '\n')
        {
            _pos = i + 1;
            return true;
        }

        return false;
    }

    // ---- autolinks, raw HTML, entities ----------------------------------------------------------------------------------------

    private bool ParseAutolink(MdNode block)
    {
        // The text of an autolink holds no white space and no '<', so the scan stops at the first of them; that keeps a text of many '<' linear.
        int gt = _pos + 1;
        while (gt < _subject.Length && _subject[gt] > ' ' && _subject[gt] != '<' && _subject[gt] != '>')
        {
            gt++;
        }

        if (gt >= _subject.Length || _subject[gt] != '>')
        {
            return false;
        }

        ReadOnlySpan<char> inner = _subject.AsSpan(_pos + 1, gt - _pos - 1);
        // §6.5: an absolute URI is a scheme (2 to 32 characters), a colon, then anything but spaces, '<' and '>'.
        int colon = inner.IndexOf(':');
        if (colon >= 2 && colon <= 32 && char.IsAsciiLetter(inner[0]))
        {
            bool ok = true;
            for (int i = 0; i < colon && ok; i++)
            {
                ok = char.IsAsciiLetterOrDigit(inner[i]) || inner[i] is '+' or '.' or '-';
            }

            for (int i = colon + 1; i < inner.Length && ok; i++)
            {
                ok = inner[i] > ' ' && inner[i] != '<';
            }

            if (ok)
            {
                string uri = inner.ToString();
                var link = new MdNode(MdKind.Link) { Destination = uri };
                link.AppendChild(new MdNode(MdKind.Text) { Literal = uri });
                block.AppendChild(link);
                _pos = gt + 1;
                return true;
            }
        }

        // An email address: local part, '@', then labels of letters, digits and hyphens.
        int at = inner.IndexOf('@');
        if (at > 0 && IsEmail(inner, at))
        {
            string email = inner.ToString();
            var link = new MdNode(MdKind.Link) { Destination = "mailto:" + email };
            link.AppendChild(new MdNode(MdKind.Text) { Literal = email });
            block.AppendChild(link);
            _pos = gt + 1;
            return true;
        }

        return false;
    }

    private static bool IsEmail(ReadOnlySpan<char> s, int at)
    {
        for (int i = 0; i < at; i++)
        {
            if (!(char.IsAsciiLetterOrDigit(s[i]) || ".!#$%&'*+/=?^_`{|}~-".Contains(s[i], StringComparison.Ordinal)))
            {
                return false;
            }
        }

        ReadOnlySpan<char> domain = s[(at + 1)..];
        if (domain.Length == 0)
        {
            return false;
        }

        int labelStart = 0;
        for (int i = 0; i <= domain.Length; i++)
        {
            if (i == domain.Length || domain[i] == '.')
            {
                int len = i - labelStart;
                if (len < 1 || len > 63 || domain[labelStart] == '-' || domain[i - 1] == '-')
                {
                    return false;
                }

                labelStart = i + 1;
                continue;
            }

            if (!(char.IsAsciiLetterOrDigit(domain[i]) || domain[i] == '-'))
            {
                return false;
            }
        }

        return true;
    }

    private bool ParseHtmlTag(MdNode block)
    {
        if (!_options.RawHtml)
        {
            return false;
        }

        if (MdScan.TryScanHtmlTag(_subject, _pos, _subject.Length, _htmlMemo, out int end))
        {
            block.AppendChild(new MdNode(MdKind.HtmlInline) { Literal = _subject.Substring(_pos, end - _pos) });
            _pos = end;
            return true;
        }

        return false;
    }

    private void ParseEntity(MdNode block)
    {
        if (MdText.TryEntity(_subject, _pos, out string value, out int end))
        {
            AppendText(block, value);
            _pos = end;
        }
        else
        {
            _pos++;
            AppendText(block, "&");
        }
    }
}
