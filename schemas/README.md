# Pinned schemas

The typed-view generator reads these files and nothing else. They are copies of published standards documents, never edited. `tools/schemas/pin-schemas.py <downloads> schemas` rebuilds the directory from the downloads below and refuses any file whose SHA-256 differs. Licence notices are in `THIRD-PARTY-NOTICES.md`.

| Directory | Source download | SHA-256 |
|---|---|---|
| `ecma376/transitional/` | ECMA-376 5th ed. Part 4 (Dec 2016), `OfficeOpenXML-XMLSchema-Transitional.zip` inside `ECMA-376-4_5th_edition_december_2016.zip` | `bd25da1109f73762356596918bf5ff8b74a1331642dba5f1c1d1dfc6bed34ecd` |
| `ecma376/strict/` | ECMA-376 5th ed. Part 1 (Dec 2016), `OfficeOpenXML-XMLSchema-Strict.zip` inside `ECMA-376-1_5th_edition_december_2016.zip` | `9d0bcad9cf06054785b03762fcfadbf6bab7e54a5f9d69434e34b7fd464d4129` |
| `ecma376/opc/` | ECMA-376 5th ed. Part 2 (Dec 2021), `OpenPackagingConventions-XMLSchema.zip` inside `ECMA-376-2_5th_edition_december_2021.zip` | `1d489dc491168ea1f9e9a59063acc8dd5f02b4ad1d21aa7ec19ba9a58d020c70` |
| `ms/MS-DOCX/` | [MS-DOCX] 23.0, 2026-08-18, Appendix A | `ed5d74bb33232d083742949d8072766e99f02fc3fbc6b7dc8b73264965dcce3a` |
| `ms/MS-ODRAWXML/` | [MS-ODRAWXML] 2026-02-17, Appendix A | `20de73988d209fd18f1776c2960103179c9c93b883c1d9a44ab3ce83b8b60f5b` |

Files under `ms/` are named from the schema's target namespace (`word-2010-wordml.xsd` is `http://schemas.microsoft.com/office/word/2010/wordml`). The two Microsoft documents both print the `word/2010/wordml` schema; the generator uses the [MS-DOCX] copy.

`views.manifest` lists the schemas the generator turns into typed views, with the C# namespace, namespace-enum name, conventional prefix and role of each. Adding a schema to the manifest is all it takes to add a vocabulary; see `tools/ViewGenerator/README.md`.
