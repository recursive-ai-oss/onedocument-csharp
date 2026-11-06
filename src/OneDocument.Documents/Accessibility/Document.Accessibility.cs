using System.Text;
using RecursiveAI.OneDocument.Accessibility;
using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Documents;

public sealed partial class Document
{
    /// <summary>
    /// Checks the document against Office's accessibility rules (see <c>docs/accessibility.md</c>): alternative text, table headers and structure,
    /// headings, spacing, floating objects, hyperlink text, content control titles, colour contrast, the title and the language. It reads the body,
    /// headers, footers, footnotes and endnotes, and edits nothing.
    /// </summary>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>The findings, in document order, and any check that could not be made.</returns>
    /// <exception cref="OfficeFormatException">The main part is damaged (raised at first use, as for every access to it).</exception>
    public override AccessibilityReport CheckAccessibility(CancellationToken cancellationToken = default) =>
        new DocumentAccessibilityChecker(this, cancellationToken).Run();

    /// <summary>
    /// Applies the explicit fixer a finding offers: marks a picture or shape decorative (<see cref="AccessibilityFixKind.MarkDecorative"/>) or marks
    /// the first row of a table as its header row (<see cref="AccessibilityFixKind.SetHeaderRow"/>). Nothing is fixed unless this is called.
    /// </summary>
    /// <param name="issue">A finding from <see cref="CheckAccessibility"/> of this document.</param>
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
            return new AccessibilityFixResult(false, "The finding is not from this document.");
        }

        switch (issue.Fix)
        {
            case AccessibilityFixKind.MarkDecorative:
                return DrawingAccessibility.MarkDecorative(issue.Target)
                    ? new AccessibilityFixResult(true, "Marked decorative.")
                    : new AccessibilityFixResult(false, "Already decorative.");
            case AccessibilityFixKind.SetHeaderRow:
                return MarkHeaderRow(issue.Target);
            default:
                return new AccessibilityFixResult(false, "Unknown fix.");
        }
    }

    private static AccessibilityFixResult MarkHeaderRow(MarkupNode row)
    {
        // ECMA-376 Part 1 §17.4.49 (tblHeader) inside the row's w:trPr (§17.4.82); w:trPr follows w:tblPrEx and precedes the cells.
        string prefix = row.Prefix.Length == 0 ? "" : Encoding.UTF8.GetString(row.Prefix) + ":";
        MarkupNode trPr = default;
        MarkupNode firstCell = default;
        foreach (MarkupNode c in row.ChildElements)
        {
            if (c.LocalNameString == "trPr")
            {
                trPr = c;
            }
            else if (c.LocalNameString == "tc" && firstCell.IsNull)
            {
                firstCell = c;
            }
        }

        byte[] marker = Encoding.UTF8.GetBytes("<" + prefix + "tblHeader/>");
        if (!trPr.IsNull)
        {
            foreach (MarkupNode c in trPr.ChildElements)
            {
                if (c.LocalNameString == "tblHeader")
                {
                    return new AccessibilityFixResult(false, "The row is a header row already.");
                }
            }

            trPr.AppendXml(marker);
        }
        else if (!firstCell.IsNull)
        {
            firstCell.InsertXmlBefore(Encoding.UTF8.GetBytes("<" + prefix + "trPr><" + prefix + "tblHeader/></" + prefix + "trPr>"));
        }
        else
        {
            return new AccessibilityFixResult(false, "The row has no cells.");
        }

        return new AccessibilityFixResult(true, "Marked the first row as the header row.");
    }

    /// <summary>Sets the document title property (<c>dc:title</c>) that screen readers announce.</summary>
    /// <param name="title">The title.</param>
    /// <returns>What was done.</returns>
    public AccessibilityFixResult SetAccessibilityTitle(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        Properties.Core.Title = title;
        return new AccessibilityFixResult(true, "Set the title property.");
    }

    /// <summary>
    /// Sets the default proofing language (<c>w:lang</c> in the document defaults of the style sheet). A document without a style sheet is left as it is.
    /// </summary>
    /// <param name="language">A language tag such as <c>en-US</c>.</param>
    /// <returns>What was done, or why nothing was.</returns>
    public AccessibilityFixResult SetAccessibilityLanguage(string language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        MarkupDocument? styles = Parts.LoadRelated(MainPartName, "styles", out _);
        if (styles is null)
        {
            return new AccessibilityFixResult(false, "The document has no style sheet to hold a default language.");
        }

        MarkupNode defaults = default;
        foreach (MarkupNode c in styles.Root.ChildElements)
        {
            if (c.LocalNameString == "docDefaults")
            {
                defaults = c;
            }
        }

        string prefix = styles.Root.Prefix.Length == 0 ? "" : Encoding.UTF8.GetString(styles.Root.Prefix) + ":";
        string lang = System.Security.SecurityElement.Escape(language);
        if (defaults.IsNull)
        {
            return new AccessibilityFixResult(false, "The style sheet has no document defaults to hold a default language.");
        }

        MarkupNode rPrDefault = default;
        foreach (MarkupNode c in defaults.ChildElements)
        {
            if (c.LocalNameString == "rPrDefault")
            {
                rPrDefault = c;
            }
        }

        if (rPrDefault.IsNull)
        {
            defaults.PrependXml(Encoding.UTF8.GetBytes("<" + prefix + "rPrDefault><" + prefix + "rPr><" + prefix + "lang " + prefix + "val=\"" + lang + "\"/></" + prefix + "rPr></" + prefix + "rPrDefault>"));
            return new AccessibilityFixResult(true, "Set the default language.");
        }

        MarkupNode rPr = default;
        foreach (MarkupNode c in rPrDefault.ChildElements)
        {
            if (c.LocalNameString == "rPr")
            {
                rPr = c;
            }
        }

        if (rPr.IsNull)
        {
            rPrDefault.AppendXml(Encoding.UTF8.GetBytes("<" + prefix + "rPr><" + prefix + "lang " + prefix + "val=\"" + lang + "\"/></" + prefix + "rPr>"));
            return new AccessibilityFixResult(true, "Set the default language.");
        }

        foreach (MarkupNode c in rPr.ChildElements)
        {
            if (c.LocalNameString == "lang")
            {
                c.SetAttribute(c.NamespaceUri, "val", language, prefix.Length == 0 ? "w" : prefix[..^1]);
                return new AccessibilityFixResult(true, "Changed the default language.");
            }
        }

        // ECMA-376 Part 1 §17.3.2.28 (rPr): w:lang comes after the formatting elements and before w:eastAsianLayout, w:specVanish and w:oMath.
        MarkupNode before = default;
        foreach (MarkupNode c in rPr.ChildElements)
        {
            if (c.LocalNameString is "eastAsianLayout" or "specVanish" or "oMath")
            {
                before = c;
                break;
            }
        }

        byte[] xml = Encoding.UTF8.GetBytes("<" + prefix + "lang " + prefix + "val=\"" + lang + "\"/>");
        if (before.IsNull)
        {
            rPr.AppendXml(xml);
        }
        else
        {
            before.InsertXmlBefore(xml);
        }

        return new AccessibilityFixResult(true, "Set the default language.");
    }
}
