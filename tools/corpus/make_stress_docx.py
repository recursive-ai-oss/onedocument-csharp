#!/usr/bin/env python3
"""Generate a large, deterministic DOCX stress document.

  make_stress_docx.py OUT.docx [--pages 1000] [--seed 1]

The document has chapters (Heading 1) with Heading 2 sections, body text, numbered and bulleted lists,
tables with a repeated header row, a new section every ten chapters with its own header and a
PAGE/NUMPAGES footer, footnotes, comments and a few tracked insertions and deletions. Text comes from a
seeded word list, so the same arguments give the same content.

The page count is a target: open the result in a layout engine (the corpus runner's `lo` check reports
pages) to see the real count, and raise or lower --pages to calibrate.
python-docx writes the baseline document and runs as a black box; footnotes and revisions are added to the
package afterwards because it has no API for them.
"""
import argparse, datetime, io, random, re, zipfile
import docx
from docx.enum.section import WD_SECTION
from docx.oxml import OxmlElement
from docx.oxml.ns import qn

WORDS = ("alpha bravo charlie delta echo foxtrot golf hotel india juliet kilo lima mike november oscar papa quebec romeo "
         "sierra tango uniform victor whiskey xray yankee zulu ledger invoice quarter budget forecast margin vendor policy "
         "contract clause schedule appendix summary review approval deadline project milestone report analysis").split()
FIXED = (1980, 1, 1, 0, 0, 0)
W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main"


def sentence(rng, n):
    return " ".join(rng.choice(WORDS) for _ in range(n)).capitalize() + "."


def paragraph_text(rng):
    return " ".join(sentence(rng, rng.randint(8, 16)) for _ in range(rng.randint(4, 7)))


def field(paragraph, instr):
    """A complex field with a cached result (ECMA-376 Part 1 §17.16.18)."""
    def fld(kind):
        r = paragraph.add_run()
        c = OxmlElement("w:fldChar")
        c.set(qn("w:fldCharType"), kind)
        r._r.append(c)
    fld("begin")
    r = paragraph.add_run()
    t = OxmlElement("w:instrText")
    t.set(qn("xml:space"), "preserve")
    t.text = " %s " % instr
    r._r.append(t)
    fld("separate")
    paragraph.add_run("1")
    fld("end")


def footnote_ref(i):
    r = OxmlElement("w:r")
    ref = OxmlElement("w:footnoteReference")
    ref.set(qn("w:id"), str(i))
    r.append(ref)
    return r


def build(pages, seed):
    rng = random.Random(seed)
    d = docx.Document()
    d.core_properties.created = d.core_properties.modified = datetime.datetime(2020, 1, 1)
    d.core_properties.author = "Generator"
    d.core_properties.title = "Stress document"
    chapters = max(1, pages // 10)
    footnote_ids, comment_budget = [], 40
    for c in range(chapters):
        if c and c % 10 == 0:
            s = d.add_section(WD_SECTION.NEW_PAGE)
            s.header.is_linked_to_previous = False
            s.footer.is_linked_to_previous = False
            s.header.paragraphs[0].text = "Part %d" % (c // 10 + 1)
            field(s.footer.paragraphs[0], "PAGE")
            s.footer.paragraphs[0].add_run(" of ")
            field(s.footer.paragraphs[0], "NUMPAGES")
        h = d.add_heading("Chapter %d: %s" % (c + 1, sentence(rng, 3)[:-1]), 1)
        if c and c % 10:
            h.paragraph_format.page_break_before = True
        for sec in range(3):
            d.add_heading("%d.%d %s" % (c + 1, sec + 1, sentence(rng, 3)[:-1]), 2)
            for p in range(7):
                para = d.add_paragraph(paragraph_text(rng))
                if p == 2 and len(footnote_ids) < 400:
                    footnote_ids.append(len(footnote_ids) + 1)
                    para._p.append(footnote_ref(footnote_ids[-1]))
                if p == 4 and comment_budget > 0 and c % 3 == 0:
                    comment_budget -= 1
                    d.add_comment(para.runs, text="Review note %d" % comment_budget, author="Reviewer", initials="R")
            for _ in range(4):
                d.add_paragraph(sentence(rng, 6), style="List Number" if sec % 2 == 0 else "List Bullet")
            if sec == 1:
                t = d.add_table(rows=1, cols=4)
                t.style = "Table Grid"
                for i, hd in enumerate(("Item", "Qty", "Price", "Note")):
                    t.rows[0].cells[i].text = hd
                t.rows[0]._tr.get_or_add_trPr().append(OxmlElement("w:tblHeader"))
                for _ in range(12):
                    cells = t.add_row().cells
                    cells[0].text = rng.choice(WORDS)
                    cells[1].text = str(rng.randint(1, 99))
                    cells[2].text = "%d.%02d" % (rng.randint(1, 999), rng.randint(0, 99))
                    cells[3].text = sentence(rng, 4)
    return d, footnote_ids


def add_revisions(xml):
    """Turn a few body runs into tracked insertions and deletions (ECMA-376 Part 1 §17.13.5)."""
    count = [0]

    def repl(m):
        count[0] += 1
        k = count[0]
        if k % 400 == 1:
            return ('<w:ins w:id="%d" w:author="Editor" w:date="2020-01-02T00:00:00Z"><w:r><w:t xml:space="preserve">%s</w:t></w:r></w:ins>'
                    % (9000 + k, m.group(1)))
        if k % 400 == 201:
            return ('<w:del w:id="%d" w:author="Editor" w:date="2020-01-02T00:00:00Z"><w:r><w:delText xml:space="preserve">%s</w:delText></w:r></w:del>'
                    % (9000 + k, m.group(1)))
        return m.group(0)
    return re.sub(r'<w:r><w:t(?: xml:space="preserve")?>([^<]{40,})</w:t></w:r>', repl, xml)


def finish(data, ids, rng):
    zin = zipfile.ZipFile(io.BytesIO(data))
    out = io.BytesIO()
    notes = ['<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\n<w:footnotes xmlns:w="%s">'
             '<w:footnote w:type="separator" w:id="-1"><w:p><w:r><w:separator/></w:r></w:p></w:footnote>'
             '<w:footnote w:type="continuationSeparator" w:id="0"><w:p><w:r><w:continuationSeparator/></w:r></w:p></w:footnote>' % W]
    for i in ids:
        notes.append('<w:footnote w:id="%d"><w:p><w:r><w:footnoteRef/></w:r><w:r><w:t xml:space="preserve"> %s</w:t></w:r></w:p></w:footnote>'
                     % (i, sentence(rng, 7)))
    notes.append("</w:footnotes>")
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as zo:
        for info in zin.infolist():
            b = zin.read(info)
            n = info.filename
            if n == "[Content_Types].xml":
                b = b.replace(b"</Types>", b'<Override PartName="/word/footnotes.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.footnotes+xml"/></Types>')
            elif n == "word/_rels/document.xml.rels":
                b = b.replace(b"</Relationships>", b'<Relationship Id="rIdFootnotes" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/footnotes" Target="footnotes.xml"/></Relationships>')
            elif n == "word/settings.xml":
                # the stock template omits the required percent attribute of w:zoom
                b = b.replace(b'<w:zoom w:val="bestFit"/>', b'<w:zoom w:val="bestFit" w:percent="100"/>')
            elif n == "word/document.xml":
                b = add_revisions(b.decode("utf-8")).encode("utf-8")
            zi = zipfile.ZipInfo(n, FIXED)
            zi.compress_type = zipfile.ZIP_DEFLATED
            zo.writestr(zi, b)
        zi = zipfile.ZipInfo("word/footnotes.xml", FIXED)
        zi.compress_type = zipfile.ZIP_DEFLATED
        zo.writestr(zi, "".join(notes))
    return out.getvalue()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("out")
    ap.add_argument("--pages", type=int, default=1000)
    ap.add_argument("--seed", type=int, default=1)
    a = ap.parse_args()
    d, ids = build(a.pages, a.seed)
    buf = io.BytesIO()
    d.save(buf)
    with open(a.out, "wb") as f:
        f.write(finish(buf.getvalue(), ids, random.Random(a.seed + 1)))


if __name__ == "__main__":
    main()
