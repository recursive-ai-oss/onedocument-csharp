using System.Globalization;
using RecursiveAI.OneDocument.Calc;
using RecursiveAI.OneDocument.Markup;
using RecursiveAI.OneDocument.Operations;

namespace RecursiveAI.OneDocument.Spreadsheets;

/// <summary>What <see cref="Workbook.Find(string, WorkbookFindOptions?)"/> searches and how.</summary>
public class WorkbookFindOptions
{
    /// <summary>Gets how the pattern is read. Default: literal, case-insensitive.</summary>
    public SearchOptions Search { get; init; } = new();

    /// <summary>Gets a value indicating whether the text of string cells is searched. Default true. Number, boolean and error cells are not searched, and neither are cells that hold a formula (their text is a cached result).</summary>
    public bool Values { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether formula text is searched (the formula as stored, without the leading <c>=</c>). Default false.
    /// A replacement in a formula is written only when the new text parses as a formula; otherwise the match is reported as
    /// <c>formula-invalid</c> and the cell is left as it was.
    /// </summary>
    public bool Formulas { get; init; }

    /// <summary>Gets the names of the sheets to search (compared without regard to case); null (the default) searches every worksheet, hidden ones included.</summary>
    public IReadOnlyCollection<string>? Sheets { get; init; }
}

/// <summary>What <see cref="Workbook.Replace(string, string, WorkbookReplaceOptions?)"/> does; currently the same choices as finding.</summary>
public sealed class WorkbookReplaceOptions : WorkbookFindOptions
{
}

public sealed partial class Workbook
{
    /// <summary>
    /// Finds text in the cells of the worksheets, sheet by sheet, in row order. A match stays inside one cell. Rich text is searched as the
    /// plain text of its runs, so a match can cross a change of formatting.
    /// </summary>
    /// <param name="pattern">The text, or the regular expression when <see cref="SearchOptions.UseRegex"/> is set.</param>
    /// <param name="options">What to search; null searches the text of every worksheet.</param>
    /// <returns>The hits; the location names the sheet and the cell.</returns>
    /// <exception cref="ArgumentException">The pattern is empty or not a valid regular expression.</exception>
    /// <exception cref="OfficeLimitException">A regular expression ran past <see cref="OfficeLimits.RegexMatchTimeoutMilliseconds"/>.</exception>
    public IReadOnlyList<TextHit> Find(string pattern, WorkbookFindOptions? options = null)
    {
        options ??= new WorkbookFindOptions();
        TextSearch search = TextSearch.Create(pattern, options.Search, Limits);
        var hits = new List<TextHit>();
        foreach (CellCandidate c in Candidates(options, search))
        {
            foreach (TextMatch m in c.Matches)
            {
                hits.Add(new TextHit(c.Location(m.Index), m.Value));
            }
        }

        return hits;
    }

    /// <summary>
    /// Replaces every match in the cells. A string cell keeps its style; a cell with rich text keeps the formatting of its runs, with the new
    /// text taking the formatting of the run where the match began. In a formula (with <see cref="WorkbookFindOptions.Formulas"/>) the new
    /// text must parse as a formula or nothing is written. Replacing in a formula discards the cell's cached value and marks the workbook for
    /// recalculation, as assigning a formula does; nothing is calculated.
    /// </summary>
    /// <param name="pattern">The text, or the regular expression when <see cref="SearchOptions.UseRegex"/> is set.</param>
    /// <param name="replacement">The new text. For a regular-expression search <c>$1</c> and <c>${name}</c> insert groups.</param>
    /// <param name="options">What to search; null replaces in the text of every worksheet.</param>
    /// <returns>
    /// Each replacement with its sheet and cell, and each match left alone with a reason: <c>formula-invalid</c>, <c>formula-too-long</c>,
    /// <c>formula-shared</c> (the text of a shared formula serves other cells too), <c>rich-text-unsupported</c>.
    /// </returns>
    /// <exception cref="ArgumentException">The pattern is empty or invalid.</exception>
    /// <exception cref="OfficeLimitException">A regular expression ran past <see cref="OfficeLimits.RegexMatchTimeoutMilliseconds"/>; cells before the one that timed out keep their replacements.</exception>
    public ReplaceReport Replace(string pattern, string replacement, WorkbookReplaceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        options ??= new WorkbookReplaceOptions();
        TextSearch search = TextSearch.Create(pattern, options.Search, Limits);
        var report = new ReplaceReport();
        foreach (CellCandidate c in Candidates(options, search))
        {
            if (c.IsFormula)
            {
                ReplaceInFormula(c, search, replacement, report);
            }
            else
            {
                ReplaceInText(c, search, replacement, report);
            }
        }

        return report;
    }

    private sealed class CellCandidate
    {
        public required Worksheet Sheet { get; init; }

        public required int Row { get; init; }

        public required int Column { get; init; }

        public required string Text { get; init; }

        public required IReadOnlyList<TextMatch> Matches { get; init; }

        public bool IsFormula { get; init; }

        public CellFormula? Formula { get; init; }

        public int SharedStringIndex { get; init; } = -1;

        public bool InlineRich { get; init; }

        public string Address => new CellAddress(Row, Column).ToString();

        public TextLocation Location(int offset) => new(Sheet.Name, IsFormula ? "formula" : "cell", 0, offset, Address);
    }

    // Candidates are collected before any cell changes, so the store is never edited while it is being walked.
    private List<CellCandidate> Candidates(WorkbookFindOptions options, TextSearch search)
    {
        var list = new List<CellCandidate>();
        HashSet<string>? wanted = options.Sheets is null ? null : new HashSet<string>(options.Sheets, StringComparer.OrdinalIgnoreCase);
        foreach (Worksheet ws in Sheets)
        {
            if (ws.Kind != SheetKind.Worksheet || (wanted is not null && !wanted.Contains(ws.Name)))
            {
                continue;
            }

            foreach (CellRowView row in ws.Store.EnumerateRows())
            {
                for (int i = 0; i < row.Count; i++)
                {
                    CellValue v = row.ValueAt(i);
                    bool hasFormula = row.HasFormulaAt(i);
                    if (hasFormula)
                    {
                        if (!options.Formulas || row.FormulaAt(i) is not { } f || f.Text.Length == 0 || f.Kind == CellFormulaKind.DataTable)
                        {
                            continue;
                        }

                        IReadOnlyList<TextMatch> fm = search.Find(f.Text);
                        if (fm.Count > 0)
                        {
                            list.Add(new CellCandidate { Sheet = ws, Row = row.Row, Column = row.ColumnAt(i), Text = f.Text, Matches = fm, IsFormula = true, Formula = f });
                        }

                        continue;
                    }

                    if (!options.Values || v.Kind != CellValueKind.Text)
                    {
                        continue;
                    }

                    string text = ws.GetText(v);
                    IReadOnlyList<TextMatch> m = search.Find(text);
                    if (m.Count > 0)
                    {
                        int sst = v.StringRef >= 0 ? v.StringRef : -1;
                        list.Add(new CellCandidate
                        {
                            Sheet = ws,
                            Row = row.Row,
                            Column = row.ColumnAt(i),
                            Text = text,
                            Matches = m,
                            SharedStringIndex = sst,
                            InlineRich = sst < 0 && ws.Store.PoolAt(v.StringRef).RawInline is { } raw && XString.HasRuns(raw),
                        });
                    }
                }
            }
        }

        return list;
    }

    private void ReplaceInText(CellCandidate c, TextSearch search, string replacement, ReplaceReport report)
    {
        if (c.InlineRich)
        {
            foreach (TextMatch m in c.Matches)
            {
                report.Add(new ReplaceIssue("rich-text-unsupported", "The cell holds rich text stored inline; it was not changed.", c.Location(m.Index)));
            }

            return;
        }

        var expanded = new string[c.Matches.Count];
        for (int i = 0; i < expanded.Length; i++)
        {
            expanded[i] = search.Expand(c.Matches[i], replacement);
        }

        Cell cell = c.Sheet[c.Row, c.Column];
        if (c.SharedStringIndex >= 0 && SharedStrings.IsRich(c.SharedStringIndex))
        {
            if (!RichStringEditor.TryReplace(SharedStrings.GetItemXml(c.SharedStringIndex).ToArray(), c.Matches, expanded, out string plain, out byte[] xml))
            {
                foreach (TextMatch m in c.Matches)
                {
                    report.Add(new ReplaceIssue("rich-text-unsupported", "The rich text of the cell has escapes or markup that cannot be edited safely; it was not changed.", c.Location(m.Index)));
                }

                return;
            }

            SharedStringTable sst = SharedStrings;
            int index = sst.AddItem(plain, xml);
            sst.NoteReference(1);
            c.Sheet.Store.SetValue(c.Row, c.Column, new CellValue(CellValueKind.Text, 0, index));
        }
        else
        {
            cell.SetString(ApplyAll(c.Text, c.Matches, expanded));
        }

        for (int i = 0; i < expanded.Length; i++)
        {
            report.Add(new ReplacementItem(c.Location(c.Matches[i].Index), c.Matches[i].Value, expanded[i], false));
        }
    }

    private void ReplaceInFormula(CellCandidate c, TextSearch search, string replacement, ReplaceReport report)
    {
        CellFormula f = c.Formula!;
        void Refuse(string code, string message)
        {
            foreach (TextMatch m in c.Matches)
            {
                report.Add(new ReplaceIssue(code, message, c.Location(m.Index)));
            }
        }

        if (f.Kind == CellFormulaKind.Shared)
        {
            Refuse("formula-shared", "The formula is shared with other cells; it was not changed.");
            return;
        }

        var expanded = new string[c.Matches.Count];
        for (int i = 0; i < expanded.Length; i++)
        {
            expanded[i] = search.Expand(c.Matches[i], replacement);
        }

        string updated = ApplyAll(c.Text, c.Matches, expanded);
        if (updated.Length > Limits.MaxFormulaLength)
        {
            Refuse("formula-too-long", "The new formula would be longer than " + Limits.MaxFormulaLength.ToString(CultureInfo.InvariantCulture) + " characters; it was not changed.");
            return;
        }

        // The new text must read as a formula in the file's own spelling; if it does not, the cell is left exactly as it was.
        if (!FormulaParser.TryParse(updated, out _, out FormulaSyntaxError error, new FormulaParserOptions { FileForm = true, Limits = Limits }))
        {
            Refuse("formula-invalid", "The new formula does not parse (" + error.Message + "); it was not changed.");
            return;
        }

        c.Sheet[c.Row, c.Column].FormulaDetail = f with { Text = updated };
        for (int i = 0; i < expanded.Length; i++)
        {
            report.Add(new ReplacementItem(c.Location(c.Matches[i].Index), c.Matches[i].Value, expanded[i], false));
        }
    }

    private static string ApplyAll(string text, IReadOnlyList<TextMatch> matches, string[] replacements)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        int at = 0;
        for (int i = 0; i < matches.Count; i++)
        {
            sb.Append(text, at, matches[i].Index - at).Append(replacements[i]);
            at = matches[i].Index + matches[i].Length;
        }

        sb.Append(text, at, text.Length - at);
        return sb.ToString();
    }
}

/// <summary>Edits the runs of a rich-text shared string, keeping the formatting of each run the match does not replace.</summary>
internal static class RichStringEditor
{
    // ECMA-376 Part 1 §18.4.8 (si): text is a t child or the t of each r child; rPh (phonetic) runs are not part of the text.
    internal static bool TryReplace(byte[] item, IReadOnlyList<TextMatch> matches, string[] replacements, out string plain, out byte[] xml)
    {
        plain = string.Empty;
        xml = item;
        if (!MarkupDocument.TryParse(item, out MarkupDocument? doc, null) || doc is null || doc.Root.IsNull)
        {
            return false;
        }

        List<MarkupNode> atoms = Atoms(doc.Root);
        foreach (MarkupNode t in atoms)
        {
            if (XString.NeedsDecoding(t.InnerText))
            {
                return false;
            }
        }

        for (int k = matches.Count - 1; k >= 0; k--)
        {
            int start = matches[k].Index;
            int end = start + matches[k].Length;
            string repl = XString.Encode(replacements[k]);
            int pos = 0;
            bool placed = false;
            var emptied = new List<MarkupNode>();
            foreach (MarkupNode t in atoms)
            {
                string s = t.InnerText;
                int a = pos;
                int b = pos + s.Length;
                pos = b;
                if (b <= start || a >= end)
                {
                    continue;
                }

                int from = Math.Max(start, a) - a;
                int to = Math.Min(end, b) - a;
                string pre = s[..from];
                string post = s[to..];
                string text = placed ? pre + post : pre + repl + post;
                placed = true;
                SetText(t, text);
                if (text.Length == 0)
                {
                    emptied.Add(t);
                }
            }

            foreach (MarkupNode t in emptied)
            {
                MarkupNode parent = t.Parent;
                if (parent.LocalNameString == "r")
                {
                    parent.Remove();
                }
            }

            atoms = Atoms(doc.Root);
        }

        var sb = new System.Text.StringBuilder();
        foreach (MarkupNode t in Atoms(doc.Root))
        {
            sb.Append(t.InnerText);
        }

        plain = XString.Decode(sb.ToString());
        xml = doc.ToArray();
        return true;
    }

    private static void SetText(MarkupNode t, string text)
    {
        t.SetText(text);
        bool edge = text.Length > 0 && (char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1]));
        if (edge && t.GetAttribute("http://www.w3.org/XML/1998/namespace", "space") is null)
        {
            t.SetAttribute("xml:space", "preserve");
        }
    }

    private static List<MarkupNode> Atoms(MarkupNode si)
    {
        var list = new List<MarkupNode>();
        foreach (MarkupNode c in si.ChildElements)
        {
            ReadOnlySpan<byte> name = c.LocalName;
            if (name.SequenceEqual("t"u8))
            {
                list.Add(c);
            }
            else if (name.SequenceEqual("r"u8))
            {
                foreach (MarkupNode t in c.ChildElements)
                {
                    if (t.LocalName.SequenceEqual("t"u8))
                    {
                        list.Add(t);
                    }
                }
            }
        }

        return list;
    }
}
