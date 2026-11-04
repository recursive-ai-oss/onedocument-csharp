using System.Collections;
using System.Globalization;
using System.Linq;
using System.Text;
using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.Markup;
using RecursiveAI.OneOffice.Schema;

namespace RecursiveAI.OneOffice.Documents.Comments;

/// <summary>Where a comment is anchored in the body: the markers that bound the commented range and the run that carries the reference.</summary>
/// <param name="Start">The <c>w:commentRangeStart</c> element, or null when missing.</param>
/// <param name="End">The <c>w:commentRangeEnd</c> element, or null when missing.</param>
/// <param name="Reference">The <c>w:commentReference</c> element, or null when missing.</param>
public readonly record struct CommentAnchor(MarkupRef? Start, MarkupRef? End, MarkupRef? Reference);

/// <summary>
/// A comment: its author, date and content, and (through the modern comment parts when present) its paragraph id, its parent
/// comment and its resolved state.
/// </summary>
/// <remarks>
/// ECMA-376 Part 1 §17.13.4 (comments): the id ties the comment to markers in the body. [MS-DOCX] (<c>commentEx</c>):
/// a reply names its parent by the <c>w14:paraId</c> of the parent's last paragraph, and the state "done" is kept there.
/// </remarks>
public sealed class Comment : BlockContainer
{
    internal Comment(Document owner, CommentCollection collection, MarkupRef element)
        : base(owner, element)
    {
        Collection = collection;
    }

    internal CommentCollection Collection { get; }

    /// <summary>Gets the comment id (<c>w:id</c>); -1 when the attribute is missing or not a number.</summary>
    public long Id => Wml.Long(Wml.Attr(Container, "id")) ?? -1;

    /// <summary>Gets or sets the author (<c>w:author</c>); empty when absent.</summary>
    public string Author
    {
        get => Wml.Attr(Container, "author") ?? string.Empty;
        set => Container.Node.SetAttribute(Container.Node.NamespaceUri, "author", value, "w");
    }

    /// <summary>Gets or sets the author's initials (<c>w:initials</c>); null when absent.</summary>
    public string? Initials
    {
        get => Wml.Attr(Container, "initials");
        set
        {
            if (value is null)
            {
                Container.Node.RemoveAttribute(PrefixedName("initials"));
            }
            else
            {
                Container.Node.SetAttribute(Container.Node.NamespaceUri, "initials", value, "w");
            }
        }
    }

    /// <summary>Gets or sets the date (<c>w:date</c>); null when absent or not a date. Written in UTC with a trailing Z.</summary>
    public DateTimeOffset? Date
    {
        get => DateTimeOffset.TryParse(Wml.Attr(Container, "date"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset d) ? d : null;
        set
        {
            if (value is null)
            {
                Container.Node.RemoveAttribute(PrefixedName("date"));
            }
            else
            {
                Container.Node.SetAttribute(Container.Node.NamespaceUri, "date", CommentCollection.FormatDate(value.Value), "w");
            }
        }
    }

    /// <summary>Gets the text of the comment: its paragraphs' text joined by line feeds.</summary>
    public string Text => string.Join('\n', Paragraphs().Select(p => p.Text));

    /// <summary>Gets the <c>w14:paraId</c> of the comment's last paragraph, which the modern comment parts use to identify it; null when absent.</summary>
    public string? ParaId
    {
        get
        {
            MarkupNode last = default;
            for (MarkupNode n = Container.Node.FirstChild; !n.IsNull; n = n.NextSibling)
            {
                if (n.Kind == MarkupNodeKind.Element && n.LocalNameString == "p")
                {
                    last = n;
                }
            }

            return last.IsNull ? null : last.GetAttribute(CommentCollection.Word2010Namespace, "paraId");
        }
    }

    /// <summary>Gets the comment this one replies to, or null for a top-level comment (or when the extended part is missing).</summary>
    public Comment? Parent => Collection.ParentOf(this);

    /// <summary>Gets a value indicating whether this comment replies to another.</summary>
    public bool IsReply => Parent is not null;

    /// <summary>Gets the replies to this comment in part order.</summary>
    public IReadOnlyList<Comment> Replies => Collection.RepliesOf(this);

    /// <summary>Gets or sets the "resolved" state kept in the extended comments part. Setting it creates the entry (and the part) when needed.</summary>
    public bool IsDone
    {
        get => Collection.GetDone(this);
        set => Collection.SetDone(this, value);
    }

    /// <summary>Gets where the comment is anchored in the body. Missing or unmatched markers are reported once (<c>docx.comment-range-unmatched</c>).</summary>
    public CommentAnchor Anchor => Collection.AnchorOf(this);

    /// <summary>
    /// Replaces the content with the given text: one paragraph per line, the first starting with the annotation mark. The
    /// comment keeps its paragraph id, so replies and the extended parts stay linked.
    /// </summary>
    /// <param name="text">The new text.</param>
    /// <exception cref="ArgumentException">The text holds a character XML cannot carry.</exception>
    public void SetText(string text) => Collection.ReplaceContent(this, text);

    private string PrefixedName(string local)
    {
        Container.Node.TryGetPrefixInScope(Container.Node.NamespaceUri, out string p);
        return p + ":" + local;
    }
}

/// <summary>
/// The comments of a document with the modern comment parts kept consistent. A document without comments has an empty
/// collection; adding the first comment creates <c>/word/comments.xml</c>. Parts of the modern set that exist
/// (<c>commentsExtended</c>, <c>commentsIds</c>, <c>commentsExtensible</c>, <c>people</c>) get an entry for every comment added and
/// lose it when the comment is removed; replies create <c>commentsExtended</c> because that is where the parent link lives.
/// </summary>
public sealed class CommentCollection : IReadOnlyList<Comment>
{
    internal const string Word2010Namespace = "http://schemas.microsoft.com/office/word/2010/wordml";
    internal const string Word2012Namespace = "http://schemas.microsoft.com/office/word/2012/wordml";
    internal const string CommentIdNamespace = "http://schemas.microsoft.com/office/word/2016/wordml/cid";
    internal const string CommentExtensibleNamespace = "http://schemas.microsoft.com/office/word/2018/wordml/cex";
    internal const string CommentsContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.comments+xml";

    private static readonly PartKind ExtendedKind = new("commentsExtended", "http://schemas.microsoft.com/office/2011/relationships/commentsExtended", "application/vnd.openxmlformats-officedocument.wordprocessingml.commentsExtended+xml", "w15", Word2012Namespace, "commentsEx");
    private static readonly PartKind IdsKind = new("commentsIds", "http://schemas.microsoft.com/office/2016/09/relationships/commentsIds", "application/vnd.openxmlformats-officedocument.wordprocessingml.commentsIds+xml", "w16cid", CommentIdNamespace, "commentsIds");
    private static readonly PartKind ExtensibleKind = new("commentsExtensible", "http://schemas.microsoft.com/office/2018/08/relationships/commentsExtensible", "application/vnd.openxmlformats-officedocument.wordprocessingml.commentsExtensible+xml", "w16cex", CommentExtensibleNamespace, "commentsExtensible");
    private static readonly PartKind PeopleKind = new("people", "http://schemas.microsoft.com/office/2011/relationships/people", "application/vnd.openxmlformats-officedocument.wordprocessingml.people+xml", "w15", Word2012Namespace, "people");

    private readonly Document _owner;
    private List<Comment>? _items;
    private MarkupRef _root;
    private Dictionary<long, CommentAnchor>? _anchors;

    internal CommentCollection(Document owner)
    {
        _owner = owner;
    }

    private sealed record PartKind(string Name, string Relationship, string ContentType, string Prefix, string Namespace, string Root);

    /// <summary>Gets the number of comments, replies included.</summary>
    public int Count => Items.Count;

    /// <summary>Gets a comment by position in the part.</summary>
    /// <param name="index">Zero-based position.</param>
    public Comment this[int index] => Items[index];

    /// <summary>Finds a comment by id; with duplicate ids (<c>docx.comment-duplicate-id</c>) the first wins.</summary>
    /// <param name="id">The comment id.</param>
    /// <returns>The comment or null.</returns>
    public Comment? Find(long id) => Items.FirstOrDefault(c => c.Id == id);

    /// <summary>Lists the top-level comments (those that are not replies).</summary>
    /// <returns>The comments in part order.</returns>
    public IReadOnlyList<Comment> TopLevel() => Items.Where(c => c.Parent is null).ToList();

    /// <inheritdoc/>
    public IEnumerator<Comment> GetEnumerator() => Items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal static string FormatDate(DateTimeOffset d) => d.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private List<Comment> Items
    {
        get
        {
            if (_items is not null)
            {
                return _items;
            }

            _items = [];
            MarkupDocument? doc = _owner.LoadRelatedByType(_owner.OfficeRelationship("comments"), out string? name);
            if (doc is null)
            {
                return _items;
            }

            _root = new MarkupRef(doc.Root);
            var seen = new HashSet<long>();
            foreach (MarkupRef e in Wml.Elements(_root))
            {
                if (!Wml.Is(e, "comment"u8))
                {
                    continue;
                }

                var comment = new Comment(_owner, this, e);
                if (!seen.Add(comment.Id))
                {
                    _owner.Context.Defects.Report(new Defect(DefectKind.Reference, "docx.comment-duplicate-id", DefectSeverity.Repaired,
                        "Two comments have the id " + comment.Id.ToString(CultureInfo.InvariantCulture) + "; the first is used.",
                        DefectLocation.InPart(name!, e.Node.SourceStart)));
                }

                _items.Add(comment);
            }

            return _items;
        }
    }

    // ---- extended data -------------------------------------------------------------------------------------------

    private MarkupDocument? Part(PartKind kind) => _owner.LoadRelatedByType(kind.Relationship, out _);

    private static MarkupNode EntryByParaId(MarkupDocument? doc, string entryName, string ns, string? paraId)
    {
        if (doc is null || paraId is null)
        {
            return default;
        }

        for (MarkupNode n = doc.Root.FirstChild; !n.IsNull; n = n.NextSibling)
        {
            if (n.Kind == MarkupNodeKind.Element && n.LocalNameString == entryName && n.GetAttribute(ns, "paraId") == paraId)
            {
                return n;
            }
        }

        return default;
    }

    internal Comment? ParentOf(Comment c)
    {
        string? parentPara = EntryByParaId(Part(ExtendedKind), "commentEx", Word2012Namespace, c.ParaId) is { IsNull: false } e ? e.GetAttribute(Word2012Namespace, "paraIdParent") : null;
        return parentPara is null ? null : Items.FirstOrDefault(x => x.ParaId == parentPara && x != c);
    }

    internal IReadOnlyList<Comment> RepliesOf(Comment c) => Items.Where(x => x.Parent == c).ToList();

    internal bool GetDone(Comment c)
    {
        MarkupNode e = EntryByParaId(Part(ExtendedKind), "commentEx", Word2012Namespace, c.ParaId);
        return !e.IsNull && (Wml.OnOff(e.GetAttribute(Word2012Namespace, "done")) ?? false);
    }

    internal void SetDone(Comment c, bool done)
    {
        string para = c.ParaId ?? EnsureParaId(c);
        MarkupNode e = EntryByParaId(Part(ExtendedKind), "commentEx", Word2012Namespace, para);
        if (e.IsNull)
        {
            AddExtendedEntry(para, null, done);
        }
        else
        {
            e.SetAttribute(Word2012Namespace, "done", done ? "1" : "0", "w15");
        }
    }

    internal CommentAnchor AnchorOf(Comment c)
    {
        _anchors ??= BuildAnchors();
        return _anchors.TryGetValue(c.Id, out CommentAnchor a) ? a : default;
    }

    private Dictionary<long, CommentAnchor> BuildAnchors()
    {
        var map = new Dictionary<long, CommentAnchor>();
        Body body = _owner.Body;
        if (!body.Container.IsValid)
        {
            return map;
        }

        foreach (MarkupNode n in HubXml.FindWordElements(body.Container.Node, "commentRangeStart", "commentRangeEnd", "commentReference"))
        {
            long id = Wml.Long(n.GetAttribute(n.NamespaceUri, "id")) ?? -1;
            map.TryGetValue(id, out CommentAnchor a);
            var r = new MarkupRef(n);
            a = n.LocalNameString switch
            {
                "commentRangeStart" => a with { Start = a.Start ?? r },
                "commentRangeEnd" => a with { End = a.End ?? r },
                _ => a with { Reference = a.Reference ?? r },
            };
            map[id] = a;
        }

        foreach ((long id, CommentAnchor a) in map)
        {
            MarkupNode at = (a.Start ?? a.End ?? a.Reference)!.Value.Node;
            if (Find(id) is null)
            {
                _owner.Context.Defects.Report(new Defect(DefectKind.Reference, "docx.comment-missing", DefectSeverity.Repaired,
                    "A comment marker names id " + id.ToString(CultureInfo.InvariantCulture) + ", which the comments part does not have.",
                    DefectLocation.InPart(_owner.MainPartName, at.SourceStart)));
            }
            else if ((a.Start is null) != (a.End is null))
            {
                _owner.Context.Defects.Report(new Defect(DefectKind.Structure, "docx.comment-range-unmatched", DefectSeverity.Repaired,
                    "The commented range of comment " + id.ToString(CultureInfo.InvariantCulture) + " has a " + (a.Start is null ? "commentRangeEnd" : "commentRangeStart") + " without its partner.",
                    DefectLocation.InPart(_owner.MainPartName, at.SourceStart)));
            }
        }

        return map;
    }

    // ---- editing -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Adds a comment on the range from <paramref name="first"/> to <paramref name="last"/> (runs of the main document part, in
    /// order, in the same or different paragraphs). The range markers go before the first run and after the last, and the
    /// reference run right after the end marker.
    /// </summary>
    /// <param name="first">The first commented run. It should not sit inside a field's code (between its begin and separator characters): the markers go beside it as written.</param>
    /// <param name="last">The last commented run; may be the same run.</param>
    /// <param name="author">The author's name.</param>
    /// <param name="text">The comment text; line feeds start new paragraphs.</param>
    /// <param name="initials">The author's initials, or null.</param>
    /// <param name="date">The date to record, or null to record none (the library never reads the clock).</param>
    /// <returns>The new comment. Its id is one more than the highest in use.</returns>
    /// <exception cref="ArgumentException">A text holds a character XML cannot carry.</exception>
    public Comment Add(Run first, Run last, string author, string text, string? initials = null, DateTimeOffset? date = null)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(last);
        Comment comment = CreateComment(author, text, initials, date, null);
        long id = comment.Id;
        string idText = id.ToString(CultureInfo.InvariantCulture);
        string p = HubXml.Prefix(first.Markup.Node, _owner.WordprocessingNamespaceUri, "w", out string decl);
        last.Markup.Node.InsertXmlAfter(HubXml.Bytes(ReferenceRun(last.Markup.Node, idText, p, decl, true)));
        first.Markup.Node.InsertXmlBefore(HubXml.Bytes("<" + p + ":commentRangeStart" + decl + " " + p + ":id=\"" + idText + "\"/>"));
        _anchors = null;
        return comment;
    }

    /// <summary>
    /// Adds a reply to a comment. The reply is anchored on the same range as the comment it answers, and the parent link is
    /// written to the extended comments part (created when missing). A reply to a reply goes to the top-level comment.
    /// </summary>
    /// <param name="parent">The comment answered.</param>
    /// <param name="author">The author's name.</param>
    /// <param name="text">The reply text.</param>
    /// <param name="initials">The author's initials, or null.</param>
    /// <param name="date">The date to record, or null for none.</param>
    /// <returns>The new reply.</returns>
    public Comment Reply(Comment parent, string author, string text, string? initials = null, DateTimeOffset? date = null)
    {
        ArgumentNullException.ThrowIfNull(parent);
        Comment top = parent;
        for (Comment? up = top.Parent; up is not null; up = top.Parent)
        {
            top = up;
        }

        string parentPara = top.ParaId ?? EnsureParaId(top);
        Comment reply = CreateComment(author, text, initials, date, parentPara);
        string idText = reply.Id.ToString(CultureInfo.InvariantCulture);
        CommentAnchor a = top.Anchor;
        if (a.Reference is { } reference)
        {
            // Word's order: the reply's markers follow the parent's, its reference follows the parent's reference.
            MarkupNode refRun = reference.Node.Parent;
            string p = HubXml.Prefix(refRun, _owner.WordprocessingNamespaceUri, "w", out string decl);
            if (a.Start is { } start)
            {
                start.Node.InsertXmlAfter(HubXml.Bytes("<" + p + ":commentRangeStart" + decl + " " + p + ":id=\"" + idText + "\"/>"));
            }

            refRun.InsertXmlAfter(HubXml.Bytes(ReferenceRun(refRun, idText, p, decl, true)));
        }

        _anchors = null;
        return reply;
    }

    /// <summary>
    /// Removes a comment, its replies, its markers and reference runs in the body, and its entries in the modern comment parts.
    /// </summary>
    /// <param name="comment">A comment of this collection.</param>
    /// <returns>True when the comment was in the collection.</returns>
    public bool Remove(Comment comment)
    {
        ArgumentNullException.ThrowIfNull(comment);
        if (!Items.Contains(comment))
        {
            return false;
        }

        foreach (Comment reply in comment.Replies.ToList())
        {
            Remove(reply);
        }

        string? para = comment.ParaId;
        foreach (MarkupNode n in HubXml.FindWordElements(_owner.Body.Container.IsValid ? _owner.Body.Container.Node : default, "commentRangeStart", "commentRangeEnd", "commentReference"))
        {
            if ((Wml.Long(n.GetAttribute(n.NamespaceUri, "id")) ?? -1) != comment.Id)
            {
                continue;
            }

            MarkupNode run = n.Parent;
            bool onlyThis = n.LocalNameString == "commentReference" && run.LocalNameString == "r" && run.ChildElements.All(c => c == n || c.LocalNameString == "rPr");
            (onlyThis ? run : n).Remove();
        }

        string? durable = null;
        MarkupNode idEntry = EntryByParaId(Part(IdsKind), "commentId", CommentIdNamespace, para);
        if (!idEntry.IsNull)
        {
            durable = idEntry.GetAttribute(CommentIdNamespace, "durableId");
            idEntry.Remove();
        }

        MarkupNode extended = EntryByParaId(Part(ExtendedKind), "commentEx", Word2012Namespace, para);
        if (!extended.IsNull)
        {
            extended.Remove();
        }

        if (durable is not null && Part(ExtensibleKind) is { } cex)
        {
            for (MarkupNode n = cex.Root.FirstChild; !n.IsNull; n = n.NextSibling)
            {
                if (n.Kind == MarkupNodeKind.Element && n.LocalNameString == "commentExtensible" && n.GetAttribute(CommentExtensibleNamespace, "durableId") == durable)
                {
                    n.Remove();
                    break;
                }
            }
        }

        comment.Container.Node.Remove();
        Items.Remove(comment);
        _anchors = null;
        return true;
    }

    internal void ReplaceContent(Comment comment, string text)
    {
        string? para = comment.ParaId;
        MarkupNode root = comment.Container.Node;
        var doomed = new List<MarkupNode>();
        for (MarkupNode n = root.FirstChild; !n.IsNull; n = n.NextSibling)
        {
            if (n.Kind == MarkupNodeKind.Element)
            {
                doomed.Add(n);
            }
        }

        foreach (MarkupNode n in doomed)
        {
            n.Remove();
        }

        string p = HubXml.Prefix(root, _owner.WordprocessingNamespaceUri, "w", out string decl);
        root.AppendXml(HubXml.Bytes(Paragraphs(p, decl, text, para, root)));
    }

    private string Paragraphs(string p, string decl, string text, string? lastParaId, MarkupNode context)
    {
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        string w14 = HubXml.Prefix(context, Word2010Namespace, "w14", out string w14Decl);
        string textStyle = _owner.Styles.Find("CommentText") is not null ? "<" + p + ":pPr><" + p + ":pStyle " + p + ":val=\"CommentText\"/></" + p + ":pPr>" : string.Empty;
        string refRPr = _owner.Styles.Find("CommentReference") is not null ? "<" + p + ":rPr><" + p + ":rStyle " + p + ":val=\"CommentReference\"/></" + p + ":rPr>" : string.Empty;
        var sb = new StringBuilder();
        for (int i = 0; i < lines.Length; i++)
        {
            bool isLast = i == lines.Length - 1;
            sb.Append('<').Append(p).Append(":p");
            if (i == 0)
            {
                sb.Append(decl);
            }

            if (isLast && lastParaId is not null)
            {
                sb.Append(w14Decl).Append(' ').Append(w14).Append(":paraId=\"").Append(lastParaId).Append('"');
            }

            sb.Append('>').Append(textStyle);
            if (i == 0)
            {
                sb.Append('<').Append(p).Append(":r>").Append(refRPr).Append('<').Append(p).Append(":annotationRef/></").Append(p).Append(":r>");
            }

            if (lines[i].Length > 0)
            {
                sb.Append('<').Append(p).Append(":r>").Append(HubXml.RunContent(p, lines[i])).Append("</").Append(p).Append(":r>");
            }

            sb.Append("</").Append(p).Append(":p>");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Makes sure the part root declares w14 and lists it in mc:Ignorable, so a reader that does not know the paragraph id
    /// attribute (and a schema validator) skips it. ECMA-376 Part 3 (markup compatibility): Ignorable names namespace prefixes.
    /// </summary>
    private static void EnsureW14Ignorable(MarkupNode root)
    {
        const string mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";
        if (!root.TryGetPrefixInScope(Word2010Namespace, out string w14))
        {
            root.SetAttribute("xmlns:w14", Word2010Namespace);
            w14 = "w14";
        }

        string? current = root.GetAttribute(mc, "Ignorable");
        if (current is null || !current.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(w14, StringComparer.Ordinal))
        {
            root.SetAttribute(mc, "Ignorable", string.IsNullOrWhiteSpace(current) ? w14 : current.Trim() + " " + w14, "mc");
        }
    }

    private Comment CreateComment(string author, string text, string? initials, DateTimeOffset? date, string? parentPara)
    {
        ArgumentNullException.ThrowIfNull(author);
        ArgumentNullException.ThrowIfNull(text);
        EnsureCommentsPart();
        long id = Items.Count == 0 ? 0 : Items.Max(c => c.Id) + 1;
        MarkupNode root = _root.Node;
        EnsureW14Ignorable(root);
        string p = HubXml.Prefix(root, _owner.WordprocessingNamespaceUri, "w", out string decl);
        string paraId = NewHexId(0x10000000u + (uint)(id & 0xFFFF) * 0x1001u, UsedParaIds());
        string attrs = " " + p + ":id=\"" + id.ToString(CultureInfo.InvariantCulture) + "\" " + p + ":author=\"" + HubXml.Escape(author) + "\""
            + (date is null ? string.Empty : " " + p + ":date=\"" + FormatDate(date.Value) + "\"")
            + (initials is null ? string.Empty : " " + p + ":initials=\"" + HubXml.Escape(initials) + "\"");
        string body = Paragraphs(p, string.Empty, text, paraId, root);
        root.AppendXml(HubXml.Bytes("<" + p + ":comment" + decl + attrs + ">" + body + "</" + p + ":comment>"));
        MarkupNode added = root.LastChild;
        while (!added.IsNull && added.Kind != MarkupNodeKind.Element)
        {
            added = added.PreviousSibling;
        }

        var comment = new Comment(_owner, this, new MarkupRef(added));
        Items.Add(comment);

        if (parentPara is not null || Part(ExtendedKind) is not null)
        {
            AddExtendedEntry(paraId, parentPara, false);
        }

        if (Part(IdsKind) is { } ids)
        {
            string durable = NewHexId(0x2B000000u + (uint)(id & 0xFFFF) * 0x1003u, UsedDurableIds());
            AppendEntry(ids, IdsKind, "commentId", " {0}:paraId=\"" + paraId + "\" {0}:durableId=\"" + durable + "\"");
            if (Part(ExtensibleKind) is { } cex)
            {
                AppendEntry(cex, ExtensibleKind, "commentExtensible", " {0}:durableId=\"" + durable + "\"" + (date is null ? string.Empty : " {0}:dateUtc=\"" + FormatDate(date.Value) + "\""));
            }
        }

        if (Part(PeopleKind) is { } people && !PersonExists(people, author))
        {
            string pp = HubXml.Prefix(people.Root, Word2012Namespace, "w15", out string pdecl);
            people.Root.AppendXml(HubXml.Bytes("<" + pp + ":person" + pdecl + " " + pp + ":author=\"" + HubXml.Escape(author) + "\"><" + pp + ":presenceInfo " + pp + ":providerId=\"None\" " + pp + ":userId=\"" + HubXml.Escape(author) + "\"/></" + pp + ":person>"));
        }

        return comment;
    }

    private static bool PersonExists(MarkupDocument people, string author)
    {
        for (MarkupNode n = people.Root.FirstChild; !n.IsNull; n = n.NextSibling)
        {
            if (n.Kind == MarkupNodeKind.Element && n.LocalNameString == "person" && n.GetAttribute(Word2012Namespace, "author") == author)
            {
                return true;
            }
        }

        return false;
    }

    private static void AppendEntry(MarkupDocument doc, PartKind kind, string element, string attributeTemplate)
    {
        string px = HubXml.Prefix(doc.Root, kind.Namespace, kind.Prefix, out string decl);
        doc.Root.AppendXml(HubXml.Bytes("<" + px + ":" + element + decl + attributeTemplate.Replace("{0}", px, StringComparison.Ordinal) + "/>"));
    }

    private void AddExtendedEntry(string paraId, string? parentPara, bool done)
    {
        MarkupDocument doc = Part(ExtendedKind) ?? CreateModernPart(ExtendedKind);
        AppendEntry(doc, ExtendedKind, "commentEx", " {0}:paraId=\"" + paraId + "\"" + (parentPara is null ? string.Empty : " {0}:paraIdParent=\"" + parentPara + "\"") + " {0}:done=\"" + (done ? "1" : "0") + "\"");
    }

    private MarkupDocument CreateModernPart(PartKind kind)
    {
        string xml = "<" + kind.Prefix + ":" + kind.Root + " xmlns:" + kind.Prefix + "=\"" + kind.Namespace + "\"/>";
        return _owner.CreatePart(kind.Relationship, "/word/" + kind.Name + ".xml", kind.ContentType, xml, out _);
    }

    private string EnsureParaId(Comment c)
    {
        // A comment from an older writer has no paragraph id; the last paragraph gets one so the extended data can refer to it.
        MarkupNode last = default;
        for (MarkupNode n = c.Container.Node.FirstChild; !n.IsNull; n = n.NextSibling)
        {
            if (n.Kind == MarkupNodeKind.Element && n.LocalNameString == "p")
            {
                last = n;
            }
        }

        if (last.IsNull)
        {
            throw new InvalidOperationException("The comment has no paragraph to carry an id.");
        }

        string id = NewHexId(0x10000000u + (uint)(c.Id & 0xFFFF) * 0x1001u, UsedParaIds());
        EnsureW14Ignorable(_root.Node);
        last.SetAttribute(Word2010Namespace, "paraId", id, "w14");
        return id;
    }

    private HashSet<string> UsedParaIds()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Collect(MarkupNode root)
        {
            foreach (MarkupNode n in HubXml.FindWordElements(root, "p"))
            {
                if (n.GetAttribute(Word2010Namespace, "paraId") is { } v)
                {
                    used.Add(v);
                }
            }
        }

        if (_owner.Body.Container.IsValid)
        {
            Collect(_owner.Body.Container.Node);
        }

        if (_root.IsValid)
        {
            Collect(_root.Node);
        }

        return used;
    }

    private HashSet<string> UsedDurableIds()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Part(IdsKind) is { } ids)
        {
            for (MarkupNode n = ids.Root.FirstChild; !n.IsNull; n = n.NextSibling)
            {
                if (n.Kind == MarkupNodeKind.Element && n.GetAttribute(CommentIdNamespace, "durableId") is { } v)
                {
                    used.Add(v);
                }
            }
        }

        return used;
    }

    /// <summary>A deterministic 8-digit hex id below 0x7FFFFFFF (the range ST_LongHexNumber ids for paragraphs allow) that is not in use.</summary>
    private static string NewHexId(uint seed, HashSet<string> used)
    {
        uint v = seed % 0x7FFFFFFFu;
        while (true)
        {
            string s = v.ToString("X8", CultureInfo.InvariantCulture);
            if (v != 0 && used.Add(s))
            {
                return s;
            }

            v = (v + 1) % 0x7FFFFFFFu;
        }
    }

    private static string ReferenceRun(MarkupNode context, string idText, string p, string decl, bool declare)
    {
        _ = context;
        return "<" + p + ":commentRangeEnd" + (declare ? decl : string.Empty) + " " + p + ":id=\"" + idText + "\"/><" + p + ":r><" + p + ":commentReference " + p + ":id=\"" + idText + "\"/></" + p + ":r>";
    }

    private void EnsureCommentsPart()
    {
        _ = Items;
        if (_root.IsValid)
        {
            return;
        }

        string ns = _owner.WordprocessingNamespaceUri;
        MarkupDocument doc = _owner.CreatePart(
            _owner.OfficeRelationship("comments"),
            "/word/comments.xml",
            CommentsContentType,
            "<w:comments xmlns:w=\"" + ns + "\" xmlns:w14=\"" + Word2010Namespace + "\" xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" mc:Ignorable=\"w14\"/>",
            out _);
        _root = new MarkupRef(doc.Root);
        _items = [];
    }
}
