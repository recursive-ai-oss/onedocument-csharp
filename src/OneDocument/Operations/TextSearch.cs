using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Operations;

/// <summary>How a search pattern is read. The same options serve documents, workbooks and presentations.</summary>
public sealed class SearchOptions
{
    /// <summary>Gets a value indicating whether upper and lower case differ. Default false (case-insensitive, invariant culture).</summary>
    public bool MatchCase { get; init; }

    /// <summary>
    /// Gets a value indicating whether a match must not touch a letter, digit or underscore on either side. Default false.
    /// The check looks at the characters next to the match, so it also works for a pattern that starts or ends with punctuation.
    /// </summary>
    public bool WholeWord { get; init; }

    /// <summary>
    /// Gets a value indicating whether the pattern is a .NET regular expression. Default false (the pattern is literal text).
    /// A regular expression runs with the match timeout of <see cref="OfficeLimits.RegexMatchTimeoutMilliseconds"/> and, when its
    /// constructs allow, the linear-time engine (<see cref="RegexOptions.NonBacktracking"/>); a pattern that needs backtracking
    /// (back-references, look-around) runs on the backtracking engine, bounded by the same timeout.
    /// </summary>
    public bool UseRegex { get; init; }

    /// <summary>
    /// Gets a value indicating whether the pattern and the replacement may use the word-processor special codes:
    /// <c>^t</c> tab, <c>^l</c> manual line break, <c>^s</c> no-break space, <c>^~</c> no-break hyphen, <c>^^</c> a caret, and in a
    /// replacement <c>^&amp;</c> for the found text. Default false.
    /// </summary>
    public bool SpecialCodes { get; init; }
}

/// <summary>One match of a <see cref="TextSearch"/> inside a piece of text.</summary>
/// <param name="Index">The UTF-16 offset of the first character.</param>
/// <param name="Length">The number of UTF-16 code units; never zero (empty matches are skipped).</param>
/// <param name="Value">The matched text.</param>
public sealed record TextMatch(int Index, int Length, string Value)
{
    internal Match? Source { get; init; }
}

/// <summary>
/// A compiled search over strings: literal, whole-word, case-sensitive or regular-expression, with replacement expansion. The
/// kinds' find and replace operations use it on the text a reader sees, one paragraph, cell or text body at a time.
/// </summary>
/// <remarks>
/// <para>Empty matches (a regular expression such as <c>x*</c>) are skipped: an operation never inserts text at a bare position.
/// Matches do not overlap; the search resumes after each one.</para>
/// <para>A match that runs longer than the timeout in <see cref="OfficeLimits.RegexMatchTimeoutMilliseconds"/> raises
/// <see cref="OfficeLimitException"/> with the limit name <c>RegexMatchTimeoutMilliseconds</c>; nothing is changed by the search itself.</para>
/// </remarks>
public sealed class TextSearch
{
    private readonly Regex _regex;
    private readonly bool _useRegex;
    private readonly bool _special;
    private readonly int _timeoutMs;

    private TextSearch(Regex regex, SearchOptions options, int timeoutMs)
    {
        _regex = regex;
        _useRegex = options.UseRegex;
        _special = options.SpecialCodes;
        _timeoutMs = timeoutMs;
    }

    /// <summary>Compiles a pattern.</summary>
    /// <param name="pattern">The text to find (or the regular expression); not empty.</param>
    /// <param name="options">How to read it; null means literal, case-insensitive.</param>
    /// <param name="limits">The limits supplying the regular-expression timeout; null means the defaults.</param>
    /// <returns>The compiled search.</returns>
    /// <exception cref="ArgumentException">The pattern is empty, is not a valid regular expression, or uses <c>^p</c> (a paragraph mark cannot be searched for: matches stay inside one paragraph).</exception>
    public static TextSearch Create(string pattern, SearchOptions? options = null, OfficeLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        if (pattern.Length == 0)
        {
            throw new ArgumentException("The search pattern is empty.", nameof(pattern));
        }

        options ??= new SearchOptions();
        limits ??= new OfficeLimits();
        int timeout = limits.RegexMatchTimeoutMilliseconds;
        string body = options.UseRegex ? (options.SpecialCodes ? ExpandCodes(pattern, regex: true) : pattern) : Regex.Escape(options.SpecialCodes ? ExpandCodes(pattern, regex: false) : pattern);
        if (options.WholeWord)
        {
            body = "(?<![\\p{L}\\p{N}_])(?:" + body + ")(?![\\p{L}\\p{N}_])";
        }

        RegexOptions ro = RegexOptions.CultureInvariant;
        if (!options.MatchCase)
        {
            ro |= RegexOptions.IgnoreCase;
        }

        var span = TimeSpan.FromMilliseconds(timeout);
        Regex rx;
        if (options.UseRegex && !options.WholeWord)
        {
            try
            {
                rx = new Regex(body, ro | RegexOptions.NonBacktracking, span);
            }
            catch (NotSupportedException)
            {
                rx = new Regex(body, ro, span);
            }
        }
        else
        {
            rx = new Regex(body, ro, span);
        }

        return new TextSearch(rx, options, timeout);
    }

    /// <summary>Finds every non-empty, non-overlapping match in a string, left to right.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The matches; empty when there are none.</returns>
    /// <exception cref="OfficeLimitException">A match took longer than the regular-expression timeout.</exception>
    public IReadOnlyList<TextMatch> Find(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var list = new List<TextMatch>();
        try
        {
            for (Match m = _regex.Match(text); m.Success; m = m.NextMatch())
            {
                if (m.Length > 0)
                {
                    list.Add(new TextMatch(m.Index, m.Length, m.Value) { Source = m });
                }
            }
        }
        catch (RegexMatchTimeoutException ex)
        {
            throw new OfficeLimitException(nameof(OfficeLimits.RegexMatchTimeoutMilliseconds), _timeoutMs, -1, DefectLocation.None, ex);
        }

        return list;
    }

    /// <summary>Works out the replacement text for a match.</summary>
    /// <param name="match">A match returned by <see cref="Find"/> of this search.</param>
    /// <param name="replacement">The replacement pattern. For a regular-expression search <c>$1</c>, <c>${name}</c>, <c>$&amp;</c> and <c>$$</c> are expanded; for a literal search it is used as written.</param>
    /// <returns>The text to put in place of the match.</returns>
    /// <exception cref="ArgumentException">The match is not from this search, or the replacement uses <c>^p</c>.</exception>
    public string Expand(TextMatch match, string replacement)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(replacement);
        string r = replacement;
        if (_special)
        {
            r = ExpandReplacementCodes(r, match.Value, _useRegex);
        }

        if (!_useRegex)
        {
            return r;
        }

        if (match.Source is null)
        {
            throw new ArgumentException("The match was not produced by a search.", nameof(match));
        }

        try
        {
            return match.Source.Result(r);
        }
        catch (RegexMatchTimeoutException ex)
        {
            throw new OfficeLimitException(nameof(OfficeLimits.RegexMatchTimeoutMilliseconds), _timeoutMs, -1, DefectLocation.None, ex);
        }
    }

    private static char Code(char c) => c switch
    {
        't' => '\t',
        'l' => '\n',
        's' => '\u00A0',
        '~' => '\u2011',
        _ => '\0',
    };

    // Word's special codes in a pattern: ^t ^l ^s ^~ ^^. In a regular expression they become escapes; in a literal search the character itself.
    private static string ExpandCodes(string pattern, bool regex)
    {
        var sb = new StringBuilder(pattern.Length);
        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];
            if (c != '^' || i + 1 >= pattern.Length)
            {
                sb.Append(c);
                continue;
            }

            char n = pattern[i + 1];
            if (n == 'p')
            {
                throw new ArgumentException("A paragraph mark (^p) cannot be searched for: a match stays inside one paragraph.", nameof(pattern));
            }

            char mapped = Code(n);
            if (mapped != '\0')
            {
                i++;
                if (regex)
                {
                    sb.Append("\\u").Append(((int)mapped).ToString("X4", CultureInfo.InvariantCulture));
                }
                else
                {
                    sb.Append(mapped);
                }
            }
            else if (n == '^')
            {
                i++;
                sb.Append(regex ? "\\^" : "^");
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    private static string ExpandReplacementCodes(string replacement, string found, bool regex)
    {
        var sb = new StringBuilder(replacement.Length);
        for (int i = 0; i < replacement.Length; i++)
        {
            char c = replacement[i];
            if (c != '^' || i + 1 >= replacement.Length)
            {
                sb.Append(c);
                continue;
            }

            char n = replacement[i + 1];
            if (n == 'p')
            {
                throw new ArgumentException("A paragraph mark (^p) cannot be inserted by a replacement.", nameof(replacement));
            }

            char mapped = Code(n);
            if (mapped != '\0')
            {
                i++;
                sb.Append(mapped);
            }
            else if (n == '^')
            {
                i++;
                sb.Append('^');
            }
            else if (n == '&')
            {
                i++;
                sb.Append(regex ? found.Replace("$", "$$", StringComparison.Ordinal) : found);
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
