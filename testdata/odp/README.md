# OpenDocument presentation fixtures

Each `lo-*.odp` was written by LibreOffice (version in `oracle-version.txt`), run as a black-box converter (`tools/odp/make-fixtures.sh`, filter
`impress8`), from the generated PresentationML deck named in the file:

| File | Source deck |
|---|---|
| `lo-slide-*.odp` | `testdata/slides/slide-*.pptx` |
| `lo-drawingml-drawing-slide-shapes.odp` | `testdata/drawingml/drawing-slide-shapes.pptx` |
| `lo-<name>-template.odp` | `testdata/templates/pptx/<name>/template.pptx` |

The tests (`OdpFixtureTests`) read them back and compare slide count, hidden slides, the text and notes of every slide, tables and pictures with the
source deck. `OdpRoundTripTests` writes the same decks as ODP and FODP with this project's exporter and reads them back. PowerPoint opening our
ODP is a separate, unanswered comparison (`ORACLE-REQUEST.md`).
