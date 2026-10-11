# Managed Brotli encoder for WebAssembly

`PeachDrawing.Text.Brotli.ManagedBrotliCompressor` (original code under `Internal/Encoder/`, ~800 lines) fills the encode side of the
`BrotliCompression` seam on a host where `BrotliEncoder` throws `PlatformNotSupportedException`. The Blazor demo registers it next to the decoder.

- **Not a port, and why.** Google's encoder is ~15,000 lines of C with no managed counterpart; the only managed full port found (BrotliSharpLib, MIT,
  unsafe-heavy, ~1 MB of generated tables) would have added a large third-party file set. The format is small enough to write: LZ77 hash chain,
  three prefix codes per meta-block, stored fallback. The earlier "full encoder port" plan was scoped down to this on purpose; the gap to the native
  encoder is measured, not guessed (see the porting notes): within a few % on text at q0-9, ~7% on fonts, 10-15% at q11.
- **Traps the round-trip tests caught or guard**: the decoder stops reading code-length-code lengths the moment its space hits zero, and stops reading
  code lengths the moment the code is complete, so the writer must stop at exactly the same point (and write *all* entries when only one code length
  symbol exists); consecutive repeat codes (16/17) are one cumulative run, so a long run needs the reference's digit-reversed multi-code form; a
  non-last meta-block may be stored but a stored block cannot be last (an empty last block follows); distance state must be rolled back when a compressed
  attempt is discarded for a stored block.
- **Evidence.** 36 tests decode every output with both the BCL and the managed decoder (12 qualities, empty/tiny/runs/random/skewed/2-5 symbols/WOFF2 font/every
  embedded resource, multi-meta-block, stored between compressed). All pass on net8.0, net10.0 and net11.0. Benchmarked on a 13 MB font, a 1 MB font, markdown and C#.
- **Not verified:** actually running in a browser (the demo builds, with 0 warnings, but the WASM runtime path was not exercised here).
