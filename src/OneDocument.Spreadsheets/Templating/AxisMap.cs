using RecursiveAI.OneDocument.Templating;

namespace RecursiveAI.OneDocument.Spreadsheets.Templating;

// Where the rows (or columns) of a rendered sheet came from. A row block repeats source rows, a false branch drops them, and empty source
// rows between populated ones keep their distance, so a reference to a source row has to be answered in terms of the output rows.
internal sealed class AxisMap
{
    private readonly Dictionary<int, List<int>> _instances = [];
    private readonly List<(int From, int To, int Out)> _gaps = [];
    private readonly int[] _present;

    public AxisMap(int[] presentSources)
    {
        _present = presentSources;
    }

    /// <summary>The last source index that was emitted, and where it went; empty source indexes after it keep their distance from it.</summary>
    public int TailSource { get; set; }

    public int TailOut { get; set; }

    public IReadOnlyDictionary<int, List<int>> Instances => _instances;

    public void AddInstance(int source, int output)
    {
        if (!_instances.TryGetValue(source, out List<int>? list))
        {
            _instances[source] = list = [];
        }

        list.Add(output);
    }

    /// <summary>
    /// Works out where the output index of a populated source index lands after <paramref name="previousSource"/> (0 at the start) and records
    /// where the empty indexes in between went. A source index not greater than the previous one starts another pass over a repeated block.
    /// </summary>
    public int Place(int source, int previousSource, int previousOut, bool first)
    {
        if (!first && source <= previousSource)
        {
            AddInstance(source, previousOut + 1);
            return previousOut + 1;
        }

        int cursor = previousOut + 1;
        int from = previousSource + 1;
        // Populated source indexes in the gap were removed by the template (a false branch or an empty loop): they take no output index.
        int at = Array.BinarySearch(_present, previousSource);
        at = at < 0 ? ~at : at + 1;
        while (from < source)
        {
            int nextPresent = at < _present.Length && _present[at] < source ? _present[at] : source;
            if (nextPresent > from)
            {
                _gaps.Add((from, nextPresent - 1, cursor));
                cursor += nextPresent - from;
            }

            from = nextPresent + 1;
            at++;
        }

        AddInstance(source, cursor);
        return cursor;
    }

    private bool IsPopulated(int source) => Array.BinarySearch(_present, source) >= 0;

    public int AbsentOut(int source)
    {
        foreach ((int from, int to, int o) in _gaps)
        {
            if (source >= from && source <= to)
            {
                return o + (source - from);
            }
        }

        return source > TailSource ? TailOut + (source - TailSource) : source;
    }

    public bool TryFirst(int source, out int output)
    {
        if (_instances.TryGetValue(source, out List<int>? l))
        {
            output = l[0];
            return true;
        }

        output = 0;
        return false;
    }

    /// <summary>Where a reference to one source index goes (0 when the index was removed).</summary>
    public int Single(int source)
    {
        if (TryFirst(source, out int o))
        {
            return o;
        }

        return IsPopulated(source) ? 0 : AbsentOut(source);
    }

    /// <summary>The first output index of a range starting at the source index; a removed start moves to the next index that survives.</summary>
    public int Start(int source)
    {
        if (TryFirst(source, out int o))
        {
            return o;
        }

        if (!IsPopulated(source))
        {
            return AbsentOut(source);
        }

        int at = Array.BinarySearch(_present, source);
        for (int i = at + 1; i < _present.Length; i++)
        {
            if (TryFirst(_present[i], out o))
            {
                return o;
            }
        }

        return 0;
    }

    /// <summary>The last output index of a range ending at the source index; a removed end moves back to the last index that survives.</summary>
    public int End(int source)
    {
        if (_instances.TryGetValue(source, out List<int>? l))
        {
            return l[^1];
        }

        if (!IsPopulated(source))
        {
            return AbsentOut(source);
        }

        int at = Array.BinarySearch(_present, source);
        for (int i = at - 1; i >= 0; i--)
        {
            if (_instances.TryGetValue(_present[i], out l))
            {
                return l[^1];
            }
        }

        return 0;
    }

    /// <summary>The instance of a source index nearest the owner's output index, looking back when the source comes before the owner's and forward otherwise.</summary>
    public int Near(int source, int ownerSource, int ownerOut)
    {
        if (source == ownerSource)
        {
            return ownerOut;
        }

        if (!_instances.TryGetValue(source, out List<int>? l))
        {
            return Single(source);
        }

        if (source < ownerSource)
        {
            for (int i = l.Count - 1; i >= 0; i--)
            {
                if (l[i] <= ownerOut)
                {
                    return l[i];
                }
            }

            return l[0];
        }

        foreach (int o in l)
        {
            if (o >= ownerOut)
            {
                return o;
            }
        }

        return l[^1];
    }
}
