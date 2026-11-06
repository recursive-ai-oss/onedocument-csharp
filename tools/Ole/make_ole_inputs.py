#!/usr/bin/env python3
"""Writes small OpenDocument files with document properties and an embedded object, for a word processor, a spreadsheet and a
presentation program to save as Word, Excel and PowerPoint 97-2003 files.

Usage: make_ole_inputs.py <out dir>
Writes ole-props.odt, ole-props.ods and ole-props.odp. The files are deterministic. make_ole_fixtures.sh converts them with a pinned
LibreOffice build; the results are the fixtures of the property and embedded-object tests (see testdata/ole/README.md).
"""
import struct, sys, zipfile, zlib

NS = ('xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0" xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0" '
      'xmlns:table="urn:oasis:names:tc:opendocument:xmlns:table:1.0" xmlns:draw="urn:oasis:names:tc:opendocument:xmlns:drawing:1.0" '
      'xmlns:xlink="http://www.w3.org/1999/xlink" xmlns:svg="urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0" '
      'xmlns:presentation="urn:oasis:names:tc:opendocument:xmlns:presentation:1.0" xmlns:style="urn:oasis:names:tc:opendocument:xmlns:style:1.0" '
      'xmlns:fo="urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0" xmlns:meta="urn:oasis:names:tc:opendocument:xmlns:meta:1.0" '
      'xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:manifest="urn:oasis:names:tc:opendocument:xmlns:manifest:1.0" '
      'xmlns:office-ooo="http://openoffice.org/2004/office" office:version="1.2"')

CALC = 'application/vnd.oasis.opendocument.spreadsheet'


def png():
    def chunk(t, d):
        c = struct.pack('>I', len(d)) + t + d
        return c + struct.pack('>I', zlib.crc32(t + d) & 0xFFFFFFFF)
    raw = b''.join(b'\x00' + bytes([30, 120, 200] * 16) for _ in range(16))
    return b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', 16, 16, 8, 2, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(raw)) + chunk(b'IEND', b'')


def meta(title):
    return ('<?xml version="1.0" encoding="UTF-8"?><office:document-meta %s><office:meta>'
            '<dc:title>%s</dc:title><dc:subject>Embedded objects</dc:subject><dc:description>Fixture with properties</dc:description>'
            '<meta:keyword>ole</meta:keyword><meta:keyword>fixture</meta:keyword>'
            '<meta:initial-creator>Ada Lovelace</meta:initial-creator><dc:creator>Grace Hopper</dc:creator>'
            '<meta:creation-date>2020-01-02T03:04:05</meta:creation-date><dc:date>2021-06-07T08:09:10</dc:date>'
            '<meta:editing-cycles>3</meta:editing-cycles>'
            '<meta:user-defined meta:name="Project">Atlas</meta:user-defined>'
            '<meta:user-defined meta:name="Count" meta:value-type="float">42</meta:user-defined>'
            '<meta:user-defined meta:name="Reviewed" meta:value-type="boolean">true</meta:user-defined>'
            '<meta:user-defined meta:name="Due" meta:value-type="date">2022-02-03T04:05:06</meta:user-defined>'
            '</office:meta></office:document-meta>') % (NS, title)


def sheet_object():
    return ('<?xml version="1.0" encoding="UTF-8"?><office:document-content %s><office:body><office:spreadsheet>'
            '<table:table table:name="Data"><table:table-row><table:table-cell office:value-type="string"><text:p>Item</text:p></table:table-cell>'
            '<table:table-cell office:value-type="string"><text:p>Qty</text:p></table:table-cell></table:table-row>'
            '<table:table-row><table:table-cell office:value-type="string"><text:p>Apples</text:p></table:table-cell>'
            '<table:table-cell office:value-type="float" office:value="3"><text:p>3</text:p></table:table-cell></table:table-row>'
            '</table:table></office:spreadsheet></office:body></office:document-content>') % NS


def manifest(main_type):
    return ('<?xml version="1.0" encoding="UTF-8"?><manifest:manifest %s>'
            '<manifest:file-entry manifest:full-path="/" manifest:media-type="%s"/>'
            '<manifest:file-entry manifest:full-path="content.xml" manifest:media-type="text/xml"/>'
            '<manifest:file-entry manifest:full-path="meta.xml" manifest:media-type="text/xml"/>'
            '<manifest:file-entry manifest:full-path="Object 1/" manifest:media-type="%s"/>'
            '<manifest:file-entry manifest:full-path="Object 1/content.xml" manifest:media-type="text/xml"/>'
            '<manifest:file-entry manifest:full-path="ObjectReplacements/Object 1" manifest:media-type="image/png"/>'
            '</manifest:manifest>') % (NS, main_type, CALC)


def write(path, main_type, content):
    with zipfile.ZipFile(path, 'w', zipfile.ZIP_DEFLATED) as z:
        def put(name, data, deflate=True):
            zi = zipfile.ZipInfo(name, (2020, 1, 1, 0, 0, 0))
            zi.compress_type = zipfile.ZIP_DEFLATED if deflate else zipfile.ZIP_STORED
            z.writestr(zi, data)
        put('mimetype', main_type, deflate=False)
        put('content.xml', content)
        put('meta.xml', meta('Fixture ' + path.rsplit('.', 1)[-1]))
        put('META-INF/manifest.xml', manifest(main_type))
        put('Object 1/content.xml', sheet_object())
        put('ObjectReplacements/Object 1', png())


def main(out):
    odt = ('<?xml version="1.0" encoding="UTF-8"?><office:document-content %s><office:body><office:text>'
           '<text:p>Before the object</text:p>'
           '<text:p><draw:frame draw:name="Object 1" text:anchor-type="as-char" svg:width="8cm" svg:height="3cm">'
           '<draw:object xlink:href="./Object 1" xlink:type="simple" xlink:show="embed" xlink:actuate="onLoad"/>'
           '<draw:image xlink:href="./ObjectReplacements/Object 1" xlink:type="simple" xlink:show="embed" xlink:actuate="onLoad"/></draw:frame></text:p>'
           '<text:p>After the object</text:p></office:text></office:body></office:document-content>') % NS
    write(out + '/ole-props.odt', 'application/vnd.oasis.opendocument.text', odt)

    ods = ('<?xml version="1.0" encoding="UTF-8"?><office:document-content %s><office:body><office:spreadsheet>'
           '<table:table table:name="Sheet1"><table:table-row><table:table-cell office:value-type="string"><text:p>Before the object</text:p></table:table-cell></table:table-row>'
           '<table:table-row><table:table-cell office:value-type="float" office:value="7"><text:p>7</text:p></table:table-cell></table:table-row>'
           '<table:shapes><draw:frame draw:name="Object 1" table:end-cell-address="Sheet1.D8" table:end-x="0cm" table:end-y="0cm" svg:x="2cm" svg:y="2cm" svg:width="8cm" svg:height="3cm" table:table-background="false">'
           '<draw:object xlink:href="./Object 1" xlink:type="simple" xlink:show="embed" xlink:actuate="onLoad"/>'
           '<draw:image xlink:href="./ObjectReplacements/Object 1" xlink:type="simple" xlink:show="embed" xlink:actuate="onLoad"/></draw:frame></table:shapes>'
           '</table:table></office:spreadsheet></office:body></office:document-content>') % NS
    write(out + '/ole-props.ods', 'application/vnd.oasis.opendocument.spreadsheet', ods)

    odp = ('<?xml version="1.0" encoding="UTF-8"?><office:document-content %s><office:body><office:presentation>'
           '<draw:page draw:name="Slide 1"><draw:frame draw:name="Object 1" svg:x="2cm" svg:y="4cm" svg:width="8cm" svg:height="3cm">'
           '<draw:object xlink:href="./Object 1" xlink:type="simple" xlink:show="embed" xlink:actuate="onLoad"/>'
           '<draw:image xlink:href="./ObjectReplacements/Object 1" xlink:type="simple" xlink:show="embed" xlink:actuate="onLoad"/></draw:frame></draw:page>'
           '</office:presentation></office:body></office:document-content>') % NS
    write(out + '/ole-props.odp', 'application/vnd.oasis.opendocument.presentation', odp)


if __name__ == '__main__':
    main(sys.argv[1])
