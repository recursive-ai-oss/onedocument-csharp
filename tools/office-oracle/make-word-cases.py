#!/usr/bin/env python3
"""Writes small generated WordprocessingML documents that ask Word one question each.

Each document is minimal and deterministic. The questions concern table, section and tracked-change behaviour that
the specification leaves open. Run them through office-oracle.sh with --probe --pdf --saveas; documents that need
revision actions come with a sidecar NAME.actions.txt (accept-all, reject-all, accept:N, reject:N,
accept-para:N, reject-para:N, one per line).

Usage: make-word-cases.py OUTDIR
"""
import importlib.util, os, sys

spec = importlib.util.spec_from_file_location('mc', os.path.join(os.path.dirname(os.path.abspath(__file__)), 'make-cases.py'))
mc = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mc)

W = mc.W
HDR = mc.HDR
AUTH = 'w:author="Probe" w:date="2024-01-01T00:00:00Z"'
STYLE_NORMAL = '<w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style>'


def build(path, body, styles='', numbering=None, settings='', headers=None, extra=None):
    """headers: {rel id: header text}. Header parts are /word/headerN.xml."""
    ct = ('<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>'
          '<Default Extension="xml" ContentType="application/xml"/>'
          '<Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>'
          '<Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>'
          '<Override PartName="/word/settings.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml"/>')
    rels = [('rIdS', 'styles', 'styles.xml'), ('rIdT', 'settings', 'settings.xml')]
    parts = [('word/styles.xml', HDR + f'<w:styles xmlns:w="{W}">{STYLE_NORMAL}{styles}</w:styles>'),
             ('word/settings.xml', HDR + f'<w:settings xmlns:w="{W}">{settings}</w:settings>')]
    if numbering:
        ct += '<Override PartName="/word/numbering.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml"/>'
        rels.append(('rIdN', 'numbering', 'numbering.xml'))
        parts.append(('word/numbering.xml', HDR + f'<w:numbering xmlns:w="{W}">{numbering}</w:numbering>'))
    for name, ctype, rtype, data in (extra or []):
        ct += f'<Override PartName="/word/{name}" ContentType="{ctype}"/>'
        rels.append((f'rIdX{len(rels)}', rtype, name))
        parts.append((f'word/{name}', data))
    for i, (rid, text) in enumerate(sorted((headers or {}).items()), 1):
        ct += f'<Override PartName="/word/header{i}.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml"/>'
        rels.append((rid, 'header', f'header{i}.xml'))
        parts.append((f'word/header{i}.xml', HDR + f'<w:hdr xmlns:w="{W}"><w:p><w:r><w:t>{text}</w:t></w:r></w:p></w:hdr>'))
    pkg = [('[Content_Types].xml', HDR + f'<Types xmlns="{mc.CT}">{ct}</Types>'),
           ('_rels/.rels', mc.rels(('rId1', 'officeDocument', 'word/document.xml'))),
           ('word/_rels/document.xml.rels', mc.rels(*rels)),
           ('word/document.xml', HDR + f'<w:document xmlns:w="{W}"><w:body>{body}</w:body></w:document>')] + parts
    mc.write(path, pkg)


def p(text, ppr='', extra_runs=''):
    return f'<w:p><w:pPr>{ppr}</w:pPr><w:r><w:t>{text}</w:t></w:r>{extra_runs}</w:p>'


PB = '<w:p><w:r><w:br w:type="page"/></w:r></w:p>'


def sect_para(text, sectpr):
    return f'<w:p><w:pPr><w:sectPr>{sectpr}</w:sectPr></w:pPr><w:r><w:t>{text}</w:t></w:r></w:p>'


def cell(text, tcpr='', style_p=''):
    return f'<w:tc><w:tcPr><w:tcW w:w="2000" w:type="dxa"/>{tcpr}</w:tcPr><w:p><w:r><w:t>{text}</w:t></w:r></w:p></w:tc>'


def table(rows, style=None, look='', grid=2):
    st = f'<w:tblStyle w:val="{style}"/>' if style else ''
    g = ''.join('<w:gridCol w:w="2000"/>' for _ in range(grid))
    return f'<w:tbl><w:tblPr>{st}<w:tblW w:w="0" w:type="auto"/>{look}</w:tblPr><w:tblGrid>{g}</w:tblGrid>{"".join(rows)}</w:tbl>'


def row(cells, trpr=''):
    return f'<w:tr><w:trPr>{trpr}</w:trPr>{"".join(cells)}</w:tr>' if trpr else f'<w:tr>{"".join(cells)}</w:tr>'


def main():
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)

    # ---- sections and tables -------------------------------------------------------------------------
    # 1. titlePg section without a first-page header reference of its own.
    def hdrref(kind, rid):
        return f'<w:headerReference w:type="{kind}" r:id="{rid}" xmlns:r="{mc.OD}"/>'
    s1 = hdrref('default', 'rIdH1') + hdrref('first', 'rIdH2') + '<w:type w:val="nextPage"/><w:titlePg/>'
    s2 = hdrref('default', 'rIdH3') + '<w:titlePg/>'
    body = p('S1 page one') + PB + sect_para('S1 page two', s1) + p('S2 page one') + PB + p('S2 page two') + f'<w:sectPr>{s2}</w:sectPr>'
    build(f'{out}/layout-titlepg-inherit.docx', body, headers={'rIdH1': 'H-default-S1', 'rIdH2': 'H-first-S1', 'rIdH3': 'H-default-S2'})
    # same, but the second section has no header references at all
    s2b = '<w:titlePg/>'
    body = p('S1 page one') + PB + sect_para('S1 page two', s1) + p('S2 page one') + PB + p('S2 page two') + f'<w:sectPr>{s2b}</w:sectPr>'
    build(f'{out}/layout-titlepg-inherit-norefs.docx', body, headers={'rIdH1': 'H-default-S1', 'rIdH2': 'H-first-S1'})

    # 2. vMerge continuation under a cell that is not part of a merge.
    t = table([row([cell('r1c1'), cell('r1c2')]), row([cell('r2c1', '<w:vMerge/>'), cell('r2c2')]),
               row([cell('r3c1', '<w:vMerge w:val="continue"/>'), cell('r3c2')])])
    build(f'{out}/layout-vmerge-orphan.docx', p('before') + t + p('after'))

    # 3. tblLook: legacy hex mask against explicit attributes. 0x0020 = firstRow, 0x0200 = noHBand, 0x0400 = noVBand.
    styles = ('<w:style w:type="table" w:styleId="TS"><w:name w:val="TS"/><w:tblPr/>'
              '<w:tblStylePr w:type="firstRow"><w:rPr><w:b/></w:rPr></w:tblStylePr>'
              '<w:tblStylePr w:type="band1Horz"><w:rPr><w:i/></w:rPr></w:tblStylePr></w:style>')

    def look3(label, lk):
        rows = [row([cell(f'{label} r{r}c1'), cell(f'{label} r{r}c2')]) for r in (1, 2, 3, 4)]
        return p(label) + table(rows, 'TS', lk)
    body = (look3('valOnly', '<w:tblLook w:val="0020"/>')
            + look3('attrsOnly', '<w:tblLook w:firstRow="1" w:lastRow="0" w:firstColumn="0" w:lastColumn="0" w:noHBand="1" w:noVBand="1"/>')
            + look3('conflict', '<w:tblLook w:val="0020" w:firstRow="0" w:lastRow="0" w:firstColumn="0" w:lastColumn="0" w:noHBand="1" w:noVBand="1"/>')
            + look3('conflict2', '<w:tblLook w:val="0600" w:firstRow="1" w:lastRow="0" w:firstColumn="0" w:lastColumn="0" w:noHBand="0" w:noVBand="0"/>'))
    build(f'{out}/layout-tbllook.docx', body, styles=styles)

    # 4. evenAndOddHeaders with a page-number restart in the second section.
    es1 = hdrref('default', 'rIdH1') + hdrref('even', 'rIdH2') + '<w:type w:val="nextPage"/>'
    es2 = hdrref('default', 'rIdH3') + hdrref('even', 'rIdH4') + '<w:pgNumType w:start="1"/>'
    body = p('S1 p1') + PB + p('S1 p2') + PB + sect_para('S1 p3', es1) + p('S2 p1') + PB + p('S2 p2') + f'<w:sectPr>{es2}</w:sectPr>'
    build(f'{out}/layout-evenodd-restart.docx', body, settings='<w:evenAndOddHeaders/>',
          headers={'rIdH1': 'ODD-S1', 'rIdH2': 'EVEN-S1', 'rIdH3': 'ODD-S2', 'rIdH4': 'EVEN-S2'})

    # 5. conditional regions in rows with merged cells and a leading grid gap.
    styles5 = ('<w:style w:type="table" w:styleId="TS5"><w:name w:val="TS5"/><w:tblPr/>'
               '<w:tblStylePr w:type="firstCol"><w:rPr><w:b/></w:rPr></w:tblStylePr>'
               '<w:tblStylePr w:type="band1Vert"><w:rPr><w:i/></w:rPr></w:tblStylePr></w:style>')
    look5 = '<w:tblLook w:val="0280" w:firstRow="0" w:lastRow="0" w:firstColumn="1" w:lastColumn="0" w:noHBand="1" w:noVBand="0"/>'
    rows = [row([cell('A gridSpan2', '<w:gridSpan w:val="2"/>'), cell('B')]),
            row([cell('C after gap'), cell('D')], '<w:gridBefore w:val="1"/>'),
            row([cell('E'), cell('F'), cell('G')])]
    body = table(rows, 'TS5', look5, grid=3)
    build(f'{out}/layout-table-regions.docx', body, styles=styles5)

    # 4b. restart at 2: the first page of section 2 is physical page 3 (odd) but printed 2 (even).
    es1b = hdrref('default', 'rIdH1') + hdrref('even', 'rIdH2') + '<w:type w:val="nextPage"/>'
    es2b = hdrref('default', 'rIdH3') + hdrref('even', 'rIdH4') + '<w:pgNumType w:start="2"/>'
    body = p('S1 p1') + PB + sect_para('S1 p2', es1b) + p('S2 p1') + PB + p('S2 p2') + f'<w:sectPr>{es2b}</w:sectPr>'
    build(f'{out}/layout-evenodd-restart2.docx', body, settings='<w:evenAndOddHeaders/>',
          headers={'rIdH1': 'ODD-S1', 'rIdH2': 'EVEN-S1', 'rIdH3': 'ODD-S2', 'rIdH4': 'EVEN-S2'})

    # 5b. band regions with bold (Range.Font.Bold shows conditional formatting reliably).
    styles6 = ('<w:style w:type="table" w:styleId="TS6"><w:name w:val="TS6"/><w:tblPr/>'
               '<w:tblStylePr w:type="band1Vert"><w:rPr><w:b/></w:rPr></w:tblStylePr></w:style>'
               '<w:style w:type="table" w:styleId="TS7"><w:name w:val="TS7"/><w:tblPr/>'
               '<w:tblStylePr w:type="band1Horz"><w:rPr><w:b/></w:rPr></w:tblStylePr></w:style>')
    vlook = '<w:tblLook w:val="0200" w:firstRow="0" w:lastRow="0" w:firstColumn="0" w:lastColumn="0" w:noHBand="1" w:noVBand="0"/>'
    hlook = '<w:tblLook w:val="0400" w:firstRow="0" w:lastRow="0" w:firstColumn="0" w:lastColumn="0" w:noHBand="0" w:noVBand="1"/>'
    rows = [row([cell('A span2', '<w:gridSpan w:val="2"/>'), cell('B')]),
            row([cell('C'), cell('D'), cell('E')]),
            row([cell('F gap'), cell('G')], '<w:gridBefore w:val="1"/>')]
    body = p('vertical bands') + table(rows, 'TS6', vlook, grid=3) + p('horizontal bands, vMerge') + table(
        [row([cell('H1', '<w:vMerge w:val="restart"/>'), cell('H2')]), row([cell('H3', '<w:vMerge/>'), cell('H4')]),
         row([cell('H5'), cell('H6')]), row([cell('H7'), cell('H8')])], 'TS7', hlook)
    build(f'{out}/layout-bands.docx', body, styles=styles6)

    # ---- tracked changes -----------------------------------------------------------------------------
    def act(name, *lines):
        open(f'{out}/{name}.actions.txt', 'w').write('\n'.join(lines) + '\n')

    # 1. removing a paragraph mark: which paragraph's properties survive.
    body = (p('First', '<w:jc w:val="center"/><w:rPr><w:del w:id="1" ' + AUTH + '/></w:rPr>') + p('Second', '<w:jc w:val="right"/>')
            + p('Third', '<w:jc w:val="center"/><w:rPr><w:ins w:id="2" ' + AUTH + '/></w:rPr>') + p('Fourth', '<w:jc w:val="right"/>'))
    build(f'{out}/rev-para-mark.docx', body)
    act('rev-para-mark', 'accept-all', 'reject-all', 'accept:1', 'reject:2')

    # 2. rPrChange inside a deletion and inside an insertion.
    rch = '<w:rPrChange w:id="{i}" ' + AUTH + '><w:rPr/></w:rPrChange>'
    body = (f'<w:p><w:r><w:t>keep </w:t></w:r><w:del w:id="3" {AUTH}><w:r><w:rPr><w:b/>{rch.format(i=4)}</w:rPr><w:delText>deleted-with-change</w:delText></w:r></w:del></w:p>'
            f'<w:p><w:r><w:t>keep </w:t></w:r><w:ins w:id="5" {AUTH}><w:r><w:rPr><w:b/>{rch.format(i=6)}</w:rPr><w:t>inserted-with-change</w:t></w:r></w:ins></w:p>')
    build(f'{out}/rev-rprchange.docx', body)
    act('rev-rprchange', 'accept-para:1', 'reject-para:2', 'accept-all', 'reject-all')

    # 3. numPr/ins and numberingChange.
    numbering = ('<w:abstractNum w:abstractNumId="0"><w:multiLevelType w:val="singleLevel"/><w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="decimal"/>'
                 '<w:lvlText w:val="%1."/><w:lvlJc w:val="left"/></w:lvl></w:abstractNum><w:num w:numId="1"><w:abstractNumId w:val="0"/></w:num>')
    body = (p('plain') + p('numPr-ins', f'<w:numPr><w:ilvl w:val="0"/><w:numId w:val="1"/><w:ins w:id="7" {AUTH}/></w:numPr>')
            + p('numbering-change', f'<w:numPr><w:ilvl w:val="0"/><w:numId w:val="1"/><w:numberingChange w:id="8" {AUTH} w:original="%1:1"/></w:numPr>')
            + p('end'))
    build(f'{out}/rev-numbering.docx', body, numbering=numbering)
    act('rev-numbering', 'reject-para:2', 'reject-para:3', 'reject-all', 'accept-all')

    # 4. cellMerge with vMerge / vMergeOrig.
    def merge_doc(name, second_cell_tcpr):
        t = table([row([cell('r1c1', '<w:vMerge w:val="restart"/>' if 'cont' in second_cell_tcpr and 'continue' in second_cell_tcpr else ''), cell('r1c2')]),
                   row([cell('r2c1', second_cell_tcpr), cell('r2c2')])])
        build(f'{out}/{name}.docx', p('before') + t + p('after'))
        act(name, 'accept-all', 'reject-all')
    merge_doc('rev-cellmerge-merged', f'<w:vMerge w:val="continue"/><w:cellMerge w:id="9" {AUTH} w:vMerge="cont" w:vMergeOrig="rest"/>')
    merge_doc('rev-cellmerge-split', f'<w:cellMerge w:id="9" {AUTH} w:vMerge="rest" w:vMergeOrig="cont"/>')

    # 5. one end of a move pair.
    body = (f'<w:p><w:moveFromRangeStart w:id="10" w:name="mv" {AUTH}/><w:moveFrom w:id="11" {AUTH}><w:r><w:delText>moved text</w:delText></w:r></w:moveFrom><w:moveFromRangeEnd w:id="10"/><w:r><w:t> tail1</w:t></w:r></w:p>'
            + p('middle')
            + f'<w:p><w:moveToRangeStart w:id="12" w:name="mv" {AUTH}/><w:moveTo w:id="13" {AUTH}><w:r><w:t>moved text</w:t></w:r></w:moveTo><w:moveToRangeEnd w:id="12"/><w:r><w:t> tail2</w:t></w:r></w:p>')
    build(f'{out}/rev-move.docx', body)
    u8_cases(out)
    act('rev-move', 'accept:1', 'accept:2', 'reject:1', 'reject:2', 'accept-all', 'reject-all')


W14 = 'http://schemas.microsoft.com/office/word/2010/wordml'
W15 = 'http://schemas.microsoft.com/office/word/2012/wordml'
MCNS = 'http://schemas.openxmlformats.org/markup-compatibility/2006'
CTP = 'application/vnd.openxmlformats-officedocument.wordprocessingml.'
RELO = ''


def u8_cases(out):
    # 1. endnotes with no endnotePr/numFmt anywhere.
    note = ('<w:{k} w:type="separator" w:id="-1"><w:p><w:r><w:separator/></w:r></w:p></w:{k}>'
            '<w:{k} w:type="continuationSeparator" w:id="0"><w:p><w:r><w:continuationSeparator/></w:r></w:p></w:{k}>'
            '<w:{k} w:id="1"><w:p><w:r><w:{k}Ref/></w:r><w:r><w:t> the note</w:t></w:r></w:p></w:{k}>')
    endnotes = HDR + f'<w:endnotes xmlns:w="{W}">' + note.format(k='endnote') + '</w:endnotes>'
    footnotes = HDR + f'<w:footnotes xmlns:w="{W}">' + note.format(k='footnote') + '</w:footnotes>'
    body = (p('text').replace('</w:p>', '<w:r><w:endnoteReference w:id="1"/></w:r></w:p>')
            + p('more').replace('</w:p>', '<w:r><w:footnoteReference w:id="1"/></w:r></w:p>'))
    build(f'{out}/notes-endnote-default.docx', body, extra=[('endnotes.xml', CTP + 'endnotes+xml', RELO + 'endnotes', endnotes),
                                                         ('footnotes.xml', CTP + 'footnotes+xml', RELO + 'footnotes', footnotes)])

    def comment(i, pid, text, ns=''):
        return (f'<w:comment w:id="{i}" w:author="A" w:date="2024-01-01T00:00:00Z" w:initials="A"><w:p {ns} w14:paraId="{pid}">'
                f'<w:r><w:t>{text}</w:t></w:r></w:p></w:comment>')

    def anchor(i):
        return (f'<w:commentRangeStart w:id="{i}"/><w:r><w:t>anchored{i}</w:t></w:r><w:commentRangeEnd w:id="{i}"/>'
                f'<w:r><w:commentReference w:id="{i}"/></w:r>')

    # 2. w14:paraId declared on the paragraphs, w14 listed as ignorable on the root but not declared there.
    local = (HDR + f'<w:comments xmlns:w="{W}" xmlns:mc="{MCNS}" mc:Ignorable="w14">'
             + comment(0, '0A0A0A01', 'local', f'xmlns:w14="{W14}"') + '</w:comments>')
    build(f'{out}/notes-comments-local-w14.docx', '<w:p>' + anchor(0) + '</w:p>',
          extra=[('comments.xml', CTP + 'comments+xml', RELO + 'comments', local)])

    # 3. replies in commentsExtended, no commentsIds part.
    root = f'<w:comments xmlns:w="{W}" xmlns:w14="{W14}" xmlns:mc="{MCNS}" mc:Ignorable="w14">'
    comments = HDR + root + comment(0, '0A0A0A01', 'parent') + comment(1, '0A0A0A02', 'reply') + '</w:comments>'
    ext = (HDR + f'<w15:commentsEx xmlns:w15="{W15}"><w15:commentEx w15:paraId="0A0A0A01" w15:done="0"/>'
           '<w15:commentEx w15:paraId="0A0A0A02" w15:paraIdParent="0A0A0A01" w15:done="0"/></w15:commentsEx>')
    build(f'{out}/notes-comments-replies.docx', '<w:p>' + anchor(0) + anchor(1) + '</w:p>',
          extra=[('comments.xml', CTP + 'comments+xml', RELO + 'comments', comments),
                 ('commentsExtended.xml', CTP + 'commentsExtended+xml', 'http://schemas.microsoft.com/office/2011/relationships/commentsExtended', ext)])


if __name__ == '__main__':
    main()
