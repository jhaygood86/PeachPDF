# An inline box's left padding and border were charged to the line twice (issue #1093)

`FinalizeFlowBoxExit`'s "hack for actual width handling" compared two quantities that are not the same
kind of thing. Any inline carrying `padding-left` or `border-left` therefore looked narrower than it
was by exactly that padding and border, and the correction added them to the line a second time —
pushing everything after the box right by one padding+border.

Found while fixing [#1087](2026-09-15-collapsible-white-space-is-removed-at-the-beginning-of-a-line.md)
and deliberately left out of scope there; filed as #1093 and fixed here on top of that merge.

## The load-bearing idea

**`startX` is the box's *content*-box left edge, not its border-box left edge.** `FlowBox`'s per-child
dispatch adds a child's `leftSpacing` (margin + border + padding) to the cursor *before* recursing into
it, and `FlowBox` captures `startX = coordinates.CurrentX` at its own top — so by then the cursor is
already past all of it. The matching `rightSpacing` is added by that same dispatch *after*
`FinalizeFlowBoxExit` returns. So `CurrentX - startX` measures **content width used**, beginning and
ending at the content box.

It was being compared against `box.ActualWidth`, which is `ActualBoxSizingWidth` —
`Size.Width + padding + border`, the **border-box** width. The right-hand side is `Size.Width`: the
declared *content* width, which is what the branch's own comment ("whether a box's content came out
narrower than its declared width") was always describing. For a plain inline `Size.Width` is 0, so the
branch now correctly does nothing at all.

The rectangle it registers is the box's border box — it is what background and border paint from — so
it starts one `border + padding` back from the content edge `startX` names. That was wrong in the same
way and is corrected alongside; margin is excluded on both counts, since `ActualWidth` excludes it too.

## Why the arithmetic is the proof

The hypothesis was confirmed before any code changed, by predicting all four failing fixtures from it
(monospace, every advance 9.6px, the padded box 28px):

- empty padded inline: content advance 0 < 28 → adds 28 → 28 + 28 = **56.0**
- with `X` inside: advance 9.6 < 28 → adds 18.4 → 28 + 9.6 + 18.4 = 56, then the space 9.6 → **65.6**
- mid-line: `Z` 9.6 + space 9.6 = 19.2, box adds 28 → `startX` 47.2, advance 0 → adds 28 → 75.2, then
  the space 9.6 → **84.8**

Every one matched the measured value exactly. A guess that reproduces three different wrong numbers
including an 18.4 is not a guess any more.

## Evidence

Measured against Chromium through the repo's Playwright dependency, bundled Source Code Pro embedded
via `@font-face` for both engines. `getBoundingClientRect().left` of the box after the padded one,
in CSS px:

| fixture | Chromium | before | after |
|---|---|---|---|
| empty padded inline, no space | 27.656 | 56.000 | **28.000** |
| empty padded inline, space after | 27.656 | 65.600 | **28.000** |
| padded inline with `X`, space after | 46.875 | 65.600 | **47.200** |
| padded inline mid-line | 46.875 | 84.800 | **56.800** |
| control: empty, no padding | 0.000 | 0.000 | 0.000 |
| control: `padding: 0 10pt` (no border) | 36.266 | — | **36.267** |
| control: declared `width: 60pt` | 80.000 | — | **80.000** |

**The residual ~0.33px is Chromium's, not ours**, and the controls prove it: it appears on exactly the
fixtures carrying `border-left: 1pt` (Chromium renders that 1.333px border as ~0.989px) and vanishes
on the two border-free controls, which agree to 0.001px and 0.000px. Do not "fix" it.

The second row is a bonus from #1087 composing with this: with the spurious `Line.Rectangles` entry
gone, `IsAtLineStart` now sees the line as empty after a content-less padded inline, so the collapsible
space after it is removed — which is what Chromium does (it gives the spaced and space-free forms the
same 27.656px).

- Full suite **11 677 passed / 0 failed** (net8.0); `dotnet build PeachPDF.slnx -t:Rebuild` **0 warnings**.
- **100% line and branch coverage** on all 6 changed executable lines (8/8 branches).
- 6 new tests in `InlineLeftSpacingAdvanceTests`; **4 confirmed failing against `main`**, and the 2
  that pass either way are the deliberate controls — no padding, and a declared width, the case the
  branch actually exists for.

## Still open, deliberately

**Row 4 keeps a 9.6px divergence, and it is a different rule.** Chromium renders no space between a
content-less inline and what follows it, because
[css-text-3 phase I](https://www.w3.org/TR/css-text-3/#white-space-phase-1) collapses a white-space
sequence *across* intervening inline box boundaries — the spaces on both sides of the empty inline are
one sequence, collapsed to a single space placed before it. PeachPDF collapses per text node and
renders both. Filed as **#1095** rather than folded in here: it is a white-space-processing change,
not a box-model one, and it would need its own fixtures across `pre`/`pre-wrap`, nested inlines, and
chains of several empty inlines.
