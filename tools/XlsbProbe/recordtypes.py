"""Prints the first few records of the given types from every .xlsb in a directory (a layout check against real files)."""
import sys, zipfile, collections, glob, os
sys.path.insert(0, os.path.dirname(__file__))
from dump import recs

want = set(int(x) for x in sys.argv[2].split(','))
limit = int(sys.argv[3]) if len(sys.argv) > 3 else 3
seen = collections.defaultdict(list)
for f in sorted(glob.glob(sys.argv[1] + '/*.xlsb')):
    try:
        z = zipfile.ZipFile(f)
    except Exception:
        continue
    for n in z.namelist():
        if not n.endswith('.bin'):
            continue
        try:
            for s, t, d in recs(z.read(n)):
                if t in want and len(seen[t]) < limit:
                    seen[t].append((os.path.basename(f)[:6], n.split('/')[-1], len(d), d[:70].hex()))
        except Exception:
            pass
for t in sorted(seen):
    for x in seen[t]:
        print(t, *x)
