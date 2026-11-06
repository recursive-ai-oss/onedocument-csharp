#!/usr/bin/env bash
# The 1.0 corpus gate driver: one stage per invocation, so each fits a queue slot. Output goes under $GATE_OUT
# (default $ONEDOCUMENT_HOME/u89-gate); the built tools are published by `gate.sh build` into $GATE_BIN.
#
#   tools/gate/gate.sh build            publish the probes used below
#   tools/gate/gate.sh rt-package       package layer: open, rewrite untouched and edited, compare per part (every OOXML set)
#   tools/gate/gate.sh rt-docx|rt-xlsx|rt-pptx|rt-xlsb   hub-level open/save untouched and with one edit, per-part comparison
#
# Environment: ONEDOCUMENT_HOME (corpus, oracles, schemas), GATE_BIN, GATE_OUT, DOTNET (default dotnet).
set -uo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "$here/../.." && pwd)"
home="${ONEDOCUMENT_HOME:?set ONEDOCUMENT_HOME}"
bin="${GATE_BIN:-$home/gate-bin}"
out="${GATE_OUT:-$home/gate-out}"
dn="${DOTNET:-dotnet}"
corpus="${ONEDOCUMENT_CORPUS:-$home/corpus}"
mkdir -p "$out" "$bin"

docx_sets="commoncrawl/docx bugzilla/documentfoundation/docx u1-generated"
xlsx_sets="commoncrawl/xlsx bugzilla/documentfoundation/xlsx lo-xlsx"
pptx_sets="bugzilla/documentfoundation/pptx lo-pptx"

stage="${1:?stage}"; shift || true
case "$stage" in
  build)
    cd "$root"
    "$dn" restore OneDocument.slnx -v q || exit 1
    for t in RoundTrip PackageSweep DocumentSweep XlsxProbe PptxProbe XlsbProbe XlsProbe DocProbe PptProbe Xml2003Probe CalcSweep TextCompare StreamProbe GateProbe RtfProbe LayoutProbe; do
      "$dn" publish "tools/$t" -c Release --no-restore -o "$bin/$t" -v q -clp:NoSummary 2>&1 | tail -2
    done
    ;;
  rt-package)
    mkdir -p "$out/rt-package"
    for s in $docx_sets $xlsx_sets $pptx_sets bugzilla/documentfoundation/xlsb stress; do
      [ -d "$corpus/$s" ] || continue
      n="${s//\//-}"
      "$dn" "$bin/PackageSweep/PackageSweep.dll" opc "$corpus/$s" > "$out/rt-package/$n.txt" 2>&1 &
      while [ "$(jobs -r | wc -l)" -ge "${GATE_JOBS:-3}" ]; do sleep 2; done
    done
    wait
    for f in "$out"/rt-package/*.txt; do echo "== $(basename "$f")"; grep -E '^(files tried|parts compared)' "$f"; done
    ;;
  rt-docx)
    for s in $docx_sets; do
      n="${s//\//_}"
      echo "== $s untouched"
      python3 "$root/tools/DocumentSweep/sweep.py" --input "$corpus/$s" --out "$out/rt-docx/$n" --sweep "$bin/DocumentSweep/DocumentSweep.dll" --roundtrip "$bin/RoundTrip/RoundTrip.dll" --jobs "${GATE_JOBS:-3}" 2>&1 | tail -25
      echo "== $s edit"
      python3 "$root/tools/DocumentSweep/sweep.py" --input "$corpus/$s" --out "$out/rt-docx-edit/$n" --sweep "$bin/DocumentSweep/DocumentSweep.dll" --roundtrip "$bin/RoundTrip/RoundTrip.dll" --jobs "${GATE_JOBS:-3}" --edit 2>&1 | tail -25
    done
    ;;
  rt-xlsx)
    for s in $xlsx_sets; do
      [ -d "$corpus/$s" ] || continue
      n="${s//\//-}"; mkdir -p "$out/rt-xlsx/$n"
      for i in 0 1 2 3; do "$dn" "$bin/XlsxProbe/XlsxProbe.dll" sweep "$corpus/$s" "$out/rt-xlsx/$n" --shard $i/4 > "$out/rt-xlsx/$n-$i.log" 2>&1 & done
      wait
    done
    grep -h summary "$out"/rt-xlsx/*.log
    ;;
  rt-pptx)
    for s in $pptx_sets; do
      [ -d "$corpus/$s" ] || continue
      n="${s//\//-}"; mkdir -p "$out/rt-pptx/$n"
      for i in 0 1 2 3; do "$dn" "$bin/PptxProbe/PptxProbe.dll" sweep "$corpus/$s" "$out/rt-pptx/$n" --shard $i/4 > "$out/rt-pptx/$n-$i.log" 2>&1 & done
      wait
    done
    grep -h summary "$out"/rt-pptx/*.log
    ;;
  rt-xlsb)
    mkdir -p "$out/rt-xlsb"
    "$dn" "$bin/XlsbProbe/XlsbProbe.dll" sweep "$corpus/bugzilla/documentfoundation/xlsb" > "$out/rt-xlsb/sweep.txt" 2>&1; tail -n 12 "$out/rt-xlsb/sweep.txt"
    ;;
  text)
    # text LABEL SRC_DIR EXT [N]: a deterministic sample of N files (every k-th in name order), our text and Tika's, and the similarity.
    label="${1:?label}"; src="${2:?dir}"; ext="${3:?ext}"; n="${4:-150}"
    w="$out/text/$label"; rm -rf "$w"; mkdir -p "$w/in"
    total=$(find "$src" -type f -iname "*.$ext" | wc -l)
    step=$(( total / n + 1 ))
    find "$src" -type f -iname "*.$ext" | sort | awk -v s=$step '(NR - 1) % s == 0' | while read -r f; do ln -sf "$f" "$w/in/$(basename "$f")"; done
    java=$(ls "$home"/oracles/jre/*/bin/java "$home"/oracles/jre/bin/java 2>/dev/null | head -1)
    jar=$(ls "$home"/oracles/tika-3.3.3/*.jar | head -1)
    export PATH="$(dirname "$java"):$PATH"
    "$java" -jar "$jar" -t -i "$w/in" -o "$w/tika" > "$w/tika.log" 2>&1
    "$dn" "$bin/GateProbe/GateProbe.dll" text "$w/in" "$ext" "$w/ours" > "$w/ours.log" 2>&1
    echo "sampled $(ls "$w/in" | wc -l) of $total; ours errors $(grep -c code "$w/ours.log")"
    python3 "$here/text_stats.py" "$w/ours" "$w/tika" "$ext" --json "$out/text/$label.json" | cut -c1-1800
    ;;
  text-stats)
    # text-stats: recompute the similarity statistics from the text already extracted by `text`.
    for w in "$out"/text/*/; do
      l="$(basename "$w")"; e="${l%%-*}"
      python3 "$here/text_stats.py" "$w/ours" "$w/tika" "$e" --worst 6 --json "$out/text/$l.json" > /dev/null
      python3 - "$out/text/$l.json" "$l" <<'PY'
import json, sys
r = json.load(open(sys.argv[1]))
print(sys.argv[2], r["files"], "sim", r["sim"], "precision", r["precision"], "recallNoDigits", r["recallNoDigits"], "oneEmpty", r["exactlyOneSideEmptyCount"], "sim<0.8", r["simLt08"])
PY
    done
    ;;
  calc)
    # calc LABEL DIR [SHARDS]: cached-value reproduction over every workbook (xlsx xlsm xlsb xls ods fods) under DIR.
    label="${1:?label}"; dir="${2:?dir}"; shards="${3:-3}"
    w="$out/calc/$label"; mkdir -p "$w"
    skipargs=(); for n in ${GATE_CALC_SKIP:-}; do skipargs+=(--skip "$n"); done   # files whose recalculation needs tens of gigabytes
    for i in $(seq 0 $((shards - 1))); do
      "$dn" "$bin/CalcSweep/CalcSweep.dll" "$dir" --shard $i/$shards --threads 1 --show 0 --out "$w" "${skipargs[@]}" > "$w/shard-$i.log" 2>&1 &
    done
    wait
    python3 "$here/calc_summary.py" "$w" "$label" | tee "$out/calc/$label.json" | cut -c1-2500
    ;;
  build-bench)
    cd "$root" && "$dn" publish bench/OneDocument.Bench -c Release -o "$bin/Bench" -v q -clp:NoSummary 2>&1 | grep -v 'NU190' | tail -3
    ;;
  mem-stream)
    # Working set at checkpoints and bytes allocated per cell of the streaming cursors on the 1M-row, 20-column stress workbook.
    mkdir -p "$out/mem"
    echo "== read"; "$dn" "$bin/StreamProbe/StreamProbe.dll" read "$corpus/stress/$(ls "$corpus/stress" | grep -m1 xlsx)" 2>&1 | tail -12
    echo "== write"; "$dn" "$bin/StreamProbe/StreamProbe.dll" write "$out/mem/gen.xlsx" 1000000 20 2>&1 | tail -12
    ls -l "$out/mem/gen.xlsx"; rm -f "$out/mem/gen.xlsx"
    ;;
  mem-open)
    # What opening a package touches: the 1,000-page document and a generated package of at least 500 MB.
    mkdir -p "$out/mem"
    echo "== 1000-page docx"; "$dn" "$bin/PackageSweep/PackageSweep.dll" open-cost "$corpus/stress/stress-1000p.docx"
    "$dn" "$bin/PackageSweep/PackageSweep.dll" make-large "$out/mem/large.docx" 500 | tail -2
    echo "== 500 MB package"; "$dn" "$bin/PackageSweep/PackageSweep.dll" open-cost "$out/mem/large.docx"; rm -f "$out/mem/large.docx"
    ;;
  mem-bpc)
    # Retained bytes per cell of the full model beside ClosedXML and NPOI (memory only; the timings the tool prints are not used).
    f="$corpus/stress/$(ls "$corpus/stress" | grep -m1 xlsx)"
    for lib in onedocument closedxml npoi; do "$dn" "$bin/Bench/OneDocument.Bench.dll" bpc $lib "$f" 2>&1 | tail -2; done
    ;;
  conv)
    # conv LABEL SRC_DIR EXTS TARGET_EXT [N]: a deterministic sample of N files with those extensions (comma list), opened by content and saved
    # as TARGET_EXT through the hub (GateProbe convert); one JSON line per file and a summary with loss codes counted.
    label="${1:?label}"; src="${2:?dir}"; exts="${3:?exts}"; target="${4:?target}"; n="${5:-100}"
    w="$out/conv/$label"; rm -rf "$w"; mkdir -p "$w/in"
    for e in ${exts//,/ }; do
      total=$(find "$src" -type f -iname "*.$e" | wc -l)
      step=$(( total / n + 1 ))
      find "$src" -type f -iname "*.$e" | sort | awk -v s=$step '(NR - 1) % s == 0' | while read -r f; do ln -sf "$f" "$w/in/$(basename "$f")"; done
    done
    "$dn" "$bin/GateProbe/GateProbe.dll" convert "$w/in" "$w/out" "$target" > "$w/convert.jsonl" 2> "$w/convert.err"
    tail -n 1 "$w/convert.jsonl" | cut -c1-2500
    ;;
  check-ooxml)
    # check-ooxml LABEL: schema validation (ECMA-376 Transitional + OPC + the pinned Microsoft extension schemas) and LibreOffice opening of out/conv/LABEL/out.
    label="${1:?label}"
    w="$out/conv/$label"
    schemas=(--schemas "$home/schemas/xsd/transitional" --schemas "$home/schemas/xsd/opc")
    for d in "$root"/schemas/ms/*/; do schemas+=(--schemas "$d"); done
    "$home/oracles/venv/bin/python" "$root/tools/corpus/run_checks.py" validate --input "$w/out" --out "$w/validate" --shards "${GATE_JOBS:-3}" --max-minutes 20 "${schemas[@]}" > /dev/null 2>&1
    cat "$w/validate/summary-validate.json" 2>/dev/null | cut -c1-600
    export ONEDOCUMENT_SOFFICE="$home/oracles/soffice.sh" LO_PROFILE_ROOT="$out/lo-profiles/$label"
    "$home/oracles/venv/bin/python" "$root/tools/corpus/run_checks.py" lo --input "$w/out" --out "$w/lo" --shards "${GATE_JOBS:-3}" --max-minutes 20 --timeout 120 > /dev/null 2>&1
    cat "$w/lo/summary-lo.json" 2>/dev/null | cut -c1-600
    rm -rf "$out/lo-profiles/$label"
    ;;
  check-lo)
    # check-lo LABEL: LibreOffice opens (converts to PDF) every file in out/conv/LABEL/out; for formats with no schema check here (XLS, XLSB, RTF).
    label="${1:?label}"
    w="$out/conv/$label"
    export ONEDOCUMENT_SOFFICE="$home/oracles/soffice.sh" LO_PROFILE_ROOT="$out/lo-profiles/$label"
    "$home/oracles/venv/bin/python" "$root/tools/corpus/run_checks.py" lo --input "$w/out" --out "$w/lo" --shards "${GATE_JOBS:-3}" --max-minutes 20 --timeout 120 > /dev/null 2>&1
    cat "$w/lo/summary-lo.json" 2>/dev/null | tr -s ' \n' ' ' | cut -c1-400; echo
    rm -rf "$out/lo-profiles/$label"
    ;;
  check-odf)
    # check-odf LABEL: Jing against the ODF 1.3 RelaxNG schemas (extensions ignored, as the OASIS schema allows no foreign attributes) and LibreOffice opening.
    label="${1:?label}"
    w="$out/conv/$label"
    "$home/oracles/venv/bin/python" "$root/tools/validate/validate_odf.py" --ignore-extensions "$w/out" > "$w/jing.txt" 2>&1
    tail -n 3 "$w/jing.txt" | cut -c1-400
    export ONEDOCUMENT_SOFFICE="$home/oracles/soffice.sh" LO_PROFILE_ROOT="$out/lo-profiles/$label"
    "$home/oracles/venv/bin/python" "$root/tools/corpus/run_checks.py" lo --input "$w/out" --out "$w/lo" --shards "${GATE_JOBS:-3}" --max-minutes 20 --timeout 120 > /dev/null 2>&1
    cat "$w/lo/summary-lo.json" 2>/dev/null | cut -c1-600
    rm -rf "$out/lo-profiles/$label"
    ;;
  pdf)
    # pdf LABEL SRC_DIR EXT N PROFILE(ua1|a2a|ua1a2a): renders a deterministic sample with tagged output in that profile (LayoutProbe) and validates it with veraPDF.
    label="${1:?label}"; src="${2:?dir}"; ext="${3:?ext}"; n="${4:-20}"; profile="${5:-ua1}"
    w="$out/pdf/$label-$profile"; rm -rf "$w"; mkdir -p "$w/in"
    total=$(find "$src" -type f -iname "*.$ext" | wc -l)
    step=$(( total / n + 1 ))
    find "$src" -type f -iname "*.$ext" | sort | awk -v s=$step '(NR - 1) % s == 0' | while read -r f; do ln -sf "$f" "$w/in/$(basename "$f")"; done
    LAYOUTPROBE_PROFILE="$profile" "$dn" "$bin/LayoutProbe/LayoutProbe.dll" render "$w/in" "$w/pdf" > "$w/render.log" 2>&1
    tail -n 3 "$w/render.log" | cut -c1-300
    flavour="$profile"; [ "$profile" = a2a ] && flavour=2a; [ "$profile" = none ] && flavour=ua1
    java=$(ls "$home"/oracles/jre/*/bin/java "$home"/oracles/jre/bin/java 2>/dev/null | head -1)
    export PATH="$(dirname "$java"):$PATH" JAVA_HOME="$(dirname "$(dirname "$java")")"
    flavours="$flavour"; [ "$profile" = ua1a2a ] && flavours="ua1"
    "$home/oracles/verapdf-1.30.3/verapdf" --flavour $flavours --format xml "$w"/pdf/*.pdf > "$w/verapdf-$flavours.xml" 2> "$w/verapdf.err"
    python3 "$here/verapdf_summary.py" "$w/verapdf-$flavours.xml" --label "$label-$flavours" | tee "$out/pdf/$label-$flavours.json" | cut -c1-2800
    if [ "$profile" = ua1a2a ]; then
      "$home/oracles/verapdf-1.30.3/verapdf" --flavour 2a --format xml "$w"/pdf/*.pdf > "$w/verapdf-2a.xml" 2>> "$w/verapdf.err"
      python3 "$here/verapdf_summary.py" "$w/verapdf-2a.xml" --label "$label-2a" | tee "$out/pdf/$label-2a.json" | cut -c1-2800
    fi
    ;;
  lo-convert)
    # lo-convert LABEL SRC_DIR EXT TARGET [N]: LibreOffice (black box) writes TARGET files from a sample, for the OpenDocument reader and round-trip checks.
    label="${1:?label}"; src="${2:?dir}"; ext="${3:?ext}"; target="${4:?target}"; n="${5:-60}"
    w="$out/lo-convert/$label"; rm -rf "$w"; mkdir -p "$w/out"
    total=$(find "$src" -type f -iname "*.$ext" | wc -l)
    step=$(( total / n + 1 ))
    export LO_PROFILE="$out/lo-profiles/lo-convert-$label"
    ok=0; bad=0
    for f in $(find "$src" -type f -iname "*.$ext" | sort | awk -v s=$step '(NR - 1) % s == 0'); do
      if timeout 150 "$home/oracles/soffice.sh" --convert-to "$target" --outdir "$w/out" "$f" > /dev/null 2>&1; then ok=$((ok + 1)); else bad=$((bad + 1)); fi
    done
    pkill -f "$LO_PROFILE" || true
    rm -rf "$LO_PROFILE"
    echo "LibreOffice wrote $(ls "$w/out" | wc -l) $target files from $ok runs ($bad failed)"
    ;;
  odf-rt)
    # odf-rt DIR: untouched round trip at the OpenDocument layer, entry by entry, and the one-entry edit check.
    "$dn" "$bin/GateProbe/GateProbe.dll" odf-rt "${1:?dir}" > "$out/odf-rt-$(basename "$1").jsonl" 2>&1
    tail -n 1 "$out/odf-rt-$(basename "$1").jsonl" | cut -c1-600
    ;;
  paired-docx)
    # paired-docx [N]: schema validation of the edited DOCX written by rt-docx against the originals (parts that are invalid only in our output).
    n="${1:-300}"
    mkdir -p "$out/paired"; : > "$out/paired/docx.tsv"
    for s in commoncrawl_docx:commoncrawl/docx bugzilla_documentfoundation_docx:bugzilla/documentfoundation/docx; do
      d="${s%%:*}"; c="${s#*:}"
      total=$(ls "$out/rt-docx-edit/$d" | grep -c '\.docx$'); step=$(( total / (n / 2) + 1 ))
      ls "$out/rt-docx-edit/$d" | grep '\.docx$' | awk -v s=$step '(NR - 1) % s == 0' | while read -r f; do printf '%s\t%s\n' "$corpus/$c/$f" "$out/rt-docx-edit/$d/$f"; done >> "$out/paired/docx.tsv"
    done
    schemas=(--schemas "$home/schemas/xsd/transitional" --schemas "$home/schemas/xsd/opc")
    for d in "$root"/schemas/ms/*/; do schemas+=(--schemas "$d"); done
    "$home/oracles/venv/bin/python" "$here/paired_validate.py" --pairs "$out/paired/docx.tsv" "${schemas[@]}" --procs "${GATE_JOBS:-3}" --json "$out/paired/docx.json" | cut -c1-3000
    ;;
  edit-checks)
    # edit-checks xlsx|pptx [N]: the existing edit checks over the rt-xlsx / rt-pptx sweeps: schema validity (parts whose error count rose) and LibreOffice.
    kind="${1:?xlsx|pptx}"; n="${2:-150}"
    schemas=(--schemas "$home/schemas/xsd/transitional" --schemas "$home/schemas/xsd/opc")
    for d in "$root"/schemas/ms/*/; do schemas+=(--schemas "$d"); done
    export ONEDOCUMENT_SOFFICE="$home/oracles/soffice.sh" LO_PROFILE="$out/lo-profiles/edit-$kind"
    py="$home/oracles/venv/bin/python"
    for s in $( [ "$kind" = xlsx ] && echo "$xlsx_sets" || echo "$pptx_sets" ); do
      nn="${s//\//-}"
      [ -d "$out/rt-$kind/$nn" ] || continue
      echo "== $s validate"
      if [ "$kind" = xlsx ]; then
        "$py" "$root/tools/corpus/xlsx_edit_checks.py" validate "$out/rt-xlsx/$nn" "$corpus/$s" "${schemas[@]}" --sample-validate "$n" --max-minutes 12 | cut -c1-900
        echo "== $s lo"; "$py" "$root/tools/corpus/xlsx_edit_checks.py" lo "$out/rt-xlsx/$nn" "$corpus/$s" --sample 60 --max-minutes 8 --open-only | cut -c1-600
      else
        "$py" "$root/tools/corpus/pptx_edit_checks.py" validate "$out/rt-pptx/$nn" "$corpus/$s" "${schemas[@]}" --max-minutes 12 | cut -c1-900
        echo "== $s lo"; "$py" "$root/tools/corpus/pptx_edit_checks.py" lo "$out/rt-pptx/$nn" "$corpus/$s" --sample 60 --max-minutes 8 | cut -c1-600
      fi
    done
    pkill -f "$LO_PROFILE" || true
    ;;
  *) echo "unknown stage $stage"; exit 2 ;;
esac
