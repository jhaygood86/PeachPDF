# `<hr>` resolves a percentage width against its containing block, not against itself (#1230)

`CssBoxHr.PerformLayoutImp` computed one `width` expression — the available space, already reduced by
the rule's own margins and borders — and used it for **both** the `auto` case and the percentage
basis. CSS 2.1 [§10.1](https://www.w3.org/TR/CSS21/visudet.html#containing-block-details) puts the
containing block at the content edge of the nearest block container ancestor and
[§10.2](https://www.w3.org/TR/CSS21/visudet.html#the-width-property) resolves the percentage against
its width, with the element's own margins, borders and padding outside it. The two are now separate
statements.

## The load-bearing idea

The `auto` expression is correct where it belongs and wrong as a basis, and that is the entire bug:
one value doing two jobs. Splitting it is a handful of lines; the work was in establishing which
subtrahends belong to which half — and then in finding that *neither* half had been written in terms
that survive `box-sizing`.

- **Percentage basis**: `ContainingBlock.AvailableWidth`. Nothing of the rule's own comes out of it.
- **`auto`**: that same width, less the rule's own margins and `ActualBoxSizeIncludedWidth` — CSS 2.1
  §10.3.3's constraint solved for `width`, which is what makes an unstyled rule's margin box span its
  container exactly.

**Both halves must be written in `box-sizing` terms, and neither obviously is.** This is the part
worth carrying forward, because both mistakes read as correct arithmetic:

- `ContainingBlock.Size.Width` is the containing block's **content** width under `content-box` and its
  **border box** under `border-box`. Subtracting the container's padding and border from it by hand —
  which the original expression did, and which the first version of this fix kept — takes them out a
  second time for every `content-box` container, i.e. almost all of them. `AvailableWidth` is the one
  expression that means "content width" under either. Measured: an `hr { width: 50%; border: 4px }` in
  a `width: 200pt; padding: 0 10pt; border: 5pt` parent came out 82pt at v0.9.19 and 93pt with the
  hand-written subtraction, against Chrome's and the equivalent `<div>`'s 100pt.
- Symmetrically, subtracting the *rule's* own padding and border literally in the `auto` half is right
  under `content-box` and wrong under `border-box`, where `Size.Width` already **is** the border box.
  `ActualBoxSizeIncludedWidth` is exactly that quantity-or-zero, and is what
  `CssLayoutEngine.GetBoxWidth` has always used for the same reason. Measured: a
  `box-sizing: border-box; padding: 0 10pt` rule in a 200pt block came out 178pt with the literal
  subtraction and 198.5pt at v0.9.19, against Chrome's 200pt.

The general rule the next change should take from this: **in layout arithmetic, `Size.Width` alone is
never the whole story.** Reach for `AvailableWidth` when you mean a content width and
`ActualBoxSizeIncludedWidth` when you mean "what sits outside `Size.Width`", rather than writing out
padding and border and being right for only one `box-sizing`.

## Not only a styled-rule problem

Worth knowing because it changes how widely this was visible: the UA sheet gives every rule a 1px
border, which is 0.75pt a side. So a plain `<hr style="width: 50%">` in a 200pt block came out
**99.25pt** rather than 100 — wrong on a document that never declares a border at all. With a declared
`border: 4px` it was 97pt, and with a margin as well the margin came out of the basis too
(`margin-left: 20pt` → 87pt).

The worst case was `width: 50%; margin: 0 auto`: the auto margins were resolved first and consumed the
whole basis, leaving a percentage of nothing, so the rule collapsed to **a 1.5pt dot** (its own UA
borders) where Chrome draws 101.5pt centred. Nothing in the issue mentioned it and no test covered it;
it turned up only when the before/after probe was widened past the cases in the issue.

## Found by running it, not by reading it

- **The first raster probe said the bug did not exist.** An `<hr>` and its paired zero-height `<div>`
  were drawn 0px apart, so the two painted bands merged into one and the measurement returned the
  union — i.e. the div's (correct, wider) span, with the rule's narrower one hidden inside it. The
  layout-level assertions found it immediately. If a pixel probe of a paired hr/div ever says
  "no difference", separate the two boxes before believing it.
- **An existing test encoded the bug in its name.**
  `HrPlacementTests.TheRule_StillSpansItsContainingBlocksContentWidth` asserted 180pt for an unstyled
  rule in a `width: 200pt; padding: 0 10pt` container — but that container's content width *is* 200pt,
  which is what the test's own name claims and what Chrome measures. The 180 was the double
  subtraction, written down as an expectation. A test name that states a spec property is worth
  re-reading against the property, not just against the number next to it.
- **`box-sizing: border-box` percentages were fixed by the basis alone**, unmeasured beforehand and
  not mentioned in the issue: a wrong basis was simply carried through. `width: 50%; box-sizing:
  border-box; border: 4px` spanned 97pt of a 200pt block before and 100pt after, which is Chrome's
  answer. No box-sizing-specific code was needed for the *percentage* half — only for `auto`.
- **`auto` with horizontal padding overflowed its container** by exactly that padding under
  `content-box` (measured: 220pt border box inside a 200pt block, against Chrome's 200), because only
  margins and borders were taken out of the available space.
- **`Size.Width` is not always a content width.** Under `box-sizing: border-box` it holds the border
  box, so a test that computes a border box as `Size.Width + borders` is wrong there — that arithmetic
  is what produced a "96pt" figure for a case whose real v0.9.19 span was 97pt. Use
  `ActualRight - Location.X`, or assert `Size.Width` and say which box it is.
- **`height: 0` is not how you write "an equivalent empty box" under `border-box`.** The showcase's
  paired `<div>` declared it, which is harmless under `content-box` (content height 0, box = its own
  borders — the same as a rule) and destroys the box under `border-box`, where it sizes the *border*
  box to zero: the div painted nothing at all, and the `border-box` swatches read as mismatched pairs
  for a reason with nothing to do with width. `height: auto` is the one declaration that means the
  same thing under either. Found by rasterizing the regenerated showcase and counting bands — the
  numbers all agreed, and the picture did not.

## What this unblocks

Pairing a rule against its equivalent zero-height `<div>` — the natural way to test or showcase rule
painting — is now valid at any declared `width`, under either `box-sizing`, in a padded or bordered
container. It previously held only with no `width` declared and no padding anywhere, which is why the
`border_style` showcase's `<hr>` section had to avoid declaring one, and why an earlier version of it
shipped four mismatched pairs. The showcase gained two rows that do declare widths.

The equivalence is not unconditional, and the docs sentence is worded to match: `min-width`/`max-width`
are still ignored on a rule, and an `auto` rule does not shrink-to-fit as a float, an absolutely
positioned box, a flex item or an inline-block. Those are pre-existing and untouched here.

## Evidence

- Full suite green (net8.0), whole solution rebuilds with 0 warnings, diff coverage checked on the
  changed lines.
- `HrPercentageWidthLayoutTests.cs` asserts the rule **and** its equivalent div against the same
  literal in every case, since "these two agree" is the property that actually matters. It now covers
  `auto` under `border-box` with padding and border, a padded-and-bordered containing block at both
  percentage and `auto`, and `auto` with declared margins — the three mutation classes that survived
  the first version of the change.
- Before/after measured by running v0.9.19's own expression (unchanged from it on `main`) in a
  worktree, against the same probe on the branch, against Chrome 153 `getBoundingClientRect` /
  `getComputedStyle` for the same 23 declarations. Every "now" figure is Chrome's figure.
- A standalone 100/75/50/25 % deck agrees with Chrome to within one pixel at 50% and 25%, where the
  edge lands on a half-pixel (127.5px, 63.75px) and the two rasterizers round opposite ways. The
  paired `<div>` shows the identical 1px delta, which is how that is known not to be an `<hr>` issue.
