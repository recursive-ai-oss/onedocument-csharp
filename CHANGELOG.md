# Changelog

All notable changes are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project follows [Semantic Versioning](https://semver.org/).

## [Unreleased]

- Text boxes as block containers: `Document.TextBoxes()`, `BlockContainer.TextBoxes()`, `Paragraph.TextBoxes()`, `TextBox` (DrawingML and VML, the copy in `mc:Fallback` kept equal on commit, linked chains), and `Document.WrapParagraph`. Tracked changes and edits work on their paragraphs.
- Document properties: `Document.Properties` with core, extended and custom properties (`RecursiveAI.OneOffice.Properties`), created on first write, untouched when not edited, no clock reads.
- List labels for every story as Word counts them: `NumberingDefinitions.LabelStories()`; `DocumentText.ListLabels` now covers headers, footers, notes, comments and text boxes.
- Fixed: a package whose main part is typed only by a `Default` content type for `xml` is recognised and opens.

## [0.1.0-preview.1]

First preview: word-processing documents in the DOCX family, and OOXML text extraction.

- The word-processing hub: styles with inheritance, numbering and list labels, tables with a logical cell model and edits, sections, headers and footers, fields, footnotes and endnotes, comments with replies, revisions (read, accept, reject, author) and content controls with custom XML bindings.
- Generated typed views of the WordprocessingML elements over the markup tree (`RecursiveAI.OneOffice.Schema`).
- OOXML text extraction for documents, workbooks and presentations, with located, normalized text.
- OPC packages on zip and Flat OPC with untouched parts copied raw; compound-file access.
- A span-based UTF-8 XML tokenizer and writer, and a lazy, lossless markup tree.
- The `oneoffice` tool (`detect`, `extract`).
- `Document` on the word-processing hub: `Open` (sniffs content), `Create`, atomic `Save`, `SaveAs` by extension or format (DOCX, DOCM, DOTX, DOTM, Flat OPC), `Paragraphs`, `Tables`, `Paragraph.SetText`, `Body.AddParagraph`; `DocumentFormats.Register`.
- `oneoffice extract` with `-o` and `--json`; `oneoffice detect --json`.
- `RecursiveAI.OneOffice.Documents` and `RecursiveAI.OneOffice.Schema` are packable.
- Repository foundation: solution, build and packaging conventions, CI and release workflows.
- Core contracts: byte sources, resource limits, typed defects and exceptions, content-based format identification, the format registry, the part/package abstraction with atomic save, and the shared base of the document types.
