using System.Linq;
using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Schema;
using RecursiveAI.OneDocument.Operations;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Documents;

/// <summary>
/// Copies a range of the body of one document into another. Everything the copied content refers to is brought along and renumbered
/// so that nothing collides: styles, lists, bookmarks, notes, comments, revision and control ids, pictures and other related parts.
/// Every conflict and its resolution is written to the <see cref="MergeReport"/>.
/// </summary>
internal sealed partial class DocumentMerger
{
    internal const string McNs = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    internal const string W14Ns = "http://schemas.microsoft.com/office/word/2010/wordml";
    private const string RelNsTransitional = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string RelNsStrict = "http://purl.oclc.org/ooxml/officeDocument/relationships";

    private readonly Document _dst;
    private readonly Document _src;
    private readonly MergeOptions _o;
    private readonly MergeReport _r = new();
    private readonly bool _same;

    // Bookmarks defined by the copied content, and what each is called in the destination.
    private readonly HashSet<string> _defined = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _bookmarkNames = new(StringComparer.Ordinal);
    private HashSet<string>? _dstBookmarks;

    private readonly Dictionary<long, long> _bookmarkIds = [];
    private readonly Dictionary<long, long> _revisionIds = [];
    private readonly Dictionary<long, long> _commentIds = [];
    private readonly Dictionary<long, long> _footnoteIds = [];
    private readonly Dictionary<long, long> _endnoteIds = [];
    private readonly Dictionary<long, long> _controlIds = [];
    private long _nextAnnotation;
    private long _nextControl;
    private long _nextDrawing;

    private readonly HashSet<string> _authors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _ignorableUsed = new(StringComparer.Ordinal);
    private long _droppedBindings;
    private HashSet<string>? _knownAuthors;

    public DocumentMerger(Document destination, Document source, MergeOptions options)
    {
        _dst = destination;
        _src = source;
        _o = options;
        _same = ReferenceEquals(destination, source);
    }

    /// <summary>The body-level elements of a document other than its final section properties: the "blocks" of a merge.</summary>
    internal static List<MarkupNode> BodyBlocks(MarkupNode body)
    {
        var list = new List<MarkupNode>();
        if (body.IsNull)
        {
            return list;
        }

        for (MarkupNode n = body.FirstChild; !n.IsNull; n = n.NextSibling)
        {
            if (n.Kind == MarkupNodeKind.Element && !(HubXml.IsWordNamespace(n.NamespaceUri) && n.LocalNameString == "sectPr"))
            {
                list.Add(n);
            }
        }

        return list;
    }

    /// <summary>Copies the chosen blocks of the source before block <paramref name="insertAt"/> of the destination (the end when it equals the block count).</summary>
    public MergeReport Execute(int insertAt)
    {
        if (_src.IsStrict != _dst.IsStrict)
        {
            throw new OfficeUnsupportedException(
                "Combining a Strict document with a Transitional one",
                DefectLocation.InPart(_dst.MainPartName));
        }

        MarkupNode dstBody = _dst.EnsureBodyElement();
        List<MarkupNode> dstBlocks = BodyBlocks(dstBody);
        if (insertAt < 0 || insertAt > dstBlocks.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(insertAt), "The insert position is outside the destination's body blocks.");
        }

        MarkupNode srcBody = _src.Body.Container.IsValid ? _src.Body.Container.Node : default;
        List<MarkupNode> all = BodyBlocks(srcBody);
        if (_o.SourceStart < 0 || _o.SourceStart > all.Count || _o.SourceCount is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(insertAt), "The source range is outside the source's body blocks.");
        }

        int count = Math.Min(_o.SourceCount ?? all.Count, all.Count - _o.SourceStart);
        List<MarkupNode> blocks = all.GetRange(_o.SourceStart, count);
        MarkupNode? finalSect = FindBodySectPr(srcBody);
        bool takesEnd = _o.SourceStart + count == all.Count;

        MarkupNode dstSect = FindBodySectPr(dstBody) ?? default;
        MarkupNode before = insertAt < dstBlocks.Count ? dstBlocks[insertAt] : dstSect;

        _knownAuthors = DestinationAuthors();
        Prescan(blocks);
        ResolveBookmarkNames();
        InitialiseCounters();
        AllocateNoteAndCommentIds();

        // What the destination section is when the copied content starts: the first section break at or after the insert position.
        MarkupNode governing = GoverningSection(dstBlocks, insertAt, dstSect);

        string srcMain = _src.MainPartName;
        string dstMain = _dst.MainPartName;
        byte[] body = MarkupFragment.Serialize(blocks, dstBody, MakeOptions(srcMain, dstMain));

        bool keepSource = _o.Sections == SectionHandling.KeepSource && finalSect is not null && takesEnd;
        byte[]? prefix = null;
        byte[]? suffix = null;
        byte[]? newBodySect = null;
        if (keepSource)
        {
            // The destination's section ends right before the copied content; the source's last section ends right after it.
            if (!governing.IsNull)
            {
                prefix = SectionBreakParagraph(governing, dstBody, MakeOptions(dstMain, dstMain));
            }

            byte[] sect = MarkupFragment.Serialize(finalSect!.Value, dstBody, MakeOptions(srcMain, dstMain));
            if (before == dstSect && !dstSect.IsNull)
            {
                newBodySect = sect;
            }
            else
            {
                suffix = WrapSectionParagraph(sect);
            }

            _r.Count("sections");
        }
        else if (finalSect is not null && takesEnd)
        {
            ReportSectionLoss(finalSect.Value, governing);
        }

        if (prefix is not null)
        {
            Insert(before, dstBody, prefix);
        }

        Insert(before, dstBody, body);
        if (suffix is not null)
        {
            Insert(before, dstBody, suffix);
        }

        if (newBodySect is not null)
        {
            dstSect.ReplaceWithXml(newBodySect);
        }

        _r.Count("blocks", blocks.Count);
        CopyNotes();
        CopyComments();
        ReportEnvironmentLoss();
        FinishRevisionReport();
        EnsureIgnorables();
        _dst.InvalidateAfterOperation();
        return _r;
    }

    // ---- body insertion -------------------------------------------------------------------------------------------------------

    private static void Insert(MarkupNode before, MarkupNode parent, byte[] xml)
    {
        if (before.IsNull)
        {
            parent.AppendXml(xml);
        }
        else
        {
            before.InsertXmlBefore(xml);
        }
    }

    private static MarkupNode? FindBodySectPr(MarkupNode body)
    {
        if (body.IsNull)
        {
            return null;
        }

        for (MarkupNode n = body.LastChild; !n.IsNull; n = n.PreviousSibling)
        {
            if (n.Kind == MarkupNodeKind.Element && HubXml.IsWordNamespace(n.NamespaceUri) && n.LocalNameString == "sectPr")
            {
                return n;
            }
        }

        return null;
    }

    // ECMA-376 Part 1 §17.6.17 (sectPr): a section's properties sit in the last paragraph of the section, and the body's final
    // sectPr holds those of the last section. The section a position belongs to is the one whose break comes first at or after it.
    private static MarkupNode GoverningSection(List<MarkupNode> blocks, int index, MarkupNode bodySect)
    {
        for (int i = index; i < blocks.Count; i++)
        {
            if (SectPrIn(blocks[i]) is { } s)
            {
                return s;
            }
        }

        return bodySect;
    }

    private static MarkupNode? SectPrIn(MarkupNode block)
    {
        if (!HubXml.IsWordNamespace(block.NamespaceUri))
        {
            return null;
        }

        if (block.LocalNameString == "p")
        {
            for (MarkupNode c = block.FirstChild; !c.IsNull; c = c.NextSibling)
            {
                if (c.Kind == MarkupNodeKind.Element && c.LocalNameString == "pPr")
                {
                    for (MarkupNode d = c.FirstChild; !d.IsNull; d = d.NextSibling)
                    {
                        if (d.Kind == MarkupNodeKind.Element && d.LocalNameString == "sectPr")
                        {
                            return d;
                        }
                    }
                }
            }
        }
        else if (block.LocalNameString == "sdt")
        {
            for (MarkupNode c = block.FirstChild; !c.IsNull; c = c.NextSibling)
            {
                if (c.Kind == MarkupNodeKind.Element && c.LocalNameString == "sdtContent")
                {
                    for (MarkupNode d = c.FirstChild; !d.IsNull; d = d.NextSibling)
                    {
                        if (d.Kind == MarkupNodeKind.Element && SectPrIn(d) is { } s)
                        {
                            return s;
                        }
                    }
                }
            }
        }

        return null;
    }

    private byte[] SectionBreakParagraph(MarkupNode sect, MarkupNode scope, MarkupFragmentOptions options) =>
        WrapSectionParagraph(MarkupFragment.Serialize(sect, scope, options));

    private byte[] WrapSectionParagraph(byte[] sectXml)
    {
        string ns = _dst.WordprocessingNamespaceUri;
        var sb = new StringBuilder();
        sb.Append("<w:p xmlns:w=\"").Append(ns).Append("\"><w:pPr>");
        sb.Append(Encoding.UTF8.GetString(sectXml));
        sb.Append("</w:pPr></w:p>");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    // ---- prescan and counters ---------------------------------------------------------------------------------------------------

    private readonly List<long> _footnoteRefs = [];
    private readonly List<long> _endnoteRefs = [];
    private readonly List<long> _commentRefs = [];

    private void Prescan(List<MarkupNode> roots)
    {
        foreach (MarkupNode root in roots)
        {
            ScanSubtree(root);
        }
    }

    private void ScanSubtree(MarkupNode root)
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

            if (HubXml.IsWordNamespace(n.NamespaceUri))
            {
                switch (n.LocalNameString)
                {
                    case "bookmarkStart":
                        if (WAttr(n, "name") is { } name)
                        {
                            _defined.Add(name);
                        }

                        break;
                    case "footnoteReference":
                        AddRef(_footnoteRefs, n);
                        break;
                    case "endnoteReference":
                        AddRef(_endnoteRefs, n);
                        break;
                    case "commentReference":
                    case "commentRangeStart":
                    case "commentRangeEnd":
                        AddRef(_commentRefs, n);
                        break;
                }
            }

            for (MarkupNode c = n.LastChild; !c.IsNull; c = c.PreviousSibling)
            {
                stack.Push(c);
            }
        }
    }

    private static void AddRef(List<long> list, MarkupNode n)
    {
        if (Wml.Long(WAttr(n, "id")) is { } id && !list.Contains(id))
        {
            list.Add(id);
        }
    }

    internal static string? WAttr(MarkupNode e, string local)
    {
        foreach (MarkupAttributeRef a in e.Attributes)
        {
            if (!a.IsNamespaceDeclaration && a.Prefix.Length > 0 && Wml.AsciiEquals(a.LocalName, local) && HubXml.IsWordNamespace(a.NamespaceUri))
            {
                return a.Value;
            }
        }

        return null;
    }

    private void InitialiseCounters()
    {
        long maxAnnotation = 0;
        long maxControl = 0;
        long maxDrawing = 0;
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

                string local = n.LocalNameString;
                bool isW = HubXml.IsWordNamespace(n.NamespaceUri);
                if (isW && local is "bookmarkStart" or "bookmarkEnd" or "ins" or "del" or "moveFrom" or "moveTo" or "moveFromRangeStart" or "moveFromRangeEnd"
                    or "moveToRangeStart" or "moveToRangeEnd" or "rPrChange" or "pPrChange" or "sectPrChange" or "tblPrChange" or "tcPrChange" or "trPrChange" or "tblGridChange"
                    or "numberingChange" or "cellIns" or "cellDel" or "cellMerge")
                {
                    if (Wml.Long(WAttr(n, "id")) is { } id && id > maxAnnotation)
                    {
                        maxAnnotation = id;
                    }
                }
                else if (isW && local == "id" && n.Parent is { IsNull: false } p && p.LocalNameString == "sdtPr")
                {
                    if (Wml.Long(WAttr(n, "val")) is { } cid && cid > maxControl)
                    {
                        maxControl = cid;
                    }
                }
                else if (local == "docPr" && n.GetAttribute("id") is { } did && Wml.Long(did) is { } d && d > maxDrawing)
                {
                    maxDrawing = d;
                }

                for (MarkupNode c = n.FirstChild; !c.IsNull; c = c.NextSibling)
                {
                    stack.Push(c);
                }
            }
        }

        _nextAnnotation = maxAnnotation + 1;
        _nextControl = maxControl + 1;
        _nextDrawing = maxDrawing + 1;
    }

    // The roots of the destination's parts that hold ids of the annotation family: the body, headers, footers, notes, comments.
    private static IEnumerable<MarkupNode> StoryRoots(Document d)
    {
        yield return d.MainMarkup.Root;
        foreach (Relationship r in d.Package.GetRelationships(d.MainPartName))
        {
            if (r.TargetMode == RelationshipTargetMode.Internal
                && (RelationshipTypes.Is(r.Type, "header") || RelationshipTypes.Is(r.Type, "footer") || RelationshipTypes.Is(r.Type, "footnotes")
                    || RelationshipTypes.Is(r.Type, "endnotes") || RelationshipTypes.Is(r.Type, "comments"))
                && PartUri.TryResolve(new PartUri(d.MainPartName), r.Target, out PartUri uri)
                && d.Parts.Load(uri.Value, required: false) is { } doc)
            {
                yield return doc.Root;
            }
        }
    }

    private void DropMissing(List<long> refs, string relationship, string element)
    {
        if (refs.Count == 0)
        {
            return;
        }

        MarkupDocument? doc = _src.LoadRelatedByType(relationship.StartsWith("http", StringComparison.Ordinal) ? relationship : _src.OfficeRelationship(relationship), out _);
        foreach (long id in refs.ToList())
        {
            if (FindById(doc?.Root ?? default, element, id).IsNull)
            {
                refs.Remove(id);
                _r.Problem("merge." + (element == "comment" ? "comment" : "note") + "-missing", "The copied content refers to the " + element + " " + Num(id) + ", which the source does not have; the reference was left out.",
                    DefectLocation.InPart(_src.MainPartName));
            }
        }
    }

    private void AllocateNoteAndCommentIds()
    {
        DropMissing(_footnoteRefs, "footnotes", "footnote");
        DropMissing(_endnoteRefs, "endnotes", "endnote");
        DropMissing(_commentRefs, "comments", "comment");
        if (_footnoteRefs.Count > 0)
        {
            MarkupNode root = _dst.Footnotes.EnsureRootForMerge().Node;
            long next = Math.Max(1, NoteIds.NextFree(_dst, root, 1, "footnote", "footnoteReference"));
            foreach (long id in _footnoteRefs)
            {
                _footnoteIds[id] = next++;
            }
        }

        if (_endnoteRefs.Count > 0)
        {
            MarkupNode root = _dst.Endnotes.EnsureRootForMerge().Node;
            long next = Math.Max(1, NoteIds.NextFree(_dst, root, 1, "endnote", "endnoteReference"));
            foreach (long id in _endnoteRefs)
            {
                _endnoteIds[id] = next++;
            }
        }

        if (_commentRefs.Count > 0)
        {
            MarkupNode root = _dst.Comments.EnsureRootForMerge().Node;
            long next = Math.Max(0, NoteIds.NextFree(_dst, root, 0, "comment", "commentReference", "commentRangeStart", "commentRangeEnd"));
            foreach (long id in _commentRefs)
            {
                _commentIds[id] = next++;
            }
        }
    }

    // ---- notes and comments -----------------------------------------------------------------------------------------------------

    private void CopyNotes()
    {
        CopyNoteKind(_footnoteRefs, _footnoteIds, "footnotes", "footnote", _dst.Footnotes.EnsureRootForMerge, "footnotes");
        CopyNoteKind(_endnoteRefs, _endnoteIds, "endnotes", "endnote", _dst.Endnotes.EnsureRootForMerge, "endnotes");
    }

    private void CopyNoteKind(List<long> refs, Dictionary<long, long> map, string relationship, string element, Func<MarkupRef> ensureDst, string countName)
    {
        if (refs.Count == 0)
        {
            return;
        }

        MarkupDocument? srcDoc = _src.LoadRelatedByType(_src.OfficeRelationship(relationship), out string? srcPart);
        MarkupNode dstRoot = ensureDst().Node;
        string dstPart = _dst.Parts.PartNameOf(dstRoot) ?? "/word/" + relationship + ".xml";
        foreach (long id in refs)
        {
            MarkupNode note = FindById(srcDoc?.Root ?? default, element, id);
            if (note.IsNull)
            {
                _r.Problem("merge.note-missing", "A " + element + " reference points at a note the source does not have; the reference was left out.",
                    DefectLocation.InPart(_src.MainPartName));
                continue;
            }

            dstRoot.AppendXml(MarkupFragment.Serialize(note, dstRoot, MakeOptions(srcPart!, dstPart)));
            _r.Count(countName);
        }
    }

    private void CopyComments()
    {
        if (_commentRefs.Count == 0)
        {
            return;
        }

        MarkupDocument? srcDoc = _src.LoadRelatedByType(_src.OfficeRelationship("comments"), out string? srcPart);
        MarkupNode dstRoot = _dst.Comments.EnsureRootForMerge().Node;
        string dstPart = _dst.Parts.PartNameOf(dstRoot) ?? "/word/comments.xml";
        long copied = 0;
        foreach (long id in _commentRefs)
        {
            MarkupNode c = FindById(srcDoc?.Root ?? default, "comment", id);
            if (c.IsNull)
            {
                _r.Problem("merge.comment-missing", "A comment marker points at a comment the source does not have; the markers were left out.",
                    DefectLocation.InPart(_src.MainPartName));
                continue;
            }

            dstRoot.AppendXml(MarkupFragment.Serialize(c, dstRoot, MakeOptions(srcPart!, dstPart)));
            copied++;
        }

        _r.Count("comments", copied);
        if (copied > 0 && _src.LoadRelatedByType("http://schemas.microsoft.com/office/2011/relationships/commentsExtended", out _) is not null)
        {
            _r.Loss.Report("merge.comment-threading", "Comment replies and resolved state", copied,
                "Comments were copied with their text; the links between a reply and its comment and the resolved state are not carried over.");
        }
    }

    private static MarkupNode FindById(MarkupNode root, string local, long id)
    {
        if (root.IsNull)
        {
            return default;
        }

        for (MarkupNode n = root.FirstChild; !n.IsNull; n = n.NextSibling)
        {
            if (n.Kind == MarkupNodeKind.Element && HubXml.IsWordNamespace(n.NamespaceUri) && n.LocalNameString == local && Wml.Long(WAttr(n, "id")) == id)
            {
                return n;
            }
        }

        return default;
    }

    // ---- what the destination does not take over --------------------------------------------------------------------------------

    private void ReportSectionLoss(MarkupNode srcFinal, MarkupNode dstGoverning)
    {
        if (_same || dstGoverning.IsNull)
        {
            return;
        }

        string a = MergeText.CanonicalSection(srcFinal.OuterXml, out bool srcStories);
        string b = MergeText.CanonicalSection(dstGoverning.OuterXml, out _);
        if (a != b || srcStories)
        {
            _r.Loss.Report("merge.section-properties", "Page setup of the source's last section", 1,
                "The copied content continues the destination's last section: the source's page size, margins, columns, headers and footers were not taken over.");
        }
    }

    private void ReportEnvironmentLoss()
    {
        if (_same)
        {
            return;
        }

        MarkupRef? sd = _src.Styles.Markup.IsValid ? Wml.Child(_src.Styles.Markup, "docDefaults"u8) : null;
        MarkupRef? dd = _dst.Styles.Markup.IsValid ? Wml.Child(_dst.Styles.Markup, "docDefaults"u8) : null;
        string a = sd is { } s1 ? MergeText.Canonical(s1.Node.OuterXml) : string.Empty;
        string b = dd is { } d1 ? MergeText.Canonical(d1.Node.OuterXml) : string.Empty;
        if (a != b)
        {
            _r.Loss.Report("merge.doc-defaults", "Document defaults", 1,
                "The source's default paragraph and character formatting differs from the destination's; copied text without its own formatting takes the destination's defaults.");
        }

        MarkupDocument? st = _src.Parts.LoadRelated(_src.MainPartName, "theme", out _);
        MarkupDocument? dt = _dst.Parts.LoadRelated(_dst.MainPartName, "theme", out _);
        if (st is not null && (dt is null || MergeText.Canonical(Encoding.UTF8.GetString(st.ToArray())) != MergeText.Canonical(Encoding.UTF8.GetString(dt.ToArray()))))
        {
            _r.Loss.Report("merge.theme", "Theme fonts and colours", 1,
                "The destination's theme applies to the copied content; theme fonts and colours of the source were not carried over.");
        }

        HashSet<string> dstFonts = FontNames(_dst);
        var missing = FontNames(_src).Where(f => !dstFonts.Contains(f)).ToList();
        if (missing.Count > 0)
        {
            _r.Loss.Report("merge.fonts", "Font table entries", missing.Count,
                "Fonts of the source that the destination's font table does not list (" + string.Join(", ", missing.Take(5)) + (missing.Count > 5 ? ", …" : string.Empty) + "); text keeps the font names and is shown in a substitute when the font is not installed.");
        }

        if (_droppedBindings > 0)
        {
            _r.Loss.Report("merge.data-binding", "Content control data bindings", _droppedBindings,
                "Content controls bound to custom XML of the source lost their binding because that XML is not part of the destination; the controls keep their text.");
        }
    }

    private static HashSet<string> FontNames(Document d)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (d.Parts.LoadRelated(d.MainPartName, "fontTable", out _) is { } doc)
        {
            for (MarkupNode n = doc.Root.FirstChild; !n.IsNull; n = n.NextSibling)
            {
                if (n.Kind == MarkupNodeKind.Element && n.LocalNameString == "font" && WAttr(n, "name") is { } name)
                {
                    names.Add(name);
                }
            }
        }

        return names;
    }

    private void FinishRevisionReport()
    {
        if (_authors.Count == 0)
        {
            return;
        }

        HashSet<string> known = _knownAuthors ?? [];
        foreach (string a in _authors.OrderBy(x => x, StringComparer.Ordinal))
        {
            if (!known.Contains(a))
            {
                _r.Resolve(new MergeResolution("merge.revision-author", a, "added", "Revisions by '" + a + "' came with the copied content; the destination had none by this author."));
            }
        }

        _r.Count("revisions", _revisionIds.Count);
    }

    private HashSet<string> DestinationAuthors()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<MarkupNode>();
        foreach (MarkupNode r in StoryRoots(_dst))
        {
            stack.Push(r);
        }

        while (stack.Count > 0)
        {
            MarkupNode n = stack.Pop();
            if (n.Kind != MarkupNodeKind.Element)
            {
                continue;
            }

            if (WAttr(n, "author") is { } au)
            {
                set.Add(au);
            }

            for (MarkupNode c = n.FirstChild; !c.IsNull; c = c.NextSibling)
            {
                stack.Push(c);
            }
        }

        return set;
    }

    // ---- ignorable namespaces ---------------------------------------------------------------------------------------------------

    // Markup-compatibility §10.1 (mc:Ignorable): a namespace a consumer may not understand must be listed in the root's Ignorable
    // attribute, with its prefix declared on the root. Copied content that uses such a namespace needs the destination root to say so.
    private void EnsureIgnorables()
    {
        if (_ignorableUsed.Count == 0)
        {
            return;
        }

        MarkupNode root = _dst.MainMarkup.Root;
        foreach (KeyValuePair<string, string> kv in _ignorableUsed)
        {
            string prefix = kv.Key;
            string uri = kv.Value;
            if (root.TryResolvePrefix(prefix, out string current))
            {
                if (current != uri)
                {
                    _r.Problem("merge.namespace-prefix-clash", "The prefix '" + prefix + "' means another namespace in the destination's root; the copied content declares its own.", DefectLocation.InPart(_dst.MainPartName));
                    continue;
                }
            }
            else
            {
                root.SetAttribute("xmlns:" + prefix, uri);
            }

            string? list = root.GetAttribute(McNs, "Ignorable");
            if (list is null)
            {
                root.SetAttribute(McNs, "Ignorable", prefix, "mc");
            }
            else if (!list.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(prefix, StringComparer.Ordinal))
            {
                root.SetAttribute(McNs, "Ignorable", list + " " + prefix);
            }
        }
    }

    internal static string Num(long n) => n.ToString(CultureInfo.InvariantCulture);
}
