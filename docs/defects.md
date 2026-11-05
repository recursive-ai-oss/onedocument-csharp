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
| `sheet.` | the workbook model | `Reference`, `Value` |
| `formula.` | formula text and tokens | `Formula` |
| `numfmt.` | number-format codes | `Value` |
| `text.` | text extraction | `Structure`, `Reference`, `Markup`, `Packaging` |
| `docx.` | the word-processing hub | `Structure`, `Reference`, `Value`, `Packaging` |
| `xlsx.` | the SpreadsheetML workbook hub and cell store | `Structure`, `Reference`, `Value`, `Formula`, `Packaging` |

## Codes in use

| Code | Severity | Meaning |
|---|---|---|
| `limit.<property>` | fatal | The named `OfficeLimits` property (in kebab case, for example `limit.max-xml-depth`) was exceeded. `OfficeLimitException.LimitName` has the property name. |
| `encryption.password-required` | fatal | The input is encrypted and no password was given. |
| `encryption.password-incorrect` | fatal | The password does not open the input. |
| `unsupported.feature` | fatal | The input uses something this library does not handle; `OfficeUnsupportedException.Feature` names it. |
| `unsupported.format-not-available` | fatal | No reader or writer is registered for the format (its package is not referenced or does not implement it). |
| `format.unrecognised` | fatal | The content is not a format OneOffice knows. |
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
| `xlsx.no-main-part` | fatal | No `officeDocument` relationship points at a part that exists. |
| `xlsx.wrong-root` | fatal | The workbook part does not start with a `workbook` element, or a worksheet part with a `worksheet` element. The location holds the part and offset. |
| `xlsx.no-sheets` | repaired | The workbook part has no `sheets` element; the workbook reads as having no sheets. |
| `xlsx.sheet-name-duplicate` | repaired | Two sheets have the same name (ignoring case); lookups by name find the first. |
| `xlsx.sheet-part-missing` | dropped | A sheet's relationship does not lead to a part that exists; the sheet reads as empty and is not written. |
| `xlsx.sheetdata-unexpected-element` | dropped | An element other than `row` stands inside `sheetData`; it is dropped. |
| `xlsx.row-index-invalid` | repaired | A `row` has an `r` that is not a row of the sheet (or none that parses); it takes the number after the previous row. |
| `xlsx.row-out-of-order` | repaired | Rows are not in ascending order; a row is merged into the one with the same number, later cells winning. |
| `xlsx.cell-address-invalid` | dropped | A `c` has an `r` outside the sheet or not in A1 form; the cell is dropped. The location is the part and offset. |
| `xlsx.cell-row-mismatch` | repaired | A cell's `r` names another row than its `row` element; the cell stays in the element's row. |
| `xlsx.cell-out-of-order` | repaired | The cells of a row are not in ascending column order; they were sorted. |
| `xlsx.cell-duplicate` | dropped | Two cells have the same address; the later one is kept. |
| `xlsx.cell-type-unknown` | repaired | A cell's `t` is not one of the cell types; the value reads as a number if it parses, otherwise as text. |
| `xlsx.cell-value-invalid` | repaired | A cell's value does not parse for its type (a number or a shared string index); the cell reads as empty. |
| `xlsx.formula-type-unknown` | repaired | An `f` has an unknown `t`; it reads as an ordinary formula. |
| `xlsx.column-range-invalid` | dropped | A `col` has no valid `min`/`max`; it is dropped. |
| `xlsx.merge-range-invalid` | dropped | A `mergeCell` has a `ref` that is not a range of the sheet; it is dropped. |
| `xlsx.shared-string-missing` | repaired | A cell refers to a shared string index the table does not have; its text reads as empty. |

The XML layer repairs nothing: Office refuses an ill-formed part, and so does this layer. The limits it enforces (`MaxXmlDepth`, `MaxXmlAttributeCount`, `MaxXmlNameLength`, and `MaxPartSize` for a part held whole) raise `limit.*` as above.

## Locations

The location says where, in the terms of the layer: a part URI and byte offset (`/word/document.xml@1042`), a stream path and record offset (`/WordDocument@512`), a sheet and cell (`Sheet1!B7`), or a character offset in text (`text@88`). Offsets count bytes of the uncompressed part or stream from zero; -1 means not known.
