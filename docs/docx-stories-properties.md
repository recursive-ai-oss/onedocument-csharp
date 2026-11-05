# Text boxes, stories, list labels and document properties

## Text boxes

`Document.TextBoxes()` lists the text boxes of every story (body, headers and footers, footnotes, endnotes, comments); `BlockContainer.TextBoxes()` and `Paragraph.TextBoxes()` list the ones anchored in one container or paragraph. A `TextBox` is a `BlockContainer` over one `w:txbxContent`, so its paragraphs and tables, edits and tracked changes work like those of the body. Paragraphs of a box are never among the paragraphs of the container that holds the shape. A box inside a box is reached through the outer box. `Document.WrapParagraph(MarkupRef)` gives the hub paragraph for any `w:p` of the document.

- **Kinds:** DrawingML (`wps:txbx`) and VML (`v:textbox`). `Anchor` is the paragraph that holds the shape, `Shape` the shape element, `HostStory` the story it sits in.
- **`mc:AlternateContent`:** Word writes a DrawingML box twice, in `mc:Choice` and in a VML `mc:Fallback`. The box exposes the branch a modern reader takes (the first `mc:Choice` whose required namespaces are understood, else the `mc:Fallback`, ECMA-376 Part 3 §10.2.4), listed once. `Alternate` is the matching content in the other branch (pairing is by order of boxes inside the block). When the exposed content changed since the box was found, `CommitChanges` (or `SyncAlternate()`) replaces the alternate with a copy, so both branches stay equal. A box nobody edited leaves its alternate untouched.
- **Tracked changes:** `Revisions.CreateAuthor(...)` accepts the paragraph nodes of a box. After the copy, the revision list shows each change twice (once per branch, same id); accept and reject act on both.
- **Chains:** `TextBox.Chain` lists the shapes in reading order. DrawingML: the first shape holds `wps:txbx id=N`, followers hold `wps:linkedTxbx id=N seq=k` and no content (`TextBoxFrame.Story` is null). VML: `mso-next-textbox:#<shape id>` in the style of `v:textbox`; every shape has its own content.

## List labels

`NumberingDefinitions.LabelStories()` counts list numbers the way Word does, observed on generated documents in Word 16.0.20527 (`tools/office-oracle/make-story-cases.py`, probe key `stories`):

| Flow | Rule |
|---|---|
| Body | its own count |
| Headers and footers | one flow for all sections: section by section; inside a section even header, default header, even footer, default footer, first header, first footer |
| Footnotes | one flow across all notes |
| Endnotes | one flow across all notes |
| Comments | one flow across all comments |
| Text boxes of the body | one flow in document order; the VML copy is not counted |

Counters never carry from one flow into another. Text boxes anchored in a header, note or comment form one flow per kind of host; this one is not checked against Word. `DocumentText.ListLabels` covers all of these, so extracted text carries the labels of every story.

## Document properties

`Document.Properties` (core namespace `RecursiveAI.OneOffice.Properties`, usable on any package) reads and writes `docProps/core.xml`, `app.xml` and `custom.xml`. Parts are created with their relationship and content type on the first write and are left byte-identical when not edited. Edits keep other elements in order. The library never reads the clock: dates are written as given, UTC to the second (`xsi:type="dcterms:W3CDTF"` on the dcterms dates), and no date changes by itself.

Custom values: text (`vt:lpwstr`), `vt:i4`, `vt:i8`, `vt:r8`, `vt:bool`, `vt:filetime`. Other types are read as text and kept. Names compare ignoring case. A new property gets the next `pid` after the highest (the format identifier is fixed by [MS-OSHARED]); ids of other properties never change.

## Content types

A part's content type is its `Override`, else the `Default` for its extension (ECMA-376 Part 2 §10.1.2). A package whose main part has no `Override` is recognised by the format sniffer and opens.
