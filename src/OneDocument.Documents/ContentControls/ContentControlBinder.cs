using System.Globalization;
using System.Text;
using System.Linq;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Documents.MailMerge;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Schema;
using RecursiveAI.OneDocument.Templating;

namespace RecursiveAI.OneDocument.Documents.ContentControls;

/// <summary>What a content-control binding pass did.</summary>
public sealed class ContentBindingReport
{
    internal ContentBindingReport(int updated, int sectionItems, IReadOnlyList<Defect> warnings)
    {
        Updated = updated;
        SectionItems = sectionItems;
        Warnings = warnings;
    }

    /// <summary>Gets the number of controls whose content, or whose bound custom XML node, was written.</summary>
    public int Updated { get; }

    /// <summary>Gets the number of repeating-section items in the document after the pass, over all repeating sections.</summary>
    public int SectionItems { get; }

    /// <summary>
    /// Gets what could not be done, each with a <c>binding.</c> code: <c>binding.store-not-found</c>, <c>binding.unsupported-path</c>,
    /// <c>binding.no-data</c> (the data has no value for the node; the control keeps its content), <c>binding.unsupported-control</c>,
    /// <c>binding.value-not-in-list</c> and <c>binding.no-template-item</c> (a repeating section with no item to copy).
    /// </summary>
    public IReadOnlyList<Defect> Warnings { get; }
}

internal sealed class ContentBinder
{
    private readonly Document _document;
    private readonly ITemplateData? _data;
    private readonly CancellationToken _cancellation;
    private readonly List<Defect> _warnings = [];
    private int _updated;
    private long _nextId;

    public ContentBinder(Document document, ITemplateData? data, CancellationToken cancellation)
    {
        _document = document;
        _data = data;
        _cancellation = cancellation;
    }

    public ContentBindingReport Run(bool pushControls)
    {
        List<ContentControl> all = _document.ContentControls().ToList();
        _nextId = all.Select(c => c.Id ?? 0).DefaultIfEmpty(0).Max() + 1;

        // Repeating sections first, outermost first: copies bring new controls whose paths the later passes read.
        var done = new HashSet<MarkupNode>();
        while (!pushControls)
        {
            ContentControl? next = _document.ContentControls().FirstOrDefault(c => c.Type == ContentControlType.RepeatingSection && !done.Contains(c.Markup.Node));
            if (next is null)
            {
                break;
            }

            _cancellation.ThrowIfCancellationRequested();
            done.Add(next.Markup.Node);
            ProcessSection(next);
        }

        int items = 0;
        foreach (ContentControl c in _document.ContentControls().ToList())
        {
            _cancellation.ThrowIfCancellationRequested();
            if (c.Type == ContentControlType.RepeatingSectionItem)
            {
                items++;
                continue;
            }

            if (c.Type == ContentControlType.RepeatingSection || c.Binding is null)
            {
                continue;
            }

            if (pushControls)
            {
                Push(c);
            }
            else
            {
                Pull(c);
            }
        }

        return new ContentBindingReport(_updated, items, _warnings);
    }

    private void Warn(string code, string message, ContentControl c, DefectSeverity severity = DefectSeverity.Repaired) =>
        _warnings.Add(new Defect(DefectKind.Reference, code, severity, message, DefectLocation.InPart(c.PartName ?? _document.MainPartName, c.Markup.Node.SourceStart)));

    // ---- data in, store, control

    private void Pull(ContentControl c)
    {
        ContentControlBinding binding = c.Binding!;
        CustomXmlPart? store = binding.FindStore(_document);
        if (store is null)
        {
            Warn("binding.store-not-found", "No custom XML part has the store item id of this binding.", c);
            return;
        }

        IReadOnlyDictionary<string, string> prefixes = binding.GetPrefixMap();
        if (string.IsNullOrWhiteSpace(binding.XPath))
        {
            Warn("binding.unsupported-path", "The binding has no path.", c);
            return;
        }

        if (_data is not null)
        {
            if (!SimplePath.TryParse(binding.XPath, out SimplePath? path))
            {
                Warn("binding.unsupported-path", "Data can only be bound through paths of plain child steps with positions (as Word writes them); this path is read from the part only.", c);
            }
            else
            {
                ITemplateData? found = Lookup(_data, path!);
                if (found is { Kind: TemplateDataKind.Scalar } scalar)
                {
                    string text = ValueFor(c, scalar, store, binding.XPath, prefixes);
                    if (Write(store, binding.XPath, prefixes, text, c))
                    {
                        _updated++;
                    }
                }
                else if (found is null)
                {
                    Warn("binding.no-data", "The data has no value for " + binding.XPath + "; the control keeps its content.", c, DefectSeverity.Dropped);
                }
            }
        }

        BindingValue v = BindingPath.Evaluate(store.Data, binding.XPath, prefixes);
        if (v.Status == BindingStatus.Resolved)
        {
            if (ApplyToControl(c, v.Value ?? string.Empty))
            {
                _updated++;
            }
        }
        else if (v.Status is BindingStatus.UnsupportedPath or BindingStatus.Invalid)
        {
            Warn("binding.unsupported-path", "The path " + binding.XPath + " is outside the supported XPath subset.", c);
        }
    }

    private void Push(ContentControl c)
    {
        ContentControlBinding binding = c.Binding!;
        CustomXmlPart? store = binding.FindStore(_document);
        if (store is null)
        {
            Warn("binding.store-not-found", "No custom XML part has the store item id of this binding.", c);
            return;
        }

        if (string.IsNullOrWhiteSpace(binding.XPath) || c.IsShowingPlaceholder)
        {
            return;
        }

        string? value = ControlValue(c, store, binding);
        if (value is null)
        {
            Warn("binding.unsupported-control", "A " + c.Type + " control's content is not written to its data store.", c);
            return;
        }

        if (Write(store, binding.XPath, binding.GetPrefixMap(), value, c))
        {
            _updated++;
        }
    }

    private static string? ControlValue(ContentControl c, CustomXmlPart store, ContentControlBinding binding)
    {
        switch (c.Type)
        {
            case ContentControlType.PlainText or ContentControlType.RichText or ContentControlType.Citation:
                return c.Text;
            case ContentControlType.ComboBox:
                return c.SelectedIndex is { } ci && ci < c.ListItems.Count ? c.ListItems[ci].Value : c.Text;
            case ContentControlType.DropDownList:
                return c.SelectedIndex is { } di && di < c.ListItems.Count ? c.ListItems[di].Value : c.Text;
            case ContentControlType.Checkbox:
                return BooleanText(c.IsChecked == true, CurrentValue(store, binding));
            case ContentControlType.Date:
                return c.Date is { } d ? d.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) : c.Text;
            default:
                return null;
        }
    }

    private static string? CurrentValue(CustomXmlPart store, ContentControlBinding binding)
    {
        BindingValue v = BindingPath.Evaluate(store.Data, binding.XPath ?? string.Empty, binding.GetPrefixMap());
        return v.Status == BindingStatus.Resolved ? v.Value : null;
    }

    private static string BooleanText(bool value, string? existing) =>
        existing is "true" or "false" ? (value ? "true" : "false") : (value ? "1" : "0");

    // How a scalar from the data is stored for a control of this type.
    private static string ValueFor(ContentControl c, ITemplateData scalar, CustomXmlPart store, string xpath, IReadOnlyDictionary<string, string> prefixes)
    {
        string text = MergePass.ScalarText(scalar);
        switch (c.Type)
        {
            case ContentControlType.Checkbox:
                {
                    bool value = scalar.Raw switch
                    {
                        bool b => b,
                        string s => IsTrue(s),
                        _ => scalar.Value.Kind == RecursiveAI.OneDocument.Calc.ValueKind.Number && scalar.Value.Number != 0,
                    };
                    BindingValue current = BindingPath.Evaluate(store.Data, xpath, prefixes);
                    return BooleanText(value, current.Status == BindingStatus.Resolved ? current.Value : null);
                }

            case ContentControlType.Date:
                {
                    DateTime? d = scalar.Raw switch
                    {
                        DateTime dt => dt,
                        DateTimeOffset dto => dto.UtcDateTime,
                        DateOnly dateOnly => dateOnly.ToDateTime(TimeOnly.MinValue),
                        _ => DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime parsed) ? parsed : null,
                    };
                    return d is { } v ? v.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) : text;
                }

            default:
                return text;
        }
    }

    private static bool IsTrue(string s) => s.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on" or "checked";

    private bool ApplyToControl(ContentControl c, string value)
    {
        try
        {
            switch (c.Type)
            {
                case ContentControlType.PlainText or ContentControlType.RichText or ContentControlType.Citation:
                    c.SetText(value);
                    return true;
                case ContentControlType.Checkbox:
                    c.SetChecked(IsTrue(value));
                    return true;
                case ContentControlType.Date:
                    return ApplyDate(c, value);
                case ContentControlType.DropDownList or ContentControlType.ComboBox:
                    {
                        IReadOnlyList<ContentControlListItem> items = c.ListItems;
                        for (int i = 0; i < items.Count; i++)
                        {
                            if (items[i].Value == value || items[i].DisplayText == value)
                            {
                                c.SelectItem(i);
                                return true;
                            }
                        }

                        if (c.Type == ContentControlType.ComboBox)
                        {
                            c.SetText(value);
                            return true;
                        }

                        Warn("binding.value-not-in-list", "'" + value + "' is not an item of the drop-down list; the control keeps its content.", c);
                        return false;
                    }

                default:
                    Warn("binding.unsupported-control", "A " + c.Type + " control is not filled from its data store.", c);
                    return false;
            }
        }
        catch (InvalidOperationException)
        {
            Warn("binding.unsupported-control", "A " + c.Level + "-level control has no content to set.", c);
            return false;
        }
    }

    // ECMA-376 Part 1 §17.5.2.7 (date): fullDate holds the date as xsd:dateTime; the content shows it in the control's dateFormat.
    private static bool ApplyDate(ContentControl c, string value)
    {
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset d))
        {
            c.SetText(value);
            return true;
        }

        string format = c.DateFormat ?? "M/d/yyyy";
        c.SetText(WordPicture.FormatDate(d.DateTime, format));
        if (c.PropertiesElement is { } pr && Wml.Child(pr, "date"u8) is { } dateElement)
        {
            MarkupNode node = dateElement.Node;
            node.SetAttribute(node.NamespaceUri, "fullDate", d.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture), "w");
        }

        return true;
    }

    // ---- store writes

    private bool Write(CustomXmlPart store, string xpath, IReadOnlyDictionary<string, string> prefixes, string value, ContentControl at)
    {
        if (!XPathSubset.TryParse(xpath, out XPathSubset? expression, out _) || !expression!.Select(store.Data, prefixes, out List<XPathSubset.Node> nodes))
        {
            Warn("binding.unsupported-path", "The path " + xpath + " is outside the supported XPath subset.", at);
            return false;
        }

        if (nodes.Count == 0)
        {
            if (!SimplePath.TryParse(xpath, out SimplePath? path) || !Create(store.Data, path!, prefixes, out nodes))
            {
                Warn("binding.unsupported-path", "The node " + xpath + " does not exist and cannot be created from this path.", at);
                return false;
            }
        }

        XPathSubset.Node n = nodes[0];
        if (n.IsAttribute)
        {
            if (n.AttributeNamespace is { Length: > 0 } ns)
            {
                n.Element.SetAttribute(ns, n.AttributeName!, value);
            }
            else
            {
                n.Element.SetAttribute(n.AttributeName!, value);
            }

            return true;
        }

        foreach (MarkupNode child in n.Element.ChildElements)
        {
            _ = child;
            Warn("binding.unsupported-path", "The bound element has child elements, so its text is not replaced.", at);
            return false;
        }

        n.Element.SetText(value);
        return true;
    }

    // Creates the missing elements of a plain path. Each [n] step needs n siblings of that name: missing ones are appended.
    private static bool Create(MarkupDocument data, SimplePath path, IReadOnlyDictionary<string, string> prefixes, out List<XPathSubset.Node> nodes)
    {
        nodes = [];
        MarkupNode current = data.DocumentNode;
        for (int i = 0; i < path.Steps.Count; i++)
        {
            SimplePath.PathStep step = path.Steps[i];
            if (step.Kind == SimplePath.StepKind.Text)
            {
                nodes.Add(new XPathSubset.Node(current, null, null, null, true));
                return true;
            }

            string ns = string.Empty;
            if (!string.IsNullOrEmpty(step.Prefix) && !prefixes.TryGetValue(step.Prefix, out ns!))
            {
                return false;
            }

            if (step.Kind == SimplePath.StepKind.Attribute)
            {
                if (current.Kind == MarkupNodeKind.Document)
                {
                    return false;
                }

                if (ns.Length == 0)
                {
                    current.SetAttribute(step.Name, string.Empty);
                }
                else
                {
                    current.SetAttribute(ns, step.Name, string.Empty);
                }

                nodes.Add(new XPathSubset.Node(current, step.Name, ns.Length == 0 ? null : ns, string.Empty, false));
                return true;
            }

            int index = step.Index ?? 1;
            int seen = 0;
            MarkupNode found = default;
            foreach (MarkupNode child in current.ChildElements)
            {
                if (child.LocalNameString == step.Name && child.NamespaceUri == ns && ++seen == index)
                {
                    found = child;
                    break;
                }
            }

            if (found.IsNull)
            {
                if (current.Kind == MarkupNodeKind.Document)
                {
                    // The root element is never invented.
                    return false;
                }

                while (seen < index)
                {
                    string qname = string.IsNullOrEmpty(step.Prefix) ? step.Name : step.Prefix + ":" + step.Name;
                    string declaration = string.Empty;
                    if (!string.IsNullOrEmpty(step.Prefix) && !(current.TryResolvePrefix(step.Prefix, out string? bound) && bound == ns))
                    {
                        declaration = " xmlns:" + step.Prefix + "=\"" + HubXml.Escape(ns) + "\"";
                    }

                    current.AppendXml(Encoding.UTF8.GetBytes("<" + qname + declaration + "/>"));
                    seen++;
                }

                found = current.LastChild;
                while (!found.IsNull && found.Kind != MarkupNodeKind.Element)
                {
                    found = found.PreviousSibling;
                }
            }

            current = found;
        }

        nodes.Add(new XPathSubset.Node(current, null, null, null, false));
        return true;
    }

    // ---- data lookup by a plain path

    // The root step names the data's root and is skipped. A step finds the member of that name; a list member takes [n] (first when
    // absent). A final step with no position that names a list returns the list itself, which repeating sections count.
    private static ITemplateData? Lookup(ITemplateData root, SimplePath path)
    {
        ITemplateData current = root;
        for (int i = 1; i < path.Steps.Count; i++)
        {
            SimplePath.PathStep step = path.Steps[i];
            if (step.Kind == SimplePath.StepKind.Text)
            {
                break;
            }

            if (current.Kind != TemplateDataKind.Record || !current.TryGetMember(step.Name, out ITemplateData? member) || member is null)
            {
                return null;
            }

            if (member.Kind == TemplateDataKind.Sequence)
            {
                if (step.Index is null && i == path.Steps.Count - 1)
                {
                    return member;
                }

                // A list in the data may stand for the repeated element's container: JSON {"lines": [..]} for <lines><line/>..</lines>.
                // It does when the step is not a later copy of the container itself and the items have no member of the next step's name.
                if (i < path.Steps.Count - 1 && path.Steps[i + 1].Kind == SimplePath.StepKind.Element && (step.Index ?? 1) == 1
                    && !(member.Items.Count > 0 && member.Items[0].Kind == TemplateDataKind.Record && member.Items[0].TryGetMember(path.Steps[i + 1].Name, out _)))
                {
                    SimplePath.PathStep item = path.Steps[i + 1];
                    if (item.Index is null && i + 1 == path.Steps.Count - 1)
                    {
                        return member;
                    }

                    int position = item.Index ?? 1;
                    if (position > member.Items.Count)
                    {
                        return null;
                    }

                    current = member.Items[position - 1];
                    i++;
                    continue;
                }

                int index = step.Index ?? 1;
                if (index > member.Items.Count)
                {
                    return null;
                }

                member = member.Items[index - 1];
            }
            else if ((step.Index ?? 1) != 1)
            {
                return null;
            }

            current = member;
        }

        return current;
    }

    // ---- repeating sections

    private void ProcessSection(ContentControl section)
    {
        ContentControlBinding? binding = section.Binding;
        if (binding is null || string.IsNullOrWhiteSpace(binding.XPath))
        {
            return;
        }

        CustomXmlPart? store = binding.FindStore(_document);
        if (store is null)
        {
            Warn("binding.store-not-found", "No custom XML part has the store item id of this repeating section.", section);
            return;
        }

        if (!SimplePath.TryParse(binding.XPath, out SimplePath? path) || path!.Steps.Count < 2 || path.Steps[^1].Kind != SimplePath.StepKind.Element)
        {
            Warn("binding.unsupported-path", "A repeating section needs a path of plain child steps naming the repeated element.", section);
            return;
        }

        IReadOnlyDictionary<string, string> prefixes = binding.GetPrefixMap();
        string baseXPath = path.WithoutLastPosition();
        int count;
        if (_data is not null && Lookup(_data, path) is { } listed)
        {
            count = listed.Kind == TemplateDataKind.Sequence ? listed.Items.Count : (listed.Kind == TemplateDataKind.Null ? 0 : 1);
            if (!SetNodeCount(store, baseXPath, prefixes, count, section))
            {
                return;
            }
        }
        else
        {
            if (_data is not null)
            {
                Warn("binding.no-data", "The data has no list for " + baseXPath + "; the section keeps the items the part has.", section, DefectSeverity.Dropped);
            }

            if (!XPathSubset.TryParse(baseXPath, out XPathSubset? e, out _) || !e!.Select(store.Data, prefixes, out List<XPathSubset.Node> existing))
            {
                Warn("binding.unsupported-path", "The path " + baseXPath + " cannot be read.", section);
                return;
            }

            count = existing.Count;
        }

        List<ContentControl> items = ItemsOf(section);
        if (items.Count == 0)
        {
            Warn("binding.no-template-item", "The repeating section has no item to copy.", section);
            return;
        }

        while (items.Count < count)
        {
            MarkupNode last = items[^1].Markup.Node;
            last.InsertXmlAfter(Encoding.UTF8.GetBytes(last.OuterXml));
            MarkupNode copy = last.NextSibling;
            while (!copy.IsNull && copy.Kind != MarkupNodeKind.Element)
            {
                copy = copy.NextSibling;
            }

            RenumberIds(copy);
            items = ItemsOf(section);
        }

        while (items.Count > count)
        {
            items[^1].Markup.Node.Remove();
            items.RemoveAt(items.Count - 1);
        }

        for (int k = 0; k < items.Count; k++)
        {
            foreach (MarkupNode sdt in DocumentOrder.Elements(items[k].Markup.Node, stopAt: null))
            {
                if (sdt.LocalNameString != "sdt")
                {
                    continue;
                }

                var inner = new ContentControl(_document, new MarkupRef(sdt), section.PartName);
                if (inner.PropertiesElement is { } pr && Wml.Child(pr, "dataBinding"u8) is { } b && Wml.Attr(b, "xpath") is { } xp)
                {
                    b.Node.SetAttribute(b.Node.NamespaceUri, "xpath", Reindex(xp, baseXPath, k + 1), "w");
                }
            }
        }
    }

    private static List<ContentControl> ItemsOf(ContentControl section)
    {
        var items = new List<ContentControl>();
        if (section.ContentElement is not { } content)
        {
            return items;
        }

        foreach (MarkupNode child in content.Node.ChildElements)
        {
            if (child.LocalNameString == "sdt")
            {
                var c = new ContentControl(section.Owner, new MarkupRef(child), section.PartName);
                if (c.Type == ContentControlType.RepeatingSectionItem)
                {
                    items.Add(c);
                }
            }
        }

        return items;
    }

    // A copy of an item gets fresh control ids so every w:id stays unique in the part.
    private void RenumberIds(MarkupNode copy)
    {
        foreach (MarkupNode sdt in new[] { copy }.Concat(DocumentOrder.Elements(copy, stopAt: null)).ToList())
        {
            if (sdt.LocalNameString != "sdt")
            {
                continue;
            }

            MarkupNode pr = HubXml.FirstChildElement(sdt, "sdtPr");
            if (pr.IsNull)
            {
                continue;
            }

            MarkupNode id = HubXml.FirstChildElement(pr, "id");
            if (!id.IsNull)
            {
                id.SetAttribute(id.NamespaceUri, "val", (_nextId++).ToString(CultureInfo.InvariantCulture), "w");
            }
        }
    }

    // The part holds exactly 'count' elements at the repeated path: missing ones are appended, extra ones removed.
    private bool SetNodeCount(CustomXmlPart store, string baseXPath, IReadOnlyDictionary<string, string> prefixes, int count, ContentControl at)
    {
        if (!XPathSubset.TryParse(baseXPath, out XPathSubset? expression, out _) || !expression!.Select(store.Data, prefixes, out List<XPathSubset.Node> existing))
        {
            Warn("binding.unsupported-path", "The path " + baseXPath + " cannot be read.", at);
            return false;
        }

        if (existing.Count < count)
        {
            string withIndex = baseXPath + "[" + count.ToString(CultureInfo.InvariantCulture) + "]";
            if (!SimplePath.TryParse(withIndex, out SimplePath? target) || !Create(store.Data, target!, prefixes, out _))
            {
                Warn("binding.unsupported-path", "The elements of " + baseXPath + " cannot be created.", at);
                return false;
            }
        }
        else
        {
            for (int i = existing.Count - 1; i >= count; i--)
            {
                existing[i].Element.Remove();
            }
        }

        return true;
    }

    // Re-indexes a bound path that runs through the repeated element: .../item[1]/name[1] becomes .../item[k]/name[1].
    internal static string Reindex(string xpath, string baseXPath, int k)
    {
        if (!xpath.StartsWith(baseXPath, StringComparison.Ordinal))
        {
            return xpath;
        }

        int at = baseXPath.Length;
        string rest = xpath[at..];
        if (rest.StartsWith('['))
        {
            int close = rest.IndexOf(']', StringComparison.Ordinal);
            if (close < 0)
            {
                return xpath;
            }

            rest = rest[(close + 1)..];
        }
        else if (rest.Length > 0 && rest[0] != '/')
        {
            // The path only shares a prefix with the base (item vs items).
            return xpath;
        }

        return baseXPath + "[" + k.ToString(CultureInfo.InvariantCulture) + "]" + rest;
    }
}

/// <summary>A path of plain child steps with optional positions, ending in an element, an attribute or <c>text()</c>: the form Word writes.</summary>
internal sealed class SimplePath
{
    public enum StepKind
    {
        Element,
        Attribute,
        Text,
    }

    public readonly record struct PathStep(StepKind Kind, string? Prefix, string Name, int? Index);

    private static readonly System.Buffers.SearchValues<char> Forbidden = System.Buffers.SearchValues.Create("()[]'\" =<>!|,");

    public List<PathStep> Steps { get; } = [];

    public static SimplePath Parse(string xpath) => TryParse(xpath, out SimplePath? p) ? p! : new SimplePath();

    public static bool TryParse(string xpath, out SimplePath? path)
    {
        path = null;
        if (!xpath.StartsWith('/') || xpath.StartsWith("//", StringComparison.Ordinal))
        {
            return false;
        }

        var result = new SimplePath();
        string[] parts = xpath[1..].Split('/');
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i].Trim();
            bool last = i == parts.Length - 1;
            if (part == "text()" && last)
            {
                result.Steps.Add(new PathStep(StepKind.Text, null, string.Empty, null));
                continue;
            }

            StepKind kind = StepKind.Element;
            if (part.StartsWith('@'))
            {
                if (!last)
                {
                    return false;
                }

                kind = StepKind.Attribute;
                part = part[1..];
            }

            int? index = null;
            int br = part.IndexOf('[', StringComparison.Ordinal);
            if (br >= 0)
            {
                if (kind == StepKind.Attribute || !part.EndsWith(']') || !int.TryParse(part[(br + 1)..^1], NumberStyles.None, CultureInfo.InvariantCulture, out int n) || n < 1)
                {
                    return false;
                }

                index = n;
                part = part[..br];
            }

            string? prefix = null;
            int colon = part.IndexOf(':', StringComparison.Ordinal);
            if (colon >= 0)
            {
                prefix = part[..colon];
                part = part[(colon + 1)..];
            }

            if (part.Length == 0 || part == "*" || part.AsSpan().IndexOfAny(Forbidden) >= 0 || (prefix is not null && prefix.Length == 0))
            {
                return false;
            }

            result.Steps.Add(new PathStep(kind, prefix, part, index));
        }

        path = result.Steps.Count > 0 ? result : null;
        return path is not null;
    }

    /// <summary>The path as text with the last step's position dropped.</summary>
    public string WithoutLastPosition()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < Steps.Count; i++)
        {
            PathStep s = Steps[i];
            sb.Append('/');
            if (s.Kind == StepKind.Text)
            {
                sb.Append("text()");
                continue;
            }

            if (s.Kind == StepKind.Attribute)
            {
                sb.Append('@');
            }

            if (s.Prefix is not null)
            {
                sb.Append(s.Prefix).Append(':');
            }

            sb.Append(s.Name);
            if (s.Index is { } n && i < Steps.Count - 1)
            {
                sb.Append('[').Append(n.ToString(CultureInfo.InvariantCulture)).Append(']');
            }
        }

        return sb.ToString();
    }
}
