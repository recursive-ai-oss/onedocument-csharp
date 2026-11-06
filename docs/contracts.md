# Shared contracts

The contracts every layer of OneDocument is written against. They live in the core package (`RecursiveAI.OneDocument`). This note states the behaviour; the XML documentation on each type states the details.

## Sources (`RecursiveAI.OneDocument.IO`)

`IByteSource` is input bytes however they are held. Reads are positioned (`Read(offset, buffer)`, `ReadAsync(offset, buffer, token)`), not tied to a cursor, so one source can serve several readers and a reader touches only the bytes it needs. A source never reads past its `Length`; a short read happens only at the end.

| Source | Made by | Sync | Async | Zero-copy memory |
|---|---|---|---|---|
| memory | `ByteSource.FromMemory` | yes | yes | yes (`TryGetMemory`) |
| file | `ByteSource.FromFile` | yes, concurrent | yes | no |
| seekable stream | `ByteSource.FromStream` | yes, serialised | yes | no |
| remote / ranged | `ByteSource.FromRanged` | no (`CanReadSync` is false) | yes | no |

`ByteSource.ToMemoryAsync` buffers a remote source (bounded by a limit) for readers that need synchronous access. `ByteSource.AsStream` presents a source as a read-only seekable `Stream` for the stream-based archive readers.

## Limits (`OfficeLimits`)

One immutable object, passed down through every layer inside `OpenContext`. All limits are on by default and must be positive. A limit that is hit raises `OfficeLimitException` naming the property (`LimitName`) and carrying a defect with the code `limit.<property in kebab case>`. Nothing is truncated silently. DTDs and entity declarations in XML are always refused; there is no limit to raise.

| Property | Default | Why |
|---|---|---|
| `MaxTotalUncompressedSize` | 2 GiB | all parts together; `Large` raises it to 256 GiB |
| `MaxExpansionRatio` | 200 : 1 | zip-bomb guard, checked after 32 MiB are produced; Office's own XML compresses 10-30 : 1 |
| `MaxPartCount` | 50,000 | zip entries or compound-file streams |
| `MaxPartSize` | 1 GiB | one part read whole into memory; streaming cursors are not bound by it |
| `MaxXmlDepth` | 256 | open elements |
| `MaxXmlAttributeCount` | 256 | per element, namespace declarations included |
| `MaxXmlNameLength` | 1,024 | element, attribute and prefix names |
| `MaxFormulaLength` | 8,192 | Excel's own limit |
| `MaxFormulaDepth` | 64 | Excel's own limit |
| `MaxRecursionDepth` | 256 | any other tree walk, group tree or evaluator |
| `MaxRecordCount` | 100,000,000 | records in one binary stream |
| `MaxRecordSize` | 256 MiB | one binary record |
| `MaxAllocationBytes` | 1 GiB | any single allocation driven by input |
| `MaxDefects` | 10,000 | defects a collection keeps (fatal ones always kept; the rest are counted) |

`OfficeLimits.ToArchiveLimits()` maps total size, part count and expansion ratio onto the zip and compound-file readers; their path-length and depth limits keep the archive library's defaults. Parts of the library that call the archive readers translate its exceptions with the same names (`MaxEntryCount` surfaces as `MaxPartCount`).

## Defects and exceptions (`RecursiveAI.OneDocument.Diagnostics`, `OfficeException`)

Reading is tolerant, so a repair is not an error, but it is never silent. A `Defect` has a closed `DefectKind` (where in the stack), a stable string `Code` (the precise problem; see `defects.md`), a `DefectSeverity` (`Repaired`, `Dropped`, `Fatal`), an English message for people, and a `DefectLocation`:

- `Part`: part URI + byte offset in the uncompressed part;
- `Record`: stream path + byte offset of the record;
- `Cell`: sheet name + A1 address;
- `Text`: character offset in the text (RTF, CSV, Markdown).

Readers write defects to an `IDefectSink` (from any thread). `DefectCollection` keeps them in order up to a cap and counts the rest.

**The contract the parsers honour:** malformed, hostile, truncated, encrypted or unsupported input surfaces as an `OfficeException` and never as an untyped exception (`IndexOutOfRangeException`, `InvalidCastException`, `OutOfMemoryException`, and so on). The subtypes are `OfficeFormatException`, `OfficeLimitException`, `OfficeEncryptedException` (password required or wrong), `OfficeUnsupportedException` and `FormatNotAvailableException` (no reader or writer registered). Each carries the fatal `Defect`. Caller mistakes (null arguments, disposed objects) and failures of the caller's own streams (`IOException`, `OperationCanceledException`) are the only other exceptions. Each format also offers `TryOpen`-style methods that return false instead of raising these.

Code that calls the archive readers wraps every call and translates what it catches with the internal `ArchiveErrors.Translate`.

## Formats, registry and sniffing (`RecursiveAI.OneDocument.Formats`)

`OfficeFormat` names every format; `OfficeFormats` gives each a descriptor (kind, media type, extensions, traits) and maps an extension to a format **for naming outputs only**.

`FormatRegistry` is an explicit table of readers, text extractors and writers per format. Each kind's package registers what it implements. No application code is needed: a kind package ships a small C# file (`buildTransitive/`) that MSBuild compiles into the referencing project, where a module initializer calls the package's `Register(FormatRegistry.Default)`. It is plain source, not reflection or assembly scanning, so the trimmer and NativeAOT keep exactly the kinds the application references, and a project that references only core and `.Documents` has no spreadsheet code in its binary. Set `<OneDocumentAutoRegister>false</OneDocumentAutoRegister>` to leave it out (a project that fills a custom `FormatRegistry` itself, or a non-C# project, calls `DocumentFormats.Register` / `SpreadsheetFormats.Register` explicitly). Registering twice is harmless: a later registration replaces the earlier handler. `Office` and the document types use `FormatRegistry.Default`. For a kind whose package is not referenced, the operations raise `FormatNotAvailableException` naming the format.

`FormatSniffer` identifies content, never an extension. It reads a 4,096-byte prefix and, for containers, the least more it needs:

1. zip signature: an entry named `mimetype` means OpenDocument and its text names the format; otherwise `[Content_Types].xml` plus the package relationship of type `.../officeDocument` name the main part, and the main part's content type names the format (Transitional and Strict spellings alike); a zip with neither is `Unknown`;
2. compound-file signature: the streams at the root (`EncryptedPackage` with `EncryptionInfo`, `WordDocument`, `Workbook`, `PowerPoint Document`, `Book`);
3. binary signatures: PDF, PNG, JPEG, TIFF, WebP, bare BIFF streams;
4. `{\rtf`;
5. XML by root element: Flat OPC (`package` in the `xmlPackage` namespace; the main part's content type, within 1 MiB, tells the kind), the 2003 formats, flat OpenDocument (`office:document` with `office:mimetype`), SVG, HTML;
6. last, delimited text: at least two complete lines of valid text with the same number of tabs, or of commas or semicolons, outside double quotes. Markdown and plain text are never detected.

A damaged or unreadable container is `Unknown` (opening it reports the damage with its location). A resource limit being hit raises `OfficeLimitException`, so a bomb is not mistaken for junk. Remote sources are decided from the prefix when possible and buffered whole only for containers.

## Loss reports (`RecursiveAI.OneDocument.Diagnostics`)

A format that is narrower than the model it is written from, and every conversion, says what it dropped. `ILossSink.Report(code, feature, count, message)` is the one method; `LossReport` is the collector (one `LossItem` per code, counts added, `IsLossless` when empty, `CountOf(code)`). A writer takes an optional `ILossSink`; null means the caller does not care. Codes are `loss.<feature>`, lower case, never reused, and are listed with the format that reports them (CSV: `loss.formulas`, `loss.cell-styles`, `loss.number-formats`, `loss.sheets`, `loss.merged-cells`, `loss.embedded-objects`; see `csv.md`; Word binary files: the `loss.doc-*` codes in `doc.md`). `feature` is words for people ("formulas"), `count` is the number of cells, sheets or paragraphs affected (at least 1), and `message` says what happened to them. Loss is not a defect: defects describe damage in the input, losses describe what the output cannot hold.

## Packages and parts (`RecursiveAI.OneDocument.Packaging`)

`IPackage` is a set of named parts over a container; `IOpcPackage` adds relationships; the OPC and compound-file readers implement them. A part's bytes load lazily. **A part is untouched until someone replaces or opens it for writing**; reading never touches it. The writer copies untouched parts raw (a zip entry's compressed bytes), so they come out byte-identical. `PartUri` validates and compares OPC part names (ASCII case-insensitive; spelling kept) and resolves relationship targets.

`Save()` replaces the file the package was opened from, atomically: the new content goes to a temporary file beside it (`.<name>.<pid>.<n>.tmp`), is flushed to disk and renamed over the original (`AtomicFile`). A failed, cancelled or interrupted save leaves the original as it was. `InMemoryPackage` implements the contract in memory for tests and for generated documents; it is not a file format.

## Document base and the statics

`OfficeFile` is the base of `Document`, `Workbook` and `Presentation`: `Format`, `Kind`, `Limits`, `FilePath`, `Defects`, `Save()`, `SaveAs(path)` (format chosen by extension), `SaveAs(path, format)`, `SaveAs(stream, format)`, `ExtractText`, `Dispose`. `SaveAs` writes a copy and never changes the object's own `Format` or `FilePath`. Documents are not safe for concurrent mutation; read-only use from many threads is safe after load.

**Lazy parts in `Document`.** `Document.Open` and `FromPackage` read the package structure (content types, relationships) and nothing else. The main part is parsed the first time something needs it (`Body`, `Paragraphs`, `Markup`, `Revisions`, `IsStrict`, …), and styles, numbering, theme, notes and comments likewise. A main part that is not well-formed XML, or does not start with `w:document` (`docx.wrong-root`), therefore raises its `OfficeFormatException` at that first access, with the part name and offset, not at open; it raises again at every later access. `Save`, `SaveAs` and `ExtractText` on a document nobody has edited never parse the main part: it is copied as stored, so even a damaged main part survives a save unchanged. First-use initialisation is guarded, so concurrent readers share one tree and one hub object per part.

`Office` holds the one-liners (`Detect`, `Open`, `ExtractText`, `Convert`). They take the same `OpenContext` (limits, defect sink, password) the layers below take, and add no options of their own.
