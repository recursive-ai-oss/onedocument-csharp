using System;
using System.Collections.Generic;

namespace RecursiveAI.OneOffice.Calc;

/// <summary>How the evaluator hands an argument to a function.</summary>
public enum ArgKind : byte
{
    /// <summary>One value. A reference to one cell is read; a block or array makes the call <em>lift</em>: the function runs once per element and the results form an array. The evaluator does all of that.</summary>
    Value,

    /// <summary>An array of values. References are read into arrays; a scalar stays a scalar; lambdas pass through. No lifting.</summary>
    Array,

    /// <summary>The argument as evaluated, untouched: references stay references (so the function can tell a cell from a constant, or take its shape), arrays and lambdas pass through. No lifting.</summary>
    Any,
}

/// <summary>Behaviour flags of a function.</summary>
[Flags]
public enum FunctionTraits
{
    /// <summary>None.</summary>
    None = 0,

    /// <summary>The result changes on every recalculation (<c>RAND</c>, <c>NOW</c>); the evaluator sets <see cref="Evaluator.UsedVolatile"/>.</summary>
    Volatile = 1,

    /// <summary>
    /// The function wants to see errors in <see cref="ArgKind.Value"/> arguments (<c>ISERROR</c>, <c>IFERROR</c>). Without this flag the evaluator
    /// returns the first error argument, left to right, without calling the function.
    /// </summary>
    HandlesErrors = 2,

    /// <summary>The function reads the formula's own cell or position (<c>ROW()</c>, <c>COLUMN()</c>).</summary>
    UsesOrigin = 4,

    /// <summary>The function builds a reference from its arguments at run time (<c>INDIRECT</c>, <c>OFFSET</c>), so the cells it reads cannot be found by reading the formula. Workbook calculation evaluates such formulas after the cells they may read.</summary>
    DynamicReference = 8,
}

/// <summary>A function that receives evaluated arguments.</summary>
/// <param name="context">The evaluator, for coercion helpers, reading references and invoking lambdas.</param>
/// <param name="args">The arguments, already prepared per the definition's <see cref="ArgKind"/> list. Valid only during the call.</param>
/// <returns>The result: a scalar, an array (a dynamic-array result), or a reference.</returns>
public delegate CalcValue EagerFunction(Evaluator context, ReadOnlySpan<CalcValue> args);

/// <summary>A function that decides what to evaluate: <c>IF</c>, <c>IFERROR</c>, <c>LET</c>. It receives the argument expressions.</summary>
/// <param name="context">The evaluator; call <see cref="Evaluator.Evaluate(FormulaNode)"/> on the arguments it wants.</param>
/// <param name="args">The argument expressions (an omitted argument is a <see cref="MissingNode"/>).</param>
/// <returns>The result.</returns>
public delegate CalcValue LazyFunction(Evaluator context, ReadOnlySpan<FormulaNode> args);

/// <summary>A function written by the application, with plain value arguments (each lifts over arrays) and a value result.</summary>
/// <param name="args">The arguments; text, numbers, booleans and blanks (errors never arrive: they propagate before the call).</param>
/// <returns>The result.</returns>
public delegate CalcValue CustomFunction(ReadOnlySpan<CalcValue> args);

/// <summary>The signature and implementation of one function. Immutable; register it in a <see cref="FunctionRegistry"/>.</summary>
public sealed class FunctionDefinition
{
    private readonly ArgKind[] _kinds;

    private FunctionDefinition(string name, int min, int max, ArgKind[] kinds, bool repeatLast, FunctionTraits flags, EagerFunction? eager, LazyFunction? lazy)
    {
        Name = name;
        MinArgs = min;
        MaxArgs = max;
        _kinds = kinds;
        RepeatLast = repeatLast;
        Flags = flags;
        Eager = eager;
        Lazy = lazy;
    }

    /// <summary>The name, upper case, without prefix.</summary>
    public string Name { get; }

    /// <summary>The fewest arguments.</summary>
    public int MinArgs { get; }

    /// <summary>The most arguments (<see cref="int.MaxValue"/> when the last kind repeats without bound).</summary>
    public int MaxArgs { get; }

    /// <summary>True when the last <see cref="ArgKind"/> applies to every further argument.</summary>
    public bool RepeatLast { get; }

    /// <summary>The flags.</summary>
    public FunctionTraits Flags { get; }

    /// <summary>True when the result may change on every recalculation.</summary>
    public bool IsVolatile => (Flags & FunctionTraits.Volatile) != 0;

    internal EagerFunction? Eager { get; }

    internal LazyFunction? Lazy { get; }

    /// <summary>The kind of the argument at a position.</summary>
    /// <param name="index">Zero-based argument index.</param>
    /// <returns>Its kind.</returns>
    public ArgKind KindOf(int index) => _kinds.Length == 0 ? ArgKind.Value : index < _kinds.Length ? _kinds[index] : RepeatLast ? _kinds[^1] : ArgKind.Value;

    /// <summary>Defines a function over evaluated arguments.</summary>
    /// <param name="name">The name without prefix; compared ignoring case.</param>
    /// <param name="minArgs">The fewest arguments, at least 0.</param>
    /// <param name="maxArgs">The most arguments; <see cref="int.MaxValue"/> for no bound.</param>
    /// <param name="kinds">The kind of each argument in order; the last also covers the rest when <paramref name="repeatLast"/> is true; empty means all <see cref="ArgKind.Value"/>.</param>
    /// <param name="implementation">The function.</param>
    /// <param name="flags">Behaviour flags.</param>
    /// <param name="repeatLast">True when the last kind repeats.</param>
    /// <returns>The definition.</returns>
    /// <exception cref="ArgumentException">The name is empty or the counts are inconsistent.</exception>
    public static FunctionDefinition Create(string name, int minArgs, int maxArgs, ReadOnlySpan<ArgKind> kinds, EagerFunction implementation, FunctionTraits flags = FunctionTraits.None, bool repeatLast = false)
    {
        Validate(name, minArgs, maxArgs);
        ArgumentNullException.ThrowIfNull(implementation);
        return new FunctionDefinition(name.ToUpperInvariant(), minArgs, maxArgs, kinds.ToArray(), repeatLast, flags, implementation, null);
    }

    /// <summary>Defines a function that evaluates its own arguments.</summary>
    /// <param name="name">The name without prefix.</param>
    /// <param name="minArgs">The fewest arguments.</param>
    /// <param name="maxArgs">The most arguments.</param>
    /// <param name="implementation">The function.</param>
    /// <param name="flags">Behaviour flags.</param>
    /// <returns>The definition.</returns>
    /// <exception cref="ArgumentException">The name is empty or the counts are inconsistent.</exception>
    public static FunctionDefinition CreateLazy(string name, int minArgs, int maxArgs, LazyFunction implementation, FunctionTraits flags = FunctionTraits.None)
    {
        Validate(name, minArgs, maxArgs);
        ArgumentNullException.ThrowIfNull(implementation);
        return new FunctionDefinition(name.ToUpperInvariant(), minArgs, maxArgs, [], false, flags, null, implementation);
    }

    private static void Validate(string name, int min, int max)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (min < 0 || max < min)
        {
            throw new ArgumentException("The argument counts are inconsistent.", nameof(max));
        }
    }
}

/// <summary>
/// The functions a formula can call, looked up by name ignoring case. Registration is explicit code, never reflection: a function family adds
/// one <c>Register…</c> method that calls <see cref="Add"/> for each of its functions. <see cref="CreateDefault"/> returns a fresh registry with
/// the functions the library ships.
/// </summary>
public sealed class FunctionRegistry
{
    private readonly Dictionary<string, FunctionDefinition> _map = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates an empty registry.</summary>
    public FunctionRegistry()
    {
    }

    /// <summary>The names added a second time (the later definition won); a built-in registry must have none.</summary>
    internal List<string> ReplacedNames { get; } = [];

    /// <summary>The functions registered.</summary>
    public int Count => _map.Count;

    /// <summary>Creates a registry holding every function the library ships.</summary>
    /// <returns>A new, modifiable registry.</returns>
    public static FunctionRegistry CreateDefault()
    {
        var r = new FunctionRegistry();
        SeedFunctions.Register(r);
        StatisticalFunctions.Register(r);
        DateTimeFunctions.Register(r);
        MathFunctions.Register(r);
        LogicalFunctions.Register(r);
        InformationFunctions.Register(r);
        TextFunctions.Register(r);
        LookupFunctions.Register(r);
        ArrayFunctions.Register(r);
        LambdaHelpers.Register(r);
        GroupFunctions.Register(r);
        return r;
    }

    /// <summary>Adds a function, replacing one of the same name.</summary>
    /// <param name="definition">The function.</param>
    public void Add(FunctionDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (_map.ContainsKey(definition.Name))
        {
            ReplacedNames.Add(definition.Name);
        }

        _map[definition.Name] = definition;
    }

    /// <summary>Adds an application function with value arguments and a value result. Arguments lift over arrays and errors propagate before the call.</summary>
    /// <param name="name">The name; formulas call it by this name.</param>
    /// <param name="minArgs">The fewest arguments.</param>
    /// <param name="maxArgs">The most arguments.</param>
    /// <param name="function">The function.</param>
    /// <param name="isVolatile">True when the result changes on every recalculation.</param>
    public void AddCustom(string name, int minArgs, int maxArgs, CustomFunction function, bool isVolatile = false)
    {
        ArgumentNullException.ThrowIfNull(function);
        Add(FunctionDefinition.Create(name, minArgs, maxArgs, [], (_, a) => function(a), isVolatile ? FunctionTraits.Volatile : FunctionTraits.None));
    }

    /// <summary>Finds a function.</summary>
    /// <param name="name">The name; any <c>_xlfn.</c> or <c>_xlws.</c> prefix is ignored.</param>
    /// <param name="definition">The function.</param>
    /// <returns>True when found.</returns>
    public bool TryGet(string name, out FunctionDefinition? definition)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _map.TryGetValue(FunctionCallNode.StripPrefix(name), out definition);
    }

    /// <summary>The registered definitions, in no particular order.</summary>
    public IEnumerable<FunctionDefinition> Definitions => _map.Values;
}
