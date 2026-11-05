# Content controls (structured document tags)

`Document.ContentControls()` enumerates every `w:sdt` of the main part and of its headers, footers, footnotes and endnotes in document order (a control before the controls nested in it). `BlockContentControl.Control` and `InlineContentControl.Control` reach the same model from the block and inline hub objects. A `ContentControl` is a thin wrapper over the element: reads go to the markup tree, edits change only the control's own markup, and everything unknown (extension elements, `mc:` markup, attributes, prefixes, order) stays.

## Read

| Member | Source |
|---|---|
| `Level` | Block, Inline, Row (`w:sdt` in `w:tbl`), Cell (`w:sdt` in `w:tr`). A control nested in another takes its place from the wrappers above it. |
| `Type` | The one type element of `w:sdtPr`: `richText`, `text`, `picture`, `date`, `dropDownList`, `comboBox`, `group`, `citation`, `bibliography`, `equation`, `docPartObj`, `docPartList`; `w14:checkbox`; `w15:repeatingSection`, `w15:repeatingSectionItem` (these two win when they sit beside another type). No type element reads as rich text, and `IsTypeExplicit` says so. |
| `Tag`, `Alias`, `Id`, `Lock`, `IsTemporary`, `IsShowingPlaceholder`, `PlaceholderDocPart` | `w:tag`, `w:alias`, `w:id`, `w:lock`, `w:temporary`, `w:showingPlcHdr`, `w:placeholder/w:docPart`. |
| `ListItems`, `SelectedIndex`, `LastValue` | `w:listItem` (display text defaults to value and the reverse); the selection is the item whose display text equals the content, else the one whose value is `w:lastValue`. |
| `IsChecked` | `w14:checked` (a check box without it is unchecked). |
| `Date`, `DateFormat` | `w:date/@w:fullDate`, `w:dateFormat`. |
| `Text` | `w:t`, `w:tab` (`\t`), `w:br`/`w:cr` (`\n`), and `\n` between paragraphs. Deleted text, drawings, field instructions and `mc:Fallback` are not text. |
| `Children()`, `Parent` | Controls nested directly in, or around, this one. A repeating section's children are its items. |
| `Binding`, `EvaluateBinding()` | `w:dataBinding`; see below. |

## Edit

- `SetText(text)` replaces the content. Inline: one run with the first run's properties, `w:tab` for `\t`, `w:br` for `\n` (rich text and multi-line plain text; a single-line plain text control turns `\n` into a space). Block: one paragraph per line, each with the first paragraph's properties. A showing placeholder is cleared (`w:showingPlcHdr` removed, the placeholder run style not copied). A drop-down list only takes one of its items, by display text or value. A character XML cannot carry throws `ArgumentException` before anything is changed.
- `SelectItem(index)` sets the content to the item's display text and `w:lastValue` to its value.
- `SetChecked(bool)` sets `w14:checked` and the glyph (`w14:checkedState`/`w14:uncheckedState`, ☒ and ☐ by default) in the first `w:t`, so the run's symbol font stays.
- `RemoveKeepingContent()` replaces the control with the children of its `w:sdtContent` (blocks, runs, rows or cells). `RemoveWithContent()` removes both.
- Row-level and cell-level controls, check boxes, pictures, groups and repeating sections have no text; `SetText` throws `InvalidOperationException` for them.
- Editing does not push data into the bound custom XML part, and a bound control's content is not refreshed from it. Applying bindings is the template engine's job.

## Data bindings and the XPath subset

`CustomXmlParts` lists the parts the main part relates to with `customXml`, with their store item ids (`ds:itemID` of the related `customXmlProps` part). A binding names its store by `w:storeItemID`, matched case-insensitively. The id `{6C3C8BC8-F283-45AE-878A-BAB7291924A1}` is the package's core properties (`/docProps/core.xml`, found by the package relationship); Word-written files use it for title, subject and similar controls.

`EvaluateBinding()` reads the current value of the bound node. It never changes anything, and no script runs. The path is evaluated by an XPath 1.0 subset (`XPathSubset`):

- absolute and relative location paths of child steps (`/ns0:root[1]/ns0:name[1]`), `//` descendant steps, `.`, `..`, `*`, `@attr` and `text()`;
- predicates: 1-based position `[n]`, `last()`, comparisons (`=`, `!=`, `<`, `>`, `<=`, `>=`) between paths, strings and numbers, `and`, `or`, `not()`, `name()`, `local-name()`, `contains()`, `starts-with()`;
- prefixes come from `w:prefixMappings` (`xmlns:ns0='uri' …`); an unprefixed name matches no-namespace elements, as in XPath 1.0.

The value is the element's text content, the attribute's value, or the element's own text nodes for `text()`. Results (`BindingStatus`): `Resolved`, `NoMatch`, `StoreNotFound`, `UnsupportedPath` (other axes, functions and operators outside the list, and anything that is not a node set at the top), `Invalid` (no path, malformed, a prefix the mappings do not declare).

## Binding data

`Document.BindContentControls(data)` writes data into the custom XML parts and shows it in the controls; `RefreshContentControls()` re-reads the parts into the controls; `PushContentControls()` is the reverse (controls into parts). `data` is anything the template engine reads, or XML given as a string or bytes. Each control's `w:dataBinding` path is read as a path into the data: the root element's name is not matched, names match ignoring case, `[n]` takes the n-th item of a list. A list in the data may stand for the container of the repeated element (JSON `lines: [...]` for `lines/line`). The value is written into the bound node (created when missing) and the control shows it. Controls with no value in the data keep their content, and nothing outside the custom XML parts and the controls changes.

| Control | Shown value | Written back |
|---|---|---|
| plain text, rich text | the text (a rich control keeps its first run's formatting) | the text |
| date | the value formatted with `w:dateFormat` | the full date, `yyyy-MM-ddTHH:mm:ssZ` |
| check box (w14) | the check state; `1`, `true` and `on` are checked | `1`/`0` (`true`/`false` when the node used those) |
| drop-down, combo box | the list item whose value (or text) matches; a combo keeps a value not in its list | the item's value |
| attribute binding (`@attr`) | the attribute | the attribute |

A repeating section (`w15:repeatingSection`) bound to a repeating node takes the first item as the pattern and is copied or trimmed to the number of nodes in the list, with the paths inside re-indexed (`[1]` becomes `[n]`); nested repeating sections work. A placeholder-showing control writes nothing on push.

## Limits and defects

Enumeration, text reading and `Children()` walk the tree iteratively, so nesting depth is bounded only by the XML depth limit. Reading repairs nothing. Binding reports `binding.` defects in `ContentBindingReport.Warnings` (`binding.store-not-found`, `binding.unsupported-path`, `binding.no-data`, `binding.unsupported-control`, `binding.value-not-in-list`, `binding.no-template-item`; see `defects.md`). Unreadable custom XML parts are reported by the part cache as `docx.part-unreadable` and the bindings to them read as `StoreNotFound`.
