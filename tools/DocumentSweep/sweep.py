#!/usr/bin/env python3
"""Open and save every document under a directory through Document, then compare each result with its input using RoundTrip.

usage: sweep.py --input DIR --out DIR --sweep DocumentSweep.dll --roundtrip RoundTrip.dll [--edit] [--limit N] [--jobs N]

Writes results.jsonl (one line per file) and prints a summary: files, opened, saved, failures by exception and code, and per part
counts (identical / changed / raw entries preserved). With --edit, every saved file must differ from its input in word/document.xml
only (and the content-types and relationship parts are reported when they change).
"""
import argparse, collections, concurrent.futures, json, os, subprocess, sys


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--input', required=True)
    ap.add_argument('--out', required=True)
    ap.add_argument('--sweep', required=True)
    ap.add_argument('--roundtrip', required=True)
    ap.add_argument('--edit', action='store_true')
    ap.add_argument('--limit', type=int, default=0)
    ap.add_argument('--jobs', type=int, default=4)
    ap.add_argument('--dotnet', default='dotnet')
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    cmd = [a.dotnet, a.sweep, a.input, a.out] + (['--edit'] if a.edit else []) + (['--limit', str(a.limit)] if a.limit else [])
    res = subprocess.run(cmd, capture_output=True, text=True)
    rows = [json.loads(l) for l in res.stdout.splitlines() if l.startswith('{')]
    sys.stderr.write(res.stderr[-2000:])

    def compare(row):
        if row['status'] != 'saved':
            return row
        before = os.path.join(a.input, row['file'])
        after = os.path.join(a.out, row['file'].replace(os.sep, '_'))
        r = subprocess.run([a.dotnet, a.roundtrip, before, after, '--json', after + '.rt.jsonl', '--quiet'], capture_output=True, text=True)
        parts = [json.loads(l) for l in open(after + '.rt.jsonl') if l.strip()]
        os.remove(after + '.rt.jsonl')
        row['changed'] = sorted(p['uri'] for p in parts if p.get('kind') == 'part' and p['status'] != 'identical')
        s = parts[-1]
        row['identical'] = s['identical']
        row['rawNotPreserved'] = s['rawNotPreserved']
        row['rawPreserved'] = s['rawPreserved']
        row['ctDiffs'] = s['contentTypeDiffs']
        return row

    with concurrent.futures.ThreadPoolExecutor(a.jobs) as ex:
        rows = list(ex.map(compare, rows))
    with open(os.path.join(a.out, 'results.jsonl'), 'w') as f:
        for r in rows:
            f.write(json.dumps(r) + '\n')

    saved = [r for r in rows if r['status'] == 'saved']
    fails = collections.Counter((r.get('exception'), r.get('code')) for r in rows if r['status'] != 'saved')
    print(f"files {len(rows)} saved {len(saved)} failed {len(rows) - len(saved)}")
    for (e, c), n in fails.most_common():
        print(f"  {n:5d} {e} {c}")
    if not a.edit:
        clean = [r for r in saved if not r['changed'] and r['rawNotPreserved'] == 0]
        print(f"untouched round trip: {len(clean)}/{len(saved)} files with every part byte-identical and every raw entry preserved")
        print(f"parts: {sum(r['identical'] for r in saved)} identical, {sum(len(r['changed']) for r in saved)} differing; raw entries preserved {sum(r['rawPreserved'] for r in saved)}, not preserved {sum(r['rawNotPreserved'] for r in saved)}")
        for r in [r for r in saved if r['changed'] or r['rawNotPreserved']][:20]:
            print('  diff', r['file'], r['changed'][:5], r['rawNotPreserved'])
    else:
        edited = [r for r in saved if r['edited']]
        only = [r for r in edited if r['changed'] == ['/word/document.xml'] or r['changed'] == ['word/document.xml']]
        print(f"edited {len(edited)} files; only the main part changed in {len(only)}")
        for r in [r for r in edited if r not in only][:20]:
            print('  other', r['file'], r['changed'][:6])
    return 0


sys.exit(main())
