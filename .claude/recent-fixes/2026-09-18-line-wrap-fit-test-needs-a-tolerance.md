# The line-fit test needs a tolerance: exact-fit text must not wrap its last word

**Symptom.** In the `aspect_ratio` showcase the flex-centred label `16 / 9` split into `16 /` + `9`,
`3 / 4` likewise, and `abspos -> 192px wide` in an absolutely positioned aspect-ratio flex box broke
onto two lines; Chrome keeps every one of them on one line. The `flexbox` showcase's `also centered`
and `full_bleed`'s `Corner marks touch all four paper edges` (`edges` alone on a second line) were the
same defect, and `bengali_gujarati_tamil_use`, `cmyk_colors` and `table_visibility_collapse` lost a
spurious wrap too. It first showed up as a regression at #1044, but it is latent: the v0.9.18 PDFs
already wrap `also centered` and `edges`, #1044 just moved more layouts onto the unlucky side of it.

**Root cause.** A shrink-wrapped anonymous flex item is *exactly* its text's natural width
(`box.Words.Sum(x => x.FullWidth) - box.Words[^1].ActualWordSpacing`, `CssLayoutEngine`) - zero slack
by construction. At the commit pass the item's line is laid out again for real, re-accumulating
`CurrentX` word by word at a (possibly shifted) X, and the per-word wrap test in `FlowBox`
(`overflows`) was a strict `CurrentX + word.Width + ... > actualLimitRight`. The two sides are the same
number computed along two different summation orders, so they can differ in the last bit: one
failing case had `CurrentX + word.Width = 103.0618445675161` against a limit of `103.06184456751609` -
one ULP over, so the last word wrapped. `LayoutHarness` / `BuildAndLayout` hard-code
`PixelsPerPoint = 1.0`, where the two sides happen to be bit-identical, which is why no existing test
saw it; a non-1.0 pixel scale, a fractional margin or a fractional font size shifts the last bits.

**Fix.** A private `LineFitTolerance = 0.01` in `CssLayoutEngine`, added to the limit in the horizontal
per-word wrap test, in FlowBox's whole-`nowrap`-run compare (`wrapNoWrapBox`) and in the two
vertical-flow counterparts in `CreateVerticalLineBoxes` (`wordDoesNotFit`, `wrapsWholeNoWrapRun`).
It is the idiom the file already used elsewhere (`outerRight <= actualLimitRight + 0.01` for atomic inlines, `> available + 0.01` in the
intrinsic-width walk): 0.01 layout units, the same order as Chrome's 1/64px `LayoutUnit`. `availableWidth` (the
hyphenation slack) is left exact - it is a measurement of remaining room, not a fit test.

**Found by running, not reading.** Sweeping pixel scale (1 + i*0.00091), left margin and font size
over 400 layouts on unmodified `main` (67d5ac21): the centred `16 / 9` label wrapped in 22/400, the
absolutely positioned `abspos -> 192px wide` label in 38/400 (fixture later given more headroom), and the same shape in
`writing-mode: vertical-rl` in 197/400 (`FlexboxIntegrationTests`, `*_NeverWraps`, plus pinned
`*_PinnedLastBitCases_*` cases). All three go to 0/400 with the tolerance, and setting the constant
back to `0.0` makes every pinned case fail again, so the tests genuinely depend on it. The
`wrapNoWrapBox` compare is the exception: a shrink-wrapped item's intrinsic width leaves ~0.01 units of
headroom against it (measured: slack never above -0.0099 over 2400 evaluations), so its `NoWrapSpan`
sweeps pass with or without the tolerance and only guard that path. Pinned indices come from one
machine's font metrics; the sweeps are the real guard on other hosts.

**Deliberately not done.**
- *Applying the flex hypothetical-width `+0.01` epsilon to an auto-width item.* It is computed in
  `CssLayoutEngineFlex` but the auto-width path returns early with the natural main size, so the epsilon
  never reaches the item. Wiring it through (or lowering the `> 0.5` resize threshold that decides
  whether a measured size is applied) changes item sizes for every flex layout, a far larger blast radius
  than a wrap test that only ever asks "does this word fit".
- *Padding `GetBoxWidth`* for the same reason - it feeds min/max-content sizing everywhere, not just
  shrink-wrapped flex items.
- *Changing measurement.* The measured width is correct; the defect is the strict compare against it.

**Trap for a future change.** A tolerance in the fit test is not a licence to make measurement
"nearly" right. If a new sizing path produces an item narrower than its text by more than
`LineFitTolerance` the wrap is real and correct; only sub-0.01-unit drift is absorbed.
