# SmartArt

How OneDocument paints SmartArt diagrams in Word, PowerPoint and Excel drawings. Namespace `RecursiveAI.OneDocument.Rendering.SmartArt`
(`src/OneDocument.Rendering/SmartArt/`). A diagram is a graphic frame (`dgm:relIds`) that points at four parts, plus an optional fifth
that Office stores as a cache:

| Part | Root | Relationship | What it holds |
|---|---|---|---|
| data model | `dgm:dataModel` | `…/diagramData` (`r:dm`) | the text tree: points (nodes, transitions) and parent-of connections |
| layout definition | `dgm:layoutDef` | `…/diagramLayout` (`r:lo`) | the algorithms, constraints and rules that place the nodes |
| quick style | `dgm:styleDef` | `…/diagramQuickStyle` (`r:qs`) | per style label: line, fill, effect and font references |
| colour transform | `dgm:colorsDef` | `…/diagramColors` (`r:cs`) | per style label: the colour lists |
| stored drawing | `dsp:drawing` | `…/diagramDrawing` (the data model's `dsp:dataModelExt/@relId`) | the shapes Office laid out, as ordinary DrawingML |

ECMA-376 Part 1 §21.4 defines the first four; the stored drawing is Microsoft's extension ([MS-ODRAWXML] §2.3.1, `dsp:drawing`,
`dsp:sp`, `dsp:txXfrm`, `dsp:dataModelExt`). Nothing is built in: layout, colours and style are always read from the file's own parts.

## Which path paints

`DiagramRegeneration` (`DiagramOptions.Regeneration`):

- `WhenMissingOrStale` (default): paint the stored drawing. Run the layout instead when the diagram has no readable stored drawing
  (`missing`), or when the text of a node in the data model does not appear in any shape of the stored drawing (`stale`: the data
  was edited without Office refreshing the drawing). A stale or missing drawing whose layout cannot be read paints the stale
  drawing if there is one, else nothing, and says so.
- `Always`: run the layout and ignore the stored drawing (`forced`). This is the path after an edit through OneDocument.
- `Never`: only the stored drawing; nothing is painted without one (`regeneration-off`).

Both paths end in the same code: the layout engine writes its shapes as a `dsp:drawing` part in memory (`DiagramDrawingWriter`), and
`DrawingPainter.DrawShapes` paints that exactly as it paints a stored one (or a slide's `p:spTree`). A stored drawing and a
regenerated drawing with the same shapes therefore give the same display list. `DiagramOptions.Report` receives a `DiagramReport`
per frame: source (`Cached`, `Regenerated`, `None`), shape count and the reason.

Notes on `DrawingContext.Notes` (the file is fine, the renderer approximated): `smartart.data-unreadable`, `smartart.layout-unreadable`,
`smartart.not-painted`, `smartart.layout-truncated`, `drawing.diagram-not-painted`, and `drawing.graphic-frame` when no painter is
installed.

## Hooking a host

The drawing painter calls `DrawingContext.DiagramPainter` for every `dgm:relIds` frame it meets, in a Word drawing (`w:drawing` →
`a:graphic`), a slide or worksheet `graphicFrame`, or a group. A host installs one function:

```csharp
ctx.DiagramPainter = DiagramHost.CreatePainter(package, sourcePartName, options);
```

`sourcePartName` is the part that holds the frame (the relationship ids of `dgm:relIds` and of the data model's `relId` are relative
to it). The Word renderer installs it for the document's main part (`LayoutPainter` through `PaintOptions.Diagrams`); the slide and
worksheet renderers install it for the slide or drawing part they paint. Frames in headers, footers and notes use the part they sit
in, which the Word renderer does not do yet.

The frame's box comes from the caller (the frame's `xfrm` in a slide, the layout's box in Word). The stored drawing's coordinates are
relative to the frame's top-left corner and are not scaled; a regenerated drawing is laid out for the box's size.

Tagging: a frame is a `Figure` tag with the frame's alternative text (`wp:docPr/@descr`, `p:cNvPr/@descr`); the Word path gets its
`Figure` from `DrawingPainter.Draw`, the others from the diagram painter (`DiagramPaintRequest.InFigure`). The text of every shape
is tagged as paragraphs inside it, in drawing order (which is the data model's reading order).

Other entry points on `DiagramHost`: `Find(package)` lists the frames of a package with their stated size; `Generate(...)` runs the
layout without painting and returns the shapes (`DiagramShape`: geometry, rectangle in points, rotation, text, font size, style
label), the `dsp:drawing` bytes and an `Outline` of the layout nodes; `ReadStored(...)` reads the stored drawing's shapes.

## The data model

`DiagramData` reads points and connections (§21.4.3). The tree is the `parOf` connections ordered by `srcOrd`; each connection owns a
parent transition and a sibling transition (`parTransId`, `sibTransId`). Presentation points (`pres`) and `presOf`/`presParOf`
connections are what a layout run produces; they are read only for their user formatting (below) and are never written. Limits: 20,000
points, 5,000 layout nodes, depth 64; a larger diagram is refused as unreadable or truncated with a note.

The axes of §21.4.2 (`self`, `ch`, `des`, `desOrSelf`, `par`, `ancst`, `ancstOrSelf`, `followSib`, `precedSib`, `follow`, `preced`,
`root`) walk this tree. The sequence of a node's children is, for each connection, its parent transition, the node and the
sibling transition that leads to the next one; `ptType` filters it, `st`/`step`/`cnt` select from it, and `hideLastTrans` (default
true) drops the sibling transition after the last child. Iterator attributes are lists (`axis="ch desOrSelf"` applies the axes in
turn). `choose`/`if` evaluate `cnt`, `pos`, `revPos`, `posEven`, `posOdd`, `depth`, `maxDepth` and `var` (`dir`, `orgChart`, `chMax`,
`chPref`, `bulEnabled`, `hierBranch`, `animOne`, `animLvl`, `resizeHandles`); a variable takes the data model's root point value
(`presLayoutVars`, which stores the user's choices, for instance `dir="rev"`), else the nearest layout node's `varLst`, else the default.
`forEach/@ref` makes a layout recurse; recursion is bounded by the data and the node cap.

## Layout

Each layout node is made for one point. A layout node's `constrLst` sets properties of itself, its children or its descendants
(`for`, `forName`, `ptType`), to a literal (`val`; lengths are millimetres, font sizes points) or to a property of a referenced node
times `fact`. `op="equ"` makes the targets share one value; `gte`/`lte` clamp. A constraint with neither value nor reference only
names a property that a rule would grow, and states no size.

An algorithm places a node's children inside its rectangle. When the children's preferred sizes do not fit, they and the spacing
shrink together by one factor (never above 1):

| Algorithm | Behaviour here |
|---|---|
| `lin` | a row or column (`linDir`), in order or reversed; group aligned by `horzAlign`/`vertAlign`, each child across the line by `nodeVertAlign`/`nodeHorzAlign`; spacing from spacer/connector children, else the node's `sp` |
| `snake` | rows (or columns, `flowDir`) that wrap; the row length that keeps the children largest, ties to more per row; each short row centred; `grDir` mirrors, `contDir="revDir"` reverses alternate rows |
| `cycle` | children on a circle or arc (`stAng`, `spanAng` degrees clockwise, first at the top), scaled down until the gap between neighbours can hold an arrow (45 % of the smaller node side); `rotPath="alongPath"` rotates the nodes |
| `pyra` | levels from the apex: a triangle, then trapezoids that continue its sides, each level `height/n` tall (`linDir="fromB"` reverses) |
| `composite` | every child by its own `l`, `t`, `w`, `h`, `r`, `b`, `ctrX`, `ctrY` or `diam` |
| `hierRoot`, `hierChild` | a root centred over its sub-trees; sub-trees side by side with `sibSp` (else a quarter of the root width) between them and `sp` (else half the root height) below the root; scaled to the frame |
| `conn` | a connector between the layout nodes that present the two ends of its transition: `dim` 2D is a block arrow (a `rightArrow` in the node's box, or between the two nodes when the algorithm gave it none), 1D a line routed `stra`, `bend` or `curve` with `begPts`/`endPts` sides and `begSty`/`endSty` arrowheads |
| `sp` | space only |
| `tx` | the node's text |

A sibling transition after the last node leads back to the first (a cycle closes) when `hideLastTrans="0"`.

Not done: `rotPath`/`ctrShpMap` beyond the above, `autoTxRot`, the secondary-child parameters of the hierarchy algorithms, `chMax`/`chPref`
(they do not limit the children), accent-text positions of `pyra`, `moveWith`, `chOrder`, 3-D, and the `custScale…`/`custLinFact…`
customisations stored on presentation points. Rules (`ruleLst`) are read, but only `primFontSz` acts (as the font-size floor); the
growth rules (`type="h"`/`"w"` with `val="INF"`) are not run, so layouts that rely on text growing a box (several built-in list
layouts) come out with other proportions than Office draws.

## Text

Shape text is DrawingML, so it does not go through the Word paragraph formatter. `DiagramText` formats runs (size, weight, style,
colour, font), paragraphs (alignment, margins, hanging bullet, line spacing, space before and after) and wraps at spaces and between
East Asian characters; a word wider than the line is split inside and reported to the fit. It paints through `ShapeTextRequest`, so the
text rectangle is the shape's (or `dsp:txXfrm` when present) less its insets, anchored by `anchor`; text taller than the rectangle
overflows evenly. Run colour is the run's own, else the shape style's `fontRef` colour. Right-to-left text is shaped left to right
for now.

Font fitting: the layout's `primFontSz` constraint is the largest size (default 18 pt when absent) and the rule `primFontSz val` the
smallest (default 5 pt). The size is the largest whole point at which every paragraph fits the text rectangle (the preset's text
rectangle less `lMarg`/`tMarg`/`rMarg`/`bMarg`, where margins that reference `primFontSz` scale with the candidate size), and nodes
bound by one `op="equ"` constraint take the smallest size any of them needs. A node with child text (`presOf` `desOrSelf` with bullets
enabled) shows its own text at the primary size and its descendants' at 80 % of it, one level in, with a hanging bullet. Text
paragraphs are written as Office writes them: 90 % line spacing, 35 % space after, `kern` 1200.

## Colours and style

The style label of a layout node (`styleLbl`; `node1` for text shapes, `sibTrans2D1` for block arrows, `parChTrans1D1` for lines when
the node names none) selects the entry of the colour transform and of the quick style. The i-th of n shapes with one label takes the
i-th of n colours: `repeat` and `cycle` walk the list round and round, `span` blends neighbours in HSL along `hueDir` (the standard
gives no formula for the blend). A presentation point stored in the data model with the same `presAssocID` and `presName` overrides
the label, the colour index and count, and the shape's fill and line. The quick style supplies the `lnRef`, `fillRef`,
`effectRef` and `fontRef` of `dsp:style`; fills and lines are also written explicitly, as Office does.

## Differences from Office

- Office's algorithms are specified only in outline. Row length in `snake`, ring scale in `cycle`, hierarchy spacing, the `span` blend and
  the text margins' unit (points) are this engine's reading of the standard; the layout differential decides them.
- Rules other than the font floor do not act (above), so the proportions of layouts that depend on them differ.
- Measured against the drawings Office stored with 29 diagrams in public sample files (built-in layouts such as list, process, hierarchy and
  organisation chart; `tools/SmartArtProbe compare`): the engine makes a shape for 123 of their 135 text shapes, reproduces the stored
  drawing of the block-list layout almost exactly, and puts 2 of 27 frames within 5 % of Office's positions and 0.8 to 1.25 times its
  sizes. The built-in layouts lean on growth rules and on constructs this engine does not run (assistants, secondary children,
  `INF` rules), so regeneration of those is a fallback for missing or stale drawings, not a substitute for Office's own layout.
- A diagram painted in a header, footer or note reads its parts through the main part's relationships.
- Right-to-left and vertical text in diagram shapes is laid out left to right and horizontal.

## Checks

`tests/OneDocument.Rendering.Tests/SmartArtTests.cs` builds diagrams with `SmartArtSamples.cs` (data models, a layout definition per
family, a colour transform and a quick style, all written for these tests) and checks, for list, process, cycle, hierarchy, pyramid
and composite layouts: shape counts, reading order, arrangement (positions, equal sizes, aspect, centring, ring radius, parents over
children), fit inside the frame, text fitting (one size for all nodes, the largest that fits), the `dir` variable, the stale test, the
three regeneration modes, damaged parts, slide and Word hooks, and that a stored drawing paints the same pixels as the same plain
shapes. `testdata/smartart` holds the generated documents (Word and PowerPoint, each with and without a stored drawing) as inputs for the
layout differential (`tools/layout-differential.sh`); they have not been compared with Office yet. `tools/SmartArtProbe` finds every
diagram under a set of directories and paints each as stored and with the layout forced (`sweep`), compares the engine's shapes with
the stored ones (`compare`) and prints both (`dump`). `fuzz/OneDocument.Fuzz/SmartArtTarget.cs` is the fuzz target.
