using System.Linq;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Documents.Styles;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Schema;
using RecursiveAI.OneDocument.Operations;

namespace RecursiveAI.OneDocument.Documents;

internal sealed partial class DocumentMerger
{
    private readonly Dictionary<string, string> _styleMap = new(StringComparer.Ordinal);

    internal static string PartOf(Document d, MarkupNode node) =>
        node.Document is { } doc ? d.Parts.NameOf(doc) ?? d.MainPartName : d.MainPartName;

    // ECMA-376 Part 1 §17.7.4.17 (style): styles are matched by name and type, as Word matches them when it pastes between documents.
    // Content refers to a style by id, so every reference in the copied content is rewritten through this map.
    private string MapStyle(string srcId)
    {
        if (_styleMap.TryGetValue(srcId, out string? mapped))
        {
            return mapped;
        }

        StyleDefinition? s = _same ? null : _src.Styles.Find(srcId);
        if (s is null)
        {
            _styleMap[srcId] = srcId;
            return srcId;
        }

        string name = s.Name ?? srcId;
        StyleDefinition? d = _dst.Styles.Styles.FirstOrDefault(x => x.Type == s.Type && string.Equals(x.Name ?? x.Id, name, StringComparison.OrdinalIgnoreCase));
        if (d is null)
        {
            string id = _dst.Styles.Find(srcId) is null ? srcId : UniqueStyleId(srcId);
            CopyStyle(s, id, newName: null, keepDefault: false);
            _r.Count("styles");
            return id;
        }

        if (MergeText.Canonical(s.Markup.Node.OuterXml, ignoreStyleId: true) == MergeText.Canonical(d.Markup.Node.OuterXml, ignoreStyleId: true))
        {
            _styleMap[srcId] = d.Id;
            return d.Id;
        }

        switch (_o.Styles)
        {
            case StyleConflict.UseSource:
                _styleMap[srcId] = d.Id;
                ReplaceStyle(s, d);
                _r.Resolve(new MergeResolution("merge.style-conflict", name, "used source",
                    "The style '" + name + "' is defined differently in the source; the destination's definition was replaced, so text already in the destination that uses it changes."));
                return d.Id;
            case StyleConflict.KeepBoth:
                string newName = UniqueStyleName(name);
                string newId = UniqueStyleId(srcId);
                CopyStyle(s, newId, newName, keepDefault: false);
                _r.Count("styles");
                _r.Resolve(new MergeResolution("merge.style-conflict", name, "kept both",
                    "The style '" + name + "' is defined differently in the source; it was added as '" + newName + "' and the copied content uses that."));
                return newId;
            default:
                _styleMap[srcId] = d.Id;
                _r.Resolve(new MergeResolution("merge.style-conflict", name, "kept destination",
                    "The style '" + name + "' is defined differently in the source; the copied content takes the destination's definition."));
                return d.Id;
        }
    }

    private string UniqueStyleId(string id)
    {
        for (int i = 2; ; i++)
        {
            string candidate = id + Num(i);
            if (_dst.Styles.Find(candidate) is null && !_styleMap.ContainsValue(candidate))
            {
                return candidate;
            }
        }
    }

    private string UniqueStyleName(string name)
    {
        for (int i = 2; ; i++)
        {
            string candidate = name + " (" + Num(i) + ")";
            if (!_dst.Styles.Styles.Any(x => string.Equals(x.Name, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                return candidate;
            }
        }
    }

    private void CopyStyle(StyleDefinition s, string newId, string? newName, bool keepDefault)
    {
        _styleMap[s.Id] = newId;
        MarkupNode root = _dst.Styles.EnsureRootForMerge().Node;
        byte[] xml = MarkupFragment.Serialize(
            s.Markup.Node,
            root,
            MakeOptions(PartOf(_src, s.Markup.Node), PartOf(_dst, root), StyleHook(newId, newName, keepDefault)));
        root.AppendXml(xml);
        _dst.Styles.Invalidate();
    }

    private void ReplaceStyle(StyleDefinition s, StyleDefinition d)
    {
        MarkupNode root = d.Markup.Node.Parent;
        byte[] xml = MarkupFragment.Serialize(
            s.Markup.Node,
            root,
            MakeOptions(PartOf(_src, s.Markup.Node), PartOf(_dst, root), StyleHook(d.Id, null, d.IsDefault)));
        d.Markup.Node.ReplaceWithXml(xml);
        _dst.Styles.Invalidate();
    }

    private static ExtraRewrite StyleHook(string id, string? name, bool keepDefault) => (e, eLocal, aLocal, value) =>
    {
        if (eLocal == "style" && aLocal == "styleId")
        {
            return AttributeEdit.Set(id);
        }

        if (eLocal == "style" && aLocal == "default")
        {
            return keepDefault ? AttributeEdit.Set("1") : AttributeEdit.Remove;
        }

        if (name is not null && eLocal == "name" && aLocal == "val" && e.Parent is { IsNull: false } p && p.LocalNameString == "style")
        {
            return AttributeEdit.Set(name);
        }

        return null;
    };
}
