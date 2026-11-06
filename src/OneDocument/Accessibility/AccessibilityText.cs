using System.Globalization;
using System.Text.RegularExpressions;

namespace RecursiveAI.OneDocument.Accessibility;

/// <summary>Text heuristics shared by the accessibility rules of all three kinds.</summary>
public static partial class AccessibilityText
{
    private static readonly string[] MeaninglessLinkTexts =
    [
        "click here", "click", "here", "link", "this link", "this", "read more", "more", "more info", "learn more", "see more", "go", "go here", "page", "website", "web site",
    ];

    /// <summary>
    /// Tells whether alternative text is a file name: it ends in a file extension of one to five letters or digits after a dot, with no
    /// spaces in the last word (<c>IMG_0042.jpg</c>, <c>chart.png</c>), or is a path.
    /// </summary>
    /// <param name="text">The alternative text.</param>
    /// <returns>True for something that looks like a file name.</returns>
    public static bool LooksLikeFileName(string text)
    {
        string t = text.Trim();
        return t.Length > 0 && FileNamePattern().IsMatch(t);
    }

    /// <summary>The marker an automatic image-description service appends to the alternative text it generates.</summary>
    public const string GeneratedAltTextMarker = "Description automatically generated";

    /// <summary>Tells whether alternative text still carries the marker of an automatic description.</summary>
    /// <param name="text">The alternative text.</param>
    /// <returns>True when it does.</returns>
    public static bool IsGeneratedAltText(string text) => text.Contains(GeneratedAltTextMarker, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Tells why link text says nothing about the target, or null when it is fine. Empty text, generic phrases (“click here”, “read more”)
    /// and an address written out (the text is the target, or starts with <c>http://</c>, <c>https://</c> or <c>www.</c>) are not meaningful.
    /// </summary>
    /// <param name="text">The text of the link.</param>
    /// <param name="target">The address or bookmark the link goes to, when known.</param>
    /// <returns>The reason in plain words, or null.</returns>
    public static string? MeaninglessLinkReason(string text, string? target)
    {
        string t = text.Trim();
        if (t.Length == 0)
        {
            return "the link has no text";
        }

        string lower = t.Trim('.', ':', '!', '>', '<', '[', ']', '(', ')').Trim().ToLowerInvariant();
        if (Array.IndexOf(MeaninglessLinkTexts, lower) >= 0)
        {
            return "“" + t + "” does not say where the link goes";
        }

        if (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || t.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("www.", StringComparison.OrdinalIgnoreCase) || (target is not null && string.Equals(t.TrimEnd('/'), target.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)))
        {
            return "the link text is the web address itself";
        }

        return null;
    }

    /// <summary>Tells whether a sheet name is a generated one: <c>Sheet</c>, <c>Chart</c>, <c>Macro</c> or <c>Dialog</c> followed by digits, in any case.</summary>
    /// <param name="name">The sheet name.</param>
    /// <returns>True for a default name.</returns>
    public static bool IsDefaultSheetName(string name) => DefaultSheet().IsMatch(name.Trim());

    /// <summary>Tells whether a workbook table name is a generated one (<c>Table1</c>, …).</summary>
    /// <param name="name">The table name.</param>
    /// <returns>True for a default name.</returns>
    public static bool IsDefaultTableName(string name) => DefaultTable().IsMatch(name.Trim());

    /// <summary>Tells whether a presentation section name is a placeholder: “Default Section”, “Untitled Section” or “Section N”.</summary>
    /// <param name="name">The section name.</param>
    /// <returns>True for a placeholder name.</returns>
    public static bool IsDefaultSectionName(string name) => DefaultSection().IsMatch(name.Trim());

    /// <summary>Normalises a title for comparing: trimmed, inner whitespace collapsed, case-insensitive.</summary>
    /// <param name="title">The title.</param>
    /// <returns>The comparison key.</returns>
    public static string TitleKey(string title) => string.Join(' ', title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpper(CultureInfo.InvariantCulture);

    [GeneratedRegex(@"(^|[\\/])[^\\/\s]+\.[A-Za-z0-9]{1,5}$", RegexOptions.CultureInvariant)]
    private static partial Regex FileNamePattern();

    [GeneratedRegex(@"^(Sheet|Chart|Macro|Dialog)\s*\d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DefaultSheet();

    [GeneratedRegex(@"^Table\s*\d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DefaultTable();

    [GeneratedRegex(@"^(Default Section|Untitled Section|Section\s*\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DefaultSection();
}
