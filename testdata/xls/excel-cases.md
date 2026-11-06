# Legacy workbook cases for Excel

Inputs for the comparison with Excel's own reading of the same legacy files. They are not answered yet; they run once at the release gate on the Office machine.

For each `.xls` file: open it in Excel, save it as `.xlsx`, and compare with what `Workbook.Open("x.xls")` gives (`SaveAs` to `.xlsx`): cell values, formula text, number-format codes, fonts, fills, borders, alignment, merged cells, column widths, row heights, hidden rows and columns, defined names, hyperlinks, comments, data validation, conditional formats, page setup, pictures.

| File | sha256 | What to look at |
|---|---|---|
| basic.xls | 9e72f85e292673f07a6f34e7082a6e861432f064c9facc08ef4b3f606ad96a59 | numbers, text, dates, errors, booleans, number formats, cached results |
| formulas.xls | 92d472ccf3561c9fbeaba084bb97a32f04ddeca15dd9749a1534975d6cc33108 | the formula text of 46 formulas, shared and array formulas, defined names, 3-D references |
| styles.xls | 223eaa2fcb4000563c9f1186deb6265911076c3ddae7e68d417af636578ee649 | fonts, fills, borders, rotation, indent, shrink, protection, merges, hidden rows and columns |
| features.xls | 68502e21859ea15d75bf11152a66b59c961386c37714fca55140f7eba8a6c6a9 | hyperlinks, comments, validation lists, conditional formats, print setup, breaks, protection, filter, outline groups |
| pictures.xls | b065086607a38cccb98a2c96e21e7a6919f9eee611350b3a23cc6137b959a2ee | picture anchors and sizes |

## Open questions

The specification leaves these open; Excel's output decides them.

1. **Row heights.** The reader sets a height only for rows the file marks as manually sized (`fUnsynced`) and leaves the others to automatic fitting. Does Excel's own conversion write `ht` for the other rows (with `customHeight` off)? Compare row heights in `features.xls`.
2. **Number formats below id 164.** A `FORMAT` record that redefines a built-in id (LibreOffice writes some) is renumbered into the custom range. Does Excel keep the id?
3. **Solid fills in conditional-format differential formats.** The reader puts the colour into `bgColor` (the usual `dxf` convention). Compare the `dxf` fills of `features.xls`.
4. **Typed validation lists.** A literal list is one string constant with NUL separators; the reader writes `"a,b,c"`. Check the three lists of `features.xls`.
5. **Hyperlink to a place in the same file.** LibreOffice stores it as a file moniker to the original path; the reader keeps that as an external target. What does Excel make of `C2` in `features.xls`?
6. **Print titles and the filter range** written as built-in names twice by LibreOffice (once flagged built-in, once by text): the reader keeps the first. What does Excel keep?
7. **BIFF5 XF layout** (fill and border bit positions): no BIFF5 file can be written by the available tools; the question is answered with Excel 95 files from the corpus on the Office machine.
