# OpenDocument text cases for the desktop Word run

Not yet answered; the desktop-Office run fills in the last column. The files in `ours/` are written by OneDocument from generated test documents
(`OdfTests.OutputFilesForTheOracleRunsWhenAskedFor`, `ONEDOCUMENT_ODF_OUT=<dir>`); `rich.docx` (here beside them) is the source of the first two.
All three validate against the OpenDocument 1.3 RelaxNG schema and open in LibreOffice.

| File | sha256 |
|---|---|
| `ours/rich-written.odt` (`rich.docx` through the hub, written as ODT) | `646d10d0a00fd5903d1f5b07cb8e5562126a163538d793aa274c22fc5088fcb3` |
| `ours/rich-written.fodt` (same, flat) | `b97aa83064f2846c0f4a53dfb3372ff2eea215e9e7f4c375bdd44176ae9e2e91` |
| `ours/features-written.odt` (`features.fodt` (notes, sections, tracked changes and fields) read and written again) | `6e1e43a72ff796b98018bf34003b649b01c48bcc5f432af420ad21aeb7054a95` |

| # | Request | Record | Answer |
|---|---|---|---|
| 1 | Open `rich-written.odt` in Word | opens without a repair or security prompt; page count; heading styles; list numbers; table grid, spans and header row; footnotes; headers and footers per section | |
| 2 | Open `rich-written.fodt` | same | |
| 3 | Open `features-written.odt` | same; tracked changes and comments shown | |
| 4 | Save `rich-written.odt` as DOCX in Word | the DOCX; compare paragraph text, list and table structure with `rich.docx` | |

What decides: Word's own import of OpenDocument. Differences that change text or structure are findings; spacing and font-substitution differences are not.

## Office run (Word, Excel and PowerPoint 16.0.20527)

Answered as far as the oracle can drive the applications: see the row for this request in [`docs/office-oracle-1.0.md`](../../docs/office-oracle-1.0.md#the-other-requests). Questions not listed there as run are still open.
