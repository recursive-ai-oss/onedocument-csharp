#!/usr/bin/env bash
# Restore, build, test, smoke-publish and pack the whole repository: what CI runs on linux-x64, and what runs the same way on any
# build machine.
#
#   build/ci.sh [step...]       steps: restore generated build test samples aot wasm pack (default: all, in that order), and on request consumer
#                               and wasm-run (publishes the browser host and runs it in headless Chromium; needs the wasm-tools workload,
#                               python3, curl and unzip)
#
# Environment:
#   ONEDOCUMENT_ARTIFACTS   where build output goes (default: artifacts/ at the repository root, which git ignores)
#   RID                   runtime identifier for the NativeAOT smoke app (default: the host's, e.g. linux-x64)
#   DOTNET                the dotnet executable (default: dotnet from PATH)
#   ONEDOCUMENT_HOME      where wasm-run keeps the pinned Chromium, under oracles/ (default: $HOME/.onedocument)
#   CI_OUT                where publish and pack output go (default: <artifacts>/ci)
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"
dotnet_bin="${DOTNET:-dotnet}"
config=Release
out="${CI_OUT:-${ONEDOCUMENT_ARTIFACTS:-$root/artifacts}/ci}"
rid="${RID:-$("$dotnet_bin" --info | awk '/^ RID:/ {print $2; exit}')}"

steps=("$@")
[[ ${#steps[@]} -gt 0 ]] || steps=(restore generated build test samples aot wasm pack)

for step in "${steps[@]}"; do
  echo "== $step"
  case "$step" in
    restore) "$dotnet_bin" restore OneDocument.slnx ;;
    # The typed views are committed; regenerating from the pinned schemas must change nothing.
    generated) "$dotnet_bin" run --project tools/ViewGenerator -c "$config" --no-restore -- --check ;;
    build) "$dotnet_bin" build OneDocument.slnx -c "$config" --no-restore ;;
    test) "$dotnet_bin" test --solution OneDocument.slnx -c "$config" --no-build ;;
    samples)
      # Each sample generates its own input, performs its task and exits non-zero when the result is wrong.
      for project in samples/*/*.csproj; do
        name="$(basename "$project" .csproj)"
        echo "-- $name"
        "$dotnet_bin" run --project "$project" -c "$config" --no-build -- "$out/samples/$name"
      done
      ;;
    aot)
      # Warnings are errors: a trim or AOT warning in the library fails the publish.
      "$dotnet_bin" publish smoke/aot/AotSmoke.csproj -c "$config" -r "$rid" -o "$out/aot" -warnaserror
      "$out/aot/AotSmoke"
      ;;
    wasm)
      # Builds the browser host; a headless-browser run is a separate step.
      "$dotnet_bin" build smoke/wasm/WasmSmoke.csproj -c "$config"
      ;;
    wasm-run)
      # Publishes the browser host with warnings as errors (no trim or AOT warning), serves it on localhost and runs it in the pinned
      # headless Chromium. The console log lands in $out/wasm-run.log; the exit code is the result.
      mkdir -p "$out"
      "$dotnet_bin" publish smoke/wasm/WasmSmoke.csproj -c "$config" -o "$out/wasm" -warnaserror
      echo "app size: $(du -sh "$out/wasm/wwwroot" | cut -f1) published, $(find "$out/wasm/wwwroot" -name '*.br' -printf '%s\n' | awk '{s+=$1} END {printf "%.1f MiB", s/1048576}') brotli-compressed"
      chrome="$(build/wasm/fetch-chromium.sh)"
      python3 build/wasm/run-browser.py --root "$out/wasm/wwwroot" --chrome "$chrome" --log "$out/wasm-run.log"
      ;;
    consumer)
      # Applications that reference the packages as packages (from a throwaway local pack) and call the Office statics with no
      # registration line: under NativeAOT with warnings as errors, with and without the spreadsheet package.
      # A new version each run: a reused package cache would otherwise serve the assemblies of an earlier run.
      feed="$out/consumer-feed"; version="0.0.0-consumer.$(date +%s)"
      for project in OneDocument OneDocument.Schema OneDocument.Documents OneDocument.Spreadsheets OneDocument.Presentations OneDocument.Rendering OneDocument.All; do
        "$dotnet_bin" pack "src/$project" -c "$config" -p:Version=$version -p:EnablePackageValidation=false -o "$feed"
      done
      export ONEDOCUMENT_CONSUMER_FEED="$feed" ONEDOCUMENT_CONSUMER_VERSION=$version
      for app in ConsumerAot ConsumerDocsOnly ConsumerSheetsOnly ConsumerSlidesOnly; do
        "$dotnet_bin" publish "smoke/consumer/$app.csproj" -c "$config" -r "$rid" -o "$out/$app" -warnaserror
        "$out/$app/$app"
      done
      # The metapackage on its own: format registration must reach the application through it.
      "$dotnet_bin" publish smoke/consumer/ConsumerAll.csproj -c "$config" -o "$out/ConsumerAll" -warnaserror
      "$dotnet_bin" "$out/ConsumerAll/ConsumerAll.dll"
      "$dotnet_bin" build smoke/consumer/ConsumerWasm.csproj -c "$config" -warnaserror
      ;;
    pack)
      # Every project under src/, as the release workflow does: those with IsPackable false produce nothing.
      for project in src/*/*.csproj; do
        "$dotnet_bin" pack "$project" -c "$config" --no-build -o "$out/packages"
      done
      ;;
    *) echo "unknown step '$step'" >&2; exit 2 ;;
  esac
done
