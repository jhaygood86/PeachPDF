# A capped scroll container taller than a page loses the line on each slice boundary

_CSS Fragmentation Level 3 §4.4. Tracker: [#1328](https://github.com/jhaygood86/PeachPDF/issues/1328)._

A scroll container with a capped block size (a non-auto `height`, or a `max-height`) is monolithic
(`MonolithicContent.IsMonolithic`). When it is taller than every page's band it can't be moved, so
`CssBox.LayoutContents` lays its content out with the fragmentainer detached, and each page shows one
slice of it. A line straddling a slice boundary is claimed only by the fragmentainer its top falls in
(`FragmentEmitter.ClaimsLine`), so that page draws it past its band, the page clip hides it, and no other
page draws it.

Measured with `MonolithicContentLayoutIntegrationTests.TallAutoHeightScrollContainer_DrawsEveryLineInsideAPageBand`
given an `overflow: hidden; max-height: 10000pt` row: `L9`, `L19` and `L28` end at 182.78pt, 190.78pt and
181.98pt against a band ending at 180pt.

The auto-height case, which is the common one (a clearfix wrapper), was fixed for #1321 by taking it out
of the monolithic set; see
[the recent fix](../recent-fixes/2026-09-24-auto-height-scroll-containers-fragment.md). This capped case
was left out because either fix changes the emitter's line-membership rule, which
[#484](https://github.com/jhaygood86/PeachPDF/issues/484) narrowed on purpose
([the invariant](../invariants/fragmentation-one-membership-question-is-asked-with-one-tolerance.md)).
There are two candidates: slice the line across both bands (the table engine's
`SliceARowAcrossTheBandsItOverflows` is precedent), or re-enable breaking once the box's used height fits
no fragmentainer (§4.4's "may break anywhere").
