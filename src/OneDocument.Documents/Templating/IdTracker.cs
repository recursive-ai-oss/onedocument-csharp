using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RecursiveAI.OneDocument.Documents.Templating;

/// <summary>
/// Keeps the identifiers a template can duplicate unique. A loop that repeats a paragraph, a table row or a section repeats everything inside it:
/// bookmarks, comment anchors, footnote references, pictures and <c>w14:paraId</c>. The first copy keeps its identifiers; later copies get new ones, chosen
/// above everything already in the document so the output depends only on the template and the data.
/// </summary>
internal sealed partial class IdTracker
{
    private readonly HashSet<string> _seenBookmarkIds = [];
    private readonly HashSet<string> _seenBookmarkNames = [];
    private readonly HashSet<string> _seenComments = [];
    private readonly HashSet<string> _seenNotes = [];
    private readonly HashSet<string> _seenDrawings = [];
    private readonly HashSet<string> _seenParas = [];
    private readonly Dictionary<string, string> _bookmarkMap = [];
    private readonly Dictionary<string, string> _commentMap = [];
    private readonly Dictionary<string, string> _noteMap = [];
    private readonly Counters _c;

    private sealed class Counters
    {
        public long Bookmark;
        public long Comment;
        public long Note;
        public long Drawing;
        public uint Para;
        public readonly List<(string Old, string New)> CommentClones = [];
        public readonly List<(string Old, string New, bool Endnote)> NoteClones = [];
    }

    /// <summary>Comments that were copied: old id, new id.</summary>
    public List<(string Old, string New)> CommentClones => _c.CommentClones;

    /// <summary>Footnotes that were copied: old id, new id.</summary>
    public List<(string Old, string New, bool Endnote)> NoteClones => _c.NoteClones;

    public IdTracker(ulong seed)
    {
        _c = new Counters { Para = (uint)(0x10000000 + (seed % 0x1000000)) };
    }

    private IdTracker(Counters shared)
    {
        _c = shared;
    }

    /// <summary>
    /// A tracker that shares this one's counters and copies but has its own record of which identifiers were seen. A text box is rendered with one, so
    /// its repeats are renumbered inside the box and the paragraph that holds the box then sees each identifier for the first time.
    /// </summary>
    public IdTracker Fork() => new(_c);

    [GeneratedRegex(@"<(?<prefix>[A-Za-z_][\w.-]*:)?(?<name>bookmarkStart|bookmarkEnd|commentRangeStart|commentRangeEnd|commentReference|footnoteReference|endnoteReference|docPr|p)(?=[\s/>])[^>]*>", RegexOptions.CultureInvariant)]
    private static partial Regex ElementRegex();

    [GeneratedRegex(@"(?<lead>[\s])(?<attr>(?:[A-Za-z_][\w.-]*:)?(?:id|name|paraId))=(?<q>[""'])(?<value>[^""']*)\k<q>", RegexOptions.CultureInvariant)]
    private static partial Regex AttributeRegex();

    [GeneratedRegex(@"<(?:[A-Za-z_][\w.-]*:)?(?<name>bookmarkStart|commentRangeStart|commentReference|comment|footnoteReference|endnoteReference|footnote|endnote|docPr)(?=[\s/>])[^>]*?\b(?:[A-Za-z_][\w.-]*:)?id=[""'](?<id>-?\d+)[""']", RegexOptions.CultureInvariant)]
    private static partial Regex ExistingIdRegex();

    /// <summary>Reads the largest identifiers already in some XML, so new ones start above them.</summary>
    /// <param name="xml">A part's XML.</param>
    public void Scan(string xml)
    {
        foreach (Match m in ExistingIdRegex().Matches(xml))
        {
            if (!long.TryParse(m.Groups["id"].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long id))
            {
                continue;
            }

            switch (m.Groups["name"].Value)
            {
                case "bookmarkStart":
                    _c.Bookmark = Math.Max(_c.Bookmark, id + 1);
                    break;
                case "commentRangeStart" or "commentReference" or "comment":
                    _c.Comment = Math.Max(_c.Comment, id + 1);
                    break;
                case "docPr":
                    _c.Drawing = Math.Max(_c.Drawing, id + 1);
                    break;
                default:
                    _c.Note = Math.Max(_c.Note, id + 1);
                    break;
            }
        }
    }

    /// <summary>Raises the next comment or note identifier above ids found in the comments and notes parts.</summary>
    public void Reserve(long comment, long note)
    {
        _c.Comment = Math.Max(_c.Comment, comment);
        _c.Note = Math.Max(_c.Note, note);
    }

    /// <summary>A new picture identifier for the document.</summary>
    public string NewDrawingId()
    {
        long id = Math.Max(_c.Drawing, 1);
        _c.Drawing = id + 1;
        _seenDrawings.Add(id.ToString(CultureInfo.InvariantCulture));
        return id.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Makes the identifiers in a piece of emitted XML unique: the first use of an identifier stays, a repeat gets a new one.</summary>
    /// <param name="xml">Emitted XML (one or more paragraphs, or markup that follows them).</param>
    /// <returns>The XML, unchanged when nothing repeats.</returns>
    public string Uniquify(string xml)
    {
        if (xml.Length == 0 || xml.IndexOf('<', StringComparison.Ordinal) < 0)
        {
            return xml;
        }

        return ElementRegex().Replace(xml, m => Rewrite(m));
    }

    private string Rewrite(Match m)
    {
        string name = m.Groups["name"].Value;
        string tag = m.Value;
        return name switch
        {
            "bookmarkStart" => EditAttributes(tag, (attr, value) => attr switch
            {
                "id" => BookmarkId(value),
                "name" => BookmarkName(value),
                _ => null,
            }),
            "bookmarkEnd" => EditAttributes(tag, (attr, value) => attr == "id" ? (_bookmarkMap.TryGetValue(value, out string? mapped) ? mapped : null) : null),
            "commentRangeStart" => EditAttributes(tag, (attr, value) => attr == "id" ? CommentId(value, start: true) : null),
            "commentRangeEnd" or "commentReference" => EditAttributes(tag, (attr, value) => attr == "id" ? CommentId(value, start: false) : null),
            "footnoteReference" => EditAttributes(tag, (attr, value) => attr == "id" ? NoteId(value, endnote: false) : null),
            "endnoteReference" => EditAttributes(tag, (attr, value) => attr == "id" ? NoteId(value, endnote: true) : null),
            "docPr" => EditAttributes(tag, (attr, value) => attr == "id" ? DrawingId(value) : null),
            "p" => EditAttributes(tag, (attr, value) => attr == "paraId" ? ParaId(value) : null),
            _ => tag,
        };
    }

    private static string EditAttributes(string tag, Func<string, string, string?> edit)
    {
        return AttributeRegex().Replace(tag, m =>
        {
            string attr = m.Groups["attr"].Value;
            string local = attr[(attr.IndexOf(':', StringComparison.Ordinal) + 1)..];
            string value = m.Groups["value"].Value;
            string? replacement = edit(local, value);
            if (replacement is null || replacement == value)
            {
                return m.Value;
            }

            string q = m.Groups["q"].Value;
            return m.Groups["lead"].Value + attr + "=" + q + replacement + q;
        });
    }

    private string BookmarkId(string id)
    {
        if (_seenBookmarkIds.Add(id))
        {
            _bookmarkMap[id] = id;
            return id;
        }

        string fresh = (_c.Bookmark++).ToString(CultureInfo.InvariantCulture);
        _seenBookmarkIds.Add(fresh);
        _bookmarkMap[id] = fresh;
        return fresh;
    }

    private string BookmarkName(string name)
    {
        // ECMA-376 Part 1 §17.13.6.2: a bookmark name is unique in the document (case-insensitive in Word).
        string key = name.ToUpperInvariant();
        if (_seenBookmarkNames.Add(key))
        {
            return name;
        }

        for (int n = 2; ; n++)
        {
            string candidate = name + "_" + n.ToString(CultureInfo.InvariantCulture);
            if (_seenBookmarkNames.Add(candidate.ToUpperInvariant()))
            {
                return candidate;
            }
        }
    }

    private string CommentId(string id, bool start)
    {
        if (start)
        {
            if (_seenComments.Add(id))
            {
                _commentMap[id] = id;
                return id;
            }

            string fresh = (_c.Comment++).ToString(CultureInfo.InvariantCulture);
            _seenComments.Add(fresh);
            _commentMap[id] = fresh;
            CommentClones.Add((id, fresh));
            return fresh;
        }

        return _commentMap.TryGetValue(id, out string? mapped) ? mapped : id;
    }

    private string NoteId(string id, bool endnote)
    {
        string key = (endnote ? "e" : "f") + id;
        if (_seenNotes.Add(key))
        {
            return id;
        }

        string fresh = (_c.Note++).ToString(CultureInfo.InvariantCulture);
        _seenNotes.Add((endnote ? "e" : "f") + fresh);
        NoteClones.Add((id, fresh, endnote));
        return fresh;
    }

    private string DrawingId(string id)
    {
        if (_seenDrawings.Add(id))
        {
            return id;
        }

        return NewDrawingId();
    }

    private string ParaId(string id)
    {
        if (_seenParas.Add(id.ToUpperInvariant()))
        {
            return id;
        }

        // ECMA-376 / [MS-DOCX] 2.6.2.3: w14:paraId is an 8-digit hexadecimal number below 0x80000000.
        string fresh;
        do
        {
            fresh = (_c.Para++).ToString("X8", CultureInfo.InvariantCulture);
        }
        while (!_seenParas.Add(fresh));
        return fresh;
    }

    /// <summary>Copies a comment or footnote element with a new id; the identifiers inside it are made unique like any other emitted XML.</summary>
    /// <param name="elementXml">The element.</param>
    /// <param name="newId">The new <c>w:id</c>.</param>
    /// <returns>The copy.</returns>
    public string Reid(string elementXml, string newId)
    {
        string start = XmlSnip.StartTag(elementXml, out _);
        string rest = elementXml[start.Length..];
        start = AttributeRegex().Replace(start, m =>
        {
            string attr = m.Groups["attr"].Value;
            if (!attr.EndsWith("id", StringComparison.Ordinal) || attr.EndsWith("paraId", StringComparison.Ordinal))
            {
                return m.Value;
            }

            string q = m.Groups["q"].Value;
            return m.Groups["lead"].Value + attr + "=" + q + newId + q;
        });
        return start + Uniquify(rest);
    }
}
