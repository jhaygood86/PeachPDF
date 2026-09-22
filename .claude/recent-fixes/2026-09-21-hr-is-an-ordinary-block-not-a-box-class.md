# `<hr>` is an ordinary block, not a box class (#1248, #1247, #1243)

`CssBoxHr` was deleted. It had been shrinking one fix at a time - painting (#1231), height (#1233), width
basis (#1241) - each time moving one private formula onto the shared path, and each time the private copy
had drifted from it. What was left was one behaviour that distinguished it from a `<div>` (the nominal 2-unit
box) and a set of formulas that were the shared ones spelled again.

## The load-bearing idea

**A rule is an ordinary block whose visible part is its border.** There is no reason for a layout class, and
Blink has none (`LayoutBlockFlow`, chosen from `display` alone); what it has is an *attribute-mapping* class.
The suite agreed before any of this was written: routing the factory to a plain `CssBox` broke exactly the
three tests that pinned the 2-unit quirk and nothing else, out of 12,793 at the time. This change is that
experiment made permanent, plus the verification the experiment did not do.

## Verification the suite could not give

- **All 150 showcases render identically.** Compared page-for-page against a baseline built from the parent
  commit, at 60dpi. The `<hr>`-heavy `border_style` showcase was then compared at 200dpi in **both** PDFium and
  MuPDF, and its page content streams are **byte-identical**: the rule paints exactly the same operators through
  the generic path.
- **Chrome 153 for the behaviour the rule newly inherits.** A real `<hr>` as a float, an absolutely positioned
  box, a flex item and an `inline-block` measures 1.5pt (content 0 plus its 1.5pt of border); `width: 50%` as a
  float 101.5; `width: 50%; min-width: 180pt` 181.5; `max-width: 40pt` 41.5. All asserted, each against the
  equivalent `<div>` as well.

## #1247: answering the fragment-tree question rather than paying for it

The 2-unit constant existed because a zero-size box emits no fragment, so a borderless rule would be absent
from the fragment tree. That is true, and it is true of an empty `<div>` too: the emitter's membership test is
geometric. A rule that paints nothing needs no fragment - its tag type is already `Artifact` - so the correct
answer was to stop paying for it with a wrong height. The two tests that pinned it now assert the box is 0
tall, has no fragment and matches the equivalent `<div>`; `FragmentPaintHarness.HasFragment` was added so
"absent" is a first-class assertion instead of a `FragmentOf` failure.

## Deleting the class left dead code, which went with it

- `CssBox.PlaceAsBlockChild` and `PlaceBlockChild` existed for `CssBoxHr` alone (the frame's own child loop calls
  `ResolveBlockChildOffset`/`CommitBlockChildOffset` directly). Four comments named `PlaceBlockChild` for logic
  that actually lives in `ResolveBlockChildOffset`; they now say so.
- `CssLayoutEngine.ClampToMaxHeight` was split out of `ApplyHeight` solely so the rule could clamp a pass early.
  It is inlined back, byte-for-byte the original.

## #1236 is narrower now, not closed

The two-call split (rule's own pass vs the epilogue) cannot occur any more: the issue's exact repro is
consistent (1.5 / 1.5 / 1.5). But probing with a content-bearing parent showed the same paint-versus-flow
disagreement on a plain `<div>` - `height: calc(100% - 5px)` paints 36.25 while the next block is placed as if
it were 0 - which confirmed the root cause as `calc()` not being recognised as percentage-dependent
(`.EndsWith('%')` at a dozen sites). Recorded in the accepted-gap note; fixed separately.

## Not done

`hr[color]` mapping to `background-color` (Blink does), and the `size`/`align` attribute mappings, are
independent of the box class - verified by the issues, and unchanged here.

## Evidence

Full net8.0 suite green; 935 `<hr>` tests pass unchanged apart from the three rewritten; 15 new tests in
`HrSharesTheBlockPathTests` (rule against the equivalent `<div>` and against Chrome literals).
