# RecursiveAI.OneDocument.Rendering

**Read, write, calculate, template and render Word, Excel and PowerPoint documents (OOXML, legacy binary, RTF, ODF) in pure .NET. Not affiliated with Microsoft.**

This package lays out documents, workbooks and presentations by its own implementation of the Office layout rules (a preview, not yet published) and paints them into a display list, which becomes tagged PDF (optionally PDF/UA-1 or PDF/A-2a) or page images (PNG, JPEG, TIFF, WebP, SVG). It also draws DrawingML shapes, charts, SmartArt, equations and embedded metafiles. How closely the result matches Office is checked against Office's own PDF export at the 1.0 gate; until then treat the layout as implemented, not verified.

```csharp
using RecursiveAI.OneDocument;

Office.Convert("contract.docx", "contract.pdf");   // also .xlsx and .pptx; ".png", ".jpg", ".webp" and ".svg" write the first page, ".tif" every page
```

The `Office` statics find this package as soon as an application references it; no registration call is needed. The page model is public (`LayoutEngine`, `LayoutDocument`) for callers who need positions of lines and runs.

See the [repository](https://github.com/recursive-ai-oss/onedocument-csharp) for the support table and the [changelog](https://github.com/recursive-ai-oss/onedocument-csharp/blob/main/CHANGELOG.md).

Licensed `MIT OR Apache-2.0`, © Recursive AI, LLC. Not affiliated with Microsoft or with any other product or company named OneDocument.
