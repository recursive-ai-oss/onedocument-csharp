# Protection

Editing restrictions and the passwords that lift them, for the three kinds. Read, set, verify and remove.

> **Protection is not encryption.** The content stays readable and the restrictions bind only applications that honour them: anyone who edits the XML can delete the element. A password here is stored as a salted hash only so that a program can decide whether to stop enforcing the restriction. To keep a file confidential, encrypt it (`docs/encryption.md`).

| Kind | API | Element |
|---|---|---|
| Document | `Document.Protection` | `w:documentProtection` (`edit`, `enforcement`, `formatting`) |
| Document | `Document.WriteProtection` | `w:writeProtection` (read-only recommended, write-reservation password) |
| Document | `Document.AddEditingException`, `EditingExceptions`, `RemoveEditingExceptions` | `w:permStart` / `w:permEnd` |
| Workbook | `Worksheet.Protection` | `sheetProtection`, `protectedRanges` |
| Workbook | `Workbook.Protection` | `workbookProtection` (`lockStructure`, `lockWindows`, revisions password) |
| Workbook | `Workbook.FileSharing` | `fileSharing` (write-reservation password, read-only recommended) |
| Presentation | `Presentation.Protection` | `p:modifyVerifier` |

Each has `Verify(password)` returning a `PasswordCheck` (`Match`, `Mismatch`, `UnsupportedAlgorithm`), a `Remove()` that lifts the restriction without asking (the caller decides who may), and `TryRemove(password)` that removes it only on a match. A protection with no password stored matches any input. Reading a file never needs a password; the restrictions are metadata.

## Hashes

Current spelling (ECMA-376 Part 1 §17.15.1.29, §18.2.29, §18.3.1.85, §19.2.1.19): `algorithmName`, `hashValue`, `saltValue`, `spinCount` (the workbook element prefixes them `workbook…`/`revisions…`; the presentation spells the count `spinValue`).

1. The password is encoded as UTF-16LE; a leading U+FEFF is dropped.
2. `H0 = H(salt + password)`.
3. `Hn = H(Hn-1 + n)` for `n = 0 … spinCount-1`, `n` as a 4-byte little-endian integer.
4. The last value is the hash, in base64.

Algorithms computed: SHA-512 (the default), SHA-384, SHA-256, SHA-1, MD5. MD2, MD4, RIPEMD and Whirlpool are named by the schema but not computed: such a verifier reads fine and verifies as `UnsupportedAlgorithm`. A stored spin count above `OfficeLimits.MaxEncryptionSpinCount` raises `OfficeLimitException` before any hashing.

New passwords are written with `ProtectionOptions` (`Algorithm`, `SpinCount`, `Seed`). The default is SHA-512 with 100,000 iterations, what Office writes. **The salt is 16 bytes taken from a hash of the seed and the element's name** (and, for sheets and ranges, their names); the library reads neither the clock nor a random generator, so the same document, password and seed write the same bytes. The default seed is empty, so equal passwords in different documents get equal salts unless the caller passes a per-document seed.

Older spellings, read and verified, replaced when a new protection is written:

- **Word 2007** (`w:cryptAlgorithmSid`, `w:cryptSpinCount`, `w:hash`, `w:salt`; ECMA-376 Part 4 §14.8.2.3). The password is first reduced to the 32-bit key of the pre-2007 word processors ([MS-OFFCRYPTO] §2.3.7.4, `PasswordVerifier.WordKey`; the specification works `Example` out to `0x64CEED7E`), the key's bytes are reversed (its little-endian form), and that is hashed as above. `cryptAlgorithmSid` is the CryptoAPI id (3 MD5, 4 SHA-1, 12 SHA-256, 13 SHA-384, 14 SHA-512; 1 MD2 and 2 MD4 are `UnsupportedAlgorithm`).
- **Excel 16-bit** (`password`, `workbookPassword`, `revisionsPassword`, `reservationPassword`: up to four hex digits). `PasswordVerifier.ExcelHash` is the verifier of [MS-OFFCRYPTO] §2.3.7.1; `password` → `83AF` is the value Excel stores.
- **PowerPoint** (`p:cryptAlgorithmSid`, `p:hashData`, `p:saltData`, `p:spinCount`) is verified as the current procedure on the UTF-16LE password. No PowerPoint-written file has confirmed it yet (see the open Office checks below).

## Documents

`Protect(restriction, password, restrictFormatting, options)` writes `w:documentProtection` into the settings part (created when there is none) in schema order, replacing the previous element and dropping its old-spelling attributes. `ReadOnly`, `Comments`, `TrackedChanges` and `Forms` are the `w:edit` values; `IsEnforced` is true when `enforcement` is on and the restriction is not `None`.

`WriteProtection.Set(password, recommended, options)` writes `w:writeProtection`.

**Editing exceptions.** `AddEditingException(first, last, group, user)` writes `w:permStart` before the first paragraph and `w:permEnd` after the last, with a new id. The paragraphs must be siblings in one container, in order. `group` is a `w:edGrp` value (`everyone` by default); `user` writes `w:ed` instead. The editable stretch is whole paragraphs; column ranges for tables (`colFirst`, `colLast`) are not offered.

## Workbooks

`Worksheet.Protection.Protect(allowed, password, options)` replaces `sheetProtection`. `SheetActions` lists what the user may still do; the schema stores each action as "locked" with a default per action, and only values that differ from the default are written, so `Allowed` reads back exactly what was set. With no list, selecting locked and unlocked cells is allowed (what Excel offers). Protected ranges (`AddProtectedRange(name, ranges, password)`, `ProtectedRanges()`, `VerifyRange`, `RemoveProtectedRange`) go into `protectedRanges` in schema order. `Workbook.Protection.Protect(lockStructure, lockWindows, password)` keeps an existing revisions password. `Workbook.FileSharing.Set(password, readOnlyRecommended, userName)` is the write reservation.

Sheet protection works on worksheets; chart and macro sheets raise `NotSupportedException` on `Protect`. Converting a workbook to XLS or XLSB keeps sheet protection with the limits those writers already report: the XLS writer stores the 16-bit password and the sheet, objects and scenarios flags and reports a modern hash and the allowed-action list as losses; the XLSB writer writes the sheet protection record. Neither binary writer carries `workbookProtection` or `fileSharing` today (nothing in the writers reads them).

## Presentations

`Protection.Protect(password, options)` writes `p:modifyVerifier` in schema order (after the default text style).

## Open Office checks

Committed for the next Office oracle run, none answered yet: that Word, Excel and PowerPoint open the files `SamplesForExternalChecksAreWritten` writes (`operation-samples/replace-protect.*`, `replace-textbox.docx`) without repair; that each application accepts the password that was set (`sample`; `reserve` for the write reservation, `book` for the workbook, `range` for the protected range); and that PowerPoint accepts a verifier written in the old spelling.
