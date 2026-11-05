#!/bin/sh
# Builds the app for the iOS simulator, installs it on the booted simulator (or $1, a simulator name or UDID), launches it and prints its PEACH_SMOKE log.
# ValidateXcodeVersion is off because a newer Xcode than the installed workload was built against is fine for a library of this kind.
cd "$(dirname "$0")" || exit 1
dotnet build -p:ValidateXcodeVersion=false -nologo -v q || exit 1
app=$(find bin -name '*.app' -maxdepth 4 | head -1)
device="${1:-booted}"
xcrun simctl install "$device" "$app" || exit 1
xcrun simctl terminate "$device" net.peachpdf.coregraphics.ios 2>/dev/null
xcrun simctl launch --console-pty "$device" net.peachpdf.coregraphics.ios
