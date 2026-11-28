#!/usr/bin/env bash
# Installs the PeachBrackets.Media.Android example on a running emulator, starts it and checks what it logs (tag PEACH_MEDIA): one "OK <name>" or
# "FAIL <name>: <why>" line per check and "DONE" at the end. Run by .github/workflows/android.yml inside the emulator runner; usable by hand against any
# attached device or emulator.
#
#   android-media-smoke.sh <path to the signed .apk>
set -euo pipefail

apk="$1"
package=net.peachpdf.brackets.media.example

adb install -r "$apk"
adb logcat -c
adb shell am start -n "$package/$package.MainActivity" >/dev/null

# The checks play two short clips in both modes; a cold emulator is slow, so give them up to three minutes.
for _ in $(seq 1 180); do
  if [[ "$(adb logcat -d -s PEACH_MEDIA:V)" == *DONE* ]]; then break; fi
  sleep 1
done

log="$(adb logcat -d -s PEACH_MEDIA:V)"
echo "$log"

if [[ "$log" != *DONE* ]]; then echo "the media checks did not finish"; exit 1; fi
if [[ "$log" == *"FAIL "* ]]; then echo "a media check failed"; exit 1; fi

passed=$(grep -c "PEACH_MEDIA: OK " <<<"$log" || true)
if [[ "$passed" -lt 6 ]]; then echo "only $passed of 6 media checks passed"; exit 1; fi
echo "all $passed media checks passed"
