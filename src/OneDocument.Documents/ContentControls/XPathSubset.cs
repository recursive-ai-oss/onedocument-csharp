using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Documents.ContentControls;

/// <summary>
/// The XPath 1.0 subset used by data bindings, evaluated over a custom XML part. Absolute location paths with the child,
/// descendant (<c>//</c>), self, parent and attribute axes, name tests (<c>*</c> too), <c>text()</c> and <c>node()</c>; predicates
/// with positions, <c>last()</c>, comparisons, <c>and</c>/<c>or</c>/<c>not()</c>, attribute and child tests, <c>name()</c>,
/// <c>local-name()</c>, <c>namespace-uri()</c>, <c>position()</c>, <c>contains()</c>, <c>starts-with()</c>, <c>true()</c> and
/// <c>false()</c>. Nothing else runs: no other functions, unions, arithmetic or variables.
/// </summary>
internal sealed class XPathSubset
{
    private readonly List<Step> _steps;

    private XPathSubset(List<Step> steps) => _steps = steps;

    /// <summary>One selected node: an element (or the document), an attribute of it, or its text.</summary>
    internal readonly record struct Node(MarkupNode Element, string? AttributeName, string? AttributeNamespace, string? Value, bool IsText)
    {
        public bool IsAttribute => AttributeName is not null;

        public string StringValue => IsAttribute || IsText ? Value ?? string.Empty : Element.InnerText;
    }

    public static bool TryParse(string xpath, out XPathSubset? expression, out BindingStatus failure)
    {
        expression = null;
        failure = BindingStatus.Invalid;
        if (!Lex(xpath, out List<Tok>? tokens, out failure))
        {
            return false;
        }

        var parser = new Parser(tokens!);
        try
        {
            List<Step> steps = parser.ParseAbsolute();
            if (!parser.AtEnd)
            {
                throw new XFail(BindingStatus.Invalid);
            }

            expression = new XPathSubset(steps);
            return true;
        }
        catch (XFail f)
        {
            failure = f.Status;
            return false;
        }
    }

    /// <summary>Selects the nodes in document order; false when a prefix in the path has no mapping.</summary>
    public bool Select(MarkupDocument data, IReadOnlyDictionary<string, string> prefixes, out List<Node> result)
    {
        var ctx = new Eval(prefixes);
        result = RunSteps(_steps, [new Node(data.DocumentNode, null, null, null, false)], ctx);
        return !ctx.BadPrefix;
    }

    // ---- evaluation

    private sealed class Eval(IReadOnlyDictionary<string, string> prefixes)
    {
        public readonly IReadOnlyDictionary<string, string> Prefixes = prefixes;
        public bool BadPrefix;
    }

    private static List<Node> RunSteps(List<Step> steps, List<Node> start, Eval ctx)
    {
        List<Node> current = start;
        foreach (Step s in steps)
        {
            var next = new List<Node>();
            var seen = new HashSet<(MarkupNode, string?)>();
            List<Node> contexts = current;
            if (s.Descendants)
            {
                contexts = [];
                foreach (Node c in current)
                {
                    contexts.Add(c);
                    if (!c.IsAttribute && !c.IsText)
                    {
                        AddDescendants(c.Element, contexts);
                    }
                }
            }

            foreach (Node c in contexts)
            {
                List<Node> axis = Axis(s, c, ctx);
                foreach (Pred p in s.Predicates)
                {
                    var kept = new List<Node>();
                    for (int i = 0; i < axis.Count; i++)
                    {
                        if (Truth(p.Expr.Evaluate(axis[i], i + 1, axis.Count, ctx), i + 1))
                        {
                            kept.Add(axis[i]);
                        }
                    }

                    axis = kept;
                }

                foreach (Node n in axis)
                {
                    if (seen.Add((n.Element, n.AttributeName is null ? (n.IsText ? "#text" : null) : "@" + n.AttributeNamespace + ":" + n.AttributeName)))
                    {
                        next.Add(n);
                    }
                }
            }

            current = next;
        }

        return current;
    }

    private static void AddDescendants(MarkupNode element, List<Node> into)
    {
        // Iterative walk: bindings come from files, so nesting may be as deep as the XML depth limit allows.
        var stack = new Stack<MarkupNode>();
        for (MarkupNode c = element.LastChild; !c.IsNull; c = c.PreviousSibling)
        {
            if (c.Kind == MarkupNodeKind.Element)
            {
                stack.Push(c);
            }
        }

        while (stack.Count > 0)
        {
            MarkupNode n = stack.Pop();
            into.Add(new Node(n, null, null, null, false));
            for (MarkupNode c = n.LastChild; !c.IsNull; c = c.PreviousSibling)
            {
                if (c.Kind == MarkupNodeKind.Element)
                {
                    stack.Push(c);
                }
            }
        }
    }

    private static List<Node> Axis(Step s, Node context, Eval ctx)
    {
        var list = new List<Node>();
        if (context.IsAttribute || context.IsText)
        {
            if (s.Axis == AxisKind.Parent)
            {
                list.Add(new Node(context.Element, null, null, null, false));
            }
            else if (s.Axis == AxisKind.Self && s.Test == TestKind.AnyNode)
            {
                list.Add(context);
            }

            return list;
        }

        MarkupNode e = context.Element;
        switch (s.Axis)
        {
            case AxisKind.Self:
                if (s.Test == TestKind.AnyNode || (s.Test == TestKind.Name && NameMatches(e, s, ctx)))
                {
                    list.Add(context);
                }

                break;
            case AxisKind.Parent:
                if (!e.Parent.IsNull && e.Kind != MarkupNodeKind.Document)
                {
                    list.Add(new Node(e.Parent, null, null, null, false));
                }

                break;
            case AxisKind.Attribute:
                foreach (MarkupAttributeRef a in e.Attributes)
                {
                    if (a.IsNamespaceDeclaration)
                    {
                        continue;
                    }

                    string ns = a.NamespaceUri;
                    string local = Encoding.UTF8.GetString(a.LocalName);
                    if (s.Test == TestKind.Wildcard || (s.Test == TestKind.Name && local == s.Local && AttributeNamespaceMatches(s, ns, ctx)))
                    {
                        list.Add(new Node(e, local, ns, a.Value, false));
                    }
                }

                break;
            default:
                StringBuilder? text = null;
                for (MarkupNode c = e.FirstChild; !c.IsNull; c = c.NextSibling)
                {
                    if (c.Kind == MarkupNodeKind.Element)
                    {
                        if (s.Test is TestKind.AnyNode or TestKind.Wildcard || (s.Test == TestKind.Name && NameMatches(c, s, ctx)))
                        {
                            list.Add(new Node(c, null, null, null, false));
                        }
                    }
                    else if ((c.Kind == MarkupNodeKind.Text || c.Kind == MarkupNodeKind.CData) && s.Test is TestKind.Text or TestKind.AnyNode)
                    {
                        // An element's own text nodes read as one text node, which is how a binding takes its value.
                        text ??= new StringBuilder();
                        text.Append(c.InnerText);
                    }
                }

                if (text is not null)
                {
                    list.Add(new Node(e, null, null, text.ToString(), true));
                }

                break;
        }

        return list;
    }

    private static bool Resolve(string? prefix, Eval ctx, out string ns)
    {
        ns = string.Empty;
        if (string.IsNullOrEmpty(prefix))
        {
            return true;
        }

        if (ctx.Prefixes.TryGetValue(prefix, out string? uri))
        {
            ns = uri;
            return true;
        }

        ctx.BadPrefix = true;
        return false;
    }

    private static bool NameMatches(MarkupNode e, Step s, Eval ctx)
    {
        if (!Resolve(s.Prefix, ctx, out string ns))
        {
            return false;
        }

        return e.LocalNameString == s.Local && string.Equals(e.NamespaceUri, ns, StringComparison.Ordinal);
    }

    private static bool AttributeNamespaceMatches(Step s, string attributeNamespace, Eval ctx) =>
        Resolve(s.Prefix, ctx, out string ns) && string.Equals(ns, attributeNamespace, StringComparison.Ordinal);

    private static bool Truth(XVal v, int position) => v.Kind switch
    {
        XKind.Number => v.Number == position,
        XKind.Boolean => v.Boolean,
        XKind.String => v.Text.Length > 0,
        _ => v.Set!.Count > 0,
    };

    // ---- values

    private enum XKind
    {
        Number,
        String,
        Boolean,
        Set,
    }

    private sealed class XVal
    {
        public XKind Kind;
        public double Number;
        public string Text = string.Empty;
        public bool Boolean;
        public List<Node>? Set;

        public static XVal Num(double d) => new() { Kind = XKind.Number, Number = d };

        public static XVal Str(string s) => new() { Kind = XKind.String, Text = s };

        public static XVal Bool(bool b) => new() { Kind = XKind.Boolean, Boolean = b };

        public static XVal Nodes(List<Node> n) => new() { Kind = XKind.Set, Set = n };

        public string AsString() => Kind switch
        {
            XKind.Number => Number.ToString("R", CultureInfo.InvariantCulture),
            XKind.Boolean => Boolean ? "true" : "false",
            XKind.String => Text,
            _ => Set!.Count > 0 ? Set[0].StringValue : string.Empty,
        };
    }

    private static bool Compare(XVal a, string op, XVal b)
    {
        if (a.Kind == XKind.Set || b.Kind == XKind.Set)
        {
            // XPath 1.0 §3.4: a comparison with a node-set is true when it holds for some node's string value.
            foreach (Node n in a.Kind == XKind.Set ? a.Set! : b.Set!)
            {
                XVal v = XVal.Str(n.StringValue);
                XVal other = a.Kind == XKind.Set ? b : a;
                if (other.Kind == XKind.Set)
                {
                    other = XVal.Nodes(other.Set!);
                    foreach (Node m in other.Set!)
                    {
                        if (Compare(v, op, XVal.Str(m.StringValue)))
                        {
                            return true;
                        }
                    }

                    continue;
                }

                if (a.Kind == XKind.Set ? Compare(v, op, other) : Compare(other, op, v))
                {
                    return true;
                }
            }

            return false;
        }

        if (a.Kind == XKind.Boolean || b.Kind == XKind.Boolean)
        {
            bool x = a.Kind == XKind.Boolean ? a.Boolean : a.AsString().Length > 0;
            bool y = b.Kind == XKind.Boolean ? b.Boolean : b.AsString().Length > 0;
            return op == "=" ? x == y : x != y;
        }

        if ((a.Kind == XKind.Number || b.Kind == XKind.Number || op is "<" or ">" or "<=" or ">="))
        {
            double x = ToNumber(a);
            double y = ToNumber(b);
            return op switch
            {
                "=" => x == y,
                "!=" => x != y,
                "<" => x < y,
                ">" => x > y,
                "<=" => x <= y,
                _ => x >= y,
            };
        }

        bool eq = string.Equals(a.AsString(), b.AsString(), StringComparison.Ordinal);
        return op == "=" ? eq : !eq;
    }

    private static double ToNumber(XVal v) =>
        v.Kind == XKind.Number ? v.Number
        : double.TryParse(v.AsString().Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : double.NaN;

    // ---- syntax tree

    private enum AxisKind
    {
        Child,
        Self,
        Parent,
        Attribute,
    }

    private enum TestKind
    {
        Name,
        Wildcard,
        Text,
        AnyNode,
    }

    private sealed class Step
    {
        public AxisKind Axis;
        public TestKind Test;
        public string? Prefix;
        public string Local = string.Empty;
        public bool Descendants;
        public List<Pred> Predicates = [];
    }

    private sealed class Pred(Expr expr)
    {
        public readonly Expr Expr = expr;
    }

    private abstract class Expr
    {
        public abstract XVal Evaluate(Node context, int position, int size, Eval ctx);
    }

    private sealed class Literal(XVal value) : Expr
    {
        public override XVal Evaluate(Node context, int position, int size, Eval ctx) => value;
    }

    private sealed class PathExpr(List<Step> steps) : Expr
    {
        public override XVal Evaluate(Node context, int position, int size, Eval ctx) => XVal.Nodes(RunSteps(steps, [context], ctx));
    }

    private sealed class Binary(string op, Expr left, Expr right) : Expr
    {
        public override XVal Evaluate(Node context, int position, int size, Eval ctx)
        {
            XVal a = left.Evaluate(context, position, size, ctx);
            if (op == "or")
            {
                return XVal.Bool(Truth(a, position) || Truth(right.Evaluate(context, position, size, ctx), position));
            }

            if (op == "and")
            {
                return XVal.Bool(Truth(a, position) && Truth(right.Evaluate(context, position, size, ctx), position));
            }

            return XVal.Bool(Compare(a, op, right.Evaluate(context, position, size, ctx)));
        }
    }

    private sealed class Call(string name, List<Expr> args) : Expr
    {
        public override XVal Evaluate(Node context, int position, int size, Eval ctx)
        {
            switch (name)
            {
                case "position":
                    return XVal.Num(position);
                case "last":
                    return XVal.Num(size);
                case "true":
                    return XVal.Bool(true);
                case "false":
                    return XVal.Bool(false);
                case "not":
                    // A number in a boolean position is true when non-zero here (not() takes a boolean, never a position test).
                    return XVal.Bool(!AsBool(args[0].Evaluate(context, position, size, ctx)));
                case "name" or "local-name" or "namespace-uri":
                    {
                        Node n = context;
                        if (args.Count > 0 && args[0].Evaluate(context, position, size, ctx) is { Kind: XKind.Set } set)
                        {
                            if (set.Set!.Count == 0)
                            {
                                return XVal.Str(string.Empty);
                            }

                            n = set.Set[0];
                        }

                        if (n.IsText)
                        {
                            return XVal.Str(string.Empty);
                        }

                        if (n.IsAttribute)
                        {
                            return XVal.Str(name == "namespace-uri" ? n.AttributeNamespace ?? string.Empty : n.AttributeName!);
                        }

                        return XVal.Str(name switch
                        {
                            "name" => n.Element.Name,
                            "local-name" => n.Element.LocalNameString,
                            _ => n.Element.NamespaceUri,
                        });
                    }

                case "contains":
                    return XVal.Bool(args[0].Evaluate(context, position, size, ctx).AsString().Contains(args[1].Evaluate(context, position, size, ctx).AsString(), StringComparison.Ordinal));
                case "starts-with":
                    return XVal.Bool(args[0].Evaluate(context, position, size, ctx).AsString().StartsWith(args[1].Evaluate(context, position, size, ctx).AsString(), StringComparison.Ordinal));
                default:
                    throw new XFail(BindingStatus.UnsupportedPath);
            }
        }

        private static bool AsBool(XVal v) => v.Kind switch
        {
            XKind.Boolean => v.Boolean,
            XKind.Number => v.Number != 0 && !double.IsNaN(v.Number),
            XKind.String => v.Text.Length > 0,
            _ => v.Set!.Count > 0,
        };
    }

    // ---- lexer and parser

    private sealed class XFail(BindingStatus status) : Exception
    {
        public BindingStatus Status { get; } = status;
    }

    private enum TokKind
    {
        Name,
        Number,
        String,
        Symbol,
    }

    private readonly record struct Tok(TokKind Kind, string Text);

    private static bool Lex(string s, out List<Tok>? tokens, out BindingStatus failure)
    {
        failure = BindingStatus.Invalid;
        tokens = [];
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (c is '\'' or '"')
            {
                int end = s.IndexOf(c, i + 1);
                if (end < 0)
                {
                    return false;
                }

                tokens.Add(new Tok(TokKind.String, s[(i + 1)..end]));
                i = end + 1;
            }
            else if (char.IsDigit(c) || (c == '.' && i + 1 < s.Length && char.IsDigit(s[i + 1])))
            {
                int start = i;
                while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.'))
                {
                    i++;
                }

                tokens.Add(new Tok(TokKind.Number, s[start..i]));
            }
            else if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                i = ReadNcName(s, i);
                if (i + 1 < s.Length && s[i] == ':' && s[i + 1] != ':' && (char.IsLetter(s[i + 1]) || s[i + 1] == '_' || s[i + 1] == '*'))
                {
                    i++;
                    i = s[i] == '*' ? i + 1 : ReadNcName(s, i);
                }

                tokens.Add(new Tok(TokKind.Name, s[start..i]));
            }
            else if (c == ':' && i + 1 < s.Length && s[i + 1] == ':')
            {
                tokens.Add(new Tok(TokKind.Symbol, "::"));
                i += 2;
            }
            else if (c is '/' && i + 1 < s.Length && s[i + 1] == '/')
            {
                tokens.Add(new Tok(TokKind.Symbol, "//"));
                i += 2;
            }
            else if (c == '.' && i + 1 < s.Length && s[i + 1] == '.')
            {
                tokens.Add(new Tok(TokKind.Symbol, ".."));
                i += 2;
            }
            else if (c is '!' or '<' or '>' && i + 1 < s.Length && s[i + 1] == '=')
            {
                tokens.Add(new Tok(TokKind.Symbol, s.Substring(i, 2)));
                i += 2;
            }
            else if (c is '/' or '@' or '[' or ']' or '(' or ')' or ',' or '=' or '<' or '>' or '*' or '.')
            {
                tokens.Add(new Tok(TokKind.Symbol, c.ToString()));
                i++;
            }
            else
            {
                // '|' (union), '+', '-', '$' (variables) and the like are outside the subset.
                failure = c is '|' or '+' or '$' ? BindingStatus.UnsupportedPath : BindingStatus.Invalid;
                tokens = null;
                return false;
            }
        }

        return true;
    }

    private static int ReadNcName(string s, int i)
    {
        while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] is '_' or '-' or '.'))
        {
            i++;
        }

        return i;
    }

    private sealed class Parser(List<Tok> tokens)
    {
        private int _at;

        public bool AtEnd => _at >= tokens.Count;

        private Tok? Peek => _at < tokens.Count ? tokens[_at] : null;

        private bool IsSymbol(string text) => Peek is { Kind: TokKind.Symbol } t && t.Text == text;

        private bool IsName(string text) => Peek is { Kind: TokKind.Name } t && t.Text == text;

        private void Expect(string symbol)
        {
            if (!IsSymbol(symbol))
            {
                throw new XFail(BindingStatus.Invalid);
            }

            _at++;
        }

        public List<Step> ParseAbsolute()
        {
            if (!IsSymbol("/") && !IsSymbol("//"))
            {
                // A relative path has no context in a binding.
                throw new XFail(BindingStatus.UnsupportedPath);
            }

            return ParsePath(allowRelative: false);
        }

        private List<Step> ParsePath(bool allowRelative)
        {
            var steps = new List<Step>();
            bool descendants = false;
            if (IsSymbol("/"))
            {
                _at++;
                if (AtEnd || IsSymbol("]"))
                {
                    // "/" alone selects the document node: a binding cannot use it.
                    throw new XFail(BindingStatus.Invalid);
                }
            }
            else if (IsSymbol("//"))
            {
                _at++;
                descendants = true;
            }
            else if (!allowRelative)
            {
                throw new XFail(BindingStatus.UnsupportedPath);
            }

            while (true)
            {
                Step step = ParseStep();
                step.Descendants = descendants;
                steps.Add(step);
                descendants = false;
                if (IsSymbol("/"))
                {
                    _at++;
                }
                else if (IsSymbol("//"))
                {
                    _at++;
                    descendants = true;
                }
                else
                {
                    return steps;
                }
            }
        }

        private Step ParseStep()
        {
            var step = new Step { Axis = AxisKind.Child };
            if (IsSymbol("."))
            {
                _at++;
                step.Axis = AxisKind.Self;
                step.Test = TestKind.AnyNode;
                return step;
            }

            if (IsSymbol(".."))
            {
                _at++;
                step.Axis = AxisKind.Parent;
                step.Test = TestKind.AnyNode;
                return step;
            }

            if (IsSymbol("@"))
            {
                _at++;
                step.Axis = AxisKind.Attribute;
            }
            else if (Peek is { Kind: TokKind.Name } n && _at + 1 < tokens.Count && tokens[_at + 1] is { Kind: TokKind.Symbol, Text: "::" })
            {
                step.Axis = n.Text switch
                {
                    "child" => AxisKind.Child,
                    "attribute" => AxisKind.Attribute,
                    "self" => AxisKind.Self,
                    "parent" => AxisKind.Parent,
                    _ => throw new XFail(BindingStatus.UnsupportedPath),
                };
                _at += 2;
            }

            if (IsSymbol("*"))
            {
                _at++;
                step.Test = TestKind.Wildcard;
            }
            else if (Peek is { Kind: TokKind.Name } name)
            {
                _at++;
                if (IsSymbol("(") && name.Text is "text" or "node")
                {
                    _at++;
                    Expect(")");
                    step.Test = name.Text == "text" ? TestKind.Text : TestKind.AnyNode;
                    if (step.Test == TestKind.Text && step.Axis != AxisKind.Child)
                    {
                        throw new XFail(BindingStatus.UnsupportedPath);
                    }
                }
                else if (IsSymbol("("))
                {
                    throw new XFail(BindingStatus.UnsupportedPath);
                }
                else
                {
                    step.Test = TestKind.Name;
                    int colon = name.Text.IndexOf(':', StringComparison.Ordinal);
                    if (colon >= 0)
                    {
                        step.Prefix = name.Text[..colon];
                        step.Local = name.Text[(colon + 1)..];
                        if (step.Local == "*")
                        {
                            throw new XFail(BindingStatus.UnsupportedPath);
                        }
                    }
                    else
                    {
                        step.Local = name.Text;
                    }
                }
            }
            else
            {
                throw new XFail(BindingStatus.Invalid);
            }

            while (IsSymbol("["))
            {
                _at++;
                Expr e = ParseOr();
                Expect("]");
                step.Predicates.Add(new Pred(e));
            }

            return step;
        }

        private Expr ParseOr()
        {
            Expr left = ParseAnd();
            while (IsName("or"))
            {
                _at++;
                left = new Binary("or", left, ParseAnd());
            }

            return left;
        }

        private Expr ParseAnd()
        {
            Expr left = ParseComparison();
            while (IsName("and"))
            {
                _at++;
                left = new Binary("and", left, ParseComparison());
            }

            return left;
        }

        private Expr ParseComparison()
        {
            Expr left = ParsePrimary();
            if (Peek is { Kind: TokKind.Symbol } t && t.Text is "=" or "!=" or "<" or ">" or "<=" or ">=")
            {
                _at++;
                return new Binary(t.Text, left, ParsePrimary());
            }

            return left;
        }

        private Expr ParsePrimary()
        {
            if (Peek is not { } t)
            {
                throw new XFail(BindingStatus.Invalid);
            }

            if (t.Kind == TokKind.Number)
            {
                _at++;
                return double.TryParse(t.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
                    ? new Literal(XVal.Num(d))
                    : throw new XFail(BindingStatus.Invalid);
            }

            if (t.Kind == TokKind.String)
            {
                _at++;
                return new Literal(XVal.Str(t.Text));
            }

            if (IsSymbol("("))
            {
                _at++;
                Expr e = ParseOr();
                Expect(")");
                return e;
            }

            if (t.Kind == TokKind.Name && _at + 1 < tokens.Count && tokens[_at + 1] is { Kind: TokKind.Symbol, Text: "(" } && t.Text is not ("text" or "node"))
            {
                _at += 2;
                var args = new List<Expr>();
                if (!IsSymbol(")"))
                {
                    args.Add(ParseOr());
                    while (IsSymbol(","))
                    {
                        _at++;
                        args.Add(ParseOr());
                    }
                }

                Expect(")");
                int expected = t.Text switch
                {
                    "position" or "last" or "true" or "false" => 0,
                    "not" => 1,
                    "contains" or "starts-with" => 2,
                    "name" or "local-name" or "namespace-uri" => -1,
                    _ => throw new XFail(BindingStatus.UnsupportedPath),
                };
                if ((expected >= 0 && args.Count != expected) || (expected < 0 && args.Count > 1))
                {
                    throw new XFail(BindingStatus.Invalid);
                }

                return new Call(t.Text, args);
            }

            return new PathExpr(ParsePath(allowRelative: true));
        }
    }
}
