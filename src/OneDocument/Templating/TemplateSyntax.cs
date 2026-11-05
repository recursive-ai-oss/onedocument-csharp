using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Templating;

// The syntax layer: text of units -> tokens -> tree. It knows nothing about data; the Liquid/Jinja-style surface lives only here, so the
// syntax can change without touching the renderer.

internal enum TokenKind
{
    Text,
    Output,
    Block,
}

internal sealed class Token
{
    public TokenKind Kind;
    public int Unit;

    /// <summary>Unit-local range of the whole token including delimiters (Text: the text).</summary>
    public int Start;

    public int End;

    /// <summary>Unit-local range of the tag's inside, delimiters and trim markers excluded.</summary>
    public int InnerStart;

    public int InnerEnd;

    /// <summary>For a block: the lower-cased tag name; <c>#</c> for a comment.</summary>
    public string Name = string.Empty;

    public bool TrimBefore;
    public bool TrimAfter;

    /// <summary>Block arguments: the text after the tag name, from the normalised copy (straight quotes), trimmed, and where it starts in the unit.</summary>
    public string Args = string.Empty;

    public int ArgsStart;
}

internal sealed class Expr
{
    public string Text = string.Empty;
    public int Unit;
    public int Offset;
    public FormulaNode Ast = null!;
    public string[]? Path;
}

internal abstract class Node
{
}

internal sealed class SeqNode : Node
{
    public List<Node> Items { get; } = [];
}

internal sealed class TextNode(Token token) : Node
{
    public Token Token { get; } = token;
}

internal sealed class OutputNode(Token token, Expr expr) : Node
{
    public Token Token { get; } = token;

    public Expr Expr { get; } = expr;
}

internal sealed class IncludeNode(Token token, Expr expr) : Node
{
    public Token Token { get; } = token;

    public Expr Expr { get; } = expr;
}

internal sealed class InsertNode(Token token, TemplatePieceKind kind, Expr expr, Dictionary<string, string> options) : Node
{
    public Token Token { get; } = token;

    public TemplatePieceKind Kind { get; } = kind;

    public Expr Expr { get; } = expr;

    public Dictionary<string, string> Options { get; } = options;
}

internal sealed class Branch
{
    public Token Tag = null!;
    public Expr? Condition;
    public SeqNode Body = new();
}

internal sealed class IfNode : Node
{
    public List<Branch> Branches { get; } = [];

    public SeqNode? Else { get; set; }

    public Token Open = null!;
    public Token Close = null!;
    public string? Scope;
}

internal sealed class ForNode : Node
{
    public string Variable = string.Empty;
    public Expr Source = null!;
    public SeqNode Body = new();
    public SeqNode? Else;
    public Token Open = null!;
    public Token Close = null!;
    public string? Scope;
    public bool BodyStartsNewUnit;
}

internal sealed class TemplateSyntax
{
    private static readonly HashSet<string> KnownTags = new(StringComparer.Ordinal)
    {
        "for", "endfor", "if", "elif", "elsif", "else", "endif", "include", "raw", "endraw", "image", "html", "markdown",
    };

    private static readonly HashSet<string> Scopes = new(StringComparer.Ordinal)
    {
        "inline", "paragraph", "list-item", "table-row", "table", "section", "slide", "sheet", "row", "column",
    };

    private readonly TemplateSource _source;
    private readonly TemplateOptions _options;
    private readonly OfficeLimits _limits;
    private readonly List<Defect> _scanDefects = [];
    private readonly List<Defect> _scopeDefects = [];
    private List<Defect> _treeDefects = [];
    private List<Defect> _defects;
    private readonly int[] _unitStart;
    private readonly string[] _unitText;
    private readonly string[] _unitNorm;
    private bool _inRaw;
    private int _rawUnit;
    private int _rawOffset;

    public TemplateSyntax(TemplateSource source, TemplateOptions options)
    {
        _source = source;
        _options = options;
        _limits = options.Limits ?? OfficeLimits.Default;
        _defects = _scanDefects;
        int n = source.Units.Count;
        _unitStart = new int[n];
        _unitText = new string[n];
        _unitNorm = new string[n];
        int running = 0;
        for (int i = 0; i < n; i++)
        {
            _unitText[i] = source.Units[i].Text;
            _unitNorm[i] = NormalizeQuotes(_unitText[i]);
            _unitStart[i] = running;
            running += _unitText[i].Length + 1;
        }
    }

    /// <summary>Every defect found so far: scanning, then the latest tree build, then scope errors.</summary>
    public List<Defect> Collect() => [.. _scanDefects, .. _treeDefects, .. _scopeDefects];

    public Token[] Tokens { get; private set; } = [];

    public bool[] BlockOnly { get; private set; } = [];

    public SeqNode Root { get; private set; } = new();

    public List<(ForNode? For, IfNode? If)> Blocks { get; } = [];

    /// <summary>Opening tags of blocks whose range the binder chose: they repeat whole units even when both tags sit in one unit.</summary>
    public HashSet<Token> Forced { get; } = [];

    public string UnitText(int unit) => _unitText[unit];

    public DefectLocation Locate(int unit, int offset)
    {
        string? location = _source.Units[unit].Location;
        return location is null ? DefectLocation.AtText(_unitStart[unit] + offset) : DefectLocation.InPart(location, offset);
    }

    public void Error(DefectKind kind, string code, string message, int unit, int offset, DefectSeverity severity = DefectSeverity.Fatal)
    {
        if (_defects.Count < _limits.MaxDefects)
        {
            _defects.Add(new Defect(kind, code, severity, message, Locate(unit, offset)));
        }
    }

    // Word's autocorrect turns straight quotes into curly ones; tags written in Word carry them. Same length, so offsets hold.
    private static readonly System.Buffers.SearchValues<char> CurlyQuotes = System.Buffers.SearchValues.Create("\u201C\u201D\u201E\u201F\u2018\u2019\u201A\u201B");

    private static string NormalizeQuotes(string s)
    {
        if (s.AsSpan().IndexOfAny(CurlyQuotes) < 0)
        {
            return s;
        }

        return string.Create(s.Length, s, static (span, src) =>
        {
            for (int i = 0; i < span.Length; i++)
            {
                char c = src[i];
                span[i] = c switch
                {
                    '\u201C' or '\u201D' or '\u201E' or '\u201F' => '"',
                    '\u2018' or '\u2019' or '\u201A' or '\u201B' => '\'',
                    _ => c,
                };
            }
        });
    }

    // ---- scanning --------------------------------------------------------------------------------------------------------------

    public void Scan()
    {
        _defects = _scanDefects;
        var tokens = new List<Token>();
        int units = _source.Units.Count;
        var blockOnly = new bool[units];
        TemplateDelimiters d = _options.Delimiters;
        for (int u = 0; u < units; u++)
        {
            int first = tokens.Count;
            ScanUnit(u, tokens, d, first);
            if (tokens.Count == first)
            {
                tokens.Add(new Token { Kind = TokenKind.Text, Unit = u, Start = 0, End = 0 });
            }

            bool any = false;
            bool only = true;
            for (int i = first; i < tokens.Count; i++)
            {
                Token t = tokens[i];
                if (t.Kind == TokenKind.Block && IsStructural(t.Name))
                {
                    any = true;
                }
                else if (t.Kind == TokenKind.Text)
                {
                    if (!IsBlank(_unitText[u].AsSpan(t.Start, t.End - t.Start)))
                    {
                        only = false;
                    }
                }
                else
                {
                    only = false;
                }
            }

            blockOnly[u] = any && only;
        }

        if (_inRaw)
        {
            Error(DefectKind.Structure, "template.unclosed-block", "{% raw %} is never closed by {% endraw %}.", _rawUnit, _rawOffset);
        }

        Tokens = [.. tokens];
        BlockOnly = blockOnly;
    }

    private static bool IsStructural(string name) => name is "for" or "endfor" or "if" or "elif" or "elsif" or "else" or "endif" or "raw" or "endraw" or "#";

    private static bool IsBlank(ReadOnlySpan<char> s)
    {
        foreach (char c in s)
        {
            if (!char.IsWhiteSpace(c))
            {
                return false;
            }
        }

        return true;
    }

    private void ScanUnit(int u, List<Token> tokens, TemplateDelimiters d, int first)
    {
        string text = _unitText[u];
        string norm = _unitNorm[u];
        int pos = 0;
        while (pos < text.Length)
        {
            if (_inRaw)
            {
                int found = FindEndRaw(norm, pos, d, out int tagStart, out int tagEnd);
                if (found < 0)
                {
                    tokens.Add(new Token { Kind = TokenKind.Text, Unit = u, Start = pos, End = text.Length });
                    return;
                }

                if (tagStart > pos)
                {
                    tokens.Add(new Token { Kind = TokenKind.Text, Unit = u, Start = pos, End = tagStart });
                }

                tokens.Add(new Token { Kind = TokenKind.Block, Unit = u, Start = tagStart, End = tagEnd, InnerStart = tagStart, InnerEnd = tagEnd, Name = "endraw" });
                _inRaw = false;
                pos = tagEnd;
                continue;
            }

            int next = FindOpen(text, pos, d, out int which);
            if (next < 0)
            {
                tokens.Add(new Token { Kind = TokenKind.Text, Unit = u, Start = pos, End = text.Length });
                return;
            }

            if (next > pos)
            {
                tokens.Add(new Token { Kind = TokenKind.Text, Unit = u, Start = pos, End = next });
            }

            string open = which == 0 ? d.ExpressionOpen : which == 1 ? d.BlockOpen : d.CommentOpen;
            string close = which == 0 ? d.ExpressionClose : which == 1 ? d.BlockClose : d.CommentClose;
            int innerStart = next + open.Length;
            int closeAt = FindClose(norm, innerStart, close, which == 1, which == 2);
            if (closeAt < 0)
            {
                Error(DefectKind.Markup, "template.unclosed-tag", "The tag opened here is never closed by '" + close + "' in this paragraph or line.", u, next);
                tokens.Add(new Token { Kind = TokenKind.Text, Unit = u, Start = next, End = text.Length });
                return;
            }

            int innerEnd = closeAt;
            bool trimBefore = false;
            bool trimAfter = false;
            if (innerEnd > innerStart && norm[innerStart] == '-' && which != 2)
            {
                trimBefore = true;
                innerStart++;
            }

            if (innerEnd > innerStart && norm[innerEnd - 1] == '-' && which != 2)
            {
                trimAfter = true;
                innerEnd--;
            }

            int end = closeAt + close.Length;
            var token = new Token
            {
                Kind = which == 0 ? TokenKind.Output : TokenKind.Block,
                Unit = u,
                Start = next,
                End = end,
                InnerStart = innerStart,
                InnerEnd = innerEnd,
                TrimBefore = trimBefore,
                TrimAfter = trimAfter,
            };
            if (which == 2)
            {
                token.Name = "#";
            }
            else if (which == 1)
            {
                ReadBlockHead(norm, token);
            }
            else
            {
                token.Args = norm.Substring(innerStart, innerEnd - innerStart);
                token.ArgsStart = innerStart;
            }

            if (trimBefore && tokens.Count > first && tokens[^1].Kind == TokenKind.Text)
            {
                Token previous = tokens[^1];
                while (previous.End > previous.Start && char.IsWhiteSpace(text[previous.End - 1]))
                {
                    previous.End--;
                }
            }

            tokens.Add(token);
            if (token.Kind == TokenKind.Block && token.Name == "raw")
            {
                _inRaw = true;
                _rawUnit = u;
                _rawOffset = next;
            }

            pos = end;
            if (trimAfter)
            {
                while (pos < text.Length && char.IsWhiteSpace(text[pos]))
                {
                    pos++;
                }
            }
        }
    }

    private static void ReadBlockHead(string norm, Token token)
    {
        int i = token.InnerStart;
        int end = token.InnerEnd;
        while (i < end && char.IsWhiteSpace(norm[i]))
        {
            i++;
        }

        int nameStart = i;
        while (i < end && (char.IsAsciiLetter(norm[i]) || norm[i] == '_'))
        {
            i++;
        }

        token.Name = norm.Substring(nameStart, i - nameStart).ToLowerInvariant();
        while (i < end && char.IsWhiteSpace(norm[i]))
        {
            i++;
        }

        int e = end;
        while (e > i && char.IsWhiteSpace(norm[e - 1]))
        {
            e--;
        }

        token.Args = norm.Substring(i, e - i);
        token.ArgsStart = i;
    }

    private static int FindOpen(string text, int from, TemplateDelimiters d, out int which)
    {
        int best = -1;
        which = -1;
        string[] opens = [d.ExpressionOpen, d.BlockOpen, d.CommentOpen];
        for (int k = 0; k < 3; k++)
        {
            int at = text.IndexOf(opens[k], from, StringComparison.Ordinal);
            if (at >= 0 && (best < 0 || at < best))
            {
                best = at;
                which = k;
            }
        }

        return best;
    }

    // Looks for the closing delimiter outside string literals ("..." with "" inside, and '...' in block tags). A comment has no strings.
    private static int FindClose(string norm, int from, string close, bool singleQuotes, bool isComment)
    {
        if (isComment)
        {
            return norm.IndexOf(close, from, StringComparison.Ordinal);
        }

        char quote = '\0';
        for (int i = from; i < norm.Length; i++)
        {
            char c = norm[i];
            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (c == '"' || (singleQuotes && c == '\''))
            {
                quote = c;
                continue;
            }

            if (string.CompareOrdinal(norm, i, close, 0, close.Length) == 0)
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindEndRaw(string norm, int from, TemplateDelimiters d, out int tagStart, out int tagEnd)
    {
        tagStart = tagEnd = -1;
        int at = from;
        while (true)
        {
            at = norm.IndexOf(d.BlockOpen, at, StringComparison.Ordinal);
            if (at < 0)
            {
                return -1;
            }

            int i = at + d.BlockOpen.Length;
            if (i < norm.Length && norm[i] == '-')
            {
                i++;
            }

            while (i < norm.Length && char.IsWhiteSpace(norm[i]))
            {
                i++;
            }

            if (string.CompareOrdinal(norm, i, "endraw", 0, 6) == 0)
            {
                int close = norm.IndexOf(d.BlockClose, i + 6, StringComparison.Ordinal);
                if (close >= 0 && IsBlank(norm.AsSpan(i + 6, close - (i + 6)).TrimEnd('-')))
                {
                    tagStart = at;
                    tagEnd = close + d.BlockClose.Length;
                    return at;
                }
            }

            at += d.BlockOpen.Length;
        }
    }

    // ---- expressions -------------------------------------------------------------------------------------------------------------

    private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_';

    private static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static string[]? TryPath(string text)
    {
        var segs = new List<string>();
        int i = 0;
        while (true)
        {
            if (i >= text.Length || !IsIdentifierStart(text[i]))
            {
                return null;
            }

            int s = i;
            while (i < text.Length && IsIdentifierPart(text[i]))
            {
                i++;
            }

            segs.Add(text[s..i]);
            if (i == text.Length)
            {
                break;
            }

            if (text[i] != '.')
            {
                return null;
            }

            i++;
        }

        // TRUE and FALSE are literals, not data.
        if (segs.Count == 1 && (segs[0].Equals("TRUE", StringComparison.OrdinalIgnoreCase) || segs[0].Equals("FALSE", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        return [.. segs];
    }

    /// <summary>Parses an expression: a plain (dotted) name is a path read straight from the data, so a variable called <c>rev7</c> is never taken for a cell; anything else is an Excel formula.</summary>
    public Expr? ParseExpression(string text, int unit, int offset, bool allowEmpty = false)
    {
        string trimmed = text.Trim();
        int lead = text.Length - text.TrimStart().Length;
        if (trimmed.Length == 0)
        {
            if (!allowEmpty)
            {
                Error(DefectKind.Formula, "template.empty-expression", "The tag has no expression.", unit, offset);
            }

            return null;
        }

        var expr = new Expr { Text = trimmed, Unit = unit, Offset = offset + lead };
        string[]? path = TryPath(trimmed);
        if (path is not null)
        {
            expr.Path = path;
            expr.Ast = path.Length == 1 ? new NameNode(null, path[0]) : new MemberPathNode(path);
            return expr;
        }

        var po = new FormulaParserOptions { TemplateExtensions = true, Limits = _limits, FileForm = false };
        bool ok;
        FormulaNode? node;
        FormulaSyntaxError error;
        try
        {
            ok = FormulaParser.TryParse(trimmed, out node, out error, po);
        }
        catch (OfficeLimitException)
        {
            ok = false;
            node = null;
            error = new FormulaSyntaxError("formula.too-deep", "The expression is nested too deeply.", 0, trimmed.Length);
        }

        if (!ok || node is null)
        {
            Error(DefectKind.Formula, "template.expression-syntax", "Expression '" + Shorten(trimmed) + "': " + error.Message + " (" + error.Code + ")", unit, expr.Offset + Math.Max(0, error.Offset));
            return null;
        }

        expr.Ast = node;
        CheckFunctions(node, expr);
        return expr;
    }

    private static string Shorten(string s) => s.Length <= 60 ? s : string.Concat(s.AsSpan(0, 57), "...");

    private void CheckFunctions(FormulaNode node, Expr expr)
    {
        var registry = _options.Functions ?? DefaultRegistry;
        var stack = new Stack<FormulaNode>();
        stack.Push(node);
        while (stack.Count > 0)
        {
            FormulaNode n = stack.Pop();
            if (n is FunctionCallNode f && !registry.TryGet(f.Name, out _))
            {
                Error(DefectKind.Formula, "template.unknown-function", "Function " + f.Name + " is not known; the expression will evaluate to #NAME?.", expr.Unit, expr.Offset + Math.Max(0, f.Offset), DefectSeverity.Dropped);
            }

            foreach (FormulaNode c in n.Children)
            {
                stack.Push(c);
            }
        }
    }

    private static FunctionRegistry? _defaultRegistry;

    internal static FunctionRegistry DefaultRegistry => _defaultRegistry ??= FunctionRegistry.CreateDefault();

    // Splits "expression name=value name2='value'" into the expression and its options. Only a name followed by '=' (not '==', '=>' and the like)
    // after white space at the top level, outside quotes and brackets, starts the options.
    private bool SplitOptions(Token t, string args, out string expression, out Dictionary<string, string> options, HashSet<string>? allowedKeys)
    {
        options = new Dictionary<string, string>(StringComparer.Ordinal);
        expression = args;
        int depth = 0;
        char quote = '\0';
        for (int i = 0; i < args.Length; i++)
        {
            char c = args[i];
            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c is '(' or '[' or '{')
            {
                depth++;
            }
            else if (c is ')' or ']' or '}')
            {
                depth--;
            }
            else if (depth == 0 && i > 0 && char.IsWhiteSpace(args[i - 1]) && IsIdentifierStart(c))
            {
                int j = i;
                while (j < args.Length && (IsIdentifierPart(args[j]) || args[j] == '-'))
                {
                    j++;
                }

                int k = j;
                while (k < args.Length && args[k] == ' ')
                {
                    k++;
                }

                if (k < args.Length && args[k] == '=' && (k + 1 >= args.Length || (args[k + 1] != '=' && args[k + 1] != '>')))
                {
                    string key = args[i..j].ToLowerInvariant();
                    if (allowedKeys is null || allowedKeys.Contains(key))
                    {
                        expression = args[..i].TrimEnd();
                        return ParseOptionList(t, args, i, options);
                    }
                }
            }
        }

        return true;
    }

    private bool ParseOptionList(Token t, string args, int from, Dictionary<string, string> options)
    {
        int i = from;
        while (i < args.Length)
        {
            while (i < args.Length && char.IsWhiteSpace(args[i]))
            {
                i++;
            }

            if (i >= args.Length)
            {
                break;
            }

            int ks = i;
            while (i < args.Length && (IsIdentifierPart(args[i]) || args[i] == '-'))
            {
                i++;
            }

            string key = args[ks..i].ToLowerInvariant();
            while (i < args.Length && args[i] == ' ')
            {
                i++;
            }

            if (key.Length == 0 || i >= args.Length || args[i] != '=')
            {
                Error(DefectKind.Structure, "template.bad-option", "Expected name=value after the expression.", t.Unit, t.ArgsStart + ks);
                return false;
            }

            i++;
            while (i < args.Length && args[i] == ' ')
            {
                i++;
            }

            string value;
            if (i < args.Length && (args[i] == '"' || args[i] == '\''))
            {
                char q = args[i];
                int close = args.IndexOf(q, i + 1);
                if (close < 0)
                {
                    Error(DefectKind.Structure, "template.bad-option", "The option value is not closed.", t.Unit, t.ArgsStart + i);
                    return false;
                }

                value = args[(i + 1)..close];
                i = close + 1;
            }
            else
            {
                int vs = i;
                while (i < args.Length && !char.IsWhiteSpace(args[i]))
                {
                    i++;
                }

                value = args[vs..i];
            }

            options[key] = value;
        }

        return true;
    }

    // ---- tree --------------------------------------------------------------------------------------------------------------------

    public void BuildTree(Token[] tokens)
    {
        Blocks.Clear();
        _treeDefects = [];
        _defects = _treeDefects;
        var stack = new Stack<Frame>();
        var root = new Frame { Seq = new SeqNode() };
        Frame cur = root;
        int maxDepth = Math.Min(_options.MaxBlockDepth, _limits.MaxRecursionDepth);
        for (int i = 0; i < tokens.Length; i++)
        {
            Token t = tokens[i];
            switch (t.Kind)
            {
                case TokenKind.Text:
                    cur.Seq.Items.Add(new TextNode(t));
                    break;
                case TokenKind.Output:
                    {
                        Expr? e = ParseExpression(t.Args, t.Unit, t.ArgsStart);
                        if (e is not null)
                        {
                            cur.Seq.Items.Add(new OutputNode(t, e));
                        }

                        break;
                    }

                case TokenKind.Block:
                    cur = HandleBlock(t, cur, stack, root, maxDepth);
                    break;
            }
        }

        while (stack.Count > 0 || cur != root)
        {
            Token open = cur.Open!;
            Error(DefectKind.Structure, "template.unclosed-block", "{% " + open.Name + " %} is never closed by {% end" + open.Name + " %}.", open.Unit, open.Start);
            if (stack.Count == 0)
            {
                break;
            }

            cur = stack.Pop();
        }

        Root = root.Seq;
    }

    private sealed class Frame
    {
        public SeqNode Seq = null!;
        public Token? Open;
        public ForNode? For;
        public IfNode? If;
        public bool InElse;
    }

    private Frame HandleBlock(Token t, Frame cur, Stack<Frame> stack, Frame root, int maxDepth)
    {
        switch (t.Name)
        {
            case "#":
            case "raw":
            case "endraw":
                return cur;
            case "for":
                {
                    if (stack.Count + 1 > maxDepth)
                    {
                        throw new OfficeLimitException(nameof(OfficeLimits.MaxRecursionDepth), maxDepth, stack.Count + 1, Locate(t.Unit, t.Start));
                    }

                    return OpenFor(t, cur, stack);
                }

            case "if":
                {
                    if (stack.Count + 1 > maxDepth)
                    {
                        throw new OfficeLimitException(nameof(OfficeLimits.MaxRecursionDepth), maxDepth, stack.Count + 1, Locate(t.Unit, t.Start));
                    }

                    return OpenIf(t, cur, stack);
                }

            case "elif":
            case "elsif":
                {
                    if (cur.If is null || cur.InElse)
                    {
                        Error(DefectKind.Structure, "template.unexpected-tag", cur.If is null ? "{% " + t.Name + " %} outside an {% if %}." : "{% " + t.Name + " %} after {% else %}.", t.Unit, t.Start);
                        return cur;
                    }

                    SplitOptions(t, t.Args, out string cond, out _, null);
                    Expr? e = ParseExpression(cond, t.Unit, t.ArgsStart);
                    var branch = new Branch { Tag = t, Condition = e };
                    cur.If.Branches.Add(branch);
                    cur.Seq = branch.Body;
                    return cur;
                }

            case "else":
                {
                    if (cur.If is null && cur.For is null)
                    {
                        Error(DefectKind.Structure, "template.unexpected-tag", "{% else %} outside an {% if %} or {% for %}.", t.Unit, t.Start);
                        return cur;
                    }

                    if (cur.InElse)
                    {
                        Error(DefectKind.Structure, "template.unexpected-tag", "A second {% else %}.", t.Unit, t.Start);
                        return cur;
                    }

                    cur.InElse = true;
                    var seq = new SeqNode();
                    if (cur.If is not null)
                    {
                        cur.If.Else = seq;
                    }
                    else
                    {
                        cur.For!.Else = seq;
                    }

                    cur.Seq = seq;
                    return cur;
                }

            case "endif":
            case "endfor":
                {
                    bool isIf = t.Name == "endif";
                    if ((isIf ? cur.If is null : cur.For is null))
                    {
                        Error(DefectKind.Structure, "template.unexpected-tag", "{% " + t.Name + " %} has no matching {% " + t.Name[3..] + " %}" + (cur.Open is not null ? " (the open block is {% " + cur.Open.Name + " %})." : "."), t.Unit, t.Start);
                        return cur;
                    }

                    if (cur.If is not null)
                    {
                        cur.If.Close = t;
                    }
                    else
                    {
                        cur.For!.Close = t;
                        cur.For.BodyStartsNewUnit = cur.For.Open.Unit != t.Unit || Forced.Contains(cur.For.Open);
                    }

                    return stack.Pop();
                }

            case "include":
                {
                    Expr? e = ParseExpression(QuotedLiteral(t.Args), t.Unit, t.ArgsStart);
                    if (e is not null)
                    {
                        cur.Seq.Items.Add(new IncludeNode(t, e));
                    }

                    return cur;
                }

            case "image":
            case "html":
            case "markdown":
                {
                    SplitOptions(t, t.Args, out string ex, out Dictionary<string, string> options, null);
                    Expr? e = ParseExpression(ex, t.Unit, t.ArgsStart);
                    if (e is not null)
                    {
                        TemplatePieceKind kind = t.Name == "image" ? TemplatePieceKind.Image : t.Name == "html" ? TemplatePieceKind.Html : TemplatePieceKind.Markdown;
                        cur.Seq.Items.Add(new InsertNode(t, kind, e, options));
                    }

                    return cur;
                }

            default:
                Error(DefectKind.Structure, "template.unknown-tag", "Unknown tag '" + t.Name + "'. Known tags: " + string.Join(", ", KnownTags.Order(StringComparer.Ordinal)) + ".", t.Unit, t.Start);
                return cur;
        }
    }

    // {% include 'name' %}: a single-quoted word is a string here, though a formula only knows double quotes.
    private static string QuotedLiteral(string args)
    {
        string a = args.Trim();
        if (a.Length >= 2 && a[0] == '\'' && a[^1] == '\'')
        {
            return "\"" + a[1..^1].Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        }

        return args;
    }

    private string? ReadScope(Token t, Dictionary<string, string> options)
    {
        foreach (KeyValuePair<string, string> kv in options)
        {
            if (kv.Key != "scope")
            {
                Error(DefectKind.Structure, "template.bad-option", "Unknown option '" + kv.Key + "' (only scope is accepted here).", t.Unit, t.ArgsStart);
            }
        }

        if (options.TryGetValue("scope", out string? scope))
        {
            scope = scope.ToLowerInvariant();
            if (!Scopes.Contains(scope))
            {
                Error(DefectKind.Structure, "template.unknown-scope", "Unknown scope '" + scope + "'. Known scopes: " + string.Join(", ", Scopes.Order(StringComparer.Ordinal)) + ".", t.Unit, t.ArgsStart);
                return null;
            }

            return scope;
        }

        return null;
    }

    private Frame OpenFor(Token t, Frame cur, Stack<Frame> stack)
    {
        var f = new ForNode { Open = t, Close = t };
        SplitOptions(t, t.Args, out string head, out Dictionary<string, string> options, ScopeKey);
        f.Scope = ReadScope(t, options);
        // "item in expression"
        string h = head;
        int i = 0;
        while (i < h.Length && IsIdentifierPart(h[i]))
        {
            i++;
        }

        string variable = h[..i];
        int j = i;
        while (j < h.Length && char.IsWhiteSpace(h[j]))
        {
            j++;
        }

        bool ok = variable.Length > 0 && IsIdentifierStart(variable[0]) && j > i && j + 2 <= h.Length && h.AsSpan(j, 2).Equals("in", StringComparison.OrdinalIgnoreCase)
            && (j + 2 == h.Length || char.IsWhiteSpace(h[j + 2]));
        Expr? source = null;
        if (!ok)
        {
            Error(DefectKind.Structure, "template.bad-for", "A loop is written {% for item in items %}.", t.Unit, t.Start);
        }
        else
        {
            variable = variable.ToLowerInvariant();
            source = ParseExpression(h[(j + 2)..], t.Unit, t.ArgsStart + j + 2);
        }

        f.Variable = variable;
        f.Source = source!;
        cur.Seq.Items.Add(f);
        stack.Push(cur);
        var frame = new Frame { Seq = f.Body, Open = t, For = f };
        if (source is not null)
        {
            Blocks.Add((f, null));
        }

        return frame;
    }

    private static readonly HashSet<string> ScopeKey = new(StringComparer.Ordinal) { "scope" };

    private Frame OpenIf(Token t, Frame cur, Stack<Frame> stack)
    {
        var n = new IfNode { Open = t, Close = t };
        SplitOptions(t, t.Args, out string cond, out Dictionary<string, string> options, ScopeKey);
        n.Scope = ReadScope(t, options);
        Expr? e = ParseExpression(cond, t.Unit, t.ArgsStart);
        var branch = new Branch { Tag = t, Condition = e };
        n.Branches.Add(branch);
        cur.Seq.Items.Add(n);
        stack.Push(cur);
        Blocks.Add((null, n));
        return new Frame { Seq = branch.Body, Open = t, If = n };
    }

    /// <summary>
    /// Asks the binder which units each block covers and moves the opening tag before the first token of the first unit and the closing tag after the
    /// last token of the last unit, so the ordinary tree builder then repeats those whole units.
    /// </summary>
    public Token[] ApplyScopes(Token[] tokens)
    {
        ITemplateScopeResolver? resolver = _options.Scopes;
        if (resolver is null || Blocks.Count == 0 || _scanDefects.Concat(_treeDefects).Any(static d => d.Severity == DefectSeverity.Fatal))
        {
            return tokens;
        }

        _defects = _scopeDefects;

        var list = new List<Token>(tokens);
        bool moved = false;
        foreach ((ForNode? f, IfNode? n) in Blocks)
        {
            Token open = f?.Open ?? n!.Open;
            Token close = f?.Close ?? n!.Close;
            string expr = f is not null ? f.Source.Text : (n!.Branches[0].Condition?.Text ?? string.Empty);
            string? scope = f is not null ? f.Scope : n!.Scope;
            var info = new TemplateBlockInfo(f is not null ? "for" : "if", scope, open.Unit, close.Unit, expr);
            TemplateUnitRange? range = resolver.Resolve(info);
            if (range is not { } r)
            {
                continue;
            }

            if (r.First > open.Unit || r.Last < close.Unit || r.First < 0 || r.Last >= _source.Units.Count)
            {
                Error(DefectKind.Structure, "template.scope-invalid", "The binder's unit range must contain both tags of the block.", open.Unit, open.Start);
                continue;
            }

            Forced.Add(open);
            list.Remove(open);
            list.Remove(close);
            int firstIndex = list.FindIndex(x => x.Unit >= r.First);
            list.Insert(firstIndex < 0 ? list.Count : firstIndex, open);
            int lastIndex = list.FindLastIndex(x => x.Unit <= r.Last);
            list.Insert(lastIndex + 1, close);
            moved = true;
        }

        if (!moved)
        {
            return tokens;
        }

        return [.. list];
    }

    public static bool IsBlankText(ReadOnlySpan<char> s) => IsBlank(s);

    public int SegmentCount(int unit) => _source.Units[unit].Segments.Count;

}
