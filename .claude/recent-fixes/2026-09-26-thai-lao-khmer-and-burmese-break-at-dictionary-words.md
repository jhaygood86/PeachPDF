# Thai, Lao, Khmer and Burmese break at dictionary words

`LineBreakAlgorithm.FindOpportunities` now finds the words of a run of Complex_Context (`SA`) text of those four scripts in a word list
and allows a break where each starts, on top of what the rules say. LB1's fallback (a mark is `CM`, anything else `AL`, so nothing breaks
inside a run) is still what every other `SA` script (Tai Tham, Cham, ...) gets, and what these four get when the list cannot be read.

## Data

ICU's break-iterator dictionaries (`thaidict`, `laodict`, `khmerdict`, `burmesedict`), fetched by
`assets/unicode/generate_dictionary_breaking.py` from the pinned tag `release-78.3` and checked against a SHA-256 each. The licences were
read at that tag before anything was committed: Thai and Khmer are ICU data under the Unicode License v3; Lao and Burmese carry
BSD-style notices of their own (Wilson and Campbell 2013; Sharon 2013). They matched what the plan expected, so all four ship, and the
notices are in the package `THIRD-PARTY-LICENSES.md` (the CLI `--credits`, the release archives and the Blazor demo already ship that
file whole, so no other plumbing was needed; `LicenseInfoTests` pins the new text). NFC words, zero width joiners dropped, deduplicated,
sorted, prefix-compressed in a columnar layout, raw DEFLATE: **507,591 bytes for the four** (Thai 66,602, Lao 83,733, Khmer 234,460,
Burmese 122,796; the source text is 4.4 MB), so nothing was deferred. Measured nupkg growth is in the PR.

Deflate, not Brotli: the other Unicode resources are Brotli, which WebAssembly cannot decode, and they degrade to an empty table there. A
word list that silently disappeared would turn the feature off in the browser demo; `DeflateStream` works everywhere.

## Algorithm (written from the description of the family, not from ICU's code)

Per maximal run of one script: cut it into clusters (a grapheme boundary of UAX #29, and no boundary before a dependent vowel or sign,
after a leading vowel, a Khmer coeng or a Burmese virama, or before a Burmese consonant that carries an asat unless the asat begins a
kinzi). At each position take every list word that starts there and ends on a cluster boundary, look at most three words ahead
(memoized per position, so the time is linear) for the choice that covers the most characters (a cluster no word starts at costs 3),
break ties by the longer word, and consume the winner. A stretch no word matches is consumed a cluster at a time and stays whole, with
one break before it and one after it. Word lookup narrows a range of the sorted list one character at a time (two binary searches per
character), so the whole list is one `char[]` pool with offsets, no trie to build.

## Found by running it

- **Chrome is the oracle, and it does not always agree with the list.** Thai and Burmese matched Chrome on every sentence tried, and
  Khmer and Lao on all but a handful: Chrome splits compounds the list has whole (`សាលារៀន`, `នៅក្នុង`, `ທຸກຄົນ`, `ຕາເວັນອອກ`) and keeps
  `ប្រទេសកម្ពុជា` whole though the list has it as two words. The fixtures in `DictionaryLineBreakingTests` are split in two theories for
  that reason: those that agree with Chrome, and those where the list decides. Do not "fix" the second by chasing Chrome; the data is the
  decided source.
- **`word-break: keep-all` does not suppress these breaks in Chrome**, though SA is resolved to `AL` and keep-all forbids `AL AL`. The
  dictionary opportunity is therefore applied after the keep-all suppression. `break-all` splits Thai even inside a cluster in Chrome
  (`ผู|้`); ours breaks between clusters only.
- **A break inside a grapheme cluster is not only "before a mark".** A random-text property test found Khmer `ឡ` coeng `ី` `ហ`: UAX #29 in
  Unicode 18 keeps the following consonant in the cluster (GB9c), so the segmenter takes grapheme boundaries from `GraphemeBreaker`, not
  only from its own list of marks. The property test asserts every allowed break is a grapheme boundary, for random text of each block.
- **Zero width joiners.** ICU's Khmer and Burmese lists spell some words with U+200C/U+200D. Keeping them in the words made a run
  containing a joiner unmatchable across it and produced a break in the middle of a word (`ภา|ษา‌ไทย`, next to the joiner). The joiners
  are dropped from the list and ignored in the text, and no word starts right after one; Chrome keeps such text whole.
- **A property test over "any break" must be limited to `SA` and unassigned characters:** a Thai digit, `฿` or `๚` has its own line breaking
  class and the rules break around it whatever the dictionary says.
- **Khmer and Burmese are not shaped by PeachPDF.** Rendering the showcase with real Noto Sans Khmer and Myanmar glyphs showed broken
  stacks and dotted circles (only Devanagari, Bengali, Gujarati and Tamil have USE support), so those two scripts appear in the
  showcase only as blocks in `LineBreakTest.ttf`. Line breaking is unaffected. The four-script block font is the one deterministic way
  to assert positions: `assets/fonts/generate_line_break_font.py` now also covers every assigned character of the four blocks.
- **The UAX #14 conformance file expects LB1.** Its Thai, Lao, Khmer and Burmese lines fail with the dictionary on, by design, so
  `LineBreakOptions.ComplexContext` (default `Dictionary`) exists and the conformance test sets `GeneralCategory`.

## Not done

- Text-side normalization: the list is NFC, the text is not normalized, so a Burmese `U+1025 U+102E` written decomposed does not match a
  word spelled `U+1026`, and marks typed in a non-canonical order do not match; such text falls back to unmatched (whole) stretches.
- Weights or frequency: the lists have none; the scoring is coverage only.
- Khmer and Burmese shaping (see above).

## Evidence

`PeachDrawing.Text.Tests` (with the UAX #14, #29 conformance suites at 100%) and `PeachPDF.Tests`, Debug and Release; layout tests over
`LineBreakTest.ttf` in both projects; hostile input (300k-character runs of words and of random characters, a 100,000-mark cluster, 20,000
repeats of one word, unassigned code points) bounded in time; the `dictionary_line_breaking` showcase rasterized with PDFium and MuPDF
(identical).
