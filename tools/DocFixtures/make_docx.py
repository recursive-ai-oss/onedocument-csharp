#!/usr/bin/env python3
"""Writes small DOCX files (hand-written WordprocessingML) that a word processor can save as Word 97-2003 files.

Usage: make_docx.py <out dir>
The files are deterministic. make_doc.sh converts them with a pinned LibreOffice build; the results are the fixtures of
tests/OneDocument.Documents.Tests/DocFixtureTests.cs (see testdata/doc/README.md).
"""
import struct, sys, zipfile, zlib, os

W = 'xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture"'
CT = 'application/vnd.openxmlformats-officedocument.wordprocessingml.'
REL = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships/'


def png():
    def chunk(t, d):
        c = struct.pack('>I', len(d)) + t + d
        return c + struct.pack('>I', zlib.crc32(t + d) & 0xFFFFFFFF)
    raw = b''.join(b'\x00' + bytes([200, 30, 30] * 8) for _ in range(8))
    return b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', 8, 8, 8, 2, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(raw)) + chunk(b'IEND', b'')


def write(path, body, parts=None, rels=None, extra_types=None):
    parts = parts or {}
    rels = rels or []
    types = [('/word/document.xml', CT + 'document.main+xml')] + [(k, v[0]) for k, v in parts.items()]
    ct = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Default Extension="png" ContentType="image/png"/>'
    ct += ''.join('<Override PartName="%s" ContentType="%s"/>' % t for t in types) + '</Types>'
    drel = ''.join('<Relationship Id="%s" Type="%s" Target="%s"%s/>' % (i, REL + t, tg, ' TargetMode="External"' if ext else '') for i, t, tg, ext in rels)
    with zipfile.ZipFile(path, 'w', zipfile.ZIP_DEFLATED) as z:
        def put(name, data):
            zi = zipfile.ZipInfo(name, (2020, 1, 1, 0, 0, 0))
            zi.compress_type = zipfile.ZIP_DEFLATED
            z.writestr(zi, data)
        put('[Content_Types].xml', ct)
        put('_rels/.rels', '<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="%sofficeDocument" Target="word/document.xml"/></Relationships>' % REL)
        put('word/document.xml', '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><w:document %s><w:body>%s</w:body></w:document>' % (W, body))
        put('word/_rels/document.xml.rels', '<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">%s</Relationships>' % drel)
        for name, (ctype, data) in parts.items():
            put(name[1:], data)
        if extra_types:
            for name, data in extra_types.items():
                put(name, data)


def p(text='', style=None, ppr='', rpr=''):
    s = '<w:pStyle w:val="%s"/>' % style if style else ''
    pp = '<w:pPr>%s%s</w:pPr>' % (s, ppr) if (s or ppr) else ''
    r = '<w:r>%s<w:t xml:space="preserve">%s</w:t></w:r>' % ('<w:rPr>%s</w:rPr>' % rpr if rpr else '', text) if text else ''
    return '<w:p>%s%s</w:p>' % (pp, r)


SECT = '<w:sectPr><w:pgSz w:w="12240" w:h="15840"/><w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440" w:header="720" w:footer="720" w:gutter="0"/></w:sectPr>'
STYLES = ('<?xml version="1.0" encoding="UTF-8"?><w:styles %s><w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/><w:qFormat/></w:style>'
          '<w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:qFormat/><w:pPr><w:keepNext/><w:spacing w:before="240" w:after="120"/><w:outlineLvl w:val="0"/></w:pPr><w:rPr><w:b/><w:sz w:val="32"/></w:rPr></w:style></w:styles>') % W


def main(out):
    os.makedirs(out, exist_ok=True)
    sty = {'/word/styles.xml': (CT + 'styles+xml', STYLES)}
    srel = [('rId1', 'styles', 'styles.xml', False)]

    # basic text and character formatting, hyperlink, tab stops, alignment
    body = (p('Quarterly report', 'Heading1') +
            '<w:p><w:r><w:t xml:space="preserve">Plain, </w:t></w:r><w:r><w:rPr><w:b/></w:rPr><w:t>bold</w:t></w:r><w:r><w:t xml:space="preserve">, </w:t></w:r><w:r><w:rPr><w:i/></w:rPr><w:t>italic</w:t></w:r><w:r><w:t xml:space="preserve">, </w:t></w:r><w:r><w:rPr><w:u w:val="single"/></w:rPr><w:t>underlined</w:t></w:r><w:r><w:t xml:space="preserve"> and </w:t></w:r><w:r><w:rPr><w:color w:val="FF0000"/><w:sz w:val="28"/></w:rPr><w:t>large red</w:t></w:r><w:r><w:t>.</w:t></w:r></w:p>' +
            p('Centered paragraph', ppr='<w:jc w:val="center"/>') +
            p('Indented with spacing', ppr='<w:spacing w:before="240" w:after="120" w:line="360" w:lineRule="auto"/><w:ind w:left="720" w:firstLine="360"/>') +
            '<w:p><w:pPr><w:tabs><w:tab w:val="right" w:pos="7200"/></w:tabs></w:pPr><w:r><w:t>Item</w:t></w:r><w:r><w:tab/></w:r><w:r><w:t>42</w:t></w:r></w:p>' +
            '<w:p><w:r><w:t xml:space="preserve">Visit </w:t></w:r><w:hyperlink r:id="rId2"><w:r><w:rPr><w:u w:val="single"/></w:rPr><w:t>the site</w:t></w:r></w:hyperlink><w:r><w:t>.</w:t></w:r></w:p>' +
            '<w:p><w:r><w:t>Before the break</w:t></w:r><w:r><w:br w:type="page"/></w:r><w:r><w:t>After the break</w:t></w:r></w:p>' + SECT)
    write(out + '/basic.docx', body, sty, srel + [('rId2', 'hyperlink', 'https://example.com/', True)])

    # tables
    def tc(text, w=2000, extra=''):
        return '<w:tc><w:tcPr><w:tcW w:w="%d" w:type="dxa"/>%s</w:tcPr>%s</w:tc>' % (w, extra, p(text))
    tbl = ('<w:tbl><w:tblPr><w:tblW w:w="6000" w:type="dxa"/><w:tblBorders><w:top w:val="single" w:sz="4" w:space="0" w:color="000000"/><w:left w:val="single" w:sz="4" w:space="0" w:color="000000"/><w:bottom w:val="single" w:sz="4" w:space="0" w:color="000000"/><w:right w:val="single" w:sz="4" w:space="0" w:color="000000"/><w:insideH w:val="single" w:sz="4" w:space="0" w:color="000000"/><w:insideV w:val="single" w:sz="4" w:space="0" w:color="000000"/></w:tblBorders></w:tblPr><w:tblGrid><w:gridCol w:w="2000"/><w:gridCol w:w="2000"/><w:gridCol w:w="2000"/></w:tblGrid>'
           '<w:tr><w:trPr><w:tblHeader/></w:trPr>' + tc('Name', extra='<w:shd w:val="clear" w:color="auto" w:fill="DDDDDD"/>') + tc('Qty') + tc('Price') + '</w:tr>'
           '<w:tr>' + tc('Apples') + tc('3') + tc('1.50') + '</w:tr>'
           '<w:tr>' + tc('Merged across two', 4000, '<w:gridSpan w:val="2"/>') + tc('9.99') + '</w:tr>'
           '<w:tr>' + tc('Tall', extra='<w:vMerge w:val="restart"/>') + tc('x') + tc('y') + '</w:tr>'
           '<w:tr>' + tc('', extra='<w:vMerge/>') + tc('z') + tc('w') + '</w:tr></w:tbl>')
    write(out + '/table.docx', p('Before') + tbl + p('After') + SECT, sty, srel)

    # lists: numbered with sub-level, bullets
    numbering = ('<?xml version="1.0" encoding="UTF-8"?><w:numbering %s>'
                 '<w:abstractNum w:abstractNumId="0"><w:multiLevelType w:val="multilevel"/>'
                 '<w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%%1."/><w:lvlJc w:val="left"/><w:pPr><w:ind w:left="720" w:hanging="360"/></w:pPr></w:lvl>'
                 '<w:lvl w:ilvl="1"><w:start w:val="1"/><w:numFmt w:val="lowerLetter"/><w:lvlText w:val="%%2)"/><w:lvlJc w:val="left"/><w:pPr><w:ind w:left="1440" w:hanging="360"/></w:pPr></w:lvl></w:abstractNum>'
                 '<w:abstractNum w:abstractNumId="1"><w:multiLevelType w:val="hybridMultilevel"/><w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="bullet"/><w:lvlText w:val="\u2022"/><w:lvlJc w:val="left"/><w:pPr><w:ind w:left="720" w:hanging="360"/></w:pPr></w:lvl></w:abstractNum>'
                 '<w:num w:numId="1"><w:abstractNumId w:val="0"/></w:num><w:num w:numId="2"><w:abstractNumId w:val="1"/></w:num></w:numbering>') % W
    def li(text, num, lvl=0):
        return p(text, ppr='<w:numPr><w:ilvl w:val="%d"/><w:numId w:val="%d"/></w:numPr>' % (lvl, num))
    body = li('First step', 1) + li('Sub step', 1, 1) + li('Second step', 1) + li('Bullet one', 2) + li('Bullet two', 2) + SECT
    parts = dict(sty)
    parts['/word/numbering.xml'] = (CT + 'numbering+xml', numbering)
    write(out + '/lists.docx', body, parts, srel + [('rId3', 'numbering', 'numbering.xml', False)])

    # footnote, endnote, comment
    notes = ('<?xml version="1.0" encoding="UTF-8"?><w:footnotes %s><w:footnote w:type="separator" w:id="-1"><w:p><w:r><w:separator/></w:r></w:p></w:footnote><w:footnote w:type="continuationSeparator" w:id="0"><w:p><w:r><w:continuationSeparator/></w:r></w:p></w:footnote>'
             '<w:footnote w:id="1"><w:p><w:r><w:footnoteRef/></w:r><w:r><w:t xml:space="preserve"> The footnote text.</w:t></w:r></w:p></w:footnote></w:footnotes>') % W
    ends = ('<?xml version="1.0" encoding="UTF-8"?><w:endnotes %s><w:endnote w:type="separator" w:id="-1"><w:p><w:r><w:separator/></w:r></w:p></w:endnote><w:endnote w:type="continuationSeparator" w:id="0"><w:p><w:r><w:continuationSeparator/></w:r></w:p></w:endnote>'
            '<w:endnote w:id="1"><w:p><w:r><w:endnoteRef/></w:r><w:r><w:t xml:space="preserve"> The endnote text.</w:t></w:r></w:p></w:endnote></w:endnotes>') % W
    comments = ('<?xml version="1.0" encoding="UTF-8"?><w:comments %s><w:comment w:id="0" w:author="Reviewer" w:initials="RV"><w:p><w:r><w:annotationRef/></w:r><w:r><w:t>Check this sentence.</w:t></w:r></w:p></w:comment></w:comments>') % W
    body = ('<w:p><w:r><w:t xml:space="preserve">Body text with a note</w:t></w:r><w:r><w:footnoteReference w:id="1"/></w:r><w:r><w:t xml:space="preserve"> and an endnote</w:t></w:r><w:r><w:endnoteReference w:id="1"/></w:r><w:r><w:t>.</w:t></w:r></w:p>'
            '<w:p><w:commentRangeStart w:id="0"/><w:r><w:t>Commented sentence.</w:t></w:r><w:commentRangeEnd w:id="0"/><w:r><w:commentReference w:id="0"/></w:r></w:p>' + SECT)
    parts = dict(sty)
    parts['/word/footnotes.xml'] = (CT + 'footnotes+xml', notes)
    parts['/word/endnotes.xml'] = (CT + 'endnotes+xml', ends)
    parts['/word/comments.xml'] = (CT + 'comments+xml', comments)
    write(out + '/notes.docx', body, parts, srel + [('rId3', 'footnotes', 'footnotes.xml', False), ('rId4', 'endnotes', 'endnotes.xml', False), ('rId5', 'comments', 'comments.xml', False)])

    # sections, header and footer with a page field, columns
    hdr = '<?xml version="1.0" encoding="UTF-8"?><w:hdr %s>%s</w:hdr>' % (W, p('Running header'))
    ftr = ('<?xml version="1.0" encoding="UTF-8"?><w:ftr %s><w:p><w:r><w:t xml:space="preserve">Page </w:t></w:r><w:r><w:fldChar w:fldCharType="begin"/></w:r><w:r><w:instrText xml:space="preserve"> PAGE </w:instrText></w:r><w:r><w:fldChar w:fldCharType="separate"/></w:r><w:r><w:t>1</w:t></w:r><w:r><w:fldChar w:fldCharType="end"/></w:r></w:p></w:ftr>') % W
    sect1 = '<w:p><w:pPr><w:sectPr><w:headerReference w:type="default" r:id="rId3"/><w:footerReference w:type="default" r:id="rId4"/><w:pgSz w:w="12240" w:h="15840"/><w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440" w:header="720" w:footer="720" w:gutter="0"/></w:sectPr></w:pPr><w:r><w:t>End of first section</w:t></w:r></w:p>'
    sect2 = '<w:sectPr><w:headerReference w:type="default" r:id="rId3"/><w:footerReference w:type="default" r:id="rId4"/><w:type w:val="nextPage"/><w:pgSz w:w="15840" w:h="12240" w:orient="landscape"/><w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440" w:header="720" w:footer="720" w:gutter="0"/><w:cols w:num="2" w:space="720"/></w:sectPr>'
    body = p('First section text') + sect1 + p('Second section, landscape, two columns') + sect2
    parts = dict(sty)
    parts['/word/header1.xml'] = (CT + 'header+xml', hdr)
    parts['/word/footer1.xml'] = (CT + 'footer+xml', ftr)
    write(out + '/sections.docx', body, parts, srel + [('rId3', 'header', 'header1.xml', False), ('rId4', 'footer', 'footer1.xml', False)])

    # tracked changes
    body = ('<w:p><w:r><w:t xml:space="preserve">Kept </w:t></w:r><w:ins w:id="1" w:author="Ann" w:date="2020-03-04T10:20:00Z"><w:r><w:t>inserted</w:t></w:r></w:ins>'
            '<w:del w:id="2" w:author="Bob" w:date="2020-03-05T11:30:00Z"><w:r><w:delText xml:space="preserve"> removed</w:delText></w:r></w:del><w:r><w:t xml:space="preserve"> and the end.</w:t></w:r></w:p>' + SECT)
    write(out + '/revisions.docx', body, sty, srel)

    # a picture
    img = png()
    body = ('<w:p><w:r><w:t xml:space="preserve">Before </w:t></w:r><w:r><w:drawing><wp:inline distT="0" distB="0" distL="0" distR="0"><wp:extent cx="914400" cy="914400"/><wp:docPr id="1" name="Picture 1"/><a:graphic><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture"><pic:pic><pic:nvPicPr><pic:cNvPr id="1" name="red.png"/><pic:cNvPicPr/></pic:nvPicPr><pic:blipFill><a:blip r:embed="rId3"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill><pic:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="914400" cy="914400"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></pic:spPr></pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r><w:r><w:t xml:space="preserve"> after.</w:t></w:r></w:p>' + SECT)
    write(out + '/picture.docx', body, sty, srel + [('rId3', 'image', 'media/red.png', False)], {'word/media/red.png': img})


if __name__ == '__main__':
    main(sys.argv[1])
