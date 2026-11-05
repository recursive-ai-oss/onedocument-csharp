#!/usr/bin/env python3
"""Writes small documents that ask Word what it does with an inline wrapper (content control, simple field, hyperlink, smart tag,
custom XML) whose whole content is a tracked change that is then rejected or accepted.

Each NAME-ins.docx holds "before [wrapper with only inserted text] after"; reject-all must empty the wrapper. Each NAME-del.docx holds
the same with only deleted text; accept-all must empty it. NAME.actions.txt asks for both. Compare the saved documents:
see check-wrappers.py.

Usage: make-wrapper-cases.py OUTDIR
"""
import importlib.util, os, sys

here = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location('mwc', os.path.join(here, 'make-word-cases.py'))
mwc = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mwc)

AUTH = mwc.AUTH
INS = f'<w:ins w:id="{{id}}" {AUTH}><w:r><w:t>new</w:t></w:r></w:ins>'
DEL = f'<w:del w:id="{{id}}" {AUTH}><w:r><w:delText>old</w:delText></w:r></w:del>'

WRAPPERS = {
    'sdt-text': '<w:sdt><w:sdtPr><w:alias w:val="Name"/><w:tag w:val="name"/><w:id w:val="101"/><w:text/></w:sdtPr><w:sdtContent>{c}</w:sdtContent></w:sdt>',
    'sdt-bare': '<w:sdt><w:sdtPr/><w:sdtContent>{c}</w:sdtContent></w:sdt>',
    'sdt-richtext': '<w:sdt><w:sdtPr><w:id w:val="102"/><w:richText/></w:sdtPr><w:sdtContent>{c}</w:sdtContent></w:sdt>',
    'sdt-placeholder': '<w:sdt><w:sdtPr><w:id w:val="103"/><w:showingPlcHdr/><w:text/></w:sdtPr><w:sdtContent>{c}</w:sdtContent></w:sdt>',
    'fldsimple': '<w:fldSimple w:instr=" DATE \\@ &quot;yyyy&quot; ">{c}</w:fldSimple>',
    'hyperlink-anchor': '<w:hyperlink w:anchor="_Top">{c}</w:hyperlink>',
    'smarttag': '<w:smartTag w:uri="urn:test" w:element="thing">{c}</w:smartTag>',
    'customxml': '<w:customXml w:uri="urn:test" w:element="thing">{c}</w:customXml>',
}


def para(wrapper, content):
    return ('<w:p><w:r><w:t xml:space="preserve">before </w:t></w:r>' + wrapper.format(c=content) +
            '<w:r><w:t xml:space="preserve"> after</w:t></w:r></w:p>')


def main():
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    for name, wrapper in WRAPPERS.items():
        for kind, content in (('ins', INS), ('del', DEL)):
            base = f'{out}/wrap-{name}-{kind}'
            mwc.build(base + '.docx', para(wrapper, content.format(id=1)))
            with open(base + '.actions.txt', 'w') as f:
                f.write('accept-all-stories\nreject-all-stories\n')
    # Block-level content control whose only paragraph was inserted, and the same inside a table cell.
    blk = ('<w:sdt><w:sdtPr><w:id w:val="110"/><w:richText/></w:sdtPr><w:sdtContent><w:p><w:pPr><w:rPr><w:ins w:id="2" ' + AUTH + '/></w:rPr></w:pPr>'
           '<w:ins w:id="3" ' + AUTH + '><w:r><w:t>new block</w:t></w:r></w:ins></w:p></w:sdtContent></w:sdt>')
    mwc.build(f'{out}/wrap-sdt-block-ins.docx', mwc.p('before') + blk + mwc.p('after'))
    with open(f'{out}/wrap-sdt-block-ins.actions.txt', 'w') as f:
        f.write('accept-all-stories\nreject-all-stories\n')


if __name__ == '__main__':
    main()
