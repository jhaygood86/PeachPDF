# A first child breaks before itself when its parent's top edge crosses the page

## What was wrong

A block whose top padding or border crossed the page foot kept its first child on the page being filled.
`CssBox.ResolveBlockChildOffset` asks whether a child's collapsed top margin crosses a boundary (§5.2)
against the band its starting point *ends in*, and for a first child that point is the parent's content
top, already on the next page. So the child was placed at that content top (plus its own margin) while the
pass still filled the previous page, the pass broke before the child's first line, and the next pass started
that line at the page's top: above the child's own box. With
`<div style="padding-top:5pt"><p>…</p></div>` starting 4.5pt above the foot, the paragraph box sat at
y=191.7 and its first line at y=180.

What a reader sees is any of: text above the background it belongs to, a top border bar drawn partly into the
page margin, or a highlighted heading whose highlight is missing (`<div style="border-top:5pt solid #888"><h2
style="margin:20pt 0 4pt;background:#ffee99">…</h2></div>` 3pt above the foot drew the bar at y=17–22 and the
heading's text at y=19, with no highlight at all).

## The fix

A first child whose parent's content top already falls past the live fragmentainer's band
(`CurrentFragmentainer`, not the band the edge ends in) takes the break before it, resuming at that band's
bottom. The parent, left with nothing on the page but its leading edge, moves whole, and the child's first
line starts at its own content top, as in Chrome
([the invariant](../invariants/fragmentation-a-first-child-is-judged-against-the-fragmentainer-being-filled.md)).
`FirstChildAtThePageFootIntegrationTests` fails without it.

## The version that was too narrow

The first version also required the child to have a top margin (`top > baseTop`), because the review that
found the defect saw it with a margin. A sweep showed that was the wrong boundary: the misplacement does not
depend on the margin at all. Words drawn outside the highlighted box they belong to, over 972 generated
documents (a spacer swept in 0.5pt steps so the parent's edge crosses the foot at every offset):

| Parent shape | `main` | With the margin condition | Without it (this fix) |
|---|---|---|---|
| top padding 5pt, first child with a 20pt margin | 290 in 10 documents | 0 | 0 |
| top padding 20pt, first child with no margin | 500 in 28 | 500 in 28 | 0 |
| padded wrapper inside a padded parent | 64 in 4 | 64 in 4 | 0 |
| leading padding of 200pt / border of 170pt (taller than the page) | 1,680 in 68 / 1,268 in 46 | unchanged | 0 / 0 |
| top padding or border of 5pt around a paragraph, a list, a `break-inside: avoid` or an `overflow: hidden` box | 0 | 0 | 0 |
| the same parent as a two-column container | 150 in 22 | 150 in 22 | 150 in 22 |

The two-column rows lose words and duplicate others on every build; that is a separate, older problem in
columns and is not touched here. The leading edges taller than the page do not hang or lose a word.

## Why it matters beyond a misplaced line

A box that clips to its fragment (an auto-height `overflow: hidden` block, since those break across pages)
cut the misplaced first line away entirely, so the loss was a missing line, not a misplaced one.

## What was found by running it

- It applies to every block, so it moves other boundary lines too. On 250 generated mixed-feature documents
  (columns, flex, grid, floats, scroll containers) the words lost fall from 37,625 to 37,384 and the
  duplicated words from 8,052 to 7,911: 19 documents get better and 11 get worse, each by at most 11 words.
  The 14 documents `main` renders completely are unchanged.
- On 250 ordinary generated documents, 203 are rendered completely by `main`; 202 are unchanged and one moves
  80 words without losing or duplicating any. That document has a float on each side and its text overlaps
  them in both builds, so neither placement is right.
- Of the 192 existing showcases, 189 rasterize identically to `main`'s. `canvas_background`, `container_queries`
  and `quotes` have identical text and positions but differ in 116 to 703 of about 348,000 pixels, by at most 5 of
  255.
- Only a first child is covered. A later sibling starts from its predecessor's bottom, which a break would
  already have handled.
