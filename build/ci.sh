#!/usr/bin/env bash
# Restore, build, test, smoke-publish and pack the whole repository: what CI runs on linux-x64, and what runs the same way on any
# build machine.
#
#   build/ci.sh [step...]       steps: restore generated build test aot wasm pack (default: all, in that order) and consumer (on request)
#
# Environment:
#   ONEOFFICE_ARTIFACTS   where build output goes (default: artifacts/ at the repository root, which git ignores)
#   RID                   runtime identifier for the NativeAOT smoke app (default: the host's, e.g. linux-x64)
#   DOTNET                the dotnet executable (default: dotnet from PATH)
#   CI_OUT                where publish and pack output go (default: <artifacts>/ci)
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"
dotnet_bin="${DOTNET:-dotnet}"
config=Release
out="${CI_OUT:-${ONEOFFICE_ARTIFACTS:-$root/artifacts}/ci}"
rid="${RID:-$("$dotnet_bin" --info | awk '/^ RID:/ {print $2; exit}')}"

steps=("$@")
[[ ${#steps[@]} -gt 0 ]] || steps=(restore generated build test aot wasm pack)

for step in "${steps[@]}"; do
  echo "== $step"
  case "$step" in
    restore) "$dotnet_bin" restore OneOffice.slnx ;;
    # The typed views are committed; regenerating from the pinned schemas must change nothing.
    generated) "$dotnet_bin" run --project tools/ViewGenerator -c "$config" --no-restore -- --check ;;
    build) "$dotnet_bin" build OneOffice.slnx -c "$config" --no-restore ;;
    test) "$dotnet_bin" test --solution OneOffice.slnx -c "$config" --no-build ;;
    aot)
      # Warnings are errors: a trim or AOT warning in the library fails the publish.
      "$dotnet_bin" publish smoke/aot/AotSmoke.csproj -c "$config" -r "$rid" -o "$out/aot" -warnaserror
      "$out/aot/AotSmoke"
      ;;
    wasm)
      # Builds the browser host; a headless-browser run is a separate step.
      "$dotnet_bin" build smoke/wasm/WasmSmoke.csproj -c "$config"
      ;;
    consumer)
      # Applications that reference the packages as packages (from a throwaway local pack) and call the Office statics with no
      # registration line: under NativeAOT with warnings as errors, with and without the spreadsheet package.
      feed="$out/consumer-feed"; version=0.0.0-consumer.1
      for project in OneOffice OneOffice.Schema OneOffice.Documents OneOffice.Spreadsheets; do
        "$dotnet_bin" pack "src/$project" -c "$config" -p:Version=$version -p:EnablePackageValidation=false -o "$feed"
      done
      export ONEOFFICE_CONSUMER_FEED="$feed" ONEOFFICE_CONSUMER_VERSION=$version
      for app in ConsumerAot ConsumerDocsOnly; do
        "$dotnet_bin" publish "smoke/consumer/$app.csproj" -c "$config" -r "$rid" -o "$out/$app" -warnaserror
        "$out/$app/$app"
      done
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
