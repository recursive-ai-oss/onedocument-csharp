# PowerPoint 97-2003 fixtures

Each `lo-*.ppt` was written by LibreOffice 26.8.2.2 (build 9dea406ab7695df6325b11b0e19fdca8db4b7276), run as a black-box converter
(`tools/ppt/make-fixtures.sh`, filter `MS PowerPoint 97`), from the generated PresentationML deck named in the file:

| File | Source deck |
|---|---|
| `lo-slide-*.ppt` | `testdata/slides/slide-*.pptx` |
| `lo-drawingml-drawing-slide-shapes.ppt` | `testdata/drawingml/drawing-slide-shapes.pptx` |
| `lo-<name>-template.ppt` | `testdata/templates/pptx/<name>/template.pptx` |

The converter writes a large thumbnail into `SummaryInformation`. `PptProbe slim` removes every stream except `Current User`,
`PowerPoint Document` and `Pictures`, which stay byte for byte as the converter wrote them, so the fixtures are small.

The tests read them back and compare the text with the source deck. PowerPoint's own conversion of the same decks is a separate,
unanswered comparison (`docs/ppt.md`).
