using System.Globalization;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Documents.Fields;
using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Schema;
using RecursiveAI.OneDocument.Templating;

namespace RecursiveAI.OneDocument.Documents.MailMerge;

/// <summary>
/// Evaluates the merge fields of one copy of the main document against the current record and edits the fields into their
/// results. One instance serves one pass over the document (Word's unit of "one record, or several with <c>NEXT</c>").
/// </summary>
internal sealed class MergePass
{
    private readonly MergeState _state;
    private readonly Document _document;
    private readonly List<Pending> _pending = [];
    private string _partName = string.Empty;

    public MergePass(Document document, MergeState state)
    {
        _document = document;
        _state = state;
    }

    private sealed record Pending(Field Field, string Text, bool InCode, List<Field> InCodeChildren);

    /// <summary>Evaluates every field of a part in document order, then replaces the handled ones by their results.</summary>
    public void Run(MarkupRef root, string partName)
    {
        _partName = partName;
        _pending.Clear();
        IReadOnlyList<Field> fields = FieldScanner.Scan(_document, root, partName);
        foreach (Field f in fields)
        {
            if (f.Parent is null)
            {
                Process(f, inCode: false);
            }
        }

        if (_state.Skip)
        {
            return;
        }

        foreach (Pending p in _pending)
        {
            Apply(p);
        }
    }

    private void Process(Field f, bool inCode)
    {
        string? text = Evaluate(f);
        if (text is not null)
        {
            var codeChildren = new List<Field>();
            foreach (Field c in f.Children)
            {
                if (IsInCode(f, c))
                {
                    codeChildren.Add(c);
                }
            }

            _pending.Add(new Pending(f, text, inCode, codeChildren));
            return;
        }

        foreach (Field c in f.Children)
        {
            Process(c, IsInCode(f, c));
        }
    }

    /// <summary>True when the nested field sits in the parent's instruction (before its separator) rather than in its result.</summary>
    private static bool IsInCode(Field parent, Field child)
    {
        if (parent.Kind == FieldKind.Simple)
        {
            return false;
        }

        MarkupNode probe = child.Kind == FieldKind.Complex ? child.BeginNode : (child.OwnRuns.Count > 0 ? child.OwnRuns[0] : default);
        return probe.IsNull || !parent.ResultRuns.Contains(probe);
    }

    // The instruction as Word evaluates it: nested fields are replaced by their values first (ECMA-376 Part 1 §17.16.2: a field
    // code may contain fields, innermost first).
    private string BuildInstruction(Field f)
    {
        if (f.Kind == FieldKind.Simple || f.Children.Count == 0)
        {
            return f.Kind == FieldKind.Simple ? f.Instruction : OwnInstruction(f);
        }

        var pieces = new List<(int At, string? Text, Field? Child)>();
        foreach (MarkupNode n in f.InstructionNodes)
        {
            pieces.Add((n.SourceStart, n.InnerText, null));
        }

        foreach (Field c in f.Children)
        {
            if (IsInCode(f, c))
            {
                pieces.Add((c.BeginNode.SourceStart, null, c));
            }
        }

        pieces.Sort(static (a, b) => a.At.CompareTo(b.At));
        var sb = new System.Text.StringBuilder();
        bool quoted = false;
        char open = '\0';
        foreach ((int _, string? text, Field? child) in pieces)
        {
            if (text is not null)
            {
                sb.Append(text);
                foreach (char ch in text)
                {
                    if (quoted && IsClosing(open, ch))
                    {
                        quoted = false;
                    }
                    else if (!quoted && (ch == '"' || ch == '\u201C' || ch == '\u201D'))
                    {
                        quoted = true;
                        open = ch;
                    }
                }
            }
            else
            {
                string value = ValueOf(child!);
                if (quoted)
                {
                    sb.Append(value.Replace("\"", "\\\"", StringComparison.Ordinal));
                }
                else if (value.Length == 0 || value.AsSpan().IndexOfAny(" \t\r\n\"") >= 0)
                {
                    // Unquoted values with spaces would split into several words; Word asks authors to quote them, and an
                    // empty value must still be a word so that comparisons against it keep their operands.
                    sb.Append('"').Append(value.Replace("\"", "\\\"", StringComparison.Ordinal)).Append('"');
                }
                else
                {
                    sb.Append(value);
                }
            }
        }

        return sb.ToString();
    }

    private static bool IsClosing(char open, char ch) => open == '"' ? ch == '"' : ch is '"' or '\u201D' or '\u201C';

    private static string OwnInstruction(Field f)
    {
        var sb = new System.Text.StringBuilder();
        foreach (MarkupNode n in f.InstructionNodes)
        {
            sb.Append(n.InnerText);
        }

        return sb.ToString();
    }

    private string ValueOf(Field child) => Evaluate(child) ?? child.ResultText;

    private string? Evaluate(Field f)
    {
        string instruction = BuildInstruction(f);
        FieldInstruction fi = FieldInstruction.Parse(instruction);
        switch (fi.Type)
        {
            case "MERGEFIELD":
                return MergeField(fi, f);
            case "IF":
                return If(fi, f);
            case "NEXT":
                Advance();
                return string.Empty;
            case "NEXTIF":
                if (Condition(fi, 0, f))
                {
                    Advance();
                }

                return string.Empty;
            case "SKIPIF":
                if (Condition(fi, 0, f))
                {
                    _state.Skip = true;
                }

                return string.Empty;
            case "MERGEREC":
                return Numeric(_state.Position + 1, fi);
            case "MERGESEQ":
                return Numeric(_state.MergedCount + 1, fi);
            case "SET":
                if (fi.Arguments.Count >= 1)
                {
                    _state.Variables[fi.Arguments[0]] = fi.Arguments.Count >= 2 ? fi.Arguments[1] : string.Empty;
                }
                else
                {
                    Warn("mailmerge.field-malformed", "A SET field has no name.", f);
                }

                return string.Empty;
            case "ASK":
                if (fi.Arguments.Count >= 1)
                {
                    _state.Variables[fi.Arguments[0]] = Answer(fi.Arguments[0], fi);
                }
                else
                {
                    Warn("mailmerge.field-malformed", "An ASK field has no bookmark name.", f);
                }

                return string.Empty;
            case "FILLIN":
                return Answer(fi.FirstArgument ?? string.Empty, fi);
            case "REF":
                if (fi.FirstArgument is { } name && _state.Variables.TryGetValue(name, out string? v))
                {
                    return CaseSwitch(v, fi);
                }

                return null;
            case "ADDRESSBLOCK" or "GREETINGLINE" or "DATABASE" or "MERGEBARCODE" or "COMPARE":
                Warn("mailmerge.field-unsupported", fi.Type + " fields are not expanded by the merge and stay in the output.", f);
                return null;
            default:
                return null;
        }
    }

    private void Advance()
    {
        if (_state.Position + 1 < _state.Records.Count)
        {
            _state.Position++;
        }
        else
        {
            // Past the last record Word has nothing to show: later fields in the pass merge as blanks.
            _state.Exhausted = true;
        }
    }

    private string Answer(string key, FieldInstruction fi)
    {
        if (_state.Options.Answers is { } answers && answers.TryGetValue(key, out string? given))
        {
            return given;
        }

        return fi.Switch("d")?.Value ?? string.Empty;
    }

    private static string Numeric(int number, FieldInstruction fi)
    {
        string text = number.ToString(CultureInfo.InvariantCulture);
        if (fi.Switch("#")?.Value is { } picture)
        {
            text = WordPicture.FormatNumber(number, picture);
        }

        return CaseSwitch(text, fi);
    }

    private string MergeField(FieldInstruction fi, Field f)
    {
        string? name = fi.FirstArgument;
        if (string.IsNullOrEmpty(name))
        {
            Warn("mailmerge.field-malformed", "A MERGEFIELD has no field name.", f);
            return string.Empty;
        }

        ITemplateData? raw = Lookup(name);
        if (raw is null && !_state.Exhausted)
        {
            Warn("mailmerge.field-missing", "The data has no field '" + name + "'; it merges as blank.", f);
        }

        string text = ScalarText(raw);
        if (text.Length == 0)
        {
            return string.Empty;
        }

        // ECMA-376 Part 1 §17.16.4: a picture switch formats a value that is a date or a number; otherwise the text stays.
        if (fi.Switch("@")?.Value is { } datePicture && TryDate(raw, text, out DateTime date))
        {
            text = WordPicture.FormatDate(date, datePicture);
        }
        else if (fi.Switch("#")?.Value is { } numberPicture && TryNumber(raw, text, out double number))
        {
            text = WordPicture.FormatNumber(number, numberPicture);
        }

        text = CaseSwitch(text, fi);
        if (fi.Switch("b")?.Value is { } before)
        {
            text = before + text;
        }

        if (fi.Switch("f")?.Value is { } after)
        {
            text += after;
        }

        return text;
    }

    private static string CaseSwitch(string text, FieldInstruction fi)
    {
        foreach (FieldSwitch s in fi.Switches)
        {
            if (s.Name == "*" && s.Value is { } v)
            {
                string result = WordPicture.ApplyCase(text, v);
                if (!ReferenceEquals(result, text))
                {
                    return result;
                }
            }
        }

        return text;
    }

    private ITemplateData? Lookup(string name)
    {
        if (_state.Exhausted || _state.Position >= _state.Records.Count)
        {
            return null;
        }

        ITemplateData record = _state.Records[_state.Position];
        // Word writes field names with underscores where the data column has spaces.
        foreach (string candidate in new[] { name, name.Replace('_', ' '), name.Replace(' ', '_') })
        {
            if (record.TryGetMember(candidate, out ITemplateData? value) && value is not null)
            {
                return value;
            }
        }

        return null;
    }

    internal static string ScalarText(ITemplateData? d)
    {
        if (d is null || d.Kind != TemplateDataKind.Scalar)
        {
            return string.Empty;
        }

        switch (d.Raw)
        {
            case string s:
                return s;
            case DateTime dt:
                return dt.TimeOfDay == TimeSpan.Zero ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            case DateTimeOffset dto:
                return dto.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            case DateOnly d2:
                return d2.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            case bool b:
                return b ? "True" : "False";
        }

        return CalcConvert.ToText(d.Value);
    }

    private static bool TryDate(ITemplateData? d, string text, out DateTime date)
    {
        switch (d?.Raw)
        {
            case DateTime dt:
                date = dt;
                return true;
            case DateTimeOffset dto:
                date = dto.DateTime;
                return true;
            case DateOnly d2:
                date = d2.ToDateTime(TimeOnly.MinValue);
                return true;
        }

        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private static bool TryNumber(ITemplateData? d, string text, out double number)
    {
        if (d is { Kind: TemplateDataKind.Scalar } && d.Value.Kind == ValueKind.Number && d.Raw is not (DateTime or DateTimeOffset or DateOnly or TimeOnly or TimeSpan))
        {
            number = d.Value.Number;
            return true;
        }

        return double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out number);
    }

    // IF expression1 operator expression2 truetext falsetext (ECMA-376 Part 1 §17.16.5.28).
    private string If(FieldInstruction fi, Field f)
    {
        IReadOnlyList<string> a = fi.Arguments;
        if (a.Count < 4)
        {
            Warn("mailmerge.field-malformed", "An IF field needs two operands, an operator and the text for true.", f);
            return string.Empty;
        }

        return Condition(fi, 0, f) ? a[3] : (a.Count > 4 ? a[4] : string.Empty);
    }

    private bool Condition(FieldInstruction fi, int first, Field f)
    {
        IReadOnlyList<string> a = fi.Arguments;
        if (a.Count < first + 3 || !IsOperator(a[first + 1]))
        {
            Warn("mailmerge.field-malformed", fi.Type + " needs 'expression operator expression'.", f);
            return false;
        }

        return Compare(a[first], a[first + 1], a[first + 2]);
    }

    private static bool IsOperator(string s) => s is "=" or "<>" or "<" or ">" or "<=" or ">=";

    // Numbers compare as numbers when both sides are numbers; otherwise text compares without regard to case, and the right operand of
    // = and <> may use ? and * as wildcards.
    internal static bool Compare(string left, string op, string right)
    {
        left = left.Trim();
        right = right.Trim();
        int cmp;
        if (double.TryParse(left, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double x)
            && double.TryParse(right, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double y))
        {
            cmp = x.CompareTo(y);
        }
        else if (op is "=" or "<>" && right.AsSpan().IndexOfAny("*?") >= 0)
        {
            bool match = Wildcard(left, right);
            return op == "=" ? match : !match;
        }
        else
        {
            cmp = string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
        }

        return op switch
        {
            "=" => cmp == 0,
            "<>" => cmp != 0,
            "<" => cmp < 0,
            ">" => cmp > 0,
            "<=" => cmp <= 0,
            _ => cmp >= 0,
        };
    }

    private static bool Wildcard(string text, string pattern)
    {
        int t = 0, p = 0, star = -1, mark = 0;
        while (t < text.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' || char.ToUpperInvariant(pattern[p]) == char.ToUpperInvariant(text[t])) && pattern[p] != '*')
            {
                t++;
                p++;
            }
            else if (p < pattern.Length && pattern[p] == '*')
            {
                star = p++;
                mark = t;
            }
            else if (star >= 0)
            {
                p = star + 1;
                t = ++mark;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*')
        {
            p++;
        }

        return p == pattern.Length;
    }

    private void Warn(string code, string message, Field f) =>
        _state.Warnings.Add(new Defect(DefectKind.Value, code, DefectSeverity.Repaired, message, DefectLocation.InPart(_partName, f.BeginNode.SourceStart)));

    // ---- Editing: a merged field is replaced by plain runs holding its result.

    private void Apply(Pending p)
    {
        Field f = p.Field;
        if (p.InCode)
        {
            ReplaceWithInstruction(f, p.Text);
            return;
        }

        if (f.Kind == FieldKind.Simple)
        {
            if (p.Text.Length == 0)
            {
                f.BeginNode.Remove();
                return;
            }

            f.SetResult(p.Text);
            var inner = new System.Text.StringBuilder();
            foreach (MarkupNode n in f.BeginNode.ChildElements)
            {
                inner.Append(n.OuterXml);
            }

            f.BeginNode.ReplaceWithXml(HubXml.Bytes(inner.ToString()));
            return;
        }

        if (f.EndNode.IsNull)
        {
            Warn("mailmerge.field-unbalanced", "A field that never ends is left as it is.", f);
            return;
        }

        var removed = new HashSet<MarkupNode>();
        if (p.Text.Length == 0)
        {
            RemoveFieldRuns(f, removed);
            return;
        }

        // Fields in the instruction vanish with it; capture them before SetResult forgets its children.
        List<Field> codeChildren = p.InCodeChildren;
        MarkupNode begin = f.BeginNode;
        MarkupNode end = f.EndNode;
        var instructionNodes = new List<MarkupNode>(f.InstructionNodes);
        f.SetResult(p.Text);
        MarkupNode separate = f.SeparateNode;
        foreach (Field c in codeChildren)
        {
            RemoveFieldRuns(c, removed);
        }

        foreach (MarkupNode n in instructionNodes)
        {
            RemoveNodeAndEmptyRun(n, removed);
        }

        RemoveFldChar(begin, removed);
        RemoveFldChar(separate, removed);
        RemoveFldChar(end, removed);
    }

    private static void ReplaceWithInstruction(Field f, string value)
    {
        string prefix = HubXml.Prefix(f.BeginNode, f.Owner.WordprocessingNamespaceUri, "w", out string decl);
        f.BeginNode.InsertXmlBefore(HubXml.Bytes(
            "<" + prefix + ":r" + decl + "><" + prefix + ":instrText xml:space=\"preserve\">" + HubXml.Escape(value) + "</" + prefix + ":instrText></" + prefix + ":r>"));
        RemoveFieldRuns(f, []);
    }

    /// <summary>Removes every run of a field (code, separator, result, end), nested fields included.</summary>
    private static void RemoveFieldRuns(Field f, HashSet<MarkupNode> removed)
    {
        if (f.Kind == FieldKind.Simple)
        {
            RemoveNode(f.BeginNode, removed);
            return;
        }

        RemoveNode(f.BeginNode, removed);
        foreach (MarkupNode n in f.InstructionNodes)
        {
            RemoveNode(n.Parent, removed);
        }

        RemoveNode(f.SeparateNode, removed);
        foreach (MarkupNode run in f.ResultRuns)
        {
            RemoveNode(run, removed);
        }

        RemoveNode(f.EndNode, removed);
        foreach (Field c in f.Children)
        {
            RemoveFieldRuns(c, removed);
        }
    }

    private static void RemoveNode(MarkupNode node, HashSet<MarkupNode> removed)
    {
        if (!node.IsNull && removed.Add(node))
        {
            node.Remove();
        }
    }

    private static void RemoveNodeAndEmptyRun(MarkupNode node, HashSet<MarkupNode> removed)
    {
        if (node.IsNull || removed.Contains(node))
        {
            return;
        }

        MarkupNode run = node.Parent;
        RemoveNode(node, removed);
        RemoveRunIfEmpty(run, removed);
    }

    private static void RemoveFldChar(MarkupNode run, HashSet<MarkupNode> removed)
    {
        if (run.IsNull || removed.Contains(run))
        {
            return;
        }

        MarkupNode ch = HubXml.FirstChildElement(run, "fldChar");
        if (!ch.IsNull)
        {
            RemoveNode(ch, removed);
        }

        RemoveRunIfEmpty(run, removed);
    }

    private static void RemoveRunIfEmpty(MarkupNode run, HashSet<MarkupNode> removed)
    {
        if (run.IsNull || removed.Contains(run) || run.Kind != MarkupNodeKind.Element || run.LocalNameString != "r")
        {
            return;
        }

        for (MarkupNode n = run.FirstChild; !n.IsNull; n = n.NextSibling)
        {
            if (n.Kind == MarkupNodeKind.Element && n.LocalNameString != "rPr")
            {
                return;
            }
        }

        RemoveNode(run, removed);
    }
}

/// <summary>The state shared by the passes of one merge.</summary>
internal sealed class MergeState
{
    public MergeState(IReadOnlyList<ITemplateData> records, MailMergeOptions options)
    {
        Records = records;
        Options = options;
    }

    public IReadOnlyList<ITemplateData> Records { get; }

    public MailMergeOptions Options { get; }

    public Dictionary<string, string> Variables { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<Defect> Warnings { get; } = [];

    /// <summary>Index of the current record.</summary>
    public int Position { get; set; }

    /// <summary>Documents produced so far (<c>MERGESEQ</c>).</summary>
    public int MergedCount { get; set; }

    /// <summary>A <c>SKIPIF</c> was true in this pass.</summary>
    public bool Skip { get; set; }

    /// <summary>A <c>NEXT</c> ran past the last record.</summary>
    public bool Exhausted { get; set; }
}
