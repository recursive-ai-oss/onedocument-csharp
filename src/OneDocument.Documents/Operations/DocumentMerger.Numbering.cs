using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Documents.Numbering;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Schema;
using RecursiveAI.OneDocument.Operations;

namespace RecursiveAI.OneDocument.Documents;

internal sealed partial class DocumentMerger
{
    private readonly Dictionary<string, string> _numberMap = new(StringComparer.Ordinal);
    private long _nextNum;
    private long _nextAbstract;

    // ECMA-376 Part 1 §17.9 (numbering): a paragraph names a list by w:numId, which names a w:num, which names a w:abstractNum holding the
    // level formats. The abstract definition is the identity of a list's look; two documents with an equal one have "the same list".
    private string MapNumbering(string srcNum)
    {
        if (_numberMap.TryGetValue(srcNum, out string? mapped))
        {
            return mapped;
        }

        if (_same || Wml.Long(srcNum) is not { } id || _src.Numbering.FindInstance(id) is not { } instance
            || _src.Numbering.FindAbstract(instance.AbstractNumberingId) is not { } abstractDef)
        {
            if (!_same)
            {
                _r.Problem("merge.numbering-missing", "A paragraph uses the list " + srcNum + ", which the source's numbering part does not define; the reference was kept as it is.",
                    DefectLocation.InPart(_src.MainPartName));
            }

            _numberMap[srcNum] = srcNum;
            return srcNum;
        }

        MarkupNode root = _dst.Numbering.EnsureRootForMerge().Node;
        if (_nextNum == 0)
        {
            _nextNum = _dst.Numbering.Instances.Select(i => i.Id).DefaultIfEmpty(0).Max() + 1;
            _nextAbstract = _dst.Numbering.AbstractDefinitions.Select(a => a.Id).DefaultIfEmpty(-1).Max() + 1;
        }

        string canon = MergeText.CanonicalAbstract(abstractDef.Markup.Node.OuterXml);
        AbstractNumberingDefinition? same = _dst.Numbering.AbstractDefinitions.FirstOrDefault(a => MergeText.CanonicalAbstract(a.Markup.Node.OuterXml) == canon);
        long newNum = _nextNum++;
        _numberMap[srcNum] = Num(newNum);
        long dstAbstract;
        if (same is null)
        {
            dstAbstract = _nextAbstract++;
            string srcPart = PartOf(_src, abstractDef.Markup.Node);
            string dstPart = PartOf(_dst, root);
            byte[] xml = MarkupFragment.Serialize(abstractDef.Markup.Node, root, MakeOptions(srcPart, dstPart, (e, eLocal, aLocal, v) =>
                eLocal == "abstractNum" && aLocal == "abstractNumId" ? AttributeEdit.Set(Num(dstAbstract)) : null));
            MarkupNode? firstNum = FirstChildNamed(root, "num");
            if (firstNum is { } f)
            {
                f.InsertXmlBefore(xml);
            }
            else
            {
                root.AppendXml(xml);
            }

            _r.Count("numbering");
        }
        else
        {
            dstAbstract = same.Id;
            if (_o.Numbering == NumberingContinuation.Continue)
            {
                string srcOverrides = MergeText.CanonicalInstance(instance.Markup.Node.OuterXml);
                NumberingInstance? existing = _dst.Numbering.Instances.FirstOrDefault(i => i.AbstractNumberingId == dstAbstract
                    && MergeText.CanonicalInstance(i.Markup.Node.OuterXml) == srcOverrides);
                if (existing is not null)
                {
                    _numberMap[srcNum] = Num(existing.Id);
                    _r.Resolve(new MergeResolution("merge.list-continued", "list " + srcNum, "reused",
                        "The list matches the destination's list " + Num(existing.Id) + "; the copied paragraphs continue its numbering."));
                    return _numberMap[srcNum];
                }
            }
            else
            {
                AppendInstance(root, RestartInstance(newNum, dstAbstract, same));
                _r.Resolve(new MergeResolution("merge.list-restarted", "list " + srcNum, "restarted",
                    "The list matches a destination list; the copied paragraphs form a new list that starts again at the first number."));
                return Num(newNum);
            }
        }

        // The instance: the source's w:num with its own id and the destination's abstract definition.
        string dp = PartOf(_dst, root);
        byte[] inst = MarkupFragment.Serialize(instance.Markup.Node, root, MakeOptions(PartOf(_src, instance.Markup.Node), dp, (e, eLocal, aLocal, v) =>
        {
            if (eLocal == "num" && aLocal == "numId")
            {
                return AttributeEdit.Set(Num(newNum));
            }

            return eLocal == "abstractNumId" && aLocal == "val" ? AttributeEdit.Set(Num(dstAbstract)) : null;
        }));
        AppendInstance(root, inst);
        return Num(newNum);
    }

    private static MarkupNode? FirstChildNamed(MarkupNode parent, string local)
    {
        for (MarkupNode c = parent.FirstChild; !c.IsNull; c = c.NextSibling)
        {
            if (c.Kind == MarkupNodeKind.Element && c.LocalNameString == local && HubXml.IsWordNamespace(c.NamespaceUri))
            {
                return c;
            }
        }

        return null;
    }

    private static void AppendInstance(MarkupNode root, byte[] xml)
    {
        if (FirstChildNamed(root, "numIdMacAtCleanup") is { } cleanup)
        {
            cleanup.InsertXmlBefore(xml);
        }
        else
        {
            root.AppendXml(xml);
        }
    }

    // A w:num that restarts every level the abstract definition defines (ECMA-376 Part 1 §17.9.8 lvlOverride, §17.9.26 startOverride).
    private byte[] RestartInstance(long numId, long abstractId, AbstractNumberingDefinition abstractDef)
    {
        var sb = new StringBuilder();
        sb.Append("<w:num xmlns:w=\"").Append(_dst.WordprocessingNamespaceUri).Append("\" w:numId=\"").Append(Num(numId)).Append("\">");
        sb.Append("<w:abstractNumId w:val=\"").Append(Num(abstractId)).Append("\"/>");
        foreach (KeyValuePair<int, MarkupRef> level in abstractDef.Levels.OrderBy(l => l.Key))
        {
            long start = Wml.Long(Wml.Attr(Wml.Child(level.Value, "start"u8) ?? default, "val")) ?? 1;
            sb.Append("<w:lvlOverride w:ilvl=\"").Append(Num(level.Key)).Append("\"><w:startOverride w:val=\"").Append(Num(start)).Append("\"/></w:lvlOverride>");
        }

        sb.Append("</w:num>");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }
}
