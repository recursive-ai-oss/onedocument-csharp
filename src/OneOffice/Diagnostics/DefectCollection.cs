namespace RecursiveAI.OneOffice.Diagnostics;

/// <summary>
/// An <see cref="IDefectSink"/> that keeps the defects in the order they were reported. It holds at most <see cref="Capacity"/>
/// defects (so a hostile file cannot make the list itself the memory problem) and counts the rest in <see cref="Overflow"/>;
/// a <see cref="DefectSeverity.Fatal"/> defect is always kept. Safe for concurrent reporting; reading while other threads still
/// report sees a consistent snapshot.
/// </summary>
public sealed class DefectCollection : IDefectSink, IReadOnlyList<Defect>
{
    private readonly object _gate = new();
    private readonly List<Defect> _items = [];
    private int _overflow;

    /// <summary>Creates a collection that keeps up to <paramref name="capacity"/> defects (default <see cref="OfficeLimits.DefaultMaxDefects"/>).</summary>
    /// <param name="capacity">The most defects to keep; at least 1.</param>
    public DefectCollection(int capacity = OfficeLimits.DefaultMaxDefects)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        Capacity = capacity;
    }

    /// <summary>Gets the most defects kept; defects after that are only counted.</summary>
    public int Capacity { get; }

    /// <summary>Gets the number of defects reported after the collection was full and not kept.</summary>
    public int Overflow
    {
        get { lock (_gate) { return _overflow; } }
    }

    /// <summary>Gets a value indicating whether a <see cref="DefectSeverity.Fatal"/> defect was reported.</summary>
    public bool HasFatal
    {
        get
        {
            lock (_gate)
            {
                foreach (Defect d in _items)
                {
                    if (d.Severity == DefectSeverity.Fatal)
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }

    /// <inheritdoc />
    public int Count
    {
        get { lock (_gate) { return _items.Count; } }
    }

    /// <inheritdoc />
    public Defect this[int index]
    {
        get { lock (_gate) { return _items[index]; } }
    }

    /// <inheritdoc />
    public void Report(Defect defect)
    {
        ArgumentNullException.ThrowIfNull(defect);
        lock (_gate)
        {
            if (_items.Count < Capacity || defect.Severity == DefectSeverity.Fatal)
            {
                _items.Add(defect);
            }
            else
            {
                _overflow++;
            }
        }
    }

    /// <summary>Returns the kept defects as of now.</summary>
    /// <returns>A copy; later reports do not change it.</returns>
    public Defect[] ToArray()
    {
        lock (_gate)
        {
            return [.. _items];
        }
    }

    /// <inheritdoc />
    public IEnumerator<Defect> GetEnumerator() => ((IEnumerable<Defect>)ToArray()).GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
