# PowerPoint comparison request (unanswered)

Run once at the final gate, on the Office oracle machine, through `tools/office-oracle/office-oracle.sh run`. Nothing here has been answered.

For each file below:

1. Open it in PowerPoint (record whether a repair or compatibility prompt appears) and export it to PDF (`--pdf`): page count and per-page text.
2. Save it as `.pptx` from PowerPoint (PowerPoint's own conversion).
3. Convert the same file with `tools/PptProbe convert` and open the result in PowerPoint (repair prompt?) and export it to PDF.
4. Compare per slide: the text, the slide count, the placeholder types, the layout names PowerPoint generated against the layouts written here
   (the open questions in `docs/ppt.md`: gradient angle, ribbon types, layout modelling, indent origin, table borders).

Files (sha256 of the committed bytes):

```
dc149488a4ed5d0c340c5e6dae4587263ff0a428b2a53cb3452277f956d1db84  lo-drawingml-drawing-slide-shapes.ppt
224dd385e19e3f28ae31f81643d1676f1e3d017712898a324b8fce190773cc4e  lo-hyperlink-target-template.ppt
3a619107a512b68c24c8593fb96b9c3a651034780ed4789806adbc89ad8ee1c5  lo-placeholders-template.ppt
30b54af8f8097a949490beba992b505fca23ec52a26e27cf7b8b2f0b14ffb048  lo-slide-alignment-spacing.ppt
85fd03cd5b37a07d1521f8d5296e9ca5df544c58b39518915763f554f2791b28  lo-slide-anchors-wrap.ppt
4c036771973edbebad76fc42a568f24f550d34810b2821aee2e7536c0581efb1  lo-slide-autofit.ppt
57b07b4f46ca65ae2ebf90cde30117b0bc1be4b53a6b1503697be7bfabfff5ab  lo-slide-backgrounds.ppt
4039b5a593418598179fcf10a530f6e91a9d913695a28f0bfca0265c2b965147  lo-slide-bullets.ppt
dca96d27c8fa450de9321dd9f550507896276ffe000f2b5b432e8f9f00645969  lo-slide-inherit-position.ppt
ce3e65500d4540b916985702c66114ccf822d923dc6cb4d476e885876055dec4  lo-slide-per-item-with-notes-template.ppt
b4a7a1e3e6f874cf4b0c76bcffd698e81e60fb38280eae5676d7c5a0c518e83d  lo-slide-pictures-charts-diagrams.ppt
bf99b8cd911d1604e2c7d5188345a8fcd53e470e42469181147d1a1a7009e975  lo-slide-runs.ppt
796164d1319236a8c862629417c4d71a6146f20baf2303a5a5c16dbb07fabdeb  lo-slide-shapes-groups.ppt
2b3486c8cb3c8dff599e0b3c7a6900c7e1f4fca4b23f4747719e5c80bc4fb3ed  lo-slide-slides-hidden-notes.ppt
e9114c1fdfe85bf93a43b79854cc842a309d28dfbf7ad2069be48523b2ffba1b  lo-slide-spacing-checks.ppt
640478dfae8bbdd6a40059007cfc8ba6877bb4f86d2202b76e8f5d657d79d42d  lo-slide-table.ppt
f60e9113d780b9b20106a90ffc10945009d5b2e7b5c0e94cf25231aff8683ec1  lo-slide-vertical-and-columns.ppt
7e3c271cacfdaf5fe15b836bcb2e07f8d33734c27248575e2fec32c7b72458ef  lo-table-rows-template.ppt
```
