using System.Linq;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Schema;
using RecursiveAI.OneDocument.Operations;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Documents;

internal sealed partial class DocumentMerger
{
    private static readonly HashSet<string> RevisionElements = new(StringComparer.Ordinal)
    {
        "ins", "del", "moveFrom", "moveTo", "rPrChange", "pPrChange", "sectPrChange", "tblPrChange", "tcPrChange", "trPrChange", "tblGridChange",
        "numberingChange", "cellIns", "cellDel", "cellMerge", "moveFromRangeStart", "moveFromRangeEnd", "moveToRangeStart", "moveToRangeEnd",
    };

    private readonly Dictionary<string, Dictionary<string, string>> _ignorableOfPart = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _renames = new(StringComparer.Ordinal);

    /// <summary>A hook that sees an attribute first; it returns null to leave the decision to the general rules.</summary>
    internal delegate AttributeEdit? ExtraRewrite(MarkupNode element, string elementLocal, string attributeLocal, string attributeValue);

    private MarkupFragmentOptions MakeOptions(string srcPart, string dstPart, ExtraRewrite? extra = null)
    {
        Dictionary<string, string> ignorable = IgnorableOf(srcPart);
        return new MarkupFragmentOptions
        {
            KeepElement = e => Keep(e, ignorable),
            RewriteAttribute = (MarkupNode e, in MarkupAttributeRef a) => Rewrite(e, a, srcPart, dstPart, ignorable, extra),
            RewriteText = RewriteInstructionText,
        };
    }

    // The prefixes the source part lists in mc:Ignorable, with their namespaces.
    private Dictionary<string, string> IgnorableOf(string srcPart)
    {
        if (_ignorableOfPart.TryGetValue(srcPart, out Dictionary<string, string>? cached))
        {
            return cached;
        }

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        MarkupDocument? doc = _src.Parts.Load(srcPart, required: false);
        if (doc is not null && !doc.Root.IsNull && doc.Root.GetAttribute(McNs, "Ignorable") is { } list)
        {
            foreach (string p in list.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (doc.Root.TryResolvePrefix(p, out string uri))
                {
                    map[p] = uri;
                }
            }
        }

        _ignorableOfPart[srcPart] = map;
        return map;
    }

    private bool Keep(MarkupNode e, Dictionary<string, string> ignorable)
    {
        if (HubXml.IsWordNamespace(e.NamespaceUri))
        {
            switch (e.LocalNameString)
            {
                case "dataBinding":
                    _droppedBindings++;
                    return false;
                case "footnoteReference":
                    return Wml.Long(WAttr(e, "id")) is { } f && _footnoteIds.ContainsKey(f);
                case "endnoteReference":
                    return Wml.Long(WAttr(e, "id")) is { } n && _endnoteIds.ContainsKey(n);
                case "commentReference":
                case "commentRangeStart":
                case "commentRangeEnd":
                    return Wml.Long(WAttr(e, "id")) is { } c && _commentIds.ContainsKey(c);
                case "nsid" when e.Parent is { IsNull: false } p && p.LocalNameString == "abstractNum":
                    return false;
            }

            return true;
        }

        if (e.Prefix.Length > 0 && ignorable.Count > 0)
        {
            string prefix = Encoding.UTF8.GetString(e.Prefix);
            if (ignorable.TryGetValue(prefix, out string? uri))
            {
                _ignorableUsed[prefix] = uri;
            }
        }

        return true;
    }

    private AttributeEdit Rewrite(MarkupNode e, in MarkupAttributeRef a, string srcPart, string dstPart, Dictionary<string, string> ignorable, ExtraRewrite? extra)
    {
        string aLocal = Encoding.UTF8.GetString(a.LocalName);
        string aNs = a.Prefix.Length > 0 ? a.NamespaceUri : string.Empty;
        string eLocal = e.LocalNameString;
        bool eW = HubXml.IsWordNamespace(e.NamespaceUri);
        string value = a.Value;

        if (extra?.Invoke(e, eLocal, aLocal, value) is { } forced)
        {
            return forced;
        }

        if (aNs == W14Ns && aLocal is "paraId" or "textId")
        {
            // w14:paraId / w14:textId must be unique in a document, and what refers to them (comment threads) is not copied.
            return AttributeEdit.Remove;
        }

        if (aNs is RelNsTransitional or RelNsStrict)
        {
            return value.Length == 0 ? AttributeEdit.Keep : AttributeEdit.Set(MapRelationship(srcPart, value, dstPart));
        }

        if (a.Prefix.Length > 0 && ignorable.Count > 0)
        {
            string prefix = Encoding.UTF8.GetString(a.Prefix);
            if (ignorable.TryGetValue(prefix, out string? uri))
            {
                _ignorableUsed[prefix] = uri;
            }
        }

        if (!eW)
        {
            // wp:docPr/@id must be unique in the document (ECMA-376 Part 1 §20.4.2.5).
            if (eLocal == "docPr" && aLocal == "id" && a.Prefix.Length == 0)
            {
                return AttributeEdit.Set(Num(_nextDrawing++));
            }

            return AttributeEdit.Keep;
        }

        if (!HubXml.IsWordNamespace(aNs))
        {
            return AttributeEdit.Keep;
        }

        switch (eLocal)
        {
            case "pStyle" or "rStyle" or "tblStyle" or "basedOn" or "next" or "link" when aLocal == "val":
                return AttributeEdit.Set(MapStyle(value));
            case "numId" when aLocal == "val":
                return value == "0" ? AttributeEdit.Keep : AttributeEdit.Set(MapNumbering(value));
            case "bookmarkStart" when aLocal == "name":
                return _renames.TryGetValue(value, out string? renamed) ? AttributeEdit.Set(renamed) : AttributeEdit.Keep;
            case "bookmarkStart" or "bookmarkEnd" when aLocal == "id":
                return AttributeEdit.Set(Num(MapAnnotation(_bookmarkIds, value)));
            case "hyperlink" when aLocal == "anchor":
                return _renames.TryGetValue(value, out string? target) ? AttributeEdit.Set(target) : AttributeEdit.Keep;
            case "fldSimple" when aLocal == "instr":
                return RewriteInstruction(value) is { } instr ? AttributeEdit.Set(instr) : AttributeEdit.Keep;
            case "footnoteReference" or "footnote" when aLocal == "id":
                return MapKnown(_footnoteIds, value);
            case "endnoteReference" or "endnote" when aLocal == "id":
                return MapKnown(_endnoteIds, value);
            case "commentReference" or "commentRangeStart" or "commentRangeEnd" or "comment" when aLocal == "id":
                return MapKnown(_commentIds, value);
            case "id" when aLocal == "val" && e.Parent is { IsNull: false } parent && parent.LocalNameString == "sdtPr":
                return AttributeEdit.Set(Num(MapControl(value)));
        }

        if (RevisionElements.Contains(eLocal))
        {
            if (aLocal == "id")
            {
                return AttributeEdit.Set(Num(MapAnnotation(_revisionIds, value)));
            }

            if (aLocal == "author" && eLocal is "ins" or "del" or "moveFrom" or "moveTo" or "rPrChange" or "pPrChange" or "sectPrChange" or "tblPrChange" or "tcPrChange" or "trPrChange")
            {
                _authors.Add(value);
            }
        }

        return AttributeEdit.Keep;
    }

    private static AttributeEdit MapKnown(Dictionary<long, long> map, string value) =>
        Wml.Long(value) is { } id && map.TryGetValue(id, out long to) ? AttributeEdit.Set(Num(to)) : AttributeEdit.Keep;

    private long MapAnnotation(Dictionary<long, long> map, string value)
    {
        long id = Wml.Long(value) ?? -1;
        if (!map.TryGetValue(id, out long to))
        {
            to = _nextAnnotation++;
            map[id] = to;
        }

        return to;
    }

    private long MapControl(string value)
    {
        long id = Wml.Long(value) ?? -1;
        if (!_controlIds.TryGetValue(id, out long to))
        {
            to = _nextControl++;
            _controlIds[id] = to;
        }

        return to;
    }

    // ---- bookmarks ------------------------------------------------------------------------------------------------------------

    // Bookmark names are unique per document (ECMA-376 Part 1 §17.13.6.2); a copied bookmark whose name is taken gets a numbered suffix,
    // and every reference inside the copied content follows it.
    private void ResolveBookmarkNames()
    {
        if (_defined.Count == 0)
        {
            return;
        }

        _dstBookmarks = [];
        foreach (MarkupNode root in StoryRoots(_dst))
        {
            var stack = new Stack<MarkupNode>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                MarkupNode n = stack.Pop();
                if (n.Kind != MarkupNodeKind.Element)
                {
                    continue;
                }

                if (n.LocalNameString == "bookmarkStart" && WAttr(n, "name") is { } name)
                {
                    _dstBookmarks.Add(name);
                }

                for (MarkupNode c = n.FirstChild; !c.IsNull; c = c.NextSibling)
                {
                    stack.Push(c);
                }
            }
        }

        foreach (string name in _defined.OrderBy(x => x, StringComparer.Ordinal))
        {
            if (!_dstBookmarks.Contains(name))
            {
                continue;
            }

            string candidate;
            int i = 2;
            do
            {
                candidate = name + "_" + Num(i++);
            }
            while (_dstBookmarks.Contains(candidate) || _defined.Contains(candidate));

            _dstBookmarks.Add(candidate);
            _renames[name] = candidate;
            _r.Resolve(new MergeResolution("merge.bookmark-renamed", name, "renamed to " + candidate,
                "The destination already has a bookmark named '" + name + "'; the copied one and the references to it inside the copied content use '" + candidate + "'."));
        }
    }

    // Field instructions that name a bookmark: REF, PAGEREF and NOTEREF take it as their first argument, HYPERLINK after \l (§17.16.5).
    private string? RewriteInstruction(string instruction)
    {
        if (_renames.Count == 0)
        {
            return null;
        }

        string[] tokens = Tokenise(instruction);
        int first = Array.FindIndex(tokens, t => t.Trim().Length > 0);
        if (first < 0)
        {
            return null;
        }

        string code = tokens[first].Trim().ToUpperInvariant();
        bool changed = false;
        if (code is "REF" or "PAGEREF" or "NOTEREF")
        {
            for (int i = first + 1; i < tokens.Length; i++)
            {
                string t = tokens[i].Trim();
                if (t.Length == 0 || t[0] == '\\')
                {
                    continue;
                }

                if (_renames.TryGetValue(Unquote(t), out string? to))
                {
                    tokens[i] = tokens[i].Replace(Unquote(t), to, StringComparison.Ordinal);
                    changed = true;
                }

                break;
            }
        }
        else if (code == "HYPERLINK")
        {
            for (int i = first + 1; i + 1 < tokens.Length; i++)
            {
                if (tokens[i].Trim() == "\\l")
                {
                    for (int j = i + 1; j < tokens.Length; j++)
                    {
                        string t = tokens[j].Trim();
                        if (t.Length == 0)
                        {
                            continue;
                        }

                        if (_renames.TryGetValue(Unquote(t), out string? to))
                        {
                            tokens[j] = tokens[j].Replace(Unquote(t), to, StringComparison.Ordinal);
                            changed = true;
                        }

                        break;
                    }
                }
            }
        }

        return changed ? string.Concat(tokens) : null;
    }

    private string? RewriteInstructionText(MarkupNode text)
    {
        if (_renames.Count == 0 || text.Parent is not { IsNull: false } parent || parent.LocalNameString != "instrText" || !HubXml.IsWordNamespace(parent.NamespaceUri))
        {
            return null;
        }

        string s = text.InnerText;
        if (RewriteInstruction(s) is { } changed)
        {
            return changed;
        }

        // A field code split over runs: a node that is just a bookmark name.
        string trimmed = s.Trim();
        if (trimmed.Length > 0 && !trimmed.Contains(' ') && _renames.TryGetValue(trimmed, out string? to))
        {
            return s.Replace(trimmed, to, StringComparison.Ordinal);
        }

        return null;
    }

    private static string Unquote(string t) => t.Length >= 2 && t[0] == '"' && t[^1] == '"' ? t[1..^1] : t;

    // Splits into alternating white-space and word tokens, a quoted string being one word, so that string.Concat restores the text.
    private static string[] Tokenise(string s)
    {
        var list = new List<string>();
        int i = 0;
        while (i < s.Length)
        {
            int start = i;
            if (s[i] == ' ' || s[i] == '\t')
            {
                while (i < s.Length && (s[i] == ' ' || s[i] == '\t'))
                {
                    i++;
                }
            }
            else if (s[i] == '"')
            {
                i++;
                while (i < s.Length && s[i] != '"')
                {
                    i++;
                }

                i = Math.Min(s.Length, i + 1);
            }
            else
            {
                while (i < s.Length && s[i] != ' ' && s[i] != '\t')
                {
                    i++;
                }
            }

            list.Add(s[start..i]);
        }

        return [.. list];
    }
}
