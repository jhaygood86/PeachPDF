#!/bin/sh
# Builds and runs the example as the macOS app bundle it is. Arguments go to the app: --smoke <report> or --screenshot <png>.
# ValidateXcodeVersion is off because a newer Xcode than the installed workload was built against is fine for a library of this kind.
cd "$(dirname "$0")" || exit 1
dotnet build -p:ValidateXcodeVersion=false -nologo -v q || exit 1
exec bin/Debug/net10.0-macos/osx-arm64/PeachBrackets.Apple.macOS.Example.app/Contents/MacOS/PeachBrackets.Apple.macOS.Example "$@"
