# Changelog

All notable changes are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project follows [Semantic Versioning](https://semver.org/).

## [Unreleased]

- Rendering (`RecursiveAI.OneDocument.Rendering`): the text-layout core for paragraphs and lines (shaping through OneFont, UAX #14 breaking, tabs and leaders, indents, alignment and justification, bidirectional lines, Word line heights, list labels, borders and shading, widow/orphan, keep-with-next, keep-lines, page breaks over a single column per section), a public layout tree (`LayoutDocument` > pages > paragraphs > lines > runs with positions and text ranges), a font provider with Office-to-open-font substitution notes and embedded-font de-obfuscation, and painting into OneRender's display list with semantic tags. `Document.SaveAs` writes `.pdf` (tagged; PDF/UA-1 and PDF/A profiles) and `.png`; `Office.Convert` and the `onedocument render`/`convert` commands use it. Tables, headers and footers, columns, floating objects, notes and fields are reported as `layout.*` notes and come later. See `docs/layout.md`. `render.failed` is the new defect code for output failures.

- Calculation of what-if features: iterative calculation (`calcPr iterate`, `iterateCount`, `iterateDelta`; `Workbook.SetIterativeCalculation`), data tables (one- and two-variable, calculated in an overlay so the grid is never touched), `Workbook.GoalSeek`, external links (`Workbook.ExternalLinks`; references read the file's cached values, or live workbooks through an opt-in `IExternalWorkbookResolver`, with refreshed values saved back), and `WEBSERVICE`/`FILTERXML` through an opt-in `IWebServiceResolver`. Nothing is opened or fetched unless the caller supplies a resolver.

- Word mail merge: `Document.MailMerge` (`MERGEFIELD` with `\b`, `\f`, `\#`, `\@`, `\*`, `IF`, `NEXT`, `NEXTIF`, `SKIPIF`, `MERGEREC`, `MERGESEQ`, `SET`, `ASK`, `FILLIN`, `REF`; a document per record or all records in one with section breaks; the `mailMerge` connection is removed and never opened). See `docs/mail-merge.md`.
- Content-control data binding: `Document.BindContentControls`, `RefreshContentControls`, `PushContentControls` for text, rich text, date, check box, drop-down, combo box and repeating sections; the binding XPath subset now covers `//`, `.`, `..`, `last()`, comparisons, `name()` and string functions.

- Renamed from OneOffice to OneDocument: packages `RecursiveAI.OneDocument*`, namespaces, the `onedocument` tool and the `ONEDOCUMENT_*` environment variables. The `Office` statics and the `Office*` types keep their names.

- The `Office` statics work with no registration call: each kind package compiles a module initializer into the referencing C# project (no reflection; NativeAOT and trimming keep only the referenced kinds). `Register` stays public and may be called any number of times; `OneDocumentAutoRegister=false` opts out.
- Text boxes as block containers: `Document.TextBoxes()`, `BlockContainer.TextBoxes()`, `Paragraph.TextBoxes()`, `TextBox` (DrawingML and VML, the copy in `mc:Fallback` kept equal on commit, linked chains), and `Document.WrapParagraph`. Tracked changes and edits work on their paragraphs.
- Document properties: `Document.Properties` with core, extended and custom properties (`RecursiveAI.OneDocument.Properties`), created on first write, untouched when not edited, no clock reads.
- List labels for every story as Word counts them: `NumberingDefinitions.LabelStories()`; `DocumentText.ListLabels` now covers headers, footers, notes, comments and text boxes.
- The presentation hub (`RecursiveAI.OneDocument.Presentations`): `Presentation` over PPTX, PPTM, POTX, PPSX and Flat OPC (Transitional and Strict) with masters, layouts, slides, notes, shapes, text, tables and comments; placeholder inheritance from master to slide; add, delete, duplicate and move slides, text and table-cell edits. Generated PresentationML and PowerPoint-extension views in `.Schema`.
- The presentation template binder (`PresentationTemplate.Render`/`Validate` in `RecursiveAI.OneDocument.Presentations.Templating`): tags in shapes, placeholders, groups, table cells and notes bind across split runs; blocks repeat paragraphs, table rows, shapes or whole slides (cloned with their notes) by where their tags stand or by `scope=`; a shape holding only a false block is removed; `{% image %}` replaces a shape with a picture keeping its geometry; hyperlink targets bind per copy. A 23-template golden corpus lives under `testdata/templates/pptx`. See `docs/templating-pptx.md`.
- The document template binder (`Template.Render`/`Template.Validate` in the core package, `RecursiveAI.OneDocument.Documents.Templating`, CLI `onedocument template`): tags in every story bind across runs; `for`/`if` blocks repeat paragraphs, table rows, tables and sections with list numbering, bookmark, comment, note, picture and paragraph ids kept unique; `{% image %}`, `link()`, `{% html %}` and `{% markdown %}` insert pictures, hyperlinks and formatted content. See `docs/templating-docx.md`.
- Fixed: a package whose main part is typed only by a `Default` content type for `xml` is recognised and opens.
- The workbook template binder carries more than cells: chart series references and cached points, pivot cache sources (`refreshOnLoad`), conditional-format and validation rule formulas, copies of a sheet block with their tables, conditional formats, validations, page setup, comments and drawings; tags in comments, header and footer strings and text boxes; `{% image %}` pictures anchored at their cell; and `XlsxTemplateBinder.Validate`. Table columns take the rendered header text. New defect `template.image-invalid`.

## [0.1.0-preview.1]

First preview: word-processing documents in the DOCX family, and OOXML text extraction.

- The word-processing hub: styles with inheritance, numbering and list labels, tables with a logical cell model and edits, sections, headers and footers, fields, footnotes and endnotes, comments with replies, revisions (read, accept, reject, author) and content controls with custom XML bindings.
- Generated typed views of the WordprocessingML elements over the markup tree (`RecursiveAI.OneDocument.Schema`).
- OOXML text extraction for documents, workbooks and presentations, with located, normalized text.
- OPC packages on zip and Flat OPC with untouched parts copied raw; compound-file access.
- A span-based UTF-8 XML tokenizer and writer, and a lazy, lossless markup tree.
- The `onedocument` tool (`detect`, `extract`).
- `Document` on the word-processing hub: `Open` (sniffs content), `Create`, atomic `Save`, `SaveAs` by extension or format (DOCX, DOCM, DOTX, DOTM, Flat OPC), `Paragraphs`, `Tables`, `Paragraph.SetText`, `Body.AddParagraph`; `DocumentFormats.Register`.
- `onedocument extract` with `-o` and `--json`; `onedocument detect --json`.
- `RecursiveAI.OneDocument.Documents` and `RecursiveAI.OneDocument.Schema` are packable.
- Repository foundation: solution, build and packaging conventions, CI and release workflows.
- Core contracts: byte sources, resource limits, typed defects and exceptions, content-based format identification, the format registry, the part/package abstraction with atomic save, and the shared base of the document types.
