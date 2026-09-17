# An inline-flowed inline-block's declared height now sizes its line and grows from the right edge; percentage height now works too

A prior change ([2026-09-17-a-declared-height-now-sizes-an-inline-blocks-painted-box.md](2026-09-17-a-declared-height-now-sizes-an-inline-blocks-painted-box.md))
made a declared `height`/`min-height` size an inline-flowed `inline-block`'s own painted box, but
left three gaps in place, tracked as issues #1166, #1167 and #1169. All three are now closed.

**Before.** The line/flow around a tall inline-flowed `inline-block` did not reserve its declared
height, so a following line — or the containing block's own height — could overlap it. Growth always
extended downward from the box's own top, which visibly mispositioned the box under `vertical-align:
bottom`/`text-bottom`/`middle` (and, less visibly, `sub`/`super`). A percentage `height`/`min-height`
had no effect at all, even against a containing block with a perfectly definite height.

**Now.**

- The line and the containing block's own height correctly clear a tall inline-flowed
  `inline-block` (CSS 2.1 §10.8: an atomic inline-level box contributes its whole margin box to the
  line box it sits on).
- Growth extends from whichever edge `vertical-align` actually anchors: the box's own bottom for
  `bottom`/`text-bottom` (grows upward), split evenly around its own center for `middle`, and its own
  top (unchanged) for `top`/default `baseline` on a box with real content/`sub`/`super`.
- A percentage `height`/`min-height` now resolves against the containing block's own height whenever
  that is itself definite (CSS 2.1 §10.5) — including a percentage chain through several ancestors, a
  `height` derived from `aspect-ratio`, and a flex/grid item's own algorithmically-resolved
  stretch/main-size height (`align-items: stretch`, or a column-direction flex item's main size) — and
  correctly still has no effect when the containing block's own height is genuinely content-driven,
  exactly as before.

## What this does not change

One case remains: the default `vertical-align: baseline` on an **empty box, or one whose `overflow`
isn't `visible`** still only grows downward from its own top, and can be positioned too low relative
to the rest of the line. That box has no baseline of its own — the engine falls back to treating its
bottom margin edge as its baseline, computed from the box's still-natural (pre-growth) rectangle
before the extra height is applied, which makes anchoring this one case correctly a materially
larger change (issue #1169, narrowed).

Neither change affects a box whose content is block-level, or one whose own inline content must wrap
onto several lines — those already take a different layout path that sized height correctly before
either change.
