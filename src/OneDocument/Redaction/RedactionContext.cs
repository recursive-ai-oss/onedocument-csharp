using System.Text;
using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Redaction;

// The state one operation shares: the plan, the report, and the marks written so far.
internal sealed class RedactionContext
{
    private readonly Dictionary<string, string> _identifierMarks = new(StringComparer.Ordinal);
    private readonly List<(int Start, int Length, int Match)> _scratch = [];
    private readonly List<(int Start, int Length)> _found = [];

    public RedactionContext(RedactionPlan plan, RedactionReport report, OfficeLimits limits)
    {
        Plan = plan;
        Report = report;
        Limits = limits;
    }

    public RedactionPlan Plan { get; }

    public RedactionReport Report { get; }

    public OfficeLimits Limits { get; }

    // Every mark written; the verifier ignores matches that lie inside one (a pattern such as ".+" matches its own mark).
    public HashSet<string> Marks { get; } = new(StringComparer.Ordinal);

    public string Mark(string removed, bool identifier)
    {
        if (identifier)
        {
            if (!_identifierMarks.TryGetValue(removed, out string? mark))
            {
                mark = "REDACTED" + LetterCode(_identifierMarks.Count);
                _identifierMarks[removed] = mark;
                Marks.Add(mark);
            }

            return mark;
        }

        RedactionOptions o = Plan.Options;
        string text = o.Mark;
        if (text.Length == 0)
        {
            text = "\u2588";
        }

        if (text.Length == 1)
        {
            int n = o.HideLength ? Math.Max(1, o.HiddenLength) : removed.Length;
            text = new string(text[0], n);
        }

        Marks.Add(text);
        return text;
    }

    // The mark for a removed region, which has no text to take a length from.
    public string RegionMark() => Mark(new string('x', Math.Max(1, Plan.Options.HiddenLength)), false);

    // 0 -> A, 25 -> Z, 26 -> AA. Letters only, so a digit pattern never matches a mark.
    private static string LetterCode(int n)
    {
        var sb = new StringBuilder();
        n++;
        while (n > 0)
        {
            n--;
            sb.Insert(0, (char)('A' + (n % 26)));
            n /= 26;
        }

        return sb.ToString();
    }

    // Finds the spans of every match in the text, merged where they overlap. Returns false when there are none.
    public bool FindSpans(string input, string location, List<(int Start, int Length)> merged, bool literalOnly = false)
    {
        merged.Clear();
        _scratch.Clear();
        for (int i = 0; i < Plan.Matches.Count; i++)
        {
            _found.Clear();
            if (literalOnly && Plan.Matches[i].IsRegex)
            {
                continue;
            }

            Plan.Matches[i].FindAll(input, _found, Limits, location);
            foreach ((int s, int l) in _found)
            {
                _scratch.Add((s, l, i));
            }
        }

        if (_scratch.Count == 0)
        {
            return false;
        }

        _scratch.Sort(static (a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : b.Length.CompareTo(a.Length));
        int start = _scratch[0].Start;
        int end = start + _scratch[0].Length;
        for (int i = 1; i < _scratch.Count; i++)
        {
            if (_scratch[i].Start <= end)
            {
                end = Math.Max(end, _scratch[i].Start + _scratch[i].Length);
            }
            else
            {
                merged.Add((start, end - start));
                start = _scratch[i].Start;
                end = start + _scratch[i].Length;
            }
        }

        merged.Add((start, end - start));
        return true;
    }

    // Replaces every match in one string. Returns the number of replacements.
    public int Apply(string input, bool identifier, string location, out string result)
    {
        var spans = new List<(int Start, int Length)>();
        if (!FindSpans(input, location, spans))
        {
            result = input;
            return 0;
        }

        var sb = new StringBuilder(input.Length);
        int at = 0;
        foreach ((int s, int l) in spans)
        {
            sb.Append(input, at, s - at);
            sb.Append(Mark(input.Substring(s, l), identifier));
            at = s + l;
        }

        sb.Append(input, at, input.Length - at);
        result = sb.ToString();
        return spans.Count;
    }
}
