# Packages: OPC and compound files

How `RecursiveAI.OneDocument.Packaging` reads and writes the containers under every format. Defect codes are in `defects.md`; limits in `contracts.md`.

## OPC packages (`OpcPackage`)

An Open Packaging Conventions package (ECMA-376 Part 2) on a zip, or as Flat OPC.

**What opening does.** It reads the zip central directory, `[Content_Types].xml` and the package relationships (`/_rels/.rels`). Nothing else: a part's bytes are read when first asked for (`GetBytes`, `OpenRead`), a part's relationships when first asked for (`GetRelationships(part)`). `OpcPackage.LoadedPartCount` counts the items whose content has been read, and a freshly opened package reports 2. Content is never decoded further here and images are never decoded.

**What is a part.** Every zip item whose name is a valid part name (§6.2.2), except the content-types item and the relationships parts. Those two kinds are not parts of the abstract package (§7.2.3.1, §6.5.2): they are `OpcPackage.ContentTypes` and `GetRelationships`/`SetRelationships`. Everything else the zip holds (directory entries, names that are not part names, duplicates, pieces of interleaved parts) is *kept as stored* and written back, but not exposed as a part, and each is reported as a defect.

**Names.** The part name is the zip item name with a leading `/` and with percent-escapes of non-ASCII characters decoded (§7.3.5); a new part's item name is the part name without the slash, non-ASCII percent-encoded (§7.3.4). Names are equal when they differ only in ASCII case (§6.2.2.3); percent-escapes of unreserved characters (which §6.2.2.2 forbids in a name) are folded too, so an input that has them is repaired rather than refused. The spelling as stored is kept for writing.

**Content types.** A part's type is its override, else the default for its extension (§7.2.3.5). Adding a part adds an override or a default only when the map would not already say it (§7.2.3.4); removing a part removes its override. A part with no type is kept with an empty one. If the map did not change, `[Content_Types].xml` is written back as stored.

**Relationships.** `Relationship` records keep `Type` and `Target` as written. `OpcPackage.TryResolveTarget` resolves an internal target against its owner (§6.4, §6.5.2.3), ignoring a fragment or query. A change replaces the whole list for one owner; owners nobody changed keep their relationships part as stored. Setting an empty list removes the Relationships part. Targets are never fetched: external targets are data.

**Lossless writing.** `WriteTo`/`Save`/`SaveAs` write the stored items in the stored order under the stored names. An untouched item is written with the bytes it had. The container library does not yet copy a zip entry without decompressing it, so today an untouched item is decompressed and compressed again (one method, `OpcPackage.CopyOrWrite`, decides this; it becomes a raw copy when the library offers one). Either way **the decompressed bytes of every untouched item are identical**, and what changes is only the compressed form. Entry timestamps are kept; nothing reads the clock.

**Atomic save.** `Save()` writes a temporary file beside the target, flushes it and renames it over the original. If anything fails (a cancelled token, a part that cannot be read, a full disk) the original is untouched and the temporary file is removed. After a successful `Save()` the package points at the file it wrote, so its parts stay readable and an immediate second edit and save work. `SaveAs(path)` writes a copy and leaves the package as it was.

**Flat OPC.** A package opened from `pkg:package` XML is written back as Flat OPC; `WriteZipTo` and `WriteFlatOpcTo` choose a form explicitly. XML parts go in as `pkg:xmlData` without their XML declaration (it cannot appear inside the wrapper) and everything else as base64 `pkg:binaryData`. A part that is not well-formed UTF-8 XML is written as binary, so one damaged part cannot damage the file. An `mso-application` instruction naming the main part's application is written ahead of `pkg:package`.

**Recovery.** If the zip directory cannot be used, the reader is run again in recovery mode, which scans local headers; what it skipped is reported (`packaging.zip-damaged`). If recovery fails too, the original error is raised. A recovered package reads every item up front when there was no directory to find them by, so it is bounded by the limits instead of being lazy.

**Limits.** The zip-bomb ratio, total size and part count are the container reader's, set from `OfficeLimits`; `MaxPartSize` applies to every read; `MaxXmlDepth`, `MaxXmlAttributeCount` and `MaxXmlNameLength` apply to the XML of content types, relationships and Flat OPC. The XML of those three vocabularies is read by a small internal reader (no `System.Xml`): it takes the predefined entities and character references only and refuses any DTD.

**Threading.** After open, reading parts and relationships from several threads is safe (one lock guards the container reader). Mutating a package is single-threaded.

## Compound files (`CfbPackage`)

A compound file ([MS-CFB]) as an `IPackage`: every stream is a part named by its path (`/WordDocument`, `/ObjectPool/_1/CONTENTS`); `Storages` lists the storages with their CLSIDs, state bits and times. A stored name that contains `/` or `%` has those characters written `%002F` and `%0025`. Lookups compare exactly, then ignoring case ([MS-CFB] 2.6.4).

Opening reads the header, the allocation tables and the directory. A stream's chain is followed when it is read. Reading is strict first; a file the strict checks reject is read again in the container reader's recovery mode, and what it repaired is reported with the stream and file offset (`packaging.cfb-*`). A stream recovery had to shorten says so (`IsDamaged`, `DeclaredLength`). A stream that cannot be read raises `OfficeFormatException` (`packaging.damaged`) naming the stream.

Saving an unmodified package copies the original file. Once anything is changed the file is rebuilt by the compound-file writer: stream contents, storage CLSIDs, state bits and times are kept, the sector layout is the writer's (so the file is not byte-identical, its streams are). Saving is atomic as for OPC.

## Sniffing

`FormatSniffer` reads the zip directory and the compound-file directory directly through the container library's lazy readers, not through these packages, and reads only the entries it names. It needs no change for this layer.
