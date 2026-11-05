using RecursiveAI.OneDocument.Documents;
using RecursiveAI.OneDocument.Documents.Notes;

namespace RecursiveAI.OneDocument.Layout;

// Footnotes and endnotes on the page model.
//
// Footnotes: a reference in a body line asks the page for room at its bottom (or at the bottom of its column). The line stays on the page
// only when the page can also hold the start of the note: ECMA-376 Part 1 §17.11 describes the note parts but not this rule, which
// follows Word's behaviour (the reference line moves on when not even the first line of its note fits). A note that does not fit whole
// is cut between lines, the rest is carried to the next column or page behind a continuation separator, and the page that holds the
// start gets the continuation notice. The note area is built when the page is closed.
//
// Endnotes: laid out in the flow after the last body text of their section (sectEnd) or of the document (docEnd), behind the endnote
// separator, through the same placement as body paragraphs.
public static partial class LayoutEngine
{
    private sealed partial class Flow
    {
        private const double DefaultSeparatorHeight = 12; // used when the notes part has no separator note
        private const double MinBodyHeight = 24; // body space kept when notes carried from the previous page would fill the page
        private const double Slack = 0.01;

        private Dictionary<int, ColumnNotes>? _colNotes;
        private readonly List<NoteSlice> _carry = [];
        private readonly HashSet<NoteKey> _placedNotes = [];
        private readonly List<long> _docEndnotes = [];
        private readonly List<long> _deferredEndnotes = [];
        private readonly Dictionary<FormattedRow, NoteKey[]> _rowRefs = [];
        private double _baseBottom; // the bottom of the column's text before the notes of the page take their share

        private bool NotesActive => !probe && ctx.NotePlan.HasFootnotes;

        // ---- limits ----

        // Sets the bottom the column's text may reach without notes and lowers the working limit by the notes already on the column.
        private void SetBase(double bottom)
        {
            _baseBottom = bottom;
            _limitBottom = bottom - (_colNotes is not null && _colNotes.TryGetValue(_colIndex, out ColumnNotes? cn) ? cn.Height : 0);
        }

        private ColumnNotes CurrentNotes()
        {
            _colNotes ??= [];
            if (!_colNotes.TryGetValue(_colIndex, out ColumnNotes? cn))
            {
                cn = new ColumnNotes { Column = _colIndex, X = _colX, Width = _textWidth };
                _colNotes[_colIndex] = cn;
            }

            return cn;
        }

        // Called when a column is entered: notes carried over from the previous column or page come first.
        private void NotesEnterColumn()
        {
            if (!NotesActive)
            {
                return;
            }

            ColumnNotes cn = CurrentNotes();
            if (_carry.Count > 0)
            {
                PlaceCarry(cn);
            }

            _limitBottom = _baseBottom - cn.Height;
        }

        private void PlaceCarry(ColumnNotes cn)
        {
            double room = _baseBottom - _regionTop - MinBodyHeight;
            while (_carry.Count > 0)
            {
                NoteSlice c = _carry[0];
                int to = MaxLines(cn, c.Note, c.From, room);
                if (to <= c.From)
                {
                    if (cn.Slices.Count > 0)
                    {
                        break;
                    }

                    to = c.From + 1; // an empty column always takes one line, so a long note ends
                }

                cn.Slices.Add(new NoteSlice(c.Note, c.From, to));
                cn.Height = NotesHeight(cn, cn.Slices);
                if (to < c.Note.Count)
                {
                    _carry[0] = new NoteSlice(c.Note, to, c.Note.Count);
                    break;
                }

                _carry.RemoveAt(0);
            }
        }

        // ---- formatting ----

        private FormattedNote? GetNote(NoteKey key, double width)
        {
            (NoteKey, int) k = (key, (int)Math.Round(width * 10));
            if (ctx.NoteStore.Notes.TryGetValue(k, out FormattedNote? cached))
            {
                return cached;
            }

            Note? n = ctx.NoteStore.Find(ctx.Doc, key);
            FormattedNote? fn = n is { Type: NoteType.Normal } ? NoteFormatter.Format(ctx, n, width) : null;
            ctx.NoteStore.Notes[k] = fn;
            return fn;
        }

        private FormattedNote? GetSpecial(NoteKind kind, NoteType type, double width)
        {
            (NoteKind, NoteType, int) k = (kind, type, (int)Math.Round(width * 10));
            if (ctx.NoteStore.Special.TryGetValue(k, out FormattedNote? cached))
            {
                return cached;
            }

            FormattedNote? fn = null;
            foreach (Note n in kind == NoteKind.Footnote ? ctx.Doc.Footnotes : ctx.Doc.Endnotes)
            {
                if (n.Type == type)
                {
                    fn = NoteFormatter.Format(ctx, n, width);
                    break;
                }
            }

            ctx.NoteStore.Special[k] = fn;
            return fn;
        }

        private double SeparatorHeight(double width, bool continuation) =>
            GetSpecial(NoteKind.Footnote, continuation ? NoteType.ContinuationSeparator : NoteType.Separator, width) is { } fn ? fn.Height(0, fn.Count) : DefaultSeparatorHeight;

        private double NoticeHeight(double width) =>
            GetSpecial(NoteKind.Footnote, NoteType.ContinuationNotice, width) is { } fn && fn.Paragraphs.Any(static p => p.Text.Length > 0) ? fn.Height(0, fn.Count) : 0;

        // The height of the note area of a column holding these slices: the separator (the continuation one when the first note carries
        // over), the notes, and the continuation notice when the last one goes on.
        private double NotesHeight(ColumnNotes cn, List<NoteSlice> slices)
        {
            if (slices.Count == 0)
            {
                return 0;
            }

            double h = SeparatorHeight(cn.Width, slices[0].From > 0);
            foreach (NoteSlice s in slices)
            {
                h += s.Height;
            }

            if (slices[^1].ContinuesOnNext)
            {
                h += NoticeHeight(cn.Width);
            }

            return h;
        }

        // The most lines of the note, from line `from`, that the column can hold within `room` points of note area; `from` when none.
        private int MaxLines(ColumnNotes cn, FormattedNote note, int from, double room)
        {
            bool Fits(int to)
            {
                var all = new List<NoteSlice>(cn.Slices) { new(note, from, to) };
                return NotesHeight(cn, all) <= room + Slack;
            }

            if (note.Count == 0 || Fits(note.Count))
            {
                return note.Count;
            }

            int lo = from;
            int hi = note.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (Fits(mid))
                {
                    lo = mid;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            return lo;
        }

        // ---- references ----

        private List<NoteKey> RefsOf(FormattedLine line)
        {
            var list = new List<NoteKey>();
            AddRefs(line, list);
            return list;
        }

        private void AddRefs(FormattedLine line, List<NoteKey> into)
        {
            foreach (LineItem item in line.Items)
            {
                if (item.Source is { } run && ctx.NoteRefs.TryGetValue(run, out NoteKey key) && !into.Contains(key))
                {
                    into.Add(key);
                }
            }
        }

        // How many lines of a paragraph, from `from`, fit above the bottom when each line's footnotes have to fit with it. A line whose
        // note cannot even start on the page does not count, so it moves to the next page with its reference.
        private int FitLinesNotes(FormattedParagraph fp, int from, double gapApplied)
        {
            double top = _y + gapApplied;
            double h = from == 0 ? fp.BorderTopHeight : 0;
            ColumnNotes cn = CurrentNotes();
            var pending = new List<NoteSlice>(cn.Slices);
            var seen = new HashSet<NoteKey>();
            int n = 0;
            for (int i = from; i < fp.Lines.Count; i++)
            {
                h += fp.Lines[i].Height;
                double bottom = top + h;
                var whole = new List<NoteSlice>();
                var starts = new List<NoteSlice>();
                foreach (NoteKey key in RefsOf(fp.Lines[i]))
                {
                    if (key.Kind == NoteKind.Footnote && !_placedNotes.Contains(key) && seen.Add(key) && GetNote(key, cn.Width) is { Count: > 0 } note)
                    {
                        whole.Add(new NoteSlice(note, 0, note.Count));
                        starts.Add(new NoteSlice(note, 0, 1));
                    }
                }

                var withWhole = new List<NoteSlice>(pending);
                withWhole.AddRange(whole);
                if (bottom + NotesHeight(cn, withWhole) <= _baseBottom + Slack)
                {
                    pending = withWhole;
                    n++;
                    continue;
                }

                if (whole.Count > 0)
                {
                    var withStarts = new List<NoteSlice>(pending);
                    withStarts.AddRange(starts);
                    if (bottom + NotesHeight(cn, withStarts) <= _baseBottom + Slack)
                    {
                        n++; // this line is the last: its note takes the rest of the page and goes on
                    }
                }

                break;
            }

            return n;
        }

        // After a paragraph's lines are placed: notes for the references on them are put on the page, and the page of each reference is
        // recorded for the next pass (numbers that restart on each page).
        private void CommitNoteRefs(FormattedParagraph fp, int from, LayoutParagraph lp)
        {
            int n = 0;
            foreach (LayoutLine line in lp.Lines)
            {
                FormattedLine fl = fp.Lines[from + n++];
                CommitRefs(RefsOf(fl), line.Baseline + line.Descent);
            }
        }

        private void CommitRefs(List<NoteKey> keys, double bottom)
        {
            foreach (NoteKey key in keys)
            {
                ctx.NoteRefPages.TryAdd(key, _page!.Number);
                if (key.Kind == NoteKind.Footnote && NotesActive)
                {
                    ReserveNote(key, bottom);
                }
            }
        }

        // Puts a footnote on the current column: whole when it fits under `lineBottom`, else as many lines as fit (the rest is carried to
        // the next column or page), else nothing but a carry when not even its first line fits (a line forced onto an empty page).
        private void ReserveNote(NoteKey key, double lineBottom)
        {
            if (!_placedNotes.Add(key))
            {
                return;
            }

            ColumnNotes cn = CurrentNotes();
            if (GetNote(key, cn.Width) is not { Count: > 0 } note)
            {
                return;
            }

            int to = MaxLines(cn, note, 0, _baseBottom - lineBottom);
            if (to > 0)
            {
                if (cn.Slices.Count == 0 && ctx.NotePlan.TryGet(NoteKind.Footnote, key.Id, out NoteMark mark))
                {
                    // ECMA-376 Part 1 §17.11 (pos): beneathText puts the notes right under the last line of text.
                    int s = Math.Min(mark.Section, ctx.NotePlan.Footnote.Length - 1);
                    cn.BeneathText = s >= 0 && ctx.NotePlan.Footnote[s].Position == NotePosition.BeneathText;
                }

                cn.Slices.Add(new NoteSlice(note, 0, to));
            }

            if (to < note.Count)
            {
                _carry.Add(new NoteSlice(note, to, note.Count));
            }

            cn.Height = NotesHeight(cn, cn.Slices);
            _limitBottom = _baseBottom - cn.Height;
        }

        // ---- tables ----

        private NoteKey[] RowRefs(FormattedRow row)
        {
            if (_rowRefs.TryGetValue(row, out NoteKey[]? cached))
            {
                return cached;
            }

            var list = new List<NoteKey>();
            foreach (FormattedCell c in row.Starting)
            {
                CellRefs(c, list, 0);
            }

            NoteKey[] result = [.. list];
            _rowRefs[row] = result;
            return result;
        }

        private void CellRefs(FormattedCell cell, List<NoteKey> into, int depth)
        {
            foreach (CellItem item in cell.Items)
            {
                if (item.Paragraph is { } fp)
                {
                    foreach (FormattedLine fl in fp.Lines)
                    {
                        AddRefs(fl, into);
                    }
                }
                else if (item.Table is { } nested && depth < 8)
                {
                    foreach (FormattedRow r in nested.Rows)
                    {
                        foreach (FormattedCell c in r.Starting)
                        {
                            CellRefs(c, into, depth + 1);
                        }
                    }
                }
            }
        }

        // The room the footnotes of a table part and of the rows r0..r1 that may join it take from the page. A row's notes go with the page
        // its row starts on, even when the row is cut across pages.
        private double TableNotesExtra(FormattedTable ft, Part part, int r0, int r1)
        {
            if (!NotesActive || ctx.NoteRefs.Count == 0)
            {
                return 0;
            }

            ColumnNotes cn = CurrentNotes();
            var slices = new List<NoteSlice>(cn.Slices);
            var seen = new HashSet<NoteKey>();
            void AddRow(FormattedRow row)
            {
                foreach (NoteKey key in RowRefs(row))
                {
                    if (key.Kind == NoteKind.Footnote && !_placedNotes.Contains(key) && seen.Add(key) && GetNote(key, cn.Width) is { Count: > 0 } note)
                    {
                        slices.Add(new NoteSlice(note, 0, note.Count));
                    }
                }
            }

            foreach (SliceRow sr in part.Rows)
            {
                if (!sr.Repeated)
                {
                    AddRow(ft.Rows[sr.Row]);
                }
            }

            for (int r = r0; r <= r1 && r < ft.Rows.Count; r++)
            {
                AddRow(ft.Rows[r]);
            }

            return NotesHeight(cn, slices) - cn.Height;
        }

        private void CommitTableNotes(FormattedTable ft, Part part)
        {
            if (ctx.NoteRefs.Count == 0)
            {
                return;
            }

            foreach (SliceRow sr in part.Rows)
            {
                if (!sr.Repeated)
                {
                    CommitRefs([.. RowRefs(ft.Rows[sr.Row])], _y + part.Height);
                }
            }
        }

        // ---- the note area of a closed page ----

        private void EmitNotes(LayoutPage page)
        {
            if (_colNotes is null)
            {
                return;
            }

            double reserve = 0;
            foreach (ColumnNotes cn in _colNotes.Values.OrderBy(static c => c.Column))
            {
                if (cn.Slices.Count == 0)
                {
                    continue;
                }

                reserve = Math.Max(reserve, cn.Height);
                double y = _bodyBottom - cn.Height;
                if (cn.BeneathText)
                {
                    y = Math.Min(y, BodyBottom(page, cn.Column));
                }

                y = EmitSpecial(page, cn, cn.Slices[0].From > 0 ? LayoutNoteRole.ContinuationSeparator : LayoutNoteRole.Separator, y);
                foreach (NoteSlice s in cn.Slices)
                {
                    y = EmitSlice(page, cn, s, y);
                }

                if (cn.Slices[^1].ContinuesOnNext)
                {
                    EmitSpecial(page, cn, LayoutNoteRole.ContinuationNotice, y);
                }
            }

            page.NotesReservation = reserve;
            if (reserve > 0)
            {
                page.ContentArea = page.ContentArea with { Height = Math.Max(0, page.ContentArea.Height - reserve) };
            }
        }

        private static double BodyBottom(LayoutPage page, int column)
        {
            double b = page.ContentArea.Y;
            foreach (LayoutNode n in page.Children)
            {
                if (n is LayoutParagraph { Story: LayoutStory.Body } p && p.Column == column)
                {
                    b = Math.Max(b, p.Bounds.Bottom);
                }
                else if (n is LayoutTable t)
                {
                    b = Math.Max(b, t.Bounds.Bottom);
                }
            }

            return b;
        }

        private double EmitSpecial(LayoutPage page, ColumnNotes cn, LayoutNoteRole role, double y)
        {
            NoteType type = role switch
            {
                LayoutNoteRole.ContinuationSeparator => NoteType.ContinuationSeparator,
                LayoutNoteRole.ContinuationNotice => NoteType.ContinuationNotice,
                _ => NoteType.Separator,
            };
            bool rule = role != LayoutNoteRole.ContinuationNotice;
            FormattedNote? fn = GetSpecial(NoteKind.Footnote, type, cn.Width);
            if (fn is null)
            {
                if (rule)
                {
                    AddRule(page, role, cn.X, cn.Width, y + (DefaultSeparatorHeight * 0.6));
                    return y + DefaultSeparatorHeight;
                }

                return y;
            }

            if (!rule && !fn.Paragraphs.Any(static p => p.Text.Length > 0))
            {
                return y;
            }

            var block = new LayoutNoteBlock(role) { Column = cn.Column, NoteId = fn.Key.Id };
            double start = y;
            for (int p = 0; p < fn.Paragraphs.Count; p++)
            {
                FormattedParagraph fp = fn.Paragraphs[p];
                LayoutParagraph lp = ParagraphEmitter.Build(fp, 0, fp.Lines.Count, cn.X, y + fn.GapBefore(p), true, true, out double bottom, out _);
                lp.Story = LayoutStory.Footnote;
                lp.Column = cn.Column;
                lp.NoteRole = role;
                lp.NoteId = fn.Key.Id;
                block.Add(lp);
                if (rule && p == 0 && lp.Lines.FirstOrDefault() is { } line)
                {
                    AddRule(page, role, cn.X, cn.Width, line.Baseline - (line.Ascent * 0.3));
                }

                y = bottom;
            }

            block.Bounds = new LayoutRect(cn.X, start, cn.Width, y - start);
            page.Add(block);
            return y;
        }

        // ECMA-376 Part 1 §17.11 (separator), §17.11 (continuationSeparator): the separator character draws a rule. Word's separator is
        // about a third of the column wide and the continuation separator spans it (to check against Word).
        private static void AddRule(LayoutPage page, LayoutNoteRole role, double x, double width, double y)
        {
            bool full = role == LayoutNoteRole.ContinuationSeparator;
            page.Add(new LayoutDecoration(full ? LayoutDecorationKind.NoteContinuationSeparator : LayoutDecorationKind.NoteSeparator)
            {
                Bounds = new LayoutRect(x, y, full ? width : width / 3, 0.75),
                Color = 0,
            });
        }

        private double EmitSlice(LayoutPage page, ColumnNotes cn, NoteSlice slice, double y)
        {
            FormattedNote note = slice.Note;
            var block = new LayoutNoteBlock(LayoutNoteRole.Footnote)
            {
                Column = cn.Column,
                NoteId = note.Key.Id,
                Mark = ctx.NotePlan.TryGet(NoteKind.Footnote, note.Key.Id, out NoteMark mark) ? mark.Text : string.Empty,
                ContinuesFromPreviousPage = slice.From > 0,
                ContinuesOnNextPage = slice.ContinuesOnNext,
            };
            double start = y;
            int idx = slice.From;
            while (idx < slice.To)
            {
                (int p, int first) = note.Lines[idx];
                int end = idx;
                while (end < slice.To && note.Lines[end].Para == p)
                {
                    end++;
                }

                FormattedParagraph fp = note.Paragraphs[p];
                bool isFirst = first == 0;
                bool isLast = note.Lines[end - 1].Line == fp.Lines.Count - 1;
                LayoutParagraph lp = ParagraphEmitter.Build(fp, first, end - idx, cn.X, y + (isFirst ? note.GapBefore(p) : 0), isFirst, isLast, out double bottom, out _);
                lp.Story = LayoutStory.Footnote;
                lp.Column = cn.Column;
                lp.NoteRole = LayoutNoteRole.Footnote;
                lp.NoteId = note.Key.Id;
                block.Add(lp);
                y = bottom;
                idx = end;
            }

            block.Bounds = new LayoutRect(cn.X, start, cn.Width, y - start);
            page.Add(block);
            return y;
        }

        // ---- endnotes ----

        // Adds the endnotes that end in this section to its blocks. ECMA-376 Part 1 §17.11 (pos): sectEnd prints a section's endnotes
        // after its text, docEnd (and any footnote-only position) after the last section. §17.6 (noEndnote): a section that suppresses
        // them hands its endnotes on to the next section that prints.
        private void AppendEndnotes(Section section, List<FormattedParagraph> blocks)
        {
            NotePlan plan = ctx.NotePlan;
            if (!plan.HasEndnotes || probe || section.Index >= plan.Endnote.Length)
            {
                return;
            }

            List<long> own = plan.EndnotesBySection[section.Index];
            bool sectionEnd = plan.Endnote[section.Index].Position == NotePosition.SectionEnd;
            if (sectionEnd)
            {
                _deferredEndnotes.AddRange(own);
                if (!section.SuppressEndnotes)
                {
                    AddEndnoteBlocks(_deferredEndnotes, blocks);
                    _deferredEndnotes.Clear();
                }
            }
            else
            {
                _docEndnotes.AddRange(own);
            }

            if (section.IsLast)
            {
                List<long> rest = [.. _deferredEndnotes, .. _docEndnotes];
                _deferredEndnotes.Clear();
                _docEndnotes.Clear();
                AddEndnoteBlocks(rest, blocks);
            }
        }

        private void AddEndnoteBlocks(List<long> ids, List<FormattedParagraph> blocks)
        {
            if (ids.Count == 0)
            {
                return;
            }

            // Fresh formats: the placement below changes these paragraphs (keep-with-next, re-cutting for another column width).
            Note? separator = null;
            foreach (Note n in ctx.Doc.Endnotes)
            {
                if (n.Type == NoteType.Separator)
                {
                    separator = n;
                    break;
                }
            }

            if (separator is not null && NoteFormatter.Format(ctx, separator, _textWidth) is { } sep)
            {
                foreach (FormattedParagraph fp in sep.Paragraphs)
                {
                    fp.NoteRole = LayoutNoteRole.Separator;
                    fp.NoteId = sep.Key.Id;
                    fp.KeepNext = true;
                    blocks.Add(fp);
                }
            }

            foreach (long id in ids)
            {
                if (ctx.NoteStore.Find(ctx.Doc, new NoteKey(NoteKind.Endnote, id)) is not { Type: NoteType.Normal } note || NoteFormatter.Format(ctx, note, _textWidth) is not { } fn)
                {
                    continue;
                }

                foreach (FormattedParagraph fp in fn.Paragraphs)
                {
                    fp.NoteRole = LayoutNoteRole.Endnote;
                    fp.NoteId = id;
                    blocks.Add(fp);
                }
            }
        }

        // The rule above the endnotes is drawn with the separator paragraph that holds it.
        private void AddEndnoteRule(LayoutPage page, LayoutParagraph lp)
        {
            if (lp.Lines.FirstOrDefault() is { } line)
            {
                AddRule(page, LayoutNoteRole.Separator, _colX, _textWidth, line.Baseline - (line.Ascent * 0.3));
            }
        }
    }
}
