# OLE fixtures

One legacy file per format, each with document properties (title, subject, author, keywords, comments, dates, four custom properties)
and one embedded OLE object (a small spreadsheet with its preview picture). They are **generated**: `tools/Ole/make_ole_inputs.py` writes
OpenDocument inputs and `tools/Ole/make_ole_fixtures.sh` has an office suite save them as Word, Excel and PowerPoint 97-2003 files
(filters `MS Word 97`, `MS Excel 97`, `MS PowerPoint 97`). The suite is run as a black box; its version is in `oracle-version.txt`.

| File | Bytes | SHA-256 |
|---|---|---|
| `ole-props.doc` | 13312 | `0b19899d0a7aee03cd4e7089655bc93daca83cf227acfbb52556e359c9d8e595` |
| `ole-props.xls` | 9216 | `f6ed7d8ff75a399e015f758232e4500c42fbe1813fbd5434d122365664ba9938` |
| `ole-props.ppt` | 461312 | `db6aabd303b410231617b851d254485b1011785770c60e967614bef1cf645453` |

The `.ppt` is large because the suite writes a thumbnail of the slide into its summary information stream, which the converter reports
as the one property it does not carry.

The tests (`DocOleTests`, `XlsOleTests`, `PptOleTests`) read them back and check the properties, the object parts and the compound file
in each; they also change the files (drop a storage, corrupt a record) to reach each defect. Whether Word, Excel and PowerPoint open the
converted packages without a repair prompt, and activate the embedded object, is a separate, unanswered request (`ORACLE-REQUEST.md`).
