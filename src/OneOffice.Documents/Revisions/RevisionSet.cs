using System.Linq;
using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.Markup;

namespace RecursiveAI.OneOffice.Documents.Revisions;

/// <summary>A count of the tracked changes in a set of parts.</summary>
/// <param name="Total">All revisions.</param>
/// <param name="ByKind">Revisions per kind (kinds with none are absent).</param>
/// <param name="ByAuthor">Revisions per author, ordinal.</param>
/// <param name="ByPart">Revisions per part URI.</param>
/// <param name="Earliest">The earliest revision date, if any revision has one.</param>
/// <param name="Latest">The latest revision date, if any revision has one.</param>
public sealed record RevisionCensus(
    int Total,
    IReadOnlyDictionary<RevisionKind, int> ByKind,
    IReadOnlyDictionary<string, int> ByAuthor,
    IReadOnlyDictionary<string, int> ByPart,
    DateTimeOffset? Earliest,
    DateTimeOffset? Latest);

/// <summary>
/// The tracked changes (ECMA-376 Part 1 §17.13.5) of a set of parts: list them, accept or reject them, read the text as if all
/// were accepted or rejected, and author new ones with <see cref="CreateAuthor(string, DateTimeOffset?)"/>.
/// </summary>
/// <remarks>
/// <para>Edits go into the parts' markup trees and leave everything else in them untouched, in order, with its prefixes. A part
/// that no revision touches is not modified. After any edit the <see cref="Revisions"/> list is rebuilt, and revisions obtained
/// earlier must not be used again.</para>
/// <para>Not safe for concurrent use.</para>
/// </remarks>
public sealed class RevisionSet
{
    private readonly List<IRevisionPart> _parts;
    private readonly IDefectSink? _defects;
    private readonly List<Revision> _revisions = [];
    private int _maxId;
    private HashSet<int>? _used;
    private bool _stale;

    private RevisionSet(List<IRevisionPart> parts, IDefectSink? defects)
    {
        _parts = parts;
        _defects = defects;
    }

    /// <summary>Finds the tracked changes in the parts.</summary>
    /// <param name="parts">The parts to read, in the order their revisions should be listed.</param>
    /// <param name="context">Where defects go (a revision without author, id or a valid date); null discards them.</param>
    /// <returns>The set. Parts with no revisions are fine.</returns>
    public static RevisionSet Scan(IEnumerable<IRevisionPart> parts, OpenContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(parts);
        var set = new RevisionSet([.. parts], context?.Defects);
        set.Rebuild(report: true);
        return set;
    }

    /// <summary>Gets the parts the set covers.</summary>
    public IReadOnlyList<IRevisionPart> Parts => _parts;

    /// <summary>Gets the revisions in the order of the parts and, inside a part, of the document.</summary>
    public IReadOnlyList<Revision> Revisions => Items;

    /// <summary>Gets the largest <c>w:id</c> on a revision, bookmark, comment range or move range in the parts (0 when there is none).</summary>
    public int MaxId
    {
        get
        {
            _ = Items;
            return _maxId;
        }
    }

    private List<Revision> Items
    {
        get
        {
            if (_stale)
            {
                Rebuild(report: false);
            }

            return _revisions;
        }
    }

    /// <summary>Counts the revisions by kind, author and part.</summary>
    /// <returns>The census.</returns>
    public RevisionCensus Census()
    {
        var kinds = new Dictionary<RevisionKind, int>();
        var authors = new Dictionary<string, int>(StringComparer.Ordinal);
        var parts = new Dictionary<string, int>(StringComparer.Ordinal);
        DateTimeOffset? lo = null;
        DateTimeOffset? hi = null;
        foreach (Revision r in Items)
        {
            kinds[r.Kind] = kinds.GetValueOrDefault(r.Kind) + 1;
            authors[r.Author] = authors.GetValueOrDefault(r.Author) + 1;
            parts[r.Part.Uri] = parts.GetValueOrDefault(r.Part.Uri) + 1;
            if (r.Date is { } d)
            {
                lo = lo is null || d < lo ? d : lo;
                hi = hi is null || d > hi ? d : hi;
            }
        }

        return new RevisionCensus(Items.Count, kinds, authors, parts, lo, hi);
    }

    /// <summary>Gets the revisions by one author (ordinal comparison).</summary>
    /// <param name="author">The author name.</param>
    /// <returns>Their revisions in document order.</returns>
    public IReadOnlyList<Revision> ByAuthor(string author)
    {
        ArgumentNullException.ThrowIfNull(author);
        return [.. Items.Where(r => string.Equals(r.Author, author, StringComparison.Ordinal))];
    }

    /// <summary>Gets the revisions whose element lies from <paramref name="first"/> to <paramref name="last"/> in document order, both included, with everything below them.</summary>
    /// <param name="first">The first element of the range.</param>
    /// <param name="last">The last element of the range; it must come at or after <paramref name="first"/> in the same part.</param>
    /// <returns>The revisions in the range.</returns>
    /// <exception cref="ArgumentException">The nodes are not in one of the set's parts, or are in different parts, or are out of order.</exception>
    public IReadOnlyList<Revision> InRange(MarkupNode first, MarkupNode last)
    {
        IRevisionPart? part = _parts.FirstOrDefault(p => ReferenceEquals(p.Markup, first.Document));
        if (part is null || !ReferenceEquals(first.Document, last.Document))
        {
            throw new ArgumentException("Both nodes must belong to one part of the set.");
        }

        var byElement = new Dictionary<MarkupNode, Revision>();
        foreach (Revision r in Items)
        {
            if (ReferenceEquals(r.Part, part))
            {
                byElement[r.Element] = r;
            }
        }

        var found = new List<Revision>();
        MarkupNode root = part.Markup.Root;
        MarkupNode stop = default;
        bool inside = false;
        bool ended = false;
        for (MarkupNode n = root; !n.IsNull; n = WordXml.Next(n, root, true))
        {
            if (!inside && n == first)
            {
                inside = true;
            }

            if (inside && n == last)
            {
                stop = WordXml.Next(n, root, false);
                ended = true;
            }

            if (inside && ended && n == stop)
            {
                break;
            }

            if (inside && byElement.TryGetValue(n, out Revision? rev))
            {
                found.Add(rev);
            }
        }

        if (!inside || !ended)
        {
            throw new ArgumentException("The range is empty: the last node does not follow the first.");
        }

        return found;
    }

    /// <summary>Accepts every revision.</summary>
    /// <returns>How many revisions were accepted; those that could not be are left in place and reported as defects.</returns>
    public int AcceptAll() => ResolveAll(accept: true);

    /// <summary>Rejects every revision, restoring the document as it was before any of them.</summary>
    /// <returns>How many revisions were rejected.</returns>
    public int RejectAll() => ResolveAll(accept: false);

    /// <summary>Accepts one revision.</summary>
    /// <param name="revision">A revision from the current <see cref="Revisions"/>.</param>
    /// <returns>True when it was accepted.</returns>
    public bool Accept(Revision revision) => Resolve([revision], accept: true) == 1;

    /// <summary>Rejects one revision.</summary>
    /// <param name="revision">A revision from the current <see cref="Revisions"/>.</param>
    /// <returns>True when it was rejected.</returns>
    public bool Reject(Revision revision) => Resolve([revision], accept: false) == 1;

    /// <summary>Accepts the given revisions.</summary>
    /// <param name="revisions">Revisions from the current <see cref="Revisions"/>, for example <see cref="ByAuthor"/> or <see cref="InRange"/>.</param>
    /// <returns>How many were accepted.</returns>
    public int Accept(IEnumerable<Revision> revisions) => Resolve(revisions, accept: true);

    /// <summary>Rejects the given revisions.</summary>
    /// <param name="revisions">Revisions from the current <see cref="Revisions"/>.</param>
    /// <returns>How many were rejected.</returns>
    public int Reject(IEnumerable<Revision> revisions) => Resolve(revisions, accept: false);

    /// <summary>Reads the text of one part with every revision accepted or every one rejected. Nothing is changed. One line per paragraph; tabs and breaks as <c>\t</c> and <c>\n</c>.</summary>
    /// <param name="part">A part of the set (any part works; it does not have to be one that has revisions).</param>
    /// <param name="view">Which version to show.</param>
    /// <returns>The text.</returns>
    public static string GetText(IRevisionPart part, RevisionView view)
    {
        ArgumentNullException.ThrowIfNull(part);
        MarkupNode root = part.Markup.Root;
        return root.IsNull ? string.Empty : RevisionText.OfPart(root, view);
    }

    /// <summary>Reads one paragraph's text in a view, without the paragraph break.</summary>
    /// <param name="paragraph">A <c>w:p</c> element.</param>
    /// <param name="view">Which version to show.</param>
    /// <returns>The text.</returns>
    public static string GetParagraphText(MarkupNode paragraph, RevisionView view)
    {
        if (!WordXml.Is(paragraph, "p"))
        {
            throw new ArgumentException("The node is not a w:p element.", nameof(paragraph));
        }

        return RevisionText.OfParagraph(paragraph, view);
    }

    /// <summary>Starts authoring tracked changes in this set's parts. New ids continue after the largest in use.</summary>
    /// <param name="author">The author name written on every change.</param>
    /// <param name="date">The date written on every change, or null to leave the date out. The library never reads the clock, so the caller chooses it.</param>
    /// <returns>An author whose edits are visible in <see cref="Revisions"/> after <see cref="Refresh"/>.</returns>
    public RevisionAuthor CreateAuthor(string author, DateTimeOffset? date) => new(this, author, date, RevisionIds.ContinueAfterMax);

    /// <summary>Starts authoring tracked changes whose ids come from a source the caller chose.</summary>
    /// <param name="author">The author name written on every change.</param>
    /// <param name="date">The date written on every change, or null to leave the date out.</param>
    /// <param name="ids">Where ids come from; see <see cref="RevisionIds"/>. The ids already in use are read from the parts when the first id is needed.</param>
    /// <returns>The author.</returns>
    public RevisionAuthor CreateAuthor(string author, DateTimeOffset? date, RevisionIds ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        return new RevisionAuthor(this, author, date, ids);
    }

    /// <summary>Rescans the parts. Call it after editing a part directly or through a <see cref="RevisionAuthor"/>.</summary>
    public void Refresh()
    {
        _used = null;
        Rebuild(report: false);
    }

    internal void Invalidate() => _stale = true;

    internal int NextFreeId()
    {
        int id = checked(++_maxId);
        _used?.Add(id);
        return id;
    }

    /// <summary>Takes the next id from a source, refusing negative ones and any already in use.</summary>
    internal int Allocate(RevisionIds ids)
    {
        int proposed = ids.Propose();
        if (proposed < 0 && ReferenceEquals(ids, RevisionIds.ContinueAfterMax))
        {
            return NextFreeId();
        }

        if (proposed < 0)
        {
            throw new RevisionIdConflictException(proposed, negative: true);
        }

        _used ??= CollectUsedIds();
        if (!_used.Add(proposed))
        {
            throw new RevisionIdConflictException(proposed, negative: false);
        }

        _maxId = Math.Max(_maxId, proposed);
        return proposed;
    }

    /// <summary>Whether a move name is already used by a move range in the parts.</summary>
    internal bool MoveNameInUse(string name)
    {
        foreach (IRevisionPart p in _parts)
        {
            MarkupNode root = p.Markup.Root;
            for (MarkupNode n = root; !n.IsNull; n = WordXml.Next(n, root, true))
            {
                if (WordXml.IsAny(n, "moveFromRangeStart", "moveToRangeStart") && string.Equals(WordXml.Attr(n, "name"), name, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private HashSet<int> CollectUsedIds()
    {
        var used = new HashSet<int>();
        foreach (IRevisionPart p in _parts)
        {
            MarkupNode root = p.Markup.Root;
            if (root.IsNull)
            {
                continue;
            }

            for (MarkupNode n = root; !n.IsNull; n = WordXml.Next(n, root, true))
            {
                if (n.Kind != MarkupNodeKind.Element || !WordXml.InMain(n))
                {
                    continue;
                }

                string local = n.LocalNameString;
                bool counts = local is "bookmarkStart" or "commentRangeStart" or "moveFromRangeStart" or "moveToRangeStart" or "permStart"
                    || RevisionScanner.Classify(local, n, out _, out _);
                if (counts && WordXml.Attr(n, "id") is { } t
                    && int.TryParse(t, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int v))
                {
                    used.Add(v);
                }
            }
        }

        return used;
    }

    private void Rebuild(bool report)
    {
        _stale = false;
        _revisions.Clear();
        int max = 0;
        foreach (IRevisionPart p in _parts)
        {
            RevisionScanner.Scan(p, _revisions, ref max, report ? _defects : null);
        }

        _maxId = Math.Max(_maxId, max);
    }

    private int ResolveAll(bool accept)
    {
        int n = Resolve(Items.ToArray(), accept);
        foreach (IRevisionPart p in _parts)
        {
            MarkupNode root = p.Markup.Root;
            if (!root.IsNull)
            {
                RevisionResolver.SweepRangeMarkers(root);
            }
        }

        Rebuild(report: false);
        return n;
    }

    private int Resolve(IEnumerable<Revision> selection, bool accept)
    {
        ArgumentNullException.ThrowIfNull(selection);
        var chosen = new HashSet<Revision>(selection);
        var current = new HashSet<Revision>(Items);
        if (!chosen.IsSubsetOf(current))
        {
            throw new ArgumentException("A revision does not belong to the current list; read Revisions again after an edit.", nameof(selection));
        }

        // Observed in Word 365 (16.0.20527): accepting or rejecting one end of a move resolves the other end with it.
        var movedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (Revision r in chosen)
        {
            if (r.MoveName is { } name && r.Kind is RevisionKind.MoveFrom or RevisionKind.MoveTo)
            {
                movedNames.Add(name);
            }
        }

        if (movedNames.Count > 0)
        {
            foreach (Revision r in Items)
            {
                if (r.MoveName is { } name && r.Kind is RevisionKind.MoveFrom or RevisionKind.MoveTo && movedNames.Contains(name))
                {
                    chosen.Add(r);
                }
            }
        }

        var resolver = new RevisionResolver(_defects);
        int done = 0;
        foreach (IRevisionPart p in _parts)
        {
            List<Revision> mine = [.. Items.Where(r => ReferenceEquals(r.Part, p) && chosen.Contains(r))];
            if (mine.Count > 0)
            {
                done += resolver.ResolveBackwards(p, mine, accept);
            }
        }

        Rebuild(report: false);
        return done;
    }
}
