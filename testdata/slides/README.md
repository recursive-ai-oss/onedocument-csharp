# Slide rendering differential inputs

Generated decks that exercise slide rendering: placeholder inheritance, bullets and automatic numbers, autofit (stored, computed,
resize shape), vertical text and columns, anchors and wrapping, alignment and spacing, run formatting, tables with the default table
style, hidden slides with notes, groups and z-order, backgrounds, a picture, chart and saved SmartArt drawing, and a deck of
questions the standard leaves open (`slide-spacing-checks.pptx`).

They are written by `tools/LayoutProbe gen <dir>` from `tests/OneDocument.Rendering.Tests/SlideCases.cs` and `TestPptx.cs`;
regenerate them there. `tools/layout-differential.sh` renders each deck with OneDocument and asks PowerPoint for its own PDF of the
same file, then compares page counts and the text lines of each page. The theme's fonts are Liberation Sans so that both sides can use
the same metrics. Nothing here has been compared with PowerPoint yet; `docs/presentation-rendering.md` lists the open rules.
