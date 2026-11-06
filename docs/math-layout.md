# Equations (Office Math) in layout

Office Math (OMML, ECMA-376 Part 1 §22.1) is read into a typed tree, laid out with a TeX-style box layout driven by the math font's OpenType `MATH` table, and painted with the paragraph it sits in. The tree is a view over the markup (nothing is rewritten); the layout is a public, queryable box.

## Layers

| Layer | Where | What |
|---|---|---|
| Model and reader | `RecursiveAI.OneDocument.Equations` (core) | `MathNode` tree for every object of §22.1.2, `MathProperties`, `MathRunProperties` (`m:rPr` with its `w:rPr` size, colour, font, bold), `MathSettings` (`m:mathPr`). `OmmlReader.Read(MarkupNode)` and a forward reader for streaming text extraction. Unknown elements are skipped with a `math.unknown-element` note; depth is capped at 64 and the node count at 200,000 (`math.depth`, `math.size`). |
| Linear text | `MathLinearizer` | UnicodeMath-style linear form (below). |
| Math font table | `Rendering.Equations.MathTable` | Parser for the `MATH` table: constants, italic corrections, top-accent attachments, extended shapes, glyph variants and assemblies. Bounds-checked: a damaged table is refused, never thrown on. A font without one gets fixed ratios of the em and the note `math.no-math-table`. |
| Box layout | `MathLayouter.Layout(MathNode, MathLayoutOptions)` | Returns a `MathBox`: width, ascent and descent in points about the first baseline (y down), the glyphs, rules and strokes, the linear text, the line count and notes. |
| Paragraph hook | `Layout/ParagraphMath.cs`, `ParagraphFormatter` | An equation is one unbreakable atom whose text is its linear form. |
| Paint | `LayoutPainter.PaintMath` | Glyph runs, filled rules, strokes as filled quadrilaterals, in a `Formula` tag. |

## In a paragraph

- `m:oMath` inside a paragraph (also inside hyperlinks, content controls and revision wrappers) is **inline**: text style, scripts at the script scale, limits beside n-ary signs. It is placed on the line like a word; a line break is allowed before and after it where the surrounding text allows one, never inside it. Line ascent and descent grow to the box's.
- `m:oMathPara` is **display**: each `m:oMath` in it gets a line of its own, in display style (`m:dispDef` on by default), placed by `m:oMathParaPr/m:jc`, else the document's `m:defJc`, else centred (ECMA-376 §22.1.2.57). `left` and `right` are start and end; `center` and `centerGroup` both centre each equation (a group is laid out as one equation per line). The measure is the text width minus the paragraph indents and `m:lMargin`/`m:rMargin`; the margins do not otherwise move the equation yet.
- A display equation wider than the measure breaks at binary operators and relations: `m:brkBin` (before, after or repeat), `m:wrapIndent` (default 72 pt, at most half the measure), `m:wrapRight`, and manual `m:brk` in a run. `m:brkBinSub` is read but not applied.
- Size, colour and text font come from the paragraph's style (the equation's own `w:rPr` in a math run overrides). Math letters use the math font; `m:nor` text uses the paragraph font.
- The paragraph's `Text` holds the linear text in place of the equation, so offsets, line text and extraction agree. A `LayoutRun` of kind `Math` carries the `MathBox` (`LayoutRun.Math`); its `Text` is the linear form.
- Table cells call the same paragraph code, so equations in tables lay out (their width counts in autofit).

## Linear form (alternative text and text extraction)

UnicodeMath (Unicode Technical Note #28, v3.2) conventions: `(a+b)/c` fractions (parentheses only when the operand is not one atom), `a/b`, `x^2`, `x_i`, `x_k^n`, `∑_(i=1)^n▒i` n-ary, `√(x+1)`, `∛x`, `√(5&x)` with a degree, `(a+b)` delimiters with separators, `■(a&b@c&d)` matrices, `█(x&=1@y&=2)` equation arrays. `Office.ExtractText` returns this form for `m:oMath`/`m:oMathPara` in Word documents and in PowerPoint text bodies (`a14:m`), instead of the loose characters of the math runs. The painter uses it as the `Alt` text of the figure.

## Layout rules used

All values come from the font's `MATH` table (units per em scaled by the size of the level). The rules are TeX's, as the OpenType specification describes the constants; the choices Word leaves unstated are in the last section.

- **Levels:** display, text, script (`ScriptPercentScaleDown` 60 % in Noto Sans Math), script-script (`ScriptScriptPercentScaleDown`, 50 %), then it stops. Numerators and denominators step one level in text style; in display style they stay at text size. Subscripts, denominators and radicands are cramped.
- **Fractions:** rule thickness `FractionRuleThickness`; shifts `FractionNumeratorShiftUp`/`DenominatorShiftDown` (display variants in display style) raised until the gaps `FractionNumeratorGapMin`/`DenominatorGapMin` (display variants) hold, measured from the rule's edges about `AxisHeight`. Skewed: numerator up and denominator down by `SkewedFractionVerticalGap` about a slash; linear: `a/b` as characters; no bar: stacked with the same shifts and no rule.
- **Scripts:** `SuperscriptShiftUp` (cramped variant), `SubscriptShiftDown`, `SuperscriptBottomMin`, `SubscriptTopMax`, `SubSuperscriptGapMin`; the script starts after the base's advance plus its italic correction (superscript) or without it (subscript). Pre-scripts mirror this on the left.
- **Radicals:** the sign is the smallest size variant (else an assembly) that reaches the radicand's height plus the gap (`RadicalVerticalGap`, display variant) and rule thickness (`RadicalRuleThickness`); the rule starts where the sign ends at the top of the sign. A degree is script-script size, kerned by `RadicalKernBeforeDegree`/`RadicalKernAfterDegree` and raised by `RadicalDegreeBottomRaisePercent` of the sign's height.
- **Delimiters:** grow by default (`m:grow`); the target is TeX's rule: twice the larger distance of the content from the axis, times 0.901, or that minus 5 pt per 10 pt em (0.5 em), whichever is taller. The smallest variant at least that tall is chosen; else the vertical assembly (parts bottom to top; extenders repeat; overlaps grow up to what the connectors allow, never below `MinConnectorOverlap`). Separators and empty delimiters (`m:begChr` empty) follow ECMA-376 §22.1.2.31.
- **N-ary:** in display style a large operator (sum, product, union and the like, not integrals unless `m:limLoc` says) takes the largest size variant; this font's `DisplayOperatorMinHeight` (2.3 em) is beyond its largest summation (1.34 em), so the largest variant is used. The operator is centred on the axis. Limits are above/below (`undOvr`) or beside (`subSup`): display default is above/below except integrals, `m:naryLim`/`m:intLim` in the settings change that; inline is always beside. Above/below limits are centred and kept `UpperLimitGapMin`/`LowerLimitGapMin` away.
- **Accents, bars, group characters:** the accent glyph is the font's combining mark (Noto Sans Math has no spacing circumflex or tilde), centred on the base by `TopAccentAttachment` (else the middle), raised only when the base is taller than `AccentBaseHeight`; wide accents use the horizontal variants. Bars are rules (`OverbarVerticalGap`, `UnderbarVerticalGap`, `OverbarRuleThickness`). Group characters (braces, arrows) stretch by horizontal variants or assembly to the base's width and sit below by default, `m:pos` on the group-character object.
- **Matrices and equation arrays:** columns are as wide as the widest cell, gap 1 em plus `m:cGp`/`m:cSp`; rows 1.2 em apart plus `m:rSp`/`m:rSpRule`; the box is centred on the axis. `m:mcJc` aligns cells per column. Equation arrays align at `m:aln` points: the part before an alignment point is right-aligned, the part after it left-aligned, alternating by point.
- **Spacing:** TeX's table between atom classes (ordinary, operator, binary, relation, opening, closing, punctuation), in 18ths of an em: 3 for thin, 4 for medium, 5 for thick; none inside scripts; a binary operator at the start or after another operator or opening delimiter is a sign and takes none. `m:r` literal text (`m:lit`) and `m:nor` take no class spacing.
- **Characters:** Latin letters are math italic (U+1D434…), Greek follows `m:sty`/`m:scr`; `m:sty p` plain, `b` bold, `bi` bold italic; `m:scr` script, fraktur, double-struck, sans-serif, monospace map to the Mathematical Alphanumeric block; digits and `m:nor` text are upright; function names (`m:func`) upright with a thin space before the argument.
- **Baseline:** the first baseline of the box is the baseline of the text it stands in; the box extends `Ascent` above and `Descent` below.

## The math font

`Cambria Math` (Word's default) maps to **Noto Sans Math** (SIL OFL 1.1), shipped through OneRender's `Fonts.Noto.Math` package and loaded by `FontLibrary.CreateDefault` as the family and as a fallback for math characters; the note is a `FontSubstitutionNote` of kind approximate. `STIX Two Math` and `Latin Modern Math` map to it too. On a machine that has Cambria Math installed and registered with the library, its own `MATH` table is used. Metrics differ between the two fonts, so line breaks and box sizes against Word on Windows are a measured difference, not a bug: the layout differential decides how far it goes.

## Notes

`LayoutDocument.Notes` carries equation notes as `layout.math.*`: `layout.math.unknown-element` (an element not in §22.1.2 was skipped), `layout.math.depth`, `layout.math.size` (limits of the reader and the layout), `layout.math.no-math-table` (the font has no `MATH` table; fixed ratios were used), `layout.math.font-missing` (the named math font is not available; the default was used), `layout.math.glyph-missing` (no font has a glyph for a character; the missing-glyph shape is drawn).

## Choices to check against Word

These need Word's own output to settle; the layout differential inputs (`80`–`89` in the generated set) are written for it.

| Point | What is done |
|---|---|
| Inline equation ascent and descent | from the glyph ink of the box; Word may use font ascent and descent |
| Delimiter growth threshold | TeX's 0.901 and 0.5 em shortfall; `DelimitedSubFormulaMinHeight` is not used |
| N-ary growth | operators do not grow with the argument; display operators take the largest variant |
| Matrix row pitch, column gap | 1.2 em and 1 em |
| `centerGroup` against `center` | both centre each equation |
| `m:lMargin`/`m:rMargin` | narrow the measure of a display equation, do not indent it |
| `m:brkBinSub` | read, not applied |
| Display equation in a paragraph with text | Word shows it inline; here `m:oMathPara` is always display |
| Equation size in a paragraph with mixed sizes | the paragraph style's size, not the neighbouring run's |
