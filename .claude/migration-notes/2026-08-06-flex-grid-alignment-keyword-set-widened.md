# `justify-content`/`align-content`/`align-items`/`align-self` accept their full CSS Box Alignment 3 keyword sets

**Landed:** 2026-08-06 — Fix open alignment issues (#645, #644)
**Doc section:** docs/html-css-support.md § [Flexbox](../../docs/html-css-support.md#flexbox), § [Grid](../../docs/html-css-support.md#grid)

Four keyword gaps, previously rejected at the cascade (falling back to the property's initial value)
and now accepted and dispatched correctly by both `CssLayoutEngineFlex` and `CssLayoutEngineGrid`:

- `justify-content: start | left | right | stretch` (previously only `flex-start`/`flex-end`/`end`/
  `center`/`space-between`/`space-around`/`space-evenly`).
- `align-content: start | baseline` (previously missing from an otherwise-complete set).
- `align-items`/`align-self: self-start` (previously only `self-end` among the physical, container-relative
  keywords).

A document that authored any of these values previously had it silently dropped, leaving the property at
its initial value (`normal` for `justify-content`/`align-content`, `normal`/`auto` for `align-items`/
`align-self`) instead of the declared alignment.

**`justify-content: stretch` on Grid is a further, non-keyword-acceptance behavior change**: accepting the
keyword activates a pre-existing but previously-dead code path in `CssLayoutEngineGrid.SizeColumnTracks`
that stretches `auto` columns to fill the container when nothing else does — a document that already
declared `justify-content: stretch` (accepted but ignored before) will now see its auto columns actually
stretch.

**`align-self: self-end` on flex containers is a correctness fix independent of the keyword-acceptance
gap**: it was already accepted at the cascade, but `CssLayoutEngineFlex.ComputeCrossOffsets` had no
dispatch case for it and silently treated it as `flex-start` (cross-start) instead of the cross-end edge
it names. A flex item declaring `align-self: self-end` now positions at the cross-end edge as authored.

**`justify-content: left`/`right` on flex containers are physical keywords, not flow-relative ones**
(CSS Box Alignment 3 §8.3) — unlike `start`/`end`/`flex-start`/`flex-end`, they must not flip with
`flex-direction: row-reverse`, and they fall back to `start` when the main axis isn't horizontal
(`flex-direction: column`/`column-reverse`). `CssLayoutEngineFlex.ComputeMainOffsets` now dispatches
`left`/`right` as their own cases consulting the container's row/reverse state, rather than folding
`right` into the same flow-relative branch as `flex-end`. A `row-reverse` container declaring
`justify-content: right` now flushes items against the physical right edge (previously flushed left);
a `column`/`column-reverse` container declaring `justify-content: left`/`right` now packs at the top
(falls back to `start`, per spec) instead of flushing to the bottom.
