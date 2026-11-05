# macOS and iOS system fonts now come from CoreText

**Before:** macOS fonts were the files in `/System/Library/Fonts`, `/Library/Fonts` and `~/Library/Fonts`; iOS had none, so
an iOS app had to register every font and a bare `system-ui`/`sans-serif` fell back to a font that was not installed.
`monospace` resolved to Menlo, `system-ui` to the platform default (Arial) and `ui-*`, `-apple-system` and
`BlinkMacSystemFont` were just unknown family names. Text in PingFang (the CJK fallback on macOS) was not drawn.

**Now:** every installed font CoreText reports is available on macOS, Mac Catalyst, iOS and tvOS (so a font installed or
registered anywhere on the system is found, and a font outside those three directories no longer is missed). `monospace`
is Courier (WebKit's default; Menlo is the fallback), `system-ui`, `-apple-system`, `BlinkMacSystemFont` and `ui-sans-serif`
are San Francisco, `ui-serif` New York, `ui-monospace` SF Mono, `ui-rounded` SF Rounded (elsewhere the `ui-*` generics are
`serif`, `monospace` and `system-ui`). PingFang and other `hvgl` fonts are drawn on macOS 15.4 / iOS 18.4 and later. iOS,
Mac Catalyst and tvOS get the same default font as macOS (Arial) instead of Segoe UI. `Helvetica` is the real font on the
Apple platforms rather than an alias of Arial. Fonts a document author named by a family that only the directory scan
found keep working when CoreText lists them, which is every installed font.
