#!/usr/bin/env python3
"""Generate a large, deterministic workbook for memory and streaming tests.

  make_stress_xlsx.py OUT.xlsx [--rows 1000000] [--cols 20] [--strings 5000]

The sheet has a header row and `rows` data rows of `cols` columns: integers, doubles, shared strings, booleans, dates
with a date style, number-formatted doubles, an ordinary formula column and a shared formula column (cached values
included), with row attributes in the form Excel writes them. Nothing is random: the same arguments give the same bytes
apart from zip timestamps, which are fixed. The part is written as a stream, so memory stays small at any size.
"""
import argparse, zipfile

NS = "http://schemas.openxmlformats.org/spreadsheetml/2006/main"
RNS = "http://schemas.openxmlformats.org/officeDocument/2006/relationships"
DECL = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\n'
FIXED = (2020, 1, 1, 0, 0, 0)


def col(n):
    s = ""
    while n:
        n, r = divmod(n - 1, 26)
        s = chr(65 + r) + s
    return s


def add(zf, name, data):
    zi = zipfile.ZipInfo(name, FIXED)
    zi.compress_type = zipfile.ZIP_DEFLATED
    zf.writestr(zi, data, compresslevel=6)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("out")
    ap.add_argument("--rows", type=int, default=1_000_000)
    ap.add_argument("--cols", type=int, default=20)
    ap.add_argument("--strings", type=int, default=5000)
    a = ap.parse_args()
    cols = max(a.cols, 8)
    names = [col(i) for i in range(1, cols + 1)]
    with zipfile.ZipFile(a.out, "w") as zf:
        add(zf, "[Content_Types].xml", DECL + '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/><Override PartName="/xl/sharedStrings.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml"/></Types>')
        add(zf, "_rels/.rels", DECL + '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>')
        add(zf, "xl/workbook.xml", DECL + f'<workbook xmlns="{NS}" xmlns:r="{RNS}"><bookViews><workbookView/></bookViews><sheets><sheet name="Stress" sheetId="1" r:id="rId1"/></sheets><calcPr calcId="191029"/></workbook>')
        add(zf, "xl/_rels/workbook.xml.rels", DECL + '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/><Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings" Target="sharedStrings.xml"/></Relationships>')
        add(zf, "xl/styles.xml", DECL + f'<styleSheet xmlns="{NS}"><numFmts count="1"><numFmt numFmtId="164" formatCode="0.00"/></numFmts><fonts count="2"><font><sz val="11"/><name val="Calibri"/><family val="2"/></font><font><b/><sz val="11"/><name val="Calibri"/><family val="2"/></font></fonts><fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills><borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="4"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="164" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/><xf numFmtId="14" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/><xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1"/></cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>')
        sst = [f"Item {i:05d} category {i % 37}" for i in range(a.strings)]
        add(zf, "xl/sharedStrings.xml", DECL + f'<sst xmlns="{NS}" count="{a.rows * 2}" uniqueCount="{len(sst)}">' + "".join(f"<si><t>{s}</t></si>" for s in sst) + "</sst>")
        zi = zipfile.ZipInfo("xl/worksheets/sheet1.xml", FIXED)
        zi.compress_type = zipfile.ZIP_DEFLATED
        with zf.open(zi, "w", force_zip64=True) as w:
            w.write((DECL + f'<worksheet xmlns="{NS}" xmlns:r="{RNS}" xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006" xmlns:x14ac="http://schemas.microsoft.com/office/spreadsheetml/2009/9/ac" mc:Ignorable="x14ac"><dimension ref="A1:{names[-1]}{a.rows + 1}"/><sheetViews><sheetView workbookViewId="0"><pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/></sheetView></sheetViews><sheetFormatPr defaultRowHeight="15" x14ac:dyDescent="0.25"/><cols><col min="1" max="1" width="10" customWidth="1"/><col min="3" max="4" width="28" customWidth="1"/></cols><sheetData>').encode())
            header = f'<row r="1" spans="1:{cols}" x14ac:dyDescent="0.25">' + "".join(f'<c r="{names[i]}1" s="3" t="inlineStr"><is><t>Column {i + 1}</t></is></c>' for i in range(cols)) + "</row>"
            w.write(header.encode())
            buf = []
            for r in range(2, a.rows + 2):
                n = r - 1
                x = n * 0.37
                cells = [
                    f'<c r="A{r}"><v>{n}</v></c>',
                    f'<c r="B{r}" s="1"><v>{round(x, 6)}</v></c>',
                    f'<c r="C{r}" t="s"><v>{n % a.strings}</v></c>',
                    f'<c r="D{r}" t="s"><v>{(n * 7) % a.strings}</v></c>',
                    f'<c r="E{r}" t="b"><v>{n & 1}</v></c>',
                    f'<c r="F{r}" s="2"><v>{43831 + n % 3000}</v></c>',
                    f'<c r="G{r}"><f>A{r}*2</f><v>{n * 2}</v></c>',
                    (f'<c r="H{r}"><f t="shared" ref="H2:H{a.rows + 1}" si="0">A2+B2</f><v>{round(n + x, 6)}</v></c>' if r == 2 else f'<c r="H{r}"><f t="shared" si="0"/><v>{round(n + x, 6)}</v></c>'),
                ]
                for i in range(8, cols):
                    if i % 3 == 0:
                        cells.append(f'<c r="{names[i]}{r}" s="1"><v>{round((n * (i + 1)) % 9973 / 7, 4)}</v></c>')
                    else:
                        cells.append(f'<c r="{names[i]}{r}"><v>{(n * (i + 3)) % 100003}</v></c>')
                buf.append(f'<row r="{r}" spans="1:{cols}" x14ac:dyDescent="0.25">' + "".join(cells) + "</row>")
                if len(buf) >= 2000:
                    w.write("".join(buf).encode())
                    buf = []
            w.write("".join(buf).encode())
            w.write(b'</sheetData><pageMargins left="0.7" right="0.7" top="0.75" bottom="0.75" header="0.3" footer="0.3"/></worksheet>')


if __name__ == "__main__":
    main()
