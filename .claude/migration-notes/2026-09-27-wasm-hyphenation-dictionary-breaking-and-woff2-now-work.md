# `hyphens: auto`, dictionary line breaking and WOFF2 fonts now work in `PeachPDF.Demo.BlazorWasm`

**Before:** on WebAssembly (this repository's own `PeachPDF.Demo.BlazorWasm` being the concrete case), a document
author using `hyphens: auto` got unhyphenated text instead of a failed render, a Thai/Lao/Khmer/Burmese paragraph got
no dictionary-based line breaking (UAX #14 rule LB1's fallback — no break opportunity inside a run of Complex_Context
text), and a WOFF2 `@font-face` failed to load — all silently, because `System.IO.Compression.BrotliStream` throws
`PlatformNotSupportedException` in a browser and nothing registered an alternative decoder with the pluggable seam
`PeachDrawing.Text.Compression.BrotliDecompression.SetDecompressor` provides.

**Now:** a new project, `PeachDrawing.Text.Brotli`, ships a pure-managed Brotli decoder (ported from `google/brotli`'s
own C# decoder) for exactly this seam. `PeachPDF.Demo.BlazorWasm`'s `Program.cs` registers it at startup (gated on
`OperatingSystem.IsBrowser()`), so all three behaviors above now work correctly in that demo. This is not automatic
for every WebAssembly host, though — any other application that also runs PeachPDF/PeachDrawing.Text on WebAssembly
(or any other host where the BCL's Brotli support doesn't work) needs to reference `PeachDrawing.Text.Brotli` and
call `PeachDrawing.Text.Brotli.ManagedBrotliDecompressor.Register()` itself, the same way the demo does, to get the
same fix. A host that does nothing keeps the exact prior (silent-degradation) behavior — this is an opt-in fix, not a
default library behavior change, since `PeachDrawing.Text.Brotli` is a separate, non-default dependency.

Affects: `docs/peachdrawing-text.md` (the seam now has a ready-made implementation to point to),
`docs/usage-examples.md` (the "Use WOFF or TrueType, not WOFF2" browser-hosting guidance is replaced with "register a
decoder"), `docs/html-css-support.md` (`hyphens: auto`'s Brotli-decoder caveat now names the fix), and
`src/PeachPDF.Demo.BlazorWasm/README.md`/`Pages/Home.razor` (the demo's own limitation notes for these three
features).
