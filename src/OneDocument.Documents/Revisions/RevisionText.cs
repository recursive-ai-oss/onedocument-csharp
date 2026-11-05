using System.Text;
using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Documents.Revisions;

/// <summary>
/// Reads the text of a part with every tracked change accepted or every one rejected, without changing the part.
/// </summary>
internal static class RevisionText
{
    /// <summary>All text below an element, deleted text included.</summary>
    internal static string Collect(MarkupNode element)
    {
        var sb = new StringBuilder();
        Inline(element, null, sb, 0);
        return sb.ToString();
    }

    /// <summary>The text of a whole part in one of the two views; one line per paragraph.</summary>
    internal static string OfPart(MarkupNode root, RevisionView view)
    {
        var sb = new StringBuilder();
        Blocks(root, view, sb, 0);
        return sb.ToString();
    }

    /// <summary>The text of one paragraph in a view, without its paragraph break.</summary>
    internal static string OfParagraph(MarkupNode paragraph, RevisionView view)
    {
        var sb = new StringBuilder();
        Inline(paragraph, view, sb, 0);
        return sb.ToString();
    }

    private static bool Keep(RevisionView? view, bool inserted) =>
        view is null || (inserted ? view == RevisionView.Accepted : view == RevisionView.Rejected);

    /// <summary>Which marks the paragraph's own mark carries: ins/moveTo (inserted) or del/moveFrom (removed), looked up in pPr/rPr.</summary>
    internal static void MarkState(MarkupNode paragraph, out bool inserted, out bool deleted)
    {
        inserted = false;
        deleted = false;
        MarkupNode pPr = WordXml.Child(paragraph, "pPr");
        if (pPr.IsNull)
        {
            return;
        }

        MarkupNode rPr = WordXml.Child(pPr, "rPr");
        if (rPr.IsNull)
        {
            return;
        }

        foreach (MarkupNode c in rPr.ChildElements)
        {
            if (WordXml.IsAny(c, "ins", "moveTo"))
            {
                inserted = true;
            }
            else if (WordXml.IsAny(c, "del", "moveFrom"))
            {
                deleted = true;
            }
        }
    }

    private static void Blocks(MarkupNode node, RevisionView view, StringBuilder sb, int depth)
    {
        if (depth > 256)
        {
            return;
        }

        foreach (MarkupNode c in node.ChildElements)
        {
            if (WordXml.Is(c, "p"))
            {
                Inline(c, view, sb, depth + 1);
                MarkState(c, out bool ins, out bool del);
                bool gone = view == RevisionView.Accepted ? del : ins;
                if (!gone)
                {
                    sb.Append('\n');
                }
            }
            else if (WordXml.Is(c, "tr"))
            {
                MarkupNode trPr = WordXml.Child(c, "trPr");
                bool ins = !trPr.IsNull && !WordXml.Child(trPr, "ins").IsNull;
                bool del = !trPr.IsNull && !WordXml.Child(trPr, "del").IsNull;
                if ((view == RevisionView.Accepted && del) || (view == RevisionView.Rejected && ins))
                {
                    continue;
                }

                Blocks(c, view, sb, depth + 1);
            }
            else if (WordXml.Is(c, "tc"))
            {
                MarkupNode tcPr = WordXml.Child(c, "tcPr");
                bool ins = !tcPr.IsNull && !WordXml.Child(tcPr, "cellIns").IsNull;
                bool del = !tcPr.IsNull && !WordXml.Child(tcPr, "cellDel").IsNull;
                if ((view == RevisionView.Accepted && del) || (view == RevisionView.Rejected && ins))
                {
                    continue;
                }

                Blocks(c, view, sb, depth + 1);
            }
            else if (WordXml.IsAny(c, "pPr", "rPr", "sectPr", "trPr", "tcPr", "tblPr", "tblGrid", "tblPrEx"))
            {
                continue;
            }
            else
            {
                Blocks(c, view, sb, depth + 1);
            }
        }
    }

    private static void Inline(MarkupNode container, RevisionView? view, StringBuilder sb, int depth)
    {
        if (depth > 256)
        {
            return;
        }

        foreach (MarkupNode c in container.ChildElements)
        {
            if (!WordXml.InMain(c))
            {
                continue;
            }

            ReadOnlySpan<byte> name = c.LocalName;
            if (WordXml.SpanEquals(name, "r"))
            {
                Run(c, sb);
            }
            else if (WordXml.SpanEquals(name, "ins") || WordXml.SpanEquals(name, "moveTo"))
            {
                if (Keep(view, true))
                {
                    Inline(c, view, sb, depth + 1);
                }
            }
            else if (WordXml.SpanEquals(name, "del") || WordXml.SpanEquals(name, "moveFrom"))
            {
                if (Keep(view, false))
                {
                    Inline(c, view, sb, depth + 1);
                }
            }
            else if (WordXml.SpanEquals(name, "hyperlink") || WordXml.SpanEquals(name, "fldSimple") || WordXml.SpanEquals(name, "smartTag")
                || WordXml.SpanEquals(name, "customXml") || WordXml.SpanEquals(name, "dir") || WordXml.SpanEquals(name, "bdo")
                || WordXml.SpanEquals(name, "sdt") || WordXml.SpanEquals(name, "sdtContent"))
            {
                Inline(c, view, sb, depth + 1);
            }
        }
    }

    private static void Run(MarkupNode run, StringBuilder sb)
    {
        foreach (MarkupNode c in run.ChildElements)
        {
            if (!WordXml.InMain(c))
            {
                continue;
            }

            ReadOnlySpan<byte> name = c.LocalName;
            if (WordXml.SpanEquals(name, "t") || WordXml.SpanEquals(name, "delText"))
            {
                sb.Append(c.InnerText);
            }
            else if (WordXml.SpanEquals(name, "tab") || WordXml.SpanEquals(name, "ptab"))
            {
                sb.Append('\t');
            }
            else if (WordXml.SpanEquals(name, "br") || WordXml.SpanEquals(name, "cr"))
            {
                sb.Append('\n');
            }
            else if (WordXml.SpanEquals(name, "noBreakHyphen"))
            {
                sb.Append('\u2011');
            }
        }
    }
}
