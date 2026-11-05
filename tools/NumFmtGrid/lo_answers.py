"""Turns LibreOffice's CSV export of the two grid workbooks into answers.tsv (id, TEXT() result, cell display text)."""
import csv
import glob
import sys

out = sys.argv[1]


def read(pattern):
    files = glob.glob(f"{out}/lo/{pattern}")
    if not files:
        return {}
    rows = {}
    with open(files[0], newline="", encoding="utf-8") as f:
        for i, row in enumerate(csv.reader(f)):
            if i == 0 or not row or not row[0].strip().isdigit():
                continue
            rows[row[0].strip()] = row
    return rows


def esc(s):
    return s.replace("\\", "\\\\").replace("\t", "\\t").replace("\n", "\\n").replace("\r", "\\r")


text = read("grid-text*.csv")
cells = read("grid-cells*.csv")
with open(f"{out}/answers-lo.tsv", "w", encoding="utf-8") as w:
    for id_ in sorted(set(text) | set(cells), key=int):
        t = text.get(id_, ["", "", "", ""])
        c = cells.get(id_, ["", ""])
        w.write(f"{id_}\t{esc(t[3] if len(t) > 3 else '')}\t{esc(c[1] if len(c) > 1 else '')}\n")
print(f"text rows {len(text)}, cell rows {len(cells)}")
