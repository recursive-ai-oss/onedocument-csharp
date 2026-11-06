using System.Linq;
using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Formats;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Operations;
using RecursiveAI.OneDocument.Packaging;

namespace RecursiveAI.OneDocument.Documents;

/// <summary>One piece of a split document: a standalone document and the range of the original's body blocks it holds.</summary>
public sealed class DocumentPiece
{
    internal DocumentPiece(Document document, int firstBlock, int blockCount, string title)
    {
        Document = document;
        FirstBlock = firstBlock;
        BlockCount = blockCount;
        Title = title;
    }

    /// <summary>Gets the piece as a document of its own. It is owned by the <see cref="SplitResult"/> that holds it.</summary>
    public Document Document { get; }

    /// <summary>Gets the index of the piece's first block among the original's body blocks.</summary>
    public int FirstBlock { get; }

    /// <summary>Gets how many body blocks of the original the piece holds.</summary>
    public int BlockCount { get; }

    /// <summary>Gets a title for the piece: the text of its heading for a split by heading, else empty.</summary>
    public string Title { get; }
}

/// <summary>The pieces a split produced, with the report of what it did. Disposing it disposes the piece documents.</summary>
public sealed class SplitResult : IDisposable
{
    internal SplitResult(IReadOnlyList<DocumentPiece> pieces, MergeReport report)
    {
        Pieces = pieces;
        Report = report;
    }

    /// <summary>Gets the pieces in document order.</summary>
    public IReadOnlyList<DocumentPiece> Pieces { get; }

    /// <summary>Gets the report: <c>parts</c> counts the pieces, <c>styles-removed</c>, <c>lists-removed</c> and <c>parts-removed</c> what the pieces did not need.</summary>
    public MergeReport Report { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (DocumentPiece p in Pieces)
        {
            p.Document.Dispose();
        }
    }
}

public sealed partial class Document
{
    /// <summary>Splits the document at its section breaks: one piece per section.</summary>
    /// <returns>The pieces. A document with one section gives one piece.</returns>
    /// <remarks>Each piece is a copy of the document cut down to its blocks: every part of the original that the piece's content does not use (styles, lists, notes, comments, pictures, headers, links) is left out, and a section that inherited its headers and footers from an earlier one gets them explicitly. The original is not changed.</remarks>
    public SplitResult SplitBySections()
    {
        List<MarkupNode> blocks = DocumentMerger.BodyBlocks(EnsureBodyElement());
        var breaks = new List<int>();
        for (int i = 0; i + 1 < blocks.Count; i++)
        {
            if (DocumentSplitter.HasSectionBreak(blocks[i]))
            {
                breaks.Add(i + 1);
            }
        }

        return SplitBefore(breaks);
    }

    /// <summary>Splits the document before each heading of the given level or higher.</summary>
    /// <param name="level">The deepest heading level that starts a piece, 1 to 9. A heading is a paragraph whose outline level (set on it or by its style) or built-in heading style (<c>heading 1</c> …) says so.</param>
    /// <returns>The pieces. Text before the first heading is the first piece; a document without headings gives one piece.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="level"/> is not from 1 to 9.</exception>
    public SplitResult SplitByHeading(int level)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(level, 9);
        List<MarkupNode> blocks = DocumentMerger.BodyBlocks(EnsureBodyElement());
        var breaks = new List<int>();
        for (int i = 1; i < blocks.Count; i++)
        {
            int l = DocumentSplitter.HeadingLevel(this, blocks[i]);
            if (l is >= 1 && l <= level)
            {
                breaks.Add(i);
            }
        }

        return SplitBefore(breaks);
    }

    /// <summary>Splits the document before the given body blocks.</summary>
    /// <param name="blockIndices">Indices among the body blocks (the paragraphs, tables and content controls directly in the body); each starts a new piece. Duplicates and 0 are ignored.</param>
    /// <returns>The pieces. Used by splits that decide the breaks elsewhere, for instance by page from the layout.</returns>
    /// <exception cref="ArgumentOutOfRangeException">An index is outside the body.</exception>
    public SplitResult SplitBefore(IEnumerable<int> blockIndices)
    {
        ArgumentNullException.ThrowIfNull(blockIndices);
        EnsureBodyElement();
        return DocumentSplitter.Split(this, blockIndices);
    }
}

internal static class DocumentSplitter
{
    public static SplitResult Split(Document source, IEnumerable<int> breakIndices)
    {
        source.CommitChanges();
        List<MarkupNode> blocks = DocumentMerger.BodyBlocks(source.Body.Container.Node);
        int[] cuts = [.. breakIndices.Where(i => i != 0).Distinct().Order()];
        if (cuts.Length > 0 && (cuts[0] < 0 || cuts[^1] > blocks.Count))
        {
            throw new ArgumentOutOfRangeException(nameof(breakIndices), "A split position is outside the body blocks.");
        }

        int[] starts = [0, .. cuts.Where(c => c < blocks.Count)];
        var report = new MergeReport();
        var pieces = new List<DocumentPiece>();
        for (int k = 0; k < starts.Length; k++)
        {
            int first = starts[k];
            int end = k + 1 < starts.Length ? starts[k + 1] : blocks.Count;
            Document clone = Clone(source);
            Cut(source, clone, first, end - first);
            DocumentPruner.Prune(clone, report);
            string title = blocks.Count > first && HeadingLevel(source, blocks[first]) is >= 1 ? blocks[first].InnerText : string.Empty;
            pieces.Add(new DocumentPiece(clone, first, end - first, title));
        }

        report.Count("parts", pieces.Count);
        return new SplitResult(pieces, report);
    }

    private static Document Clone(Document source)
    {
        var ms = new MemoryStream();
        try
        {
            source.SaveAs(ms, source.Format);
        }
        catch (Exception ex) when (ex is OfficeUnsupportedException or FormatNotAvailableException)
        {
            ms.SetLength(0);
            source.SaveAs(ms, OfficeFormat.Docx);
        }

        ms.Position = 0;
        return Document.Open(ms, new OpenContext(source.Context.Limits), leaveOpen: false);
    }

    // Keeps blocks [first, first+count) of the clone and makes the section that covers the end of the range the body's last section.
    private static void Cut(Document source, Document clone, int first, int count)
    {
        MarkupNode body = clone.Body.Container.Node;
        List<MarkupNode> blocks = DocumentMerger.BodyBlocks(body);
        MarkupNode? bodySect = FindBodySectPr(body);
        MakeStoryReferencesExplicit(blocks, bodySect);

        // The section that covers the last kept block: the first section break at or after it.
        MarkupNode governing = bodySect ?? default;
        for (int i = first + count - 1; i < blocks.Count; i++)
        {
            if (SectPrOf(blocks[i]) is { } s)
            {
                governing = s;
                break;
            }
        }

        byte[]? sectXml = governing.IsNull ? null : MarkupFragment.Serialize(governing, body);
        bool inLastKept = !governing.IsNull && count > 0 && IsInside(governing, blocks[first + count - 1]);
        for (int i = blocks.Count - 1; i >= 0; i--)
        {
            if (i < first || i >= first + count)
            {
                blocks[i].Remove();
            }
        }

        if (sectXml is not null && !governing.IsNull && governing != bodySect)
        {
            if (inLastKept)
            {
                governing.Remove();
            }

            if (bodySect is { } old)
            {
                old.ReplaceWithXml(sectXml);
            }
            else
            {
                body.AppendXml(sectXml);
            }
        }

        clone.InvalidateAfterOperation();
    }

    private static bool IsInside(MarkupNode node, MarkupNode ancestor)
    {
        for (MarkupNode n = node; !n.IsNull; n = n.Parent)
        {
            if (n == ancestor)
            {
                return true;
            }
        }

        return false;
    }

    // ECMA-376 Part 1 §17.10.5 (headerReference): a section without a reference of some type inherits it from the section before.
    // A piece that starts at a later section has nothing before it, so the inherited references are written into the section.
    private static void MakeStoryReferencesExplicit(List<MarkupNode> blocks, MarkupNode? bodySect)
    {
        var sections = new List<MarkupNode>();
        foreach (MarkupNode b in blocks)
        {
            if (SectPrOf(b) is { } s)
            {
                sections.Add(s);
            }
        }

        if (bodySect is { } last)
        {
            sections.Add(last);
        }

        var current = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (MarkupNode sect in sections)
        {
            var own = new HashSet<string>(StringComparer.Ordinal);
            foreach (MarkupNode r in sect.ChildElements)
            {
                if (OpsXml.W(r) is "headerReference" or "footerReference")
                {
                    string key = OpsXml.W(r) + "|" + (DocumentMerger.WAttr(r, "type") ?? "default");
                    own.Add(key);
                    current[key] = r.OuterXml;
                }
            }

            string missing = string.Concat(current.Where(kv => !own.Contains(kv.Key)).OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Value));
            if (missing.Length > 0)
            {
                // The references are copied from sections of the same document, so every prefix they use is in scope.
                sect.PrependXml(System.Text.Encoding.UTF8.GetBytes(missing));
            }
        }
    }

    private static MarkupNode? FindBodySectPr(MarkupNode body)
    {
        for (MarkupNode n = body.LastChild; !n.IsNull; n = n.PreviousSibling)
        {
            if (OpsXml.W(n) == "sectPr")
            {
                return n;
            }
        }

        return null;
    }

    public static bool HasSectionBreak(MarkupNode block) => SectPrOf(block) is not null;

    private static MarkupNode? SectPrOf(MarkupNode block)
    {
        if (OpsXml.W(block) == "p")
        {
            MarkupNode pPr = OpsXml.Child(block, "pPr");
            if (!pPr.IsNull && OpsXml.Child(pPr, "sectPr") is { IsNull: false } s)
            {
                return s;
            }
        }
        else if (OpsXml.W(block) == "sdt")
        {
            MarkupNode content = OpsXml.Child(block, "sdtContent");
            for (MarkupNode c = content.FirstChild; !c.IsNull; c = c.NextSibling)
            {
                if (SectPrOf(c) is { } s)
                {
                    return s;
                }
            }
        }

        return null;
    }

    /// <summary>The heading level (1 to 9) of a body block, or 0 when it is not a heading.</summary>
    public static int HeadingLevel(Document d, MarkupNode block)
    {
        if (OpsXml.W(block) != "p")
        {
            return 0;
        }

        MarkupNode pPr = OpsXml.Child(block, "pPr");
        if (!pPr.IsNull)
        {
            if (OpsXml.Child(pPr, "outlineLvl") is { IsNull: false } direct && Wml.Long(DocumentMerger.WAttr(direct, "val")) is { } v)
            {
                return v is >= 0 and <= 8 ? (int)v + 1 : 0;
            }

            string? styleId = OpsXml.Child(pPr, "pStyle") is { IsNull: false } ps ? DocumentMerger.WAttr(ps, "val") : null;
            for (int guard = 0; styleId is not null && guard < 32; guard++)
            {
                if (d.Styles.Find(styleId) is not { } style)
                {
                    break;
                }

                if (style.ParagraphPropertiesElement is { } sp && Wml.Child(sp, "outlineLvl"u8) is { } ol && Wml.Long(Wml.Attr(ol, "val")) is { } sv)
                {
                    return sv is >= 0 and <= 8 ? (int)sv + 1 : 0;
                }

                string name = style.Name ?? string.Empty;
                if (name.StartsWith("heading ", StringComparison.OrdinalIgnoreCase) && int.TryParse(name.AsSpan(8), out int n) && n is >= 1 and <= 9)
                {
                    return n;
                }

                styleId = style.BasedOnId;
            }
        }

        return 0;
    }
}
