using System.Linq;
using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Accessibility;

/// <summary>
/// Where an accessibility finding is. Only the fields that make sense for the kind of document are set: a story, paragraph and run in
/// a word-processing document; a sheet and range in a workbook; a slide and shape in a presentation. <see cref="Description"/> says it in words.
/// </summary>
public sealed record AccessibilityLocation
{
    /// <summary>Gets the part URI the finding is in, or null for the file as a whole.</summary>
    public string? Part { get; init; }

    /// <summary>Gets the story (<c>body</c>, <c>header</c>, <c>footer</c>, <c>footnotes</c>, <c>endnotes</c>), or null.</summary>
    public string? Story { get; init; }

    /// <summary>Gets the 1-based paragraph number inside the story, counting every paragraph in document order (table cells included), or null.</summary>
    public int? Paragraph { get; init; }

    /// <summary>Gets the 1-based run number inside the paragraph, or null.</summary>
    public int? Run { get; init; }

    /// <summary>Gets the 1-based number of the table inside its story, or null.</summary>
    public int? Table { get; init; }

    /// <summary>Gets the sheet name, or null.</summary>
    public string? Sheet { get; init; }

    /// <summary>Gets the cell or range in A1 notation, or null.</summary>
    public string? Range { get; init; }

    /// <summary>Gets the 1-based slide number, or null.</summary>
    public int? Slide { get; init; }

    /// <summary>Gets the shape's <c>id</c> as stored in the file, or null.</summary>
    public long? ShapeId { get; init; }

    /// <summary>Gets the section name (presentations), or null.</summary>
    public string? Section { get; init; }

    /// <summary>Gets the location in words, such as <c>body, paragraph 4, run 2</c>.</summary>
    public string Description
    {
        get
        {
            var sb = new StringBuilder();
            void Add(string s)
            {
                if (sb.Length > 0)
                {
                    sb.Append(", ");
                }

                sb.Append(s);
            }

            if (Sheet is not null)
            {
                Add("sheet '" + Sheet + "'");
            }

            if (Range is not null)
            {
                Add(Range);
            }

            if (Slide is { } sl)
            {
                Add("slide " + sl.ToString(CultureInfo.InvariantCulture));
            }

            if (Section is not null)
            {
                Add("section '" + Section + "'");
            }

            if (ShapeId is { } sh)
            {
                Add("shape " + sh.ToString(CultureInfo.InvariantCulture));
            }

            if (Story is not null)
            {
                Add(Story);
            }

            if (Table is { } t)
            {
                Add("table " + t.ToString(CultureInfo.InvariantCulture));
            }

            if (Paragraph is { } p)
            {
                Add("paragraph " + p.ToString(CultureInfo.InvariantCulture));
            }

            if (Run is { } r)
            {
                Add("run " + r.ToString(CultureInfo.InvariantCulture));
            }

            return sb.Length == 0 ? "document" : sb.ToString();
        }
    }

    /// <inheritdoc/>
    public override string ToString() => Description;
}

/// <summary>The change an explicit fixer can make for a finding.</summary>
public enum AccessibilityFixKind
{
    /// <summary>No fixer applies: the fix needs a person's judgement (writing alternative text, restructuring a table).</summary>
    None = 0,

    /// <summary>Mark the picture or shape as decorative.</summary>
    MarkDecorative = 1,

    /// <summary>Mark the first row of the table as its header row.</summary>
    SetHeaderRow = 2,
}

/// <summary>One accessibility finding.</summary>
public sealed class AccessibilityIssue
{
    /// <summary>Creates a finding.</summary>
    /// <param name="rule">The rule it falls under.</param>
    /// <param name="location">Where it is.</param>
    /// <param name="objectName">The name of the object, such as a picture's name, a sheet's name or a table's name; or null.</param>
    /// <param name="message">What is wrong, in plain words.</param>
    /// <param name="suggestion">How to fix it, in plain words.</param>
    /// <param name="fix">The explicit fixer that can resolve it, or <see cref="AccessibilityFixKind.None"/>.</param>
    /// <param name="target">The element the fixer works on, when there is one.</param>
    public AccessibilityIssue(AccessibilityRule rule, AccessibilityLocation location, string? objectName, string message, string suggestion,
        AccessibilityFixKind fix = AccessibilityFixKind.None, MarkupNode target = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(location);
        Rule = rule;
        Location = location;
        ObjectName = objectName;
        Message = message;
        Suggestion = suggestion;
        Fix = fix;
        Target = target;
    }

    /// <summary>Gets the rule.</summary>
    public AccessibilityRule Rule { get; }

    /// <summary>Gets the rule identifier.</summary>
    public string RuleId => Rule.Id;

    /// <summary>Gets the category of the rule.</summary>
    public AccessibilityCategory Category => Rule.Category;

    /// <summary>Gets where the finding is.</summary>
    public AccessibilityLocation Location { get; }

    /// <summary>Gets the object's name, or null.</summary>
    public string? ObjectName { get; }

    /// <summary>Gets what is wrong.</summary>
    public string Message { get; }

    /// <summary>Gets how to fix it.</summary>
    public string Suggestion { get; }

    /// <summary>Gets the explicit fixer that applies, or <see cref="AccessibilityFixKind.None"/>.</summary>
    public AccessibilityFixKind Fix { get; }

    /// <summary>Gets the element in the file's markup the fixer works on; a null node when there is none.</summary>
    public MarkupNode Target { get; }

    /// <inheritdoc/>
    public override string ToString() => Rule.Id + " (" + Category + ") at " + Location.Description + ": " + Message;
}

/// <summary>A check that could not be made, with the reason. Never silent: a rule that cannot be evaluated for part of a file says so here.</summary>
/// <param name="RuleId">The rule that was not evaluated.</param>
/// <param name="Location">Where.</param>
/// <param name="Reason">Why, in plain words.</param>
public sealed record AccessibilitySkip(string RuleId, AccessibilityLocation Location, string Reason);

/// <summary>The result of an accessibility check.</summary>
public sealed class AccessibilityReport
{
    internal AccessibilityReport(OfficeKind kind, IReadOnlyList<AccessibilityIssue> issues, IReadOnlyList<AccessibilitySkip> skipped)
    {
        Kind = kind;
        Issues = issues;
        Skipped = skipped;
    }

    /// <summary>Gets the kind of document that was checked.</summary>
    public OfficeKind Kind { get; }

    /// <summary>Gets the findings, in document order.</summary>
    public IReadOnlyList<AccessibilityIssue> Issues { get; }

    /// <summary>Gets the checks that could not be made (for example the contrast of text on a gradient).</summary>
    public IReadOnlyList<AccessibilitySkip> Skipped { get; }

    /// <summary>Gets the number of findings in a category.</summary>
    /// <param name="category">The category.</param>
    /// <returns>The count.</returns>
    public int Count(AccessibilityCategory category) => Issues.Count(i => i.Category == category);

    /// <summary>Gets the findings of one rule.</summary>
    /// <param name="ruleId">The rule identifier.</param>
    /// <returns>The findings, in document order.</returns>
    public IEnumerable<AccessibilityIssue> ForRule(string ruleId) => Issues.Where(i => i.RuleId == ruleId);

    /// <summary>Gets whether there are no errors (warnings and tips do not count).</summary>
    public bool HasNoErrors => !Issues.Any(i => i.Category == AccessibilityCategory.Error);
}

/// <summary>
/// Collects findings while a document is checked. The kind packages use it; callers get an <see cref="AccessibilityReport"/>.
/// </summary>
public sealed class AccessibilityCollector
{
    private readonly List<AccessibilityIssue> _issues = [];
    private readonly List<AccessibilitySkip> _skipped = [];
    private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);
    private readonly OfficeKind _kind;
    private readonly int _maxPerRule;

    /// <summary>Creates a collector.</summary>
    /// <param name="kind">The kind of document.</param>
    /// <param name="maxPerRule">The most findings kept for one rule; further ones are counted and reported as one entry of <see cref="AccessibilityReport.Skipped"/>. Positive.</param>
    public AccessibilityCollector(OfficeKind kind, int maxPerRule = 10_000)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPerRule);
        _kind = kind;
        _maxPerRule = maxPerRule;
    }

    /// <summary>Adds a finding.</summary>
    /// <param name="issue">The finding.</param>
    public void Add(AccessibilityIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        int n = _counts.GetValueOrDefault(issue.RuleId) + 1;
        _counts[issue.RuleId] = n;
        if (n <= _maxPerRule)
        {
            _issues.Add(issue);
        }
    }

    /// <summary>Records a check that could not be made.</summary>
    /// <param name="rule">The rule.</param>
    /// <param name="location">Where.</param>
    /// <param name="reason">Why.</param>
    public void Skip(AccessibilityRule rule, AccessibilityLocation location, string reason)
    {
        ArgumentNullException.ThrowIfNull(rule);
        _skipped.Add(new AccessibilitySkip(rule.Id, location, reason));
    }

    /// <summary>Builds the report.</summary>
    /// <returns>The report.</returns>
    public AccessibilityReport ToReport()
    {
        var skipped = new List<AccessibilitySkip>(_skipped);
        foreach ((string rule, int n) in _counts)
        {
            if (n > _maxPerRule)
            {
                skipped.Add(new AccessibilitySkip(rule, new AccessibilityLocation(), (n - _maxPerRule).ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + " further findings of this rule are not listed (the limit is " + _maxPerRule.ToString(System.Globalization.CultureInfo.InvariantCulture) + " per rule)."));
            }
        }

        return new AccessibilityReport(_kind, _issues.ToArray(), skipped.ToArray());
    }
}

/// <summary>What an explicit fixer did.</summary>
/// <param name="Applied">True when the document was edited.</param>
/// <param name="Message">What was done, or why nothing was.</param>
public readonly record struct AccessibilityFixResult(bool Applied, string Message);
