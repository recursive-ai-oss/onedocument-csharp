#!/usr/bin/env python3
"""Populate schemas/ from the pinned standards downloads.

Usage: pin-schemas.py <downloads-dir> <schemas-dir>

<downloads-dir> must hold the ECMA-376 5th edition zips and the Open Specifications documents named below. Every input is
checked against its SHA-256 before use, so the output is a pure function of the pinned files:

  schemas/ecma376/transitional/*.xsd   Part 4, OfficeOpenXML-XMLSchema-Transitional.zip, unchanged
  schemas/ecma376/strict/*.xsd         Part 1, OfficeOpenXML-XMLSchema-Strict.zip, unchanged
  schemas/ecma376/opc/*.xsd            Part 2, OpenPackagingConventions-XMLSchema.zip, unchanged
  schemas/ms/<spec>/*.xsd              the schemas printed in the appendix of each Open Specifications document, text unchanged
                                       (except one missing '>' in [MS-OFFMACRO2]'s excel/2006/main, see extract_ms)

Only the standard library is used. Nothing is read from any other source.
"""
import hashlib
import html
import io
import os
import re
import sys
import zipfile

PINS = {
    'ECMA-376-1_5th_edition_december_2016.zip': '9d0bcad9cf06054785b03762fcfadbf6bab7e54a5f9d69434e34b7fd464d4129',
    'ECMA-376-2_5th_edition_december_2021.zip': '1d489dc491168ea1f9e9a59063acc8dd5f02b4ad1d21aa7ec19ba9a58d020c70',
    'ECMA-376-4_5th_edition_december_2016.zip': 'bd25da1109f73762356596918bf5ff8b74a1331642dba5f1c1d1dfc6bed34ecd',
    'MS-DOCX-260818.docx': 'ed5d74bb33232d083742949d8072766e99f02fc3fbc6b7dc8b73264965dcce3a',
    'MS-ODRAWXML-260217.docx': '20de73988d209fd18f1776c2960103179c9c93b883c1d9a44ab3ce83b8b60f5b',
    'MS-PPTX-240820.docx': '7530fef0768c14bee58463c896718a90f020606a0677cab221a63080e93c8170',
    'MS-XLSX-260519.docx': '724dc55c19963233a4a9e5f6fa93f70b8f782408641f95feabcf2bfef7540c10',
    'MS-OFFMACRO2-240820.docx': '0a8f806eef4339119de77d8cb0094dc1333c6eac0a47be425b97b4b21330f67c',
}

# (outer zip, inner zip, destination under schemas/)
ECMA = [
    ('ECMA-376-4_5th_edition_december_2016.zip', 'OfficeOpenXML-XMLSchema-Transitional.zip', 'ecma376/transitional'),
    ('ECMA-376-1_5th_edition_december_2016.zip', 'OfficeOpenXML-XMLSchema-Strict.zip', 'ecma376/strict'),
    ('ECMA-376-2_5th_edition_december_2021.zip', 'OpenPackagingConventions-XMLSchema.zip', 'ecma376/opc'),
]
MS = ['MS-DOCX-260818.docx', 'MS-ODRAWXML-260217.docx', 'MS-PPTX-240820.docx', 'MS-XLSX-260519.docx', 'MS-OFFMACRO2-240820.docx']

TOKEN = re.compile(r'<w:t(?: [^>]*)?>([^<]*)</w:t>|<w:tab/>|<w:br/>')
PARA = re.compile(r'<w:p[ >].*?</w:p>', re.S)


def sha256(path):
    h = hashlib.sha256()
    with open(path, 'rb') as f:
        for block in iter(lambda: f.read(1 << 20), b''):
            h.update(block)
    return h.hexdigest()


def write(path, data):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, 'wb') as f:
        f.write(data)


def paragraph_text(p):
    out = []
    for m in TOKEN.finditer(p):
        if m.group(1) is not None:
            out.append(m.group(1))
        else:
            out.append('\t' if m.group(0) == '<w:tab/>' else '\n')
    return html.unescape(''.join(out))


def slug(namespace):
    s = re.sub(r'^https?://', '', namespace)
    s = re.sub(r'^schemas\.microsoft\.com/office/', '', s)
    return re.sub(r'[^A-Za-z0-9]+', '-', s).strip('-')


def extract_ms(docx, outdir):
    """A schema starts at a paragraph beginning "<xsd:schema" and ends at the paragraph "</xsd:schema>"."""
    spec = re.match(r'(MS-[A-Z0-9]+)', os.path.basename(docx)).group(1)
    with zipfile.ZipFile(docx) as z:
        doc = z.read('word/document.xml').decode('utf-8')
    paras = [paragraph_text(p) for p in PARA.findall(doc)]
    seen = set()
    i = 0
    while i < len(paras):
        if paras[i].lstrip().startswith('<xsd:schema'):
            j = i
            while j < len(paras) and paras[j].strip() != '</xsd:schema>':
                j += 1
            body = '\n'.join(x.rstrip() for x in paras[i:j + 1]) + '\n'
            m = re.search(r'targetNamespace="([^"]+)"', body)
            if not m:
                raise SystemExit('%s: schema at paragraph %d has no targetNamespace' % (docx, i))
            # The one defect in the printed schemas: [MS-OFFMACRO2] leaves the <xsd:schema> start tag of
            # excel/2006/main without its closing '>'. It is the only edit made to a schema's text.
            body = re.sub(r'(targetNamespace="[^"]+")\n(\s*<xsd:import)', r'\1>\n\2', body, count=1) if spec == 'MS-OFFMACRO2' else body
            name = slug(m.group(1)) + '.xsd'
            if name in seen:
                raise SystemExit('%s: two schemas for %s' % (docx, m.group(1)))
            seen.add(name)
            write(os.path.join(outdir, 'ms', spec, name), body.encode('utf-8'))
            i = j
        i += 1


def main(downloads, outdir):
    for name, want in PINS.items():
        path = os.path.join(downloads, name)
        if not os.path.exists(path):
            raise SystemExit('missing download: %s' % name)
        got = sha256(path)
        if got != want:
            raise SystemExit('%s: sha256 %s, pinned %s' % (name, got, want))
    for outer, inner, dest in ECMA:
        with zipfile.ZipFile(os.path.join(downloads, outer)) as z:
            data = z.read(inner)
        with zipfile.ZipFile(io.BytesIO(data)) as zi:
            for info in sorted(zi.infolist(), key=lambda i: i.filename):
                if info.filename.endswith('.xsd'):
                    write(os.path.join(outdir, dest, os.path.basename(info.filename)), zi.read(info))
    for docx in MS:
        extract_ms(os.path.join(downloads, docx), outdir)


if __name__ == '__main__':
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)
    main(sys.argv[1], sys.argv[2])
