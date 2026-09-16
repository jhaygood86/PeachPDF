# `text-underline-offset`/`text-underline-position: under` can clip at a page boundary

Tracked as [#1150](https://github.com/jhaygood86/PeachPDF/issues/1150). Found during #1118's own review,
by analogy with #1124 (which this note deliberately doesn't duplicate the fix shape of).

Issue #1124 taught `CssLayoutEngine.LineBoxContributionOf` to reserve extra ascent-side headroom
specifically for a `double` overline's upward growth (`FragmentPainter.DoubleOverlineExtraReachAbove`),
so it no longer clips at a page top. #1118's own `text-underline-offset` (an arbitrary, unbounded
length/percentage - `text-underline-offset: 3em` is valid) and `text-underline-position: under`
reintroduce the *general* version of that same problem, with no comparable reservation: `PaintDecoration`'s
`ResolveUnderlineCross`/`ResolveDecorationOffset` add the resolved offset straight onto the cross
position, and `CssLayoutEngine` has no awareness of either property at all - grepping the layout engine
for `TextUnderlineOffset`/`TextUnderlinePosition` finds nothing outside the paint-time files.

A block whose last line sits flush against a page's bottom margin, decorated with a large positive
`text-underline-offset` (or `text-underline-position: under` on deep-descender text), gets its underline
drawn far enough below the line's own reserved descent that the page's own content clip cuts it off -
the same failure mode #1124 fixed for `double` overlines, just triggered by an explicit author offset on
an ordinary single underline instead.

## Why this is a known, disclosed limitation rather than a blocking bug

This is not a new class of problem #1118 invented: `text-decoration-thickness` already had the same
general shape before #1118/#1124 existed (an unusually large explicit thickness on `overline` can push
past a page's own top the same way), and #1124 only closed the one specific, bounded case its own repro
exercised (`double`'s mechanical growth, driven by the resolved thickness rather than an arbitrary
author-chosen offset). Solving the general problem needs `CssLayoutEngine.LineBoxContributionOf`'s
single-purpose `reserveDoubleOverlineReach` bool generalized into a query expressing "this box's
decoration needs N extra units above/below its ordinary extent" - covering
`text-decoration-thickness`/`text-underline-offset`/`text-underline-position` together, including
descent-side reservation the current bool parameter has no shape for - which is a larger, separate
design task rather than a one-line fix alongside #1118.

## What closing this would look like

- Generalize `reserveDoubleOverlineReach` (`CssLayoutEngine.cs`) into a per-box "extra reach above/below"
  query covering all three properties above, not only `double`'s own growth.
- A `TextDecorationDoublePdfClipTests`-style real-PDF test for `text-underline-offset`/
  `text-underline-position: under` pushing an underline past a page's own bottom clip.
