# The SVG Form XObject reuse demo is a registered showcase, and what it actually saves

The 12-page document that exercises SVG artwork reuse existed only as a hand-written HTML file
force-added into `docs/showcase/` — a directory that is gitignored build output, so the file was source
living in the one place this repo says is never source, and it appeared on no showcase card. It is now
built by `SaveShowcaseAsync("svg_form_reuse", "Graphics & Effects", …)` in
`src/PeachPDF.TestHarness/Program.cs` like every other showcase, and the hand-written copy is deleted.

## What it demonstrates

Two pieces of SVG artwork repeat on all twelve pages: a `position: fixed` logo (the real
`docs/assets/img/peach.svg` mark plus a wordmark) and a `border-image` whose source is an SVG — the
second one matters because a border-image invokes its source once per corner and once per edge tile, so
the same form is placed dozens of times on a single page, not once. Both resolve to one document-local
Form XObject each, invoked wherever they appear.

## The saving, measured rather than asserted

Numbers in the showcase copy and in Program.cs's comment come from rendering the showcase's own
generated HTML twice with everything else identical — the only way to isolate reuse from the other
changes that have landed since, and the reason not to compare against `main`, which also lacks the
border-image `srcRect` fix:

1. Render `svg_form_reuse.html` with the CLI as-is.
2. In `SvgRenderer.GetOrCreateForm`, temporarily replace `var owner = g.FormCacheOwner;` with
   `var owner = (object?)null;` — the cache is then never consulted or populated, which is exactly the
   pre-cache behaviour — rebuild, render again, restore.

Result: **2 Form XObjects and 126,666 bytes with reuse; 24 and 176,077 bytes without** — the no-reuse
file is 39% larger. The 24 is one form per artwork per page, and it is what a reader can check from the
PDF itself; the byte figure is the one that will drift if the demo's text changes, so re-measure with
the recipe above before restating it.

Worth knowing: the saving is smaller than the object count suggests because content streams are
compressed and this artwork is light. A heavier illustration repeated per page widens the gap.
