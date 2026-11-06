# RecursiveAI.OneDocument.Documents

**Read, write, calculate, template and render Word, Excel and PowerPoint documents (OOXML, legacy binary, RTF, ODF) in pure .NET. Not affiliated with Microsoft.**

This package is the word-processing part of OneDocument, an early preview. It opens, edits and saves DOCX, DOCM, DOTX, DOTM and Flat OPC documents:

- **Lossless.** Parts you did not edit are written back byte-identical (stored entries are copied, not recompressed). Edited parts keep unknown elements, attributes, prefixes, `mc:AlternateContent` and `extLst` in their original order.
- **Lazy.** Opening reads the package structure and the main part; every other part is read when you first use it. Nothing is recalculated or changed by opening.
- **Safe.** The format is found from the content, never the extension. Damage is reported as typed defects with locations, resource limits are on by default, and `Save()` replaces the file atomically.
- **The model:** paragraphs, runs, tables, sections, headers and footers, styles with inheritance, numbering, fields, footnotes and endnotes, comments, revisions and content controls, each also reachable as generated typed views over the markup.

```csharp
using RecursiveAI.OneDocument.Documents;

using var doc = Document.Open("contract.docx");
doc.Paragraphs.First().SetText("Master services agreement");
doc.Save();                       // atomic replace; only word/document.xml changes
doc.SaveAs("contract.dotx");      // a copy, as a template
string text = doc.ExtractText();
```

The `Office` statics (`Office.Open`, `Office.ExtractText`) reach this package as soon as an application references it: no registration call is needed (the package adds a module initializer to your C# project at build time; set `OneDocumentAutoRegister` to `false` to opt out and call `DocumentFormats.Register(registry)` yourself). DOCX-family files are read and written. Word 97-2003 and Word 6/95 binary files (DOC) are read and converted into the same model (`Document.SourceFormat`, `Document.ImportLoss`); other formats raise `FormatNotAvailableException` naming the format.

See the [repository](https://github.com/recursive-ai-oss/onedocument-csharp) for the status table and the [changelog](https://github.com/recursive-ai-oss/onedocument-csharp/blob/main/CHANGELOG.md).

Licensed `MIT OR Apache-2.0`, © Recursive AI, LLC. Not affiliated with Microsoft or with any other product or company named OneDocument.
