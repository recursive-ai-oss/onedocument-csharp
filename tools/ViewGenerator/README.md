# ViewGenerator

Turns the pinned OOXML schemas into the typed views in `src/OneDocument.Schema/Generated`. The output is a pure function of `schemas/**`, `schemas/views.manifest`, `schemas/names.manifest` and this program: running it twice gives the same bytes.

```
dotnet run --project tools/ViewGenerator             # rewrite src/OneDocument.Schema/Generated
dotnet run --project tools/ViewGenerator -- --check  # exit 1 and list what differs, write nothing
```

`tools/schemas/pin-schemas.py <downloads> schemas` rebuilds `schemas/` from the pinned standards downloads (see `schemas/README.md`).

## Adding a vocabulary

Add the XSD under `schemas/` (through `pin-schemas.py` when it comes from a standards document), then one line to `schemas/views.manifest`: `path|NamespaceEnumMember|prefix|role`. A `root` schema gets a view for every complex type and global element it declares; a `support` schema only for the types a root schema reaches. Spreadsheet and presentation vocabularies are added the same way. A name the schema spells awkwardly is overridden in `schemas/names.manifest`.

## What is generated

For each complex type, a `readonly struct` named after the type (`CT_P` is `Paragraph` through the names manifest; without an entry the `CT_` prefix is dropped), in the C# namespace `RecursiveAI.OneDocument.Schema.<NamespaceEnumMember>`.

- **Attributes** are properties: `string?`, `bool?`, `long?`, `double?` or an enum, null when absent. Setting null removes the attribute. Enumerations are generated per simple type with a first member `Unrecognized`; an unlisted value reads as `Unrecognized` and the `<Name>Text` property keeps the raw text, so reading never throws. Unions that start with a number read as `long?` plus a `<Name>Text` string (measures such as `1.5cm`).
- **Child elements** follow the content model. One occurrence: a property returning the view or null, `GetOrAdd<Name>()` and `Remove<Name>()`. Repeatable: a `ChildList<T>` property (a `foreach`-able ref struct), `Add<Name>()` and `Remove<Name>()`. A sequence in the schema is respected on insert: the new child goes after the elements that must precede it and before those that must follow. Property bags such as `rPr` are unbounded choices in the schema but one-per-property in practice, so their members are single.
- **Text** content (`w:t`, `m:t`) is a `Text` property; the view for `w:t` is `TextContent` and its property `Text`.
- **Inheritance** is flattened: a derived type has the members of its bases, an implicit conversion to its base view and `ToBase()`.
- **Everything undeclared stays reachable**: `Children` lists every child element, `UnknownChildren` the ones the content model does not declare (extensions, `mc:AlternateContent`, foreign markup), and `UnknownAttributes.Of(view)` the attributes it does not declare. Wildcard content (`xsd:any`) counts as declared when a root schema declares that element.

Facts the generated code states in its XML docs: the schema type name, the element names it is the type of, the attribute's schema type, whether it is required, and the schema default (not applied on read: the hub resolves defaults).

## The view contract

The pieces the document hub codes against, all in `RecursiveAI.OneDocument.Schema`:

- `MarkupRef(MarkupNode node)`: one element of a `MarkupDocument` (the lazy markup tree in `RecursiveAI.OneDocument.Markup`), and the only type here that touches the markup layer. **The hop between hub and view**: every view has `Markup` returning its `MarkupRef` (`.Node` is the `MarkupNode`), and `Paragraph.Create(markupRef)` (or `new Paragraph(markupRef)`) wraps one; `markupRef.As<Paragraph>()` checks the element's name first. Reads go to the tree's attribute and child enumerators; writes use `MarkupNode.SetAttribute`, `RemoveAttribute`, `AppendXml`, `InsertXmlBefore`, `Remove` and `SetText`, so the tree alone keeps the document lossless. Spans a view returns follow the tree's rule: valid until the next edit.
- `IElementView<TSelf>`: implemented by every view, with static `Create`, `Kind`, `Accepts(ns, local)`, `ChildKind(ns, local, out rank)` and `IsKnownAttribute`. Generic code written against it never needs reflection.
- `SchemaNamespace` and `SchemaNamespaces`: namespace identities. Transitional and Strict URIs of one vocabulary are one member; `Uri(ns, strict)` gives either spelling and `Prefix(ns)` the conventional prefix a new declaration uses.
- `ViewKind` and `ViewFactory`: `RootKind(ns, local)` for global elements (the element a part starts with), `ChildKind(parentKind, ns, local, out rank)` for a child in a parent's content model, and `Dispatch(kind, markup, ref visitor)` which hands the element to an `IViewVisitor` as the generated struct, without boxing. These are explicit switches.

### Strict and Transitional

Views are generated from the Transitional schemas. Reading matches either namespace spelling and either spelling of an attribute's namespace. Writing keeps the document's flavour: a new element or attribute uses the Strict URI when the element it goes into is in a Strict namespace, and an existing attribute is overwritten in place. Enumerations are the union of the Transitional and Strict values (`left`/`start` both exist). Where a Strict type declares different children or attributes the Transitional shape is used and the difference is listed in `src/OneDocument.Schema/Generated/GenerationNotes.txt` (for example Strict has no `w:left`/`w:right` in table borders, only `start`/`end`). Content only a Strict schema declares reads through `Children`/`UnknownChildren`.

### Markup compatibility

A view reports `mc:AlternateContent` as an unknown child and does not look inside it. Code that wants the branch Office would take uses the markup layer's compatibility helpers and then wraps the chosen element's content with the same views.

## Known gaps

- Extension attributes on base elements (`w14:paraId` on `w:p`) are not members of the base view; they are the extension schemas' global attributes. `UnknownAttributes.Of` lists them.
- The Dublin Core elements in `docProps/core.xml` (`dc:title`, `dcterms:created`, …) have no pinned schema, so the core-properties view declares only `cp:` elements.
- Elements whose type is a simple type or `anyType` have no view; they are reached as raw `MarkupRef` values.
