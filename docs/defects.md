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
| `text.` | text extraction | `Structure`, `Reference`, `Markup`, `Packaging` |
| `docx.` | the word-processing hub | `Structure`, `Reference`, `Value`, `Packaging` |

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
| `text.no-main-part` | fatal | The package names no main document part, or the part it names is not in the package. |
| `text.part-missing` | dropped | A relationship of the main part (a header, footer, notes, comments, sheet or slide) names a part the package does not have; its text is left out. |
| `text.part-unreadable` | dropped | A part other than the main one is damaged (malformed XML or a damaged archive entry); the text before the damage was kept and the rest of that part is left out. The location is the part and offset. |
| `text.shared-string-out-of-range` | dropped | A worksheet cell names a shared string the table does not have; the cell is left out. The location is the part and the cell's offset. |
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

The XML layer repairs nothing: Office refuses an ill-formed part, and so does this layer. The limits it enforces (`MaxXmlDepth`, `MaxXmlAttributeCount`, `MaxXmlNameLength`, and `MaxPartSize` for a part held whole) raise `limit.*` as above.

## Locations

The location says where, in the terms of the layer: a part URI and byte offset (`/word/document.xml@1042`), a stream path and record offset (`/WordDocument@512`), a sheet and cell (`Sheet1!B7`), or a character offset in text (`text@88`). Offsets count bytes of the uncompressed part or stream from zero; -1 means not known.
