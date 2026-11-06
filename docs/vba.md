# VBA projects

VBA is data here: it is read, listed, extracted and removed. It is never compiled, interpreted or run, the p-code is never decoded, and no password is cracked. Namespace `RecursiveAI.OneDocument.Vba` (core). Specification: Office VBA File Format Structure ([MS-OVBA]); where the project sits in each file comes from [MS-DOC], [MS-XLS], [MS-PPT] and [MS-OFFMACRO2].

## Where a project lives

| File | Project |
|---|---|
| DOCM, DOTM, XLSM, XLTM, XLAM, PPTM, POTM, PPSM, PPAM, XLSB | the `vbaProject.bin` part (content type `application/vnd.ms-office.vbaProject`), a compound file whose root is the project root storage |
| DOC | the `Macros` storage ([MS-DOC] §2.1.9) |
| XLS | the `_VBA_PROJECT_CUR` storage |
| PPT | a persist object of the `PowerPoint Document` stream: a `RT_ExternalOleObjectStg` record (type 0x1011, [MS-PPT] §2.10.40) holding the project compound file, zlib-compressed when the record instance is 1; the `VBAInfoAtom` (0x0400, §2.4.11) of the document container points at it and carries `fHasMacros`. A loose `_VBA_PROJECT_CUR` storage is read too. |

Signatures: the OOXML signature parts (`vbaProjectSignature*.bin`, content types `application/vnd.ms-office.vbaProjectSignature*`) and, inside compound files, the streams `\u0005DigitalSignature` (legacy), `…Ex` (agile) and `…Ext` (V3). They are located, not verified; verification belongs to the cryptography package. [MS-OSHARED] §2.3.2 says XLS and PPT keep their signature in property sets and DOC in the table stream's user variables: those are not looked for, so `IsSigned` can be false for a signed legacy file whose signature is kept there (a documented gap).

## Reading

```csharp
VbaProject? p = VbaProject.Open("book.xlsm");           // sniffs the content; null when there is no project
foreach (VbaModule m in p!.Modules)
    Console.WriteLine($"{m.Kind} {m.Name}: {m.Source.Length} chars");
p.ExportModules("out");                                  // Module1.bas, Class1.cls, UserForm1.frm …
using Document doc = Document.Open("contract.docm");
VbaProject? same = doc.GetVbaProject();                  // also on Workbook and Presentation
```

- **`VbaProject`**: name, code page, platform, LCID, compat version, description, help file, constants, version; `References` (registered libraries, other projects, controls, originals), `Modules`, `DesignerStorages` (user-form storages, kept opaque: the controls and layout are not decoded), `ProjectText` (the `PROJECT` stream), `Protection`, `Signatures`, `PerformanceCacheVersion`, `HasPCode`, `SourceAndPCodeMayDisagree`, `UnlistedStreams`.
- **`VbaModule`**: name, stream name, kind (`Standard`, `Class`, `Document`, `Designer`), read-only and private flags, doc string, `TextOffset` (the length of the p-code cache before the source), `SourceBytes` and `Source` (the source decompressed from `TextOffset` with the project code page), `ExportText()` and `FileName`.
- **Kinds** come from the `PROJECT` stream's item lines (`Module=`, `Class=`, `Document=`, `BaseClass=`); without them the module type record tells procedural (0x0021) from the rest (0x0022, read as `Class`).
- **Export text**: the stored source already holds Office's `Attribute` lines. A `.cls` adds `VERSION 1.0 CLASS` with its `BEGIN … END` block; a `.frm` adds `VERSION 5.00` and an empty `Begin … End` block, because the form's controls live in the designer storage. A `.bas` is the source.
- **Compression** ([MS-OVBA] §2.4.1): `VbaCompression.TryDecompress` checks every chunk header, token and length, bounds the output by the part-size and allocation limits, and returns what decoded before damage together with the reason. The three specification examples are tests.
- **Damage** is a `vba.*` defect (`defects.md`); a module with no readable source is still listed.
- **Limits**: the module count is bounded by `MaxRecordCount`, decompressed sizes by `MaxPartSize` and `MaxAllocationBytes`.

### Locked projects

`CMG`, `DPB` and `GC` in the `PROJECT` stream are reported (`VbaProtection`): the protection bits (user, host, editor), whether a password is present, and whether the project is hidden. Their obfuscation ([MS-OVBA] §2.4.3) is undone only to read these flags. The password hash is never extracted or compared. Tools that lock a project by corrupting `DPB` make the value unreadable; that counts as locked (`vba.protection-unreadable`). A lock does not stop reading the source, because the source is in the file either way.

### Source and p-code

A module stream is `[p-code cache][compressed source]`. Office runs the cache when `_VBA_PROJECT.Version` matches its own, and shows the source. A file whose source was replaced or blanked while the cache was kept ("VBA stomping") shows different code from what runs. Without decompiling p-code that is not decidable, so `SourceAndPCodeMayDisagree` is a flag, not a verdict: it is true when the cache is live (the version is not 0xFFFF) and either a module that carries a cache has no readable source, or the `VBA` storage has a module stream that `dir` does not list. A source replaced by other readable source is not detected. `HasPCode` says only that a cache is present.

## Removal

```csharp
VbaRemovalReport r = VbaRemoval.Remove("in.docm", "out.docx");   // new file; the input is untouched
Document doc = Document.Open("in.docm"); doc.RemoveVbaProject(); doc.SaveAs("out.docx");
```

The file is written as a new file. The report lists what was removed (`VbaRemovedItem`: kind, location, bytes), the format change, the modules and signatures removed, and what was left or could not be done (`NotDone`, typed codes in `defects.md`).

| Input | What is removed | Output |
|---|---|---|
| DOCM, DOTM, XLSM, XLTM, PPTM, POTM, PPSM | the project part, the signature parts, Word's `vbaData.xml`, the relationships to them, their content types (including a `bin` default no other part uses); the main part's content type becomes the macro-free one | DOCX, DOTX, XLSX, XLTX, PPTX, POTX, PPSX |
| XLAM, PPAM, XLSB | the same parts | the same format without a project (`vba.no-macro-free-type`): there is no macro-free add-in or binary-workbook type |
| DOC | the `Macros` storage | DOC |
| XLS | the `_VBA_PROJECT_CUR` storage and the `ObProj` and `ObNoMacros` records ([MS-XLS] §2.4.185, §2.4.184); the stream positions after them are corrected (`BoundSheet8.lbPlyPos`, `Index` positions, `ExtSst` string positions) | XLS |
| PPT | every project storage record is emptied in place (zero payload; for a compressed record, a zero size and an empty zlib stream) so no stream position moves, and `fHasMacros` is cleared; copies left in the stream by earlier saves are emptied too | PPT |

A signature belongs to the project it signs and goes with it. Every other part, stream and record is copied as stored (OOXML parts byte for byte; compound files are rewritten by the compound-file writer, so sector layout changes and stream contents do not). A file without a project is copied and the report says `HadProject = false`. An encrypted OOXML file is decrypted with the context's password and written unencrypted (`vba.output-not-encrypted`); an encrypted DOC, XLS or PPT is refused with `OfficeUnsupportedException` (decrypt it first).

**Kept on purpose** (reported where it matters): a DOC's customization table (key and toolbar bindings that name macros: they bind to nothing), a legacy file's signature kept in property sets, macro-sheet (XLM) content, ActiveX controls, `codeName` attributes and Ribbon `onAction` names. None runs without a project.

**Preserving.** Opening a macro-enabled file and saving it unchanged keeps `vbaProject.bin` and its signature parts byte-identical; the hubs refuse to save a file that still has a project in a macro-free format (`OfficeUnsupportedException`) rather than drop it silently, and `RemoveVbaProject()` is the explicit way.

## Oracles and what they showed

- **LibreOffice 26.2.6.3** (black box) re-writes a project it loaded from a macro-enabled file: DOCM, XLSM, PPTM and XLS outputs carry a project (`testdata/vba/lo-roundtrip.*`), with its own attribute lines and references and the user form turned into a class module. It does not write one into DOC or PPT, and it does not convert its own Basic libraries to VBA (tried with the default profile and with the `VBAStorage`-style options). Its outputs read with every module's source intact, and the de-macroed copies open in it (checked: XLS/XLSX values, DOCX text, PPTX to PDF).
- **Office** answers (does Word, Excel and PowerPoint open a de-macroed file without repair; what Office's own "save without macros" produces) are in `testdata/vba/ORACLE-REQUEST.md`.

## Gaps

- Signatures kept in property sets or a DOC's user variables are not found (above).
- A PowerPoint file that stores the project only behind a persist directory entry no scan reaches is found by scanning top-level records of the document stream; the persist directory is not consulted.
- BIFF5 workbooks are handled like BIFF8; no BIFF5 file with a project was available to check.
- The p-code cache is never decoded, so a stomped project with readable replacement source is not detected.
