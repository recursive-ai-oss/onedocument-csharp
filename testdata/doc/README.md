# Word binary fixtures

Small Word 97-2003 files used by `DocFixtureTests`. They are **generated**, not collected: `tools/DocFixtures/make_docx.py` writes DOCX inputs (hand-written WordprocessingML, deterministic) and `tools/DocFixtures/make_doc.sh` has a word processor save them as `MS Word 97`. The converter that wrote them is run only as a program; its version is in `oracle-version.txt` (LibreOffice 26.8.2.2).

| File | Bytes | SHA-256 |
|---|---|---|
| `basic.doc` | 9728 | `5bf7fd5959feae75c751d4f0cb2ec242d678de426e327b2f941645a6b80c4285` |
| `lists.doc` | 11264 | `a3bde8b3e585b5f51ca49e2caa7967aaeb59690ed5e1e416c1e85ac067e8ef59` |
| `notes.doc` | 9728 | `9c88385c305b65a9a359990d8adbca0b2c72a7f43c23637fb241b04ccc52da73` |
| `picture.doc` | 9728 | `9335274e09043b868002632421cb4bd9d63ddf59a3d559b969f01db41e0e334f` |
| `revisions.doc` | 9216 | `19fc940158c4d46c00acbd55bcf70ea401d1f00f77834fb578d3182a3d3772ce` |
| `sections.doc` | 10752 | `3d5ef7e11efc2c0b468afa8232baa0d2961f71f96ce8c39a45ba1e4d95affb5e` |
| `table.doc` | 11776 | `33bb955af1666e84581fcb7fc4af00aea808871c30e4af14b39ae0a9fef46457` |

The contents: `basic` (headings, character and paragraph formatting, tab stops, a hyperlink, a page break), `table` (header row, shading, a horizontal and a vertical merge), `lists` (a two-level numbered list and bullets), `notes` (footnote, endnote, comment), `sections` (two sections, landscape, two columns, header and footer with a page field), `revisions` (a tracked insertion and deletion by two authors), `picture` (one inline PNG).

`word-conversion-cases.md` lists what is to be recorded from desktop Word for the same files at the release gate. It has no answers yet.
