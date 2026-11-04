#!/usr/bin/env bash
# Self-test for the round-trip tool and the validator, on small generated packages.
#
#   ONEOFFICE_PY      python with lxml and python-docx (the oracle venv)
#   ONEOFFICE_XSD     schema directories (see setup/extract-schemas.sh), path-separated
#   ROUNDTRIP_DLL     built RoundTrip.dll
#   DOTNET            dotnet executable (default: dotnet)
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
py="${ONEOFFICE_PY:?}"; dll="${ROUNDTRIP_DLL:?}"; dn="${DOTNET:-dotnet}"
work="$(mktemp -d "${TMPDIR:-.}/selftest.XXXXXX")"; trap 'rm -rf "$work"' EXIT
fail=0
check() { if [[ "$2" == "$3" ]]; then echo "ok   $1"; else echo "FAIL $1: expected '$3' got '$2'"; fail=1; fi; }

"$py" "$here/validate/make_samples.py" "$work"
# Round trip: one part edited, then a re-zip of identical content.
"$py" - "$work" <<'PY'
import sys, zipfile
w = sys.argv[1]
src = zipfile.ZipFile(w + "/good.docx")
def copy(name, edit=None, level=6):
    with zipfile.ZipFile(w + "/" + name, "w", zipfile.ZIP_DEFLATED, compresslevel=level) as z:
        for i in src.infolist():
            b = src.read(i)
            if edit and i.filename == "word/document.xml":
                b = b.replace(b"Hello", b"Howdy")
            z.writestr(i.filename, b)
copy("edited.docx", edit=True)
copy("rezipped.docx", level=1)
PY
sum() { "$dn" "$dll" "$work/good.docx" "$work/$1" --quiet --json "$work/o.jsonl" 2>/dev/null || true; tail -1 "$work/o.jsonl"; }
check "edited: exactly one changed part" "$(sum edited.docx | "$py" -c 'import json,sys;print(json.load(sys.stdin)["changed"])')" 1
r="$(sum rezipped.docx)"
check "rezipped: none changed" "$(echo "$r" | "$py" -c 'import json,sys;print(json.load(sys.stdin)["changed"])')" 0
check "rezipped: raw entries not preserved" "$(echo "$r" | "$py" -c 'import json,sys;d=json.load(sys.stdin);print(d["rawNotPreserved"]>0)')" True
# Validator.
v() { "$py" "$here/validate/validate_ooxml.py" --quiet --json "$work/v.jsonl" "$work/$1" 2>/dev/null && echo valid || echo invalid; }
check "good.docx valid" "$(v good.docx)" valid
check "mc.docx valid after preprocessing" "$(v mc.docx)" valid
check "bad.docx invalid" "$(v bad.docx)" invalid
check "bad.docx error located" "$(grep -c '"column": [0-9]' "$work/v.jsonl" | head -c1)" 1
exit $fail
