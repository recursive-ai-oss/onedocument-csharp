"""Compares the cell values our reader gives for each binary workbook with what LibreOffice (run as a black box) writes as CSV.

usage: compare_lo.py <dir-with-xlsb> <work-dir> <XlsbProbe.dll> <soffice.sh>
Information only: LibreOffice and this reader may format a few values differently (dates, long numbers).
"""
import csv, glob, os, subprocess, sys

src, work, probe, soffice = sys.argv[1:5]
os.makedirs(work, exist_ok=True)

def cells(path):
    out = {}
    with open(path, newline='', encoding='utf-8-sig') as f:
        for r, row in enumerate(csv.reader(f)):
            for c, v in enumerate(row):
                if v != '':
                    out[(r, c)] = v
    return out

def same(a, b):
    if a == b:
        return True
    try:
        x, y = float(a), float(b)
        return abs(x - y) <= 1e-9 * max(1.0, abs(x), abs(y))
    except ValueError:
        pass
    return a.strip().lower() == b.strip().lower()

total = match = files = 0
shown = 0
for f in sorted(glob.glob(os.path.join(src, '**', '*.xlsb'), recursive=True)):
    base = os.path.basename(f)[:8]
    ours = os.path.join(work, base + '-ours')
    lo = os.path.join(work, base + '-lo')
    os.makedirs(lo, exist_ok=True)
    subprocess.run(['dotnet', probe, 'csv', f, ours], check=False, capture_output=True)
    env = dict(os.environ, LO_PROFILE=os.path.join(work, 'profile'))
    subprocess.run([soffice, '--headless', '--convert-to', 'csv:Text - txt - csv (StarCalc):44,34,76,1,,0,false,false,false,false,false,-1', '--outdir', lo, f],
                   check=False, capture_output=True, env=env, timeout=300)
    lofiles = sorted(glob.glob(os.path.join(lo, '*.csv')))
    ourfiles = sorted(glob.glob(os.path.join(ours, '*.csv')), key=lambda p: int(os.path.basename(p)[:-4]))
    t = m = 0
    lodata = [cells(p) for p in lofiles]
    used = set()
    for i, of in enumerate(ourfiles):
        a = cells(of)
        best = None
        for j, b in enumerate(lodata):
            if j in used:
                continue
            eq = sum(1 for k in a if k in b and same(a[k], b[k]))
            if best is None or eq > best[0]:
                best = (eq, j)
        b = lodata[best[1]] if best else {}
        if best:
            used.add(best[1])
        for k in set(a) | set(b):
            t += 1
            if k in a and k in b and same(a[k], b[k]):
                m += 1
            elif os.environ.get('SHOW') == base and shown < 12:
                shown += 1
                print('   ', i, k, repr(a.get(k)), repr(b.get(k)))
    files += 1
    total += t
    match += m
    print(f'{base} ours-sheets={len(ourfiles)} lo-sheets={len(lofiles)} cells={t} equal={m}')
print(f'files={files} cells={total} equal={match}')
