using RecursiveAI.OneDocument.Documents.Numbering;
using RecursiveAI.OneDocument.Documents.Styles;

namespace RecursiveAI.OneDocument.Documents;

public sealed partial class Paragraph
{
    /// <summary>Gets the effective paragraph properties: defaults, table style, numbering, paragraph style and direct formatting resolved, each value with its origin (<see cref="StyleSheet.ResolveParagraph"/>).</summary>
    public ResolvedParagraphProperties ResolvedProperties => Owner.Styles.ResolveParagraph(this);

    /// <summary>Gets the list the paragraph is on, or null when it is not numbered (<see cref="StyleSheet.GetNumbering"/>).</summary>
    public NumberingReference? Numbering => Owner.Styles.GetNumbering(this);

    /// <summary>
    /// Puts the paragraph on a list: sets <c>w:numPr</c> in its direct properties. The ids are not checked against the numbering part
    /// here; a reference to a missing list is reported when labels are computed.
    /// </summary>
    /// <param name="numberingId">The <c>w:numId</c> of a numbering instance; 0 means no numbering.</param>
    /// <param name="level">The zero-based level, 0 to 8.</param>
    /// <exception cref="ArgumentOutOfRangeException">The id is negative or the level is outside 0 to 8.</exception>
    public void SetNumbering(long numberingId, int level = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(numberingId);
        ArgumentOutOfRangeException.ThrowIfNegative(level);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(level, 8);
        var numPr = View.GetOrAddParagraphProperties().GetOrAddNumberingProperties();
        numPr.GetOrAddIndentLevel().Val = level;
        numPr.GetOrAddNumberingId().Val = numberingId;
    }

    /// <summary>Takes the paragraph off any list, even one its style puts it on (<c>w:numId</c> 0).</summary>
    public void ClearNumbering() => SetNumbering(0, 0);
}

public sealed partial class Run
{
    /// <summary>Gets the effective run properties: defaults, table style, paragraph style, character style and direct formatting resolved, toggle properties combined (<see cref="StyleSheet.ResolveRun"/>).</summary>
    public ResolvedRunProperties ResolvedProperties => Owner.Styles.ResolveRun(this);
}
