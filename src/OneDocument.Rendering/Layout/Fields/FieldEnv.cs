using RecursiveAI.OneDocument.Documents;

namespace RecursiveAI.OneDocument.Layout;

// Everything the field evaluation of one layout pass reads: the document model, the facts the previous pass produced and the options.
internal sealed class FieldEnv
{
    private List<RecursiveAI.OneDocument.Documents.Numbering.ListLabel>? _labels;
    private Dictionary<Paragraph, int>? _labelIndex;
    private readonly Dictionary<string, HashSet<string>> _styleIds = new(StringComparer.OrdinalIgnoreCase);

    public required Document Doc;
    public required FieldModel Model;
    public required LayoutOptions Options;

    /// <summary>The facts of the previous pass; null on the first pass.</summary>
    public FieldFacts? Facts;

    /// <summary>Set when a field looked at the facts (or would have, on the first pass), so another pass is worth running.</summary>
    public bool UsedFacts;

    public bool Update => Options.UpdateFields;

    public IReadOnlyList<RecursiveAI.OneDocument.Documents.Numbering.ListLabel> Labels
    {
        get
        {
            if (_labels is null)
            {
                _labels = [.. Doc.Numbering.LabelBody()];
                _labelIndex = [];
                for (int i = 0; i < _labels.Count; i++)
                {
                    _labelIndex[_labels[i].Paragraph] = i;
                }
            }

            return _labels;
        }
    }

    public int LabelIndex(Paragraph p)
    {
        _ = Labels;
        return _labelIndex!.TryGetValue(p, out int i) ? i : -1;
    }

    // The style ids a STYLEREF name stands for: the id itself, or the id of the style with that name (ECMA-376 Part 1 §17.16.5.59).
    public HashSet<string> StyleIdsFor(string nameOrId)
    {
        if (_styleIds.TryGetValue(nameOrId, out HashSet<string>? cached))
        {
            return cached;
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (RecursiveAI.OneDocument.Documents.Styles.StyleDefinition s in Doc.Styles.Styles)
        {
            if (string.Equals(s.Id, nameOrId, StringComparison.OrdinalIgnoreCase) || string.Equals(s.Name, nameOrId, StringComparison.OrdinalIgnoreCase))
            {
                ids.Add(s.Id);
            }
        }

        if (ids.Count == 0)
        {
            ids.Add(nameOrId);
        }

        _styleIds[nameOrId] = ids;
        return ids;
    }
}

// Where a field sits when it is evaluated.
internal sealed class FieldSite
{
    public required LayoutContext Ctx;
    public required FieldEnv Env;
    public required Paragraph Paragraph;
    public int ParagraphIndex = -1;
    public PageFieldState? Page; // set for headers and footers
    public RecursiveAI.OneDocument.Markup.MarkupNode Begin;
    public FieldInfo? Info;
    public string Cached = string.Empty;
}
