# Encrypted files

Password-protected files open when the caller supplies `OpenContext.Password` (`Document.Open`, `Workbook.Open`, `Presentation.Open`, `Office.Open`). Without a password, or with a wrong one, the reader raises `OfficeEncryptedException` (`encryption.password-required` / `encryption.password-incorrect`). Everything runs on BCL primitives (`Aes`, `IncrementalHash`, `HashData`, `HMAC*`); RC4 and the XOR obfuscation are written here, because the BCL has neither. Spec: [MS-OFFCRYPTO]; the binary formats' own sections of [MS-XLS], [MS-DOC] and [MS-PPT].

## Schemes

| Scheme | Files | Read | Written |
|---|---|---|---|
| Agile (AES-CBC/CFB, SHA-1/256/384/512, HMAC integrity) | DOCX, XLSX, PPTX, XLSB in an `EncryptedPackage` compound file | yes | no (waits for the cryptography package) |
| Standard (AES-ECB, SHA-1) | same | yes | no |
| RC4 1.1 (40-bit, MD5) | XLS, DOC, PPT | yes | no |
| RC4 CryptoAPI (SHA-1, 40 to 128-bit) | XLS, DOC, PPT | yes | XLS only |
| XOR obfuscation (methods 1 and 2) | XLS, DOC | yes | no |

The decrypted inner package is opened through the normal sniff and registry path, so an encrypted DOCX is a DOCX afterwards.

**Not supported (typed `OfficeUnsupportedException` naming the scheme):** extensible (third-party) encryption, certificate-only agile files, RC4 in an ECMA-376 package, password-protected Word 6/95 documents, encrypted BIFF2–4 workbooks, AES inside an Office binary file.

## Default passwords

When no password is given, the reader tries the passwords Office itself uses for files protected against editing only: `VelvetSweatshop` for XLS and OOXML, and `/01Hannes Ruescher/01` for PowerPoint (the value PowerPoint uses for read-only protection). A file that opens that way opens silently. A password the caller gives that does not match is an error (`PasswordIncorrect`); the defaults are not tried after it.

## Legacy specifics

- **XLS.** `FILEPASS` follows `BOF`; the records after it are decrypted per record, except the ones [MS-XLS] §2.2.10 leaves clear (`BOF`, `FILEPASS`, `INTERFACEHDR` and a few toolbar and revision-log records) and the 4-byte sheet position of `BOUNDSHEET8`. Block numbers restart every 1024 bytes of record data. The written file has `FILEPASS` inserted and every file position in the stream (sheet positions, index and shared-string-table offsets) moved by its length.
- **XOR index.** [MS-OFFCRYPTO] §2.3.7.3 does not say what array index the first byte of a record gets. Found black-box (the encryption by LibreOffice, both directions): the index is the stream offset of the byte plus the record's length field (the offset counted from the record header, so the first data byte of a record at offset p with length n gets p+4+n). Our XOR files open in LibreOffice; Excel is a pending question (`testdata/encryption/ORACLE-REQUEST.md`).
- **DOC.** `fEncrypted` in the FIB; the `1Table`/`0Table` header (`EncryptionHeader`) carries the scheme. The first 68 bytes of the FIB stay clear; the Table and Data streams and the rest of `WordDocument` are decrypted.
- **PPT.** The `CryptSession10Container` in the document container, `Current User` carrying the `0xF3D1C4DF` token. Each persist object (8-byte header plus data) is decrypted with the persist identifier as block number. A persist directory entry that points outside the stream is dropped (`ppt.persist-object-range`). The `Pictures` stream is encrypted per blip field, each field with block 0 and a fresh cipher state (the header, the UIDs, the tag or metafile header, then the picture data); [MS-PPT] states it for the whole record, and two PowerPoint-written files settled the per-field rule.
- **Writing XLS.** `Workbook.SaveAs(path|stream, OfficeFormat.Xls, password)` writes RC4 CryptoAPI (128-bit, SHA-1). The salt and verifier are seeded from a hash of the BIFF stream, so output is deterministic and the same input gives the same bytes. Other formats throw `OfficeUnsupportedException`.

## Document properties

In the binary schemes the `SummaryInformation` and `DocumentSummaryInformation` streams are stored in the clear (they are separate compound-file streams outside the encrypted ones, and Office writes them that way), so title, author and the other properties of an encrypted XLS, DOC or PPT are readable without the password and are read after decryption like any other file; the XLS writer with a password writes them clear too. When the RC4 CryptoAPI header has `fDocProps` cleared ([MS-OFFCRYPTO] §2.3.1), Office moves them into an encrypted summary stream ([MS-OFFCRYPTO] §2.3.5.4): a compound-file stream named `EncryptedSummary` that holds the property streams encrypted, each under its own block key, with an index of descriptors. The XLS, DOC and PPT readers decrypt it after the password has been checked and use the properties inside in place of any clear ones. [MS-PPT] §2.1.6 names the stream for PowerPoint; the other two documents leave the name to the application, and `EncryptedSummary` is assumed for Word and Excel until the Office oracle run (`BinaryRc4Encryption.TryReadEncryptedSummary` takes the stream bytes, so a different name is a one-line change). A damaged summary stream is ignored (the clear property streams, if the file has any, are used). The OOXML schemes have no such stream: the whole package including `docProps` is inside `EncryptedPackage`.

## Limits and safety

`OfficeLimits.MaxEncryptionSpinCount` (default 10,000,000, the schema maximum; Office writes 100,000) bounds the password hash loop. Header sizes, key lengths, stream lengths and the declared package size are checked before allocation. The verifier is checked before any data is decrypted. An agile integrity (HMAC) mismatch is reported as the repaired defect `encryption.integrity-mismatch` and the data is still returned, because a damaged file is more useful read than refused. Malformed headers raise `OfficeFormatException` with a record location.

## API notes: AOT and browser

The code uses `Aes` (ECB, CBC, and CFB where an agile descriptor asks), `IncrementalHash`/`HashData` for SHA-1/256/384/512 and MD5, and `HMAC`. On NativeAOT these call the platform crypto (OpenSSL on Linux); no reflection is involved. In browser WASM the BCL has the SHA family and HMAC, but no MD5 and no `Aes`: a file that needs one of them raises `OfficeUnsupportedException` naming it (`docs/platforms.md`). The library does not carry its own AES or MD5; the gap ends when the family's cryptography library ships.

SHA-1 and MD5 are broken for new designs but the formats fix them. Calls are confined to `OfficeHash`, the one file that switches off the analyzer rules CA5350 and CA5351. RC4 is weak: the legacy schemes protect against casual access only.

## Known gaps

Word 6/95 and BIFF2–4 encryption; certificate, extensible and rights-managed protection; no OOXML or ODF encryption on write.
