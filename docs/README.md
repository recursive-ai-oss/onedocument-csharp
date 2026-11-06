# OneDocument notes

Normative notes on how OneDocument behaves, one page per area. The code's XML documentation states the details of each type; these pages state the rules, the sources they come from and the deviations. Start with `contracts.md` and `defects.md`.

## Foundations

| Note | What it covers |
|---|---|
| [contracts.md](contracts.md) | Shared contracts: byte sources, limits, defects and exceptions, the format registry, the part model, the untouched-part round-trip rule |
| [defects.md](defects.md) | The defect catalogue: every `layer.problem` code, what it means and where it is located |
| [packaging.md](packaging.md) | OPC packages, Flat OPC and compound files under every format |
| [markup.md](markup.md) | The XML tokenizer, writer, lazy markup tree and reader |
| [platforms.md](platforms.md) | NativeAOT, trimming and browser WebAssembly: what is checked and the known browser gaps |
| [encryption.md](encryption.md) | Opening password-protected files; the supported schemes and what is written |
| [protection.md](protection.md) | Editing restrictions and their passwords for the three kinds |
| [ole.md](ole.md) | OLE property sets and embedded objects |
| [vba.md](vba.md) | VBA projects as data: read, list, extract, remove; never run |
| [officeart.md](officeart.md) | The OfficeArt reader the binary formats share |
| [number-formats.md](number-formats.md) | Excel's number-format language and date serials, as a shared service |
| [text-extraction.md](text-extraction.md) | Text extraction from OOXML packages |
| [accessibility.md](accessibility.md) | Accessibility checking against Office's published rules |

## Word-processing documents

| Note | What it covers |
|---|---|
| [docx-styles-numbering.md](docx-styles-numbering.md) | Style resolution and list labels |
| [docx-tables-sections.md](docx-tables-sections.md) | Tables, sections, headers and footers |
| [docx-fields-notes-comments.md](docx-fields-notes-comments.md) | Fields, footnotes and endnotes, comments |
| [docx-content-controls.md](docx-content-controls.md) | Content controls and custom XML binding |
| [docx-stories-properties.md](docx-stories-properties.md) | Text boxes, stories, list labels, document properties |
| [revisions.md](revisions.md) | Tracked changes: reading, authoring, accepting and rejecting |
| [doc.md](doc.md) | Reading Word 97 to 2013, Word 6/95 and fast-saved binary files |
| [rtf.md](rtf.md) | RTF 1.9.1 read and write, the group tree and the conversion |
| [xml-2003.md](xml-2003.md) | Word 2003 XML and Excel 2003 XML (read-only conversion) |
| [html.md](html.md) | HTML import (sanitised) and export |
| [markdown.md](markdown.md) | CommonMark and GFM import and export |
| [odf.md](odf.md) | OpenDocument package layer and conversions |

## Workbooks

| Note | What it covers |
|---|---|
| [spreadsheets.md](spreadsheets.md) | The workbook hub and the cell store |
| [spreadsheets-styles.md](spreadsheets-styles.md) | Styles, tables, conditional formatting, validation, names |
| [spreadsheets-streaming.md](spreadsheets-streaming.md) | Forward-only row and cell cursors |
| [spreadsheets-poco.md](spreadsheets-poco.md) | `Spreadsheet.Read<T>` and `Write<T>`: rows of plain objects |
| [spreadsheets-calc.md](spreadsheets-calc.md) | Recalculation: dependency graph, levels, circular references, iteration, data tables, Goal Seek, external links |
| [spreadsheets-pivots.md](spreadsheets-pivots.md) | Pivot caches and tables, refresh |
| [calc.md](calc.md) | The formula tree, values, evaluator and function registry |
| [calc-functions.md](calc-functions.md) | The function families and their documented behaviour |
| [csv.md](csv.md) | CSV and TSV reader, writer and loss report |
| [xlsb.md](xlsb.md) | Binary workbooks |
| [xls.md](xls.md) | Legacy Excel workbooks, BIFF2 to BIFF8 read, BIFF8 write |

## Presentations

| Note | What it covers |
|---|---|
| [presentations.md](presentations.md) | The presentation hub, inheritance, editing |
| [ppt.md](ppt.md) | Reading PowerPoint 97-2003 files |

## Templating and operations

| Note | What it covers |
|---|---|
| [templating.md](templating.md) | The template syntax, extensions to formula syntax, data sources, the binder contract |
| [templating-docx.md](templating-docx.md) | The document template binder |
| [templating-xlsx.md](templating-xlsx.md) | The workbook template binder |
| [templating-pptx.md](templating-pptx.md) | The presentation template binder |
| [mail-merge.md](mail-merge.md) | Word mail merge |
| [operations-merge.md](operations-merge.md) | Merge, append, split and persisted field update |
| [operations-find-replace.md](operations-find-replace.md) | Find and replace across runs, cells and paragraphs |

## Rendering

| Note | What it covers |
|---|---|
| [layout.md](layout.md) | The layout engine and paint contract; PDF and PNG output |
| [layout-tables.md](layout-tables.md) | Tables in layout |
| [layout-floats.md](layout-floats.md) | Floating objects and text wrap |
| [layout-fields.md](layout-fields.md) | Fields that depend on pages; table of contents and indexes |
| [spreadsheet-rendering.md](spreadsheet-rendering.md) | Printing workbooks: pages, cell text, objects |
| [presentation-rendering.md](presentation-rendering.md) | Slides to PDF and PNG |
| [drawingml-rendering.md](drawingml-rendering.md) | Shapes, pictures, groups and effects |
| [charts.md](charts.md) | Chart reading, layout and painting |
| [smartart.md](smartart.md) | SmartArt: stored drawings and the layout engine |
| [math-layout.md](math-layout.md) | Equations (Office Math) |
| [metafiles.md](metafiles.md) | Drawing embedded EMF, EMF+ and WMF pictures |

## Coming from another library

[migration/](migration/README.md) maps the common tasks of seven libraries to OneDocument.
