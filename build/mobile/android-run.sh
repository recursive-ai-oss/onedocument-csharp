#!/usr/bin/env bash
# Builds the Android smoke app, starts a headless x86_64 emulator, installs and launches the app and waits for its SMOKE-RESULT line.
#
# Needs Linux with /dev/kvm access, a .NET SDK with the android workload (dotnet workload install android), a JDK, and an Android SDK
# whose licence the caller has already accepted, with the emulator, platform-tools, one platform, build tools and an x86_64 system
# image. The script installs nothing and changes nothing in that SDK.
#
# Environment:
#   DOTNET                the dotnet executable (default: dotnet from PATH)
#   ANDROID_SDK_ROOT      the SDK (required)
#   JAVA_HOME             a JDK 17 or newer (required; the Android build compiles Java)
#   ANDROID_AVD_HOME      where this script's own virtual device lives (default: $ONEDOCUMENT_HOME/android-avd)
#   ONEDOCUMENT_HOME      default $HOME/.onedocument
#   ANDROID_ARTIFACTS     where build output goes (default: artifacts/android at the repository root, which git ignores)
#   CI_OUT                where the log goes (default: <ANDROID_ARTIFACTS>/ci)
#   EMULATOR_TIMEOUT      seconds to wait for the emulator to boot (default: 600)
#   SMOKE_TIMEOUT         seconds to wait for the result (default: 600)
#   NUGET_EXTRA_SOURCE    an extra package source for restore (a local feed)
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$root"
dotnet_bin="${DOTNET:-dotnet}"
home_dir="${ONEDOCUMENT_HOME:-$HOME/.onedocument}"
jdk="${JAVA_HOME:?set JAVA_HOME to a JDK 17 or newer}"
sdk="${ANDROID_SDK_ROOT:?set ANDROID_SDK_ROOT to an Android SDK with the emulator and an x86_64 system image}"
art="${ANDROID_ARTIFACTS:-$root/artifacts/android}"
export ONEDOCUMENT_ARTIFACTS="$art"
out="${CI_OUT:-$art/ci}"
mkdir -p "$out"
log="$out/android-run.log"
: > "$log"
export PATH="$jdk/bin:$PATH"
adb="$sdk/platform-tools/adb"
emu="$sdk/emulator/emulator"
[[ -x "$adb" && -x "$emu" ]] || { echo "Android SDK not found in $sdk; pass an SDK with the emulator installed" >&2; exit 1; }
export ANDROID_HOME="$sdk" ANDROID_USER_HOME="${ANDROID_USER_HOME:-$home_dir/android-home}" ANDROID_AVD_HOME="${ANDROID_AVD_HOME:-$home_dir/android-avd}"
mkdir -p "$ANDROID_AVD_HOME"

extra=()
[[ -z "${NUGET_EXTRA_SOURCE:-}" ]] || extra+=("-p:RestoreAdditionalProjectSources=$NUGET_EXTRA_SOURCE")

# Warnings are errors: a trim or AOT warning in the library fails this publish. The package is the signed release apk.
"$dotnet_bin" publish smoke/android/AndroidSmoke.csproj -c Release ${extra[@]+"${extra[@]}"} -p:AndroidSdkDirectory="$sdk" -p:JavaSdkDirectory="$jdk" -warnaserror -v quiet -clp:NoSummary
apk="$(find "$art" -name '*-Signed.apk' | head -1)"
[[ -n "$apk" ]] || { echo "no signed apk under $art" >&2; exit 1; }
echo "app size: $(du -k "$apk" | cut -f1) KiB apk"

# One emulator, headless, on a fixed port so this run finds its own device and stops exactly that one.
port=5584
serial="emulator-$port"
# The newest x86_64 system image the SDK already has. The virtual device is a small config.ini of our own in ANDROID_AVD_HOME, so
# neither the SDK nor anyone else's device is touched and the command-line tools are not needed.
image_dir="$(find "$sdk/system-images" -mindepth 3 -maxdepth 3 -name x86_64 | sort -V | tail -1)"
[[ -n "$image_dir" ]] || { echo "no x86_64 system image in $sdk" >&2; exit 1; }
sysdir="${image_dir#"$sdk"/}/"
tag="$(basename "$(dirname "$image_dir")")"
avd="$ANDROID_AVD_HOME/onedocument-smoke.avd"
mkdir -p "$avd"
printf 'avd.ini.encoding=UTF-8\npath=%s\ntarget=android-36\n' "$avd" > "$ANDROID_AVD_HOME/onedocument-smoke.ini"
cat > "$avd/config.ini" <<CONFIG
AvdId=onedocument-smoke
avd.ini.displayname=onedocument-smoke
abi.type=x86_64
hw.cpu.arch=x86_64
hw.cpu.ncore=4
hw.ramSize=3072
hw.lcd.width=720
hw.lcd.height=1280
hw.lcd.density=320
hw.gpu.enabled=yes
hw.gpu.mode=swiftshader_indirect
hw.keyboard=yes
hw.sdCard=no
hw.audioInput=no
hw.audioOutput=no
image.sysdir.1=$sysdir
tag.id=$tag
tag.display=$tag
disk.dataPartition.size=6G
vm.heapSize=256
CONFIG

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
