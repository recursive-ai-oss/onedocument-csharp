# PowerPoint 97-2003 files (`.ppt`)

`Presentation.Open`, `Office.Open`, `Office.ExtractText` and `Office.Convert` read PowerPoint binary presentations. The file is read at record level
(`RecursiveAI.OneDocument.Ppt.Binary`, on the shared OfficeArt reader in `officeart.md`), converted to PresentationML in memory, and the presentation hub
opens that package as typed views. Everything the hub does with a `.pptx` it does with the converted file: edit, render, template, save as `.pptx`.
`Presentation.ImportLoss` lists what did not map; `Presentation.SourceFormat` is `OfficeFormat.Ppt`. Writing `.ppt` is not offered.

The result has no file path, so `Save()` never writes over the binary original; `SaveAs` writes PresentationML.

## Structure that is read

| Stream | Use |
|---|---|
| `PowerPoint Document` | the records: persist objects, persist directories, user edits |
| `Current User` | the offset of the newest user edit; header token `0xF3D1C4DF` marks an encrypted file |
| `Pictures` | the BLIP data the drawing group's store points into |
| anything else | listed in `PptFile.StreamNames`; a `_VBA_PROJECT_CUR` storage is reported (`ppt.vba-not-carried`), never run |

**Edit chain and persist directory** ([MS-PPT] §2.1.2, §2.3.3 to §2.3.6). PowerPoint appends to the stream on every incremental save: the
objects that changed, a `PersistDirectoryAtom`, and a `UserEditAtom` that names its predecessor. The reader starts at the edit the Current User
stream names and follows `offsetLastEdit` back while the offset strictly decreases. The persist directory of each edit is read newest first and
the first definition of a persist identifier wins, so the last edit wins and older copies stay in the stream untouched. A persist object is parsed
when first asked for and cached.

Repairs, each a defect with the stream offset:

- no usable Current User stream, or an offset that is not a `UserEditAtom`: the edits are found by scanning the top-level records;
- a chain that does not move backwards ends at the loop;
- a user edit whose directory is not there: its objects are ignored, older edits still serve;
- a document persist identifier that is not listed: the first top-level `DocumentContainer` is used;
- a record that claims more bytes than the stream holds is clamped (`art.record-overrun`).

Encrypted files (header token or a `CryptSession10Container` reference) are decrypted with `OpenContext.Password`, or raise `OfficeEncryptedException`; see `encryption.md`. PowerPoint 4.0 and 95 files (a `PersistentStorage Directory` stream, no user edits) are `OfficeUnsupportedException`: [MS-PPT] does
not describe them.

## Text

A text body is a `TextHeaderAtom` and the records after it: `TextCharsAtom` (UTF-16) or `TextBytesAtom` (Latin-1), `StyleTextPropAtom`,
metacharacter atoms (slide number, date, header, footer), `TextSpecialInfoAtom` (language), `TextRulerAtom`, interactive records. Paragraphs end at
U+000D, forced line breaks are U+000B; every body has an implicit terminating break, so style runs cover the text plus one. Bodies live in the
shape's `OfficeArtClientTextbox`, or in the document's slide list when the shape holds an `OutlineTextRefAtom` (the older layout).

`TextPFException` and `TextCFException` are read with their mask bits; a field exists only when its bit is set, and what is not set inherits from the
master text styles, exactly as in PowerPoint. The converter writes only the set fields, so the hub's inheritance resolves them the same way.
`TextRulerAtom` supplies the left margin and indent per level that paragraph runs cannot carry. Paragraph spacing: 0 to 13200 is a percentage of the
line, a negative value is master units. Master units are 1/576 inch (1,587.5 EMU).

## Conversion

- **Masters and layouts.** Each main master becomes a slide master with a theme, `txStyles` from its `TextMasterStyleAtom` records (title, body, other) and
  its drawing. PowerPoint 97-2003 has no layouts; one layout is written per distinct slide layout type and placeholder set used with a master,
  modelled on the first slide that uses it (placeholders only, positions from that slide). A title master becomes the "Title Slide" layout of its
  main master, with its shapes. Layout type names follow `SlideLayoutType`.
- **Colours.** The eight scheme colours become the theme (`dk1` text, `lt1` background, `dk2` shadows, `lt2` title text, accents 1 to 4 fill, accent,
  accent and hyperlink, accent and followed hyperlink). A scheme index becomes a theme colour when the slide's scheme equals its master's, otherwise the
  slide's RGB value.
- **Slides.** Shapes keep their z-order, groups become groups with the group's coordinate space as child extents. Rotation and flips come from the
  OfficeArt properties. Backgrounds (the drawing's background shape) are written unless the slide uses the master's. Slide flags: follow master objects
  is `showMasterSp`, hidden comes from the transition record.
- **Geometry.** Preset shape types (about 130) map to DrawingML presets; freeforms become custom geometry from the vertex and segment arrays; the rounded
  rectangle's corner is converted; other adjustments are dropped (reported).
- **Fill, line, shadow.** Solid, picture, texture and gradient fills; dash styles, compound lines, caps, joins, arrowheads; an outer shadow.
  Pictures come from the BLIP store (`Pictures` stream), metafiles inflated, DIBs given a BMP header; crops become `srcRect`.
- **Text boxes and placeholders.** Insets, anchors, wrap, text flow and fit-shape-to-text come from the text properties. Placeholder types map to `ph`
  types with indexes assigned in order (date 10, footer 11, slide number 12). WordArt text (geometry text) is written as plain text.
- **Fields.** Slide numbers and dates become `a:fld`; a header or footer metacharacter becomes the text of the page's `HeadersFooters` record, else the
  document's.
- **Tables** as PowerPoint 2003 stores them: a group whose properties mark it a table and whose children are cell rectangles in row-major order; the
  row count is the length of `tableRowProperties`. A regular grid becomes `a:tbl` with each cell's own outline as its borders; anything else stays a group.
- **Notes** become notes slides with a notes master, the handout master a handout master, each with a theme.
- **Comments** (PowerPoint 2002 extension data) become legacy comment parts with an author list; positions are master units, as in `p:pos`.
- **Hyperlinks** on shapes and text ranges: web and file targets become external relationships, `slideId,slideNumber,title` targets slide jumps, jump
  actions (next, previous, first, last, end show) the matching `ppaction`.
- **Transitions** map by effect type and direction; advance on click, automatic advance time and speed are carried. Effects PowerPoint 97 had that
  PresentationML lacks are reported.

## Loss report

`Presentation.ImportLoss` items (code, feature, count, message); each is also a defect with the record offset in the source stream.

| Code | What did not carry |
|---|---|
| `ppt.shape-approximated` | a shape type with no preset (or a picture frame with nothing in it) is a rectangle |
| `ppt.shape-adjust-dropped` | adjustment handles other than the rounded rectangle's corner |
| `ppt.fill-approximated` | pattern fills are solid fills in the foreground colour |
| `ppt.path-escape` | arcs and fill switches in freeform paths |
| `ppt.geometry-guides` | guide-formula coordinates in freeform paths (written at the origin) |
| `ppt.wordart-flattened` | WordArt effects and text paths (text kept) |
| `ppt.picture-pict` | a Macintosh PICT picture is carried as is; Office will not show it |
| `ppt.picture-missing` | a picture index that is not in the store |
| `ppt.table-as-group` | a table group that is not a regular grid |
| `ppt.table-borders-approximated` | table border lines drawn as separate shapes |
| `ppt.transition-dropped` | a transition type PresentationML lacks |
| `ppt.animations-dropped` | animation effects (shape animation info, PowerPoint 2002 time nodes) |
| `ppt.ole-object-flattened` | OLE objects whose storage or picture is missing or unreadable, and controls: only the preview picture is written. Objects that could be carried are not counted: see `ole.md` |
| `ppt.document-properties-not-carried` | properties of the summary information streams with no counterpart in the package (the thumbnail, the VBA signature, hyperlink lists, custom types without an equivalent), each named in the message, or a stream that could not be read. The rest are carried: see `ole.md` |
| `ppt.vba-not-carried` | the VBA project |
| `ppt.header-footer-field` | a header or footer metacharacter with no text to show |
| `ppt.placeholder-unknown` | a placeholder type this converter does not know |

## Open questions

The specification leaves these open; PowerPoint decides at the final gate. `testdata/ppt` holds LibreOffice's PowerPoint 97 output for the
generated decks and `tools/office-oracle` cases ask PowerPoint to open each file and save it as `.pptx` for comparison; the answers are not in yet.

- gradient angle: `fillAngle` is counter-clockwise from the upward vector; the conversion measures DrawingML's clockwise angle from the x axis as
  270 degrees minus `fillAngle`;
- the ribbon pair (shape types 53 and 54, 107 and 108) maps to `ribbon2`/`ribbon` and `ellipseRibbon2`/`ellipseRibbon` by position, not by a
  verified correspondence;
- first-line `indent` is written relative to the left margin (`indent - marL`) as DrawingML defines it;
- layout modelling: one layout per layout type and placeholder set, not the layouts PowerPoint itself would generate;
- table borders from the cell outlines, not from the separate line shapes.

## Limits

The limits object applies throughout: `MaxRecordCount`, `MaxRecordSize`, `MaxRecursionDepth` (record nesting and group nesting), `MaxAllocationBytes`
(inflated metafiles), `MaxPartSize` (the streams), `MaxDefects`. Record lengths are checked against the data before anything is allocated.

## Tools

`tools/PptProbe` opens, dumps, converts and lists streams; `tools/ppt/make-fixtures.sh` makes the LibreOffice fixtures, `tools/ppt/check.sh` converts a
directory and runs the schema and LibreOffice checks; `fuzz` target `ppt`.
