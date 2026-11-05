# Presentations: the PresentationML hub

`RecursiveAI.OneDocument.Presentations` holds `Presentation`, `Slide`, `SlideLayout`, `SlideMaster`, `NotesSlide`, `NotesMaster`, `Shape`, `TextBody`/`Paragraph`/`Run` and `Table`. It is a set of views over the lazy markup tree of each part; nothing is copied out of the part buffers. The XML documentation on the types states the details; this note states the rules.

## What is read, when

`Presentation.Open` (path, stream or `IByteSource`) identifies the content (never the extension), opens the package and reads the content types, the package relationships and the presentation part. Every other part is read on first use.

| Part | Read when |
|---|---|
| the presentation part | the first member that needs it (lazy main part); an untouched save never parses it |
| a slide, layout, master, notes slide | first use of that object's shapes or properties, or when a slide operation needs its relationships |
| theme | the first colour or font resolution through that master or layout |
| comments, comment authors, sections | when they are first enumerated |
| media, charts, diagrams, OLE, tags, timing, transitions | never; they stay as stored |

Supported package kinds: PPTX, PPTM, POTX, PPTX templates and slide shows (the type is retyped through the main part's content type on save), Transitional and Strict, and Flat OPC. A chart, SmartArt diagram or OLE object is a graphic frame whose `References` lists the related parts; the parts stay as stored. `mc:AlternateContent` is kept as written; the shape children of an alternate content block are those of its first `Choice`, and edits never touch the `Fallback`.

## The round-trip contract

- A part nobody edited is copied as stored (the raw zip entry), so a presentation opened and saved without edits has every part byte-identical.
- An edited part keeps every element, attribute, prefix, `mc:` block and `extLst` it did not edit, in order.
- `docProps/app.xml` (slide counts, titles) is not rewritten by slide operations; PowerPoint rebuilds it on save.

## Inheritance

Resolution follows the chain slide → layout → master → presentation defaults.

- **Placeholder matching.** Slide to layout: by `idx` when it is non-zero (a type-compatible placeholder preferred), otherwise by type, where `title` and `ctrTitle` match each other. Layout to master: by role; every content type and `subTitle` map to the master's body placeholder. Notes slide to notes master: by type and `idx`.
- **Text styles.** For a paragraph at level *n*, properties come from, in order: the shape's own `a:lstStyle`, the matching layout placeholder's `a:lstStyle`, the matching master placeholder's `a:lstStyle`, then the master's `titleStyle`, `bodyStyle` or `otherStyle` by role (a non-placeholder shape skips the master styles and uses `otherStyle`), then the presentation's `defaultTextStyle`. Paragraph and run properties merge attribute by attribute; the bullet (none, character, auto-number, picture, with its font, colour and size) is taken as a unit from the first source that names one.
- **Body properties and bounds** inherit the same way (insets, anchor, wrap, autofit kind, and the shape's offset and extent when the slide's `xfrm` is absent).
- **Colour.** `p:clrMap` of the master, overridden by the layout's and slide's `p:clrMapOvr`, maps `bg1`/`tx1`/… to theme slots; scheme colours resolve through the map and then the theme of the master.

Placeholder matching and the chain are written from ECMA-376 Part 1 §19.3 and §21.1; PowerPoint's own behaviour for odd decks is not yet checked against the application.

## Editing

All operations are on `Presentation`; each leaves untouched parts alone.

- **AddSlide(layout)** creates a slide part with the layout's non-footer placeholders (date, footer, slide number and header placeholders are skipped; a text body only for title, centre title, subtitle, body and object placeholders), the relationship to the layout, the `sldIdLst` entry with a new id, and the content-type override.
- **DuplicateSlide** copies the slide part and the parts only it uses. Shared (the new slide's relationship points at the same part): images, hyperlinks, layouts, masters, notes and handout masters, themes, video, audio, media, HD photo, fonts. Copied transitively with new names: charts and their embedded workbooks, diagrams, OLE objects, tags, comments and the notes slide. `p14:creationId` is replaced by a deterministic hash of the source's value and the new part name. The copy joins the source slide's section. Duplicated markup is the source markup, so schema errors the source slide had are present in the copy and nothing else is added.
- **DeleteSlide** removes the slide, its `sldIdLst` entry and its section entry, and every part only it reached (decided by a fixed-point reference count over the package); shared parts stay.
- **MoveSlide** reorders `sldIdLst` and the section lists.
- **SetNotesText** edits the notes slide, creating it (with its relationships to the slide and the notes master) when the slide has none.
- **Text edits.** Setting a shape's text edits its first paragraph in place; further paragraphs copy the first paragraph's `a:pPr` and first run's `a:rPr`. Characters XML cannot hold are written as `_xHHHH_`; a vertical tab is a line break (`a:br`).
- **Table cells** are addressed on the logical grid; `gridSpan`, `rowSpan`, `hMerge` and `vMerge` give the merge model.

Slide ids come from the existing maximum plus one; names and relationship ids are the next free ones. Output is deterministic.

## Defects

Codes `pptx.*` in `defects.md`. A missing or wrong-root slide, layout or master reads as empty or as inheriting nothing and is reported once per part; a missing main part is fatal.
