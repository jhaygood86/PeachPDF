# Bidi paragraph resolution and text-box correction skip display:none subtrees

Pure performance. No production behaviour change (display:none content was never laid out or
painted; this only stops computing bidi levels/words for it).

## What was wrong

This corrects the root-cause attribution in
[2026-09-12-mathml-tests-bypass-css-for-large-bundled-font-registration.md](2026-09-12-mathml-tests-bypass-css-for-large-bundled-font-registration.md),
which blamed `Token.Data`'s repeated re-materialization for the MathML test slowdown. Direct
instrumentation (a counter on `Token.Data`'s getter, then allocation checkpoints through
`DomParser.GenerateCssTree`) showed that attribution was wrong in magnitude: `Token.Data` accounted
for only ~4-6MB of a ~190MB total for one `SetHtml` call. The actual dominant cost - 134.8MB, 71% of
the total - was `CssBidiParagraphResolver.AssignBidiLevels`.

`CssBidiParagraphResolver.Flatten` (and the whole-tree walk in `AssignBidiLevels`, and
`DomParser.CorrectTextBoxes`) had no guard against `display: none` content. PeachPDF's own default UA
stylesheet sets `style, title, script, link, meta, area, base, param, head { display: none }`
(`CssDefaults.cs`), so a `<style>` (or `<script>`) element's raw text - CSS or JS source, never
rendered - was being flattened into a "paragraph" and run through the full Unicode Bidi Algorithm
(`BidiResolver.Resolve`) plus per-codepoint script/Arabic-joining/USE-category resolution
(`CssBidiParagraphResolver.ResolveScriptsAndJoining`), and separately through `CssBox.ParseToWords` in
`CorrectTextBoxes`. Both passes allocate several parallel arrays sized to the text length; for an
embedded multi-megabyte `<style>` payload (e.g. a font as a `@font-face` data: URI) that is on the
order of a hundred megabytes of pure waste, computed for text nothing ever reads again - layout and
paint already skip `display: none` boxes via the identical `DerivedStyle.ActualDisplay ==
Keywords.None` check in over a dozen places in `CssBox.cs`; this was just a place that check was
missing.

This is not only a pathological-payload problem: every document with any `<style>`/`<script>` block
pays some of this waste proportional to that block's own text length, whether or not it happens to be
megabytes.

## The fix

Added the same, already-established `DerivedStyle.ActualDisplay == Keywords.None` guard PeachPDF's
layout code uses everywhere else, in the three places that were missing it:

- `CssBidiParagraphResolver.AssignBidiLevels`: skip recursing into (and never call `ResolveParagraph`
  on) a child whose own display is `none`.
- `CssBidiParagraphResolver.Flatten`: skip a `display: none` child entirely when flattening a paragraph
  - it contributes nothing, not even an Object Replacement Character placeholder (unlike a genuine
  atomic replaced element, it has no rendered box at all).
- `DomParser.CorrectTextBoxes`: skip a `display: none` child before generating its pseudo-content or
  parsing its text into words.

In every case the subtree is left exactly as HTML parsing produced it - nothing downstream reads it,
since layout and paint were already skipping it.

## Why this is safe

`display: none` means "generate no box for rendering purposes" - CSS Display 3 §2. Nothing in the
render pipeline reads a `display: none` box's `BidiLevels`/`Words`/generated pseudo-content today
(confirmed empirically: the full suite passes identically before and after), because layout itself
already refuses to lay such a box out. Skipping work whose result is provably never consumed is a
pure performance change.

## Evidence

Full `PeachPDF.Tests` suite, net8.0, single-threaded
(`dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0 -- xunit.maxParallelThreads=1`),
same machine, on top of the two font-related fixes already landed today:

| | before this fix | after this fix |
| --- | ---: | ---: |
| Duration | 1m 5s | 43s |
| Passed / Skipped / Total | 11031 / 9 / 11040 | 11031 / 9 / 11040 |

Identical pass/skip count - this fix changes what gets computed for content nothing renders, not what
renders.

- Full net8.0 suite, before and after: 11031 passed, 9 skipped, 0 failed.
- Rebuild (`dotnet build PeachPDF.slnx -t:Rebuild`): 0 warnings, 0 errors.
