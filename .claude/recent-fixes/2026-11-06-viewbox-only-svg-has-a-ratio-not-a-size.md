# A viewBox-only inline svg has an aspect ratio, not a size (and `<noscript>` text no longer recurses)

**Symptom:** opening slashdot.org in the WinUI demo drew a huge red YouTube play button over the page. The footer icon is `<svg viewBox="0 260 1792 1260">` (no width/height) in a `display:block; width:1.25rem` link, with `fill: red` from the site CSS. `SvgIntrinsicSize.ResolveForBox` used the viewBox's own extent as the natural size, so the icon laid out 1792px (1344pt) wide and overflowed everything.

**Rule now:** a document's size is only its `width`/`height` attributes. With one of them and a viewBox, the other follows the ratio (`width="80"` + 2:1 viewBox is 80x40, as Chromium reports; it used to take the viewBox's own height). With neither and a viewBox it is ratio-only: `MeasureIntrinsicSize(..., ratioOnly)` gives it the containing block's available width less its own margin/border/padding (CSS 2.1 section 10.3.2's suggestion, what Chromium does) and the height follows; `max-width`/`height` then work through the existing ratio paths. With neither and no viewBox it is 300x150 (it used to be the 20px broken-image placeholder). `SvgIntrinsicSize.Resolve` (used by background/border-image painting) is unchanged and still falls back to the viewBox.

**How it was checked:** the expected sizes come from running the same markup in Chromium 152 (a page of cases served from localhost to the built-in browser), not from reading the spec; `ViewBoxOnlySvgSizeTests` pins them (1px = 0.75pt). Shrink-to-fit parents (inline-block, float, absolute, table cell, flex item) give 0x0 in Chromium and here. See the accepted gaps for what is not modelled.

**Trap:** `ReplacedElementConstraintTableTests`' `10vw` row had been passing by coincidence (the 80px viewBox = 60pt, within 0.6 of 59.5) because `vw` never resolves on a replaced element; it was removed rather than left lying (see [the gap](../accepted-gaps/viewport-and-container-units-do-not-size-a-replaced-element.md)).

**Second fix, same investigation:** `HtmlParser.ParseDocument` parsed the text of a `<noscript>` by calling itself with the `<noscript>` box as root, so any top-level text (even the whitespace around Slashdot's `<img>`) re-entered the same branch forever: a stack overflow. The recursive call now carries `noScriptContent` and takes its own top-level text as text (`NoScriptParsingTests`).

**Evidence:** `PeachBrackets.Engine.Tests` 18671/18672 before removing the vacuous row, then green.
