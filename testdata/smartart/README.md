# SmartArt differential inputs

Generated documents that exercise the diagram painter: one Word document per diagram (and one PowerPoint presentation for six of
them), for six algorithm families: list (`snake`), process (`lin` with block arrows), cycle, hierarchy (`hierRoot`/`hierChild` with
elbow connectors), pyramid (`pyra`) and relationship (`composite`, three overlapping circles), plus lists with a colour `span`, child
bullets, long text and a single node. Each exists twice: `smartart-NAME.docx` stores no drawing, so a reader has to lay the diagram
out from the data model and layout definition; `smartart-NAME-cached.docx` stores the drawing OneDocument's own layout produced.

They are written by `tools/LayoutProbe gen <dir>` from `tests/OneDocument.Rendering.Tests/SmartArtSamples.cs`; regenerate them there.
The layout definitions, colour transforms and quick styles are written for these files and carry test URIs
(`urn:onedocument.test/…`), so an application treats them as custom layouts and uses the definition in the file.

`tools/layout-differential.sh` renders the Word files with OneDocument and asks Word for its own PDF of every file; the PowerPoint
export of the presentations is kept for the slide renderer. For the files without a stored drawing, Word's PDF shows how Word lays
out these definitions; for the `-cached` files it shows what Word does with a stored drawing. Nothing here has been compared with
Office yet.
