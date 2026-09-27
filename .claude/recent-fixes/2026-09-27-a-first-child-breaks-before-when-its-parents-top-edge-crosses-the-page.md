# A first child breaks before itself when its parent's top edge crosses the page

## What was wrong

A block whose top padding or border crossed the page foot kept its first child on the page being filled.
`CssBox.ResolveBlockChildOffset` asks whether a child's collapsed top margin crosses a boundary (§5.2)
against the band its starting point *ends in*, and for a first child that point is the parent's content
top, already on the next page. So the margin crossed nothing, the child was placed a margin below that
content top while the pass still filled the previous page, the pass broke before the child's first line,
and the next pass started that line at the page's top: above the child's own box. With
`<div style="padding-top:5pt"><p>…</p></div>` starting 4.5pt above the foot, the paragraph box sat at
y=191.7 and its first line at y=180.

## The fix

A first child whose parent's content top already falls past the live fragmentainer's band
(`CurrentFragmentainer`, not the band the edge ends in) takes the break before it, resuming at that band's
bottom. The parent, left with nothing on the page but its leading edge, moves whole, and the child's first
line starts at its own content top, as in Chrome
([the invariant](../invariants/fragmentation-a-first-childs-margin-is-judged-against-the-fragmentainer-being-filled.md)).
`FirstChildAtThePageFootIntegrationTests` fails without it.

## Why it matters beyond a misplaced line

Found by the review of #1413, which lets an auto-height `overflow: hidden` block break across pages. Such a
block clips to its fragment, so the misplaced first line was cut away entirely: the review's two sweeps of the
shape (2,610 documents, padded and bordered parents, `p`, `ul` and margin-top cards in 0.5pt steps) lost 72
and 184 words there, and none with this rule.

## What was found by running it

- All 185 existing showcases are byte-identical.
- It applies to every block, so it moves other boundary lines too. Measured on #1413's head with the
  fragment emitter's pruning off on both builds (#1487 dominates any comparison otherwise), the review's two
  `flow-root` fuzz corpora lost 352 and 452 words against the merge-base and recovered 429 and 322. The worst
  (f50447, 178 words) minimizes to a `break-inside: avoid` block taller than a page whose boundary line moves:
  the merge-base loses `w50447_1113`/`1114`, the rule loses `w50447_1177`, which is #1369's loss moving rather
  than a new one.
- Only a first child is covered. A later sibling starts from its predecessor's bottom, which a break would
  already have handled.
