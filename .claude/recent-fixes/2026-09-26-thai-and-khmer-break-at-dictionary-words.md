# Thai and Khmer break at dictionary words

`LineBreakAlgorithm.FindOpportunities` now finds the words of a run of Complex_Context (`SA`) text of those two scripts in a word list
and allows a break where each starts, on top of what the rules say. LB1's fallback (a mark is `CM`, anything else `AL`, so nothing breaks
inside a run) is still what every other `SA` script (Lao, Burmese, Tai Tham, Cham, ...) gets, and what Thai and Khmer get when the list
cannot be read.

## What shipped and what did not: the size budget

The task was Thai, Lao, Khmer and Burmese with a budget of well under 1 MB for the package. Measured: ICU's four lists are 476,202 bytes
of DEFLATE resource (Thai 63,344, Lao 79,001, Khmer 220,510, Burmese 113,347; the source text is 4.4 MB), and **the nupkg carries one
assembly per target framework (net8.0, net10.0, net11.0), so each resource is paid three times**: all four would have grown the package by
about 1.4 MB, Thai and Khmer alone grew it **833,183 bytes** (4,209,275 to 5,042,458). Per the plan's own fallback, Thai and Khmer ship and
Lao and Burmese wait, tracked in the accepted gap
[line-breaking-has-no-dictionary-for-lao-and-burmese](../accepted-gaps/line-breaking-has-no-dictionary-for-lao-and-burmese.md). A DAWG was
tried for the encoding and is *larger* once deflated (Khmer 332 KB against 220 KB); a columnar layout with a terminated suffix stream was the
smallest (6% under a length column). The complete four-script change, notices and tests included, is on the branch
`dictionary-line-breaking-all-four`.

## Data

ICU's break-iterator dictionaries (`thaidict`, `khmerdict`), fetched by `assets/unicode/generate_dictionary_breaking.py` from the pinned
tag `release-78.3` and checked against a SHA-256 each. The licences were read at that tag before anything was committed, all four files:
Thai and Khmer are ICU data under the Unicode License v3 (reproduced in the package `THIRD-PARTY-LICENSES.md`; the CLI `--credits`, the
release archives and the Blazor demo already ship that file whole, so no other plumbing was needed, and `LicenseInfoTests` pins the new
text); Lao and Burmese carry BSD-style notices of their own (Wilson and Campbell 2013; Sharon 2013), as expected, and were deferred only
for size. NFC words, zero width joiners dropped, deduplicated, sorted, prefix-compressed in a columnar layout, raw DEFLATE.

Deflate, not Brotli: the other Unicode resources are Brotli, which WebAssembly cannot decode, and they degrade to an empty table there. A
word list that silently disappeared would turn the feature off in the browser demo; `DeflateStream` works everywhere.

## Algorithm (written from the description of the family, not from ICU's code)

Per maximal run of one script: cut it into clusters (a grapheme boundary of UAX #29, and no boundary before a dependent vowel or sign,
after a leading vowel or a Khmer coeng). At each position take every list word that starts there and ends on a cluster boundary, look at
most three words ahead (memoized per position, so the time is linear) for the choice that covers the most characters (a cluster no word
starts at costs 3), break ties by the longer word, and consume the winner. A stretch no word matches is consumed a cluster at a time and
stays whole, with one break before it and one after it. Word lookup narrows a range of the sorted list one character at a time (two
binary searches per character), so the whole list is one `char[]` pool with offsets, no trie to build. A run longer than 16,384
characters is cut at the next cluster boundary and each piece analysed on its own.

## Found by running it

- **Chrome is the oracle, and it does not always agree with the list.** Thai (and, before it was deferred, Burmese) matched Chrome on every
  sentence tried; Khmer matched on all but a few: Chrome splits compounds the list has whole (`សាលារៀន`, `នៅក្នុង`) and keeps
  `ប្រទេសកម្ពុជា` whole though the list has it as two words. `DictionaryLineBreakingTests` therefore has two theories: the sentences that
  agree with Chrome, and those where the list decides. Do not "fix" the second by chasing Chrome; the data is the decided source.
- **`word-break: keep-all` does not suppress these breaks in Chrome**, though SA is resolved to `AL` and keep-all forbids `AL AL`. The
  dictionary opportunity is therefore applied after the keep-all suppression. `break-all` splits Thai even inside a cluster in Chrome
  (`ผู|้`); ours breaks between clusters only.
- **A break inside a grapheme cluster is not only "before a mark".** A random-text property test found Khmer `ឡ` coeng `ី` `ហ`: UAX #29 in
  Unicode 18 keeps the following consonant in the cluster (GB9c), so the segmenter takes grapheme boundaries from `GraphemeBreaker`, not
  only from its own list of marks. The property test asserts every allowed break is a grapheme boundary, for random text of each block.
- **Zero width joiners.** ICU's Khmer list spells some words with U+200C/U+200D. Keeping them in the words made a run containing a joiner
  unmatchable across it and produced a break in the middle of a word (`ภา|ษา<ZWNJ>ไทย`, next to the joiner). The joiners are dropped from the
  list and ignored in the text, and no word starts right after one; Chrome keeps such text whole.
- **A property test over "any break" must be limited to `SA` and unassigned characters:** a Thai digit, `฿` or `๚` has its own line breaking
  class and the rules break around it whatever the dictionary says.
- **Khmer is not shaped by PeachPDF.** Rendering the showcase with real Noto Sans Khmer glyphs showed broken subscripts and dotted circles
  (only Devanagari, Bengali, Gujarati and Tamil have Universal Shaping Engine support), so Khmer appears in the showcase only as blocks in
  `LineBreakTest.ttf`. Line breaking is unaffected. That font is the one deterministic way to assert positions:
  `assets/fonts/generate_line_break_font.py` now also covers every assigned character of the Thai, Lao, Khmer and Burmese blocks (Lao and
  Burmese for the tests that they still do not break).
- **The UAX #14 conformance file expects LB1.** Its Thai and Khmer lines fail with the dictionary on, by design, so
  `LineBreakOptions.ComplexContext` (default `Dictionary`) exists and the conformance test sets `GeneralCategory`.
- **WebAssembly:** the Blazor demo, published and run in the built-in browser, wraps a Thai paragraph at its words (18 text-show lines
  for a paragraph 60pt wide), so the Deflate resources load where there is no Brotli decoder.
- **Writing `\uXXXX` through the file-writing tool produced the characters themselves**, invisible ones (U+200B/C/D) included: escape them in
  test source and check with a scan for U+200B..U+200D before committing.

## Not done

- Text-side normalization: the list is NFC, the text is not normalized, so a Burmese `U+1025 U+102E` written decomposed does not match a
  word spelled `U+1026`, and marks typed in a non-canonical order do not match; such text falls back to unmatched (whole) stretches.
- Weights or frequency: the lists have none; the scoring is coverage only.
- Khmer shaping (see above).
- Lao and Burmese (the size budget above).

## Evidence

`PeachDrawing.Text.Tests` (with the UAX #14 and #29 conformance suites at 100%) and `PeachPDF.Tests`, Debug and Release; layout tests over
`LineBreakTest.ttf` in both projects; hostile input (300k-character runs of words and of random characters, a 100,000-mark cluster,
20,000 repeats of one word, unassigned code points) bounded in time; the `dictionary_line_breaking` showcase rasterized with PDFium and
MuPDF (identical); diff coverage above 90% on the engine's changed lines.
