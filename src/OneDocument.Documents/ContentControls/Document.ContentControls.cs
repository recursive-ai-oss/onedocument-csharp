using RecursiveAI.OneDocument.Documents.ContentControls;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Documents;

public sealed partial class Document
{
    private List<CustomXmlPart>? _customXmlParts;

    /// <summary>
    /// Gets the custom XML parts the main part relates to (<c>customXml</c> relationships), with their store item ids. Content
    /// control bindings resolve against these. Parts that cannot be read are skipped (the part cache reports <c>docx.part-unreadable</c>).
    /// </summary>
    public IReadOnlyList<CustomXmlPart> CustomXmlParts
    {
        get
        {
            if (_customXmlParts is null)
            {
                var list = new List<CustomXmlPart>();
                foreach (Relationship r in Package.GetRelationships(MainPartName))
                {
                    if (RelationshipTypes.Is(r.Type, "customXml") && CustomXmlPart.Read(this, r) is { } part)
                    {
                        list.Add(part);
                    }
                }

                _customXmlParts = list;
            }

            return _customXmlParts;
        }
    }

    /// <summary>The store item id that bindings use for the package's core properties (title, subject, author …), which is not a custom XML part.</summary>
    internal const string CorePropertiesStoreId = "{6C3C8BC8-F283-45AE-878A-BAB7291924A1}";

    private CustomXmlPart? _coreProperties;
    private bool _coreResolved;

    /// <summary>The core properties part as a data store, or null when the package has none.</summary>
    internal CustomXmlPart? CorePropertiesStore
    {
        get
        {
            if (!_coreResolved)
            {
                _coreResolved = true;
                foreach (Relationship r in Package.GetRelationships())
                {
                    // Word binds to the core properties by this fixed id; the part is found by the package relationship (observed in Word-written files).
                    if (r.TargetMode == RelationshipTargetMode.Internal
                        && r.Type.EndsWith("/metadata/core-properties", StringComparison.Ordinal)
                        && PartUri.TryResolve(null, r.Target, out PartUri uri) && Package.FindPart(uri.Value) is { } part
                        && Parts.Load(part.Name, required: false) is { } data)
                    {
                        _coreProperties = new CustomXmlPart(part.Name, data, CorePropertiesStoreId, []);
                        break;
                    }
                }
            }

            return _coreProperties;
        }
    }

    /// <summary>
    /// Enumerates every content control of the main part and of its headers, footers, footnotes and endnotes, in document order
    /// (a control before the controls nested in it), whatever the level: block, inline, row and cell.
    /// </summary>
    /// <returns>The controls; edits made while enumerating are not supported.</returns>
    public IEnumerable<ContentControl> ContentControls()
    {
        foreach (string partName in ContentPartNames())
        {
            MarkupDocument? doc = partName == MainPartName ? MainMarkup : Parts.Load(partName, required: false);
            if (doc is null || doc.Root.IsNull)
            {
                continue;
            }

            foreach (MarkupNode n in DocumentOrder.Elements(doc.Root, stopAt: null))
            {
                var e = new RecursiveAI.OneDocument.Schema.MarkupRef(n);
                if (Wml.Is(e, "sdt"u8))
                {
                    yield return new ContentControl(this, e, partName);
                }
            }
        }
    }

    private IEnumerable<string> ContentPartNames()
    {
        yield return MainPartName;
        foreach (Relationship r in Package.GetRelationships(MainPartName))
        {
            if (r.TargetMode == RelationshipTargetMode.Internal
                && (RelationshipTypes.Is(r.Type, "header") || RelationshipTypes.Is(r.Type, "footer")
                    || RelationshipTypes.Is(r.Type, "footnotes") || RelationshipTypes.Is(r.Type, "endnotes"))
                && PartUri.TryResolve(new PartUri(MainPartName), r.Target, out PartUri uri) && Package.FindPart(uri.Value) is { } part)
            {
                yield return part.Name;
            }
        }
    }
}
