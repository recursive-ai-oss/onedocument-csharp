using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Documents.Revisions;

/// <summary>
/// Where the <c>w:id</c> of each tracked change a <see cref="RevisionAuthor"/> writes comes from. Ids of revisions, bookmarks,
/// comment ranges and move ranges share one space in practice (Word reads them as such), so a source never hands out an id that
/// any of those already use in the set's parts, or one it has handed out before.
/// </summary>
/// <remarks>
/// <para>The default, <see cref="ContinueAfterMax"/>, numbers from one above the largest id in use. <see cref="StartingAt"/> and
/// <see cref="FromCallback"/> let a caller choose the numbers, for output that must be the same bytes on every run or must
/// line up with ids the caller keeps elsewhere. Either way the numbers depend only on the inputs: the library never reads the clock.</para>
/// <para>One source can serve several authors on one set; it is not safe for concurrent use.</para>
/// </remarks>
public abstract class RevisionIds
{
    private protected RevisionIds()
    {
    }

    /// <summary>Gets the default source: each id is one above the largest id in use, so ids never collide.</summary>
    public static RevisionIds ContinueAfterMax { get; } = new MaxSource();

    /// <summary>Creates a source that hands out <paramref name="first"/>, then <paramref name="first"/> + 1, and so on.</summary>
    /// <param name="first">The first id; zero or more.</param>
    /// <returns>The source. Each id is checked as it is used: one already in use in the set's parts throws <see cref="RevisionIdConflictException"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="first"/> is negative.</exception>
    public static RevisionIds StartingAt(int first)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(first);
        return new SequenceSource(first);
    }

    /// <summary>Creates a source that asks <paramref name="next"/> for every id.</summary>
    /// <param name="next">Returns the id for the next change; called once per id the author needs, in a fixed order for a given edit. An id that is negative or already in use throws <see cref="RevisionIdConflictException"/>.</param>
    /// <returns>The source.</returns>
    public static RevisionIds FromCallback(Func<int> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        return new CallbackSource(next);
    }

    /// <summary>The proposed id, or -1 to take one above the largest in use.</summary>
    internal abstract int Propose();

    private sealed class MaxSource : RevisionIds
    {
        internal override int Propose() => -1;
    }

    private sealed class SequenceSource(int first) : RevisionIds
    {
        private int _next = first;

        internal override int Propose() => _next == int.MaxValue ? throw new OverflowException("The id sequence ran past Int32.MaxValue.") : _next++;
    }

    private sealed class CallbackSource(Func<int> next) : RevisionIds
    {
        internal override int Propose() => next();
    }
}

/// <summary>
/// A tracked-change id given by the caller cannot be used: it is negative or the set's parts already use it. The change that needed
/// it was not written, but earlier steps of the same call may have split runs, so discard the document or start from a fresh copy.
/// </summary>
public sealed class RevisionIdConflictException : OfficeException
{
    internal RevisionIdConflictException(int id, bool negative)
        : base(new Defect(
            DefectKind.Reference,
            "docx.revision-id-conflict",
            DefectSeverity.Fatal,
            negative ? "A revision id must not be negative: " + id.ToString(System.Globalization.CultureInfo.InvariantCulture) + "."
                     : "The revision id " + id.ToString(System.Globalization.CultureInfo.InvariantCulture) + " is already in use.",
            DefectLocation.None))
    {
        Id = id;
    }

    /// <summary>Gets the id that could not be used.</summary>
    public int Id { get; }
}
