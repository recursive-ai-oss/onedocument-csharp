using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Spreadsheets;

// What the streaming reader takes from the workbook part: the date system and the parts it reads on its own.
public sealed partial class Workbook
{
    /// <summary>Gets a value indicating whether the workbook counts dates from 1904 (<c>workbookPr date1904</c>).</summary>
    internal bool Uses1904Dates
    {
        get
        {
            foreach (MarkupNode c in Markup.Root.ChildElements)
            {
                if (c.LocalName.SequenceEqual("workbookPr"u8))
                {
                    return c.GetAttribute("date1904") is "1" or "true";
                }
            }

            return false;
        }
    }

    /// <summary>Finds the part the workbook part relates to with the given relationship name (for example <c>styles</c>), or null.</summary>
    internal IPackagePart? FindRelatedPart(string relationshipName)
    {
        foreach (Relationship r in Package.GetRelationships(MainPartName))
        {
            if (IsRelationship(r.Type, relationshipName) && r.TargetMode == RelationshipTargetMode.Internal
                && PartUri.TryResolve(new PartUri(MainPartName), r.Target, out PartUri uri) && Package.FindPart(uri.Value) is { } part)
            {
                return part;
            }
        }

        return null;
    }
}
