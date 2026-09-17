# Auto table columns now interpolate between their own min/max content width

[Issue #1157](https://github.com/jhaygood86/PeachPDF/issues/1157): a `width:100%; border-collapse:collapse`
table with a dozen `<th>` columns of long two-word headers (`C (Carbon)`, `Mn (Manganese)`,
`Ni (Nickel)`, `PREN`, `Ferrite Content`, ...) laid out ~19pt wider than Chrome's for identical content,
and in the reporter's real document — the same table on 6 pages — the last header column overran the
page edge by 1.7pt and got clipped. A downstream host reacting to that clipped/off-page content by
re-rendering shrink-to-fit then scaled the *entire* document to 0.948x, visibly shrinking every font on
every page from a single 1.7pt column overshoot.

The reporter did the hard part before this ever reached the repo: they proved the drift wasn't from any
one recent commit, wasn't from cell content, and — by rendering the fixture and reading pixel rows
directly — showed that three of the twelve header columns (`C (Carbon)`, `Si (Silicon)`, `Ni (Nickel)`)
kept their entire header text on one un-wrapped line while the other nine correctly wrapped, with no
length/content property distinguishing which three. That asymmetry, not the raw 19pt, was the actual clue.

## Root cause

`CssLayoutEngineTable.DetermineMissingColumnWidths`'s `_widthSpecified` branch resolved auto (no
explicit width) columns with a left-to-right **greedy pass**: it recomputed a running average
(`(availCellSpace - occupiedSpace) / numOfNans`) on every column and granted a column its *entire*
max-content (single-line, unwrapped) width outright the instant that average happened to exceed it —
removing the column from the averaging pool and raising the average for whatever column got processed
next in the same pass. This is exactly why the captured set looked arbitrary to the reporter: it's an
artifact of iteration order and exact glyph-pixel widths, not any real property of the content. Columns
not captured this way fell through to an even split unrelated to their own max-content width, and
`EnforceMinimumSize` afterward only ever raises a column toward its own minimum — it never claws back a
column the greedy pass over-granted. Nothing in the function ever checked a max-content grant against
the total budget still owed to every other unresolved column, so the columns' sum could — and did —
exceed the table's own available width outright, which is the literal overflow this issue reports.
Chrome, by contrast, keeps every auto column strictly between its own min-content and max-content width
(every header wraps), summing exactly to the container.

The min/max *content measurement* itself (`CssBox.GetMinMaxWidth`/`GetMinMaxSumWords`, per-word
wrap-opportunity detection) was independently verified correct and uniform across every header string —
the bug was isolated entirely to this one distribution loop, not to text measurement.

## The fix

Replaced the greedy loop with an order-independent, budget-aware three-way split, using both
`minFullWidths` and `maxFullWidths` from `GetColumnsMinMaxWidthByContent` (which already computes both
together in one pass — the old code discarded `minFullWidths` via `out _`):

- **Deficit** (`remaining <= minSum`): every auto column gets its own content minimum. The table can
  still end up wider than `availCellSpace` here — CSS 2.1 never shrinks a column below its content
  minimum — but every column wraps down to its own true minimum instead of some columns keeping full
  width while siblings are starved. Mirrors what the `!_widthSpecified` branch a few lines below already
  does unconditionally.
- **Surplus** (`remaining >= maxSum`): every auto column gets its own max-content width; genuine leftover
  is handled by the existing proportional surplus-spread code that already runs after this block.
- **Interpolation** (`minSum < remaining < maxSum` — the case this issue is actually about): a single
  global scale factor `t = (remaining - minSum) / (maxSum - minSum)`, applied as
  `width[i] = min[i] + t * (max[i] - min[i])` per auto column. Every column lands strictly within its own
  `[min, max]` (CSS 2.1 §17.5.2.2's actual requirement) and the columns sum exactly to `remaining` — no
  column is singled out to keep its full unwrapped width while its siblings are squeezed, which is the
  literal symptom reported.

## What was found by reading the old code, not by running it

The old fallback block (equal-split for columns the greedy pass never captured) never decremented
`numOfNans` or added to `occupiedSpace` for the columns it assigned. This meant the later
`if (numOfNans != 0 || !(occupiedSpace < availCellSpace)) return;` check always took the early-return via
`numOfNans != 0` whenever that fallback ran at all — never via the `occupiedSpace` half. It happened to
be harmless (the fallback's own arithmetic already filled exactly the available space, so there was never
real surplus left to spread), but it was a live inconsistency, not a real safety net. The replacement
fixes it properly: every branch explicitly accumulates `occupiedSpace` and sets `numOfNans = 0` once all
NaN columns are actually resolved, so the downstream surplus-spread check means what it says.

## A crash the post-change review pass caught, not empirically discovered independently

The first version of the interpolation branch computed `t = (remaining - minSum) / (maxSum - minSum)`
unconditionally whenever `minSum < remaining < maxSum`. `GetColumnsMinMaxWidthByContent` has a
separate, deliberate early-return for a **vertical** (`writing-mode: vertical-rl`/`vertical-lr`) table:
no writing-mode-aware content measurement exists for a vertical cell's own inline-axis extent, so it
reports every column's max-content width as `double.PositiveInfinity` outright rather than attempting
one. For a vertical table with an explicit column-axis size (`height` in the vertical case) and at
least one auto column, `minSum` is `0` and `maxSum` is `+Infinity` — neither the deficit nor surplus
guard trips (a finite `remaining` is never `>= Infinity`), so the interpolation branch ran with
`t = remaining / Infinity = 0`, then `width = 0 + 0 * (Infinity - 0)` = `0 * Infinity` = `NaN`
(IEEE-754), corrupting every auto column's width and throwing during layout.

The review pass caught this before it landed; reproducing it directly (temporarily short-circuiting the
new infinity guard) confirmed the exact crash it predicted (`PeachPDF.HtmlRenderException: Exception in
box layout` out of `CssBox.LayoutBlockChild`) against a fixture matching its own repro
(`writing-mode: vertical-rl; height: 300pt` with an auto-height `<td>`). Fixed by special-casing
`double.IsPositiveInfinity(maxSum)` ahead of the other three branches: split `remaining` evenly across
the auto columns, exactly the equal-share fallback this method used unconditionally before this issue's
fix — a vertical table gets no worse (and no better) width distribution than it had before, since it
has no content-driven bound to distribute against in the first place. A new regression test,
`TableWritingModeIntegrationTests.VerticalRl_ExplicitHeight_AutoColumn_DoesNotThrow_Issue1157`, pins
this: confirmed to throw against the code without the infinity guard, and to lay out cleanly (both rows'
single auto column taking the full 300pt column-axis budget) with it.

## Deliberately not done

No attempt to match Chrome's exact internal distribution formula pixel-for-pixel. CSS 2.1 §17.5.2.2 only
requires each auto column's used width to land within its own min-content/max-content bounds; it does not
mandate a specific interpolation curve. Linear interpolation against a single global scale factor
satisfies the spec requirement, is order-independent (unlike the bug it replaces), and is the standard
choice several other engines use for this case — matching Chrome's own internal algorithm exactly would
be chasing unspecified user-agent behavior, not a spec requirement.

## Evidence

Two new tests in `CssLayoutEngineTableTests.cs`:
`TableLayout_AsymmetricWrappableHeaders_InterpolateBetweenColumnMinAndMax_Issue1157` (the interpolation
branch — confirmed to fail against the pre-fix code with `Table width 218.2 overflowed its specified
213.6pt width`, and pass after the fix) and
`TableLayout_NarrowExplicitWidth_MultipleAutoColumns_EachGetsOwnContentMinimum` (the deficit branch —
already passed pre-fix via `EnforceMinimumSize`'s own safety net, so it's coverage for a previously
untested branch rather than a regression catch, added since no existing test exercised multiple
simultaneously-auto columns in a deficit). One new test in `TableWritingModeIntegrationTests.cs`:
`VerticalRl_ExplicitHeight_AutoColumn_DoesNotThrow_Issue1157` (the vertical-table infinity guard found
by the review pass — confirmed to throw `PeachPDF.HtmlRenderException` against the code without the
guard, and pass with it). Full `PeachPDF.Tests` suite on net8.0: 12,213 passed, 0 failed (the one
failure seen mid-investigation, `AnonymousBoxDefaultingTests.AListItemCostsLittleMoreThanAPlainBlock`,
is a memory-allocation-ratio benchmark unrelated to table layout, confirmed to fail identically against
unmodified `main` — pre-existing environment-dependent flakiness, not a regression). `diff-cover` against
`origin/main` reports 100% diff coverage on the 32 changed/added lines in `CssLayoutEngineTable.cs`.
`dotnet build -t:Rebuild` on the whole solution is warning-free.
