#!/usr/bin/env python3
"""Reports, for each wrapper case, which wrapper elements are left in the document Word saved after accept-all and reject-all.

Usage: check-wrappers.py RESULTS_DIR   (the directory office-oracle.sh run wrote; it holds NAME.accept-all-stories.docx and so on)
"""
import glob, os, re, sys, zipfile

TAGS = ['sdt', 'fldSimple', 'hyperlink', 'smartTag', 'customXml']
for path in sorted(glob.glob(os.path.join(sys.argv[1], '*-all-stories.docx'))):
    xml = zipfile.ZipFile(path).read('word/document.xml').decode('utf8')
    body = xml[xml.index('<w:body'):]
    left = [t for t in TAGS if re.search(r'<w:%s[ >/]' % t, body)]
    texts = re.findall(r'<w:t[ >][^<]*|<w:t>[^<]*', body)
    print(os.path.basename(path), 'wrappers left:', left or '-', '| text:', ''.join(t.split('>')[-1] for t in texts))
