# Find and replace

Search and replace over the text of a document, a workbook or a presentation, as the reader sees it. The same matcher serves the three kinds (`RecursiveAI.OneDocument.Operations`: `SearchOptions`, `TextSearch`, `ReplaceReport`); each kind walks its own text.

```csharp
IReadOnlyList<TextHit> hits = doc.Find("invoice");
ReplaceReport report = doc.Replace("(\\d+) EUR", "€$1",
    new DocumentReplaceOptions { Search = new SearchOptions { UseRegex = true }, TrackedAuthor = "Robot" });
ReplaceReport sheets = workbook.Replace("March", "April", new WorkbookReplaceOptions { Formulas = true });
ReplaceReport slides = presentation.Replace("2024", "2025");
```

Every replace returns a `ReplaceReport`: each `ReplacementItem` (location, original, replacement, tracked) and each `ReplaceIssue` (a match that was found and left alone, with a stable code and its location). There is no bare count.

## Matching

- **Literal** (the default) is case-insensitive in the invariant culture. `MatchCase` makes it case-sensitive. `WholeWord` requires that the characters next to the match are not letters, digits or underscores (so it also works for a pattern that starts or ends with punctuation).
- **Regular expression** (`UseRegex`) is a .NET `Regex` (no `RegexOptions.Compiled`, so it is safe for NativeAOT). It runs with the timeout `OfficeLimits.RegexMatchTimeoutMilliseconds` (default 2,000 ms). A pattern the linear-time engine (`RegexOptions.NonBacktracking`) accepts runs on it; one that needs backtracking (back-references, look-around) or `WholeWord` runs on the backtracking engine under the same timeout. A timeout raises `OfficeLimitException` (`LimitName` `RegexMatchTimeoutMilliseconds`, defect code `limit.regex-match-timeout-milliseconds`); an invalid pattern raises `ArgumentException` (`RegexParseException`).
- **Replacement text.** In a regular-expression search `$1`, `${name}`, `$&` and `$$` are expanded (.NET numbers named groups after unnamed ones; use names for both). In a literal search the replacement is used as written. A tab character and a line feed in the replacement become a tab and a line break in the target.
- **Special codes** (`SpecialCodes`): `^t` tab, `^l` manual line break, `^s` no-break space, `^~` no-break hyphen, `^^` a caret, and `^&` for the found text in a replacement. `^p` (paragraph mark) is refused in a pattern and in a replacement: a match never crosses a paragraph, and a replacement never splits one.
- **Empty matches** (`x*`) are skipped. Matches do not overlap.

## Documents (`Document.Find`, `Document.Replace`)

Text is the paragraph's *current* text, the same positions `RevisionAuthor` uses: the runs of the paragraph joined, so a match crosses run boundaries, hyperlinks, smart tags, bookmarks, content controls, field results and other authors' tracked insertions. Text inside a tracked deletion is not part of it. Tabs and line breaks are one character each (`\t`, `\n`); a no-break hyphen is U+2011. Field instructions and drawings are zero-width.

Stories (`DocumentStories`): the body (tables and content controls included), headers and footers, footnotes and endnotes, comments, and text boxes anchored in any of those (nested boxes too). The default is all of them. Text boxes inside `mc:AlternateContent` are edited through the branch a modern reader takes, and `CommitChanges` copies it over the alternate branch, so both stay equal.

Field instructions (`w:instrText`, the `w:instr` attribute of simple fields) are searched only with `IncludeFieldCodes`, each instruction text on its own and never across two. They are replaced untracked; in tracked mode they are reported as `markup-unsupported`.

**How the text is replaced.** The runs are split at the edges of the match, the matched text is removed (anything that is not text, such as a field mark, a bookmark or a drawing in between, stays), and the new text is one run placed where the first removed run was. Its properties are those of the first matched character's run; `RunPropertiesXml` merges property elements over them (an element replaces the one of the same name). Bookmarks, fields, hyperlinks, content controls, unknown elements and attributes stay in place; a part with no match is not modified. Replacing the whole result of a field removes the run it sat in but not the field marks, so the new text is the field's new cached result.

**Tracked replacement.** With `TrackedAuthor` each match becomes a `w:del` of the old text and a `w:ins` of the new, through `RevisionAuthor` (author, optional date, ids from `RevisionIds`; the clock is never read). Accepting every change gives the new text, rejecting gives the original. Text this author inserted earlier is removed outright, as Word does. An object anchored right before the first replaced character (a drawing, a text box in alternate content, a note reference) is not part of the replaced text and stays; one inside the matched text goes with it.

Not done: a match across paragraphs; replacing with a paragraph mark; fields' cached results are not recomputed; content-control data binding is not re-run.

## Workbooks (`Workbook.Find`, `Workbook.Replace`)

String cells of every worksheet (chart sheets and macro sheets are skipped; `Sheets` names a subset), in row order. Numbers, booleans and errors are not text. A cell with a formula is not searched as a value (its text is a cached result). A match stays inside one cell.

- A plain string cell is set with the new text and keeps its style.
- A rich-text cell is edited run by run: the new text goes into the run where the match began, later runs lose the matched part of their text, a run left empty is removed, other runs and phonetic data stay. The edited string is added to the shared string table as a new entry (other cells that used the old one are untouched). Strings with `_xHHHH_` escapes or inline rich text are reported as `rich-text-unsupported` and left as they are.
- **Formulas** (`Formulas = true`) search the formula text as stored, without `=`. The new text must parse as a formula in the file's own spelling (the same parser as everywhere else, with the formula-length limit) or the cell is left exactly as it was and the match is reported (`formula-invalid`, `formula-too-long`). A shared formula's text serves other cells, so it is reported as `formula-shared` and not changed. Array formulas keep their kind and range. Writing a formula discards its cached value and marks the workbook for recalculation, like assigning a formula; nothing is calculated.

Not searched: defined names, comments, headers and footers, chart text, data-validation lists, table headers' own names. Replacing a table header's text does not rename the table column.

## Presentations (`Presentation.Find`, `Presentation.Replace`)

Paragraphs (`a:p`) in slides and speaker notes by default (`PresentationParts`; layouts, masters and the notes master on request), in shapes, groups and tables, in slide order. Text is the run and field text joined; a line break is U+000B. Charts, SmartArt and comments are other parts and are not searched.

The new text goes into the run where the match began and keeps its character formatting; later runs lose only the matched part of their text and a run left empty is removed. A line feed or U+000B in the replacement becomes `a:br`, which splits the run (a break cannot go inside a field: `markup-unsupported`). `a:t` takes no `xml:space`, so none is written. Paragraph properties, bullets, other runs' hyperlinks and unknown markup stay as they are. Paragraphs whose text has `_xHHHH_` escapes are reported as `markup-unsupported`.

## Issue codes

| Code | Where | Meaning |
|---|---|---|
| `markup-unsupported` | documents, presentations | The markup around the text cannot be edited safely (or the edit is not offered, such as field instructions in tracked mode). |
| `formula-invalid` | workbooks | The new formula text does not parse. |
| `formula-too-long` | workbooks | The new formula would exceed `OfficeLimits.MaxFormulaLength`. |
| `formula-shared` | workbooks | The formula is shared by several cells. |
| `rich-text-unsupported` | workbooks | The rich text has escapes or is stored inline. |

## Checks

`tools/OperationsProbe run DIR` runs the replacement over a directory of DOCX, XLSX and PPTX files and compares the result with a plain `Regex.Replace` over the same text: after the replacement, after saving and reopening, and (documents) tracked then accepted and rejected.
