# Defect catalogue

A defect's `Code` is a stable, lower-case, dot-separated identifier: `<layer>.<problem>`. The first segment names the layer that found it. A code is never reused for another meaning. Each layer adds its codes here when it adds the check; the tests of that layer assert them.

| Layer prefix | Layer | Kind (`DefectKind`) |
|---|---|---|
| `limit.` | resource limits | `Limit` |
| `encryption.` | encrypted input | `Encryption` |
| `unsupported.` | features not handled | `Unsupported` |
| `format.` | format identification | `Structure` |
| `packaging.` | zip, compound file, OPC, ODF packages | `Packaging` |
| `xml.` | the XML tokenizer and markup tree | `Markup` |
| `biff.`, `doc.`, `ppt.`, `xlsb.` | binary record layers | `Record` |
| `ole.` | OLE property sets and object storages | `Record`, `Value`, `Structure`, `Encoding`, `Packaging`, `Limit` |
| `vba.` | VBA projects (reading, extraction, removal) | `Record`, `Reference`, `Packaging` |
| `sheet.` | the workbook model | `Reference`, `Value` |
| `formula.` | formula text and tokens | `Formula` |
| `numfmt.` | number-format codes | `Value` |
| `text.` | text extraction | `Structure`, `Reference`, `Markup`, `Packaging` |
| `rtf.` | RTF (tokenizer, group tree, hub conversion, mail layer) | `Record`, `Limit`, `Structure`, `Reference` |
| `markdown.` | Markdown (parser, hub conversion) | `Limit`, `Encoding`, `Structure` |
| `docx.` | the word-processing hub | `Structure`, `Reference`, `Value`, `Packaging` |
| `csv.` | delimited text (CSV, TSV) | `Structure`, `Markup`, `Encoding`, `Value` |
| `xlsx.` | the SpreadsheetML workbook hub and cell store | `Structure`, `Reference`, `Value`, `Formula`, `Packaging` |
| `pptx.` | the PresentationML hub | `Structure`, `Reference`, `Packaging` |
| `html.` | HTML import and export | `Unsupported`, `Structure` |
| `wml2003.`, `sml2003.` | the Office 2003 XML readers (Word, Excel) | `Structure`, `Reference`, `Value`, `Encoding`, `Formula` |
| `odf.` | the OpenDocument text conversion (`odf.md`); the package layer reports under `packaging.` | `Structure`, `Reference`, `Value`, `Encoding`, `Limit` |
| `calc.` | workbook calculation | `Formula`, `Limit`, `Reference` |
| `binding.` | reading and writing rows of plain objects (`Spreadsheet.Read<T>`, `Write<T>`) | `Structure`, `Reference`, `Value` |
| `template.` | the template engine: reading, checking and rendering templates | `Markup`, `Structure`, `Formula`, `Reference`, `Value` |
| `render.` | writing laid-out pages as PDF or images | `Structure` |

## Codes in use

| Code | Severity | Meaning |
|---|---|---|
| `limit.<property>` | fatal | The named `OfficeLimits` property (in kebab case, for example `limit.max-xml-depth`) was exceeded. `OfficeLimitException.LimitName` has the property name. |
| `encryption.password-required` | fatal | The input is encrypted and no password was given. |
| `encryption.password-incorrect` | fatal | The password does not open the input. |
| `encryption.malformed-header` | fatal | An encryption header or descriptor (`EncryptionInfo`, `FILEPASS`, `CryptSession10Container`) is cut short or inconsistent. |
| `encryption.malformed-data` | fatal | The encrypted data is shorter than its declared size, ends inside a cipher block, or cannot be decrypted. |
| `encryption.integrity-mismatch` | repaired | The agile data integrity HMAC does not match; the decrypted data is returned anyway. |
| `encryption.inner-format` | fatal | The decrypted package is not a format the readers open. |
| `unsupported.feature` | fatal | The input uses something this library does not handle; `OfficeUnsupportedException.Feature` names it. |
| `unsupported.format-not-available` | fatal | No reader or writer is registered for the format (its package is not referenced or does not implement it). |
| `format.unrecognised` | fatal | The content is not a format OneDocument knows. |
| `render.failed` | fatal | The renderer could not write a document as PDF, or draw a page as an image (a writer error, or a page size past `DocumentRenderer.MaxPngPixels`); the inner exception has the cause. Layout problems are not defects: they are `LayoutNote`s with `layout.` codes (see `layout.md`). |
| `render.metafile-damaged` | dropped | A picture that should be a metafile is neither an EMF nor a WMF (no EMF signature or WMF header), or its header or a limit stopped interpretation; the picture is not drawn as vector content. |
| `render.metafile-diagnostic` | dropped | The metafile interpreter skipped or could not honour a record (the message names its index), or a font was unavailable, or a stroke or the fallback bitmap failed. |
| `render.metafile-rasterised` | repaired | The picture uses something the display list cannot express (XOR and other raster operations, triangle gradients, hatch or pattern pens, union or exclusive-or clips) and is drawn as a bitmap. |
| `render.metafile-approximated` | repaired | A union or exclusive-or clip was drawn as a replacement (only with the raster fallback turned off). |
| `render.metafile-font` | repaired | A font named in a picture is not available and another was used (once per family). |
| `doc.malformed` | fatal | The Word binary reader met a structure it could not repair and stopped on an arithmetic or indexing failure; the reader normally reports its repairs under the codes below, so this code means a case worth a bug report with the file. |
| `doc.fib-truncated` | fatal | The `WordDocument` stream is too short for the file information block (FibBase, or the counted FibRgW97/FibRgLw97 sections). |
| `doc.bad-ident` | fatal | The stream does not start with a Word binary identifier (0xA5EC, or 0xA5DC for Word 6/95). |
| `doc.no-word-stream` | fatal | The compound file has no `WordDocument` stream. |
| `doc.no-table-stream` | fatal | The FIB names `0Table` or `1Table` as the table stream and the file does not have it. |
| `doc.no-piece-table` | fatal | The `Clx` is missing, empty or holds no piece table. |
| `doc.clx-bad` | fatal | The `Clx` holds an entry that is neither a property list (1) nor the piece table (2). |
| `doc.clx-truncated` | repaired | The `Clx` (or a `Prc`/the piece table in it) is longer than the table stream; what fits was used. |
| `doc.fib-pairs-truncated` | repaired | The FIB declares more offset pairs than fit in the stream; the pairs that fit were read. |
| `doc.piece-cp-order` | repaired or dropped | A piece starts at a negative position, ends before it starts, or the first piece does not start at 0. |
| `doc.piece-out-of-range` | repaired | A piece reads past the end of the `WordDocument` stream; its character positions are kept and the missing characters read as spaces. |
| `doc.story-out-of-range` | repaired | A story length (`ccpFtn`, `ccpHdd` and so on) reaches past the text the piece table holds; it was clipped. |
| `doc.codepage-unsupported` | repaired | 8-bit text of a Word 6/95 file is in a character set other than ANSI; it was read as Windows-1252. |
| `doc.fkp-out-of-range` | dropped | A bin table entry points at a formatted disk page outside the `WordDocument` stream. |
| `doc.fkp-bad` | dropped | A formatted disk page holds a run count that cannot fit, or a paragraph property record that runs past the page. |
| `doc.plc-truncated` | repaired | A `Plc` runs past the end of the table stream; what fits was used. |
| `doc.stylesheet-bad` | dropped | The stylesheet header is cut short, or a style runs past the stylesheet; styles from that point were dropped. |
| `doc.lists-bad` | dropped | The list definitions end early; the remaining levels were dropped. |
| `doc.sepx-bad` | dropped | A section's property block runs past the `WordDocument` stream; the section has default properties. |
| `doc.huge-papx-bad` | dropped | `sprmPHugePapx` points outside the `Data` stream; the paragraph keeps the properties it has. |
| `doc.picture-unreadable` | dropped | A picture character has no readable picture in the `Data` stream. |
| `doc.ole-storage-missing` | dropped | An `EMBED` or `LINK` field names an object storage (`_<number>`, [MS-DOC] `sprmCPicLocation` with `sprmCFOle2`) that the `ObjectPool` does not have; the object keeps its preview picture and is counted in `loss.doc-ole-objects`. Kind `Reference`; the location is the storage path. |
| `doc.ole-link-unreadable` | dropped | A linked object's `\u0001Ole` stream names no file moniker the reader understands; the object keeps its preview picture. Kind `Reference`. |
| `doc.ole-object-in-markup` | dropped | An object field sits inside tracked changes, a bookmark or a comment range, where replacing it with an object element would unbalance the markup; the object keeps its preview picture. Kind `Reference`. |
| `doc.sprm-not-mapped` | dropped | A property modifier has no counterpart in the document model (once per modifier, with the offset of its first occurrence); the count is the `loss.doc-sprms` entry of the conversion. Kind `Unsupported`. |
| `packaging.damaged` | fatal | The container is damaged (zip or compound-file structure) beyond what recovery repairs, or a stream cannot be read; the location holds the part or stream and the offset when known. |
| `packaging.refused` | fatal | The container reader refused the input for safety (unsafe path, overlapping entries). |
| `packaging.not-a-package` | fatal | The input is neither a zip nor a Flat OPC document. |
| `packaging.zip-damaged` | repaired | The zip directory or an entry header was damaged and recovery mode (scanning local headers) was used; the location holds the entry and offset. |
| `packaging.missing-content-types` | fatal | The package has no `[Content_Types].xml` (ECMA-376 Part 2 §7.3.7). |
| `packaging.bad-content-types` | fatal or repaired | `[Content_Types].xml` is not well formed. Fatal when nothing could be read from it; otherwise the entries before the damage are used. |
| `packaging.dtd-refused` | fatal or dropped | A packaging XML part (content types, relationships, Flat OPC) declares a DTD, which is always refused (§6.2.5). |
| `packaging.bad-content-type-entry` | dropped | A `Default` or `Override` lacks its `Extension`, `PartName` or `ContentType`, or the part name is not valid. |
| `packaging.duplicate-content-type` | dropped | An extension or part name has more than one `Default` or `Override` (§7.2.3.2); the first wins. |
| `packaging.missing-content-type` | repaired | A part has neither an override nor a default for its extension; it is kept with an empty content type. |
| `packaging.duplicate-part-name` | dropped | The same part name is stored twice (a zip item or a Flat OPC part); the first is used and the other is kept as stored, not exposed. |
| `packaging.part-name-case-collision` | dropped | Two part names differ only in ASCII case (§6.2.2.3); the first is used. |
| `packaging.invalid-part-name` | dropped | A zip item is not a valid part name (a backslash, a `..` segment, an empty segment). It is kept as stored, not exposed. An item with an empty name or a NUL in its name is left out entirely. |
| `packaging.interleaved-part` | dropped | A zip item is a piece of an interleaved part (§7.2.4). Pieces are kept as stored and not reassembled. |
| `packaging.bad-relationship` | dropped | A `Relationship` lacks `Id`, `Type` or `Target`. |
| `packaging.duplicate-relationship-id` | dropped | A relationship id repeats an earlier one in the same part (§6.5.3.4); the first wins. |
| `packaging.bad-relationship-target-mode` | repaired | `TargetMode` is neither `Internal` nor `External`; `Internal` is used. |
| `packaging.bad-relationship-target` | repaired | An internal target is not a valid part name once resolved (for example it climbs above the root); kept as written. |
| `packaging.dangling-relationship` | repaired | An internal target names a part that is not in the package; kept as written. |
| `packaging.bad-relationships-part` | dropped | A Relationships part is not well formed; the relationships before the damage are kept. |
| `packaging.bad-flat-opc` | fatal | The XML document is not Flat OPC (no `pkg:package` root) or is not well formed. |
| `packaging.bad-flat-part` | dropped | A `pkg:part` has no valid name, no content-type attribute, or undecodable base64; it is dropped. |
| `packaging.duplicate-stream-name` | dropped | Two compound-file streams, or a stream and a storage, have names equal ignoring case ([MS-CFB] 2.6.4); the first is exposed. |
| `packaging.cfb-header-inconsistent` | repaired | A compound-file header field disagrees with the file; a value derived from the file was used. |
| `packaging.cfb-truncated` | repaired | The compound file ends inside a table, the directory or the mini stream. |
| `packaging.cfb-allocation-table-unusable` | repaired | The FAT or DIFAT is damaged; sectors it described are treated as free. |
| `packaging.cfb-mini-stream-unusable` | repaired | The mini FAT or mini stream is damaged; mini streams that need the lost part are shortened. |
| `packaging.cfb-directory-unusable` | repaired | The directory chain is cut or misplaced; directory sectors were found by scanning. |
| `packaging.cfb-bad-entry` | repaired | A directory entry has invalid fields; replaced by usable values or ignored. |
| `packaging.cfb-bad-link` | repaired | A sibling or child link is invalid, loops or shares a subtree. |
| `packaging.cfb-orphaned-entry` | repaired | An entry cannot be reached from the root and was attached to it. |
| `packaging.cfb-damaged-stream` | repaired | A stream's sector chain is short, loops or leaves the file; the stream was shortened (`CfbStreamPart.IsDamaged`). |
| `xml.unexpected-end` | fatal | The part ends inside a tag, comment, CDATA section or processing instruction, or with elements still open. The offset is the end of the input. |
| `xml.dtd-refused` | fatal | The part has a document type declaration or an entity declaration. DTDs are never read; there is no switch. The offset is the `<!`. |
| `xml.invalid-character` | fatal | A character XML 1.0 does not allow: a control character other than tab, line feed and carriage return, `<` in an attribute value, `]]>` in text. |
| `xml.invalid-encoding` | fatal | The bytes are not valid UTF-8 (or UTF-16, for a UTF-16 part). The offset is the first bad byte (for UTF-16, in the original bytes). |
| `xml.unsupported-encoding` | fatal | The part is UTF-32 or another encoding XML parts do not use. UTF-8 and UTF-16 are read. |
| `xml.invalid-name` | fatal | An element, attribute or processing-instruction name is not a valid XML name (or has more than one colon, an empty prefix or local part), or a processing-instruction target is `xml` in another case. |
| `xml.syntax` | fatal | Any other break of the XML grammar: a missing `=` or quote, no white space between attributes, `--` inside a comment, markup that is not a tag, comment or CDATA section. |
| `xml.mismatched-end-tag` | fatal | An end tag does not match the open element, or there is none open. |
| `xml.duplicate-attribute` | fatal | An element has the same attribute twice, by written name or by namespace and local name. |
| `xml.unbound-prefix` | fatal | An element or attribute uses a prefix that no namespace declaration binds. |
| `xml.invalid-namespace-declaration` | fatal | A namespace declaration binds `xmlns`, binds `xml` to something else, binds the `xml` or `xmlns` namespace to another prefix, or gives a prefix an empty name. |
| `xml.invalid-reference` | fatal | A character or entity reference is unterminated, names an entity other than `amp`, `lt`, `gt`, `quot` and `apos`, or is not an XML character. |
| `xml.misplaced-content` | fatal | Text or a CDATA section outside the root element, or a second root element. |
| `xml.no-root` | fatal | The part has no root element. |
| `xml.invalid-declaration` | fatal | The XML declaration is malformed (version, encoding, standalone) or is not the first thing in the part. |
| `numfmt.too-many-sections` | fatal | A number-format code has more than four sections. The position is the start of the fifth. |
| `numfmt.unterminated-string` | fatal | A `"` is never closed. |
| `numfmt.unterminated-bracket` | fatal | A `[` is never closed. |
| `numfmt.unknown-bracket` | fatal | A bracketed item is not a colour, condition, locale tag, elapsed-time code or `DBNum`. |
| `numfmt.invalid-condition` | fatal | A `[...]` condition has an unknown operator or no number. |
| `numfmt.condition-on-text-section` | fatal | The fourth (text) section has a condition. |
| `numfmt.dangling-escape` | fatal | `\`, `_` or `*` ends the section with nothing to apply to. |
| `numfmt.unexpected-character` | fatal | An unquoted ASCII letter that is not a format code. |
| `numfmt.multiple-decimal-points` | fatal | A section has two decimal points. |
| `numfmt.multiple-exponents` | fatal | A section has two `E+`/`E-` codes. |
| `numfmt.exponent-without-digits` | fatal | `E+` or `E-` is not followed by `0` or `#`. |
| `numfmt.mixed-codes` | fatal | Date or time codes together with digit placeholders, an exponent or `General`; or a fraction with a decimal point; or `General` with digit placeholders. |
| `numfmt.invalid-date-code` | fatal | More `m`, `d`, `h` or `s` in a row than the code allows. |
| `numfmt.fraction-denominator` | fatal | A fixed denominator above 99999, or more than five denominator placeholders. |
| `text.no-main-part` | fatal | The package names no main document part, or the part it names is not in the package. |
| `text.part-missing` | dropped | A relationship of the main part (a header, footer, notes, comments, sheet or slide) names a part the package does not have; its text is left out. |
| `text.part-unreadable` | dropped | A part other than the main one is damaged (malformed XML or a damaged archive entry); the text before the damage was kept and the rest of that part is left out. The location is the part and offset. |
| `text.shared-string-out-of-range` | dropped | A worksheet cell names a shared string the table does not have; the cell is left out. The location is the part and the cell's offset. |
| `docx.revision-id-invalid` | repaired | A tracked change has no `w:id`, or one that is not a non-negative integer. The revision is listed with id -1 and works as any other. |
| `docx.revision-author-missing` | repaired | A tracked change has no `w:author`. It is listed with an empty author. |
| `docx.revision-date-invalid` | repaired | A tracked change has a `w:date` that is not a date. It is listed without a date. |
| `docx.revision-unresolvable` | dropped | A tracked change could not be accepted or rejected because the markup around it is not what the schema allows (a row marker outside a row, content that cannot be moved without an unbound prefix). The change is left in place; the location is the change element. |
| `docx.revision-id-conflict` | fatal | Raised as `RevisionIdConflictException` (not reported while reading): an id the caller gave to a `RevisionAuthor` is negative or already used by a revision, bookmark, comment range or move range in the document. No location. |
| `pptx.no-main-part` | fatal | No `officeDocument` relationship of the package points at a part that exists. |
| `pptx.wrong-root` | fatal / dropped | The presentation part does not start with `p:presentation` (fatal). A slide, layout, master or notes part that does not start with its own root element reads as empty (dropped); the location is the part. |
| `pptx.part-missing` | fatal | A part the reader must have is not in the package. |
| `pptx.part-unreadable` | dropped | A part could not be parsed (damaged zip entry or markup); it is treated as absent. The location is the part and the offset of the failure. |
| `pptx.slide-part-missing` | dropped | A slide listed in `p:sldIdLst` has no relationship or no part; it is skipped. |
| `pptx.slide-listed-twice` | dropped | The same slide part is listed twice in `p:sldIdLst`; the second entry is skipped. |
| `pptx.slide-id-duplicate` | repaired | Two slides carry the same id in `p:sldIdLst`; both are kept. |
| `pptx.layout-missing` | repaired / dropped | A slide has no layout (repaired: its placeholders inherit nothing), or a layout listed by the master has no part (dropped). |
| `pptx.master-missing` | repaired / dropped | A layout has no master (repaired), or a master listed in `p:sldMasterIdLst` has no part (dropped). |
| `pptx.template-scope-unsupported` | dropped | A `scope=` the presentation binder cannot use (`sheet`, `row`, `column`, `section`), or `table-row`/`table` with tags that are not in a table. The scope the structure implies is used instead. |
| `pptx.template-image-position` | dropped | An `{% image %}` tag that is not the only content of a text shape on a slide (inside other text, in a table cell, in notes, in a picture or group). The tag prints nothing. |
| `pptx.template-image-invalid` | dropped | An `{% image %}` value that is not PNG, JPEG, GIF or BMP data, as bytes or base64 text. The shape is left empty. |
| `pptx.template-orphan-unit` | dropped | Text came out outside the slide, shape or table row it belongs to, because a block's scope does not match where its tags stand (for example `scope=inline` with tags on different slides). The text is left out. |
| `pptx.template-injection-unsupported` | dropped | An `{% html %}` or `{% markdown %}` tag in a presentation; its value is inserted as plain text. |
| `docx.no-main-part` | fatal | No `officeDocument` relationship of the package points at a part that exists. |
| `docx.wrong-root` | fatal | The main document part does not start with `w:document`. |
| `docx.part-missing` | fatal | A part the reader must have is not in the package. |
| `docx.no-body` | repaired | The document has no `w:body`; it reads as an empty body and the tree is left as it was. |
| `docx.part-unreadable` | dropped | An optional part (styles, numbering, theme, …) is not well-formed or is damaged; the document reads as if it were absent. The location holds the part and offset. |
| `docx.style-duplicate-id` | repaired | Two styles have the same `w:styleId`; the first is used. |
| `docx.style-base-missing` | repaired | A style is based on a style that does not exist; its chain ends there. |
| `docx.style-cycle` | repaired | `w:basedOn` links form a cycle; the chain is cut where it closes. |
| `docx.style-unknown` | repaired | A paragraph, run or table names a style that is not defined; the default style of that type applies (none for runs without a default character style). |
| `docx.num-missing` | repaired | A paragraph refers to a `w:numId` that has no `w:num`; it is not numbered. |
| `docx.num-abstract-missing` | repaired | A `w:num` names an abstract definition that does not exist; its paragraphs are not numbered. |
| `docx.num-level-missing` | repaired | The paragraph's level is not defined by the list; the paragraph is not numbered. |
| `docx.num-link-broken` | repaired | A `w:numStyleLink` does not lead to a list definition (missing style, no list, or nested too deeply); the definition's own levels are used. |
| `docx.field-unbalanced` | repaired | A field begin has no end (the field is read to the end of the part and is not complete), or a field separator or end has no open field (it is ignored), or a `w:fldChar` has no valid type. The location is the offset of the element. |
| `docx.field-too-deep` | repaired | Fields nest deeper than `MaxRecursionDepth`; the inner ones are read as plain content. Reported once per scan. |
| `docx.note-missing` | repaired | A footnote or endnote reference names an id the notes part does not have. The reference stays in the list with no note. |
| `docx.note-duplicate-id` | repaired | Two notes of one kind have the same id; references use the first. |
| `docx.comment-duplicate-id` | repaired | Two comments have the same id; the first is used. |
| `docx.comment-missing` | repaired | A comment range marker or reference in the body names a comment the comments part does not have. |
| `docx.comment-range-unmatched` | repaired | A comment has a `w:commentRangeStart` without its `w:commentRangeEnd`, or the reverse. The anchor reports what exists. |
| `docx.table-merge-orphan` | repaired | A `w:vMerge` (or legacy `w:hMerge`) continuation has no restart. A vertical one with a cell above merges into it, as Word does; with nothing to continue (first row) or a horizontal one with nothing to its left it is read as a plain cell. |
| `docx.table-merge-span-mismatch` | repaired | A vertically merged continuation spans a different number of grid columns than the cell above; the region keeps the upper cell's width. |
| `docx.table-grid-mismatch` | repaired | A row covers more grid columns than `w:tblGrid` declares; the logical grid is widened to the widest row. |
| `docx.table-span-invalid` | repaired | A `w:gridSpan` is below 1 or above 16384; it reads as 1. |
| `docx.section-page-size-invalid` | repaired | `w:pgSz` lacks a positive width or height; Letter portrait is used. |
| `docx.section-columns-mismatch` | repaired | `w:cols` names a count that differs from the number of `w:col` entries of an unequal layout; the entries win. |
| `docx.header-missing` | repaired | A header or footer reference names a relationship that does not lead to a readable part; the reference is ignored. |
| `docx.header-reference-duplicate` | repaired | A section has two header (or footer) references of one type; the first is used. |
| `props.part-unreadable` | dropped | A properties part (`docProps/core.xml`, `app.xml` or `custom.xml`) is not well-formed XML; it reads as empty. Writing a property into it raises this code instead of overwriting the part. |
| `xlsb.record-truncated` | dropped | A binary record declares more data than the part has left; the part ends there. Located by part and record offset. |
| `xlsb.record-short` | repaired / dropped | A record is shorter than its fields claim; what was read is used (repaired), or a cell record shorter than a cell header is skipped (dropped). Located by part and record offset. |
| `xlsb.row-invalid` | repaired | A row header holds a row outside the sheet; the row takes the number after the previous row. |
| `xlsb.cell-column-invalid` | dropped | A cell record holds a column outside the sheet; the cell is skipped. |
| `xlsb.formula-unreadable` | dropped | A formula's tokens could not be read (a token that is cut off or unknown); the cell keeps its cached value, or the feature that holds the formula stays as raw records. Located by cell address or record offset. |
| `xlsb.no-main-part` | fatal | No `officeDocument` relationship points at a workbook part that exists. |
| `xlsb.part-unreadable` | fatal | A part holds a record the translators cannot make sense of (a field outside its range); the inner exception has the cause. Located by part. |
| `xlsx.no-main-part` | fatal | No `officeDocument` relationship points at a part that exists. |
| `xlsx.wrong-root` | fatal | The workbook part does not start with a `workbook` element, or a worksheet part with a `worksheet` element. The location holds the part and offset. |
| `xlsx.no-sheets` | repaired | The workbook part has no `sheets` element; the workbook reads as having no sheets. |
| `xlsx.external-link-unreadable` | dropped | An external reference points at a part that is missing, or the part is not well-formed XML. The link has no sheets and no cached values, so references to it read `#REF!`; the other links keep their numbers. |
| `xlsx.sheet-name-duplicate` | repaired | Two sheets have the same name (ignoring case); lookups by name find the first. |
| `xlsx.sheet-part-missing` | dropped | A sheet's relationship does not lead to a part that exists; the sheet reads as empty and is not written. |
| `xlsx.sheetdata-unexpected-element` | dropped | An element other than `row` stands inside `sheetData`; it is dropped. |
| `xlsx.row-index-invalid` | repaired | A `row` has an `r` that is not a row of the sheet (or none that parses); it takes the number after the previous row. |
| `xlsx.row-out-of-order` | repaired | Rows are not in ascending order; a row is merged into the one with the same number, later cells winning. |
| `xlsx.cell-address-invalid` | dropped | A `c` has an `r` outside the sheet or not in A1 form; the cell is dropped. The location is the part and offset. |
| `xlsx.cell-row-mismatch` | repaired | A cell's `r` names another row than its `row` element; the cell stays in the element's row. |
| `xlsx.cell-out-of-order` | repaired | The cells of a row are not in ascending column order; they were sorted. |
| `xlsx.stream-row-order` | repaired | The streaming reader met a row whose number is not above the previous row's. It cannot sort or merge, so it delivers rows as stored. |
| `xlsx.stream-cell-order` | repaired | The streaming reader met a cell whose column is not above the previous cell's in its row (reported once per row). It delivers cells as stored. |
| `xlsx.cell-duplicate` | dropped | Two cells have the same address; the later one is kept. |
| `xlsx.cell-type-unknown` | repaired | A cell's `t` is not one of the cell types; the value reads as a number if it parses, otherwise as text. |
| `xlsx.cell-value-invalid` | repaired | A cell's value does not parse for its type (a number or a shared string index); the cell reads as empty. |
| `xlsx.formula-type-unknown` | repaired | An `f` has an unknown `t`; it reads as an ordinary formula. |
| `formula.unexpected-end` | fatal (parsing) | The text ends where an operand is expected (`1+`, an empty formula). The error carries the offset and length in the formula text; the caller that read the formula from a cell reports its address. |
| `formula.unbalanced-parenthesis` | fatal (parsing) | A `(` is never closed, or a `)` has no opening one. The error carries the offset and length in the formula text; the caller that read the formula from a cell reports its address. |
| `formula.unexpected-character` | fatal (parsing) | A character cannot continue the formula here (`1 2`, a stray `@`). The error carries the offset and length in the formula text; the caller that read the formula from a cell reports its address. |
| `formula.unterminated-string` | fatal (parsing) | A text literal is not closed. The error carries the offset and length in the formula text; the caller that read the formula from a cell reports its address. |
| `formula.bad-error-literal` | fatal (parsing) | A `#` does not start one of the fifteen error values. The error carries the offset and length in the formula text; the caller that read the formula from a cell reports its address. |
| `formula.bad-number` | fatal (parsing) | A number literal is malformed, runs into letters, or is too large for a double. The error carries the offset and length in the formula text; the caller that read the formula from a cell reports its address. |
| `formula.bad-reference` | fatal (parsing) | A sheet prefix is not followed by a reference or name. The error carries the offset and length in the formula text; the caller that read the formula from a cell reports its address. |
| `formula.array-ragged` | fatal (parsing) | The rows of an array constant differ in length. The error carries the offset and length in the formula text; the caller that read the formula from a cell reports its address. |
| `formula.bad-array-element` | fatal (parsing) | An array constant holds something other than a number, text, TRUE, FALSE or an error. The error carries the offset and length in the formula text; the caller that read the formula from a cell reports its address. |
| `formula.nested-array` | fatal (parsing) | An array constant holds another array constant. The error carries the offset and length in the formula text; the caller that read the formula from a cell reports its address. |
| `formula.unbalanced-brace` | fatal (parsing) | An array constant is not closed. The error carries the offset and length in the formula text; the caller that read the formula from a cell reports its address. |
| `formula.unbalanced-bracket` | fatal (parsing) | A table specifier is not closed. The error carries the offset and length in the formula text; the caller that read the formula from a cell reports its address. |
| `formula.too-long` | fatal (parsing) | `TryParse` only: the text is longer than `OfficeLimits.MaxFormulaLength` (`Parse` throws `OfficeLimitException`). The error carries the offset and length in the formula text; the caller that read the formula from a cell reports its address. |
| `formula.too-deep` | fatal (parsing) | `TryParse` only: the formula nests deeper than `OfficeLimits.MaxFormulaDepth` (`Parse` throws `OfficeLimitException`). The error carries the offset and length in the formula text; the caller that read the formula from a cell reports its address. |
| `xlsx.column-range-invalid` | dropped | A `col` has no valid `min`/`max`; it is dropped. |
| `xlsx.merge-range-invalid` | dropped | A `mergeCell` has a `ref` that is not a range of the sheet; it is dropped. |
| `xlsx.shared-string-missing` | repaired | A cell refers to a shared string index the table does not have; its text reads as empty. |
| `csv.ragged-record` | repaired | A delimited-text record has a different number of fields from the first; it is returned as it is. Located by character offset. |
| `csv.stray-quote` | repaired | Text follows the closing quote of a quoted field; it is kept after the quoted text. |
| `csv.quote-unterminated` | repaired | A quoted field is still open at the end of the input; it ends there. |
| `csv.encoding-assumed` | repaired | No byte-order mark and not valid UTF-8; read as Windows-1252. |
| `csv.encoding-invalid` | repaired | A byte sequence is not valid in the encoding in use (UTF-8 past the first block of a forward-only stream, or half a UTF-16 unit at the end); it reads as U+FFFD or is dropped. Once per reader. |
| `csv.grid-exceeded` | dropped | Importing delimited text: more than 1,048,576 records, or 16,384 fields in a record; the rest are dropped. |
| `xlsx.styles-wrong-root` | fatal | The styles part does not start with a `styleSheet` element. Raised when the style sheet is first used, not on open. |
| `xlsx.numfmt-invalid` | dropped | A `numFmt` has no numeric id or no format code; it is ignored. |
| `xlsx.theme-unreadable` | repaired | The theme part is not well-formed XML; theme colours use the default Office scheme. |
| `xlsx.table-part-missing` | dropped | A `tablePart` does not lead to a table part that exists; the table is ignored (the element stays in the sheet). |
| `xlsx.table-unreadable` | dropped | A table part is not well-formed or does not start with `table`; the table is ignored and the part is kept as stored. |
| `xlsx.pivot-table-part-missing` | dropped | A `pivotTable` relationship of a sheet does not lead to a part; the pivot table is ignored. |
| `xlsx.pivot-table-unreadable` | dropped | A pivot table part is not well-formed or does not start with `pivotTableDefinition`; it is ignored and kept as stored. |
| `xlsx.pivot-cache-part-missing` | dropped | A `pivotCache` element of the workbook does not lead to a cache definition part; the cache is ignored. |
| `xlsx.pivot-cache-unreadable` | dropped | A pivot cache definition is not well-formed or does not start with `pivotCacheDefinition`; it is ignored and kept as stored. |

The XML layer repairs nothing: Office refuses an ill-formed part, and so does this layer. The limits it enforces (`MaxXmlDepth`, `MaxXmlAttributeCount`, `MaxXmlNameLength`, and `MaxPartSize` for a part held whole) raise `limit.*` as above.

## Locations

The location says where, in the terms of the layer: a part URI and byte offset (`/word/document.xml@1042`), a stream path and record offset (`/WordDocument@512`), a sheet and cell (`Sheet1!B7`), or a character offset in text (`text@88`). Offsets count bytes of the uncompressed part or stream from zero; -1 means not known.

### Row binding (`binding.`)

Locations are the sheet and cell address (for CSV and TSV, the record number and field position, under the type's sheet name or `Sheet1`). A type marked `Strict` raises the fatal form as an `OfficeFormatException`; otherwise the repair is reported to `OpenContext.Defects`. See `spreadsheets-poco.md`.

| Code | Severity | Meaning |
|---|---|---|
| `binding.column-missing` | repaired (fatal when strict and the member cannot be null) | The header row has no column for a member; the member keeps its default. Located at the first cell of the header row. |
| `binding.header-duplicate` | dropped | Two header cells match the same member; the first is used and the second ignored. |
| `binding.cell-unconvertible` | repaired (fatal when strict) | A cell cannot be read as the member's type (text that is not a number, a number out of range or not whole, an unknown enum name); the member keeps its default. |
| `binding.cell-empty` | fatal (strict types only) | A cell is empty and the member cannot be null. Lenient types leave the default silently. |
| `binding.sheet-not-found` | fatal | The sheet the type names does not exist, or the workbook has no visible worksheet. |
| `binding.value-out-of-range` | fatal | Writing: a date lies outside the range of the workbook's date system (`DateTime.MinValue` alone is written as an empty cell). |

### Workbook calculation (`calc.`)

Locations are the sheet and cell address. These are reported to `OpenContext.Defects` by `Workbook.Recalculate`; see `spreadsheets-calc.md`.

| Code | Severity | Meaning |
|---|---|---|
| `calc.circular-reference` | repaired | A formula is part of a circular reference (or reads itself). Without iterative calculation it reads as 0, and so does every formula on the cycle. |
| `calc.formula-unparsable` | dropped | A formula's text cannot be read; the cell keeps its stored value and is left out of the dependency graph. |
| `calc.limit-exceeded` | repaired | Evaluating a formula passed a limit (steps, allocation); the cell shows `#VALUE!`. |
| `calc.iteration-limit` | repaired | Iterative calculation is on and a circular reference was still changing by more than the maximum change after the maximum number of iterations. The cells keep their last values. Located at the first cell of the cycle. |
| `calc.external-resolve-failed` | repaired | The `IExternalWorkbookResolver` threw an `OfficeException` or `IOException` for a link. The link's cached values are used. Located at the link's part. |

### Pivot refresh (`pivot.`)

Locations are the table's definition part (and offset). Reported to `OpenContext.Defects` by `PivotTable.Refresh` and `Workbook.RefreshPivots`; see `spreadsheets-pivots.md`.

| Code | Severity | Meaning |
|---|---|---|
| `pivot.source-field-missing` | dropped | The source range no longer has a column the cache listed; the field leaves the cache. |
| `pivot.axis-field-dropped` | dropped | A row, column or page field referred to a column that is gone; it leaves the table. |
| `pivot.data-field-dropped` | dropped | A data field referred to a column that is gone; it leaves the table. |
| `pivot.group-unsupported` | dropped | A field is grouped in a way refresh does not reproduce (a `groupBy` it does not know); the group is dropped. |
| `pivot.calculated-field-unparsable` | dropped | A calculated field's formula cannot be read; its cells show `#VALUE!`. |
| `pivot.custom-subtotals-unsupported` | repaired | A field asks for subtotal functions other than automatic; the automatic subtotal is shown. |
| `pivot.show-data-as-unsupported` | repaired | A data field uses a show-values-as form refresh does not compute (percent of parent, percent running total, rank); the normal value is shown. |
| `pivot.filters-not-applied` | repaired | The table has label, value or date filters (`filters`); refresh does not apply them. Hidden items and report filters are applied. |

### Template engine (`template.`)

Located by the binder's unit location plus the offset in the unit's text, or by a character offset in the whole text for plain-text templates. Errors make `ParsedTemplate.IsValid` false and `Render` throws `TemplateException` with the first one; every error found is listed. Warnings are in `TemplateResult.Defects`.

| Code | Severity | Meaning |
|---|---|---|
| `template.unclosed-tag` | fatal | A tag opener has no closer in its unit (a tag never spans two paragraphs, lines or shapes). The rest of the unit is kept as text. |
| `template.unclosed-block` | fatal | A `for`, `if` or `raw` block is never closed. Located at its opening tag. |
| `template.unexpected-tag` | fatal | `endfor`, `endif`, `elif` or `else` with no matching open block, `else` twice, or `elif` after `else`. |
| `template.unknown-tag` | fatal | A block tag whose name is not one of the known tags. |
| `template.empty-expression` | fatal | `{{ }}`, or a block tag that needs an expression and has none. |
| `template.expression-syntax` | fatal | An expression is not a formula. Located inside the expression; the message carries the `formula.` code. |
| `template.bad-for` | fatal | A loop is not written `for item in expression`. |
| `template.bad-option` | fatal | An option after an expression is not `name=value`, a value is not closed, or the tag takes no such option. |
| `template.unknown-scope` | fatal | `scope=` names a scope the engine does not know. |
| `template.scope-invalid` | fatal | The binder's unit range for a block does not contain both of the block's tags. |
| `template.unknown-function` | dropped | An expression calls a function that is not in the registry; it will evaluate to `#NAME?`. A warning, not an error. |
| `template.missing-data` | repaired (strict: fatal) | The template names data the data does not have, and renders empty. Handled missing data (`IFERROR(x, ...)`) is not reported. |
| `template.expression-error` | repaired (strict: fatal) | A formula evaluated to an error value, which is printed as Excel shows it. |
| `template.not-a-collection` | repaired (strict: fatal) | A loop's source is a single value; the loop runs once for it. |
| `template.include-denied` | repaired (strict: fatal) | `include` was used but no `ITemplateIncludes` was supplied; nothing is read. |
| `template.include-not-found` | repaired (strict: fatal) | The supplied `ITemplateIncludes` has no template of that name. |
| `template.sheet-overflow` | fatal | A rendered workbook sheet would have more rows (or, for a column block, columns) than a sheet can hold. Located at the sheet's part. |
| `template.sheet-name-repaired` | repaired | A rendered sheet name is empty, too long, uses a character sheet names cannot, or is taken; the sheet gets a cleaned or numbered name. |
| `template.tag-not-bound` | dropped | An `html` or `markdown` tag sits in a workbook cell, where it cannot be bound; it printed nothing. Located at the cell. |
| `template.image-invalid` | dropped | The value of an `image` tag in a workbook cell is not PNG, JPEG, GIF or BMP data (bytes, or base64 text, optionally a `data:` URI); no picture is added. Located at the cell the tag is in (after rendering). |
| `template.scope-unusable` | repaired | A document block asks for a scope the structure cannot give (`scope=table-row` or `table` outside a table). Located at the part and the paragraph of the opening tag; the block repeats the paragraphs between its tags. |
| `template.image-unsupported` | dropped | An `{% image %}` value that is not PNG, JPEG, GIF or BMP data (bytes, a stream, base64 or a `data:` URI). Nothing is inserted. |
| `template.markup-dropped` | dropped | An `{% html %}` or `{% markdown %}` value held markup the converter does not carry over (scripts, event handlers, unknown elements, disallowed link targets, images, tables, raw HTML, content over the size or nesting limit). The rest is inserted. |

Limits surface as `OfficeLimitException`: `limit.max-iterations`, `limit.max-output-characters`, `limit.max-block-depth` (block nesting and include depth), `limit.max-recursion-depth` and the formula limits.

### Mail merge (`mailmerge.`)

Reported in `MailMergeResult.Warnings`, with the part and offset of the field in the template. See `mail-merge.md`.

| Code | Action | Meaning |
|---|---|---|
| `mailmerge.field-missing` | repaired | A `MERGEFIELD` names a field the record does not have; it merged as empty. |
| `mailmerge.field-malformed` | repaired | A field instruction cannot be read (no name, bad switch argument); the field merged as empty or stays. |
| `mailmerge.field-unsupported` | dropped | `ADDRESSBLOCK`, `GREETINGLINE`, `DATABASE`, `MERGEBARCODE` or `COMPARE`: the field is left as written. |
| `mailmerge.field-unbalanced` | dropped | A field with no end marker, a stray end marker, or a field spanning paragraphs; it is left as written. |

### Content-control binding (`binding.`, continued)

Reported in `ContentBindingReport.Warnings`. See `docx-content-controls.md`.

| Code | Action | Meaning |
|---|---|---|
| `binding.store-not-found` | dropped | A control's `w:storeItemID` matches no custom XML part; the control is left. |
| `binding.unsupported-path` | dropped | The binding path is outside the XPath subset or cannot be written to (for example a predicate-only path with nothing to create). |
| `binding.no-data` | dropped | The data has no value for the control; its content is kept. |
| `binding.unsupported-control` | dropped | The control type cannot show a bound value (group, picture, building block …). |
| `binding.value-not-in-list` | repaired | A drop-down value is not one of its items; the control keeps its selection. |
| `binding.no-template-item` | dropped | A repeating section has no item to copy as its pattern. |
| `html.damaged` | fatal | The HTML could not be imported (an unexpected failure, wrapped in a typed exception; the inner exception has the cause). Any text parses as HTML, so this means a bug worth reporting with the input. |
| `html.export-failed` | fatal | The document could not be written as HTML (wrapped unexpected failure). |
| `html.dropped` | dropped | An element, attribute or value was left out of the import (a script, an event handler, an unsafe link, a form, a frame, an external image, hidden content, and so on); the location is the text position of the element in the decoded HTML. The loss report of the import counts the same things by kind (`loss.html-*`, see `html.md`). |
| `rtf.not-rtf` | fatal | The content does not start with `{\rtf`. |
| `rtf.damaged` | fatal | The RTF could not be converted (an unexpected failure while reading, wrapped in a typed exception; the inner exception has the cause). |
| `rtf.export-failed` | fatal | The document could not be written as RTF (wrapped unexpected failure). |
| `markdown.too-deep` | repaired | Block containers (quotes, list items) nested deeper than `OfficeLimits.MaxRecursionDepth`; the markers beyond the limit are read as text. Located by character offset of the first line over the limit. |
| `markdown.encoding-replaced` | repaired | The Markdown is not valid UTF-8 and has no UTF-16 byte-order mark; invalid bytes were replaced by U+FFFD. Located by byte offset of the first. |
| `markdown.damaged` | fatal | The Markdown could not be converted (an unexpected failure while importing, wrapped in a typed exception). |
| `markdown.export-failed` | fatal | The document could not be written as Markdown (wrapped unexpected failure). |
| `rtf.parameter-overflow` | repaired | A control word parameter does not fit in 32 bits; it was clamped. |
| `rtf.invalid-hex-escape` | repaired | `\'` was not followed by two hexadecimal digits; it was dropped. |
| `rtf.binary-data-truncated` | repaired | A `\bin` length is negative or runs past the end of the input; it was clamped. |
| `rtf.truncated-token` | repaired | The input ended inside a control word or escape. |
| `rtf.unbalanced-group-end` | repaired | A `}` without a `{`; ignored. |
| `rtf.unclosed-groups` | repaired | The input ended with groups still open; they are closed at the end. |
| `rtf.nesting-limit-exceeded` | dropped | Group nesting reached the limit (`MaxRecursionDepth`, at most 1000); reading stopped there. |
| `rtf.content-limit-exceeded` | dropped | The file holds more nodes than `MaxRecordCount` allows; the rest is ignored. |
| `rtf.invalid-picture-data` | dropped | Picture data is empty, not valid for its declared format or not a format that can be carried; the picture is left out. |
| `rtf.picture-limit-exceeded` | dropped | A picture exceeds `MaxPartSize`, or the pictures together exceed `MaxAllocationBytes`; it is left out. |
| `rtf.undefined-reference` | dropped | A paragraph uses a list override the list override table does not define. |
| `rtf.unclosed-row` | repaired | A table row has cells but no `\row`; it is closed at the end of the table. |
| `rtf.too-many-defects` | repaired | The reader's defect list reached its cap of 256; later ones are not recorded. |
| `rtf.header-truncated`, `rtf.unknown-compression-type`, `rtf.compressed-size-mismatch`, `rtf.missing-end-marker`, `rtf.crc-mismatch`, `rtf.raw-size-mismatch`, `rtf.reference-beyond-dictionary`, `rtf.output-limit-exceeded`, `rtf.expansion-ratio-exceeded` | repaired or dropped | Compressed RTF (the mail body form): the header, size, checksum or dictionary reference is damaged, or a limit was hit; see `RtfDefectKind`. |
| `rtf.not-encapsulated`, `rtf.ambiguous-unknown-code-page`, `rtf.lone-surrogate`, `rtf.ambiguous-html-rtf-in-plain-text` | repaired | The mail layer's de-encapsulation resolved a known ambiguity in one documented way (see `RtfDefectKind`). |
| `rtf.nested-table-flattened`, `rtf.ambiguous-picture-format`, `rtf.ambiguous-list-level`, `rtf.ambiguous-symbol-font`, `rtf.ambiguous-bare-cell` | repaired or dropped | The mail layer's HTML conversion (`RtfHtmlConverter`) repaired or flattened something; the hub conversion reads these constructs fully. |

### Legacy workbooks (`biff.`)

Located by stream path and record offset (`Record` locations) unless the kind says otherwise. See `xls.md`.

| Code | Severity | Meaning |
|---|---|---|
| `biff.truncated-record` | dropped | A record claims more data than the stream holds; it and everything after it are ignored. |
| `biff.trailing-bytes` | dropped | Fewer than a record header of bytes follow the last record. |
| `biff.no-bof` | fatal | The stream does not begin with a BOF record. |
| `biff.no-workbook-stream` | fatal | A compound file has neither a `Workbook` nor a `Book` stream. |
| `biff.no-sheet` | fatal | A BIFF2 to BIFF4 stream holds no worksheet. |
| `biff.missing-eof` | repaired | A substream has no EOF record; it is read to the end of the stream. |
| `biff.malformed` | fatal | An unexpected failure while reading damaged records (the inner exception names it). |
| `biff.codepage-unavailable` | repaired | The workbook's code page is not available in the runtime; 8-bit text is read as Windows-1252. Kind `Encoding`. |
| `biff.sheet-missing` | dropped | A sheet in the sheet list has no substream; it is left empty. |
| `biff.sheet-name` | repaired | A sheet name is invalid or repeated; the sheet is named `Sheet<n>`. |
| `biff.no-visible-sheet` | repaired | Every sheet is hidden; the first is shown. |
| `biff.cell-out-of-range` | dropped | A cell lies outside the sheet. Kind `Reference`. |
| `biff.sst-index` | dropped | A text cell refers to a shared string the table does not hold. Kind `Reference`. |
| `biff.sst-truncated` | repaired | The shared string table ends inside a string. |
| `biff.sst-short` | repaired | The table holds fewer strings than it declares. |
| `biff.string-orphan` | dropped | A STRING record does not follow a formula that needs one. |
| `biff.colinfo-range` | dropped | A column range is inverted or beyond column 255. |
| `biff.merge-range` | dropped | A merged range is inverted or beyond column 255. |
| `biff.formula-length` | dropped | A formula claims more token bytes than its record holds. |
| `biff.formula-token` | dropped | A formula holds a token or function the converter does not know; the cell keeps its cached value. Kind `Formula`. |
| `biff.formula-stack` | dropped | A formula's tokens do not reduce to one expression. Kind `Formula`. |
| `biff.formula-exp`, `biff.formula-table` | dropped | A shared-formula or data-table token reached a place it cannot be resolved. Kind `Formula`. |
| `biff.formula-exp-missing` | dropped | A formula refers to a shared or array formula the sheet does not define. Kind `Formula`. |
| `biff.formula-name` | dropped | A formula uses a name or external name the file does not define (replaced by `#NAME?`). Kind `Formula`. |
| `biff.formula-pivot-name`, `biff.formula-macro-function` | dropped | A pivot-table name or macro-command function is replaced by an error value. Kind `Formula`. |
| `biff.formula-too-long` | dropped | A formula is longer than `OfficeLimits.MaxFormulaLength`. Kind `Formula`. |
| `biff.formula-text-invalid` | dropped | A formula holds a character XML cannot carry (a control character in a text constant); the cell keeps its cached value. Kind `Formula`. |
| `biff.cf-length` | dropped | A conditional-format rule is shorter than its parts. |
| `biff.name-rejected` | dropped | A defined name is invalid in the hub and is dropped. Kind `Reference`. |
| `biff.object-count-mismatch` | repaired | The shapes with object records in a sheet's drawing and the sheet's `Obj` records differ in number, so the embedded objects cannot be matched to their shapes; they stay pictures and are counted in `loss.ole-objects`. |
| `biff.embedding-storage-missing` | dropped | An `Obj` record names an embedding storage (`MBD` and eight hex digits) the file does not have; the object keeps its picture. Kind `Reference`. |
| `biff.name-repeated` | dropped | A defined name repeats one of the same scope (LibreOffice writes some twice); the first is kept. Kind `Reference`. |
### OLE property sets and objects (`ole.`)

Reported by `RecursiveAI.OneDocument.Ole` (see `ole.md`). Property-set defects have a `Record` location: the stream name (`/\u0005SummaryInformation`) and the byte offset in the stream. A defect in one property drops that property only.

| Code | Action | Meaning |
|---|---|---|
| `ole.propset-header` | fatal, or repaired for the version | The stream is shorter than the 28-byte header or its byte order mark is not 0xFFFE (fatal; `TryRead` reports it and returns false), or the version is above 1 (repaired: read as 1). |
| `ole.propset-count` | repaired | The header lists more property sets than fit, or more than 64; or a set lists more properties than its bytes can hold. The count is clamped. |
| `ole.propset-bad-offset` | dropped | A property set starts inside the header or outside the stream; the set is dropped. |
| `ole.propset-size` | repaired | A property set's declared size is under 8 or past the stream; the rest of the stream is read as the set. |
| `ole.propset-truncated` | dropped (repaired for an odd UTF-16 byte count) | A property's offset is outside its set, or its value, a string, a vector or an array runs past the bytes it may use; only that property is dropped. |
| `ole.propset-duplicate-id` | dropped | A property identifier appears twice; the first is kept. Kind `Structure`. |
| `ole.propset-type-unsupported` | dropped | A property has a type number that is not a property type (or a code page property that is not a 16-bit integer). Kind `Unsupported` or `Value`. |
| `ole.propset-dictionary` | dropped | The dictionary is cut short, lists more entries than fit, or names a special or repeated identifier. |
| `ole.propset-codepage-unsupported` | repaired | The set's code page is not available in the runtime, or the set has strings and no code page property; its strings are read as Windows-1252 and the set's `CodePage` says so. Kind `Encoding`. |
| `ole.storage-too-large` | dropped | An object storage holds more than `MaxPartSize` in one stream or more than `MaxAllocationBytes` in all; it is not copied. Kind `Limit`. |
| `ole.storage-name-invalid` | fatal | Writing a storage as a compound file met a name a compound file cannot hold (a damaged source). Raised as `OfficeFormatException` by `OleStorageImage.ToCompoundFile`; the converters report it as a dropped object. Kind `Packaging`. |

### OfficeArt (`art.`)

Reported by the shared OfficeArt reader (`RecursiveAI.OneDocument.OfficeArt`) with a `Record` location: the stream name the caller gave and the offset of the record. See `officeart.md`.

| Code | Action | Meaning |
|---|---|---|
| `art.record-truncated` | repaired | Fewer than eight bytes remain where a record header should be; they are ignored. |
| `art.record-overrun` | repaired | A record claims more bytes than remain in its parent or the data; it is clamped. |
| `art.property-overrun` | repaired | A property table claims more entries, or a complex property more data, than the record holds; the excess is dropped. |
| `art.bse-truncated` | repaired | A BLIP store entry is shorter than its fixed part; the slot is empty. |
| `art.blip-index` | repaired | A picture refers to a BLIP index outside the store; the picture is dropped. |
| `art.blip-missing` | repaired | A BLIP is not embedded and its offset is outside the delay (picture) stream. |
| `art.blip-invalid` | dropped | A BLIP record is damaged, of an unknown type or compressed in a way the format does not define. |

### PowerPoint binary (`ppt.`)

Reported by the `.ppt` reader (`RecursiveAI.OneDocument.Ppt.Binary`) and the conversion to PresentationML, with a `Record` location in the `PowerPoint Document`
stream (the `Current User` stream for `ppt.current-user-missing`). The conversion codes are also the items of `Presentation.ConversionLoss`. See `ppt.md`.

| Code | Action | Meaning |
|---|---|---|
| `ppt.no-document-stream` | fatal | The compound file has no `PowerPoint Document` stream (used directly; the sniffer calls such a file unknown). |
| `ppt.current-user-missing` | repaired | The `Current User` stream is missing or damaged; the newest edit is found by scanning. |
| `ppt.edit-chain-broken` | repaired | A user edit offset does not lead to a `UserEditAtom`; older edits are ignored or the chain is recovered by scanning. |
| `ppt.edit-chain-loop` | repaired | The user edit chain does not move backwards; the walk stops. |
| `ppt.no-user-edit` | fatal | The stream holds no readable user edit, so the persist directory cannot be built. |
| `ppt.persist-directory-missing` | dropped | A user edit points at a persist directory that is not there; the objects it defines are ignored. |
| `ppt.persist-directory-truncated` | repaired | A directory entry lists more offsets than the record holds. |
| `ppt.persist-object-range` | dropped | An encrypted persist object lies outside the stream and is not decrypted. |
| `ppt.persist-object-missing` | dropped | A persist identifier is not in the directory, or its offset is outside the stream. |
| `ppt.document-container-recovered` | repaired | The document container was found by scanning, not through the directory. |
| `ppt.no-document-container` | fatal | Neither the directory nor a scan leads to a document container. |
| `ppt.document-atom-missing` | repaired | No `DocumentAtom`; the default slide size is used. |
| `ppt.master-missing` | dropped | A master list entry (or the file) has no usable master; a plain master is written. |
| `ppt.ole-storage-unreadable` | dropped | An `ExOleObjStg` record is not an object storage record, does not inflate, inflates to another size than it says, or its bytes are not a compound file; the object keeps its preview picture. |
| `ppt.ole-storage-too-large` | dropped | An object storage declares a decompressed size past the limits; it is not read. Kind `Limit`. |
| `ppt.slide-missing` | dropped | A slide list entry does not lead to a slide container; the slide is dropped. |
| `ppt.notes-missing` | dropped | A notes list entry does not lead to a notes container. |
| `ppt.text-style-truncated` | repaired | A paragraph, character, ruler or master style structure is cut short by the end of its record. |
| `ppt.text-special-info-unread` | repaired | Smart tags in a text special info record are not read; later language runs are ignored. |
| `ppt.outline-reference` | repaired | A shape refers to slide-list text that does not exist. |
| `ppt.malformed` | fatal | A defensive wrapper: an unexpected exception while reading is reported as a format error (a bug if it is ever seen). |
| `ppt.shape-approximated`, `ppt.shape-adjust-dropped`, `ppt.fill-approximated`, `ppt.path-escape`, `ppt.geometry-guides`, `ppt.wordart-flattened`, `ppt.picture-pict`, `ppt.picture-missing`, `ppt.table-as-group`, `ppt.table-borders-approximated`, `ppt.transition-dropped`, `ppt.animations-dropped`, `ppt.ole-object-flattened`, `ppt.vba-not-carried`, `ppt.document-properties-not-carried`, `ppt.header-footer-field`, `ppt.placeholder-unknown` | dropped | Conversion losses; meanings in `ppt.md`. |

Encrypted files are decrypted or raise `encryption.password-required`; PowerPoint 4.0/95 files raise `unsupported.feature`.

### VBA projects (`vba.`)

Reading is in `docs/vba.md`. Locations are `<container>!<stream path>@<offset>`: the container is the project part (`/word/vbaProject.bin`), the storage (`/Macros`, `/_VBA_PROJECT_CUR`) or `PowerPoint Document@<offset>`; the offset is into the decompressed `dir` stream or the module stream. Every code is exercised by a crafted input in `VbaDefectTests`.

| Code | Severity | Meaning |
|---|---|---|
| `vba.project-stream-missing` | repaired | The `PROJECT` stream is missing; module kinds come from the module type records and protection is reported as none. |
| `vba.protection-unreadable` | repaired | `CMG`, `DPB` or `GC` is present but cannot be undone; the project is reported as locked. |
| `vba.dir-missing` | dropped | The `VBA` storage has no `dir` stream, so no module can be listed. |
| `vba.dir-damaged` | repaired | The `dir` stream's compressed container is damaged; the records decoded before the damage are used. |
| `vba.dir-record` | repaired | A `dir` record is cut short or has a size field the specification does not allow; reading stops there and keeps what it has. |
| `vba.dir-record-unknown` | repaired | A `dir` record has an identifier the specification does not define; it is skipped by its size. |
| `vba.codepage-unsupported` | repaired | The project code page is not available; Latin-1 decodes byte strings and source. |
| `vba.module-list-short` | repaired | The module count announces more modules than the `dir` stream holds. |
| `vba.module-stream-missing` | dropped | A module is listed but its stream is not in the `VBA` storage; the module has no source. Kind `Reference`. |
| `vba.module-offset-invalid` | dropped | A module's source offset is past the end of its stream; the module has no source. Kind `Reference`. |
| `vba.module-source-damaged` | repaired | A module's compressed source is damaged; the text decoded before the damage is kept. |
| `vba.version-stream-missing` | repaired | The `_VBA_PROJECT` stream is missing; the p-code version is unknown. |
| `vba.version-stream-short` | repaired | The `_VBA_PROJECT` stream is shorter than its seven fixed bytes. |
| `vba.project-not-compound` | dropped | The project part is not a compound file; the project has no modules. Kind `Packaging`. |
| `vba.workbook-stream-missing` | fatal | An Excel 97-2003 file has no `Workbook` or `Book` stream, so a removal cannot correct its record positions. |

Removal reports (`VbaRemovalReport.NotDone`) carry the same style of code for what a removal left or could not do; they are notes, not defects:

| Code | Meaning |
|---|---|
| `vba.no-macro-free-type` | The format (add-in, binary workbook) has no macro-free counterpart; the file keeps its format without a project. |
| `vba.project-unreadable` | The project could not be read before it was removed (a limit, for example), so its modules are not counted. It is removed all the same. |
| `vba.output-not-encrypted` | The input was encrypted; the output is written without encryption. |
| `vba.doc-customizations-kept` | A Word file's toolbar, menu and key customizations, and a signature kept in its user variables, are not interpreted and stay. |
| `vba.ppt-signature-kept` | A PowerPoint file's signature kept in the property sets is not looked for and stays. |

### Office 2003 XML (`wml2003.`, `sml2003.`)

Reported by the readers of Word 2003 XML and Excel 2003 XML while they convert (see `xml-2003.md`). Locations are the part `/` (the file itself) with a byte offset, or a cell address for cell problems.

| Code | Severity | Meaning |
|---|---|---|
| `wml2003.not-wordml` | fatal | The root is not a `w:wordDocument` in the 2003 namespace. |
| `wml2003.missing-picture` | dropped | A picture's `v:imagedata` names data (`wordml://...`) that the file does not hold. |
| `wml2003.bad-base64` | dropped | The `w:binData` of a picture is not valid base64. |
| `wml2003.number-format` | repaired | A list level's `w:nfc` code has no known name; decimal was used. |
| `wml2003.table-grid` | repaired | A table has no `w:tblGrid`; one was made from its widest row. |
| `wml2003.style-id` | repaired | A style has no `w:styleId`; one was made from its name. |
| `wml2003.bad-value` | repaired or dropped | A fractional number where a whole one is needed (rounded), an empty attribute (left out), or a bad date or count in the document properties (left out). |
| `wml2003.duplicate-property` | repaired | One property list names a property twice; the last was kept. |
| `sml2003.not-spreadsheet` | fatal | The root is not a `Workbook` in the SpreadsheetML 2003 namespace. |
| `sml2003.no-sheets` | repaired | The workbook has no worksheet; an empty one was added. |
| `sml2003.sheet-name` | repaired | A sheet name is empty, too long, has a forbidden character or repeats another (ignoring case); it was changed. |
| `sml2003.duplicate-style` | repaired | A style ID is defined twice; the first definition is used. |
| `sml2003.bad-style-ref` | repaired | A cell, row or style names a style that does not exist, or styles inherit from each other in a loop; the default style was used. |
| `sml2003.bad-index` | repaired or dropped | A row, column or cell index goes backwards (the item follows the previous one) or lies beyond the last row or column (dropped). |
| `sml2003.bad-number` | repaired | A `Number` cell does not hold a number; its text was kept as a string. |
| `sml2003.bad-date` | repaired | A `DateTime` cell does not hold an ISO 8601 date or is outside the date range; its text was kept as a string. |
| `sml2003.bad-formula` | dropped | A cell's `ss:Formula` is not an R1C1 formula the parser reads (files written by other programs may hold OpenFormula text); the cached value stays as a constant. |
| `sml2003.bad-value` | dropped | A bad date in the document properties. |

### OpenDocument (`packaging.odf-*`, `odf.`)

The package layer (`OdfPackage`, see `odf.md`) reports under `packaging.`; the text conversion reports under `odf.`. Package locations are the entry name (`mimetype`, `META-INF/manifest.xml`, `content.xml`) with a byte offset where one is known; conversion locations are `content.xml` (or the flat file) with a byte offset.

| Code | Severity | Meaning |
|---|---|---|
| `packaging.not-odf` | fatal | A flat XML file whose root is not `office:document`, or a zip with no usable `mimetype` and no `content.xml`. |
| `packaging.odf-no-mimetype` | repaired | The package has no `mimetype` entry; the type is taken from the manifest or the body. A flat file without `office:mimetype` is repaired the same way. |
| `packaging.odf-mimetype-not-first` | repaired | `mimetype` is not the first entry; a saved package writes it first. |
| `packaging.odf-mimetype-compressed` | repaired | `mimetype` is deflated; a saved package stores it. |
| `packaging.odf-no-manifest` | repaired | There is no `META-INF/manifest.xml`; saving writes one. |
| `packaging.odf-manifest-unreadable` | repaired | The manifest is not well-formed or its root is not `manifest:manifest`; it reads as empty. |
| `packaging.odf-manifest-entry` | dropped | A `manifest:file-entry` has no `manifest:full-path`. |
| `packaging.odf-manifest-missing-entry` | repaired | The manifest lists a file the package does not hold. |
| `packaging.odf-wrong-root` | fatal | A part does not start with its root element (`office:document-content` for `content.xml`, and so on). |
| `packaging.invalid-part-name`, `packaging.duplicate-part-name`, `packaging.zip-damaged` | as for OPC | Entry names that are empty, absolute or contain `..`; an entry stored twice (the first is used); a damaged zip directory that was recovered. |
| `odf.no-content` | fatal | The package has no `content.xml`. |
| `odf.not-text` | fatal | The document is not a text document (its body holds a spreadsheet or presentation). |
| `odf.damaged` | fatal | A defensive wrapper: an unexpected exception while converting is reported as a format error (a bug if it is ever seen). |
| `odf.export-failed` | fatal | The same, while writing OpenDocument. |
| `odf.style-cycle` | repaired | A style's parent chain loops; the loop was cut. |
| `odf.bad-date` | dropped | A date in `meta.xml` is not a date. |
| `odf.unknown-change` | repaired | A change mark refers to a changed region the file does not hold. |
| `odf.missing-picture` | dropped | A picture names a package file that is not there. |
| `odf.bad-base64` | dropped | The inline data (`office:binary-data`) of a picture is not valid base64. |
| `odf.repeat-capped` | dropped | A table repeats rows or cells by counts that would exceed the reader's budget (200,000 cells per table, 10,000 repeats per element); the rest were dropped. |
