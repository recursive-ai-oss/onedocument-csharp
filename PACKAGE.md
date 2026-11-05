# OneDocument

**Read, write, calculate, template and render Word, Excel and PowerPoint documents (OOXML, legacy binary, RTF, ODF) in pure .NET. Not affiliated with Microsoft.**

This is an early preview. The core package carries the shared foundation: byte sources, resource limits, typed defects and exceptions, content-based format identification, the format registry and the part/package contracts. No document format can be opened or saved yet; the format packages are placeholders until they are implemented.

```csharp
using RecursiveAI.OneDocument;

OfficeFormat format = Office.Detect("report.bin");   // decided from the content, not the extension
```

See the [repository](https://github.com/recursive-ai-oss/onedocument-csharp) for the status table, the design notes in [docs/contracts.md](https://github.com/recursive-ai-oss/onedocument-csharp/blob/main/docs/contracts.md) and the [changelog](https://github.com/recursive-ai-oss/onedocument-csharp/blob/main/CHANGELOG.md).

Licensed `MIT OR Apache-2.0`, © Recursive AI, LLC. Not affiliated with Microsoft or with any other product or company named OneDocument.
