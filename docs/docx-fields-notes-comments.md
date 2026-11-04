# WordprocessingML fields, notes and comments

How the document hub reads and edits fields, footnotes and endnotes, and comments. The model holds codes and cached results;
evaluating a field that needs layout (page numbers, tables of contents) is not done here.

## Fields

`Document.Fields()` walks the body once and lists every field in the order its code begins, so an outer field comes before the
fields nested in it. `Document.Fields(container)` does the same for a note, a comment or any other block container.

- **Simple** (`w:fldSimple`): the instruction is the `w:instr` attribute; the result is the content. `w:dirty` and `w:fldLock`
  are exposed.
- **Complex** (`w:fldChar` begin / `w:instrText` / separate / result / end): one field however many runs, paragraphs or cells
  it crosses, and however deep it nests. The instruction is the field's own `w:instrText`, concatenated, plus the cached result
  of fields nested inside the code. The result text is the text of its runs including nested results; tabs read as `\t` and
  breaks as `\n`. `w:delText` and `w:delInstrText` count for nothing.
- The `mc:Fallback` branch of alternate content is skipped, because it repeats the chosen branch.
- **Instruction syntax** (`FieldInstruction`): the first word is the type (upper-cased); quoted strings may use straight or
  curly quotes, and a backslash inside quotes escapes the next character; `\x` starts a switch, with any glued text as its
  argument (`\*MERGEFORMAT`). A switch takes the next word as its argument unless it is a known flag of that field type (the
  table is in the source, from the field definitions). `=` fields keep the expression as one argument. Parsing never fails; an
  unclosed quote sets `IsWellFormed` to false.
- **Editing**: `SetInstruction` writes the text exactly as given. `SetResult` keeps the first plain result run (and its
  formatting), puts the text in it and removes every other result run, nested fields included; a field without a separator gets
  one and a run formatted like the begin character. A result that crossed paragraphs leaves those paragraphs in place without
  the removed runs.
- Defects: `docx.field-unbalanced`, `docx.field-too-deep`.

## Notes

`Document.Footnotes` and `Document.Endnotes` list every note of the part, separators included (`Note.Type`). A document
without the part has an empty collection.

- `Add(text)` creates the part when missing (with the separator and continuation-separator notes, ids -1 and 0), the
  relationship and content type, and the settings' list of separator ids when a settings part exists. The first normal note has
  id 1. The paragraph starts with the automatic number mark, styled when the document defines the note styles.
- `Note.InsertReference(run)` / `AppendReference(paragraph)` insert a reference run (character style when defined, else
  superscript). `Remove(note)` removes the note and all its references; separators cannot be removed or referenced.
- `NoteNumbering` (per section `w:sectPr` or document-wide in the settings) reads and writes `pos`, `numFmt`, `numStart`,
  `numRestart` in schema order; `Combine` applies section over document over defaults.
  With no `numFmt` anywhere, footnotes are `decimal` and endnotes `lowerRoman` (observed in Word 365, 16.0.20527; the
  schema's default is decimal for both).
- Defects: `docx.note-missing`, `docx.note-duplicate-id`.

## Comments

`Document.Comments` reads `comments.xml` with the modern parts that go with it, when present: `commentsExtended` (parent link
by the `w14:paraId` of the last paragraph, and the done state), `commentsIds` (durable ids), `commentsExtensible` (UTC date) and
`people`.

- `Add(first, last, author, text, ...)` writes the comment, the range start before `first`, the range end after `last`, and the
  reference run after the end. The date is only written when the caller passes one: the library never reads the clock.
  Paragraph ids and durable ids are deterministic (derived from the comment id, moved on when taken).
- `Reply(parent, ...)` anchors the reply beside the parent's markers (start after the parent's start, end and reference after
  the parent's reference) and writes the parent link, creating `commentsExtended` when needed. A reply to a reply goes to the top-level
  comment.
- `Remove(comment)` removes its replies, markers, reference runs, and its entries in every modern part; the `people` entry stays.
- Add and remove change the comment parts, `document.xml` and the relationships/content types of a new part, nothing else.
- Defects: `docx.comment-duplicate-id`, `docx.comment-missing`, `docx.comment-range-unmatched`.

## Questions for desktop Word

1. Endnote default number format with no `w:numFmt` (see above).
2. Whether a comments part without `mc:Ignorable="w14"` that gets `w14:paraId` added to an existing part (declared locally on the
   element) opens without repair; new parts declare it on the root.
3. Whether `commentsExtended` alone (no `commentsIds`) is accepted for a reply thread.
