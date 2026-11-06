# Coming from Xceed DocX

DocX loads a Word document with `DocX.Load`, exposes `Paragraphs`, `Tables`, `InsertParagraph`, `ReplaceText` and `AddTable`, and saves. OneDocument's `Document` covers the same ground on the lossless model, and adds templating, conversion and rendering. Read [README.md](README.md) first for what is true on every page.

## Load, read, add, save

```csharp
// Xceed DocX
using var doc = DocX.Load(path);
foreach (Paragraph p in doc.Paragraphs) { Console.WriteLine(p.Text); }
doc.InsertParagraph("Added");
doc.SaveAs(outPath);
```

```csharp
// OneDocument
using Document doc = Document.Open(path);
foreach (Paragraph p in doc.Paragraphs) { Console.WriteLine(p.Text); }
doc.Body.AddParagraph("Added");
doc.SaveAs(outPath);
```

`Document.Create()` makes a new document. `Save()` replaces the opened file atomically. `Document.Open` also reads `.doc`, `.rtf`, `.odt` and Word 2003 XML, which DocX does not.

## Styles on paragraphs

```csharp
// Xceed DocX
paragraph.StyleName = "Heading1";
```

```csharp
// OneDocument
paragraph.StyleId = "Heading1";
doc.Body.AddParagraph("Title", "Heading1");
```

Style definitions, inheritance and numbering are resolved by the hub (`docs/docx-styles-numbering.md`).

## Replace text

```csharp
// Xceed DocX
doc.ReplaceText("[name]", "Ada");
```

```csharp
// OneDocument: find and replace across runs, keeping formatting
doc.Replace("[name]", "Ada");
```

The exact call shape, the search options (case, whole word, regular expression) and the report it returns are in `docs/operations-find-replace.md`. For filling placeholders use the template engine below, which is tolerant of tags that Word split across runs.

## Templates and mail merge

```csharp
// Xceed DocX: placeholders replaced one by one
doc.ReplaceText("{{customer}}", customer.Name);
```

```csharp
// OneDocument: tags, loops and conditions in the document, data from an object, JSON, a dictionary or a DataTable
Template.Render(path, invoice, outPath);          // outPath may be .docx or .pdf
```

Syntax: `{{ customer.name }}`, `{% for line in lines %}` ... `{% endfor %}`, `{% if overdue %}`; expressions are Excel formulas (`docs/templating.md`, `docs/templating-docx.md`). Word's own merge fields are filled by `doc.MailMerge(data)` (`docs/mail-merge.md`).

## Tables

```csharp
// Xceed DocX
Table t = doc.AddTable(2, 3);
t.Rows[0].Cells[0].Paragraphs[0].Append("A1");
doc.InsertTable(t);
```

```csharp
// OneDocument
Table t = doc.Tables.First();
t.SetCellText(0, 0, "A1");
t.InsertRow(1);
```

There is no `AddTable` call: tables are created by the template engine (repeating rows) or the typed views. Existing tables are fully editable (rows, columns, merged cells through the logical cell model).

## Images, headers, footers, properties

Pictures: no `AddImage` call on the hub; the template engine inserts pictures (`{% image %}`), and existing pictures are kept and rendered. Headers and footers: `doc.Sections[0]` exposes them (`docs/docx-tables-sections.md`). Core properties (`Title`, `Creator`, ...) are on the document's properties object (`docs/docx-stories-properties.md`).

## Output formats

`doc.SaveAs("x.pdf")`, `"x.rtf"`, `"x.odt"`, `"x.html"`, `"x.md"` pick the writer by extension. Conversions that lose something report it (`doc.ImportLoss` for what was lost reading, `doc.ExportLoss` for writing RTF; `docs/rtf.md`, `docs/html.md`).

## Not supported or different

- No fluent chaining of formatting (`Bold().Color(...)` on appended text). Run formatting is edited through the typed view of the run (`run.View`).
- No `AddTable`, `AddImage`, `InsertHyperlink` or `AddHeaders` helpers on the hub.
- No `DocX.Create` + `InsertParagraph` of formatted text in one call; the paragraph is added, then formatted.
- Revisions are a full model (`doc.Revisions`: read, author, accept, reject), which DocX does not have.
