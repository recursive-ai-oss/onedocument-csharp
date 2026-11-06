#!/usr/bin/env bash
# Builds the iOS smoke app for the simulator, installs it, launches it and waits for its SMOKE-RESULT line.
#
# Needs macOS with Xcode, a .NET SDK with the ios workload (dotnet workload install ios), and an iOS simulator runtime.
#
# Environment:
#   DOTNET                the dotnet executable (default: dotnet from PATH)
#   IOS_ARTIFACTS         where build output goes (default: artifacts/ios at the repository root, which git ignores). It must be
#                         under the repository: the iOS SDK writes its code-signing stamp files at a path built from the relative
#                         path between the project and the app bundle, and with an unrelated location that path can land on a
#                         file inside the bundle and overwrite it
#   CI_OUT                where the log goes (default: <IOS_ARTIFACTS>/ci)
#   SIM_DEVICE            simulator device type name (default: first available iPhone)
#   SIM_TIMEOUT           seconds to wait for the result (default: 600)
#   NUGET_EXTRA_SOURCE    an extra package source for restore (a local feed)
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$root"
dotnet_bin="${DOTNET:-dotnet}"
art="${IOS_ARTIFACTS:-$root/artifacts/ios}"
export ONEDOCUMENT_ARTIFACTS="$art"
out="${CI_OUT:-$art/ci}"
timeout_s="${SIM_TIMEOUT:-600}"
bundle_id=org.recursiveai.onedocument.smoke
mkdir -p "$out"
log="$out/ios-run.log"
: > "$log"

extra=()
[[ -z "${NUGET_EXTRA_SOURCE:-}" ]] || extra+=("-p:RestoreAdditionalProjectSources=$NUGET_EXTRA_SOURCE")

# Warnings are errors in the library projects; a trim or AOT warning fails this build.
"$dotnet_bin" build smoke/ios/IosSmoke.csproj -c Release "${extra[@]}" -warnaserror -v quiet -clp:NoSummary
app="$(find "$art/bin/IosSmoke" -name IosSmoke.app -type d | head -1)"
[[ -n "$app" ]] || { echo "no IosSmoke.app under $art/bin" >&2; exit 1; }
echo "app size: $(du -sm "$app" | cut -f1) MiB on disk"

# One simulator. An existing device on the newest runtime is used and only shut down afterwards; otherwise one is created for this
# run (trying device types until the runtime accepts one) and deleted after it.
read -r runtime udid < <(xcrun simctl list devices available -j | python3 -c '
import json, sys
d = json.load(sys.stdin)["devices"]
for rt in sorted((k for k in d if "iOS" in k), reverse=True):
    if d[rt]:
        print(rt, d[rt][0]["udid"]); break
else:
    print("none none")
')
created=0
if [[ "$udid" == none ]]; then
  runtime="$(xcrun simctl list runtimes -j | python3 -c 'import json,sys; r=[x for x in json.load(sys.stdin)["runtimes"] if x["platform"]=="iOS" and x["isAvailable"]]; print(r[-1]["identifier"])')"
  for devtype in $(xcrun simctl list devicetypes -j | python3 -c 'import json,sys; [print(x["identifier"]) for x in json.load(sys.stdin)["devicetypes"] if x["name"].startswith("iPhone")]'); do
    if udid="$(xcrun simctl create onedocument-smoke "$devtype" "$runtime" 2>/dev/null)"; then created=1; break; fi
  done
  [[ $created == 1 ]] || { echo "no simulator device could be created" >&2; exit 1; }
fi
cleanup() {
  xcrun simctl shutdown "$udid" >/dev/null 2>&1 || true
  if [[ $created == 1 ]]; then xcrun simctl delete "$udid" >/dev/null 2>&1 || true; fi
}
trap cleanup EXIT
xcrun simctl boot "$udid"
xcrun simctl bootstatus "$udid" -b >/dev/null
xcrun simctl install "$udid" "$app"

# The app prints to its console and exits with the result code; simctl writes both streams to files. The exit code is not
# reported by simctl, so the result line decides.
applog="$out/ios-app.log"
: > "$applog"
set +e
xcrun simctl launch --stdout="$applog" --stderr="$applog" --terminate-running-process "$udid" "$bundle_id" > "$log" 2>&1
for ((i = 0; i < timeout_s; i++)); do
  grep -q ' SMOKE-RESULT ' "$applog" && break
  sleep 1
done
set -e
# NSLog puts a timestamp and process id in front of each line; the log keeps the line itself.
sed -E 's/^[0-9-]+ [0-9:.]+ IosSmoke\[[0-9:]+\] //' "$applog" >> "$log"

grep -E '^(RUNTIME|GROUP|FAIL|ENCRYPTED|SMOKE-RESULT)' "$log" | tail -60 || true
grep -q '^SMOKE-RESULT ok' "$log"
