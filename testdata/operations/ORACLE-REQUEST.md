# Merge, split and field-update comparison request (unanswered)

Run once at the final gate, on the Office oracle machine, through `tools/office-oracle/office-oracle.sh run`. Nothing here has been answered.

Inputs: `dotnet tools/OperationsProbe/... samples <dir>` writes `merge-a.docx` and `merge-b.docx`. They conflict on purpose: the style
`heading 1` (28 against 40 half-points, bold against italic) and `Quote`, a bookmark `intro`, one list definition used by both, a footnote and
a comment each, a tracked insertion by Ann (a) and Bob (b), and a `REF intro` field. Hashes are recorded by the run.

## Merge (Word: Insert → Text from File)

1. Open `merge-a.docx`, put the cursor at the end, Insert → Object → Text from File → `merge-b.docx`. Save as `merge-a-b-word.docx`.
2. Run `OperationsProbe docx-merge` on the pair (default options) and record the output for a-then-b.
3. Compare, and record where they differ:
   - the style used by the second heading and the second quotation (Word's choice against `StyleConflict.KeepDestination`), and whether Word
     changed the destination's `heading 1`;
   - whether the second list continues the first (`3.`, `4.`) or restarts (`NumberingContinuation.Continue`);
   - the name of the second bookmark `intro` and what the second `REF intro` field points at;
   - footnote and comment numbering and ids; the authors of the revisions;
   - the page setup of the inserted part (`SectionHandling.UseDestination`) and whether a section break appears;
   - whether Word shows a repair prompt for the probe's output (`0000-merge-default.docx`, `0000-merge-kept.docx`).

## Split

Word has no split. Open the pieces of `docx-split` over `merge-a.docx` (`0000-heading0.docx`, `0000-section0.docx`) and record whether a repair prompt
appears and whether the styles gallery, list gallery and footnote list show anything the piece does not use.

## Fields

Open the layout case 100 (`tools/LayoutProbe gen`, `100-toc-three-levels.docx`), update all fields (Ctrl+A, F9, then "update entire table" for the
contents) and save. Run `OperationsProbe docx-fields` on the same file. Compare, per paragraph, the stored text of the contents entries, the footer
`NUMPAGES` result and the `PAGE` results. Cases 101–108 and 111–112 are the same comparison for numbered headings, switches, references, captions,
running heads, note references, the index and the table of authorities.

## Sheets and slides

- Excel: in a workbook with a name `Rate` and a table, Move or Copy a sheet to another workbook (also one that already has `Rate`), and compare the
  names, table names, external references and the sheet name against `Worksheet.CopyTo` (`xlsx-copy`).
- PowerPoint: paste slides between decks with "Keep source formatting" and "Use destination theme" and compare the masters and layouts present
  and the layout each slide uses against `Presentation.InsertSlides` (`pptx-insert`).

For every output also record: opens without a repair prompt (yes/no).

## Office run (Word, Excel and PowerPoint 16.0.20527)

Answered as far as the oracle can drive the applications: see the row for this request in [`docs/office-oracle-1.0.md`](../../docs/office-oracle-1.0.md#the-other-requests). Questions not listed there as run are still open.

Round two: fields and merge ran through the object model (`Fields.Update`, the table updates, `Range.InsertFile`). Field cases 100–108 and 111–112: 17 of 24 text parts identical, two fixes made, the rest explained; merge: styles agree, the second list is a new list in Word, the duplicate bookmark is dropped by Word. Split, sheet Move or Copy and slide paste are not run. Details in `docs/office-oracle-1.0.md`.
