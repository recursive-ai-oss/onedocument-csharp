using RecursiveAI.OneDocument.Documents.Fields;
using RecursiveAI.OneDocument.Documents.Numbering;

namespace RecursiveAI.OneDocument.Layout;

// The page numbers of an index or table-of-authorities entry. The entries are built in the working copy with one of these fields for each
// entry; its arguments are the names of the bookmarks the builder put at the XE or TA fields, so the page list follows the layout like a
// PAGEREF does. A name that starts with ~ is the whole range of a bookmark (XE \r, TA \r): its first and last page, joined by \g (default an
// en dash). \l separates page numbers (default ", "). The field exists only in that copy, never in a saved file.
internal static class IndexPages
{
    public const string FieldName = "ODXEPAGES";

    public static string? Evaluate(FieldSite site, FieldInstruction f)
    {
        FieldEnv env = site.Env;
        env.UsedFacts = true;
        if (env.Facts is not { } facts)
        {
            return null;
        }

        string rangeSeparator = f.Switch("g")?.Value ?? "\u2013";
        var seen = new SortedDictionary<int, string>();
        foreach (string token in f.Arguments)
        {
            bool range = token.StartsWith('~');
            if (env.Model.Bookmark(range ? token[1..] : token) is not { } bm)
            {
                continue;
            }

            int page = facts.PageOf(env.Model, bm.StartParagraph, bm.StartOffset);
            if (page < 0)
            {
                continue;
            }

            string text = Printed(facts, page);
            if (range)
            {
                int end = facts.PageOf(env.Model, bm.EndParagraph < 0 ? bm.StartParagraph : bm.EndParagraph, int.MaxValue);
                if (end > page)
                {
                    text += rangeSeparator + Printed(facts, end);
                }
            }

            if (!seen.TryGetValue(page, out string? known) || text.Length > known.Length)
            {
                seen[page] = text;
            }
        }

        // TOA \p: five or more pages read "passim" (ECMA-376 Part 1 §17.16.5.67).
        if (f.HasSwitch("p") && seen.Count >= 5)
        {
            return "passim";
        }

        return string.Join(f.Switch("l")?.Value ?? ", ", seen.Values);
    }

    private static string Printed(FieldFacts facts, int page) => NumberFormatter.Format(facts.Pages[page].Format, facts.Pages[page].Printed).Text;
}
