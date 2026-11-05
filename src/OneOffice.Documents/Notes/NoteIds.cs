using RecursiveAI.OneOffice.Markup;

namespace RecursiveAI.OneOffice.Documents;

/// <summary>Finds the highest id in use for notes and comments, so that a new one never repeats an id.</summary>
internal static class NoteIds
{
    /// <summary>
    /// The highest id carried by an element with one of the given local names, anywhere in the main part, the part that holds
    /// the notes or comments, and the settings part (which lists the separator notes). Elements inside
    /// <c>mc:AlternateContent</c> branches count, and so do references to notes that do not exist: reusing the id of a dangling
    /// reference would attach it to the new note. ECMA-376 Part 1 §17.11.10: footnote ids identify the note a reference points to.
    /// </summary>
    /// <returns>The highest id, or <paramref name="floor"/> minus one when none is in use, so that adding one gives <paramref name="floor"/>.</returns>
    internal static long NextFree(Document owner, MarkupNode partRoot, long floor, params string[] localNames)
    {
        long max = floor - 1;
        Scan(partRoot, localNames, ref max);
        Scan(owner.MainMarkup.Root, localNames, ref max);
        if (owner.LoadRelatedByType(owner.OfficeRelationship("settings"), out _) is { } settings)
        {
            Scan(settings.Root, localNames, ref max);
        }

        return max + 1;
    }

    private static void Scan(MarkupNode root, string[] names, ref long max)
    {
        for (MarkupNode n = root; !n.IsNull;)
        {
            if (n.Kind == MarkupNodeKind.Element && HubXml.IsWordNamespace(n.NamespaceUri) && Array.IndexOf(names, n.LocalNameString) >= 0
                && Wml.Long(n.GetAttribute(n.NamespaceUri, "id")) is { } id && id > max)
            {
                max = id;
            }

            if (!n.FirstChild.IsNull)
            {
                n = n.FirstChild;
                continue;
            }

            MarkupNode next = default;
            for (MarkupNode c = n; !c.IsNull && c != root; c = c.Parent)
            {
                if (!c.NextSibling.IsNull)
                {
                    next = c.NextSibling;
                    break;
                }
            }

            n = next;
        }
    }
}
