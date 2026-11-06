# OpenDocument (ODF 1.3; 1.2 and the 1.4 drafts read)

OpenDocument files have a **native layer in the core package** (`RecursiveAI.OneDocument.Odf`) that the text, spreadsheet and presentation
units share, and a **conversion into and out of the word-processing hub** in `.Documents`. This note covers both; spreadsheets and
presentations add their own conversions on the same package layer.

## The package layer (core)

| Type | What it is |
|---|---|
| `OdfPackage` | One ODF file: a zip package (OpenDocument 1.3 Part 3) or a flat XML document (`office:document`, Part 1), told apart by content. Entries as bytes, the four XML parts as lossless markup trees, the manifest, the style families and the metadata. |
| `OdfManifest`, `OdfManifestEntry` | `META-INF/manifest.xml`: full path, media type, version, encryption flag. |
| `OdfStyleSet`, `OdfStyle` | Common and automatic styles of `styles.xml` and `content.xml`: lookup by family and name, inheritance along `style:parent-style-name` ending in the family's `style:default-style`, `Resolve` of one property. Families: the `style:family` values plus `list`, `outline`, `page-layout`, `master-page`, `data`. |
| `OdfMetadata` | `meta.xml`: title, subject, description, creator, last-modified-by, dates, language, generator, keywords, user-defined properties, document statistics. |
| `OdfNamespaces`, `OdfMarkup`, `OdfMediaTypes` | Namespace names, child/attribute helpers, media types and the body element of each. |

```csharp
using var pkg = OdfPackage.Open(ByteSource.FromFile("a.odt"));
MarkupNode body = pkg.Body;                         // lossless markup tree of content.xml
OdfStyle? s = pkg.Styles.Find("paragraph", "Heading_20_1");
string? size = pkg.Styles.Resolve(s!, "text-properties", OdfNamespaces.Fo, "font-size");
pkg.SetEntry("Pictures/a.png", bytes, "image/png"); // added to the manifest
pkg.WriteTo(stream);                                // zip; pkg.WriteFlatTo(stream) for a flat file
```

**Round trip.** A package written without edits copies each entry raw (the stored bytes, CRC, method and time, never recompressed). A part whose
markup tree was edited is written from the tree, which keeps unknown elements, attributes, prefixes, comments and order. The manifest is
written again only when an entry was added, removed or retyped. A written package starts with an uncompressed `mimetype` entry with no extra
field, as Part 3 asks, then the other entries in their order. A flat file writes its own tree. `WriteFlatTo` on a zip package merges the four
parts under one `office:document` (namespace declarations united, font faces and automatic styles de-duplicated) and inlines packaged
pictures as base64 `office:binary-data`. Flat → zip at the package level is not implemented; the hub converts through the model.

**Tolerant reading.** Missing or misplaced `mimetype`, a missing or damaged manifest, manifest entries without a file and damaged zip
directories are repaired and reported (`defects.md`). A part that is not well-formed XML, or whose root is wrong, raises its typed error when
first used. Entry names that are empty, absolute or contain `..` are dropped. Encrypted packages (any `manifest:encryption-data`) raise
`OfficeUnsupportedException`; ODF encryption and signatures are not implemented. The limits object passes through: part count, part size,
expansion ratio, XML depth, no DTDs.

## Text documents (`Document`)

`Document.Open` accepts ODT, OTT and FODT by content (the `mimetype` entry or `office:mimetype`), never the extension. The file is converted
to an in-memory DOCX package, and the hub wraps it like any DOCX; `Document.Format` stays `Odt`, `Ott` or `Fodt`, and
`Document.ImportLoss` lists what did not map. `SaveAs("x.odt" | "x.ott" | "x.fodt")` (or with an explicit `OfficeFormat`) writes OpenDocument
from the hub, and `Document.ExportLoss` lists what it could not carry. `Office.Open`, `Office.ExtractText` and `Office.Convert` reach the
formats through the registry. Direct entry points: `OpenDocumentText.ToPackage` for the DOCX package.

**Untouched files.** An ODT/OTT/FODT read and saved in its own format without an edit writes the bytes it was read from. Any edit reaches the
exporter, which writes a valid package of its own from the hub; a save to another OpenDocument format goes through the exporter too.

### Reading (ODF → hub)

| ODF | Becomes |
|---|---|
| `style:style` (paragraph, text), `style:default-style` | `w:style` with `basedOn`, `next`, outline level; the default paragraph style becomes `w:docDefaults`; `Standard` becomes `Normal`, `Heading N`, `Title`, `Header`, `Footer`, `Footnote`, `Internet link` and the like take Word's built-in ids and names. Display names are used, `_20_`-style name encodings decoded |
| automatic styles | direct formatting on the paragraph or run, with the nearest common ancestor as `w:pStyle`/`w:rStyle` |
| `fo:`/`style:` text properties | bold, italic, underline styles, strikes, small caps, caps, colour, background (as shading), font size and fonts (latin, Asian, complex), super/subscript, spacing, scale, hidden text, relief, outline, shadow, language |
| paragraph properties | alignment, indents, spacing, line height (percent, exact, at least), keep with next/together, break before page, widows/orphans, background, borders with padding, tab stops with leaders, writing mode |
| `text:p`, `text:h` | `w:p`; headings carry `w:outlineLvl` |
| white space | collapsed as Part 1 says (`text:s`, `text:tab`, `text:line-break` explicit) |
| `text:list`, `text:list-item`, `text:list-header`, list styles, `text:outline-style` | `w:numPr` over `numbering.xml`; one abstract numbering per list style, one `w:num` per list that restarts; `text:continue-numbering`, `text:continue-list`, `text:start-value` map to reuse and `w:startOverride`; label alignment (or the older space-before/min-label-width) becomes indents |
| `table:table` | `w:tbl` with a grid from the column styles (`style:rel-column-width` shares what the fixed columns leave), spans, covered cells (`w:vMerge`), header rows, row heights, nested tables, cell borders, background, padding and vertical alignment |
| master pages and page layouts | one `w:sectPr` per run of paragraphs on a master page: size, orientation, margins (header and footer heights folded in), columns, page-number format; `style:header`, `-left`, `-first` become default, even and first headers, `evenAndOddHeaders` and `titlePg` |
| `text:note` | footnotes and endnotes |
| `text:a`, bookmarks, reference marks | `w:hyperlink` (external targets become external relationships and are never followed), `w:bookmarkStart/End` |
| fields | `PAGE`, `NUMPAGES`, `DATE`, `TIME`, `TITLE`, `SUBJECT`, `AUTHOR`, `KEYWORDS`, `COMMENTS`, `FILENAME`, `REF`, `PAGEREF`, `NOTEREF`, `SEQ` as simple fields with the stored result; other fields keep their text |
| `text:table-of-content`, `text:alphabetical-index` | a `TOC` / `INDEX` field around the stored entries |
| `office:annotation`, `office:annotation-end` | comments with ranges |
| `text:tracked-changes` | insertions and deletions (`w:ins`, `w:del` with `w:delText`) with author and date |
| `draw:frame` with `draw:image` | inline or anchored DrawingML picture (position, wrap, alt text, title); embedded objects keep their preview picture |
| `draw:frame` with `draw:text-box` | a VML text box with converted content |
| `office:meta` | core, extended and custom properties |

Reported as losses (`loss.*`): `unmapped-markup.<element>`, `shapes` (lines, rectangles, custom shapes, groups), `embedded-objects` (formulas,
charts and OLE objects keep only a preview picture), `forms`, `linked-images` (never fetched), `pictures` (missing or undecodable data),
`picture-cropping`, `ruby` (the text above the base), `formatting-revisions`, `column-breaks`, `page-break-after`, `table-page-break`,
`section-columns` (text sections with columns), `index-fields` (indexes other than contents and alphabetical), `page-background`.

### Writing (hub → ODF)

The exporter reads the hub's markup tree and writes `content.xml`, `styles.xml` and `meta.xml` (`settings.xml` is not written). Styles map back by
the same names; direct formatting becomes automatic styles (de-duplicated); lists become list styles (`WWNum<n>`, one per `w:num`; a list that
resumes after other content sets `text:continue-numbering`); tables keep spans, vertical merges, header rows, widths, borders (from the cell, the
table and its table style), shading, margins and heights; each `w:sectPr` becomes a page layout and a master page with its headers and
footers, and the first block of a later section names its master page; notes, comments with ranges, bookmarks, hyperlinks, fields (page
number, page count, date, time, document properties, references, sequences, TOC), tracked insertions and deletions, pictures and text boxes
(inline and anchored) are written. A page or column break inside a paragraph splits it, because ODF has no inline page break.

Reported as losses: `content-controls` (read through), `equations` (written as linear text), `fields` (other fields keep their last result),
`charts`, `smartart`, `shapes`, `pictures` (linked or missing data), `ole-objects`, `ruby`, `continuous-section-page-change`.

## Checks

- Written ODT, OTT and FODT validate against the OASIS ODF 1.3 RelaxNG schema with Jing (`tools/validate/validate_odf.py`; ID/IDREF checking is
  switched off because Jing rejects the schema's own ID typing) and open in LibreOffice headless, which converts them to PDF and DOCX.
- Text of the DOCX → ODT → hub round trip equals the source's; files LibreOffice writes from the same DOCX read into the hub with the same text
  (`testdata/odf`).
- Converted DOCX validates against the ECMA-376 Transitional schemas.
- Fuzz targets `odfpkg` (the package layer) and `odt` (the conversion in both directions).
