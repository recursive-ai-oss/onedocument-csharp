# Floating-object differential inputs

Generated documents with anchored objects: every relative-from base, alignments, percentage and simple positions, each wrap type and
side, tight wrap round a triangle and an ellipse, behind and in-front stacking, anchors that move to the next page, a second column,
objects in table cells, a floating table, text frames, drop caps, VML shapes and a table in a text box. They are written by
`tools/LayoutProbe gen <dir>` from `tests/OneDocument.Rendering.Tests/FloatSamples.cs`; regenerate them there.
`tools/layout-differential.sh` asks Word for its own PDF of each file and compares page counts and text lines with ours. Nothing here
has been compared with Word yet.
