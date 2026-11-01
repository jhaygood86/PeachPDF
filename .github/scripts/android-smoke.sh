#!/usr/bin/env bash
# Installs the PeachBrackets.Android example on a running emulator, starts it, and checks what it logs (tag PEACH_SMOKE) after the page has loaded
# and been scrolled. Run by .github/workflows/android.yml inside the emulator runner; usable by hand against any attached device or emulator.
#
#   android-smoke.sh <path to the signed .apk>
set -euo pipefail

apk="$1"
package=net.peachpdf.brackets.example

adb install -r "$apk"
adb logcat -c
adb shell monkey -p "$package" -c android.intent.category.LAUNCHER 1 >/dev/null

# The page loads and lays out asynchronously, and a cold emulator is slow: give it up to three minutes.
for _ in $(seq 1 180); do
  if [[ "$(adb logcat -d -s PEACH_SMOKE:V)" == *'loaded pages='* ]]; then break; fi
  sleep 1
done

# A swipe up scrolls the page: the view must report it.
size=$(adb shell wm size | grep -o '[0-9]*x[0-9]*' | tail -1)
width=${size%x*}
height=${size#*x}
adb shell input swipe $((width / 2)) $((height * 3 / 4)) $((width / 2)) $((height / 4)) 300
sleep 3

log=$(adb logcat -d -s PEACH_SMOKE:V)
echo "$log"

fail=0
check() {
  if ! grep -q -- "$1" <<<"$log"; then
    echo "MISSING: $2" >&2
    fail=1
  fi
}

check 'loaded pages=' 'the document loaded'
check 'link-cursor=Pointer' 'a link asks for the pointer cursor'
check 'locked-selectable=False' 'user-select: none is reported'
check 'pinned-touch=None' 'touch-action: none is reported'
check 'scroll' 'a swipe scrolled the page'
if grep -q 'FAILED' <<<"$log"; then
  echo 'The document failed to load.' >&2
  fail=1
fi

exit $fail
