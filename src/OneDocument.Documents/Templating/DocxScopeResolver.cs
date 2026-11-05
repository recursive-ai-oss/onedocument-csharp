using RecursiveAI.OneDocument.Diagnostics;
using RecursiveAI.OneDocument.Templating;

namespace RecursiveAI.OneDocument.Documents.Templating;

/// <summary>The kind of structure a block repeats as a whole.</summary>
internal enum RepeatKind
{
    /// <summary>Whatever lies in the block's range: paragraphs, tables, rows.</summary>
    Any,

    /// <summary>Table rows only; the table around them stays one table.</summary>
    Row,

    /// <summary>Whole tables.</summary>
    Table,

    /// <summary>Paragraphs only (sections are repeated as paragraphs with their section breaks).</summary>
    None,
}

/// <summary>
/// Tells the engine which paragraphs a block repeats or keeps, from the structure of the story: a block whose tags sit in one table row repeats the row,
/// <c>scope=table-row</c> forces that, <c>scope=table</c> repeats whole tables, and <c>scope=section</c> repeats whole sections. Everything else repeats the paragraphs
/// between its tags (or the runs inside one paragraph), which is the engine's own meaning.
/// </summary>
internal sealed class DocxScopeResolver(StoryModel story, List<Defect> defects) : ITemplateScopeResolver
{
    /// <summary>The range chosen for each block in the order the engine asked, or null when it kept the default meaning.</summary>
    public List<TemplateUnitRange?> Ranges { get; } = [];

    /// <summary>What each block repeats, parallel to <see cref="Ranges"/>.</summary>
    public List<RepeatKind> Kinds { get; } = [];

    private RepeatKind _kind = RepeatKind.Any;

    /// <summary>The blocks that repeat sections.</summary>
    public HashSet<int> SectionBlocks { get; } = [];

    public TemplateUnitRange? Resolve(in TemplateBlockInfo block)
    {
        int index = Ranges.Count;
        _kind = RepeatKind.Any;
        TemplateUnitRange? range = Choose(block, index);
        Ranges.Add(range);
        Kinds.Add(range is null ? RepeatKind.Any : _kind);
        return range;
    }

    private TemplateUnitRange? Choose(in TemplateBlockInfo block, int index)
    {
        string? scope = block.Scope?.ToLowerInvariant();
        if (block.OpenUnit < 0 || block.CloseUnit >= story.Units.Count)
        {
            return null;
        }

        switch (scope)
        {
            case "section":
                return SectionRange(block, index);
            case "table":
                return TableRange(block);
            case "table-row":
                return RowRange(block, explicitScope: true);
            case null:
                return RowRange(block, explicitScope: false);
            default:
                return null;
        }
    }

    private TemplateUnitRange? RowRange(in TemplateBlockInfo block, bool explicitScope)
    {
        List<StoryNode> a = story.Chain(block.OpenUnit);
        List<StoryNode> b = story.Chain(block.CloseUnit);
        int common = 0;
        while (common < a.Count && common < b.Count && ReferenceEquals(a[common], b[common]))
        {
            common++;
        }

        StoryNode? lca = common == 0 ? null : a[common - 1];
        StoryNode? rowA = null;
        StoryNode? rowB = null;
        if (lca is { Kind: StoryNodeKind.Table } && common < a.Count && common < b.Count)
        {
            // The tags are in different rows of one table.
            rowA = a[common];
            rowB = b[common];
            if (rowA.Kind != StoryNodeKind.Row || rowB.Kind != StoryNodeKind.Row)
            {
                return Fail(block, explicitScope, "The tags of a table-row block must sit in table rows.");
            }

            if (!explicitScope && !(rowA.IsTagRow && rowB.IsTagRow))
            {
                return null;
            }
        }
        else
        {
            // Tags in different cells of one row: that row. Tags in one cell repeat the cell's paragraphs, unless the author asked for table-row.
            StoryNode? row = lca is { Kind: StoryNodeKind.Row } ? lca : null;
            if (row is null && explicitScope)
            {
                for (StoryNode? n = lca; n is not null && row is null; n = n.Parent)
                {
                    row = n.Kind == StoryNodeKind.Row ? n : null;
                }
            }

            if (row is null)
            {
                return explicitScope ? Fail(block, explicitScope, "scope=table-row is used outside a table.") : null;
            }

            rowA = rowB = row;
        }

        if (rowA.FirstUnit < 0 || rowB!.LastUnit < 0)
        {
            return null;
        }

        _kind = RepeatKind.Row;
        return new TemplateUnitRange(Math.Min(rowA.FirstUnit, block.OpenUnit), Math.Max(rowB.LastUnit, block.CloseUnit));
    }

    private TemplateUnitRange? TableRange(in TemplateBlockInfo block)
    {
        StoryNode? a = OutermostTable(block.OpenUnit);
        StoryNode? b = OutermostTable(block.CloseUnit);
        if (a is null || b is null)
        {
            return Fail(block, true, "scope=table is used outside a table.");
        }

        _kind = RepeatKind.Table;
        return new TemplateUnitRange(Math.Min(a.FirstUnit, block.OpenUnit), Math.Max(b.LastUnit, block.CloseUnit));
    }

    private StoryNode? OutermostTable(int unit)
    {
        StoryNode? found = null;
        foreach (StoryNode n in story.Chain(unit))
        {
            if (n.Kind == StoryNodeKind.Table)
            {
                found = n;
                break;
            }
        }

        return found;
    }

    // ECMA-376 Part 1 §17.6: a section ends at the paragraph whose properties hold w:sectPr, or at the end of the body.
    private TemplateUnitRange? SectionRange(in TemplateBlockInfo block, int index)
    {
        int first = block.OpenUnit;
        while (first > 0 && !story.Units[first - 1].EndsSection)
        {
            first--;
        }

        int last = block.CloseUnit;
        while (last < story.Units.Count - 1 && !story.Units[last].EndsSection)
        {
            last++;
        }

        SectionBlocks.Add(index);
        _kind = RepeatKind.None;
        return new TemplateUnitRange(first, last);
    }

    private TemplateUnitRange? Fail(in TemplateBlockInfo block, bool report, string message)
    {
        if (report)
        {
            defects.Add(new Defect(DefectKind.Structure, "template.scope-unusable", DefectSeverity.Repaired,
                message + " The block repeats the paragraphs between its tags instead.", DefectLocation.InPart(story.PartName + "#p" + (block.OpenUnit + 1).ToString(System.Globalization.CultureInfo.InvariantCulture))));
        }

        return null;
    }
}
