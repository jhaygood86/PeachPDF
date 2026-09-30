# A pure-managed Brotli decoder now exists and is registered by default on WebAssembly

Closes #1492, the item the earlier `PeachDrawing.Text.Compression.BrotliDecompression` seam left open (see
`.claude/recent-fixes/2026-09-27-text-data-split-into-its-own-package-and-unified-to-brotli.md`'s "Not done" bullet:
"nothing currently calls `BrotliDecompression.SetDecompressor` in the Blazor demo"). `hyphens: auto`, Thai/Lao/Khmer/
Burmese dictionary line breaking and WOFF2 font loading all now work in `PeachPDF.Demo.BlazorWasm`. Verified with real
byte-level and pipeline-level tests, not by reading the code or trusting that it compiles - see "Verification" below
for exactly what ran and what it proved, and "Not done" for the one verification step (an actual browser session)
this particular PR could not complete and why.

## The premise in the issue didn't survive contact with the actual encoder settings

The issue speculated a decoder for "what this repo's own generators produce" could be "considerably smaller" than a
general RFC 7932 decoder, since the encoder side uses a bounded quality/window size. That turned out to be false, and
it's worth recording why rather than letting it get re-litigated: quality level and window size are *encoder-side*
choices about how well data compresses, not which parts of the bitstream *format* a compliant decoder has to
understand. `assets/unicode/generate_dictionary_breaking.py` and this repo's other `.br`-producing scripts call
Python's `brotli.compress(payload, quality=11)` — the *highest* setting, which makes the heaviest use of the format's
features (not a reduced one), and `fonttools`' WOFF2 writer does the same. A decoder for "what quality 11 actually
produces" is, in practice, the general decoder. So this ships the full decoder side of RFC 7932, not a narrowed
subset — see `src/PeachDrawing.Text.Brotli/PORTING-NOTES.md` for the detailed reasoning.

## What was actually done

Ported (not written from scratch) Google's own C# decoder from `google/brotli`'s `csharp/org/brotli/dec/`
(MIT-licensed, commit `11017d7812b0502005603cf0f3cce0054e885d72`) into a new project, `src/PeachDrawing.Text.Brotli/`,
kept separate from `PeachDrawing.Text` itself so a consumer that never needs it never pays for it (no new dependency
in the main NuGet packages; `publish.yml` was not touched, so this project is not packed/published — only
`PeachPDF.Demo.BlazorWasm` references it today). The public surface is one static class,
`PeachDrawing.Text.Brotli.ManagedBrotliDecompressor`, with `Decompress`/`Register`/`Unregister`; `Register()` calls
the existing `BrotliDecompression.SetDecompressor` seam.

Considered and rejected: writing a decoder from the RFC 7932 spec text directly. Once the encoder-settings finding
above was confirmed, porting the real reference implementation was no larger a task than a hand-written "subset"
would have been (the static dictionary that dominates the file size is required input data regardless of which
format features a decoder implements), and porting a known-good, previously-shipped implementation is much lower
risk than a fresh implementation of a 40+ page binary format spec that then has to be trusted with zero real-world
mileage behind it.

## A real bug was found by testing, not by reading the code

`BrotliInputStream.Read(byte[], int, int)` returned `-1` at end-of-stream — correct for the `java.io.InputStream`
contract this file was transliterated from, wrong for `System.IO.Stream.Read`, which must return `0` there (`-1` is
not a valid return value for it at all). This broke every idiomatic .NET stream consumer:
`PeachDrawing.Text.Brotli.Tests.ManagedBrotliRoundTripTests`' very first byte-for-byte round-trip assertions, on
ordinary small inputs (`"a"`, an empty array, `"The quick brown fox..."`), threw `ArgumentOutOfRangeException` from
inside `MemoryStream.Write` via `Stream.CopyTo`'s default implementation — not a fabricated edge case, the first real
test run. Fixed in the vendored file (two call sites: the inner `Read` and the `ReadByte` that reads its result) and
documented in `PORTING-NOTES.md`'s "Deliberate differences" section, since it's exactly the kind of thing a future
re-sync with upstream needs to know to re-apply. This is the reason the round-trip tests exist at all rather than
trusting "the port compiles and the license is fine" — a token/compile check would have shipped this bug straight
through, silently corrupting nothing (managed code doesn't corrupt memory) but throwing on essentially every real
decode once `Stream.CopyTo` or `StreamReader` touched the result.

A second, related bug in the same method surfaced during this PR's own post-change review pass (not a test, this
time): after that first fix, `Read` could still return a bare `0` even after it had already copied bytes left over
in its internal read-ahead buffer into the caller's array - correct when there was nothing buffered either, silently
wrong (data written but reported as "nothing read") whenever there was. Fixed by returning the actual byte count
(`copyLen`) instead of an unconditional `0`, and covered by a new regression test that mixes `ReadByte()` and
`Read()` calls near the end of a stream, the specific pattern that reaches this code path (no call site in this
repository happens to mix the two, so nothing here was actually affected, but the bug was real and would have
surfaced for any caller that did). Both fixes are recorded in `PORTING-NOTES.md`'s "Deliberate differences" section.

## Verification, per this repo's own painting/rendering-correctness convention applied to a decoder instead

- `PeachDrawing.Text.Brotli.Tests.ManagedBrotliRoundTripTests` decodes every one of the 87 real `.br` resources
  `PeachDrawing.Text.Data` embeds (read by reflection over its assembly — no accessibility grant needed for resource
  names/streams) and asserts byte-for-byte equality against `System.IO.Compression.BrotliStream`'s own decompression
  of the same bytes, plus synthetic payloads (empty, single-byte, highly repetitive, natural-language-shaped,
  incompressible random, and a 2 MB payload) compressed at `CompressionLevel.SmallestSize` (the BCL's closest
  equivalent to quality 11).
- `PeachDrawing.Text.Brotli.Tests.ManagedBrotliHostileInputTests` fuzzes with random byte sequences (0-4096 bytes),
  every truncation prefix of a valid compressed stream, and 300 single-to-four-bit-flip mutations of one, each run on
  a background task with a 5-second wall-clock timeout: every case must either decode or throw within the timeout,
  never hang. All pass; nothing found beyond the `Read` bug above (fixed before this suite existed as a suite - the
  round-trip tests found it first).
- `PeachDrawing.Text.Brotli.Tests.ManagedBrotliWoff2IntegrationTests` loads a real WOFF2 font (`assets/fonts/Inter-Medium.woff2`)
  through the full `PeachDrawing.Text` public API (`FontSet.AddFile` → `Typeface`) twice — once with the managed
  decoder registered, once with the BCL default — and asserts family name, metrics, cmap, advances and every glyph
  outline's contours/segments are identical for a sample of real characters. This is the "not just a token match"
  check this repo's testing conventions ask for: it proves the whole WOFF2-table-decompression-through-glyph-outline
  pipeline, not just that the raw Brotli bytes match.
- `dotnet test --framework net8.0`, Debug and Release, for `PeachDrawing.Text.Brotli.Tests`: 534/534 passed both
  configurations. `PeachDrawing.Text.Tests` (6,386 passed) and `PeachPDF.Tests` (14,486/14,476 Debug/Release, 9
  skipped both - platform-specific MIME tests, pre-existing and unrelated) unaffected, confirming this change adds
  new projects and a demo startup call without touching either.
- `BrotliInputStreamTests` (added after the review pass above) exercises the vendored `BrotliInputStream` directly
  (this test project has its own `InternalsVisibleTo` from `PeachDrawing.Text.Brotli`): every constructor overload,
  byte-by-byte reading via `ReadByte()`, `Close()` actually closing the wrapped source, a custom-dictionary
  constructor, an `IOException` from the underlying source at both construction and mid-read being wrapped, every
  `NotSupportedException`-throwing `Stream` member, and a small-window (`lgwin=10`, 1 KiB) fixture that forces the
  ring buffer to wrap around roughly 175 times decoding 180,000 bytes - closing the diff-coverage gap these paths
  left (`Woff2Converter`/`TextDataResources` only ever construct the default single-argument form and read via
  `Stream.CopyTo`, so nothing else in this repository reaches any of them). Diff coverage
  (`diff-cover --compare-branch=origin/main`) on the final diff: 93% (1,434 lines, 98 missing) - comfortably above
  the 90% gate. The remaining misses are almost entirely defensive/hard-to-reach branches in the vendored decoder
  itself (a dictionary-corruption assertion that only fires if the compiled-in static dictionary were literally
  corrupted, a handful of ring-buffer-reallocation and block-type-wraparound branches that need an encoder-chosen
  metablock structure the BCL's `BrotliStream` doesn't expose a way to force) rather than anything in this
  repository's own new code.
- `dotnet build PeachPDF.slnx -t:Rebuild`: zero warnings. (`GenerateDocumentationFile` was deliberately left off
  `PeachDrawing.Text.Brotli.csproj` — the vendored files carry their original Javadoc-style `<p>` comments verbatim,
  which the C# XML-doc parser flags as malformed XML (CS1570); turning off doc generation for this non-packed project
  was the right fix, not editing 15 files of vendored code to satisfy a doc-comment parser they were never written
  for.)

## Not done / left as-is

- **A live built-in-browser session against this exact branch was not completed**, and that gap is worth recording
  honestly rather than glossing over: this PR was authored in an isolated git worktree, and the coding session's
  browser-preview tooling (`preview_start`) launches dev servers with a working directory fixed to the shared
  primary checkout, independent of which worktree the session's own file edits live in - a `.claude/launch.json`
  edit made inside the worktree is invisible to it, and writing into the shared checkout to work around that is
  correctly refused by the same tooling (to avoid one session's temporary changes colliding with another's, per
  `.claude/recent-fixes`' own project-memory precedent on shared-checkout collisions). Concretely: the demo's own
  wiring in `Program.cs` is two lines (an `if (OperatingSystem.IsBrowser())` guard around one `Register()` call) that
  read no differently from a hundred other one-line startup calls in this codebase - the *substance* that needed
  proving was the decoder itself, which is what `PeachDrawing.Text.Brotli.Tests` actually exercises end to end: the
  WOFF2 integration test loads a real font through the exact same public `FontSet`/`Typeface` API the demo's font
  loading code calls, with the managed decoder registered exactly as `Program.cs` registers it, and gets
  byte-for-byte identical results to the BCL path. A maintainer who can run `dotnet run --project
  src/PeachPDF.Demo.BlazorWasm` locally should still do a final visual check before relying on this in production
  (upload a Thai/Khmer paragraph, a `hyphens: auto` word, and a WOFF2 `@font-face`) - the mechanism is proven, the
  pixels were not personally observed by whoever lands this.

- The shipped Liberation UI fonts in `PeachPDF.Demo.BlazorWasm` stay WOFF 1.0, not WOFF2, even though WOFF2 now
  decodes correctly there — switching them would mean re-running `convert_liberation_webfonts.py`'s OFL round-trip
  verification for a payload-size win only (WOFF 1.0 is already close: 2.3 MB vs. WOFF2's 1.6 MB for twelve faces)
  and wasn't part of closing this issue. `README.md` says so.
- `PeachDrawing.Text.Brotli` is not packed/published as its own NuGet package (`IsPackable=false`); it is consumed
  today only as a `ProjectReference` from the demo. Publishing it is a reasonable future step if another host wants
  it without a source checkout, but nothing in the issue asked for that.
