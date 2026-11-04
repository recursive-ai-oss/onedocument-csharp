# WordprocessingML styles and numbering

How the document hub resolves formatting and list labels. References are to ECMA-376 Part 1 (5th edition) unless noted.
Where Word's behaviour is not settled by the text, the rule below is what the hub does today and the item is marked
**to check against Word**.

## Resolved properties (§17.7.2)

Layers, lowest first. Each property keeps the layer (and style id) that set it.

| Paragraph | Run |
|---|---|
| document defaults (`w:pPrDefault`) | document defaults (`w:rPrDefault`) |
| table style: whole table, then regions | table style run properties |
| numbering level `w:pPr` (when the *style* supplies the list) | |
| paragraph style chain, base first (default paragraph style when none is named) | paragraph style chain `w:rPr` |
| numbering level `w:pPr` (when the paragraph names the list itself) | character style chain (default character style when none is named) |
| direct `w:pPr` | direct `w:rPr` |

- A property is an element. `spacing`, `ind`, `rFonts` and `lang` merge attribute by attribute; a directly written
  `ascii` replaces an inherited `asciiTheme` (likewise `hAnsi`, `eastAsia`, `cs`), `firstLine` replaces an inherited
  `hanging` and the reverse, `left`/`start` and `right`/`end` are one edge. Everything else is replaced whole.
- Borders (`pBdr`) merge per side, tab stops (`tabs`) per position (a `clear` removes the stop), the list reference (`numPr`) per child.
- **Toggle properties (§17.7.3)** `b bCs i iCs caps smallCaps strike dstrike outline shadow emboss imprint vanish`: direct
  formatting sets the value outright; otherwise the values the table style, paragraph style and character style levels set are
  XORed (inside one level a `basedOn` chain overrides normally); with none, the document default stands. **To check against Word:**
  the XOR runs over the three style levels only, and the document default is not part of it.
- A style chain follows `w:basedOn`. A missing base, a cycle and an unknown style id are defects (`docx.style-*`) and never exceptions.
- Table style regions apply in the order wholeTable, band1Vert, band2Vert, band1Horz, band2Horz, firstCol, lastCol, firstRow,
  lastRow, nwCell, neCell, swCell, seCell (§17.7.6). `w:tblLook` comes from its attributes or its hex mask; absent means no
  region flags. The header row is not banded and banding counts from the first row after it. `w:cnfStyle` caches are ignored.
- Theme fonts and colours resolve from the theme part with the default colour map. A colour's tint and shade are not applied
  (the raw attributes stay reachable) — **needs Word output**.
- **Numbering level versus paragraph style (to check against Word):** when the style supplies `numId` the level's `w:pPr`
  sits below the paragraph style; when the paragraph names the list directly it sits above it.

## Numbering (§17.9)

- A paragraph's list is its direct `numId`, else the nearest style in its chain with one; `numId` 0 turns numbering off. The
  level is the direct `ilvl`, else the style's, else the level whose `w:pStyle` names the paragraph's style, else 0.
- Counters belong to the abstract definition (after `w:numStyleLink`), so instances of one definition continue each other.
  An instance with a `w:startOverride` restarts that level the first time the instance is used. A `w:lvlOverride/w:lvl`
  replaces the level for that instance. LibreOffice's text export agrees on every generated case.
- The first number of a level is the override, else `w:start`, else 0.
- After a paragraph at level *L*, level *j > L* starts again when *L < lvlRestart* (one-based `w:lvlRestart`; default *j*
  zero-based, i.e. after any higher level). `lvlRestart` 0 never restarts. **To check against Word:** LibreOffice ignores
  `lvlRestart` 0 (it restarts); the hub follows the specification.
- Label text fills `%1`..`%9` with the counter of that level in that level's format; legal numbering (`w:isLgl`) shows the
  other levels in decimal. A level that was never used shows its start value.
- Formats produced exactly: `decimal decimalZero upperRoman lowerRoman upperLetter lowerLetter ordinal bullet none
  decimalEnclosedCircle (1-20) decimalFullWidth decimalHalfWidth`. Letters repeat (`z`, `aa`, `bb`). Text forms
  (`cardinalText`, `ordinalText`), `chicago`, `hex` and every script-specific format are produced as a best guess and every
  label that uses one says `NeedsWordOutput`.
- Defects: `docx.num-missing`, `docx.num-abstract-missing`, `docx.num-level-missing`, `docx.num-link-broken`.

## Editing

Style and numbering edits change only `styles.xml`, `numbering.xml` and `document.xml`: a part is rewritten only when its
tree was edited, and missing parts are created with their relationship. New elements use the part's own prefix and respect
the schema's sequence (abstract definitions before numbering instances).
