# Markup: the XML tokenizer, writer, tree and reader

This note states the behaviour of the XML layer (`RecursiveAI.OneOffice.Markup.Xml` and `RecursiveAI.OneOffice.Markup`). The XML documentation on each type states the details.

## Standards

XML 1.0 fifth edition and Namespaces in XML 1.0 third edition (W3C) are the grammar. ECMA-376 Part 3 (Markup Compatibility and Extensibility) is the model for the compatibility helpers.

## The tokenizer (`Markup.Xml`)

A forward-only reader over UTF-8 bytes. It depends on nothing else in OneOffice (limits and errors are its own types), so it can be lifted out.

- **Tokens tile the input.** Apart from a leading UTF-8 byte order mark, every byte belongs to exactly one token (XML declaration, start tag, empty-element tag, end tag, text, whitespace, CDATA section, comment, processing instruction). Concatenating the raw tokens reproduces the input.
- **Names keep their prefixes** as written. Namespaces are resolved from the declarations in scope; `xml` is predeclared.
- **Text and attribute values are raw.** `XmlText` decodes the five predefined entities and character references, and normalises line ends (and, in attribute values, tab/line feed/carriage return to space) on demand.
- **Strict.** A part is refused with `XmlSyntaxException` (code, byte offset) when it has: a DTD of any kind (internal subset included), an entity other than the five predefined, a control character, invalid UTF-8, a name that is not a name, an unquoted or unterminated attribute, a duplicate attribute (by name or by expanded name), an unbound prefix, a reserved namespace binding, a mismatched end tag, text outside the root, a second root, a misplaced or malformed XML declaration, or truncation. There is no recovery and no repair; the codes are in `defects.md`.
- **Limits** (`XmlLimits`): open elements (256), attributes per element, namespace declarations included (256), name length in bytes (1,024). The core package builds `XmlLimits` from `OfficeLimits`.
- **Encodings.** The tokenizer reads UTF-8. A UTF-16 or UTF-32 byte order mark, or a UTF-16 `<`, is refused with `xml.unsupported-encoding`. `XmlEncoding` detects the encoding and transcodes UTF-16 to UTF-8 and back; the tree and the reader use it.
- **No allocation per token.** The tokenizer is a ref struct over a span. Its `XmlTokenizerState` holds the open-element names and namespace bindings in a few arrays that grow with nesting depth and the number of declarations in scope, never with the document.
- **Blocks.** Given a non-final block, `Read` returns false with `EndOfDocument` false when a token is incomplete; the caller supplies a block starting at `BytesConsumed` and the same state. Spans stay valid until the next `Read`.

`XmlWriter` writes well-formed UTF-8 to an `IBufferWriter<byte>`: names, prefixes and attribute order as given, no indentation, empty elements as `<a/>`, an escaped-text API and verbatim raw writes. It checks names, duplicate attributes and characters, and raises `ArgumentException` or `InvalidOperationException` for misuse.

## The tree (`MarkupDocument`, `MarkupNode`)

- **Lazy.** `Parse` makes one validating pass and records for each node a kind, links and byte ranges (about 44 bytes per node). Names, attributes, text and namespaces are read from the part's buffer when asked for. The buffer is held, not copied (a UTF-16 part is transcoded once and the UTF-8 copy is held).
- **Lossless.** Whitespace, comments, processing instructions, CDATA sections and the XML declaration are nodes. An unedited document writes its original bytes exactly, UTF-16 and byte order mark included.
- **Edits** (set or remove an attribute, set text, insert, replace or remove nodes by XML fragment) never touch the original buffer. New bytes go to a side buffer and the node table points at them. On write, a clean subtree is one block copy from the original; only edited start tags, inserted nodes and rewritten text are new bytes. An edited start tag keeps its other attributes, quotes, spacing and order. A self-closing element that gains content turns into a start tag and an end tag.
- **Unknown content needs no special handling.** Nothing is interpreted, so `mc:AlternateContent`, `mc:Ignorable`, `extLst`, foreign namespaces and unknown elements survive in place.
- **Inserted XML** must be well-formed content; its prefixes must be declared in it or in scope where it lands (`ArgumentException` otherwise). Insertion is atomic: on failure the tree is unchanged.
- **Errors** are `OfficeFormatException` (`xml.*`, located by part URI and byte offset; for UTF-16 parts, the offset in the original bytes) or `OfficeLimitException`. `TryParse` returns false instead.
- **Threading.** Not safe for concurrent mutation; concurrent reads of an unmodified document are safe.

## Markup compatibility (`MarkupCompatibility`)

Read-only helpers for ECMA-376 Part 3: ignorable namespaces (§7.2), `ProcessContent` (§7.3), `MustUnderstand` (§7.4), `AlternateContent`/`Choice`/`Fallback` selection (§7.5–7.7, §9.3). The consumer gives the set of namespaces it understands as a predicate; nothing is removed from the tree.

## The reader (`MarkupReader`)

The tokenizer with the library's limits and exceptions, over memory or a stream with a refilled buffer. Memory is bounded by the largest token plus the open-element stack, not by the part, so it serves streaming row and slide readers. It reads synchronously or asynchronously. UTF-16 streams are buffered whole (bounded by `MaxPartSize`) and transcoded.
