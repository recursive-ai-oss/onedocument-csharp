using RecursiveAI.OneDocument.Accessibility;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Properties;

namespace RecursiveAI.OneDocument.Presentations;

public sealed partial class Presentation
{
    /// <summary>
    /// Checks the presentation against Office's accessibility rules (see <c>docs/accessibility.md</c>): alternative text, slide titles and their
    /// uniqueness, reading order, tables, section names, media captions, hyperlink text, colour contrast, the title and the language. It edits nothing.
    /// </summary>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>The findings, slide by slide, and any check that could not be made.</returns>
    public override AccessibilityReport CheckAccessibility(CancellationToken cancellationToken = default) =>
        new PresentationAccessibilityChecker(this, cancellationToken).Run();

    /// <summary>
    /// Applies the explicit fixer a finding offers: marks a picture or shape decorative or turns on the table's header row.
    /// Nothing is fixed unless this is called.
    /// </summary>
    /// <param name="issue">A finding from <see cref="CheckAccessibility"/> of this presentation.</param>
    /// <returns>What was done, or why nothing was.</returns>
    public AccessibilityFixResult ApplyAccessibilityFix(AccessibilityIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        if (issue.Fix == AccessibilityFixKind.None || issue.Target.IsNull)
        {
            return new AccessibilityFixResult(false, "The finding has no automatic fix; " + issue.Suggestion);
        }

        string? part = issue.Location.Part;
        if (part is null || !ReferenceEquals(Parts.Load(part, required: false), issue.Target.Document))
        {
            return new AccessibilityFixResult(false, "The finding is not from this presentation.");
        }

        switch (issue.Fix)
        {
            case AccessibilityFixKind.MarkDecorative:
                return DrawingAccessibility.MarkDecorative(issue.Target)
                    ? new AccessibilityFixResult(true, "Marked decorative.")
                    : new AccessibilityFixResult(false, "Already decorative.");
            case AccessibilityFixKind.SetHeaderRow:
                {
                    // ECMA-376 Part 1 §21.1.3.15 (tblPr): firstRow turns on the special formatting of the first row, which is the header row.
                    MarkupNode pr = default;
                    foreach (MarkupNode c in issue.Target.ChildElements)
                    {
                        if (c.LocalNameString == "tblPr")
                        {
                            pr = c;
                        }
                    }

                    if (pr.IsNull)
                    {
                        string prefix = issue.Target.Prefix.Length == 0 ? "" : System.Text.Encoding.UTF8.GetString(issue.Target.Prefix) + ":";
                        issue.Target.PrependXml(System.Text.Encoding.UTF8.GetBytes("<" + prefix + "tblPr " + "firstRow=\"1\"/>"));
                    }
                    else
                    {
                        pr.SetAttribute("firstRow", "1");
                    }

                    return new AccessibilityFixResult(true, "Turned on the header row.");
                }

            default:
                return new AccessibilityFixResult(false, "Unknown fix.");
        }
    }

    /// <summary>Sets the title property (<c>dc:title</c>) that screen readers announce.</summary>
    /// <param name="title">The title.</param>
    /// <returns>What was done.</returns>
    public AccessibilityFixResult SetAccessibilityTitle(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var props = new DocumentProperties(Package, Context);
        props.Core.Title = title;
        props.Commit();
        return new AccessibilityFixResult(true, "Set the title property.");
    }
}
