#!/usr/bin/env python3
"""Write three small DOCX samples for the validator check: good, bad (one schema error) and mc (markup compatibility).

  make_samples.py OUTDIR

Needs python-docx (run as a black box to write a baseline document). Fixed core-property
dates keep the output stable.
"""
import datetime, io, os, sys, zipfile
import docx


def baseline():
    d = docx.Document()
    d.core_properties.created = d.core_properties.modified = datetime.datetime(2020, 1, 1)
    d.add_heading("Sample", 1)
    d.add_paragraph("Hello, validator.")
    t = d.add_table(rows=2, cols=2)
    t.cell(0, 0).text = "a"
    buf = io.BytesIO()
    d.save(buf)
    return buf.getvalue()


def rewrite(data, edit):
    out = io.BytesIO()
    with zipfile.ZipFile(io.BytesIO(data)) as zi, zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as zo:
        for info in zi.infolist():
            b = zi.read(info)
            if info.filename == "word/settings.xml":
                # the stock template omits the required percent attribute of w:zoom
                b = b.decode("utf-8").replace('<w:zoom w:val="bestFit"/>', '<w:zoom w:val="bestFit" w:percent="100"/>').encode("utf-8")
            if info.filename == "word/document.xml":
                b = edit(b.decode("utf-8")).encode("utf-8")
            zo.writestr(info.filename, b)
    return out.getvalue()


def bad(xml):
    # an element the schema does not allow inside a paragraph
    return xml.replace("<w:p>", "<w:p><w:notAnElement/>", 1)


W14 = "http://schemas.microsoft.com/office/word/2010/wordml"


def mc(xml):
    # w14:paraId attribute made ignorable, and an AlternateContent whose Choice needs w14 with a plain Fallback.
    add = ""
    for decl in ('xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"', 'xmlns:w14="%s"' % W14):
        if decl.split("=")[0] not in xml.split(">", 2)[1]:
            add += decl + " "
    if "mc:Ignorable" not in xml.split(">", 2)[1]:
        add += 'mc:Ignorable="w14" '
    xml = xml.replace("<w:document ", "<w:document " + add, 1)
    xml = xml.replace("<w:p>", '<w:p w14:paraId="1A2B3C4D">', 1)
    alt = ('<mc:AlternateContent><mc:Choice Requires="w14"><w:p><w:pPr><w14:ligatures w14:val="standard"/></w:pPr>'
           '<w:r><w:t>w14 branch</w:t></w:r></w:p></mc:Choice><mc:Fallback><w:p><w:r><w:t>fallback</w:t></w:r></w:p>'
           '</mc:Fallback></mc:AlternateContent>')
    return xml.replace("<w:tbl>", alt + "<w:tbl>", 1)


def main():
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    base = baseline()
    for name, fn in (("good", lambda x: x), ("bad", bad), ("mc", mc)):
        with open(os.path.join(out, name + ".docx"), "wb") as f:
            f.write(rewrite(base, fn))


if __name__ == "__main__":
    main()
