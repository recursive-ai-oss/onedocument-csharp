#!/usr/bin/env bash
# Restore, build, test, smoke-publish and pack the whole repository: what CI runs on linux-x64, and what runs the same way on any
# build machine.
#
#   build/ci.sh [step...]       steps: restore generated build test aot wasm pack (default: all, in that order)
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
    pack)
      for project in OneOffice OneOffice.Schema OneOffice.Documents; do
        "$dotnet_bin" pack "src/$project/$project.csproj" -c "$config" --no-build -o "$out/packages"
      done
      ;;
    *) echo "unknown step '$step'" >&2; exit 2 ;;
  esac
done
