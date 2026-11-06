using System.Text.RegularExpressions;

namespace RecursiveAI.OneDocument.Documents;

/// <summary>
/// Comparison of markup for merge decisions: two styles, list definitions or section properties count as equal when they differ only in
/// prefixes, white space, revision-session ids and the ids that name them.
/// </summary>
internal static partial class MergeText
{
    [GeneratedRegex(@"\s+[A-Za-z0-9_]+:rsid[A-Za-z]*=""[^""]*""")]
    private static partial Regex Rsid();

    [GeneratedRegex(@"\s+xmlns(:[A-Za-z0-9_]+)?=""[^""]*""")]
    private static partial Regex Namespaces();

    [GeneratedRegex(@"(?<=[</\s])[A-Za-z0-9_]+:")]
    private static partial Regex Prefixes();

    [GeneratedRegex(@">\s+<")]
    private static partial Regex Gaps();

    [GeneratedRegex(@"\s+styleId=""[^""]*""|\s+default=""[^""]*""")]
    private static partial Regex StyleIds();

    [GeneratedRegex(@"<nsid [^>]*/>|<tmpl [^>]*/>|\s+abstractNumId=""[^""]*""")]
    private static partial Regex AbstractIds();

    [GeneratedRegex(@"\s+numId=""[^""]*""|<abstractNumId [^>]*/>")]
    private static partial Regex InstanceIds();

    [GeneratedRegex(@"<(header|footer)Reference [^>]*/>")]
    private static partial Regex Story();

    public static string Canonical(string xml, bool ignoreStyleId = false)
    {
        string s = Namespaces().Replace(Rsid().Replace(xml, string.Empty), string.Empty);
        s = Prefixes().Replace(s, string.Empty);
        if (ignoreStyleId)
        {
            s = StyleIds().Replace(s, string.Empty);
        }

        return Gaps().Replace(s, "><").Trim();
    }

    public static string CanonicalAbstract(string xml) => AbstractIds().Replace(Canonical(xml), string.Empty);

    public static string CanonicalInstance(string xml) => InstanceIds().Replace(Canonical(xml), string.Empty);

    /// <summary>Section properties without the header and footer references, which name parts and so always differ between documents.</summary>
    public static string CanonicalSection(string xml, out bool hasStoryReferences)
    {
        string s = Canonical(xml);
        hasStoryReferences = Story().IsMatch(s);
        return Story().Replace(s, string.Empty);
    }
}
