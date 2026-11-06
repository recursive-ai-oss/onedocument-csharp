# Legacy Excel workbooks (BIFF2 to BIFF8)

`RecursiveAI.OneDocument.Spreadsheets.Biff` reads Excel 2.0 to 2003 workbooks and converts them to the SpreadsheetML hub (`Workbook`). Reading follows [MS-XLS] (BIFF8); the layouts of BIFF2 to BIFF5 are not in that specification and are read from the files themselves (see "What is certain" below).

```csharp
using Workbook wb = Workbook.Open("old.xls");     // sniffs the content, never the extension
LossReport loss = wb.ImportLoss!;                   // what the hub could not carry
wb.SaveAs("new.xlsx");                              // an ordinary workbook from here on
Office.Convert("old.xls", "new.xlsx");
string text = Office.ExtractText("old.xls");
```

`XlsConverter.ToWorkbook(source, context, loss, token)` is the same with an explicit loss sink. The converted workbook has no file path, so `Save()` is refused and `SaveAs` picks the format. Opening never recalculates: cached results stay as the file stored them.

## Layers

1. **Records** (`BiffRecordSet`, `Biff` namespace): a stream is cut into records (a 2-byte type, a 2-byte length, up to 8,224 data bytes). Every record is kept as an offset and length over the stream's bytes, unknown and future records included; nothing is copied or dropped at this layer. A record that claims more bytes than the stream holds ends the list (`biff.truncated-record`). `CONTINUE` records are joined where a reader needs them: a string split by a `CONTINUE` restarts with a new flag byte that says whether the rest is 8-bit or UTF-16 ([MS-XLS] §2.5.293).
2. **Conversion** (`XlsConverter`): the first `BOF` gives the version; the workbook-global records (code page, fonts, formats, XFs, styles, palette, shared strings, sheet list, `SUPBOOK`/`EXTERNSHEET`, names) are read first, then each sheet substream in `BOUNDSHEET` order (found by its stored offset; when the offset points nowhere the next unused substream is taken, `biff.sheet-missing` if there is none).
3. **Formulas** (`PtgDecoder`, `PtgEncoder`): `Ptg` tokens ([MS-XLS] §2.5.198) become the public formula tree and print as the A1 text a workbook stores. Shared formulas (`SHRFMLA` with `PtgExp` and relative `PtgRefN`) are resolved to one text per cell; array formulas become an anchor with its range, the other cells keep values; data tables become `dataTable` formulas. The encoder writes a tree back as BIFF8 tokens (references in the reference class, no evaluation hints), so `tree → tokens → tree` is exact; the writer uses it. The function numbers come from `XlsbFunctionTable`, the one table behind the BIFF reader, the BIFF8 writer and the binary workbook codec; BIFF8 uses only the numbers below 380.

## What carries over

| Source | Hub |
|---|---|
| `NUMBER`, `RK`, `MULRK`, `INTEGER` | numbers (RK: the 30-bit integer or the high 30 bits of a double, divided by 100 when flagged) |
| `LABELSST`, `LABEL`, `RSTRING` | text (rich-text runs dropped, `loss.rich-text`) |
| `BOOLERR`, `BLANK`, `MULBLANK` | booleans, errors, styled empty cells |
| `FORMULA` + `STRING` | formula text and the cached result (number, text, boolean, error) |
| `XF`, `FONT`, `FORMAT`, `STYLE`, `PALETTE` | `styles.xml`: fonts, fills (all 18 patterns), borders (14 line styles, diagonals), alignment, rotation, indent, shrink, protection, number formats, cell styles. Colour indexes stay indexed (`indexed="n"`) and a custom palette becomes `indexedColors`. Formats below id 164 that the file defines are renumbered into the custom range. |
| `ROW`, `COLINFO`, `COLWIDTH`, `DEFCOLWIDTH`, `DEFAULTROWHEIGHT` | row height (only when the file marks it manual), hidden rows and columns, outline levels, default formats |
| `MERGEDCELLS` | merged ranges |
| `WINDOW2`, `PANE`, `SCL` | grid lines, headers, zeros, right-to-left, zoom, frozen and split panes |
| `NAME` | defined names (workbook and sheet scope), built-in names (print area and titles, filter database); function and macro names are dropped |
| `HLINK` | hyperlinks (URL, file and in-document targets) |
| `NOTE`, `OBJ`, `TXO` | comments (the text from the `TXO` of the note's object) with a plain legacy drawing so the notes show |
| `DV` | data validation (all types, operators, messages; typed lists as a literal list) |
| `CONDFMT`, `CF` | conditional formatting: "cell value" and "formula" rules with their differential formats (`DXFN`: font, fill, borders, number format) |
| `CONDFMT12`, `CF12` | the Excel 2007 rule kinds: colour scales, data bars, icon sets (all 17 sets), top/bottom N and percent, text rules (contains, does not contain, begins, ends), blanks, errors, the date periods, above/below average (with equal and standard deviations), unique and duplicate values, with their `DXFN12` formats and priorities. `CFEx` (the extension of a plain `CF` rule) is not read and counts as `loss.conditional-formats-extended`. |
| `SETUP`, margins, `HEADER`, `FOOTER`, centring, grid lines, breaks, `WSBOOL` | page setup, print options, header/footer, page breaks, fit-to-page, outline summary direction |
| `PROTECT`, `OBJPROTECT`, `SCENPROTECT`, `PASSWORD` | sheet protection with the legacy 16-bit password hash |
| `AUTOFILTERINFO` + `_FilterDatabase` | the filter range (criteria are dropped) |
| `DATEMODE` | 1904 date system |
| `MSODRAWINGGROUP`, `MSODRAWING` | pictures (JPEG, PNG, DIB as BMP, TIFF, EMF and WMF inflated) with their cell anchors |

## Loss report

`Workbook.ImportLoss` lists, with counts, what the hub could not take. Codes are `loss.<feature>`:
`loss.document-properties` (properties of the summary information streams with no counterpart, named in the message; the rest are carried, see `ole.md`), `loss.ole-objects` (embedded objects that could not be matched to their shape or storage, and linked objects: they keep their picture), `loss.charts` (chart sheets and embedded charts; their records stay in the record layer only), `loss.vba` (the macro project is not carried), `loss.macro-sheets`, `loss.macro-names`, `loss.pivot-tables`, `loss.drawing-shapes` (shapes other than pictures), `loss.form-controls`, `loss.pictures` (a picture whose blip could not be decoded), `loss.rich-text`, `loss.formulas` (formulas that could not be converted keep their cached values), `loss.external-links` (references into other workbooks: the cell keeps its cached value), `loss.hyperlinks` (a target of a kind that is not read), `loss.conditional-formats` and `loss.conditional-formats-extended` (a rule that has no hub form, or `CFEx`), `loss.data-validation`, `loss.autofilter-criteria`, `loss.defined-names`, `loss.document-properties` (on import only), `loss.legacy-cell-borders` (BIFF3 and BIFF4 cell borders and fills).

## Defects

Every repair is a `Record` defect located by stream path and record offset, except where noted. See `defects.md` for the table. An encrypted workbook (`FILEPASS`) is decrypted with `OpenContext.Password`, or raises `OfficeEncryptedException`; see `encryption.md`. Any exception that is not an `OfficeException` raised while reading is turned into `biff.malformed`.

## Code pages

Eight-bit text in BIFF2 to BIFF5 (and in `FILEPASS`-free BIFF8 header strings) is decoded with the workbook's `CODEPAGE` through the .NET `Encoding` registry. The reader registers `CodePagesEncodingProvider` for this. A code page the runtime does not have is reported as `biff.codepage-unavailable` (kind `Encoding`) and the text is read as Windows-1252. BIFF8 compressed strings are Latin-1 by definition and do not depend on the code page.

## What is certain, and what is not

- **BIFF8** is read from [MS-XLS] (v20250819) and checked against files LibreOffice writes from generated inputs (`testdata/xls`: values, formulas, styles, names, merges, comments, validation, conditional formats, pictures; the test compares the converted workbook with the workbook the file was written from).
- **BIFF5** record layouts (XF, FONT, NAME, FORMULA, BOUNDSHEET, EXTERNSHEET, narrow references) are written from the format as it appears in files; LibreOffice no longer writes BIFF5, so they are covered by hand-built streams and by the govdocs1 corpus read on the build host (counts in the release notes), not by an oracle. The XF alignment, border and fill bit positions of BIFF5 are the least certain part.
- **BIFF2 to BIFF4**: values, text, booleans, errors, formulas, fonts, number formats and column widths. BIFF2 cells carry their format in three attribute bytes (folded into the XF table); BIFF3 and BIFF4 XF records are read for font, format, protection and horizontal alignment only.
- Excel's own conversion of the same legacy files is the reference for open questions: the cases are listed in `testdata/xls/excel-cases.md` and are answered at the release gate.

## Not converted yet

Chart conversion, VBA, pivot tables, the `CFEx` conditional-format extension, external workbook references as formulas, drawing shapes other than pictures, form controls, filter criteria, phonetic text, and the OLE objects (kept as opaque storages in the record layer's source file, not in the hub).

## Writing

```csharp
workbook.SaveAs("out.xls");                      // the extension picks BIFF8
Office.Convert("in.xlsx", "out.xls");
LossReport loss = workbook.ExportLoss!;        // what the last .xls save could not carry
```

`XlsWriter` ([MS-XLS] §2.1.7.20) writes one clean BIFF8 dialect into a compound file with a single `Workbook` stream (class id of an Excel 97-2003 workbook, code page 1200). Everything is rebuilt from the hub; nothing of a legacy file's own record stream is kept, so records the hub has no place for (chart records, OLE storages, phonetic text, `CFEx`) are not carried and the loss report says so where they were counted at read time.

**Written:** the workbook globals (`INTERFACEHDR`, `CODEPAGE`, `WINDOW1`, `DATE1904`, fonts, formats, XFs, styles, palette, `BOUNDSHEET8`, `SUPBOOK`, `EXTERNSHEET`, names, `SST` and `EXTSST` with `CONTINUE` splitting at string boundaries), and per sheet: `INDEX` and `DBCELL` blocks of 32 rows, `ROW`, `NUMBER`/`RK`/`LABELSST`/`BOOLERR`/`BLANK`/`FORMULA` (+ `STRING`, `ARRAY`, `TABLE`) records, `COLINFO`, `DEFCOLWIDTH`, `MERGEDCELLS` (1,026 to a record), `WINDOW2`, `PANE`, `SELECTION`, `SCL`, page setup and print options, breaks, header and footer, sheet protection with the 16-bit password hash, the autofilter range with its hidden `_FilterDatabase` name, hyperlinks (URL, in-document), data validation, conditional formats, and cell notes (`MSODRAWING` with the shape, `OBJ`, `TXO` and `NOTE`, with a `MSODRAWINGGROUP` in the globals). Excel 16.0.20527 opens workbooks written with notes (`docs/office-oracle-1.0.md`, defect 4): the declared lengths of a note's shape container and of the containers above it count the eight-byte text box marker that sits in the next `MSODRAWING` record.

- **Styles.** Every hub cell format becomes one XF (flattened, with the cell style as parent); the style formats come first, with at least 15 as Excel expects. Colours are palette indexes: a colour that is already in the default palette keeps its index, others take free slots of a custom `PALETTE` (56 slots), beyond that the nearest is used (`loss.colors`). Gradient fills become a solid fill of the first stop (`loss.gradient-fills`). Fonts, formats and XFs beyond BIFF8's counts take the first font, General and the default format (`loss.fonts`, `loss.number-formats`, `loss.cell-formats`).
- **Formulas** go through the formula tree and `PtgEncoder`; shared formulas are written out as one formula per cell. Array formulas keep their range (a dynamic array becomes a legacy array over its current range, `loss.dynamic-arrays`); data tables are `TABLE` records. A formula BIFF8 cannot express (structured references, `LET`, `LAMBDA`, newer functions without a `_xlfn.` form, references into other workbooks, over 8,192 token bytes) keeps its cached value as a constant (`loss.formulas`). A name that cannot be encoded stays in its place and refers to `#REF!` (`loss.defined-names`), because formulas number names by position.
- **Conditional formats.** A group of up to three "cell value"/"formula" rules is written as `CONDFMT` + `CF`; any other group as `CONDFMT12` + `CF12` ([MS-XLS] §2.4.43, §2.4.57) with colour scales, data bars, icon sets, top/bottom, text, blank, error and date rules. Unique, duplicate and average rules, and the Excel 2010 extension kinds, are `loss.conditional-formats-extended`.
- **Limits.** 65,536 rows and 256 columns: cells, column settings, merges, comments and references outside are not written (`loss.cells-outside-grid`, `loss.formulas`). Strings are cut at 32,767 characters, sheet names at 31.
- **Not written, reported:** charts and chart sheets (`loss.charts`), pictures (`loss.pictures`; the shared OfficeArt layer has no writer yet), drawing shapes, tables (`loss.tables`), sparklines, pivot tables, external links, macro projects, rich-text runs (`loss.rich-text`), first/even headers and footers, tab colours, sheet protection permissions beyond the three flags, modern password hashes, hyperlink screen tips, autofilter criteria, extended data validation. Document properties are written: the core, extended and custom properties become the `\u0005SummaryInformation` and `\u0005DocumentSummaryInformation` streams (`ole.md`).
- **Password.** `Workbook.SaveAs(..., OfficeFormat.Xls, password)` encrypts the stream with RC4 CryptoAPI (`encryption.md`).
- **Left for the Excel check:** whether Excel opens the output without repair (the cases are in `testdata/xls/excel-cases.md`).
