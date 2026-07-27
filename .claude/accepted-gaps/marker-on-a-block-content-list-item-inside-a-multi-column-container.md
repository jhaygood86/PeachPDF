# A block-content list item does not settle its `::marker` inside a multi-column container

`<li><p>…</p></li>` is numbered correctly on the page grid, but inside a **multi-column** container its
marker can land in a later column fragment than the one the item begins in, and in a minority of cases
be claimed twice or not at all. [CSS 2.1 §12.5.1](https://www.w3.org/TR/CSS21/generate.html#lists) and
[CSS Lists Level 3 §3.1](https://www.w3.org/TR/css-lists-3/#marker-position) put the marker beside the
item's **first** line box, so this is a real deviation. Tracked as
[#483](https://github.com/jhaygood86/PeachPDF/issues/483).

**Measured**, over 162 documents (`column-fill: auto|balance` × `column-count: 1|2|3` × 3|5|8 items ×
1|2|4 block children × page heights 120/200/300pt):

| item content | `column-count: 1` | `column-count: 2\|3` |
|---|---|---|
| block children, no words | 0 late, 0 bad | 55 late, 3 claimed twice |
| block children with text | 0 late, 0 bad | 6 late, 18 claimed twice, 6 claimed **zero** times |

The page grid is clean in both rows, and **inline** item content is clean everywhere — that is the
660-document sweep [#468](https://github.com/jhaygood86/PeachPDF/issues/468) was closed against.

**Why it is not the marker rule's doing.** An outside marker is positioned by the pass that *places*
its item and left alone afterwards
([the invariant](../invariants/fragmentation-an-outside-marker-is-positioned-by-the-pass-that-places-its-item.md)),
which is correct wherever the item's own fragmentation is. Here the item's fragmentation is what is
unsettled: a block-content item flows through `CssBox.LayoutBlockChildren`, so nothing calls
`CssBox.AwaitPlacement` over its subtree the way `CssLayoutEngine.CreateLineBoxes` does for an
inline-content item (#433's defect class, still open for this shape), and the loop's column arms can
re-decide which column a child belongs to after the item has been placed.
`CssBox.TakeBackTheMarkerOfAnItemThisPassKeptNothingOf` covers only the case where the item kept
*nothing*, not the case where it kept content in a column it is later moved out of.

**Measured, not assumed, to sit upstream of the take-back:** disabling that method entirely leaves the
same counts (55 late / 3 bad on the wordless row), and a words-only "kept nothing" test — the obvious
alternative — is much worse, turning 55 late markers into 52 claimed zero or twice. A marker in the
wrong column is visible; one claimed zero times is invisible, which is #444's symptom.

**Reachable only because [#467](https://github.com/jhaygood86/PeachPDF/issues/467) closed.** The same
sweep on the build before it reports every one of these markers missing outright, and some items
producing no fragment at all — so this is a residual of a strictly improved state, not a regression.
Closing it means settling how the columns engine fragments a block-level list item, which is a larger
change than the marker rule.
