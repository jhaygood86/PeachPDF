# A bevelled `currentColor` border shades a fixed base, not the box's colour (#1226)

`inset`/`outset`/`groove`/`ridge` used to be shaded from whatever colour the border resolved to,
including when that colour came from `currentColor`. Blink does not: when it resolves a border side's
`currentColor` **and that side's style is one of those four**, it substitutes the opaque
`rgb(238, 238, 238)` for the box's `color` and shades that.

## The load-bearing idea: it is a *used* value, so it belongs in `DerivedStyle`

The four `border-*-color` longhands are now the only colour properties `CssUtils.ApplyCurrentColor`
does **not** substitute. They keep the literal `currentcolor` in `ComputedStyle`, and
`DerivedStyle.ResolveBorderSideColor` resolves each side per box, against the fixed base when that
side is bevelled and against the box's own `color` otherwise.

That placement is the whole fix, and the first attempt got it wrong. Substituting during the cascade
looks equivalent — the box paints the same bytes either way — and is not, because **`border-color:
inherit` hands a child the computed value**. Two measured failures, both in Chrome, neither caught by
any test that only looks at one box:

| | before #1226 | cascade-time substitution | `DerivedStyle` | Chrome 153 |
| --- | --- | --- | --- | --- |
| solid child, `border-color: inherit`, parent `inset` + `color: red` | `#ff0000` | **`#eeeeee`** | `#ff0000` | `#ff0000` |
| same, child also has `color: blue` | **`#ff0000`** | **`#eeeeee`** | `#0000ff` | `#0000ff` |

The second row was already wrong before this change: the child inherited the parent's resolved colour
where a browser re-resolves `currentcolor` against the child's own. Keeping the keyword unresolved
fixes both at once, which is the argument for this placement over any flag-carrying alternative.

Nothing else reads the raw `BorderTopColor` string expecting a resolved colour — checked before
committing to this — so `DerivedStyle` and the separate `MarginBoxRenderer` path are the only two
places that had to learn the rule.

### The latent trap that comes with it

`DerivedStyle` caches each side's answer in `_actualBorder*Color`, and the only thing that clears one
is its own `border-*-color` longhand being rewritten. That answer now also depends on
`border-*-style`, on `color` and on `display` — none of which invalidate it. Safe today (every reader
runs after the cascade has settled, and nothing mutates those afterwards except the `display` swap in
flex/grid's `PerformLayoutBlockified`, which only ever swaps `inline`→`block`), and a real hazard for
any future reader that moves earlier. Written up as
[.claude/invariants/dom-a-border-sides-cached-actual-colour-is-invalidated-only-by-its-own-longhand.md](../invariants/dom-a-border-sides-cached-actual-colour-is-invalidated-only-by-its-own-longhand.md),
since it outlives this note.

## What was found by running it rather than by reading it

The rule was measured against Chrome 153 headless before a line was written. Four things came out of
that which reading the Blink source alone would not have settled:

- **The base ignores `color` completely**, including `white`, `transparent` and `rgba(255,0,0,0.5)` —
  it is opaque, so alpha is lost too. Not "falls back when the colour is unsuitable".
- **`getComputedStyle` reports the *unsubstituted* colour** (`rgb(255, 0, 0)` for
  `border: 2px inset; color: red`). That is the direct evidence it is a used value, and in hindsight
  it is also the tell that the cascade was the wrong layer.
- **Tables are exempt** (`style.IsDisplayTableType()`), confirmed for all ten table display types and
  confirmed *not* to apply to `block`/`inline-block`/`flex`/`grid`/`list-item`. The exemption reads
  the **blockified** display: a `float: left; display: table-cell` is a block by then and loses it.
- **`outline-color` and `column-rule-color` are never substituted**, even when their own style is
  bevelled — `outline: 20px inset; color: red` paints a shaded red in Chrome. An outline really is
  bevelled through `BorderBevelColors` here, so that exemption is load-bearing. A column rule is not
  bevelled here at all — `FragmentPainter.PaintColumnRules` maps only `dashed`/`dotted` to a dash
  style and strokes everything else as a plain line of `ActualColumnRuleColor` — so for that one the
  exemption is only future-proofing.

## The UA sheet went back to the HTML Standard's literal text

`hr { color: gray; border-style: inset; border-width: 1px }` plus `hr[color], hr[noshade] {
border-style: solid }` — no colour declared in either rule any more.

This is what closed the residue the sheet's declared `#eee` left behind: an author writing
`hr { border-style: dashed }` and nothing else used to get a near-invisible `#eeeeee` rule, because a
declared base is only the right colour while something is shading it. It is gray now, as in Chrome.

**Do not re-declare a colour in either rule.** That is the third time the same mistake would be made
(four per-side greys → a single `#eee` base → whatever comes next); each version was Chrome's output
written down in place of something the engine could derive, and each one broke a different case.

## Deliberately not done

- **Collapsed-table and margin-box bevels are still all one face** — `BorderBevelColors.ForSegment`
  maps every segment to `Border.Top`/`Border.Left`, both of which darken under `inset`. Measured:
  Chrome splits 5600/5600 px between `#ab0000` and `#ff0000` for a collapsed cell, PeachPDF paints
  8000 px of `#ab0000`. Independent of this change — it reproduces with a declared colour — so it is
  #1237 with its own gap note. **Since closed**, by
  [2026-09-22-a-collapsed-grid-line-shows-both-bevel-faces.md](2026-09-22-a-collapsed-grid-line-shows-both-bevel-faces.md),
  which also deleted that gap note; the measurement above is history, not current behaviour.
- **An out-of-flow table-internal box keeps the exemption** (`position: absolute; display:
  table-cell`), because `BlockifyPositionedBox` does not implement CSS Display 3 §2.7's
  layout-internal arms. #1238.
- **`position: fixed; display: table-row` crashes layout.** Pre-existing on `main`. #1239.

## Evidence

- Full suite green (12,751 passed, 9 skipped / net8.0), whole solution rebuilds with 0 warnings,
  **100% diff coverage**. `diff-cover` — the tool the CI gate runs — reports **29** measurable
  changed lines, 0 missing. A by-hand count comes out higher (a review counted 40) because much of
  what it sees is not a coverable statement: the UA sheet's `hr` rules and the comment above them are
  lines inside one multi-line string literal, and a multi-line expression — the four getters' wrapped
  assignments, `ResolveBorderColor`'s nested conditional — carries one sequence point, not one per
  line. Quote the tool's number; that is the one the gate is about.
- 52 tests in `BeveledBorderCurrentColorTests.cs`, split deliberately across the two layers:
  `ActualBorder*Color` is the **resolved base** (`#eee`, unshaded) and the painted calls are the two
  **faces**. Getting that backwards is the first thing that goes wrong when writing a test here — the
  first draft of the file asserted faces against the resolved property and failed on all eight rows.
- The per-side test runs **two** arrangements, `inset solid inset solid` and `inset solid solid
  inset`, because one cannot do the job: four sides over two styles always leaves some pair sharing a
  style, and a mutant that swaps exactly that pair survives. Those two leave no pair equal in both.
  This took two rounds of review mutation to get right — the first attempt paired an arrangement with
  its own mirror, which kept *left* equal to *top* in both, so a left longhand wired to the top style
  still survived the whole file. A second test asserts four distinct **declared** colours across the
  four sides, covering the colour argument the same way (every other test in the file leaves all four
  at `currentcolor`, where a colour-argument swap is invisible).
- Probe deck of 16 bevel cases rendered through both Chrome (headless screenshot) and PeachPDF
  (PDFium raster at 96 dpi, giving 1 CSS px per image px): **16/16 byte-identical**. The nine `<hr>`
  cases match Chrome exactly too, read out of the PDF content stream rather than the raster.
- Note for anyone repeating this: **MuPDF disagrees with both on the greys** (`#ededed` for
  `#eeeeee`, `#d3d3d3` for `#d4d4d4`) — its own colour conversion rounds differently. PDFium is
  Chrome's engine and is the one to compare bytes against here; MuPDF's "mismatch" is not one.
- `border_style` showcase gained two sections (a bevel with no declared colour, and the same `<hr>`
  rules with none) and was compared against Chrome printing the same HTML.
