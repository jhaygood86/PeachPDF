# A collapsed block-end margin escapes or is contained — it is never dropped

Found the same way as [the line-height fix](2026-09-14-a-line-box-is-its-line-height-not-its-tallest-glyph-box.md):
by rendering Acid2 against its reference image, where the last row of the face (`ul`, four black table
cells) was painting *on top of* `.parser` instead of below it, and the face was 12pt short.

## The load-bearing idea

CSS 2.1 [§8.3.1](https://www.w3.org/TR/CSS21/box.html#collapsing-margins) puts a box's block-end margin
and its last in-flow child's in one adjoining set whenever nothing of the box's own separates them —
an `auto` height, a zero `min-height`, no bottom padding or border, no new formatting context. Two
outcomes follow, and the engine implemented a third:

- **Collapsing**: the collapsed value belongs to the gap *after* the box. It is not part of the box's
  own height, and it keeps escaping outward through every ancestor that also collapses.
- **Blocked**: the child's margin has nowhere to escape to, so it stays *inside* the box and is counted
  in its auto height. Blocked means contained, not discarded.

`MarginBottomCollapse` conflated the question "does it collapse?" with "where does the value go?" in a
single condition, and got both answers from it. A box that was its own parent's last child folded the
collapsed value **into its own `ActualBottom`**; any other box returned a flat zero and the child's
margin vanished — it neither escaped into the gap nor stayed in the height. A wrapper whose own margin
is zero therefore contributed nothing at all, which is exactly Acid2's `.parser-container`: `.parser`'s
`margin-bottom: 1em` was dropped, so `ul`'s own `margin-top: -1em` had nothing to collapse against and
pulled it a full `1em` up onto the row above.

The fix splits the two questions:

- `CollapsesBlockEndMarginWithLastChild()` answers §8.3.1's question alone — auto height (including a
  percentage against an indefinite containing block, the same rule `IsMarginCollapseThrough` already
  applies), zero `min-height`, no bottom padding/border, `overflow: visible`.
- When it says no, the child's effective bottom margin is added to this box's auto height. The one
  blocking reason that does *not* do that is a non-`auto` height: the declared height is the height, and
  the contained margin overflows it rather than growing it (Chrome measures 20pt, not 22pt, for a
  `height: 20px` box around a 10px child with a 12px bottom margin).
- When it says yes, nothing is baked into `ActualBottom` at all. The value is read from the outside by
  `FoldOwnAdjoiningBlockEndMargins`, a new block-end mirror of `FoldOwnAdjoiningBlockStartMargins` that
  walks the last-in-flow-child chain folding each member's bottom margin into the caller's
  `AdjoiningMarginSet`. `FoldMarginsPrecedingChild` (both of its branches) and `GetEffectiveBottomMargin`
  now use it instead of reading a bare `ActualMarginBottom`.

The old "is this box its own parent's last child" gate existed solely to stop the baked-in value being
counted a second time by a following sibling's own fold. With nothing baked in anywhere, it has nothing
left to guard and is gone.

## Four existing tests were pinning the wrong answers

`Acid2FeatureVerificationTests` had four cases asserting the old behaviour, and **all four disagree with
Chrome on their own markup** — checked one at a time in headless Chrome before changing anything:

| case | asserted | Chrome |
|---|---|---|
| wrapper is its parent's only child | 70pt (margin folded in) | **20pt** (margin escapes) |
| negative margins on wrapper and child | 10pt | **20pt**, with −10pt in the gap after it |
| bordered parent, child `margin-bottom: -10pt` | 22pt ("no effect") | **11.5pt** (contained, so it *shrinks* the parent) |
| `overflow: hidden` parent | 20pt ("stays external") | **70pt** (contained — external is where it cannot go) |

The third and fourth are the same misreading twice: they treated a *blocked* margin as one that
disappears. All four are rewritten with Chrome's numbers and the reasoning that produces them, and the
first is renamed (`..._IsItsOwnParentsLastChild_Folds` → `..._CollapsedMarginEscapes_RatherThanInflatingTheParent`)
because its old name asserted the behaviour that was wrong.

`ParentLastChildBottomCollapse_NotItsOwnParentsLastChild_DoesNotFold` — the regression test for the
60pt double-count the old gate was added to prevent — still passes untouched, which is the evidence
that removing the gate did not bring the double-count back.

## What running it found that reading would not

A test written for the new chain (`SelfCollapsingLastChild_...`) initially asserted an 8pt gap from a
self-collapsing last child with `margin-top: 8pt; margin-bottom: 3pt`. It passed — and Chrome says the
gap is **0** for that markup, because a wrapper whose only content is self-collapsing becomes
self-collapsing itself and the whole set escapes *above* it. Giving the wrapper real content ahead of
the child exposed a genuinely separate defect: this engine positions a self-collapsing last child below
its own collapsed-away top margin, so the wrapper measures 18pt where Chrome says 10pt. That is a bug in
self-collapsing box *placement*, not in the block-end chain, and it is out of scope here — the test now
uses a bottom-margin-only child (verified 10pt/3pt in both engines) and says in its own comment why.

**The lesson: a test that passes on a shape you have not measured elsewhere is not evidence.** Two of
the three shapes tried here disagreed with Chrome in ways the assertion would have frozen.

The review pass turned up a second one. `MarginBottomCollapse` picks the child whose margin it folds
with a predicate of its own (`lastNonFloatingBox`), and that predicate tests only `IsExcludedFromFlow`
— which is `IsOutOfFlow || IsRunningPositioned` and says nothing about `display: none`. The chain walk
filtered hidden boxes out from the start, so the two disagreed, and the new contained-margin arm made
the disagreement visible: a `display: none` last child with `margin-bottom: 40pt` grew a bordered
parent from 23pt to **51pt**, its hidden margin folded in as if it were real. Both now select through
one `IsBlockEndMarginChainMember` helper (in flow, not `display: none`, not a table's synthetic grid
decoration box), so they cannot drift again; the original looser predicate survives only as a final
fallback so a box whose every child is hidden still resolves to something rather than throwing.
`DisplayNoneLastChild_IsNotTheChildWhoseMarginCounts` pins both the blocked and unblocked shapes
against Chrome's own numbers.

The review pass found one missing part of the same CSS 2.1 condition: the block-end margin also cannot
collapse through a box that establishes an independent formatting context. The initial implementation
only encoded this indirectly for `overflow`-created contexts. It now reuses
`DomUtils.EstablishesIndependentFormattingContext`, covering floats, absolutely positioned boxes,
inline-blocks, table cells/captions, and flex/grid contexts as well; representative float, absolute,
and inline-block cases all contain the child's 12pt bottom margin.

The last-child selection also has a real empty result: when every child is `display: none`, no child
generates a box and none can contribute geometry or a margin. Falling back to a hidden child made a
bordered empty parent 41pt tall from that child's 40pt margin; the method now retains the parent's own
already-resolved border-box bottom instead.

Only block-level children participate in this chain. Filtering merely for in-flow boxes admitted inline
children, so a `margin-bottom` on a text-bearing span incorrectly became a 100pt gap after its parent.
The shared membership predicate now excludes inline-level boxes while retaining anonymous block
wrappers.

## Evidence

- New `BlockEndMarginCollapseTests` (13 tests), every expectation measured in headless Chrome first.
  Against the unfixed code **9 of the 11** that existed at that point fail; the two that pass are the
  cases where the wrapper's own margin already dominated (`margin-bottom: 20pt`) and the
  non-auto-height case, which was already right.
- Four `Acid2FeatureVerificationTests` cases rewritten (see the table above);
  `ParentLastChildBottomCollapse_NotItsOwnParentsLastChild_DoesNotFold` - the regression test for the
  60pt double-count the removed gate was added to prevent - still passes untouched.
- Full suite: 11,360 passed, 0 failed, 9 skipped.
- `dotnet build PeachPDF.slnx -t:Rebuild` — 0 warnings.
- Diff coverage 100% on the changed lines.
- **Acid2's face is now exactly 144 × 144 — the reference image's own dimensions.** Rows matching the
  reference went from 48/168 to 72/168 with this change alone: `.parser` paints black|yellow|black
  instead of solid black, and the bottom `ul` band is present at all. The entire vertical shortfall
  identified when this work started is gone. What still differs is the absolutely-positioned
  `blockquote` not containing its float, the missing HTML5 stray-`</p>` element, and the two documented
  Acid2 gaps.
