# PowerPoint comparison request (unanswered)

Run once at the final gate, on the Office oracle machine, through `tools/office-oracle/office-oracle.sh run`. Nothing here has been answered.

For each file below:

1. Open it in PowerPoint (record whether a repair or compatibility prompt appears) and export it to PDF (`--pdf`): page count and per-page text.
2. Save it as `.pptx` from PowerPoint (PowerPoint's own ODP import).
3. Convert the same file with `onedocument convert <file>.odp <file>.pptx` and open the result in PowerPoint (repair prompt?) and export it to PDF.
4. Write the source deck of each file (`README.md`) as `.odp` with `onedocument convert <deck>.pptx <deck>.odp`, open that in PowerPoint (prompt?),
   export to PDF and compare the page count and the text of every slide with the deck's own export.
5. Compare per slide: the text, the slide count, the placeholder types, the layout names PowerPoint generated.

Files (sha256 of the committed bytes):

```
6b0db0396085742c2eb2d7bacdf938b1fbd50a26f11f5afb92ae244a12e68422  lo-drawingml-drawing-slide-shapes.odp
74fcadc849ac5698eb821a940da90cce1d32d9d970228acab7f34485c6c68009  lo-hyperlink-target-template.odp
626beb428fd7adc7c0769aec184e948dbdb9397f93c9e5b7dba0e73ad0522881  lo-placeholders-template.odp
f1261bd91086904a9af6483f8bb84a455861d36c5952c8515d37a7637f0cb30d  lo-slide-alignment-spacing.odp
aabbee3ec6e78634cdd96d320c39955b5c8e00d64761b655e76fb106dcf292a8  lo-slide-anchors-wrap.odp
8748f4c4547e17a4d3e3f5e0f8f5fd5055372d7d3050954d7de62ff6898d4f8b  lo-slide-autofit.odp
1095d39ac700a5698784606870f70950cce708a1792410ed9a85fc9847e54126  lo-slide-backgrounds.odp
daecdaa99a308fb131ce1ffde5c6653295c892822ed577a35dbec26389f20795  lo-slide-bullets.odp
8067a1dc59c7ecba182e43b26e0b565011f121996017b32b6cc48a701c16a8a8  lo-slide-inherit-position.odp
0e3306ea0efd4bc34e6da80a14eee6194700d77c1ee5d333f77e8bd35c8ce0c9  lo-slide-per-item-with-notes-template.odp
a4f2066402f2cb8184eccd080b82eea590f2d182a218ebbd9b0d3edd2a7ac416  lo-slide-pictures-charts-diagrams.odp
9e3b88ece213bf8fd1641a0650fe55e4e3b169752d5a358d0d0fa3640f465063  lo-slide-runs.odp
53f31f538bdb7b41b465ed65d91bb472bb6c5f3893710998cca492e9d9089a97  lo-slide-shapes-groups.odp
fe22dba14adb25c0085299dcfa8af3c1d9b1960f0b34e1ded25edf243b957e17  lo-slide-slides-hidden-notes.odp
cfdb6776e1886eaf6a69c90b78b4c609877d6b9ae550b6f3de4739f4f600472a  lo-slide-spacing-checks.odp
1a9a9fdd116519217ae601c35590423542fe9eecb54f9096f622ceb97820c8a2  lo-slide-table.odp
11cda6a0dbca934b9780785eeb09aea15bbb364d23cdb6c5f0b4177dae09e65a  lo-slide-vertical-and-columns.odp
35691827a84aa7e55f9f8dd9d01f791b10c3a5d7e7b96f2d51fd8bf1cd424df4  lo-table-rows-template.odp
```

## Office run (Word, Excel and PowerPoint 16.0.20527)

Answered as far as the oracle can drive the applications: see the row for this request in [`docs/office-oracle-1.0.md`](../../docs/office-oracle-1.0.md#the-other-requests). Questions not listed there as run are still open.
