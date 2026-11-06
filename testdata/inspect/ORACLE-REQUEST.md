# Office cases for inspecting and sanitising (unanswered)

Files are in this directory. Hash them (sha256, first 16 hex digits below) and run each case in the desktop application on the Office oracle machine, one batch at a time. Our own counts are in the last column of table 1; `onedocument inspect <file>` prints them.

| File | sha256 |
|---|---|
| `torture.docx` | `19619046d02c68ee…` |
| `torture.pptx` | `e8ac4702d774413a…` |
| `torture.xlsx` | `756010529daa5a32…` |
| `sanitized/all.docx` | `d7b04103eb936d1d…` |
| `sanitized/all.pptx` | `7ad86d169daf3ea3…` |
| `sanitized/all.xlsx` | `85c8cf6d902ae656…` |
| `sanitized/default.docx` | `d7b04103eb936d1d…` |
| `sanitized/default.pptx` | `cfe9f5e2a91f8b02…` |
| `sanitized/default.xlsx` | `f347507b6e6fe55d…` |

## 1. Document Inspector on the torture files

Open each `torture.*`, run Check for Issues > Inspect Document with every inspector ticked, and record per inspector whether it finds something and how many items it lists.

| File | Inspector | Our finding |
|---|---|---|
| `torture.docx` | Comments, Revisions, Versions, Annotations | 1 comment (author PLANTED-Commenter), 2 revision marks (PLANTED-RevAuthor) |
| `torture.docx` | Document Properties and Personal Information | core: title, creator, last modified by, created; app: company, manager; custom: 1; document variable: 1; data binding; attached template; person part; rsids |
| `torture.docx` | Headers, Footers, Watermarks | header and footer text |
| `torture.docx` | Hidden Text | 1 run; the white run is not reported (we list it as a tip) |
| `torture.docx` | Custom XML Data | 1 part |
| `torture.docx` | Invisible Content | 1 hidden picture |
| `torture.docx` | Collapsed Headings, Task Pane Add-ins, Embedded Documents, Macros | 1 collapsed heading; 2 add-in parts; 1 OLE object; no macros |
| `torture.xlsx` | Comments and Annotations | a note and a threaded comment |
| `torture.xlsx` | Document Properties and Personal Information | as above, plus user name (file sharing), printer path, absolute path, "document location" |
| `torture.xlsx` | Headers and Footers | 1 sheet header |
| `torture.xlsx` | Hidden Rows and Columns | row 2 and column C of Visible |
| `torture.xlsx` | Hidden Worksheets | Secret (hidden), Deep (very hidden) |
| `torture.xlsx` | Custom XML, Invisible Content | 1 part; 1 hidden shape |
| `torture.xlsx` | External links, Embedded objects, Hidden names, connections | 1 link; 1 OLE object; HiddenName; 1 connection |
| `torture.pptx` | Comments and Annotations | 2 comments (classic and modern), ink |
| `torture.pptx` | Revision Tracking Data, Invisible On-Slide Content, Off-Slide Content, Presentation Notes | none (no co-authoring part); 1 hidden shape; 1 shape outside the slide; 2 notes |
| `torture.pptx` | Custom XML Data, Task Pane Add-ins, Embedded | 1 part; 2 parts; 1 OLE object |

Also record: does Office list hidden slides anywhere (we find slide 2)? does the Inspector list the Excel thumbnail or Word preview picture?

## 2. Office's Remove All against ours

In a copy of each torture file, Remove All on every inspector that offers it, Save As. Compare with `sanitized/default.*`: list the part names of both packages and the differences in `word/document.xml`, `xl/workbook.xml`, `xl/worksheets/sheet1.xml`, `ppt/presentation.xml`. In particular:

1. Word revision marks: does Remove All accept the changes, reject them, or leave them? (We accept.)
2. Excel hidden rows and columns: confirm Office deletes them (we clear their cells and show them so references stay valid). Which row/column positions does Office report after?
3. Excel hidden sheets: Office removes Secret and Deep; what happens to the defined name HiddenName and to the print areas scoped to the sheets? (We remove the sheets the other parts do not refer to.)
4. PowerPoint hidden slide: not in the inspector list; confirm it stays.
5. Word data-binding link: confirm the bound content control keeps its text.

## 3. Our outputs open without a repair prompt

Open `sanitized/default.*` and `sanitized/all.*` in Word, Excel and PowerPoint. Expected: no repair prompt; the visible text is `Visible text` (docx), cells A1, B1, D1 = 42 and A3 = 1 (xlsx; D1 was an external-link formula and is a value), slide titles `Visible slide` and `Third slide` (pptx). Then run the Inspector on each output: every inspector reports nothing except Embedded Documents (the OLE object).
