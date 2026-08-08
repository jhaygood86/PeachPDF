# `unicode-bidi: plaintext` performs real first-strong-character detection

Closes [#552](https://github.com/jhaygood86/PeachPDF/issues/552).

## The load-bearing idea

Three layered gaps, all on the path CSS `unicode-bidi: plaintext` takes into
`BidiResolver.Resolve`:

1. `CssBidiParagraphResolver.ResolveParagraph` derived every paragraph's `BidiParagraphDirection`
   from `paragraphRoot.Direction.Value` alone, so a paragraph-establishing box (a block, a `<p>`,
   etc.) with `unicode-bidi: plaintext` never reached `BidiParagraphDirection.Auto` - the value that
   drives `BidiResolver`'s own P2/P3 content detection (`ComputeAutoParagraphLevel`). Fixed by
   checking `paragraphRoot.UnicodeBidi.Value == UnicodeMode.Plaintext` first and using `Auto` when
   it is, regardless of the computed `direction`.
2. For a `plaintext` box that does *not* establish its own paragraph (an inline box participating
   in its parent's, e.g. `<span style="unicode-bidi:plaintext">`), `CssUnicodeBidiMapping` already
   mapped it to a synthetic `Fsi` push - but `BidiResolver.PushSyntheticPush`'s `Fsi` arm just
   treated it as `Lri` (always LTR-isolate) rather than doing real detection, on the stated reasoning
   that unlike a real FSI *character* there was no bounded "matching PDI" range to scan ahead of
   time. That reasoning didn't hold: a synthetic push's own `BidiIsolateOverride.Start`/`End` already
   *is* that exact bounded range (the box's own flattened text, nothing more). Fixed by calling
   `ComputeAutoParagraphLevel(originalTypes, o.Start, o.End, ...)` directly.
3. A **second-order bug found in code review of fix #2, not in the original issue report**:
   `ComputeAutoParagraphLevel` only tracked isolate depth via real `BidiClass.LRI/RLI/FSI/PDI`
   *characters* in the flattened text - it had no way to see a CSS-synthetic isolate override (a
   plain `dir="rtl"` span, `unicode-bidi: isolate`, etc.) nested inside the range being scanned,
   since those never occupy a character position of their own. P2 requires skipping isolate content
   when hunting for the first strong character; without this, a `plaintext` box whose content leads
   with some *other* nested isolate span would read straight through that span's own characters
   instead of skipping them - e.g. `<p style="unicode-bidi:plaintext"><span dir="rtl">שלום</span>
   Hello</p>` should detect LTR from 'H' but would have detected RTL from 'ש' instead. Fixed by
   threading `overrides` through `ComputeAutoParagraphLevel`, which now skips any nested
   isolate-initiating override's own `[Start, End)` range too (embedding/override pushes are *not*
   skipped - only isolates are, per spec). This needed one further piece: an override scanning its
   *own* range must exclude itself from that skip (or it would just skip straight over its entire own
   content and never detect anything) - and a nested override can share an identical `(Start, Length)`
   with its own enclosing one (when the nested span is 100% of the outer box's content), so
   `startsAt`'s entries now carry the override's original index into the full `overrides` list
   (`excludeOverrideIndex`) rather than relying on value equality, which that coincident-range case
   would get wrong.

All three reuse the same `ComputeAutoParagraphLevel` P2/P3 implementation `BidiParagraphDirection
.Auto`'s block-level path already had - `BidiResolverConformanceTests` had validated it end-to-end
against Unicode's conformance suite, it just had no production call site for the CSS-driven cases
until now, and no notion of CSS-synthetic isolates at all until gap #3's fix.

## What didn't need to change

`FindMatchingPdiForFsi` (unchanged - reused as-is for the real-FSI-character case), the real (non-
synthetic) `BidiClass.FSI` character handling in `ResolveExplicitLevels` (already correct, was the
model gap #2's fix follows), and `CssUnicodeBidiMapping.MapToPushes` (already emitted `Fsi` for
`Plaintext` - the gap was purely in how the resolver's synthetic-push stack consumed it).

## A trap worth knowing for next time

A strong character's own *final* level self-corrects to match its own resolved type via I1/I2,
regardless of which (right or wrong) explicit level the enclosing isolate picked - so a unit test
built only out of strong L/R characters cannot actually distinguish "detection ran correctly" from
"detection silently defaulted to LTR" once I1/I2 has run; both converge on the same final answer for
strong content. The first version of the nested-isolate-skip regression test in
`BidiResolverSyntheticIsolateTests.cs` looked reasonable but passed unchanged with the fix reverted -
caught only by actually reverting the fix and rerunning, not by reading the assertions. The fix:
assert on a *neutral* character's level instead (here, the space right after the nested isolate
closes) - N0-N2 resolves a neutral from its `sos`/`eos` type, which is read directly from the
enclosing isolate's actual (uncorrected) explicit level and does not self-heal the way a strong
character's does.

## Evidence

- Three new `CssLayoutEngineBidiTests.cs`/`BidiResolverSyntheticIsolateTests.cs` integration cases
  for gaps #1/#2 (block-level and inline-span `plaintext`, both reordering per detected content) plus
  three unit cases for gap #3 (self-vs-nested-isolate exclusion, a nested CSS override skip
  discriminated via the neutral-character trap above, and the real-Unicode-isolate-character
  counterpart of the same skip path).
- Confirmed each new regression test actually fails with its corresponding fix reverted (not just
  that it passes with the fix applied) - caught the false-positive test from the trap above this way.
- Full `net8.0` suite: 8315 passed, 0 failed, 9 skipped (a `ContainerQueryLayoutIntegrationTests` case
  failed on one run and passed clean on a re-run - pre-existing cross-test-class flakiness already on
  `main`, unrelated to this change).
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings.
- `diff-cover` against `main`: 100% diff coverage (45/45 lines).
- Closed the matching accepted-gap
  (`.claude/accepted-gaps/unicode-bidi-plaintext-is-a-no-op.md`, deleted) and corrected
  `docs/html-css-support.md`'s `unicode-bidi` row, which had described `plaintext` as behaving like
  `isolate`.
