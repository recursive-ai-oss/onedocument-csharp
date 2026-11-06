# Coming from the Open XML SDK

The Open XML SDK gives you the packages, parts and a typed class per schema element: `WordprocessingDocument`, `SpreadsheetDocument`, `PresentationDocument`, `OpenXmlElement` and its subclasses, `OpenXmlReader` / `OpenXmlWriter` for streaming. OneDocument has two ways to work at that level, and a hub above it. Read [README.md](README.md) first for what is true on every page.

| SDK idea | OneDocument |
|---|---|
| `WordprocessingDocument.Open(path, true)` | `Document.Open(path)` |
| `SpreadsheetDocument.Open(path, true)` | `Workbook.Open(path)` |
| `PresentationDocument.Open(path, true)` | `Presentation.Open(path)` |
| The package, parts, relationships | `doc.Package` (`IOpcPackage`): parts by URI, relationships, content types (`docs/packaging.md`) |
| Typed element classes (`Paragraph`, `Run`, `Worksheet`, ...) | Generated typed views in `RecursiveAI.OneDocument.Schema.*`, reached with `.View` on a hub object |
| `OpenXmlElement` tree | The markup tree (`MarkupNode`, `MarkupRef`): lazy, over the part buffer, lossless (`docs/markup.md`) |
| `OpenXmlReader` / `OpenXmlWriter` | `XlsxStreamReader` / `XlsxStreamWriter` for sheets; the XML tokenizer and writer for anything else |
| `OpenXmlValidator` | not provided; written files are checked against the pinned schemas in the repository's own harness |

## Open, read text, edit, save

```csharp
// Open XML SDK
using var doc = WordprocessingDocument.Open(path, true);
Body body = doc.MainDocumentPart!.Document.Body!;
foreach (Paragraph p in body.Descendants<Paragraph>()) { Console.WriteLine(p.InnerText); }
body.AppendChild(new Paragraph(new Run(new Text("Added"))));
doc.MainDocumentPart.Document.Save();
```

```csharp
// OneDocument
using Document doc = Document.Open(path);
foreach (Paragraph p in doc.Paragraphs) { Console.WriteLine(p.Text); }
doc.Body.AddParagraph("Added");
doc.Save();
```

The hub gives the common reads and edits (paragraphs, runs, tables, styles, numbering, sections, headers and footers, fields, notes, comments, revisions, content controls). When it does not have a call, drop to the typed view of the same element:

```csharp
// OneDocument: a typed view over the same markup, no conversion
var view = doc.Paragraphs.First().View;      // RecursiveAI.OneDocument.Schema.Wordprocessing.Paragraph
```

Differences that matter when moving code:

- **No DOM load.** The markup tree is lazy: a part is parsed when first touched, and a part nobody edited is written back as stored. `Save()` is an atomic replace.
- **No `Dispose` of parts.** The document owns everything; dispose the document.
- **Unknown content is kept** by construction (prefixes, `mc:AlternateContent`, `extLst`, order); you do not have to model it.
- **Typed views are zero-copy wrappers**, not a separate tree: editing through a view edits the markup the hub reads.
- **No `OpenXmlCompositeElement.Append` family of validation.** Write what the schema allows; the typed views create children in schema order (`AddX`, `GetOrAddX`).

## Spreadsheets

```csharp
// Open XML SDK
using var ss = SpreadsheetDocument.Open(path, true);
WorkbookPart wbp = ss.WorkbookPart!;
Sheet sheet = wbp.Workbook.Descendants<Sheet>().First(s => s.Name == "Sales");
var wsp = (WorksheetPart)wbp.GetPartById(sheet.Id!);
Cell cell = wsp.Worksheet.Descendants<Cell>().First(c => c.CellReference == "B2");
// ...resolve shared strings and styles yourself
```

```csharp
// OneDocument
using Workbook wb = Workbook.Open(path);
string text = wb.Sheets["Sales"]["B2"].Text;        // shared strings, styles and dates resolved
double n = wb.Sheets["Sales"]["B2"].Number;
```

### Streaming a large sheet

```csharp
// Open XML SDK
using OpenXmlReader reader = OpenXmlReader.Create(worksheetPart);
while (reader.Read()) { if (reader.ElementType == typeof(Cell) && reader.IsStartElement) { /* ... */ } }
```

```csharp
// OneDocument
using XlsxStreamReader book = XlsxStreamReader.Open(path);
using SheetCursor sheet = book.OpenSheet(0);
while (sheet.ReadRow()) { while (sheet.ReadCell()) { /* sheet.Kind, sheet.Number, sheet.TextUtf8 */ } }
```

Shared strings, styles and date detection are resolved by the cursor; it allocates nothing per cell (`docs/spreadsheets-streaming.md`).

## Presentations

```csharp
// Open XML SDK
using var ppt = PresentationDocument.Open(path, true);
SlideIdList ids = ppt.PresentationPart!.Presentation.SlideIdList!;
```

```csharp
// OneDocument
using Presentation deck = Presentation.Open(path);
foreach (Slide slide in deck.Slides) { Console.WriteLine(slide.Title); }
```

## Not supported or different

- No validator class. Output validation against the ECMA-376 and extension schemas is a repository test harness, not an API.
- No `OpenXmlPackage.SaveAs` with package-level edits through a part collection API beyond `IOpcPackage` (parts are added and replaced by URI).
- Typed views are generated for the schemas OneDocument pins (ECMA-376 and the Microsoft extension schemas); a namespace outside them stays reachable as markup.
- No `FlatOpc` helper class: Flat OPC is a save format (`doc.SaveAs("x.xml")`) and is read by content.
