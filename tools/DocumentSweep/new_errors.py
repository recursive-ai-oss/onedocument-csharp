#!/usr/bin/env python3
"""Reports the schema errors an edit introduced: validates each edited file and its original and lists the errors the edited one has
and the original does not (compared by part and message, ignoring line numbers).

usage: new_errors.py --results results.jsonl --input DIR --out DIR --validator validate_ooxml.py [--schemas DIR]...
"""
import argparse, collections, json, os, subprocess, sys

ap = argparse.ArgumentParser()
ap.add_argument('--results', required=True)
ap.add_argument('--input', required=True)
ap.add_argument('--out', required=True)
ap.add_argument('--validator', required=True)
ap.add_argument('--schemas', action='append', default=[])
a = ap.parse_args()

rows = [json.loads(l) for l in open(a.results) if l.strip()]
rows = [r for r in rows if r['status'] == 'saved' and r.get('edited')]


def errors(files, tag):
    found = collections.defaultdict(set)
    tmp = os.path.join(a.out, 'validate-%s.jsonl' % tag)
    cmd = [sys.executable, a.validator, '--quiet', '--json', tmp] + [x for s in a.schemas for x in ('--schemas', s)] + files
    subprocess.run(cmd, capture_output=True, text=True)
    for line in open(tmp):
        j = json.loads(line)
        if j.get('kind') == 'part' and j.get('errors'):
            for e in j['errors']:
                found[os.path.basename(j['file'])].add((j['uri'], e['message']))
    return found


edited = [os.path.join(a.out, r['file'].replace(os.sep, '_')) for r in rows]
originals = [os.path.join(a.input, r['file']) for r in rows]
# Originals may share a base name across directories, so validate them under the edited name via a link.
links = []
linkdir = os.path.join(a.out, 'orig-links')
os.makedirs(linkdir, exist_ok=True)
for r, o in zip(rows, originals):
    l = os.path.join(linkdir, r['file'].replace(os.sep, '_'))
    if not os.path.exists(l):
        os.symlink(os.path.abspath(o), l)
    links.append(l)
before = errors(links, 'orig')
after = errors(edited, 'edited')
new = {f: sorted(s - before.get(f, set())) for f, s in after.items() if s - before.get(f, set())}
print(f'edited files {len(rows)}; with schema errors before {len(before)}, after {len(after)}; files with new errors {len(new)}')
for f, s in list(new.items())[:20]:
    print(' ', f, s[:3])
