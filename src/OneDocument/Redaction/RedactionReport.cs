namespace RecursiveAI.OneDocument.Redaction;

/// <summary>Something the operation removed or replaced: where, what kind of place, and how many matches or items.</summary>
/// <param name="Category">The kind of place (<c>body</c>, <c>comments</c>, <c>chart-cache</c>, <c>shared-strings</c>, <c>hyperlink-target</c>, <c>region</c>, <c>thumbnail</c>, …; see docs/redaction.md).</param>
/// <param name="Location">The part (and nested package) it was in.</param>
/// <param name="Count">How many replacements or removals.</param>
public readonly record struct RedactedItem(string Category, string Location, int Count);

/// <summary>Something the operation could not remove, or content it could not look into. Never carries the removed text.</summary>
/// <param name="Code">A stable code, <c>redaction.problem</c> (see docs/redaction.md).</param>
/// <param name="Location">The part, entry or relationship it concerns.</param>
/// <param name="Message">A description.</param>
public readonly record struct RedactionGap(string Code, string Location, string Message);

/// <summary>A place where a match was still found after writing. Never carries the matched text.</summary>
/// <param name="Location">The entry and the kind of place (<c>text</c>, <c>attribute</c>, <c>raw</c>, <c>name</c>).</param>
/// <param name="MatchIndex">The index of the match in <see cref="RedactionPlan.Matches"/>.</param>
public readonly record struct RedactionSurvivor(string Location, int MatchIndex);

/// <summary>What the operation did, what it could not do, and whether re-extraction found anything left.</summary>
public sealed class RedactionReport
{
    private readonly List<RedactedItem> _items = [];
    private readonly List<RedactionGap> _gaps = [];
    private readonly List<RedactionSurvivor> _survivors = [];

    /// <summary>Gets everything removed, one entry per part and category.</summary>
    public IReadOnlyList<RedactedItem> Items => _items;

    /// <summary>Gets what could not be removed or looked into.</summary>
    public IReadOnlyList<RedactionGap> Gaps => _gaps;

    /// <summary>Gets what the verification found; empty when verified.</summary>
    public IReadOnlyList<RedactionSurvivor> Survivors => _survivors;

    /// <summary>Gets a value that says whether verification ran.</summary>
    public bool VerificationRan { get; internal set; }

    /// <summary>Gets a value that says whether the output was re-opened, searched in every entry and found clean. False when it did not run or found survivors.</summary>
    public bool Verified => VerificationRan && _survivors.Count == 0;

    /// <summary>Gets the format that was written. Legacy and other inputs are redacted through the hub and written as OOXML; the original binary is never edited.</summary>
    public OfficeFormat OutputFormat { get; internal set; }

    /// <summary>Gets a value that says whether the output format differs from the input's (a conversion happened).</summary>
    public bool Converted { get; internal set; }

    /// <summary>Gets the total of all item counts.</summary>
    public int TotalReplacements
    {
        get
        {
            int total = 0;
            foreach (RedactedItem item in _items)
            {
                total += item.Count;
            }

            return total;
        }
    }

    internal void Add(string category, string location, int count)
    {
        if (count > 0)
        {
            _items.Add(new RedactedItem(category, location, count));
        }
    }

    internal void Gap(string code, string location, string message) => _gaps.Add(new RedactionGap(code, location, message));

    internal void Survivor(string location, int matchIndex) => _survivors.Add(new RedactionSurvivor(location, matchIndex));
}
