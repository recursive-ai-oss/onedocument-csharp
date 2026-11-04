"""Writes small Office Open XML files with python-docx, openpyxl and python-pptx (run only, as generators).

    generate.py <out dir>

The files use features that exercise the XML layer: many namespaces, mc:AlternateContent, extLst, odd prefixes, CRLF, entities,
very long attributes and text, and unicode.
"""
import sys
from pathlib import Path

import docx
import openpyxl
import pptx


def main(out: Path) -> None:
    out.mkdir(parents=True, exist_ok=True)
    long = "x" * 200_000
    d = docx.Document()
    d.add_heading("Heading & <entities> \u00e9\u4e2d\U0001F600", 1)
    for i in range(300):
        p = d.add_paragraph(f"Paragraph {i} with & ampersand, <angle> and \"quotes\" 'apos'. {long if i == 7 else ''}")
        p.add_run(" bold").bold = True
    t = d.add_table(rows=20, cols=5)
    for r, row in enumerate(t.rows):
        for c, cell in enumerate(row.cells):
            cell.text = f"{r},{c}"
    d.core_properties.title = "T \u00fc"
    d.save(out / "gen.docx")

    wb = openpyxl.Workbook()
    ws = wb.active
    for r in range(1, 2001):
        ws.append([r, f"s{r}", r * 1.5, f"=A{r}*2", "text & <more>"])
    ws2 = wb.create_sheet("Second \u00e9")
    ws2["A1"] = long
    ws2.merge_cells("B2:D3")
    wb.save(out / "gen.xlsx")

    prs = pptx.Presentation()
    for i in range(20):
        s = prs.slides.add_slide(prs.slide_layouts[1])
        s.shapes.title.text = f"Slide {i} & <title>"
        s.placeholders[1].text = "Body \u00e9\n" * 5
    prs.save(out / "gen.pptx")


if __name__ == "__main__":
    main(Path(sys.argv[1]))
