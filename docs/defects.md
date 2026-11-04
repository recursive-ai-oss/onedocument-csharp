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
| `packaging.damaged` | fatal | The container is damaged (zip or compound-file structure); the location holds the offset when known. |
| `packaging.refused` | fatal | The container reader refused the input for safety (unsafe path, overlapping entries). |

## Locations

The location says where, in the terms of the layer: a part URI and byte offset (`/word/document.xml@1042`), a stream path and record offset (`/WordDocument@512`), a sheet and cell (`Sheet1!B7`), or a character offset in text (`text@88`). Offsets count bytes of the uncompressed part or stream from zero; -1 means not known.
