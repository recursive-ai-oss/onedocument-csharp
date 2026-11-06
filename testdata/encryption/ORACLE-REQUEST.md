# Encrypted-file cases for Office (unanswered)

Run once at the final gate on the Office oracle machine. Nothing here has been answered.

For each file: open it with the password given, record whether Office prompts for a password, accepts it and shows a repair prompt or not, and save as an unencrypted file for comparison with `Workbook.Open(path, password)` / `Document.Open` converted the same way.

| File | Password | Question |
|---|---|---|
| written-secret.xls | `p@ss word` | Written by `Workbook.SaveAs(..., OfficeFormat.Xls, password)` (RC4 CryptoAPI, 128-bit). Opens in Excel without repair? Same cells as the unencrypted save? |
| cryptoapi-plain-basic.xls | `p@ss word` | Our RC4 CryptoAPI encryption of a plain workbook (test encryptor). Opens? |
| cryptoapi-features.xls | `p@ss word` | As above with comments, validation, conditional formats, names. Opens? |
| xor-v0-basic.xls | `pw12` | XOR obfuscation (method 1). The specification does not say what array index a record's first byte takes; we use the stream offset of the byte plus the record length, which LibreOffice accepts both ways. Does Excel open it with the right text? |
| secret-basic.xls, secret-basic.doc | `secret` | Written by LibreOffice (RC4 1.1). Open in Excel and Word, same content as the plain twin? |
| secret-basic.docx, secret-basic.xlsx, secret-runs.pptx | `secret` | Written by LibreOffice (agile). Information only. |
| secret-epplus.xlsx | `Epp lus-1` | Agile written by EPPlus 4.5.3.3. Information only. |

PowerPoint: the binary PowerPoint encryption has no writer in the library (the encryptor used by the tests is test-only, and LibreOffice does not write encrypted PowerPoint files), so only two real PowerPoint-encrypted files from the public corpus validate it.

sha256 of the committed bytes:

```
85ba30e30480aba1f5ae6d59d570c4681d4f8b2d1dc336814426b934c39d7de0  written-secret.xls
00ba51ba16b1ab8095f2ae0031e2d447d3d901a3a94adcd0f45adbb547afb129  cryptoapi-plain-basic.xls
c0d19a1692053ac057f5b125c6f176d6b74a7527439c4633c4c8c85543101bdb  cryptoapi-features.xls
e93e8e8256c8a7ec38154dbcc0e880e7bfcddf109dff3cb12932eceb683f0c70  xor-v0-basic.xls
be64233a366dc194dc3dae2ca4d1a81a1991b3d6911dd65a5eff29d3805c23a4  secret-basic.xls
38741823e77102647deeb70a40746126a16eb0cb4b1d6db986a7143194d916e3  secret-basic.doc
14fbbf4e6de18b448e09e31c2777a1634aa643691f6079ecce77515b0271999e  secret-basic.docx
8c2a18be49c2bf12a619e81fba6ea97795c0da08882797b3c03a18bd8cc1919a  secret-basic.xlsx
6b47029d6f438c90eacb294e566ab31a4247254909bf7ca5d0bee79685c3a0d7  secret-runs.pptx
320638299d52b655e77f33de278fd7f160086656802a027ac3e65e0fbc8323d3  secret-epplus.xlsx
```

## Office run (Word, Excel and PowerPoint 16.0.20527)

Answered as far as the oracle can drive the applications: see the row for this request in [`docs/office-oracle-1.0.md`](../../docs/office-oracle-1.0.md#the-other-requests). Questions not listed there as run are still open.
