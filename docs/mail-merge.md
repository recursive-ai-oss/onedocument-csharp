# Mail merge

`Document.MailMerge(data, options)` runs Word's mail merge on a main document: the merge fields are replaced by their results for each record, the `mailMerge` connection is removed, and the output is a new document per record or one document holding every record. The data is read the way the template engine reads it (`[TemplateModel]` classes, `JsonDocument`, dictionaries, `DataTable`/`IDataReader`, XML); a sequence is the records, a single object is one record, `null` is none. Nothing is opened from the connection string in the `mailMerge` settings part: connections are kept out of the output and never followed.

```csharp
using var doc = Document.Open("letter.docx");
using MailMergeResult result = doc.MailMerge(people, new MailMergeOptions { Mode = MailMergeMode.SingleDocument });
result.Documents[0].SaveAs("letters.docx");
foreach (Defect w in result.Warnings) Console.WriteLine($"{w.Code} {w.Message}");
```

The template is not changed. Output is `Docx` (`Docm` for a macro-enabled main document). Output is deterministic.

## Options and result

| Member | Meaning |
|---|---|
| `Mode` = `PerRecord` (default) | One document per pass over the main document. A pass reads one record, or several when the document uses `NEXT`. |
| `Mode` = `SingleDocument` | One document, every pass in turn, each pass ending in a next-page section break (Word's "merge to new document"). Drawing, comment, bookmark and revision ids are renumbered so they stay unique. Headers and footers are taken from the first record's pass. |
| `Answers` | Values for `ASK` (keyed by bookmark) and `FILLIN` (keyed by prompt). With no answer the `\d` default, else empty. Nothing is asked interactively. |
| `MaxRecords` | Default 100,000; more raises `OfficeLimitException`. |
| `MailMergeResult.Documents` / `.Warnings` | The documents (none when there are no records) and the located `mailmerge.` defects. |

## Fields

Simple fields (`w:fldSimple`) and complex fields (`w:fldChar`, with run splits, proofing marks and rsids inside the instruction) are both read through the field model in `docx-fields-notes-comments.md`. Field names are case-insensitive.

| Field | Behaviour |
|---|---|
| `MERGEFIELD name` | The record's value, by name ignoring case. Switches: `\b "text"` text before and `\f "text"` text after, each only when the value is not empty; `\# picture`; `\@ picture`; `\* Upper|Lower|FirstCap|Caps|MERGEFORMAT|CHARFORMAT`. The result takes the formatting of the field's first result run. A missing field merges as empty and warns `mailmerge.field-missing`. |
| `IF a op b "then" "else"` | Operators `=`, `<>`, `<`, `>`, `<=`, `>=`. Both sides are numbers when both parse, else text compared ignoring case, with `?` and `*` wildcards on `=` and `<>`. Nested fields in either side or in the branches are evaluated first, and the result is itself merged (nested `IF`). |
| `NEXT` | Moves to the next record in the same pass. Past the last record the fields after it merge blank. |
| `NEXTIF a op b` / `SKIPIF a op b` | `NEXTIF` moves to the next record when the test is true. `SKIPIF` abandons the pass when true: the record is not emitted. |
| `MERGEREC` / `MERGESEQ` | `MERGEREC` is the 1-based record number. `MERGESEQ` counts the passes emitted so far (it does not count skipped records). |
| `SET name "v"`, `REF name`, `ASK name "prompt"`, `FILLIN "prompt"` | Variables and answers as far as the merge needs: `SET` stores the (merged) text, `REF` to a variable inserts it, `ASK` and `FILLIN` take `Answers`. |
| `ADDRESSBLOCK`, `GREETINGLINE`, `DATABASE`, `MERGEBARCODE`, `COMPARE` | Not expanded. The field stays as written, with `mailmerge.field-unsupported`. |

Other fields (`PAGE`, `DATE`, …) are untouched.

## Pictures (`\#`, `\@`)

Word's pictures are not the spreadsheet format language, so they are implemented here (`WordPicture`), culture-invariant (en-US):

- **Date (`\@`)**: `M` month, `m` minute, `d`/`dd`/`ddd`/`dddd`, `yy`/`yyyy`, `h` 1–12 and `H` 0–23, `s`, `AM/PM` (always capitals), `A/P`, `a/p`; `'text'` and `\x` are literals.
- **Numeric (`\#`)**: translated to a number-format code and formatted by the number-format service, so `#,##0.00` and `positive;negative;zero` sections behave as in a cell. Differences: `%` and `$` are literal (no scaling by 100); `'text'` is literal; `-` prints a minus or a space, `+` prints `+`, `-` or a space; `x` drops the digits to its left (`x##` gives `492` for 222492 and `3` for 3), or rounds at its place when it sits right of the point; `#` shows a space where the number has no digit, except in a picture with a grouping comma, where it shows nothing.
- Case formats: `FirstCap` lowercases the rest of the text.

These details were set from Microsoft's "Format field results" page and the ECMA-376 Part 1 §17.16.4 text; desktop Word is the judge at the 1.0 gate. The committed Word cases (below) hold the comparison.

## Defects

`mailmerge.field-missing`, `.field-malformed`, `.field-unsupported`, `.field-unbalanced` (a field with no end or a stray end marker): catalogue in `defects.md`. Each carries the part and offset of the field in the template.

## Limits

- The main document is saved and re-opened once per pass, so it must be a document with a real container (a file or stream opened with `Document.Open`, or a saved copy); a document built over `InMemoryPackage` is not one and raises `OfficeFormatException`.
- A field whose begin and end are in different paragraphs is not merged (`mailmerge.field-unbalanced`).
- Revision ids (`w:ins`, `w:del`) are shifted in single-document mode by rewriting the attribute only.
- Unquoted nested values in `IF` that are empty or contain spaces are quoted before the comparison.

## Reference output from Word

`OracleCases --mailmerge testdata/templates OUT` writes, per golden case, the main document, a `.data.csv` and an actions sidecar (`mailmerge:DATA.csv|single`) so that `tools/office-oracle` has Word merge the same main document and data into a new document, and our outputs (one per pass, and merged into one) in `OUT/ours`. `ASK`/`FILLIN` cases are not sent to Word. The cases are unanswered until the 1.0 gate (`tools/office-oracle/README.md`).
