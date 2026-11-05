#!/usr/bin/env bash
# Table, section and header/footer sweep over the DOCX corpus on the worker.
#
#   sweep-tables.sh OUT_DIR [VALIDATE_LIMIT]
#
# 1. `DocxProbe tables` over every DOCX: table/merge/section/story counts and defects, one JSON line per file.
# 2. `DocxProbe edit-tables` over every DOCX that has a table of at least 2x2 cells (insert/delete row and column, set text);
#    the edited files are validated against the pinned schemas, and the originals too, so only parts that became invalid count.
# 3. The first VALIDATE_LIMIT edited files are converted by headless LibreOffice.
#
# Environment: ONEDOCUMENT_HOME (default ~/oneoffice), DOTNET_ROOT/PATH set for .NET 10, ONEDOCUMENT_PYTHON (python with lxml).
set -u
out=${1:?output dir}; limit=${2:-80}
home=${ONEDOCUMENT_HOME:-$HOME/oneoffice}
here=$(cd "$(dirname "$0")" && pwd); root=$(cd "$here/../.." && pwd)
py=${ONEDOCUMENT_PYTHON:-$home/oracles/venv/bin/python}
mkdir -p "$out/edited" "$out/lo" "$out/bin"
dotnet build -c Release -v quiet -clp:NoSummary -o "$out/bin" "$root/tools/DocxProbe" || exit 2
probe="dotnet $out/bin/DocxProbe.dll"

list="$out/files.txt"
{ find "$home/corpus/commoncrawl/docx" "$home/corpus/bugzilla/documentfoundation/docx" "$home/corpus/stress" "${ONEDOCUMENT_CONVERTED:-$home/converted}" -name '*.docx' 2>/dev/null; } | sort > "$list"
echo "files: $(wc -l < "$list")"

rm -rf "$out/t"; mkdir -p "$out/t"
# One output file per chunk: concurrent appends to one file interleave long lines.
xargs -a "$list" -n 40 -P 6 sh -c 'dotnet '"$out"'/bin/DocxProbe.dll tables "$@" > '"$out"'/t/$$.jsonl' _
cat "$out"/t/*.jsonl > "$out/tables.jsonl"
echo "tables.jsonl lines: $(wc -l < "$out/tables.jsonl")"

# Files with an editable table.
"$py" - "$out" <<'E'
import json, sys, os
out = sys.argv[1]
names = {}
for line in open(os.path.join(out, "files.txt")):
    p = line.strip(); names.setdefault(os.path.basename(p), p)
sel = []
for line in open(os.path.join(out, "tables.jsonl")):
    r = json.loads(line)
    if r.get("ok") and r.get("tables", 0) > 0 and r.get("maxColumns", 0) >= 2 and r.get("rows", 0) >= 2:
        sel.append(names[r["file"]])
open(os.path.join(out, "editable.txt"), "w").write("\n".join(sel) + "\n")
print("editable:", len(sel))
E

: > "$out/edit.jsonl"
i=0
while read -r f; do
  i=$((i+1)); o="$out/edited/$(printf %05d $i)-$(basename "$f")"
  $probe edit-tables "$f" "$o" >> "$out/edit.jsonl" 2>>"$out/edit.err" || { echo "edit failed: $f" >> "$out/edit.fail"; rm -f "$o"; }
done < "$out/editable.txt"
echo "edited: $(ls "$out/edited" | wc -l)  failed: $(wc -l < "$out/edit.fail" 2>/dev/null || echo 0)"

xsd=(--schemas "$root/schemas/ecma376/transitional" --schemas "$root/schemas/ecma376/opc")
"$py" "$root/tools/validate/validate_ooxml.py" --quiet --json "$out/validate-edited.jsonl" "${xsd[@]}" "$out"/edited/*.docx > "$out/validate-edited.out" 2>&1
tail -1 "$out/validate-edited.out"
# Originals of the edited files, for the baseline.
mkdir -p "$out/orig"; i=0
while read -r f; do i=$((i+1)); ln -sf "$f" "$out/orig/$(printf %05d $i)-$(basename "$f")"; done < "$out/editable.txt"
"$py" "$root/tools/validate/validate_ooxml.py" --quiet --json "$out/validate-orig.jsonl" "${xsd[@]}" "$out"/orig/*.docx > "$out/validate-orig.out" 2>&1
tail -1 "$out/validate-orig.out"

n=0; for f in "$out"/edited/*.docx; do n=$((n+1)); [ $n -le "$limit" ] || break; echo "$f"; done > "$out/lo-list.txt"
LO_PROFILE="$out/lo-profile" "$home/oracles/soffice.sh" --convert-to txt:Text --outdir "$out/lo" $(cat "$out/lo-list.txt") > "$out/lo.out" 2>&1
echo "libreoffice converted: $(ls "$out/lo" | wc -l) of $(wc -l < "$out/lo-list.txt")"
