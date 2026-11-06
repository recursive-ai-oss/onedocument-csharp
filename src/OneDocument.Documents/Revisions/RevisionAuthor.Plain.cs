using System.Text;
using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Documents.Revisions;

// Untracked text edits that share the run splitting of the tracked ones, so a replacement made without tracking and one made with it
// see the same text positions and leave the runs the same way.
public sealed partial class RevisionAuthor
{
    /// <summary>The paragraph's current text in the positions the editing methods use: tracked insertions count, tracked deletions do not.</summary>
    internal static string CurrentText(MarkupNode paragraph)
    {
        var sb = new StringBuilder();
        foreach (Atom a in Atoms(paragraph))
        {
            if (a.Length == 0)
            {
                continue;
            }

            if (WordXml.Is(a.Child, "t"))
            {
                sb.Append(a.Child.InnerText);
            }
            else if (WordXml.IsAny(a.Child, "tab", "ptab"))
            {
                sb.Append('\t');
            }
            else if (WordXml.IsAny(a.Child, "br", "cr"))
            {
                sb.Append('\n');
            }
            else
            {
                // ECMA-376 Part 1 §17.3.3.18 noBreakHyphen: a hyphen that never breaks; U+2011 is its character.
                sb.Append('\u2011');
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Replaces a stretch of the paragraph's current text without tracking: the runs are split at its edges, the new text is written into
    /// the first run of the stretch where its first character was (so it keeps that run's properties and stays inside whatever wraps it,
    /// a hyperlink or another author's insertion), and the text of the stretch is removed from the runs it was in. Anything that is not
    /// text, such as a field mark or a drawing, stays. <paramref name="runPropertiesXml"/> is merged over that run's properties.
    /// </summary>
    internal static void ReplacePlain(MarkupNode paragraph, int offset, int length, string text, string? runPropertiesXml)
    {
        WordXml.RequireXmlText(text, nameof(text));
        CheckRange(paragraph, offset, length);
        if (length == 0)
        {
            return;
        }

        WmlWriter w = WmlWriter.For(paragraph);
        IsolateBoundary(paragraph, offset + length);
        IsolateBoundary(paragraph, offset);
        var inRange = new List<Atom>();
        foreach (Atom a in Atoms(paragraph))
        {
            if (a.Length > 0 && a.Start >= offset && a.Start + a.Length <= offset + length)
            {
                inRange.Add(a);
            }
        }

        if (inRange.Count == 0)
        {
            return;
        }

        // The new content goes in front of the first removed child, in the same run: nothing is moved, so no handle in the list goes stale.
        Atom first = inRange[0];
        if (text.Length > 0)
        {
            first.Child.InsertXmlBefore(WordXml.Bytes(RunContent(w, text)));
        }

        var runs = new List<MarkupNode>();
        foreach (Atom a in inRange)
        {
            if (!runs.Contains(a.Run))
            {
                runs.Add(a.Run);
            }

            a.Child.Remove();
        }

        if (!string.IsNullOrEmpty(runPropertiesXml))
        {
            string merged = MergedProperties(first.Run, runPropertiesXml, w);
            MarkupNode rPr = WordXml.Child(first.Run, "rPr");
            if (rPr.IsNull)
            {
                first.Run.PrependXml(WordXml.Bytes(merged));
            }
            else
            {
                rPr.ReplaceWithXml(WordXml.Bytes(merged));
            }
        }

        foreach (MarkupNode run in runs)
        {
            bool empty = true;
            foreach (MarkupNode c in run.ChildElements)
            {
                if (!WordXml.Is(c, "rPr"))
                {
                    empty = false;
                    break;
                }
            }

            if (!empty)
            {
                continue;
            }

            MarkupNode parent = run.Parent;
            run.Remove();
            if (IsWrapper(parent) && !HasElement(parent))
            {
                parent.Remove();
            }
        }
    }

    // The run's own properties, minus pending changes and revision marks, with the caller's elements replacing those of the same name.
    private static string MergedProperties(MarkupNode run, string format, WmlWriter w)
    {
        HashSet<string> names = ElementNames(format, w);
        var keep = new StringBuilder();
        MarkupNode rPr = WordXml.Child(run, "rPr");
        if (!rPr.IsNull)
        {
            foreach (MarkupNode c in rPr.ChildElements)
            {
                if (WordXml.InMain(c) && (names.Contains(c.LocalNameString) || WordXml.IsAny(c, "rPrChange", "ins", "del", "moveFrom", "moveTo")))
                {
                    continue;
                }

                keep.Append(WordXml.Portable(c));
            }
        }

        return w.Wrap("rPr", string.Empty, keep + format);
    }
}
