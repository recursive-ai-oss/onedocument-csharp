using System.Globalization;
using System.Text;
using RecursiveAI.OneDocument.Templating;

namespace RecursiveAI.OneDocument.Documents.Templating;

/// <summary>
/// Puts a story back together from the engine's output, which is a flat list of paragraphs in output order. Structures are re-created from the way the
/// list moves through the source: a table row that the template repeated is opened again for each repetition (the engine records which loops were running),
/// a table or row left with nothing in it is dropped, a cell that lost its paragraphs gets an empty one, and the identifiers repeated copies would share are renumbered.
/// </summary>
internal sealed class StoryRebuilder
{
    private sealed class Entry(StoryNode node, string key)
    {
        public StoryNode Node { get; } = node;

        public string Key { get; } = key;

        public StringBuilder Body { get; } = new();

        public bool HasChild { get; set; }

        public bool NonBlank { get; set; }

        public bool LastWasTable { get; set; }

        public int NextCell { get; set; }
    }

    private readonly StoryModel _story;
    private readonly RenderContext _ctx;
    private readonly ParagraphEmitter _emitter;
    private readonly List<TemplateUnitRange?> _ranges;
    private readonly IReadOnlyList<TemplateBlockInfo> _blocks;
    private readonly HashSet<int> _sectionBlocks;
    private readonly List<Entry> _stack = [];

    public StoryRebuilder(StoryModel story, RenderContext ctx, IReadOnlyList<TemplateBlockInfo> blocks, DocxScopeResolver resolver)
    {
        _story = story;
        _ctx = ctx;
        _emitter = new ParagraphEmitter(ctx, story);
        _ranges = resolver.Ranges;
        _blocks = blocks;
        _sectionBlocks = resolver.SectionBlocks;
    }

    /// <summary>Builds the markup that replaces the story's children.</summary>
    /// <param name="units">The engine's output.</param>
    /// <returns>The new content of the story's root element.</returns>
    public string Run(IReadOnlyList<TemplateRenderedUnit> units)
    {
        _stack.Clear();
        _stack.Add(new Entry(_story.Tree, string.Empty));
        for (int i = 0; i < units.Count; i++)
        {
            TemplateRenderedUnit ru = units[i];
            StoryNode leaf = _story.Units[ru.SourceUnit];
            List<StoryNode> chain = _story.Chain(ru.SourceUnit);
            string[] keys = new string[chain.Count];
            for (int d = 0; d < chain.Count; d++)
            {
                keys[d] = KeyFor(chain[d], ru.Loops);
            }

            int common = 1;
            while (common < chain.Count && common < _stack.Count && ReferenceEquals(_stack[common].Node, chain[common]) && _stack[common].Key == keys[common])
            {
                common++;
            }

            Pop(common);
            for (int d = common; d < chain.Count; d++)
            {
                Push(chain[d], keys[d]);
            }

            Entry top = _stack[^1];
            string sectionBreak = NeedsSectionBreak(leaf, ru, i < units.Count - 1 ? units[i + 1] : null) ? _story.FinalSectionProperties : string.Empty;
            string xml = _ctx.Ids.Uniquify(_emitter.Emit(leaf, ru, sectionBreak) + leaf.After);
            top.Body.Append(xml);
            top.HasChild = true;
            top.LastWasTable = false;
            top.NonBlank |= IsVisible(leaf, ru);
        }

        Pop(1);
        Entry root = _stack[0];
        return _story.Tree.Open + root.Body + _story.Tree.Close;
    }

    // The loops that repeat a node as a whole are the ones whose range holds all of its paragraphs. Loops that cut through it (a loop over some
    // paragraphs of a cell) leave the cell alone.
    private string KeyFor(StoryNode node, IReadOnlyList<TemplateLoopFrame> loops)
    {
        if (loops.Count == 0 || node.FirstUnit < 0)
        {
            return string.Empty;
        }

        StringBuilder? sb = null;
        foreach (TemplateLoopFrame f in loops)
        {
            (int first, int last) = RangeOf(f.Block);
            if (first <= node.FirstUnit && node.LastUnit <= last)
            {
                (sb ??= new StringBuilder()).Append(f.Block.ToString(CultureInfo.InvariantCulture)).Append(':').Append(f.Iteration.ToString(CultureInfo.InvariantCulture)).Append(';');
            }
        }

        return sb?.ToString() ?? string.Empty;
    }

    private (int First, int Last) RangeOf(int block)
    {
        if (block >= 0 && block < _ranges.Count && _ranges[block] is { } r)
        {
            return (r.First, r.Last);
        }

        if (block >= 0 && block < _blocks.Count)
        {
            TemplateBlockInfo b = _blocks[block];
            return (b.OpenUnit + 1, b.CloseUnit - 1);
        }

        return (1, 0);
    }

    private bool NeedsSectionBreak(StoryNode leaf, TemplateRenderedUnit ru, TemplateRenderedUnit? next)
    {
        if (_sectionBlocks.Count == 0 || next is null || leaf.EndsSection || _story.FinalSectionProperties.Length == 0)
        {
            return false;
        }

        foreach (TemplateLoopFrame f in ru.Loops)
        {
            if (!_sectionBlocks.Contains(f.Block) || RangeOf(f.Block).Last != leaf.Unit)
            {
                continue;
            }

            foreach (TemplateLoopFrame g in next.Loops)
            {
                if (g.Block == f.Block && g.Iteration > f.Iteration)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsVisible(StoryNode leaf, TemplateRenderedUnit ru)
    {
        if (!string.IsNullOrWhiteSpace(ru.Text))
        {
            return true;
        }

        foreach (TemplatePiece p in ru.Pieces)
        {
            if (p.Kind != TemplatePieceKind.Text)
            {
                return true;
            }
        }

        foreach (ParagraphItem item in leaf.Paragraph!.Items)
        {
            if (item.Xml is not null && (item.Xml.Contains("drawing", StringComparison.Ordinal) || item.Xml.Contains("pict", StringComparison.Ordinal) || item.Xml.Contains("object", StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }

    private void Push(StoryNode node, string key)
    {
        if (node.Kind == StoryNodeKind.Cell && _stack[^1].Node.Kind == StoryNodeKind.Row)
        {
            // Cells the row's paragraphs skipped still belong to the row: they come out empty.
            Entry row = _stack[^1];
            int index = row.Node.Children.IndexOf(node);
            for (int k = row.NextCell; k < index; k++)
            {
                row.Body.Append(EmptyCell(row.Node.Children[k]));
            }

            row.NextCell = index + 1;
        }

        _stack.Add(new Entry(node, key));
    }

    private void Pop(int depth)
    {
        while (_stack.Count > depth)
        {
            Entry e = _stack[^1];
            _stack.RemoveAt(_stack.Count - 1);
            Entry parent = _stack[^1];
            StoryNode n = e.Node;
            string content;
            switch (n.Kind)
            {
                case StoryNodeKind.Row:
                    for (int k = e.NextCell; k < n.Children.Count; k++)
                    {
                        e.Body.Append(EmptyCell(n.Children[k]));
                    }

                    // A row that only held block tags is scaffolding for the template, not content.
                    content = n.IsTagRow && !e.NonBlank ? string.Empty : n.Open + e.Body + n.Close;
                    break;
                case StoryNodeKind.Cell:
                    if (!e.HasChild || e.LastWasTable)
                    {
                        e.Body.Append(EmptyParagraph());
                    }

                    content = n.Open + e.Body + n.Close;
                    break;
                default:
                    content = e.HasChild ? n.Open + e.Body + n.Close : string.Empty;
                    break;
            }

            if (content.Length == 0)
            {
                continue;
            }

            parent.Body.Append(content);
            if (n.After.Length > 0)
            {
                parent.Body.Append(_ctx.Ids.Uniquify(n.After));
            }

            parent.HasChild = true;
            parent.LastWasTable = n.Kind == StoryNodeKind.Table;
            parent.NonBlank |= e.NonBlank || n.Kind == StoryNodeKind.Table;
        }
    }

    private string EmptyParagraph() => "<" + _storyPrefix + "p/>";

    private string EmptyCell(StoryNode cell) => cell.Open + EmptyParagraph() + cell.Close;

    private string _storyPrefix => _story.Root.Prefix.Length == 0 ? string.Empty : Encoding.UTF8.GetString(_story.Root.Prefix) + ":";
}
