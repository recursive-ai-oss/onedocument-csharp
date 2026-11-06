# OneDocument

**Read, write, calculate, template and render Word, Excel and PowerPoint documents (OOXML, legacy binary, RTF, ODF) in pure .NET. Not affiliated with Microsoft.**

OneDocument is a clean-room document processor for `net10.0` that depends only on the .NET base class library and other Recursive AI libraries, and is NativeAOT-, trim- and browser-WebAssembly-friendly. This package is a piece of it (a preview, not yet published): the core carries byte sources, resource limits, typed defects, content-based format identification, the format registry, packages and compound files, the XML tokenizer and markup tree, number formats, the formula and template engines, and text extraction. The format packages (`.Documents`, `.Spreadsheets`, `.Presentations`, `.Rendering`) add the models; the `.All` package references every one that has content.

```csharp
using RecursiveAI.OneDocument;

OfficeFormat format = Office.Detect("report.bin");   // decided from the content, not the extension
string text = Office.ExtractText("report.docx");     // needs the package for that kind
```

See the [repository](https://github.com/recursive-ai-oss/onedocument-csharp) for the support table, samples and design notes, and the [changelog](https://github.com/recursive-ai-oss/onedocument-csharp/blob/main/CHANGELOG.md).

Licensed `MIT OR Apache-2.0`, © Recursive AI, LLC. Not affiliated with Microsoft or with any other product or company named OneDocument.
