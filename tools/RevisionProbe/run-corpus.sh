#!/usr/bin/env bash
# Runs the revision census and accept-all/reject-all over a directory of Word files, then compares the text with
# LibreOffice's. Environment: DOTNET (a dotnet on PATH is used otherwise), CORPUS (input directory), OUT (work directory),
# SOFFICE (path of soffice), SHARDS (default 4).
set -u
HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$HERE/../.."
SHARDS="${SHARDS:-4}"
mkdir -p "$OUT/probe" "$OUT/lo"
if [ -z "${SKIP_CENSUS:-}" ]; then
dotnet build -c Release -m:2 -v quiet -clp:NoSummary "$HERE/RevisionProbe.csproj" -o "$OUT/bin" || exit 1
for i in $(seq 0 $((SHARDS - 1))); do
  nice dotnet "$OUT/bin/RevisionProbe.dll" batch "$CORPUS" "$OUT/probe" --shard "$i/$SHARDS" > "$OUT/census-$i.jsonl" 2> "$OUT/census-$i.err" &
done
wait
cat "$OUT"/census-*.jsonl > "$OUT/census.jsonl"
fi
# Files with revisions, as LibreOffice names them.
python3 - "$OUT" "$CORPUS" <<'P'
import json, os, sys
out, corpus = sys.argv[1], sys.argv[2]
with open(os.path.join(out, "with-revisions.txt"), "w") as w:
    for line in open(os.path.join(out, "census.jsonl")):
        r = json.loads(line)
        if r.get("revisions", 0) > 0:
            w.write(os.path.join(corpus, r["file"]) + "\n")
P
if [ -n "${SOFFICE:-}" ] && [ -s "$OUT/with-revisions.txt" ]; then
  # LibreOffice names its output after the file; the probe names its after the relative path with separators replaced.
  mkdir -p "$OUT/lo-in"
  while read -r f; do ln -sf "$f" "$OUT/lo-in/$(echo "${f#"$CORPUS"/}" | tr '/' '_')"; done < "$OUT/with-revisions.txt"
  for mode in accept reject; do
    LO_PROFILE="$OUT/lo-profile" timeout 1500 ${PYUNO:-python3} "$HERE/lo_resolve.py" $mode "$OUT/lo" "$OUT"/lo-in/* > "$OUT/lo-$mode.log" 2>&1
  done
  python3 "$HERE/compare_lo.py" "$OUT/probe" "$OUT/lo" --json "$OUT/compare.jsonl"
fi

# Untouched parts and schema validity of the results (needs the schema directories in XSD_DIRS, space separated).
if [ -n "${XSD_DIRS:-}" ]; then
  dotnet build -c Release -v quiet -clp:NoSummary "$REPO/tools/RoundTrip/RoundTrip.csproj" -o "$OUT/rt" > /dev/null || exit 1
  SCH=(); for d in $XSD_DIRS; do SCH+=(--schemas "$d"); done
  ${PYLXML:-python3} "$HERE/post_check.py" "$OUT/census.jsonl" "$CORPUS" "$OUT/probe" "$OUT/rt/RoundTrip.dll" "$REPO/tools/validate/validate_ooxml.py" "${SCH[@]}" --json "$OUT/post-check.jsonl"
fi
