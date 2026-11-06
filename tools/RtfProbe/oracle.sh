#!/usr/bin/env bash
# RTF against LibreOffice (information, not a gate). Runs LibreOffice only as a black-box converter.
#
#   oracle.sh <docx dir> <work dir>
#
# Needs: a built RtfProbe (RTFPROBE=path to RtfProbe.dll), dotnet on PATH, ONEDOCUMENT_SOFFICE (headless LibreOffice launcher,
# LO_PROFILE selects its profile), and for the validation step ONEDOCUMENT_XSD and PYTHON (with lxml).
#
# 1. LibreOffice writes RTF for every DOCX (oracle output); our reader reads it; its text is compared with the DOCX's own text.
# 2. We write RTF for every DOCX; LibreOffice reads it and writes text and PDF; the text is compared with the DOCX's text.
# 3. Our reader's DOCX for the LibreOffice RTF is validated against the schemas (when ONEDOCUMENT_XSD is set).
set -uo pipefail
docx=${1:?docx dir}
work=${2:?work dir}
probe=${RTFPROBE:?set RTFPROBE}
so=${ONEDOCUMENT_SOFFICE:?set ONEDOCUMENT_SOFFICE}
mkdir -p "$work"/{src,lo-rtf,ours-rtf,lo-txt,txt-docx,txt-lo-rtf,txt-ours-rtf,docx-from-lo,lo-pdf}

find "$docx" -name '*.docx' | while read -r f; do cp "$f" "$work/src/$(basename "$f")"; done
echo "docx: $(ls "$work/src" | wc -l)"

dotnet "$probe" text "$work/src" docx "$work/txt-docx" | tail -3

# 1
( cd "$work/lo-rtf" && "$so" --convert-to rtf --outdir . "$work"/src/*.docx >/dev/null 2>&1 )
echo "LibreOffice rtf: $(ls "$work/lo-rtf" | wc -l)"
dotnet "$probe" read "$work/lo-rtf" "$work/read-lo.jsonl" --docx "$work/docx-from-lo" --rtf "$work/ours-from-lo" >/dev/null
tail -1 "$work/read-lo.jsonl"
dotnet "$probe" text "$work/lo-rtf" rtf "$work/txt-lo-rtf" | tail -3
echo "our text of LibreOffice's RTF against the DOCX text:"
dotnet "$probe" similar "$work/txt-docx" "$work/txt-lo-rtf" > "$work/similar-1.jsonl"; tail -1 "$work/similar-1.jsonl"

# 2
dotnet "$probe" write "$work/src" "$work/ours-rtf" > "$work/write.jsonl"
echo "our RTF written: $(ls "$work/ours-rtf" | wc -l); re-read text equal: $(grep -c '"rereadTextEqual":true' "$work/write.jsonl") of $(grep -c rereadTextEqual "$work/write.jsonl")"
( cd "$work/lo-txt" && "$so" --convert-to txt:Text --outdir . "$work"/ours-rtf/*.rtf >/dev/null 2>&1 )
( cd "$work/lo-pdf" && "$so" --convert-to pdf --outdir . "$work"/ours-rtf/*.rtf >/dev/null 2>&1 )
echo "LibreOffice opened our RTF as text: $(ls "$work/lo-txt" | wc -l), as PDF: $(ls "$work/lo-pdf" | wc -l)"
dotnet "$probe" similar "$work/txt-docx" "$work/lo-txt" > "$work/similar-2.jsonl"; tail -1 "$work/similar-2.jsonl"
# LibreOffice's plain text leaves out notes and text boxes, so its DOCX for our RTF is read back as well: text of ours -> LibreOffice DOCX -> our reader.
mkdir -p "$work/lo-docx" "$work/txt-lo-docx"
( cd "$work/lo-docx" && "$so" --convert-to docx --outdir . "$work"/ours-rtf/*.rtf >/dev/null 2>&1 )
dotnet "$probe" text "$work/lo-docx" docx "$work/txt-lo-docx" | tail -3
dotnet "$probe" similar "$work/txt-docx" "$work/txt-lo-docx" > "$work/similar-3.jsonl"; tail -1 "$work/similar-3.jsonl"

# 3
if [[ -n "${ONEDOCUMENT_XSD:-}" ]]; then
  "${PYTHON:-python3}" "$(dirname "$0")/../validate/validate_ooxml.py" --schemas "$ONEDOCUMENT_XSD/transitional" --schemas "$ONEDOCUMENT_XSD/opc" --json "$work/validate.jsonl" --quiet "$work"/docx-from-lo/*.docx >/dev/null 2>&1
  grep '"kind": *"summary"' "$work/validate.jsonl" | python3 -c "import sys,json; r=[json.loads(l) for l in sys.stdin]; print('schema validation of', len(r), 'DOCX written from LibreOffice RTF; invalid files:', sum(1 for x in r if x['invalid']))"
fi
