# Intrinsic sizing: a trailing space may only hang where a LINE is known to have ended

[css-text-3 §4.1.2](https://www.w3.org/TR/css-text-3/#white-space-phase-2) hangs the white space that
ends a **line**. `CssBox.GetMinMaxSumWords` carries **one** running `maxSum` across a whole subtree,
so the point where any given box's words run out is *not* a line end — a sibling's content can follow
on the same line, and there that space is an ordinary inter-word gap.

## The invariant the subtraction rests on

**`trailingSpace` is non-zero only while the most recently measured word is still the tail of
`maxSum`.** Every path that puts something else on the line after that word must zero it, and every
path that ends the line does zero it. Only then is the amount taken off provably still in `maxSum`.

Do **not** substitute the more intuitive-sounding justification "`maxSum` at the epilogue is this
box's last line only" — that is false. A `white-space: nowrap` child does not reset the running line,
so `<div>AB CD</div><div>EF</div>` under `nowrap` sums both lines into one `maxSum` (46.1836pt where
Chromium says 32.9883 — a separate, pre-existing defect). The subtraction survives that because of the
invariant above, not because of any claim about line counts.

The paths that had to be taught the invariant, each found only by measuring:

- `maxSum += rowMax` (flex row) — an item's width comes back from its own top-level
  `GetMinMaxWidth`, which already hung that item's own final line's space, so nothing hangs off the
  row.
- the explicit-width fold, `Math.Max(maxSum, maxSumBeforeChild + explicitContentWidth)` — only when
  the declared length actually *raises* the total, since that is when it replaces the measured tail.
  Zeroing unconditionally would throw away a legitimate hang in the case where the measured total
  still wins.

Both are reachable only under `white-space: nowrap`, because that is what stops the walk treating an
inline-level box as opening a line of its own — so a fixture without `nowrap` will not catch a
regression here.

An empty inline child's own horizontal margins (`maxSum += childBox.ActualMarginLeft + …`)
deliberately do **not** zero it: Chromium hangs the space there too.

## Where the rule may be applied

There are exactly three places in the intrinsic walk where the line is known to have ended, and the
rule is applied at each:

1. the `word.IsLineBreak` branch — a `<br>` closes the line;
2. the block-boundary epilogue (`if (oldSum.HasValue)`) — the box opened a line of its own at the top
   of the call and is closing it now;
3. `GetMinMaxWidth`, after the walk — the document ran out. This one is a no-op whenever (1) or (2)
   already ran, since both zero `trailingSpace`, and exists for the box kinds `StartsNewLine`
   excludes so they never reach (2): a `display: table-cell`, a `white-space: nowrap` block, and an
   inline box measured directly.

## The measured symptom of getting it wrong

**Hanging it per box** (at the end of the `box.Words` loop, which is where it looks like it belongs):
`<span>AB </span><span>CD</span>` measures 26.3906pt at `font: 16px monospace` where it draws 32.9883
— a space short of its own content, so a float/inline-block/table column around it wraps text that
fits. Chromium gives 43.9844px = 32.9883pt.

**Hanging it at the top-of-call reset** (subtracting from `oldSum` when a box starts its own line):
redundant for the ordinary block-sibling case, because the preceding block's own epilogue already
hung its space and left `trailingSpace` at 0 — and wrong for an `inline-block`, which
`StartsNewLine` selects but which does not end the line before it.

**Subtracting after the epilogue's `Math.Max`** rather than before it: `maxSum` is then the widest of
two *different* lines, and the space belongs to only one of them.
`AnEarlierBlockSiblingsLine_KeepsItsOwnWidth_WhenALaterOneHangsASpace` and
`TheLineAForcedBreakEnds_KeepsItsOwnWidth_WhenALaterLineHangsASpace` are the tests that pin this.

**Leaving `trailingSpace` stale across a non-word addition to `maxSum`** (the two paths above): the
epilogue then takes a real inter-word gap back off. `AB <span style='display:inline-flex'>CD</span>`
under `nowrap` measures 26.3906pt where it draws — and Chromium measures — 32.9883.
`AFlexRowLandingOnTheLineAfterASpace_DoesNotSwallowIt` and
`AnExplicitChildWidthLandingOnTheLineAfterASpace_DoesNotSwallowIt` pin these.

## `GetBoxWidth` is a second, independent measurement of the same thing

`CssLayoutEngine.GetBoxWidth`'s `box.Words.Sum(x => x.FullWidth)` is not part of the walk, but
`GetFitContentWidth` → `GetLargestChildWidth` folds it into every shrink-to-fit box's size, so it
decides a float's used width whenever it exceeds max-content. It excludes the last word's
`ActualWordSpacing` for the same reason, and a change that restores the bare `Sum` reopens issue
#1014 on its own, with the intrinsic walk still perfectly correct. The giveaway is a box whose used
width exceeds its own `max-content`.

## What never hangs

`&nbsp;` (not white space for §4.1.2) and a `white-space: pre`/`pre-wrap` trailing space (preserved
as its own `IsSpaces` word). Both come out right for free rather than by a special case —
`CssRect.ActualWordSpacing` is 0 for each — so a future change that starts deriving the hung amount
from something other than `ActualWordSpacing` has to re-check both.
