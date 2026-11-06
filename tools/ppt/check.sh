#!/usr/bin/env bash
# Converts a directory of PowerPoint 97-2003 files to PresentationML with PptProbe, validates every converted part against the pinned
# schemas and asks LibreOffice to open a sample of the results.
#
# usage: tools/ppt/check.sh <ppt-dir> <out-dir> [lo-sample-size]
# env:   ONEDOCUMENT_HOME, ONEDOCUMENT_XSD (schema directories, path separated), PPTPROBE_DLL (built PptProbe.dll)
set -uo pipefail
in=${1:?usage: check.sh <ppt-dir> <out-dir> [lo-sample]}
out=${2:?}
sample=${3:-20}
home=${ONEDOCUMENT_HOME:?set ONEDOCUMENT_HOME}
here=$(cd "$(dirname "$0")" && pwd)
mkdir -p "$out/pptx" "$out/pdf"
dotnet "${PPTPROBE_DLL:?set PPTPROBE_DLL}" convert "$in" "$out/pptx" > "$out/convert.log" 2>&1
tail -1 "$out/convert.log"
schemas=()
IFS=: read -ra dirs <<< "${ONEDOCUMENT_XSD:?set ONEDOCUMENT_XSD}"
for d in "${dirs[@]}"; do schemas+=(--schemas "$d"); done
python3 "$here/../validate/validate_ooxml.py" "${schemas[@]}" --json "$out/validate.jsonl" --quiet "$out"/pptx/*.pptx > /dev/null 2>&1
python3 - "$out/validate.jsonl" <<'PY'
import collections, json, sys
files = valid = invalid = 0
errs = collections.Counter()
known = collections.Counter()  # the pinned Transitional schema types buSzPct as "N%"; Office writes and reads integers (1/1000 percent)
bad_files = set()
for line in open(sys.argv[1]):
    r = json.loads(line)
    if r.get("kind") == "summary":
        files += 1
    elif r.get("status") == "invalid":
        for e in r["errors"]:
            if "buSzPct" in e["message"] and "pattern" in e["message"]:
                known["buSzPct integer"] += 1
            else:
                errs[e["message"][:140]] += 1
                bad_files.add(r["file"])
print("schema: files", files, "with errors", len(bad_files), "known buSzPct integer-percent errors", known["buSzPct integer"])
for m, n in errs.most_common(12): print("  ", n, m)
PY
export LO_PROFILE="$out/.lo-profile"
n=0
for f in $(ls "$out"/pptx/*.pptx | head -n "$sample"); do
  timeout 120 "$home/oracles/soffice.sh" --convert-to pdf --outdir "$out/pdf" "$f" > /dev/null 2>&1 && n=$((n+1))
done
echo "libreoffice opened: $n of $(ls "$out"/pptx/*.pptx | head -n "$sample" | wc -l)"
