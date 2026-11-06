using System.Linq;
using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Operations;

/// <summary>
/// One conflict an operation met while combining documents, and how it was settled.
/// </summary>
/// <param name="Code">A stable code of the form <c>merge.problem</c>, for example <c>merge.style-conflict</c> or <c>merge.bookmark-renamed</c>.</param>
/// <param name="Item">What the conflict was about (a style name, a bookmark name, a list, a sheet name …).</param>
/// <param name="Resolution">What was done: <c>kept destination</c>, <c>used source</c>, <c>kept both</c>, <c>renamed to …</c>, <c>reused</c>, <c>copied</c>.</param>
/// <param name="Detail">A sentence for people.</param>
public sealed record MergeResolution(string Code, string Item, string Resolution, string Detail);

/// <summary>
/// What a merge, append, copy or insert did: counts of what moved, each conflict with its resolution, what was lost on the way and
/// what could not be done. The destination is edited in place; this report is the only record, so nothing is silent.
/// </summary>
public sealed class MergeReport
{
    private readonly Dictionary<string, long> _counts = new(StringComparer.Ordinal);
    private readonly List<MergeResolution> _resolutions = [];
    private readonly List<Defect> _problems = [];

    /// <summary>Gets the counts of what moved, by name (for example <c>blocks</c>, <c>styles</c>, <c>numbering</c>, <c>images</c>, <c>footnotes</c>, <c>comments</c>, <c>slides</c>, <c>sheets</c>). A name that did not occur is absent.</summary>
    public IReadOnlyDictionary<string, long> Counts => _counts;

    /// <summary>Gets every conflict and how it was resolved, in the order they were met.</summary>
    public IReadOnlyList<MergeResolution> Resolutions => _resolutions;

    /// <summary>Gets what the destination lost or did not take over from the source (fonts, theme, document defaults, data bindings …), as a loss report.</summary>
    public LossReport Loss { get; } = new();

    /// <summary>Gets what could not be done, each with a code and a location in the source or the destination (severity <see cref="DefectSeverity.Dropped"/>).</summary>
    public IReadOnlyList<Defect> Problems => _problems;

    /// <summary>Gets the number counted under <paramref name="name"/>, or zero.</summary>
    /// <param name="name">A name from <see cref="Counts"/>.</param>
    /// <returns>The count.</returns>
    public long CountOf(string name) => _counts.TryGetValue(name, out long n) ? n : 0;

    /// <summary>Adds to a count.</summary>
    /// <param name="name">The count's name.</param>
    /// <param name="by">The amount.</param>
    public void Count(string name, long by = 1)
    {
        _counts.TryGetValue(name, out long n);
        _counts[name] = n + by;
    }

    /// <summary>Records a resolved conflict.</summary>
    /// <param name="resolution">The conflict and its resolution.</param>
    public void Resolve(MergeResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        _resolutions.Add(resolution);
    }

    /// <summary>Records something that could not be done.</summary>
    /// <param name="code">The code (<c>merge.problem</c>).</param>
    /// <param name="message">A sentence for people.</param>
    /// <param name="location">Where, in the source or the destination.</param>
    public void Problem(string code, string message, DefectLocation location) =>
        _problems.Add(new Defect(DefectKind.Structure, code, DefectSeverity.Dropped, message, location));

    /// <summary>Gets the resolutions with the given code.</summary>
    /// <param name="code">The conflict code.</param>
    /// <returns>The matching resolutions.</returns>
    public IEnumerable<MergeResolution> ResolutionsOf(string code) => _resolutions.Where(r => r.Code == code);
}
