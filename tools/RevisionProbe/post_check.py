#!/usr/bin/env python3
"""Checks the files `RevisionProbe batch` wrote: untouched parts and schema validity.

  post_check.py CENSUS_JSONL CORPUS_DIR PROBE_OUT_DIR ROUNDTRIP_DLL VALIDATE_PY [--schemas DIR]... --json FILE

For every file with tracked changes and each of its accepted and rejected results:
* RoundTrip compares the result with the source: a part may change only when the census found revisions in it, and every
  other entry must be copied as stored;
* the schema validator runs on the source and on the result: the result may not have more invalid parts than the source.
Prints one summary line of counts; every finding goes to the JSON-lines file.
"""
import json, os, subprocess, sys


def run_json(cmd):
    p = subprocess.run(cmd, capture_output=True, text=True)
    rows = []
    for line in p.stdout.splitlines():
        try:
            rows.append(json.loads(line))
        except ValueError:
            pass
    return rows


def invalid_parts(validate, schemas, path):
    cmd = [sys.executable, validate] + [x for s in schemas for x in ('--schemas', s)] + ['--quiet', path]
    rows = run_json(cmd)
    return sorted(r['uri'] for r in rows if r.get('kind') == 'part' and r.get('status') == 'invalid')


def main():
    census, corpus, probe, rt, validate = sys.argv[1:6]
    schemas = [sys.argv[i + 1] for i, a in enumerate(sys.argv) if a == '--schemas']
    out = sys.argv[sys.argv.index('--json') + 1]
    tally = {'results': 0, 'unexpectedPartChange': 0, 'rawNotPreserved': 0, 'validityRegression': 0, 'sourceInvalid': 0}
    with open(out, 'w') as w:
        for line in open(census):
            r = json.loads(line)
            if r.get('status') != 'ok' or not r.get('revisions'):
                continue
            src = os.path.join(corpus, r['file'])
            ident = r['file'].replace('/', '_')
            bad_src = invalid_parts(validate, schemas, src)
            if bad_src:
                tally['sourceInvalid'] += 1
            for mode in ('accepted', 'rejected'):
                res = os.path.join(probe, f'{ident}.{mode}.docx')
                if not os.path.exists(res):
                    continue
                tally['results'] += 1
                rows = run_json(['dotnet', rt, src, res, '--quiet'])
                changed = [x['uri'] for x in rows if x.get('kind') == 'part' and x.get('status') != 'identical']
                unexpected = [u for u in changed if u not in r['parts']]
                summary = next((x for x in rows if x.get('kind') == 'summary'), {})
                bad_res = invalid_parts(validate, schemas, res)
                regress = [u for u in bad_res if u not in bad_src]
                finding = {}
                if unexpected:
                    tally['unexpectedPartChange'] += 1
                    finding['unexpectedChanged'] = unexpected
                if summary.get('rawNotPreserved'):
                    tally['rawNotPreserved'] += 1
                    finding['rawNotPreserved'] = summary['rawNotPreserved']
                if regress:
                    tally['validityRegression'] += 1
                    finding['newlyInvalid'] = regress
                if finding:
                    finding.update(file=r['file'], mode=mode)
                    w.write(json.dumps(finding) + '\n')
    print(json.dumps(tally))


main()
