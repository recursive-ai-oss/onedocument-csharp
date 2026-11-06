using System.Linq;
using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Markup;

namespace RecursiveAI.OneDocument.Layout;

/// <summary>Splits a document where the layout puts its pages.</summary>
public static class DocumentPageSplit
{
    /// <summary>
    /// Splits the document into pieces of <paramref name="pagesPerPiece"/> pages each (the last may be shorter), using the layout tree to
    /// find which body block starts each piece.
    /// </summary>
    /// <param name="document">The document. It is read, not changed.</param>
    /// <param name="pagesPerPiece">Pages in each piece, at least 1. Default 1.</param>
    /// <param name="options">Layout options; null for the defaults. A table of contents or index with no stored result is laid out empty, so call <see cref="DocumentFieldUpdate.UpdateFields"/> first when it should count.</param>
    /// <param name="cancellationToken">Cancels the layout.</param>
    /// <returns>The pieces (see <see cref="Document.SplitBefore"/>).</returns>
    /// <remarks>A piece can only start at the beginning of a body block (a paragraph, table or content control directly in the body). When a block runs over a page boundary the break moves to the next block start, so a piece can hold a little more than the pages asked for.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pagesPerPiece"/> is less than 1.</exception>
    public static SplitResult SplitByPage(this Document document, int pagesPerPiece = 1, LayoutOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentOutOfRangeException.ThrowIfLessThan(pagesPerPiece, 1);
        LayoutOptions opt = options ?? new LayoutOptions();
        opt.SkipFieldBuild = true;
        LayoutDocument layout = LayoutEngine.Layout(document, opt, cancellationToken);

        MarkupNode body = document.EnsureBodyElement();
        var blockIndex = new Dictionary<MarkupNode, int>();
        foreach (MarkupNode b in DocumentMerger.BodyBlocks(body))
        {
            blockIndex[b] = blockIndex.Count;
        }

        int BlockOf(LayoutParagraph p)
        {
            for (MarkupNode n = p.Source.Markup.Node; !n.IsNull; n = n.Parent)
            {
                if (blockIndex.TryGetValue(n, out int i))
                {
                    return i;
                }
            }

            return -1;
        }

        var starts = new SortedSet<int>();
        int previousLast = -1;
        for (int page = 0; page < layout.Pages.Count; page++)
        {
            List<LayoutParagraph> paragraphs = [.. layout.Pages[page].BodyParagraphs];
            if (paragraphs.Count == 0)
            {
                continue;
            }

            int first = BlockOf(paragraphs[0]);
            int last = BlockOf(paragraphs[^1]);
            if (page > 0 && page % pagesPerPiece == 0 && first > 0 && first > previousLast)
            {
                starts.Add(first);
            }
            else if (page > 0 && page % pagesPerPiece == 0 && first >= 0 && first == previousLast && last > first)
            {
                // The block that began on the earlier page goes on here: the piece starts at the next block.
                starts.Add(first + 1);
            }

            previousLast = Math.Max(previousLast, last);
        }

        return document.SplitBefore(starts.Where(s => s < blockIndex.Count));
    }
}
