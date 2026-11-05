using System;
using System.Collections.Generic;
using RecursiveAI.OneOffice.NumberFormats;

namespace RecursiveAI.OneOffice.Calc;

/// <summary>A name bound by <c>LET</c> or a lambda parameter; an immutable linked list so lambdas capture it for free.</summary>
internal sealed class Scope(string name, CalcValue value, Scope? parent)
{
    public string Name { get; } = name;

    public CalcValue Value { get; } = value;

    public Scope? Parent { get; } = parent;
}

/// <summary>Settings of an <see cref="Evaluator"/>.</summary>
public sealed class EvaluatorOptions
{
    /// <summary>
    /// False (default, dynamic-array Excel): a range where one value is expected makes the result an array. True: the range is cut to the
    /// formula's own row or column (implicit intersection), as in files written before dynamic arrays unless they carry the array flag.
    /// </summary>
    public bool LegacyImplicitIntersection { get; init; }

    /// <summary>The date system for date text and date functions. Default 1900.</summary>
    public DateSystem DateSystem { get; init; } = DateSystem.Date1900;

    /// <summary>The most nodes one <see cref="Evaluator.Evaluate(FormulaNode, CellAddress)"/> call may visit before <see cref="OfficeLimitException"/> (limit name <c>MaxEvaluationSteps</c>). Default 50,000,000.</summary>
    public long MaxSteps { get; init; } = 50_000_000;

    /// <summary>The culture for <c>TEXT</c> and number-to-text conversion in functions that format. Default US English.</summary>
    public NumberFormatCulture? Culture { get; init; }
}

/// <summary>
/// Evaluates formula trees over an <see cref="ICalcResolver"/> with Excel's rules for coercion, errors, arrays, references, <c>LET</c> and <c>LAMBDA</c>.
/// The result of a dynamic-array formula is an array; putting it on a grid is the caller's job. An evaluator keeps scratch state and is not thread-safe:
/// use one per thread. Evaluating never changes the resolver and never recalculates anything the resolver holds.
/// </summary>
public sealed class Evaluator
{
    private readonly ICalcResolver _resolver;
    private readonly FunctionRegistry _registry;
    private readonly OfficeLimits _limits;
    private readonly EvaluatorOptions _options;
    private readonly Dictionary<string, NumberFormat?> _formats = new(StringComparer.Ordinal);
    private CalcValue[] _stack = new CalcValue[64];
    private int _sp;
    private CellAddress _origin;
    private Scope? _scope;
    private int _lambdaDepth;
    private long _steps;
    private FormulaNode? _snapRoot;

    /// <summary>Creates an evaluator.</summary>
    /// <param name="resolver">Where cells, names and tables come from.</param>
    /// <param name="registry">The functions; null for <see cref="FunctionRegistry.CreateDefault"/>.</param>
    /// <param name="limits">Limits (recursion depth for lambdas, allocation ceiling); null for the defaults.</param>
    /// <param name="options">Settings; null for the defaults.</param>
    public Evaluator(ICalcResolver resolver, FunctionRegistry? registry = null, OfficeLimits? limits = null, EvaluatorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _resolver = resolver;
        _registry = registry ?? FunctionRegistry.CreateDefault();
        _limits = limits ?? OfficeLimits.Default;
        _options = options ?? new EvaluatorOptions();
    }

    /// <summary>The resolver.</summary>
    public ICalcResolver Resolver => _resolver;

    /// <summary>The function registry.</summary>
    public FunctionRegistry Registry => _registry;

    /// <summary>The limits in force.</summary>
    public OfficeLimits Limits => _limits;

    /// <summary>The options.</summary>
    public EvaluatorOptions Options => _options;

    /// <summary>The cell being evaluated (valid inside a function call).</summary>
    public CellAddress Origin => _origin;

    /// <summary>True when the last <see cref="Evaluate(FormulaNode, CellAddress)"/> called a function flagged <see cref="FunctionTraits.Volatile"/>.</summary>
    public bool UsedVolatile { get; private set; }

    /// <summary>True when the last evaluation stopped a <c>LAMBDA</c> recursion at <see cref="OfficeLimits.MaxRecursionDepth"/> (the value there is <c>#NUM!</c>, as in Excel).</summary>
    public bool RecursionLimitHit { get; private set; }

    /// <summary>Evaluates a formula for a cell.</summary>
    /// <param name="formula">The parsed formula (A1 coordinates are absolute positions in the sheet).</param>
    /// <param name="origin">The cell that holds the formula; positions <c>ROW()</c>, implicit intersection and <c>#This Row</c>, and names the sheet of unqualified references.</param>
    /// <returns>A scalar, an array, or a lambda. References are read: a formula that is just <c>A1</c> gives that cell's value.</returns>
    /// <exception cref="OfficeLimitException">The evaluation visited more than <see cref="EvaluatorOptions.MaxSteps"/> nodes or asked for an array larger than <see cref="OfficeLimits.MaxAllocationBytes"/>.</exception>
    public CalcValue Evaluate(FormulaNode formula, CellAddress origin)
    {
        ArgumentNullException.ThrowIfNull(formula);
        _origin = origin;
        _scope = null;
        _sp = 0;
        _lambdaDepth = 0;
        _steps = 0;
        UsedVolatile = false;
        RecursionLimitHit = false;
        // Excel snaps a sum or difference that is almost zero to zero only when it is the last operation of the formula (not inside parentheses).
        _snapRoot = formula is BinaryNode { Operator: BinaryOperator.Add or BinaryOperator.Subtract } ? formula : null;
        try
        {
            CalcValue v = Eval(formula);
            return v.Kind == ValueKind.Reference && _options.LegacyImplicitIntersection ? ImplicitIntersect(v) : Dereference(v);
        }
        finally
        {
            Array.Clear(_stack, 0, Math.Min(_stack.Length, Math.Max(_sp, 0) + 64));
            _scope = null;
            _snapRoot = null;
        }
    }

    /// <summary>Evaluates an argument expression inside a <see cref="LazyFunction"/>. References stay references.</summary>
    /// <param name="node">The expression.</param>
    /// <returns>The raw result.</returns>
    public CalcValue Evaluate(FormulaNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return Eval(node);
    }

    // ---- stepping and allocation -------------------------------------------------------------------------------------------

    private void Step()
    {
        if (++_steps > _options.MaxSteps)
        {
            throw new OfficeLimitException("MaxEvaluationSteps", _options.MaxSteps, _steps);
        }
    }

    /// <summary>Makes the cell storage for an array, refusing sizes past <see cref="OfficeLimits.MaxAllocationBytes"/>.</summary>
    /// <param name="rows">Rows.</param>
    /// <param name="columns">Columns.</param>
    /// <returns>An array of rows × columns values.</returns>
    /// <exception cref="OfficeLimitException">The array would be too large.</exception>
    public CalcValue[] NewCells(long rows, long columns)
    {
        long count = rows * columns;
        long bytes = count * 24;
        if (rows < 0 || columns < 0 || count > int.MaxValue / 2 || bytes > _limits.MaxAllocationBytes)
        {
            throw new OfficeLimitException(nameof(OfficeLimits.MaxAllocationBytes), _limits.MaxAllocationBytes, bytes);
        }

        return new CalcValue[count];
    }

    // ---- evaluation --------------------------------------------------------------------------------------------------------

    private CalcValue Eval(FormulaNode n)
    {
        Step();
        switch (n.Kind)
        {
            case NodeKind.Number:
                return ((NumberNode)n).Cached;
            case NodeKind.Text:
                return ((TextNode)n).Cached;
            case NodeKind.Boolean:
                return CalcValue.FromBoolean(((BooleanNode)n).Value);
            case NodeKind.Error:
                return EvalErrorNode((ErrorNode)n);
            case NodeKind.Missing:
                return CalcValue.Missing;
            case NodeKind.Cell:
                return EvalCell((CellReferenceNode)n);
            case NodeKind.Area:
                return EvalArea((AreaReferenceNode)n);
            case NodeKind.Name:
                return EvalName((NameNode)n);
            case NodeKind.Structured:
                return _resolver.ResolveStructured((StructuredReferenceNode)n, _origin);
            case NodeKind.Call:
                return EvalCall((FunctionCallNode)n);
            case NodeKind.Invoke:
                return EvalInvoke((InvokeNode)n);
            case NodeKind.Unary:
                return EvalUnary((UnaryNode)n);
            case NodeKind.Percent:
                return EvalPercent((PercentNode)n);
            case NodeKind.Spill:
                return Spill(Eval(((SpillNode)n).Operand));
            case NodeKind.Binary:
                return EvalBinary((BinaryNode)n);
            case NodeKind.Paren:
                return Eval(((ParenNode)n).Inner);
            case NodeKind.ArrayConstant:
                return CalcValue.FromArray(((ArrayConstantNode)n).Value);
            case NodeKind.MemberPath:
                return _resolver.ResolveMember(((MemberPathNode)n).Segments);
            default:
                return CalcValue.FromError(CalcError.Value);
        }
    }

    private CalcValue EvalErrorNode(ErrorNode n)
    {
        if (n.Prefix?.Workbook is not null)
        {
            return _resolver.ResolveExternal(n.Prefix, n);
        }

        return CalcValue.FromError(n.Error);
    }

    private CalcValue EvalCell(CellReferenceNode n)
    {
        if (n.Prefix?.Workbook is not null)
        {
            return _resolver.ResolveExternal(n.Prefix, n);
        }

        if (n.Cached.Kind == ValueKind.Blank)
        {
            n.Cached = CalcValue.FromReference(new CalcReference(
                [new CellArea(n.Prefix?.Sheet, n.Prefix?.LastSheet, n.Row.Index, n.Column.Index, n.Row.Index, n.Column.Index)]));
        }

        return n.Cached;
    }

    private CalcValue EvalArea(AreaReferenceNode n)
    {
        if (n.Prefix?.Workbook is not null)
        {
            return _resolver.ResolveExternal(n.Prefix, n);
        }

        if (n.Cached.Kind == ValueKind.Blank)
        {
            int r1 = n.AreaKind == AreaKind.Columns ? 1 : n.Row1.Index;
            int r2 = n.AreaKind == AreaKind.Columns ? CellArea.MaxRow : n.Row2.Index;
            int c1 = n.AreaKind == AreaKind.Rows ? 1 : n.Column1.Index;
            int c2 = n.AreaKind == AreaKind.Rows ? CellArea.MaxColumn : n.Column2.Index;
            n.Cached = CalcValue.FromReference(new CalcReference(
                [new CellArea(n.Prefix?.Sheet, n.Prefix?.LastSheet, Math.Min(r1, r2), Math.Min(c1, c2), Math.Max(r1, r2), Math.Max(c1, c2))]));
        }

        return n.Cached;
    }

    private CalcValue EvalName(NameNode n)
    {
        if (n.Prefix is null)
        {
            string bare = n.BareName;
            for (Scope? s = _scope; s is not null; s = s.Parent)
            {
                if (string.Equals(s.Name, bare, StringComparison.OrdinalIgnoreCase))
                {
                    return s.Value;
                }
            }
        }
        else if (n.Prefix.Workbook is not null)
        {
            return _resolver.ResolveExternal(n.Prefix, n);
        }

        return _resolver.ResolveName(n.Prefix?.Sheet, n.BareName, _origin);
    }

    internal CalcValue Spill(CalcValue anchor)
    {
        if (anchor.IsError)
        {
            return anchor;
        }

        CalcReference? r = anchor.Reference;
        if (r is null || r.AreaCount != 1 || !r.First.IsSingleCell)
        {
            return CalcValue.FromError(CalcError.Ref);
        }

        CellArea a = r.First;
        if (!_resolver.TryGetSpill(a.Sheet ?? _origin.Sheet, a.Row1, a.Column1, out CellArea spill))
        {
            return CalcValue.FromError(CalcError.Ref);
        }

        return CalcValue.FromReference(new CalcReference([spill with { Sheet = spill.Sheet ?? a.Sheet }], true));
    }

    private CalcValue EvalInvoke(InvokeNode n)
    {
        CalcValue callee = Dereference(Eval(n.Callee));
        if (callee.IsError)
        {
            return callee;
        }

        if (callee.Kind != ValueKind.Lambda)
        {
            return CalcValue.FromError(CalcError.Value);
        }

        return InvokeNodes(callee, n.Arguments);
    }

    private CalcValue InvokeNodes(CalcValue lambda, IReadOnlyList<FormulaNode> args)
    {
        int baseSp = _sp;
        EnsureStack(baseSp + args.Count);
        _sp += args.Count;
        for (int i = 0; i < args.Count; i++)
        {
            CalcValue v = Eval(args[i]);
            _stack[baseSp + i] = v.Kind == ValueKind.Reference ? v : v;
        }

        CalcValue result = Invoke(lambda, new ReadOnlySpan<CalcValue>(_stack, baseSp, args.Count));
        Array.Clear(_stack, baseSp, args.Count);
        _sp = baseSp;
        return result;
    }

    /// <summary>Calls a lambda value.</summary>
    /// <param name="lambda">A value of kind <see cref="ValueKind.Lambda"/>.</param>
    /// <param name="args">The arguments, bound to the parameters in order (references are bound as references).</param>
    /// <returns>The body's result; <c>#VALUE!</c> when the value is not a lambda or the argument count is wrong; <c>#NUM!</c> past the recursion limit.</returns>
    public CalcValue Invoke(in CalcValue lambda, ReadOnlySpan<CalcValue> args)
    {
        LambdaValue? l = lambda.Lambda;
        if (l is null || args.Length != l.Parameters.Length)
        {
            return CalcValue.FromError(CalcError.Value);
        }

        if (_lambdaDepth >= _limits.MaxRecursionDepth)
        {
            RecursionLimitHit = true;
            return CalcValue.FromError(CalcError.Num);
        }

        Scope? saved = _scope;
        Scope? sc = l.Captured;
        for (int i = 0; i < args.Length; i++)
        {
            sc = new Scope(l.Parameters[i], args[i], sc);
        }

        _scope = sc;
        _lambdaDepth++;
        try
        {
            return Eval(l.Body);
        }
        finally
        {
            _lambdaDepth--;
            _scope = saved;
        }
    }

    internal CalcValue MakeLambda(string[] parameters, FormulaNode body) => CalcValue.FromLambda(new LambdaValue(parameters, body, _scope));

    internal Scope? CurrentScope
    {
        get => _scope;
        set => _scope = value;
    }

    internal static Scope Bind(string name, CalcValue value, Scope? parent) => new(name, value, parent);

    private void EnsureStack(int needed)
    {
        if (needed > _stack.Length)
        {
            if (needed > 1 << 20)
            {
                throw new OfficeLimitException("MaxEvaluationSteps", 1 << 20, needed);
            }

            Array.Resize(ref _stack, Math.Max(needed, _stack.Length * 2));
        }
    }

    // ---- function calls ----------------------------------------------------------------------------------------------------

    private CalcValue EvalCall(FunctionCallNode n)
    {
        IReadOnlyList<FormulaNode> args = n.Arguments;
        if (!_registry.TryGet(n.Name, out FunctionDefinition? def) || def is null)
        {
            // A LET name or lambda parameter bound to a lambda is called like a function.
            string bare = n.Name.StartsWith("_xlpm.", StringComparison.OrdinalIgnoreCase) ? n.Name[6..] : n.Name;
            for (Scope? s = _scope; s is not null; s = s.Parent)
            {
                if (string.Equals(s.Name, bare, StringComparison.OrdinalIgnoreCase))
                {
                    CalcValue f = Dereference(s.Value);
                    return f.Kind == ValueKind.Lambda ? InvokeNodes(f, args) : CalcValue.FromError(CalcError.Value);
                }
            }

            CalcValue named = _resolver.ResolveName(null, bare, _origin);
            if (named.Kind == ValueKind.Lambda)
            {
                return InvokeNodes(named, args);
            }

            return CalcValue.FromError(CalcError.Name);
        }

        if (args.Count < def.MinArgs || args.Count > def.MaxArgs)
        {
            return CalcValue.FromError(CalcError.Value);
        }

        if (def.IsVolatile)
        {
            UsedVolatile = true;
        }

        if (def.Lazy is not null)
        {
            return def.Lazy(this, n.ArgumentArray);
        }

        int baseSp = _sp;
        EnsureStack(baseSp + args.Count);
        _sp += args.Count;
        bool lift = false;
        CalcValue? firstError = null;
        bool handles = (def.Flags & FunctionTraits.HandlesErrors) != 0;
        for (int i = 0; i < args.Count; i++)
        {
            CalcValue v = Eval(args[i]);
            ArgKind kind = def.KindOf(i);
            switch (kind)
            {
                case ArgKind.Value:
                    v = DerefForValue(v);
                    if (v.Kind == ValueKind.Array)
                    {
                        lift = true;
                    }
                    else if (v.IsError && !handles)
                    {
                        firstError ??= v;
                    }

                    break;
                case ArgKind.Array:
                    v = DerefForArray(v);
                    break;
                default:
                    break;
            }

            _stack[baseSp + i] = v;
        }

        try
        {
            if (firstError is { } fe && !lift)
            {
                return fe;
            }

            ReadOnlySpan<CalcValue> span = new(_stack, baseSp, args.Count);
            return lift ? LiftCall(def, span, handles) : def.Eager!(this, span);
        }
        finally
        {
            Array.Clear(_stack, baseSp, args.Count);
            _sp = baseSp;
        }
    }

    private CalcValue LiftCall(FunctionDefinition def, ReadOnlySpan<CalcValue> args, bool handlesErrors)
    {
        int rows = 1;
        int cols = 1;
        for (int i = 0; i < args.Length; i++)
        {
            if (def.KindOf(i) == ArgKind.Value && args[i].Array is { } a)
            {
                rows = Math.Max(rows, a.Rows);
                cols = Math.Max(cols, a.Columns);
            }
        }

        CalcValue[] cells = NewCells(rows, cols);
        var scalars = new CalcValue[args.Length];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                CalcValue? err = null;
                for (int i = 0; i < args.Length; i++)
                {
                    CalcValue v = args[i];
                    if (def.KindOf(i) == ArgKind.Value && v.Array is { } a)
                    {
                        v = a.Broadcasting(r, c);
                    }

                    if (v.IsError && !handlesErrors && def.KindOf(i) == ArgKind.Value)
                    {
                        err ??= v;
                    }

                    scalars[i] = v;
                }

                CalcValue res = err ?? def.Eager!(this, scalars);
                cells[(r * cols) + c] = ToScalar(res);
            }
        }

        return CalcValue.FromArray(new CalcArray(rows, cols, cells));
    }

    // ---- dereferencing ------------------------------------------------------------------------------------------------------

    /// <summary>Reads a value for use as data: a reference becomes the value of its cell (one cell) or an array (a block); arrays, scalars and lambdas pass through.</summary>
    /// <param name="value">Any value.</param>
    /// <returns>A scalar, an array or a lambda. A union or 3-D block that cannot be one array gives <c>#VALUE!</c>.</returns>
    public CalcValue Dereference(in CalcValue value)
    {
        if (value.Kind != ValueKind.Reference)
        {
            return value;
        }

        CalcReference r = value.Reference!;
        if (!TryReadAreas(r, out CalcArray[] blocks, out CalcError err))
        {
            return CalcValue.FromError(err);
        }

        if (blocks.Length != 1)
        {
            return CalcValue.FromError(CalcError.Value);
        }

        CalcArray b = blocks[0];
        return b.Rows == 1 && b.Columns == 1 && !r.IsSpill ? b[0, 0] : CalcValue.FromArray(b);
    }

    private CalcValue DerefForValue(CalcValue v)
    {
        v = Dereference(v);
        if (v.Kind == ValueKind.Array && _options.LegacyImplicitIntersection)
        {
            return ImplicitIntersection(v.Array!, null);
        }

        return v;
    }

    private CalcValue DerefForArray(CalcValue v) => Dereference(v);

    /// <summary>Applies implicit intersection to a value: a block gives the cell in the formula's own row or column, a one-cell array its value.</summary>
    /// <param name="value">Any value.</param>
    /// <returns>A scalar, or <c>#VALUE!</c> when the block does not cross the formula's row or column.</returns>
    public CalcValue ImplicitIntersect(in CalcValue value)
    {
        CalcValue v = value;
        if (v.Kind == ValueKind.Reference)
        {
            CalcReference r = v.Reference!;
            if (r.AreaCount == 1 && !r.IsSpill)
            {
                CellArea a = r.First;
                if (a.IsSingleCell)
                {
                    return Dereference(v);
                }

                int row = a.Rows == 1 ? a.Row1 : _origin.Row;
                int col = a.Columns == 1 ? a.Column1 : _origin.Column;
                if (a.Contains(row, col) && (a.Rows == 1 || a.Columns == 1 || a.Contains(_origin.Row, _origin.Column)))
                {
                    return _resolver.GetCell(a.Sheet ?? _origin.Sheet, row, col);
                }

                return CalcValue.FromError(CalcError.Value);
            }
        }

        v = Dereference(v);
        return v.Kind == ValueKind.Array ? ImplicitIntersection(v.Array!, null) : v;
    }

    private CalcValue ImplicitIntersection(CalcArray a, CellArea? area)
    {
        if (a.Rows == 1 && a.Columns == 1)
        {
            return a[0, 0];
        }

        _ = area;
        if (a.Rows == 1)
        {
            return _origin.Column >= 1 && _origin.Column <= a.Columns ? a[0, _origin.Column - 1] : CalcValue.FromError(CalcError.Value);
        }

        if (a.Columns == 1)
        {
            return _origin.Row >= 1 && _origin.Row <= a.Rows ? a[_origin.Row - 1, 0] : CalcValue.FromError(CalcError.Value);
        }

        return CalcValue.FromError(CalcError.Value);
    }

    /// <summary>Reads the cells of a reference into blocks, one per area (and per sheet for a 3-D area).</summary>
    /// <param name="reference">The reference.</param>
    /// <param name="blocks">The values of each block, in order.</param>
    /// <param name="error">The error when the reference cannot be read (<c>#REF!</c> for a 3-D reference whose sheets are unknown or a spill with no anchor).</param>
    /// <returns>True when read.</returns>
    public bool TryReadAreas(CalcReference reference, out CalcArray[] blocks, out CalcError error)
    {
        ArgumentNullException.ThrowIfNull(reference);
        error = CalcError.None;
        var list = new List<CalcArray>(reference.AreaCount);
        ReadOnlySpan<CellArea> areas = reference.Areas;
        for (int i = 0; i < areas.Length; i++)
        {
            CellArea a = areas[i];
            if (reference.IsSpill)
            {
                if (!_resolver.TryGetSpill(a.Sheet ?? _origin.Sheet, a.Row1, a.Column1, out CellArea sp))
                {
                    blocks = [];
                    error = CalcError.Ref;
                    return false;
                }

                a = sp with { Sheet = sp.Sheet ?? a.Sheet };
            }

            if (a.LastSheet is not null && a.Sheet is not null)
            {
                if (!_resolver.TryExpandSheets(a.Sheet, a.LastSheet, out IReadOnlyList<string> sheets))
                {
                    blocks = [];
                    error = CalcError.Ref;
                    return false;
                }

                foreach (string sh in sheets)
                {
                    list.Add(ReadArea(a with { Sheet = sh, LastSheet = null }));
                }
            }
            else
            {
                list.Add(ReadArea(a));
            }
        }

        blocks = [.. list];
        return true;
    }

    private CalcArray ReadArea(CellArea a)
    {
        CellArea concrete = a with { Sheet = a.Sheet ?? _origin.Sheet };
        long count = (long)concrete.Rows * concrete.Columns;
        if (count == 1)
        {
            return CalcArray.OfValue(_resolver.GetCell(concrete.Sheet, concrete.Row1, concrete.Column1));
        }

        if (count * 24 > _limits.MaxAllocationBytes)
        {
            throw new OfficeLimitException(nameof(OfficeLimits.MaxAllocationBytes), _limits.MaxAllocationBytes, count * 24);
        }

        return _resolver.GetRange(concrete);
    }

    /// <summary>Turns any value into a scalar for a place that holds one value: an array gives its top-left value, a reference its first cell, a lambda <c>#CALC!</c>.</summary>
    /// <param name="value">Any value.</param>
    /// <returns>A scalar.</returns>
    public CalcValue ToScalar(in CalcValue value)
    {
        CalcValue v = Dereference(value);
        return v.Kind switch
        {
            ValueKind.Array => v.Array![0, 0],
            ValueKind.Lambda => CalcValue.FromError(CalcError.Calc),
            _ => v,
        };
    }

    /// <summary>Converts a value to an array for element-wise work: a scalar becomes 1 × 1, a reference is read.</summary>
    /// <param name="value">Any value.</param>
    /// <returns>The array; <c>null</c> when the value is an error or a lambda (use <see cref="Dereference"/> to see which).</returns>
    public CalcArray? ToArray(in CalcValue value)
    {
        CalcValue v = Dereference(value);
        return v.Kind switch
        {
            ValueKind.Array => v.Array,
            ValueKind.Error or ValueKind.Lambda => null,
            _ => CalcArray.OfValue(v),
        };
    }

    internal NumberFormat? GetFormat(string code)
    {
        if (!_formats.TryGetValue(code, out NumberFormat? f))
        {
            f = NumberFormat.TryParse(code, out NumberFormat? parsed, out _) ? parsed : null;
            if (_formats.Count > 256)
            {
                _formats.Clear();
            }

            _formats[code] = f;
        }

        return f;
    }

    // ---- operators ----------------------------------------------------------------------------------------------------------

    private CalcValue Operand(FormulaNode n)
    {
        CalcValue v = Eval(n);
        if (v.Kind == ValueKind.Reference)
        {
            if (_options.LegacyImplicitIntersection)
            {
                return ImplicitIntersect(v);
            }

            v = Dereference(v);
        }

        return v;
    }

    private CalcValue EvalUnary(UnaryNode n)
    {
        if (n.Operator == '@')
        {
            return ImplicitIntersect(Eval(n.Operand));
        }

        CalcValue v = Operand(n.Operand);
        if (n.Operator == '+')
        {
            return v;
        }

        return v.Kind == ValueKind.Array ? MapArray(v.Array!, Negate) : Negate(v);

        static CalcValue Negate(CalcValue x)
        {
            if (x.IsError)
            {
                return x;
            }

            CalcError e = CalcConvert.ToNumber(x, out double d);
            return e != CalcError.None ? CalcValue.FromError(e) : CalcValue.FromNumber(d == 0 ? 0 : -d);
        }
    }

    private CalcValue EvalPercent(PercentNode n)
    {
        CalcValue v = Operand(n.Operand);
        return v.Kind == ValueKind.Array ? MapArray(v.Array!, Pct) : Pct(v);

        static CalcValue Pct(CalcValue x)
        {
            if (x.IsError)
            {
                return x;
            }

            CalcError e = CalcConvert.ToNumber(x, out double d);
            return e != CalcError.None ? CalcValue.FromError(e) : CalcValue.FromNumber(d / 100);
        }
    }

    private CalcValue MapArray(CalcArray a, Func<CalcValue, CalcValue> f)
    {
        CalcValue[] cells = NewCells(a.Rows, a.Columns);
        for (int r = 0; r < a.Rows; r++)
        {
            for (int c = 0; c < a.Columns; c++)
            {
                cells[(r * a.Columns) + c] = f(a[r, c]);
            }
        }

        return CalcValue.FromArray(new CalcArray(a.Rows, a.Columns, cells));
    }

    private CalcValue EvalBinary(BinaryNode n)
    {
        BinaryOperator op = n.Operator;
        if (op is BinaryOperator.Range or BinaryOperator.Intersect or BinaryOperator.Union)
        {
            return EvalReferenceOperator(n);
        }

        CalcValue a = Operand(n.Left);
        CalcValue b = Operand(n.Right);
        if (a.IsScalar && b.IsScalar)
        {
            CalcValue r = Apply(op, a, b);
            if (ReferenceEquals(n, _snapRoot) && r.Kind == ValueKind.Number)
            {
                r = Snap(a, b, r);
            }

            return r;
        }

        return LiftBinary(op, a, b);
    }

    private static CalcValue Snap(in CalcValue a, in CalcValue b, CalcValue r)
    {
        double x = r.Number;
        if (x != 0 && Math.Abs(x) < Math.Max(Math.Abs(a.Number), Math.Abs(b.Number)) * 8.881784197001252E-16 && a.Kind == ValueKind.Number && b.Kind == ValueKind.Number)
        {
            return CalcValue.FromNumber(0);
        }

        return r;
    }

    private CalcValue LiftBinary(BinaryOperator op, CalcValue a, CalcValue b)
    {
        if (a.IsError)
        {
            return a;
        }

        if (b.IsError)
        {
            return b;
        }

        if (a.Kind == ValueKind.Lambda || b.Kind == ValueKind.Lambda)
        {
            return CalcValue.FromError(CalcError.Value);
        }

        CalcArray aa = a.Array ?? CalcArray.OfValue(a);
        CalcArray ba = b.Array ?? CalcArray.OfValue(b);
        int rows = Math.Max(aa.Rows, ba.Rows);
        int cols = Math.Max(aa.Columns, ba.Columns);
        CalcValue[] cells = NewCells(rows, cols);
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                cells[(r * cols) + c] = Apply(op, aa.Broadcasting(r, c), ba.Broadcasting(r, c));
            }
        }

        return CalcValue.FromArray(new CalcArray(rows, cols, cells));
    }

    /// <summary>Applies a binary operator to two scalars with Excel's rules: errors propagate (left first), operands are coerced, results that are not finite are <c>#NUM!</c>.</summary>
    /// <param name="op">An arithmetic, comparison or concatenation operator.</param>
    /// <param name="a">Left scalar.</param>
    /// <param name="b">Right scalar.</param>
    /// <returns>The result.</returns>
    public static CalcValue Apply(BinaryOperator op, in CalcValue a, in CalcValue b)
    {
        if (a.IsError)
        {
            return a;
        }

        if (b.IsError)
        {
            return b;
        }

        switch (op)
        {
            case BinaryOperator.Concat:
                return CalcValue.FromText(string.Concat(CalcConvert.ToText(a), CalcConvert.ToText(b)));
            case BinaryOperator.Equal:
                return CalcValue.FromBoolean(Compare(a, b) == 0);
            case BinaryOperator.NotEqual:
                return CalcValue.FromBoolean(Compare(a, b) != 0);
            case BinaryOperator.Less:
                return CalcValue.FromBoolean(Compare(a, b) < 0);
            case BinaryOperator.LessOrEqual:
                return CalcValue.FromBoolean(Compare(a, b) <= 0);
            case BinaryOperator.Greater:
                return CalcValue.FromBoolean(Compare(a, b) > 0);
            case BinaryOperator.GreaterOrEqual:
                return CalcValue.FromBoolean(Compare(a, b) >= 0);
            default:
                break;
        }

        CalcError e = CalcConvert.ToNumber(a, out double x);
        if (e != CalcError.None)
        {
            return CalcValue.FromError(e);
        }

        e = CalcConvert.ToNumber(b, out double y);
        if (e != CalcError.None)
        {
            return CalcValue.FromError(e);
        }

        double r;
        switch (op)
        {
            case BinaryOperator.Add:
                r = x + y;
                break;
            case BinaryOperator.Subtract:
                r = x - y;
                break;
            case BinaryOperator.Multiply:
                r = x * y;
                break;
            case BinaryOperator.Divide:
                if (y == 0)
                {
                    return CalcValue.FromError(CalcError.Div0);
                }

                r = x / y;
                break;
            case BinaryOperator.Power:
                if (x == 0 && y == 0)
                {
                    return CalcValue.FromError(CalcError.Num);
                }

                if (x == 0 && y < 0)
                {
                    return CalcValue.FromError(CalcError.Div0);
                }

                if (x < 0 && y != Math.Floor(y))
                {
                    return CalcValue.FromError(CalcError.Num);
                }

                r = Math.Pow(x, y);
                break;
            default:
                return CalcValue.FromError(CalcError.Value);
        }

        return double.IsFinite(r) ? CalcValue.FromNumber(r == 0 ? 0 : r) : CalcValue.FromError(CalcError.Num);
    }

    /// <summary>Orders two scalars as the comparison operators do: numbers before text before booleans; text ignoring case; numbers equal when they agree to 15 significant digits; blank acts as 0, empty text or FALSE against the other kind.</summary>
    /// <param name="a">Left scalar (not an error).</param>
    /// <param name="b">Right scalar (not an error).</param>
    /// <returns>Negative, zero or positive.</returns>
    public static int Compare(in CalcValue a, in CalcValue b)
    {
        ValueKind ka = a.Kind;
        ValueKind kb = b.Kind;
        if (ka == ValueKind.Blank)
        {
            ka = kb == ValueKind.Blank ? ValueKind.Number : kb;
        }

        if (kb == ValueKind.Blank)
        {
            kb = ka;
        }

        int ra = Rank(ka);
        int rb = Rank(kb);
        if (ra != rb)
        {
            return ra.CompareTo(rb);
        }

        switch (ka)
        {
            case ValueKind.Text:
                return string.Compare(a.Kind == ValueKind.Blank ? string.Empty : a.Text, b.Kind == ValueKind.Blank ? string.Empty : b.Text, StringComparison.OrdinalIgnoreCase);
            case ValueKind.Boolean:
                return (a.Kind == ValueKind.Blank ? 0 : a.Number).CompareTo(b.Kind == ValueKind.Blank ? 0 : b.Number);
            default:
                return CompareNumbers(a.Number, b.Number);
        }

        static int Rank(ValueKind k) => k switch
        {
            ValueKind.Text => 1,
            ValueKind.Boolean => 2,
            _ => 0,
        };
    }

    // Excel compares numbers after rounding each to 15 significant digits, so (0.1+0.2)=0.3 is TRUE.
    private static int CompareNumbers(double x, double y)
    {
        if (x == y)
        {
            return 0;
        }

        double big = Math.Max(Math.Abs(x), Math.Abs(y));
        if (Math.Abs(x - y) > big * 1e-14)
        {
            return x < y ? -1 : 1;
        }

        double rx = RoundTo15(x);
        double ry = RoundTo15(y);
        return rx == ry ? 0 : rx < ry ? -1 : 1;
    }

    private static double RoundTo15(double v)
    {
        if (v == 0 || !double.IsFinite(v))
        {
            return v;
        }

        int e = (int)Math.Floor(Math.Log10(Math.Abs(v)));
        int shift = 14 - e;
        if (shift > 300 || shift < -300)
        {
            return v;
        }

        double scale = Math.Pow(10, shift);
        return Math.Round(v * scale) / scale;
    }

    private CalcValue EvalReferenceOperator(BinaryNode n)
    {
        CalcValue a = Eval(n.Left);
        CalcValue b = Eval(n.Right);
        if (a.IsError)
        {
            return a;
        }

        if (b.IsError)
        {
            return b;
        }

        CalcReference? ra = a.Reference;
        CalcReference? rb = b.Reference;
        if (ra is null || rb is null)
        {
            return CalcValue.FromError(CalcError.Value);
        }

        switch (n.Operator)
        {
            case BinaryOperator.Union:
                {
                    var all = new List<CellArea>(ra.Areas.Length + rb.Areas.Length);
                    all.AddRange(ra.Areas.ToArray());
                    all.AddRange(rb.Areas.ToArray());
                    return CalcValue.FromReference(new CalcReference(all.ToArray()));
                }

            case BinaryOperator.Range:
                {
                    CellArea x = ra.First;
                    CellArea y = rb.First;
                    if (ra.AreaCount != 1 || rb.AreaCount != 1 || !string.Equals(x.Sheet ?? _origin.Sheet, y.Sheet ?? _origin.Sheet, StringComparison.Ordinal))
                    {
                        return CalcValue.FromError(CalcError.Value);
                    }

                    return CalcValue.FromReference(new CalcReference([new CellArea(x.Sheet, x.LastSheet, Math.Min(x.Row1, y.Row1), Math.Min(x.Column1, y.Column1), Math.Max(x.Row2, y.Row2), Math.Max(x.Column2, y.Column2))]));
                }

            default:
                {
                    var found = new List<CellArea>();
                    foreach (CellArea x in ra.Areas)
                    {
                        foreach (CellArea y in rb.Areas)
                        {
                            if (!string.Equals(x.Sheet ?? _origin.Sheet, y.Sheet ?? _origin.Sheet, StringComparison.Ordinal))
                            {
                                continue;
                            }

                            int r1 = Math.Max(x.Row1, y.Row1);
                            int r2 = Math.Min(x.Row2, y.Row2);
                            int c1 = Math.Max(x.Column1, y.Column1);
                            int c2 = Math.Min(x.Column2, y.Column2);
                            if (r1 <= r2 && c1 <= c2)
                            {
                                found.Add(new CellArea(x.Sheet, x.LastSheet, r1, c1, r2, c2));
                            }
                        }
                    }

                    return found.Count == 0 ? CalcValue.FromError(CalcError.Null) : CalcValue.FromReference(new CalcReference(found.ToArray()));
                }
        }
    }
}
