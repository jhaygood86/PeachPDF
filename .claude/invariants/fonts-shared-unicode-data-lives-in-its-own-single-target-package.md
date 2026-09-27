# Shared Unicode/hyphenation/dictionary data lives in its own single-target package

`PeachDrawing.Text` multi-targets `net8.0;net10.0;net11.0`. NuGet never merges a resource across a package's
`lib/<tfm>` folders within one package — each target framework's assembly is a separate copy in the nupkg, so an
embedded resource in a multi-targeted assembly is physically duplicated once per target framework it ships for.

The Bidi/Script/Use/VerticalOrientation/ArabicJoining Unicode tables, the `hyph-utf8` hyphenation patterns, the
OpenType/BCP-47 language tag lists, and the Thai/Lao/Khmer/Burmese dictionary word lists are all read-only data with
no target-framework-specific behavior, so none of it needs to be triplicated. It lives instead in
`PeachDrawing.Text.Data` (`src/PeachDrawing.Text.Data/`), a single-target (`netstandard2.0`) sibling package that
`PeachDrawing.Text` takes an ordinary `<ProjectReference>` on — one copy of the data, shipped once, regardless of how
many frameworks the engine itself targets.

## The rule

**A future embedded Unicode/hyphenation/dictionary resource goes into `PeachDrawing.Text.Data`, not directly into
`PeachDrawing.Text`.** Adding it straight to `PeachDrawing.Text.csproj` out of habit (the natural reflex, since that
is where the reader code lives) reintroduces the triplication this package split was built to avoid. Data that is
genuinely target-framework-specific (there is none of this kind today) is the only thing that would belong directly
in `PeachDrawing.Text` instead.

## Shape to follow

- `PeachDrawing.Text.Data` exposes **no public API** — every member is `internal`, reachable only from
  `PeachDrawing.Text` via `InternalsVisibleTo`. It is an implementation-detail dependency, not something a consumer
  references directly (see its own project file header and README for the framing: "no third-party dependencies",
  not "no dependencies", is the accurate claim everywhere in the docs now that this exists).
- `PeachDrawing.Text.Data.TextData` is the one place that knows how to find and open a raw (still compressed)
  embedded resource by manifest-name suffix (`OpenRaw`). It has **no reference to `System.IO.Compression.Brotli`**,
  because `netstandard2.0`'s reference assemblies do not include `BrotliStream` (added in `netstandard2.1`) — trying
  to reference it there fails with `CS0246`, which is why decompression is not this package's job.
- `PeachDrawing.Text.Internal.Text.TextDataResources.OpenBrotli` (in `PeachDrawing.Text` itself, which targets real
  runtimes that do have `BrotliStream`) is what every reader actually calls: it opens the raw resource via
  `Data.TextData.OpenRaw`, then decompresses it — the registered custom decoder if one is set
  (`PeachDrawing.Text.Compression.BrotliDecompression`/`Internal.Text.BrotliDecoderRegistry`), else `BrotliStream`,
  falling back to `null` (an empty table/no-op) on `PlatformNotSupportedException` exactly as before this package
  existed.
- All four Thai/Lao/Khmer/Burmese dictionaries and every other Unicode/hyphenation resource are Brotli now (not a mix
  of Brotli and raw DEFLATE) — see `.claude/recent-fixes/` for the measured before/after sizes and the WASM
  behavior consequence (a host with no Brotli decoder, and no custom one registered, loses these four dictionaries
  too, the same way it already lost every other Brotli resource).

## Measured symptom if this is skipped

Embedding a new resource directly in `PeachDrawing.Text.csproj` costs it 3x in the published nupkg (once per
`net8.0`/`net10.0`/`net11.0` `lib/` folder) — the exact defect that made the Thai/Khmer/Lao/Burmese dictionary
word lists too expensive to ship all four at once before this package existed (see the now-deleted accepted-gap
file this invariant's own PR removed, and `.claude/recent-fixes/` for the measured nupkg sizes before and after).
