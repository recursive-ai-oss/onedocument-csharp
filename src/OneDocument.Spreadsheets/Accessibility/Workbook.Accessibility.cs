using RecursiveAI.OneDocument.Accessibility;
using RecursiveAI.OneDocument.Properties;

namespace RecursiveAI.OneDocument.Spreadsheets;

public sealed partial class Workbook
{
    /// <summary>
    /// Checks the workbook against Office's accessibility rules (see <c>docs/accessibility.md</c>): alternative text on drawings, sheet and table
    /// names, blank sheets, tables without a header row, merged cells, hyperlink text, colour contrast of cell text, red-only negative number
    /// formats and the title. It reads cell formats and cached values; it never recalculates and edits nothing.
    /// </summary>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>The findings, sheet by sheet, and any check that could not be made.</returns>
    public override AccessibilityReport CheckAccessibility(CancellationToken cancellationToken = default) =>
        new WorkbookAccessibilityChecker(this, cancellationToken).Run();

    /// <summary>Sets the title property (<c>dc:title</c>) that screen readers announce.</summary>
    /// <param name="title">The title.</param>
    /// <returns>What was done.</returns>
    public AccessibilityFixResult SetAccessibilityTitle(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var props = new DocumentProperties(Package, OpenContext);
        props.Core.Title = title;
        props.Commit();
        return new AccessibilityFixResult(true, "Set the title property.");
    }
}
