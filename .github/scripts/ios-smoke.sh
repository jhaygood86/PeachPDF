#!/bin/sh
# Builds the iOS example, installs it on a booted simulator (booting the newest available iPhone if none is), launches it and asserts on the PEACH_SMOKE lines
# it logs: the page loaded, the document said what it should at each point, and the main thread was never held for long.
set -e
cd "$(dirname "$0")/../../src/examples/PeachBrackets.Apple.iOS.Example"
dotnet build -p:ValidateXcodeVersion=false -p:RuntimeIdentifier=iossimulator-arm64 -nologo -v q
app=$(find bin/Debug -path '*iossimulator-arm64*' -name '*.app' -maxdepth 4 | head -1)
if [ -z "$app" ]; then
  echo "No simulator app was built" >&2
  exit 1
fi
if ! xcrun simctl list devices booted | grep -q Booted; then
  # The last available iPhone is on the newest runtime installed.
  udid=$(xcrun simctl list devices available iPhone | grep -Eo '[0-9A-F-]{36}' | tail -1)
  xcrun simctl boot "$udid"
  xcrun simctl bootstatus "$udid" -b
fi
id=net.peachpdf.brackets.ios.example
xcrun simctl install booted "$app"
xcrun simctl terminate booted "$id" 2>/dev/null || true
log="${RUNNER_TEMP:-/tmp}/ios-smoke.log"
xcrun simctl launch --console-pty booted "$id" --smoke > "$log" 2>&1 &
pid=$!
for _ in $(seq 1 90); do
  grep -q "RESULT" "$log" && break
  # A launch that failed ends at once: report it now instead of waiting out the time.
  kill -0 "$pid" 2>/dev/null || break
  sleep 1
done
kill "$pid" 2>/dev/null || true
xcrun simctl terminate booted "$id" 2>/dev/null || true
cat "$log"
grep -q "PEACH_SMOKE RESULT ALL PASSED" "$log"
