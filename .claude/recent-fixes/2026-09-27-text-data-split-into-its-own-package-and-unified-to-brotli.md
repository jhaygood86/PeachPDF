# The Unicode/hyphenation/dictionary data moved to its own package, and unified to Brotli

`PeachDrawing.Text` multi-targets `net8.0;net10.0;net11.0`, and NuGet never merges a resource across a package's
`lib/<tfm>` folders — every embedded Unicode/hyphenation/dictionary resource was physically tripled in the nupkg,
once per target framework. That tripling was the reason Lao and Burmese dictionaries were deferred (see the
now-deleted accepted-gap file this PR removes, and the 2026-09-26 dictionary-line-breaking fix). New sibling package
`PeachDrawing.Text.Data` (`netstandard2.0`, referenced via ordinary `<ProjectReference>`, versioned in lockstep via
`Version.props`) now holds all of it as a single, un-tripled copy: `PeachDrawing.Text` depends on it like any other
NuGet dependency, and it has no public API of its own.

## What moved, and what didn't

Everything under `Internal/Text/Resources/` moved wholesale: the Bidi/Script/Use/VerticalOrientation/ArabicJoining
UCD-derived tables, the `hyph-utf8` hyphenation patterns, the OpenType/BCP-47 language tag lists, and the
Thai/Lao/Khmer/Burmese dictionaries. The reader *logic* stayed in `PeachDrawing.Text` on purpose:
`PeachDrawing.Text.Data.TextData.OpenRaw` only opens a raw (still-compressed) manifest resource by name suffix and
has no reference to `System.IO.Compression.Brotli` at all - **`netstandard2.0`'s reference assemblies do not
include `BrotliStream`** (added in `netstandard2.1`), so a first attempt at putting decompression in the Data
project itself failed outright with `CS0246`. `PeachDrawing.Text.Internal.Text.TextDataResources.OpenBrotli` (in
`PeachDrawing.Text`, which targets real runtimes that do have it) is what every reader actually calls now, and it is
also where the new pluggable-decoder registry lives.

## Unified to Brotli, dictionaries included

The four dictionaries were raw DEFLATE before (chosen so WebAssembly, with no Brotli decoder, could still read
them) while every other resource in the package was already Brotli. Measured on the real payload: switching the six
already-Brotli groups to DEFLATE would cost +27.8%; switching the dictionaries from DEFLATE to Brotli saves 6-8% per
script (thai 63,344 -> 59,733 bytes, khmer 220,510 -> 203,627, lao 79,001 -> 74,050, burmese 113,347 -> 105,273).
Brotli won for consistency and size. The two small BCP-47/OpenType language-tag text files (1,234 and 1,666 bytes
uncompressed) were also worth compressing (559 and 830 bytes Brotli - about half) and now are.

**This is a real, if narrow, WASM behavior change**: a host with no Brotli decoder now gets an empty
Thai/Lao/Khmer/Burmese dictionary (falling back to rule LB1, no break inside the run) exactly like every other
Brotli resource, where it used to get the real DEFLATE-loaded dictionary for Thai and Khmer. The new pluggable
decoder seam exists specifically so such a host can register a managed Brotli implementation and recover all of
this data, dictionaries included, at once.

## The pluggable Brotli decoder seam

`PeachDrawing.Text.Compression.BrotliDecompression.SetDecompressor(Func<Stream, Stream>?)` registers a decoder that
`TextDataResources.OpenBrotli` and `Woff2Converter` (WOFF2 is Brotli-compressed by its own spec, and its call site
was already unguarded - `new BrotliStream(...)` outside any try/catch, so it would have thrown
`PlatformNotSupportedException` uncaught on such a host) both prefer over the BCL's own `BrotliStream` once set.
Default behavior (decompressor never set) is byte-for-byte the same as before this PR on every platform where the
BCL's own Brotli support works; the demo's existing WOFF-not-WOFF2 workaround and inert `hyphens: auto` on
WebAssembly are unaffected unless a decoder is actually registered.

**The WASM finding this seam exists for is not new**: `.claude/recent-fixes/2026-07-26-peachpdf-runs-in-a-browser-and-the-two-things-that-stopped-it-are.md`
already measured, empirically, that `System.IO.Compression.Brotli` throws `PlatformNotSupportedException` on
browser/WASM and that relinking with the `wasm-tools` workload does *not* fix it (native `libbrotlidec.a` links, but
the managed wrapper still throws) - that file is now over 30 days old and is deleted as part of this PR since its
durable content already lives in `src/PeachPDF.Demo.BlazorWasm/README.md`. The seam was not re-verified against a
fresh WASM publish in this PR; it is designed and wired in as the follow-up the earlier finding called for, but no
demo currently registers a decoder (a pure-managed Brotli implementation is not shipped by this repo) - if the demo
is ever updated to register one, this is where the wiring point is.

## Data measured, not assumed

- Nupkg sizes (Release, `dotnet pack`, this branch vs. the `main` this branch is based on, same `PeachDrawing.Text`
  version 0.9.20 for direct comparison): baseline `PeachDrawing.Text.0.9.20.nupkg` (Thai+Khmer only, no Data split,
  DEFLATE dictionaries) = 5,295,627 bytes. After this change: `PeachDrawing.Text.0.9.20.nupkg` = 1,580,043 bytes +
  `PeachDrawing.Text.Data.0.9.20.nupkg` = 1,419,134 bytes = 2,999,177 bytes combined install size - a 43.4%
  reduction in total download **while also adding the two previously-deferred dictionaries** (Lao and Burmese, see
  below), not just splitting the existing four Brotli/DEFLATE groups into a second package.
- `unzip -l`/Python `zipfile` inspection confirmed the data lives in exactly one place: `PeachDrawing.Text`'s
  `lib/<tfm>/` folders now carry only the DLL and XML docs (no embedded resource bytes), and
  `PeachDrawing.Text.Data`'s single `lib/netstandard2.0/` folder carries all of it once.
- Restore/consumption proven for real: packed all three (`PeachDrawing.Text.Data`, `PeachDrawing.Text`, `PeachPDF`)
  into a local flat-folder feed and installed `PeachPDF` into a fresh scratch console app via `dotnet add package`
  (not a project reference) against that feed plus nuget.org for the third-party transitive dependencies, in an
  isolated `--packages` folder to avoid a same-version collision with the real previously-published 0.9.20 in the
  machine's global packages cache (a real trap hit while verifying this: NuGet treats a cached id+version as
  immutable and will silently prefer it over a local feed's same-numbered package, which is why the first
  restore attempt "succeeded" without ever pulling in `PeachDrawing.Text.Data` at all). The scratch app rendered a
  real PDF (`PdfGenerator.GeneratePdf` on HTML containing Thai/Lao/Khmer/Burmese text and `hyphens: auto`), proving
  the split resource layout resolves and loads correctly outside a project-reference build.
- `dotnet publish -p:PublishAot=true` for `PeachPDF.Cli` needed the same `_RequiresILLinkPack=false` guard
  `PeachPDF.SourceGenerators.csproj` already carries (see that project's own comment): `PublishAot=true` reaches
  `PeachDrawing.Text.Data`'s restore/evaluation through its ordinary `ProjectReference` from `PeachDrawing.Text`, and
  trips `NETSDK1207` (AOT unsupported for `netstandard2.0`) without it, even though the Data project is never itself
  published. Confirmed the `NETSDK1207` failure is gone with the guard added (a native-linker failure downstream of
  that, `vswhere.exe`/`link.exe` not found, is this sandbox's own missing VS toolchain in `PATH`, unrelated to this
  change, and not chased further).
- `dotnet build PeachPDF.slnx -t:Rebuild`: zero warnings, zero errors, across every target framework this machine's
  SDK resolves (net8.0/net10.0/net11.0, plus the Blazor WASM demo project).
- `PeachDrawing.Text.Tests` and `PeachPDF.Tests`, `--framework net8.0`: 6,382 and 14,478 (9 skipped, unrelated
  platform-specific MIME tests) passed, 0 failed. `PeachPDF.Cli.Tests` (net10.0, since `--credits` output changed to
  include the Data package's own `THIRD-PARTY-LICENSES.md`): 181 passed.

## Lao and Burmese folded in (closes the accepted gap, #1457)

With the tripling gone and Brotli smaller than DEFLATE, the size objection that deferred Lao and Burmese no longer
applies, so this PR folds them in rather than leaving them for a follow-up. The cluster rules
(`DictionarySegmenter.CannotStartWord`/`CannotEndWord`/the Burmese asat-closes-a-syllable rule in
`IsClusterBoundary`) and the generator's `SCRIPTS` entries were carried over from the already-written, already
Chrome-verified branch `dictionary-line-breaking-all-four` (same author, same approach, diverged from main before
Thai+Khmer's own version of this landed) rather than re-derived - diffed line for line against current main's
Thai/Khmer-only version first to confirm the only difference actually was the two additional scripts. Not carried
over: that branch's `NotoSansLaoSubset.ttf`/real-font showcase treatment for Lao (needs a `fontTools` subsetting
step this PR didn't spend time on) - Lao is shown "as blocks" in the `dictionary_line_breaking` showcase, same
treatment as Khmer/Burmese, which is accurate (no subset font prepared) but understates that Lao itself has no
Khmer/Burmese-style shaping gap.

## Not done / left for a follow-up

- The random-text hostile-input property test (`DictionaryLineBreakingTests.RandomText_NeverBreaksInsideAGraphemeCluster_OrNextToAJoiningCharacter`)
  was not extended to Lao/Burmese blocks: doing that correctly needs `IsLeading`/`IsDependent` extended with Lao's
  and Burmese's own leading-vowel/mark sets (not just `DictionarySegmenter`'s already-updated
  `CannotStartWord`/`CannotEndWord`, which the property test does not call), and getting it subtly wrong under time
  pressure seemed worse than leaving Thai/Khmer-only coverage there for now. The targeted sentence-level tests
  (`Sentences_BreakWhereChromeBreaksThem`, `WhereChromeDiffers_TheListDecides`, `EachListLoads_SortedAndComplete`,
  `ParagraphDictionaryBreakingTests`, `DictionaryLineBreakLayoutTests`) do cover Lao and Burmese.
- A real Noto Sans Lao subset font for the showcase (see above).
- The seam was not wired to an actual pure-managed Brotli implementation anywhere (none is shipped by this repo);
  nothing currently calls `BrotliDecompression.SetDecompressor` in the Blazor demo.
