using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Layout;

// Collects what layout computed for each field in its last pass, so a document operation can write it back as the stored result.
// A field in a header or footer is laid out once per page; when those pages disagree (PAGE) it has no single result to store.
internal sealed class FieldResultSink
{
    internal sealed class Entry
    {
        public required string Value;
        public required string Cached;
        public required string Type;
        public bool InHeaderFooter;
        public bool Ambiguous;
    }

    private readonly Dictionary<MarkupNode, Entry> _entries = [];

    public IEnumerable<KeyValuePair<MarkupNode, Entry>> Entries => _entries;

    public void Reset() => _entries.Clear();

    public void Record(MarkupNode begin, string value, string cached, string type, bool inHeaderFooter)
    {
        if (_entries.TryGetValue(begin, out Entry? known))
        {
            if (known.Value != value)
            {
                known.Ambiguous = true;
            }

            return;
        }

        _entries[begin] = new Entry { Value = value, Cached = cached, Type = type, InHeaderFooter = inHeaderFooter };
    }
}
