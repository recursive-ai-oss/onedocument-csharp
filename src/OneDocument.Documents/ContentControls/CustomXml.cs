using System.Globalization;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Documents.ContentControls;

/// <summary>
/// A custom XML part of the document (<c>/customXml/itemN.xml</c>): the data store a content control's <c>w:dataBinding</c> points at.
/// The part is read through the markup tree and is never edited here.
/// </summary>
public sealed class CustomXmlPart
{
    internal CustomXmlPart(string partName, MarkupDocument data, string? itemId, IReadOnlyList<string> schemaUris)
    {
        PartName = partName;
        Data = data;
        ItemId = itemId;
        SchemaUris = schemaUris;
    }

    /// <summary>Gets the part name, for example <c>/customXml/item1.xml</c>.</summary>
    public string PartName { get; }

    /// <summary>Gets the part's markup tree.</summary>
    public MarkupDocument Data { get; }

    /// <summary>
    /// Gets the store item id (<c>ds:itemID</c> of the related properties part, normally a braced GUID), which bindings name in
    /// <c>w:storeItemID</c>; null when the part has no properties part.
    /// </summary>
    public string? ItemId { get; }

    /// <summary>Gets the schema namespaces the properties part lists (<c>ds:schemaRef</c>); empty when none.</summary>
    public IReadOnlyList<string> SchemaUris { get; }

    /// <summary>Gets the root element's namespace URI, or an empty string.</summary>
    public string RootNamespace => Data.Root.IsNull ? string.Empty : Data.Root.NamespaceUri;

    internal static CustomXmlPart? Read(Document document, Relationship rel)
    {
        if (rel.TargetMode != RelationshipTargetMode.Internal
            || !PartUri.TryResolve(new PartUri(document.MainPartName), rel.Target, out PartUri uri)
            || document.Package.FindPart(uri.Value) is not { } part)
        {
            return null;
        }

        MarkupDocument? data = document.Parts.Load(part.Name, required: false);
        if (data is null)
        {
            return null;
        }

        string? itemId = null;
        var schemas = new List<string>();
        foreach (Relationship r in document.Package.GetRelationships(part.Name))
        {
            if (r.TargetMode != RelationshipTargetMode.Internal || !RelationshipTypes.Is(r.Type, "customXmlProps")
                || !PartUri.TryResolve(new PartUri(part.Name), r.Target, out PartUri propsUri))
            {
                continue;
            }

            MarkupDocument? props = document.Parts.Load(propsUri.Value, required: false);
            if (props is null || props.Root.IsNull)
            {
                continue;
            }

            // ECMA-376 Part 1 §22.5 (custom XML data properties, datastoreItem): itemID names the store item; schemaRefs lists its schemas.
            itemId = props.Root.GetAttribute(DataStoreNamespace, "itemID");
            foreach (MarkupNode refs in props.Root.ChildElements)
            {
                foreach (MarkupNode schemaRef in refs.ChildElements)
                {
                    if (schemaRef.GetAttribute(DataStoreNamespace, "uri") is { } u)
                    {
                        schemas.Add(u);
                    }
                }
            }

            break;
        }

        return new CustomXmlPart(part.Name, data, itemId, schemas);
    }

    internal const string DataStoreNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/customXml";
}

/// <summary>How a binding's path evaluated.</summary>
public enum BindingStatus
{
    /// <summary>The path matched a node and <see cref="BindingValue.Value"/> holds its text.</summary>
    Resolved = 0,

    /// <summary>The path is well-formed but matches nothing in the store.</summary>
    NoMatch = 1,

    /// <summary>No custom XML part has the binding's store item id.</summary>
    StoreNotFound = 2,

    /// <summary>The path uses XPath beyond the supported subset (see <see cref="BindingPath"/>).</summary>
    UnsupportedPath = 3,

    /// <summary>The binding has no path, or the path is malformed.</summary>
    Invalid = 4,
}

/// <summary>The result of evaluating a content control's data binding.</summary>
/// <param name="Status">How the evaluation ended.</param>
/// <param name="Value">The matched node's text (the element's text content, or the attribute's value); null unless <paramref name="Status"/> is <see cref="BindingStatus.Resolved"/>.</param>
public readonly record struct BindingValue(BindingStatus Status, string? Value);

/// <summary>A content control's <c>w:dataBinding</c>: where its value lives in a custom XML part.</summary>
public sealed class ContentControlBinding
{
    /// <summary>Creates a binding description, for evaluating a path without a control.</summary>
    /// <param name="xpath">The path.</param>
    /// <param name="prefixMappings">Raw prefix mappings, or null.</param>
    /// <param name="storeItemId">The store item id, or null.</param>
    public ContentControlBinding(string? xpath, string? prefixMappings, string? storeItemId)
    {
        XPath = xpath;
        PrefixMappings = prefixMappings;
        StoreItemId = storeItemId;
    }

    /// <summary>Gets the path (<c>w:xpath</c>) of the bound node; null when absent.</summary>
    public string? XPath { get; }

    /// <summary>Gets the raw prefix mappings (<c>w:prefixMappings</c>), for example <c>xmlns:ns0='urn:x'</c>; null when absent.</summary>
    public string? PrefixMappings { get; }

    /// <summary>Gets the id of the custom XML part holding the data (<c>w:storeItemID</c>); null when absent.</summary>
    public string? StoreItemId { get; }

    /// <summary>Parses <see cref="PrefixMappings"/> into prefix → namespace URI. Malformed pieces are skipped.</summary>
    /// <returns>The mappings; empty when none.</returns>
    public IReadOnlyDictionary<string, string> GetPrefixMap() => BindingPath.ParsePrefixMappings(PrefixMappings);

    /// <summary>Evaluates the binding against the document's custom XML parts. Nothing is changed and nothing is executed.</summary>
    /// <param name="document">The document that holds the control.</param>
    /// <returns>The current value of the bound node, or why there is none.</returns>
    public BindingValue Evaluate(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrWhiteSpace(XPath))
        {
            return new BindingValue(BindingStatus.Invalid, null);
        }

        CustomXmlPart? store = FindStore(document);
        if (store is null)
        {
            return new BindingValue(BindingStatus.StoreNotFound, null);
        }

        return BindingPath.Evaluate(store.Data, XPath, GetPrefixMap());
    }

    internal CustomXmlPart? FindStore(Document document)
    {
        foreach (CustomXmlPart p in document.CustomXmlParts)
        {
            if (StoreItemId is not null && string.Equals(p.ItemId, StoreItemId, StringComparison.OrdinalIgnoreCase))
            {
                return p;
            }
        }

        return string.Equals(StoreItemId, Document.CorePropertiesStoreId, StringComparison.OrdinalIgnoreCase) ? document.CorePropertiesStore : null;
    }
}

/// <summary>
/// The XPath subset used in <c>w:dataBinding</c>, evaluated over a custom XML part. It is not general XPath. Supported: absolute
/// paths of child steps, <c>//</c>, <c>*</c> name tests, <c>self</c>, <c>parent</c> and <c>attribute</c> steps (<c>.</c>, <c>..</c>,
/// <c>@a</c>), a final <c>text()</c>; predicates with positions (<c>[2]</c>, <c>[last()]</c>, <c>[position()=2]</c>), comparisons,
/// <c>and</c>/<c>or</c>/<c>not()</c>, attribute and child-element tests (<c>[@id='x']</c>, <c>[name='x']</c>), <c>name()</c>,
/// <c>local-name()</c>, <c>namespace-uri()</c>, <c>contains()</c>, <c>starts-with()</c>. Unprefixed names match elements in no
/// namespace, as in XPath 1.0. Anything else (other functions, unions, arithmetic, other axes, relative paths) is reported as
/// <see cref="BindingStatus.UnsupportedPath"/>; a malformed path or a prefix the mappings do not declare is
/// <see cref="BindingStatus.Invalid"/>.
/// </summary>
public static class BindingPath
{
    /// <summary>Evaluates <paramref name="xpath"/> over <paramref name="data"/>.</summary>
    /// <param name="data">The custom XML part's tree.</param>
    /// <param name="xpath">The path.</param>
    /// <param name="prefixes">Prefix → namespace URI for the names in the path.</param>
    /// <returns>The value of the first node the path selects (an element's text content, an attribute's value, or an element's own text nodes for <c>text()</c>), or the reason there is none.</returns>
    public static BindingValue Evaluate(MarkupDocument data, string xpath, IReadOnlyDictionary<string, string> prefixes)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(xpath);
        ArgumentNullException.ThrowIfNull(prefixes);
        if (!XPathSubset.TryParse(xpath, out XPathSubset? expression, out BindingStatus failure))
        {
            return new BindingValue(failure, null);
        }

        if (data.Root.IsNull)
        {
            return new BindingValue(BindingStatus.NoMatch, null);
        }

        if (!expression!.Select(data, prefixes, out List<XPathSubset.Node> nodes))
        {
            return new BindingValue(BindingStatus.Invalid, null);
        }

        return nodes.Count == 0 ? new BindingValue(BindingStatus.NoMatch, null) : new BindingValue(BindingStatus.Resolved, nodes[0].StringValue);
    }

    internal static Dictionary<string, string> ParsePrefixMappings(string? text)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(text))
        {
            return map;
        }

        // ECMA-376 Part 1 §17.5.2 (dataBinding, prefixMappings): space-separated xmlns:prefix='uri' declarations.
        int i = 0;
        while (i < text.Length)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i]))
            {
                i++;
            }

            int eq = text.IndexOf('=', i);
            if (eq < 0)
            {
                break;
            }

            string name = text[i..eq].Trim();
            int q = eq + 1;
            while (q < text.Length && char.IsWhiteSpace(text[q]))
            {
                q++;
            }

            if (q >= text.Length || (text[q] != '\'' && text[q] != '"'))
            {
                break;
            }

            int end = text.IndexOf(text[q], q + 1);
            if (end < 0)
            {
                break;
            }

            string uri = text[(q + 1)..end];
            if (name.StartsWith("xmlns:", StringComparison.Ordinal))
            {
                map[name[6..]] = uri;
            }

            i = end + 1;
        }

        return map;
    }
}
