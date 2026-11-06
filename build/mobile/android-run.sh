#!/usr/bin/env bash
# Builds the Android smoke app, starts a headless x86_64 emulator, installs and launches the app and waits for its SMOKE-RESULT line.
#
# Needs Linux with /dev/kvm access, a .NET SDK with the android workload (dotnet workload install android), a JDK, and the Android
# SDK from build/mobile/fetch-android-sdk.sh. That script downloads Google's SDK and only runs once the operator has accepted the
# Android SDK License Agreement (see the script).
#
# Environment:
#   DOTNET                the dotnet executable (default: dotnet from PATH)
#   ANDROID_SDK_ROOT      the SDK (default: $ONEDOCUMENT_HOME/oracles/android-sdk, ONEDOCUMENT_HOME defaulting to $HOME/oneoffice)
#   ANDROID_ARTIFACTS     where build output goes (default: artifacts/android at the repository root, which git ignores)
#   CI_OUT                where the log goes (default: <ANDROID_ARTIFACTS>/ci)
#   EMULATOR_TIMEOUT      seconds to wait for the emulator to boot (default: 600)
#   SMOKE_TIMEOUT         seconds to wait for the result (default: 600)
#   NUGET_EXTRA_SOURCE    an extra package source for restore (a local feed)
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$root"
dotnet_bin="${DOTNET:-dotnet}"
home_dir="${ONEDOCUMENT_HOME:-$HOME/oneoffice}"
sdk="${ANDROID_SDK_ROOT:-$home_dir/oracles/android-sdk}"
art="${ANDROID_ARTIFACTS:-$root/artifacts/android}"
export ONEDOCUMENT_ARTIFACTS="$art"
out="${CI_OUT:-$art/ci}"
mkdir -p "$out"
log="$out/android-run.log"
: > "$log"
adb="$sdk/platform-tools/adb"
emu="$sdk/emulator/emulator"
[[ -x "$adb" && -x "$emu" ]] || { echo "Android SDK not found in $sdk; run build/mobile/fetch-android-sdk.sh" >&2; exit 1; }
export ANDROID_HOME="$sdk" ANDROID_USER_HOME="${ANDROID_USER_HOME:-$home_dir/android-home}" ANDROID_AVD_HOME="${ANDROID_AVD_HOME:-$home_dir/android-home/avd}"
mkdir -p "$ANDROID_AVD_HOME"

extra=()
[[ -z "${NUGET_EXTRA_SOURCE:-}" ]] || extra+=("-p:RestoreAdditionalProjectSources=$NUGET_EXTRA_SOURCE")

# Warnings are errors: a trim or AOT warning in the library fails this publish.
"$dotnet_bin" publish smoke/android/AndroidSmoke.csproj -c Release "${extra[@]}" -p:AndroidSdkDirectory="$sdk" -warnaserror -v quiet -clp:NoSummary
apk="$(find "$art" -name '*-Signed.apk' | head -1)"
[[ -n "$apk" ]] || { echo "no signed apk under $art" >&2; exit 1; }
echo "app size: $(du -k "$apk" | cut -f1) KiB apk"

# One emulator, headless, on a fixed port so this run finds its own device and stops exactly that one.
port=5584
serial="emulator-$port"
image="$(cat "$sdk/system-image.txt")"
if ! "$sdk/cmdline-tools/latest/bin/avdmanager" list avd | grep -q 'Name: onedocument-smoke'; then
  echo no | "$sdk/cmdline-tools/latest/bin/avdmanager" create avd -n onedocument-smoke -k "$image" -d pixel_5 >/dev/null
fi
"$emu" -avd onedocument-smoke -port "$port" -no-window -no-audio -no-boot-anim -no-snapshot -gpu swiftshader_indirect -accel on > "$out/emulator.log" 2>&1 &
emu_pid=$!
cleanup() {
  "$adb" -s "$serial" emu kill >/dev/null 2>&1 || true
  sleep 3
  kill "$emu_pid" >/dev/null 2>&1 || true
}
trap cleanup EXIT

"$adb" -s "$serial" wait-for-device
boot_timeout="${EMULATOR_TIMEOUT:-600}"
for ((i = 0; i < boot_timeout; i++)); do
  [[ "$("$adb" -s "$serial" shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')" == 1 ]] && break
  sleep 1
done
"$adb" -s "$serial" install -r "$apk" >> "$log"
"$adb" -s "$serial" logcat -c
"$adb" -s "$serial" shell monkey -p org.recursiveai.onedocument.smoke -c android.intent.category.LAUNCHER 1 >/dev/null

applog="$out/android-app.log"
smoke_timeout="${SMOKE_TIMEOUT:-600}"
set +e
"$adb" -s "$serial" logcat -v brief 'OneDocumentSmoke:I' '*:S' > "$applog" 2>&1 &
logcat_pid=$!
for ((i = 0; i < smoke_timeout; i++)); do
  grep -q 'SMOKE-RESULT' "$applog" && break
  sleep 1
done
kill "$logcat_pid" 2>/dev/null
wait "$logcat_pid" 2>/dev/null
set -e

# logcat's brief format puts "I/OneDocumentSmoke( pid): " in front of each line.
sed -E 's/^I\/OneDocumentSmoke\( *[0-9]+\): //' "$applog" >> "$log"
grep -E '^(RUNTIME|GROUP|FAIL|ENCRYPTED|SMOKE-RESULT)' "$log" | tail -60 || true
grep -q '^SMOKE-RESULT ok' "$log"
