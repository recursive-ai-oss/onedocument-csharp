#!/usr/bin/env bash
# veraPDF over the generated layout case set, grouped by kind, in one build of the probe.
#
#   tools/gate/pdf-sets.sh LABEL
#
# Publishes LayoutProbe from this checkout, generates the cases (`LayoutProbe gen`), renders each group as tagged PDF/UA-1 and PDF/A-2a and
# runs veraPDF over it. Groups: business, long, edge (maths, drawings, floats, diagrams), cjkrtl (Arabic, Hebrew, bidi, CJK, Thai),
# decks (PPTX) and sheets (XLSX). The summary of each group and profile is printed as JSON by verapdf_summary.py.
# Environment: ONEDOCUMENT_HOME (oracles; default ~/oneoffice), DOTNET (default dotnet).
set -uo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "$here/../.." && pwd)"
label="${1:?label}"
home="${ONEDOCUMENT_HOME:-$HOME/oneoffice}"
dn="${DOTNET:-dotnet}"
w="$home/u-pdf-sets/$label"
rm -rf "$w"; mkdir -p "$w"
cd "$root"
"$dn" publish tools/LayoutProbe -c Release -o "$w/probe" -v q -clp:NoSummary 2>&1 | tail -2
"$dn" "$w/probe/LayoutProbe.dll" gen "$w/cases" > /dev/null

for g in business long edge cjkrtl decks sheets; do mkdir -p "$w/in/$g"; done
for f in "$w"/cases/*; do
  b="$(basename "$f")"
  case "$b" in
    19-*|20-*|21-*|22-*|23-*|24-*|40-*) g=cjkrtl ;;
    *.pptx) g=decks ;;
    *.xlsx) g=sheets ;;
    drawing-*|float-*|smartart-*|8[0-9]-math*|8[0-9]-vertical*) g=edge ;;
    25-*|27-*|28-*|35-*|111-footnotes-many*|124-*) g=long ;;
    *) g=business ;;
  esac
  ln -s "$f" "$w/in/$g/$b"
done

java=$(ls "$home"/oracles/jre/*/bin/java "$home"/oracles/jre/bin/java 2>/dev/null | head -1)
export PATH="$(dirname "$java"):$PATH" JAVA_HOME="$(dirname "$(dirname "$java")")"
for g in business long edge cjkrtl decks sheets; do
  for p in ua1 a2a; do
    flavour="$p"; [ "$p" = a2a ] && flavour=2a
    LAYOUTPROBE_PROFILE="$p" "$dn" "$w/probe/LayoutProbe.dll" render "$w/in/$g" "$w/pdf/$g-$p" > "$w/render-$g-$p.log" 2>&1
    n=$(ls "$w/pdf/$g-$p"/*.pdf 2>/dev/null | wc -l)
    if [ "$n" -gt 0 ]; then
      "$home/oracles/verapdf-1.30.3/verapdf" --flavour "$flavour" --format xml "$w/pdf/$g-$p"/*.pdf > "$w/verapdf-$g-$p.xml" 2> "$w/verapdf-$g-$p.err"
      python3 "$here/verapdf_summary.py" "$w/verapdf-$g-$p.xml" --label "$g-$p" | tee "$w/summary-$g-$p.json" | cut -c1-1500
    else
      echo "{\"label\": \"$g-$p\", \"pdfs\": 0}"
    fi
    echo "  input $(ls "$w/in/$g" | wc -l), PDFs $n, refused $(grep -c 'render.failed' "$w/render-$g-$p.log")"
  done
done
