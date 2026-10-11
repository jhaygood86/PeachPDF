# Porting notes: the Brotli decoder in PeachDrawing.Text.Brotli

Everything under `Internal/` is derived from Google's own C# port of the Brotli reference decoder and is used
under the MIT license (`Internal/LICENSE`, byte-identical to upstream). Nothing else in this project is
Brotli-derived; `ManagedBrotliDecompressor.cs` (the public wrapper) is this repository's own code, BSD 3-Clause
like the rest of PeachPDF. This project is therefore BSD 3-Clause *and* MIT, same shape as
`PeachDrawing.Text/Internal/Hinting/FreeType/` being FTL-and-BSD-3-Clause.

## What was ported, from where

| | |
|---|---|
| Upstream | [google/brotli](https://github.com/google/brotli), `csharp/org/brotli/dec/` |
| Commit | `11017d7812b0502005603cf0f3cce0054e885d72` (`master`, retrieved 2026-09-27) |
| License | MIT (`Internal/LICENSE`, `Copyright (c) 2009, 2010, 2013-2016 by the Brotli Authors`) |

Only the decoder (`csharp/org/brotli/dec/`) was used, not the encoder (which lives in `csharp/org/brotli/wrapper/enc/`
and elsewhere in the C tree) - this repository never compresses Brotli at runtime, only decompresses data its own
Python generator scripts and WOFF2 (via `fonttools`) produced ahead of time. Google's own `*Test.cs` files
(`BitReaderTest.cs`, `DecodeTest.cs`, `DictionaryTest.cs`, `SynthTest.cs`, `TransformTest.cs`) were not vendored;
this project has its own tests (`PeachDrawing.Text.Brotli.Tests`) that verify byte-for-byte agreement with the
BCL's `System.IO.Compression.BrotliStream` on every `.br` resource this repository actually ships, plus
hostile-input tests of its own (see that project's README below).

## The C# files

Every file under `Internal/` begins with Google's original header (copyright + MIT notice) unchanged, followed by
a short comment block naming this exact port (source path, commit, retrieval date) and a `#nullable disable` (the
port predates nullable reference types and its fields are deliberately left as Google wrote them rather than
retrofitted with `?`/`!` annotations file by file).

| C# file | What it is |
|---|---|
| `BitReader.cs` | Bit-level reads from the input stream, with the ring-buffer/slack input buffering scheme the format needs |
| `BrotliInputStream.cs` | The `Stream` wrapper (`internal`, not `public` - only `ManagedBrotliDecompressor` constructs it) |
| `BrotliRuntimeException.cs` | The decoder's own exception type for malformed input |
| `Context.cs` | The context-modeling lookup tables (literal context computation) |
| `Decode.cs` | The decoder proper: meta-block headers, Huffman-coded block types/lengths, context maps, the main insert-copy-literal loop, the static dictionary word lookup and its 121 transforms |
| `Dictionary.cs` | The compiled-in static dictionary (13,504 words, RFC 7932 Appendix A) that `Decode.cs`'s backward references beyond the window can point into |
| `Huffman.cs` | Canonical Huffman table construction from code lengths |
| `HuffmanTreeGroup.cs` | A group of same-alphabet Huffman tables (literal/insert-copy/distance can each have several, selected by block type) |
| `IntReader.cs` | Byte-to-int repacking used by the ring-buffer input scheme |
| `Prefix.cs` | The fixed lookup tables mapping prefix codes to block-length/insert-length/copy-length ranges |
| `RunningState.cs` | The decoder's state-machine enum |
| `State.cs` | All per-decode mutable state (one instance per `BrotliInputStream`, so decoding is reentrant/thread-safe across separate streams) |
| `Transform.cs` | The 121 word transforms (prefix/suffix/case changes) applied to a dictionary word before it is copied into the output |
| `Utils.cs` | Zero-fill helpers |
| `WordTransformType.cs` | The transform-type enum `Transform.cs` indexes by |

## Deliberate differences from upstream

* **`BrotliInputStream.Read` returns 0 at end-of-stream, not -1.** This is a real bug in the upstream port, found by
  this project's own round-trip tests (not by reading the code): `Read(byte[], int, int)` and the `ReadByte()` that
  calls it were transliterated straight from `java.io.InputStream`, whose `read()` contract returns `-1` at
  end-of-stream. `System.IO.Stream.Read` returns `0` at end-of-stream instead - `-1` is not a valid return value for
  it at all. Every idiomatic .NET consumer of a stream (`Stream.CopyTo`, `StreamReader`, ...) breaks on a `Read` that
  returns `-1`: `Stream.CopyTo`'s default implementation treats any nonzero return as "more data", passes it
  straight to `MemoryStream.Write(buffer, 0, -1)`, and that throws `ArgumentOutOfRangeException` - which is exactly
  how `PeachDrawing.Text.Brotli.Tests.ManagedBrotliRoundTripTests` first caught this, on ordinary small inputs, not
  on anything exotic. `ReadByte()` still returns `-1` for its own end-of-stream (correct - that one *is* `-1` by the
  .NET `Stream.ReadByte()` contract too), so only the inner `Read()` call and the check right after it were fixed to
  test for `0` instead of `-1`. Both fixes are marked inline in `BrotliInputStream.cs` with a comment pointing back
  here.
* **A second, related bug in the same method: `Read` could return `0` after already writing bytes into the
  caller's buffer.** `Read(byte[], int, int)` first copies any bytes left over in its internal read-ahead buffer
  (`copyLen`, drawn down by `ReadByte()`), then decompresses more if the caller asked for more than that. If
  decompression had nothing further to produce (`state.outputUsed == 0`), the fixed-but-still-wrong code above
  returned `0` unconditionally - correct when `copyLen` was also `0`, but wrong whenever `copyLen > 0`: those bytes
  had already been written into the caller's buffer, and a `Read` returning `0` tells the caller "nothing was
  written this call", which is false and silently drops them from the caller's point of view. Found by a code
  review pass over this diff (not a test, this time), then confirmed with a regression test
  (`BrotliInputStreamTests.ReadNearEndOfStreamAfterReadByteDoesNotDiscardAlreadyBufferedBytes`, which mixes
  `ReadByte()` and `Read()` calls near the end of a stream) and fixed by returning `copyLen` instead of a bare `0` -
  which is `0` anyway in the case that was already correct, and `copyLen` in the case that wasn't. No production
  call site in this repository is affected today (`Woff2Converter`/`TextDataResources` both only ever call
  `Stream.CopyTo`, which never calls `ReadByte()`), but the bug would have shipped in `BrotliInputStream`'s general
  public contract as an internal implementation detail waiting to surface the moment any caller mixed the two
  read styles.
* **Namespace.** `Org.Brotli.Dec` became `PeachDrawing.Text.Brotli.Internal`, matching this repository's own
  namespace conventions; every fully-qualified reference inside the files was renamed along with it. Nothing
  else in the port's logic was touched - it is a line-by-line namespace rename, not a rewrite.
* **`BrotliInputStream` is `internal`, not `public`.** Upstream ships it as the library's public entry point;
  here the only public surface is `ManagedBrotliDecompressor.Decompress`/`Register`/`Unregister`, which
  constructs it internally. `DefaultInternalBufferSize` was narrowed the same way.
* **`#nullable disable`** on every file, since the port is C# 5-era code with no nullable annotations and
  retrofitting them file-by-file would risk changing behavior for no benefit (this is vendored, not
  hand-maintained, code).
* **Not ported:** Google's encoder (the managed encoder under `Internal/Encoder/` is original code - see the section at the end
  of this file), `BrotliOutputStream` and
  the encoder-facing parts of `BrotliInputStream` upstream doesn't have anyway (the decoder-only subtree has
  none), and upstream's own JUnit-derived `*Test.cs` files (this project has its own tests instead, verifying
  against the BCL decoder rather than against values transcribed from the Java tests).

## What "pure-managed Brotli decoder" actually means here

The GitHub issue this project closes speculated that since this repository's own `.br` resources and WOFF2's
own Brotli streams are produced with a bounded quality/window size, a decoder "does not need to handle the
fully general RFC 7932 case", and could be "considerably smaller" than a general-purpose one. That turned out
not to be true, and it is worth recording why: quality level and window size are *encoder-side* choices that
affect how well data compresses, not which parts of the bitstream *format* a compliant decoder has to
understand. `assets/unicode/generate_dictionary_breaking.py` and this repository's other `.br`-producing
scripts call Python's `brotli.compress(payload, quality=11)` - the *highest* quality setting, which makes the
heaviest use of the format's features (larger Huffman alphabets, richer context modeling, the full static
dictionary and its 121 transforms), not a reduced one. `fonttools`' WOFF2 writer does the same. A decoder for
"what quality 11 actually produces" is, in practice, a decoder for the general format - there was no smaller
subset to scope this port down to, which is why it is the complete decoder side of RFC 7932 rather than a
narrowed one. This is not a limitation to work around; it just means the "considerably smaller" framing in the
original issue did not pan out once the actual encoder settings were checked, and porting the real thing was
no larger a task than porting a hypothetical subset would have been (the static dictionary in `Dictionary.cs`
dominates the file size regardless of which features a decoder implements, since it is required input data,
not code).

## Verification

`PeachDrawing.Text.Brotli.Tests` decodes every `.br` resource embedded in `PeachDrawing.Text.Data` (Bidi/Script/
Use/VerticalOrientation/ArabicJoining tables, hyphenation patterns, language tags, the Thai/Lao/Khmer/Burmese
dictionaries) and a real WOFF2 font's Brotli-compressed table stream, and asserts the managed decoder's output
is byte-for-byte identical to `System.IO.Compression.BrotliStream`'s. It also fuzzes the decoder with random,
truncated and mutated byte sequences and asserts every one either decodes correctly or throws within a bounded
time (never hangs), since this is the same recipe the accepted-gaps/recent-fixes conventions ask for real
correctness evidence rather than "it compiles and looks right".

## The encoder (original code, not a port)

`ManagedBrotliCompressor` and `Internal/Encoder/` are written for this repository, not taken from google/brotli (whose encoder is
about 15,000 lines of C with no managed counterpart upstream). They share only the format: `Internal/Prefix.cs`'s length tables are
the decoder's own, reused to code insert and copy lengths. What it implements, from RFC 7932:

* LZ77 over a hash chain (4-byte hash; chain depth 1 to 512 by quality; lazy matching from quality 5), a window that grows with
  the input up to 4 MB, last-distance reuse (distance code 0) and explicit distance codes otherwise.
* One literal, one insert-and-copy and one distance prefix code per 1 MB meta-block, as "simple" codes for up to four symbols and
  "complex" codes (run-length coded code lengths) otherwise; length-limited Huffman construction.
* A stored (uncompressed) meta-block, followed by an empty last block, whenever a compressed one would not be smaller.

What it deliberately leaves out, and what that costs (measured against .NET's native encoder on a 13 MB CJK font, a 1 MB Latin font and
two text files): no static dictionary, no context modeling, no block splitting, no ring-buffer distance codes 1-15, no optimal parsing.
Size is within a few percent of the native encoder at qualities 0-9 on text and about 7% worse on fonts, and smaller than Flate on fonts and
prose from quality 3 (on C# source it ties Flate at quality 6 and wins from 9); at quality 11 the native encoder is 10-15% smaller because it does far more work. The managed encoder's quality 11
is not "zopfli-grade": it only searches the chain deeper.

Verification: every output is decoded by the BCL's `BrotliStream` and by the managed decoder and compared to the input (all 12 qualities,
empty and tiny inputs, runs, random data, skewed alphabets, 2 to 5 distinct bytes (the simple-code shapes), a real WOFF2 font, every
embedded `.br` resource's decoded content, inputs over several meta-blocks, and a stored block between compressed ones).
