#!/bin/sh
# Builds and runs the example as the macOS app bundle it is, writing the PNGs into the folder given (default: ./out).
# ValidateXcodeVersion is off because a newer Xcode than the installed workload was built against is fine for a library of this kind.
cd "$(dirname "$0")" || exit 1
dotnet build -p:ValidateXcodeVersion=false -nologo -v q || exit 1
out="${1:-$PWD/bin/out}"
exec bin/Debug/net10.0-macos/osx-arm64/PeachDrawing.CoreGraphics.Example.app/Contents/MacOS/PeachDrawing.CoreGraphics.Example "$out"
