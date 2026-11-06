using System.Text;

namespace RecursiveAI.OneDocument.Documents.Markdown;

/// <summary>Which Markdown the parser reads: CommonMark 0.31.2, with the GitHub Flavored Markdown extensions on or off.</summary>
internal sealed class MarkdownParserOptions
{
    /// <summary>Plain CommonMark.</summary>
    public static MarkdownParserOptions CommonMark { get; } = new() { Tables = false, Strikethrough = false, TaskLists = false, Autolinks = false, Footnotes = false };

    /// <summary>CommonMark plus tables, strike-through, task lists, extended autolinks and footnotes.</summary>
    public static MarkdownParserOptions Gfm { get; } = new();

    /// <summary>GFM tables (pipe tables with a delimiter row).</summary>
    public bool Tables { get; init; } = true;

    /// <summary>GFM strike-through with one or two tildes.</summary>
    public bool Strikethrough { get; init; } = true;

    /// <summary>GFM task list items (<c>- [ ]</c> and <c>- [x]</c>).</summary>
    public bool TaskLists { get; init; } = true;

    /// <summary>GFM extended autolinks (www., http://, https://, ftp://, e-mail).</summary>
    public bool Autolinks { get; init; } = true;

    /// <summary>Footnote definitions and references (<c>[^1]</c>).</summary>
    public bool Footnotes { get; init; } = true;

    /// <summary>Whether raw HTML blocks and inlines are recognised (they are always carried verbatim in the tree).</summary>
    public bool RawHtml { get; init; } = true;

    /// <summary>The deepest nesting of block containers (quotes, list items); beyond it, container markers are read as text. At least 8.</summary>
    public int MaxDepth { get; init; } = 256;
}

/// <summary>What <see cref="MarkdownParser.Parse"/> returns: the tree and what the parser had to repair.</summary>
internal sealed class MarkdownParseResult
{
    public required MdNode Root { get; init; }

    /// <summary>Things the parser did not follow, as (code, message, 1-based line).</summary>
    public List<(string Code, string Message, int Line)> Issues { get; } = [];
}

/// <summary>
/// The block parser: CommonMark 0.31.2 §3 and §5, line by line, with each line meeting the open containers first and then the block
/// starts, as the spec's appendix describes. Work per line is bounded by the nesting limit, so the whole parse is linear in the input.
/// </summary>
internal sealed class MarkdownParser
{
    private const int CodeIndent = 4;

    private readonly MarkdownParserOptions _options;
    private readonly MarkdownParseResult _result;
    private readonly Dictionary<string, MdReference> _references = [];
    private readonly HashSet<string> _footnoteLabels = [];
    private readonly MdNode _doc;
    private MdNode _tip;
    private MdNode _oldTip;
    private MdNode _lastMatched;
    private bool _allClosed = true;
    private string _line = string.Empty;
    private int _lineNumber;
    private int _offset;
    private int _column;
    private int _nextNonspace;
    private int _nextNonspaceColumn;
    private int _indent;
    private bool _indented;
    private bool _blank;
    private bool _partiallyConsumedTab;
    private bool _depthReported;
    private bool _skipRow;

    private MarkdownParser(MarkdownParserOptions options)
    {
        _options = options;
        _doc = new MdNode(MdKind.Document) { StartLine = 1 };
        _tip = _doc;
        _oldTip = _doc;
        _lastMatched = _doc;
        _result = new MarkdownParseResult { Root = _doc };
    }

    /// <summary>Parses Markdown text into a tree.</summary>
    /// <param name="text">The text; line ends may be \n, \r\n or \r.</param>
    /// <param name="options">The dialect.</param>
    /// <returns>The tree and the issues found.</returns>
    public static MarkdownParseResult Parse(string text, MarkdownParserOptions options)
    {
        var p = new MarkdownParser(options);
        p.Run(text);
        return p._result;
    }

    private void Run(string text)
    {
        int i = 0;
        int n = text.Length;
        while (i < n)
        {
            int j = i;
            while (j < n && text[j] != '\n' && text[j] != '\r')
            {
                j++;
            }

            IncorporateLine(text.Substring(i, j - i).Replace('\0', '\uFFFD'));
            if (j < n && text[j] == '\r' && j + 1 < n && text[j + 1] == '\n')
            {
                j++;
            }

            i = j + 1;
        }

        while (_tip.Parent is not null || _tip != _doc)
        {
            Finalize(_tip);
        }

        Finalize(_doc);
        ProcessInlines();
    }

    // ---- inline pass ----------------------------------------------------------------------------------------------------------

    private void ProcessInlines()
    {
        var inline = new MdInlineParser(_references, _options.Footnotes ? _footnoteLabels : null, _options);
        var stack = new Stack<MdNode>();
        stack.Push(_doc);
        while (stack.Count > 0)
        {
            MdNode n = stack.Pop();
            if (n.Kind == MdKind.Item && _options.TaskLists && n.FirstChild is { Kind: MdKind.Paragraph } first && first.Text is { } t)
            {
                // GFM task list items: "[ ]" or "[x]" and a space begin the item's first paragraph.
                if (t.Length >= 3 && t[0] == '[' && t[1] is ' ' or 'x' or 'X' && t[2] == ']' && (t.Length == 3 || t[3] is ' ' or '\t'))
                {
                    n.Task = t[1] == ' ' ? 1 : 2;
                    first.Text = t.Length > 4 ? t[4..] : string.Empty;
                }
            }

            if (n.Kind is MdKind.Paragraph or MdKind.Heading or MdKind.TableCell)
            {
                inline.Parse(n, (n.Text ?? string.Empty).Trim(' ', '\t', '\n'));
                continue;
            }

            for (MdNode? c = n.LastChild; c is not null; c = c.Prev)
            {
                stack.Push(c);
            }
        }
    }

    // ---- line handling --------------------------------------------------------------------------------------------------------

    private char At(int index) => index >= 0 && index < _line.Length ? _line[index] : '\0';

    private bool HasChar(int index) => index >= 0 && index < _line.Length;

    private void FindNextNonspace()
    {
        int i = _offset;
        int cols = _column;
        while (i < _line.Length)
        {
            char c = _line[i];
            if (c == ' ')
            {
                i++;
                cols++;
            }
            else if (c == '\t')
            {
                i++;
                cols += 4 - (cols % 4);
            }
            else
            {
                break;
            }
        }

        _blank = i >= _line.Length;
        _nextNonspace = i;
        _nextNonspaceColumn = cols;
        _indent = _nextNonspaceColumn - _column;
        _indented = _indent >= CodeIndent;
    }

    private void AdvanceNextNonspace()
    {
        _offset = _nextNonspace;
        _column = _nextNonspaceColumn;
        _partiallyConsumedTab = false;
    }

    private void AdvanceOffset(int count, bool columns)
    {
        while (count > 0 && _offset < _line.Length)
        {
            char c = _line[_offset];
            if (c == '\t')
            {
                int charsToTab = 4 - (_column % 4);
                if (columns)
                {
                    _partiallyConsumedTab = charsToTab > count;
                    int advance = charsToTab > count ? count : charsToTab;
                    _column += advance;
                    _offset += _partiallyConsumedTab ? 0 : 1;
                    count -= advance;
                }
                else
                {
                    _partiallyConsumedTab = false;
                    _column += charsToTab;
                    _offset += 1;
                    count -= 1;
                }
            }
            else
            {
                _partiallyConsumedTab = false;
                _offset += 1;
                _column += 1;
                count -= 1;
            }
        }
    }

    private void AddLine()
    {
        StringBuilder content = _tip.Content ??= new StringBuilder();
        if (_partiallyConsumedTab)
        {
            _offset += 1;
            int charsToTab = 4 - (_column % 4);
            content.Append(' ', charsToTab);
        }

        content.Append(_line, _offset, _line.Length - _offset).Append('\n');
    }

    private static bool CanContain(MdKind parent, MdKind child) => parent switch
    {
        MdKind.Document or MdKind.BlockQuote or MdKind.Item or MdKind.FootnoteDefinition => child != MdKind.Item,
        MdKind.List => child == MdKind.Item,
        _ => false,
    };

    private MdNode AddChild(MdKind kind)
    {
        while (!CanContain(_tip.Kind, kind))
        {
            Finalize(_tip);
        }

        var node = new MdNode(kind) { StartLine = _lineNumber, Depth = _tip.Depth + 1 };
        _tip.AppendChild(node);
        _tip = node;
        return node;
    }

    private void CloseUnmatchedBlocks()
    {
        if (_allClosed)
        {
            return;
        }

        while (_oldTip != _lastMatched)
        {
            MdNode parent = _oldTip.Parent!;
            Finalize(_oldTip);
            _oldTip = parent;
        }

        _allClosed = true;
    }

    private bool AtDepthLimit()
    {
        if (_tip.Depth + 2 < Math.Max(8, _options.MaxDepth))
        {
            return false;
        }

        if (!_depthReported)
        {
            _depthReported = true;
            _result.Issues.Add(("markdown.too-deep", "Block containers nested deeper than " + _options.MaxDepth + " levels are read as text.", _lineNumber));
        }

        return true;
    }

    private void IncorporateLine(string ln)
    {
        bool allMatched = true;
        MdNode container = _doc;
        _oldTip = _tip;
        _offset = 0;
        _column = 0;
        _blank = false;
        _partiallyConsumedTab = false;
        _lineNumber++;
        _line = ln;
        _skipRow = false;

        // Meet the open containers one by one; each continues or not (§5: the line must carry the container's marker).
        MdNode? lastChild;
        while ((lastChild = container.LastChild) is not null && lastChild.Open)
        {
            container = lastChild;
            FindNextNonspace();
            switch (Continue(container))
            {
                case 0:
                    break;
                case 1:
                    allMatched = false;
                    break;
                case 2:
                    return;
            }

            if (!allMatched)
            {
                container = container.Parent!;
                break;
            }
        }

        _allClosed = container == _oldTip;
        _lastMatched = container;
        bool matchedLeaf = container.Kind != MdKind.Paragraph && AcceptsLines(container.Kind);

        while (!matchedLeaf)
        {
            FindNextNonspace();
            // Cheap exit: a line that cannot start a block (not indented, first character not special).
            if (!_indented && !MaybeSpecial(At(_nextNonspace)))
            {
                AdvanceNextNonspace();
                break;
            }

            int result = 0;
            for (int s = 0; s < 10 && result == 0; s++)
            {
                result = TryStart(s, container);
            }

            if (result == 1)
            {
                container = _tip;
            }
            else if (result == 2)
            {
                container = _tip;
                matchedLeaf = true;
            }
            else
            {
                AdvanceNextNonspace();
                break;
            }
        }

        if (!_allClosed && !_blank && _tip.Kind == MdKind.Paragraph)
        {
            // Lazy paragraph continuation (§5.1).
            AddLine();
        }
        else
        {
            CloseUnmatchedBlocks();
            if (_blank && container.LastChild is { } lc)
            {
                lc.LastLineBlank = true;
            }

            MdKind t = container.Kind;
            bool lastLineBlank = _blank && !(t == MdKind.BlockQuote || (t == MdKind.CodeBlock && container.Fenced)
                || (t == MdKind.Item && container.FirstChild is null && container.StartLine == _lineNumber));
            for (MdNode? cont = container; cont is not null; cont = cont.Parent)
            {
                cont.LastLineBlank = lastLineBlank;
            }

            if (AcceptsLines(t))
            {
                AddLine();
                if (t == MdKind.HtmlBlock && container.HtmlType is >= 1 and <= 5 && MdScan.HtmlBlockEnds(container.HtmlType, _line.AsSpan(Math.Min(_offset, _line.Length))))
                {
                    Finalize(container);
                }
            }
            else if (t == MdKind.Table && !_blank)
            {
                // The line that opened the table was its delimiter row, not a body row.
                if (!_skipRow)
                {
                    AddTableRow(container);
                }
            }
            else if (_offset < _line.Length && !_blank)
            {
                AddChild(MdKind.Paragraph);
                AdvanceNextNonspace();
                AddLine();
            }
        }
    }

    private static bool MaybeSpecial(char c) => c is '#' or '`' or '~' or '*' or '+' or '_' or '=' or '<' or '>' or '-' or '[' or '|' or ':' || char.IsAsciiDigit(c);

    private static bool AcceptsLines(MdKind kind) => kind is MdKind.Paragraph or MdKind.CodeBlock or MdKind.HtmlBlock;

    // 0: matched, 1: not matched, 2: the line is consumed (closing fence).
    private int Continue(MdNode c)
    {
        switch (c.Kind)
        {
            case MdKind.Document or MdKind.List:
                return 0;
            case MdKind.BlockQuote:
                if (!_indented && At(_nextNonspace) == '>')
                {
                    AdvanceNextNonspace();
                    AdvanceOffset(1, false);
                    if (MdScan.IsSpaceOrTab(At(_offset)) && HasChar(_offset))
                    {
                        AdvanceOffset(1, true);
                    }

                    return 0;
                }

                return 1;
            case MdKind.Item:
                if (_blank)
                {
                    if (c.FirstChild is null)
                    {
                        return 1;
                    }

                    AdvanceNextNonspace();
                }
                else if (_indent >= c.MarkerOffset + c.Padding)
                {
                    AdvanceOffset(c.MarkerOffset + c.Padding, true);
                }
                else
                {
                    return 1;
                }

                return 0;
            case MdKind.FootnoteDefinition:
                if (_blank)
                {
                    AdvanceNextNonspace();
                    return 0;
                }

                if (_indent >= CodeIndent)
                {
                    AdvanceOffset(CodeIndent, true);
                    return 0;
                }

                return 1;
            case MdKind.Heading or MdKind.ThematicBreak:
                return 1;
            case MdKind.Table:
                return _blank ? 1 : 0;
            case MdKind.CodeBlock:
                if (c.Fenced)
                {
                    if (_indent <= 3 && At(_nextNonspace) == c.FenceChar && IsClosingFence(c))
                    {
                        Finalize(c);
                        return 2;
                    }

                    int i = c.FenceOffset;
                    while (i > 0 && MdScan.IsSpaceOrTab(At(_offset)) && HasChar(_offset))
                    {
                        AdvanceOffset(1, true);
                        i--;
                    }
                }
                else if (_indent >= CodeIndent)
                {
                    AdvanceOffset(CodeIndent, true);
                }
                else if (_blank)
                {
                    AdvanceNextNonspace();
                }
                else
                {
                    return 1;
                }

                return 0;
            case MdKind.HtmlBlock:
                return _blank && c.HtmlType is 6 or 7 ? 1 : 0;
            case MdKind.Paragraph:
                return _blank ? 1 : 0;
            default:
                return 1;
        }
    }

    private bool IsClosingFence(MdNode block)
    {
        int i = _nextNonspace;
        char ch = block.FenceChar;
        while (i < _line.Length && _line[i] == ch)
        {
            i++;
        }

        int length = i - _nextNonspace;
        if (length < block.FenceLength)
        {
            return false;
        }

        while (i < _line.Length && MdScan.IsSpaceOrTab(_line[i]))
        {
            i++;
        }

        return i >= _line.Length;
    }

    // ---- block starts ---------------------------------------------------------------------------------------------------------

    // 0: no start, 1: a container started (keep looking for more starts), 2: a leaf started (the rest of the line is its content).
    private int TryStart(int index, MdNode container)
    {
        switch (index)
        {
            case 0: return StartBlockQuote();
            case 1: return StartAtxHeading();
            case 2: return StartFencedCode();
            case 3: return StartHtmlBlock(container);
            case 4: return StartSetextHeading(container);
            case 5: return StartThematicBreak();
            case 6: return StartListItem(container);
            case 7: return StartIndentedCode();
            case 8: return _options.Tables ? StartTable(container) : 0;
            case 9: return _options.Footnotes ? StartFootnoteDefinition(container) : 0;
            default: return 0;
        }
    }

    private int StartBlockQuote()
    {
        if (!_indented && At(_nextNonspace) == '>' && !AtDepthLimit())
        {
            AdvanceNextNonspace();
            AdvanceOffset(1, false);
            if (HasChar(_offset) && MdScan.IsSpaceOrTab(At(_offset)))
            {
                AdvanceOffset(1, true);
            }

            CloseUnmatchedBlocks();
            AddChild(MdKind.BlockQuote);
            return 1;
        }

        return 0;
    }

    private int StartAtxHeading()
    {
        if (_indented || At(_nextNonspace) != '#')
        {
            return 0;
        }

        int i = _nextNonspace;
        while (i < _line.Length && _line[i] == '#')
        {
            i++;
        }

        int level = i - _nextNonspace;
        if (level is < 1 or > 6 || (i < _line.Length && !MdScan.IsSpaceOrTab(_line[i])))
        {
            return 0;
        }

        AdvanceNextNonspace();
        AdvanceOffset(level, false);
        while (HasChar(_offset) && MdScan.IsSpaceOrTab(_line[_offset]))
        {
            AdvanceOffset(1, true);
        }

        CloseUnmatchedBlocks();
        MdNode heading = AddChild(MdKind.Heading);
        heading.Level = level;
        // §4.2: the closing sequence of '#' (preceded by a space or tab, or the whole content) is not part of the content.
        string rest = _line.Substring(Math.Min(_offset, _line.Length));
        int end = rest.Length;
        while (end > 0 && MdScan.IsSpaceOrTab(rest[end - 1]))
        {
            end--;
        }

        int hashEnd = end;
        while (end > 0 && rest[end - 1] == '#')
        {
            end--;
        }

        if (end < hashEnd && (end == 0 || MdScan.IsSpaceOrTab(rest[end - 1])))
        {
            rest = rest[..end];
        }
        else
        {
            rest = rest[..hashEnd];
        }

        heading.Text = rest.Trim(' ', '\t');
        AdvanceOffset(_line.Length - _offset, false);
        return 2;
    }

    private int StartFencedCode()
    {
        if (_indented)
        {
            return 0;
        }

        char ch = At(_nextNonspace);
        if (ch is not ('`' or '~'))
        {
            return 0;
        }

        int i = _nextNonspace;
        while (i < _line.Length && _line[i] == ch)
        {
            i++;
        }

        int length = i - _nextNonspace;
        if (length < 3)
        {
            return 0;
        }

        // §4.5: the info string of a backtick fence cannot contain a backtick.
        if (ch == '`' && _line.IndexOf('`', i) >= 0)
        {
            return 0;
        }

        CloseUnmatchedBlocks();
        MdNode code = AddChild(MdKind.CodeBlock);
        code.Fenced = true;
        code.FenceLength = length;
        code.FenceChar = ch;
        code.FenceOffset = _indent;
        AdvanceNextNonspace();
        AdvanceOffset(length, false);
        return 2;
    }

    private int StartHtmlBlock(MdNode container)
    {
        if (_indented || At(_nextNonspace) != '<' || !_options.RawHtml)
        {
            return 0;
        }

        ReadOnlySpan<char> rest = _line.AsSpan(_nextNonspace);
        bool mayStart7 = container.Kind != MdKind.Paragraph && !(!_allClosed && !_blank && _tip.Kind == MdKind.Paragraph);
        int type = MdScan.HtmlBlockStart(rest, mayStart7);
        if (type == 0)
        {
            return 0;
        }

        CloseUnmatchedBlocks();
        MdNode html = AddChild(MdKind.HtmlBlock);
        html.HtmlType = type;
        // The line is added whole, from the offset: leading spaces belong to the block's text.
        return 2;
    }

    private int StartSetextHeading(MdNode container)
    {
        if (_indented || container.Kind != MdKind.Paragraph)
        {
            return 0;
        }

        char ch = At(_nextNonspace);
        if (ch is not ('=' or '-'))
        {
            return 0;
        }

        int i = _nextNonspace;
        while (i < _line.Length && _line[i] == ch)
        {
            i++;
        }

        while (i < _line.Length && MdScan.IsSpaceOrTab(_line[i]))
        {
            i++;
        }

        if (i < _line.Length)
        {
            return 0;
        }

        CloseUnmatchedBlocks();
        // Link reference definitions at the start of the paragraph are taken out first; what is left is the heading.
        string content = container.Content?.ToString() ?? string.Empty;
        int pos = ExtractReferences(content);
        content = content[pos..];
        if (content.Length == 0)
        {
            // Only definitions were there: the paragraph stays open (empty) and this line becomes its text.
            container.Content = new StringBuilder();
            return 0;
        }

        var heading = new MdNode(MdKind.Heading) { StartLine = container.StartLine, Level = ch == '=' ? 1 : 2, Depth = container.Depth, Text = content };
        container.InsertAfter(heading);
        container.Unlink();
        _tip = heading;
        AdvanceOffset(_line.Length - _offset, false);
        return 2;
    }

    private int StartThematicBreak()
    {
        if (_indented)
        {
            return 0;
        }

        char ch = At(_nextNonspace);
        if (ch is not ('*' or '-' or '_'))
        {
            return 0;
        }

        int count = 0;
        for (int i = _nextNonspace; i < _line.Length; i++)
        {
            char c = _line[i];
            if (c == ch)
            {
                count++;
            }
            else if (!MdScan.IsSpaceOrTab(c))
            {
                return 0;
            }
        }

        if (count < 3)
        {
            return 0;
        }

        CloseUnmatchedBlocks();
        AddChild(MdKind.ThematicBreak);
        AdvanceOffset(_line.Length - _offset, false);
        return 2;
    }

    private int StartListItem(MdNode container)
    {
        if (_indented && container.Kind != MdKind.List)
        {
            return 0;
        }

        if (!ParseListMarker(container, out MdNode? data) || data is null)
        {
            return 0;
        }

        CloseUnmatchedBlocks();
        if (_tip.Kind != MdKind.List || !ListsMatch(_tip, data))
        {
            MdNode list = AddChild(MdKind.List);
            list.Ordered = data.Ordered;
            list.Marker = data.Marker;
            list.Start = data.Start;
        }

        MdNode item = AddChild(MdKind.Item);
        item.MarkerOffset = data.MarkerOffset;
        item.Padding = data.Padding;
        return 1;
    }

    private static bool ListsMatch(MdNode list, MdNode data) => list.Ordered == data.Ordered && list.Marker == data.Marker;

    // §5.2: a list marker is a bullet (- + *) or 1 to 9 digits and . or ), followed by a space, a tab or the end of the line.
    private bool ParseListMarker(MdNode container, out MdNode? data)
    {
        data = null;
        if (_indent >= CodeIndent)
        {
            return false;
        }

        int i = _nextNonspace;
        char c = At(i);
        var d = new MdNode(MdKind.Item) { MarkerOffset = _indent };
        int markerLength;
        if (c is '-' or '+' or '*')
        {
            d.Marker = c;
            markerLength = 1;
        }
        else if (char.IsAsciiDigit(c))
        {
            int j = i;
            while (j < _line.Length && char.IsAsciiDigit(_line[j]) && j - i < 10)
            {
                j++;
            }

            int digits = j - i;
            if (digits > 9 || j >= _line.Length || _line[j] is not ('.' or ')'))
            {
                return false;
            }

            int start = int.Parse(_line.AsSpan(i, digits), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture);
            // An ordered list can interrupt a paragraph only when it starts at 1.
            if (container.Kind == MdKind.Paragraph && start != 1)
            {
                return false;
            }

            d.Ordered = true;
            d.Start = start;
            d.Marker = _line[j];
            markerLength = digits + 1;
        }
        else
        {
            return false;
        }

        int after = i + markerLength;
        if (after < _line.Length && !MdScan.IsSpaceOrTab(_line[after]))
        {
            return false;
        }

        // An empty item cannot interrupt a paragraph.
        if (container.Kind == MdKind.Paragraph && _line.AsSpan(after).Trim(" \t").Length == 0)
        {
            return false;
        }

        if (AtDepthLimit())
        {
            return false;
        }

        AdvanceNextNonspace();
        AdvanceOffset(markerLength, true);
        int spacesStartColumn = _column;
        int spacesStartOffset = _offset;
        do
        {
            AdvanceOffset(1, true);
        }
        while (_column - spacesStartColumn < 5 && HasChar(_offset) && MdScan.IsSpaceOrTab(_line[_offset]));

        bool blankItem = !HasChar(_offset);
        int spacesAfter = _column - spacesStartColumn;
        if (spacesAfter >= 5 || spacesAfter < 1 || blankItem)
        {
            d.Padding = markerLength + 1;
            _column = spacesStartColumn;
            _offset = spacesStartOffset;
            if (HasChar(_offset) && MdScan.IsSpaceOrTab(_line[_offset]))
            {
                AdvanceOffset(1, true);
            }
        }
        else
        {
            d.Padding = markerLength + spacesAfter;
        }

        data = d;
        return true;
    }

    private int StartIndentedCode()
    {
        if (_indented && _tip.Kind != MdKind.Paragraph && !_blank)
        {
            AdvanceOffset(CodeIndent, true);
            CloseUnmatchedBlocks();
            AddChild(MdKind.CodeBlock);
            return 2;
        }

        return 0;
    }

    // ---- GFM tables -----------------------------------------------------------------------------------------------------------

    private static List<string> SplitRow(ReadOnlySpan<char> row)
    {
        string s = row.Trim(" \t").ToString();
        var cells = new List<string>();
        var sb = new StringBuilder();
        int i = 0;
        if (s.Length > 0 && s[0] == '|')
        {
            i = 1;
        }

        bool trailingPipe = false;
        for (; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '\\' && i + 1 < s.Length)
            {
                // "\|" is a pipe inside the cell; any other escape is left for the inline parser.
                if (s[i + 1] == '|')
                {
                    sb.Append('|');
                }
                else
                {
                    sb.Append(c).Append(s[i + 1]);
                }

                i++;
                trailingPipe = false;
            }
            else if (c == '|')
            {
                cells.Add(sb.ToString().Trim(' ', '\t'));
                sb.Clear();
                trailingPipe = true;
            }
            else
            {
                sb.Append(c);
                trailingPipe = false;
            }
        }

        if (!trailingPipe || sb.Length > 0)
        {
            cells.Add(sb.ToString().Trim(' ', '\t'));
        }

        return cells;
    }

    private static bool TryDelimiterRow(ReadOnlySpan<char> line, out List<int> alignments)
    {
        alignments = [];
        if (line.IndexOf('|') < 0 && line.IndexOf('-') < 0)
        {
            return false;
        }

        List<string> cells = SplitRow(line);
        if (cells.Count == 0)
        {
            return false;
        }

        foreach (string cell in cells)
        {
            int a = 0;
            string c = cell;
            if (c.Length > 0 && c[0] == ':')
            {
                a = 1;
                c = c[1..];
            }

            if (c.Length > 0 && c[^1] == ':')
            {
                a = a == 1 ? 2 : 3;
                c = c[..^1];
            }

            if (c.Length == 0 || c.AsSpan().IndexOfAnyExcept('-') >= 0)
            {
                return false;
            }

            alignments.Add(a);
        }

        return true;
    }

    private int StartTable(MdNode container)
    {
        if (_indented || container.Kind != MdKind.Paragraph)
        {
            return 0;
        }

        ReadOnlySpan<char> rest = _line.AsSpan(_nextNonspace);
        if (rest.IndexOf('|') < 0 && rest.IndexOf(':') < 0)
        {
            // A delimiter row of only hyphens needs a pipe somewhere; "---" alone is a heading underline or a break.
            return 0;
        }

        if (!TryDelimiterRow(rest, out List<int> alignments))
        {
            return 0;
        }

        string content = container.Content?.ToString() ?? string.Empty;
        string trimmed = content.TrimEnd('\n');
        int lastNewline = trimmed.LastIndexOf('\n');
        string header = lastNewline < 0 ? trimmed : trimmed[(lastNewline + 1)..];
        List<string> headerCells = SplitRow(header);
        if (headerCells.Count != alignments.Count || (header.IndexOf('|', StringComparison.Ordinal) < 0 && rest.IndexOf('|') < 0))
        {
            return 0;
        }

        CloseUnmatchedBlocks();
        // Lines of the paragraph above the header row stay a paragraph.
        string before = lastNewline < 0 ? string.Empty : trimmed[..lastNewline];
        var table = new MdNode(MdKind.Table) { StartLine = _lineNumber - 1, Depth = container.Depth };
        if (before.Length > 0)
        {
            container.Content = new StringBuilder(before).Append('\n');
            container.InsertAfter(table);
        }
        else
        {
            container.InsertAfter(table);
            container.Unlink();
        }

        MdNode headerRow = new(MdKind.TableRow) { IsHeader = true, Depth = table.Depth + 1 };
        table.AppendChild(headerRow);
        for (int i = 0; i < headerCells.Count; i++)
        {
            headerRow.AppendChild(new MdNode(MdKind.TableCell) { Text = headerCells[i], Align = alignments[i], IsHeader = true });
        }

        // Remember the alignments for the body rows.
        table.Info = string.Join(',', alignments);
        _tip = table;
        _skipRow = true;
        AdvanceOffset(_line.Length - _offset, false);
        return 2;
    }

    private void AddTableRow(MdNode table)
    {
        List<string> cells = SplitRow(_line.AsSpan(Math.Min(_offset, _line.Length)));
        string[] aligns = table.Info.Split(',');
        var row = new MdNode(MdKind.TableRow) { Depth = table.Depth + 1 };
        table.AppendChild(row);
        // §4.10 (GFM): excess cells are ignored and missing cells are empty.
        for (int i = 0; i < aligns.Length; i++)
        {
            row.AppendChild(new MdNode(MdKind.TableCell) { Text = i < cells.Count ? cells[i] : string.Empty, Align = int.Parse(aligns[i], System.Globalization.CultureInfo.InvariantCulture) });
        }

        _tip = table;
    }

    // ---- footnote definitions -------------------------------------------------------------------------------------------------

    private int StartFootnoteDefinition(MdNode container)
    {
        if (_indented || container.Kind == MdKind.Paragraph || At(_nextNonspace) != '[' || At(_nextNonspace + 1) != '^')
        {
            return 0;
        }

        int i = _nextNonspace + 2;
        int labelStart = i;
        while (i < _line.Length && _line[i] is not (']' or ' ' or '\t'))
        {
            i++;
        }

        if (i == labelStart || At(i) != ']' || At(i + 1) != ':' || AtDepthLimit())
        {
            return 0;
        }

        string label = _line.Substring(labelStart, i - labelStart);
        CloseUnmatchedBlocks();
        MdNode def = AddChild(MdKind.FootnoteDefinition);
        def.Label = label;
        _footnoteLabels.Add(MdText.NormalizeLabel(label));
        AdvanceNextNonspace();
        AdvanceOffset(i + 2 - _offset, false);
        while (HasChar(_offset) && MdScan.IsSpaceOrTab(_line[_offset]))
        {
            AdvanceOffset(1, true);
        }

        return 1;
    }

    // ---- finalising -----------------------------------------------------------------------------------------------------------

    private int ExtractReferences(string content)
    {
        var inline = new MdInlineParser(_references, null, _options);
        int pos = 0;
        while (pos < content.Length && content[pos] == '[')
        {
            int next = inline.ParseReference(content, pos);
            if (next < 0)
            {
                break;
            }

            pos = next;
        }

        return pos;
    }

    private void Finalize(MdNode block)
    {
        MdNode? above = block.Parent;
        block.Open = false;
        switch (block.Kind)
        {
            case MdKind.Paragraph:
                {
                    string content = block.Content?.ToString() ?? string.Empty;
                    int pos = ExtractReferences(content);
                    content = content[pos..];
                    if (pos > 0 && content.AsSpan().Trim(" \t\n").Length == 0)
                    {
                        block.Unlink();
                    }
                    else
                    {
                        block.Text = content;
                    }

                    break;
                }

            case MdKind.CodeBlock:
                {
                    string content = block.Content?.ToString() ?? string.Empty;
                    if (block.Fenced)
                    {
                        // §4.5: the text after the fence on its first line is the info string; the rest is the code.
                        int nl = content.IndexOf('\n', StringComparison.Ordinal);
                        string first = nl < 0 ? content : content[..nl];
                        block.Info = MdText.Unescape(first.Trim(' ', '\t'));
                        block.Literal = nl < 0 ? string.Empty : content[(nl + 1)..];
                    }
                    else
                    {
                        // §4.4: trailing blank lines are not part of an indented code block.
                        int cut = TrailingBlankStart(content);
                        block.Literal = cut < content.Length ? content[..cut] + "\n" : content;
                    }

                    break;
                }

            case MdKind.HtmlBlock:
                {
                    string content = block.Content?.ToString() ?? string.Empty;
                    block.Literal = content[..TrailingBlankStart(content)];
                    break;
                }

            case MdKind.List:
                block.Tight = IsTight(block);
                break;
        }

        _tip = above ?? block;
        if (above is null)
        {
            _tip = block;
        }
    }

    // The index where a run of line ends, each followed only by spaces, reaches the end of the text (the whole length when there is none).
    private static int TrailingBlankStart(string content)
    {
        int cut = content.Length;
        int q = cut;
        while (true)
        {
            int k = q;
            while (k > 0 && content[k - 1] == ' ')
            {
                k--;
            }

            if (k > 0 && content[k - 1] == '\n')
            {
                q = k - 1;
                cut = q;
            }
            else
            {
                break;
            }
        }

        return cut;
    }

    private static bool EndsWithBlankLine(MdNode? block)
    {
        while (block is not null)
        {
            if (block.LastLineBlank)
            {
                return true;
            }

            if (!block.LastLineChecked && block.Kind is MdKind.List or MdKind.Item)
            {
                block.LastLineChecked = true;
                block = block.LastChild;
            }
            else
            {
                block.LastLineChecked = true;
                break;
            }
        }

        return false;
    }

    // §5.3: a list is loose if any of its items is separated by blank lines, or any item directly contains two block elements with a blank line between.
    private static bool IsTight(MdNode list)
    {
        for (MdNode? item = list.FirstChild; item is not null; item = item.Next)
        {
            if (EndsWithBlankLine(item) && item.Next is not null)
            {
                return false;
            }

            for (MdNode? sub = item.FirstChild; sub is not null; sub = sub.Next)
            {
                if (EndsWithBlankLine(sub) && (item.Next is not null || sub.Next is not null))
                {
                    return false;
                }
            }
        }

        return true;
    }
}
