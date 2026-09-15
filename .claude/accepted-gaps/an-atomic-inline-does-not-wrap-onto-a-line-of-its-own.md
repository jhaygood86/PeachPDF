# An atomic inline is never moved onto a line of its own when it does not fit

`CssLayoutEngine.FlowAtomicBlockContentChild` places an atomic inline-level box (an `inline-table`,
an `inline-grid`, and an `inline-block` reached through
[`LaysOutAsAnAtomicBox`](../recent-fixes/2026-09-16-an-inline-block-that-wraps-is-laid-out-as-one.md))
at `coordinates.CurrentX` unconditionally. It never asks whether the box fits in what is left of the
line, so a box that does not fit overhangs the containing block's right edge where a browser would
close the line and start a new one for it. CSS 2.1
[§9.4.2](https://www.w3.org/TR/CSS21/visuren.html#inline-formatting) makes an atomic inline-level box
one unbreakable unit on the line, which is exactly the case a line break exists for.

Tracked as **#1105**.

## Why it was not fixed here

The wrap decision lives inside `FlowBox`'s per-word loop (the `wrapping` branch that restores
`MaxBottom`, applies `line-clamp`, records `PrecedesForcedBreak`, re-measures a `::first-line` tail,
re-intersects floats and opens the next `CssLineBox`). None of it is reachable for a box that
contributes no word to the parent's line, so honouring the break means lifting that sequence into a
helper both paths call — a refactor of the engine's most load-bearing loop, and not something to
bundle with a baseline-alignment change.

## What it is coupled to, and why that coupling is deliberate

`FlowAtomicBlockContentChild` sizes a **declared** `width` as the whole border box rather than adding
the box's own padding and border to it under `box-sizing: content-box`. That is wrong by the same
rule the rest of the engine follows (`ResolveAtomicInlineDeclaredWidth`), and Chrome paints such a
box its declared width **plus** its padding and border — measured on the `cascade_layers` showcase's
own `.card { width: 150px; padding: 16px }`: Chrome 182px, PeachPDF 150px.

Correcting it in isolation makes the rendering **worse**, and the showcase is what proved it: with
the cards at their correct 182px, three fit across the page and Chrome wraps the fourth onto a second
row — PeachPDF, having no wrap, runs it off the page instead. So the short measurement is kept for
that path only (`HasBlockLevelDescendant(b)`), and the two belong in one change. A box that reaches
the same path because its own inline content wraps is sized correctly, because otherwise the same
declaration would paint two different widths depending on whether the text inside it happened to fit.
