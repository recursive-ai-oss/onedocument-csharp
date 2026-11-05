#!/usr/bin/env python3
"""Writes small generated documents that ask Word how list numbering runs through the stories of a document.

Every numbered paragraph carries a short tag as its text so the probe output (office-oracle.sh run --probe, key `stories`)
can be matched to it. One document per question: headers and footers of one and of two sections, footnotes and endnotes,
comments, DrawingML text boxes (with their VML fallback) and plain VML text boxes.

Usage: make-story-cases.py OUTDIR
"""
import importlib.util, os, sys

spec = importlib.util.spec_from_file_location('mc', os.path.join(os.path.dirname(os.path.abspath(__file__)), 'make-cases.py'))
mc = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mc)

W, HDR = mc.W, mc.HDR
NS = (f'xmlns:w="{W}" xmlns:r="{mc.OD}" '
      'xmlns:wps="http://schemas.microsoft.com/office/word/2010/wordprocessingShape" '
      'xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006" xmlns:v="urn:schemas-microsoft-com:vml" '
      'xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing" '
      'xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"')
CTYPE = 'application/vnd.openxmlformats-officedocument.wordprocessingml.'
NUMBERING = ('<w:abstractNum w:abstractNumId="0"><w:multiLevelType w:val="hybridMultilevel"/>'
             '<w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%1."/><w:lvlJc w:val="left"/></w:lvl></w:abstractNum>'
             '<w:abstractNum w:abstractNumId="1"><w:multiLevelType w:val="hybridMultilevel"/>'
             '<w:lvl w:ilvl="0"><w:start w:val="1"/><w:numFmt w:val="lowerLetter"/><w:lvlText w:val="(%1)"/><w:lvlJc w:val="left"/></w:lvl></w:abstractNum>'
             '<w:num w:numId="1"><w:abstractNumId w:val="0"/></w:num><w:num w:numId="2"><w:abstractNumId w:val="1"/></w:num>')


def np(tag, num=1):
    return f'<w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="{num}"/></w:numPr></w:pPr><w:r><w:t>{tag}</w:t></w:r></w:p>'


def plain(tag):
    return f'<w:p><w:r><w:t>{tag}</w:t></w:r></w:p>'


def build(path, body, parts=(), settings=''):
    """parts: (name, content type suffix, relationship type, rId, xml)."""
    ct = ('<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>'
          '<Default Extension="xml" ContentType="application/xml"/>'
          f'<Override PartName="/word/document.xml" ContentType="{CTYPE}document.main+xml"/>'
          f'<Override PartName="/word/numbering.xml" ContentType="{CTYPE}numbering+xml"/>'
          f'<Override PartName="/word/settings.xml" ContentType="{CTYPE}settings+xml"/>')
    rl = [('rIdN', 'numbering', 'numbering.xml'), ('rIdT', 'settings', 'settings.xml')]
    files = [('word/numbering.xml', HDR + f'<w:numbering xmlns:w="{W}">{NUMBERING}</w:numbering>'),
             ('word/settings.xml', HDR + f'<w:settings xmlns:w="{W}">{settings}</w:settings>')]
    for name, suffix, rtype, rid, xml in parts:
        ct += f'<Override PartName="/word/{name}" ContentType="{CTYPE}{suffix}"/>'
        rl.append((rid, rtype, name))
        files.append((f'word/{name}', HDR + xml))
    pkg = [('[Content_Types].xml', HDR + f'<Types xmlns="{mc.CT}">{ct}</Types>'),
           ('_rels/.rels', mc.rels(('rId1', 'officeDocument', 'word/document.xml'))),
           ('word/_rels/document.xml.rels', mc.rels(*rl)),
           ('word/document.xml', HDR + f'<w:document {NS}><w:body>{body}</w:body></w:document>')] + files
    mc.write(path, pkg)


def hf(root, rid, name, paras):
    kind = 'header' if root == 'hdr' else 'footer'
    return (name, kind + '+xml', kind, rid, f'<w:{root} {NS}>{paras}</w:{root}>')


def sect(refs, extra=''):
    return f'<w:sectPr>{refs}{extra}<w:pgSz w:w="12240" w:h="15840"/><w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440" w:header="720" w:footer="720" w:gutter="0"/></w:sectPr>'


def case_headers_footers(out):
    refs1 = ('<w:headerReference w:type="default" r:id="rIdH1"/><w:headerReference w:type="first" r:id="rIdH2"/><w:headerReference w:type="even" r:id="rIdH3"/>'
             '<w:footerReference w:type="default" r:id="rIdF1"/>')
    s1 = sect(refs1, '<w:titlePg/>')
    refs2 = '<w:headerReference w:type="default" r:id="rIdH4"/><w:footerReference w:type="default" r:id="rIdF2"/>'
    s2 = sect(refs2)
    body = (np('b1') + np('b2') + f'<w:p><w:pPr>{s1}</w:pPr><w:r><w:t>sec1end</w:t></w:r></w:p>' + np('b3') + np('b4') + s2)
    parts = [hf('hdr', 'rIdH1', 'header1.xml', np('hd1') + np('hd2') + np('hd3')),
             hf('hdr', 'rIdH2', 'header2.xml', np('hf1') + np('hf2')),
             hf('hdr', 'rIdH3', 'header3.xml', np('he1') + np('he2')),
             hf('hdr', 'rIdH4', 'header4.xml', np('h2a') + np('h2b')),
             hf('ftr', 'rIdF1', 'footer1.xml', np('fd1') + np('fd2')),
             hf('ftr', 'rIdF2', 'footer2.xml', np('f2a') + np('f2b'))]
    build(os.path.join(out, 'story-headers-footers.docx'), body, parts, '<w:evenAndOddHeaders/>')


def case_notes(out):
    sep = ('<w:footnote w:type="separator" w:id="-1"><w:p><w:r><w:separator/></w:r></w:p></w:footnote>'
           '<w:footnote w:type="continuationSeparator" w:id="0"><w:p><w:r><w:continuationSeparator/></w:r></w:p></w:footnote>')
    fn = f'<w:footnotes {NS}>{sep}<w:footnote w:id="1">{np("fn1a")}{np("fn1b")}</w:footnote><w:footnote w:id="2">{np("fn2a")}{plain("fn2plain")}{np("fn2b")}</w:footnote><w:footnote w:id="3">{np("fn3a", 2)}{np("fn3b")}</w:footnote></w:footnotes>'
    esep = sep.replace('w:footnote', 'w:endnote')
    en = f'<w:endnotes {NS}>{esep}<w:endnote w:id="1">{np("en1a")}{np("en1b")}</w:endnote><w:endnote w:id="2">{np("en2a")}</w:endnote></w:endnotes>'
    ref = lambda kind, i: f'<w:r><w:{kind}Reference w:id="{i}"/></w:r>'
    body = (f'<w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="1"/></w:numPr></w:pPr><w:r><w:t>b1</w:t></w:r>{ref("footnote", 1)}{ref("endnote", 1)}</w:p>'
            f'<w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="1"/></w:numPr></w:pPr><w:r><w:t>b2</w:t></w:r>{ref("footnote", 2)}{ref("footnote", 3)}{ref("endnote", 2)}</w:p>'
            + np('b3') + sect(''))
    parts = [('footnotes.xml', 'footnotes+xml', 'footnotes', 'rIdFn', fn), ('endnotes.xml', 'endnotes+xml', 'endnotes', 'rIdEn', en)]
    build(os.path.join(out, 'story-notes.docx'), body, parts)


def case_comments(out):
    cm = (f'<w:comments {NS}><w:comment w:id="0" w:author="P" w:date="2024-01-01T00:00:00Z" w:initials="P">{np("cm1a")}{np("cm1b")}</w:comment>'
          f'<w:comment w:id="1" w:author="P" w:date="2024-01-01T00:00:00Z" w:initials="P">{np("cm2a")}</w:comment></w:comments>')
    c = lambda i, t: f'<w:p><w:commentRangeStart w:id="{i}"/><w:r><w:t>{t}</w:t></w:r><w:commentRangeEnd w:id="{i}"/><w:r><w:commentReference w:id="{i}"/></w:r></w:p>'
    body = np('b1') + c(0, 'anchor0') + np('b2') + c(1, 'anchor1') + sect('')
    build(os.path.join(out, 'story-comments.docx'), body, [('comments.xml', 'comments+xml', 'comments', 'rIdCm', cm)])


def content(tags):
    return '<w:txbxContent>' + ''.join(np(t) for t in tags) + '</w:txbxContent>'


def dml_box(tags, ident):
    return (f'<w:r><mc:AlternateContent><mc:Choice Requires="wps"><w:drawing><wp:anchor distT="0" distB="0" distL="0" distR="0" simplePos="0" relativeHeight="1" behindDoc="0" locked="0" layoutInCell="1" allowOverlap="1">'
            '<wp:simplePos x="0" y="0"/><wp:positionH relativeFrom="column"><wp:posOffset>3000000</wp:posOffset></wp:positionH><wp:positionV relativeFrom="paragraph"><wp:posOffset>0</wp:posOffset></wp:positionV>'
            f'<wp:extent cx="2000000" cy="1000000"/><wp:wrapSquare wrapText="bothSides"/><wp:docPr id="{ident}" name="Box {ident}"/><wp:cNvGraphicFramePr/>'
            '<a:graphic><a:graphicData uri="http://schemas.microsoft.com/office/word/2010/wordprocessingShape"><wps:wsp><wps:cNvSpPr txBox="1"/>'
            '<wps:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="2000000" cy="1000000"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></wps:spPr>'
            f'<wps:txbx>{content(tags)}</wps:txbx><wps:bodyPr/></wps:wsp></a:graphicData></a:graphic></wp:anchor></w:drawing></mc:Choice>'
            f'<mc:Fallback><w:pict><v:shape id="fb{ident}" type="#_x0000_t202" style="position:absolute;margin-left:236pt;margin-top:0;width:157pt;height:79pt;z-index:{ident}"><v:textbox>{content(tags)}</v:textbox></v:shape></w:pict></mc:Fallback>'
            '</mc:AlternateContent></w:r>')


def vml_box(tags, ident):
    return (f'<w:r><w:pict><v:shape id="vb{ident}" type="#_x0000_t202" style="position:absolute;margin-left:236pt;margin-top:0;width:157pt;height:79pt;z-index:{ident}">'
            f'<v:textbox>{content(tags)}</v:textbox></v:shape></w:pict></w:r>')


def case_textboxes(out):
    for name, box in (('dml', dml_box), ('vml', vml_box)):
        def anchor(tag, boxes):
            return f'<w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="1"/></w:numPr></w:pPr><w:r><w:t>{tag}</w:t></w:r>{boxes}</w:p>'
        body = (anchor('b1', box(['t1a', 't1b'], 1)) + anchor('b2', box(['t2a', 't2b'], 2)) + np('b3') + sect(''))
        build(os.path.join(out, f'story-textbox-{name}.docx'), body)


if __name__ == '__main__':
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    case_headers_footers(out)
    case_notes(out)
    case_comments(out)
    case_textboxes(out)
    print('wrote', len(os.listdir(out)), 'files to', out)
