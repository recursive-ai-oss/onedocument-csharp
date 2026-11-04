using System.Globalization;
using System.Text;
using RecursiveAI.OneOffice.Markup;
using RecursiveAI.OneOffice.Packaging;

namespace RecursiveAI.OneOffice.Documents;

public sealed partial class Document
{
    private const string TransitionalRelationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private const string StrictRelationships = "http://purl.oclc.org/ooxml/officeDocument/relationships/";

    /// <summary>The namespace of WordprocessingML for this document (Transitional or Strict).</summary>
    internal string WordprocessingNamespaceUri =>
        IsStrict ? "http://purl.oclc.org/ooxml/wordprocessingml/main" : HubXml.WordprocessingNamespace;

    /// <summary>The full relationship type for a name in the officeDocument family, in this document's spelling.</summary>
    internal string OfficeRelationship(string name) => (IsStrict ? StrictRelationships : TransitionalRelationships) + name;

    /// <summary>
    /// Adds a part with the given content, related to the main part, and returns its parsed tree. The name is made unique when
    /// the preferred one is taken.
    /// </summary>
    internal MarkupDocument CreatePart(string relationshipType, string preferredName, string contentType, string xml, out string partName)
    {
        partName = preferredName;
        for (int i = 2; Package.FindPart(partName) is not null; i++)
        {
            partName = preferredName.Insert(preferredName.LastIndexOf('.'), i.ToString(CultureInfo.InvariantCulture));
        }

        Package.AddPart(partName, contentType, Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n" + xml));
        var rels = new List<Relationship>(Package.GetRelationships(MainPartName));
        int n = 1;
        while (rels.Exists(r => r.Id == "rId" + n.ToString(CultureInfo.InvariantCulture)))
        {
            n++;
        }

        string target = partName.StartsWith("/word/", StringComparison.OrdinalIgnoreCase) ? partName[6..] : partName;
        rels.Add(new Relationship("rId" + n.ToString(CultureInfo.InvariantCulture), relationshipType, target));
        Package.SetRelationships(MainPartName, rels);
        return Parts.Load(partName, required: true)!;
    }

    /// <summary>Finds a related part of the main part by its full relationship type.</summary>
    internal MarkupDocument? LoadRelatedByType(string relationshipType, out string? partName)
    {
        partName = null;
        foreach (Relationship r in Package.GetRelationships(MainPartName))
        {
            if (r.TargetMode == RelationshipTargetMode.Internal && string.Equals(r.Type, relationshipType, StringComparison.Ordinal)
                && PartUri.TryResolve(new PartUri(MainPartName), r.Target, out PartUri uri) && Package.FindPart(uri.Value) is { } part)
            {
                partName = part.Name;
                return Parts.Load(part.Name, required: false);
            }
        }

        return null;
    }
}
