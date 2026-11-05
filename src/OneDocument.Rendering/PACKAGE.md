# RecursiveAI.OneDocument.Rendering

**Read, write, calculate, template and render Word, Excel and PowerPoint documents (OOXML, legacy binary, RTF, ODF) in pure .NET. Not affiliated with Microsoft.**

This package lays out documents the way the Office applications do and paints them into a display list, which becomes tagged PDF or PNG pages. It is an early preview: paragraphs and lines of word-processing documents so far.

```csharp
using RecursiveAI.OneDocument;
using RecursiveAI.OneDocument.Documents;

Office.Convert("contract.docx", "contract.pdf");

using var doc = Document.Open("contract.docx");
doc.SaveAs("contract.png");   // first page; use Layout for every page
```
