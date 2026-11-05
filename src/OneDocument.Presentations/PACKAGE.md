# RecursiveAI.OneDocument.Presentations

**Read, write, calculate, template and render Word, Excel and PowerPoint documents (OOXML, legacy binary, RTF, ODF) in pure .NET. Not affiliated with Microsoft.**

This package is the presentation part of OneDocument, an early preview. It opens, edits and saves PPTX, PPTM, POTX, POTM, PPSX, PPSM, PPAM and Flat OPC presentations:

- **Lossless.** Parts you did not edit are written back byte-identical. Edited parts keep unknown elements, attributes, prefixes, `mc:AlternateContent` and `extLst` in their original order; transitions and animations are never touched.
- **Lazy.** Opening reads the package structure; the presentation part, masters, layouts and slides are read when you first use them.
- **The model:** slide masters, layouts and slides with placeholder inheritance (text styles, body properties, bounds, colour map and theme), shapes, groups, pictures, tables with merged cells, charts and diagrams kept as references, text bodies, notes, comments, sections, custom shows and hidden slides.
- **Slide editing:** add a slide from a layout, delete, duplicate and move slides; relationships, content types, notes, sections and custom shows stay consistent.

```csharp
using RecursiveAI.OneDocument.Presentations;

using var deck = Presentation.Open("quarterly.pptx");
Slide second = deck.DuplicateSlide(deck.Slides[0]);
second.Shapes[0].TextBody!.SetText("Quarterly results");
deck.Save();                      // atomic replace; only the edited parts change
string text = deck.ExtractText(); // slides, then notes
```

The `Office` statics (`Office.Open`, `Office.ExtractText`) reach this package as soon as an application references it: the package adds a module initializer to your C# project at build time; set `OneDocumentAutoRegister` to `false` to opt out and call `PresentationFormats.Register(registry)` yourself.

Licensed `MIT OR Apache-2.0`, © Recursive AI, LLC. Not affiliated with Microsoft or with any other product or company named OneDocument.
