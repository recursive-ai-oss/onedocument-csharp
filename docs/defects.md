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
| `packaging.invalid-part-name` | dropped | A zip item is not a valid part name (a backslash, a `..` segment, an empty segment). It is kept as stored, not exposed. |
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

## Locations

The location says where, in the terms of the layer: a part URI and byte offset (`/word/document.xml@1042`), a stream path and record offset (`/WordDocument@512`), a sheet and cell (`Sheet1!B7`), or a character offset in text (`text@88`). Offsets count bytes of the uncompressed part or stream from zero; -1 means not known.
