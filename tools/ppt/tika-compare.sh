#!/usr/bin/env bash
# Information only: compares the text OneDocument extracts from PowerPoint 97-2003 files with Apache Tika's, as bags of words.
#
# usage: tools/ppt/tika-compare.sh <ppt-dir> <work-dir>
# env:   ONEDOCUMENT_HOME (oracles under $ONEDOCUMENT_HOME/oracles: jre/, tika-3.3.3/), PPTPROBE_DLL (built PptProbe.dll)
set -uo pipefail
in=${1:?usage: tika-compare.sh <ppt-dir> <work-dir>}
work=${2:?}
home=${ONEDOCUMENT_HOME:?set ONEDOCUMENT_HOME}
mkdir -p "$work/tika" "$work/ours"
java=$(ls "$home"/oracles/jre/*/bin/java "$home"/oracles/jre/bin/java 2>/dev/null | head -1)
jar=$(ls "$home"/oracles/tika-3.3.3/*.jar | head -1)
# Tika's batch mode starts child JVMs through PATH, and reads a directory: give it only the presentations.
export PATH="$(dirname "$java"):$PATH"
rm -rf "$work/in"; mkdir -p "$work/in"
for f in "$in"/*.ppt; do ln -s "$f" "$work/in/$(basename "$f")"; done
"$java" -jar "$jar" -t -i "$work/in" -o "$work/tika" > "$work/tika.log" 2>&1
dotnet "${PPTPROBE_DLL:?set PPTPROBE_DLL}" textdir "$in" "$work/ours"
python3 - "$work" <<'PY'
import os, re, sys, statistics
work = sys.argv[1]
def words(path):
    try: text = open(path, encoding="utf-8", errors="replace").read()
    except OSError: return None
    return [w for w in re.findall(r"\w+", text.lower())]
recalls, precisions, rows = [], [], []
for name in sorted(os.listdir(os.path.join(work, "ours"))):
    stem = name[:-4]
    ours = words(os.path.join(work, "ours", name))
    tika = None
    for cand in (stem + ".ppt.txt", stem + ".txt", stem + ".ppt"):
        p = os.path.join(work, "tika", cand)
        if os.path.exists(p): tika = words(p); break
    if ours is None or tika is None or not tika: continue
    from collections import Counter
    t, o = Counter(tika), Counter(ours)
    common = sum((t & o).values())
    r = common / sum(t.values()); pr = common / max(1, sum(o.values()))
    recalls.append(r); precisions.append(pr); rows.append((r, pr, stem, sum(t.values()), sum(o.values())))
print("files compared", len(rows))
if rows:
    print("mean recall of Tika's words %.3f, median %.3f; mean precision %.3f, median %.3f" % (statistics.mean(recalls), statistics.median(recalls), statistics.mean(precisions), statistics.median(precisions)))
    print("recall >= 0.99:", sum(1 for r in recalls if r >= 0.99), "; < 0.8:", sum(1 for r in recalls if r < 0.8))
    for r, pr, stem, nt, no in sorted(rows)[:8]: print("  lowest recall %.2f precision %.2f %s tika=%d ours=%d" % (r, pr, stem, nt, no))
PY
