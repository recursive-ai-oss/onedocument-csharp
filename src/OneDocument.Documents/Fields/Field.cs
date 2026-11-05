using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Schema;

namespace RecursiveAI.OneDocument.Documents.Fields;

/// <summary>How a field is written in the markup.</summary>
public enum FieldKind
{
    /// <summary>A <c>w:fldSimple</c> element: the instruction is an attribute, the cached result its content.</summary>
    Simple = 0,

    /// <summary>A complex field: <c>w:fldChar</c> begin, instruction runs, separate, result, end. It may span runs, paragraphs and table cells, and nest.</summary>
    Complex = 1,
}

/// <summary>
/// A field in a document part: its instruction (the code) and its cached result. The model never evaluates a field; a result
/// is whatever the file stored. Editing replaces the instruction or the cached result and leaves the rest of the markup alone.
/// </summary>
/// <remarks>
/// ECMA-376 Part 1 §17.16 (fields: <c>fldSimple</c>, <c>fldChar</c>): a complex field is a begin character, the
/// instruction text, an optional separator and the result runs, then an end character. Fields are listed in the order their
/// begin characters appear, so a field comes before the fields nested in it. The objects are snapshots of the tree at scan time:
/// after an edit made through another route, scan again.
/// </remarks>
public sealed class Field
{
    private readonly StringBuilder _instruction = new();
    private readonly StringBuilder _result = new();
    private readonly List<MarkupNode> _instructionNodes = [];
    private readonly List<MarkupNode> _resultRuns = [];
    private readonly List<MarkupNode> _ownRuns = [];
    private readonly List<Field> _children = [];

    internal Field(Document owner, FieldKind kind, Field? parent, MarkupNode begin)
    {
        Owner = owner;
        Kind = kind;
        Parent = parent;
        Depth = parent is null ? 0 : parent.Depth + 1;
        BeginNode = begin;
    }

    /// <summary>Gets the document the field belongs to.</summary>
    public Document Owner { get; }

    /// <summary>Gets how the field is written.</summary>
    public FieldKind Kind { get; }

    /// <summary>Gets the field this one is nested in (in its instruction or its result), or null.</summary>
    public Field? Parent { get; }

    /// <summary>Gets the fields nested directly in this one, in order.</summary>
    public IReadOnlyList<Field> Children => _children;

    /// <summary>Gets the nesting depth: 0 for an outermost field.</summary>
    public int Depth { get; }

    /// <summary>Gets the <c>w:fldSimple</c> element, or for a complex field the run that holds the begin character.</summary>
    public MarkupRef Markup => new(BeginNode);

    /// <summary>Gets the run with the separator character, or null for a simple field or a field without a result.</summary>
    public MarkupRef? SeparatorRun => SeparateNode.IsNull ? null : new MarkupRef(SeparateNode);

    /// <summary>Gets the run with the end character; null for a simple field or a field that is never closed.</summary>
    public MarkupRef? EndRun => EndNode.IsNull ? null : new MarkupRef(EndNode);

    /// <summary>Gets a value indicating whether the field is closed: always true for a simple field, true for a complex field when its end character was found.</summary>
    public bool IsComplete { get; internal set; }

    /// <summary>Gets a value indicating whether the field has a result: a separator was found, or the simple field has content.</summary>
    public bool HasResult => Kind == FieldKind.Simple || !SeparateNode.IsNull;

    /// <summary>Gets a value indicating whether the file marks the cached result as out of date (<c>w:dirty</c>).</summary>
    public bool IsDirty { get; internal set; }

    /// <summary>Gets a value indicating whether the file locks the field against updates (<c>w:fldLock</c>).</summary>
    public bool IsLocked { get; internal set; }

    /// <summary>
    /// Gets the instruction text. For a complex field it is the <c>w:instrText</c> of the field itself, concatenated, followed by
    /// the cached result of any field nested in the instruction (as Word shows it).
    /// </summary>
    public string Instruction => _instruction.ToString();

    /// <summary>Gets the instruction parsed into type, arguments and switches.</summary>
    public FieldInstruction Parsed => FieldInstruction.Parse(Instruction);

    /// <summary>Gets the field type in upper case (<c>PAGE</c>, <c>TOC</c>, …); empty when the instruction is empty.</summary>
    public string Type => Parsed.Type;

    /// <summary>
    /// Gets the text of the cached result: the text of its runs including the results of nested fields (their code does not
    /// count), tabs as <c>\t</c> and breaks as <c>\n</c>. Empty when there is no result.
    /// </summary>
    public string ResultText => _result.ToString();

    internal MarkupNode BeginNode { get; }

    internal MarkupNode SeparateNode { get; set; }

    internal MarkupNode EndNode { get; set; }

    internal bool InResult { get; set; }

    internal StringBuilder InstructionBuilder => _instruction;

    internal StringBuilder ResultBuilder => _result;

    internal List<MarkupNode> InstructionNodes => _instructionNodes;

    internal List<MarkupNode> ResultRuns => _resultRuns;

    internal List<MarkupNode> OwnRuns => _ownRuns;

    internal List<Field> ChildList => _children;

    /// <summary>
    /// Replaces the instruction. A simple field gets a new <c>w:instr</c>; a complex field keeps its first instruction run, sets its
    /// text and drops the other instruction runs of its own (nested fields are left alone). The text is stored exactly as given:
    /// include the spaces Word puts around it (<c> PAGE </c>).
    /// </summary>
    /// <param name="instruction">The new instruction text.</param>
    /// <exception cref="ArgumentException">The text holds a character XML cannot carry.</exception>
    public void SetInstruction(string instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        string escaped = HubXml.Escape(instruction);
        if (Kind == FieldKind.Simple)
        {
            BeginNode.SetAttribute(BeginNode.NamespaceUri, "instr", instruction, "w");
        }
        else if (_instructionNodes.Count > 0)
        {
            MarkupNode keeper = _instructionNodes[0];
            keeper.SetText(instruction);
            keeper.SetAttribute("xml:space", "preserve");
            for (int i = 1; i < _instructionNodes.Count; i++)
            {
                MarkupNode doomed = _instructionNodes[i];
                MarkupNode run = doomed.Parent;
                doomed.Remove();
                RemoveIfEmptyRun(run);
            }

            _instructionNodes.RemoveRange(1, _instructionNodes.Count - 1);
        }
        else
        {
            string prefix = HubXml.Prefix(BeginNode, Owner.WordprocessingNamespaceUri, "w", out string decl);
            string rPr = RunPropertiesXml(BeginNode);
            BeginNode.InsertXmlAfter(HubXml.Bytes("<" + prefix + ":r" + decl + ">" + rPr + "<" + prefix + ":instrText xml:space=\"preserve\">" + escaped + "</" + prefix + ":instrText></" + prefix + ":r>"));
            MarkupNode inserted = BeginNode.NextSibling;
            while (!inserted.IsNull && inserted.Kind != MarkupNodeKind.Element)
            {
                inserted = inserted.NextSibling;
            }

            _instructionNodes.Add(HubXml.FirstChildElement(inserted, "instrText"));
        }

        // Nested results that were part of the old instruction are gone from the text now.
        _instruction.Clear().Append(instruction);
    }

    /// <summary>
    /// Replaces the cached result with plain text. The first plain result run is kept (so its character formatting survives) and
    /// gets the text; every other result run, nested fields included, is removed. A field without a result gets a separator and a
    /// run formatted like the begin character. Tabs and line feeds in the text become <c>w:tab</c> and <c>w:br</c>. A result that
    /// ran over several paragraphs keeps those paragraphs, now without the removed runs.
    /// </summary>
    /// <param name="text">The new result text.</param>
    /// <exception cref="InvalidOperationException">A complex field is not closed, so there is nowhere to put a result.</exception>
    /// <exception cref="ArgumentException">The text holds a character XML cannot carry.</exception>
    public void SetResult(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (Kind == FieldKind.Complex && EndNode.IsNull)
        {
            throw new InvalidOperationException("The field has no end character, so its result cannot be replaced.");
        }

        string prefix = HubXml.Prefix(BeginNode, Owner.WordprocessingNamespaceUri, "w", out string decl);
        string content = HubXml.RunContent(prefix, text);
        MarkupNode keeper = default;
        foreach (MarkupNode run in _ownRuns)
        {
            if (HubXml.FirstChildElement(run, "fldChar").IsNull)
            {
                keeper = run;
                break;
            }
        }

        if (!keeper.IsNull)
        {
            var doomed = new HashSet<MarkupNode>();
            foreach (MarkupNode run in _resultRuns)
            {
                if (run != keeper)
                {
                    doomed.Add(run);
                }
            }

            foreach (MarkupNode run in _resultRuns)
            {
                if (run == keeper || HasAncestorIn(run, doomed, keeper))
                {
                    continue;
                }

                run.Remove();
            }

            HubXml.ClearChildrenExcept(keeper, "rPr");
            keeper.AppendXml(HubXml.Bytes(content));
        }
        else if (Kind == FieldKind.Simple)
        {
            BeginNode.AppendXml(HubXml.Bytes("<" + prefix + ":r>" + content + "</" + prefix + ":r>"));
            keeper = BeginNode.LastChild;
        }
        else
        {
            string rPr = RunPropertiesXml(BeginNode);
            string separate = SeparateNode.IsNull
                ? "<" + prefix + ":r" + decl + ">" + rPr + "<" + prefix + ":fldChar " + prefix + ":fldCharType=\"separate\"/></" + prefix + ":r>"
                : string.Empty;
            string run = "<" + prefix + ":r" + (separate.Length == 0 ? decl : string.Empty) + ">" + rPr + content + "</" + prefix + ":r>";
            EndNode.InsertXmlBefore(HubXml.Bytes(separate + run));
            keeper = EndNode.PreviousSibling;
            while (!keeper.IsNull && keeper.Kind != MarkupNodeKind.Element)
            {
                keeper = keeper.PreviousSibling;
            }

            if (separate.Length > 0)
            {
                MarkupNode sep = keeper.PreviousSibling;
                while (!sep.IsNull && sep.Kind != MarkupNodeKind.Element)
                {
                    sep = sep.PreviousSibling;
                }

                SeparateNode = sep;
            }
        }

        _resultRuns.Clear();
        _resultRuns.Add(keeper);
        _ownRuns.Clear();
        _ownRuns.Add(keeper);
        _result.Clear().Append(text);
        _children.Clear();
    }

    private static bool HasAncestorIn(MarkupNode run, HashSet<MarkupNode> doomed, MarkupNode keeper)
    {
        for (MarkupNode p = run.Parent; !p.IsNull; p = p.Parent)
        {
            if (p == keeper || doomed.Contains(p))
            {
                return true;
            }
        }

        return false;
    }

    private static string RunPropertiesXml(MarkupNode run)
    {
        MarkupNode rPr = HubXml.FirstChildElement(run, "rPr");
        return rPr.IsNull ? string.Empty : rPr.OuterXml;
    }

    private static void RemoveIfEmptyRun(MarkupNode run)
    {
        if (run.IsNull || run.Kind != MarkupNodeKind.Element || run.LocalNameString != "r")
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

        run.Remove();
    }

    /// <inheritdoc/>
    public override string ToString() => Type + (HasResult ? " = " + ResultText : string.Empty);
}

/// <summary>Finds the fields of a part by one walk over its markup.</summary>
internal static class FieldScanner
{
    private const string McNamespace = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    public static List<Field> Scan(Document owner, MarkupRef root, string partName)
    {
        var all = new List<Field>();
        if (!root.IsValid)
        {
            return all;
        }

        var state = new ScanState(owner, partName, all);
        Walk(state, root.Node);
        foreach (Field open in state.Stack)
        {
            Report(state, "docx.field-unbalanced", "A field begins here and is never ended; it is read to the end of the part.", open.BeginNode);
        }

        return all;
    }

    private sealed class ScanState(Document owner, string partName, List<Field> all)
    {
        public readonly Document Owner = owner;
        public readonly string PartName = partName;
        public readonly List<Field> All = all;
        public readonly List<Field> Stack = [];
        public int IgnoredBegins;
        public bool ReportedDepth;
        public MarkupNode CurrentRun;
    }

    private static void Walk(ScanState s, MarkupNode node)
    {
        for (MarkupNode n = node.FirstChild; !n.IsNull; n = n.NextSibling)
        {
            if (n.Kind != MarkupNodeKind.Element)
            {
                continue;
            }

            string ns = n.NamespaceUri;
            ReadOnlySpan<byte> local = n.LocalName;
            if (ns == McNamespace)
            {
                // Only the branch Office uses; the fallback repeats the same content in an older form.
                if (!local.SequenceEqual("Fallback"u8))
                {
                    Walk(s, n);
                }

                continue;
            }

            if (!IsWordprocessing(ns))
            {
                Walk(s, n);
                continue;
            }

            if (local.SequenceEqual("fldSimple"u8))
            {
                WalkSimple(s, n);
            }
            else if (local.SequenceEqual("r"u8))
            {
                MarkupNode saved = s.CurrentRun;
                s.CurrentRun = n;
                foreach (Field f in s.Stack)
                {
                    if (f.InResult)
                    {
                        f.ResultRuns.Add(n);
                    }
                }

                if (s.Stack.Count > 0 && s.Stack[^1].InResult)
                {
                    s.Stack[^1].OwnRuns.Add(n);
                }

                Walk(s, n);
                s.CurrentRun = saved;
            }
            else if (local.SequenceEqual("fldChar"u8))
            {
                FieldChar(s, n);
            }
            else if (local.SequenceEqual("instrText"u8))
            {
                if (s.Stack.Count > 0 && !s.Stack[^1].InResult)
                {
                    Field top = s.Stack[^1];
                    top.InstructionBuilder.Append(n.InnerText);
                    top.InstructionNodes.Add(n);
                }
            }
            else if (local.SequenceEqual("t"u8))
            {
                AppendResult(s, n.InnerText);
            }
            else if (local.SequenceEqual("tab"u8) && s.CurrentRun.IsNull is false)
            {
                AppendResult(s, "\t");
            }
            else if ((local.SequenceEqual("br"u8) || local.SequenceEqual("cr"u8)) && s.CurrentRun.IsNull is false)
            {
                AppendResult(s, "\n");
            }
            else if (local.SequenceEqual("delText"u8) || local.SequenceEqual("delInstrText"u8))
            {
                // Deleted text is not part of what the document shows.
            }
            else
            {
                Walk(s, n);
            }
        }
    }

    private static void WalkSimple(ScanState s, MarkupNode n)
    {
        Field? parent = s.Stack.Count > 0 ? s.Stack[^1] : null;
        if (s.Stack.Count >= s.Owner.Context.Limits.MaxRecursionDepth)
        {
            ReportDepth(s, n);
            Walk(s, n);
            return;
        }

        var field = new Field(s.Owner, FieldKind.Simple, parent, n) { IsComplete = true };
        field.InstructionBuilder.Append(n.GetAttribute(n.NamespaceUri, "instr") ?? string.Empty);
        field.IsDirty = Wml.OnOff(n.GetAttribute(n.NamespaceUri, "dirty")) ?? false;
        field.IsLocked = Wml.OnOff(n.GetAttribute(n.NamespaceUri, "fldLock")) ?? false;
        field.InResult = true;
        Register(s, field, parent);
        s.Stack.Add(field);
        Walk(s, n);
        s.Stack.RemoveAt(s.Stack.Count - 1);
        Finished(s, field);
    }

    private static void FieldChar(ScanState s, MarkupNode n)
    {
        string? type = n.GetAttribute(n.NamespaceUri, "fldCharType");
        switch (type)
        {
            case "begin":
                {
                    Field? parent = s.Stack.Count > 0 ? s.Stack[^1] : null;
                    if (s.Stack.Count >= s.Owner.Context.Limits.MaxRecursionDepth)
                    {
                        ReportDepth(s, n);
                        s.IgnoredBegins++;
                        return;
                    }

                    MarkupNode run = s.CurrentRun.IsNull ? n : s.CurrentRun;
                    var field = new Field(s.Owner, FieldKind.Complex, parent, run)
                    {
                        IsDirty = Wml.OnOff(n.GetAttribute(n.NamespaceUri, "dirty")) ?? false,
                        IsLocked = Wml.OnOff(n.GetAttribute(n.NamespaceUri, "fldLock")) ?? false,
                    };
                    Register(s, field, parent);
                    s.Stack.Add(field);
                    break;
                }

            case "separate":
                {
                    if (s.IgnoredBegins > 0)
                    {
                        return;
                    }

                    if (s.Stack.Count == 0 || s.Stack[^1].InResult)
                    {
                        Report(s, "docx.field-unbalanced", "A field separator has no open field to belong to; it is ignored.", n);
                        return;
                    }

                    Field top = s.Stack[^1];
                    top.SeparateNode = s.CurrentRun.IsNull ? n : s.CurrentRun;
                    top.InResult = true;
                    break;
                }

            case "end":
                {
                    if (s.IgnoredBegins > 0)
                    {
                        s.IgnoredBegins--;
                        return;
                    }

                    if (s.Stack.Count == 0)
                    {
                        Report(s, "docx.field-unbalanced", "A field end has no field begin before it; it is ignored.", n);
                        return;
                    }

                    Field top = s.Stack[^1];
                    s.Stack.RemoveAt(s.Stack.Count - 1);
                    top.EndNode = s.CurrentRun.IsNull ? n : s.CurrentRun;
                    top.IsComplete = true;
                    if (top.ResultRuns.Count > 0 && top.ResultRuns[^1] == top.EndNode)
                    {
                        top.ResultRuns.RemoveAt(top.ResultRuns.Count - 1);
                    }

                    if (top.OwnRuns.Count > 0 && top.OwnRuns[^1] == top.EndNode)
                    {
                        top.OwnRuns.RemoveAt(top.OwnRuns.Count - 1);
                    }

                    Finished(s, top);
                    break;
                }

            default:
                Report(s, "docx.field-unbalanced", "A field character has no valid w:fldCharType; it is ignored.", n);
                break;
        }
    }

    private static void Register(ScanState s, Field field, Field? parent)
    {
        s.All.Add(field);
        parent?.ChildList.Add(field);
    }

    private static void Finished(ScanState s, Field field)
    {
        // A field inside another field's instruction shows its result there (Word's field-code view).
        if (field.Parent is { InResult: false } p)
        {
            p.InstructionBuilder.Append(field.ResultText);
        }
    }

    private static void AppendResult(ScanState s, string text)
    {
        foreach (Field f in s.Stack)
        {
            if (f.InResult)
            {
                f.ResultBuilder.Append(text);
            }
        }
    }

    private static void ReportDepth(ScanState s, MarkupNode at) =>
        Report(s, "docx.field-too-deep", "Fields nest deeper than the recursion limit; the inner fields are read as plain content.", at, ref s.ReportedDepth);

    private static void Report(ScanState s, string code, string message, MarkupNode at, ref bool once)
    {
        if (once)
        {
            return;
        }

        once = true;
        Report(s, code, message, at);
    }

    private static void Report(ScanState s, string code, string message, MarkupNode at) =>
        s.Owner.Context.Defects.Report(new Defect(DefectKind.Structure, code, DefectSeverity.Repaired, message, DefectLocation.InPart(s.PartName, at.SourceStart)));

    private static bool IsWordprocessing(string ns) =>
        ns == HubXml.WordprocessingNamespace || ns == "http://purl.oclc.org/ooxml/wordprocessingml/main";
}
