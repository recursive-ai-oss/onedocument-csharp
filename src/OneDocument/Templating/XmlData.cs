using System;
using System.Collections.Generic;
using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Templating;

// XML as template data, read with the library's own tokenizer-backed tree (never the BCL XML classes).
internal static class XmlData
{
    public static ITemplateData Read(ReadOnlyMemory<byte> utf8, OfficeLimits limits)
    {
        MarkupDocument doc = MarkupDocument.Parse(utf8, new OpenContext(limits));
        MarkupNode root = doc.Root;
        if (root.IsNull)
        {
            return TemplateData.Null;
        }

        ITemplateData node = Convert(root, 0, limits);
        if (node.Kind == TemplateDataKind.Scalar)
        {
            // A root with only text still has to be an object so that paths start from its members.
            return TemplateData.Record([new KeyValuePair<string, ITemplateData>("#text", node)]);
        }

        return node;
    }

    private static ITemplateData Convert(MarkupNode element, int depth, OfficeLimits limits)
    {
        OfficeLimitException.ThrowIfExceeded(nameof(OfficeLimits.MaxXmlDepth), limits.MaxXmlDepth, depth);
        var members = new Dictionary<string, ITemplateData>(StringComparer.Ordinal);
        foreach (MarkupAttributeRef attribute in element.Attributes)
        {
            if (!attribute.IsNamespaceDeclaration)
            {
                members[System.Text.Encoding.UTF8.GetString(attribute.LocalName)] = TextScalar(attribute.Value);
            }
        }

        var groups = new Dictionary<string, List<ITemplateData>>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (MarkupNode child in element.ChildElements)
        {
            string name = child.LocalNameString;
            if (!groups.TryGetValue(name, out List<ITemplateData>? list))
            {
                list = [];
                groups[name] = list;
                order.Add(name);
            }

            list.Add(Convert(child, depth + 1, limits));
        }

        foreach (string name in order)
        {
            List<ITemplateData> list = groups[name];
            members[name] = list.Count == 1 ? list[0] : TemplateData.Sequence(list);
        }

        if (order.Count == 0)
        {
            string text = element.InnerText;
            if (members.Count == 0)
            {
                return TextScalar(text);
            }

            members["#text"] = TextScalar(text);
        }

        return TemplateData.Record(members);
    }

    // XML has no types: text that reads as a plain number counts as one in formulas (so SUM works), and still prints as written.
    private static ITemplateData TextScalar(string text)
    {
        if (text.Length > 0 && text.Length < 40 && (char.IsAsciiDigit(text[0]) || text[0] is '-' or '.')
            && double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d) && double.IsFinite(d))
        {
            return new TemplateData.ScalarData(CalcValue.FromNumber(d), text, text);
        }

        return TemplateData.Scalar(CalcValue.FromText(text));
    }
}
