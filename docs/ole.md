# OLE property sets and embedded objects

How the legacy formats' document properties and embedded objects are read, written and carried into the hub. Namespace `RecursiveAI.OneDocument.Ole` (core). Specifications: [MS-OLEPS] (property set data structures), [MS-OSHARED] §2.3.3.2 (the Summary Information, Document Summary Information and User Defined property sets), [MS-OLEDS] (the OLE storage structures) and, for the file moniker inside a link, [MS-OSHARED] §2.3.7.8. Defect codes: `defects.md` (`ole.`, `doc.ole-*`, `ppt.ole-*`, `biff.object-*`).

## Property sets

A property set stream (`\u0005SummaryInformation`, `\u0005DocumentSummaryInformation`, or any other) is an `OlePropertySets`: a version (0 or 1), a system identifier, a class identifier and one or more `OlePropertySet`s. Each set has a format identifier (FMTID), a code page, an optional locale and behavior, an optional dictionary of property names, and its properties in stored order.

**Values** are `OlePropertyValue`: the stored type number (`OleVarType`, flag bits included) and the value as a .NET object. Every type of [MS-OLEPS] §2.15 is held: the integer and floating-point types, currency, date, decimal, HRESULT, Boolean, FILETIME (as ticks, so a duration such as the edit time survives), strings, blobs, clipboard data, CLSIDs, indirect stream and storage names, versioned streams, vectors (including vectors of variants) and arrays of up to 31 dimensions. The .NET type for each is in the type's documentation.

**Strings** are decoded with the set's code page (`Encoding`, with the Windows code pages registered once), or as UTF-16 when it is 1200. A string type keeps the form it had (`VT_LPSTR`, `VT_LPWSTR`, `VT_BSTR`). A code page the runtime does not have is a typed defect (`ole.propset-codepage-unsupported`): the strings are read as Windows-1252 and the set's `CodePage` becomes 1252, so a write stores the text it holds.

**Reading** is tolerant and bounded. Every count and length is checked against the bytes that remain before anything is allocated: a vector or array cannot claim more elements than its bytes hold, a string cannot run past its property, a set cannot list more properties than fit. A damaged property is dropped with a defect (offset in the stream) and the others are kept; only an unusable header is fatal (`Read` raises `OfficeFormatException`, `TryRead` reports it). At most 64 sets are read. A vector of ANSI strings is read both with the padding [MS-OLEPS] §2.5 requires after each string and without it ([MS-OSHARED] §2.3.3.1.5 describes an unaligned form), taking the one that ends where the value ends.

**Writing** mirrors reading and is deterministic: a code page property first, then the dictionary (when the set has names), locale, behavior, and the properties in order; every value padded as [MS-OLEPS] §2.15 says; offsets and sizes computed, never taken from the input. A write of what was read reads back to the same values. A string a narrow code page cannot hold becomes `?`. A value whose .NET type does not match its type number throws `ArgumentException` (a caller mistake).

```csharp
var set = new OlePropertySet(OlePropertySets.SummaryInformationFormatId);   // CodePage 1200 by default
set.Set(2, OlePropertyValue.FromString("Title"));                           // PIDSI_TITLE
var sets = new OlePropertySets();
sets.Add(set);
byte[] stream = sets.ToArray();                                             // the content of \u0005SummaryInformation
```

## Mapping to the package properties

`OleDocumentProperties.Read(summary, documentSummary, target, context)` fills a `DocumentProperties` (core, extended and custom parts of the package) and returns a description of every property that has no counterpart. `OleDocumentProperties.Create(properties)` makes the two streams from the hub's properties for the writers of the legacy formats (the XLS writer calls it; it writes text as UTF-16 and reads no clock). The table is the mapping both ways.

| Stream and identifier | Package property |
|---|---|
| Summary 2 title, 3 subject, 4 author, 5 keywords, 6 comments | `dc:title`, `dc:subject`, `dc:creator`, `cp:keywords`, `dc:description` |
| Summary 7 template, 18 application name, 19 security | `Template`, `Application`, `DocSecurity` |
| Summary 8 last author, 9 revision number | `cp:lastModifiedBy`, `cp:revision` |
| Summary 10 edit time (FILETIME as a duration) | `TotalTime`, in minutes (the 100 ns ticks divided by 600,000,000, rounded down) |
| Summary 11 last printed, 12 created, 13 last saved | `cp:lastPrinted`, `dcterms:created`, `dcterms:modified` (a zero FILETIME means unset and is skipped) |
| Summary 14 pages, 15 words, 16 characters | `Pages`, `Words`, `Characters` |
| Document summary 2 category, 27 content status, 28 language, 29 document version | `cp:category`, `cp:contentStatus`, `dc:language`, `cp:version` |
| Document summary 3 presentation format, 5 lines, 6 paragraphs, 7 slides, 8 notes, 9 hidden slides, 10 multimedia clips, 17 characters with spaces | `PresentationFormat`, `Lines`, `Paragraphs`, `Slides`, `Notes`, `HiddenSlides`, `MMClips`, `CharactersWithSpaces` |
| Document summary 11 scale, 16 links dirty, 19 shared document, 22 hyperlinks changed | `ScaleCrop`, `LinksUpToDate` (inverted), `SharedDoc`, `HyperlinksChanged` |
| Document summary 12 heading pairs, 13 document parts | `HeadingPairs`, `TitlesOfParts` |
| Document summary 14 manager, 15 company | `Manager`, `Company` |
| Document summary 23 version (major in the high 16 bits, minor in the low) | `AppVersion` as `MM.mmmm` |
| User-defined `_PID_LINKBASE` (a blob of null-terminated UTF-16) | `HyperlinkBase` |
| Every other named user-defined property | a custom property: text, 32-bit and 64-bit integers, double, Boolean, date (`VT_FILETIME`, and `VT_DATE` converted) |

What is not carried is named in the returned list and reported as one loss item per converter (`loss.doc-properties`, `loss.document-properties` for XLS, `ppt.document-properties-not-carried`), with the stream, the identifier and the reason: the thumbnail (Summary 17: the package keeps a thumbnail as a picture part), the VBA signature (24), content type (26), a hyperlink list with entries, custom properties of other types (blobs, vectors, 64-bit unsigned values past the signed range, non-finite numbers), property identifiers not in the standard sets, and a stream that could not be read. Dropped without a report: the byte count (Document summary 4, which Office recomputes and the package has no element for), the document's identity GUID (`_PID_GUID`) and an empty hyperlink list. Characters XML cannot carry are removed from text.

## Embedded objects

An OLE object in a legacy file is a storage ([MS-OLEDS] §1.3.3): a Word `ObjectPool/_<number>` storage, an Excel `MBD<8 hex digits>` storage at the root, or a PowerPoint `ExOleObjStg` persist object (a zlib-compressed or plain compound file). `OleStorageImage` is a copy of one such storage (its streams, sub-storages and CLSIDs) held as bytes, so it outlives the file; `ToCompoundFile()` writes it as a compound file whose root is the storage, which is what OOXML stores for an embedded object. Nothing in an object is interpreted beyond description, executed or activated.

`OleStorageImage.Describe()` gives an `OleObjectInfo`:

- **Linked** when the `\u0001Ole` stream's flags say so ([MS-OLEDS] §2.3.3) or the CLSID is the standard link class; the link target is the file moniker of the relative source, else of the absolute one ([MS-OLEDS] §2.3.3: the relative moniker is used when present), as the path stored.
- **Package** when the CLSID is the packager's (an embedded file; its `\u0001Ole10Native` stream is carried whole, not unpacked).
- **Embedded** otherwise.
- The display name and the programmatic identifier come from the `\u0001CompObj` stream ([MS-OLEDS] §2.3.8; implementations store the identifier in its third string); when there is none, a small table of well-known classes gives one. A damaged stream gives less detail and never an exception.

### Into OOXML

| Source | Written |
|---|---|
| **DOC** (`EMBED`/`LINK` field whose separator has `sprmCFOle2`, `sprmCPicLocation` naming `_<number>`) | `w:object` with a VML picture shape (the preview of the field result, as `v:imagedata`) and `o:OLEObject Type="Embed"` (`ProgID` from the field instruction, else `\u0001CompObj`), whose `r:id` names `word/embeddings/oleObject<n>.bin`; a link is `Type="Link"` with an external `oleObject` relationship to a `file:///` URI. The field is replaced by the object, as Word writes embedded objects. |
| **XLS** (`Obj` record with `ot` 8, `fDde` and `fPrstm` clear, `FtPictFmla.lPosInCtlStm` naming `MBD<hex>`) | `xl/embeddings/oleObject<n>.bin`; an `oleObjects/oleObject` (`progId`, `shapeId`, `r:id`) and a picture shape in the sheet's legacy drawing (`o:ole`, `v:imagedata`, `x:ClientData ObjectType="Pict"` with the cell anchor in pixels). The shape is no longer a plain picture of the sheet's DrawingML drawing. Shapes and `Obj` records are matched by order, as the format orders them; when their counts differ nothing is matched. |
| **PPT** (shape with an `ExObjRefAtom` → `ExOleObjAtom` → persist object) | `p:graphicFrame` with `p:oleObj` (`progId`, `r:id`, `p:embed` or `p:link`, the preview as the object's `p:pic`) and `ppt/embeddings/oleObject<n>.bin`. Only slides carry the object: the shapes a layout copies from a slide keep their picture. |

The relationship type is `…/officeDocument/2006/relationships/oleObject` and the part's content type `application/vnd.openxmlformats-officedocument.oleObject` (`OleEmbedding`). The compound file keeps the streams and sub-storages byte for byte; the writer's sector layout differs from the source's.

### What stays a loss

The preview picture is always kept. The data of an object is not carried (the object keeps its picture; `loss.doc-ole-objects`, `loss.ole-objects` for XLS, `ppt.ole-object-flattened`) when its storage is missing, is not a compound file, is past the limits, or cannot be written; for a linked object whose moniker names no file; in DOC when the object is inside tracked changes, a bookmark or a comment range; in XLS for linked objects and when shapes and records cannot be matched; and in PPT for ActiveX controls. Each has a defect with a location.

### Open behaviour (Office decides at the 1.0 gate)

The converted files are committed as cases for the Office oracle without answers: whether Word, Excel and PowerPoint open the packages without a repair prompt, show the preview, and activate the embedded object; whether Excel accepts the pre-2010 form (`oleObject` with a legacy-drawing shape) without the `objectPr` anchor and the drawing shape that newer files add; whether a VML picture shape with the shape type defined once per part is accepted in every story of a Word document.
