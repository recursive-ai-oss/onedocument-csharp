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
    /// Replaces a stretch of the paragraph's current text without tracking: the runs are split at its edges, the text inside is
    /// removed (anything that is not text, such as a field mark or a drawing, stays) and a new run goes where the first removed run was.
    /// The new run has the first replaced character's run properties, with <paramref name="runPropertiesXml"/> merged over them.
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
        string props = MergedProperties(paragraph, offset, runPropertiesXml, w);
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

        MarkupNode first = inRange[0].Run;
        if (text.Length > 0)
        {
            PlaceBefore(first, w.Wrap("r", string.Empty, props + RunContent(w, text)));
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

    // The run properties of the first character in the range, as they are, with the caller's elements replacing those of the same name.
    private static string MergedProperties(MarkupNode paragraph, int offset, string? format, WmlWriter w)
    {
        string source = FormattingSource(paragraph, offset + 1);
        if (string.IsNullOrEmpty(format))
        {
            return source;
        }

        var names = ElementNames(format, w);
        var keep = new StringBuilder();
        if (source.Length > 0)
        {
            string prefix = w.Name("")[..^1];
            MarkupDocument doc = MarkupDocument.Parse(WordXml.Bytes("<wrap xmlns:" + prefix + "=\"" + (w.IsStrict ? WordXml.MainStrict : WordXml.Main) + "\">" + source + "</wrap>"));
            MarkupNode rPr = WordXml.Child(doc.Root, "rPr");
            foreach (MarkupNode c in rPr.ChildElements)
            {
                if (!WordXml.InMain(c) || !names.Contains(c.LocalNameString))
                {
                    keep.Append(WordXml.Portable(c));
                }
            }
        }

        return w.Wrap("rPr", string.Empty, keep + format);
    }
}
