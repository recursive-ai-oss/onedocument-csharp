#!/usr/bin/env bash
# Builds a directory of OOXML files for the markup round-trip probe from inputs we have and generators we run.
#
#   tools/MarkupProbe/make-corpus.sh <out dir> [count per kind]
#
# Environment:
#   ONEOFFICE_SOFFICE   the LibreOffice launcher (run only; headless conversion of legacy files to OOXML)
#   ONEOFFICE_VENV      a Python venv with python-docx, openpyxl and python-pptx (run only)
#   ONEOFFICE_LEGACY    a directory of .doc, .xls and .ppt files to convert (for example a published sample set)
#   LO_PROFILE          LibreOffice profile directory (default: <out dir>/lo-profile)
set -euo pipefail

out="${1:?usage: make-corpus.sh <out dir> [count per kind]}"
count="${2:-60}"
mkdir -p "$out/lo" "$out/generated"
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if [[ -n "${ONEOFFICE_VENV:-}" ]]; then
  "$ONEOFFICE_VENV/bin/python" "$here/generate.py" "$out/generated"
fi

if [[ -n "${ONEOFFICE_SOFFICE:-}" && -n "${ONEOFFICE_LEGACY:-}" ]]; then
  export LO_PROFILE="${LO_PROFILE:-$out/lo-profile}"
  for pair in doc:docx xls:xlsx ppt:pptx; do
    from="${pair%%:*}"
    to="${pair##*:}"
    mapfile -t files < <(find "$ONEOFFICE_LEGACY" -maxdepth 1 -name "*.$from" | sort | head -n "$count")
    if ((${#files[@]} > 0)); then
      "$ONEOFFICE_SOFFICE" --convert-to "$to" --outdir "$out/lo" "${files[@]}" >/dev/null 2>&1 || true
    fi
  done
fi
find "$out" -type f \( -name '*.docx' -o -name '*.xlsx' -o -name '*.pptx' \) | wc -l
