#!/usr/bin/env bash
# Writes the properties fixture (testdata/ole/ole-props.xls) back out as an Excel binary file through the BIFF8 writer, then asks a
# spreadsheet program to convert that file to ODS and prints the document properties it found (meta.xml).
#
#   tools/Ole/xls-props-sample.sh <out-dir>
#
# Environment: ONEDOCUMENT_HOME (oracles), OLEPROBE_DLL (built OleProbe.dll).
set -uo pipefail
out="${1:?usage: xls-props-sample.sh <out-dir>}"
home="${ONEDOCUMENT_HOME:?set ONEDOCUMENT_HOME}"
here="$(cd "$(dirname "$0")" && pwd)"
mkdir -p "$out"
dotnet "${OLEPROBE_DLL:?set OLEPROBE_DLL}" convert "$here/../../testdata/ole/ole-props.xls" "$out/rewritten.xls"
dotnet "$OLEPROBE_DLL" props "$out/rewritten.xls" | head -40
export LO_PROFILE="$out/.lo-profile"
"$home/oracles/soffice.sh" --convert-to ods --outdir "$out/ods" "$out/rewritten.xls" > /dev/null 2>&1
unzip -p "$out/ods/rewritten.ods" meta.xml | python3 -c 'import re,sys; print(re.sub(r"><", ">\n<", sys.stdin.read()))' | grep -E "title|creator|initial-creator|user-defined|Company|subject|keyword"
