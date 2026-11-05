#!/bin/sh
# Builds and runs the tests as the macOS app bundle they are. Extra arguments go to the xunit runner (for example -class "*Drawing*").
# ValidateXcodeVersion is off because a newer Xcode than the installed workload was built against is fine for a library of this kind.
cd "$(dirname "$0")" || exit 1
dotnet build -p:ValidateXcodeVersion=false -nologo -v q || exit 1
exec bin/Debug/net10.0-macos/osx-arm64/PeachDrawing.CoreGraphics.Tests.app/Contents/MacOS/PeachDrawing.CoreGraphics.Tests "$@"
