using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Templating;

/// <summary>A template that has been read and checked. Rendering it with data never re-reads the text.</summary>
public sealed class ParsedTemplate
{
    internal ParsedTemplate(TemplateSource source, TemplateOptions options, TemplateSyntax syntax, IReadOnlyList<TemplateBlockInfo> blocks)
    {
        Source = source;
        Options = options;
        Syntax = syntax;
        Blocks = blocks;
        Defects = syntax.Collect();
    }

    internal TemplateSource Source { get; }

    internal TemplateOptions Options { get; }

    internal TemplateSyntax Syntax { get; }

    /// <summary>Every problem found while reading the template, located, in the order found. Errors have severity <see cref="DefectSeverity.Fatal"/>; warnings (<c>template.unknown-function</c>) have <see cref="DefectSeverity.Dropped"/>.</summary>
    public IReadOnlyList<Defect> Defects { get; }

    /// <summary>The loop and condition blocks in the order their opening tags appear, as the binder's scope callback saw them.</summary>
    public IReadOnlyList<TemplateBlockInfo> Blocks { get; }

    /// <summary>True when there is no error: the template can be rendered.</summary>
    public bool IsValid => !Defects.Any(static d => d.Severity == DefectSeverity.Fatal);

    /// <summary>Renders the template.</summary>
    /// <param name="data">The data. The root must be an object (its members are the names templates use); null is an empty object.</param>
    /// <param name="cancellationToken">Cancels a long render between loop iterations.</param>
    /// <returns>The output units and the warnings.</returns>
    /// <exception cref="TemplateException">The template is invalid (the first error), or strict mode met missing data or an error value.</exception>
    /// <exception cref="OfficeLimitException">A limit was exceeded: iterations, output size, block depth, evaluation steps.</exception>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    public TemplateResult Render(ITemplateData? data, CancellationToken cancellationToken = default)
    {
        Defect? fatal = Defects.FirstOrDefault(static d => d.Severity == DefectSeverity.Fatal);
        if (fatal is not null)
        {
            throw new TemplateException(fatal);
        }

        var state = new RenderState(Options, data ?? TemplateData.Null, cancellationToken);
        var renderer = new Renderer(this, state);
        renderer.Run();
        return new TemplateResult(renderer.Units, state.Warnings);
    }

    /// <summary>Renders the template with data given as any value <see cref="TemplateData.From"/> understands.</summary>
    /// <param name="data">The data.</param>
    /// <param name="cancellationToken">Cancels a long render.</param>
    /// <returns>The result.</returns>
    /// <exception cref="TemplateException">As <see cref="Render(ITemplateData?, CancellationToken)"/>.</exception>
    public TemplateResult Render(object? data, CancellationToken cancellationToken = default) =>
        Render(TemplateData.From(data, Options.MaxRows), cancellationToken);

    internal SeqNode Root { get; set; } = new();
}

/// <summary>
/// The template engine core: reads a template from the units a binder supplies, checks it, and renders it against data. It knows no document format;
/// the DOCX, XLSX and PPTX binders turn their markup into <see cref="TemplateSource"/> units and rebuild the document from the rendered units.
/// </summary>
public static class TemplateEngine
{
    /// <summary>Reads and checks a template. Errors do not throw: they are in <see cref="ParsedTemplate.Defects"/>, so a binder can show them all with their places.</summary>
    /// <param name="source">The units.</param>
    /// <param name="options">Options, or null for the defaults.</param>
    /// <returns>The template; see <see cref="ParsedTemplate.IsValid"/>.</returns>
    /// <exception cref="OfficeLimitException">Block nesting deeper than the limit.</exception>
    /// <exception cref="ArgumentException">The delimiters are unusable.</exception>
    public static ParsedTemplate Parse(TemplateSource source, TemplateOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        options ??= new TemplateOptions();
        options.Delimiters.Validate();
        var syntax = new TemplateSyntax(source, options);
        syntax.Scan();
        syntax.BuildTree(syntax.Tokens);
        Token[] moved = syntax.ApplyScopes(syntax.Tokens);
        if (!ReferenceEquals(moved, syntax.Tokens))
        {
            // The binder widened some blocks: the tokens were reordered, so the tree is built again.
            syntax.BuildTree(moved);
        }

        var blocks = new List<TemplateBlockInfo>();
        foreach ((ForNode? f, IfNode? n) in syntax.Blocks)
        {
            Token open = f?.Open ?? n!.Open;
            Token close = f?.Close ?? n!.Close;
            blocks.Add(new TemplateBlockInfo(f is not null ? "for" : "if", f is not null ? f.Scope : n!.Scope, open.Unit, close.Unit, f is not null ? f.Source.Text : n!.Branches[0].Condition?.Text ?? string.Empty));
        }

        var parsed = new ParsedTemplate(source, options, syntax, blocks) { Root = syntax.Root };
        if (options.Defects is { } sink)
        {
            foreach (Defect d in parsed.Defects)
            {
                sink.Report(d);
            }
        }

        return parsed;
    }

    /// <summary>Reads, checks and renders in one call.</summary>
    /// <param name="source">The units.</param>
    /// <param name="data">The data.</param>
    /// <param name="options">Options, or null.</param>
    /// <param name="cancellationToken">Cancels a long render.</param>
    /// <returns>The result.</returns>
    /// <exception cref="TemplateException">The template has an error (the first), or strict mode met missing data.</exception>
    public static TemplateResult Render(TemplateSource source, ITemplateData? data, TemplateOptions? options = null, CancellationToken cancellationToken = default) =>
        Parse(source, options).Render(data, cancellationToken);

    /// <summary>Renders plain text, one unit per line; the shape the tests and the command line use.</summary>
    /// <param name="template">The template text.</param>
    /// <param name="data">The data, as <see cref="TemplateData.From"/> reads it.</param>
    /// <param name="options">Options, or null.</param>
    /// <returns>The output text, lines joined with <c>\n</c>.</returns>
    /// <exception cref="TemplateException">As <see cref="Render(TemplateSource, ITemplateData?, TemplateOptions?, CancellationToken)"/>.</exception>
    public static string RenderText(string template, object? data, TemplateOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(template);
        options ??= new TemplateOptions();
        return Parse(TemplateSource.FromText(template), options).Render(data).ToText();
    }
}

// What a render shares between a template and the templates it includes.
internal sealed class RenderState
{
    public RenderState(TemplateOptions options, ITemplateData root, CancellationToken ct)
    {
        Options = options;
        Root = root;
        Ct = ct;
        Limits = options.Limits ?? OfficeLimits.Default;
        Resolver = new TemplateResolver(this);
        var ev = new EvaluatorOptions { Clock = options.Clock, RandomSeed = options.RandomSeed };
        Evaluator = new Evaluator(Resolver, options.Functions ?? TemplateSyntax.DefaultRegistry, Limits, ev);
    }

    public TemplateOptions Options { get; }

    public ITemplateData Root { get; }

    public CancellationToken Ct { get; }

    public OfficeLimits Limits { get; }

    public TemplateResolver Resolver { get; }

    public Evaluator Evaluator { get; }

    public List<Defect> Warnings { get; } = [];

    public List<Dictionary<string, ITemplateData>> Frames { get; } = [];

    public List<string> PendingMissing { get; } = [];

    public long Iterations { get; set; }

    public long OutputCharacters { get; set; }

    public int Depth { get; set; }

    public Dictionary<string, ParsedTemplate> IncludeCache { get; } = new(StringComparer.Ordinal);

    // ---- path resolution (data level) -----------------------------------------------------------------------------------------

    public ITemplateData? ResolvePath(IReadOnlyList<string> path, out bool missing)
    {
        missing = false;
        ITemplateData? node = null;
        string first = path[0];
        for (int i = Frames.Count - 1; i >= 0 && node is null; i--)
        {
            if (Frames[i].TryGetValue(first, out ITemplateData? v))
            {
                node = v;
            }
        }

        if (node is null)
        {
            if (Root.Kind == TemplateDataKind.Record && Root.TryGetMember(first, out ITemplateData? m) && m is not null)
            {
                node = m;
            }
            else
            {
                missing = true;
                return null;
            }
        }

        for (int i = 1; i < path.Count; i++)
        {
            node = Step(node, path[i], out bool miss);
            if (miss)
            {
                missing = true;
                return null;
            }
        }

        return node;
    }

    private static ITemplateData Step(ITemplateData node, string name, out bool missing)
    {
        missing = false;
        switch (node.Kind)
        {
            case TemplateDataKind.Null:
                return TemplateData.Null;
            case TemplateDataKind.Record:
                if (node.TryGetMember(name, out ITemplateData? m) && m is not null)
                {
                    return m;
                }

                missing = true;
                return TemplateData.Null;
            case TemplateDataKind.Sequence:
                {
                    // Array lifting: a member of a collection is the collection of that member over its items.
                    var lifted = new List<ITemplateData>();
                    bool any = false;
                    foreach (ITemplateData item in node.Items)
                    {
                        ITemplateData next = Step(item, name, out bool miss);
                        if (miss)
                        {
                            next = TemplateData.Null;
                        }
                        else
                        {
                            any = true;
                        }

                        if (next.Kind == TemplateDataKind.Sequence)
                        {
                            lifted.AddRange(next.Items);
                        }
                        else
                        {
                            lifted.Add(next);
                        }
                    }

                    if (!any && node.Items.Count > 0)
                    {
                        if (name.Equals("length", StringComparison.OrdinalIgnoreCase) || name.Equals("count", StringComparison.OrdinalIgnoreCase))
                        {
                            return TemplateData.Scalar(CalcValue.FromNumber(node.Items.Count));
                        }

                        missing = true;
                        return TemplateData.Null;
                    }

                    if (node.Items.Count == 0 && (name.Equals("length", StringComparison.OrdinalIgnoreCase) || name.Equals("count", StringComparison.OrdinalIgnoreCase)))
                    {
                        return TemplateData.Scalar(CalcValue.FromNumber(0));
                    }

                    return TemplateData.Sequence(lifted);
                }

            default:
                missing = true;
                return TemplateData.Null;
        }
    }

    public static CalcValue ToCalc(ITemplateData? node)
    {
        if (node is null)
        {
            return CalcValue.FromError(CalcError.Name);
        }

        switch (node.Kind)
        {
            case TemplateDataKind.Null:
                return CalcValue.Blank;
            case TemplateDataKind.Scalar:
                return node.Value;
            case TemplateDataKind.Sequence:
                {
                    IReadOnlyList<ITemplateData> items = node.Items;
                    if (items.Count == 0)
                    {
                        // An empty collection reads as one blank cell so SUM and friends give 0 (Excel would give #CALC!).
                        return CalcValue.FromArray(CalcArray.OfValue(CalcValue.Blank));
                    }

                    var cells = new CalcValue[items.Count];
                    for (int i = 0; i < cells.Length; i++)
                    {
                        ITemplateData item = items[i];
                        cells[i] = item.Kind switch
                        {
                            TemplateDataKind.Scalar => item.Value,
                            TemplateDataKind.Null => CalcValue.Blank,
                            _ => CalcValue.FromError(CalcError.Value),
                        };
                    }

                    return CalcValue.FromArray(new CalcArray(cells.Length, 1, cells));
                }

            default:
                return CalcValue.FromError(CalcError.Value);
        }
    }
}

// The evaluator's window onto template data. There is no grid: a cell reference such as REV7 reads the variable of that name, so a variable called
// rev7 still works inside a formula.
internal sealed class TemplateResolver(RenderState state) : ICalcResolver
{
    public CalcValue GetCell(string? sheet, int row, int column)
    {
        if (sheet is null && column is >= 1 and <= 16384 && row >= 1)
        {
            string name = ColumnName(column) + row.ToString(CultureInfo.InvariantCulture);
            ITemplateData? node = state.ResolvePath([name], out bool missing);
            if (!missing && node is { Kind: TemplateDataKind.Scalar or TemplateDataKind.Null })
            {
                return RenderState.ToCalc(node);
            }

            if (missing)
            {
                state.PendingMissing.Add(name);
            }
        }

        return CalcValue.FromError(CalcError.Ref);
    }

    public CalcValue ResolveName(string? scopeSheet, string name, in CalcCell origin)
    {
        ITemplateData? node = state.ResolvePath([name], out bool missing);
        if (missing)
        {
            state.PendingMissing.Add(name);
            return CalcValue.FromError(CalcError.Name);
        }

        return RenderState.ToCalc(node);
    }

    public CalcValue ResolveMember(IReadOnlyList<string> segments)
    {
        ITemplateData? node = state.ResolvePath(segments, out bool missing);
        if (missing)
        {
            state.PendingMissing.Add(string.Join('.', segments));
            return CalcValue.FromError(CalcError.Name);
        }

        return RenderState.ToCalc(node);
    }

    private static string ColumnName(int column)
    {
        string name = string.Empty;
        while (column > 0)
        {
            column--;
            name = (char)('A' + (column % 26)) + name;
            column /= 26;
        }

        return name;
    }
}

internal sealed class Renderer
{
    private sealed class OutUnit(int source)
    {
        public int Source { get; } = source;

        public List<TemplatePiece> Pieces { get; } = [];
    }

    private readonly ParsedTemplate _template;
    private readonly RenderState _s;
    private readonly List<OutUnit> _units = [];
    private OutUnit? _cur;
    private bool _force;
    private int[][]? _segStarts;

    public Renderer(ParsedTemplate template, RenderState state)
    {
        _template = template;
        _s = state;
    }

    public IReadOnlyList<TemplateRenderedUnit> Units => [.. _units.Select(static u => new TemplateRenderedUnit(u.Source, u.Pieces))];

    public void Run()
    {
        if (_s.Depth > Math.Min(_s.Options.MaxBlockDepth, _s.Limits.MaxRecursionDepth))
        {
            throw new OfficeLimitException(nameof(TemplateOptions.MaxBlockDepth), _s.Options.MaxBlockDepth, _s.Depth);
        }

        Walk(_template.Root);
    }

    private TemplateSyntax Syntax => _template.Syntax;

    private void Walk(SeqNode seq)
    {
        foreach (Node node in seq.Items)
        {
            _s.Ct.ThrowIfCancellationRequested();
            switch (node)
            {
                case TextNode t:
                    EmitText(t.Token);
                    break;
                case OutputNode o:
                    EmitOutput(o);
                    break;
                case IfNode i:
                    WalkIf(i);
                    break;
                case ForNode f:
                    WalkFor(f);
                    break;
                case IncludeNode inc:
                    WalkInclude(inc);
                    break;
                case InsertNode ins:
                    WalkInsert(ins);
                    break;
            }
        }
    }

    // ---- output ----------------------------------------------------------------------------------------------------------------

    private void Enter(int unit)
    {
        if (_cur is null || _cur.Source != unit || _force)
        {
            _cur = new OutUnit(unit);
            _units.Add(_cur);
            _force = false;
        }
    }

    private void Add(TemplatePiece piece)
    {
        _s.OutputCharacters += piece.Text.Length;
        if (_s.OutputCharacters > _s.Options.MaxOutputCharacters)
        {
            throw new OfficeLimitException(nameof(TemplateOptions.MaxOutputCharacters), _s.Options.MaxOutputCharacters, _s.OutputCharacters);
        }

        List<TemplatePiece> pieces = _cur!.Pieces;
        if (piece.Kind == TemplatePieceKind.Text && pieces.Count > 0 && pieces[^1].Kind == TemplatePieceKind.Text && pieces[^1].Segment == piece.Segment && pieces[^1].Typed is null && piece.Typed is null)
        {
            pieces[^1] = pieces[^1] with { Text = pieces[^1].Text + piece.Text };
        }
        else
        {
            pieces.Add(piece);
        }
    }

    private int[] SegmentStarts(int unit)
    {
        _segStarts ??= new int[_template.Source.Units.Count][];
        if (_segStarts[unit] is { } have)
        {
            return have;
        }

        IReadOnlyList<TemplateSegment> segs = _template.Source.Units[unit].Segments;
        var starts = new int[segs.Count + 1];
        for (int i = 0; i < segs.Count; i++)
        {
            starts[i + 1] = starts[i] + (segs[i].Text?.Length ?? 0);
        }

        return _segStarts[unit] = starts;
    }

    private int SegmentAt(int unit, int offset)
    {
        int[] starts = SegmentStarts(unit);
        for (int i = 0; i < starts.Length - 1; i++)
        {
            if (offset < starts[i + 1])
            {
                return i;
            }
        }

        return Math.Max(0, starts.Length - 2);
    }

    private void EmitText(Token t)
    {
        string text = Syntax.UnitText(t.Unit);
        ReadOnlySpan<char> span = text.AsSpan(t.Start, t.End - t.Start);
        if (Syntax.BlockOnly[t.Unit] && TemplateSyntax.IsBlankText(span))
        {
            return;
        }

        Enter(t.Unit);
        if (t.End == t.Start)
        {
            return;
        }

        int[] starts = SegmentStarts(t.Unit);
        for (int k = 0; k < starts.Length - 1; k++)
        {
            int s = Math.Max(t.Start, starts[k]);
            int e = Math.Min(t.End, starts[k + 1]);
            if (e > s)
            {
                Add(new TemplatePiece(TemplatePieceKind.Text, k, text.Substring(s, e - s)));
            }
        }
    }

    private void EmitTag(Token t, TemplatePiece piece)
    {
        Enter(t.Unit);
        Add(piece);
    }

    // ---- expressions -----------------------------------------------------------------------------------------------------------

    private Defect Make(DefectKind kind, string code, DefectSeverity severity, string message, Expr e) =>
        new(kind, code, severity, message, Syntax.Locate(e.Unit, e.Offset));

    private void Missing(Expr e, string path)
    {
        Defect d = Make(DefectKind.Reference, "template.missing-data", DefectSeverity.Repaired, "No data named '" + path + "'; rendered empty.", e);
        if (_s.Options.Strict)
        {
            throw new TemplateException(d with { Severity = DefectSeverity.Fatal, Message = "No data named '" + path + "'." });
        }

        Warn(d);
    }

    private void Warn(Defect d)
    {
        if (_s.Warnings.Count < _s.Limits.MaxDefects)
        {
            _s.Warnings.Add(d);
            _s.Options.Defects?.Report(d);
        }
    }

    // The outcome of one expression: the data node it names (a bare path), or the calc value of a formula.
    private (ITemplateData? Node, CalcValue Value, bool IsMissing) Evaluate(Expr e)
    {
        if (e.Path is { } path)
        {
            ITemplateData? node = _s.ResolvePath(path, out bool missing);
            if (missing)
            {
                Missing(e, string.Join('.', path));
                return (TemplateData.Null, CalcValue.Blank, true);
            }

            return (node, RenderState.ToCalc(node), false);
        }

        _s.PendingMissing.Clear();
        CalcValue v = _s.Evaluator.Evaluate(e.Ast, new CalcCell(null, 1, 1));
        if (v.IsError)
        {
            if (_s.PendingMissing.Count > 0)
            {
                string first = _s.PendingMissing[0];
                Missing(e, first);
                return (TemplateData.Null, CalcValue.Blank, true);
            }

            Defect d = Make(DefectKind.Formula, "template.expression-error", DefectSeverity.Repaired, "Expression '" + e.Text + "' evaluates to " + CalcErrors.ToText(v.Error) + ".", e);
            if (_s.Options.Strict)
            {
                throw new TemplateException(d with { Severity = DefectSeverity.Fatal });
            }

            Warn(d);
        }

        return (null, v, false);
    }

    private static string Print(ITemplateData? node, CalcValue v)
    {
        if (node is TemplateData.ScalarData { Display: { } display })
        {
            return display;
        }

        if (node is { Kind: TemplateDataKind.Null })
        {
            return string.Empty;
        }

        if (node is { Kind: TemplateDataKind.Record })
        {
            return string.Empty;
        }

        return PrintValue(v);
    }

    private static string PrintValue(CalcValue v)
    {
        if (v.Kind == ValueKind.Array)
        {
            CalcArray a = v.Array!;
            var sb = new StringBuilder();
            for (int r = 0; r < a.Rows; r++)
            {
                for (int c = 0; c < a.Columns; c++)
                {
                    if (sb.Length > 0)
                    {
                        sb.Append(", ");
                    }

                    sb.Append(CalcConvert.ToText(a[r, c]));
                }
            }

            return sb.ToString();
        }

        return v.Kind == ValueKind.Lambda ? CalcErrors.ToText(CalcError.Calc) : CalcConvert.ToText(v);
    }

    private void EmitOutput(OutputNode o)
    {
        (ITemplateData? node, CalcValue v, bool isMissing) = Evaluate(o.Expr);
        string text = isMissing ? string.Empty : Print(node, v);
        CalcValue? typed = null;
        if (!isMissing)
        {
            if (node is { Kind: TemplateDataKind.Scalar })
            {
                typed = node.Value;
            }
            else if (node is null && v.Kind != ValueKind.Array && v.Kind != ValueKind.Lambda)
            {
                typed = v;
            }
        }

        EmitTag(o.Token, new TemplatePiece(TemplatePieceKind.Text, SegmentAt(o.Token.Unit, o.Token.Start), text, null, typed));
    }

    private bool Truthy(Expr e)
    {
        (ITemplateData? node, CalcValue v, bool isMissing) = Evaluate(e);
        if (isMissing)
        {
            return false;
        }

        if (node is not null)
        {
            switch (node.Kind)
            {
                case TemplateDataKind.Null:
                    return false;
                case TemplateDataKind.Record:
                    return true;
                case TemplateDataKind.Sequence:
                    return node.Items.Count > 0;
            }
        }

        return ValueTruthy(v);
    }

    private static bool ValueTruthy(CalcValue v) => v.Kind switch
    {
        ValueKind.Boolean => v.Boolean,
        ValueKind.Number => v.Number != 0,
        ValueKind.Text => v.Text.Length > 0,
        ValueKind.Array => v.Array!.ToArray().Any(static c => c.Kind != ValueKind.Error && ValueTruthy(c)),
        _ => false,
    };

    // ---- blocks ----------------------------------------------------------------------------------------------------------------

    private void WalkIf(IfNode n)
    {
        foreach (Branch b in n.Branches)
        {
            if (b.Condition is not null && Truthy(b.Condition))
            {
                Walk(b.Body);
                return;
            }
        }

        if (n.Else is not null)
        {
            Walk(n.Else);
        }
    }

    private void WalkFor(ForNode f)
    {
        (ITemplateData? node, CalcValue v, bool isMissing) = Evaluate(f.Source);
        List<ITemplateData> items;
        if (isMissing)
        {
            items = [];
        }
        else if (node is not null && node.Kind == TemplateDataKind.Sequence)
        {
            items = [.. node.Items];
        }
        else if (node is not null && node.Kind == TemplateDataKind.Null)
        {
            items = [];
        }
        else if (v.Kind == ValueKind.Array)
        {
            items = [.. v.Array!.ToArray().Select(static c => TemplateData.Scalar(c))];
        }
        else
        {
            Defect d = Make(DefectKind.Value, "template.not-a-collection", DefectSeverity.Repaired, "'" + f.Source.Text + "' is not a collection; the loop runs once.", f.Source);
            if (_s.Options.Strict)
            {
                throw new TemplateException(d with { Severity = DefectSeverity.Fatal });
            }

            Warn(d);
            items = [node ?? TemplateData.Scalar(v)];
        }

        if (items.Count == 0)
        {
            if (f.Else is not null)
            {
                Walk(f.Else);
            }

            return;
        }

        if (++_s.Depth > Math.Min(_s.Options.MaxBlockDepth, _s.Limits.MaxRecursionDepth))
        {
            throw new OfficeLimitException(nameof(TemplateOptions.MaxBlockDepth), _s.Options.MaxBlockDepth, _s.Depth);
        }

        ITemplateData? parentLoop = FindVariable("loop");
        for (int i = 0; i < items.Count; i++)
        {
            _s.Ct.ThrowIfCancellationRequested();
            if (++_s.Iterations > _s.Options.MaxIterations)
            {
                throw new OfficeLimitException(nameof(TemplateOptions.MaxIterations), _s.Options.MaxIterations, _s.Iterations);
            }

            var frame = new Dictionary<string, ITemplateData>(StringComparer.OrdinalIgnoreCase)
            {
                [f.Variable] = items[i],
                ["loop"] = LoopObject(i, items.Count, parentLoop),
            };
            _s.Frames.Add(frame);
            if (f.BodyStartsNewUnit)
            {
                _force = true;
            }

            try
            {
                Walk(f.Body);
            }
            finally
            {
                _s.Frames.RemoveAt(_s.Frames.Count - 1);
            }
        }

        _s.Depth--;
    }

    private ITemplateData? FindVariable(string name)
    {
        for (int i = _s.Frames.Count - 1; i >= 0; i--)
        {
            if (_s.Frames[i].TryGetValue(name, out ITemplateData? v))
            {
                return v;
            }
        }

        return null;
    }

    private static ITemplateData LoopObject(int index0, int length, ITemplateData? parent)
    {
        var members = new List<KeyValuePair<string, ITemplateData>>
        {
            new("index", TemplateData.Scalar(CalcValue.FromNumber(index0 + 1))),
            new("index0", TemplateData.Scalar(CalcValue.FromNumber(index0))),
            new("first", TemplateData.Scalar(CalcValue.FromBoolean(index0 == 0))),
            new("last", TemplateData.Scalar(CalcValue.FromBoolean(index0 == length - 1))),
            new("length", TemplateData.Scalar(CalcValue.FromNumber(length))),
            new("revindex", TemplateData.Scalar(CalcValue.FromNumber(length - index0))),
            new("revindex0", TemplateData.Scalar(CalcValue.FromNumber(length - index0 - 1))),
        };
        if (parent is not null)
        {
            members.Add(new("parent", parent));
        }

        return TemplateData.Record(members);
    }

    private void WalkInclude(IncludeNode inc)
    {
        (ITemplateData? node, CalcValue v, bool isMissing) = Evaluate(inc.Expr);
        if (isMissing)
        {
            return;
        }

        string name = node is { Kind: TemplateDataKind.Scalar } ? CalcConvert.ToText(node.Value) : CalcConvert.ToText(v);
        ITemplateIncludes? includes = _s.Options.Includes;
        if (includes is null)
        {
            IncludeProblem(inc, "template.include-denied", "Includes are not enabled; '" + name + "' was not read.");
            return;
        }

        if (!_s.IncludeCache.TryGetValue(name, out ParsedTemplate? parsed))
        {
            TemplateSource? source = includes.Find(name);
            if (source is null)
            {
                IncludeProblem(inc, "template.include-not-found", "No template named '" + name + "'.");
                return;
            }

            parsed = TemplateEngine.Parse(source, _s.Options);
            _s.IncludeCache[name] = parsed;
        }

        Defect? fatal = parsed.Defects.FirstOrDefault(static d => d.Severity == DefectSeverity.Fatal);
        if (fatal is not null)
        {
            throw new TemplateException(fatal);
        }

        _s.Depth++;
        var sub = new Renderer(parsed, _s);
        try
        {
            sub.Run();
        }
        finally
        {
            _s.Depth--;
        }

        string text = string.Join('\n', sub._units.Select(static u => string.Concat(u.Pieces.Select(static p => p.Text))));
        EmitTag(inc.Token, new TemplatePiece(TemplatePieceKind.Text, SegmentAt(inc.Token.Unit, inc.Token.Start), text));
    }

    private void IncludeProblem(IncludeNode inc, string code, string message)
    {
        Defect d = Make(DefectKind.Reference, code, DefectSeverity.Repaired, message, inc.Expr);
        if (_s.Options.Strict)
        {
            throw new TemplateException(d with { Severity = DefectSeverity.Fatal });
        }

        Warn(d);
    }

    private void WalkInsert(InsertNode ins)
    {
        (ITemplateData? node, CalcValue v, bool isMissing) = Evaluate(ins.Expr);
        if (isMissing)
        {
            Enter(ins.Token.Unit);
            return;
        }

        ITemplateData value = node ?? TemplateData.Scalar(v);
        EmitTag(ins.Token, new TemplatePiece(ins.Kind, SegmentAt(ins.Token.Unit, ins.Token.Start), string.Empty, new TemplateInsertion(value, ins.Options)));
    }
}
