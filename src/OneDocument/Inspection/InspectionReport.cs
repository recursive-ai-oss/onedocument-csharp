using System.Linq;
using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Inspection;

/// <summary>One thing inspection found: where it is, what it is, and how many items it stands for.</summary>
/// <param name="Category">The category.</param>
/// <param name="Location">Where it is: a part name (<c>/word/comments.xml</c>), optionally followed by <c>!</c> and a position inside it (an element path, a sheet and row, a slide number).</param>
/// <param name="Description">A short description in plain words.</param>
/// <param name="Count">How many items the finding stands for; at least 1. A part holding forty comments is one finding of count 40.</param>
/// <param name="Detail">The personal or hidden value itself where it is short and useful (an author name, a property value, a sheet name); null otherwise. Not escaped.</param>
public sealed record InspectionFinding(InspectionCategory Category, string Location, string Description, int Count = 1, string? Detail = null);

/// <summary>What inspection found in one file, grouped by category.</summary>
public sealed class InspectionReport
{
    private readonly List<InspectionFinding> _findings;
    private readonly List<string> _notes;
    private readonly List<string> _tips;

    internal InspectionReport(OfficeFormat format, OfficeFormat sourceFormat, OfficeKind kind, List<InspectionFinding> findings, List<string> notes, List<string> tips)
    {
        Format = format;
        SourceFormat = sourceFormat;
        Kind = kind;
        _findings = findings;
        _notes = notes;
        _tips = tips;
    }

    /// <summary>Gets the format that was inspected: the file's own format for OOXML, or the OOXML format a legacy file was converted to first.</summary>
    public OfficeFormat Format { get; }

    /// <summary>Gets the format of the file as it was given.</summary>
    public OfficeFormat SourceFormat { get; }

    /// <summary>Gets the kind of document.</summary>
    public OfficeKind Kind { get; }

    /// <summary>Gets a value indicating whether the file was converted through the hub before inspection (a legacy or RTF file); findings then describe the converted document.</summary>
    public bool WasConverted => Format != SourceFormat;

    /// <summary>Gets every finding, grouped by category in declaration order and, within a category, in the order found.</summary>
    public IReadOnlyList<InspectionFinding> Findings => _findings;

    /// <summary>Gets notes about the inspection itself (a conversion, a part that could not be read).</summary>
    public IReadOnlyList<string> Notes => _notes;

    /// <summary>Gets tips: things Office's inspector does not report either but a person may want to check, such as white text.</summary>
    public IReadOnlyList<string> Tips => _tips;

    /// <summary>Gets the total count of a category.</summary>
    /// <param name="category">The category.</param>
    /// <returns>The sum of the counts of its findings; 0 when there is none.</returns>
    public int CountOf(InspectionCategory category)
    {
        int n = 0;
        foreach (InspectionFinding f in _findings)
        {
            if (f.Category == category)
            {
                n += f.Count;
            }
        }

        return n;
    }

    /// <summary>Gets the findings of one category.</summary>
    /// <param name="category">The category.</param>
    /// <returns>The findings, possibly none.</returns>
    public IReadOnlyList<InspectionFinding> FindingsOf(InspectionCategory category) => [.. _findings.Where(f => f.Category == category)];

    /// <summary>Gets a value indicating whether nothing was found in any category.</summary>
    public bool IsEmpty => _findings.Count == 0;

    /// <summary>Gets the categories with at least one finding.</summary>
    public IReadOnlyList<InspectionCategory> CategoriesFound => [.. _findings.Select(f => f.Category).Distinct()];

    /// <summary>Writes the report as plain text: one line per category with its count, then the findings indented.</summary>
    /// <returns>The text.</returns>
    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"{Kind} ({SourceFormat}");
        if (WasConverted)
        {
            sb.Append(CultureInfo.InvariantCulture, $", inspected as {Format}");
        }

        sb.Append(")\n");
        foreach (InspectionCategory c in InspectionCategories.All)
        {
            int n = CountOf(c);
            if (n == 0)
            {
                continue;
            }

            sb.Append(CultureInfo.InvariantCulture, $"{InspectionCategories.Name(c)}: {n}\n");
            foreach (InspectionFinding f in _findings)
            {
                if (f.Category == c)
                {
                    sb.Append(CultureInfo.InvariantCulture, $"  {f.Location}: {f.Description}");
                    if (f.Count != 1)
                    {
                        sb.Append(CultureInfo.InvariantCulture, $" ({f.Count})");
                    }

                    if (f.Detail is not null)
                    {
                        sb.Append(CultureInfo.InvariantCulture, $" [{f.Detail}]");
                    }

                    sb.Append('\n');
                }
            }
        }

        if (IsEmpty)
        {
            sb.Append("nothing found\n");
        }

        foreach (string n in _notes)
        {
            sb.Append("note: ").Append(n).Append('\n');
        }

        foreach (string t in _tips)
        {
            sb.Append("tip: ").Append(t).Append('\n');
        }

        return sb.ToString();
    }
}

/// <summary>What happened to one category during sanitising.</summary>
public enum SanitizeStatus
{
    /// <summary>The category was not selected; whatever is there stays.</summary>
    NotSelected,

    /// <summary>The category was selected and the file had nothing in it.</summary>
    NothingFound,

    /// <summary>The category was selected and everything found was removed.</summary>
    Removed,

    /// <summary>The category was selected, some of it was removed and some could not be (see <see cref="SanitizeReport.NotRemoved"/>).</summary>
    PartlyRemoved,

    /// <summary>The category was selected but sanitising never removes it (it is reported only); see <see cref="InspectionCategories.IsReportOnly"/>.</summary>
    ReportOnly,
}

/// <summary>An item sanitising removed.</summary>
/// <param name="Category">The category.</param>
/// <param name="Location">Where it was: a part name, optionally with a position.</param>
/// <param name="Description">What was removed.</param>
/// <param name="Count">How many items.</param>
public sealed record RemovedItem(InspectionCategory Category, string Location, string Description, int Count = 1);

/// <summary>An item sanitising did not remove although its category was selected, and why.</summary>
/// <param name="Category">The category.</param>
/// <param name="Code">A stable code: <c>sanitize.referenced</c>, <c>sanitize.shared-container</c>, <c>sanitize.unsupported-construct</c>, <c>sanitize.report-only</c>.</param>
/// <param name="Location">Where it is.</param>
/// <param name="Reason">Why it stays, in plain words.</param>
public sealed record NotRemovedItem(InspectionCategory Category, string Code, string Location, string Reason);

/// <summary>What sanitising did: what it removed, what it could not remove, and the inspection of the result.</summary>
public sealed class SanitizeReport
{
    private readonly List<RemovedItem> _removed;
    private readonly List<NotRemovedItem> _notRemoved;
    private readonly List<string> _notes;
    private readonly Dictionary<InspectionCategory, SanitizeStatus> _status;

    internal SanitizeReport(
        OfficeFormat sourceFormat,
        OfficeFormat outputFormat,
        InspectionReport before,
        InspectionReport after,
        List<RemovedItem> removed,
        List<NotRemovedItem> notRemoved,
        List<string> notes,
        Dictionary<InspectionCategory, SanitizeStatus> status)
    {
        SourceFormat = sourceFormat;
        OutputFormat = outputFormat;
        Before = before;
        After = after;
        _removed = removed;
        _notRemoved = notRemoved;
        _notes = notes;
        _status = status;
    }

    /// <summary>Gets the format of the input.</summary>
    public OfficeFormat SourceFormat { get; }

    /// <summary>Gets the format written. It differs from the input for legacy and RTF files (converted to OOXML) and for macro-enabled files whose project was removed (the macro-free counterpart).</summary>
    public OfficeFormat OutputFormat { get; }

    /// <summary>Gets the inspection of the input (after conversion, for a legacy file).</summary>
    public InspectionReport Before { get; }

    /// <summary>Gets the inspection of the output file as written. Every category with status <see cref="SanitizeStatus.Removed"/> or <see cref="SanitizeStatus.NothingFound"/> is empty in it.</summary>
    public InspectionReport After { get; }

    /// <summary>Gets what was removed.</summary>
    public IReadOnlyList<RemovedItem> Removed => _removed;

    /// <summary>Gets what could not be removed in the categories that were selected.</summary>
    public IReadOnlyList<NotRemovedItem> NotRemoved => _notRemoved;

    /// <summary>Gets notes: the conversion, the macro-free format change, anything that changed beyond removal.</summary>
    public IReadOnlyList<string> Notes => _notes;

    /// <summary>Gets what happened to a category.</summary>
    /// <param name="category">The category.</param>
    /// <returns>The status.</returns>
    public SanitizeStatus StatusOf(InspectionCategory category) => _status.TryGetValue(category, out SanitizeStatus s) ? s : SanitizeStatus.NotSelected;

    /// <summary>Gets how many items of a category were removed.</summary>
    /// <param name="category">The category.</param>
    /// <returns>The sum of the counts of the removed items.</returns>
    public int RemovedCount(InspectionCategory category) => _removed.Where(r => r.Category == category).Sum(r => r.Count);

    /// <summary>Writes the report as plain text.</summary>
    /// <returns>The text.</returns>
    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"{SourceFormat} -> {OutputFormat}\n");
        foreach (InspectionCategory c in InspectionCategories.All)
        {
            SanitizeStatus s = StatusOf(c);
            if (s == SanitizeStatus.NotSelected || s == SanitizeStatus.NothingFound)
            {
                continue;
            }

            sb.Append(CultureInfo.InvariantCulture, $"{InspectionCategories.Name(c)}: {s}, {RemovedCount(c)} removed, {Before.CountOf(c)} before, {After.CountOf(c)} after\n");
            foreach (RemovedItem r in _removed)
            {
                if (r.Category == c)
                {
                    sb.Append(CultureInfo.InvariantCulture, $"  removed {r.Location}: {r.Description}{(r.Count != 1 ? $" ({r.Count})" : string.Empty)}\n");
                }
            }

            foreach (NotRemovedItem n in _notRemoved)
            {
                if (n.Category == c)
                {
                    sb.Append(CultureInfo.InvariantCulture, $"  kept {n.Location}: {n.Reason} [{n.Code}]\n");
                }
            }
        }

        foreach (string n in _notes)
        {
            sb.Append("note: ").Append(n).Append('\n');
        }

        return sb.ToString();
    }
}

/// <summary>Sanitising wrote a file in which a category it claims to have removed is not empty. Nothing was written to the destination.</summary>
public sealed class SanitizeVerificationException : OfficeException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="category">The category that is not empty.</param>
    /// <param name="remaining">What re-inspection still finds.</param>
    public SanitizeVerificationException(InspectionCategory category, IReadOnlyList<InspectionFinding> remaining)
        : base(new Defect(
            DefectKind.Structure,
            "sanitize.verification-failed",
            DefectSeverity.Fatal,
            "Re-inspecting the sanitised file still finds " + InspectionCategories.Name(category) + ": " + string.Join("; ", remaining.Take(3).Select(f => f.Location + " " + f.Description)),
            remaining.Count > 0 ? DefectLocation.InPart(remaining[0].Location.Split('!')[0]) : DefectLocation.None))
    {
        Category = category;
        Remaining = remaining;
    }

    /// <summary>Gets the category that is not empty.</summary>
    public InspectionCategory Category { get; }

    /// <summary>Gets the findings that remain.</summary>
    public IReadOnlyList<InspectionFinding> Remaining { get; }
}
