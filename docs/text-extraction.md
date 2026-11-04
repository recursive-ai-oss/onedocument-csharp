# Text extraction (OOXML)

`OoxmlTextExtractor` (namespace `RecursiveAI.OneOffice.TextExtraction`) extracts the text of word-processing, spreadsheet and presentation packages by streaming the parts that hold text from an `OpcPackage` (zip or Flat OPC). It reads tokens from the markup reader and builds neither the document model nor a tree, so memory is the open-paragraph buffers (and, for workbooks, the shared string table), whatever the size of the part. `Office.ExtractText` reaches it through the format registry.

## Output

One output for every way the producer split its runs. Plain text and `NormalizedText` come from the same pass.

**Word-processing** (ECMA-376 Part 1 §17; Transitional and Strict namespaces alike)

| Construct | Output |
|---|---|
| paragraph (`w:p`) | its text and a line feed; empty paragraphs give an empty line |
| `w:t` | the text; character data is taken as written, white space included |
| `w:tab`, `w:ptab` | a tab (not the tab stops in `w:pPr`) |
| `w:br`, `w:cr` | a line feed, of any break type |
| `w:noBreakHyphen`, `w:softHyphen` | `-`; nothing |
| field (`w:fldChar` … `w:instrText` … `w:fldChar`, `w:fldSimple`) | the result only; the code (`w:instrText`) is never output |
| table | a row on one line, cells separated by a tab; inside a cell, paragraphs and line breaks become single spaces; a table inside a cell flattens into that cell |
| text box (`w:txbxContent`) | its paragraphs, after the paragraph that holds the box (nested boxes after their host, in order) |
| `mc:AlternateContent` | the first `mc:Choice` whose required namespaces are all understood, else `mc:Fallback`; the other branches are skipped |
| equation text (`m:t`) | the text, concatenated |
| content controls (`w:sdt`) | the content; `w:sdtPr` is skipped |
| tracked changes | inserted and moved-to text is text; deleted and moved-from text (`w:del`, `w:moveFrom`) is left out unless `IncludeDeletedText`; a paragraph whose mark was deleted joins the next paragraph (§17.13.5) |
| diagram (SmartArt) text | the text of the diagram data part, after the story (not where the diagram is anchored) |
| chart text | the rich text of a chart part (titles, axis titles, labels), after the story; cached values are not output |
| phonetic guides (`w:rt`) | after their base text |
| property-change records (`…PrChange`) | skipped |
| footnote and endnote separators | skipped |

After the body: headers, footers, footnotes, endnotes, comments, each on option (`IncludeHeadersAndFooters`, `IncludeNotes`, `IncludeComments`, all on by default). Parts are told apart by a blank line.

"Understood" for `mc:Choice/@Requires` means a namespace under the standard's own roots (`http://schemas.openxmlformats.org/`, `http://purl.oclc.org/ooxml/`), Microsoft's Office extension namespaces (`http://schemas.microsoft.com/office/`) and VML. This picks the DrawingML text box over its VML copy, as current Office does.

List labels are off by default, because they need the numbering definitions, which streaming extraction does not build. `TextExtractionOptions.ListLabels` takes an `IListLabelSource`, which names a paragraph by the byte offset of its start tag; `DocumentText.ListLabels(document)` (word-processing package) builds one for the main story from the document model's numbering, with each level's separator (tab, space or nothing). Headers, footers and notes get no labels. The document must have no uncommitted edits, because the offsets are those of the stored part.

**Workbooks.** Sheets in workbook order; per row, the string cells (shared strings, formula string results, inline strings) separated by a tab, rows on lines. Phonetic runs (`rPh`) are left out. Numbers, booleans and errors have no text of their own and are not output; cell values with number formats belong to the workbook model.

**Presentations.** Slides in `sldIdLst` order; shapes in document order; paragraphs (`a:p`), line breaks, field results, tables as for Word. Empty paragraphs are not output. Slide-number placeholders (`sldNum`) and notes-page slide images are skipped. Each slide's speaker notes follow it (`IncludeNotes`).

## Normalized text

`ExtractNormalized` returns a list of `NormalizedParagraph` (`Text`, `Story`, `PartName`, `PartOffset`, `InTable`, `InTextBox`, `TextOffset`). `PartOffset` is the byte offset of the paragraph's start tag (the cell's, for a worksheet) in the uncompressed part, the same offset the markup tree reports. `NormalizedText.Text` joins the paragraphs with line feeds, and `TryLocate(offset)` maps a character of it back to a paragraph. Cell structure is not in the text: table paragraphs are ordinary paragraphs flagged `InTable`.

## Errors

Damage in the main part raises `OfficeFormatException` (`xml.*`, located by part and offset); text already written stays written. A secondary part that is missing or damaged is reported to `OpenContext.Defects` (`text.part-missing`, `text.part-unreadable`) and extraction goes on. Limits (`OfficeLimits`) apply to the archive and to every XML part.

## Memory and speed

Paragraph buffers are reused, so extraction allocates nothing per paragraph once they have grown. Reading a part holds no more than its largest token.
