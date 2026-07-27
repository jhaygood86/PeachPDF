# A line layout could not move keeps both of its pages

Issue [#477](https://github.com/jhaygood86/PeachPDF/issues/477), correcting
[#446](https://github.com/jhaygood86/PeachPDF/issues/446) (PR #472) the same day.

## What was wrong

#446's tie-break — a word belongs to the fragmentainer its own top starts in — was applied to **every** rect.
It is only true where `CssRect.WouldStraddleFragmentainer` was actually *asked* and answered "it fits", which
it does for an overhang up to `PageBoundaryEpsilon`. Two paths never ask it:

- **flex/grid item content.** `CssLayoutEngineFlex.PerformLayoutBlockified` and its grid twin set
  `HtmlContainerInt.SuppressWordPageBreaks = true`, and `CssLayoutEngine.FlowBox` gates the straddle check on
  that flag. The engine's later `AssignLocations` translation never re-runs it.
- **`MonolithicContent.FitsNoFragmentainer`** — anything taller than the band stays put, since breaking to a
  fresh fragmentainer would only repeat the problem forever.

Those lines overhang by *many points*, and both bands must keep them: the earlier shows the sliver that fits,
the later the readable remainder. Removing the later claim deleted the remainder — the line survived only as a
clipped sliver at the foot of the page above.

## The fix

```csharp
region.Contains(rect)
&& (isFixed
    || container.SlotStartingAt(rect.Top) == slotIndex
    || HtmlContainerInt.FallsPast(rect.Bottom, container.BandStartingAt(rect.Top)))
```

`FallsPast` is already "did this bottom edge leave that band", in `SlotEndingAt`'s tolerance, so this adds no
third epsilon — it asks the *same* question, and the tie-break now applies exactly where layout had a decision
to make. It still only ever removes claims relative to pre-#446 behaviour, so
[the re-emission invariant](../invariants/fragmentation-which-drafts-exist-decides-whether-a-frozen-slot-is-emitted-again.md)
still holds.

## Measured

A flex column of 120 blockified items at `line-height: 13pt`, A4 at 10pt margins:

```
STRADDLE 'f189' top=829.00 bottom=841.00 band=0 bandBottom=832.00 overhang=9.00
```

**9pt — 18× the tolerance.** `[0,1]` before #446, `[0]` after it, `[0,1]` again with this fix. `display: grid`
reproduces it identically. A four-page flex document lost 45 words, one line per break.

## Why #446's own evidence could not see it

Every measurement it took was correct about the wrong case, and this is the part worth remembering:

- **"Every word claimed exactly once" is *satisfied* by the loss.** The word is claimed once — by the page that
  can only show a sliver of it. The workhorse invariant is blind to this entire class.
- **The showcase pixel diff was blind too.** #446's duplicate lands in the next page's *top margin*, where the
  page clip hides it, so "69 showcases pixel- and text-identical" was a true statement about a case that never
  renders. The straddle-beyond-tolerance copy lands *inside* the content area — and no showcase has one.
- **`BandMembershipToleranceTests` cannot reach it by construction**: it tunes its fixture to a 0.25pt
  overhang. Worse, its per-word assertion re-computed `SlotStartingAt` verbatim, so it held for the broken
  rule too — a tautology that read like an independent check. It now scans the grid's raw band coordinates
  instead.

The general form: **a membership rule needs a fixture on both sides of "could layout have moved this?"**
`StraddlingLineClaimTests` is the other side, and it exercises the two production mechanisms rather than
simulating them.

## Evidence

All 5 new tests fail against the unconditional rule and pass with the gate. Suite, CLI, zero warnings,
showcases and diff coverage as recorded in PR #478.
