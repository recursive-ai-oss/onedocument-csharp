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

        CustomXmlPart? store = null;
        foreach (CustomXmlPart p in document.CustomXmlParts)
        {
            if (StoreItemId is not null && string.Equals(p.ItemId, StoreItemId, StringComparison.OrdinalIgnoreCase))
            {
                store = p;
                break;
            }
        }

        if (store is null && string.Equals(StoreItemId, Document.CorePropertiesStoreId, StringComparison.OrdinalIgnoreCase))
        {
            store = document.CorePropertiesStore;
        }

        if (store is null)
        {
            return new BindingValue(BindingStatus.StoreNotFound, null);
        }

        return BindingPath.Evaluate(store.Data, XPath, GetPrefixMap());
    }
}

/// <summary>
/// The XPath subset that Word writes into <c>w:dataBinding</c>, evaluated over a custom XML part. It is not general XPath.
/// Supported: an absolute path of child steps (<c>/a:root/a:item</c>, <c>*</c> as a name test), 1-based position predicates
/// (<c>[2]</c>), attribute-equality predicates (<c>[@id='x']</c>), a final attribute step (<c>/@name</c>) or <c>text()</c>.
/// Unprefixed names match elements in no namespace, as in XPath 1.0. Anything else (<c>//</c>, axes, functions, unions, operators)
/// is reported as <see cref="BindingStatus.UnsupportedPath"/>.
/// </summary>
public static class BindingPath
{
    /// <summary>Evaluates <paramref name="xpath"/> over <paramref name="data"/>.</summary>
    /// <param name="data">The custom XML part's tree.</param>
    /// <param name="xpath">The path.</param>
    /// <param name="prefixes">Prefix → namespace URI for the names in the path.</param>
    /// <returns>The value of the first node the path selects, or the reason there is none.</returns>
    public static BindingValue Evaluate(MarkupDocument data, string xpath, IReadOnlyDictionary<string, string> prefixes)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(xpath);
        ArgumentNullException.ThrowIfNull(prefixes);
        List<Step>? steps = Parse(xpath, out BindingStatus failure);
        if (steps is null)
        {
            return new BindingValue(failure, null);
        }

        MarkupNode root = data.Root;
        if (root.IsNull)
        {
            return new BindingValue(BindingStatus.NoMatch, null);
        }

        // The context is the document node, so the first step tests the root element.
        var candidates = new List<MarkupNode> { data.DocumentNode };
        for (int i = 0; i < steps.Count; i++)
        {
            Step s = steps[i];
            if (s.Kind == StepKind.Attribute || s.Kind == StepKind.Text)
            {
                if (i != steps.Count - 1 || candidates.Count == 0)
                {
                    return new BindingValue(i != steps.Count - 1 ? BindingStatus.UnsupportedPath : BindingStatus.NoMatch, null);
                }

                return s.Kind == StepKind.Text ? Text(candidates[0]) : Attribute(candidates[0], s, prefixes);
            }

            var next = new List<MarkupNode>();
            foreach (MarkupNode c in candidates)
            {
                int position = 0;
                foreach (MarkupNode child in c.ChildElements)
                {
                    if (!NameMatches(child, s, prefixes, out bool bad))
                    {
                        if (bad)
                        {
                            return new BindingValue(BindingStatus.Invalid, null);
                        }

                        continue;
                    }

                    if (s.AttributeName is not null && !AttributeEquals(child, s, prefixes))
                    {
                        continue;
                    }

                    position++;
                    if (s.Index == 0 || s.Index == position)
                    {
                        next.Add(child);
                    }
                }
            }

            if (next.Count == 0)
            {
                return new BindingValue(BindingStatus.NoMatch, null);
            }

            candidates = next;
        }

        return new BindingValue(BindingStatus.Resolved, candidates[0].InnerText);
    }

    private static BindingValue Text(MarkupNode element)
    {
        var sb = new System.Text.StringBuilder();
        foreach (MarkupNode n in element.Children)
        {
            if (n.Kind == MarkupNodeKind.Text || n.Kind == MarkupNodeKind.CData)
            {
                sb.Append(n.InnerText);
            }
        }

        return new BindingValue(BindingStatus.Resolved, sb.ToString());
    }

    private static BindingValue Attribute(MarkupNode element, Step s, IReadOnlyDictionary<string, string> prefixes)
    {
        if (!Resolve(s.Prefix, prefixes, out string ns))
        {
            return new BindingValue(BindingStatus.Invalid, null);
        }

        string? v = ns.Length == 0 ? element.GetAttribute(s.Name) : element.GetAttribute(ns, s.Name);
        return v is null ? new BindingValue(BindingStatus.NoMatch, null) : new BindingValue(BindingStatus.Resolved, v);
    }

    private static bool Resolve(string? prefix, IReadOnlyDictionary<string, string> prefixes, out string ns)
    {
        ns = string.Empty;
        return string.IsNullOrEmpty(prefix) || prefixes.TryGetValue(prefix, out ns!);
    }

    private static bool NameMatches(MarkupNode e, Step s, IReadOnlyDictionary<string, string> prefixes, out bool unboundPrefix)
    {
        unboundPrefix = false;
        if (s.Name == "*")
        {
            return true;
        }

        if (!Resolve(s.Prefix, prefixes, out string ns))
        {
            unboundPrefix = true;
            return false;
        }

        return e.LocalNameString == s.Name && string.Equals(e.NamespaceUri, ns, StringComparison.Ordinal);
    }

    private static bool AttributeEquals(MarkupNode e, Step s, IReadOnlyDictionary<string, string> prefixes)
    {
        if (!Resolve(s.AttributePrefix, prefixes, out string ns))
        {
            return false;
        }

        string? v = ns.Length == 0 ? e.GetAttribute(s.AttributeName!) : e.GetAttribute(ns, s.AttributeName!);
        return v == s.AttributeValue;
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

    private enum StepKind
    {
        Element,
        Attribute,
        Text,
    }

    private sealed class Step
    {
        public StepKind Kind;
        public string? Prefix;
        public string Name = string.Empty;
        public int Index;
        public string? AttributePrefix;
        public string? AttributeName;
        public string? AttributeValue;
    }

    private static List<Step>? Parse(string path, out BindingStatus failure)
    {
        failure = BindingStatus.Invalid;
        string p = path.Trim();
        if (p.Length == 0 || p[0] != '/')
        {
            failure = p.Length == 0 ? BindingStatus.Invalid : BindingStatus.UnsupportedPath;
            return null;
        }

        var steps = new List<Step>();
        int i = 0;
        while (i < p.Length)
        {
            if (p[i] != '/')
            {
                return null;
            }

            i++;
            if (i < p.Length && p[i] == '/')
            {
                failure = BindingStatus.UnsupportedPath;
                return null;
            }

            int start = i;
            int bracket = 0;
            char quote = '\0';
            while (i < p.Length && (p[i] != '/' || bracket > 0 || quote != '\0'))
            {
                char c = p[i];
                if (quote != '\0')
                {
                    if (c == quote)
                    {
                        quote = '\0';
                    }
                }
                else if (c == '\'' || c == '"')
                {
                    quote = c;
                }
                else if (c == '[')
                {
                    bracket++;
                }
                else if (c == ']')
                {
                    bracket--;
                }

                i++;
            }

            if (i == start || bracket != 0 || quote != '\0')
            {
                return null;
            }

            Step? step = ParseStep(p[start..i]);
            if (step is null)
            {
                failure = BindingStatus.UnsupportedPath;
                return null;
            }

            steps.Add(step);
        }

        return steps;
    }

    private static Step? ParseStep(string text)
    {
        if (text == "text()")
        {
            return new Step { Kind = StepKind.Text };
        }

        var step = new Step();
        string name = text;
        int br = text.IndexOf('[', StringComparison.Ordinal);
        if (br >= 0)
        {
            name = text[..br];
            string rest = text[br..];
            if (rest.Length < 3 || rest[^1] != ']' || rest.IndexOf(']', StringComparison.Ordinal) != rest.Length - 1)
            {
                return null;
            }

            string pred = rest[1..^1].Trim();
            if (int.TryParse(pred, NumberStyles.None, CultureInfo.InvariantCulture, out int index) && index >= 1)
            {
                step.Index = index;
            }
            else if (pred.StartsWith('@'))
            {
                int eq = pred.IndexOf('=', StringComparison.Ordinal);
                if (eq < 0)
                {
                    return null;
                }

                string attr = pred[1..eq].Trim();
                string value = pred[(eq + 1)..].Trim();
                if (value.Length < 2 || (value[0] != '\'' && value[0] != '"') || value[^1] != value[0] || !IsName(attr, out string? ap, out string al))
                {
                    return null;
                }

                step.AttributePrefix = ap;
                step.AttributeName = al;
                step.AttributeValue = value[1..^1];
            }
            else
            {
                return null;
            }
        }

        if (name.StartsWith('@'))
        {
            if (br >= 0 || !IsName(name[1..], out string? attrPrefix, out string attrLocal))
            {
                return null;
            }

            step.Kind = StepKind.Attribute;
            step.Prefix = attrPrefix;
            step.Name = attrLocal;
            return step;
        }

        if (name == "*")
        {
            step.Name = "*";
            return step;
        }

        if (!IsName(name, out string? prefix, out string local))
        {
            return null;
        }

        step.Prefix = prefix;
        step.Name = local;
        return step;
    }

    private static bool IsName(string text, out string? prefix, out string local)
    {
        prefix = null;
        local = text;
        int colon = text.IndexOf(':', StringComparison.Ordinal);
        if (colon >= 0)
        {
            prefix = text[..colon];
            local = text[(colon + 1)..];
        }

        return IsNcName(local) && (prefix is null || IsNcName(prefix));
    }

    private static bool IsNcName(string s)
    {
        if (s.Length == 0 || !(char.IsLetter(s[0]) || s[0] == '_'))
        {
            return false;
        }

        foreach (char c in s)
        {
            if (!(char.IsLetterOrDigit(c) || c is '_' or '-' or '.'))
            {
                return false;
            }
        }

        return true;
    }
}
