using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;
using RecursiveAI.OneDocument.Spreadsheets.Calc;

namespace RecursiveAI.OneDocument.Spreadsheets;

public sealed partial class Workbook
{
    private List<ExternalLink>? _externalLinks;

    /// <summary>
    /// Gets the workbook's links to other workbooks, in the order formulas number them (<c>[1]</c> is the first). Reading them parses each
    /// external-link part once; nothing outside the file is opened.
    /// </summary>
    /// <remarks>A part that cannot be read is reported as the defect <c>xlsx.external-link-unreadable</c> and appears as a link with no sheets and no cached values, so the numbering of the others holds.</remarks>
    public IReadOnlyList<ExternalLink> ExternalLinks => _externalLinks ??= LoadExternalLinks();

    private List<ExternalLink> LoadExternalLinks()
    {
        var list = new List<ExternalLink>();
        MarkupNode refs = default;
        foreach (MarkupNode c in _tree.Root.ChildElements)
        {
            if (c.LocalName.SequenceEqual("externalReferences"u8))
            {
                refs = c;
                break;
            }
        }

        if (refs.IsNull)
        {
            return list;
        }

        IReadOnlyList<Relationship> rels = Package.GetRelationships(MainPartName);
        foreach (MarkupNode er in refs.ChildElements)
        {
            string? rid = er.GetAttribute(IsStrict ? RelNsStrict : RelNs, "id");
            int index = list.Count + 1;
            Relationship? rel = null;
            foreach (Relationship r in rels)
            {
                if (r.Id == rid)
                {
                    rel = r;
                    break;
                }
            }

            if (rel is null || rel.TargetMode != RelationshipTargetMode.Internal
                || !PartUri.TryResolve(new PartUri(MainPartName), rel.Target, out PartUri uri) || Package.FindPart(uri.Value) is not { } part)
            {
                Context.Defects.Report(new Defect(DefectKind.Structure, "xlsx.external-link-unreadable", DefectSeverity.Dropped,
                    "An external reference points at a part that is not in the package; its cached values are ignored.", DefectLocation.InPart(MainPartName)));
                list.Add(new ExternalLink(index, MainPartName, string.Empty, [], null));
                continue;
            }

            string target = string.Empty;
            foreach (Relationship r in Package.GetRelationships(uri.Value))
            {
                if (IsRelationship(r.Type, "externalLinkPath"))
                {
                    target = r.Target;
                    break;
                }
            }

            list.Add(ExternalLink.Read(index, uri.Value, part.GetBytes(), target, Context));
        }

        return list;
    }
}
