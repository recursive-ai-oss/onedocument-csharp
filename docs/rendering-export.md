# Page export: images and SVG

`PageExporter` writes the pages of a document, workbook or presentation as PNG, JPEG, TIFF, WebP or SVG. Pages are laid out once (the layout of each kind is described in `layout.md`, `spreadsheet-rendering.md` and `presentation-rendering.md`), painted into a OneRender display list, and then either drawn by OneRender's image backend and encoded by OneImage, or written as SVG by `RecursiveAI.OneDocument.Rendering.Svg.SvgWriter`.

## One-liners

```csharp
document.SaveAs("page.webp");               // the first page; the format comes from the extension
document.SaveAs("all-pages.tif");           // a TIFF holds every page
Office.Convert("deck.pptx", "slide1.svg");  // the first slide
```

The registered writers take the first page, and every page for a TIFF. Extensions: `.png`, `.jpg`/`.jpeg`, `.tif`/`.tiff`, `.webp`, `.svg`.

## Choosing pages, size and encoder settings

```csharp
var options = new PageExportOptions
{
    Pages = PageSelection.Parse("2-4,9"),   // 1-based; "all", "7-", "-3" also parse
    WidthPixels = 1600,                      // or Dpi = 200; both Width and Height fit the page inside the box
    Quality = 85,                            // JPEG and lossy WebP
};

PageExporter.Export(document, stream, PageImageFormat.Tiff, options);                    // one multi-page TIFF
PageExporter.ExportPages(document, PageImageFormat.Png, page => Save(page.Number, page.Data), options);   // one file per page
```

| Setting | Default |
|---|---|
| `Pages` | all pages for `ExportPages` and TIFF; the first page for a single-page format written to one stream |
| `Dpi` | 144 for raster formats; 96 (CSS pixels) for the `width`/`height` of an SVG, whose `viewBox` is always in CSS pixels |
| `WidthPixels`, `HeightPixels` | none; the height follows the page's proportions |
| `Quality` | 90 |
| `WebPLossless` | true: pages are text and flat colour, which lossy coding smears |
| `TiffCompression` | Deflate |
| `TransparentBackground` | false (white paper); JPEG is always white |
| `SvgGlyphMode`, `SvgTags` | `Auto`, true |

Limits: a page is at most 100 million pixels (`PageExporter.MaxPixels`), dpi 1 to 2400, quality 1 to 100. A range ends at the last page when it runs past it (`1-99` is every page of a short document); a range that starts past the last page throws `ArgumentOutOfRangeException`; a single-page format given several pages in one stream throws `ArgumentException` (use `ExportPages`). A page that cannot be drawn or encoded throws `OfficeFormatException` with code `render.failed`. The report lists font substitutions and layout and SVG notes as `code: message` lines. JPEG uses 4:4:4 sampling because 4:2:0 fringes glyph edges.

A workbook with nothing to print exports one blank Letter page. A presentation exports its visible slides in order (the pages `PresentationRenderer` writes to PDF).

### Multi-page TIFF

OneImage encodes one image per call, so each page is encoded as a TIFF of its own and the files are joined by chaining their directories (`TiffPages`): the bytes after the header are copied unchanged and the offsets inside them are moved. Only the plain tags OneImage's encoder writes are accepted; anything holding further offsets (sub-directories, JPEG-in-TIFF) raises `render.failed` instead of writing a corrupt file. A page range gives a TIFF of just those pages. The decoded pixels of each joined page are tested equal to the single-page file for all four compressions.

## SVG

`SvgWriter` turns a finished `DisplayList` into an SVG 1.1 document (`xlink:href`, plus the CSS `mix-blend-mode`, `isolation` and `mask-type` properties browsers add). The namespace depends only on OneRender's display-list types and OneFont's face type, so OneRender can lift it. Output is deterministic: ids come from a counter, numbers are invariant-culture with at most `Precision` (3) decimals, and nothing reads a clock or a random source.

| Display list | SVG |
|---|---|
| fills (rect, rounded rect, path; fill rules) | `rect`, `path` |
| solid and linear/radial gradients | `linearGradient`, `radialGradient` in user space; repeating gradients cut to their span with `spreadMethod="repeat"` |
| clips (rect, rounded, path) | `clipPath` and a `g` |
| transforms, groups (opacity, blend modes, isolation) | `g transform`, `g opacity style` |
| filters (blur, brightness, contrast, grayscale, hue-rotate, invert, opacity, saturate, sepia, drop shadow) | one `filter` of primitives in sRGB |
| masks of one layer (alpha or luminance) | `mask` |
| images (source rectangle, smooth or pixelated, tiled) | a `defs` image per picture (PNG data URI), a nested `svg` with a `viewBox` for the source rectangle, a `pattern` for tiles |
| borders: solid, double, groove, ridge, inset, outset, dashed, dotted | polygons per side, dashes as stroked lines |
| outer box shadows | blurred path clipped to the outside of the box |
| inline SVG pictures | nested `svg` with the colour as `color` |
| glyph runs | see below |
| tags | `role`, `aria-level`, `aria-label`, `title`, `xml:lang`; links as `a xlink:href`; artifacts as `aria-hidden` |

**What SVG cannot express** (conic gradients, rounded borders, inset shadows, masks of several layers or with subtract/intersect/exclude, 3D transforms) is drawn by the `ISvgRasterizer` the caller gives and embedded as an image where it belongs, at the resolution `RasterScale` (2) times the enclosing transforms' scale, at most 16 million pixels per region. `PageExporter` supplies one that uses OneRender's image backend. Without a rasterizer the content is written without the effect. Backdrop filters are left out. Each case is a note: `svg.conic-gradient`, `svg.rounded-border`, `svg.inset-shadow`, `svg.mask-layers`, `svg.transform-3d`, `svg.backdrop-filter`, `svg.image-missing`, `svg.inline-svg-ids`, `svg.outlines-refused`, `svg.text-approximated`.

### Glyph runs

`SvgGlyphMode.Auto` (default) writes `<text>` where the SVG text model reproduces the run exactly: one glyph per BMP character, left to right, each character at its own x. That keeps text searchable and small. Ligatures, marks, right-to-left and joined scripts are written as glyph outlines (one `path` per run), because a viewer shapes text its own way. `Text` always writes text (clusters that are not one glyph per character become a `tspan` per cluster, noted as `svg.text-approximated`); `Outlines` always writes outlines. Characters a face does not have draw nothing, as in the image and PDF backends. Colour bitmap fonts (`CBDT`, `sbix`) are always text.

**Font licences.** No font is embedded. Outlines are glyph shapes, so they are refused for a font whose OS/2 `fsType` has the restricted-licence bit (0x0002) set: the run is written as text and the note `svg.outlines-refused` says so. The substituted open fonts (Liberation, Carlito, Caladea, Noto) are SIL OFL and have no such bit.

## Checks

- **Round trip against the image backend** (`PageExporterTests.SvgDrawnBackMatchesTheRasterOfTheSamePage`): every page of the rendering test documents is exported as PNG at 1200 pixels wide and as SVG with outlines, the SVG is drawn back by OneImage.Svg, and the two are compared after averaging 4 x 4 blocks (two rasterizers cover glyph edges differently; averaging keeps misplaced lines, glyphs and shapes and drops the anti-aliasing noise). Tolerance: mean absolute difference per channel at most 1.0 of 255, and at most 1.5% of blocks differing by more than 32. Over 202 pages (151 word-processing, 29 workbook, 13 slide, and the drawing fixtures) the mean of the means is 0.04 and the largest 0.85. The one page near the limit is `drawing-docx/fills` (0.85, 1.07% over): its 24 pattern fills are tiles a few pixels wide, and the two rasterizers sample them in a different phase (the gradients and solid fills in the same page agree).
- **A browser** (information): the same fixture (`drawing-effects-groups`) opened in headless Chrome for Testing 157 draws it; against our PNG at 96 dpi the mean difference is 0.30 with outlines and 0.65 with text (Chrome used its own substitute font).
- AOT and browser-WASM smoke each write one page as SVG and one as WebP (`smoke/shared/RenderSmoke.cs`).

## Command line

```
onedocument render in.docx -o out.png|jpg|tif|webp|svg [--pages 1-3,5|all] [--dpi n] [--width px] [--height px]
                 [--quality n] [--svg-glyphs auto|text|outlines] [--transparent] [--no-tags]
```

A TIFF holds every page asked for (all by default). Other formats write page 1, or the one page `--pages`/`--page` names; several pages give `out-1.png`, `out-2.png`, and so on. `convert` writes the first page (every page for TIFF).
