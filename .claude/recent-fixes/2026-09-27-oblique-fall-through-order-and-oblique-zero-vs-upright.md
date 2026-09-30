# Face matching: the oblique fall-through reaches italic, and `oblique 0deg` prefers an upright face

`FontResolver.NarrowByStyle` (`src/PeachDrawing.Text/Internal/Fonts/FontResolver.cs`) closes the two edge cases the
previous face-matching PR left as an accepted gap: an `oblique <angle>` request of 0 degrees or more now reaches a
declared italic face before crossing to oblique ranges on the other side of upright, and `oblique 0deg` now considers
a genuinely upright face. Closes issue #1447 and deletes
[the accepted gap](../accepted-gaps/face-matching-oblique-fall-through-and-oblique-zero.md).

## What the load-bearing idea was

**`NearestOblique` was one function doing two jobs**: searching the oblique ranges on the same side of upright as the
request (CSS Fonts 4 §5.2's "only positive values... checked in this stage"), and, only when that finds nothing,
crossing to the ranges at or below 0. The bug was that both jobs ran back-to-back with no italic check in between, so
whenever a family had *any* declared oblique range, an `oblique <angle>` request calling `NearestOblique` would
silently cross to a range on the wrong side of upright before the caller ever got a chance to try a declared italic
face. This only became visible when every oblique range in the family was at or below 0 (so the "same side" search
came up empty) *and* the family also had an italic face - both conditions the previous PR's tests happened not to
combine.

The fix splits the function: `LeaningOblique` is the "same side" search alone, returning an empty list rather than
crossing over when it finds nothing; `NearestOblique` is now `LeaningOblique` and, only if that is empty, the
crossover. `NarrowByStyle`'s `oblique <angle>` branch (only for `angle >= 0` - see below) calls `LeaningOblique` first,
tries the declared italic faces if that comes up empty, and only then falls to the full `NearestOblique` (which
repeats the leaning search for free and then crosses over). This exactly matches the specification's fall-through for
a non-negative angle: same-side oblique, then italic, then the other side.

**`oblique 0deg` needed the *upright* check, not another oblique search.** An upright face is oblique 0 on the
specification's scale, but this engine keeps "genuinely upright" (no `Ranges.Oblique`) and "declares an oblique range"
as separate categories, so an upright face was never a candidate inside the oblique-angle branch at all - it would
only be reached by the `!request.IsItalic` branch, which a `font-style: oblique 0deg` request never takes (it still
sets `IsItalic` in this engine's model; see the doc comment on `TypefaceQuery.ObliqueAngle`). The fix reuses
`SlantMatches`/`PreferStrictSlant` - the exact pair the plain-upright branch already uses - as an early check gated on
`request.ObliqueAngle == 0`: if any face is upright or declares an oblique range containing 0, `PreferStrictSlant`
picks the genuinely upright one over the range (same preference as `AnUprightFace_BeatsAnObliqueRangeForUprightText_
HoweverTheyWereDeclared`), and that's the whole style step - a range containing 0 is as much an "exact" match as a
literal upright face is, but a literal upright face is the more precise one. Only when that set is empty (no upright
face, no range covering 0) does the request fall through the ordinary `angle >= 0` chain above.

**Negative angles were deliberately left alone.** The specification's mirrored fall-through for `oblique <angle>`
below 0 degrees would insert a "negative italic" step, which does not exist in this engine's model (italic is a
boolean per static face, never a signed range) - inserting an italic check there would be fabricating a step the
specification's mirroring doesn't actually give a value to search. The plain `italic` keyword request (no angle) was
already correct before this change: it already tries the declared italic faces before ever calling `NearestOblique`,
regardless of which side of upright the family's oblique ranges lean.

## What running it found rather than reading

- The two new bugs needed the exact combination the previous PR's test matrix never produced: an `oblique <angle>`
  request (not the bare `italic` keyword) against a family whose *only* oblique ranges are at or below 0, or a
  `oblique 0deg` request against a family with an upright face and a range that excludes (or, separately, includes) 0.
  Confirmed by reverting the fix and rerunning the four new cases: all four fail against the prior code (three by
  picking the wrong face, the fourth - `AnObliqueZeroRequest_FallsBackToTheOldSearch_WhereThereIsNoUprightFace` -
  intentionally still passes either way, since it exercises the unaffected fallback when there's no upright face at
  all).
- CSS Fonts 4's own text ("If no match is found, italic values greater than or equal to 1 are checked...") reads as
  though a genuinely-normal request could, as a last resort, fall back to an italic face too - but that reading
  applies to the `oblique <angle>` chain only; the existing `!request.IsItalic` (plain upright) branch was correct
  before this change and stayed untouched, matching the plan's scope.

## Deliberately not done

- Negative `oblique <angle>` requests still call the unsplit `NearestOblique` directly, with no italic step - see
  above for why.
- The plain upright (`font-style: normal`) branch does not get an italic fallback either, even though the
  specification's text for it also mentions one; this repo's model has no signed italic scale to search there,
  the same reasoning as above, and it was not part of the reported gap.

## Evidence

`PeachDrawing.Text.Tests.Fonts.FontResolverFaceRangesTests`: two new tests reproduce the reported fall-through bug
(one for an explicit positive angle against negative-only ranges, one confirming the bare `italic` keyword was already
unaffected), two more reproduce the `oblique 0deg` bug (against a range that excludes 0 and one that includes it), one
confirms the unaffected all-ranges fallback, and one is a regression guard for the width-then-style-then-weight
ordering and `PreferStrictSlant` from the prior PR. Full `PeachDrawing.Text.Tests` and `PeachPDF.Tests` suites pass,
`net8.0`, Debug and Release.
