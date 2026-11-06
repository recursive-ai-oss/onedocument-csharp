# OneDocument

**Read, write, calculate, template and render Word, Excel and PowerPoint documents (OOXML, legacy binary, RTF, ODF) in pure .NET. Not affiliated with Microsoft.**

OneDocument is a clean-room document processor for `net10.0` that depends only on the .NET base class library and other Recursive AI libraries. It is NativeAOT-, trim- and browser-WebAssembly-friendly: no reflection, no native code, no `System.Xml` in the document path. Parts you do not edit are written back byte-identical, edits keep unknown markup, and every repair on read is a located defect.

| Package | What it adds |
|---|---|
| `RecursiveAI.OneDocument` | Core: byte sources, limits, typed defects, format sniffing and registry, packages and compound files, the XML tokenizer and markup tree, formula and calculation core, number formats, the template engine, text extraction, `Office` and `Template` |
| `RecursiveAI.OneDocument.Documents` | `Document`: DOCX, DOC, RTF, ODT, Word 2003 XML, HTML, Markdown, revisions, mail merge, operations |
| `RecursiveAI.OneDocument.Spreadsheets` | `Workbook`: XLSX, XLSB, XLS, ODS, CSV/TSV, Excel 2003 XML, calculation, pivots, streaming, `Spreadsheet` |
| `RecursiveAI.OneDocument.Presentations` | `Presentation`: PPTX, PPT, ODP |
| `RecursiveAI.OneDocument.Schema` | Generated zero-copy typed views over every pinned ECMA-376 and Microsoft extension schema |
| `RecursiveAI.OneDocument.Rendering` | Layout, charts, SmartArt, equations; tagged PDF and page images for all three kinds |
| `RecursiveAI.OneDocument.All` | Every package above |
| `onedocument` | Command-line tool: `detect`, `extract`, `recalc`, `template`, `render`, `convert`, `inspect`, `sanitize` |

```csharp
using RecursiveAI.OneDocument;
using RecursiveAI.OneDocument.Documents;

string text = Office.ExtractText("report.docx");      // the format comes from the content, not the extension
Office.Convert("deck.pptx", "deck.pdf");              // needs the Rendering package (or All)
Template.Render("invoice.docx", invoice, "invoice.pdf");

using var doc = Document.Open("contract.docx");
doc.Paragraphs.First().SetText("Master services agreement");
doc.Save();                                           // atomic; only word/document.xml changes
```

See the [repository](https://github.com/recursive-ai-oss/onedocument-csharp) for the support table, samples and design notes, and the [changelog](https://github.com/recursive-ai-oss/onedocument-csharp/blob/main/CHANGELOG.md).

Licensed `MIT OR Apache-2.0`, © Recursive AI, LLC. Not affiliated with Microsoft or with any other product or company named OneDocument. Word, Excel and PowerPoint are Microsoft product names used here only to describe the formats.
