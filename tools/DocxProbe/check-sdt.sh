#!/usr/bin/env bash
# Worker run for the content-control unit: build the probe, generate and edit the all-types document, validate and open both,
# then run the census (and an edit pass) over the corpus.
set -u
export PATH=~/dotnet10:$PATH DOTNET_ROOT=~/dotnet10 DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_PROCESSOR_COUNT=4
cd "$(dirname "$0")/../.."
root=$(pwd)
W=~/oneoffice/sdt-check; mkdir -p $W/gen $W/lo
O=~/oneoffice/oracles
dotnet build -m:4 tools/DocxProbe -c Release -o $W/probe -v quiet -clp:NoSummary 2>&1 | grep -E 'error|Warn|Elapsed' | head
P="dotnet $W/probe/DocxProbe.dll"
$P sdt-generate $W/gen
$P sdt-edit $W/gen/sdt-all-types.docx $W/gen/sdt-edited.docx
$P sdt-census $W/gen/sdt-edited.docx | cut -c1-200
PY=$O/venv/bin/python
$PY tools/validate/validate_ooxml.py --json $W/validate.jsonl --schemas schemas/ecma376/transitional --schemas schemas/ecma376/opc $W/gen/*.docx 2>&1 | tail -8
for f in $W/gen/*.docx; do
  LO_PROFILE=$W/lo-profile $O/soffice.sh -env:UserInstallation=file://$W/lo-profile --headless --convert-to txt:Text --outdir $W/lo $f >/dev/null 2>&1
done
ls $W/lo; head -c 600 $W/lo/sdt-edited.txt
