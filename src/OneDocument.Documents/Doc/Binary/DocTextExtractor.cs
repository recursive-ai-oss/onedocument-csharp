using System.Text;

namespace RecursiveAI.OneDocument.Doc.Binary;

/// <summary>
/// Extracts the text of a Word binary file without building the document model. It reads the piece table, the paragraph and
/// character property runs (for table structure and deleted text) and the story <c>Plc</c> arrays, and nothing else.
/// </summary>
/// <remarks>
/// The output follows the rules of the other word-processing formats: a paragraph is a line; a table row is one line with the cells
/// separated by a tab and the paragraphs of a cell by a space; a field gives its result and never its instruction; tracked deletions
/// are left out; after the body come headers and footers, footnotes, endnotes and comments, each part set off by a blank line, then
/// the text boxes. Footnote and comment reference marks, pictures and other special characters give no text.
/// </remarks>
public static class DocTextExtractor
{
    /// <summary>Writes the text of an opened file.</summary>
    /// <param name="file">The file.</param>
    /// <param name="output">Where the text goes.</param>
    /// <param name="includeParts">True to write headers, footers, notes and comments after the body.</param>
    /// <param name="cancellationToken">Cancels the extraction.</param>
    public static void Extract(DocFile file, TextWriter output, bool includeParts = true, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(output);
        var reader = new DocReader(file);
        var w = new StoryWriter(file, reader, output, cancellationToken);
        (int ms, int ml) = file.StoryRange(DocStory.Main);
        w.Write(ms, ml, reader.SectionEnds);
        if (!includeParts)
        {
            return;
        }

        bool first = true;
        void Part(DocStory story, Plc plc, int skip, Func<int, bool>? keep = null)
        {
            (int s, int l) = file.StoryRange(story);
            // The text plcs of notes and comments hold one more position than there are items (the end of the story).
            int last = story == DocStory.Headers ? plc.Cps.Length - 1 : plc.Cps.Length - 2;
            for (int i = skip; i < last; i++)
            {
                if (keep is not null && !keep(i))
                {
                    continue;
                }

                int a = plc.Cps[i];
                int b = plc.Cps[i + 1];
                if (a < 0 || b <= a || b > l)
                {
                    continue;
                }

                if (story == DocStory.Headers && b - a <= 1)
                {
                    continue;
                }

                if (!first || w.Wrote)
                {
                    output.Write('\n');
                }

                first = false;
                w.Write(s + a, b - a, null);
            }
        }

        // Headers and footers: the first six entries are the footnote and endnote separators ([MS-DOC] 2.8.22 Plcfhdd).
        bool facing = reader.IsFacingPages;
        var titlePages = new List<bool>();
        for (int s = 0; s < reader.SectionEnds.Count; s++)
        {
            titlePages.Add(reader.SectionProperties(s).Exists(p => p.Sprm == 0x300A && p.OperandByte != 0));
        }

        // Slots of a section: even header, odd header, even footer, odd footer, first header, first footer.
        Part(DocStory.Headers, reader.ReadPlc(DocFib.PlcfHdd, 0), 6, i =>
        {
            int slot = (i - 6) % 6;
            int section = (i - 6) / 6;
            return slot switch
            {
                0 or 2 => facing,
                4 or 5 => section < titlePages.Count && titlePages[section],
                _ => true,
            };
        });
        Part(DocStory.Footnotes, reader.ReadPlc(DocFib.PlcffndTxt, 0), 0);
        Part(DocStory.Endnotes, reader.ReadPlc(DocFib.PlcfendTxt, 0), 0);
        Part(DocStory.Comments, reader.ReadPlc(DocFib.PlcfandTxt, 0), 0);
        foreach (DocStory s in (DocStory[])[DocStory.TextBoxes, DocStory.HeaderTextBoxes])
        {
            (int st, int len) = file.StoryRange(s);
            if (len > 1)
            {
                output.Write('\n');
                w.Write(st, len, null);
            }
        }
    }

    private sealed class StoryWriter(DocFile file, DocReader reader, TextWriter output, CancellationToken token)
    {
        private readonly List<bool> _fields = [];
        private readonly StringBuilder _para = new();
        private readonly StringBuilder _cell = new();
        private readonly List<string> _row = [];

        public bool Wrote { get; private set; }

        public void Write(int start, int length, IReadOnlyCollection<int>? sectionEnds)
        {
            List<DocParagraph> paragraphs = reader.Paragraphs(start, length, sectionEnds);
            string text = file.Text;
            foreach (DocParagraph p in paragraphs)
            {
                token.ThrowIfCancellationRequested();
                bool inCell = p.Itap > 0;
                bool markDeleted = Scan(text, p, inCell);
                if (!inCell)
                {
                    if (!markDeleted || p.Mark >= text.Length)
                    {
                        output.Write(_para.ToString());
                        output.Write('\n');
                        Wrote = true;
                        _para.Clear();
                    }

                    continue;
                }

                if (_cell.Length > 0 && _para.Length > 0)
                {
                    _cell.Append(' ');
                }

                _cell.Append(_para);
                _para.Clear();
                if (p.Itap == 1 && p.IsCellEnd)
                {
                    _row.Add(_cell.ToString());
                    _cell.Clear();
                }
                else if (p.Itap == 1 && p.IsRowEnd)
                {
                    output.Write(string.Join('\t', _row));
                    output.Write('\n');
                    Wrote = true;
                    _row.Clear();
                    _cell.Clear();
                }
            }

            if (_row.Count > 0 || _cell.Length > 0)
            {
                // A table cut off by the end of the story.
                if (_cell.Length > 0)
                {
                    _row.Add(_cell.ToString());
                }

                output.Write(string.Join('\t', _row));
                output.Write('\n');
                Wrote = true;
                _row.Clear();
                _cell.Clear();
            }
        }

        // Appends the visible text of a paragraph to _para; returns true if its mark is a tracked deletion.
        private bool Scan(string text, DocParagraph p, bool inCell)
        {
            int cp = p.Start;
            int end = Math.Min(p.Mark, text.Length);
            while (cp < end)
            {
                List<Prl> props = reader.CharacterProperties(cp, end, out int runEnd);
                bool deleted = false;
                foreach (Prl pr in props)
                {
                    if (pr.Sprm == 0x0800 && pr.OperandByte == 1)
                    {
                        deleted = true;
                    }
                }

                for (int i = cp; i < runEnd; i++)
                {
                    char c = text[i];
                    // Field characters are structure even inside deleted text, so the nesting stays balanced.
                    if (c == '\u0013')
                    {
                        _fields.Add(true);
                        continue;
                    }

                    if (c == '\u0014')
                    {
                        if (_fields.Count > 0)
                        {
                            _fields[^1] = false;
                        }

                        continue;
                    }

                    if (c == '\u0015')
                    {
                        if (_fields.Count > 0)
                        {
                            _fields.RemoveAt(_fields.Count - 1);
                        }

                        continue;
                    }

                    if (deleted || _fields.Contains(true))
                    {
                        continue;
                    }

                    switch (c)
                    {
                        case '\t':
                            _para.Append('\t');
                            break;
                        case '\v':
                        case '\f':
                        case '\u000E':
                            _para.Append(inCell ? ' ' : '\n');
                            break;
                        case '\u001E':
                            _para.Append('-');
                            break;
                        default:
                            if (c >= ' ')
                            {
                                _para.Append(c);
                            }

                            break;
                    }
                }

                cp = runEnd;
            }

            if (p.Mark < text.Length)
            {
                foreach (Prl pr in reader.CharacterProperties(p.Mark, p.Mark + 1, out _))
                {
                    if (pr.Sprm == 0x0800 && pr.OperandByte == 1)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
