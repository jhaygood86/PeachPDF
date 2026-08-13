## What broke

peachpdf.net/showcase.html rendered with everything after the "Table Captions
(caption-side)" card stacked in a single column instead of flowing through the
`card-grid` layout.

## Root cause

`docs/showcase.html` interpolated `{{ s.title }}` / `{{ s.description }}` (and
`s.title` again inside the thumbnail's `alt` attribute) into the page without
escaping. Several showcase descriptions in
`src/PeachPDF.TestHarness/Program.cs`'s `SaveShowcaseAsync` calls mention real
tag names as plain text — `<table>`, `<caption>`, `<thead>/<tfoot>`,
`<string>`, `<input>/<select>`, `<img>` — which is fine as text but fatal once
Liquid drops it into the page unescaped: the browser's HTML parser sees an
actual `<table>` start tag with no matching `</table>`, switches into "in
table" insertion mode, and never leaves it. Every element the parser sees
afterward (all later `<h2>`/`<div class="card-grid">`/`<div
class="showcase-card">` elements for every remaining category) gets
foster-parented or nested under that dangling table instead of being a normal
sibling, which is what broke the grid for the whole remainder of the page.
`<thead>`/`<tfoot>` alone (without a preceding unclosed `<table>`) don't cause
this — they're just inserted as ordinary elements while insertion mode stays
"in body" — which is why only the content *after* the `<table>`/`<caption>`
card broke, not after the earlier `<thead>/<tfoot>` one.

## Fix

Escape at the template boundary, not at the data source: `docs/showcase.html`
now uses `{{ s.title | escape }}` / `{{ s.description | escape }}` everywhere
they're interpolated, including inside the `alt` attribute. This is the
correct fix regardless of which specific description strings happen to
contain tag-like text today — any showcase description added in the future
that mentions an HTML tag by name is safe by construction, rather than relying
on every future description author remembering to hand-escape.
`src/PeachPDF.TestHarness/Program.cs`'s description strings were deliberately
left as plain, readable text (not pre-escaped with `&lt;`/`&gt;`) since the
template is now the thing responsible for making them safe to render.

## Verification

Built the Jekyll site locally (`jekyll build` from `docs/`) against a small
hand-written `_data/showcases.json` reproducing the `<table>'s <caption>`
description. Before the fix, the built `showcase.html` had the second
category's `card-grid` nested inside the first category's card (matching the
live-site bug); after the fix, `&lt;table&gt;&#39;s &lt;caption&gt;` renders
as escaped text and the second category's `card-grid` is a proper sibling
`<div>`. `docs/_data/showcases.json` is gitignored build output — the
reproduction file was deleted after verification, nothing was committed.
