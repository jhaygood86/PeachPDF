#!/bin/sh
# Builds and runs the example as the macOS app bundle it is. Arguments go to the app: --report <file>.
cd "$(dirname "$0")" || exit 1
dotnet build -p:ValidateXcodeVersion=false -nologo -v q || exit 1
exec "$(find bin/Debug -path '*PeachBrackets.Media.Apple.Example.app/Contents/MacOS/*' -type f -name PeachBrackets.Media.Apple.Example | head -1)" "$@"
