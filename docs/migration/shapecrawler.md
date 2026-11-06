# Coming from ShapeCrawler

ShapeCrawler edits PowerPoint files through a presentation object with slides and shapes. OneDocument's `Presentation` is the same idea on a lossless model, with templating and PDF output, and it reads legacy `.ppt`. The names below follow ShapeCrawler's published documentation; its API has changed between releases, so check the release you use. Read [README.md](README.md) first for what is true on every page.

## Open, read, save

```csharp
// ShapeCrawler
var pres = new Presentation(path);
foreach (var slide in pres.Slides)
{
    foreach (var shape in slide.Shapes)
    {
        if (shape.TextFrame is { } tf) { Console.WriteLine(tf.Text); }
    }
}

pres.Save();
```

```csharp
// OneDocument
using Presentation deck = Presentation.Open(path);   // .pptx, .ppsx, .potx and legacy .ppt
foreach (Slide slide in deck.Slides)
{
    foreach (Shape shape in slide.Shapes)
    {
        if (shape.TextBody is { } body) { Console.WriteLine(body.Text); }
    }
}

deck.Save();                                          // atomic replace; only edited parts change
```

## Edit text

```csharp
// ShapeCrawler
shape.TextFrame!.Text = "Quarterly results";
```

```csharp
// OneDocument
shape.TextBody!.SetText("Quarterly results");        // keeps the first run's formatting
shape.TextBody.AddParagraph("Second line");
```

Placeholder inheritance (master, layout, slide) is resolved for you: `paragraph.ResolveFormat()` returns the effective size, bullet and alignment (`docs/presentations.md`).

## Add, duplicate, move, delete slides

```csharp
// ShapeCrawler
pres.Slides.Add(layout);               // add from a layout
pres.Slides[0].Remove();
```

```csharp
// OneDocument
Slide added = deck.AddSlide(deck.Masters[0].Layouts[1]);
Slide copy = deck.DuplicateSlide(deck.Slides[0]);
deck.MoveSlide(copy, 0);
deck.DeleteSlide(deck.Slides[2]);
```

Relationships, content types, notes, sections and custom shows stay consistent. Speaker notes: `slide.SetNotesText("...")`.

## Tables

```csharp
// OneDocument: an existing table shape
foreach (Shape shape in slide.Shapes)
{
    if (shape.Table is { } table) { /* rows, cells, merged cells */ }
}
```

Tables are read and edited; creating a table shape is not a hub call.

## Templates and output

Fill a deck from data, one slide per item, with the same engine as for documents and workbooks:

```csharp
Template.Render(path, report, outPath);              // outPath may be .pptx or .pdf
```

Slides render to PDF and PNG (`deck.SaveAs("deck.pdf")`, `Office.Convert`), with the Rendering package. ShapeCrawler does not render.

## Not supported or different

- No high-level call to add shapes, pictures, charts or connectors to a slide. Existing ones are kept byte-identical and drawn; new content comes from templates (`{% image %}`) or the typed views and markup.
- No animation or transition API: they are preserved and never touched, not rendered.
- No fluent shape-geometry setters (`shape.X = ...`): geometry is in the shape's typed view.
- `.ppt` can be read and converted, not written.
