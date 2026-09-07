# CSS scientific-notation number tokenization (issue #921)

## The load-bearing idea

`Lexer.NumberRest`/`NumberFraction` (`src/PeachPDF/CSS/Parser/Lexer.cs`) never reached their own
exponent-handling code: the scanning loop's `current.IsNameStart()` branch claimed `'e'`/`'E'` as a
unit-start letter before the trailing `switch`'s `case 'e': case 'E': return
NumberExponential(current);` could ever run, so `NumberExponential`/`SciNotation` were dead code and
`"1e2"` tokenized as two tokens (`Dimension("1","e")` + `Number("2")`) instead of the single
scientific-notation `Number(100)` CSS Syntax Level 3 §4.3.13 describes. Filed as #921 while writing
the allocation-free length fast path for #910 - see
[`2026-09-07-allocation-free-length-tokenization-stage-1.md`](2026-09-07-allocation-free-length-tokenization-stage-1.md)
(that entry now describes this as a still-open gap; it's fixed as of this entry, and will read as
stale prose once it ages out at its own 30-day mark rather than being edited here, per this folder's
own "add a file, don't edit a sibling" rule).

The fix: the loop now breaks unconditionally on `'e'`/`'E'` instead of claiming it as a unit start,
deferring entirely to the pre-existing `NumberExponential`, which already re-derives "digit, or sign
then digit, follows" and already falls back to `Dimension` correctly when it isn't a valid exponent.
No new lookahead helper was needed - an earlier draft added one (`IsExponentStart`), but it turned out
to fully duplicate `NumberExponential`'s own decision and was removed in favor of just breaking and
letting the existing switch dispatch do the one decision it was already built for.

## What was found by running it, not by reading it

**`SciNotation()` was untested dead code, and reaching it for the first time exposed a second, real
bug**: it unconditionally returned a plain `NewNumber`, never checking whether what follows the
exponent digits starts a unit, an escape, or `%` - so `"1e2px"` would have mis-tokenized into
`Number("1e2")` + a stray `px` ident, and `"1e2-foo"` into `Number("1e2")` + a stray `-foo` ident,
even after the loop-ordering fix made the exponent path reachable. A post-change multi-angle review
(8 parallel finder passes) independently converged on this from three directions - reuse,
simplification, and altitude all flagged that `SciNotation` was re-deriving logic `NumberRest`/
`NumberFraction` already had, and one finder empirically traced `"1e2-foo"` through the pre-fix code
and confirmed it produced two tokens instead of the spec-correct single `Dimension(100, "-foo")` that
the non-exponent `"1-foo"` already produces via `NumberDash`.

Fixed by extracting two helpers shared by `NumberRest`, `NumberFraction`, and `SciNotation` instead of
a third hand-copied dispatch: `TryStartDimension(char)` (the loop-level "does this start a unit or
escape" check) and `FinishNumberOrPercentage(char)` (the terminal `%`/`-`-led-dimension/plain-number
dispatch, including the `NumberDash` case `SciNotation` had been missing).

## Deliberately not done

`NumberSyntax.cs`'s `TryConsumeNumber` (the allocation-free fast-path number grammar from #910) still
doesn't consume an exponent - that's unrelated to this bug and doesn't need to be: its own caller
(`CssValueParser.TryClassifyLengthFast`) already rejects any exponent-bearing shape as `Inconclusive`
via its unrelated "is the remainder all letters" check, before or after this fix, so the fast path and
the real tokenizer never disagreed on classification for these inputs - only the real tokenizer's own
internal token shape changed. Its doc comments were reworded to stop describing the old Lexer bug as
current fact, not to add exponent support.

## Evidence

Full net8.0 suite: 10198 passed / 0 failed / 9 skipped (pre-existing platform-specific skips), run
twice (before and after the post-review refactor). Diff coverage 100% (gate is 90%) against `main`.
Zero-warning `dotnet build PeachPDF.slnx -t:Rebuild`. An 8-angle post-change review pass caught the
`SciNotation`/`NumberDash` gap and the redundant lookahead helper before merge; both fixed and
re-verified against the full suite and coverage gate.
