using System.Text.RegularExpressions;
using RecursiveAI.OneDocument.Diagnostics;

namespace RecursiveAI.OneDocument.Redaction;

/// <summary>
/// One thing to find and remove: a literal string or a regular expression. Matching runs over the text a reader sees, so a match
/// may span several runs of one paragraph, and over every other place the text can hide (attributes, field codes, caches, properties).
/// </summary>
public sealed class RedactionMatch
{
    private readonly string _pattern;
    private readonly bool _regex;
    private readonly bool _ignoreCase;
    private Regex? _compiled;
    private int _compiledTimeout;

    private RedactionMatch(string pattern, bool regex, bool ignoreCase)
    {
        _pattern = pattern;
        _regex = regex;
        _ignoreCase = ignoreCase;
    }

    /// <summary>Creates a match for a literal string, compared ordinally.</summary>
    /// <param name="text">The text to remove; must not be empty.</param>
    /// <param name="ignoreCase">Compare ignoring case (invariant culture).</param>
    /// <returns>The match.</returns>
    /// <exception cref="ArgumentException"><paramref name="text"/> is empty.</exception>
    public static RedactionMatch Literal(string text, bool ignoreCase = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        return new RedactionMatch(text, false, ignoreCase);
    }

    /// <summary>Creates a match for a regular expression (.NET syntax, culture-invariant, matched with the limits' timeout).</summary>
    /// <param name="pattern">The pattern. A pattern that can match the empty string is allowed; empty matches are ignored.</param>
    /// <param name="ignoreCase">Match ignoring case.</param>
    /// <returns>The match.</returns>
    /// <exception cref="ArgumentException">The pattern is empty or not a valid regular expression.</exception>
    public static RedactionMatch Pattern(string pattern, bool ignoreCase = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(pattern);
        var match = new RedactionMatch(pattern, true, ignoreCase);
        _ = match.GetRegex(OfficeLimits.DefaultRegexMatchTimeoutMilliseconds);
        return match;
    }

    /// <summary>Gets a value that says whether this is a regular expression.</summary>
    public bool IsRegex => _regex;

    /// <summary>Gets a value that says whether the comparison ignores case.</summary>
    public bool IgnoreCase => _ignoreCase;

    internal string PatternText => _pattern;

    private Regex GetRegex(int timeoutMilliseconds)
    {
        if (_compiled is null || _compiledTimeout != timeoutMilliseconds)
        {
            RegexOptions options = RegexOptions.CultureInvariant | (_ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
            _compiled = new Regex(_pattern, options, TimeSpan.FromMilliseconds(timeoutMilliseconds));
            _compiledTimeout = timeoutMilliseconds;
        }

        return _compiled;
    }

    // Appends every non-empty, non-overlapping match in the input as (start, length).
    internal void FindAll(string input, List<(int Start, int Length)> spans, OfficeLimits limits, string location)
    {
        if (input.Length == 0)
        {
            return;
        }

        if (!_regex)
        {
            StringComparison comparison = _ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            int from = 0;
            while (from <= input.Length - _pattern.Length)
            {
                int at = input.IndexOf(_pattern, from, comparison);
                if (at < 0)
                {
                    break;
                }

                spans.Add((at, _pattern.Length));
                from = at + _pattern.Length;
            }

            return;
        }

        try
        {
            foreach (ValueMatch m in GetRegex(limits.RegexMatchTimeoutMilliseconds).EnumerateMatches(input))
            {
                if (m.Length > 0)
                {
                    spans.Add((m.Index, m.Length));
                }
            }
        }
        catch (RegexMatchTimeoutException ex)
        {
            throw new OfficeLimitException(nameof(OfficeLimits.RegexMatchTimeoutMilliseconds), limits.RegexMatchTimeoutMilliseconds, -1, DefectLocation.InPart(location), ex);
        }
    }
}

/// <summary>How the removed text is marked and what else the operation does.</summary>
public sealed class RedactionOptions
{
    /// <summary>
    /// Gets the replacement for removed text. A single character is repeated once per removed character (the default, <c>█</c>, which also
    /// renders as a black box); a longer string replaces the span as written. Names that must stay valid identifiers (bookmarks, defined
    /// names, sheet and table names, field codes, formulas) always get a letters-only mark, <c>REDACTED</c> plus a letter code that is
    /// the same for the same removed text, so references stay consistent.
    /// </summary>
    public string Mark { get; init; } = "\u2588";

    /// <summary>Gets a value that says whether a one-character <see cref="Mark"/> is a fixed length (<see cref="HiddenLength"/>) instead of the removed length. Default false.</summary>
    public bool HideLength { get; init; }

    /// <summary>Gets the length used when <see cref="HideLength"/> is set. Default 8.</summary>
    public int HiddenLength { get; init; } = 8;

    /// <summary>Gets a value that says whether the package thumbnail is dropped (it is a raster of the content). Default true.</summary>
    public bool DropThumbnails { get; init; } = true;

    /// <summary>Gets a value that says whether the written file is re-opened and searched for survivors. Default true.</summary>
    public bool Verify { get; init; } = true;
}

/// <summary>An area to remove regardless of its text. Kinds: <see cref="ParagraphRegion"/>, <see cref="CellRangeRegion"/>, <see cref="ShapeRegion"/>, <see cref="SlideRegion"/>.</summary>
public abstract class RedactionRegion
{
    private protected RedactionRegion()
    {
    }
}

/// <summary>Paragraphs of a word-processing document, counted from zero in document order over every paragraph of the main story (tables included).</summary>
public sealed class ParagraphRegion : RedactionRegion
{
    /// <summary>Creates a region.</summary>
    /// <param name="first">The first paragraph, from zero.</param>
    /// <param name="last">The last paragraph, inclusive.</param>
    /// <exception cref="ArgumentOutOfRangeException">The range is negative or reversed.</exception>
    public ParagraphRegion(int first, int last)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(first);
        ArgumentOutOfRangeException.ThrowIfLessThan(last, first);
        First = first;
        Last = last;
    }

    /// <summary>Gets the first paragraph.</summary>
    public int First { get; }

    /// <summary>Gets the last paragraph, inclusive.</summary>
    public int Last { get; }
}

/// <summary>A range of cells on one sheet, in A1 notation (<c>B2:D9</c> or <c>C3</c>).</summary>
public sealed class CellRangeRegion : RedactionRegion
{
    /// <summary>Creates a region.</summary>
    /// <param name="sheet">The sheet name.</param>
    /// <param name="range">The range, A1 notation, no sheet prefix.</param>
    /// <exception cref="ArgumentException">The range is not valid A1 notation.</exception>
    public CellRangeRegion(string sheet, string range)
    {
        ArgumentException.ThrowIfNullOrEmpty(sheet);
        Sheet = sheet;
        Range = range;
        if (!TryParse(range, out FirstRow, out FirstColumn, out LastRow, out LastColumn))
        {
            throw new ArgumentException("The range is not valid A1 notation.", nameof(range));
        }
    }

    internal readonly int FirstRow;
    internal readonly int FirstColumn;
    internal readonly int LastRow;
    internal readonly int LastColumn;

    /// <summary>Gets the sheet name.</summary>
    public string Sheet { get; }

    /// <summary>Gets the range as given.</summary>
    public string Range { get; }

    internal bool Contains(int row, int column) => row >= FirstRow && row <= LastRow && column >= FirstColumn && column <= LastColumn;

    internal bool Intersects(int r1, int c1, int r2, int c2) => r1 <= LastRow && r2 >= FirstRow && c1 <= LastColumn && c2 >= FirstColumn;

    // One-based rows and columns.
    internal static bool TryParseCell(ReadOnlySpan<char> text, out int row, out int column)
    {
        row = 0;
        column = 0;
        int i = 0;
        if (i < text.Length && text[i] == '$')
        {
            i++;
        }

        int letters = 0;
        while (i < text.Length && char.IsAsciiLetter(text[i]))
        {
            column = (column * 26) + (char.ToUpperInvariant(text[i]) - 'A' + 1);
            i++;
            letters++;
            if (letters > 3)
            {
                return false;
            }
        }

        if (i < text.Length && text[i] == '$')
        {
            i++;
        }

        int digits = 0;
        while (i < text.Length && char.IsAsciiDigit(text[i]))
        {
            row = (row * 10) + (text[i] - '0');
            i++;
            digits++;
            if (digits > 7)
            {
                return false;
            }
        }

        return letters > 0 && digits > 0 && i == text.Length && row >= 1 && column >= 1 && column <= 16384 && row <= 1048576;
    }

    internal static bool TryParse(string range, out int r1, out int c1, out int r2, out int c2)
    {
        r1 = c1 = r2 = c2 = 0;
        if (string.IsNullOrEmpty(range))
        {
            return false;
        }

        int colon = range.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0)
        {
            if (!TryParseCell(range, out r1, out c1))
            {
                return false;
            }

            r2 = r1;
            c2 = c1;
            return true;
        }

        if (!TryParseCell(range.AsSpan(0, colon), out r1, out c1) || !TryParseCell(range.AsSpan(colon + 1), out r2, out c2))
        {
            return false;
        }

        if (r2 < r1)
        {
            (r1, r2) = (r2, r1);
        }

        if (c2 < c1)
        {
            (c1, c2) = (c2, c1);
        }

        return true;
    }
}

/// <summary>A shape, picture, table, chart or group on one slide, found by name or by its shape id.</summary>
public sealed class ShapeRegion : RedactionRegion
{
    /// <summary>Creates a region that names a shape.</summary>
    /// <param name="slideNumber">The slide, counted from one in presentation order.</param>
    /// <param name="name">The shape's name (<c>cNvPr/@name</c>); every shape with this name on the slide is removed.</param>
    public ShapeRegion(int slideNumber, string name)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(slideNumber, 1);
        ArgumentException.ThrowIfNullOrEmpty(name);
        SlideNumber = slideNumber;
        Name = name;
    }

    /// <summary>Creates a region that names a shape by id.</summary>
    /// <param name="slideNumber">The slide, counted from one in presentation order.</param>
    /// <param name="shapeId">The shape id (<c>cNvPr/@id</c>).</param>
    public ShapeRegion(int slideNumber, int shapeId)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(slideNumber, 1);
        SlideNumber = slideNumber;
        ShapeId = shapeId;
    }

    /// <summary>Gets the slide number, from one.</summary>
    public int SlideNumber { get; }

    /// <summary>Gets the shape name, or null when the shape is named by id.</summary>
    public string? Name { get; }

    /// <summary>Gets the shape id when the shape is named by id.</summary>
    public int? ShapeId { get; }
}

/// <summary>A whole slide: removed from the presentation together with its notes and everything only it uses.</summary>
public sealed class SlideRegion : RedactionRegion
{
    /// <summary>Creates a region.</summary>
    /// <param name="slideNumber">The slide, counted from one in presentation order.</param>
    public SlideRegion(int slideNumber)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(slideNumber, 1);
        SlideNumber = slideNumber;
    }

    /// <summary>Gets the slide number, from one.</summary>
    public int SlideNumber { get; }
}

/// <summary>What to remove and how: matches, regions and options.</summary>
public sealed class RedactionPlan
{
    /// <summary>Gets the matches, applied everywhere text can be found.</summary>
    public IList<RedactionMatch> Matches { get; } = [];

    /// <summary>Gets the regions, applied before the matches. A region that does not fit the kind of file is reported, not applied.</summary>
    public IList<RedactionRegion> Regions { get; } = [];

    /// <summary>Gets or sets the options.</summary>
    public RedactionOptions Options { get; set; } = new();
}
