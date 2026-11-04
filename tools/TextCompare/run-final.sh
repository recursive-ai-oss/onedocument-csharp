#!/usr/bin/env bash
# Re-extracts the Word corpora with the current extractor into dirs that already hold Tika's text (<name>.tika.txt).
# Run from the repository root: tools/TextCompare/run-final.sh <corpus root> <work root>
set -euo pipefail
corpus=$1
work=$2
dotnet build tools/TextCompare -c Release -v quiet -clp:NoSummary 2>&1 | tail -3
dll=$(find "${ONEOFFICE_ARTIFACTS:-artifacts}" -path '*bin*' -name TextCompare.dll | head -1)
echo "dll=$dll"
for s in cc:commoncrawl/docx bz:bugzilla/documentfoundation/docx; do
  k=${s%%:*}
  rm -f "$work"/out-$k/*.ours.* "$work"/out-$k/*.sdk.* "$work"/out-$k/*.fmt
  pids=()
  for i in 0 1 2 3 4 5 6 7; do
    dotnet "$dll" extract "$corpus/${s#*:}" "$work/out-$k" --shard $i/8 > "$work/ext-$k-$i.log" 2>&1 &
    pids+=($!)
  done
  wait "${pids[@]}"
  echo "$k done: $(ls "$work"/out-$k/*.ours.txt | wc -l) ours texts"
done
