# Coming from another library

These pages map the common tasks of seven libraries to OneDocument, call by call. They are written from each library's published API documentation. OneDocument has no compatibility layer and no shim: the pages are a map, and the code on the right is ordinary OneDocument.

| You use | Page |
|---|---|
| EPPlus 4.x | [epplus.md](epplus.md) |
| ClosedXML | [closedxml.md](closedxml.md) |
| NPOI (XSSF, HSSF, XWPF) | [npoi.md](npoi.md) |
| Open XML SDK | [open-xml-sdk.md](open-xml-sdk.md) |
| Xceed DocX | [xceed-docx.md](xceed-docx.md) |
| MiniExcel | [miniexcel.md](miniexcel.md) |
| ShapeCrawler | [shapecrawler.md](shapecrawler.md) |

## Things that are the same on every page

- **Packages.** Reference `RecursiveAI.OneDocument.All`, or only the kind you need: `.Spreadsheets` for workbooks, `.Documents` for word-processing documents, `.Presentations` for presentations, `.Rendering` for PDF and PNG output. No registration call is needed.
- **Opening finds the format from the content.** `Workbook.Open`, `Document.Open` and `Presentation.Open` take a path, a stream or a byte source; the extension is never trusted. `Office.Open` returns whichever kind the file is.
- **Opening never recalculates.** Cached values are what you read. `Workbook.Recalculate()` runs the formulas when you ask.
- **Unknown content survives.** A part you did not touch is written back byte-identical; an edited part keeps the elements and attributes the model does not know.
- **Cells are addressed on the sheet**, `sheet["B2"]` or `sheet[row, column]` (1-based). There is no range object with bulk setters; loop, or use the typed-row binding for tables of data.
- **Damage is a defect, not a crash.** What Office repairs on open is repaired and listed in `file.Defects` with a location; hostile input hits limits (`OfficeLimits`) and raises a typed exception.
- **No network.** External links, remote templates and linked pictures are default-deny and go through resolvers you supply.
- **No speed claims here.** Benchmark rows against these libraries are published with the 1.0 gate (`bench/README.md` has the preview rows and the method).

Where a task has no high-level OneDocument call yet, the page says so and names the lower layer that does it (the typed views, `.View`, over the same markup, or the markup tree).
