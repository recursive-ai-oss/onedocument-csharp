# Pinned schemas

The typed-view generator reads these files and nothing else. They are copies of published standards documents, never edited. `tools/schemas/pin-schemas.py <downloads> schemas` rebuilds the directory from the downloads below and refuses any file whose SHA-256 differs. Licence notices are in `THIRD-PARTY-NOTICES.md`.

| Directory | Source download | SHA-256 |
|---|---|---|
| `ecma376/transitional/` | ECMA-376 5th ed. Part 4 (Dec 2016), `OfficeOpenXML-XMLSchema-Transitional.zip` inside `ECMA-376-4_5th_edition_december_2016.zip` | `bd25da1109f73762356596918bf5ff8b74a1331642dba5f1c1d1dfc6bed34ecd` |
| `ecma376/strict/` | ECMA-376 5th ed. Part 1 (Dec 2016), `OfficeOpenXML-XMLSchema-Strict.zip` inside `ECMA-376-1_5th_edition_december_2016.zip` | `9d0bcad9cf06054785b03762fcfadbf6bab7e54a5f9d69434e34b7fd464d4129` |
| `ecma376/opc/` | ECMA-376 5th ed. Part 2 (Dec 2021), `OpenPackagingConventions-XMLSchema.zip` inside `ECMA-376-2_5th_edition_december_2021.zip` | `1d489dc491168ea1f9e9a59063acc8dd5f02b4ad1d21aa7ec19ba9a58d020c70` |
| `ms/MS-DOCX/` | [MS-DOCX] 23.0, 2026-08-18, Appendix A | `ed5d74bb33232d083742949d8072766e99f02fc3fbc6b7dc8b73264965dcce3a` |
| `ms/MS-ODRAWXML/` | [MS-ODRAWXML] 2026-02-17, Appendix A | `20de73988d209fd18f1776c2960103179c9c93b883c1d9a44ab3ce83b8b60f5b` |
| `ms/MS-PPTX/` | [MS-PPTX] v20240820 (file `[MS-PPTX]-240820.docx`), Appendix A | `7530fef0768c14bee58463c896718a90f020606a0677cab221a63080e93c8170` |
| `ms/MS-XLSX/` | [MS-XLSX] 2026-05-19 (`MS-XLSX-260519.docx`), Appendix A | `724dc55c19963233a4a9e5f6fa93f70b8f782408641f95feabcf2bfef7540c10` |
| `ms/MS-OFFMACRO2/` | [MS-OFFMACRO2] v20240820 (`[MS-OFFMACRO2]-240820.docx`, officeprotocoldoc.z19.web.core.windows.net/files/MS-OFFMACRO2/), Appendix A | `0a8f806eef4339119de77d8cb0094dc1333c6eac0a47be425b97b4b21330f67c` |

Files under `ms/` are named from the schema's target namespace (`word-2010-wordml.xsd` is `http://schemas.microsoft.com/office/word/2010/wordml`). Several namespaces are printed by more than one document, and the generator merges them (the first file in `views.manifest` wins on a clash): `word/2010/wordml` ([MS-DOCX] main, [MS-ODRAWXML] adds `contentPart`), `excel/2010/spreadsheetDrawing` ([MS-ODRAWXML] main, [MS-XLSX] merged) and `excel/2006/main` ([MS-XLSX] has the reference types, [MS-OFFMACRO2] the macro-sheet worksheet type). The [MS-OFFMACRO2] `excel/2006/main` schema is printed with its `<xsd:schema>` start tag missing the closing `>`; `pin-schemas.py` restores that one character and changes nothing else.

`views.manifest` lists the schemas the generator turns into typed views, with the C# namespace, namespace-enum name, conventional prefix and role of each. Adding a schema to the manifest is all it takes to add a vocabulary; see `tools/ViewGenerator/README.md`.

## Schemas not turned into views

Every XSD above is listed in `views.manifest`, as a root or merged into one, except the `ecma376/strict/` files, which are the Strict counterparts of the Transitional files of the same name and supply the Strict namespace URIs and enumeration values. Namespaces that Office writes but that no pinned document defines, so they have no view and read as unknown children (counted on the public corpus, `tests/OneDocument.Schema.Tests/WalkTests.cs`): Dublin Core (`dc:`, `dcterms:` in `docProps/core.xml`), W3C InkML (referenced by the ink-action schema), the ActiveX `ocx` part, the SharePoint content-type and property-bag parts (`metadata/contentType`, `metadata/properties`, `FormTemplates`), `DataMashup`, `mipLabelMetadata`, the data-model `gemini/pivotcustomization` parts, and Google Sheets' `sheetsCustomData`.
