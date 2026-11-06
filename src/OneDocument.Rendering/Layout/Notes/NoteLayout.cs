using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Documents.Notes;

namespace RecursiveAI.OneDocument.Layout;

// A note's text formatted for one width, flattened into lines so a note can be cut between any two of them.
internal sealed class FormattedNote
{
    public required NoteKey Key;
    public List<FormattedParagraph> Paragraphs = [];
    public List<(int Para, int Line)> Lines = [];

    // Cum[i] is the height of lines 0..i-1, including the space between paragraphs and paragraph borders.
    public double[] Cum = [0];

    public int Count => Lines.Count;

    public double Height(int from, int to) => Cum[to] - Cum[from];

    // The space above the first line of paragraph p: its space before, plus the previous paragraph's space after.
    public double GapBefore(int p) => p == 0 ? Paragraphs[0].SpaceBefore : Math.Max(Paragraphs[p - 1].SpaceAfter, Paragraphs[p].SpaceBefore);

    public void Measure()
    {
        var cum = new List<double> { 0 };
        double run = 0;
        for (int p = 0; p < Paragraphs.Count; p++)
        {
            FormattedParagraph fp = Paragraphs[p];
            for (int l = 0; l < fp.Lines.Count; l++)
            {
                Lines.Add((p, l));
                run += fp.Lines[l].Height + (l == 0 ? GapBefore(p) + fp.BorderTopHeight : 0) + (l == fp.Lines.Count - 1 ? fp.BorderBottomHeight : 0);
                cum.Add(run);
            }
        }

        Cum = [.. cum];
    }
}

// Lines [From, To) of a note placed in one column of one page. From > 0 means the note continues from the previous page.
internal readonly record struct NoteSlice(FormattedNote Note, int From, int To)
{
    public bool ContinuesOnNext => To < Note.Count;

    public double Height => Note.Height(From, To);
}

// The notes of one column of the page being laid out.
internal sealed class ColumnNotes
{
    public int Column;
    public double X;
    public double Width;
    public bool BeneathText;
    public List<NoteSlice> Slices = [];
    public double Height; // separator, slices and continuation notice
}

// Formatted notes and separators, kept for the widths of one layout pass.
internal sealed class NoteStore
{
    public Dictionary<(NoteKey Key, int Width), FormattedNote?> Notes { get; } = [];

    public Dictionary<(NoteKind Kind, NoteType Type, int Width), FormattedNote?> Special { get; } = [];

    private Dictionary<NoteKey, Note>? _byId;

    // The note with this id; the notes parts are indexed once because a document can have thousands.
    public Note? Find(Document doc, NoteKey key)
    {
        if (_byId is null)
        {
            _byId = [];
            foreach (NoteKind kind in new[] { NoteKind.Footnote, NoteKind.Endnote })
            {
                foreach (Note n in kind == NoteKind.Footnote ? doc.Footnotes : doc.Endnotes)
                {
                    _byId.TryAdd(new NoteKey(kind, n.Id), n);
                }
            }
        }

        return _byId.GetValueOrDefault(key);
    }
}

internal static class NoteFormatter
{
    // Formats the paragraphs of a note. The note's own mark shows the number its reference shows (ECMA-376 Part 1 §17.11.14).
    public static FormattedNote? Format(LayoutContext ctx, Note note, double width)
    {
        var fn = new FormattedNote { Key = new NoteKey(note.Kind, note.Id) };
        NoteKey? saved = ctx.CurrentNote;
        ctx.CurrentNote = fn.Key;
        try
        {
            Collect(ctx, note.Blocks(), fn.Paragraphs, width, 0);
        }
        finally
        {
            ctx.CurrentNote = saved;
        }

        fn.Measure();
        return fn;
    }

    private static void Collect(LayoutContext ctx, IEnumerable<BlockElement> blocks, List<FormattedParagraph> into, double width, int depth)
    {
        foreach (BlockElement b in blocks)
        {
            switch (b)
            {
                case Paragraph p:
                    into.Add(ParagraphFormatter.Format(p, ctx, width));
                    break;
                case BlockContentControl cc when depth < 16:
                    Collect(ctx, cc.Content.Blocks(), into, width, depth + 1);
                    break;
                case Table:
                    ctx.NoteOnce("layout.note-table-skipped", "layout.note-table-skipped", "A table inside a footnote or endnote is not laid out; its content is left out.", b);
                    break;
            }
        }
    }
}
