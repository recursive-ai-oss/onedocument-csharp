using System.Text;
using RecursiveAI.OneOffice.Diagnostics;
using RecursiveAI.OneOffice.Documents.Styles;

namespace RecursiveAI.OneOffice.Documents.Numbering;

/// <summary>
/// Counts list levels through a story in document order and builds the label strings. Counters belong to the abstract definition, so
/// two numbering instances of one definition continue one count (Word's behaviour, which is how "continue numbering" works); an
/// instance with a start override restarts the levels it overrides when it first appears.
/// </summary>
internal sealed class ListLabeler
{
    private readonly NumberingDefinitions _numbering;
    private readonly StyleSheet _styles;
    private readonly Dictionary<long, long?[]> _counters = [];
    private readonly HashSet<long> _started = [];

    public ListLabeler(NumberingDefinitions numbering)
    {
        _numbering = numbering;
        _styles = numbering.Owner.Styles;
    }

    public IReadOnlyList<ListLabel> Run(IEnumerable<Paragraph> paragraphs)
    {
        var labels = new List<ListLabel>();
        foreach (Paragraph p in paragraphs)
        {
            if (_styles.GetNumbering(p) is not { } reference)
            {
                continue;
            }

            NumberingInstance? inst = _numbering.FindInstance(reference.NumberingId);
            if (inst is null)
            {
                _numbering.Report("docx.num-missing", DefectKind.Reference, reference.NumberingId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "Numbering instance " + reference.NumberingId + " is not defined; paragraphs that use it are not numbered.", p.Markup);
                continue;
            }

            AbstractNumberingDefinition? abs = _numbering.FindAbstract(inst.AbstractNumberingId);
            if (abs is null)
            {
                _numbering.Report("docx.num-abstract-missing", DefectKind.Reference, inst.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "Numbering instance " + inst.Id + " names abstract definition " + inst.AbstractNumberingId + ", which is not defined.", inst.Markup);
                continue;
            }

            LevelDefinition? level = _numbering.GetLevel(inst.Id, reference.Level);
            if (level is null)
            {
                _numbering.Report("docx.num-level-missing", DefectKind.Reference, inst.Id + "/" + reference.Level,
                    "Numbering instance " + inst.Id + " has no level " + reference.Level + "; the paragraph is not numbered.", p.Markup);
                continue;
            }

            // Counters are shared by every instance of the definition that holds the levels.
            long key = _numbering.ResolveLevelSource(abs).Id;
            if (!_counters.TryGetValue(key, out long?[]? counters))
            {
                counters = new long?[9];
                _counters[key] = counters;
            }

            if (_started.Add(inst.Id))
            {
                // First paragraph of this instance: levels it overrides the start of begin again.
                foreach ((int l, _, long? start) in inst.Overrides)
                {
                    if (start is not null)
                    {
                        counters[l] = null;
                    }
                }
            }

            int lv = reference.Level;
            counters[lv] = counters[lv] is { } cur ? cur + 1 : level.Start;

            // A deeper level starts again after this one unless its w:lvlRestart says otherwise.
            for (int j = lv + 1; j < 9; j++)
            {
                LevelDefinition? deeper = _numbering.GetLevel(inst.Id, j);
                long restartAfter = deeper?.RestartAfterLevel ?? j;
                if (lv < restartAfter)
                {
                    counters[j] = null;
                }
            }

            labels.Add(Build(p, inst, level, counters));
        }

        return labels;
    }

    private ListLabel Build(Paragraph p, NumberingInstance inst, LevelDefinition level, long?[] counters)
    {
        string text = level.Text;
        bool needsWord = false;
        var sb = new StringBuilder(text.Length + 4);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '%' && i + 1 < text.Length && text[i + 1] is >= '1' and <= '9')
            {
                int k = text[i + 1] - '1';
                LevelDefinition? referenced = k == level.Level ? level : _numbering.GetLevel(inst.Id, k);
                long value = counters[k] ?? referenced?.Start ?? 0;

                // §17.9.4 (isLgl): legal numbering shows the numbers of the other levels in Arabic digits.
                string? format = level.IsLegal && k != level.Level ? "decimal" : referenced?.Format;
                FormattedNumber f = NumberFormatter.Format(format, value);
                sb.Append(f.Text);
                needsWord |= f.NeedsWordOutput;
                i++;
            }
            else
            {
                sb.Append(c);
            }
        }

        if (level.IsBullet)
        {
            sb.Clear().Append(text);
        }

        return new ListLabel(p, inst.Id, level.Level, counters[level.Level] ?? level.Start, sb.ToString(), level.Suffix, level, needsWord);
    }
}
