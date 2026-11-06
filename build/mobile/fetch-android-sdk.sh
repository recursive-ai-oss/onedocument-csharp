#!/usr/bin/env bash
# Installs the Android command-line tools, platform tools, emulator, one platform, build tools and one x86_64 system image into
# $ANDROID_SDK_ROOT (default: $ONEDOCUMENT_HOME/oracles/android-sdk). It downloads from Google and is safe to run twice.
#
# The SDK is licensed under the Android Software Development Kit License Agreement (https://developer.android.com/studio/terms),
# which the person running this script must accept; the script does not accept it for you. Read it, then run with
#   ANDROID_SDK_LICENSE_ACCEPTED=yes build/mobile/fetch-android-sdk.sh
#
# The command-line tools archive is checked against the SHA-1 Google lists for it in its repository index
# (https://dl.google.com/android/repository/repository2-3.xml); everything else comes through sdkmanager.
set -euo pipefail

tools_zip=commandlinetools-linux-16111833_latest.zip
tools_sha1=e025545c62a8e64c7559119566a569fb1dec5f60
platform=platforms\;android-36
build_tools=build-tools\;36.0.0
image=system-images\;android-35\;default\;x86_64

if [[ "${ANDROID_SDK_LICENSE_ACCEPTED:-}" != yes ]]; then
  echo "The Android SDK License Agreement (https://developer.android.com/studio/terms) has not been accepted." >&2
  echo "Read it and rerun with ANDROID_SDK_LICENSE_ACCEPTED=yes." >&2
  exit 2
fi

home_dir="${ONEDOCUMENT_HOME:-$HOME/oneoffice}"
sdk="${ANDROID_SDK_ROOT:-$home_dir/oracles/android-sdk}"
mkdir -p "$sdk"

if [[ ! -x "$sdk/cmdline-tools/latest/bin/sdkmanager" ]]; then
  tmp="$(mktemp -d "${TMPDIR:-$home_dir}/android-sdk.XXXXXX")"
  curl -sSfL -o "$tmp/$tools_zip" "https://dl.google.com/android/repository/$tools_zip"
  echo "$tools_sha1  $tmp/$tools_zip" | sha1sum -c -
  unzip -q "$tmp/$tools_zip" -d "$tmp"
  mkdir -p "$sdk/cmdline-tools"
  mv "$tmp/cmdline-tools" "$sdk/cmdline-tools/latest"
  rm -rf "$tmp"
fi

sdkmanager="$sdk/cmdline-tools/latest/bin/sdkmanager"
yes | "$sdkmanager" --sdk_root="$sdk" --licenses >/dev/null
"$sdkmanager" --sdk_root="$sdk" "platform-tools" "emulator" "$platform" "$build_tools" "$image"
printf '%s\n' "$image" > "$sdk/system-image.txt"
"$sdk/platform-tools/adb" --version | head -1
echo "installed: $platform $build_tools $image"
