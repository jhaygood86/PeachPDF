# System font discovery skips TrueType collections (`.ttc`)

`FontResolver.FontExtensions` is `*.ttf` and `*.otf`, so fonts that a platform ships only inside a `.ttc`
collection are never discovered. The consequence that surfaced with the `math` generic: Windows' Cambria Math
lives in `cambria.ttc`, so `IsFontExists("Cambria Math")` is false on a stock Windows machine and the Windows
`math` chain (Latin Modern Math, Cambria Math, STIX Two Math) falls through to the platform default font unless
Latin Modern Math or STIX Two Math is installed or a math font is registered with `AddFontFromStream`. Cambria
Math stays in the chain so it works the day collections are readable (or when a caller registers it).

Not done because reading a `.ttc` is a font-loading feature of its own - a collection header, per-face table
directories and per-face identity in `FontResolver`'s caches - well beyond the `math` generic. Documented in
[usage-examples.md](../../docs/usage-examples.md#generic-families-and-system-ui).
