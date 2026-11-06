#!/usr/bin/env python3
"""Paired validation: parts that are invalid in the output but valid in the input (information)."""
import json, os, subprocess, sys, collections
home = os.path.expanduser('~/oneoffice')
py = home + '/oracles/venv/bin/python'
val = 'tools/validate/validate_ooxml.py'
schemas = ['--schemas', home + '/schemas/xsd/transitional', '--schemas', home + '/schemas/xsd/opc']

def invalid_parts(path):
    out = subprocess.run([py, val, *schemas, path], capture_output=True, text=True).stdout
    res = {}
    for line in out.splitlines():
        try:
            j = json.loads(line)
        except Exception:
            continue
        if j.get('kind') == 'part' and j.get('status') == 'invalid':
            res[j['uri']] = j['errors'][:1]
    return res

R = home + '/u84/probe'
newly = collections.Counter()
examples = {}
checked = 0
for name, corpus in [('commoncrawl-docx', 'commoncrawl/docx'), ('bugzilla-documentfoundation-docx', 'bugzilla/documentfoundation/docx'), ('commoncrawl-xlsx', 'commoncrawl/xlsx'), ('lo-pptx', 'lo-pptx')]:
    n = 0
    for line in open(f'{R}/{name}.jsonl'):
        j = json.loads(line)
        if j.get('kind') == 'summary' or j['status'] != 'ok':
            continue
        n += 1
        if n > 40:
            break
        before = invalid_parts(f"{home}/corpus/{corpus}/{j['file']}")
        after = invalid_parts(f"{R}/{name}/{j['output']}")
        checked += 1
        for uri, err in after.items():
            if uri not in before:
                key = (name, uri.split('/')[1] + '/' + os.path.basename(uri).rstrip('0123456789.xml'))
                newly[key] += 1
                examples.setdefault(key, (j['file'], err[0]['message'][:200] if err else ''))
print('pairs checked', checked)
for k, c in newly.most_common():
    print(c, k, examples[k])
