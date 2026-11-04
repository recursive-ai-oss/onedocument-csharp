#!/usr/bin/env python3
"""Writes the small deterministic files used to prove the desktop-Office driver.

  clean.docx / clean.xlsx / clean.pptx       minimal valid packages (expect: opened clean)
  corrupt-*.{docx,xlsx,pptx}                  the same with a main part that is not well-formed XML (expect: repair prompt)
  corrupt-schema.*                            well-formed but invalid content (expect: repair prompt)
  corrupt-rel.docx                            a relationship that points at a part which does not exist
  prec-*.docx                                 style-precedence cases for the Word probe mode

Usage: make-cases.py OUTDIR
"""
import sys, os, zipfile

CT = 'http://schemas.openxmlformats.org/package/2006/content-types'
RELNS = 'http://schemas.openxmlformats.org/package/2006/relationships'
OD = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships'
W = 'http://schemas.openxmlformats.org/wordprocessingml/2006/main'
HDR = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\n'


def write(path, parts):
    # Fixed timestamps and order keep the output reproducible.
    with zipfile.ZipFile(path, 'w', zipfile.ZIP_DEFLATED) as z:
        for name, data in parts:
            zi = zipfile.ZipInfo(name, (2024, 1, 1, 0, 0, 0))
            zi.compress_type = zipfile.ZIP_DEFLATED
            z.writestr(zi, data)


def rels(*items):
    body = ''.join(f'<Relationship Id="{i}" Type="{t if t.startswith("http") else OD + "/" + t}" Target="{target}"/>' for i, t, target in items)
    return HDR + f'<Relationships xmlns="{RELNS}">{body}</Relationships>'


def docx(body, styles=None, extra_rels=()):
    ct = ('<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>'
          '<Default Extension="xml" ContentType="application/xml"/>'
          '<Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>')
    r = []
    parts = []
    if styles:
        ct += '<Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>'
        r.append(('rId1', 'styles', 'styles.xml'))
        parts.append(('word/styles.xml', HDR + f'<w:styles xmlns:w="{W}">{styles}</w:styles>'))
    r.extend(extra_rels)
    return [('[Content_Types].xml', HDR + f'<Types xmlns="{CT}">{ct}</Types>'),
            ('_rels/.rels', rels(('rId1', 'officeDocument', 'word/document.xml'))),
            ('word/_rels/document.xml.rels', rels(*r)),
            ('word/document.xml', HDR + f'<w:document xmlns:w="{W}"><w:body>{body}<w:sectPr/></w:body></w:document>')] + parts


def para(text, ppr='', rpr=''):
    return f'<w:p><w:pPr>{ppr}</w:pPr><w:r><w:rPr>{rpr}</w:rPr><w:t>{text}</w:t></w:r></w:p>'


def xlsx(sheet):
    S = 'http://schemas.openxmlformats.org/spreadsheetml/2006/main'
    return [('[Content_Types].xml', HDR + f'<Types xmlns="{CT}"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>'),
            ('_rels/.rels', rels(('rId1', 'officeDocument', 'xl/workbook.xml'))),
            ('xl/workbook.xml', HDR + f'<workbook xmlns="{S}" xmlns:r="{OD}"><sheets><sheet name="Sheet1" sheetId="1" r:id="rId1"/></sheets></workbook>'),
            ('xl/_rels/workbook.xml.rels', rels(('rId1', 'worksheet', 'worksheets/sheet1.xml'))),
            ('xl/worksheets/sheet1.xml', sheet.replace('@NS@', S))]


CLEAN_SHEET = HDR + '<worksheet xmlns="@NS@"><sheetData><row r="1"><c r="A1"><v>1</v></c><c r="B1"><v>2</v></c><c r="C1"><f>A1+B1</f><v>3</v></c></row></sheetData></worksheet>'
BAD_SHEET = HDR + '<worksheet xmlns="@NS@"><sheetData><row r="1"><c r="A1"><v>1</c></row></sheetData></worksheet>'


def pptx(slide):
    P = 'http://schemas.openxmlformats.org/presentationml/2006/main'
    A = 'http://schemas.openxmlformats.org/drawingml/2006/main'
    sm = (HDR + f'<p:sldMaster xmlns:a="{A}" xmlns:r="{OD}" xmlns:p="{P}"><p:cSld><p:spTree><p:nvGrpSpPr><p:cNvPr id="1" name=""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr/></p:spTree></p:cSld>'
          '<p:clrMap bg1="lt1" tx1="dk1" bg2="lt2" tx2="dk2" accent1="accent1" accent2="accent2" accent3="accent3" accent4="accent4" accent5="accent5" accent6="accent6" hlink="hlink" folHlink="folHlink"/>'
          '<p:sldLayoutIdLst><p:sldLayoutId id="2147483649" r:id="rId1"/></p:sldLayoutIdLst></p:sldMaster>')
    sl = (HDR + f'<p:sldLayout xmlns:a="{A}" xmlns:r="{OD}" xmlns:p="{P}" type="blank"><p:cSld name="Blank"><p:spTree><p:nvGrpSpPr><p:cNvPr id="1" name=""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr/></p:spTree></p:cSld></p:sldLayout>')
    theme = (HDR + f'<a:theme xmlns:a="{A}" name="T"><a:themeElements><a:clrScheme name="c"><a:dk1><a:srgbClr val="000000"/></a:dk1><a:lt1><a:srgbClr val="FFFFFF"/></a:lt1><a:dk2><a:srgbClr val="000000"/></a:dk2><a:lt2><a:srgbClr val="FFFFFF"/></a:lt2>'
             + ''.join(f'<a:accent{i}><a:srgbClr val="4472C4"/></a:accent{i}>' for i in range(1, 7)) + '<a:hlink><a:srgbClr val="0000FF"/></a:hlink><a:folHlink><a:srgbClr val="800080"/></a:folHlink></a:clrScheme>'
             '<a:fontScheme name="f"><a:majorFont><a:latin typeface="Arial"/><a:ea typeface=""/><a:cs typeface=""/></a:majorFont><a:minorFont><a:latin typeface="Arial"/><a:ea typeface=""/><a:cs typeface=""/></a:minorFont></a:fontScheme>'
             '<a:fmtScheme name="m"><a:fillStyleLst><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:solidFill><a:schemeClr val="phClr"/></a:solidFill></a:fillStyleLst>'
             '<a:lnStyleLst><a:ln w="9525"><a:solidFill><a:schemeClr val="phClr"/></a:solidFill></a:ln><a:ln w="9525"><a:solidFill><a:schemeClr val="phClr"/></a:solidFill></a:ln><a:ln w="9525"><a:solidFill><a:schemeClr val="phClr"/></a:solidFill></a:ln></a:lnStyleLst>'
             '<a:effectStyleLst><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle></a:effectStyleLst>'
             '<a:bgFillStyleLst><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:solidFill><a:schemeClr val="phClr"/></a:solidFill></a:bgFillStyleLst></a:fmtScheme></a:themeElements></a:theme>')
    ct = ('<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/>'
          '<Override PartName="/ppt/presentation.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml"/>'
          '<Override PartName="/ppt/slideMasters/slideMaster1.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.slideMaster+xml"/>'
          '<Override PartName="/ppt/slideLayouts/slideLayout1.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.slideLayout+xml"/>'
          '<Override PartName="/ppt/slides/slide1.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.slide+xml"/>'
          '<Override PartName="/ppt/theme/theme1.xml" ContentType="application/vnd.openxmlformats-officedocument.theme+xml"/>')
    pres = (HDR + f'<p:presentation xmlns:a="{A}" xmlns:r="{OD}" xmlns:p="{P}"><p:sldMasterIdLst><p:sldMasterId id="2147483648" r:id="rId1"/></p:sldMasterIdLst>'
            '<p:sldIdLst><p:sldId id="256" r:id="rId2"/></p:sldIdLst><p:sldSz cx="9144000" cy="6858000"/><p:notesSz cx="6858000" cy="9144000"/></p:presentation>')
    return [('[Content_Types].xml', HDR + f'<Types xmlns="{CT}">{ct}</Types>'),
            ('_rels/.rels', rels(('rId1', 'officeDocument', 'ppt/presentation.xml'))),
            ('ppt/presentation.xml', pres),
            ('ppt/_rels/presentation.xml.rels', rels(('rId1', 'slideMaster', 'slideMasters/slideMaster1.xml'), ('rId2', 'slide', 'slides/slide1.xml'))),
            ('ppt/slideMasters/slideMaster1.xml', sm),
            ('ppt/slideMasters/_rels/slideMaster1.xml.rels', rels(('rId1', 'slideLayout', '../slideLayouts/slideLayout1.xml'), ('rId2', 'theme', '../theme/theme1.xml'))),
            ('ppt/slideLayouts/slideLayout1.xml', sl),
            ('ppt/slideLayouts/_rels/slideLayout1.xml.rels', rels(('rId1', 'slideMaster', '../slideMasters/slideMaster1.xml'))),
            ('ppt/slides/slide1.xml', slide),
            ('ppt/slides/_rels/slide1.xml.rels', rels(('rId1', 'slideLayout', '../slideLayouts/slideLayout1.xml'))),
            ('ppt/theme/theme1.xml', theme)]


def slide_xml(ok=True):
    P = 'http://schemas.openxmlformats.org/presentationml/2006/main'
    A = 'http://schemas.openxmlformats.org/drawingml/2006/main'
    sp = ('<p:sp><p:nvSpPr><p:cNvPr id="2" name="T"/><p:cNvSpPr txBox="1"/><p:nvPr/></p:nvSpPr><p:spPr><a:xfrm><a:off x="914400" y="914400"/><a:ext cx="4572000" cy="914400"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></p:spPr>'
          '<p:txBody><a:bodyPr/><a:lstStyle/><a:p><a:r><a:rPr lang="en-US"/><a:t>Hello</a:t></a:r></a:p></p:txBody></p:sp>')
    s = (HDR + f'<p:sld xmlns:a="{A}" xmlns:r="{OD}" xmlns:p="{P}"><p:cSld><p:spTree><p:nvGrpSpPr><p:cNvPr id="1" name=""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr/>{sp}</p:spTree></p:cSld></p:sld>')
    return s if ok else s.replace('</a:t>', '</a:r>', 1)


def main():
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    body = para('Hello') + para('World')
    write(f'{out}/clean.docx', docx(body))
    bad = docx(body)
    bad[3] = (bad[3][0], bad[3][1].replace('</w:t>', '</w:r>', 1))  # mismatched end tag
    write(f'{out}/corrupt-xml.docx', bad)
    write(f'{out}/corrupt-rel.docx', docx(body, extra_rels=[('rId9', 'header', 'missing-header.xml')]))
    write(f'{out}/corrupt-schema.docx', docx(body + '<w:p><w:bogus/></w:p>'))
    write(f'{out}/corrupt-schema.xlsx', xlsx(CLEAN_SHEET.replace('<c r="A1"><v>1</v></c><c r="B1"><v>2</v></c>', '<c r="B1"><v>2</v></c><c r="A1"><v>1</v></c>')))
    write(f'{out}/corrupt-schema.pptx', pptx(slide_xml(True).replace('</p:spTree>', '<p:bogus/></p:spTree>')))
    cell_bad = '<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w="2000"/></w:tblGrid><w:tr><w:tc><w:tcPr/></w:tc></w:tr></w:tbl>'
    write(f'{out}/corrupt-cell.docx', docx(body + cell_bad))
    write(f'{out}/corrupt-attr.docx', docx(para('Hello', '<w:jc w:val="sideways"/>', '<w:sz w:val="big"/>')))
    write(f'{out}/corrupt-style.xlsx', xlsx(CLEAN_SHEET.replace('<c r="A1">', '<c r="A1" s="99">')))
    write(f'{out}/corrupt-sst.xlsx', xlsx(CLEAN_SHEET.replace('<c r="A1"><v>1</v>', '<c r="A1" t="s"><v>7</v>')))
    write(f'{out}/corrupt-rel.pptx', pptx(slide_xml(True))[:1] + [pptx(slide_xml(True))[1]] + [(n, d.replace('slides/slide1.xml', 'slides/slide9.xml') if n == 'ppt/_rels/presentation.xml.rels' else d) for n, d in pptx(slide_xml(True))[2:]])
    write(f'{out}/clean.xlsx', xlsx(CLEAN_SHEET))
    write(f'{out}/corrupt-xml.xlsx', xlsx(BAD_SHEET))
    write(f'{out}/clean.pptx', pptx(slide_xml(True)))
    write(f'{out}/corrupt-xml.pptx', pptx(slide_xml(False)))

    # Style precedence (ECMA-376 Part 1 §17.7): each paragraph carries one question; the probe reads the
    # effective formatting of its first run from Word.
    styles = ('<w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii="Arial" w:hAnsi="Arial"/><w:sz w:val="28"/></w:rPr></w:rPrDefault></w:docDefaults>'
              '<w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style>'
              '<w:style w:type="paragraph" w:styleId="A"><w:name w:val="A"/><w:basedOn w:val="Normal"/><w:rPr><w:b/><w:sz w:val="40"/></w:rPr></w:style>'
              '<w:style w:type="paragraph" w:styleId="B"><w:name w:val="B"/><w:basedOn w:val="A"/><w:rPr><w:i/><w:color w:val="FF0000"/></w:rPr></w:style>'
              '<w:style w:type="character" w:styleId="Bold"><w:name w:val="Bold"/><w:rPr><w:b/></w:rPr></w:style>'
              '<w:style w:type="character" w:styleId="Green"><w:name w:val="Green"/><w:rPr><w:color w:val="00FF00"/></w:rPr></w:style>'
              '<w:style w:type="paragraph" w:styleId="Ind"><w:name w:val="Ind"/><w:basedOn w:val="Normal"/><w:pPr><w:ind w:left="720" w:firstLine="360"/><w:spacing w:before="240" w:after="120"/></w:pPr></w:style>')
    cases = [
        ('docdefaults only: 14pt Arial', para('plain')),
        ('basedOn chain: B inherits bold+size 20 from A, adds italic and red', para('chain', '<w:pStyle w:val="B"/>')),
        ('toggle: paragraph style A is bold, character style Bold also bold: XOR gives not bold', '<w:p><w:pPr><w:pStyle w:val="A"/></w:pPr><w:r><w:rPr><w:rStyle w:val="Bold"/></w:rPr><w:t>toggle</w:t></w:r></w:p>'),
        ('direct overrides style: B is red italic, direct color blue and not italic', para('direct', '<w:pStyle w:val="B"/>', '<w:color w:val="0000FF"/><w:i w:val="0"/>')),
        ('character style color beats paragraph style color', '<w:p><w:pPr><w:pStyle w:val="B"/></w:pPr><w:r><w:rPr><w:rStyle w:val="Green"/></w:rPr><w:t>charstyle</w:t></w:r></w:p>'),
        ('direct bold false overrides toggle', '<w:p><w:pPr><w:pStyle w:val="A"/></w:pPr><w:r><w:rPr><w:b w:val="0"/></w:rPr><w:t>boldoff</w:t></w:r></w:p>'),
        ('paragraph style indentation and spacing', para('indent', '<w:pStyle w:val="Ind"/>')),
        ('direct indentation overrides style (left only; firstLine inherited)', para('indent2', '<w:pStyle w:val="Ind"/><w:ind w:left="1440"/>')),
    ]
    write(f'{out}/prec-styles.docx', docx(''.join(c[1] for c in cases), styles=styles))
    with open(f'{out}/prec-styles.questions.txt', 'w') as f:
        for i, c in enumerate(cases, 1):
            f.write(f'{i}: {c[0]}\n')


if __name__ == '__main__':
    main()
