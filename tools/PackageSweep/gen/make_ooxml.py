"""Writes small, varied OOXML files with python-docx, openpyxl and python-pptx for the package sweep.

usage: make_ooxml.py OUT_DIR

The inputs are generated here (seeded, no clock in the content); the libraries only act as black-box writers. LibreOffice can
then be run over the output (see make_ooxml.sh) to add files written by a second implementation.
"""
import io
import random
import struct
import sys
import zlib
from pathlib import Path


def png(w, h, seed):
    rnd = random.Random(seed)
    raw = b"".join(b"\x00" + bytes(rnd.randrange(256) for _ in range(w * 3)) for _ in range(h))

    def chunk(kind, data):
        body = kind + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)

    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw)) + chunk(b"IEND", b"")


def docx(path, paragraphs, images):
    from docx import Document
    from docx.shared import Inches

    d = Document()
    d.core_properties.author = "sweep"
    d.add_heading("Package sweep document", 0)
    for i in range(paragraphs):
        d.add_paragraph(f"Paragraph {i}: the quick brown fox jumps over the lazy dog. " * 3, style="List Bullet" if i % 7 == 0 else None)
    t = d.add_table(rows=4, cols=3)
    for r in range(4):
        for c in range(3):
            t.cell(r, c).text = f"r{r}c{c}"
    for i in range(images):
        d.add_picture(io.BytesIO(png(40 + i, 30 + i, i)), width=Inches(1))
    d.add_paragraph("caf\u00e9 \u4e2d\u6587 \u0645\u0631\u062d\u0628\u0627")
    d.save(path)


def xlsx(path, rows):
    from openpyxl import Workbook

    wb = Workbook()
    ws = wb.active
    ws.title = "Data"
    for r in range(1, rows + 1):
        ws.append([r, f"name {r}", r * 1.5, f"=A{r}*2"])
    ws2 = wb.create_sheet("Second")
    ws2["A1"] = "x"
    wb.save(path)


def pptx(path, slides):
    from pptx import Presentation
    from pptx.util import Inches

    p = Presentation()
    for i in range(slides):
        s = p.slides.add_slide(p.slide_layouts[5])
        s.shapes.title.text = f"Slide {i}"
        s.shapes.add_picture(io.BytesIO(png(30, 30, 100 + i)), Inches(1), Inches(2))
    p.save(path)


def macro_enabled(src, dst, old_main, new_main):
    """Rewrites a package as its macro-enabled variant: main content type changed and a (non-functional) vbaProject.bin part added."""
    import zipfile

    with zipfile.ZipFile(src) as zin, zipfile.ZipFile(dst, "w", zipfile.ZIP_DEFLATED) as zout:
        for item in zin.infolist():
            data = zin.read(item.filename)
            if item.filename == "[Content_Types].xml":
                text = data.decode("utf-8").replace(old_main, new_main)
                text = text.replace("</Types>", '<Default Extension="bin" ContentType="application/vnd.ms-office.vbaProject"/></Types>')
                data = text.encode("utf-8")
            zout.writestr(item, data)
        zout.writestr("word/vbaProject.bin" if str(dst).endswith("docm") else "xl/vbaProject.bin", bytes(range(256)) * 20)


def main():
    out = Path(sys.argv[1])
    out.mkdir(parents=True, exist_ok=True)
    docx(out / "small.docx", 5, 0)
    docx(out / "medium.docx", 600, 6)
    docx(out / "large.docx", 6000, 20)
    xlsx(out / "small.xlsx", 10)
    xlsx(out / "large.xlsx", 20000)
    pptx(out / "small.pptx", 3)
    pptx(out / "large.pptx", 60)
    macro_enabled(out / "small.docx", out / "small.docm", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml", "application/vnd.ms-word.document.macroEnabled.main+xml")
    macro_enabled(out / "small.xlsx", out / "small.xlsm", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml", "application/vnd.ms-excel.sheet.macroEnabled.main+xml")


main()
