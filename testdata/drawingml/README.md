# DrawingML differential inputs

Generated documents that exercise the shape painter: preset geometries, adjust values and custom geometry, fills, lines, effects,
groups, pictures, and text in shapes (`drawing-*.docx`), plus a one-slide presentation of the same shapes (`drawing-slide-shapes.pptx`).
They are written by `tools/LayoutProbe gen <dir>` from `tests/OneDocument.Rendering.Tests/DrawingSamples.cs`; regenerate them there.
`tools/layout-differential.sh` renders the DOCX files with OneDocument and asks Word for its own PDF of every file; the PowerPoint
export of the presentation is kept for the slide renderer. Nothing here has been compared with Office yet.
